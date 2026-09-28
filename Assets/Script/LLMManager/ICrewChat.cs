using UnityEngine;

/// <summary>
/// Tier 3 conversation backend. The game ships one: OpenAIClient (cloud chat).
///
/// CommandRouter talks to whichever backend registered with the highest priority, so a
/// local model could be added later without touching the router. None is required: with
/// no backend at all, Tier 0 commands and Tier 2 barks still work.
/// </summary>
public interface ICrewChat
{
    /// <summary>False while the model is still loading or the backend is unusable.</summary>
    bool Ready { get; }

    /// <summary>True while a reply is being generated.</summary>
    bool IsBusy { get; }

    void Converse(RadioChannel crew, string playerSpeech);

    void ResetMemory();
}

/// <summary>
/// Tiny service locator for the active conversation backend. Backends register
/// themselves in Awake; highest priority wins.
/// </summary>
public static class CrewChat
{
    private static ICrewChat _active;
    private static int _activePriority = int.MinValue;

    public static ICrewChat Active { get { return _active; } }

    /// <summary>True only if a backend exists AND is ready to answer.</summary>
    public static bool Available { get { return _active != null && _active.Ready; } }

    /// <summary>Higher priority replaces lower. OpenAIClient uses 0.</summary>
    public static void Register(ICrewChat backend, int priority, string label)
    {
        if (backend == null) return;

        if (_active != null && priority <= _activePriority)
        {
            Debug.Log("[CREW CHAT]: keeping existing backend; " + label +
                      " (priority " + priority + ") stays idle.");
            return;
        }

        _active = backend;
        _activePriority = priority;
        Debug.Log("[CREW CHAT]: conversation backend is now " + label + ".");
    }

    public static void Unregister(ICrewChat backend)
    {
        if (!ReferenceEquals(_active, backend)) return;

        _active = null;
        _activePriority = int.MinValue;
    }
}
