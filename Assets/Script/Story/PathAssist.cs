using UnityEngine;

/// <summary>
/// Story mode's quiet helmsman: once an order has run its course and the rudder would come back
/// to midships, he holds a few degrees toward the channel (Channel) instead, aiming at a point a
/// little way ahead along it. A turn the player orders still decides where the ship goes; this
/// only keeps it from drifting off afterwards, and settles it back onto the channel. Off when the
/// ship is slow, going astern, near the berth (the docking pilot's), or when Settings turns it off.
/// The StoryDirector adds it; it works through ShipHelm.Assist.
/// </summary>
public class PathAssist : MonoBehaviour
{
    private const float MaxRudder = 6f;      // degrees: a nudge, not a turn
    private const float Gain = 0.3f;         // degrees of rudder per degree off the channel's line
    private const float LookAhead = 32f;     // metres along the channel to aim at
    private const float MinSpeed = 1f;
    private const float BerthRange = 60f;

    private int _progress;

    private void Update()
    {
        var nav = ShipNavigator.Instance;
        var helm = nav != null ? nav.GetComponent<ShipHelm>() : null;
        if (helm == null) return;
        helm.Assist = Wanted(nav, helm);
    }

    private void OnDisable()
    {
        var nav = ShipNavigator.Instance;
        var helm = nav != null ? nav.GetComponent<ShipHelm>() : null;
        if (helm != null) helm.Assist = 0f;
    }

    private float Wanted(ShipNavigator nav, ShipHelm helm)
    {
        bool on = SettingsService.Instance == null || SettingsService.Instance.PathAssist;
        var gm = GameManager.Instance;
        var engine = nav.GetComponent<ShipEngine>();
        if (!on || gm == null || gm.CurrentState != GameState.Playing || engine == null || engine.Speed < MinSpeed || !Channel.Ready) return 0f;

        var state = helm.Snapshot;
        if (state.Ordered || state.Autopilot) return 0f; // the player's order is being carried out

        Vector3 ship = nav.transform.position;
        if (Quay.Destination != null && Channel.Flat(ship - Quay.Destination.Berth).magnitude < BerthRange) return 0f;

        if (_progress >= Channel.Points.Count) _progress = 0;
        _progress = Channel.Nearest(ship, Mathf.Max(0, _progress - 3), out _);
        Vector3 aim = Channel.Points[Channel.IndexAt(Channel.Along[_progress] + LookAhead)];
        Vector3 to = Channel.Flat(aim - ship);
        if (to.sqrMagnitude < 1f) return 0f;

        float wanted = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
        float error = Mathf.DeltaAngle(nav.transform.eulerAngles.y, wanted);
        // Past a right angle the player has chosen another way (or is turning round): leave it to them.
        if (Mathf.Abs(error) > 90f) return 0f;
        return Mathf.Clamp(error * Gain, -MaxRudder, MaxRudder);
    }
}
