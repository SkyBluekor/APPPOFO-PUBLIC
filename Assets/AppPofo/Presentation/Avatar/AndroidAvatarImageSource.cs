using AppPofo.Presentation.Diagnostics;
using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Android;

// 이 코드는 Android에서 AppPofoImagePicker.java를 호출하여
// 시스템 사진 선택창을 열고, 선택된 이미지를 byte[]로 변환해
// 기존 아바타 저장/표시 기능으로 전달하기 위한 코드입니다.

namespace AppPofo.Presentation.Avatar
{
    public sealed class AndroidAvatarImageSource : MonoBehaviour, IAvatarImageSource, IOrientedCameraSource, ICameraFacingSource
    {
        private WebCamTexture webCamTexture;
        private bool isCameraReady;
        private bool isCameraStarting;
        private bool isCapturing;
        private int cameraGeneration;
        private Coroutine startupRoutine;
        private CameraFacing preferredFacing = CameraFacing.Front;
        public CameraFacing CurrentFacing => preferredFacing;
        public bool SupportsRearCamera
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                foreach (WebCamDevice device in WebCamTexture.devices)
                    if (!device.isFrontFacing) return true;
#endif
                return false;
            }
        }
        public bool TrySelectFacing(CameraFacing facing)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (isCameraStarting || IsCameraRunning) return false;
            foreach (WebCamDevice device in WebCamTexture.devices)
                if (device.isFrontFacing == (facing == CameraFacing.Front))
                {
                    preferredFacing = facing;
                    return true;
                }
#endif
            return false;
        }

        private int receivedFrameCount;
        private double lastCameraUpdateAt;
        private double lastCameraGapWarningAt;
        private CameraFrameOrientation previewOrientation;
        private bool hasPreviewOrientation;

        public bool TryGetPreviewFrame(out Texture texture, out CameraFrameOrientation orientation)
        {
            texture = null;
            orientation = default;
            if (!IsCameraReady || receivedFrameCount < 2) return false;
            if ((webCamTexture.didUpdateThisFrame || !hasPreviewOrientation) &&
                CameraFrameOrientation.TryRead(webCamTexture, out var updated))
            {
                previewOrientation = updated;
                hasPreviewOrientation = true;
            }
            if (!hasPreviewOrientation) return false;
            texture = webCamTexture;
            orientation = previewOrientation;
            return true;
        }

        private Action<byte[]> pickSuccess;
        private Action<string> pickFailure;

        public bool IsCameraRunning => webCamTexture != null && webCamTexture.isPlaying;
        public bool IsCameraReady => IsCameraRunning && isCameraReady;

        private void LateUpdate()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if(webCamTexture == null || !webCamTexture.isPlaying)
            {
                return;
            }

            if(webCamTexture.width <= 16 || webCamTexture.height <= 16)
            {
                return;
            }

            if(!webCamTexture.didUpdateThisFrame)
            {
#if DEVELOPMENT_BUILD
                double now = Time.realtimeSinceStartupAsDouble;
                if (isCameraReady && lastCameraUpdateAt > 0d &&
                    now - lastCameraUpdateAt > .5d &&
                    now - lastCameraGapWarningAt > 2d)
                {
                    lastCameraGapWarningAt = now;
                    Debug.LogWarning("[S4-15] CameraUpdateGap ms=" +
                        ((now - lastCameraUpdateAt) * 1000d).ToString("F1",
                            System.Globalization.CultureInfo.InvariantCulture) +
                        " frame=" + Time.frameCount);
                }
#endif
                return;
            }

            lastCameraUpdateAt = Time.realtimeSinceStartupAsDouble;
            PerformanceDiagnostics.ObserveCamera(webCamTexture);
            receivedFrameCount++;
