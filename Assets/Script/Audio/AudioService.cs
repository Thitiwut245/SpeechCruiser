using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

public enum AudioBus { Music, Effects, Radio }

/// <summary>
/// All game audio that outlives a scene: one-shot effects, the two-layer music, the radio
/// (squelch when the player keys the mic, chatter and a roger beep when the crew answers)
/// and button clicks. Lives on the persistent Services prefab. Ship sounds that belong to the
/// Game scene (engine, wash, hits) are in ShipAudio and play through this service.
/// Every sound runs on real time, so radio slow-mo and the pause menu never stretch it.
/// </summary>
public class AudioService : Singleton<AudioService>
{
    [SerializeField] private SfxLibrary Library;
    [SerializeField] private int OneShotVoices = 12;

    [Header("Music")]
    [SerializeField] private float CalmVolume = 0.4f;
    [SerializeField] private float TensionVolume = 0.55f;
    [Tooltip("Music level while a crew member is speaking, so the words stay clear.")]
    [SerializeField] private float DuckedLevel = 0.45f;

    [Header("Radio chatter")]
    [SerializeField] private float HarrisPitch = 0.82f;
    [SerializeField] private float FinnPitch = 1.15f;
    [SerializeField] private float ChatterVolume = 0.55f;
    [SerializeField] private float StaticVolume = 0.12f;
    [Tooltip("Long lines stop babbling after this; the subtitle carries the rest.")]
    [SerializeField] private float MaxChatterSeconds = 3.2f;

    public SfxLibrary Sfx => Library;
    public bool IsChattering => _chatterActive;

    private readonly List<AudioSource> _voices = new List<AudioSource>();
    private readonly HashSet<VisualElement> _clickPanels = new HashSet<VisualElement>();
    private int _nextVoice;

    private AudioSource _calm;
    private AudioSource _tension;
    private AudioSource _chatter;
    private AudioSource _static;

    private float _tensionTarget;
    private float _tensionLevel;
    private float _duck = 1f;
    private float _musicFade;
    private float _stingUntil;

    private RadioMic _mic;
    private CrewVoice _crew;
    private OpenAIClient _openAI;
    private bool _keyed;
    private bool _chatterActive;
    private Coroutine _chatterRoutine;
    private int _lastSyllable = -1;

    /// <summary>The player's volume for a bus (0..1), or 1 before settings exist.</summary>
    public static float Volume(AudioBus bus)
    {
        return SettingsService.Instance != null ? SettingsService.Instance.GetBusVolume(bus) : 1f;
    }

    protected override void Awake()
    {
        base.Awake();
        if (Instance != this) return;

        var holder = new GameObject("Audio");
        holder.transform.SetParent(transform, false);

        for (int i = 0; i < OneShotVoices; i++) _voices.Add(NewSource(holder, false));
        _calm = NewSource(holder, true);
        _tension = NewSource(holder, true);
        _chatter = NewSource(holder, false);
        _static = NewSource(holder, true);
    }

    private static AudioSource NewSource(GameObject holder, bool loop)
    {
        var s = holder.AddComponent<AudioSource>();
        s.playOnAwake = false;
        s.loop = loop;
        s.spatialBlend = 0f;
        s.ignoreListenerPause = true;
        return s;
    }

    private void Start()
    {
        if (Instance != this) return;

        _mic = GetComponent<RadioMic>();
        _crew = GetComponent<CrewVoice>();
        _openAI = GetComponent<OpenAIClient>();
        if (_mic != null) _mic.OnRadioStatus += HandleRadioStatus;
        if (_crew != null) _crew.OnSubtitleUpdated += HandleCrewLine;
        if (_openAI != null) _openAI.OnSubtitleUpdated += HandleCrewLine;

        SceneManager.sceneLoaded += HandleSceneLoaded;
        RegisterButtonClicks();
        StartMusic();
    }

    protected override void OnDestroy()
    {
        if (Instance == this)
        {
            if (_mic != null) _mic.OnRadioStatus -= HandleRadioStatus;
            if (_crew != null) _crew.OnSubtitleUpdated -= HandleCrewLine;
            if (_openAI != null) _openAI.OnSubtitleUpdated -= HandleCrewLine;
            SceneManager.sceneLoaded -= HandleSceneLoaded;
        }
        base.OnDestroy();
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        _tensionTarget = 0f;
        RegisterButtonClicks();
    }

