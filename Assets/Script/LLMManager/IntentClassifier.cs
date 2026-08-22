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
    [Tooltip("Enter your OpenAI API key here (sk-...).")]
    [FormerlySerializedAs("openAIApiKey")] [SerializeField] private string OpenAIApiKey = "";
    
    [FormerlySerializedAs("url")] [SerializeField] private string Url = "https://api.openai.com/v1/chat/completions";

    [Tooltip("A cheap and fast model for simple classification.")]
    [FormerlySerializedAs("model")] [SerializeField] private string Model = "gpt-4o-mini";

    [Tooltip("Give up and fall back to 'say again' after this long.")]
    [FormerlySerializedAs("timeoutSeconds")] [SerializeField] private int TimeoutSeconds = 6;

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

    public void Classify(string speech, Action<ShipAction, bool> onDone)
    {
        if (string.IsNullOrWhiteSpace(speech))
        {
            if (onDone != null) onDone(ShipAction.None, false);
            return;
        }

        if (string.IsNullOrWhiteSpace(OpenAIApiKey))
        {
            if (LogRequests) Debug.LogWarning("[CLASSIFIER]: OpenAI API Key missing, skipping classification.");
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
            request.SetRequestHeader("Authorization", "Bearer " + OpenAIApiKey);
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
