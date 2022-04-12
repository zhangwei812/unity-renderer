﻿using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PublicChannelEntry : BaseComponentView
{
    [SerializeField] private Button openChatButton;
    [SerializeField] private TMP_Text nameLabel;
    [SerializeField] private PublicChannelEntryModel model;

    public PublicChannelEntryModel Model => model;

    public event Action OnOpenChat;

    public override void Awake()
    {
        base.Awake();
        openChatButton.onClick.AddListener(() => OnOpenChat?.Invoke());
    }

    public void Set(PublicChannelEntryModel model)
    {
        this.model = model;
        RefreshControl();
    }

    public override void RefreshControl()
    {
        nameLabel.text = $"#{model.name}";
    }

    [Serializable]
    public struct PublicChannelEntryModel
    {
        public string channelId;
        public string name;

        public PublicChannelEntryModel(string channelId, string name)
        {
            this.channelId = channelId;
            this.name = name;
        }
    }
}