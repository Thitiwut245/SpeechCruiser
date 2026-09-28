using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The first-voyage harbour drill. Nothing pauses: each step asks the player to do one
/// thing for real (a radio check, an engine order, a turn...) and moves on when the ship
/// or crew show it worked. The coach panel sits on the left, clear of the ship, and the
/// HUD part being taught pulses. Finishing or skipping is saved; the pause menu replays it.
/// Words are plain English first, ship words second, because many players are beginners.
/// </summary>
public class TutorialManager : MonoBehaviour
{
    public static TutorialManager Instance { get; private set; }

    [Tooltip("After this many failed transmissions in one step, the coach shows a hint.")]
    [SerializeField] private int FailuresBeforeHint = 2;

    private sealed class Step
    {
        public string Title;
        public string Text;
        public string Try;
        public string Highlight;
        /// <summary>Button label for read-only steps; null when the step completes by doing it.</summary>
        public string NextLabel;
        public Func<bool> Done;
    }

    private List<Step> _steps;
    private int _index = -1;
    private bool _active;

    // What happened since the current step started.
    private bool _heard;
    private readonly HashSet<ShipAction> _executed = new HashSet<ShipAction>();
    private int _failures;

    private RadioMic _mic;
    private LevelMap _map;

    public bool IsActive => _active;

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        _mic = FindAnyObjectByType<RadioMic>();
        _map = FindAnyObjectByType<LevelMap>();

