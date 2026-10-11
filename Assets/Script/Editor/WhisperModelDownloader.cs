using UnityEngine;
using UnityEditor;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

public class WhisperModelDownloader : EditorWindow
{
    private const string MODEL_URL = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.en.bin";
    private const string TINY_MODEL_URL = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-tiny.en.bin";

    [MenuItem("Tools/Whisper/Download ggml-base.en.bin (~140 MB - Recommended)")]
    public static void DownloadBaseModel()
    {
        _ = DownloadModelAsync(MODEL_URL, "ggml-base.en.bin");
    }

    [MenuItem("Tools/Whisper/Download ggml-tiny.en.bin (~75 MB - Faster)")]
    public static void DownloadTinyModel()
    {
        _ = DownloadModelAsync(TINY_MODEL_URL, "ggml-tiny.en.bin");
    }

    private static async Task DownloadModelAsync(string url, string fileName)
    {
        string targetDir = Path.Combine(Application.dataPath, "StreamingAssets", "Whisper");
        if (!Directory.Exists(targetDir))
        {
            Directory.CreateDirectory(targetDir);
            Debug.Log($"[WHISPER DOWNLOADER]: Created directory at {targetDir}");
        }

        string targetPath = Path.Combine(targetDir, fileName);

        if (File.Exists(targetPath))
        {
            if (!EditorUtility.DisplayDialog("Model Already Exists", 
                $"{fileName} is already downloaded in StreamingAssets/Whisper/. Do you want to re-download and overwrite it?", 
                "Yes, Overwrite", "Cancel"))
            {
                return;
            }
        }

        Debug.Log($"[WHISPER DOWNLOADER]: Starting download of {fileName} from {url}...");
        EditorUtility.DisplayProgressBar("Downloading Whisper AI Model", $"Downloading {fileName} from HuggingFace... Please wait.", 0.2f);

        try
        {
            using (HttpClient client = new HttpClient())
            {
                // Download data
                byte[] modelBytes = await client.GetByteArrayAsync(url);
                
                EditorUtility.DisplayProgressBar("Downloading Whisper AI Model", $"Saving {fileName} to StreamingAssets...", 0.9f);
                
                File.WriteAllBytes(targetPath, modelBytes);
                
                Debug.Log($"[WHISPER DOWNLOADER]: Successfully downloaded and saved {fileName} to: {targetPath}");
                EditorUtility.DisplayDialog("Download Complete!", $"Successfully downloaded {fileName} to Assets/StreamingAssets/Whisper/!\n\nYou can now set your WhisperManager Model Path to:\nWhisper/{fileName}", "OK");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[WHISPER DOWNLOADER ERROR]: Failed to download model. Error: {ex.Message}");
            EditorUtility.DisplayDialog("Download Failed", $"Could not download {fileName}.\nError: {ex.Message}\n\nYou can also download it manually from:\n{url}\nAnd put it into Assets/StreamingAssets/Whisper/", "OK");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            AssetDatabase.Refresh();
        }
    }
}
