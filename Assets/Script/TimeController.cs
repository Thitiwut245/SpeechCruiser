using UnityEngine;
using UnityEngine.Serialization;

public class TimeController : MonoBehaviour
{
    public static TimeController Instance;

    [Header("Slow-Mo Settings")]
    [Tooltip("Time scale during PTT recording and AI processing (0.1 = 10% speed, very slow). 0 = full pause.")]
    [Range(0f, 1f)]
    [FormerlySerializedAs("slowMoTimeScale")]
    [SerializeField] private float SlowMoTimeScale = 0.15f;

    [Tooltip("How fast time smoothly transitions to/from slow-mo (higher = snappier).")]
    [FormerlySerializedAs("transitionSpeed")]
    [SerializeField] private float TransitionSpeed = 8f;

    private float _targetTimeScale = 1f;
    private float _normalFixedDeltaTime;
    private bool _pttActive = false;
    private bool _aiProcessing = false;

    private void Awake()
    {
        Instance = this;
        _normalFixedDeltaTime = Time.fixedDeltaTime;
    }

    private void Update()
    {
        _targetTimeScale = (_pttActive || _aiProcessing) ? SlowMoTimeScale : 1f;
        Time.timeScale = Mathf.MoveTowards(Time.timeScale, _targetTimeScale, TransitionSpeed * Time.unscaledDeltaTime);
        Time.fixedDeltaTime = _normalFixedDeltaTime * Time.timeScale;
    }

    public void SetPTTActive(bool active)
    {
        _pttActive = active;
        Debug.Log($"⏱️ [TIME]: PTT {(active ? "ON — Entering Command Time" : "OFF")}. Target timeScale: {(_pttActive || _aiProcessing ? SlowMoTimeScale : 1f)}");
    }

    public void SetAIProcessing(bool active)
    {
        _aiProcessing = active;
        Debug.Log($"⏱️ [TIME]: AI Processing {(active ? "STARTED — Holding Command Time" : "DONE — Restoring normal time")}. Target timeScale: {(_pttActive || _aiProcessing ? SlowMoTimeScale : 1f)}");
    }

    public void ForceNormalTime()
    {
        _pttActive = false;
        _aiProcessing = false;
        Time.timeScale = 1f;
        Time.fixedDeltaTime = _normalFixedDeltaTime;
    }
}
