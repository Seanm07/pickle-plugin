#if UNITY_ANDROID
using System;
using System.Collections.Generic;
using UnityEngine;

namespace AmazonIap
{
    /// <summary>
    /// C# implementation of com.amazon.device.iap.PurchasingListener.
    /// AndroidJavaProxy dispatches Java interface calls to C# methods by NAME, so the
    /// four method names below must match the Java interface exactly - do not rename them.
    /// </summary>
    internal class AmazonIapListenerProxy : AndroidJavaProxy
    {
        public event Action<AmazonUserDataResult> UserDataResponse;
        public event Action<AmazonProductDataResult> ProductDataResponse;
        public event Action<AmazonPurchaseResult> PurchaseResponse;
        public event Action<AmazonPurchaseUpdatesResult> PurchaseUpdatesResponse;

        public AmazonIapListenerProxy() : base("com.amazon.device.iap.PurchasingListener") { }

        // Invoked on Unity's main thread by the Amazon Appstore SDK.
        public void onUserDataResponse(AndroidJavaObject response)
        {
            using (response)
            {
                UserDataResponse?.Invoke(new AmazonUserDataResult
                {
                    RequestId = GetRequestIdString(response),
                    Status = ParseStatus(response),
                    UserData = ParseUserData(response.Call<AndroidJavaObject>("getUserData"))
                });
            }
        }

        public void onProductDataResponse(AndroidJavaObject response)
        {
            using (response)
            {
                ProductDataResponse?.Invoke(new AmazonProductDataResult
                {
                    RequestId = GetRequestIdString(response),
                    Status = ParseStatus(response),
                    ProductData = ParseProductMap(response.Call<AndroidJavaObject>("getProductData")),
                    UnavailableSkus = JavaStringSetToList(response.Call<AndroidJavaObject>("getUnavailableSkus"))
                });
            }
        }

        public void onPurchaseResponse(AndroidJavaObject response)
        {
            using (response)
            {
                AndroidJavaObject receiptObj = response.Call<AndroidJavaObject>("getReceipt");
                PurchaseResponse?.Invoke(new AmazonPurchaseResult
                {
                    RequestId = GetRequestIdString(response),
                    Status = ParseStatus(response),
                    UserData = ParseUserData(response.Call<AndroidJavaObject>("getUserData")),
                    Receipt = receiptObj != null ? ParseReceipt(receiptObj) : null
                });
            }
        }

        public void onPurchaseUpdatesResponse(AndroidJavaObject response)
        {
            using (response)
            {
                PurchaseUpdatesResponse?.Invoke(new AmazonPurchaseUpdatesResult
                {
                    RequestId = GetRequestIdString(response),
                    Status = ParseStatus(response),
                    UserData = ParseUserData(response.Call<AndroidJavaObject>("getUserData")),
                    Receipts = ParseReceiptList(response.Call<AndroidJavaObject>("getReceipts")),
                    HasMore = response.Call<bool>("hasMore")
                });
            }
        }

        // ---- Parsing helpers -------------------------------------------------

        private static string GetRequestIdString(AndroidJavaObject response)
        {
            using (var requestId = response.Call<AndroidJavaObject>("getRequestId"))
            {
                return requestId?.Call<string>("toString");
            }
        }

        private static AmazonRequestStatus ParseStatus(AndroidJavaObject response)
        {
            using (var statusObj = response.Call<AndroidJavaObject>("getRequestStatus"))
            {
                return (AmazonRequestStatus)Enum.Parse(typeof(AmazonRequestStatus), statusObj.Call<string>("name"));
            }
        }

        private static AmazonUserData ParseUserData(AndroidJavaObject userData)
        {
            if (userData == null) return null;
            using (userData)
            {
                return new AmazonUserData
                {
                    UserId = userData.Call<string>("getUserId"),
                    Marketplace = userData.Call<string>("getMarketplace")
                };
            }
        }

        private static AmazonProductType ParseProductType(AndroidJavaObject owner)
        {
            using (var typeObj = owner.Call<AndroidJavaObject>("getProductType"))
            {
                return (AmazonProductType)Enum.Parse(typeof(AmazonProductType), typeObj.Call<string>("name"));
            }
        }

        private static AmazonProduct ParseProduct(AndroidJavaObject product)
        {
            using (product)
            {
                return new AmazonProduct
                {
                    Sku = product.Call<string>("getSku"),
                    Title = product.Call<string>("getTitle"),
                    Description = product.Call<string>("getDescription"),
                    Price = product.Call<string>("getPrice"),
                    SmallIconUrl = product.Call<string>("getSmallIconUrl"),
                    ProductType = ParseProductType(product)
                };
            }
        }

        private static AmazonReceipt ParseReceipt(AndroidJavaObject receipt)
        {
            using (receipt)
            {
                long purchaseDateMs = 0;
                using (var purchaseDate = receipt.Call<AndroidJavaObject>("getPurchaseDate"))
                {
                    if (purchaseDate != null) purchaseDateMs = purchaseDate.Call<long>("getTime");
                }

                long? cancelDateMs = null;
                using (var cancelDate = receipt.Call<AndroidJavaObject>("getCancelDate"))
                {
                    if (cancelDate != null) cancelDateMs = cancelDate.Call<long>("getTime");
                }

                return new AmazonReceipt
                {
                    ReceiptId = receipt.Call<string>("getReceiptId"),
                    Sku = receipt.Call<string>("getSku"),
                    TermSku = receipt.Call<string>("getTermSku"),
                    ProductType = ParseProductType(receipt),
                    IsCanceled = receipt.Call<bool>("isCanceled"),
                    PurchaseDateEpochMs = purchaseDateMs,
                    CancelDateEpochMs = cancelDateMs
                };
            }
        }

        private static Dictionary<string, AmazonProduct> ParseProductMap(AndroidJavaObject javaMap)
        {
            var result = new Dictionary<string, AmazonProduct>();
            using (javaMap)
            using (var keySet = javaMap.Call<AndroidJavaObject>("keySet"))
            using (var iterator = keySet.Call<AndroidJavaObject>("iterator"))
            {
                while (iterator.Call<bool>("hasNext"))
                {
                    string sku = iterator.Call<string>("next");
                    using (var productObj = javaMap.Call<AndroidJavaObject>("get", sku))
                    {
                        result[sku] = ParseProduct(productObj);
                    }
                }
            }
            return result;
        }

        private static List<string> JavaStringSetToList(AndroidJavaObject javaSet)
        {
            var result = new List<string>();
            using (javaSet)
            using (var iterator = javaSet.Call<AndroidJavaObject>("iterator"))
            {
                while (iterator.Call<bool>("hasNext"))
                {
                    result.Add(iterator.Call<string>("next"));
                }
            }
            return result;
        }

        private static List<AmazonReceipt> ParseReceiptList(AndroidJavaObject javaList)
        {
            var result = new List<AmazonReceipt>();
            using (javaList)
            {
                int size = javaList.Call<int>("size");
                for (int i = 0; i < size; i++)
                {
                    result.Add(ParseReceipt(javaList.Call<AndroidJavaObject>("get", i)));
                }
            }
            return result;
        }
    }
}
#endif
