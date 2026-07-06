using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System.Text;
using Yetibyte.Unity.SpeechRecognition;

public class LLMManager : MonoBehaviour
{
    [Header("Speech Recognition")]
    [SerializeField]
    [Tooltip("อ้างอิงถึง VoskListener ใน Scene (ถ้าไม่ใส่ ระบบจะค้นหาให้อัตโนมัติ)")]
    private VoskListener voskListener;

    [Header("Tank Command Processor")]
    [SerializeField]
    [Tooltip("อ้างอิงถึง TankCommandProcessor ใน Scene (ถ้าไม่ใส่ ระบบจะค้นหาให้อัตโนมัติ)")]
    private TankCommandProcessor tankProcessor;

    private void Awake()
    {
        if (voskListener == null)
        {
            voskListener = FindObjectOfType<VoskListener>();
        }
        if (tankProcessor == null)
        {
            tankProcessor = FindObjectOfType<TankCommandProcessor>();
        }
    }

    private void OnEnable()
    {
        if (voskListener == null)
        {
            voskListener = FindObjectOfType<VoskListener>();
        }
        if (tankProcessor == null)
        {
            tankProcessor = FindObjectOfType<TankCommandProcessor>();
        }

        if (voskListener != null)
        {
            voskListener.ResultFound += OnVoskResultFound;
        }
        else
        {
            Debug.LogWarning("LLMManager: ไม่พบ VoskListener ใน Scene");
        }
    }

    private void OnDisable()
    {
        if (voskListener != null)
        {
            voskListener.ResultFound -= OnVoskResultFound;
        }
    }

    private void OnVoskResultFound(object sender, VoskResultEventArgs e)
    {
        if (e.Result != null && !e.Result.IsEmpty)
        {
            string recognizedText = e.Result.Text;
            Debug.Log("Vosk ได้ยินคำว่า: " + recognizedText);
            SendOrderToCrew(recognizedText);
        }
    }

    // ฟังก์ชันนี้จะถูกเรียกเมื่อ VOSK แปลงเสียงเป็นตัวหนังสือเสร็จแล้ว
    public void SendOrderToCrew(string textFromVosk)
    {
        // ส่งคำสั่งเสียงให้รถถังวิเคราะห์และทำงานทันที (ถ้าตรงกับคำสั่งพื้นฐาน)
        if (tankProcessor != null)
        {
            tankProcessor.ProcessCommandText(textFromVosk);
        }

        // เริ่มต้นการทำงานเบื้องหลัง (เกมจะไม่ค้าง)
        StartCoroutine(PostToOllama(textFromVosk));
    }

    IEnumerator PostToOllama(string prompt)
    {
        string url = "http://localhost:11434/api/generate";
        
        // 1. จัดเตรียมข้อมูล JSON ที่จะส่งให้ Ollama
        string jsonToSend = "{\"model\": \"phi3\", \"prompt\": \"" + prompt + "\", \"stream\": false}";
        
        UnityWebRequest request = new UnityWebRequest(url, "POST");
        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonToSend);
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");

        Debug.Log("กำลังรอ AI คิด...");

        // 2. ปล่อยให้มันส่งข้อมูลไป เกมยังคงเล่นต่อไปได้ลื่นๆ (yield return)
        yield return request.SendWebRequest();

        // 3. เมื่อ AI คิดเสร็จและตอบกลับมา
        if (request.result == UnityWebRequest.Result.Success)
        {
            string aiResponse = request.downloadHandler.text;
            Debug.Log("AI ตอบว่า: " + aiResponse);
            
            // ตรงนี้ดึงคำตอบไปสั่งให้รถถังขยับ หรือเล่นเสียง
            if (tankProcessor != null)
            {
                tankProcessor.ProcessCommandText(aiResponse);
            }
        }
        else
        {
            Debug.LogError("Ollama Error: " + request.error);
        }
    }
}