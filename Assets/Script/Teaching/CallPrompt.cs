using System;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// An order the Captain must give in time, as the story encounters ask for it (KrakenFight,
/// PirateChase): a tag ("KRAKEN"), the order big ("HARD A PORT!"), a hint line, and a line of
/// time that shortens. Time slows while it is up, and its clock stops while the radio records or
/// decodes, so the speech system's delay never costs the player (as in EmergencyCall). Orders
/// arrive through LanguageLog.OnOrder and are judged by the asker; the outcome goes to
/// emergencies.csv (ResearchLog) and the voyage report (LanguageLog), like an emergency.
/// </summary>
public class CallPrompt : MonoBehaviour
{
    public bool Running { get; private set; }

    private Func<ShipAction, EmergencyCall.Response> _judge;
    private Action<bool> _done;   // true: in time
    private Action _wrong;
    private float _clock, _window, _started;
    private int _tries;
    private string _tag, _call;
    private bool _slow;

    private VisualElement _layer, _root, _bar;
    private Label _tagLabel, _callLabel, _hint;
    private GameObject _layerObject;

    public static CallPrompt Create(Transform parent)
    {
        var go = new GameObject("Call Prompt");
        go.transform.SetParent(parent, false);
        return go.AddComponent<CallPrompt>();
    }

    private void OnEnable() => LanguageLog.OnOrder += HandleOrder;

    private void OnDisable()
    {
        LanguageLog.OnOrder -= HandleOrder;
        if (Running) Stop();
    }

    private void OnDestroy()
    {
        if (_layerObject != null) Destroy(_layerObject);
    }

    /// <summary>
    /// Asks for <paramref name="call"/> within <paramref name="window"/> seconds. <paramref name="judge"/>
    /// decides each order; <paramref name="done"/> gets true when one saves it, false when time runs out.
    /// </summary>
    public void Ask(string tag, string call, string hint, float window, Func<ShipAction, EmergencyCall.Response> judge,
                    Action<bool> done, Action wrong = null, bool slowTime = true)
    {
        if (Running) Stop();
        Running = true;
        _tag = tag; _call = call;
        _judge = judge; _done = done; _wrong = wrong;
        _window = window; _clock = 0f; _tries = 0;
        _started = Time.unscaledTime;
        _slow = slowTime;
        if (_slow && TimeController.Instance != null) TimeController.Instance.SetEmergency(true);
        ScreenBanner.ClearAll(); // nothing covers the order
        Show(tag, call, hint);
    }

    /// <summary>Takes the prompt down without an outcome (the encounter moved on).</summary>
    public void Cancel()
    {
        if (Running) Stop();
    }

    private void Update()
    {
        if (!Running) return;
        bool atSea = GameManager.Instance != null && GameManager.Instance.CurrentState == GameState.Playing;
        if (!atSea) { Stop(); return; }
        bool radioBusy = TimeController.Instance != null && TimeController.Instance.RadioBusy;
        bool paused = TimeController.Instance != null && TimeController.Instance.IsPaused;
        if (!radioBusy && !paused) _clock += Time.unscaledDeltaTime;
        if (_bar != null)
        {
            _bar.style.width = Length.Percent(Mathf.Clamp01(1f - _clock / _window) * 100f);
            _bar.style.backgroundColor = radioBusy ? HudLook.BrassDim : HudLook.Danger;
        }
        if (_clock >= _window) Finish(false, false);
    }

    private void HandleOrder(ShipAction action, bool standard)
    {
        if (!Running || _judge == null) return;
        _tries++;
        var verdict = _judge(action);
        if (verdict == EmergencyCall.Response.WrongWay) { _wrong?.Invoke(); return; }
        if (verdict == EmergencyCall.Response.Saves) Finish(true, standard);
    }

    private void Finish(bool inTime, bool standard)
    {
        int reactionMs = Mathf.RoundToInt((Time.unscaledTime - _started) * 1000f);
        var done = _done;
        string tag = _tag, call = _call;
        float window = _window;
        int tries = _tries;
        Stop();
        LanguageLog.Emergency(inTime);
        ResearchLog.Emergency(tag, 0f, window, inTime ? "saved" : "hit", inTime ? reactionMs : -1, tries, inTime ? (standard ? "1" : "0") : "", call);
        done?.Invoke(inTime);
    }

    private void Stop()
    {
        Running = false;
        if (_slow && TimeController.Instance != null) TimeController.Instance.SetEmergency(false);
        if (_root != null) _root.style.display = DisplayStyle.None;
    }

    // ------------------------------------------------------------------ the banner

    private void Show(string tag, string call, string hint)
    {
        if (_root == null) Build();
        if (_root == null) return;
        _tagLabel.text = tag;
        _callLabel.text = call.ToUpperInvariant() + "!";
        string key = FindAnyObjectByType<RadioMic>() is RadioMic mic ? mic.OrderKeyName : "T";
        _hint.text = (string.IsNullOrEmpty(hint) ? "" : hint + "  ·  ") + "hold " + key + " and say it";
        _bar.style.width = Length.Percent(100f);
        _root.style.display = DisplayStyle.Flex;
    }

    // Its own object at the scene root, so it never nests under another document.
    private void Build()
    {
        _layerObject = new GameObject("CallPromptUI");
        _layer = ScreenBanner.Layer(_layerObject, 6);
        if (_layer == null) return;

        _root = new VisualElement { pickingMode = PickingMode.Ignore };
        _root.style.position = Position.Absolute;
        _root.style.left = 0; _root.style.right = 0; _root.style.top = Length.Percent(17f);
        _root.style.alignItems = Align.Center;

        _tagLabel = HudLook.Words("", 20f, HudLook.Danger);
        _tagLabel.style.letterSpacing = 8f;
        _tagLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        _callLabel = HudLook.Words("", 72f, HudLook.Text);
        _callLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        _callLabel.style.letterSpacing = 3f;
        _callLabel.style.textShadow = new TextShadow { offset = new Vector2(0f, 3f), blurRadius = 8f, color = new Color(0.05f, 0.03f, 0.08f, 0.85f) };
        _hint = HudLook.Words("", 22f, HudLook.TextDim);
        _hint.style.textShadow = new TextShadow { offset = new Vector2(0f, 2f), blurRadius = 6f, color = new Color(0.05f, 0.03f, 0.08f, 0.85f) };

        var track = new VisualElement { pickingMode = PickingMode.Ignore };
        track.style.width = 560f; track.style.height = 4f; track.style.marginTop = 14f;
        track.style.backgroundColor = new Color(1f, 1f, 1f, 0.2f);
        _bar = new VisualElement { pickingMode = PickingMode.Ignore };
        _bar.style.height = 4f;
        _bar.style.backgroundColor = HudLook.Danger;
        track.Add(_bar);

        _root.Add(_tagLabel);
        _root.Add(_callLabel);
        _root.Add(_hint);
        _root.Add(track);
        _root.style.display = DisplayStyle.None;
        _layer.Add(_root);
    }
}
