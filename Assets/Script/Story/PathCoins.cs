using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Story mode's coins: a few runs of three over the channel's centre line, clear of both harbours,
/// and most of them off it: rings round the lookout's finds and short arcs in tight water between
/// rocks (SeaFinds), so leaving the line pays. The hull picks them up by passing over them;
/// each scores (StoryScore: more for a long run without missing one) and is worth a few credits,
/// paid when the cargo arrives. A channel coin the ship has sailed past is missed: the run starts
/// again, but the coin stays, so turning back for it still counts. A hit also ends the run.
/// The count and the score sit top right under Help and Pause. Checkpoints keep which coins
/// were taken and the score. The StoryDirector adds it.
/// </summary>
public class PathCoins : MonoBehaviour
{
    public const float FirstAt = 45f;      // metres along the channel: clear of the home quay
    public const float EndClear = 60f;     // and this far short of the berth
    public const float Step = 10f;         // between coins in a run
    public const float Gap = 70f;          // between runs
    public const int RunLength = 3;
    private const float Height = 1.8f;     // above the waves
    private const float Reach = 4.5f;      // from the hull's centre line: half the beam and a little
    private const float MissedBehind = 20f; // this far astern of the ship along the channel, it was missed
    private const float BowZ = 9.3f, SternZ = -8f;
    private static readonly Color Gold = new Color(1f, 0.76f, 0.2f), Deep = new Color(0.85f, 0.52f, 0.08f);

    public static PathCoins Current { get; private set; }

    public int Total => _coins.Count;
    public int Taken { get; private set; }
    public int Score { get; private set; }
    /// <summary>Coins in a row without a miss or a hit.</summary>
    public int Run { get; private set; }
    public int Credits => Taken * StoryScore.CoinCredits;

    private sealed class Coin
    {
        public Transform T;
        public Vector3 At;
        public float Along;
        public bool Taken, Missed;
        public float TakenAt = -1f;
    }

    private readonly List<Coin> _coins = new List<Coin>();
    private Transform _root;
    private bool _built, _spoke;
    private int _progress;
    private Checkpoints.Snapshot _pending;
    private GameObject _hudObject;
    private VisualElement _hud;
    private Label _count, _score, _multiplier;

    /// <summary>Metres along a channel of <paramref name="length"/> for each coin: runs of three, clear of both ends.</summary>
    public static List<float> Layout(float length)
    {
        var at = new List<float>();
        for (float run = FirstAt; ; run += (RunLength - 1) * Step + Gap)
            for (int i = 0; i < RunLength; i++)
            {
                float d = run + i * Step;
                if (d > length - EndClear) return at;
                at.Add(d);
            }
    }

    private void Awake() => Current = this;

    private void Start()
    {
        ShipHull.OnHullHit += OnHit;
        if (GameManager.Instance != null) GameManager.Instance.OnStateChanged += OnState;
    }

    private void OnDestroy()
    {
        if (Current == this) Current = null;
        ShipHull.OnHullHit -= OnHit;
        if (GameManager.Instance != null) GameManager.Instance.OnStateChanged -= OnState;
        if (_root != null) Destroy(_root.gameObject);
        if (_hudObject != null) Destroy(_hudObject);
    }

    private void OnHit(int containers)
    {
        if (GameManager.Instance != null && GameManager.Instance.CurrentState == GameState.Playing) Run = 0;
    }

    private void OnState(GameState state)
    {
        if (state == GameState.Playing) Build();
        if (_hud != null) _hud.style.display = state == GameState.Playing ? DisplayStyle.Flex : DisplayStyle.None;
    }

    // ------------------------------------------------------------ checkpoints

    /// <summary>Puts the coins, the score and the run into a checkpoint as it is passed.</summary>
    public void Save(Checkpoints.Snapshot s)
    {
        s.CoinsTaken = new bool[_coins.Count];
        for (int i = 0; i < _coins.Count; i++) s.CoinsTaken[i] = _coins[i].Taken;
        s.Score = Score;
        s.CoinRun = Run;
    }

