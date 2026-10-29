using System;
using AppPofo.Domain.Entities;
using AppPofo.Presentation.Common;
using UnityEngine;
using UnityEngine.UI;

namespace AppPofo.Presentation.Screens
{
    public class EditProfileScreen : MonoBehaviour
    {
        [SerializeField] private AppPofoBootstrap bootstrap;
        [SerializeField] private ProfileDetailScreen profileDetailScreen;
        [SerializeField] private InputField displayNameInput;
        [SerializeField] private InputField headlineInput;
        [SerializeField] private InputField bioInput;
        [SerializeField] private InputField emaliInput;
        [SerializeField] private InputField websiteInput;

        private long currentProfileId;

        public void Show(long profileId)
        {
            Profile profile = bootstrap.GetProfileUseCase.Execute(profileId);

            if(profile == null)
            {
                Debug.LogError($"수정할 프로필을 찾을 수 없습니다. profile: {profileId}");
                return;
            }

            currentProfileId = profile.Id;

            displayNameInput.text = profile.DisplayName;
            headlineInput.text = profile.Headline;
            bioInput.text = profile.Bio;
            emaliInput.text = profile.Email;
            websiteInput.text = profile.Website;

            gameObject.SetActive(true);
            profileDetailScreen.gameObject.SetActive(false);
        }

        public void Back()
        {
            gameObject.SetActive(false);
            profileDetailScreen.gameObject.SetActive(true);
        }

        public void Save()
        {
            if (currentProfileId <= 0)
            {
                Debug.LogError("수정할 PorofileId가 설정되지 않았습니다.");
                return;
            }

            try
            {
                Profile updatedProfile = bootstrap.UpdateProfileUseCase.Execute(
                    currentProfileId,
                    displayNameInput.text,
                    headlineInput.text,
                    bioInput.text,
                    emaliInput.text,
                    websiteInput.text,
                    DateTimeOffset.UtcNow
                );

                gameObject.SetActive(false);

                profileDetailScreen.Show(updatedProfile.Id);
            }
            catch(Exception ex)
            {
                Debug.LogError(
                    $"프로필 수정에 실패했습니다. \n{ex.Message}"
                );
                
            }
        }
    }
}