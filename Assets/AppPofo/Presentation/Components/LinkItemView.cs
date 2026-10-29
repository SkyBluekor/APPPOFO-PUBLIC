using System;
using AppPofo.Domain.Entities;
using JetBrains.Annotations;
using UnityEngine;
using UnityEngine.UI;

namespace AppPofo.Presentation.Components
{
    public sealed class LinkItemView : MonoBehaviour
    {
        [SerializeField] private Text titleText;
        [SerializeField] private Button openButton;
        [SerializeField] private Button deleteButton;

        private ProfileItem item;
        private Action<ProfileItem> onOpen;
        private Action<ProfileItem> onDelete;

        public void Bind(
            ProfileItem profileItem,
            Action<ProfileItem> openAction,
            Action<ProfileItem> deleteAction
        )
        {
            if(profileItem == null)
            {
                Debug.LogError("[LinkItemView] ProfileItem이 null입니다.");
                return;
            }

            item = profileItem;
            onOpen = openAction;
            onDelete = deleteAction;

            titleText.text = profileItem.Title;

            openButton.onClick.RemoveAllListeners();
            deleteButton.onClick.RemoveAllListeners();

            openButton.onClick.AddListener(HandleOpen);
            deleteButton.onClick.AddListener(HandleDelete);
        }

        public void BindDisplay(string title, Action openAction, Action deleteAction)
        {
            item = null; onOpen = null; onDelete = null;
            titleText.supportRichText = false;
            titleText.text = title ?? "";
            openButton.onClick.RemoveAllListeners();
            deleteButton.onClick.RemoveAllListeners();
            openButton.onClick.AddListener(() => openAction?.Invoke());
            deleteButton.onClick.AddListener(() => deleteAction?.Invoke());
            openButton.interactable = openAction != null;
            deleteButton.interactable = deleteAction != null;
        }

        private void HandleOpen()
        {
            onOpen?.Invoke(item);
        }

        private void HandleDelete()
        {
            onDelete?.Invoke(item);
        }
    }
}