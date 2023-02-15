using System.Collections.Generic;
using UnityEngine;
using System.Collections;
using UnityEngine.Purchasing;
using UnityEngine.Purchasing.Security;
using UnityEngine.UDP;

public class IABHandlerUDP : MonoBehaviour, IABHandler, IPurchaseListener
{
    private IABManager IAB;

    // Just using a fixed developer payload as we're not really doing purchase validation on UDP stores
    // Also because of how UDP works I'm worried purchases will overlap due to the delayed callbacks
    // so I don't want to set a unique payload (developer payload isn't a good way to secure purchases anyway,
    // every spoofing app knows how to take the payload from the purchase request and add it into the dummy receipt)
    private string devPayload = "0000000000000000";

    private Inventory cachedInventory; // Note: This will have outdated purchase ownership info until the inventory is re-queried
    
    public void Init(IABManager manager) {
        IAB = manager;
    }
    
    public void InitializePurchasing() {
        if (!CrossPlatformManager.instance.hasInitialized) {
            Debug.LogError("Failed to initialize billing! (Store initialization has not completed)");
            return;
        }

        // UDP the billing initialization is part of the store initialization so we're already done initialising
        IAB.isInitializing = false;
        IAB.isInitialized = true;

        // UDP we need to initialise the purchasing differently
        // The product list will always be the catalog configured in the Unity IAP dashboard
        // Calling StoreService.QueryInventory with a product list parameter does nothing
        StoreService.QueryInventory(this);
    }
    
    // Forward UDP purchase events to our standard purchase manager
    public void OnPurchase(PurchaseInfo purchaseInfo) {
        if(FirebaseManager.instance.debugMode)
            Debug.Log("OnPurchase called for SKU: " + purchaseInfo.ProductId);

        IABItem localItem = IAB.GetItemBySKU(purchaseInfo.ProductId);

        if (localItem != null && !localItem.isProcessingValidation)
            OnPurchaseComplete(purchaseInfo);
    }

    public void OnPurchaseComplete(PurchaseInfo purchaseInfo) {
        OnPurchaseComplete(purchaseInfo, false);
    }
    
    private IEnumerator WaitForInitialisationThenResumePurchaseComplete(PurchaseInfo purchaseInfo) {
        float waitTime = 0f;

        // If billing isn't initialising and billing isn't supported then the previous initialisation attempt failed - try again
        if (!IAB.isInitializing)
            InitializePurchasing();

        yield return null;

        // Wait up to 5 seconds for billing to finish initialising (ends early if initilisation finished, success or failure)
        while (waitTime < 5f && IAB.isInitializing) {
            yield return null;

            waitTime += Time.unscaledDeltaTime;
        }

        OnPurchaseComplete(purchaseInfo, true);
    }

    public void OnPurchaseComplete(PurchaseInfo purchaseInfo, bool skipInitialisingCheck) {
        if (!skipInitialisingCheck && IAB.isInitializing) {
            IAB.StartCoroutine(WaitForInitialisationThenResumePurchaseComplete(purchaseInfo));

            return;
        }
        
        if(FirebaseManager.instance.debugMode)
            Debug.Log("Purchase ready to be processed [Item: " + purchaseInfo.ProductId + "]");

        // Something's happening, refresh the timer for the hold on screen to show the skip button
		if(ClickLockManager.Instance != null)
			ClickLockManager.Instance.ResetTimer();
        
        // UDP needs to manually consume consumable items once purchase is complete
        IABItem localItem = IAB.GetItemBySKU(purchaseInfo.ProductId);
        
        IAB.OnPurchaseVerificationCompleted(localItem, ""); // UDP version skips serverside verification checks

        if(localItem.type == ProductType.Consumable) 
            ConsumeItem(localItem.productId, purchaseInfo);
    }

