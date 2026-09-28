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
    private bool _aiProcessing = false;
    private bool _typing = false;

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
    }

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

        _targetTimeScale = (_pttActive || _aiProcessing || _typing) ? SlowMoTimeScale : 1f;
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
        Debug.Log($"⏱️ [TIME]: PTT {(active ? "ON — Entering Command Time" : "OFF")}. Target timeScale: {(_pttActive || _aiProcessing ? SlowMoTimeScale : 1f)}");
    }

    /// <summary>The typed-order box is open: slow time like a voice order, since typing takes longer.</summary>
    public void SetTypingActive(bool active)
    {
        _typing = active;
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
