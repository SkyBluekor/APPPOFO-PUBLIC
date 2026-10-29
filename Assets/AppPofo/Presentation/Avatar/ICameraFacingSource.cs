namespace AppPofo.Presentation.Avatar
{
    /// <summary>Optional camera-lens selection contract. Does not alter avatar file picking.</summary>
    public enum CameraFacing { Front, Rear }

    public interface ICameraFacingSource
    {
        CameraFacing CurrentFacing { get; }
        bool SupportsRearCamera { get; }
        /// <summary>Change the preferred lens while stopped. False if busy/unavailable.</summary>
        bool TrySelectFacing(CameraFacing facing);
    }
}
