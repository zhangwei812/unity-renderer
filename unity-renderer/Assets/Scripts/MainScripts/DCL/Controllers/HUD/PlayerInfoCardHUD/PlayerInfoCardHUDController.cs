using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DCL;
using DCL.Helpers;
using DCL.Interface;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Assertions;
using Object = UnityEngine.Object;

public class PlayerInfoCardHUDController : IHUD
{
    internal readonly PlayerInfoCardHUDView view;
    internal readonly StringVariable currentPlayerId;
    internal UserProfile currentUserProfile;

    private UserProfile viewingUserProfile;
    private UserProfile ownUserProfile => userProfileBridge.GetOwn();

    private readonly IFriendsController friendsController;
    private readonly InputAction_Trigger toggleFriendsTrigger;
    private readonly InputAction_Trigger closeWindowTrigger;
    private readonly InputAction_Trigger toggleWorldChatTrigger;
    private readonly IUserProfileBridge userProfileBridge;
    private readonly IWearableCatalogBridge wearableCatalogBridge;
    private readonly RegexProfanityFilter profanityFilter;
    private readonly DataStore dataStore;
    private readonly ICatalyst catalyst;
    private readonly List<string> loadedWearables = new List<string>();

    public PlayerInfoCardHUDController(IFriendsController friendsController,
        StringVariable currentPlayerIdData,
        IUserProfileBridge userProfileBridge,
        IWearableCatalogBridge wearableCatalogBridge,
        RegexProfanityFilter profanityFilter,
        DataStore dataStore,
        ICatalyst catalyst)
    {
        this.friendsController = friendsController;
        view = PlayerInfoCardHUDView.CreateView();
        view.Initialize(() => OnCloseButtonPressed(),
            ReportPlayer, BlockPlayer, UnblockPlayer,
            AddPlayerAsFriend, CancelInvitation, AcceptFriendRequest, RejectFriendRequest);
        currentPlayerId = currentPlayerIdData;
        this.userProfileBridge = userProfileBridge;
        this.wearableCatalogBridge = wearableCatalogBridge;
        this.profanityFilter = profanityFilter;
        this.dataStore = dataStore;
        this.catalyst = catalyst;
        currentPlayerId.OnChange += OnCurrentPlayerIdChanged;
        OnCurrentPlayerIdChanged(currentPlayerId, null);

        toggleFriendsTrigger = Resources.Load<InputAction_Trigger>("ToggleFriends");
        toggleFriendsTrigger.OnTriggered -= OnCloseButtonPressed;
        toggleFriendsTrigger.OnTriggered += OnCloseButtonPressed;

        closeWindowTrigger = Resources.Load<InputAction_Trigger>("CloseWindow");
        closeWindowTrigger.OnTriggered -= OnCloseButtonPressed;
        closeWindowTrigger.OnTriggered += OnCloseButtonPressed;

        toggleWorldChatTrigger = Resources.Load<InputAction_Trigger>("ToggleWorldChat");
        toggleWorldChatTrigger.OnTriggered -= OnCloseButtonPressed;
        toggleWorldChatTrigger.OnTriggered += OnCloseButtonPressed;

        friendsController.OnUpdateFriendship -= OnFriendStatusUpdated;
        friendsController.OnUpdateFriendship += OnFriendStatusUpdated;
    }

    public void CloseCard()
    {
        currentPlayerId.Set(null);
    }

    private void OnCloseButtonPressed(DCLAction_Trigger action = DCLAction_Trigger.CloseWindow)
    {
        CloseCard();
    }

    private void AddPlayerAsFriend()
    {
        // Add fake action to avoid waiting for kernel
        userProfileBridge.AddUserProfileToCatalog(new UserProfileModel
        {
            userId = currentPlayerId,
            name = currentPlayerId
        });
        friendsController.RequestFriendship(currentPlayerId);

        WebInterface.UpdateFriendshipStatus(new FriendsController.FriendshipUpdateStatusMessage
        {
            userId = currentPlayerId, action = FriendshipAction.REQUESTED_TO
        });
    }