    public void OnPurchaseFailed(string error, PurchaseInfo purchaseInfo)
    {
        Debug.LogError("UDP Purchasing Error (" + (purchaseInfo == null ? "no purchaseInfo" : purchaseInfo.ProductId) + ") - " + error);

        // Remove the exception from the end of the error string if a purchase is made without a connection
        if (error.Contains("DATA_COMMUNICATION_ERROR"))
            error = "DATA_COMMUNICATION_ERROR";
        
        // Remove the exception from the end of the error string if a purchase is made without a connection
        if (error.Contains("needs to be consumed"))
            error = "needs to be consumed";

        if (error.Contains("Cancelled"))
            error = "purchase canceled";
        
        // Convert some basic error things into some better worded messages
        // (I literally can't find any examples anywhere of the errors it returns..)
        switch (error) {
            case "purchase canceled":
                error = "Purchase cancelled, you have not been charged.";
                break;
            
            case "Resource not found":
                error = "Item no longer available for purchase!\nCheck the store for app updates!";
                break;
            
            case "Please initialize first":
                error = "Sign into the store to make purchases!\nMake sure you're logged in and try again.";

                CrossPlatformManager.instance.InitialiseStoreService();
                break;
            
            case "DATA_COMMUNICATION_ERROR":
                error = "Failed to connect to store!\nCheck your connection and try again later.";
                break;
            
            case "needs to be consumed":
                error = "An existing purchase is still being processed!\nCheck your connection and try again later.";
                break;
        }
        
        IAB.OnPurchaseFailed(error);
    }

    // Called when a non-consumable item was attempted to be purchased again (just give the user the item as if this is a purchase success)
    public void OnPurchaseRepeated(string sku) {
        Debug.Log("UDP OnPurchaseRepeated (" + sku + ")");

        // Something's happening, refresh the timer for the hold on screen to show the skip button
		if(ClickLockManager.Instance != null)
			ClickLockManager.Instance.ResetTimer();
        
        // UDP needs to manually consume consumable items once purchase is complete
        IABItem localItem = IAB.GetItemBySKU(sku);
        
        IAB.OnPurchaseVerificationCompleted(localItem, ""); // UDP version skips serverside verification checks

        // If the item was consumable we may need to consume it, however without any purchaseInfo in the callback we need to refresh the inventory
        StoreService.QueryInventory(this);
    }

    public void OnPurchaseConsume(PurchaseInfo purchaseInfo)
    {
        Debug.Log("UDP OnPurchaseConsume (" + purchaseInfo.ProductId + ")");
        // Purchase has been consumed, no need to call anything
    }

    public void OnPurchaseConsumeFailed(string message, PurchaseInfo purchaseInfo)
    {
        Debug.LogError("UDP Consume Error (" + purchaseInfo.ProductId + ") - " + message);
        // We don't need to call anything from this
    }

    public void OnQueryInventory(Inventory inventory) {
        if (FirebaseManager.instance.debugMode)
            Debug.Log("IAB UDP Item data is ready!");

        cachedInventory = inventory;
        
        IList<ProductInfo> allProductInfo = inventory.GetProductList();
        
        if (FirebaseManager.instance.debugMode)
            Debug.Log("Found " + allProductInfo.Count + " products in product list");
        
        foreach (ProductInfo productInfo in allProductInfo) {
            //PurchaseInfo purchaseInfo = inventory.GetPurchaseInfo(productInfo.ProductId);
            IABItem localItem = IAB.GetItemBySKU(productInfo.ProductId);

            if (localItem != null) {
                localItem.priceValue = (productInfo.PriceAmountMicros / 1000000).ToString("F2");
                localItem.formattedPrice = productInfo.Price;
                localItem.description = productInfo.Description;
                localItem.title = productInfo.Title;
                localItem.currencyCode = productInfo.Currency;

                if (FirebaseManager.instance.debugMode)
                    Debug.Log("Product " + productInfo.ProductId + " loaded with data: " + localItem.priceValue + ", " + localItem.formattedPrice + ", " + localItem.description + ", " + localItem.title + ", " + localItem.currencyCode);
                
                if (inventory.HasPurchase(productInfo.ProductId) && !localItem.isProcessingValidation) {
                    if (FirebaseManager.instance.debugMode)
                        Debug.Log("Product " + productInfo.ProductId + " is currently owned");
                    
                    switch (localItem.type) {
                        case ProductType.Consumable:
                            if (localItem.purchaseState == PurchaseState.Pending)
                                IAB.OnPurchaseVerificationCompleted(localItem, ""); // UDP version skips serverside verification checks
                            
                            // UDP needs to manually consume consumable items once purchase is complete
                            ConsumeItem(localItem.productId);
                            break;

                        case ProductType.NonConsumable:
                            // If a NonConsumable item has a receipt and is marked as NotOwned then call OnOwnedItemLoaded as it's a newly purchased item
                            IAB.OnOwnedItemLoad(localItem);
                            break;

                        case ProductType.Subscription:
                            // Note: This ProductType is not supported on UDP, but just leaving it here for now
                            // If a Subscription item has a receipt and is marked as NotOwned then call OnOwnedItemLoaded as it's a newly purchased subscription
                            IAB.OnOwnedItemLoad(localItem);
                            break;
                    }
                }
            } else {
                if (FirebaseManager.instance.debugMode)
                    Debug.Log("Tried to load product " + productInfo.ProductId + ", but it was not setup within the app!");
            }
        }
        
        IAB.itemDataReady = true;
        
        IAB.OnInventoryReady();
    }

