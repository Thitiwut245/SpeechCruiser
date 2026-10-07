using UnityEngine;

/// <summary>
/// Game-scene ship sounds: the diesel (EngineSynth, chugging faster with the revs) and the
/// bow wash that follow the speed, the telegraph bell on every engine order, hull hits, cargo
/// going overboard, hazard alarms, the horn at departure and arrival, the sea's mood (harbour
/// or open water) and the music's tension layer from the radar.
/// </summary>
public class ShipAudio : MonoBehaviour
{
    [Header("Engine (generated diesel)")]
    [Tooltip("Engine level at idle (moored or stopped).")]
    [SerializeField] private float DieselIdleLevel = 0.3f;
    [Tooltip("Engine level at full revs.")]
    [SerializeField] private float DieselFullLevel = 0.75f;

    [Header("Water")]
    [SerializeField] private float WashMaxVolume = 0.45f;

    [Tooltip("Speed that counts as full revs and full wash.")]
    [SerializeField] private float FullSpeedKnots = 10f;

    [Header("Music tension (radar time to impact)")]
    [SerializeField] private float TensionStartSeconds = 8f;
    [SerializeField] private float TensionFullSeconds = 1.5f;

    private ShipEngine _engine;
    private EngineSynth _diesel;
    private AudioSource _washSource;
    private ShipEngine.TelegraphGear _lastGear;
    private GameState _state = GameState.Shipyard;
    private float _stateGain = 0.5f;
    private bool _paused;

    private static SfxLibrary Sfx => AudioService.Instance != null ? AudioService.Instance.Sfx : null;

    private void Start()
    {
        _engine = ShipNavigator.Instance != null && ShipNavigator.Instance.Engine != null
            ? ShipNavigator.Instance.Engine
            : FindAnyObjectByType<ShipEngine>();

        _diesel = EngineSynth.Create(transform);
        _washSource = NewLoop(Sfx != null ? Sfx.WashLoop : null);

        if (_engine != null)
        {
            _lastGear = _engine.Gear;
            _engine.OnGearChanged += HandleGearChanged;
        }
        ShipHull.OnHullHit += HandleHullHit;
        ShipHull.OnPracticeBump += HandlePracticeBump;
        ShipHull.OnHealthChanged += HandleHealthChanged;
        ShipHull.OnShipDestroyed += HandleShipDestroyed;
        CrewAlertSystem.OnHazardStage += HandleHazardStage;
        if (GameManager.Instance != null)
        {
            _state = GameManager.Instance.CurrentState;
            GameManager.Instance.OnStateChanged += HandleStateChanged;
        }
        SetSeaMood(_state);
    }

    /// <summary>The harbour has gulls and lapping; open water has a longer swell and fewer gulls.</summary>
    private static void SetSeaMood(GameState state)
    {
        var audio = AudioService.Instance;
        if (audio == null) return;
        if (state == GameState.Playing) audio.SetSeaMood(0.45f, 0.35f, 0.12f);
        else if (state == GameState.GameOver) audio.SetSeaMood(0.4f, 0.35f, 0f);
        else audio.SetHarbourMood();
    }

    private void OnDestroy()
    {
        if (_engine != null) _engine.OnGearChanged -= HandleGearChanged;
        ShipHull.OnHullHit -= HandleHullHit;
        ShipHull.OnPracticeBump -= HandlePracticeBump;
        ShipHull.OnHealthChanged -= HandleHealthChanged;
        ShipHull.OnShipDestroyed -= HandleShipDestroyed;
        CrewAlertSystem.OnHazardStage -= HandleHazardStage;
        if (GameManager.Instance != null) GameManager.Instance.OnStateChanged -= HandleStateChanged;
        if (AudioService.Instance != null) AudioService.Instance.SetTension(0f);
    }

    private AudioSource NewLoop(AudioClip clip)
    {
        var s = gameObject.AddComponent<AudioSource>();
        s.playOnAwake = false;
        s.loop = true;
        s.spatialBlend = 0f;
        s.volume = 0f;
        s.clip = clip;
        if (clip != null)
        {
            s.time = Random.Range(0f, clip.length); // engine and wash never start in step
            s.Play();
        }
        return s;
    }

