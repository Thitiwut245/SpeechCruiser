using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

public enum RadioChannel
{
    ChiefEngineer = 1, // Channel 1: Chief Harris, engine
    Lookout = 2        // Channel 2: Finn, helm and lookout
}

public class CommandRouter : MonoBehaviour
{
    public static CommandRouter Instance;

    [Header("Radio Frequency Selector")]
    [FormerlySerializedAs("activeChannel")]
    [SerializeField] private RadioChannel ActiveChannel = RadioChannel.ChiefEngineer;

    [Header("Routing")]
    [Tooltip("Send unmatched, non-question speech to the classifier before giving up.")]
    [FormerlySerializedAs("useClassifierFallback")]
    [SerializeField] private bool UseClassifierFallback = true;

    [Tooltip("Send unmatched speech that looks like a question to the conversation model.")]
    [FormerlySerializedAs("useConversationFallback")]
    [SerializeField] private bool UseConversationFallback = true;

    [Header("Debug")]
    [FormerlySerializedAs("logRouting")]
    [SerializeField] private bool LogRouting = true;

    public event System.Action<string> OnChannelChanged;

    /// <summary>An order was carried out or a question answered. The tutorial listens to this.</summary>
    public event System.Action<ShipAction> OnActionExecuted;

    // "Radio check" and "help" are handled before parsing: they are about the radio, not the ship.
    private static readonly System.Text.RegularExpressions.Regex RadioCheckPattern = new System.Text.RegularExpressions.Regex(
        @"\b(radio check|mic check|comms check|testing|can you hear me|do you hear me)\b",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    private static readonly System.Text.RegularExpressions.Regex HelpPattern = new System.Text.RegularExpressions.Regex(
        @"\b(help|what can i say|what do i say|what can i do|commands|radio card|list of orders)\b",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    // Said on their own, these mean "keep the ship moving": the last forward speed, or on as she is.
    private static readonly System.Text.RegularExpressions.Regex ContinuePattern = new System.Text.RegularExpressions.Regex(
        @"^\W*(ok(ay)?\W+)?(continue|keep going|carry on|go on|keep moving|keep sailing|resume|move on|go ahead)(\W+(please|sir|captain|now))*\W*$",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    // A bare "wait!" or "hold on!" at sea means stop.
    private static readonly System.Text.RegularExpressions.Regex WaitPattern = new System.Text.RegularExpressions.Regex(
        @"^\W*(wait|hold on|hold it|wait a (moment|second|minute)|wait wait)(\W+(please|sir|captain))*\W*$",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    // The last forward gear, so "continue" after a stop picks up where the ship was.
    private ShipEngine.TelegraphGear _lastAheadGear = ShipEngine.TelegraphGear.Slow;

    // After this many "say again" in a row, the crew suggests a simple order instead.
    private const int MissesBeforeHint = 2;
    private int _misses;

    public RadioChannel CurrentChannel => ActiveChannel;

    // What the last order changed, so "belay that" can put it back.
    private struct UndoRecord
    {
        public bool Valid;
        public bool Engine;
        public bool Helm;
        public ShipEngine.TelegraphGear GearBefore;
    }

    private UndoRecord _undo;
    private bool _classifierPending;

    /// <summary>True while the classifier or the conversation model is still working on a reply.</summary>
    public bool IsWaitingForReply
    {
        get
        {
            var chat = CrewChat.Active;
            return _classifierPending || (chat != null && chat.IsBusy);
        }
    }

    // A second read-back waits this long so both lines can be read.
    private const float FollowUpDelaySeconds = 1.6f;

    /// <summary>
    /// Who answers for an action. Harris runs the engine; Finn has the helm and the lookout.
    /// A general status report comes from whoever the radio is tuned to.
    /// </summary>
    public static RadioChannel OwnerOf(ShipAction action, RadioChannel tuned)
    {
        switch (action)
        {
            case ShipAction.TurnPort:
            case ShipAction.TurnStarboard:
            case ShipAction.HardPort:
            case ShipAction.HardStarboard:
            case ShipAction.RudderMidships:
            case ShipAction.QueryHazard:
            case ShipAction.QueryDestination:
            case ShipAction.QueryHeading:
                return RadioChannel.Lookout;
            case ShipAction.QueryStatus:
                return tuned;
            default:
                return RadioChannel.ChiefEngineer;
        }
    }

    private static bool ChatReady
    {
        get
        {
            var chat = CrewChat.Active;
            return chat != null && chat.Ready;
        }
    }

    private void Awake() 
    { 
        Instance = this; 
    }

    private void Start() 
    { 
        UpdateChannelUI(); 
    }

    public void SetChannel(RadioChannel channel)
    {
        ActiveChannel = channel;
        UpdateChannelUI();
    }

    public void RouteCommand(string transcript, bool isConversation = false)
    {
        if (LooksLikeNoise(transcript))
        {
            EndInstantTurn();
            return;
        }

        if (RadioCheckPattern.IsMatch(transcript))
        {
            Say(ActiveChannel, CrewBarkLibrary.RadioCheck(ActiveChannel), "radiocheck");
            OnActionExecuted?.Invoke(ShipAction.None);
            EndInstantTurn();
            return;
        }

        if (ContinuePattern.IsMatch(transcript))
        {
            Resume();
            EndInstantTurn();
            return;
        }

        if (WaitPattern.IsMatch(transcript)) transcript = "stop";

        var parsed = CommandParser.Parse(transcript);

        // "Help! Turn right!" turns right: the orders card only opens when no order was heard.
        if (HelpPattern.IsMatch(transcript) && !parsed.HasConfidentOrder)
        {
            if (ShipHUD.Instance != null) ShipHUD.Instance.SetRadioCardOpen(true);
            Say(ActiveChannel, CrewBarkLibrary.HelpCard(ActiveChannel), "help");
            EndInstantTurn();
            return;
        }

        RadioChannel target = parsed.AddressedCrew ?? ActiveChannel;
        if (parsed.AddressedCrew.HasValue && parsed.AddressedCrew.Value != ActiveChannel)
        {
            SetChannel(parsed.AddressedCrew.Value);
        }

        if (LogRouting)
        {
            Debug.Log("[ROUTER]: \"" + parsed.Normalized + "\" -> " +
                      (parsed.Matches.Count > 0
                          ? parsed.Matches[0].Action + " @ " + parsed.TopConfidence.ToString("0.00")
                          : "no match") +
                      DescribeFlags(parsed) +
                      " | target " + target + " | mode " + (isConversation ? "Conversation" : "Command"));
        }

        if (parsed.WasNameOnly)
        {
            if (CrewVoice.Instance != null) CrewVoice.Instance.NameOnly(target);
            EndInstantTurn();
            return;
        }

        // A contradictory order is asked about, never guessed.
        if (parsed.DirectionConflict)
        {
            Say(RadioChannel.ChiefEngineer, CrewBarkLibrary.AskAheadOrAstern(), "askdirection");
            EndInstantTurn();
            return;
        }

        if (parsed.SideConflict)
        {
            Say(RadioChannel.Lookout, CrewBarkLibrary.AskPortOrStarboard(RadioChannel.Lookout), "askside");
            EndInstantTurn();
            return;
        }

        // Orders are orders on either key; the talk key only changes where non-orders go.
        if (parsed.HasConfidentOrder || (parsed.HasConfidentCommand && (!isConversation || !ChatReady)))
        {
            ApplyMatches(parsed, target);
            EndInstantTurn();
            return;
        }

        if (isConversation && ChatReady && !parsed.NoChange && !parsed.WasFillerOnly)
        {
            SendToConversation(target, transcript);
            return;
        }

        if (parsed.OrderInQuestion)
        {
            Say(target, CrewBarkLibrary.IsThatAnOrder(target), "isorder");
            EndInstantTurn();
            return;
        }

        if (parsed.NoChange)
        {
            Say(target, CrewBarkLibrary.NoChange(target), "nochange");
            EndInstantTurn();
            return;
        }

        if (parsed.WasFillerOnly)
        {
            Say(target, CrewBarkLibrary.StandingBy(target), "standingby");
            EndInstantTurn();
            return;
        }

        if (isConversation)
        {
            SendToConversation(target, transcript);
            return;
        }

        if (parsed.Matches.Count == 0 && parsed.LooksLikeQuestion && UseConversationFallback)
        {
            SendToConversation(target, transcript);
            return;
        }

        if (UseClassifierFallback && IntentClassifier.Instance != null)
        {
            string captured = transcript;
            RadioChannel capturedTarget = target;
            int epoch = RadioEpoch.Current;

            _classifierPending = true;
            if (TimeController.Instance != null) TimeController.Instance.SetAIProcessing(true);
            IntentClassifier.Instance.Classify(captured, (action, ok) =>
            {
                // The classifier outlives the scene; this router may be gone, or be the old one.
                if (this == null || !RadioEpoch.IsCurrent(epoch))
                {
                    Debug.Log("[ROUTER]: dropped a classifier result from the previous scene.");
                    return;
                }
                _classifierPending = false;
                if (TimeController.Instance != null) TimeController.Instance.SetAIProcessing(false);
                if (ok && action != ShipAction.None)
                {
                    ExecuteAndAcknowledge(capturedTarget, action, captured);
                    EndInstantTurn();
                    return;
                }

                if (UseConversationFallback)
                {
                    SendToConversation(capturedTarget, captured);
                }
                else
                {
                    SayAgainOrHint(capturedTarget);
                    EndInstantTurn();
                }
            });
            return;
        }

        if (UseConversationFallback)
        {
            SendToConversation(target, transcript);
        }
        else
        {
            SayAgainOrHint(target);
            EndInstantTurn();
        }
    }

    /// <summary>"Say again" — but after repeated misses, suggest a simple order and the radio card.</summary>
    private void SayAgainOrHint(RadioChannel target)
    {
        _misses++;
        if (_misses >= MissesBeforeHint)
        {
            _misses = 0;
            Say(target, CrewBarkLibrary.TryHint(target), "tryhint");
            return;
        }
        if (CrewVoice.Instance != null) CrewVoice.Instance.SayAgain(target);
    }

    private void Update()
    {
        var gear = CurrentGear();
        if (gear > ShipEngine.TelegraphGear.Stop) _lastAheadGear = gear;
    }

    /// <summary>"Continue": already going ahead, Harris keeps her there; stopped or astern, back to the last forward speed.</summary>
    private void Resume()
    {
        if (CurrentGear() > ShipEngine.TelegraphGear.Stop)
        {
            Say(RadioChannel.ChiefEngineer, CrewBarkLibrary.KeepGoing(), "keepgoing");
            OnActionExecuted?.Invoke(ShipAction.None);
            return;
        }
        ExecuteAndAcknowledge(RadioChannel.ChiefEngineer, ActionFor(_lastAheadGear), "");
    }

    private static ShipAction ActionFor(ShipEngine.TelegraphGear gear)
    {
        switch (gear)
        {
            case ShipEngine.TelegraphGear.DeadSlow: return ShipAction.EngineDeadSlow;
            case ShipEngine.TelegraphGear.Half: return ShipAction.EngineHalf;
            case ShipEngine.TelegraphGear.Full: return ShipAction.EngineFull;
            case ShipEngine.TelegraphGear.Flank: return ShipAction.EngineFlank;
            default: return ShipAction.EngineSlow;
        }
    }

    private void UpdateChannelUI()
    {
        string title = ActiveChannel == RadioChannel.Lookout
            ? "CH 2: FINN (STEERING + LOOKOUT)"
            : "CH 1: CHIEF HARRIS (ENGINE)";

        OnChannelChanged?.Invoke(title);
    }

    private void ApplyMatches(CommandParser.Result parsed, RadioChannel target)
    {
        if (parsed.Matches.Count > 0 && parsed.Matches[0].Action == ShipAction.Belay)
        {
            Belay();
            return;
        }

        var gearBefore = CurrentGear();
        bool engineChanged = false, helmChanged = false;
        var readBacks = new System.Collections.Generic.List<ShipAction>(3);
        ShipAction query = ShipAction.None;

        for (int i = 0; i < parsed.Matches.Count; i++)
        {
            var m = parsed.Matches[i];
            if (m.Confidence < CommandParser.AcceptHigh) continue;

            ShipAction resolved = Resolve(m.Action, target);
            if (TryExecute(resolved, parsed.Degrees))
            {
                if (resolved.IsHelm()) helmChanged = true;
                else engineChanged = true;
                readBacks.Add(resolved);
            }
            else if (resolved.IsQuery() && query == ShipAction.None)
            {
                query = resolved;
            }
        }

        RememberUndo(gearBefore, engineChanged, helmChanged);
        _misses = 0;
        foreach (var done in readBacks) OnActionExecuted?.Invoke(done);
        if (query != ShipAction.None) OnActionExecuted?.Invoke(query);

        // Every order that ran gets read back, engine first, then helm, then any answer.
        readBacks.Sort((a, b) => a.IsHelm().CompareTo(b.IsHelm()));
        if (query != ShipAction.None) readBacks.Add(query);

        if (readBacks.Count == 0)
        {
            if (AtSea && CrewVoice.Instance != null) CrewVoice.Instance.Redirect(target);
            return;
        }

        Acknowledge(target, readBacks[0]);
        if (readBacks.Count > 1) StartCoroutine(FollowUpReadBacks(target, readBacks));
    }

    private System.Collections.IEnumerator FollowUpReadBacks(RadioChannel target, System.Collections.Generic.List<ShipAction> readBacks)
    {
        for (int i = 1; i < readBacks.Count; i++)
        {
            yield return new WaitForSecondsRealtime(FollowUpDelaySeconds);
            Acknowledge(target, readBacks[i]);
        }
    }

    private void ExecuteAndAcknowledge(RadioChannel target, ShipAction action, string rawSpeech)
    {
        if (action == ShipAction.Belay)
        {
            Belay();
            return;
        }

        ShipAction resolved = Resolve(action, target);
        float? degrees = CommandParser.ExtractDegrees(rawSpeech);
        var gearBefore = CurrentGear();

        bool executed = TryExecute(resolved, degrees);
        if (executed) RememberUndo(gearBefore, !resolved.IsHelm(), resolved.IsHelm());

        if (!executed && !resolved.IsQuery())
        {
            if (AtSea && CrewVoice.Instance != null) CrewVoice.Instance.Redirect(target);
            return;
        }

        _misses = 0;
        OnActionExecuted?.Invoke(resolved);
        Acknowledge(target, resolved);
    }

    private static ShipEngine.TelegraphGear CurrentGear()
    {
        var nav = ShipNavigator.Instance;
        return nav != null ? nav.currentGear : ShipEngine.TelegraphGear.Stop;
    }

    private void RememberUndo(ShipEngine.TelegraphGear gearBefore, bool engineChanged, bool helmChanged)
    {
        if (!engineChanged && !helmChanged) return;

        _undo = new UndoRecord
        {
            Valid = true,
            Engine = engineChanged,
            Helm = helmChanged,
            GearBefore = gearBefore,
        };
    }

    /// <summary>"Belay that": the engine goes back to its previous gear and the rudder to midships.</summary>
    private void Belay()
    {
        var nav = ShipNavigator.Instance;
        if (!_undo.Valid || nav == null)
        {
            Say(ActiveChannel, CrewBarkLibrary.NothingToBelay(), "belaynone");
            return;
        }

        if (_undo.Engine) nav.SetGear(_undo.GearBefore);
        if (_undo.Helm) nav.RudderMidships();

        string[] lines = _undo.Engine && _undo.Helm ? CrewBarkLibrary.BelayEngineAndHelm()
                       : _undo.Engine ? CrewBarkLibrary.BelayEngine()
                       : CrewBarkLibrary.BelayHelm();

        if (LogRouting) Debug.Log("[ROUTER]: belay -> gear " + (_undo.Engine ? _undo.GearBefore.ToString() : "unchanged") + (_undo.Helm ? ", rudder midships" : ""));

        RadioChannel speaker = _undo.Engine ? RadioChannel.ChiefEngineer : RadioChannel.Lookout;
        _undo = default(UndoRecord);
        Say(speaker, lines, "belay");
    }

    private void Say(RadioChannel crew, string[] lines, string key)
    {
        if (CrewVoice.Instance == null) return;
        CrewVoice.Instance.SpeakFrom(crew, lines, ShipTelemetry.Capture(), crew + ":" + key);
    }

    private static string DescribeFlags(CommandParser.Result parsed)
    {
        var sb = new System.Text.StringBuilder();
        if (parsed.Degrees.HasValue) sb.Append(" ").Append(parsed.Degrees.Value).Append("deg");
        if (parsed.DirectionConflict) sb.Append(" [ahead/astern?]");
        if (parsed.SideConflict) sb.Append(" [port/starboard?]");
        if (parsed.OrderInQuestion) sb.Append(" [order in question]");
        if (parsed.NoChange) sb.Append(" [no change]");
        if (parsed.WasFillerOnly) sb.Append(" [filler]");
        return sb.ToString();
    }

    private ShipAction Resolve(ShipAction action, RadioChannel target)
    {
        if (action == ShipAction.QueryStatus && target == RadioChannel.Lookout)
        {
            return ShipAction.QueryHazard;
        }
        return action;
    }

    // Orders reach their owner whichever channel is tuned; the channel only picks who you chat with.
    /// <summary>True while a voyage is under way. Orders only move the ship then.</summary>
    private static bool AtSea => GameManager.Instance == null || GameManager.Instance.CurrentState == GameState.Playing;

    private bool TryExecute(ShipAction action, float? degrees)
    {
        if (!action.IsMachinery()) return false;
        // The radio is off while moored, so only a late result can get here; it must not
        // spin up the engine in the shipyard or after the voyage ended.
        if (!AtSea)
        {
            Debug.Log("[ROUTER]: ignored " + action + " outside a voyage.");
            return false;
        }
        return CommandParser.Execute(action, degrees);
    }

    private void Acknowledge(RadioChannel target, ShipAction action)
    {
        if (CrewVoice.Instance == null) return;

        RadioChannel speaker = OwnerOf(action, target);

        var telemetry = ShipTelemetry.Capture();
        string line = CrewVoice.Instance.Speak(speaker, action, telemetry);

        if (line == null) CrewVoice.Instance.SayAgain(speaker);
    }

    private void SendToConversation(RadioChannel target, string transcript)
    {
        var chat = CrewChat.Active;

        if (chat == null || !chat.Ready)
        {
            SayAgainOrHint(target);
            EndInstantTurn();
            return;
        }

        chat.Converse(target, transcript);
    }

    private void EndInstantTurn()
    {
        if (TimeController.Instance != null) TimeController.Instance.SetPTTActive(false);
    }

    private static readonly System.Text.RegularExpressions.Regex SoundTagPattern =
        new System.Text.RegularExpressions.Regex(@"\([^)]*\)|\[[^\]]*\]|\*[^*]*\*");

    /// <summary>True for empty transcripts and the phrases Whisper invents from silence.</summary>
    public static bool LooksLikeNoise(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return true;
        
        string lower = text.ToLowerInvariant().Trim();
        
        if (lower.Contains("[blank_audio]") || 
            lower.Contains("[silence]") || 
            lower.Contains("subtitles by") ||
            lower.Contains("amara.org"))
        {
            return true;
        }

        if (lower == "thank you." || lower == "thank you") 
        {
            return true;
        }

        // Whisper describes non-speech in brackets: "(chimes)", "[Music]", "*coughs*".
        // What is left once those are gone is what was actually said.
        string spoken = SoundTagPattern.Replace(text, " ");
        string charsOnly = "";
        foreach (char c in spoken)
        {
            if (char.IsLetterOrDigit(c)) charsOnly += c;
        }

        if (charsOnly.Length < 2) 
        {
            return true; 
        }

        return false;
    }
}
