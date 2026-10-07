using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

public enum AudioBus { Music, Effects, Radio }

/// <summary>
/// All game audio that outlives a scene: one-shot effects, the two-layer music, the radio
/// (squelch when the player keys the mic, the crew's spoken lines and a roger beep when they
/// answer) and button clicks. Crew lines are spoken with Windows' voices through CrewSpeech,
/// Finn and Harris each with their own; where that isn't available they fall back to babble. Lives on the persistent Services prefab. Ship sounds that belong to the
/// Game scene (engine, wash, hits) are in ShipAudio and play through this service. The sea
/// itself (surf, lapping, gulls) is SeaAmbience, kept here so it carries on across scenes;
/// ShipAudio and the story weather set its mood.
/// The radio's beeps are kept soft: they mark a transmission, they shouldn't nag.
/// Every sound runs on real time, so radio slow-mo and the pause menu never stretch it.
/// </summary>
public class AudioService : Singleton<AudioService>
{
    [SerializeField] private SfxLibrary Library;
    [SerializeField] private int OneShotVoices = 12;

    [Header("Music")]
    [SerializeField] private float CalmVolume = 0.28f;
    [SerializeField] private float TensionVolume = 0.4f;
    [Tooltip("Music level while a crew member is speaking, so the words stay clear.")]
    [SerializeField] private float DuckedLevel = 0.45f;

    [Header("Spoken crew (Windows voices)")]
    [Tooltip("Speak crew lines in English. Off, or where no voice is installed, the crew babble instead.")]
    [SerializeField] private bool SpokenVoices = true;
    [Tooltip("Part of the Windows voice name for Finn, the young lookout.")]
    [SerializeField] private string FinnVoice = "Mark";
    [SerializeField] private float FinnRate = 1.08f;
    [SerializeField] private float FinnVoicePitch = 1.06f;
    [Tooltip("Part of the Windows voice name for Harris, the older chief engineer.")]
    [SerializeField] private string HarrisVoice = "George";
    [SerializeField] private float HarrisRate = 0.94f;
    [SerializeField] private float HarrisVoicePitch = 0.9f;
    [SerializeField] private float SpokenVolume = 0.95f;
    [Tooltip("A line not ready after this many seconds is babbled instead, so the radio never hangs.")]
    [SerializeField] private float SpeechTimeout = 1.5f;

    [Header("Radio chatter")]
    [SerializeField] private float HarrisPitch = 0.82f;
    [SerializeField] private float FinnPitch = 1.15f;
    [SerializeField] private float ChatterVolume = 0.55f;
    [SerializeField] private float StaticVolume = 0.05f;
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

    private CrewSpeech _speech;
    private AudioSource _voice;
    private int _speechId;
    private AudioClip _speechClip;
    private int _lastSyllable = -1;

    // Story dialogue: spoken face to face, so no radio filter, no static, no beeps.
    private AudioSource _face;
    private int _faceId = FaceIdBase;
    private bool _faceWaiting;
    private const int FaceIdBase = 1000000;

    // The sea: a mood (how loud, how rough, how many gulls) that the sound eases toward.
    private SeaAmbience _sea;
    private float _seaLevel = 0.55f, _seaSurf = 0.15f, _seaGulls = 0.8f;
    private float _seaLevelNow, _seaSurfNow = 0.15f, _seaGullsNow = 0.8f;

    /// <summary>Weather on the current story mission; it overrides the sea's mood (Clear = none).</summary>
    public Weather SeaWeather { get; set; }

    /// <summary>The harbour: quiet lapping and plenty of gulls (the title screen and the shipyard).</summary>
    public void SetHarbourMood() => SetSeaMood(0.55f, 0.15f, 0.8f);

    /// <summary>How loud the sea is (0..1), how rough (0..1) and how often gulls cry (0..1).</summary>
    public void SetSeaMood(float level, float surf, float gulls)
    {
        _seaLevel = level;
        _seaSurf = surf;
        _seaGulls = gulls;
    }

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

        // The spoken crew sound like a radio: a band-pass on their own object, so the
        // filters don't touch music or effects.
        var radio = new GameObject("CrewRadioVoice");
        radio.transform.SetParent(transform, false);
        _voice = NewSource(radio, false);
        radio.AddComponent<AudioHighPassFilter>().cutoffFrequency = 320f;
        radio.AddComponent<AudioLowPassFilter>().cutoffFrequency = 4200f;

        var face = new GameObject("DialogueVoice");
        face.transform.SetParent(transform, false);
        _face = NewSource(face, false);

        _sea = SeaAmbience.Create(transform);

