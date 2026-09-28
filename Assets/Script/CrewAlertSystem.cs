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

    [Header("Politeness")]
    [Tooltip("Stay quiet while the player is speaking or a conversation is in flight. The brace call always gets through.")]
    public bool yieldToPlayer = true;

    private float _lastFuelAlert = -999f;
    private bool _firedCriticalFuel = false;

    // Escalation for the hazard currently on our course: 0 none, 1 warned, 2 closing, 3 brace.
    private int _threatId;
    private int _threatStage;

    void Awake() { Instance = this; }

    private RadioMic _mic;

    void Start()
    {
        _mic = FindAnyObjectByType<RadioMic>();
        StartCoroutine(MonitorShipStatus());
    }

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
        }
    }

    private bool CheckHazard(ShipTelemetry t)
    {
        var radar = RadarSystem.Instance;
        if (radar == null || !radar.IsThreat)
        {
            _threatId = 0;
            _threatStage = 0;
            return false;
        }

        if (radar.ThreatId != _threatId)
        {
            _threatId = radar.ThreatId;
            _threatStage = 0;
        }

        float tti = radar.TimeToImpact;
        int stage = tti <= braceSeconds ? 3 : tti <= closingSeconds ? 2 : tti <= warnSeconds ? 1 : 0;
        if (stage <= _threatStage) return false;

        // Brace always gets through; the earlier calls wait for the player to finish talking.
        if (stage < 3 && yieldToPlayer && PlayerIsTalking()) return false;

        _threatStage = stage;
        OnHazardStage?.Invoke(stage);
        string[] lines = stage == 3 ? CrewBarkLibrary.AlertHazardBrace()
                       : stage == 2 ? CrewBarkLibrary.AlertHazardClosing()
                       : CrewBarkLibrary.AlertHazardAhead();

        CrewVoice.Instance.SpeakFrom(RadioChannel.Lookout, lines, t, "alert:hazard" + stage);
        return true;
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
