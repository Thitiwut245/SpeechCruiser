using UnityEngine;

/// <summary>
/// Story mode's quiet helmsman: once an order has run its course and the rudder would come back
/// to midships, he holds a few degrees toward the channel (Channel) instead, aiming at a point a
/// little way ahead along it. A turn the player orders still decides where the ship goes; this
/// only keeps it from drifting off afterwards, and settles it back onto the channel. Off when the
/// ship is slow, going astern, near the berth (the docking pilot's), or when Settings turns it off.
/// The StoryDirector adds it; it works through ShipHelm.Assist.
///
/// It also answers "follow the light" / "take us home" (<see cref="Follow"/>): where a guiding
/// light leads the way (story 3-4), the helmsman steers after it along the channel himself until
/// the Captain gives a helm order of his own. Anywhere else the crew say they can't find the way
/// alone, so the order never becomes an autopilot for the whole game.
/// </summary>
public class PathAssist : MonoBehaviour
{
    private const float MaxRudder = 6f;      // degrees: a nudge, not a turn
    private const float Gain = 0.3f;         // degrees of rudder per degree off the channel's line
    private const float LookAhead = 32f;     // metres along the channel to aim at
    private const float MinSpeed = 1f;
    private const float BerthRange = 60f;

    private int _progress;

    public static PathAssist Instance { get; private set; }

    private bool _following;
    private float _nextSteer;
    private const float FollowAhead = 40f;   // metres along the channel to steer for
    private const float FollowRudder = 25f;

    private void Awake() => Instance = this;

    private void OnEnable() => LanguageLog.OnOrder += HandleOrder;

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>"Follow the light" / "take us home". Always answered (the order is never lost).</summary>
    public static bool Follow()
    {
        bool atSea = GameManager.Instance != null && GameManager.Instance.CurrentState == GameState.Playing;
        if (!atSea) return false;
        var me = Instance;
        bool guided = me != null && StoryDirector.Instance != null && StoryDirector.Instance.GuideTarget.HasValue;
        if (!guided)
        {
            Say(RadioChannel.Lookout, "I can't find the way by myself, Captain. Give me a course: port or starboard. Over.",
                                      "There's no light to follow here, Sir. You steer, I'll watch. Over.");
            return true;
        }
        me._following = true;
        me._nextSteer = 0f;
        Say(RadioChannel.Lookout, "Following the light, Captain! I'll keep her behind it. Over.",
                                  "Aye, Sir! After the light. Taking us home. Over.");
        return true;
    }

    // A helm order of the Captain's own takes the wheel back.
    private void HandleOrder(ShipAction action, bool standard)
    {
        if (_following && action.IsHelm())
        {
            _following = false;
            Say(RadioChannel.Lookout, "Your helm, Captain. Over.");
        }
    }

    private void FollowStep(ShipNavigator nav, ShipHelm helm)
    {
        var target = StoryDirector.Instance != null ? StoryDirector.Instance.GuideTarget : null;
        bool nearBerth = Quay.Destination != null && Channel.Flat(nav.transform.position - Quay.Destination.Berth).magnitude < BerthRange;
        if (!target.HasValue || nearBerth)
        {
            _following = false;
            Say(RadioChannel.Lookout, "There's the dock, Captain. She's yours. Bring her in. Over.");
            return;
        }
        if (Time.time < _nextSteer) return;
        _nextSteer = Time.time + 0.6f;
        // After the light; if it is behind or far off the channel, after the channel ahead instead.
        Vector3 aim = target.Value;
        Vector3 to = Channel.Flat(aim - nav.transform.position);
        if (to.sqrMagnitude < 25f || Vector3.Dot(to.normalized, nav.transform.forward) < -0.2f)
        {
            float bearing = Channel.BearingFrom(nav.transform, FollowAhead);
            if (Mathf.Abs(bearing) > 3f) helm.OrderHeadingChange(bearing, FollowRudder);
            return;
        }
        float turn = Mathf.DeltaAngle(nav.transform.eulerAngles.y, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg);
        if (Mathf.Abs(turn) > 3f) helm.OrderHeadingChange(turn, FollowRudder);
    }

    private static void Say(RadioChannel who, params string[] lines)
    {
        if (CrewVoice.Instance != null) CrewVoice.Instance.SpeakFrom(who, lines, ShipTelemetry.Capture(), "follow:" + lines[0].GetHashCode());
    }

    private void Update()
    {
        var nav = ShipNavigator.Instance;
        var helm = nav != null ? nav.GetComponent<ShipHelm>() : null;
        if (helm == null) return;
        bool atSea = GameManager.Instance != null && GameManager.Instance.CurrentState == GameState.Playing;
        if (_following && atSea) { helm.Assist = 0f; FollowStep(nav, helm); return; }
        if (!atSea) _following = false;
        helm.Assist = Wanted(nav, helm);
    }

    private void OnDisable()
    {
        LanguageLog.OnOrder -= HandleOrder;
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
