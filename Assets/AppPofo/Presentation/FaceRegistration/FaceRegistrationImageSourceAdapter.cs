using System;
using AppPofo.Presentation.Avatar;
using UnityEngine;

namespace AppPofo.Presentation.FaceRegistration
{
    /// <summary>
    /// Reuses only the platform byte/camera transport. Face-registration state is
    /// never written to avatar storage or passed to an avatar use case.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FaceRegistrationImageSourceAdapter : MonoBehaviour
    {
        [SerializeField] private AvatarImageSourceResolver transportResolver;

        private IAvatarImageSource source;

        public CameraFacing CurrentFacing => ResolveSource() is ICameraFacingSource source
            ? source.CurrentFacing : CameraFacing.Front;
        public bool SupportsRearCamera => ResolveSource() is ICameraFacingSource source &&
            source.SupportsRearCamera;

        public bool TrySelectFacing(CameraFacing facing) =>
            ResolveSource() is ICameraFacingSource source && source.TrySelectFacing(facing);

        public bool IsCameraRunning => ResolveSource()?.IsCameraRunning == true;
        public bool IsCameraReady => ResolveSource()?.IsCameraReady == true;
        public bool IsSourceHostActive
        {
            get
            {
                IAvatarImageSource resolvedSource = ResolveSource();
                return resolvedSource is MonoBehaviour component &&
                    component.isActiveAndEnabled;
            }
        }

        public void PickImage(Action<byte[]> onSuccess, Action<string> onFailure)
        {
            IAvatarImageSource resolvedSource = ResolveSource();
            if (resolvedSource == null)
            {
                onFailure?.Invoke("사진 입력 기능을 준비하지 못했습니다.");
                return;
            }

            resolvedSource.PickImage(onSuccess, onFailure);
        }

        public void StartCamera(Action<Texture> onReady, Action<string> onFailure)
        {
            IAvatarImageSource resolvedSource = ResolveSource();
            if (resolvedSource == null)
            {
                onFailure?.Invoke("카메라 기능을 준비하지 못했습니다.");
                return;
            }

            resolvedSource.StartCamera(onReady, onFailure);
        }

        public void CaptureImage(Action<byte[]> onSuccess, Action<string> onFailure)
        {
            IAvatarImageSource resolvedSource = ResolveSource();
            if (resolvedSource == null)
            {
                onFailure?.Invoke("카메라 기능을 준비하지 못했습니다.");
                return;
            }

            if (resolvedSource is IOrientedCameraSource orientedSource)
                orientedSource.CaptureUprightImage(onSuccess, onFailure);
            else
                resolvedSource.CaptureImage(onSuccess, onFailure);
        }

        public bool TryGetPreviewFrame(out Texture texture, out CameraFrameOrientation orientation)
        {
            if (ResolveSource() is IOrientedCameraSource orientedSource)
                return orientedSource.TryGetPreviewFrame(out texture, out orientation);
            texture = null;
            orientation = default;
            return false;
        }

        public void StopCamera()
        {
            ResolveSource()?.StopCamera();
        }

        private IAvatarImageSource ResolveSource()
        {
            if (source == null && transportResolver != null)
            {
                source = transportResolver.Resolve();
            }

            return source;
        }
    }
}
