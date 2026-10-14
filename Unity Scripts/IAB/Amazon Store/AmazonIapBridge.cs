using System;
using System.Collections.Generic;

namespace AmazonIap
{
#if UNITY_ANDROID
    using UnityEngine;

    /// <summary>
    /// Static entry point for the Amazon Appstore In-App Purchasing SDK (v3.0.9), called
    /// directly from C# via AndroidJavaObject/AndroidJavaClass instead of a compiled
    /// native (.so) bridge. Because this is plain JNI reflection over Java classes, it
    /// works identically on armeabi-v7a and arm64-v8a - there is nothing architecture-
    /// specific to build or ship.
    ///
    /// The Amazon SDK enforces internally that every PurchasingService call happens on
    /// Android's actual UI thread (it throws "Executing thread must be thread: X, was: Y"
    /// otherwise) - this is NOT guaranteed to be the same OS thread as Unity's C# "main
    /// thread" depending on your Unity/Android configuration. Every call below is routed
    /// through Activity.runOnUiThread accordingly. That makes these calls fire-and-forget
    /// from C#'s perspective (the RequestId Amazon returns synchronously in Java is no
    /// longer available to us here) - nothing in this bridge relied on that return value
    /// for correctness, only for logging, so callers just get a "submitted" log instead.
    ///
    /// Requires: amazon-appstore-sdk-3.0.9.jar in Assets/Plugins/Android/libs/, the
    /// ResponseReceiver + queries block added to your merged AndroidManifest.xml, and
    /// IL2CPP scripting backend with ARM64 included in Target Architectures (Mono cannot
    /// produce arm64 Android builds).
    /// </summary>
    public static class AmazonIapBridge
    {
        private const string PurchasingServiceClass = "com.amazon.device.iap.PurchasingService";
        private const string FulfillmentResultClass = "com.amazon.device.iap.model.FulfillmentResult";

        public static event Action<AmazonUserDataResult> OnUserDataResponse;
        public static event Action<AmazonProductDataResult> OnProductDataResponse;
        public static event Action<AmazonPurchaseResult> OnPurchaseResponse;
        public static event Action<AmazonPurchaseUpdatesResult> OnPurchaseUpdatesResponse;

        private static AmazonIapListenerProxy _listener;
        private static AndroidJavaObject _activity;
        private static bool _initialized;

        /// <summary>Call once, e.g. from your bootstrap scene before any other method here.</summary>
        public static void Initialize()
        {
            if (_initialized) return;

            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            {
                _activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            }

            _listener = new AmazonIapListenerProxy();
            _listener.UserDataResponse += r => OnUserDataResponse?.Invoke(r);
            _listener.ProductDataResponse += r => OnProductDataResponse?.Invoke(r);
            _listener.PurchaseResponse += r => OnPurchaseResponse?.Invoke(r);
            _listener.PurchaseUpdatesResponse += r => OnPurchaseUpdatesResponse?.Invoke(r);

            RunOnUiThread(() =>
            {
                using (var appContext = _activity.Call<AndroidJavaObject>("getApplicationContext"))
                using (var purchasingService = new AndroidJavaClass(PurchasingServiceClass))
                {
                    // Order matches Amazon's own sample app: enable pending purchases, then register.
                    purchasingService.CallStatic("enablePendingPurchases");
                    purchasingService.CallStatic("registerListener", appContext, _listener);
                }
            });

            _initialized = true;
        }

        /// <summary>Result arrives via OnUserDataResponse. Call on resume/foreground.</summary>
        public static void GetUserData()
        {
            RunOnUiThread(() =>
            {
                using (var purchasingService = new AndroidJavaClass(PurchasingServiceClass))
                using (purchasingService.CallStatic<AndroidJavaObject>("getUserData")) { }
            });
        }

        /// <summary>Result arrives via OnProductDataResponse. Call on resume/foreground.</summary>
        public static void GetProductData(IEnumerable<string> skus)
        {
            RunOnUiThread(() =>
            {
                using (var purchasingService = new AndroidJavaClass(PurchasingServiceClass))
                using (var skuSet = new AndroidJavaObject("java.util.HashSet"))
                {
                    foreach (var sku in skus)
                    {
                        skuSet.Call<bool>("add", sku);
                    }
                    using (purchasingService.CallStatic<AndroidJavaObject>("getProductData", skuSet)) { }
                }
            });
        }

        /// <summary>Result arrives via OnPurchaseResponse.</summary>
        public static void Purchase(string sku)
        {
            RunOnUiThread(() =>
            {
                using (var purchasingService = new AndroidJavaClass(PurchasingServiceClass))
                using (purchasingService.CallStatic<AndroidJavaObject>("purchase", sku)) { }
            });
        }

        /// <summary>
        /// Result arrives via OnPurchaseUpdatesResponse. Call on resume/foreground; if the
        /// result's HasMore is true, call again with reset=false to page through the rest.
        /// </summary>
        public static void GetPurchaseUpdates(bool reset)
        {
            RunOnUiThread(() =>
            {
                using (var purchasingService = new AndroidJavaClass(PurchasingServiceClass))
                using (purchasingService.CallStatic<AndroidJavaObject>("getPurchaseUpdates", reset)) { }
            });
        }

        /// <summary>Tells Amazon a receipt has been granted (or why it wasn't) so it stops re-sending it.</summary>
        public static void NotifyFulfillment(string receiptId, AmazonFulfillmentResult result)
        {
            RunOnUiThread(() =>
            {
                using (var purchasingService = new AndroidJavaClass(PurchasingServiceClass))
                using (var fulfillmentResultClass = new AndroidJavaClass(FulfillmentResultClass))
                using (var resultObj = fulfillmentResultClass.CallStatic<AndroidJavaObject>("valueOf", result.ToString()))
                {
                    purchasingService.CallStatic("notifyFulfillment", receiptId, resultObj);
                }
            });
        }

        private static void RunOnUiThread(Action action)
        {
            _activity.Call("runOnUiThread", new AndroidJavaRunnable(action));
        }
    }
#else
    /// <summary>Editor / non-Android stub so the rest of your game code compiles on every platform.</summary>
    public static class AmazonIapBridge
    {
        public static event Action<AmazonUserDataResult> OnUserDataResponse;
        public static event Action<AmazonProductDataResult> OnProductDataResponse;
        public static event Action<AmazonPurchaseResult> OnPurchaseResponse;
        public static event Action<AmazonPurchaseUpdatesResult> OnPurchaseUpdatesResponse;

        public static void Initialize() { }
        public static void GetUserData() { }
        public static void GetProductData(IEnumerable<string> skus) { }
        public static void Purchase(string sku) { }
        public static void GetPurchaseUpdates(bool reset) { }
        public static void NotifyFulfillment(string receiptId, AmazonFulfillmentResult result) { }
    }
#endif
}
