using System.Collections;
using UnityEngine;

/// <summary>
/// Crew speak up on their own when something is wrong.
///
/// These used to be full LLM round-trips fired from a 3-second polling loop, which
/// meant the game could kick off a 4B generation while the player was mid-order —
/// competing for the same runner and the same VRAM. Unprompted warnings are exactly
/// the case where a model buys you nothing: the trigger condition is already known
/// precisely, and the line needs to land the instant the danger appears, not two
/// seconds later.
///
/// So they are authored barks now, filled with real telemetry. Hazard warnings are
/// timed by seconds to impact, not distance, and escalate in three steps per hazard:
/// a call with the side and which way to turn, "closing!", then "brace!".
///
/// On a good line they still talk, so the radio never goes dead: Finn says so when a
/// hazard he warned about goes by without a hit, calls a quarter, half and three quarters
/// of the way, and sights the dock; after a long quiet spell one of them reports in.
/// </summary>
public class CrewAlertSystem : MonoBehaviour
{
    public static CrewAlertSystem Instance;

    /// <summary>A hazard warning fired: 1 = ahead, 2 = closing, 3 = brace. ShipAudio sounds the alarm.</summary>
    public static event System.Action<int> OnHazardStage;

    [Header("Hazard Warnings (seconds to impact)")]
    [Tooltip("Finn's first call: what it is, where, and which way to turn.")]
    public float warnSeconds = 6f;

    [Tooltip("Second, urgent call.")]
    public float closingSeconds = 3f;

    [Tooltip("Last call before the hit.")]
    public float braceSeconds = 1.5f;

    [Header("Fuel")]
    [Tooltip("Harris warns when fuel drops below this percentage.")]
    public float fuelWarningThreshold = 25f;

    [Tooltip("Harris declares an emergency below this percentage.")]
    public float fuelCriticalThreshold = 10f;

    [Header("Cooldowns")]
    [Tooltip("Minimum seconds between fuel warnings.")]
    public float alertCooldown = 12f;

    [Tooltip("How often the monitor checks ship state (real time).")]
    public float checkInterval = 0.25f;

    [Header("Progress (a good line)")]
    [Tooltip("Finn sights the dock inside this many metres.")]
    public float dockSightedMetres = 150f;

    [Tooltip("After this many seconds at sea with no crew line, someone reports in.")]
    public float quietSeconds = 40f;

    [Tooltip("Progress calls wait this long after any crew line, so they don't talk over a read-back.")]
    public float gapAfterLine = 4f;

    [Header("Politeness")]
    [Tooltip("Stay quiet while the player is speaking or a conversation is in flight. The brace call always gets through.")]
    public bool yieldToPlayer = true;

    private float _lastFuelAlert = -999f;
    private bool _firedCriticalFuel = false;

    // Escalation for the hazard currently on our course: 0 none, 1 warned, 2 closing, 3 brace.
    private int _threatId;
    private int _threatStage;
    private bool _hitSinceWarning;

    // Progress along the route this voyage.
    private float _routeLength = -1f;
    private int _quartersCalled;
    private bool _dockSighted;
    private bool _quietReportByFinn;
    private float _lastLineAt;
    private CrewVoice _voice;
    private OpenAIClient _chat;

    void Awake() { Instance = this; }

    private RadioMic _mic;

    void Start()
    {
        _mic = FindAnyObjectByType<RadioMic>();
        _voice = CrewVoice.Instance;
        _chat = FindAnyObjectByType<OpenAIClient>();
        if (_voice != null) _voice.OnSubtitleUpdated += HandleCrewLine;
        if (_chat != null) _chat.OnSubtitleUpdated += HandleCrewLine;
        if (_mic != null) _mic.OnRadioStatus += HandleRadioStatus;
        ShipHull.OnHullHit += HandleHit;
        ShipHull.OnPracticeBump += HandleBump;
        _lastLineAt = Time.realtimeSinceStartup;
        StartCoroutine(MonitorShipStatus());
    }

    void OnDestroy()
    {
        // The voice, chat and radio live on the Services prefab and outlive this scene.
        if (_voice != null) _voice.OnSubtitleUpdated -= HandleCrewLine;
        if (_chat != null) _chat.OnSubtitleUpdated -= HandleCrewLine;
        if (_mic != null) _mic.OnRadioStatus -= HandleRadioStatus;
        ShipHull.OnHullHit -= HandleHit;
        ShipHull.OnPracticeBump -= HandleBump;
    }

    private void HandleCrewLine(string line, string crew) { _lastLineAt = Time.realtimeSinceStartup; }

    // The player talking counts as radio traffic too.
    private void HandleRadioStatus(RadioStatus status, string text)
    {
        if (status == RadioStatus.Heard) _lastLineAt = Time.realtimeSinceStartup;
    }

    private void HandleHit(int containers) { _hitSinceWarning = true; }
    private void HandleBump() { _hitSinceWarning = true; }

    private bool PlayerIsTalking()
    {
        if (_mic != null && (_mic.IsRecording || _mic.IsTranscribing)) return true;
        return CrewChat.Active != null && CrewChat.Active.IsBusy;
    }

