using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Speaks authored crew lines instantly — no model, no network, no waiting.
///
/// Lives on the Services prefab with RadioMic and OpenAIClient.
///
/// Optional voice audio: drop clips under a Resources folder using the convention
///   Resources/CrewVoice/&lt;Crew&gt;/&lt;Action&gt;_&lt;n&gt;.wav
/// for example  Resources/CrewVoice/ChiefEngineer/EngineSlow_0.wav
/// and they play automatically alongside the subtitle. Missing clips are cached as
/// absent after the first lookup, so text-only costs nothing.
/// </summary>
public class CrewVoice : MonoBehaviour
{
    public static CrewVoice Instance;
    public event System.Action<string, string> OnSubtitleUpdated;

    [Header("Audio (optional — text-only works fine without this)")]
    [Tooltip("AudioSource used for crew radio voice. Leave empty for subtitles only.")]
    [FormerlySerializedAs("radioSource")] [SerializeField] private AudioSource RadioSource;

    [Tooltip("Try to load pre-rendered voice clips from Resources/CrewVoice/...")]
    [FormerlySerializedAs("useVoiceClips")] [SerializeField] private bool UseVoiceClips = false;

    [Header("Debug")]
    [FormerlySerializedAs("logBarks")] [SerializeField] private bool LogBarks = true;

    // Last line index used per bark group, so the same line never repeats twice running.
    private readonly Dictionary<string, int> _lastIndex = new Dictionary<string, int>();

    // Cache of clip lookups, including misses (stored as null) to avoid repeat disk hits.
    private readonly Dictionary<string, AudioClip> _clipCache = new Dictionary<string, AudioClip>();

    void Awake() { Instance = this; }

    public static string DisplayName(RadioChannel crew)
    {
        return crew == RadioChannel.Lookout ? "LOOKOUT FINN" : "CHIEF HARRIS";
    }

    /// <summary>
    /// Respond to a recognised action. Returns the spoken line, or null if the
    /// library had nothing authored for this combination.
    /// </summary>
    public string Speak(RadioChannel crew, ShipAction action, ShipTelemetry telemetry)
    {
        var tension = CrewBarkLibrary.CurrentTension(telemetry);
        var lines = CrewBarkLibrary.Lines(crew, action, tension, telemetry);

        if (lines == null || lines.Length == 0) return null;

        string key = crew + ":" + action + ":" + tension;
        return SpeakFrom(crew, lines, telemetry, key, action);
    }

    /// <summary>Speak one line from an explicit set (alerts, refusals, confusion).</summary>
    public string SpeakFrom(RadioChannel crew, string[] lines, ShipTelemetry telemetry, string groupKey, ShipAction action = ShipAction.None)
    {
        if (lines == null || lines.Length == 0) return null;

        int index = PickIndex(groupKey, lines.Length);
        string line = CrewBarkLibrary.Fill(lines[index], telemetry);

        OnSubtitleUpdated?.Invoke(line, DisplayName(crew));
        ResearchLog.Replied(DisplayName(crew), line);

        if (UseVoiceClips) TryPlayClip(crew, action, index);

        if (LogBarks)
        {
            Debug.Log("[BARK " + DisplayName(crew) + "]: " + line);
        }

        return line;
    }

    public string SayAgain(RadioChannel crew)
    {
        var t = ShipTelemetry.Capture();
        return SpeakFrom(crew, CrewBarkLibrary.SayAgain(crew), t, crew + ":sayagain");
    }

    public string Redirect(RadioChannel crew)
    {
        var t = ShipTelemetry.Capture();
        return SpeakFrom(crew, CrewBarkLibrary.Redirect(crew), t, crew + ":redirect");
    }

    public string NameOnly(RadioChannel crew)
    {
        var t = ShipTelemetry.Capture();
        return SpeakFrom(crew, CrewBarkLibrary.NameOnly(crew), t, crew + ":nameonly");
    }

    /// <summary>Pick an index that isn't the one used last time for this group.</summary>
    private int PickIndex(string groupKey, int count)
    {
        if (count <= 1) return 0;

        int last;
        if (!_lastIndex.TryGetValue(groupKey, out last)) last = -1;

        int index = Random.Range(0, count);
        if (index == last)
        {
            // Single deterministic step avoids an unbounded retry loop.
            index = (index + 1) % count;
        }

        _lastIndex[groupKey] = index;
        return index;
    }

    private void TryPlayClip(RadioChannel crew, ShipAction action, int index)
    {
        if (RadioSource == null || action == ShipAction.None) return;

        string path = "CrewVoice/" + crew + "/" + action + "_" + index;

        AudioClip clip;
        if (!_clipCache.TryGetValue(path, out clip))
        {
            clip = Resources.Load<AudioClip>(path);
            _clipCache[path] = clip; // caches misses as null too
        }

        if (clip != null) RadioSource.PlayOneShot(clip);
    }
}
