using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Camera for a heavy ship: zooms out as speed builds so there is always several seconds of
/// water ahead, the mouse wheel adds the player's own zoom on top, and a hit shakes the view.
/// Lives on the CinemachineCamera.
/// </summary>
[RequireComponent(typeof(CinemachineCamera))]
public class ShipCamera : MonoBehaviour
{
    [Header("Speed zoom (orthographic size)")]
    [SerializeField] private float StoppedSize = 24f;
    [SerializeField] private float FullSpeedSize = 34f;
    [Tooltip("Speed that gets the full zoom-out.")]
    [SerializeField] private float FullSpeedKnots = 10f;
    [SerializeField] private float ZoomSmoothTime = 0.9f;

    [Header("Mouse wheel")]
    [SerializeField] private float WheelStep = 2f;
    [SerializeField] private float MinSize = 18f;
    [SerializeField] private float MaxSize = 44f;

    [Header("Hit shake")]
    [SerializeField] private float ShakeSeconds = 0.35f;
    [SerializeField] private float ShakeAmount = 0.9f;

    private CinemachineCamera _camera;
    private CinemachinePositionComposer _composer;
    private Vector3 _baseOffset;
    private float _userBias;
    private float _zoomVelocity;
    private float _shakeUntil;

    private void Awake()
    {
        _camera = GetComponent<CinemachineCamera>();
        _composer = GetComponent<CinemachinePositionComposer>();
        if (_composer != null) _baseOffset = _composer.TargetOffset;
    }

    private void OnEnable()
    {
        ShipHull.OnHullHit += HandleHit;
    }

    private void OnDisable()
    {
        ShipHull.OnHullHit -= HandleHit;
    }

    private void HandleHit(int containers)
    {
        _shakeUntil = Time.unscaledTime + ShakeSeconds * (containers >= 2 ? 1.6f : 1f);
    }

    private void Update()
    {
        float dt = Time.unscaledDeltaTime;

        var mouse = Mouse.current;
        if (mouse != null)
        {
            float wheel = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(wheel) > 0.01f) _userBias -= Mathf.Sign(wheel) * WheelStep;
        }

        float speed = ShipNavigator.Instance != null && ShipNavigator.Instance.Engine != null
            ? Mathf.Abs(ShipNavigator.Instance.Engine.Speed)
            : 0f;
        float speedSize = Mathf.Lerp(StoppedSize, FullSpeedSize, Mathf.Clamp01(speed / FullSpeedKnots));
        _userBias = Mathf.Clamp(_userBias, MinSize - speedSize, MaxSize - speedSize);

        var lens = _camera.Lens;
        lens.OrthographicSize = Mathf.SmoothDamp(lens.OrthographicSize, speedSize + _userBias, ref _zoomVelocity,
                                                 ZoomSmoothTime, Mathf.Infinity, dt);
        _camera.Lens = lens;

        if (_composer != null)
        {
            float left = _shakeUntil - Time.unscaledTime;
            _composer.TargetOffset = left > 0f
                ? _baseOffset + Random.insideUnitSphere * (ShakeAmount * Mathf.Clamp01(left / ShakeSeconds))
                : _baseOffset;
        }
    }
}
