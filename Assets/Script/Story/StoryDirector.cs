using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runs a story mission in the Game scene: places the mission's rowboat and sea sights along
/// the generated route, plays the crew's radio lines as the ship gets further along, keeps the
/// dock closed until a rescue is done, and plays the closing dialogue before the voyage report.
/// The shipyard installs it when a story mission is active; Free Sail never sees it.
/// </summary>
public class StoryDirector : MonoBehaviour
{
    /// <summary>Close enough for Finn to see the rowboat.</summary>
    private const float SpotDistance = 75f;
    /// <summary>Stopped within this of the rowboat, the person climbs aboard.</summary>
    private const float PickupRadius = 18f;
    /// <summary>"Stopped" for a pickup: slower than this, in knots.</summary>
    private const float PickupSpeed = 1.2f;
    private const float PickupHoldSeconds = 0.8f;
    /// <summary>Close enough for a sight to show itself.</summary>
    private const float SightDistance = 60f;
    /// <summary>Shortest gap between two story lines on the radio.</summary>
    private const float LineGap = 1.2f;
    /// <summary>Seconds after arrival before the closing dialogue starts.</summary>
    private const float OutroDelay = 1.6f;

    public static StoryDirector Instance { get; private set; }

    public StoryMission Mission { get; private set; }

    private Transform _props;
    private RescueTarget _rescue;
    private bool _rescueSpotted;
    private float _stoppedFor;
    private readonly List<SeaSight> _sights = new List<SeaSight>();
    private readonly List<int> _sightsFired = new List<int>();
    private readonly Queue<RadioCue> _queue = new Queue<RadioCue>();
    private int _nextCue;
    private RadioCue[] _cues;
    private float _quietUntil;
    private float _missedSaidAt = -100f;
    private bool _outroPlayed;
    private bool _holdResults;

    /// <summary>The rescue is done (or the mission has none).</summary>
    public bool RescueDone => Mission == null || Mission.Goal != MissionGoal.Rescue || (_rescue != null && _rescue.Boarded) || _rescueBoarded;
    private bool _rescueBoarded;

    public static StoryDirector Install(StoryMission mission)
    {
        var go = new GameObject("StoryDirector");
        var director = go.AddComponent<StoryDirector>();
        director.Mission = mission;
        return director;
    }

    private void Awake()
    {
        Instance = this;
    }

    private void OnEnable()
    {
        EndPort.CanFinish = CanFinish;
        MissionResultsPanel.Hold = () => _holdResults;
    }

    private void Start()
    {
        if (LevelGenerator.Instance != null)
        {
            LevelGenerator.Instance.OnLevelGenerated += PlaceProps;
            if (LevelGenerator.Instance.IsLevelReady) PlaceProps();
        }
        if (GameManager.Instance != null) GameManager.Instance.OnStateChanged += HandleState;

        _cues = Mission.Radio != null ? (RadioCue[])Mission.Radio.Clone() : new RadioCue[0];
        System.Array.Sort(_cues, (a, b) => a.At.CompareTo(b.At));
    }

    private void OnDisable()
    {
        if (EndPort.CanFinish == (System.Func<bool>)CanFinish) EndPort.CanFinish = null;
        MissionResultsPanel.Hold = null;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (LevelGenerator.Instance != null) LevelGenerator.Instance.OnLevelGenerated -= PlaceProps;
        if (GameManager.Instance != null) GameManager.Instance.OnStateChanged -= HandleState;
        EndPort.CanFinish = null;
        MissionResultsPanel.Hold = null;
    }

    // ------------------------------------------------------------ placing the mission's things

    /// <summary>Runs after every level build (the shipyard rebuilds it on Set Sail): same seed, same places.</summary>
    private void PlaceProps()
    {
        if (_props != null) Destroy(_props.gameObject);
        _props = new GameObject("StoryProps").transform;
        _sights.Clear();
        _sightsFired.Clear();
        _rescue = null;
        _rescueBoarded = false;
        _rescueSpotted = false;

        var level = LevelGenerator.Instance;
        var path = level.SolutionWorldPath;
        if (path == null || path.Count < 2) return;
        float surface = WaterSurface();

        if (Mission.Goal == MissionGoal.Rescue)
        {
            Vector3 at = PointAlong(path, Mission.RescueAt, out Vector3 dir);
            // A little off the channel's centre line, so the ship can stop beside it without hitting it.
            Vector3 side = Vector3.Cross(Vector3.up, dir).normalized;
            at += side * 5f;
            at.y = surface;
            _rescue = RescueTarget.Create(_props, at, Quaternion.LookRotation(dir).eulerAngles.y + 70f, StoryCast.Get(Mission.Client));
        }

        foreach (var spec in Mission.Sights)
        {
            Vector3 at = PointAlong(path, spec.At, out Vector3 dir);
            Vector3 side = Vector3.Cross(Vector3.up, dir).normalized * Mathf.Sign(spec.Side == 0 ? 1 : spec.Side);
            Vector3 start = at + side * level.CellWorldSize * 0.9f;
            // It crosses ahead of the ship, from the side it was seen on toward the far side.
            Vector3 heading = (dir.normalized * 0.35f - side).normalized;
            start.y = surface;
            _sights.Add(SeaSight.Create(_props, spec.Kind, start, heading));
        }
    }

    /// <summary>Height of the water's top surface (the water art isn't flat at y = 0).</summary>
    private static float WaterSurface()
    {
        var water = GameObject.Find("Water");
        if (water == null) return 0f;
        float top = float.MinValue;
        foreach (var r in water.GetComponentsInChildren<Renderer>()) top = Mathf.Max(top, r.bounds.max.y);
        return top == float.MinValue ? 0f : top;
    }

