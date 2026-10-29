using AppPofo.Presentation.Common;
using NUnit.Framework;

namespace AppPofo.Tests.EditMode
{
    public sealed class CloudStartPlanTests
    {
        [Test]
        public void AndroidKeystoreJavaSource_IsIncludedInAndroidOnly()
        {
            const string path =
                "Assets/AppPofo/AndroidPlugins/com/cloud/AppPofoDeviceKeyStore.java";
            var importer = UnityEditor.AssetImporter.GetAtPath(path) as UnityEditor.PluginImporter;
            Assert.That(importer, Is.Not.Null, "Java plugin must have PluginImporter.");
            Assert.That(importer.GetCompatibleWithPlatform(UnityEditor.BuildTarget.Android), Is.True);
            Assert.That(importer.GetCompatibleWithEditor(), Is.False);
        }

        [Test]
        public void Off_StartIsLocalAndDoesNotContactServer()
        {
            var plan = CloudModeSession.Plan(false);
            Assert.That(plan.Mode, Is.EqualTo(ProfileDataMode.Local));
            Assert.That(plan.RequestConnectivityProbe, Is.False);
            Assert.That(plan.UploadExistingProfiles, Is.False);
        }

        [Test]
        public void On_StartContactsServerWithoutUploadingLocalProfiles()
        {
            var plan = CloudModeSession.Plan(true);
            Assert.That(plan.Mode, Is.EqualTo(ProfileDataMode.Cloud));
            Assert.That(plan.RequestConnectivityProbe, Is.True);
            Assert.That(plan.UploadExistingProfiles, Is.False);
        }

        [Test]
        public void HealthUrlIsReadOnlyAndHasNoEmbeddedAdminCredential()
        {
            Assert.That(CloudModeSession.HealthUrl, Does.EndWith("/api/health"));
            Assert.That(CloudModeSession.HealthUrl, Does.Not.Contain("token"));
            Assert.That(CloudModeSession.HealthUrl, Does.Not.Contain("APPPOFO_API_TOKEN"));
        }
    }
}
