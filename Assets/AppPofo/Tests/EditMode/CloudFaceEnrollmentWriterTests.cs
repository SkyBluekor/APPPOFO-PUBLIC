using System;
using System.Collections;
using System.Collections.Generic;
using AppPofo.Presentation.FaceRegistration;
using AppPofo.Presentation.FaceRegistration.Embedding;
using NUnit.Framework;
using UnityEngine;

namespace AppPofo.Tests.EditMode
{
    public sealed class CloudFaceEnrollmentWriterTests
    {
        private const string ProfileId = "11111111-1111-4111-8111-111111111111";

        [Serializable]
        private sealed class WirePayload
        {
            public string request_id;
            public int expected_revision;
            public int purge_epoch;
            public string model_version;
            public string embeddings_b64url;
            public WireConsent consent;
        }

        [Serializable]
        private sealed class WireConsent
        {
            public string policy_version;
            public bool storage_opt_in;
            public bool discoverable_opt_in;
            public long confirmed_at;
            public bool subject_present;
        }

        private sealed class FakeTransport : ICloudFaceEnrollmentTransport
        {
            public int StatusCalls;
            public int EnrollCalls;
            public CloudFaceTransportStatus UploadOutcome = CloudFaceTransportStatus.Success;
            public CloudFaceTransportStatus InitialStatusOutcome = CloudFaceTransportStatus.Success;
            public bool MalformedAck;
            public bool ConfirmUnknown;
            public int StartingRevision = 2;
            public int Epoch = 5;
            public string LastRequestId;
            public WirePayload LastWire;

            public IEnumerator GetStatus(string profileId, string requestId,
                Action<CloudFaceTransportResult, CloudFaceStatusData> completed)
            {
                StatusCalls++;
                Assert.That(profileId, Is.EqualTo(ProfileId));
                if (requestId == null)
                {
                    if (InitialStatusOutcome != CloudFaceTransportStatus.Success)
                    {
                        completed(new CloudFaceTransportResult(InitialStatusOutcome), null);
                        yield break;
                    }
                    completed(new CloudFaceTransportResult(CloudFaceTransportStatus.Success),
                        new CloudFaceStatusData { revision = StartingRevision,
                            purge_epoch = Epoch, registered = true });
                }
                else
                {
                    Assert.That(requestId, Is.EqualTo(LastRequestId));
                    completed(new CloudFaceTransportResult(CloudFaceTransportStatus.Success),
                        new CloudFaceStatusData {
                            registered = ConfirmUnknown, revision = StartingRevision + 1,
                            purge_epoch = Epoch, request_status = ConfirmUnknown
                                ? "COMMITTED" : "UNKNOWN",
                            request_revision = ConfirmUnknown ? StartingRevision + 1 : 0,
                            model_version = SFaceRecognizer.ModelVersion,
                        });
                }
                yield break;
            }

            public IEnumerator Enroll(string profileId, string json,
                Action<CloudFaceTransportResult, CloudFaceAckData> completed)
            {
                EnrollCalls++;
                Assert.That(profileId, Is.EqualTo(ProfileId));
                LastWire = JsonUtility.FromJson<WirePayload>(json);
                LastRequestId = LastWire.request_id;
                var result = new CloudFaceTransportResult(UploadOutcome);
                var ack = UploadOutcome == CloudFaceTransportStatus.Success ?
                    new CloudFaceAckData { registered = true,
                        revision = StartingRevision + 1,
                        request_id = MalformedAck ? Guid.NewGuid().ToString("D") : LastRequestId,
                        model_version = SFaceRecognizer.ModelVersion } : null;
                completed(result, ack);
                yield break;
            }
        }

        private static FaceEmbeddingSet SampleSet(string model = SFaceRecognizer.ModelVersion)
        {
            var front = new float[128]; front[0] = 1f;
            var left = new float[128]; left[1] = 1f;
            var right = new float[128]; right[2] = 1f;
            return new FaceEmbeddingSet(front, left, right, model);
        }

        // Unity yields nested IEnumerator values; drive all levels without
        // invoking any network or loading the camera scene.
        private static void Run(IEnumerator routine)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(routine);
            while (stack.Count > 0)
            {
                var top = stack.Peek();
                if (!top.MoveNext()) { stack.Pop(); continue; }
                if (top.Current is IEnumerator nested) stack.Push(nested);
                else Assert.Fail("Unexpected non-coroutine yield in synthetic test.");
            }
        }

        [Test]
        public void Save_UsesServerRevisionAndConsent_ForOneComplete128DTriplet()
        {
            var fake = new FakeTransport();
            var writer = new CloudFaceEnrollmentWriter(ProfileId, "synthetic-policy-v1",
                true, true, fake);
            FaceEnrollmentSaveResult output = null;
            Run(writer.Save(SampleSet(), r => output = r));
            Assert.That(output.Status, Is.EqualTo(FaceEnrollmentSaveStatus.Success));
            Assert.That(fake.StatusCalls, Is.EqualTo(1));
            Assert.That(fake.EnrollCalls, Is.EqualTo(1));
            Assert.That(fake.LastWire.expected_revision, Is.EqualTo(2));
            Assert.That(fake.LastWire.purge_epoch, Is.EqualTo(5));
            Assert.That(fake.LastWire.model_version, Is.EqualTo("sface_2021dec"));
            Assert.That(fake.LastWire.consent.storage_opt_in, Is.True);
            Assert.That(fake.LastWire.consent.discoverable_opt_in, Is.True);
            Assert.That(fake.LastWire.consent.policy_version, Is.EqualTo("synthetic-policy-v1"));
            Assert.That(fake.LastWire.consent.subject_present, Is.True);
            Assert.That(fake.LastWire.embeddings_b64url,
                Is.EqualTo(FaceEmbeddingTransportCodec.ToBase64Url(SampleSet())));
            Assert.That(Guid.TryParse(fake.LastWire.request_id, out _), Is.True);
        }

