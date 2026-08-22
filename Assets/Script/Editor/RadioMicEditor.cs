using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(RadioMic))]
public class RadioMicEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        RadioMic radioMic = (RadioMic)target;

        // Header
        EditorGUILayout.Space(5);
        EditorGUILayout.LabelField(" Microphone Device Selection", EditorStyles.boldLabel);

        string[] devices = Microphone.devices;

        if (devices == null || devices.Length == 0)
        {
            EditorGUILayout.HelpBox(" No microphone devices detected on this computer! Plug in a microphone and click Refresh.", MessageType.Warning);
            if (GUILayout.Button(" Refresh Microphones"))
            {
                radioMic.RefreshAndSelectMicrophone();
            }
        }
        else
        {
            SerializedProperty indexProp = serializedObject.FindProperty("SelectedDeviceIndex");
            SerializedProperty nameProp = serializedObject.FindProperty("SelectedDeviceName");

            // Find current selected index based on name or saved index
            int currentIndex = indexProp.intValue;
            if (!string.IsNullOrEmpty(nameProp.stringValue))
            {
                for (int i = 0; i < devices.Length; i++)
                {
                    if (devices[i] == nameProp.stringValue)
                    {
                        currentIndex = i;
                        break;
                    }
                }
            }

            // Ensure index is within valid bounds
            currentIndex = Mathf.Clamp(currentIndex, 0, devices.Length - 1);

            // Draw popup dropdown inside horizontal row with Refresh button
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            int newIndex = EditorGUILayout.Popup("Available Microphones", currentIndex, devices);
            if (EditorGUI.EndChangeCheck())
            {
                indexProp.intValue = newIndex;
                nameProp.stringValue = devices[newIndex];
            }

            if (GUILayout.Button("", GUILayout.Width(30)))
            {
                radioMic.RefreshAndSelectMicrophone();
            }
            EditorGUILayout.EndHorizontal();

            // Show currently active device status if in Play Mode
            if (Application.isPlaying)
            {
                EditorGUILayout.Space(2);
                EditorGUILayout.LabelField($"Active In-Game: {(string.IsNullOrEmpty(radioMic.ActiveMicrophone) ? "None" : radioMic.ActiveMicrophone)}", EditorStyles.miniBoldLabel);
            }
        }

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("⚙️ General Settings", EditorStyles.boldLabel);

        // Draw all regular Inspector properties
        DrawPropertiesExcluding(serializedObject, "m_Script", "SelectedDeviceName", "SelectedDeviceIndex");
        
        serializedObject.ApplyModifiedProperties();
    }
}
