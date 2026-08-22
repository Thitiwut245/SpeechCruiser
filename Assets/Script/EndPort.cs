using UnityEngine;

/// <summary>
/// Attach this script to a large Trigger Collider representing the destination port.
/// Make sure the GameObject has a Collider with "Is Trigger" checked.
/// </summary>
public class EndPort : MonoBehaviour
{
    private void OnTriggerEnter(Collider other)
    {
        // Check if the Ship entered the destination
        if (other.CompareTag("Player") || other.GetComponentInParent<ShipNavigator>() != null)
        {
            if (GameManager.Instance != null && GameManager.Instance.CurrentState == GameState.Playing)
            {
                GameManager.Instance.MissionSuccess();
            }
        }
    }
}
