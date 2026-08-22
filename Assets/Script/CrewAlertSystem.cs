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
/// So they are authored barks now, filled with real telemetry.
/// </summary>
public class CrewAlertSystem : MonoBehaviour
{
    public static CrewAlertSystem Instance;

    [Header("Alert Thresholds")]
    [Tooltip("Finn warns about a reef closer than this (metres).")]
    public float reefDangerDistance = 15f;

    [Tooltip("Harris warns when fuel drops below this percentage.")]
    public float fuelWarningThreshold = 25f;

    [Tooltip("Harris declares an emergency below this percentage.")]
    public float fuelCriticalThreshold = 10f;

    [Header("Cooldowns")]
    [Tooltip("Minimum seconds between autonomous alerts of the same kind.")]
    public float alertCooldown = 12f;

    [Tooltip("How often the monitor checks ship state.")]
    public float checkInterval = 2f;

    [Header("Politeness")]
    [Tooltip("Stay quiet while the player is speaking or a conversation is in flight.")]
    public bool yieldToPlayer = true;

    private float _lastReefAlert = -999f;
    private float _lastFuelAlert = -999f;
    private bool _firedCriticalFuel = false;

    void Awake() { Instance = this; }

    void Start() { StartCoroutine(MonitorShipStatus()); }

    private IEnumerator MonitorShipStatus()
    {
        // Let the scene finish waking up.
        yield return new WaitForSecondsRealtime(3f);

        while (true)
        {
            // Realtime so Command Time slow-mo doesn't stretch the cooldowns.
            yield return new WaitForSecondsRealtime(checkInterval);

            if (CrewVoice.Instance == null) continue;

            // Don't talk over the player or an in-flight conversation, whichever
            // conversation backend is active.
            if (yieldToPlayer && CrewChat.Active != null && CrewChat.Active.IsBusy) continue;

            var t = ShipTelemetry.Capture();
            float now = Time.realtimeSinceStartup;

            // ---- Lookout Finn: reef warning ----
            if (t.HazardDetected &&
                t.HazardDistanceM <= reefDangerDistance &&
                now - _lastReefAlert > alertCooldown)
            {
                _lastReefAlert = now;
                CrewVoice.Instance.SpeakFrom(
                    RadioChannel.Lookout,
                    CrewBarkLibrary.AlertReef(),
                    t,
                    "alert:reef");
                continue; // one alert per tick, so the crew don't talk over each other
            }

            // ---- Chief Harris: fuel ----
            if (t.FuelPct <= fuelCriticalThreshold && !_firedCriticalFuel)
            {
                _firedCriticalFuel = true;
                _lastFuelAlert = now;
                CrewVoice.Instance.SpeakFrom(
                    RadioChannel.ChiefEngineer,
                    CrewBarkLibrary.AlertFuelCritical(),
                    t,
                    "alert:fuelcritical");
            }
            else if (t.FuelPct <= fuelWarningThreshold &&
                     t.FuelPct > fuelCriticalThreshold &&
                     now - _lastFuelAlert > alertCooldown)
            {
                _lastFuelAlert = now;
                CrewVoice.Instance.SpeakFrom(
                    RadioChannel.ChiefEngineer,
                    CrewBarkLibrary.AlertFuelLow(),
                    t,
                    "alert:fuellow");
            }

            // Refuelling re-arms the critical warning.
            if (t.FuelPct > fuelCriticalThreshold + 5) _firedCriticalFuel = false;
        }
    }
}
