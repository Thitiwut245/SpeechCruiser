using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

public enum RadioChannel
{
    ChiefEngineer = 1, // Channel 1: engine room (Chief Harris)
    Lookout = 2        // Channel 2: bow (Lookout Finn)
}

public class CommandRouter : MonoBehaviour
{
    public static CommandRouter Instance;

    [Header("Radio Frequency Selector")]
    [FormerlySerializedAs("activeChannel")]
    [SerializeField] private RadioChannel ActiveChannel = RadioChannel.ChiefEngineer;

    [Header("Routing")]
    [Tooltip("If the lookout is handed a machinery order, pass it to the engine room instead of refusing. Turn off for strict role separation.")]
    [FormerlySerializedAs("autoRelayMachineryToEngineer")]
    [SerializeField] private bool AutoRelayMachineryToEngineer = true;

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

    public RadioChannel CurrentChannel => ActiveChannel;

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
        if (IsNonsenseOrHallucination(transcript))
        {
            EndInstantTurn();
            return;
        }

        var parsed = CommandParser.Parse(transcript);

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
                      " | target " + target + " | mode " + (isConversation ? "Conversation" : "Command"));
        }

        if (parsed.WasNameOnly)
        {
            if (CrewVoice.Instance != null) CrewVoice.Instance.NameOnly(target);
            EndInstantTurn();
            return;
        }

        if (isConversation)
        {
            SendToConversation(target, transcript);
            return;
        }

        if (parsed.HasConfidentCommand)
        {
            ApplyMatches(parsed, target, transcript);
            EndInstantTurn();
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

            IntentClassifier.Instance.Classify(captured, (action, ok) =>
            {
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
                    if (CrewVoice.Instance != null) CrewVoice.Instance.SayAgain(capturedTarget);
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
            if (CrewVoice.Instance != null) CrewVoice.Instance.SayAgain(target);
            EndInstantTurn();
        }
    }

    private void UpdateChannelUI()
    {
        string title = ActiveChannel == RadioChannel.Lookout
            ? "CH 2: LOOKOUT FINN"
            : "CH 1: CHIEF HARRIS";

        OnChannelChanged?.Invoke(title);
    }

    private void ApplyMatches(CommandParser.Result parsed, RadioChannel target, string rawSpeech)
    {
        ShipAction spoken = ShipAction.None;
        float? degrees = CommandParser.ExtractDegrees(rawSpeech);

        for (int i = 0; i < parsed.Matches.Count; i++)
        {
            var m = parsed.Matches[i];
            if (m.Confidence < CommandParser.AcceptHigh) continue;

            ShipAction resolved = Resolve(m.Action, target);
            bool executed = TryExecute(resolved, target, degrees);

            if (spoken == ShipAction.None && (executed || resolved.IsQuery())) spoken = resolved;
        }

        if (spoken == ShipAction.None)
        {
            if (CrewVoice.Instance != null) CrewVoice.Instance.Redirect(target);
            return;
        }

        Acknowledge(target, spoken);
    }

    private void ExecuteAndAcknowledge(RadioChannel target, ShipAction action, string rawSpeech)
    {
        ShipAction resolved = Resolve(action, target);
        float? degrees = CommandParser.ExtractDegrees(rawSpeech);

        if (!TryExecute(resolved, target, degrees) && !resolved.IsQuery())
        {
            if (CrewVoice.Instance != null) CrewVoice.Instance.Redirect(target);
            return;
        }

        Acknowledge(target, resolved);
    }

    private ShipAction Resolve(ShipAction action, RadioChannel target)
    {
        if (action == ShipAction.QueryStatus && target == RadioChannel.Lookout)
        {
            return ShipAction.QueryHazard;
        }
        return action;
    }

    private bool TryExecute(ShipAction action, RadioChannel target, float? degrees)
    {
        if (!action.IsMachinery()) return false; 

        if (target == RadioChannel.Lookout)
        {
            if (!AutoRelayMachineryToEngineer) return false;

            SetChannel(RadioChannel.ChiefEngineer);
            if (LogRouting) Debug.Log("[ROUTER]: relayed " + action + " from lookout to engine room.");
        }

        return CommandParser.Execute(action, degrees);
    }

    private void Acknowledge(RadioChannel target, ShipAction action)
    {
        if (CrewVoice.Instance == null) return;

        RadioChannel speaker = action.IsMachinery() ? RadioChannel.ChiefEngineer : target;

        var telemetry = ShipTelemetry.Capture();
        string line = CrewVoice.Instance.Speak(speaker, action, telemetry);

        if (line == null) CrewVoice.Instance.SayAgain(speaker);
    }

    private void SendToConversation(RadioChannel target, string transcript)
    {
        var chat = CrewChat.Active;

        if (chat == null || !chat.Ready)
        {
            if (CrewVoice.Instance != null) CrewVoice.Instance.SayAgain(target);
            EndInstantTurn();
            return;
        }

        chat.Converse(target, transcript);
    }

    private void EndInstantTurn()
    {
        if (TimeController.Instance != null) TimeController.Instance.SetPTTActive(false);
    }

    private bool IsNonsenseOrHallucination(string text)
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

        string charsOnly = "";
        foreach (char c in text)
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
