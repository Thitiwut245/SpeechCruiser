using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

/// <summary>
/// The voyage report after a delivery or a sinking, as a page of the ship's log: what was
/// delivered, what it paid, hits, time, the repair bill and one plain-English tip, under a
/// rubber stamp (DELIVERED, COMPLETE or LOST). It waits a moment so the player sees the
/// arrival (or the wreck), then waits for Continue (button, Enter or Space) instead of a timer.
/// Lives next to ShipyardManager and uses the same UIDocument.
/// </summary>
public class MissionResultsPanel : MonoBehaviour
{
    [SerializeField] private UIDocument _document;

    [Tooltip("Real seconds between the end of the voyage and the report appearing.")]
    [SerializeField] private float ShowDelay = 1.4f;

    [Tooltip("Real seconds before the report after a sinking, so the wreck can go down first.")]
    [SerializeField] private float ShowDelayAfterSinking = 2.6f;

    [Tooltip("Real seconds after the report appears before Continue works, so a held key can't skip it.")]
    [SerializeField] private float ContinueDelay = 0.6f;

    private VisualElement _backdrop;
    private VisualElement _panel;
    private Label _title;
    private Label _subtitle;
    private VisualElement _rows;
    private Label _totalValue;
    private Label _tip;
    private Label _stamp;
    private Label _voyage;
    private Button _continue;

    private float _showAt = -1f;
    private float _continueAt = float.MaxValue;
    private bool _shown;

    public bool IsShown => _shown;

    /// <summary>While this says true the report waits (a story mission's closing dialogue).</summary>
    public static System.Func<bool> Hold;

    private void Start()
    {
        if (_document == null) _document = GetComponent<UIDocument>();
        var root = _document.rootVisualElement;

        _backdrop = root.Q<VisualElement>("results-backdrop");
        _panel = root.Q<VisualElement>("results-panel");
        _title = root.Q<Label>("results-title");
        _subtitle = root.Q<Label>("results-subtitle");
        _rows = root.Q<VisualElement>("results-rows");
        _totalValue = root.Q<Label>("results-total-value");
        _tip = root.Q<Label>("results-tip");
        _stamp = root.Q<Label>("results-stamp");
        _voyage = root.Q<Label>("results-voyage");
        _continue = root.Q<Button>("results-continue-btn");
        _continue.clicked += Continue;

        if (GameManager.Instance != null) GameManager.Instance.OnStateChanged += HandleStateChanged;
        _mic = FindAnyObjectByType<RadioMic>();
        if (_mic != null) _mic.OnRadioStatus += HandleRadioStatus;
    }

    private void OnDestroy()
    {
        if (GameManager.Instance != null) GameManager.Instance.OnStateChanged -= HandleStateChanged;
        if (_mic != null) _mic.OnRadioStatus -= HandleRadioStatus; // RadioMic outlives this scene
    }

    private RadioMic _mic;

    // The radio is off on the report: say why instead of only beeping, then put the tip back.
    private string _tipText;
    private float _restoreTipAt = -1f;

    private void HandleRadioStatus(RadioStatus status, string text)
    {
        if (!_shown || status != RadioStatus.NotAtSea || _tip == null) return;
        if (_restoreTipAt < 0f) _tipText = _tip.text;
        _tip.text = text;
        _restoreTipAt = Time.unscaledTime + 4f;
    }

    private void HandleStateChanged(GameState state)
    {
        if (state == GameState.Victory || state == GameState.GameOver)
        {
            _showAt = Time.unscaledTime + (state == GameState.GameOver ? ShowDelayAfterSinking : ShowDelay);
        }
        else
        {
            _showAt = -1f;
            Hide();
        }
    }

