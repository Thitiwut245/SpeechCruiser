using System.Collections;
using UnityEngine;

/// <summary>
/// Hull integrity and what a collision does. A hit costs one hull point, shakes cargo
/// overboard, pushes the ship clear of the rock, takes most of its way off and eases the
/// engine two steps, then gives a short grace window so one rock can't hit twice.
/// Mines explode and are gone. Losing the last point sinks the ship over a couple of seconds.
/// </summary>
public class ShipHull : MonoBehaviour
{
    public static event System.Action OnShipDestroyed;
    public static event System.Action<int, int> OnHealthChanged; // CurrentHealth, MaxHealth

    /// <summary>The hull struck something. Argument: containers shaken overboard (2 for a mine, 1 otherwise).</summary>
    public static event System.Action<int> OnHullHit;

    /// <summary>A hit during the tutorial drill: the ship bounces off, but nothing is damaged or lost.</summary>
    public static event System.Action OnPracticeBump;

    [Tooltip("Seconds between the crew's \"that was free\" remarks during practice.")]
    [SerializeField] private float PracticeRemarkCooldown = 8f;
    private float _nextPracticeRemark;

    [Header("Hull Integrity")]
    [SerializeField] private int MaxHealth = 3;

    [Header("Collision")]
    [Tooltip("Seconds after a hit during which the hull can't be hit again.")]
    [SerializeField] private float InvulnerableSeconds = 1.5f;

    [Tooltip("Fraction of the ship's speed that survives a hit.")]
    [Range(0f, 1f)]
    [SerializeField] private float KeptSpeedOnHit = 0.35f;

    [Tooltip("Clearance, in units, left between hull and hazard after the push-out.")]
    [SerializeField] private float PushClearance = 1.5f;

    [Tooltip("Speed of the bounce away from the hazard, units/sec.")]
    [SerializeField] private float BounceSpeed = 6f;

    [Header("Sinking")]
    [SerializeField] private float SinkSeconds = 2.5f;
    [SerializeField] private float SinkDepth = 5f;

    private int _currentHealth;
    private bool _isDestroyed = false;
    private float _invulnerableUntil;
    private Collider _collider;
    private int _lostThisHit;

    public int CurrentHealth => _currentHealth;
    public int MaxHealthAmount => MaxHealth;
    public bool IsInvulnerable => Time.time < _invulnerableUntil;

    // After a story emergency call was answered in time (EmergencyCall): a touch only scrapes.
    private float _sparedUntil = -1f;

    /// <summary>For <paramref name="seconds"/>, hitting a hazard only scrapes past it: pushed clear, no damage.</summary>
    public void Spare(float seconds) { _sparedUntil = Time.time + seconds; _spareSaid = false; }
    private bool _spareSaid;

    // Awake, so anything reading the hull in its own Start sees the carried-over value.
    private void Awake()
    {
        _collider = GetComponent<Collider>();
        var session = GameSession.Instance;
        _currentHealth = session != null && session.HullHealth >= 0
            ? Mathf.Clamp(session.HullHealth, 1, MaxHealth)
            : MaxHealth;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_isDestroyed) return;
        if (GameManager.Instance != null && GameManager.Instance.CurrentState != GameState.Playing) return;
        // The harbour pilot has her (or she's tied up): nothing can be hit.
        if (DockingPilot.Busy) return;
        // The quay: coming in too fast bumps it. No damage, but she's pushed off and slowed.
        if (other.name == "QuayEdge")
        {
            BumpQuay(other);
            return;
        }
        // A sandbank doesn't hole the hull: the ship runs aground and must back off.
        if (other.GetComponentInParent<Shoal>() != null)
        {
            RunAground();
            return;
        }
        if (IsInvulnerable || !other.CompareTag("Hazard")) return;

        _invulnerableUntil = Time.time + InvulnerableSeconds;
        if (Time.time < _sparedUntil)
        {
            PushClear(other);
            if (CrewVoice.Instance != null && !_spareSaid)
                CrewVoice.Instance.SpeakFrom(RadioChannel.Lookout, new[] { "We scraped past it, Captain! No damage! Over." },
                    ShipTelemetry.Capture(), "hull:spared");
            _spareSaid = true;
            return;
        }
        bool mine = RadarSystem.KindOf(other) == "Mine";