    public void OnQueryInventoryFailed(string message) {
        Debug.LogError("Failed to query store inventory - " + message);
    }

    public void OnPurchasePending(string message, PurchaseInfo purchaseInfo) {
        IAB.OnPurchaseDeferred(IAB.GetItemBySKU(purchaseInfo.ProductId));
    }
    
    public void PurchaseItem(string productId, bool skipInitializingCheck) {
        IAB.isPurchaseActive = true;

        IABItem localItem = IAB.GetItemByProductId(productId);
        
        // Check if the item is setup within the app (internet connection not required)
        if (localItem != null) {
            // Store reference to the last product we attempted to purchase incase it's needed
            IAB.lastPurchaseAttemptProductId = Application.isEditor ? productId : localItem.productId;

            string sku = IAB.ProductIdToSKU(localItem.productId);

            if (!string.IsNullOrEmpty(sku)) {
                StoreService.Purchase(sku, devPayload, this);
            } else {
                OnPurchaseFailed("Data for this item has not been configured for this store inside the app!", null);
            }
        } else {
            OnPurchaseFailed("Item does not exist in this app version!", null);
        }
    }

    public void ConsumeItem(string productId) {
        // Get the item from our local inventory
        IABItem localItem = IAB.GetItemByProductId(productId);
        
        if (localItem != null) {
            string sku = IAB.ProductIdToSKU(productId);

            if (!string.IsNullOrEmpty(sku)) {
                PurchaseInfo purchaseInfo = cachedInventory?.GetPurchaseInfo(sku);
                
                // If the purchaseInfo is already null then the item isn't owned
                if(purchaseInfo != null)
                    StoreService.ConsumePurchase(purchaseInfo, this);
                
                // Change the local inventory purchase state of the item
                localItem.purchaseState = PurchaseState.NotOwned;
            }
        } else {
            Debug.LogError("Failed to consume! Invalid localItem for productId: " + productId);
        }
    }
    
    public void ConsumeItem(string productId, PurchaseInfo purchaseInfo) {
        // Get the item from our local inventory
        IABItem localItem = IAB.GetItemByProductId(productId);
        
        if (localItem != null) {
            string sku = IAB.ProductIdToSKU(productId);

            if (!string.IsNullOrEmpty(sku)) {
                if(purchaseInfo != null)
                    StoreService.ConsumePurchase(purchaseInfo, this);
                
                // Change the local inventory purchase state of the item
                localItem.purchaseState = PurchaseState.NotOwned;
            }
        } else {
            Debug.LogError("Failed to consume! Invalid localItem for productId: " + productId);
        }
    }

    public void RestorePurchases() {
        StoreService.QueryInventory(this);
    }
    
    // Raw receipts for subscriptions not supported for UDP
    public bool HasRawReceipt(string productId) {
        return false;
    }

    public string GetRawReceipt(string productId) {
        return null;
    }

    public string GetAppleAppReceipt() {
        return "";
    }
    
    public IPurchaseReceipt GetPurchaseReceipt(string productId) {
        return null;
    }
}