#endif            
        }
        public void PickImage(
            Action<byte[]> onSuccess,
            Action<string> onFailure
        )
        {
#if UNITY_ANDROID && !UNITY_EDITOR

            if(pickSuccess != null)
            {
                onFailure?.Invoke("이미 사진 선택이 진행 중입니다.");

                return;
            }
            pickSuccess = onSuccess;
            pickFailure = onFailure;

            try
            {
                using AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                using AndroidJavaClass imagePicker = new AndroidJavaClass("com.apppofo.avatar.AppPofoImagePicker");                
                imagePicker.CallStatic("pickImage",activity, gameObject.name);
            }
            catch (Exception ex)
            {
                ClearPickerCallbacks();

                onFailure?.Invoke(
                    $"Android 사진 선택창을 열지 못했습니다.\n" +
                    $"{ex.Message}");                
            }
#else
            onFailure?.Invoke("Android 사진 선택은 실제 Android 기기에서만 사용할 수 있습니다.");
#endif        
        }
        
        public void OnAndroidImagePicked(string temporaryFilePath)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            Action<byte[]> success = pickSuccess;
            Action<string> failure = pickFailure;

            ClearPickerCallbacks();

            if(string.IsNullOrWhiteSpace(temporaryFilePath))
            {
                failure?.Invoke("선택한 이미지 경로가 비어있습니다.");
                return;
            }

            try
            {
                if(!File.Exists(temporaryFilePath))
                {
                    failure?.Invoke("선택한 이미지 파일을 찾을 수 없습니다.");
                    return;                
                }

                byte[] imageBytes = File.ReadAllBytes(temporaryFilePath);

                if(imageBytes == null || imageBytes.Length == 0)
                {
                    failure?.Invoke("선택한 이미지가 비어있습니다.");
                    return;
                }

                File.Delete(temporaryFilePath);

                success?.Invoke(imageBytes);                
            }
            catch(Exception ex)
            {
                failure?.Invoke(
                    $"선택한 이미지를 읽지 못했습니다.\n"+ $"{ex.Message}"
                );
            }
#endif            
        }

        public void OnAndroidImagePickFailed(string message)
        {
#if UNITY_ANDROID && !UNITY_EDITOR            

            Action<string> failure = pickFailure;
            ClearPickerCallbacks();

            failure?.Invoke(string.IsNullOrWhiteSpace(message) ? "Android 사진 선택에 실패했습니다." :message);
#endif            
        }

        public void OnAndroidImagePickCancelled(string message)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            // Java 플러그인은 사용자가 선택창을 닫으면 이 메서드를 호출합니다.
            // 콜백을 남겨 두면 다음 이미지 선택이 "진행 중"으로 오인됩니다.
            ClearPickerCallbacks();
