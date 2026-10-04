using UnityEngine;

/// <summary>
/// Attach this script to a large Trigger Collider representing the destination port.
/// Make sure the GameObject has a Collider with "Is Trigger" checked.
/// </summary>
public class EndPort : MonoBehaviour
{
    /// <summary>Set by a story mission that must be done first (a rescue): false keeps the dock shut.</summary>
    public static System.Func<bool> CanFinish;

    private void OnTriggerEnter(Collider other)
    {
        // Check if the Ship entered the destination
        if (other.CompareTag("Player") || other.GetComponentInParent<ShipNavigator>() != null)
        {
            if (GameManager.Instance != null && GameManager.Instance.CurrentState == GameState.Playing)
            {
                if (CanFinish != null && !CanFinish()) return;
                GameManager.Instance.MissionSuccess();
            }
        }
    }
}
