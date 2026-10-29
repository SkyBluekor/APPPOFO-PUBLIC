using System;
using UnityEngine;
using Rect = UnityEngine.Rect;
using Mat = OpenCvSharp.Mat;


namespace AppPofo.Presentation.FaceRegistration.Detection
{
    public static class FaceBoundingBoxMapper
    {
        public static Rect ModelToCanonical(
            Rect modelBounds,
            FaceLetterboxMetadata metadata
        )
        {
            float x = (modelBounds.x - metadata.PaddingX) / metadata.Scale;
            float y = (modelBounds.y - metadata.PaddingY) / metadata.Scale;
            float width = modelBounds.width / metadata.Scale;
            float height = modelBounds.height / metadata.Scale;


            return new Rect(x,y,width,height);
        }

        public static FaceLandmarks5 ModelToCanonical(
            FaceLandmarks5 modelLandmarks,
            FaceLetterboxMetadata metadata
        )
        {
            Vector2 MapPoint(Vector2 point) => new Vector2(
                (point.x - metadata.PaddingX) / metadata.Scale,
                (point.y - metadata.PaddingY) / metadata.Scale);

            return new FaceLandmarks5(
                MapPoint(modelLandmarks.RightEye),
                MapPoint(modelLandmarks.LeftEye),
                MapPoint(modelLandmarks.NoseTip),
                MapPoint(modelLandmarks.RightMouthCorner),
                MapPoint(modelLandmarks.LeftMouthCorner));
        }

        public static Rect ClampToImage(
            Rect bounds,
            int imageWidth,
            int imageHeight
        )
        {            
            float left = Mathf.Clamp(bounds.x,0f,imageWidth);
            float top = Mathf.Clamp(bounds.y,0f,imageHeight);
            
            float right = Mathf.Clamp(bounds.x + bounds.width, 0f, imageWidth);
            float bottom = Mathf.Clamp(bounds.y + bounds.height,0f,imageHeight);            

            float width = Mathf.Max(0f, right - left);
            float height = Mathf.Max(0f, bottom - top);

            return new Rect(left,top,width,height);
        }

        public static Rect ExpandMargin(
            Rect bounds,
            float marginRatio
        )
        {
            float marginX = bounds.width * marginRatio;
            float marginY = bounds.height * marginRatio;

            float x = bounds.x - marginX;
            float y = bounds.y - marginY;

            float width = bounds.width + (marginX * 2f);
            float height = bounds.height + (marginY * 2f);

            return new Rect(x,y,width,height);
        }
    }
}