    /// <summary>Back at a checkpoint: the coins taken by then stay taken, the rest come back.</summary>
    public void Restore(Checkpoints.Snapshot s)
    {
        if (!_built) { _pending = s; return; }
        _pending = null;
        Taken = 0;
        for (int i = 0; i < _coins.Count; i++)
        {
            var c = _coins[i];
            c.Taken = s.CoinsTaken != null && i < s.CoinsTaken.Length && s.CoinsTaken[i];
            c.Missed = false;
            c.TakenAt = -1f;
            c.T.gameObject.SetActive(!c.Taken);
            c.T.localScale = Vector3.one;
            if (c.Taken) Taken++;
        }
        Score = s.Score;
        Run = s.CoinRun;
        _progress = 0;
        Refresh();
    }

    // ------------------------------------------------------------ building

    private void Build()
    {
        if (_built || !Channel.Ready) return;
        _built = true;
        _root = new GameObject("PathCoins").transform;

        foreach (float d in Layout(Channel.Length))
        {
            int i = Channel.IndexAt(d);
            Add(Channel.Points[i], Channel.Along[i]);
        }
        // Off the channel: at the finds and between the rocks. Never "missed" (Along < 0).
        if (SeaFinds.Current != null)
            foreach (var spot in SeaFinds.Current.CoinSpots()) Add(spot, -1f);

        BuildHud();
        if (_pending != null) Restore(_pending);
        Refresh();
    }

    private Mesh _mesh;
    private Material[] _materials;

