using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Restarting a story voyage, from its start or from the last checkpoint. Either way the Game
/// scene is reloaded and the ship sets sail at once (no shipyard, no briefing); from a checkpoint
/// it is then put back there as it was (Checkpoints). Restarting is free: the voyage is dropped,
/// not abandoned.
/// </summary>
public static class VoyageRestart
{
    public enum Mode { None, FromStart, FromCheckpoint }

    private static Mode _pending;

    public static void Request(Mode mode)
    {
        if (SceneLoader.Instance == null || SceneLoader.Instance.IsLoading) return;
        if (mode == Mode.FromCheckpoint && Checkpoints.Saved == null) mode = Mode.FromStart;
        var session = GameSession.Instance;
        if (session != null)
        {
            session.MarkAtSea(false);
            session.ClearCargo();
        }
        _pending = mode;
        SceneLoader.Instance.Load(SceneId.Game);
    }

    /// <summary>The restart asked for, once (the shipyard reads it as the scene starts).</summary>
    public static Mode Take()
    {
        var mode = _pending;
        _pending = Mode.None;
        return mode;
    }
}

/// <summary>
/// Story mode's checkpoints: three points along the channel (a quarter, half and three quarters
/// of the way). Passing one saves where the voyage stands: the ship on the channel there, the
/// hull, the cargo aboard, the clock and the hits, and whether the rescue is done. Restarting from
/// it (pause menu, the report after a sinking, or the prompt below) puts all of that back.
/// Far off the channel (50 m for 8 s), it asks whether to go back to the last checkpoint.
/// The StoryDirector adds it.
/// </summary>
public class Checkpoints : MonoBehaviour
{
    public sealed class Snapshot
    {
        public string MissionId;
        public int Number;              // 1..3
        public Vector3 Position;        // on the channel
        public float Heading;           // along it
        public int Hull, Cargo, Hits, MineHits;
        public float Elapsed;
        public bool RescueDone;
    }

    private static readonly float[] Shares = { 0.25f, 0.5f, 0.75f };
    private const float OnChannel = 50f;      // metres from the channel that still count as on it
    private const float OffSeconds = 8f;
    private const float SnoozeSeconds = 25f;  // "keep sailing" quiets the prompt this long

    /// <summary>The last checkpoint passed on this mission, kept across the restart's scene reload.</summary>
    public static Snapshot Saved { get; private set; }

    public static void Clear() => Saved = null;

    private int _progress;
    private float _offFor, _snoozeUntil;
    private VisualElement _layer, _prompt;
    private GameObject _layerObject;
    private bool _restoring;

    private static string MissionId => StoryProgress.ActiveMission != null ? StoryProgress.ActiveMission.Id : "";

    private void Start()
    {
        if (Saved != null && Saved.MissionId != MissionId) Saved = null;
        if (GameManager.Instance != null) GameManager.Instance.OnStateChanged += HandleState;
    }

    private void OnDestroy()
    {
        if (GameManager.Instance != null) GameManager.Instance.OnStateChanged -= HandleState;
        if (_prompt != null && TimeController.Instance != null) TimeController.Instance.ReleasePause(this);
        if (_layerObject != null) Destroy(_layerObject);
    }

    /// <summary>Set by the shipyard before sailing: on reaching the sea, go back to <see cref="Saved"/>.</summary>
    public void RestoreOnSailing() => _restoring = Saved != null;

    private void HandleState(GameState state)
    {
        if (state == GameState.Victory) Clear(); // done: the next try starts fresh
        if (state != GameState.Playing || !_restoring) return;
        _restoring = false;
        Restore(Saved);
    }

    private void Update()
    {
        var gm = GameManager.Instance;
        var nav = ShipNavigator.Instance;
        if (gm == null || nav == null || gm.CurrentState != GameState.Playing || !Channel.Ready || _prompt != null) return;

        Vector3 ship = nav.transform.position;
        if (_progress >= Channel.Points.Count) _progress = 0;
        _progress = Channel.Nearest(ship, Mathf.Max(0, _progress - 3), out float near);
        Channel.Nearest(ship, 0, out float off); // anywhere on the channel: the ship may have doubled back

        PassCheckpoints(nav, near);
        WatchOffCourse(ship, off);
    }

    // ------------------------------------------------------------ passing

    private void PassCheckpoints(ShipNavigator nav, float distance)
    {
        if (distance > OnChannel) return;
        int passed = Saved != null ? Saved.Number : 0;
        if (passed >= Shares.Length) return;
        float at = Channel.Along[_progress];
        if (at < Shares[passed] * Channel.Length) return;

        int index = Channel.IndexAt(Shares[passed] * Channel.Length);
        Vector3 dir = Channel.Direction(index);
        var hull = nav.GetComponentInChildren<ShipHull>();
        var session = GameSession.Instance;
        var gm = GameManager.Instance;
        Saved = new Snapshot
        {
            MissionId = MissionId,
            Number = passed + 1,
            Position = Channel.Points[index],
            Heading = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg,
            Hull = hull != null ? hull.CurrentHealth : 1,
            Cargo = session != null ? session.CargoAboard : 0,
            Hits = gm.Hits,
            MineHits = gm.MineHits,
            Elapsed = gm.Elapsed,
            RescueDone = StoryDirector.Instance == null || StoryDirector.Instance.RescueDone,
        };
        Banner("CHECKPOINT", Saved.Number + " of " + Shares.Length, "You can restart from here (Esc).");
        PlayBell();
    }

