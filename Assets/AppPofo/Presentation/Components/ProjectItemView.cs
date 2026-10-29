using AppPofo.Domain.Entities;
using UnityEngine;
using UnityEngine.UI;
using System;

namespace AppPofo.Presentation.Components
{
    public sealed class ProjectItemView : MonoBehaviour
    {
        [SerializeField] private Text titleText;
        [SerializeField] private Button button;

        private ProfileItem item;
        private Action<ProfileItem> onClick;

        public void Bind(ProfileItem profileItem, Action<ProfileItem> clickAction)
        {
            item = profileItem;
            onClick = clickAction;

            titleText.text = profileItem.Title;

            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(HandleClick);
        }

        public void BindDisplay(string title, Action clickAction)
        {
            item = null; onClick = null;
            titleText.supportRichText = false;
            titleText.text = title ?? "";
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => clickAction?.Invoke());
        }

        private void HandleClick()
        {
            onClick?.Invoke(item);
        }
    }
}