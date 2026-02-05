using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Firebase;
using Firebase.Installations;

public class FirebaseManager : MonoBehaviour
{
    public static FirebaseManager instance;
    
    // Enables some extra logging + other checks which notify the developer of issues
    // Disable this in release builds as it affects performance
    public bool debugMode;

    // Used as an iOS fallback when unable to resolve the appstore id from the package name
    public string iosAppStoreId;

    [Header("Firebase Settings")]
    // Must be the same on android and iOS to allow the app to be launched from shared links (this is usually just the android app
    public string uniqueGameIdentifier = "unique_game_id";
    
    // Unique id for this player, it's used to keep track of their referrals and track them in analytics
    public string persistantUserId { get; set; }
    public string instanceId { get; private set; }
    
    public bool isFirstSession { get; private set; }
    
    // Reference to the firebase base instance
    public FirebaseApp app { get; private set; }

    // References to the firebase scripts which we instantiate at runtime
    public FirebaseAnalyticsManager analyticsManager { get; private set; }
    public FirebaseMessagingManager messagingManager { get; private set; }

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
        if (PlayerPrefs.HasKey("firebase_user_id")) {
            persistantUserId = PlayerPrefs.GetString("firebase_user_id");
        } else {
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
        messagingManager = (FirebaseMessagingManager)AddComponentIfNotAlreadyAdded(cachedObj, typeof(FirebaseMessagingManager));
        
        OnFirebaseInitialised += FirebaseInitialised;
    }

    private object AddComponentIfNotAlreadyAdded(GameObject obj, Type type) {
        object existingComponent = obj.GetComponent(type);

        return existingComponent != null ? existingComponent : obj.AddComponent(type);
    }

    #if UNITY_EDITOR
        void Reset() {
            uniqueGameIdentifier = Application.identifier;
        }
    #endif

    private bool hasInitCallbackBeenRan = false;
    
    public void Start() {
        if (debugMode) {
            FirebaseApp.LogLevel = LogLevel.Verbose;
        } else {
            FirebaseApp.LogLevel = LogLevel.Error;
        }

        try {
            FirebaseApp.CheckAndFixDependenciesAsync().ContinueWith(task => {
                DependencyStatus status = task.Result;

                switch (status) {
                    case DependencyStatus.Available:
                        // Trigger the callback for firebase init being done before doing anything with firebase (just in case errors prevent us continuing)
                        CallbackInitDone();

                        app = FirebaseApp.DefaultInstance;
                        
                        // Set an initialised bool to true before invoking the initialised callback (just a simple variable to cheaply check if it's initialised)
                        isInitialised = true;
                        
                        // Forces OnFirebaseInitialised to be called on the main thread to prevent potential crashes (such as if the callback triggers something with PlayerPrefs)
                        UnityMainThreadDispatcher.instance.Enqueue(() => OnFirebaseInitialised?.Invoke());
                        
                        try {
                            FirebaseInstallations firebaseInstance = FirebaseInstallations.DefaultInstance;

                            if (firebaseInstance != null) {
                                // Get the instance id of the app (used for testing some firebase features or if the user wants to do a data deletion request)
                                firebaseInstance.GetIdAsync().ContinueWith(instanceTask => {
                                    instanceId = instanceTask.Result;
                                    Debug.Log("Firebase initialised successfully! Instance id: " + instanceId);
                                });
                            } else {
                                Debug.LogError("Firebase installations instance was null! Failed to get instance id");
                            }
                        } catch (Exception e) {
                            Debug.LogError("Failed to get instance id, your device may not support firebase - " + e.Message);
                        }
                        break;

                    default:
                        switch (status) {
                            case DependencyStatus.UnavailableDisabled:
                                Debug.LogError("Google Play Services is disabled! Unable to initialise Firebase.");
                                break;
                            
                            case DependencyStatus.UnavailableInvalid:
                                Debug.LogError("Invalid Google Play Services setup! Unable to initialise Firebase.");
                                break;
                            
                            case DependencyStatus.UnavailableOther:
                                Debug.LogError("Unknown Google Play Services error! Unable to initialise Firebase.");
                                break;
                            
                            case DependencyStatus.UnavailablePermission:
                                Debug.LogError("Google Play Services permission error! Unable to initialise Firebase.");
                                break;
                            
                            case DependencyStatus.UnavailableUpdaterequired:
                                Debug.LogError("Google Play Services update required! Unable to initialise Firebase.");
                                break;
                            
                            case DependencyStatus.UnavailableUpdating:
                                Debug.LogError("Google Play Services is currently updating! Unable to initialise Firebase.");
                                break;
                            
                            case DependencyStatus.UnavilableMissing:
                                Debug.LogError("Google Play Services not installed! Unable to initialise Firebase.");
                                break;
                        }

                        CallbackInitDone();
                        break;
                }
            });
        } catch (Exception e) {
            Debug.LogError("Failed to initialise firebase - " + e.Message);
            CallbackInitDone();
        }
    }

    public void CallbackInitDone() {
        if (!hasInitCallbackBeenRan) {
            hasInitCallbackBeenRan = true;
            UnityMainThreadDispatcher.instance.Enqueue(() => OnFirebaseInitialisationDone?.Invoke());
        }
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
        if (instance == null)
            return false;
        
        return instance.isInitialised;
    }
    
    public static void AddToInitialiseQueue(Action function) {
        functionQueue.Enqueue(function);
    }

    public long GetTimestamp() {
        return (long) (DateTime.UtcNow - epochStart).TotalSeconds;
    }
}
