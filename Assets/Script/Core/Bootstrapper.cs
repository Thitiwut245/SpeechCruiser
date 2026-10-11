using UnityEngine;

/// <summary>
/// Spawns the persistent Services prefab (Whisper, RadioMic, LLM, CrewVoice, TimeController, GameSession,
/// SceneLoader) before the first scene loads, so pressing Play in any scene works.
/// </summary>
public static class Bootstrapper
{
    private const string ServicesResourcePath = "Services";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void SpawnServices()
    {
        // The Log* toggles (routing, barks, audio stats, requests) are for the editor and
        // development builds. A release build keeps only warnings and errors.
        if (!Debug.isDebugBuild) Debug.unityLogger.filterLogType = LogType.Warning;

        GameObject prefab = Resources.Load<GameObject>(ServicesResourcePath);
        if (prefab == null)
        {
            Debug.LogError("[BOOT]: Assets/Resources/Services.prefab is missing. " +
                           "Run Tools > SpeechCruiser > Build Services Prefab.");
            return;
        }

        GameObject services = Object.Instantiate(prefab);
        services.name = prefab.name;
        Object.DontDestroyOnLoad(services);
    }
}
