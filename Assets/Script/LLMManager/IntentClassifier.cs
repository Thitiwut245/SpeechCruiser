using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Serialization;

public class IntentClassifier : MonoBehaviour
{
    public static IntentClassifier Instance;

    [Header("OpenAI API")]
    
    [FormerlySerializedAs("url")] [SerializeField] private string Url = "https://api.openai.com/v1/chat/completions";

    [Tooltip("A cheap and fast model for simple classification.")]
    [FormerlySerializedAs("model")] [SerializeField] private string Model = "gpt-4o-mini";

    [Tooltip("Give up and fall back to 'say again' after this long.")]
    [FormerlySerializedAs("timeoutSeconds")] [SerializeField] private int TimeoutSeconds = 6;

    [Header("Local Matching")]
    [Tooltip("Minimum similarity score (0.0 to 1.0) to match locally without using OpenAI")]
    [SerializeField] private float LocalMatchThreshold = 0.75f;

    private readonly Dictionary<string, ShipAction> _localCommandDictionary = new Dictionary<string, ShipAction>()
    {
        { "full astern", ShipAction.EngineFullAstern },
        { "slow astern", ShipAction.EngineSlowAstern },
        { "stop engine", ShipAction.EngineStop },
        { "dead slow", ShipAction.EngineDeadSlow },
        { "slow ahead", ShipAction.EngineSlow },
        { "half ahead", ShipAction.EngineHalf },
        { "full speed", ShipAction.EngineFull },
        { "full ahead", ShipAction.EngineFull },
        { "flank speed", ShipAction.EngineFlank },
        { "increase speed", ShipAction.EngineIncrease },
        { "decrease speed", ShipAction.EngineDecrease },
        { "turn port", ShipAction.TurnPort },
        { "turn starboard", ShipAction.TurnStarboard },
        { "hard port", ShipAction.HardPort },
        { "hard starboard", ShipAction.HardStarboard },
        { "midships", ShipAction.RudderMidships },
        { "steady as she goes", ShipAction.RudderMidships },
        { "status report", ShipAction.QueryStatus },
        { "where is the hazard", ShipAction.QueryHazard },
        { "fuel report", ShipAction.QueryFuel },
        { "what is our heading", ShipAction.QueryHeading },
        { "what is our speed", ShipAction.QuerySpeed },
    };

    [Header("Debug")]
    [FormerlySerializedAs("logRequests")] [SerializeField] private bool LogRequests = true;

    private string _systemPrompt;

    [Serializable]
    private class OpenAIRequestFormat
    {
#pragma warning disable 0414
        [SerializeField] private string type = "json_object";
#pragma warning restore 0414
    }

    [Serializable]
    private class IntentRequest
    {
        [SerializeField] private string model;
        [SerializeField] private List<OpenAIMessage> messages;
        [SerializeField] private float temperature;
        [SerializeField] private OpenAIRequestFormat response_format;

        public IntentRequest(string model, List<OpenAIMessage> messages, float temperature, OpenAIRequestFormat responseFormat)
        {
            this.model = model;
            this.messages = messages;
            this.temperature = temperature;
            this.response_format = responseFormat;
        }
    }

    [Serializable]
    private class ClassifierOutput
    {
        [SerializeField] private string action;
        public string Action => action;
    }

    private void Awake()
    {
        Instance = this;
        BuildStaticPromptParts();
    }

    private void BuildStaticPromptParts()
    {
        var names = Enum.GetNames(typeof(ShipAction));
        
        string enumList = string.Join(", ", names);

        _systemPrompt =
            "Map a ship captain's spoken order to exactly one action code. " +
            "The speech comes from an imperfect recogniser and the captain is not a native English speaker, " +
            "so judge by intent, not spelling. Port is left, starboard is right. " +
            "If the order is not about the engine, the rudder, or a status question, answer None. " +
            "Answer with a JSON object containing a single key \"action\" mapping to one of the following exact string values: [" + enumList + "].\n\n" +
            "Examples:\n" +
            "\"hard to pork\" -> {\"action\": \"HardPort\"}\n" +
            "\"give her everything she has\" -> {\"action\": \"EngineFlank\"}\n" +
            "\"ease back a touch\" -> {\"action\": \"EngineDecrease\"}\n" +
            "\"how close is that rock\" -> {\"action\": \"QueryHazard\"}\n" +
            "\"steady on that course\" -> {\"action\": \"RudderMidships\"}\n" +
            "\"never mind, forget it\" -> {\"action\": \"None\"}";
    }

