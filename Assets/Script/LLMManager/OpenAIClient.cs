using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Serialization;

[System.Serializable]
public class OpenAIMessage
{
    [SerializeField] private string role;
    [SerializeField] private string content;

    public string Role => role;
    public string Content => content;

    public OpenAIMessage(string role, string content)
    {
        this.role = role;
        this.content = content;
    }
}

[System.Serializable]
public class OpenAIRequest
{
    [SerializeField] private string model;
    [SerializeField] private List<OpenAIMessage> messages;
    [SerializeField] private float temperature;
    [SerializeField] private float top_p;
    [SerializeField] private float frequency_penalty;
    [SerializeField] private int max_tokens;

    public OpenAIRequest(string model, List<OpenAIMessage> messages, float temperature, float topP, float frequencyPenalty, int maxTokens)
    {
        this.model = model;
        this.messages = messages;
        this.temperature = temperature;
        this.top_p = topP;
        this.frequency_penalty = frequencyPenalty;
        this.max_tokens = maxTokens;
    }
}

[System.Serializable]
public class OpenAIResponse
{
    [SerializeField] private List<OpenAIChoice> choices;
    [SerializeField] private OpenAIUsage usage;

    public List<OpenAIChoice> Choices => choices;
    public OpenAIUsage Usage => usage;
}

[System.Serializable]
public class OpenAIUsage
{
    [SerializeField] private int prompt_tokens;
    [SerializeField] private int completion_tokens;
    [SerializeField] private int total_tokens;

    public int TotalTokens => total_tokens;
}

[System.Serializable]
public class OpenAIChoice
{
    [SerializeField] private OpenAIMessage message;
    
    public OpenAIMessage Message => message;
}

public class OpenAIClient : MonoBehaviour, ICrewChat
{
    public static OpenAIClient Instance;

    [Header("Backend selection")]
    [FormerlySerializedAs("priority")] [SerializeField] private int Priority = 0;

    [Header("OpenAI API")]
    
    [FormerlySerializedAs("url")] [SerializeField] private string Url = "https://api.openai.com/v1/chat/completions";
    
    [FormerlySerializedAs("model")] [SerializeField] private string Model = "gpt-4o-mini";
    
    [FormerlySerializedAs("timeoutSeconds")] [SerializeField] private int TimeoutSeconds = 15;

    [Header("Generation")]
    [Range(0f, 1f)] [FormerlySerializedAs("temperature")] [SerializeField] private float Temperature = 0.6f;
    [Range(0f, 1f)] [FormerlySerializedAs("topP")] [SerializeField] private float TopP = 0.9f;
    [FormerlySerializedAs("repeatPenalty")] [SerializeField] private float RepeatPenalty = 0.15f;
    [FormerlySerializedAs("maxTokens")] [SerializeField] private int MaxTokens = 60;

    [Header("Memory")]
    [FormerlySerializedAs("historyPerCrew")] [SerializeField] private int HistoryPerCrew = 3;
    
    [HideInInspector] public string lastFinnReport = "";

    private readonly Dictionary<RadioChannel, List<string>> _history = new Dictionary<RadioChannel, List<string>>();
    private bool _busy;

    public event System.Action<string, string> OnSubtitleUpdated;

    public bool IsBusy => _busy;
    public bool Ready => OpenAIKey.IsSet;

    private void Awake()
    {
        Instance = this;
        CrewChat.Register(this, Priority, "OpenAI (Cloud)");
    }

    private void OnDestroy() 
    { 
        CrewChat.Unregister(this); 
    }

    public void Converse(RadioChannel crew, string playerSpeech)
    {
        if (string.IsNullOrWhiteSpace(playerSpeech)) return;

        if (!Ready)
        {
            Debug.LogWarning("[RADIO]: OpenAI API Key is missing. Falling back to simple phrases.");
            if (CrewVoice.Instance != null) CrewVoice.Instance.SayAgain(crew);
            if (TimeController.Instance != null) TimeController.Instance.SetPTTActive(false);
            return;
        }

        if (_busy)
        {
            Debug.Log("[RADIO]: channel busy, ignoring overlapping transmission.");
            return;
        }

        StartCoroutine(ConverseCoroutine(crew, playerSpeech));
    }
    
