using UnityRect = UnityEngine.Rect;
using Mat = OpenCvSharp.Mat;
using CvRect = OpenCvSharp.Rect;
using UnityEngine;
using System;

namespace AppPofo.Presentation.FaceRegistration.Detection
{
    public static class FaceCropper
    {
    /// <summary>Same clamping and rounding as the registration Mat crop.
    /// Input and result are in upright TOP-LEFT coordinates.</summary>
    public static RectInt BuildTopLeftPixelRoi(UnityRect bounds, int width, int height)
    {
        int left = Mathf.Clamp(Mathf.FloorToInt(bounds.x), 0, width);
        int top = Mathf.Clamp(Mathf.FloorToInt(bounds.y), 0, height);
        int right = Mathf.Clamp(Mathf.CeilToInt(bounds.x + bounds.width), 0, width);
        int bottom = Mathf.Clamp(Mathf.CeilToInt(bounds.y + bounds.height), 0, height);
        return new RectInt(left, top, Mathf.Max(0, right - left),
            Mathf.Max(0, bottom - top));
    }

    public static Mat Crop(
        Mat frame,
        UnityRect bounds,
        out CvRect roi)
    {
        roi = default;
        if (frame == null || frame.Empty())
        {
            return new Mat();
        }

        RectInt pixelRoi = BuildTopLeftPixelRoi(bounds, frame.Cols, frame.Rows);
        int left = pixelRoi.x;
        int top = pixelRoi.y;
        int width = pixelRoi.width;
        int height = pixelRoi.height;

        if (width <= 0 || height <= 0)
        {
            return new Mat();
        }

        CvRect cropRoi = new CvRect(
            left,
            top,
            width,
            height
        );

        using Mat view = new Mat(frame, cropRoi);

        Mat faceMat = view.Clone();
        if (!faceMat.Empty())
            roi = cropRoi;
        return faceMat;
    }
    }
}
