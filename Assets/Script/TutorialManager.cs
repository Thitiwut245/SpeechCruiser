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
    // Hits are free only on the learning layout, and only when the drill started with the voyage.
    private bool _practice;

    // What happened since the current step started.
    private bool _heard;
    private readonly HashSet<ShipAction> _executed = new HashSet<ShipAction>();
    private int _failures;

    private RadioMic _mic;
    private LevelMap _map;

    /// <summary>
    /// The drill is running on the learning layout and started with the voyage. While it is,
    /// ShipHull treats hits as free practice bumps. Any other job, or Replay Tutorial in the
    /// middle of a voyage, runs the drill with hits counting as usual.
    /// </summary>
    public bool IsPractice => _active && _practice;

    /// <summary>The drill is running (practice or not).</summary>
    public bool IsActive => _active;

    // The step reached, saved, so a voyage that ends mid-drill resumes there instead of at step 1.
    private const string StepPref = "tutorial_step";

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
        if (SettingsService.Instance != null) SettingsService.Instance.OnControlsChanged += HandleControlsChanged;
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
        if (SettingsService.Instance != null) SettingsService.Instance.OnControlsChanged -= HandleControlsChanged;
        if (ShipHUD.Instance != null)
        {
            ShipHUD.Instance.CoachNextPressed -= HandleNext;
            ShipHUD.Instance.CoachSkipPressed -= Skip;
        }
        if (Instance == this) Instance = null;
    }

    private void HandleStateChanged(GameState state)
    {
        if (state == GameState.Playing && !GameSession.Instance.HasSeenTutorial)
            Begin(practice: LevelGenerator.Instance != null && LevelGenerator.Instance.IsLearningLayout);
        else if (state != GameState.Playing && _active) Stop(markDone: false);
    }

    /// <summary>Runs the drill again from the start (pause menu).</summary>
    public void Replay()
    {
        GameSession.Instance.HasSeenTutorial = false;
        PlayerPrefs.DeleteKey(StepPref);
        // Mid-voyage the drill is coaching only: this voyage's hits still count.
        if (GameManager.Instance != null && GameManager.Instance.CurrentState == GameState.Playing) Begin(practice: false);
    }

    public void Skip()
    {
        if (_active) Stop(markDone: true);
    }

    private void Begin(bool practice)
    {
        _practice = practice;
        _steps = BuildSteps();
        _active = true;
        // Resume where the last voyage left off (0 on a first run).
        _index = Mathf.Clamp(PlayerPrefs.GetInt(StepPref, 0), 0, _steps.Count - 1) - 1;
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
        PlayerPrefs.SetInt(StepPref, _index);
        PlayerPrefs.Save();
        ShowStep();
    }

    /// <summary>A new radio key or toggle-to-talk mid-drill: the step on screen names the new one.</summary>
    private void HandleControlsChanged()
    {
        if (!_active || _index < 0 || _index >= _steps.Count) return;
        _steps = BuildSteps();
        ShowStep();
    }

    private void ShowStep()
    {
        var step = _steps[_index];
        if (ShipHUD.Instance != null)
        {
            ShipHUD.Instance.ShowCoach(_index + 1, _steps.Count, _practice, step.Title, step.Text, step.Try, step.NextLabel);
            ShipHUD.Instance.Highlight(step.Highlight);
        }
    }

    private void Stop(bool markDone)
    {
        _active = false;
        _practice = false;
        if (markDone)
        {
            GameSession.Instance.HasSeenTutorial = true;
            PlayerPrefs.DeleteKey(StepPref);
            PlayerPrefs.Save();
        }
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
                Text = "On this ship you give every order with your voice. Let's practise first. " +
                       (_practice
                           ? "Until this drill ends the ship can't be damaged: bumping a rock costs nothing. Take your time."
                           : "Careful: this is a real voyage, so every hit costs hull and cargo. Stop the ship if you need time."),
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
                Text = "Each \"left\" turns the ship about 30 degrees, then Finn steadies her. Say \"straight\" to stop a turn early.",
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
                Text = "The chart shows the whole route. Orange squares are danger. The harbour is at the top: dock at the yellow berth.",
                Try = "Press " + (_map != null ? _map.ToggleKeyName : "M") + " to open the chart",
                Done = () => _map != null && _map.IsOpen,
            },
            new Step
            {
                Title = "Stop",
                Text = "A big ship drifts! From full speed she keeps moving for about 8 seconds after \"stop\", so stop early. \"Full astern\" brakes faster.",
                Try = "Say: \"stop\"",
                Highlight = "telegraph",
                Done = () => Ran(a => a == ShipAction.EngineStop),
            },
            new Step
            {
                Title = "You're ready, Captain",
                Text = (_practice ? "Practice is over: from now on every hit costs hull and cargo. " : "") +
                       "Sail to the dock, listen for Finn's warnings and turn away from danger early.",
                Try = "F1: all orders · M: chart · Esc: pause",
                NextLabel = "Set sail",
            },
        };
    }
}
