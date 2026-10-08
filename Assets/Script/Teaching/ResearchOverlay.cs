using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

/// <summary>
/// For the researcher, not the player: F9 shows the radio timings on screen (the last call:
/// speech-to-text, the crew's reply, until the ship answered; and the averages this run),
/// the session id and where the CSV files are saved. It also closes calls that never got a
/// reply and writes the open call when the game quits. Made once, lives across scenes.
/// </summary>
public class ResearchOverlay : MonoBehaviour
{
    private const double ReplyTimeout = 20.0; // seconds before a call with no reply is written anyway

    private VisualElement _box;
    private Label _text;
    private bool _shown;
    private float _nextRefresh;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Create()
    {
        var go = new GameObject("ResearchOverlay");
        DontDestroyOnLoad(go);
        go.AddComponent<ResearchOverlay>();
    }

    private void Update()
    {
        ResearchLog.Tick(ReplyTimeout);

        var kb = Keyboard.current;
        if (kb != null && kb.f9Key.wasPressedThisFrame) Toggle();
        if (_shown && Time.unscaledTime >= _nextRefresh)
        {
            _nextRefresh = Time.unscaledTime + 0.25f;
            _text.text = Describe();
        }
    }

    private void OnApplicationQuit() => ResearchLog.Flush();

    private void Toggle()
    {
        if (_box == null) Build();
        if (_box == null) return;
        _shown = !_shown;
        _box.style.display = _shown ? DisplayStyle.Flex : DisplayStyle.None;
        _nextRefresh = 0f;
    }

    private void Build()
    {
        var host = SceneLoader.Instance != null ? SceneLoader.Instance.GetComponent<UIDocument>() : null;
        if (host == null || host.panelSettings == null) return;
        var doc = gameObject.AddComponent<UIDocument>();
        doc.panelSettings = host.panelSettings;
        doc.sortingOrder = 1000; // over everything
        var root = doc.rootVisualElement;
        root.pickingMode = PickingMode.Ignore;
        root.style.position = Position.Absolute;
        root.style.left = 0; root.style.right = 0; root.style.top = 0; root.style.bottom = 0;

        _box = new VisualElement { pickingMode = PickingMode.Ignore };
        _box.style.position = Position.Absolute;
        _box.style.right = 16; _box.style.top = 84; // under HELP and PAUSE, clear of the gauges
        _box.style.paddingLeft = 14; _box.style.paddingRight = 14; _box.style.paddingTop = 10; _box.style.paddingBottom = 10;
        _box.style.backgroundColor = new Color(0.96f, 0.93f, 0.86f, 0.95f); // paper
        _box.style.borderLeftWidth = 3;
        _box.style.borderLeftColor = new Color(0.05f, 0.42f, 0.38f);
        _text = new Label { pickingMode = PickingMode.Ignore };
        _text.style.color = new Color(0.09f, 0.14f, 0.25f); // navy ink
        _text.style.fontSize = 16;
        _text.style.whiteSpace = WhiteSpace.Normal;
        _text.style.maxWidth = 620;
        _box.Add(_text);
        _box.style.display = DisplayStyle.None;
        root.Add(_box);
    }

    private static string Describe()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("RADIO TIMING  (F9 to hide)\n");
        var t = ResearchLog.Last;
        if (t == null) sb.Append("No call yet this run.\n");
        else
        {
            sb.Append("Last: \"").Append(t.Heard).Append("\"  →  ").Append(t.Outcome).Append(" (").Append(t.Route).Append(")\n");
            sb.Append("  speech to text ").Append(Ms(t.SttMs))
              .Append("   ·   crew reply ").Append(Ms(t.ReplyMs))
              .Append("   ·   total ").Append(Ms(t.TotalMs))
              .Append("   ·   ship ").Append(Ms(t.ShipMs)).Append('\n');
        }
        sb.Append("Average of ").Append(ResearchLog.SessionCount).Append(" calls: speech to text ")
          .Append(Ms(ResearchLog.Average(x => x.SttMs)))
          .Append("   ·   total ").Append(Ms(ResearchLog.Average(x => x.TotalMs)))
          .Append("   ·   ship ").Append(Ms(ResearchLog.Average(x => x.ShipMs))).Append('\n');
        string who = ResearchLog.Participant;
        sb.Append("Session ").Append(ResearchLog.SessionId).Append(who.Length > 0 ? "   ·   participant " + who : "").Append('\n');
        sb.Append("Saved to ").Append(ResearchLog.Folder);
        return sb.ToString();
    }

    private static string Ms(int v) => v >= 0 ? v + " ms" : "–";
}
