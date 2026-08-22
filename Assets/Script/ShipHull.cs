using UnityEngine;

public class ShipHull : MonoBehaviour
{
    public static event System.Action OnShipDestroyed;

    private bool _isDestroyed = false;

    // This built-in Unity function runs the exact frame the ship touches the Reef
    private void OnTriggerEnter(Collider other)
    {
        if (_isDestroyed) return;

        if (other.CompareTag("Hazard"))
        {
            CrashShip();
        }
    }

    private void CrashShip()
    {
        _isDestroyed = true;
        
        var nav = ShipNavigator.Instance;

        // 1. Stop the ship permanently. ShipMovement has to go too, or it keeps
        // translating and banking the wreck every frame.
        if (nav != null)
        {
            nav.SetSpeed(0f);
            if (nav.Movement != null) nav.Movement.enabled = false;
            nav.enabled = false;
        }

        // 2. Broadcast Game Over event
        OnShipDestroyed?.Invoke();

        // 3. Make the ship "sink" visually. This component lives on a child
        // trigger collider, so the wreck pose has to be applied to the hull root.
        Transform wreck = nav != null ? nav.transform : transform;
        wreck.Rotate(15f, 0f, 15f);
        wreck.position = new Vector3(wreck.position.x, -1f, wreck.position.z);

        Debug.Log("[HULL]: We struck a reef! Abandon ship!");
    }

    public void RepairShip()
    {
        _isDestroyed = false;
        
        var nav = ShipNavigator.Instance;
        if (nav != null)
        {
            nav.enabled = true;
            if (nav.Movement != null) nav.Movement.enabled = true;
        }
        Debug.Log("[HULL]: Ship repaired and ready for sea.");
    }
}
