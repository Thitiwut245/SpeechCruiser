using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;

/// <summary>
/// The only script that writes Time.timeScale. Pauses (pause menu, tutorial) are requests
/// keyed by owner and always win; otherwise time eases between normal and radio slow-mo.
/// Lives on the persistent Services prefab.
/// </summary>
public class TimeController : Singleton<TimeController>
{
    [Header("Slow-Mo Settings")]
    [Tooltip("Time scale during PTT recording and AI processing (0.1 = 10% speed, very slow). 0 = full pause.")]
    [Range(0f, 1f)]
    [FormerlySerializedAs("slowMoTimeScale")]
    [SerializeField] private float SlowMoTimeScale = 0.15f;

    [Tooltip("How fast time smoothly transitions to/from slow-mo (higher = snappier).")]
    [FormerlySerializedAs("transitionSpeed")]
    [SerializeField] private float TransitionSpeed = 8f;

    private const float MinFixedDeltaTime = 0.0001f;

    private readonly HashSet<Object> _pauseOwners = new HashSet<Object>();
    private float _targetTimeScale = 1f;
    private float _normalFixedDeltaTime;
    private bool _pttActive = false;
    private int _aiRequests = 0;
    private bool _typing = false;
    private RadioMic _mic;

    public bool IsPaused
    {
        get
        {
            // An owner destroyed without releasing (e.g. by a scene load) must not pause forever.
            _pauseOwners.RemoveWhere(owner => owner == null);
            return _pauseOwners.Count > 0;
        }
    }

    protected override void Awake()
    {
        base.Awake();
        if (Instance != this) return;

        _normalFixedDeltaTime = Time.fixedDeltaTime;
        _mic = GetComponent<RadioMic>();
    }

    /// <summary>
    /// Why time is slowed. The microphone counts for itself while it records or decodes, so a
    /// reply landing from an earlier message can't end slow motion mid-sentence; each pending
    /// classifier or chat request counts once.
    /// </summary>
    private bool WantsSlowMo =>
        _pttActive || _aiRequests > 0 || _typing || (_mic != null && (_mic.IsRecording || _mic.IsTranscribing) && !_mic.IsTesting);

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void Update()
    {
        if (IsPaused)
        {
            Time.timeScale = 0f; // fixedDeltaTime is left alone: 0 is invalid and nothing steps while paused
            return;
        }

        _targetTimeScale = WantsSlowMo ? SlowMoTimeScale : 1f;
        Time.timeScale = Mathf.MoveTowards(Time.timeScale, _targetTimeScale, TransitionSpeed * Time.unscaledDeltaTime);
        Time.fixedDeltaTime = Mathf.Max(MinFixedDeltaTime, _normalFixedDeltaTime * Time.timeScale);
    }

    public void RequestPause(Object owner)
    {
        _pauseOwners.Add(owner);
        Time.timeScale = 0f;
    }

    public void ReleasePause(Object owner)
    {
        _pauseOwners.Remove(owner);
    }

    public void SetPTTActive(bool active)
    {
        _pttActive = active;
        Debug.Log($"⏱️ [TIME]: PTT {(active ? "ON — Entering Command Time" : "OFF")}. Target timeScale: {(WantsSlowMo ? SlowMoTimeScale : 1f)}");
    }

    /// <summary>The typed-order box is open: slow time like a voice order, since typing takes longer.</summary>
    public void SetTypingActive(bool active)
    {
        _typing = active;
    }

    /// <summary>A classifier or chat request started (true) or finished (false). Requests are counted.</summary>
    public void SetAIProcessing(bool active)
    {
        _aiRequests = Mathf.Max(0, _aiRequests + (active ? 1 : -1));
    }

    public void ForceNormalTime()
    {
        _pttActive = false;
        _aiRequests = 0;
        _typing = false;
        _pauseOwners.Clear();
        Time.timeScale = 1f;
        Time.fixedDeltaTime = _normalFixedDeltaTime;
    }

    // A new scene starts at normal speed: any radio exchange from the old scene is gone.
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        ForceNormalTime();
    }
}
