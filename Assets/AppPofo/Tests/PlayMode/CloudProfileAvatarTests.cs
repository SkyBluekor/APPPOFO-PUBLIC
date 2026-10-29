using System.Collections;
using AppPofo.Presentation.Common;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AppPofo.Tests.PlayMode
{
    public sealed class CloudProfileAvatarTests
    {
        [UnityTest] public IEnumerator RealJpegMatchAboveLegacyLimitCreatesCardWithPhoto()
        {
            LogAssert.Expect(LogType.Log, "[AppPofo Bootstrap] Initialization successfull");
            UnityEngine.SceneManagement.SceneManager.LoadScene("Bootstrap", UnityEngine.SceneManagement.LoadSceneMode.Single);
            yield return null;
            var image = new Texture2D(192,192,TextureFormat.RGB24,false);
            var pixels = new Color32[192*192]; uint state = 1234567;
            for (int i=0;i<pixels.Length;i++) { state=1664525*state+1013904223; pixels[i]=new Color32((byte)state,(byte)(state>>8),(byte)(state>>16),255); }
            image.SetPixels32(pixels); image.Apply();
            byte[] jpeg = CloudProfileAvatar.Thumbnail(image.EncodeToPNG()); Object.Destroy(image);
            Assert.That(jpeg,Is.Not.Null);
            string json = JsonUtility.ToJson(new AppPofo.Presentation.FaceRecognition.CloudFaceMatchResponse {
                matched=true,decision_version="production-v1",profile=new AppPofo.Presentation.FaceRecognition.CloudFacePublicCard {
                    id="11111111-1111-4111-8111-111111111111",display_name="Synthetic photo card",headline="Role",
                    avatar_base64=System.Convert.ToBase64String(jpeg),business_card_theme="BLUSH"
                }
            });
            Assert.That(json.Length,Is.GreaterThan(4096));
            Assert.That(AppPofo.Presentation.FaceRecognition.CloudFaceMatchCodec.TryReadResponse(json,out var result,"production-v1"),Is.True);
            var panel=Object.FindAnyObjectByType<AppPofo.Presentation.Screens.BusinessCardPanelController>(FindObjectsInactive.Include);
            var root=new GameObject("Synthetic matched photo canvas",typeof(RectTransform),typeof(Canvas));
            var card=panel.CreateCloudRecognitionCard(result.profile.id,result.profile.display_name,result.profile.headline,root.transform,
                result.profile.avatar_base64,result.profile.business_card_theme);
            Assert.That(card,Is.Not.Null); Assert.That(card.gameObject.activeInHierarchy,Is.True);
            var avatar=(UnityEngine.UI.Image)typeof(AppPofo.Presentation.Components.BusinessCardView).GetField("avatarImage",
                System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).GetValue(card);
            Assert.That(avatar.sprite,Is.Not.Null);
            Object.Destroy(root); yield return null;
        }
        [UnityTest] public IEnumerator DisplayThumbnailIsBoundedJpegAndDoesNotKeepOriginalResolution()
        {
            var original = new Texture2D(640, 320, TextureFormat.RGB24, false);
            byte[] encoded = original.EncodeToPNG();
            Object.Destroy(original);
            byte[] jpeg = CloudProfileAvatar.Thumbnail(encoded);
            Assert.That(jpeg, Is.Not.Null); Assert.That(jpeg.Length, Is.LessThanOrEqualTo(24576));
            Assert.That(jpeg[0], Is.EqualTo(255)); Assert.That(jpeg[1], Is.EqualTo(216));
            var decoded = new Texture2D(2, 2); Assert.That(decoded.LoadImage(jpeg), Is.True);
            Assert.That(decoded.width, Is.EqualTo(192)); Assert.That(decoded.height, Is.EqualTo(96));
            Assert.That(CloudProfileAvatar.Decode("invalid base64"), Is.Null);
            // Used by the Node contract check to verify Unity's actual JPEG encoder output.
            System.IO.Directory.CreateDirectory(".utmp/r4-ui-completion");
            System.IO.File.WriteAllBytes(".utmp/r4-ui-completion/unity-thumbnail.jpg",jpeg);
            Object.Destroy(decoded); yield return null;
        }
    }
}
