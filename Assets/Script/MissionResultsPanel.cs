using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

/// <summary>
/// The voyage report after a delivery or a sinking, on paper (navy ink on a full sand page,
/// projector-safe): what arrived (or was lost), the hits, the time, the credits, and one
/// plain-English tip, by a stamp (DELIVERED, COMPLETE or LOST). It waits a moment so the player sees the
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
    private Button _checkpoint;

    private float _showAt = -1f;
    private float _continueAt = float.MaxValue;
    private bool _shown;

    public bool IsShown => _shown;

    /// <summary>While this says true the report waits (a story mission's closing dialogue).</summary>
    public static System.Func<bool> Hold;

    /// <summary>While above zero the report waits too (the crane unloading the cargo).</summary>
    public static int Waiting;

    private void Start()
    {
        if (_document == null) _document = GetComponent<UIDocument>();
        var root = _document.rootVisualElement;
        Waiting = 0; // a new scene: nothing is unloading

        _backdrop = root.Q<VisualElement>("results-backdrop");
        UiTheme.Paper(_backdrop);
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
        // A story ship lost after a checkpoint can go back there instead of starting over.
        _checkpoint = new Button(() => { if (_shown) { _shown = false; VoyageRestart.Request(VoyageRestart.Mode.FromCheckpoint); } });
        foreach (var c in _continue.GetClasses()) _checkpoint.AddToClassList(c);
        _checkpoint.style.display = DisplayStyle.None;
        _continue.parent.Insert(_continue.parent.IndexOf(_continue), _checkpoint);

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
        if (state == GameState.Playing)
        {
            LanguageLog.Begin(); // a voyage starts: a fresh language report
            ResearchLog.BeginVoyage();
        }
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

        if (!_shown && _showAt > 0f && Time.unscaledTime >= _showAt && !(Hold != null && Hold()) && Waiting <= 0)
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
        bool checkpoint = story != null && !result.Delivered && Checkpoints.Saved != null;
        _checkpoint.style.display = checkpoint ? DisplayStyle.Flex : DisplayStyle.None;
        if (checkpoint) _checkpoint.text = "RESTART FROM CHECKPOINT " + Checkpoints.Saved.Number;
        _continue.text = story == null ? "BACK TO THE SHIPYARD"
                       : result.Delivered ? "BACK TO THE STORY" : "TRY AGAIN";
        if (result.Delivered)
        {
            _title.text = story != null ? "MISSION COMPLETE" : "CARGO DELIVERED";
            _subtitle.text = result.CargoDelivered == result.CargoLoaded
                ? job + ". Every container arrived."
                : job + ". Some containers went overboard.";
            AddRow("Containers delivered", result.CargoDelivered + " / " + result.CargoLoaded,
                   result.CargoDelivered == result.CargoLoaded ? "good" : null);
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
            AddRow("Time at sea", FormatTime(result.Seconds) + "  (par " + FormatTime(result.ParSeconds) + ")" +
                                  (onTime ? "  +$" + result.OnTimeBonus : ""), onTime ? "good" : null);
        }
        else
        {
            AddRow("Time at sea", FormatTime(result.Seconds), null);
        }

        var language = LanguageLog.Read();
        AddLanguageRows(language);
        ResearchLog.EndVoyage(result);
        string congrats = AddProgressRows(language, result);

        int change = result.CreditsAfter - result.CreditsBefore;
        _totalValue.text = "$" + result.CreditsAfter + "  (" + (change >= 0 ? "+$" + change : "-$" + (-change)) + ")";
        _totalValue.EnableInClassList("bad", change < 0);
        _tip.text = TipFor(result);
        if (story != null && result.Delivered && result.Hits == 0 && (result.OnTimeBonus > 0 || result.ParSeconds <= 0f))
            _tip.text = "Clean run and on time. Well done, Captain!";
        if (congrats != null) _tip.text = congrats;

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

    private void AddRow(string label, string value, string tone, bool compact = false)
    {
        var row = new VisualElement();
        row.AddToClassList("results-row");
        if (compact) row.AddToClassList("compact");

        var l = new Label(label);
        l.AddToClassList("results-row-label");
        var v = new Label(value);
        v.AddToClassList("results-row-value");
        if (tone != null) v.AddToClassList(tone);

        row.Add(l);
        row.Add(v);
        _rows.Add(row);
    }

    /// <summary>
    /// Radio English on this voyage (the in-game evaluation): calls understood, orders in
    /// standard phrases, "say again"s, how fast the Captain answered a danger warning, and
    /// the standard phrases to practise.
    /// </summary>
    private void AddLanguageRows(LanguageLog.Summary s)
    {
        if (s.Calls == 0) return;
        AddHeading("RADIO ENGLISH");
        AddRow("Calls understood", s.Understood + " / " + s.Calls + "  (" + s.UnderstoodPercent + "%)",
               s.UnderstoodPercent >= 80 ? "good" : s.UnderstoodPercent < 50 ? "bad" : null, true);
        if (s.Orders > 0)
            AddRow("Standard orders", s.StandardOrders + " / " + s.Orders, s.StandardOrders == s.Orders ? "good" : null, true);
        if (s.Unclear > 0) AddRow("\"Say again\"", s.Unclear.ToString(), null, true);
        if (s.AverageReaction >= 0f)
            AddRow("Order after a warning", s.AverageReaction.ToString("0.0") + " s average", s.AverageReaction <= 4f ? "good" : null, true);
        if (s.Emergencies > 0)
            AddRow("Emergency calls", s.EmergenciesSaved + " / " + s.Emergencies + " in time", s.EmergenciesSaved == s.Emergencies ? "good" : null, true);
        if (s.WordsAsked > 0) AddRow("Words asked about", s.WordsAsked.ToString(), "good", true);
        if (s.Practise != null && s.Practise.Count > 0)
            AddRow("Practise saying", "\"" + string.Join("\", \"", s.Practise) + "\"", null, true);
    }

    /// <summary>
    /// The voyage's practice goes into the Captain's record (Progression): certificates earned,
    /// a promotion, or else the nearest certificate and how far along it is. Returns the crew's
    /// congratulations for the tip line, or null.
    /// </summary>
    private string AddProgressRows(LanguageLog.Summary s, MissionResult r)
    {
        var session = GameSession.Instance;
        if (session != null) Progression.Begin(session.EngineLevel, session.HelmLevel, PlayerPrefs.GetInt("tutorial_done", 0) == 1);
        var outcome = Progression.EndVoyage(new Progression.Voyage
        {
            EngineOrders = s.EngineStandard, HelmOrders = s.HelmStandard, HelmDegrees = s.HelmDegrees,
            Questions = s.Questions, CrewTalk = s.CrewTalk, Emergencies = s.EmergenciesSaved,
            Clean = r.Delivered && r.Hits == 0,
            Clear = s.Calls >= 5 && s.UnderstoodPercent >= 80,
        });

        AddHeading("RECORD");
        foreach (var c in outcome.Earned)
            AddRow("Certificate earned", c.Title + "  ·  " + Progression.UpgradeName(c.For).ToLowerInvariant() + " Mk " + (c.Level == 2 ? "II" : "III") + " can be fitted", "good", true);
        if (outcome.PromotedTo != null) AddRow("Promoted", outcome.PromotedTo, "good", true);
        if (outcome.Earned.Count == 0)
        {
            var next = Progression.Closest();
            if (next != null)
            {
                Progression.Need need = next.Needs[0];
                foreach (var n in next.Needs) if (Progression.Count(n.Skill) < n.Target) { need = n; break; }
                AddRow("Next: " + next.Title, Progression.SkillText(need.Skill).ToLowerInvariant() + "  " + Mathf.Min(Progression.Count(need.Skill), need.Target) + " / " + need.Target, null, true);
            }
        }
        if (outcome.PromotedTo != null) return "Harris: \"Congratulations, " + outcome.PromotedTo + "! Well earned. The crew are proud of you.\"";
        if (outcome.Earned.Count > 0) return "Finn: \"A new certificate, Sir! Fit the " + Progression.UpgradeName(outcome.Earned[0].For).ToLowerInvariant() + " in the shipyard: UPGRADE SHIP.\"";
        return null;
    }

    private void AddHeading(string text)
    {
        var h = new Label(text);
        h.AddToClassList("results-heading");
        _rows.Add(h);
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
