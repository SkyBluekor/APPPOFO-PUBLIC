#if UNITY_EDITOR_WIN
using System.Reflection;
using AppPofo.Presentation.Common;
using AppPofo.Presentation.Screens;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AppPofo.Tests.EditMode
{
    public sealed class CloudAdminLoginPanelTests
    {
        private const BindingFlags StaticPrivate = BindingFlags.Static | BindingFlags.NonPublic;
        private GameObject root;
        private CloudAdminLoginPanel panel;
        private ProfileDataMode previousMode;
        private CloudConnectionStatus previousStatus;
        private CloudAccessRole previousRole;
        private FieldInfo tokenField, loadedField, pendingField;
        private object previousToken, previousLoaded, previousPending;

        [SetUp]
        public void SetUp()
        {
            previousMode = CloudModeSession.Mode;
            previousStatus = CloudModeSession.Status;
            previousRole = CloudModeSession.AccessRole;
            // Never read, write, or delete the developer's actual DPAPI credential.
            var tokenType = typeof(CloudModeSession).Assembly.GetType(
                "AppPofo.Presentation.Common.DesktopCloudAdminToken");
            tokenField = tokenType.GetField("activeToken", StaticPrivate);
            loadedField = tokenType.GetField("loaded", StaticPrivate);
            pendingField = tokenType.GetField("pendingValidation", StaticPrivate);
            previousToken = tokenField.GetValue(null);
            previousLoaded = loadedField.GetValue(null);
            previousPending = pendingField.GetValue(null);
            tokenField.SetValue(null, null);
            loadedField.SetValue(null, true);
            pendingField.SetValue(null, false);
            SetSession(ProfileDataMode.Cloud, CloudConnectionStatus.ServerReachable, CloudAccessRole.User);

            root = new GameObject("AdminPanelTest", typeof(RectTransform));
            ((RectTransform)root.transform).sizeDelta = new Vector2(1080, 1920);
            var top = new GameObject("TopBar", typeof(RectTransform));
            top.transform.SetParent(root.transform, false);
            panel = root.AddComponent<CloudAdminLoginPanel>();
            panel.Initialize(root.transform, top.transform, null);
        }

        [TearDown]
        public void TearDown()
        {
            if (root != null) Object.DestroyImmediate(root);
            tokenField.SetValue(null, previousToken);
            loadedField.SetValue(null, previousLoaded);
            pendingField.SetValue(null, previousPending);
            SetSession(previousMode, previousStatus, previousRole);
        }

        [Test]
        public void ShortcutOpensAuthenticationWithoutGrantingPrivileges_AndCloseClearsCode()
        {
            Assert.That(Menu.activeSelf, Is.False);
            Assert.That(panel.IsDialogOpen, Is.False);
            Press(Key.LeftCtrl, Key.A);
            Assert.That(panel.IsDialogOpen, Is.False, "Ordinary Ctrl+A must still select text.");
            Press(Key.LeftShift, Key.A);
            Assert.That(panel.IsDialogOpen, Is.False, "Shift+A must not open administrator login.");
            Press(Key.LeftCtrl, Key.LeftShift);
            Assert.That(panel.IsDialogOpen, Is.False, "Both modifiers without A must not open login.");
            Press(Key.LeftCtrl, Key.LeftShift, Key.A);
            Assert.That(panel.IsDialogOpen, Is.True);
            Assert.That(CloudModeSession.AccessRole, Is.EqualTo(CloudAccessRole.User));
            Assert.That(Menu.activeSelf, Is.False);
            var input = root.GetComponentInChildren<InputField>(true);
            Assert.That(input.contentType, Is.EqualTo(InputField.ContentType.Password));
            input.text = "not-a-real-credential";
            Press(Key.Escape);
            Assert.That(panel.IsDialogOpen, Is.False);
            Assert.That(input.text, Is.Empty);
            SetSession(ProfileDataMode.Local, CloudConnectionStatus.Off, CloudAccessRole.None);
            Press(Key.LeftCtrl, Key.LeftShift, Key.A);
            Assert.That(panel.IsDialogOpen, Is.False);
        }

        [Test]
        public void MenuRequiresServerValidationEvenWithRememberedToken_AndHidesOnLossOfRole()
        {
            tokenField.SetValue(null, new string('x', 32));
            panel.Refresh();
            Assert.That(Menu.activeSelf, Is.False, "A credential alone must not reveal admin controls.");
            SetSession(ProfileDataMode.Cloud, CloudConnectionStatus.Connecting, CloudAccessRole.None);
            panel.Refresh();
            Assert.That(Menu.activeSelf, Is.False);
            SetSession(ProfileDataMode.Cloud, CloudConnectionStatus.ServerReachable, CloudAccessRole.Administrator);
            panel.Refresh();
            Assert.That(Menu.activeSelf, Is.True);
            Menu.GetComponent<Button>().onClick.Invoke();
            Assert.That(panel.IsDialogOpen, Is.True);
            Assert.That(root.transform.Find("CloudAdministrationDialog/AdminLoginSheet/AdministratorCredentials")
                .gameObject.activeSelf, Is.False);
            SetSession(ProfileDataMode.Cloud, CloudConnectionStatus.ServerReachable, CloudAccessRole.User);
            panel.Refresh();
            Assert.That(Menu.activeSelf, Is.False);
        }

        [TestCase(500f, 900f)]
        [TestCase(1080f, 1920f)]
        [TestCase(1920f, 600f)]
        public void RoundedCardFitsViewport_AndCardClickDoesNotDismiss(float width, float height)
        {
            ((RectTransform)root.transform).sizeDelta = new Vector2(width, height);
            // Call the same private modal action that the button invokes.
            // SendMessage triggers Unity's ShouldRunBehaviour assertion in EditMode.
            typeof(CloudAdminLoginPanel).GetMethod("Open",
                BindingFlags.Instance | BindingFlags.NonPublic).Invoke(panel, null);
            Assert.That(panel.IsDialogOpen, Is.True, "Modal did not open.");
            var sheet = (RectTransform)root.transform.Find("CloudAdministrationDialog/AdminLoginSheet");
            var corners = new Vector3[4];
            sheet.GetWorldCorners(corners);
            var bounds = ((RectTransform)root.transform).rect;
            foreach (Vector3 corner in corners)
            {
                var local = root.transform.InverseTransformPoint(corner);
                Assert.That(local.x, Is.InRange(bounds.xMin + 31, bounds.xMax - 31));
                Assert.That(local.y, Is.InRange(bounds.yMin + 31, bounds.yMax - 31));
            }
            var surface = sheet.GetComponent<Image>();
            Assert.That(surface.type, Is.EqualTo(Image.Type.Sliced));
            Assert.That(surface.sprite.texture.GetPixel(0, 0).a, Is.LessThan(.05f));
            Assert.That(surface.sprite.texture.GetPixel(2, 36).a, Is.GreaterThan(.95f));
            sheet.GetComponent<Button>().onClick.Invoke();
            Assert.That(panel.IsDialogOpen, Is.True);
            root.transform.Find("CloudAdministrationDialog").GetComponent<Button>().onClick.Invoke();
            Assert.That(panel.IsDialogOpen, Is.False);
        }

        private GameObject Menu => root.transform.Find("TopBar/CloudAdministrationMenu").gameObject;

        private void Press(params Key[] keys)
        {
            // EditMode has no reliable per-frame InputSystem transition clock.
            // Exercise the exact key interpretation method invoked by Update.
            bool controlHeld = System.Array.IndexOf(keys, Key.LeftCtrl) >= 0 ||
                System.Array.IndexOf(keys, Key.RightCtrl) >= 0;
            bool shiftHeld = System.Array.IndexOf(keys, Key.LeftShift) >= 0 ||
                System.Array.IndexOf(keys, Key.RightShift) >= 0;
            bool aJustPressed = System.Array.IndexOf(keys, Key.A) >= 0;
            bool escapeJustPressed = System.Array.IndexOf(keys, Key.Escape) >= 0;
            typeof(CloudAdminLoginPanel).GetMethod("HandleKeyboard",
                BindingFlags.Instance | BindingFlags.NonPublic).Invoke(panel,
                new object[] { controlHeld, shiftHeld, aJustPressed, escapeJustPressed, false });
        }

        private static void SetSession(ProfileDataMode mode, CloudConnectionStatus status, CloudAccessRole role)
        {
            var type = typeof(CloudModeSession);
            type.GetField("<Mode>k__BackingField", StaticPrivate).SetValue(null, mode);
            type.GetField("<Status>k__BackingField", StaticPrivate).SetValue(null, status);
            type.GetField("<AccessRole>k__BackingField", StaticPrivate).SetValue(null, role);
        }
    }
}
#endif