        if (GameManager.Instance != null) GameManager.Instance.OnStateChanged += HandleStateChanged;
        if (CommandRouter.Instance != null) CommandRouter.Instance.OnActionExecuted += HandleActionExecuted;
        if (_mic != null) _mic.OnRadioStatus += HandleRadioStatus;
        if (ShipHUD.Instance != null)
        {
            ShipHUD.Instance.CoachNextPressed += HandleNext;
            ShipHUD.Instance.CoachSkipPressed += Skip;
        }
    }

    private void OnDestroy()
    {
        if (GameManager.Instance != null) GameManager.Instance.OnStateChanged -= HandleStateChanged;
        if (CommandRouter.Instance != null) CommandRouter.Instance.OnActionExecuted -= HandleActionExecuted;
        if (_mic != null) _mic.OnRadioStatus -= HandleRadioStatus; // RadioMic outlives this scene
        if (ShipHUD.Instance != null)
        {
            ShipHUD.Instance.CoachNextPressed -= HandleNext;
            ShipHUD.Instance.CoachSkipPressed -= Skip;
        }
        if (Instance == this) Instance = null;
    }

    private void HandleStateChanged(GameState state)
    {
        if (state == GameState.Playing && !GameSession.Instance.HasSeenTutorial) Begin();
        else if (state != GameState.Playing && _active) Stop(markDone: false);
    }

    /// <summary>Runs the drill again (pause menu).</summary>
    public void Replay()
    {
        GameSession.Instance.HasSeenTutorial = false;
        if (GameManager.Instance != null && GameManager.Instance.CurrentState == GameState.Playing) Begin();
    }

    public void Skip()
    {
        if (_active) Stop(markDone: true);
    }

    private void Begin()
    {
        _steps = BuildSteps();
        _active = true;
        _index = -1;
        Advance();
    }

    private void Update()
    {
        if (!_active || _index < 0 || _index >= _steps.Count) return;

        var step = _steps[_index];
        if (step.Done != null && step.Done()) Advance();
    }

    private void Advance()
    {
        _index++;
        if (_index >= _steps.Count)
        {
            Stop(markDone: true);
            return;
        }

        _heard = false;
        _executed.Clear();
        _failures = 0;

        var step = _steps[_index];
        if (ShipHUD.Instance != null)
        {
            ShipHUD.Instance.ShowCoach(_index + 1, _steps.Count, step.Title, step.Text, step.Try, step.NextLabel);
            ShipHUD.Instance.Highlight(step.Highlight);
        }
    }

    private void Stop(bool markDone)
    {
        _active = false;
        if (markDone) GameSession.Instance.HasSeenTutorial = true;
        if (ShipHUD.Instance != null) ShipHUD.Instance.HideCoach();
    }

    private void HandleNext()
    {
        if (!_active || _index < 0 || _index >= _steps.Count) return;
        if (_steps[_index].NextLabel != null) Advance();
    }

    private void HandleActionExecuted(ShipAction action)
    {
        if (_active) _executed.Add(action);
    }

    private void HandleRadioStatus(RadioStatus status, string text)
    {
        if (!_active) return;

        if (status == RadioStatus.Heard)
        {
            _heard = true;
            return;
        }

        if (status == RadioStatus.TooShort || status == RadioStatus.TooQuiet || status == RadioStatus.NotUnderstood)
        {
            _failures++;
            if (_failures >= FailuresBeforeHint && ShipHUD.Instance != null)
            {
                string key = _mic != null ? _mic.OrderKeyName : "T";
                bool toggle = SettingsService.Instance != null && SettingsService.Instance.ToggleToTalk;
                ShipHUD.Instance.ShowCoachHint(
                    (toggle ? "Tip: press " + key + ", say the words slowly, then press " + key + " again. "
                            : "Tip: press and HOLD " + key + ", say the words slowly, then let go. ") +
                    "If it still fails, check your microphone in Settings, or press Enter and type the order.");
            }
        }
    }

    private bool Ran(Func<ShipAction, bool> match)
    {
        foreach (var a in _executed) if (match(a)) return true;
        return false;
    }

    private List<Step> BuildSteps()
    {
        // Key names follow the player's settings (rebinding, toggle-to-talk).
        string key = _mic != null ? _mic.OrderKeyName : "T";
        bool toggle = SettingsService.Instance != null && SettingsService.Instance.ToggleToTalk;
        string radioHow = toggle
            ? "Press " + key + ", say the words, then press " + key + " again to send. The radio box at the bottom shows SENDING while it listens."
            : "Hold " + key + ", say the words, then let go. The radio box at the bottom shows SENDING while you hold " + key + ".";
        string radioTry = (toggle ? "Press " : "Hold ") + key + " and say: \"radio check\"";

        return new List<Step>
        {
            new Step
            {
                Title = "Welcome aboard, Captain!",
                Text = "On this ship you give every order with your voice. Let's practise here in the harbour. " +
                       "Nothing can hurt you yet.",
                NextLabel = "Let's start",
            },
            new Step
            {
                Title = "Radio check",
                Text = radioHow + " Can't speak right now? Press Enter and type it.",
                Try = radioTry,
                Highlight = "radio",
                Done = () => _heard,
            },
            new Step
            {
                Title = "Start the engine",
                Text = "Chief Harris runs the engine. Slow, half and full set your speed.",
                Try = "Say: \"slow ahead\"  (or \"go forward\")",
                Highlight = "telegraph",
                Done = () => Ran(a => a.IsAheadGear() || a == ShipAction.EngineIncrease),
            },
            new Step
            {
                Title = "Turn left",
                Text = "Finn steers. Port means LEFT and starboard means RIGHT. You can say either.",
                Try = "Say: \"turn left\"  (or \"port\")",
                Highlight = "rudder",
                Done = () => Ran(a => a.IsPortTurn()),
            },
            new Step
            {
                Title = "Go straight",
                Text = "The rudder bar shows how the ship is turning. Straighten up when you point where you want to go.",
                Try = "Say: \"straight\"  (or \"midships\")",
                Highlight = "rudder",
                Done = () => Ran(a => a == ShipAction.RudderMidships),
            },
            new Step
            {
                Title = "Ask the lookout",
                Text = "Finn watches the water and warns you about rocks and mines. You can ask him any time.",
                Try = "Say: \"any danger?\"",
                Highlight = "crew",
                Done = () => Ran(a => a == ShipAction.QueryHazard),
            },
            new Step
            {
                Title = "The chart",
                Text = "The chart shows the whole route. Orange squares are danger. The dock is the gold line at the top.",
                Try = "Press M to open the chart",
                Done = () => _map != null && _map.IsOpen,
            },
            new Step
            {
                Title = "Stop",
                Text = "A big ship drifts! After \"stop\" she keeps moving for about 8 seconds, so stop early. \"Full astern\" brakes faster.",
                Try = "Say: \"stop\"",
                Highlight = "telegraph",
                Done = () => Ran(a => a == ShipAction.EngineStop),
            },
            new Step
            {
                Title = "You're ready, Captain",
                Text = "Now sail to the dock. Listen for Finn's warnings and turn away from danger. " +
                       "Each hit knocks cargo overboard.",
                Try = "F1: all orders · M: chart · Esc: pause",
                NextLabel = "Set sail",
            },
        };
    }
}