    private IEnumerator MonitorShipStatus()
    {
        // Let the scene finish waking up.
        yield return new WaitForSecondsRealtime(1f);

        while (true)
        {
            // Realtime so Command Time slow-mo doesn't stretch the checks.
            yield return new WaitForSecondsRealtime(checkInterval);

            if (CrewVoice.Instance == null) continue;
            if (GameManager.Instance != null && GameManager.Instance.CurrentState != GameState.Playing) continue;
            if (TimeController.Instance != null && TimeController.Instance.IsPaused) continue;

            var t = ShipTelemetry.Capture();

            if (CheckHazard(t)) continue; // one alert per tick, so the crew don't talk over each other

            if (yieldToPlayer && PlayerIsTalking()) continue;
            CheckFuel(t);
            CheckProgress(t);
        }
    }

    private bool CheckHazard(ShipTelemetry t)
    {
        var radar = RadarSystem.Instance;
        if (radar == null || !radar.IsThreat)
        {
            bool passed = _threatStage > 0 && !_hitSinceWarning;
            _threatId = 0;
            _threatStage = 0;
            return passed && SayPassed(t);
        }

        float tti = radar.TimeToImpact;
        if (radar.ThreatId != _threatId)
        {
            // Another hazard is on course: only mention the last one if the new one is still far off.
            bool passed = _threatStage > 0 && !_hitSinceWarning && tti > warnSeconds;
            _threatId = radar.ThreatId;
            _threatStage = 0;
            if (passed && SayPassed(t)) return true;
        }

        int stage = tti <= braceSeconds ? 3 : tti <= closingSeconds ? 2 : tti <= warnSeconds ? 1 : 0;
        if (stage <= _threatStage) return false;

        // Brace always gets through; the earlier calls wait for the player to finish talking.
        if (stage < 3 && yieldToPlayer && PlayerIsTalking()) return false;

        if (_threatStage == 0) _hitSinceWarning = false; // a fresh warning
        _threatStage = stage;
        OnHazardStage?.Invoke(stage);
        string[] lines = stage == 3 ? CrewBarkLibrary.AlertHazardBrace()
                       : stage == 2 ? CrewBarkLibrary.AlertHazardClosing()
                       : CrewBarkLibrary.AlertHazardAhead();

        CrewVoice.Instance.SpeakFrom(RadioChannel.Lookout, lines, t, "alert:hazard" + stage);
        return true;
    }

    private bool SayPassed(ShipTelemetry t)
    {
        if (yieldToPlayer && PlayerIsTalking()) return false;
        CrewVoice.Instance.SpeakFrom(RadioChannel.Lookout, CrewBarkLibrary.HazardPassed(), t, "alert:passed");
        return true;
    }

    /// <summary>Quarter-way calls, the dock sighting, and a report after a long silence.</summary>
    private void CheckProgress(ShipTelemetry t)
    {
        var level = LevelGenerator.Instance;
        var nav = ShipNavigator.Instance;
        if (level == null || !level.IsLevelReady || nav == null) return;

        // Measured from where the ship is on the first tick at sea (the route is built at Set Sail).
        Vector3 toDock = level.EndPosition - nav.transform.position;
        toDock.y = 0f;
        float remaining = toDock.magnitude;
        if (_routeLength < 0f) _routeLength = Mathf.Max(remaining, 1f);

        float now = Time.realtimeSinceStartup;
        if (now - _lastLineAt < gapAfterLine) return;

        if (!_dockSighted && remaining <= dockSightedMetres)
        {
            _dockSighted = true;
            _quartersCalled = 3; // "nearly there" would only repeat it
            CrewVoice.Instance.SpeakFrom(RadioChannel.Lookout, CrewBarkLibrary.DockSighted(), t, "progress:dock");
            return;
        }

        int quarter = Mathf.FloorToInt((1f - remaining / _routeLength) * 4f);
        // "Nearly there" just before the dock sighting would only repeat it.
        if (quarter == 3 && remaining <= dockSightedMetres * 1.6f) quarter = 2;
        if (quarter > _quartersCalled && quarter <= 3)
        {
            _quartersCalled = quarter;
            CrewVoice.Instance.SpeakFrom(RadioChannel.Lookout, CrewBarkLibrary.Progress(quarter), t, "progress:" + quarter);
            return;
        }

        // The drill has its own coach; nobody reports in over it.
        bool drill = TutorialManager.Instance != null && TutorialManager.Instance.IsActive;
        if (!drill && now - _lastLineAt >= quietSeconds)
        {
            _quietReportByFinn = !_quietReportByFinn;
            var crew = _quietReportByFinn ? RadioChannel.Lookout : RadioChannel.ChiefEngineer;
            CrewVoice.Instance.SpeakFrom(crew, CrewBarkLibrary.QuietReport(crew), t, "progress:quiet" + crew);
        }
    }

    private void CheckFuel(ShipTelemetry t)
    {
        float now = Time.realtimeSinceStartup;

        if (t.FuelPct <= fuelCriticalThreshold && !_firedCriticalFuel)
        {
            _firedCriticalFuel = true;
            _lastFuelAlert = now;
            CrewVoice.Instance.SpeakFrom(RadioChannel.ChiefEngineer, CrewBarkLibrary.AlertFuelCritical(), t, "alert:fuelcritical");
        }
        else if (t.FuelPct <= fuelWarningThreshold &&
                 t.FuelPct > fuelCriticalThreshold &&
                 now - _lastFuelAlert > alertCooldown)
        {
            _lastFuelAlert = now;
            CrewVoice.Instance.SpeakFrom(RadioChannel.ChiefEngineer, CrewBarkLibrary.AlertFuelLow(), t, "alert:fuellow");
        }

        // Refuelling re-arms the critical warning.
        if (t.FuelPct > fuelCriticalThreshold + 5) _firedCriticalFuel = false;
    }
}
