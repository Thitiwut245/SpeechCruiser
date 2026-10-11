using UnityEngine;
using UnityEditor;

public class ListMicrophones : EditorWindow
{
    [MenuItem("Tools/Audio/List All Microphones")]
    public static void ShowMicrophones()
    {
        string[] devices = Microphone.devices;
        if (devices.Length == 0)
        {
            Debug.LogError("[MIC FINDER]: No microphone devices detected!");
            return;
        }
        Debug.Log($"[MIC FINDER]: Found {devices.Length} microphone(s):");
        for (int i = 0; i < devices.Length; i++)
        {
            Debug.Log($"   [{i}] : '{devices[i]}'");
        }
    }
}
