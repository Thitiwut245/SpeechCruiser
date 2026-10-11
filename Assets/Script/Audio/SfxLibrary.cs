using UnityEngine;

/// <summary>
/// Every clip the game plays, in one asset (Assets/Audio/SfxLibrary.asset). The default
/// clips are synthesised by Tools/Audio/generate_audio.py; swap any slot for a recording.
/// </summary>
[CreateAssetMenu(menuName = "SpeechCruiser/Sfx Library", fileName = "SfxLibrary")]
public class SfxLibrary : ScriptableObject
{
    [Header("Radio")]
    public AudioClip RadioKeyOn;
    public AudioClip RadioKeyOff;
    public AudioClip RadioRoger;
    public AudioClip RadioStaticLoop;
    public AudioClip RadioBusy;
    public AudioClip RadioError;

    [Tooltip("Short muffled syllables strung together while a crew line is on screen.")]
    public AudioClip[] VoiceSyllables;

    [Header("Ship")]
    public AudioClip TelegraphBell;
    public AudioClip EngineLoop;
    public AudioClip WashLoop;
    public AudioClip HullCrunch;
    public AudioClip MineExplosion;
    public AudioClip CargoSplash;
    public AudioClip Klaxon;
    public AudioClip ShipHorn;

    [Header("Alerts")]
    public AudioClip AlertWarn;
    public AudioClip AlertClosing;
    public AudioClip AlertBrace;

    [Header("UI and results")]
    public AudioClip UiClick;
    public AudioClip VictorySting;
    public AudioClip FailSting;

    [Header("Music (both loops share one length and tempo so they layer)")]
    public AudioClip MusicCalm;
    public AudioClip MusicTension;
}
