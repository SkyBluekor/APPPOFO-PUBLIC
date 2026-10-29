using System.Collections;
using System.Linq;
using AppPofo.Presentation.Screens;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace AppPofo.Tests.PlayMode
{
    public sealed class CloudFaceConsentUiTests
    {
        [UnityTest]
        public IEnumerator ScanConsent_DefaultsOff_RequiresBothChoices_AndCancelDoesNotOpenCamera()
        {
            var root=new GameObject("synthetic consent canvas",typeof(RectTransform),typeof(Canvas));
            var safe=new GameObject("SafeAreaRoot",typeof(RectTransform));
            safe.transform.SetParent(root.transform,false);
            var safeRect=(RectTransform)safe.transform;
            safeRect.anchorMin=Vector2.zero;safeRect.anchorMax=Vector2.one;
            safeRect.offsetMin=safeRect.offsetMax=Vector2.zero;
            var panel=root.AddComponent<CloudFaceConsentPanel>();
            bool opened=false;
            try
            {
                panel.ShowScan(()=>opened=true);
                Assert.That(safe.transform.Find("CloudScanConsent"),Is.Not.Null);
                var toggles=root.GetComponentsInChildren<Toggle>();
                Assert.That(toggles.Length,Is.EqualTo(2));
                Assert.That(toggles.All(toggle=>!toggle.isOn),Is.True);
                var approve=root.GetComponentsInChildren<Button>().Single(button=>button.name=="ApproveScan");
                approve.onClick.Invoke();
                Assert.That(opened,Is.False);
                Assert.That(root.GetComponentsInChildren<Text>().Any(text=>text.text.Contains("각각 동의")),Is.True);
                toggles[0].isOn=true;
                approve.onClick.Invoke();
                Assert.That(opened,Is.False);
                root.GetComponentsInChildren<Button>().Single(button=>button.name=="CancelScan").onClick.Invoke();
                yield return null;
                Assert.That(safe.transform.Find("CloudScanConsent"),Is.Null);
                panel.AddScanStopControl();
                Assert.That(safe.transform.Find("StopCloudFaceScan"),Is.Not.Null);
                Assert.That(opened,Is.False);
            }
            finally { UnityEngine.Object.Destroy(root); }
        }
    }
}
