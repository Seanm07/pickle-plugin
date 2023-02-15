using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Firebase.Messaging;
//using Firebase.RemoteConfig;

public class FirebaseMessagingManager : MonoBehaviour {
    public string registrationToken { get; private set; }

    public static event Action OnSubscribeActionComplete;
    public static event Action<string> OnSubscribeActionFailed;

    public static event Action OnUnsubscribeActionComplete;
    public static event Action<string> OnUnsubscribeActionFailed;
    
    private const string PREF_MESSAGING_STATE = "firebase_messaging_state";
    
    void Awake()
    {
        // Assign the callback reference
        FirebaseManager.OnFirebaseInitialised += FirebaseInitialised;
        
        #if !UNITY_EDITOR
        FirebaseMessaging.TokenReceived += TokenReceived;
        FirebaseMessaging.MessageReceived += MessageReceived;
        #endif
        
        // Check if the key for the main subscription state has been set already
        if (!PlayerPrefs.HasKey(PREF_MESSAGING_STATE + "_main"))
        {
            // By default the main subscription state is true (as far as we know.. the user could've just not allowed notifications)
            PlayerPrefs.SetInt(PREF_MESSAGING_STATE + "_main", 1);
        }
    }
    
    private static bool IsFirebaseInitialised(Action action) {
        bool isInitialised = FirebaseManager.IsInitialised();
        
        if (!isInitialised)
            FirebaseManager.instance.AddToInitialiseQueue(action);
        
        return isInitialised;
    }

    // Called when firebase has successfully been initialised
    private void FirebaseInitialised()
    {
        FirebaseManager.OnFirebaseInitialised -= FirebaseInitialised;
        
        // Subscribe to the firebase function ping called when the remote config has changed
        Subscribe("REMOTE_CONFIG"); // Name defined in index.js of the firebase console
    }

    public static void Subscribe(string topic)
    {
        // If firebase isn't initialised yet then this function call is queued and re-called when it is
        if (!IsFirebaseInitialised(() => Subscribe(topic))) return;
        
        FirebaseMessaging.SubscribeAsync(topic).ContinueWith(task =>
        {
            switch (task.Status) {
                case TaskStatus.Canceled:
                    Debug.LogError("Subscribe notifications cancelled!");
                    
                    // Enqueue the callback to be called on the main thread
                    UnityMainThreadDispatcher.instance.Enqueue(() => OnSubscribeActionFailed?.Invoke("Cancelled"));
                    break;
                
                case TaskStatus.Faulted:
                    Debug.LogError("Subscribe notifications failed! Error: " + task.Exception?.ToString());
                    
                    FirebaseAnalyticsManager.LogError("Subscribe - " + task.Exception?.ToString());
                    
                    // Enqueue the callback to be called on the main thread
                    UnityMainThreadDispatcher.instance.Enqueue(() => OnSubscribeActionFailed?.Invoke("Failed with error: " + task.Exception));
                    break;
                
                default:
                    // Enqueue the callback to be called on the main thread
                    UnityMainThreadDispatcher.instance.Enqueue(() => OnSubscribeActionComplete?.Invoke());
                    
                    PlayerPrefs.SetInt(PREF_MESSAGING_STATE + "_" + topic, 1);
                    break;
            }
        });
    }

    public static void Unsubscribe(string topic)
    {
        // If firebase isn't initialised yet then this function call is queued and re-called when it is
        if (!IsFirebaseInitialised(() => Unsubscribe(topic))) return;
        
        FirebaseMessaging.UnsubscribeAsync(topic).ContinueWith(task =>
        {
            switch (task.Status) {
                case TaskStatus.Canceled:
                    Debug.LogError("Unsubscribe notifications cancelled!");
                    
                    // Enqueue the callback to be called on the main thread
                    UnityMainThreadDispatcher.instance.Enqueue(() => OnUnsubscribeActionFailed?.Invoke("Cancelled"));
                    break;
                
                case TaskStatus.Faulted:
                    Debug.LogError("Unsubscribe notifications failed! Error: " + task.Exception?.ToString());
                    
                    FirebaseAnalyticsManager.LogError("Unsubscribe - " + task.Exception?.ToString());
                    
                    // Enqueue the callback to be called on the main thread
                    UnityMainThreadDispatcher.instance.Enqueue(() => OnUnsubscribeActionFailed?.Invoke("Failed with error: " + task.Exception));
                    break;
                
                default:
                    // Enqueue the callback to be called on the main thread
                    UnityMainThreadDispatcher.instance.Enqueue(() => OnUnsubscribeActionComplete?.Invoke());
                    
                    PlayerPrefs.SetInt(PREF_MESSAGING_STATE + "_" + topic, 0);
                    break;
            }
        });
    }

    /*public static void Send(string title, string message) {
        // If firebase isn't initialised yet then this function call is queued and re-called when it is
        if (!IsFirebaseInitialised(() => Send(title, message))) return;
        
        FirebaseMessage firebaseMessage = new FirebaseMessage();

        firebaseMessage.To = FirebaseManager.instance.messagingManager.registrationToken;
        firebaseMessage.Data = new Dictionary<string, string>();
        firebaseMessage.Data.Add("title", title);
        firebaseMessage.Data.Add("body", message);
        
        FirebaseMessaging.Send(firebaseMessage);
    }*/

    public static bool IsSubscribed(string topic) {
        return PlayerPrefs.GetInt(PREF_MESSAGING_STATE + "_" + topic, 1) == 1;
    }
    
    private void TokenReceived(object sender, TokenReceivedEventArgs e)
    {
        FirebaseAnalyticsManager.LogEvent("messaging_token", "error", e.Token);

        Debug.Log("Received messaging token!");

        registrationToken = e.Token;
    }

    private void MessageReceived(object sender, MessageReceivedEventArgs e)
    {
        FirebaseAnalyticsManager.LogEvent("messaging_message", "error", e.Message.From);

        Debug.Log("Received message from " + e.Message.From);
        
        // From is the topic name when topic messages are sent https://stackoverflow.com/questions/48415951/how-to-know-which-topic-name-in-firebase-notification-in-android
        switch (e.Message.From) {
            /*case "REMOTE_CONFIG": // Send via firebase functions when the remote config has changed
                foreach (KeyValuePair<string, string> data in e.Message.Data) {
                    if (data.Key == "CONFIG_STATE" && data.Value == "STALE")
                        FirebaseRemoteConfigManager.FetchRemoteConfig();
                }
                break;*/
        }
    }
    
}
