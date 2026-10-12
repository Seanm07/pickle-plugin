using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;

public class UnityMainThreadDispatcher : MonoBehaviour
{
    private static readonly Queue<Action> executionQueue = new Queue<Action>();

    public static UnityMainThreadDispatcher instance;

    // Bootstrap before any scene loads so `instance` is guaranteed non-null before any
    // background-thread callback (e.g. the Amazon SDK) tries to Enqueue() onto it.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (instance != null) return;

        var go = new GameObject("UnityMainThreadDispatcher");
        instance = go.AddComponent<UnityMainThreadDispatcher>();
        DontDestroyOnLoad(go);
    }

    void Awake()
    {
        instance = instance ?? this;
    }
    
    void Update()
    {
        lock (executionQueue)
        {
            while (executionQueue.Count > 0)
            {
                executionQueue.Dequeue().Invoke();
            }
        }
    }
    
    private IEnumerator ActionWrapper(Action action)
    {
        action();

        yield return null;
    }

    public void Enqueue(IEnumerator action)
    {
        lock (executionQueue)
        {
            executionQueue.Enqueue(() =>
            {
                StartCoroutine(action);
            });
        }
    }

    public void Enqueue(Action action)
    {
        Enqueue(ActionWrapper(action));
    }
}