    private void CancelInvitation()
    {
        // Add fake action to avoid waiting for kernel
        friendsController.CancelRequest(currentPlayerId);

        WebInterface.UpdateFriendshipStatus(new FriendsController.FriendshipUpdateStatusMessage()
        {
            userId = currentPlayerId, action = FriendshipAction.CANCELLED
        });
    }

    private void AcceptFriendRequest()
    {
        // Add fake action to avoid waiting for kernel
        friendsController.AcceptFriendship(currentPlayerId);

        WebInterface.UpdateFriendshipStatus(new FriendsController.FriendshipUpdateStatusMessage()
        {
            userId = currentPlayerId, action = FriendshipAction.APPROVED
        });
    }

    private void RejectFriendRequest()
    {
        // Add fake action to avoid waiting for kernel
        friendsController.RejectFriendship(currentPlayerId);

        WebInterface.UpdateFriendshipStatus(new FriendsController.FriendshipUpdateStatusMessage()
        {
            userId = currentPlayerId, action = FriendshipAction.REJECTED
        });
    }

    private void OnCurrentPlayerIdChanged(string current, string previous)
    {
        if (currentUserProfile != null)
            currentUserProfile.OnUpdate -= SetUserProfile;

        currentUserProfile = string.IsNullOrEmpty(current)
            ? null
            : userProfileBridge.Get(current);

        if (currentUserProfile == null)
        {
            view.SetCardActive(false);
            wearableCatalogBridge.RemoveWearablesInUse(loadedWearables);
            loadedWearables.Clear();
        }
        else
        {
            currentUserProfile.OnUpdate += SetUserProfile;
            SetUserProfile(currentUserProfile);
            view.SetCardActive(true);
        }
    }

    private void SetUserProfile(UserProfile userProfile)
    {
        Assert.IsTrue(userProfile != null, "userProfile can't be null");

        view.SetName(FilterName(userProfile));
        view.SetDescription(FilterDescription(userProfile));
        view.ClearCollectibles();
        view.SetIsBlocked(IsBlocked(userProfile.userId));
        LoadAndShowWearables(userProfile);
        UpdateFriendshipInteraction();
        view.HideActivationDate();

        GetProfileFromCatalyst(userProfile)
            .Then(catalystProfile =>
            {
                if (catalystProfile == null) return;
                view.ShowActivationDate(catalystProfile.GetActivationDate());
            });

        if (viewingUserProfile != null)
            viewingUserProfile.snapshotObserver.RemoveListener(view.SetFaceSnapshot);
        userProfile.snapshotObserver.AddListener(view.SetFaceSnapshot);
        viewingUserProfile = userProfile;
    }

    private Promise<CatalystProfileSchema> GetProfileFromCatalyst(UserProfile userProfile)
    {
        var promise = new Promise<CatalystProfileSchema>();
        // TODO: get this from a provider (ie: UserProfileBridge) instead of parsing everything here?
        catalyst.GetEntities(CatalystEntitiesType.PROFILE, new[] {userProfile.ethAddress})
            .Then(profileRawData =>
            {
                var catalystProfileSchema = JsonConvert.DeserializeObject<CatalystProfileListSchema>(profileRawData);
                promise.Resolve(catalystProfileSchema?.FirstOrDefault());
            }).Catch(error => promise.Reject(error));
        return promise;
    }

    public void SetVisibility(bool visible)
    {
        view.SetVisibility(visible);
        
        if (viewingUserProfile != null)
            viewingUserProfile.snapshotObserver.RemoveListener(view.SetFaceSnapshot);

        if (visible)
        {
            if (viewingUserProfile != null)
                viewingUserProfile.snapshotObserver.AddListener(view.SetFaceSnapshot);
        }
    }

    private void BlockPlayer()
    {
        if (ownUserProfile.IsBlocked(currentUserProfile.userId)) return;
        ownUserProfile.Block(currentUserProfile.userId);
        view.SetIsBlocked(true);
        WebInterface.SendBlockPlayer(currentUserProfile.userId);
    }

    private void UnblockPlayer()
    {
        if (!ownUserProfile.IsBlocked(currentUserProfile.userId)) return;
        ownUserProfile.Unblock(currentUserProfile.userId);
        view.SetIsBlocked(false);
        WebInterface.SendUnblockPlayer(currentUserProfile.userId);
    }

