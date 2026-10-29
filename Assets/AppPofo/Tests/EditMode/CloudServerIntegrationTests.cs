using System;
using System.Reflection;
using AppPofo.Presentation.Common;
using AppPofo.Presentation.FaceRecognition;
using AppPofo.Presentation.FaceRegistration;
using NUnit.Framework;

namespace AppPofo.Tests.EditMode
{
    public sealed class CloudServerIntegrationTests
    {
        [Test]
        public void ProductionFaces_RequireExplicitBuildFlagAndMatchingServerEnvironment()
        {
            var field=typeof(CloudServerConfiguration).GetField("settings",BindingFlags.NonPublic|BindingFlags.Static);
            object previous=field.GetValue(null);
            var accepts=typeof(CloudFaceRuntime).GetMethod("AcceptsConfiguration",BindingFlags.NonPublic|BindingFlags.Static);
            try
            {
                field.SetValue(null,new CloudServerConfiguration.Settings{environment="production"});
                Assert.That(CloudServerConfiguration.RemoteFacesEnabled,Is.False);
                string originalScope=CloudServerConfiguration.Scope;
                field.SetValue(null,new CloudServerConfiguration.Settings{environment="production",production_faces_enabled=true,
                    staging_base_url="https://apppofo-profile-api-staging.synthetic.workers.dev"});
                Assert.That(CloudServerConfiguration.RemoteFacesEnabled,Is.True);
                Assert.That(CloudServerConfiguration.IsStaging,Is.False);
                Assert.That(CloudServerConfiguration.BaseUrl,Is.EqualTo(CloudServerConfiguration.ProductionUrl));
                Assert.That(CloudServerConfiguration.Scope,Is.EqualTo(originalScope),"Production device identity scope must remain compatible.");
                var config=new CloudFaceRuntime.Configuration{environment="production",decision_version="production-v1",
                    model_version="sface_2021dec",enrollment_policy="production-face-storage-v1",scan_policy="production-face-scan-v1"};
                Assert.That((bool)accepts.Invoke(null,new object[]{config}),Is.True);
                config.environment="staging";
                Assert.That((bool)accepts.Invoke(null,new object[]{config}),Is.False);
                config.environment="production";config.decision_version="staging-unvalidated-v1";
                Assert.That((bool)accepts.Invoke(null,new object[]{config}),Is.False);
                config.decision_version="production-v1";config.model_version="wrong";
                Assert.That((bool)accepts.Invoke(null,new object[]{config}),Is.False);
                field.SetValue(null,new CloudServerConfiguration.Settings{environment="staging",production_faces_enabled=true,
                    staging_base_url="https://apppofo-profile-api-staging.synthetic.workers.dev"});
                Assert.That(CloudServerConfiguration.RemoteFacesEnabled,Is.False,"Production opt-in must not enable staging.");
            }
            finally{field.SetValue(null,previous);}
        }

        [Test]
        public void ProductionDecisionVersion_RejectsStagingAndLocalResponses()
        {
            const string response="{\"matched\":true,\"profile\":{\"id\":\"11111111-1111-4111-8111-111111111111\",\"display_name\":\"Synthetic\",\"headline\":\"Role\"},\"decision_version\":\"production-v1\"}";
            Assert.That(CloudFaceMatchCodec.TryReadResponse(response,out _,"production-v1"),Is.True);
            Assert.That(CloudFaceMatchCodec.TryReadResponse(response,out _,"staging-unvalidated-v1"),Is.False);
            Assert.That(CloudFaceMatchCodec.TryReadResponse(response,out _),Is.False);
        }
        [Test]
        public void CaptureApprovalSnapshot_IsKeptUntilSave_InsteadOfReadingANewerConsentOrPurgeState()
        {
            var approved=new CloudFaceStatusData {revision=3,purge_epoch=2,consent_generation=7};
            var transport=new CloudFaceHttpsTransport(approved,"11111111-1111-4111-8111-111111111111");
            approved.revision=99;
            CloudFaceStatusData returned=null;
            var routine=transport.GetStatus("11111111-1111-4111-8111-111111111111",null,(_,state)=>returned=state);
            Assert.That(routine.MoveNext(),Is.False,"The writer uses the pre-capture snapshot, without a later network GET.");
            Assert.That(returned.revision,Is.EqualTo(3));
            Assert.That(returned.purge_epoch,Is.EqualTo(2));
            Assert.That(returned.consent_generation,Is.EqualTo(7));
            CloudFaceTransportResult rejected=null;
            var wrongTarget=transport.GetStatus("22222222-2222-4222-8222-222222222222",null,(result,_)=>rejected=result);
            Assert.That(wrongTarget.MoveNext(),Is.False);
            Assert.That(rejected.Status,Is.EqualTo(CloudFaceTransportStatus.Rejected));
        }

