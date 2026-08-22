using UnityEngine;

/// <summary>
/// Tier 3 conversation backend. Two implementations exist:
///
///   OllamaClient   — HTTP to a local Ollama server. Needs Ollama installed.
///   LLMUnityChat   — llama.cpp in-process via LLMUnity. Ships with the game.
///
/// CommandRouter talks to whichever registered with the highest priority, so you can
/// keep both in the scene and switch by enabling/disabling a component. Neither is
/// required — with no backend at all, Tier 0 commands and Tier 2 barks still work.
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

    /// <summary>Higher priority replaces lower. LLMUnity uses 10, Ollama uses 0.</summary>
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
