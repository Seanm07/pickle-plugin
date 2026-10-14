
using UnityEngine;
using UnityEngine.Purchasing;
using UnityEngine.Purchasing.Extension;

class AmazonStoreWrapper : IStoreWrapper
{
    private readonly string _mStoreName;
    private readonly AmazonCustomStore _mStoreInstance;
    
    public AmazonStoreWrapper(string storeName, AmazonCustomStore storeInstance)
    {
        _mStoreName = storeName;
        _mStoreInstance = storeInstance;
    }

    
    /// <summary>
    /// Gets the Store Name.
    /// </summary>
    public string name => _mStoreName;

    
    /// <summary>
    /// Gets the Store Instance.
    /// </summary>
    public Store instance => _mStoreInstance;


    /// <summary>
    /// Gets the connection state of the store.
    /// </summary>
    public ConnectionState GetStoreConnectionState()
    {
        return _mStoreInstance.GetStoreConnectionState();
    }
}