        [Test]
        public void Save_WhenUploadHasUnknownResult_ResolvesOnlyCommittedCurrentReceipt()
        {
            var fake = new FakeTransport { UploadOutcome = CloudFaceTransportStatus.Unknown,
                ConfirmUnknown = true };
            FaceEnrollmentSaveResult output = null;
            Run(new CloudFaceEnrollmentWriter(ProfileId, "synthetic-policy-v1", false, true, fake)
                .Save(SampleSet(), r => output = r));
            Assert.That(output.Status, Is.EqualTo(FaceEnrollmentSaveStatus.Success));
            Assert.That(fake.StatusCalls, Is.EqualTo(2));
            Assert.That(fake.EnrollCalls, Is.EqualTo(1));
            Assert.That(fake.LastWire.consent.discoverable_opt_in, Is.False);
        }

        [Test]
        public void Save_WhenReceiptIsUnconfirmed_RemainsUnknownAndNeverAutoResends()
        {
            var fake = new FakeTransport { UploadOutcome = CloudFaceTransportStatus.Unknown,
                ConfirmUnknown = false };
            FaceEnrollmentSaveResult output = null;
            Run(new CloudFaceEnrollmentWriter(ProfileId, "synthetic-policy-v1", false, true, fake)
                .Save(SampleSet(), r => output = r));
            Assert.That(output.Status, Is.EqualTo(FaceEnrollmentSaveStatus.Unknown));
            Assert.That(fake.StatusCalls, Is.EqualTo(2));
            Assert.That(fake.EnrollCalls, Is.EqualTo(1));
        }

        [Test]
        public void Save_RejectsMalformedSuccessAckUnlessReceiptConfirms()
        {
            var fake = new FakeTransport { MalformedAck = true };
            FaceEnrollmentSaveResult output = null;
            Run(new CloudFaceEnrollmentWriter(ProfileId, "synthetic-policy-v1", false, true, fake)
                .Save(SampleSet(), r => output = r));
            Assert.That(output.Status, Is.EqualTo(FaceEnrollmentSaveStatus.Unknown));
            Assert.That(fake.StatusCalls, Is.EqualTo(2));
        }

        [Test]
        public void Save_RefusesUploadWhenPreflightStateIsUnavailable()
        {
            var fake = new FakeTransport
            {
                InitialStatusOutcome = CloudFaceTransportStatus.Unavailable
            };
            FaceEnrollmentSaveResult output = null;
            Run(new CloudFaceEnrollmentWriter(ProfileId, "synthetic-policy-v1",
                false, true, fake).Save(SampleSet(), result => output = result));
            Assert.That(output.Status, Is.EqualTo(FaceEnrollmentSaveStatus.Unavailable));
            Assert.That(fake.StatusCalls, Is.EqualTo(1));
            Assert.That(fake.EnrollCalls, Is.Zero,
                "Never upload without a server revision and epoch.");
        }

        [Test]
        public void Save_ExplicitServerRejectionIsFailureWithoutBlindRetry()
        {
            var fake = new FakeTransport
            {
                UploadOutcome = CloudFaceTransportStatus.Rejected
            };
            FaceEnrollmentSaveResult output = null;
            Run(new CloudFaceEnrollmentWriter(ProfileId, "synthetic-policy-v1",
                false, true, fake).Save(SampleSet(), result => output = result));
            Assert.That(output.Status, Is.EqualTo(FaceEnrollmentSaveStatus.Failed));
            Assert.That(fake.StatusCalls, Is.EqualTo(1));
            Assert.That(fake.EnrollCalls, Is.EqualTo(1));
        }

        [Test]
        public void Save_RejectsUnsupportedModelBeforeAnyTransport()
        {
            var fake = new FakeTransport();
            FaceEnrollmentSaveResult output = null;
            Run(new CloudFaceEnrollmentWriter(ProfileId, "synthetic-policy-v1", false, true, fake)
                .Save(SampleSet("unsupported-model"), r => output = r));
            Assert.That(output.Status, Is.EqualTo(FaceEnrollmentSaveStatus.Failed));
            Assert.That(fake.EnrollCalls, Is.Zero);
            Assert.That(fake.StatusCalls, Is.Zero);
        }

        [Test]
        public void LoopbackFactory_DefaultDisablesBiometricTransmission()
        {
            // Do not set local biometric test env vars during automated tests.
            // Windows Editor test runners may inherit explicitly opted-in vars;
            // in that case only the factory state (not the real network) changes.
            if (CloudFaceLocalTestFactory.IsLocalCaptureEnabled)
                Assert.Ignore("Explicitly opted-in local real-face test environment.");
            Assert.That(CloudFaceLocalTestFactory.CreateOrPending(ProfileId, false, false),
                Is.TypeOf<PendingCloudFaceEnrollmentWriter>());
        }
    }
}
