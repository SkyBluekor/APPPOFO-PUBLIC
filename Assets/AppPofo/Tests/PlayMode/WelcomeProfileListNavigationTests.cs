using System.Collections;
using System.Linq;
using AppPofo.Presentation.Screens;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace AppPofo.Tests.PlayMode
{
    public sealed class WelcomeProfileListNavigationTests
    {
        [UnityTest]
        public IEnumerator WelcomeStart_ProfileListEmptyOrFilledState_AndCreateRemainConnected()
        {
            LogAssert.Expect(LogType.Log, "[AppPofo Bootstrap] Initialization successfull");
            SceneManager.LoadScene("Bootstrap", LoadSceneMode.Single);
            yield return null;
            var welcome = Resources.FindObjectsOfTypeAll<RectTransform>()
                .Single(r => r.name == "WelcomeScreen" && r.gameObject.scene.IsValid());
            var list = Object.FindAnyObjectByType<ProfileListScreen>(FindObjectsInactive.Include);
            list.gameObject.SetActive(false);
            welcome.gameObject.SetActive(true);
            welcome.Find("SafeAreaRoot/CenteredContent/BottomActions/StartButton")
                .GetComponent<Button>().onClick.Invoke();
            yield return null;
            Assert.That(welcome.gameObject.activeSelf, Is.False);
            Assert.That(list.gameObject.activeSelf, Is.True);
            var body = list.transform.Find("SafeAreaRoot/CenteredContent/Body");
            var content = body.Find("Scroll View/Viewport/Content");
            // The content may also contain the search panel; count cards only.
            int visibleCards = content.GetComponentsInChildren<AppPofo.Presentation.Components.ProfileListItemView>(false).Length;
            var searchPanel = content.Find("ProfileSearchPanel");
            Assert.That(searchPanel, Is.Not.Null, "Name/skill search should be available in the list.");
            Assert.That(searchPanel.GetComponentsInChildren<InputField>(true).Length, Is.EqualTo(2));
            Assert.That(searchPanel.GetComponent<ProfileSearchPanelView>(), Is.Not.Null);
            Assert.That(searchPanel.GetComponent<LayoutElement>().preferredHeight,
                Is.EqualTo(260).Within(.01f), "Search card must contain three 62-unit rows plus margins.");
            var searchButtons = searchPanel.GetComponentsInChildren<Button>(true);
            Assert.That(searchButtons.Length, Is.EqualTo(3), "Reset, previous and next buttons.");
            if (visibleCards == 0)
            {
                // An empty list intentionally hides its search card. Measuring an
                // inactive ScrollView produces zero width in a clean test database.
                Assert.That(searchPanel.gameObject.activeSelf, Is.False);
                Assert.That(body.Find("EmptyContent").gameObject.activeSelf, Is.True);
                body.Find("EmptyContent/Content/PrimaryAddButton").GetComponent<Button>().onClick.Invoke();
                yield return null;
                Assert.That(list.gameObject.activeSelf, Is.False);
                var emptyCreator = Object.FindAnyObjectByType<CreateProfileScreen>(FindObjectsInactive.Include);
                Assert.That(emptyCreator.gameObject.activeSelf, Is.True);
                yield break;
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)content);
            Canvas.ForceUpdateCanvases();
            searchPanel.GetComponent<ProfileSearchPanelView>().RefreshGeometry();
            Canvas.ForceUpdateCanvases();
            var searchGraphic = searchPanel.GetComponent<Image>();
            Assert.That(searchGraphic.sprite, Is.Not.Null);
            Color centerFill = searchGraphic.sprite.texture.GetPixel(36, 36);
            Assert.That(centerFill.a, Is.GreaterThan(.95f), "Search card must be filled, not a transparent outline.");
            Assert.That(centerFill.b - centerFill.r, Is.GreaterThan(.02f),
                "Card fill should have a visibly lavender-tinted interior.");
            Assert.That(searchGraphic.type, Is.EqualTo(Image.Type.Sliced));
            Assert.That(searchGraphic.sprite.texture.width, Is.EqualTo(72),
                "Only a small, genuine nine-slice UI primitive should be stretched.");
            Assert.That(searchGraphic.sprite.border.x, Is.GreaterThanOrEqualTo(30),
                "Outer card corners must be visibly rounded.");
            Assert.That(searchGraphic.sprite.texture.GetPixel(0, 0).a,
                Is.LessThan(.05f), "The card corners must be transparent and rounded.");
            var cardRect = (RectTransform)searchPanel;
            Assert.That(searchPanel.GetComponent<VerticalLayoutGroup>()?.enabled ?? false,
                Is.False, "Nested layout groups must not squeeze search controls.");
            Assert.That(searchPanel.GetComponent<RectMask2D>(), Is.Not.Null,
                "The full card must clip any overflowing artwork.");
            Assert.That(cardRect.rect.width, Is.GreaterThan(360f),
                "Card must receive an actual width from its parent layout.");
            AssertContained(cardRect, (RectTransform)searchPanel.Find("NameSearchRow"));
            AssertContained(cardRect, (RectTransform)searchPanel.Find("SkillSearchRow"));
            AssertContained(cardRect, (RectTransform)searchPanel.Find("PageNavigation"));
            foreach (var rowName in new[] { "NameSearchRow", "SkillSearchRow" })
            {
                var fieldGraphic = searchPanel.Find(rowName + "/SearchInput").GetComponent<Image>();
                var rowRect = (RectTransform)searchPanel.Find(rowName);
                AssertContained(rowRect, fieldGraphic.rectTransform);
                Assert.That(fieldGraphic.rectTransform.rect.width,
                    Is.GreaterThan(120f), rowName + " input must have usable width.");
                var labelRect = (RectTransform)searchPanel.Find(rowName + "/Label");
                Assert.That(labelRect.rect.width, Is.GreaterThanOrEqualTo(70f),
                    rowName + " bold label must have its own space.");
                var labelCorners = new Vector3[4];
                var fieldCorners = new Vector3[4];
                labelRect.GetWorldCorners(labelCorners);
                fieldGraphic.rectTransform.GetWorldCorners(fieldCorners);
                Assert.That(rowRect.InverseTransformPoint(fieldCorners[0]).x,
                    Is.GreaterThan(rowRect.InverseTransformPoint(labelCorners[3]).x + 8f),
                    rowName + " input must start AFTER the label, not cover it.");
                Assert.That(rowRect.InverseTransformPoint(fieldCorners[2]).x,
                    Is.LessThanOrEqualTo(rowRect.rect.xMax + .5f),
                    rowName + " input must stop before card edge.");
                float leftCap = fieldGraphic.sprite.border.x * 100f /
                    fieldGraphic.sprite.pixelsPerUnit;
                Assert.That(leftCap, Is.LessThan(fieldGraphic.rectTransform.rect.height * .5f),
                    rowName + " input edge must fit inside its height.");
                AssertContained(cardRect, fieldGraphic.rectTransform);
                AssertContained(cardRect, fieldGraphic.transform.Find("Text") as RectTransform);
                AssertContained(cardRect, fieldGraphic.transform.Find("Placeholder") as RectTransform);
                Assert.That(fieldGraphic.type, Is.EqualTo(Image.Type.Sliced), rowName);
                Assert.That(fieldGraphic.sprite.name, Does.Contain("input_profile_add_field"), rowName);
                Assert.That(fieldGraphic.sprite.border.x, Is.GreaterThan(0), rowName);
                var label = searchPanel.Find(rowName + "/Label").GetComponent<Text>();
                Assert.That(label.font.name, Does.Contain("Pretendard-Bold"), rowName);
                Assert.That(label.alignment, Is.EqualTo(TextAnchor.MiddleCenter), rowName);
                AssertContained(rowRect, label.rectTransform);
                Assert.That(fieldGraphic.GetComponent<LayoutElement>().preferredHeight,
                    Is.GreaterThanOrEqualTo(60), rowName);
            }
            foreach (var buttonName in new[] { "ResetButton", "PreviousButton", "NextButton" })
            {
                var button = searchPanel.Find("PageNavigation/" + buttonName);
                Assert.That(button, Is.Not.Null, buttonName);
                Assert.That(button.GetComponent<LayoutElement>().preferredHeight,
                    Is.GreaterThanOrEqualTo(58), buttonName);
                var caption = button.Find("Caption").GetComponent<Text>();
                Assert.That(caption.alignment, Is.EqualTo(TextAnchor.MiddleCenter), buttonName);
                var bounds = (RectTransform)button;
                AssertContained(cardRect, bounds);
                AssertContained(bounds, caption.rectTransform);
                Assert.That(bounds.rect.height, Is.GreaterThanOrEqualTo(58), buttonName);
            }
            // Regression: test the same search panel at narrow and wide widths.
            // Parent layout controls the width in production; temporarily disable it
            // while simulating alternate widths, then restore it before navigating.
            var contentGroup = content.GetComponent<VerticalLayoutGroup>();
            float originalWidth = cardRect.rect.width;
            bool originallyEnabled = contentGroup.enabled;
            try
            {
                contentGroup.enabled = false;
                foreach (float testWidth in new[] { 500f, 720f, 900f })
                {
                    cardRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, testWidth);
                    searchPanel.GetComponent<ProfileSearchPanelView>().RefreshGeometry();
                    Canvas.ForceUpdateCanvases();
                    foreach (string rowName in new[] { "NameSearchRow", "SkillSearchRow" })
                    {
                        RectTransform row = (RectTransform)searchPanel.Find(rowName);
                        RectTransform label = (RectTransform)row.Find("Label");
                        RectTransform input = (RectTransform)row.Find("SearchInput");
                        AssertContained(cardRect, row);
                        AssertContained(cardRect, label);
                        AssertContained(cardRect, input);
                        Vector3[] labelCorners = new Vector3[4];
                        Vector3[] inputCorners = new Vector3[4];
                        label.GetWorldCorners(labelCorners);
                        input.GetWorldCorners(inputCorners);
                        Assert.That(row.InverseTransformPoint(inputCorners[0]).x,
                            Is.GreaterThan(row.InverseTransformPoint(labelCorners[3]).x + 8f),
                            "Labels must remain visible at card width " + testWidth);
                    }
                }
            }
            finally
            {
                cardRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, originalWidth);
                contentGroup.enabled = originallyEnabled;
                LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)content);
                searchPanel.GetComponent<ProfileSearchPanelView>().RefreshGeometry();
            }
            Assert.That(body.Find("EmptyContent").gameObject.activeSelf, Is.EqualTo(visibleCards == 0));
            list.transform.Find("SafeAreaRoot/CenteredContent/TopBar/CreateProfileButton")
                .GetComponent<Button>().onClick.Invoke();
            yield return null;
            Assert.That(list.gameObject.activeSelf, Is.False);
            var create = Resources.FindObjectsOfTypeAll<RectTransform>()
                .Single(r => r.name == "CreateProfileScreen" && r.gameObject.scene.IsValid());
            Assert.That(create.gameObject.activeSelf, Is.True);
        }

        private static void AssertContained(RectTransform outer, RectTransform inner)
        {
            Assert.That(inner, Is.Not.Null);
            Canvas.ForceUpdateCanvases();
            Vector3[] corners = new Vector3[4];
            inner.GetWorldCorners(corners);
            foreach (Vector3 world in corners)
            {
                Vector3 local = outer.InverseTransformPoint(world);
                Assert.That(local.x, Is.GreaterThanOrEqualTo(outer.rect.xMin - .5f), inner.name);
                Assert.That(local.x, Is.LessThanOrEqualTo(outer.rect.xMax + .5f), inner.name);
                Assert.That(local.y, Is.GreaterThanOrEqualTo(outer.rect.yMin - .5f), inner.name);
                Assert.That(local.y, Is.LessThanOrEqualTo(outer.rect.yMax + .5f), inner.name);
            }
        }
    }
}
