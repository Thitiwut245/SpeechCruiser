using System;
using System.IO;
using UnityEngine;

/// <summary>
/// Supplies the OpenAI API key without ever serializing it into a scene or prefab.
/// Looks for the OPENAI_API_KEY environment variable first, then StreamingAssets/openai.key
/// (git-ignored). Never commit the key.
/// </summary>
public static class OpenAIKey
{
    private const string EnvVar = "OPENAI_API_KEY";
    private const string FileName = "openai.key";

    private static string _value;
    private static bool _loaded;

    public static string Value
    {
        get
        {
            if (!_loaded)
            {
                _value = Load();
                _loaded = true;
            }
            return _value;
        }
    }

    public static bool IsSet => !string.IsNullOrWhiteSpace(Value);

    private static string Load()
    {
        string fromEnv = Environment.GetEnvironmentVariable(EnvVar);
        if (!string.IsNullOrWhiteSpace(fromEnv)) return fromEnv.Trim();

        string path = Path.Combine(Application.streamingAssetsPath, FileName);
        if (File.Exists(path)) return File.ReadAllText(path).Trim();

        Debug.LogWarning($"[OPENAI]: no API key. Set {EnvVar} or create Assets/StreamingAssets/{FileName}.");
        return "";
    }
}
