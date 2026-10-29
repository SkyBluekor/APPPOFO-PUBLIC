using System.Reflection;
using AppPofo.Presentation.Common;
using NUnit.Framework;
using UnityEngine;

namespace AppPofo.Tests.EditMode
{
    public sealed class PublicDistributionDefaultsTests
    {
        [Test]
        public void BundledResource_DisablesRemoteFacesAndStartsOffline()
        {
            var asset = Resources.Load<TextAsset>("AppPofoCloudEnvironment");
            Assert.That(asset, Is.Not.Null);
            var config = JsonUtility.FromJson<CloudServerConfiguration.Settings>(asset.text);
            Assert.That(config.environment, Is.EqualTo("offline"));
            Assert.That(config.production_faces_enabled, Is.False);
            Assert.That(config.staging_faces_enabled, Is.False);
            Assert.That(config.staging_base_url, Is.Empty);
        }

        [Test]
        public void MissingResourceFallback_IsOfflineAndDoesNotEnableRemoteFaces()
        {
            var field = typeof(CloudServerConfiguration).GetField("settings", BindingFlags.NonPublic | BindingFlags.Static);
            var previous = field.GetValue(null);
            try
            {
                field.SetValue(null, new CloudServerConfiguration.Settings());
                Assert.That(CloudServerConfiguration.EnvironmentName, Is.EqualTo("offline"));
                Assert.That(CloudServerConfiguration.BaseUrl, Is.EqualTo("https://invalid.invalid"));
                Assert.That(CloudServerConfiguration.RemoteFacesEnabled, Is.False);
            }
            finally { field.SetValue(null, previous); }
        }

        [Test]
        public void ProductionPlaceholder_UsesReservedInvalidDomain()
        {
            Assert.That(new System.Uri(CloudServerConfiguration.ProductionUrl).Host, Does.EndWith(".invalid"));
        }
    }
}
