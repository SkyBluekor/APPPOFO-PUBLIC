using System;
using System.Collections;
using System.Collections.Generic;
using AppPofo.Application.Abstractions;
using AppPofo.Domain.Entities;
using AppPofo.Presentation.FaceRegistration;
using NUnit.Framework;

namespace AppPofo.Tests.EditMode
{
    public class FaceEnrollmentContractTests
    {
        private static float[] UnitVector(int index)
        {
            var vector = new float[128];
            vector[index] = 1f;
            return vector;
        }

        private static FaceEmbeddingSet Set()
        {
            return new FaceEmbeddingSet(UnitVector(0), UnitVector(1), UnitVector(2), "SFace-Test");
        }

        private static void Complete(IEnumerator coroutine)
        {
            while (coroutine.MoveNext())
                Assert.Fail("The local and pending writers must complete without a network wait.");
        }

        [Test]
        public void EmbeddingSet_DefensivelyCopiesAllThreeVectors()
        {
            float[] front = UnitVector(0);
            var data = new FaceEmbeddingSet(front, UnitVector(1), UnitVector(2), "SFace-Test");
            front[0] = 0f;
            float[] read = data.Front;
            read[0] = 0f;

            Assert.That(data.Front[0], Is.EqualTo(1f));
            Assert.That(data.Left[1], Is.EqualTo(1f));
            Assert.That(data.Right[2], Is.EqualTo(1f));
        }

        [Test]
        public void EmbeddingSet_RejectsInvalidDimensionsOrNorm()
        {
            Assert.Throws<ArgumentException>(() =>
                new FaceEmbeddingSet(new float[127], UnitVector(1), UnitVector(2), "model"));
            Assert.Throws<ArgumentException>(() =>
                new FaceEmbeddingSet(new float[128], UnitVector(1), UnitVector(2), "model"));
            var invalid = UnitVector(0);
            invalid[0] = float.NaN;
            Assert.Throws<ArgumentException>(() =>
                new FaceEmbeddingSet(invalid, UnitVector(1), UnitVector(2), "model"));
        }

        [Test]
        public void TransportCodec_EmitsThreePosesInStrictLittleEndianOrder()
        {
            byte[] bytes = FaceEmbeddingTransportCodec.ToLittleEndianBytes(Set());
            Assert.That(bytes, Has.Length.EqualTo(1536));
            // float32(1.0) little-endian: 00 00 80 3F.
            Assert.That(bytes[0], Is.EqualTo(0));
            Assert.That(bytes[1], Is.EqualTo(0));
            Assert.That(bytes[2], Is.EqualTo(128));
            Assert.That(bytes[3], Is.EqualTo(63));
            Assert.That(bytes[4], Is.Zero);
            Assert.That(bytes[512 + 4 + 2], Is.EqualTo(128));
            Assert.That(bytes[512 + 4 + 3], Is.EqualTo(63));
            Assert.That(bytes[1024 + 8 + 2], Is.EqualTo(128));
            Assert.That(bytes[1024 + 8 + 3], Is.EqualTo(63));
        }

        [Test]
        public void TransportCodec_ProducesCanonicalUnpaddedBase64Url()
        {
            string encoded = FaceEmbeddingTransportCodec.ToBase64Url(Set());
            Assert.That(encoded.Length, Is.EqualTo(2048));
            Assert.That(encoded, Does.Not.Contain("="));
            Assert.That(encoded, Does.Not.Contain("+"));
            Assert.That(encoded, Does.Not.Contain("/"));
            byte[] decoded = Convert.FromBase64String(encoded.Replace('-', '+').Replace('_', '/'));
            Assert.That(decoded, Is.EqualTo(FaceEmbeddingTransportCodec.ToLittleEndianBytes(Set())));
        }

        [Test]
        public void LocalWriter_SavesThreeFacesForItsOwnNumericProfile()
        {
            var repository = new FakeRepository();
            var writer = new LocalFaceEnrollmentWriter(repository, 123);
            FaceEnrollmentSaveResult result = null;
            Complete(writer.Save(Set(), value => result = value));

            Assert.That(result.Status, Is.EqualTo(FaceEnrollmentSaveStatus.Success));
            Assert.That(repository.ReplaceCalls, Is.EqualTo(1));
            Assert.That(repository.LastProfileId, Is.EqualTo(123));
            Assert.That(repository.LastEmbeddings.Count, Is.EqualTo(3));
            foreach (FaceEmbedding embedding in repository.LastEmbeddings)
            {
                Assert.That(embedding.ProfileId, Is.EqualTo(123));
                Assert.That(embedding.Values.Count, Is.EqualTo(128));
                Assert.That(embedding.ModelVersion, Is.EqualTo("SFace-Test"));
            }
        }

        [Test]
        public void LocalWriter_ReportsFailureWithoutSuccessOnDatabaseError()
        {
            var repository = new FakeRepository { ThrowOnReplace = true };
            var writer = new LocalFaceEnrollmentWriter(repository, 123);
            FaceEnrollmentSaveResult result = null;
            Complete(writer.Save(Set(), value => result = value));

            Assert.That(result.Status, Is.EqualTo(FaceEnrollmentSaveStatus.Failed));
            Assert.That(repository.ReplaceCalls, Is.EqualTo(1));
        }

        [Test]
        public void LocalWriter_LazilyResolvesRepositoryAndRefusesMissingOne()
        {
            var writer = new LocalFaceEnrollmentWriter(() => null, 123);
            FaceEnrollmentSaveResult result = null;
            Complete(writer.Save(Set(), value => result = value));
            Assert.That(result.Status, Is.EqualTo(FaceEnrollmentSaveStatus.Failed));
        }

        [Test]
        public void CloudWriter_RefusesToClaimUnimplementedServerSave()
        {
            var writer = new PendingCloudFaceEnrollmentWriter(
                "72d8d1df-62bd-443b-af3f-e63c2f6b810e");
            FaceEnrollmentSaveResult result = null;
            Complete(writer.Save(Set(), value => result = value));

            Assert.That(result.Status, Is.EqualTo(FaceEnrollmentSaveStatus.Unavailable));
            Assert.That(result.Message, Does.Contain("저장되지 않았습니다"));
            Assert.That(writer.CloudProfileId, Is.Not.Empty);
        }

        [Test]
        public void CloudWriter_RejectsInvalidUuid()
        {
            Assert.Throws<ArgumentException>(() => new PendingCloudFaceEnrollmentWriter("42"));
        }

        private sealed class FakeRepository : IFaceEmbeddingRepository
        {
            public int ReplaceCalls;
            public long LastProfileId;
            public IReadOnlyList<FaceEmbedding> LastEmbeddings;
            public bool ThrowOnReplace;

            public bool HasByProfile(long profileId) => false;
            public IReadOnlyList<FaceEmbedding> GetByModel(string modelVersion) =>
                Array.Empty<FaceEmbedding>();
            public FaceEmbedding Add(FaceEmbedding embedding) => throw new NotImplementedException();
            public void DeleteByProfile(long profileId) => throw new NotImplementedException();

            public void ReplaceByProfile(long profileId, IReadOnlyList<FaceEmbedding> embeddings)
            {
                ReplaceCalls++;
                if (ThrowOnReplace) throw new InvalidOperationException("Synthetic database failure");
                LastProfileId = profileId;
                LastEmbeddings = embeddings;
            }
        }
    }
}
