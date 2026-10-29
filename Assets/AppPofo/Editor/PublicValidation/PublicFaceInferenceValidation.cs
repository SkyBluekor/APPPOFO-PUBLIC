using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Unity.InferenceEngine;
using OpenCvSharp;
using AppPofo.Presentation.Avatar;
using AppPofo.Presentation.FaceRegistration.Alignment;
using AppPofo.Presentation.FaceRegistration.Detection;
using AppPofo.Presentation.FaceRegistration.Embedding;
using AppPofo.Presentation.FaceRecognition.Matching;

public static class PublicFaceInferenceValidation
{
    [Serializable] private class BackendResult
    {
        public string backend;
        public string status;
        public int detectedFaces;
        public float faceConfidence;
        public int embeddingDimensions;
        public double embeddingNorm;
        public float repeatCosine;
        public float adjustedImageCosine;
        public float asyncCosine;
        public string detail;
    }
    [Serializable] private class Report
    {
        public string fixture = "Docs/TestFixtures/synthetic-face.png";
        public string fixtureProvenance = "Newly generated fictional adult; OpenAI ImageGen; no real identity or original private photograph.";
        public string sfaceGuid;
        public string yunetGuid;
        public string nativeVersion;
        public string nativeBuildInformation;
        public List<BackendResult> results = new List<BackendResult>();
    }
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    public static void Run()
    {
        if (!Application.isBatchMode || PlayerSettings.companyName != "PortfolioDemo")
            throw new InvalidOperationException("Public candidate batch validation only.");
        string output = Environment.GetEnvironmentVariable("APPPOFO_PUBLIC_INFERENCE_OUTPUT");
        if (string.IsNullOrWhiteSpace(output)) throw new InvalidOperationException("Explicit output path required.");
        var report = new Report();
        int exit = 0;
        Texture2D texture = null;
        try
        {
            const string yunetPath = "Assets/AppPofo/Models/FaceDetection/face_detection_yunet_2026may.onnx";
            const string sfacePath = "Assets/AppPofo/Models/FaceRecognition/face_recognition_sface_2021dec.onnx";
            report.yunetGuid = AssetDatabase.AssetPathToGUID(yunetPath);
            report.sfaceGuid = AssetDatabase.AssetPathToGUID(sfacePath);
            Require(report.yunetGuid == "b50bd7ca6efb52c44a98482462ef9421", "YuNet GUID changed.");
            Require(report.sfaceGuid == "bf55204260a4ac84d927fe4c1d8dbaa4", "SFace GUID changed.");
            var yunet = AssetDatabase.LoadAssetAtPath<ModelAsset>(yunetPath);
            var sface = AssetDatabase.LoadAssetAtPath<ModelAsset>(sfacePath);
            Require(yunet != null && sface != null, "Real imported ONNX models required.");
            report.nativeVersion = Cv2.GetVersionString();
            report.nativeBuildInformation = Cv2.GetBuildInformation();
            texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            Require(texture.LoadImage(File.ReadAllBytes(report.fixture)), "Synthetic fixture could not be loaded.");
            foreach (var backend in new[] { BackendType.CPU, BackendType.GPUCompute })
            {
                var result = new BackendResult { backend = backend.ToString(), status = "FAIL" };
                report.results.Add(result);
                if (backend == BackendType.GPUCompute && !SystemInfo.supportsComputeShaders)
                { result.status = "NOT_RUN"; result.detail = "Compute shaders unavailable on this host."; continue; }
                try
                {
                    var policy = backend == BackendType.CPU ? YuNetExecutionPolicy.Registration : new YuNetExecutionPolicy(false, false, 0);
                    using (var detector = new SentisFaceDetector(yunet, policy))
                    {
                        var controller = new FaceDetectionController(detector);
                        using (var frame = controller.ProcessFrameWithCrop(texture, new CameraFrameOrientation(0, false, texture.width, texture.height)))
                        {
                            result.detectedFaces = frame.Detection.FaceCount;
                            result.faceConfidence = frame.Detection.Confidencce;
                            Require(frame.Detection.Found && frame.HasFaceCrop && frame.Detection.HasCurrentFrameBounds, "Real YuNet did not produce a current face crop.");
                            var points = frame.CanonicalLandmarks;
                            var offset = (Vector2)frame.CropOrigin;
                            var local = new FaceLandmarks5(points.RightEye - offset, points.LeftEye - offset, points.NoseTip - offset, points.RightMouthCorner - offset, points.LeftMouthCorner - offset);
                            using (var aligned = FaceAligner.Align(frame.FaceMat, local))
                            using (var recognizer = new SFaceRecognizer(sface, backend))
                            {
                                Require(aligned.Rows == 112 && aligned.Cols == 112 && aligned.Type() == MatType.CV_8UC3, "Native face alignment contract failed.");
                                var first = recognizer.ExtractEmbedding(aligned);
                                var repeat = recognizer.ExtractEmbedding(aligned);
                                result.embeddingDimensions = first.Length;
                                Require(first.Length == 128 && first.All(v => !float.IsNaN(v) && !float.IsInfinity(v)), "Invalid real SFace embedding.");
                                result.embeddingNorm = Math.Sqrt(first.Sum(v => (double)v * v));
                                Require(Math.Abs(result.embeddingNorm - 1) < .001, "SFace output normalization failed.");
                                result.repeatCosine = FaceEmbeddingSimilarity.CosineSimilarity(first, repeat);
                                Require(result.repeatCosine > .999, "Repeated inference differs unexpectedly.");
                                using (var changed = new Mat())
                                {
                                    aligned.ConvertTo(changed, MatType.CV_8UC3, 1, 2);
                                    var adjusted = recognizer.ExtractEmbedding(changed);
                                    result.adjustedImageCosine = FaceEmbeddingSimilarity.CosineSimilarity(first, adjusted);
                                    Require(result.adjustedImageCosine > .9, "Small synthetic brightness perturbation did not preserve this fixture's match.");
                                }
                                recognizer.BeginAsync(aligned, 4);
                                int guard = 0;
                                while (recognizer.IsScheduling) { recognizer.AdvanceAsyncSchedule(); Require(++guard < 4096, "SFace scheduling did not complete."); }
                                if (backend == BackendType.GPUCompute) UnityEngine.Rendering.AsyncGPUReadback.WaitAllRequests();
                                var deadline = DateTime.UtcNow.AddSeconds(30);
                                while (!recognizer.IsAsyncComplete) { System.Threading.Thread.Sleep(1); Require(DateTime.UtcNow < deadline, "SFace readback timed out."); }
                                result.asyncCosine = FaceEmbeddingSimilarity.CosineSimilarity(first, recognizer.CompleteAsync());
                                Require(result.asyncCosine > .999, "Actual async inference differs from sync inference.");
                            }
                        }
                    }
                    result.status = "PASS";
                    result.detail = "Real YuNet -> camera orientation/crop -> native five-landmark affine alignment -> real SFace sync, perturbed input and layer-budget async -> normalized comparison. No mocks or preprocessing-only diagnostic bypass.";
                }
                catch (Exception error) { result.detail = error.ToString(); exit = 1; }
            }
        }
        catch (Exception error)
        { report.results.Add(new BackendResult { backend = "setup", status = "FAIL", detail = error.ToString() }); exit = 1; }
        finally
        {
            if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
            File.WriteAllText(output, JsonUtility.ToJson(report, true));
            EditorApplication.Exit(exit);
        }
    }
}