        [Test]
        public void DispatchBudget_BoundsDelayedAndCompressedHttpCalls_IndependentlyOfInferenceStarts()
        {
            var budget=new CloudFaceRequestBudget();
            for(int i=0;i<120;i++) Assert.That(budget.TryReserve(5+i*.001d),Is.True);
            Assert.That(budget.TryReserve(60),Is.False,"An inference start at t=0 sent at t=5 still counts at t=60.");
            Assert.That(budget.TryReserve(65),Is.True);
            Assert.That(budget.TryReserve(65),Is.False);
        }

        [TestCase("http://apppofo-profile-api-staging.synthetic.workers.dev")]
        [TestCase("https://portfolio-api.example.invalid")]
        [TestCase("https://demo@example.invalid")]
        [TestCase("https://apppofo-profile-api-staging.synthetic.workers.dev/api")]
        [TestCase("https://apppofo-profile-api-staging.synthetic.workers.dev?x=1")]
        [TestCase("https://apppofo-profile-api-staging.synthetic.workers.dev#fragment")]
        [TestCase("https://127.0.0.1")]
        public void StagingRejectsUnsafeOrProductionEndpoints(string endpoint) =>
            Assert.That(CloudServerConfiguration.ValidStagingUrl(endpoint), Is.False);

        [Test]
        public void StagingDoesNotFallBackToProductionOrLocalBiometrics()
        {
            FieldInfo field=typeof(CloudServerConfiguration).GetField("settings",BindingFlags.NonPublic|BindingFlags.Static);
            object previous=field.GetValue(null);
            try
            {
                field.SetValue(null,new CloudServerConfiguration.Settings {environment="staging"});
                Assert.That(CloudServerConfiguration.BaseUrl,Is.EqualTo("https://invalid.invalid"));
                Assert.That(CloudFaceMatchApi.CanQuery,Is.False);
                Assert.That(CloudFaceMatchApi.QueryReadiness,Is.EqualTo("StagingUnavailable"));
                Assert.That(CloudServerConfiguration.ValidStagingUrl("https://apppofo-profile-api-staging.synthetic.workers.dev"),Is.True);
            }
            finally { field.SetValue(null,previous); }
        }

        [Test]
        public void StagingCosmeticUrlChangesKeepTheSameDeviceIdentityScope()
        {
            FieldInfo field=typeof(CloudServerConfiguration).GetField("settings",BindingFlags.NonPublic|BindingFlags.Static);
            object previous=field.GetValue(null);
            try
            {
                field.SetValue(null,new CloudServerConfiguration.Settings {environment="staging",
                    staging_base_url="https://apppofo-profile-api-staging.synthetic.workers.dev"});
                string scope=CloudServerConfiguration.Scope;
                field.SetValue(null,new CloudServerConfiguration.Settings {environment="staging",
                    staging_base_url="https://APPPOFO-PROFILE-API-STAGING.SYNTHETIC.WORKERS.DEV:443/"});
                Assert.That(CloudServerConfiguration.Scope,Is.EqualTo(scope));
            }
            finally { field.SetValue(null,previous); }
        }

        [Test]
        public void StagingDecisionVersionDoesNotAcceptLocalTestIdentityResponses()
        {
            const string response="{\"matched\":true,\"profile\":{\"id\":\"11111111-1111-4111-8111-111111111111\",\"display_name\":\"Synthetic\",\"headline\":\"Role\"},\"decision_version\":\"staging-unvalidated-v1\"}";
            Assert.That(CloudFaceMatchCodec.TryReadResponse(response,out _),Is.False);
            Assert.That(CloudFaceMatchCodec.TryReadResponse(response,out var card,"staging-unvalidated-v1"),Is.True);
            Assert.That(card.profile.display_name,Is.EqualTo("Synthetic"));
        }
    }
}
