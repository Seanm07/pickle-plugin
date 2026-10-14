using System;
using System.Collections.Generic;

namespace AmazonIap
{
    /// <summary>Mirrors com.amazon.device.iap.model.ProductType</summary>
    public enum AmazonProductType
    {
        CONSUMABLE,
        ENTITLED,
        SUBSCRIPTION
    }

    /// <summary>Mirrors com.amazon.device.iap.model.FulfillmentResult</summary>
    public enum AmazonFulfillmentResult
    {
        FULFILLED,
        UNAVAILABLE,
        NOT_ELIGIBLE,
        EXISTING_PURCHASE
    }

    /// <summary>
    /// Union of every RequestStatus value across UserDataResponse, ProductDataResponse,
    /// PurchaseResponse and PurchaseUpdatesResponse. Each response only ever populates the
    /// subset documented for that call (e.g. PENDING/INVALID_SKU only come from purchase()),
    /// but sharing one enum keeps the bridge simple.
    /// </summary>
    public enum AmazonRequestStatus
    {
        SUCCESSFUL,
        FAILED,
        NOT_SUPPORTED,
        PENDING,
        ALREADY_PURCHASED,
        INVALID_SKU,
        INACTIVE_BASE_SUBSCRIPTION
    }

    [Serializable]
    public class AmazonUserData
    {
        public string UserId;
        public string Marketplace;
    }

    [Serializable]
    public class AmazonProduct
    {
        public string Sku;
        public string Title;
        public string Description;
        public string Price;
        public string SmallIconUrl;
        public AmazonProductType ProductType;
    }

    [Serializable]
    public class AmazonReceipt
    {
        public string ReceiptId;
        public string Sku;
        public string TermSku;
        public AmazonProductType ProductType;
        public bool IsCanceled;
        /// <summary>Unix epoch milliseconds. Mirrors Receipt.getPurchaseDate().getTime().</summary>
        public long PurchaseDateEpochMs;
        /// <summary>Unix epoch milliseconds, or null if the receipt isn't canceled. Mirrors Receipt.getCancelDate().</summary>
        public long? CancelDateEpochMs;
    }

    public class AmazonUserDataResult
    {
        public string RequestId;
        public AmazonRequestStatus Status;
        public AmazonUserData UserData;
    }

    public class AmazonProductDataResult
    {
        public string RequestId;
        public AmazonRequestStatus Status;
        public Dictionary<string, AmazonProduct> ProductData;
        public List<string> UnavailableSkus;
    }

    public class AmazonPurchaseResult
    {
        public string RequestId;
        public AmazonRequestStatus Status;
        public AmazonUserData UserData;
        /// <summary>Only populated when Status == SUCCESSFUL.</summary>
        public AmazonReceipt Receipt;
    }

    public class AmazonPurchaseUpdatesResult
    {
        public string RequestId;
        public AmazonRequestStatus Status;
        public AmazonUserData UserData;
        public List<AmazonReceipt> Receipts;
        /// <summary>If true, call AmazonIapBridge.GetPurchaseUpdates(false) again for the next page.</summary>
        public bool HasMore;
    }
}
