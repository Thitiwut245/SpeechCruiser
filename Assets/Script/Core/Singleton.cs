using UnityEngine;

/// <summary>
/// Single-instance MonoBehaviour. A second copy removes itself; the static Instance is
/// cleared when the owner is destroyed so no one holds a dead reference across scene loads.
/// Persistence is not handled here: objects on the Services prefab persist via Bootstrapper.
/// </summary>
public abstract class Singleton<T> : MonoBehaviour where T : Singleton<T>
{
    public static T Instance { get; private set; }

    protected virtual void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning($"[{typeof(T).Name}]: duplicate on '{name}' removed.");
            Destroy(this);
            return;
        }
        Instance = (T)this;
    }

    protected virtual void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