    public void AskCrew(string modelName, string crewName, string playerSpeech)
    {
        bool isLookout =
            (!string.IsNullOrEmpty(modelName) && modelName.IndexOf("Lookout", System.StringComparison.OrdinalIgnoreCase) >= 0) ||
            (!string.IsNullOrEmpty(crewName) && crewName.IndexOf("FINN", System.StringComparison.OrdinalIgnoreCase) >= 0);

        Converse(isLookout ? RadioChannel.Lookout : RadioChannel.ChiefEngineer, playerSpeech);
    }

    private IEnumerator ConverseCoroutine(RadioChannel crew, string speech)
    {
        _busy = true;

        var telemetry = ShipTelemetry.Capture();
        string crewName = CrewVoice.DisplayName(crew);

        List<string> log;
        _history.TryGetValue(crew, out log);
        string prompt = CrewPersona.StatelessPrompt(telemetry, lastFinnReport, crew, log, speech);

        var requestData = new OpenAIRequest(
            Model,
            new List<OpenAIMessage>
            {
                new OpenAIMessage("system", CrewPersona.SystemPrompt(crew)),
                new OpenAIMessage("user", prompt)
            },
            Temperature,
            TopP,
            RepeatPenalty,
            MaxTokens
        );

        string body = JsonUtility.ToJson(requestData);

        OnSubtitleUpdated?.Invoke("...", crewName);
        if (TimeController.Instance != null) TimeController.Instance.SetAIProcessing(true);

        using (var request = new UnityWebRequest(Url, "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", "Bearer " + OpenAIKey.Value);
            request.timeout = TimeoutSeconds;

            float started = Time.realtimeSinceStartup;
            yield return request.SendWebRequest();
            float elapsed = Time.realtimeSinceStartup - started;

            if (request.result == UnityWebRequest.Result.Success)
            {
                string reply = "";
                int totalTokens = 0;
                try 
                {
                    var parsed = JsonUtility.FromJson<OpenAIResponse>(request.downloadHandler.text);
                    if (parsed != null) 
                    {
                        if (parsed.Choices != null && parsed.Choices.Count > 0)
                        {
                            reply = parsed.Choices[0].Message.Content.Trim();
                        }
                        if (parsed.Usage != null)
                        {
                            totalTokens = parsed.Usage.TotalTokens;
                        }
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogError("[RADIO]: failed to parse OpenAI response: " + e.Message);
                }

                reply = CrewPersona.Sanitize(reply);

                if (CrewPersona.ClaimsOrderCarriedOut(reply))
                {
                    Debug.LogWarning("[RADIO]: " + crewName + " claimed to carry out an order over chat; replaced. Reply was: " + reply);
                    reply = CrewBarkLibrary.NotAnOrderOnChat(crew)[0];
                }

                if (string.IsNullOrEmpty(reply))
                {
                    if (CrewVoice.Instance != null) CrewVoice.Instance.SayAgain(crew);
                }
                else
                {
                    OnSubtitleUpdated?.Invoke(reply, crewName);

                    RecordExchange(crew, speech, reply);
                    if (crew == RadioChannel.Lookout) lastFinnReport = reply;

                    string tokenStr = totalTokens > 0 ? $" {totalTokens}tk" : "";
                    Debug.Log("[" + crewName + " " + (elapsed * 1000f).ToString("F0") + "ms" + tokenStr + "]: " + reply);
                }
            }
            else
            {
                Debug.LogWarning("[RADIO]: lost contact with " + crewName + " (" + request.error + ")");
                Debug.LogWarning("[RADIO]: Response: " + request.downloadHandler.text);
                if (CrewVoice.Instance != null) CrewVoice.Instance.SayAgain(crew);
            }
        }

        if (TimeController.Instance != null)
        {
            TimeController.Instance.SetAIProcessing(false);
            TimeController.Instance.SetPTTActive(false);
        }

        _busy = false;
    }

    public void ResetMemory()
    {
        _history.Clear();
        lastFinnReport = "";
    }

    private void RecordExchange(RadioChannel crew, string said, string replied)
    {
        List<string> log;
        if (!_history.TryGetValue(crew, out log))
        {
            log = new List<string>();
            _history[crew] = log;
        }

        log.Add(CrewPersona.LogLine(said, replied));

        int cap = Mathf.Max(1, HistoryPerCrew);
        while (log.Count > cap) log.RemoveAt(0);
    }
}