    private void Update()
    {
        bool paused = TimeController.Instance != null && TimeController.Instance.IsPaused;
        if (paused != _paused)
        {
            _paused = paused;
            if (paused) _washSource.Pause();
            else _washSource.UnPause();
        }
        if (_diesel != null && (paused || _engine == null)) _diesel.Gain = 0f;
        if (paused || _engine == null) return;

        float dt = Time.unscaledDeltaTime;
        float targetGain = _state == GameState.Playing ? 1f : _state == GameState.Shipyard ? 0.5f : 0f;
        _stateGain = Mathf.MoveTowards(_stateGain, targetGain, dt * 0.8f);

        float revs = Mathf.Clamp01(Mathf.Abs(_engine.Speed) / FullSpeedKnots);
        float effects = AudioService.Volume(AudioBus.Effects);

        _diesel.Revs = revs;
        _diesel.Gain = Mathf.Lerp(DieselIdleLevel, DieselFullLevel, revs) * effects * _stateGain;
        _washSource.volume = WashMaxVolume * Mathf.Pow(revs, 1.3f) * effects * _stateGain;
        _washSource.pitch = 0.85f + 0.3f * revs;

        if (AudioService.Instance != null) AudioService.Instance.SetTension(CurrentTension());
    }

    private float CurrentTension()
    {
        var radar = RadarSystem.Instance;
        if (_state != GameState.Playing || radar == null || !radar.IsThreat) return 0f;
        return 1f - Mathf.InverseLerp(TensionFullSeconds, TensionStartSeconds, radar.TimeToImpact);
    }

    private void HandleGearChanged(string display)
    {
        var gear = _engine.Gear;
        bool changed = gear != _lastGear;
        _lastGear = gear;

        // Leaving Playing forces STOP; that isn't an order the player gave. GameManager sets its
        // state before it stops the engine, so read the live state, not the last event.
        bool atSea = GameManager.Instance != null && GameManager.Instance.CurrentState == GameState.Playing;
        if (!changed || !atSea || Sfx == null) return;
        AudioService.Instance.Play(Sfx.TelegraphBell, AudioBus.Effects, 0.4f);
    }

    private void HandleHullHit(int containersLost)
    {
        if (Sfx == null) return;
        var audio = AudioService.Instance;
        bool mine = containersLost >= 2;

        if (mine) audio.Play(Sfx.MineExplosion, AudioBus.Effects, 1f);
        audio.Play(Sfx.HullCrunch, AudioBus.Effects, mine ? 0.7f : 1f);
        audio.Play(Sfx.CargoSplash, AudioBus.Effects, 0.7f, 1f, 0.35f);
        if (mine) audio.Play(Sfx.CargoSplash, AudioBus.Effects, 0.6f, 0.9f, 0.6f);
    }

    private void HandlePracticeBump()
    {
        if (Sfx == null) return;
        AudioService.Instance.Play(Sfx.HullCrunch, AudioBus.Effects, 0.45f, 1.15f);
    }

    private void HandleHealthChanged(int current, int max)
    {
        if (Sfx == null || current <= 0 || current >= max || _state != GameState.Playing) return;
        AudioService.Instance.Play(Sfx.Klaxon, AudioBus.Effects, 0.3f, 1f, 0.4f);
    }

    private void HandleShipDestroyed()
    {
        if (Sfx == null) return;
        AudioService.Instance.Play(Sfx.Klaxon, AudioBus.Effects, 0.4f, 0.9f, 0.3f);
    }

    private void HandleHazardStage(int stage)
    {
        // Finn's spoken warning does the work: no beep for the first warning, soft ones after.
        if (Sfx == null || stage < 2) return;
        var clip = stage >= 3 ? Sfx.AlertBrace : Sfx.AlertClosing;
        AudioService.Instance.Play(clip, AudioBus.Effects, stage >= 3 ? 0.4f : 0.25f);
    }

    private void HandleStateChanged(GameState state)
    {
        var previous = _state;
        _state = state;
        if (state != previous) SetSeaMood(state);
        if (Sfx == null || state == previous) return;
        var audio = AudioService.Instance;

        switch (state)
        {
            case GameState.Playing:
                audio.Play(Sfx.ShipHorn, AudioBus.Effects, 0.45f);
                break;
            case GameState.Victory:
                audio.Play(Sfx.ShipHorn, AudioBus.Effects, 0.45f);
                audio.Play(Sfx.VictorySting, AudioBus.Effects, 0.75f, 1f, 0.5f);
                audio.DuckMusicFor(4f);
                break;
            case GameState.GameOver:
                audio.Play(Sfx.FailSting, AudioBus.Effects, 0.75f, 1f, 0.6f);
                audio.DuckMusicFor(4f);
                break;
        }
    }
}