    private float CalculateSimilarity(string source, string target)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(target)) return 0f;
        if (source == target) return 1f;

        int sourceWordCount = source.Length;
        int targetWordCount = target.Length;

        if (sourceWordCount == 0 || targetWordCount == 0) return 0f;

        int[,] distance = new int[sourceWordCount + 1, targetWordCount + 1];

        for (int i = 0; i <= sourceWordCount; i++) distance[i, 0] = i;
        for (int j = 0; j <= targetWordCount; j++) distance[0, j] = j;

        for (int i = 1; i <= sourceWordCount; i++)
        {
            for (int j = 1; j <= targetWordCount; j++)
            {
                int cost = (target[j - 1] == source[i - 1]) ? 0 : 1;
                distance[i, j] = Math.Min(Math.Min(distance[i - 1, j] + 1, distance[i, j - 1] + 1), distance[i - 1, j - 1] + cost);
            }
        }

        int maxLen = Math.Max(sourceWordCount, targetWordCount);
        return 1.0f - ((float)distance[sourceWordCount, targetWordCount] / maxLen);
    }

    public void Classify(string speech, Action<ShipAction, bool> onDone)
    {
        if (string.IsNullOrWhiteSpace(speech))
        {
            if (onDone != null) onDone(ShipAction.None, false);
            return;
        }

        string normalizedSpeech = speech.ToLowerInvariant().Trim();
        normalizedSpeech = normalizedSpeech.Replace(".", "").Replace(",", "").Replace("!", "").Replace("?", "");

        float bestScore = 0f;
        ShipAction bestMatch = ShipAction.None;
        string matchedPhrase = "";

        foreach (var kvp in _localCommandDictionary)
        {
            float score = CalculateSimilarity(normalizedSpeech, kvp.Key);
            if (score > bestScore)
            {
                bestScore = score;
                bestMatch = kvp.Value;
                matchedPhrase = kvp.Key;
            }
        }

        if (bestScore >= LocalMatchThreshold)
        {
            if (LogRequests)
            {
                Debug.Log($"[CLASSIFIER LOCAL]: \"{speech}\" matched \"{matchedPhrase}\" -> {bestMatch} (Score: {bestScore:F2})");
            }
            if (onDone != null) onDone(bestMatch, true);
            return;
        }

        if (!OpenAIKey.IsSet)
        {
            if (LogRequests) Debug.LogWarning("[CLASSIFIER]: OpenAI API Key missing and local match failed.");
            if (onDone != null) onDone(ShipAction.None, false);
            return;
        }

        StartCoroutine(ClassifyCoroutine(speech, onDone));
    }

    private IEnumerator ClassifyCoroutine(string speech, Action<ShipAction, bool> onDone)
    {
        var requestData = new IntentRequest(
            Model,
            new List<OpenAIMessage>
            {
                new OpenAIMessage("system", _systemPrompt),
                new OpenAIMessage("user", "Order: \"" + speech + "\"")
            },
            0f,
            new OpenAIRequestFormat()
        );

        string body = JsonUtility.ToJson(requestData);

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

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning("[CLASSIFIER]: request failed (" + request.error + ")");
                if (onDone != null) onDone(ShipAction.None, false);
                yield break;
            }

            ShipAction action = ShipAction.None;
            bool ok = false;

            try
            {
                var parsed = JsonUtility.FromJson<OpenAIResponse>(request.downloadHandler.text);
                if (parsed != null && parsed.Choices != null && parsed.Choices.Count > 0)
                {
                    string jsonResult = parsed.Choices[0].Message.Content.Trim();
                    var inner = JsonUtility.FromJson<ClassifierOutput>(jsonResult);
                    
                    if (inner != null && !string.IsNullOrWhiteSpace(inner.Action))
                    {
                        ok = Enum.TryParse(inner.Action.Trim(), false, out action);
                        if (!ok) Debug.LogWarning("[CLASSIFIER]: unknown action '" + inner.Action + "'");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[CLASSIFIER]: could not parse response: " + ex.Message + "\nBody: " + request.downloadHandler.text);
            }

            if (LogRequests)
            {
                Debug.Log("[CLASSIFIER]: \"" + speech + "\" -> " + action +
                          " (" + (elapsed * 1000f).ToString("F0") + " ms, " + Model + ")");
            }

            if (onDone != null) onDone(action, ok);
        }
    }
}