    private void Update()
    {
        if (_restoreTipAt > 0f && Time.unscaledTime >= _restoreTipAt)
        {
            _restoreTipAt = -1f;
            if (_tip != null && _tipText != null) _tip.text = _tipText;
        }

        if (!_shown && _showAt > 0f && Time.unscaledTime >= _showAt && !(Hold != null && Hold()))
        {
            _showAt = -1f;
            Show(GameSession.Instance != null ? GameSession.Instance.LastResult : null);
        }

        if (!_shown || Time.unscaledTime < _continueAt) return;
        var kb = Keyboard.current;
        if (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame))
            Continue();
    }

    public void Show(MissionResult result)
    {
        if (result == null) return;

        _shown = true;
        _continueAt = Time.unscaledTime + ContinueDelay;
        _panel.EnableInClassList("failed", !result.Delivered);
        var mission = StoryProgress.ActiveMission;
        if (_stamp != null) _stamp.text = !result.Delivered ? "LOST" : mission != null ? "COMPLETE" : "DELIVERED";
        if (_voyage != null)
        {
            int voyage = GameSession.Instance != null ? GameSession.Instance.VoyagesStarted : 0;
            _voyage.text = (mission != null ? mission.Label + "  ·  " : "") + (voyage > 0 ? "Voyage no. " + voyage : "");
        }
        _rows.Clear();

        var story = StoryProgress.ActiveMission;
        string job = story != null ? story.Label + ": " + story.Title
                                   : result.JobName.Substring(0, 1) + result.JobName.Substring(1).ToLowerInvariant() + " run";
        _continue.text = story == null ? "BACK TO THE SHIPYARD  (Enter)"
                       : result.Delivered ? "BACK TO THE STORY  (Enter)" : "TRY AGAIN  (Enter)";
        if (result.Delivered)
        {
            _title.text = story != null ? "MISSION COMPLETE" : "CARGO DELIVERED";
            _subtitle.text = result.CargoDelivered == result.CargoLoaded
                ? job + ". Every container arrived."
                : job + ". Some containers went overboard.";
            AddRow("Containers delivered", result.CargoDelivered + " / " + result.CargoLoaded,
                   result.CargoDelivered == result.CargoLoaded ? "good" : null);
            AddRow("Pay", "+$" + result.Pay + (result.Pay < result.JobReward ? "  (of $" + result.JobReward + ")" : ""), "good");
        }
        else
        {
            _title.text = "SHIP LOST";
            _subtitle.text = job + ". The ship sank and was towed back to port.";
            AddRow("Cargo lost", result.CargoLoaded + " containers", "bad");
            AddRow("Towing fee", "-$" + result.TowingFee, result.TowingFee > 0 ? "bad" : null);
        }

        AddRow("Hits", result.Hits == 0 ? "None" : result.Hits + (result.MineHits > 0 ? "  (" + result.MineHits + " mine" + (result.MineHits == 1 ? "" : "s") + ")" : ""),
               result.Hits == 0 ? "good" : "bad");
        if (result.Delivered && result.ParSeconds > 0f)
        {
            bool onTime = result.OnTimeBonus > 0;
            AddRow("Time at sea", FormatTime(result.Seconds) + "  (par " + FormatTime(result.ParSeconds) + ")", onTime ? "good" : null);
            AddRow("On-time bonus", onTime ? "+$" + result.OnTimeBonus : "missed", onTime ? "good" : null);
        }
        else
        {
            AddRow("Time at sea", FormatTime(result.Seconds), null);
        }
        AddRow("Hull", result.HullAfter + " / " + result.HullMax +
                       (result.RepairCost > 0 ? "   repair costs $" + result.RepairCost : "   no repairs needed"),
               result.RepairCost > 0 ? "bad" : "good");

        int change = result.CreditsAfter - result.CreditsBefore;
        _totalValue.text = "$" + result.CreditsAfter + "  (" + (change >= 0 ? "+$" + change : "-$" + (-change)) + ")";
        _tip.text = TipFor(result);
        if (story != null && result.Delivered && result.Hits == 0 && (result.OnTimeBonus > 0 || result.ParSeconds <= 0f))
            _tip.text = "Clean run and on time. Well done, Captain!";

        _backdrop.style.display = DisplayStyle.Flex;
        // Next frame, so the opacity transition runs from 0.
        _backdrop.schedule.Execute(() => _backdrop.AddToClassList("shown"));
        _continue.Focus();
    }

    private void Hide()
    {
        _shown = false;
        _continueAt = float.MaxValue;
        if (_backdrop == null) return;
        _backdrop.RemoveFromClassList("shown");
        _backdrop.style.display = DisplayStyle.None;
    }

    private void AddRow(string label, string value, string tone)
    {
        var row = new VisualElement();
        row.AddToClassList("results-row");

        var l = new Label(label);
        l.AddToClassList("results-row-label");
        var v = new Label(value);
        v.AddToClassList("results-row-value");
        if (tone != null) v.AddToClassList(tone);

        row.Add(l);
        row.Add(v);
        _rows.Add(row);
    }

    private static string FormatTime(float seconds)
    {
        int s = Mathf.Max(0, Mathf.RoundToInt(seconds));
        return (s / 60) + ":" + (s % 60).ToString("00");
    }

    /// <summary>One short tip in plain English, picked from what went wrong.</summary>
    public static string TipFor(MissionResult r)
    {
        if (!r.Delivered && r.MineHits > 0)
            return "Tip: mines blink red and knock off two containers. Ask Finn \"any danger?\" and steer wide around them.";
        if (!r.Delivered)
            return "Tip: go slower near rocks. At \"slow ahead\" you have much more time to turn.";
        if (r.Hits > 0)
            return "Tip: turn as soon as Finn warns you. The ship needs a few seconds to swing around.";
        if (r.OnTimeBonus == 0 && r.ParSeconds > 0f)
            return "Clean run! Beat the par time for a bonus: use full speed on open water, or upgrade the engine.";
        return "Clean run and on time! A harder job pays more.";
    }

    private void Continue()
    {
        if (!_shown || GameManager.Instance == null) return;
        // A finished story mission goes back to the story screen; a lost one is tried again.
        var result = GameSession.Instance != null ? GameSession.Instance.LastResult : null;
        if (StoryProgress.ActiveMission != null && result != null && result.Delivered)
        {
            if (SceneLoader.Instance.IsLoading) return;
            SceneLoader.Instance.Load(SceneId.Menu);
        }
        // Another load still running: stay on the report so Continue can be pressed again.
        else if (!GameManager.Instance.ReturnToShipyard()) return;
        _shown = false;
        _continue.SetEnabled(false);
    }
}
