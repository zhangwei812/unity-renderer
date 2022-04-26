using System;
using UnityEngine;

public class UserProfileController : MonoBehaviour
{
    public static UserProfileController i { get; private set; }

    public event Action OnBaseWereablesFail;

    private static UserProfileDictionary userProfilesCatalogValue;
    private bool baseWearablesAlreadyRequested = false;

    public static UserProfileDictionary userProfilesCatalog
    {
        get
        {
            if (userProfilesCatalogValue == null)
            {
                userProfilesCatalogValue = Resources.Load<UserProfileDictionary>("UserProfilesCatalog");
            }

            return userProfilesCatalogValue;
        }
    }

    [NonSerialized] public UserProfile ownUserProfile;

    public void Awake()
    {
        Log("User profile controller: is ready");
        i = this;
        ownUserProfile = UserProfile.GetOwnUserProfile();
    }

    public void LoadProfile(string payload)
    {
        Log("Forcing a caught exception");
        try
        {
            throw new Exception("Do something");
        }
        catch (Exception  e)
        {
            Log($"Caught exception: {e.ToString()}");
        }
        Log("Done forcing a caught exception");


        Log("Loading profile");
        Log(payload);
        if (!baseWearablesAlreadyRequested)
        {
            baseWearablesAlreadyRequested = true;
            CatalogController.RequestBaseWearables()
                             .Catch((error) =>
                             {
                                 OnBaseWereablesFail?.Invoke();
                                 Debug.LogError(error);
                             });
        }

        if (payload == null)
            return;

        var model = JsonUtility.FromJson<UserProfileModel>(payload);
        ownUserProfile.UpdateData(model);
        userProfilesCatalog.Add(model.userId, ownUserProfile);
        Log("Updated own user profile");
    }

    public void Log(string msg)
    {
        bool current = Debug.unityLogger.logEnabled; 
        Debug.unityLogger.logEnabled = true;
        Debug.Log(msg);
        Debug.unityLogger.logEnabled = current;
    }

    public void AddUserProfileToCatalog(string payload) { AddUserProfileToCatalog(JsonUtility.FromJson<UserProfileModel>(payload)); }

    public void AddUserProfilesToCatalog(string payload)
    {
        UserProfileModel[] items = JsonUtility.FromJson<UserProfileModel[]>(payload);
        int count = items.Length;
        for (int i = 0; i < count; ++i)
        {
            AddUserProfileToCatalog(items[i]);
        }
    }

    public void AddUserProfileToCatalog(UserProfileModel model)
    {
        var userProfile = ScriptableObject.CreateInstance<UserProfile>();
        userProfile.UpdateData(model);
        userProfilesCatalog.Add(model.userId, userProfile);
    }

    public static UserProfile GetProfileByName(string targetUserName)
    {
        foreach (var userProfile in userProfilesCatalogValue)
        {
            if (userProfile.Value.userName.ToLower() == targetUserName.ToLower())
                return userProfile.Value;
        }

        return null;
    }

    public static UserProfile GetProfileByUserId(string targetUserId) { return userProfilesCatalogValue.Get(targetUserId); }

    public void RemoveUserProfilesFromCatalog(string payload)
    {
        string[] usernames = JsonUtility.FromJson<string[]>(payload);
        for (int index = 0; index < usernames.Length; index++)
        {
            RemoveUserProfileFromCatalog(userProfilesCatalog.Get(usernames[index]));
        }
    }

    public void RemoveUserProfileFromCatalog(UserProfile userProfile)
    {
        if (userProfile == null)
            return;

        userProfilesCatalog.Remove(userProfile.userId);
        Destroy(userProfile);
    }

    public void ClearProfilesCatalog() { userProfilesCatalog?.Clear(); }
}