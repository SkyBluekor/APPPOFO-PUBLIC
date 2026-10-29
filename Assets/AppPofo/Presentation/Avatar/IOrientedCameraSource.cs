using System;
using UnityEngine;

namespace AppPofo.Presentation.Avatar
{
    /// <summary>Optional camera contract; picker and existing avatar transport remain unchanged.</summary>
    public interface IOrientedCameraSource
    {
        // Metadata belongs to the returned raw preview frame, including CPU-backed previews.
        bool TryGetPreviewFrame(out Texture texture, out CameraFrameOrientation orientation);
        // Read pixels and metadata together at capture time, return upright non-selfie PNG.
        void CaptureUprightImage(Action<byte[]> onSuccess, Action<string> onFailure);
    }
}