    // ------------------------------------------------------------ restoring

    private void Restore(Snapshot s)
    {
        var nav = ShipNavigator.Instance;
        if (s == null || nav == null) return;

        Vector3 p = s.Position;
        p.y = nav.transform.position.y;
        nav.transform.SetPositionAndRotation(p, Quaternion.Euler(0f, s.Heading, 0f));
        nav.SetGear(ShipEngine.TelegraphGear.Stop);
        nav.SetSpeed(0f);
        var helm = nav.GetComponent<ShipHelm>();
        if (helm != null) helm.RudderMidships();
        if (nav.Movement != null) nav.Movement.KillTurn();

        var hull = nav.GetComponentInChildren<ShipHull>();
        if (hull != null) hull.SetHealth(s.Hull);
        var session = GameSession.Instance;
        if (session != null) ShipyardManager.ShedCargo(session.CargoAboard - s.Cargo);
        GameManager.Instance.RestoreProgress(s.Elapsed, s.Hits, s.MineHits);
        if (StoryDirector.Instance != null) StoryDirector.Instance.ResumeAt(p, s.RescueDone);

        // The camera jumps with the ship instead of gliding there.
        var vcam = FindAnyObjectByType<Unity.Cinemachine.CinemachineCamera>();
        if (vcam != null) vcam.PreviousStateIsValid = false;
        _progress = 0;
        Banner("BACK AT CHECKPOINT", s.Number + " of " + Shares.Length, "Engines stopped. Give your orders, Captain.");
    }

    // ------------------------------------------------------------ off course

    private void WatchOffCourse(Vector3 ship, float distance)
    {
        bool nearBerth = Quay.Destination != null && Channel.Flat(ship - Quay.Destination.Berth).magnitude < 80f;
        bool busy = DialoguePlayer.IsPlaying || nearBerth || Time.unscaledTime < _snoozeUntil;
        _offFor = distance > OnChannel && !busy ? _offFor + Time.deltaTime : 0f;
        if (_offFor >= OffSeconds) ShowPrompt();
    }

    private void ShowPrompt()
    {
        _offFor = 0f;
        var layer = Layer();
        if (layer == null) return;
        TimeController.Instance.RequestPause(this);

        _prompt = new VisualElement();
        UiTheme.FullDim(_prompt, 0.6f);
        _prompt.pickingMode = PickingMode.Position;
        _prompt.style.flexDirection = FlexDirection.Column;

        var kicker = new Label("OFF COURSE");
        kicker.style.fontSize = 22f;
        kicker.style.letterSpacing = 6f;
        kicker.style.color = UiTheme.Coral;
        kicker.style.unityFontStyleAndWeight = FontStyle.Bold;
        var title = new Label("We're far from the channel, Captain.");
        title.style.fontSize = 40f;
        title.style.color = UiTheme.White;
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.marginTop = 6f;
        string where = Saved != null ? "checkpoint " + Saved.Number : "the start of the voyage";
        var line = new Label("Go back to " + where + ", or keep sailing and find the way yourself?");
        line.style.fontSize = 22f;
        line.style.color = UiTheme.Dim;
        line.style.marginTop = 10f;
        line.style.marginBottom = 26f;

        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        var back = new Button(() => { HidePrompt(); VoyageRestart.Request(VoyageRestart.Mode.FromCheckpoint); })
            { text = Saved != null ? "RESTART FROM CHECKPOINT " + Saved.Number : "RESTART THE VOYAGE" };
        UiTheme.WordButton(back, primary: true, size: 22f);
        var keep = new Button(() => { HidePrompt(); _snoozeUntil = Time.unscaledTime + SnoozeSeconds; }) { text = "KEEP SAILING" };
        UiTheme.WordButton(keep, primary: false, size: 22f);
        back.style.marginRight = 48f;
        row.Add(back);
        row.Add(keep);

        _prompt.Add(kicker);
        _prompt.Add(title);
        _prompt.Add(line);
        _prompt.Add(row);
        layer.Add(_prompt);
        back.Focus();
    }

    private void HidePrompt()
    {
        if (_prompt == null) return;
        _prompt.RemoveFromHierarchy();
        _prompt = null;
        if (TimeController.Instance != null) TimeController.Instance.ReleasePause(this);
    }

    // ------------------------------------------------------------ show and sound

    // Its own object at the scene root: on the director's, EmergencyCall's UI would nest under it.
    private VisualElement Layer()
    {
        if (_layer != null) return _layer;
        _layerObject = new GameObject("CheckpointsUI");
        return _layer = ScreenBanner.Layer(_layerObject, 20);
    }

    private void Banner(string kicker, string title, string line) =>
        ScreenBanner.Show(Layer(), kicker, title, line, 3f, top: 20f);

    private static void PlayBell()
    {
        var audio = AudioService.Instance;
        if (audio != null && audio.Sfx != null && audio.Sfx.TelegraphBell != null) audio.Play(audio.Sfx.TelegraphBell, AudioBus.Effects, 0.5f, 1.2f);
    }
}
