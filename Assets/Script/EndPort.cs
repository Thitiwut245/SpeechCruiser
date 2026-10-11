using UnityEngine;

/// <summary>
/// The destination's gate for story missions. The voyage itself ends at the berth
/// (DockingPilot: "request docking", then "make fast"); this only says whether it may.
/// Kept as a component so older harbour prefabs that carry it still load.
/// </summary>
public class EndPort : MonoBehaviour
{
    /// <summary>Set by a story mission that must be done first (a rescue): false keeps the berth shut.</summary>
    public static System.Func<bool> CanFinish;
}