    // ------------------------------------------------------------ one-shots

    public void Play(AudioClip clip, AudioBus bus, float volume = 1f, float pitch = 1f, float delay = 0f)
    {
        if (clip == null) return;

        var src = NextVoice();
        src.clip = clip;
        src.volume = volume * Volume(bus);
        src.pitch = pitch;
        if (delay > 0f) src.PlayDelayed(delay);
        else src.Play();
    }

    private AudioSource NextVoice()
    {
        for (int i = 0; i < _voices.Count; i++)
        {
            var candidate = _voices[(_nextVoice + i) % _voices.Count];
            if (!candidate.isPlaying)
            {
                _nextVoice = (_nextVoice + i + 1) % _voices.Count;
                return candidate;
            }
        }
        // All busy: steal the next in turn.
        var stolen = _voices[_nextVoice];
        _nextVoice = (_nextVoice + 1) % _voices.Count;
        stolen.Stop();
        return stolen;
    }

    // ------------------------------------------------------------ music

    private void StartMusic()
    {
        if (Library == null || Library.MusicCalm == null) return;

        _calm.clip = Library.MusicCalm;
        _tension.clip = Library.MusicTension;
        _calm.volume = 0f;
        _tension.volume = 0f;

        // Same start time, same length: the layers stay locked together forever.
        double at = AudioSettings.dspTime + 0.1;
        _calm.PlayScheduled(at);
        if (_tension.clip != null) _tension.PlayScheduled(at);
    }

    /// <summary>0 = calm, 1 = hazard about to hit. ShipAudio drives this from the radar.</summary>
    public void SetTension(float tension)
    {
        _tensionTarget = Mathf.Clamp01(tension);
    }

    /// <summary>Pull the music down while a result sting plays.</summary>
    public void DuckMusicFor(float seconds)
    {
        _stingUntil = Time.realtimeSinceStartup + seconds;
    }

    private void Update()
    {
        float dt = Time.unscaledDeltaTime;

        // Rise quickly when danger appears, relax slowly when it passes.
        float rate = _tensionTarget > _tensionLevel ? 1.2f : 0.3f;
        _tensionLevel = Mathf.MoveTowards(_tensionLevel, _tensionTarget, rate * dt);
        _duck = Mathf.MoveTowards(_duck, _chatterActive ? DuckedLevel : 1f, 3f * dt);
        float fadeTarget = Time.realtimeSinceStartup < _stingUntil ? 0.2f : 1f;
        _musicFade = Mathf.MoveTowards(_musicFade, fadeTarget, 0.8f * dt);

        float music = Volume(AudioBus.Music) * _duck * _musicFade;
        _calm.volume = music * CalmVolume * (1f - 0.4f * _tensionLevel);
        _tension.volume = music * TensionVolume * _tensionLevel;
        _static.volume = _chatterActive ? StaticVolume * Volume(AudioBus.Radio) : 0f;
    }

    // ------------------------------------------------------------ radio

    private void HandleRadioStatus(RadioStatus status, string text)
    {
        if (Library == null) return;

        if (status == RadioStatus.Recording)
        {
            StopChatter();
            Play(Library.RadioKeyOn, AudioBus.Radio, 0.8f);
            _keyed = true;
            return;
        }

        // The first status after recording means the key was released (or the message was cut).
        if (_keyed)
        {
            _keyed = false;
            Play(Library.RadioKeyOff, AudioBus.Radio, 0.8f);
        }

        switch (status)
        {
            case RadioStatus.Busy:
            case RadioStatus.NoMic:
            case RadioStatus.ModelNotReady:
            case RadioStatus.NotAtSea:
                Play(Library.RadioBusy, AudioBus.Radio, 0.55f);
                break;
            case RadioStatus.Tested:
                Play(Library.RadioRoger, AudioBus.Radio, 0.7f);
                break;
            case RadioStatus.TooShort:
            case RadioStatus.TooQuiet:
            case RadioStatus.NoAudio:
            case RadioStatus.NotUnderstood:
            case RadioStatus.Failed:
                Play(Library.RadioError, AudioBus.Radio, 0.5f, 1f, 0.3f);
                break;
        }
    }

    private void HandleCrewLine(string line, string speaker)
    {
        if (Library == null || string.IsNullOrWhiteSpace(line) || line == "...") return;
        if (_keyed) return; // the player is transmitting; the line still shows as text

        StopChatter();
        _chatterRoutine = StartCoroutine(Chatter(line, speaker));
    }

