using UnityEngine.Purchasing.Security;

public interface IABHandler {
    void Init(IABManager manager);

    void InitializePurchasing();

    void PurchaseItem(string productId, bool skipInitializingCheck = false);

    void ConsumeAllItems();

    void RestorePurchases();

    IPurchaseReceipt GetPurchaseReceipt(string productId);
    
    bool HasRawReceipt(string productId);
    string GetRawReceipt(string productId);

    string GetAppleAppReceipt();
}