        // The practice drill promises the ship can't be damaged while it runs: bounce, slow down,
        // explain, but take nothing. Mines don't go off either.
        if (TutorialManager.Instance != null && TutorialManager.Instance.IsPractice)
        {
            PushClear(other);
            OnPracticeBump?.Invoke();
            if (Time.time >= _nextPracticeRemark && CrewVoice.Instance != null)
            {
                _nextPracticeRemark = Time.time + PracticeRemarkCooldown;
                CrewVoice.Instance.SpeakFrom(RadioChannel.ChiefEngineer, CrewBarkLibrary.PracticeBump(mine),
                    ShipTelemetry.Capture(), "hull:practice");
            }
            return;
        }

        int aboardBefore = GameSession.Instance != null ? GameSession.Instance.CargoAboard : 0;
        OnHullHit?.Invoke(mine ? 2 : 1); // ShipyardManager drops the containers
        _lostThisHit = aboardBefore - (GameSession.Instance != null ? GameSession.Instance.CargoAboard : 0);
        // A fatal hit doesn't ease the engine: the ship is going down, not answering the telegraph.
        PushClear(other, easeEngine: _currentHealth > 1);
        if (mine) Detonate(other);

        TakeDamage(1);
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.GetComponentInParent<Shoal>() == null) return;
        var nav = ShipNavigator.Instance;
        if (nav == null || nav.Engine == null || nav.Engine.AgroundDirection == 0) return;
        nav.Engine.Refloat();
        if (CrewVoice.Instance != null)
            CrewVoice.Instance.SpeakFrom(RadioChannel.Lookout, new[] { "We're afloat again, Captain. Deep water." },
                ShipTelemetry.Capture(), "hull:refloat");
    }

    private float _nextBump;

    /// <summary>Into the quay without the pilot: pushed off, slowed, and told how to dock.</summary>
    private void BumpQuay(Collider quay)
    {
        if (Time.time < _nextBump) return;
        _nextBump = Time.time + InvulnerableSeconds;
        PushClear(quay);
        if (CrewVoice.Instance != null)
            CrewVoice.Instance.SpeakFrom(RadioChannel.Lookout,
                new[] { "We bumped the quay, Captain! No damage. Come in slowly and say \"request docking\" for the pilot." },
                ShipTelemetry.Capture(), "hull:quay");
    }

    /// <summary>
    /// Onto a sandbank: the ship stops dead, no damage, and the engineer says how to get off
    /// (back the other way). Only the first touch counts until the ship is afloat again.
    /// </summary>
    private void RunAground()
    {
        var nav = ShipNavigator.Instance;
        if (nav == null || nav.Engine == null || nav.Engine.AgroundDirection != 0) return;
        float speed = nav.Engine.Speed;
        int direction = Mathf.Abs(speed) > 0.05f ? (speed > 0f ? 1 : -1) : (nav.Engine.OrderedSpeed >= 0f ? 1 : -1);
        nav.Engine.RunAground(direction);
        if (nav.Movement != null) nav.Movement.KillTurn();
        if (CrewVoice.Instance != null)
        {
            string line = direction > 0
                ? "We've run aground in the shallows! No damage. Order slow astern to back off."
                : "We've backed into the shallows! No damage. Order slow ahead to get off.";
            CrewVoice.Instance.SpeakFrom(RadioChannel.ChiefEngineer, new[] { line }, ShipTelemetry.Capture(), "hull:aground");
        }
    }

    /// <summary>
    /// Still touching a hazard when the grace window ends (the bounce carried the ship into
    /// it, or it was entered while protected): that counts as a new hit.
    /// </summary>
    private void OnTriggerStay(Collider other)
    {
        if (!IsInvulnerable) OnTriggerEnter(other);
    }

    /// <summary>Moves the ship out of the hazard along the contact normal, then bounces it away.</summary>
    private void PushClear(Collider other, bool easeEngine = true)
    {
        var nav = ShipNavigator.Instance;
        if (nav == null) return;
        Transform ship = nav.transform;

        Vector3 away = transform.position - other.ClosestPoint(transform.position);
        away.y = 0f;
        if (away.sqrMagnitude < 0.0001f) away = -ship.forward;
        away.Normalize();

        if (_collider != null && Physics.ComputePenetration(
                _collider, _collider.transform.position, _collider.transform.rotation,
                other, other.transform.position, other.transform.rotation,
                out Vector3 direction, out float distance))
        {
            direction.y = 0f;
            if (direction.sqrMagnitude > 0.0001f) away = direction.normalized;
            ship.position += away * (distance + PushClearance);
        }

        if (nav.Movement != null)
        {
            nav.Movement.KillTurn();
            nav.Movement.ApplyKnockback(away * BounceSpeed);
        }
        if (nav.Engine != null && easeEngine) nav.Engine.AbsorbImpact(KeptSpeedOnHit);
    }

    /// <summary>A mine goes off once: the hazard is removed so it can't be hit again.</summary>
    private static void Detonate(Collider mine)
    {
        Vector3 at = mine.bounds.center;
        Transform t = mine.transform;
        while (t.parent != null && !t.parent.name.StartsWith("GeneratedLevel")) t = t.parent;
        t.gameObject.SetActive(false);

        // The chart stops showing it once the whole field there is gone.
        if (LevelGenerator.Instance != null) LevelGenerator.Instance.ClearHazardAt(at);
    }

    private void TakeDamage(int damage)
    {
        _currentHealth -= damage;
        if (GameSession.Instance != null) GameSession.Instance.HullHealth = Mathf.Max(0, _currentHealth);
        OnHealthChanged?.Invoke(_currentHealth, MaxHealth);
        Debug.Log($"[HULL]: Hit a hazard! Hull integrity: {_currentHealth}/{MaxHealth}");

        if (_currentHealth <= 0)
        {
            CrashShip();
            return;
        }

        // One damage report covers the hull, the cargo and the engine.
        var session = GameSession.Instance;
        var nav = ShipNavigator.Instance;
        if (CrewVoice.Instance != null && session != null && nav != null && nav.Engine != null)
        {
            CrewVoice.Instance.SpeakFrom(RadioChannel.ChiefEngineer,
                CrewBarkLibrary.HullDamage(_currentHealth, MaxHealth, _lostThisHit, session.CargoAboard, nav.Engine.Gear),
                ShipTelemetry.Capture(), "hull:damage");
        }
    }

    private void CrashShip()
    {
        _isDestroyed = true;

        var nav = ShipNavigator.Instance;

        // Announce first: GameManager ends the voyage (and stops the engine) before anything
        // below touches the telegraph, so nothing mistakes the wreck for an engine order.
        OnShipDestroyed?.Invoke();

        // Stop the ship for good. ShipMovement has to go too, or it keeps
        // translating and banking the wreck every frame.
        if (nav != null)
        {
            nav.SetSpeed(0f);
            if (nav.Movement != null) nav.Movement.enabled = false;
            nav.enabled = false;
        }

        // This component lives on a child trigger collider, so the wreck pose goes on the hull root.
        Transform wreck = nav != null ? nav.transform : transform;
        StartCoroutine(Sink(wreck));

        Debug.Log("[HULL]: We struck a reef! Abandon ship!");
    }

    private IEnumerator Sink(Transform wreck)
    {
        Vector3 start = wreck.position;
        Quaternion startRotation = wreck.rotation;
        Quaternion endRotation = startRotation * Quaternion.Euler(14f, 0f, 22f);

        for (float t = 0f; t < SinkSeconds; t += Time.deltaTime)
        {
            float k = t / SinkSeconds;
            float eased = k * k * (3f - 2f * k);
            wreck.position = start + Vector3.down * (SinkDepth * eased);
            wreck.rotation = Quaternion.Slerp(startRotation, endRotation, Mathf.Min(1f, k * 1.6f));
            yield return null;
        }
        wreck.position = start + Vector3.down * SinkDepth;
        wreck.rotation = endRotation;
    }

    public void RepairShip() => RepairPoints(MaxHealth);

    /// <summary>Restores up to <paramref name="points"/> hull points (the shipyard sells them one at a time).</summary>
    public void RepairPoints(int points)
    {
        _isDestroyed = false;
        _currentHealth = Mathf.Min(MaxHealth, Mathf.Max(_currentHealth, 0) + Mathf.Max(0, points));
        if (GameSession.Instance != null) GameSession.Instance.HullHealth = _currentHealth;
        OnHealthChanged?.Invoke(_currentHealth, MaxHealth);

        var nav = ShipNavigator.Instance;
        if (nav != null)
        {
            nav.enabled = true;
            if (nav.Movement != null) nav.Movement.enabled = true;
        }
        Debug.Log("[HULL]: Ship repaired and ready for sea.");
    }
}