    private void StopChatter()
    {
        if (_chatterRoutine != null) StopCoroutine(_chatterRoutine);
        _chatterRoutine = null;
        _chatterActive = false;
        _chatter.Stop();
        _static.Stop();
    }

    /// <summary>Muffled radio "voice": one syllable per vowel group, pauses at punctuation.</summary>
    private IEnumerator Chatter(string line, string speaker)
    {
        _chatterActive = true;
        float basePitch = speaker != null && speaker.ToUpperInvariant().Contains("FINN") ? FinnPitch : HarrisPitch;
        bool question = line.TrimEnd().EndsWith("?");

        Play(Library.RadioKeyOn, AudioBus.Radio, 0.45f);
        if (Library.RadioStaticLoop != null)
        {
            _static.clip = Library.RadioStaticLoop;
            _static.volume = StaticVolume * Volume(AudioBus.Radio);
            _static.Play();
        }
        yield return new WaitForSecondsRealtime(0.1f);

        var syllables = Library.VoiceSyllables;
        if (syllables != null && syllables.Length > 0)
        {
            float stopAt = Time.realtimeSinceStartup + MaxChatterSeconds;
            string[] words = line.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);

            for (int w = 0; w < words.Length && Time.realtimeSinceStartup < stopAt; w++)
            {
                string word = words[w];
                int count = CountSyllables(word);
                bool lastWord = w == words.Length - 1;

                for (int s = 0; s < count && Time.realtimeSinceStartup < stopAt; s++)
                {
                    var clip = PickSyllable(syllables);
                    float pitch = basePitch * Random.Range(0.93f, 1.07f);
                    if (question && lastWord) pitch *= 1.12f + 0.06f * s;
                    _chatter.pitch = pitch;
                    _chatter.PlayOneShot(clip, ChatterVolume * Volume(AudioBus.Radio));
                    yield return new WaitForSecondsRealtime(clip.length / pitch * 0.8f);
                }

                char end = word[word.Length - 1];
                float gap = end == ',' || end == ';' || end == ':' ? 0.14f
                          : end == '.' || end == '!' || end == '?' ? 0.24f
                          : 0.035f;
                yield return new WaitForSecondsRealtime(gap);
            }
        }

        _static.Stop();
        _chatterActive = false;
        Play(Library.RadioRoger, AudioBus.Radio, 0.4f);
        Play(Library.RadioKeyOff, AudioBus.Radio, 0.45f, 1f, 0.17f);
        _chatterRoutine = null;
    }

    private AudioClip PickSyllable(AudioClip[] syllables)
    {
        int i = Random.Range(0, syllables.Length);
        if (i == _lastSyllable && syllables.Length > 1) i = (i + 1) % syllables.Length;
        _lastSyllable = i;
        return syllables[i];
    }

    /// <summary>Vowel groups, 1 to 4. Numbers count as two.</summary>
    public static int CountSyllables(string word)
    {
        int groups = 0;
        bool inVowel = false;
        bool hasLetter = false;
        foreach (char raw in word)
        {
            char c = char.ToLowerInvariant(raw);
            if (char.IsDigit(c)) return 2;
            if (!char.IsLetter(c)) continue;
            hasLetter = true;
            bool vowel = "aeiouy".IndexOf(c) >= 0;
            if (vowel && !inVowel) groups++;
            inVowel = vowel;
        }
        if (!hasLetter) return 0;
        return Mathf.Clamp(groups, 1, 4);
    }

    // ------------------------------------------------------------ UI

    /// <summary>A click on any Button in any panel plays the UI click.</summary>
    private void RegisterButtonClicks()
    {
        foreach (var doc in FindObjectsByType<UIDocument>(FindObjectsInactive.Exclude))
        {
            var panelRoot = doc.rootVisualElement?.panel?.visualTree;
            if (panelRoot == null || !_clickPanels.Add(panelRoot)) continue;
            panelRoot.RegisterCallback<ClickEvent>(HandleClick, TrickleDown.TrickleDown);
        }
    }

    private void HandleClick(ClickEvent evt)
    {
        var element = evt.target as VisualElement;
        while (element != null && !(element is Button)) element = element.parent;
        if (element != null && Library != null) Play(Library.UiClick, AudioBus.Effects, 0.6f);
    }
}
