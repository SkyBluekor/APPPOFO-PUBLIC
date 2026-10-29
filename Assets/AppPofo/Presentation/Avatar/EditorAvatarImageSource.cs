using AppPofo.Presentation.Diagnostics;
using System;
using System.Collections;
using System.IO;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace AppPofo.Presentation.Avatar
{
    /// <summary>
    /// Unity Editor의 파일 선택 및 웹캠 입력을 제공하는 아바타 이미지 소스입니다.
    /// </summary>
    public sealed class EditorAvatarImageSource
        : MonoBehaviour, IAvatarImageSource, IOrientedCameraSource
    {
        private const int FallbackCameraWidth = 1280;
        private const int FallbackCameraHeight = 720;
        private const int TargetCameraFrameRate = 30;

        // 미리보기는 매 프레임 CPU 복사를 하므로 메모리 대역폭을 1080p 수준으로 제한합니다.
        private const long MaximumCameraPixelCount =
            1920L * 1080L;

        private WebCamTexture webCamTexture;
        private Texture2D cameraPreviewTexture;
        private Color32[] cameraPixelBuffer;
        private Coroutine cameraPreviewUpdateCoroutine;
        private uint lastCopiedUpdateCount = uint.MaxValue;
        private bool isRecoveringCamera;
        private int requestedCameraWidth = FallbackCameraWidth;
        private int requestedCameraHeight = FallbackCameraHeight;

        private bool isCameraReady;
        private bool isCapturing;
        private CameraFrameOrientation previewOrientation;
        private bool hasPreviewOrientation;

        public bool TryGetPreviewFrame(out Texture texture, out CameraFrameOrientation orientation)
        {
            texture = null;
            orientation = default;
            if (!IsCameraReady || !hasPreviewOrientation || cameraPreviewTexture == null) return false;
            texture = cameraPreviewTexture;
            orientation = previewOrientation;
            return true;
        }

        // WebCamTexture는 실제 입력 전에 검은 초기 버퍼도 프레임으로 보고할 수 있습니다.
        private bool hasReceivedVisiblePixelData;

        // 포커스 전환 중에도 복구 조건이 일정하도록 런타임 설정을 일시적으로 유지합니다.
        private bool previousRunInBackground;
        private int previousTargetFrameRate;
        private bool hasOverriddenCameraRuntimeSettings;

        public bool IsCameraRunning =>
            webCamTexture != null &&
            webCamTexture.isPlaying;


        public bool IsCameraReady =>
            IsCameraRunning &&
            isCameraReady &&
            hasReceivedVisiblePixelData;


        public void PickImage(
            Action<byte[]> onSuccess,
            Action<string> onFailure)
        {
#if UNITY_EDITOR

            string path =
                EditorUtility.OpenFilePanel(
                    "프로필 이미지 선택",
                    "",
                    "png,jpg,jpeg");


            if (string.IsNullOrWhiteSpace(path))
                return;


            try
            {
                byte[] imageBytes =
                    File.ReadAllBytes(path);


                if (imageBytes == null ||
                    imageBytes.Length == 0)
                {
                    onFailure?.Invoke(
                        "선택한 이미지 파일이 비어 있습니다.");

                    return;
                }


                onSuccess?.Invoke(imageBytes);
            }
            catch (Exception ex)
            {
                onFailure?.Invoke(
                    $"이미지를 불러오지 못했습니다.\n" +
                    $"{ex.Message}");
            }

#else

            onFailure?.Invoke(
                "EditorAvatarImageSource는 Unity Editor에서만 사용할 수 있습니다.");

#endif
        }


        public void StartCamera(
            Action<Texture> onPreviewReady,
            Action<string> onFailure)
        {
#if UNITY_EDITOR || UNITY_STANDALONE

            if (!isActiveAndEnabled)
            {
                onFailure?.Invoke(
                    "PC 이미지 입력 Source가 활성 공용 Host에 연결되지 않았습니다.");
                return;
            }

            EnableBackgroundCameraUpdates();

            if (IsCameraReady)
            {
                EnsureCameraPreviewUpdates();

                onPreviewReady?.Invoke(cameraPreviewTexture);
                return;
            }


            if (IsCameraRunning ||
                isRecoveringCamera)
            {
                return;
            }


            StartCoroutine(
                StartCameraRoutine(
                    onPreviewReady,
                    onFailure));

#else

            onFailure?.Invoke(
                "EditorAvatarImageSource는 Unity Editor에서만 사용할 수 있습니다.");

#endif
        }


        private IEnumerator StartCameraRoutine(
            Action<Texture> onPreviewReady,
            Action<string> onFailure)
        {
            if (!UnityEngine.Application.HasUserAuthorization(
                    UserAuthorization.WebCam))
            {
                Debug.Log(
                    "[Avatar Camera] 웹캠 권한을 요청합니다.");


                yield return
                    UnityEngine.Application.RequestUserAuthorization(
                        UserAuthorization.WebCam);
            }


            if (!UnityEngine.Application.HasUserAuthorization(
                    UserAuthorization.WebCam))
            {
                RestoreBackgroundCameraUpdates();

                onFailure?.Invoke(
                    "웹캠 사용 권한이 허용되지 않았습니다.");

                yield break;
            }


            Debug.Log(
                "[Avatar Camera] 웹캠 권한 확인 완료");


            WebCamDevice[] devices =
                WebCamTexture.devices;


            if (devices == null ||
                devices.Length == 0)
            {
                RestoreBackgroundCameraUpdates();

                onFailure?.Invoke(
                    "사용 가능한 웹캠을 찾을 수 없습니다.");

                yield break;
            }


            Debug.Log(
                $"[Avatar Camera] 감지된 카메라 수: " +
                $"{devices.Length}");


            for (int i = 0; i < devices.Length; i++)
            {
                Debug.Log(
                    $"[Avatar Camera] Device {i}: " +
                    $"Name={devices[i].name}, " +
                    $"Kind={devices[i].kind}, " +
                    $"Front={devices[i].isFrontFacing}");
            }


            // 가상 카메라처럼 열리지만 프레임을 주지 않는 장치를 건너뛸 수 있게 모두 시도합니다.
            for (int i = 0; i < devices.Length; i++)
            {
                string deviceName =
                    devices[i].name;


                Debug.Log(
                    $"[Avatar Camera] 카메라 테스트 시작: " +
                    $"{deviceName}");


                isCameraReady = false;
                hasReceivedVisiblePixelData = false;


                // 특정 해상도와 FPS를 강제로 지정하지 않고
                // 실행하면 일부 Windows 카메라는 매우 낮은 기본 FPS 모드를 선택하여
                // updateCount가 첫 프레임에서 멈출 수 있습니다.
                SelectRequestedCameraResolution(
                    devices[i]);

                webCamTexture =
                    CreateWebCamTexture(
                        deviceName);


                // didUpdateThisFrame은 확인 시점에 따라 놓칠 수 있어 Play 전후의
                // 누적 updateCount를 비교해 실제 프레임 수신 여부를 판별합니다.
                uint initialUpdateCount =
                    webCamTexture.updateCount;


                webCamTexture.Play();


                const float deviceTimeout = 6f;

                float elapsed = 0f;


                while (elapsed < deviceTimeout)
                {
                    if (webCamTexture == null)
                        break;


                    bool validSize =
                        webCamTexture.width > 16 &&
                        webCamTexture.height > 16;


                    bool receivedFrame =
                        webCamTexture.updateCount !=
                        initialUpdateCount;


                    if (validSize &&
                        receivedFrame)
                    {
                        CreateCameraPreviewTexture();

                        isCameraReady = true;

                        EnsureCameraPreviewUpdates();


                        Debug.Log(
                            $"[Avatar Camera] 카메라 장치 초기화 완료, " +
                            $"유효 픽셀 확인 중: " +
                            $"{deviceName} / " +
                            $"{webCamTexture.width}x" +
                            $"{webCamTexture.height}");


                        onPreviewReady?.Invoke(
                            cameraPreviewTexture);


                        yield break;
                    }


                    elapsed +=
                        Time.unscaledDeltaTime;


                    yield return null;
                }


                Debug.LogWarning(
                    $"[Avatar Camera] {deviceName}에서 " +
                    $"지속적인 영상 프레임을 받지 못했습니다.");


                if (webCamTexture != null)
                {
                    if (webCamTexture.isPlaying)
                    {
                        webCamTexture.Stop();
                    }


                    Destroy(webCamTexture);

                    webCamTexture = null;
                }


                // Unity의 객체 파괴가 프레임 끝에 반영된 뒤 다음 장치를 엽니다.
                yield return null;
            }


            RestoreBackgroundCameraUpdates();

            onFailure?.Invoke(
                "연결된 카메라에서 정상 영상 프레임을 받지 못했습니다.");
        }


        private void SelectRequestedCameraResolution(
            WebCamDevice device)
        {
            requestedCameraWidth =
                FallbackCameraWidth;

            requestedCameraHeight =
                FallbackCameraHeight;


            Resolution[] availableResolutions =
                device.availableResolutions;


            if (availableResolutions == null ||
                availableResolutions.Length == 0)
            {
                Debug.Log(
                    $"[Avatar Camera] 지원 해상도 목록 없음, " +
                    $"기본 요청 사용: {device.name} / " +
                    $"{requestedCameraWidth}x{requestedCameraHeight}");

                return;
            }


            int bestResolutionIndex = -1;
            long bestPixelCount = -1L;
            int smallestOversizeResolutionIndex = -1;
            long smallestOversizePixelCount = long.MaxValue;


            for (int i = 0;
                 i < availableResolutions.Length;
                 i++)
            {
                Resolution resolution =
                    availableResolutions[i];


                if (resolution.width <= 0 ||
                    resolution.height <= 0)
                {
                    continue;
                }


                long pixelCount =
                    (long)resolution.width *
                    resolution.height;


                if (pixelCount <= MaximumCameraPixelCount)
                {
                    if (pixelCount > bestPixelCount)
                    {
                        bestResolutionIndex = i;
                        bestPixelCount = pixelCount;
                    }


                    continue;
                }


                if (pixelCount < smallestOversizePixelCount)
                {
                    smallestOversizeResolutionIndex = i;
                    smallestOversizePixelCount = pixelCount;
                }
            }


            int selectedResolutionIndex =
                bestResolutionIndex >= 0
                    ? bestResolutionIndex
                    : smallestOversizeResolutionIndex;


            if (selectedResolutionIndex >= 0)
            {
                Resolution selectedResolution =
                    availableResolutions[selectedResolutionIndex];

                requestedCameraWidth =
                    selectedResolution.width;

                requestedCameraHeight =
                    selectedResolution.height;
            }


            Debug.Log(
                $"[Avatar Camera] 해상도 선택: {device.name} / " +
                $"{requestedCameraWidth}x{requestedCameraHeight} / " +
                $"지원 모드 {availableResolutions.Length}개");
        }


        private WebCamTexture CreateWebCamTexture(
            string deviceName)
        {
            return new WebCamTexture(
                deviceName,
                requestedCameraWidth,
                requestedCameraHeight,
                TargetCameraFrameRate);
        }


        private void CreateCameraPreviewTexture()
        {
            ReleaseCameraPreviewTexture();

            // Unity 6 Editor의 WebCamTexture 표시 문제를 우회하기 위해 미리보기와
            // PNG 캡처가 공유할 CPU 텍스처를 별도로 유지합니다.
            cameraPixelBuffer =
                new Color32[
                    webCamTexture.width *
                    webCamTexture.height];

            cameraPreviewTexture =
                new Texture2D(
                    webCamTexture.width,
                    webCamTexture.height,
                    TextureFormat.RGBA32,
                    false)
                {
                    name = "AvatarCameraPreviewCpu",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };

            CopyCameraFrameToPreview();
        }


        private IEnumerator UpdateCameraPreviewTexture()
        {
            const float visiblePixelWarningDelay = 2f;
            float elapsed = 0f;
            int recoveryAttempt = 0;


            while (webCamTexture != null &&
                   webCamTexture.isPlaying &&
                   cameraPreviewTexture != null)
            {
                CopyCameraFrameToPreview();


                elapsed +=
                    Time.unscaledDeltaTime;


                if (!hasReceivedVisiblePixelData &&
                    elapsed >= visiblePixelWarningDelay)
                {
                    recoveryAttempt++;

                    Debug.LogWarning(
                        $"[Avatar Camera] 프레임 정지 감지: " +
                        $"updateCount={webCamTexture.updateCount}, " +
                        $"자동 복구 시도={recoveryAttempt}");


                    if (recoveryAttempt <= 2)
                    {
                        yield return
                            RecoverStalledCamera(
                                recoveryAttempt);

                        elapsed = 0f;
                    }
                    else
                    {
                        isCameraReady = false;


                        if (webCamTexture != null)
                        {
                            if (webCamTexture.isPlaying)
                            {
                                webCamTexture.Stop();
                            }

                            Destroy(webCamTexture);
                            webCamTexture = null;
                        }


                        ReleaseCameraPreviewTexture();
                        RestoreBackgroundCameraUpdates();
                        cameraPreviewUpdateCoroutine = null;

                        Debug.LogError(
                            "[Avatar Camera] 자동 복구 후에도 " +
                            "WebCamTexture가 검은 첫 프레임에서 멈췄습니다.");

                        yield break;
                    }
                }


                yield return null;
            }

            cameraPreviewUpdateCoroutine = null;
        }


        private void EnsureCameraPreviewUpdates()
        {
            if (cameraPreviewUpdateCoroutine != null ||
                !isActiveAndEnabled ||
                !isCameraReady ||
                cameraPreviewTexture == null)
            {
                return;
            }


            cameraPreviewUpdateCoroutine =
                StartCoroutine(
                    UpdateCameraPreviewTexture());
        }


        private IEnumerator RecoverStalledCamera(
            int recoveryAttempt)
        {
            if (webCamTexture == null)
                yield break;


            // Unity 6 Windows Editor에서는 Stop/Play만으로 검은 첫 프레임 정지가
            // 풀리지 않으므로 객체를 파괴한 뒤 새 WebCamTexture로 연결합니다.
            isRecoveringCamera = true;

            string deviceName =
                webCamTexture.deviceName;


            if (webCamTexture.isPlaying)
            {
                webCamTexture.Stop();
            }


            Destroy(webCamTexture);

            webCamTexture = null;
            lastCopiedUpdateCount = uint.MaxValue;


            yield return
                new WaitForSecondsRealtime(0.5f);


            if (!isActiveAndEnabled)
            {
                isRecoveringCamera = false;
                yield break;
            }


            webCamTexture =
                CreateWebCamTexture(
                    deviceName);

            webCamTexture.Play();

            isRecoveringCamera = false;


            Debug.Log(
                $"[Avatar Camera] 카메라 객체 재생성 완료: " +
                $"시도={recoveryAttempt}");
        }


        private void EnableBackgroundCameraUpdates()
        {
            if (!hasOverriddenCameraRuntimeSettings)
            {
                previousRunInBackground =
                    UnityEngine.Application.runInBackground;

                previousTargetFrameRate =
                    UnityEngine.Application.targetFrameRate;

                hasOverriddenCameraRuntimeSettings = true;
            }


            UnityEngine.Application.runInBackground =
                true;

            // 카메라 요청 FPS와 Player Loop 속도를 맞춰 초기화 조건을 일정하게 유지합니다.
            UnityEngine.Application.targetFrameRate =
                TargetCameraFrameRate;
        }


        private void RestoreBackgroundCameraUpdates()
        {
            if (!hasOverriddenCameraRuntimeSettings)
                return;


            UnityEngine.Application.runInBackground =
                previousRunInBackground;

            UnityEngine.Application.targetFrameRate =
                previousTargetFrameRate;

            hasOverriddenCameraRuntimeSettings = false;
        }


        private void CopyCameraFrameToPreview()
        {
            if (webCamTexture == null ||
                cameraPreviewTexture == null ||
                cameraPixelBuffer == null)
            {
                return;
            }


            uint currentUpdateCount =
                webCamTexture.updateCount;


            // GetPixels32의 CPU 복사 비용을 피하도록 카메라 프레임마다 한 번만 읽습니다.
            if (currentUpdateCount == lastCopiedUpdateCount ||
                (currentUpdateCount == 0 && !webCamTexture.didUpdateThisFrame))
            {
                return;
            }

            if (!CameraFrameOrientation.TryRead(webCamTexture, out var orientation)) return;
            if (cameraPixelBuffer.Length != orientation.SourceWidth * orientation.SourceHeight)
                cameraPixelBuffer = new Color32[orientation.SourceWidth * orientation.SourceHeight];


            PerformanceDiagnostics.ObserveCamera(webCamTexture);
            using (PerformanceDiagnostics.Measure(PerfMetric.GetPixels32Ms))
                cameraPixelBuffer =
                    webCamTexture.GetPixels32(cameraPixelBuffer);


            if (cameraPixelBuffer == null ||
                cameraPixelBuffer.Length !=
                    orientation.SourceWidth * orientation.SourceHeight)
            {
                return;
            }

            if (!CameraFrameOrientation.TryRead(webCamTexture, out var afterRead) ||
                !orientation.Equals(afterRead)) return;

            // Resize only once a complete matching frame is available. Keep the
            // Texture instance held by consumers and avoid blank transition frames.
            if (cameraPreviewTexture.width != orientation.SourceWidth ||
                cameraPreviewTexture.height != orientation.SourceHeight)
                cameraPreviewTexture.Reinitialize(orientation.SourceWidth, orientation.SourceHeight);


            lastCopiedUpdateCount =
                currentUpdateCount;


            using (PerformanceDiagnostics.Measure(PerfMetric.TextureUploadMs))
            {
                cameraPreviewTexture.SetPixels32(cameraPixelBuffer);
                cameraPreviewTexture.Apply(false, false);
            }
            previewOrientation = orientation;
            hasPreviewOrientation = true;


            if (!hasReceivedVisiblePixelData &&
                ContainsVisiblePixelData(
                    cameraPixelBuffer))
            {
                hasReceivedVisiblePixelData = true;

                Debug.Log(
                    $"[Avatar Camera] CPU 유효 픽셀 수신 완료: " +
                    $"updateCount={webCamTexture.updateCount}");
            }
        }


        private static bool ContainsVisiblePixelData(
            Color32[] pixels)
        {
            if (pixels == null ||
                pixels.Length == 0)
            {
                return false;
            }


            // 초기 검은 버퍼 판별이 프레임 비용을 키우지 않도록 일부 픽셀만 샘플링합니다.
            int sampleInterval =
                Mathf.Max(1, pixels.Length / 256);


            for (int i = 0;
                 i < pixels.Length;
                 i += sampleInterval)
            {
                Color32 pixel = pixels[i];


                if (pixel.r > 4 ||
                    pixel.g > 4 ||
                    pixel.b > 4)
                {
                    return true;
                }
            }


            return false;
        }


        public void CaptureImage(
            Action<byte[]> onSuccess,
            Action<string> onFailure)
        {
            CaptureImage(onSuccess, onFailure, false);
        }

        public void CaptureUprightImage(Action<byte[]> onSuccess, Action<string> onFailure)
        {
            CaptureImage(onSuccess, onFailure, true);
        }

        private void CaptureImage(Action<byte[]> onSuccess, Action<string> onFailure, bool upright)
        {
#if UNITY_EDITOR || UNITY_STANDALONE

            if (!isActiveAndEnabled)
            {
                onFailure?.Invoke(
                    "PC 이미지 입력 Source가 활성 공용 Host에 연결되지 않았습니다.");
                return;
            }

            if (!IsCameraRunning)
            {
                onFailure?.Invoke(
                    "현재 실행 중인 웹캠이 없습니다.");

                return;
            }


            if (!IsCameraReady)
            {
                onFailure?.Invoke(
                    "카메라가 아직 촬영 가능한 상태가 아닙니다.");

                return;
            }


            if (isCapturing)
                return;


            StartCoroutine(
                CaptureCurrentFrame(
                    onSuccess,
                    onFailure, upright));

#else

            onFailure?.Invoke(
                "EditorAvatarImageSource는 Unity Editor에서만 사용할 수 있습니다.");

#endif
        }


        private IEnumerator CaptureCurrentFrame(
            Action<byte[]> onSuccess,
            Action<string> onFailure, bool upright)
        {
            isCapturing = true;


            if (webCamTexture == null ||
                !webCamTexture.isPlaying ||
                !isCameraReady)
            {
                isCapturing = false;


                onFailure?.Invoke(
                    "카메라가 촬영 가능한 상태가 아닙니다.");


                yield break;
            }


            // UI 입력과 같은 프레임의 카메라 갱신이 끝난 뒤 캡처합니다.
            yield return new WaitForEndOfFrame();
            Texture2D canonicalTexture = null;
            try
            {
                if (webCamTexture == null ||
                    !webCamTexture.isPlaying)
                {
                    onFailure?.Invoke(
                        "촬영 중 웹캠이 종료되었습니다.");

                    yield break;
                }


                if (cameraPreviewTexture == null)
                {
                    onFailure?.Invoke(
                        "카메라 미리보기 Texture가 준비되지 않았습니다.");

                    yield break;
                }


                // 코루틴 대기 중 도착한 마지막 프레임까지 반영합니다.
                CopyCameraFrameToPreview();


                int width =
                    cameraPreviewTexture.width;

                int height =
                    cameraPreviewTexture.height;


                if (width <= 16 ||
                    height <= 16)
                {
                    onFailure?.Invoke(
                        "카메라 이미지 크기가 올바르지 않습니다.");

                    yield break;
                }


                if (upright && !hasPreviewOrientation)
                {
                    onFailure?.Invoke("카메라 방향 정보가 아직 준비되지 않았습니다.");
                    yield break;
                }
                // Use metadata stored with the copied CPU frame, never newer sensor metadata.
                if (upright && !previewOrientation.IsIdentity)
                    canonicalTexture = previewOrientation.CreateUprightTexture(cameraPreviewTexture.GetPixels32());
                byte[] imageBytes = (canonicalTexture != null ? canonicalTexture : cameraPreviewTexture).EncodeToPNG();


                if (imageBytes == null ||
                    imageBytes.Length == 0)
                {
                    onFailure?.Invoke(
                        "촬영 이미지를 생성하지 못했습니다.");

                    yield break;
                }


                Debug.Log(
                    $"[Avatar Camera] 촬영 성공: " +
                    $"{width}x{height}");


                onSuccess?.Invoke(
                    imageBytes);
            }
            finally
            {
                if (canonicalTexture != null) Destroy(canonicalTexture);
                isCapturing = false;
            }
        }


        public void StopCamera()
        {
            isCameraReady = false;
            isCapturing = false;
            hasReceivedVisiblePixelData = false;
            isRecoveringCamera = false;
            lastCopiedUpdateCount = uint.MaxValue;


            if (cameraPreviewUpdateCoroutine != null)
            {
                StopCoroutine(
                    cameraPreviewUpdateCoroutine);

                cameraPreviewUpdateCoroutine = null;
            }


            ReleaseCameraPreviewTexture();


            if (webCamTexture == null)
            {
                RestoreBackgroundCameraUpdates();
                return;
            }


            if (webCamTexture.isPlaying)
            {
                webCamTexture.Stop();
            }


            Destroy(webCamTexture);

            webCamTexture = null;

            RestoreBackgroundCameraUpdates();


            Debug.Log(
                "[Avatar Camera] 카메라 종료");
        }


        private void ReleaseCameraPreviewTexture()
        {
            hasPreviewOrientation = false;
            if (cameraPreviewTexture == null)
            {
                cameraPixelBuffer = null;
                return;
            }

            Destroy(cameraPreviewTexture);

            cameraPreviewTexture = null;
            cameraPixelBuffer = null;
        }


        private void OnEnable()
        {
            // 패널 비활성화 중 중단한 CPU 복사만 재개하고 카메라 연결은 재사용합니다.
            EnsureCameraPreviewUpdates();
        }


        private void OnDisable()
        {
            if (cameraPreviewUpdateCoroutine != null)
            {
                StopCoroutine(
                    cameraPreviewUpdateCoroutine);

                cameraPreviewUpdateCoroutine = null;
            }


            // 자동 재생성 대기 중 패널이 닫힌 경우에는 다음 활성화 시
            // 새 StartCamera 요청이 가능하도록 복구 상태를 해제합니다.
            if (isRecoveringCamera &&
                webCamTexture == null)
            {
                isRecoveringCamera = false;
                isCameraReady = false;

                RestoreBackgroundCameraUpdates();
            }
        }


        private void OnDestroy()
        {
            StopCamera();
        }
    }
}
