using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public interface IPurchaseServiceCustom : IDisposable
{
    event Action<string> OnPurchaseDeferred;
    event Action<GenericPurchaseResult> OnPurchaseSucceeded;
    event Action<string, string> OnPurchaseFailed;
    event Action<string, string, long, long> OnPurchaseUpdated;
    event Action<List<GenericPurchaseResult>> OnPurchasesFetched;
    event Action<string> OnPurchasesFetchFailed;

    void Init();
    void FetchPurchases(bool reset = false);
    Task<bool> ConnectAsync(TimeSpan timeout, CancellationToken cancellationToken = default);
    void Disconnect();
    void Purchase(string productId);
    void NotifyFulfillment(string receiptId);
}
