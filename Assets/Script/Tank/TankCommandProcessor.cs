using UnityEngine;
using Yetibyte.Unity.SpeechRecognition.KeywordDetection;

public class TankCommandProcessor : MonoBehaviour
{
    [Header("Movement Settings")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float turnSpeed = 60f;

    [Header("Combat Settings")]
    [Tooltip("Prefab ของลูกปืน (เช่น Sphere หรือ Bullet Prefab)")]
    [SerializeField] private GameObject projectilePrefab;
    [Tooltip("ตำแหน่งที่จะปล่อยลูกปืน (ถ้าไม่ใส่ จะใช้านหน้าของรถถังอัตโนมัติ)")]
    [SerializeField] private Transform firePoint;
    [SerializeField] private float projectileSpeed = 20f;
    [SerializeField] private float projectileLifeTime = 5f;

    private float currentMoveSpeed = 0f;
    private float currentTurnSpeed = 0f;

    private void Update()
    {
        // คำนวณการเคลื่อนที่และการหมุนในแต่ละเฟรม
        if (currentMoveSpeed != 0f)
        {
            transform.Translate(Vector3.forward * currentMoveSpeed * Time.deltaTime);
        }

        if (currentTurnSpeed != 0f)
        {
            transform.Rotate(Vector3.up * currentTurnSpeed * Time.deltaTime);
        }
    }

    #region Basic Commands (สำหรับการเรียกใช้ผ่าน Code หรือ UI Button)
    public void MoveForward()
    {
        Debug.Log("Tank Command: เดินหน้า (Move Forward)");
        currentMoveSpeed = moveSpeed;
    }

    public void MoveBackward()
    {
        Debug.Log("Tank Command: ถอยหลัง (Move Backward)");
        currentMoveSpeed = -moveSpeed;
    }

    public void TurnLeft()
    {
        Debug.Log("Tank Command: เลี้ยวซ้าย (Turn Left)");
        currentTurnSpeed = -turnSpeed;
    }

    public void TurnRight()
    {
        Debug.Log("Tank Command: เลี้ยวขวา (Turn Right)");
        currentTurnSpeed = turnSpeed;
    }

    public void Stop()
    {
        Debug.Log("Tank Command: หยุด (Stop)");
        currentMoveSpeed = 0f;
        currentTurnSpeed = 0f;
    }

    public void Fire()
    {
        Debug.Log("Tank Command: ยิงปืน (Fire Projectile)!");
        if (projectilePrefab != null)
        {
            Vector3 spawnPos = firePoint != null ? firePoint.position : transform.position + transform.forward * 1.5f + Vector3.up * 0.5f;
            Quaternion spawnRot = firePoint != null ? firePoint.rotation : transform.rotation;

            GameObject projectile = Instantiate(projectilePrefab, spawnPos, spawnRot);
            
            Rigidbody rb = projectile.GetComponent<Rigidbody>();
            if (rb == null)
            {
                rb = projectile.AddComponent<Rigidbody>();
            }
            rb.useGravity = false;
            rb.velocity = spawnRot * Vector3.forward * projectileSpeed;

            Destroy(projectile, projectileLifeTime);
        }
        else
        {
            Debug.LogWarning("TankCommandProcessor: ไม่ได้ใส่ Projectile Prefab กรุณาใส่ Prefab ใน Inspector");
        }
    }
    #endregion

    #region Vosk Voice Command Handlers (เข้ากันได้กับ Vosk4Unity KeywordDetector)
    public void OnVoiceCommand_MoveForward(VoiceCommand voiceCommand, string detectedText)
    {
        MoveForward();
    }

    public void OnVoiceCommand_MoveBackward(VoiceCommand voiceCommand, string detectedText)
    {
        MoveBackward();
    }

    public void OnVoiceCommand_TurnLeft(VoiceCommand voiceCommand, string detectedText)
    {
        TurnLeft();
    }

    public void OnVoiceCommand_TurnRight(VoiceCommand voiceCommand, string detectedText)
    {
        TurnRight();
    }

    public void OnVoiceCommand_Stop(VoiceCommand voiceCommand, string detectedText)
    {
        Stop();
    }

    public void OnVoiceCommand_Fire(VoiceCommand voiceCommand, string detectedText)
    {
        Fire();
    }
    #endregion

    #region Text / LLM Command Processing
    // ฟังก์ชันสำหรับรับข้อความจาก Vosk หรือคำตอบจาก LLM (Ollama) มาวิเคราะห์สั่งการอัตโนมัติ
    public void ProcessCommandText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        text = text.ToLower();
        Debug.Log("TankCommandProcessor วิเคราะห์คำสั่ง: " + text);

        if (text.Contains("fire") || text.Contains("shoot") || text.Contains("ยิง") || text.Contains("attack"))
        {
            Fire();
        }
        else if (text.Contains("forward") || text.Contains("เดินหน้า") || text.Contains("ไปข้างหน้า") || text.Contains("move forward"))
        {
            MoveForward();
        }
        else if (text.Contains("back") || text.Contains("ถอยหลัง") || text.Contains("ถอย"))
        {
            MoveBackward();
        }
        else if (text.Contains("left") || text.Contains("ซ้าย"))
        {
            TurnLeft();
        }
        else if (text.Contains("right") || text.Contains("ขวา"))
        {
            TurnRight();
        }
        else if (text.Contains("stop") || text.Contains("หยุด") || text.Contains("จอด"))
        {
            Stop();
        }
    }
    #endregion
}