    /// <summary>The point <paramref name="share"/> of the way along the path, and the path's direction there.</summary>
    private static Vector3 PointAlong(List<Vector3> path, float share, out Vector3 direction)
    {
        float total = 0f;
        for (int i = 1; i < path.Count; i++) total += Vector3.Distance(path[i - 1], path[i]);
        float want = Mathf.Clamp01(share) * total;
        for (int i = 1; i < path.Count; i++)
        {
            float seg = Vector3.Distance(path[i - 1], path[i]);
            if (want <= seg || i == path.Count - 1)
            {
                direction = path[i] - path[i - 1];
                if (direction.sqrMagnitude < 0.01f) direction = Vector3.forward;
                return Vector3.Lerp(path[i - 1], path[i], seg > 0f ? Mathf.Clamp01(want / seg) : 0f);
            }
            want -= seg;
        }
        direction = Vector3.forward;
        return path[path.Count - 1];
    }

    // ------------------------------------------------------------ during the voyage

    private void Update()
    {
        var gm = GameManager.Instance;
        var nav = ShipNavigator.Instance;
        var level = LevelGenerator.Instance;
        if (gm == null || nav == null || level == null || gm.CurrentState != GameState.Playing) return;

        Vector3 ship = nav.transform.position;
        float length = Mathf.Max(1f, level.EndPosition.z);
        float progress = Mathf.Clamp01(ship.z / length);

        // Lines for the stretch of route just reached.
        while (_nextCue < _cues.Length && progress >= _cues[_nextCue].At) _queue.Enqueue(_cues[_nextCue++]);

        // Sights show themselves when the ship comes near.
        for (int i = 0; i < _sights.Count; i++)
        {
            if (_sightsFired.Contains(i) || _sights[i] == null) continue;
            Vector3 s = _sights[i].transform.position;
            if (Flat(s - ship).magnitude > SightDistance) continue;
            _sightsFired.Add(i);
            _sights[i].Reveal();
            foreach (var line in Mission.Sights[i].Lines) _queue.Enqueue(line);
        }

        UpdateRescue(ship, nav);
        SpeakNext();
    }

    private void UpdateRescue(Vector3 ship, ShipNavigator nav)
    {
        if (_rescue == null || _rescue.Boarded) return;
        float distance = Flat(_rescue.transform.position - ship).magnitude;

        if (!_rescueSpotted && distance < SpotDistance)
        {
            _rescueSpotted = true;
            _queue.Enqueue(Mission.RescueSpotted);
            if (ShipHUD.Instance != null) ShipHUD.Instance.ShowRadioNote("Stop next to " + Mission.RescueName + ": say \"stop\".");
        }

        bool stopped = Mathf.Abs(nav.currentSpeed) < PickupSpeed;
        _stoppedFor = distance < PickupRadius && stopped ? _stoppedFor + Time.deltaTime : 0f;
        if (_stoppedFor < PickupHoldSeconds) return;

        _rescue.Board(nav.transform);
        _rescueBoarded = true;
        _queue.Clear(); // the good news first
        _queue.Enqueue(Mission.RescueAboard);
        if (ShipHUD.Instance != null) ShipHUD.Instance.ShowRadioNote("Aboard! Now sail to the dock.");
        if (AudioService.Instance != null && AudioService.Instance.Sfx != null)
            AudioService.Instance.Play(AudioService.Instance.Sfx.RadioRoger, AudioBus.Effects, 0.8f);
    }

    private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

    /// <summary>One queued line at a time, never over the crew or the tutorial.</summary>
    private void SpeakNext()
    {
        if (_queue.Count == 0 || Time.unscaledTime < _quietUntil) return;
        var audio = AudioService.Instance;
        if (audio != null && audio.IsChattering) return;
        if (TutorialManager.Instance != null && TutorialManager.Instance.IsActive) return;

        var cue = _queue.Dequeue();
        Say(cue.Speaker, cue.Text);
        _quietUntil = Time.unscaledTime + LineGap;
    }

    private static void Say(CharacterId speaker, string text)
    {
        if (CrewVoice.Instance == null || string.IsNullOrEmpty(text)) return;
        var channel = speaker == CharacterId.Harris ? RadioChannel.ChiefEngineer : RadioChannel.Lookout;
        CrewVoice.Instance.SpeakFrom(channel, new[] { text }, ShipTelemetry.Capture(), "story");
    }

    /// <summary>The dock stays shut until the rescue is done; Finn says why.</summary>
    private bool CanFinish()
    {
        if (RescueDone) return true;
        if (Time.time - _missedSaidAt > 6f)
        {
            _missedSaidAt = Time.time;
            Say(CharacterId.Finn, Mission.RescueMissed);
            if (ShipHUD.Instance != null) ShipHUD.Instance.ShowRadioNote("Find " + Mission.RescueName + " first.");
        }
        return false;
    }

    // ------------------------------------------------------------ the end

    private void HandleState(GameState state)
    {
        if (state != GameState.Victory) return;
        StoryProgress.MarkDone(Mission);
        if (_outroPlayed) return;
        _outroPlayed = true;
        _holdResults = Mission.Outro.Length > 0;
        if (_holdResults) StartCoroutine(PlayOutro());
    }

    // Real time: the arrival may be in slow motion.
    private System.Collections.IEnumerator PlayOutro()
    {
        yield return new WaitForSecondsRealtime(OutroDelay);
        _queue.Clear();
        DialoguePlayer.Play(Mission.Outro, Mission.Label.ToUpperInvariant() + "  ·  " + Mission.Title.ToUpperInvariant() + "  ·  COMPLETE",
            () => _holdResults = false);
    }
}