        _speech = new CrewSpeech(System.IO.Path.Combine(Application.temporaryCachePath, "crewvoice"));
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
        if (SpokenVoices) _speech.Start();
    }

    private void OnApplicationQuit()
    {
        if (Instance == this) _speech?.Dispose();
    }

    protected override void OnDestroy()
    {
        if (Instance == this)
        {
            if (_mic != null) _mic.OnRadioStatus -= HandleRadioStatus;
            if (_crew != null) _crew.OnSubtitleUpdated -= HandleCrewLine;
            if (_openAI != null) _openAI.OnSubtitleUpdated -= HandleCrewLine;
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            _speech?.Dispose();
        }
        base.OnDestroy();
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        _tensionTarget = 0f;
        SeaWeather = Weather.Clear;
        SetHarbourMood();
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

        // Spoken lines arrive from the voice helper; only the newest request is kept.
        while (_speech != null && _speech.Poll(out var spoken))
        {
            if (spoken.Id == _speechId) _speechClip = spoken.Clip;
            else if (spoken.Id == _faceId && _faceWaiting) PlayFace(spoken.Clip);
            else Destroy(spoken.Clip);
        }

        // Rise quickly when danger appears, relax slowly when it passes.
        float rate = _tensionTarget > _tensionLevel ? 1.2f : 0.3f;
        _tensionLevel = Mathf.MoveTowards(_tensionLevel, _tensionTarget, rate * dt);
        _duck = Mathf.MoveTowards(_duck, _chatterActive || FaceSpeaking ? DuckedLevel : 1f, 3f * dt);
        float fadeTarget = Time.realtimeSinceStartup < _stingUntil ? 0.2f : 1f;
        _musicFade = Mathf.MoveTowards(_musicFade, fadeTarget, 0.8f * dt);

        float music = Volume(AudioBus.Music) * _duck * _musicFade;
        _calm.volume = music * CalmVolume * (1f - 0.4f * _tensionLevel);
        _tension.volume = music * TensionVolume * _tensionLevel;
        _static.volume = _chatterActive ? StaticVolume * Volume(AudioBus.Radio) : 0f;

        UpdateSea(dt);
    }

    private void UpdateSea(float dt)
    {
        if (_sea == null) return;
        float level = _seaLevel, surf = _seaSurf, gulls = _seaGulls;
        switch (SeaWeather)
        {
            case Weather.Storm: level = Mathf.Max(level, 0.85f); surf = 1f; gulls = 0f; break;
            case Weather.Fog:
            case Weather.NightFog: surf = Mathf.Min(surf, 0.25f); gulls = 0f; break;
        }
        // Ease between moods over a few seconds, the way weather changes.
        _seaLevelNow = Mathf.MoveTowards(_seaLevelNow, level, 0.25f * dt);
        _seaSurfNow = Mathf.MoveTowards(_seaSurfNow, surf, 0.2f * dt);
        _seaGullsNow = Mathf.MoveTowards(_seaGullsNow, gulls, 0.3f * dt);
        // The sea sits under the music when a crew member speaks, like everything else.
        _sea.Level = _seaLevelNow * Volume(AudioBus.Effects) * Mathf.Lerp(0.6f, 1f, _duck);
        _sea.Surf = _seaSurfNow;
        _sea.Gulls = _seaGullsNow;
    }

    // ------------------------------------------------------------ radio

    private void HandleRadioStatus(RadioStatus status, string text)
    {
        if (Library == null) return;

        if (status == RadioStatus.Recording)
        {
            StopChatter();
            Play(Library.RadioKeyOn, AudioBus.Radio, 0.35f);
            _keyed = true;
            return;
        }

        // The first status after recording means the key was released (or the message was cut).
        if (_keyed)
        {
            _keyed = false;
            Play(Library.RadioKeyOff, AudioBus.Radio, 0.3f);
        }

        switch (status)
        {
            case RadioStatus.Busy:
            case RadioStatus.NoMic:
            case RadioStatus.ModelNotReady:
            case RadioStatus.NotAtSea:
                Play(Library.RadioBusy, AudioBus.Radio, 0.3f);
                break;
            case RadioStatus.Tested:
                Play(Library.RadioRoger, AudioBus.Radio, 0.35f);
                break;
            case RadioStatus.TooShort:
            case RadioStatus.TooQuiet:
            case RadioStatus.NoAudio:
            case RadioStatus.NotUnderstood:
            case RadioStatus.Failed:
                Play(Library.RadioError, AudioBus.Radio, 0.3f, 1f, 0.3f);
                break;
        }
    }

    private void HandleCrewLine(string line, string speaker)
    {
        if (Library == null || string.IsNullOrWhiteSpace(line) || line == "...") return;
        if (_keyed) return; // the player is transmitting; the line still shows as text

        StopChatter();
        _chatterRoutine = StartCoroutine(SpokenVoices && _speech != null && _speech.Available
            ? Spoken(line, speaker)
            : Chatter(line, speaker));
    }

    /// <summary>True when the voice helper is running and the crew speak in English.</summary>
    public bool SpeaksAloud => SpokenVoices && _speech != null && _speech.Available;

    private static bool IsFinn(string speaker) => speaker != null && speaker.ToUpperInvariant().Contains("FINN");

    /// <summary>The line in the speaker's own voice, over the radio. Babble if it takes too long.</summary>
    private IEnumerator Spoken(string line, string speaker)
    {
        _chatterActive = true;
        bool finn = IsFinn(speaker);
        _speechClip = null;
        _speech.Request(++_speechId, new CrewSpeech.Voice
        {
            Name = finn ? FinnVoice : HarrisVoice,
            Rate = finn ? FinnRate : HarrisRate,
            Pitch = finn ? FinnVoicePitch : HarrisVoicePitch,
        }, line);

        Play(Library.RadioKeyOn, AudioBus.Radio, 0.22f);
        if (Library.RadioStaticLoop != null)
        {
            _static.clip = Library.RadioStaticLoop;
            _static.volume = StaticVolume * Volume(AudioBus.Radio);
            _static.Play();
        }

        float giveUp = Time.realtimeSinceStartup + SpeechTimeout;
        while (_speechClip == null && Time.realtimeSinceStartup < giveUp) yield return null;

        if (_speechClip == null)
        {
            _chatterRoutine = StartCoroutine(Chatter(line, speaker));
            yield break;
        }

        _voice.clip = _speechClip;
        _voice.volume = SpokenVolume * Volume(AudioBus.Radio);
        _voice.Play();
        yield return new WaitForSecondsRealtime(_speechClip.length);

        _static.Stop();
        _chatterActive = false;
        // Just the soft click of the key being let go: no roger beep after every line.
        Play(Library.RadioKeyOff, AudioBus.Radio, 0.2f, 1f, 0.1f);
        Destroy(_speechClip);
        _speechClip = null;
        _chatterRoutine = null;
    }

    private void StopChatter()
    {
        if (_chatterRoutine != null) StopCoroutine(_chatterRoutine);
        _chatterRoutine = null;
        _chatterActive = false;
        _chatter.Stop();
        _static.Stop();
        if (_voice != null) _voice.Stop();
    }

    /// <summary>Muffled radio "voice": one syllable per vowel group, pauses at punctuation.</summary>
    private IEnumerator Chatter(string line, string speaker)
    {
        _chatterActive = true;
        float basePitch = speaker != null && speaker.ToUpperInvariant().Contains("FINN") ? FinnPitch : HarrisPitch;
        bool question = line.TrimEnd().EndsWith("?");

        Play(Library.RadioKeyOn, AudioBus.Radio, 0.22f);
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
        Play(Library.RadioKeyOff, AudioBus.Radio, 0.2f, 1f, 0.1f);
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

    // ------------------------------------------------------------ story dialogue

    /// <summary>A dialogue line is waiting for its voice or still being spoken.</summary>
    public bool FaceSpeaking => _faceWaiting || (_face != null && _face.isPlaying);

    /// <summary>The dialogue voice is playing right now (for a talking mouth).</summary>
    public bool FaceVoicePlaying => _face != null && _face.isPlaying;

    /// <summary>
    /// Speaks a story dialogue line in the given voice, face to face (no radio). Cuts off any
    /// line still playing. False when spoken voices aren't available: show the text only.
    /// </summary>
    public bool SpeakFace(CrewSpeech.Voice voice, string text)
    {
        StopFace();
        if (!SpeaksAloud || string.IsNullOrWhiteSpace(text)) return false;
        StopChatter(); // the radio gives way to the person in front of you
        _faceWaiting = true;
        _speech.Request(++_faceId, voice, text);
        return true;
    }

    public void StopFace()
    {
        _faceWaiting = false;
        if (_face == null) return;
        if (_face.clip != null)
        {
            _face.Stop();
            Destroy(_face.clip);
            _face.clip = null;
        }
    }

    private void PlayFace(AudioClip clip)
    {
        _faceWaiting = false;
        if (_face.clip != null) Destroy(_face.clip);
        _face.clip = clip;
        _face.volume = SpokenVolume * Volume(AudioBus.Radio);
        _face.Play();
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
        if (element != null && Library != null) Play(Library.UiClick, AudioBus.Effects, 0.3f);
    }
}
