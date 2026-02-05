using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Firebase.Messaging;
using UnityEngine.Purchasing;

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
        
        // Check if the key for the main subscription state has been set already
        if (!PlayerPrefs.HasKey(PREF_MESSAGING_STATE + "_main"))
        {
            // By default the main subscription state is true (as far as we know.. the user could've just not allowed notifications)
            PlayerPrefs.SetInt(PREF_MESSAGING_STATE + "_main", 1);
        }

        OnSubscribeActionComplete += OnSubscribeComplete;
        OnSubscribeActionFailed += OnSubscribeFailed;
        OnUnsubscribeActionComplete += OnUnsubscribeComplete;
        OnUnsubscribeActionFailed += OnUnsubscribeFailed;
    }
    
    private static bool IsFirebaseInitialised(Action action) {
        bool isInitialised = FirebaseManager.IsInitialised();
        
        if (!isInitialised)
            FirebaseManager.AddToInitialiseQueue(action);
        
        return isInitialised;
    }

    // Called when firebase has successfully been initialised
    private void FirebaseInitialised()
    {
#if !UNITY_EDITOR
        FirebaseMessaging.TokenReceived += TokenReceived;
        FirebaseMessaging.MessageReceived += MessageReceived;
#endif
        
        FirebaseManager.OnFirebaseInitialised -= FirebaseInitialised;
        
        // Subscribe to the firebase function ping called when the remote config has changed
        //Subscribe("REMOTE_CONFIG"); // Name defined in index.js of the firebase console
    }

    public string activeSubscribeTopic { get; set; }
    
    public static void Subscribe(string topic)
    {
        // If firebase isn't initialised yet then this function call is queued and re-called when it is
        if (!IsFirebaseInitialised(() => Subscribe(topic))) return;
        
        FirebaseManager.instance.messagingManager.activeSubscribeTopic = topic;
        
        FirebaseMessaging.SubscribeAsync(topic).ContinueWith(task =>
        {
            switch (task.Status) {
                case TaskStatus.Canceled:
                    // Enqueue the callback to be called on the main thread
                    UnityMainThreadDispatcher.instance.Enqueue(() => OnSubscribeActionFailed?.Invoke("Cancelled"));
                    break;
                
                case TaskStatus.Faulted:
                    // Enqueue the callback to be called on the main thread
                    UnityMainThreadDispatcher.instance.Enqueue(() => OnSubscribeActionFailed?.Invoke("Failed with error: " + task.Exception));
                    break;
                
                default:
                    // Enqueue the callback to be called on the main thread
                    UnityMainThreadDispatcher.instance.Enqueue(() => OnSubscribeActionComplete?.Invoke());
                    break;
            }
        });
    }
    
    public string activeUnsubscribeTopic { get; set; }
    
    public static void Unsubscribe(string topic)
    {
        // If firebase isn't initialised yet then this function call is queued and re-called when it is
        if (!IsFirebaseInitialised(() => Unsubscribe(topic))) return;

        FirebaseManager.instance.messagingManager.activeUnsubscribeTopic = topic;
        
        FirebaseMessaging.UnsubscribeAsync(topic).ContinueWith(task =>
        {
            switch (task.Status) {
                case TaskStatus.Canceled:
                    // Enqueue the callback to be called on the main thread
                    UnityMainThreadDispatcher.instance.Enqueue(() => OnUnsubscribeActionFailed?.Invoke("Cancelled"));
                    break;
                
                case TaskStatus.Faulted:
                    // Enqueue the callback to be called on the main thread
                    UnityMainThreadDispatcher.instance.Enqueue(() => OnUnsubscribeActionFailed?.Invoke("Failed with error: " + task.Exception));
                    break;
                
                default:
                    // Enqueue the callback to be called on the main thread
                    UnityMainThreadDispatcher.instance.Enqueue(() => OnUnsubscribeActionComplete?.Invoke());
                    break;
            }
        });
    }

    private void OnSubscribeComplete() {
        PlayerPrefs.SetInt(PREF_MESSAGING_STATE + "_" + activeSubscribeTopic, 1);
        Debug.Log("Subscribed to topic: " + activeSubscribeTopic);
    }

    private void OnSubscribeFailed(string error) {
        Debug.LogError("Failed to subscribe to topic: " + activeSubscribeTopic + " - Error: " + error);
        FirebaseAnalyticsManager.LogError("Subscribe - " + error);
    }
    
    private void OnUnsubscribeComplete() {
        PlayerPrefs.SetInt(PREF_MESSAGING_STATE + "_" + activeUnsubscribeTopic, 0);
        Debug.Log("Unsubscribed from topic: " + activeUnsubscribeTopic);
    }

    private void OnUnsubscribeFailed(string error) {
        Debug.LogError("Failed to unsubscribe from topic: " + activeUnsubscribeTopic + " - Error: " + error);
        FirebaseAnalyticsManager.LogError("Unsubscribe - " + error);
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
        //FirebaseAnalyticsManager.LogEvent("messaging_token", "token", e.Token);

        Debug.Log("Received messaging token! - " + e.Token);

        registrationToken = e.Token;
    }

    private void MessageReceived(object sender, MessageReceivedEventArgs e)
    {
        //FirebaseAnalyticsManager.LogEvent("messaging_message", "error", e.Message.From);
        
        Debug.Log("Received message from " + e.Message.From);
        
        // From is the topic name when topic messages are sent https://stackoverflow.com/questions/48415951/how-to-know-which-topic-name-in-firebase-notification-in-android
        switch (e.Message.From) {
            /*case "REMOTE_CONFIG": // Send via firebase functions when the remote config has changed
                foreach (KeyValuePair<string, string> data in e.Message.Data) {
                    if (data.Key == "CONFIG_STATE" && data.Value == "STALE")
                        FirebaseRemoteConfigManager.FetchRemoteConfig();
                }
                break;*/
            
            default:
                // If this message was received via tapping the notification on the system tray
                if (e.Message.NotificationOpened) {
                    Debug.Log("Message was opened via system tray!");
                    
                    // Assume the notification is only targetting Google Play/iOS unless otherwise specified
                    bool targetAllStores = false;
                    AppStore targetStore = Application.platform == RuntimePlatform.Android ? AppStore.GooglePlay : AppStore.AppleAppStore;

                    if (e.Message.Data.ContainsKey("store")) {
                        if (e.Message.Data["store"].ToLowerInvariant() == "all") {
                            targetAllStores = true;
                        }
                    }

                    AppStore activeStore = CrossPlatformManager.GetActiveStore();
                    
                    if (targetAllStores || activeStore == targetStore) {
                        Debug.Log("Message was targetting this app store!");
                        
                        if (e.Message.Data.ContainsKey("url")) {
                            ClickLockManager.Instance.ShowClickLock("Launching App Store", true, true, false, false, true);
                            
                            pendingURL = e.Message.Data["url"];
                            
                            Debug.Log("Message opening url: " + pendingURL);
                            
                            Application.quitting += OpenPendingURL;
    
                            Application.Quit();
                        } else {
                            Debug.Log("Message didn't have url data set!");

                            Debug.Log("Dumping the data dictionary:");

                            foreach (KeyValuePair<string, string> curData in e.Message.Data) {
                                Debug.Log("Data dump: KEY[" + curData.Key + "] VALUE[" + curData.Value + "]");
                            }
                        }
                    } else {
                        Debug.Log("Message not targeting this store, ignoring..");
                    }
                } else {
                    Debug.Log("Message wasn't opened via system tray.. ignoring");
                }
                break;
        }
    }

    private string pendingURL;

    private void OpenPendingURL() {
        // If the push notification contained url data then open the url when the notification is tapped
        Application.OpenURL(pendingURL);
    }

}
