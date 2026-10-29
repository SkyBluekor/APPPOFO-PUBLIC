using UnityEngine;

namespace AppPofo.Presentation.Avatar
{
    public sealed class AvatarImageSourceResolver : MonoBehaviour
    {
        [SerializeField] private MonoBehaviour editorImageSource;
        [SerializeField] private MonoBehaviour androidImageSource;

        public IAvatarImageSource Resolve()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return ResolveSource(androidImageSource,"Android");
#else 
            return ResolveSource(editorImageSource,"Editor/PC");
#endif        
        }

        private IAvatarImageSource ResolveSource(
            MonoBehaviour source,
            string platformName
        )
        {
            if (source == null)
                        {
                            Debug.LogError(
                                $"[{nameof(AvatarImageSourceResolver)}] " +
                                $"{platformName} AvatarImageSource가 연결되지 않았습니다.");

                            return null;
                        }

                        if (source is not IAvatarImageSource imageSource)
                        {
                            Debug.LogError(
                                $"{source.GetType().Name}은 " +
                                $"{nameof(IAvatarImageSource)}를 구현하지 않았습니다.");

                            return null;
                        }

                        return imageSource;            
        }
    }
}