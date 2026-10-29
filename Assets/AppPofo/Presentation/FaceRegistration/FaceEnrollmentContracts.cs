using System;
using System.Collections;
using System.Collections.Generic;
using AppPofo.Application.Abstractions;
using AppPofo.Domain.Entities;
using AppPofo.Domain.ValueObjects;

namespace AppPofo.Presentation.FaceRegistration
{
    // Transport-neutral face features. Local long IDs and cloud UUIDs belong to the writer,
    // not to the camera or to the embedding data.
    public sealed class FaceEmbeddingSet
    {
        private readonly float[] front;
        private readonly float[] left;
        private readonly float[] right;

        public string ModelVersion { get; }
        public float[] Front => (float[])front.Clone();
        public float[] Left => (float[])left.Clone();
        public float[] Right => (float[])right.Clone();

        public FaceEmbeddingSet(float[] front, float[] left, float[] right, string modelVersion)
        {
            if (string.IsNullOrWhiteSpace(modelVersion))
                throw new ArgumentException("얼굴 모델 버전이 필요합니다.", nameof(modelVersion));
            this.front = CopyAndValidate(front, nameof(front));
            this.left = CopyAndValidate(left, nameof(left));
            this.right = CopyAndValidate(right, nameof(right));
            ModelVersion = modelVersion.Trim();
        }

        private static float[] CopyAndValidate(float[] values, string name)
        {
            if (values == null || values.Length != 128)
                throw new ArgumentException("얼굴 임베딩은 128차원이어야 합니다.", name);
            double squares = 0;
            foreach (float value in values)
            {
                if (float.IsNaN(value) || float.IsInfinity(value))
                    throw new ArgumentException("임베딩에 유효하지 않은 숫자가 있습니다.", name);
                squares += (double)value * value;
            }
            if (Math.Abs(Math.Sqrt(squares) - 1.0) > 0.001)
                throw new ArgumentException("얼굴 임베딩은 L2 정규화되어야 합니다.", name);
            return (float[])values.Clone();
        }
    }

    public enum FaceEnrollmentSaveStatus
    {
        Success,
        Failed,
        Unknown,
        Unavailable
    }

    public sealed class FaceEnrollmentSaveResult
    {
        public FaceEnrollmentSaveStatus Status { get; }
        public string Message { get; }

        public FaceEnrollmentSaveResult(FaceEnrollmentSaveStatus status, string message = null)
        {
            Status = status;
            Message = message ?? string.Empty;
        }
    }

    // A single asynchronous contract for the camera; cancellation is owned by its
    // running Coroutine/session generation, rather than by a particular transport.
    public interface IFaceEnrollmentWriter
    {
        IEnumerator Save(FaceEmbeddingSet embeddings, Action<FaceEnrollmentSaveResult> completed);
    }

    public sealed class LocalFaceEnrollmentWriter : IFaceEnrollmentWriter
    {
        private readonly Func<IFaceEmbeddingRepository> repositoryProvider;
        private readonly long profileId;

        public LocalFaceEnrollmentWriter(IFaceEmbeddingRepository repository, long profileId)
            : this(() => repository, profileId)
        {
            if (repository == null) throw new ArgumentNullException(nameof(repository));
        }

        public LocalFaceEnrollmentWriter(Func<IFaceEmbeddingRepository> repositoryProvider, long profileId)
        {
            this.repositoryProvider = repositoryProvider ?? throw new ArgumentNullException(nameof(repositoryProvider));
            if (profileId <= 0) throw new ArgumentOutOfRangeException(nameof(profileId));
            this.profileId = profileId;
        }

        public IEnumerator Save(FaceEmbeddingSet embeddings, Action<FaceEnrollmentSaveResult> completed)
        {
            if (completed == null) throw new ArgumentNullException(nameof(completed));
            if (embeddings == null)
            {
                completed(new FaceEnrollmentSaveResult(FaceEnrollmentSaveStatus.Failed, "얼굴 특징 정보가 없습니다."));
                yield break;
            }

            FaceEnrollmentSaveResult result;
            try
            {
                DateTimeOffset now = DateTimeOffset.UtcNow;
                var faceEmbeddings = new List<FaceEmbedding>
                {
                    NewEmbedding(embeddings.Front, embeddings.ModelVersion, now),
                    NewEmbedding(embeddings.Left, embeddings.ModelVersion, now),
                    NewEmbedding(embeddings.Right, embeddings.ModelVersion, now)
                };
                // Existing SQLite implementation uses RunInTransaction: all three
                // templates are replaced together, or the old set remains.
                IFaceEmbeddingRepository repository = repositoryProvider();
                if (repository == null)
                    throw new InvalidOperationException("로컬 얼굴 저장소가 준비되지 않았습니다.");
                repository.ReplaceByProfile(profileId, faceEmbeddings);
                result = new FaceEnrollmentSaveResult(FaceEnrollmentSaveStatus.Success);
            }
            catch (Exception error)
            {
                UnityEngine.Debug.LogWarning("[FaceEnrollment] 로컬 저장 실패: " + error.GetType().Name);
                result = new FaceEnrollmentSaveResult(FaceEnrollmentSaveStatus.Failed,
                    "얼굴정보를 저장하지 못했습니다. 다시 시도해 주세요.");
            }
            // Never invoke a UI callback from inside the storage try/catch:
            // a callback failure must not be misclassified as a DB failure.
            completed(result);
        }

        private FaceEmbedding NewEmbedding(float[] values, string version, DateTimeOffset now)
        {
            return new FaceEmbedding(0, profileId, values, version, null,
                FaceEmbeddingSourceType.Camera, now);
        }
    }

    // S5-08-A is a client boundary only. S5-08-B must supply an authenticated,
    // consent-gated writer backed by a verified Worker/D1 API. Never use SQLite
    // as a fallback, and never report an unimplemented server write as success.
    public sealed class PendingCloudFaceEnrollmentWriter : IFaceEnrollmentWriter
    {
        public string CloudProfileId { get; }

        public PendingCloudFaceEnrollmentWriter(string cloudProfileId)
        {
            if (!Guid.TryParse(cloudProfileId, out _))
                throw new ArgumentException("올바른 서버 프로필 UUID가 필요합니다.", nameof(cloudProfileId));
            CloudProfileId = cloudProfileId;
        }

        public IEnumerator Save(FaceEmbeddingSet embeddings, Action<FaceEnrollmentSaveResult> completed)
        {
            if (completed == null) throw new ArgumentNullException(nameof(completed));
            completed(new FaceEnrollmentSaveResult(FaceEnrollmentSaveStatus.Unavailable,
                "서버 얼굴 저장 기능을 준비하고 있습니다. 현재 얼굴정보는 전송되거나 저장되지 않았습니다."));
            yield break;
        }
    }
}