    private void ReportPlayer()
    {
        WebInterface.SendReportPlayer(currentPlayerId);
    }

    public void Dispose()
    {
        if (currentUserProfile != null)
            currentUserProfile.OnUpdate -= SetUserProfile;

        if (currentPlayerId != null)
            currentPlayerId.OnChange -= OnCurrentPlayerIdChanged;

        if (closeWindowTrigger != null)
            closeWindowTrigger.OnTriggered -= OnCloseButtonPressed;

        if (closeWindowTrigger != null)
            closeWindowTrigger.OnTriggered -= OnCloseButtonPressed;

        if (toggleWorldChatTrigger != null)
            toggleWorldChatTrigger.OnTriggered -= OnCloseButtonPressed;

        if (toggleFriendsTrigger != null)
            toggleFriendsTrigger.OnTriggered -= OnCloseButtonPressed;
        
        if (viewingUserProfile != null)
            viewingUserProfile.snapshotObserver.RemoveListener(view.SetFaceSnapshot);

        if (view != null)
            Object.Destroy(view.gameObject);
    }

    private void OnFriendStatusUpdated(string userId, FriendshipAction action)
    {
        if (currentUserProfile == null)
            return;

        UpdateFriendshipInteraction();
    }

    private void UpdateFriendshipInteraction()
    {
        if (currentUserProfile == null)
        {
            view.HideFriendshipInteraction();
            return;
        }

        view.UpdateFriendshipInteraction(CanBeFriends(),
            friendsController.GetUserStatus(currentUserProfile.userId));
    }

    private bool CanBeFriends()
    {
        return friendsController != null && friendsController.isInitialized && currentUserProfile.hasConnectedWeb3;
    }

    private void LoadAndShowWearables(UserProfile userProfile)
    {
        wearableCatalogBridge.RequestOwnedWearables(userProfile.userId)
            .Then(wearables =>
            {
                var wearableIds = wearables.Select(x => x.id).ToArray();
                userProfile.SetInventory(wearableIds);
                loadedWearables.AddRange(wearableIds);
                var containedWearables = wearables
                    // this makes any sense?
                    .Where(wearable => wearableCatalogBridge.IsValidWearable(wearable.id));
                view.SetWearables(containedWearables);
            })
            .Catch(Debug.LogError);
    }

    private bool IsBlocked(string userId)
    {
        return ownUserProfile != null && ownUserProfile.IsBlocked(userId);
    }

    private string FilterName(UserProfile userProfile)
    {
        return IsProfanityFilteringEnabled()
            ? profanityFilter.Filter(userProfile.userName)
            : userProfile.userName;
    }

    private string FilterDescription(UserProfile userProfile)
    {
        return IsProfanityFilteringEnabled()
            ? profanityFilter.Filter(userProfile.description)
            : userProfile.description;
    }

    private bool IsProfanityFilteringEnabled()
    {
        return dataStore.settings.profanityChatFilteringEnabled.Get();
    }
    
    class CatalystProfileListSchema : ICollection<CatalystProfileSchema>
    {
        private readonly List<CatalystProfileSchema> profiles = new List<CatalystProfileSchema>();
        
        public IEnumerator<CatalystProfileSchema> GetEnumerator() => profiles.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public void Add(CatalystProfileSchema item) => profiles.Add(item);

        public void Clear() => profiles.Clear();

        public bool Contains(CatalystProfileSchema item) => profiles.Contains(item);

        public void CopyTo(CatalystProfileSchema[] array, int arrayIndex) => profiles.CopyTo(array, arrayIndex);

        public bool Remove(CatalystProfileSchema item) => profiles.Remove(item);

        public int Count => profiles.Count;
        public bool IsReadOnly => false;
    }

    class CatalystProfileSchema
    { 
        public ulong timestamp;

        public DateTime GetActivationDate()
        {
            var dateOffset = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
            return dateOffset
                .AddMilliseconds(timestamp)
                .ToLocalTime();
        }
    }
}