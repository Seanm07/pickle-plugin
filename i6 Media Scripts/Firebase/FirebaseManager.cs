using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Firebase;

public class FirebaseManager : MonoBehaviour
{
    public static FirebaseManager instance;
    
    // Enables some extra logging + other checks which notify the developer of issues
    // Disable this in release builds as it affects performance
    public bool debugMode;

    [Header("Android")]
    // Used to launch URLs with the appropriate package name
    // e.g clicking a link on android sends them to the android package name on the google play store
    // But clicking the same link on an iOS device sends them to the ios package name on the ios app store
    public string androidPackageName;
    
    [Header("iOS")]
    public string iosPackageName;
    public string iosAppStoreId;

    [Header("Firebase Settings")]
    // Must be the same on android and iOS to allow the app to be launched from shared links
    public string uniqueGameIdentifier = "unique_game_id";
    
    // This is the URL used in the sharing links, for example set to "gamepickle" the sharing URLs would look like: https://gamepickle.page.link/whatever
    public string dynamicLinkBase = "gamepickle.page.link";
    
    // The database URL is required to access the realtime database in the editor
    public string databaseURL;
    
    // Unless the database access has been set to public (not recommended!) then some security details are needed to access the database from the editor
    // Ask i6 media to generate this for you via https://console.cloud.google.com/iam-admin/serviceaccounts/project?project=YOUR-FIREBASE-APP
    public string databaseP12FileName;
    public string databaseServiceAccountEmail;
    public string databaseP12Password;

    [Header("Firebase Settings > Cloud Messaging > Project Credentials")]
    public string serverKey = "";

    // Unique id for this player, it's used to keep track of their referrals and track them in analytics
    public string persistantUserId { get; private set; }
    
    public bool isFirstSession { get; private set; }
    
    // Reference to the firebase base instance
    public FirebaseApp app { get; private set; }

    // References to the firebase scripts which we instantiate at runtime
    public FirebaseAnalyticsManager analyticsManager { get; private set; }
    //public FirebaseAuthManager authManager { get; private set;  }
    //public FirebaseDatabaseManager databaseManager { get; private set; }
    //public FirebaseDynamicLinksManager DynamicLinksManager { get; private set; }
    public FirebaseMessagingManager messagingManager { get; private set; }
    //public FirebaseRemoteConfigManager remoteConfigManager { get; private set; }

    // Callback to when firebase is ready
    public static event Action OnFirebaseInitialised;

    // Callback to when firebase is done initialising (successful or not)
    public static event Action OnFirebaseInitialisationDone;

    public bool isInitialised { get; private set; }
    
    private DateTime epochStart;
    
    private static readonly Queue<Action> functionQueue = new Queue<Action>();
    
    void Awake()
    {
        // Set the instance variable to an instance of this script if it's not already set
        instance = instance ?? this;

        epochStart = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        
        // Set the unique user id for this user, it's used to track this player in analytics as well as in referral data
        if (PlayerPrefs.HasKey("firebase_user_id"))
        {
            persistantUserId = PlayerPrefs.GetString("firebase_user_id");
        }
        else
        {
            isFirstSession = true;
            
            // Generate a globally unique id https://docs.microsoft.com/en-us/dotnet/api/system.guid
            persistantUserId = Guid.NewGuid().ToString();
            PlayerPrefs.SetString("firebase_user_id", persistantUserId);
        }

        // Cache a reference to the self gameobject
        GameObject cachedObj = gameObject;
        
        // Instantiate misc required script
        AddComponentIfNotAlreadyAdded(cachedObj, typeof(UnityMainThreadDispatcher));
        
        // Instantiate the firebase management scripts
        analyticsManager = (FirebaseAnalyticsManager)AddComponentIfNotAlreadyAdded(cachedObj, typeof(FirebaseAnalyticsManager));
        
        //authManager = (FirebaseAuthManager)AddComponentIfNotAlreadyAdded(cachedObj, typeof(FirebaseAuthManager));
        //databaseManager = (FirebaseDatabaseManager)AddComponentIfNotAlreadyAdded(cachedObj, typeof(FirebaseDatabaseManager));
        //DynamicLinksManager = (FirebaseDynamicLinksManager)AddComponentIfNotAlreadyAdded(cachedObj, typeof(FirebaseDynamicLinksManager));
        messagingManager = (FirebaseMessagingManager)AddComponentIfNotAlreadyAdded(cachedObj, typeof(FirebaseMessagingManager));
        //remoteConfigManager = (FirebaseRemoteConfigManager)AddComponentIfNotAlreadyAdded(cachedObj, typeof(FirebaseRemoteConfigManager));

        OnFirebaseInitialised += FirebaseInitialised;
    }

    private object AddComponentIfNotAlreadyAdded(GameObject obj, Type type) {
        object existingComponent = obj.GetComponent(type);

        return existingComponent != null ? existingComponent : obj.AddComponent(type);
    }

    #if UNITY_EDITOR
        void Reset()
        {
            androidPackageName = Application.identifier;
            iosPackageName = Application.identifier;
        }
    #endif
    
    public void Start() {
        if (debugMode) {
            FirebaseApp.LogLevel = LogLevel.Verbose;
        } else {
            FirebaseApp.LogLevel = LogLevel.Error;
        }

        FirebaseApp.CheckAndFixDependenciesAsync().ContinueWith(task =>
        {
            DependencyStatus status = task.Result;

            switch (status)
            {
                case DependencyStatus.Available:
                    app = FirebaseApp.DefaultInstance;
                    
                    // Set an initialised bool to true before invoking the initialised callback (just a simple variable to cheaply check if it's initialised)
                    isInitialised = true;

                    // Null propagated invoke on the firebase initialisation callback
                    UnityMainThreadDispatcher.instance.Enqueue(() => OnFirebaseInitialised?.Invoke());
                    
                    // Get the instance id of the app (used for testing some firebase features)
                    Firebase.Installations.FirebaseInstallations.DefaultInstance.GetIdAsync().ContinueWith(instanceTask => {
                        #if !UNITY_EDITOR
                            Debug.Log("Firebase initialised successfully! Instance id: " + instanceTask.Result);
                        #endif

                        UnityMainThreadDispatcher.instance.Enqueue(() => OnFirebaseInitialisationDone?.Invoke());
                    });
                    break;
                
                default:
                    // Firebase isn't working correctly due to plugin errors, firebase will not be used
                    Debug.LogError("Could not resolve all firebase dependencies: " + status);
                    
                    UnityMainThreadDispatcher.instance.Enqueue(() => OnFirebaseInitialisationDone?.Invoke());
                    break;
            }
        });
    }
    
    public string GetPathFriendlyIdentifier() {
        return uniqueGameIdentifier.Replace(".", "_");
    }

    private void FirebaseInitialised() {
        if(debugMode)
            Debug.Log("Firebase initialised, invoking queued functions..");
        
        OnFirebaseInitialised -= FirebaseInitialised;

        StartCoroutine(InvokeFunctions());
    }

    private IEnumerator InvokeFunctions() {
        yield return null;
        
        while (functionQueue.Count > 0) {
            // Invokes the queued function and removes it from the queue
            functionQueue.Dequeue().Invoke();
        }
    }

    public static bool IsInitialised() {
        return instance.isInitialised;
    }
    
    public void AddToInitialiseQueue(Action function) {
        functionQueue.Enqueue(function);
    }

    public long GetTimestamp()
    {
        return (long) (DateTime.UtcNow - epochStart).TotalSeconds;
    }
}