    private void Add(Vector3 at, float along)
    {
        var go = new GameObject("Coin");
        go.transform.SetParent(_root, false);
        if (_mesh == null)
        {
            var m = new LowPolyMesh();
            m.Ellipsoid(Vector3.zero, new Vector3(1.3f, 0.16f, 1.3f), 16, 6, Gold, 0f, 1f);
            m.Ellipsoid(Vector3.zero, new Vector3(0.8f, 0.23f, 0.8f), 12, 4, Deep, 0f, 1f);
            _materials = m.AttachTo(go, castShadows: false, glow: 0.7f).sharedMaterials;
            _mesh = go.GetComponent<MeshFilter>().sharedMesh;
        }
        else
        {
            go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = _materials;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        var coin = new Coin { T = go.transform, At = at, Along = along };
        _coins.Add(coin);
        Place(coin, 0f);
    }

    private void Place(Coin c, float rise)
    {
        float t = Time.time;
        float wave = SeaWaves.HeightAt(c.At.x, c.At.z);
        // Stood on edge (the disc's flat sides face along x), turning slowly, bobbing a little.
        float phase = (c.Along >= 0f ? c.Along : c.At.x + c.At.z) * 0.13f;
        c.T.position = new Vector3(c.At.x, wave + Height + Mathf.Sin(t * 2f + phase) * 0.2f + rise, c.At.z);
        c.T.rotation = Quaternion.Euler(0f, t * 120f + phase * 57f, 90f);
    }

    // ------------------------------------------------------------ sailing

    private void Update()
    {
        if (!_built) return;
        var gm = GameManager.Instance;
        var nav = ShipNavigator.Instance;
        bool sailing = gm != null && nav != null && gm.CurrentState == GameState.Playing && !DockingPilot.Busy;

        float shipAlong = -1f;
        Vector3 bow = Vector3.zero, stern = Vector3.zero;
        if (sailing)
        {
            Transform ship = nav.transform;
            bow = Channel.Flat(ship.position + ship.forward * BowZ);
            stern = Channel.Flat(ship.position + ship.forward * SternZ);
            _progress = Channel.Nearest(ship.position, Mathf.Max(0, _progress - 3), out float off);
            shipAlong = off < 50f ? Channel.Along[_progress] : -1f;
        }

        foreach (var c in _coins)
        {
            if (c.Taken)
            {
                if (c.TakenAt < 0f) continue;
                // Up and away, shrinking, then gone.
                float k = (Time.time - c.TakenAt) / 0.45f;
                if (k >= 1f) { c.T.gameObject.SetActive(false); c.TakenAt = -1f; continue; }
                c.T.localScale = Vector3.one * (1f - k * 0.8f);
                Place(c, k * 3f);
                continue;
            }
            Place(c, 0f);
            if (!sailing) continue;

            if (Reaches(bow, stern, c.At)) { Take(c); continue; }
            if (!c.Missed && c.Along >= 0f && shipAlong >= 0f && shipAlong - c.Along > MissedBehind)
            {
                c.Missed = true;
                Run = 0;
                Refresh();
            }
        }
    }

    /// <summary>True when <paramref name="at"/> is within reach of the hull's centre line (bow to stern), flat.</summary>
    public static bool Reaches(Vector3 bow, Vector3 stern, Vector3 at, float reach = Reach)
    {
        at.y = 0f; bow.y = 0f; stern.y = 0f;
        Vector3 line = bow - stern;
        float t = line.sqrMagnitude > 0.0001f ? Mathf.Clamp01(Vector3.Dot(at - stern, line) / line.sqrMagnitude) : 0f;
        return (stern + line * t - at).sqrMagnitude <= reach * reach;
    }

    private void Take(Coin c)
    {
        c.Taken = true;
        c.TakenAt = Time.time;
        Score += StoryScore.ForCoin(Run);
        Run++;
        Taken++;
        for (int i = 0; i < 3; i++)
            Spray.Wisp(c.T.position + Random.insideUnitSphere * 0.6f, Gold, 1f);
        UiSounds.Play(UiSounds.Coin(Run), 0.45f);
        Refresh();

        if (!_spoke && CrewVoice.Instance != null)
        {
            _spoke = true;
            CrewVoice.Instance.SpeakFrom(RadioChannel.Lookout,
                new[] { "Gold on the water, Captain! Stay on the channel and we collect it. Over." },
                ShipTelemetry.Capture(), "coins:first");
        }
    }

    // ------------------------------------------------------------ the count, top right

    private void BuildHud()
    {
        _hudObject = new GameObject("PathCoinsUI");
        var layer = ScreenBanner.Layer(_hudObject, 5);
        if (layer == null) return;
        _hud = new VisualElement { pickingMode = PickingMode.Ignore };
        _hud.style.position = Position.Absolute;
        _hud.style.top = 52f;
        _hud.style.right = 28f;
        _hud.style.alignItems = Align.FlexEnd;
        UiTheme.Shadow(_hud);

        var row = new VisualElement { pickingMode = PickingMode.Ignore };
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        var dot = new VisualElement { pickingMode = PickingMode.Ignore };
        dot.style.width = 14f; dot.style.height = 14f;
        dot.style.marginRight = 8f;
        dot.style.backgroundColor = Gold;
        dot.style.borderTopLeftRadius = dot.style.borderTopRightRadius = dot.style.borderBottomLeftRadius = dot.style.borderBottomRightRadius = 7f;
        _count = new Label { pickingMode = PickingMode.Ignore };
        _count.style.fontSize = 20f;
        _count.style.color = UiTheme.White;
        _count.style.unityFontStyleAndWeight = FontStyle.Bold;
        row.Add(dot);
        row.Add(_count);

        var line = new VisualElement { pickingMode = PickingMode.Ignore };
        line.style.flexDirection = FlexDirection.Row;
        line.style.alignItems = Align.Center;
        _multiplier = new Label { pickingMode = PickingMode.Ignore };
        _multiplier.style.fontSize = 15f;
        _multiplier.style.color = UiTheme.Peach;
        _multiplier.style.unityFontStyleAndWeight = FontStyle.Bold;
        _multiplier.style.marginRight = 8f;
        _score = new Label { pickingMode = PickingMode.Ignore };
        _score.style.fontSize = 15f;
        _score.style.letterSpacing = 2f;
        _score.style.color = UiTheme.Dim;
        line.Add(_multiplier);
        line.Add(_score);

        _hud.Add(row);
        _hud.Add(line);
        layer.Add(_hud);
        bool atSea = GameManager.Instance != null && GameManager.Instance.CurrentState == GameState.Playing;
        _hud.style.display = atSea ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private void Refresh()
    {
        if (_count == null) return;
        _count.text = Taken + " / " + Total;
        _score.text = "SCORE " + Score;
        int x = StoryScore.Multiplier(Run);
        _multiplier.text = x > 1 ? "x" + x : "";
    }
}