#endif
        }

        private void ClearPickerCallbacks()
        {
            pickSuccess = null;
            pickFailure = null;
        }

        public void StartCamera(Action<Texture> onPreviewReady, Action<string> onFailure)
        {
#if UNITY_ANDROID && !UNITY_EDITOR            
            if (!isActiveAndEnabled)
            {
                onFailure?.Invoke(
                    "Android 이미지 입력 Source가 활성 공용 Host에 연결되지 않았습니다.");
                return;
            }

            if(IsCameraReady)
            {
                onPreviewReady?.Invoke(webCamTexture);
                return;
            }

            if (isCameraStarting)
            {
                onFailure?.Invoke("카메라가 이미 시작 중입니다.");
                return;
            }

            isCameraStarting = true;
            int generation = ++cameraGeneration;

            if (UnityEngine.Android.Permission.HasUserAuthorizedPermission(
                    UnityEngine.Android.Permission.Camera))
            {
                startupRoutine = StartCoroutine(StartCameraRoutine(onPreviewReady,onFailure, generation));
                return;                
            }

            PermissionCallbacks callbacks = new PermissionCallbacks();

            callbacks.PermissionGranted += permissionName =>
            {
                if (!isActiveAndEnabled || cameraGeneration != generation) return;
                startupRoutine = StartCoroutine(StartCameraRoutine(onPreviewReady,onFailure,generation));
            };
            
            callbacks.PermissionDenied += 
                permissionName =>
                {
                    if (cameraGeneration != generation) return;
                    isCameraStarting = false;
                    onFailure?.Invoke("카메라 사용 권한이 거부되었습니다.");
                };

            callbacks.PermissionDeniedAndDontAskAgain +=
                permissionName =>
                {
                    if (cameraGeneration != generation) return;
                    isCameraStarting = false;
                    onFailure?.Invoke(
                        "카메라 권한이 차단되어 있습니다. " +
                        "Android 설정에서 카메라 권한을 허용해주세요.");
                };           

            try
            {
                UnityEngine.Android.Permission.RequestUserPermission(
                    UnityEngine.Android.Permission.Camera, callbacks);
            }
            catch (Exception error)
            {
                if (generation == cameraGeneration)
                {
                    isCameraStarting = false;
                    onFailure?.Invoke("카메라 권한 요청에 실패했습니다: " + error.Message);
                }
            }
#else
            onFailure?.Invoke("안드로이드 카메라는 실제 안드로이드 기기에서만 사용 가능합니다.");
#endif            

        }

        private IEnumerator StartCameraRoutine(Action<Texture> onPreviewReady,
            Action<string> onFailure, int generation)
        {
            receivedFrameCount = 0;
            isCameraReady = false;

            WebCamDevice[] devices = WebCamTexture.devices;

            if(devices == null || devices.Length == 0)
            {
                isCameraStarting = false;

                onFailure?.Invoke("사용 가능한 카메라를 찾을 수 없습니다.");

                yield break;
            }
            int selectedIndex = -1;
            for (int i = 0; i < devices.Length; i++)
            {
                if (devices[i].isFrontFacing == (preferredFacing == CameraFacing.Front))
                {
                    selectedIndex = i;
                    break;
                }
            }
            if (selectedIndex < 0)
            {
                isCameraStarting = false;
                onFailure?.Invoke(preferredFacing == CameraFacing.Front
                    ? "전면 카메라를 찾을 수 없습니다." : "후면 카메라를 찾을 수 없습니다.");
                yield break;
            }
            WebCamDevice selectedDevice = devices[selectedIndex];

            Debug.Log(
                $"[Android Avatar Camera] 선택된 카메라: " +
                $"{selectedDevice.name}, " +
                $"Front={selectedDevice.isFrontFacing}");

            try
            {
                webCamTexture = new WebCamTexture(selectedDevice.name);
                webCamTexture.Play();
                lastCameraUpdateAt = Time.realtimeSinceStartupAsDouble;
                lastCameraGapWarningAt = 0d;
            }
            catch (Exception error)
            {
                if (webCamTexture != null)
                {
                    if (webCamTexture.isPlaying) webCamTexture.Stop();
                    Destroy(webCamTexture);
                    webCamTexture = null;
                }
                isCameraStarting = false;
                startupRoutine = null;
                onFailure?.Invoke("카메라 장치를 열지 못했습니다: " + error.Message);
                yield break;
            }

            const float timeout = 10f;
            float elapsed = 0f;

            while(elapsed < timeout)
            {
                if (generation != cameraGeneration || !isActiveAndEnabled) yield break;
                bool validSize = 
                    webCamTexture != null &&
                    webCamTexture.width > 16 &&
                    webCamTexture.height > 16;

                if(validSize && receivedFrameCount >= 2)
                {
                    isCameraReady = true;
                    isCameraStarting = false;
                    startupRoutine = null;

                    Debug.Log(
                        $"[Android Avatar Camera] 준비 완료: " +
                        $"{webCamTexture.width}x" +
                        $"{webCamTexture.height}");


                    onPreviewReady?.Invoke(
                        webCamTexture);


                    yield break;
                }

                elapsed += Time.unscaledDeltaTime;

                yield return null;
            }

            if (generation != cameraGeneration) yield break;
            // Do not StopCoroutine(this coroutine) from within itself:
            // report the timeout to its current owner before it exits.
            if (webCamTexture != null)
            {
                if (webCamTexture.isPlaying) webCamTexture.Stop();
                Destroy(webCamTexture);
                webCamTexture = null;
            }
            isCameraReady = false;
            isCameraStarting = false;
            receivedFrameCount = 0;
            hasPreviewOrientation = false;
            startupRoutine = null;
            onFailure?.Invoke("Android 카메라의 영상 프레임을 받지 못했습니다.");            
        }

        public void CaptureImage(
            Action<byte[]> onSuccess, Action<string> onFailure
        )
        {
            CaptureImage(onSuccess, onFailure, false);
        }

        public void CaptureUprightImage(Action<byte[]> onSuccess, Action<string> onFailure)
        {
            CaptureImage(onSuccess, onFailure, true);
        }

        private void CaptureImage(Action<byte[]> onSuccess, Action<string> onFailure, bool upright)
        {
#if UNITY_ANDROID && !UNITY_EDITOR            
            if (!isActiveAndEnabled)
            {
                onFailure?.Invoke(
                    "Android 이미지 입력 Source가 활성 공용 Host에 연결되지 않았습니다.");
                return;
            }

            if(!isCameraReady)
            {
                onFailure?.Invoke("카메라가 아직 촬영 가능 상태가 아닙니다.");
                return;
            }

            if(isCapturing)
                return;

            StartCoroutine(CaptureCurrentFrame(onSuccess,onFailure,upright));
#else
            onFailure?.Invoke("안드로이드 카메라는 실제 안드로이드 기기에서만 사용 가능합니다.");
#endif

        }

        private IEnumerator CaptureCurrentFrame(Action<byte[]> onSuccess, Action<string> onFailure, bool upright)
        {
            isCapturing = true;

            yield return new WaitForEndOfFrame();

            Texture2D capturedTexture = null;

            try
            {
                if(webCamTexture == null || !webCamTexture.isPlaying)
                {
                    onFailure?.Invoke("촬영 중 카메라가 종료되었습니다.");

                    yield break;
                }

                int width = webCamTexture.width;
                int height = webCamTexture.height;
                CameraFrameOrientation orientation = default;
                if (upright && !TryGetPreviewFrame(out _, out orientation))
                {
                    onFailure?.Invoke("카메라 방향 정보가 아직 준비되지 않았습니다.");
                    yield break;
                }
                Color32[] pixels = webCamTexture.GetPixels32();

                if(pixels == null || pixels.Length == 0)
                {
                    onFailure?.Invoke("카메라 이미지 데이터를 가져오지 못했습니다.");
                    yield break;
                }

                if (upright)
                {
                    if (!CameraFrameOrientation.TryRead(webCamTexture, out var afterRead) ||
                        !afterRead.Equals(orientation) || pixels.Length != orientation.SourceWidth * orientation.SourceHeight)
                    {
                        onFailure?.Invoke("카메라 방향이 변경 중입니다. 다시 촬영해주세요.");
                        yield break;
                    }
                    capturedTexture = orientation.CreateUprightTexture(pixels);
                }
                else
                {
                    capturedTexture = new Texture2D(width,height,TextureFormat.RGB24,false);
                    capturedTexture.SetPixels32(pixels);
                    capturedTexture.Apply();
                }

                byte[] imagesBytes = capturedTexture.EncodeToPNG();

                if(imagesBytes == null || imagesBytes.Length == 0)
                {
                    onFailure?.Invoke("촬영 이미지를 생성하지 못했습니다.");

                    yield break;
                }

                onSuccess?.Invoke(imagesBytes);
            }
            finally
            {
                if(capturedTexture != null)
                {
                    Destroy(capturedTexture);
                }
                isCapturing = false;
            }
        }

        public void StopCamera()
        {
            cameraGeneration++;
            if (startupRoutine != null)
            {
                StopCoroutine(startupRoutine);
                startupRoutine = null;
            }
            hasPreviewOrientation = false;
            isCameraReady = false;
            isCameraStarting = false;
            isCapturing = false;

            receivedFrameCount = 0;
            lastCameraUpdateAt = 0d;
            lastCameraGapWarningAt = 0d;

            if (webCamTexture == null)
                return;


            if (webCamTexture.isPlaying)
                webCamTexture.Stop();


            Destroy(
                webCamTexture);


            webCamTexture = null;
        }
        private void OnDisable()
        {
            StopCamera();
        }
        private void OnDestroy()
        {
            StopCamera();
            ClearPickerCallbacks();            
        }

    }
}
