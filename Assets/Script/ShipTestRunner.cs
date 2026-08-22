using System.Collections;
using UnityEngine;

/// <summary>
/// A utility script to automatically test all ship movement and helm commands.
/// Attach this to any GameObject (like the Ship or GameManager) and check "Run On Start",
/// or right-click the component and select "Run Test Sequence".
/// </summary>
public class ShipTestRunner : MonoBehaviour
{
    [Tooltip("If true, the test sequence will automatically begin when you press Play.")]
    [SerializeField] private bool RunOnStart = false;
    
    private void Start()
    {
        if (RunOnStart)
        {
            StartCoroutine(RunTestSequence());
        }
    }
    
    [ContextMenu("Run Test Sequence")]
    public void StartTests()
    {
        StartCoroutine(RunTestSequence());
    }

    private IEnumerator RunTestSequence()
    {
        var nav = ShipNavigator.Instance;
        if (nav == null)
        {
            Debug.LogError("🔴 [TEST] ShipNavigator instance not found! Make sure the Ship exists in the scene.");
            yield break;
        }

        Debug.Log("🟢 [TEST] --- STARTING MOVEMENT TEST SEQUENCE ---");

        Debug.Log("🔵 [TEST 1/9] Engine: Dead Slow Ahead (Accelerating)");
        nav.SetGear(ShipEngine.TelegraphGear.DeadSlow);
        yield return new WaitForSeconds(4f);

        Debug.Log("🔵 [TEST 2/9] Engine: Half Ahead (Accelerating further)");
        nav.SetGear(ShipEngine.TelegraphGear.Half);
        yield return new WaitForSeconds(4f);

        Debug.Log("🔵 [TEST 3/9] Engine: Full Ahead (Max Speed)");
        nav.SetGear(ShipEngine.TelegraphGear.Full);
        yield return new WaitForSeconds(4f);

        Debug.Log("🔵 [TEST 4/9] Helm: Turn Starboard (20 deg manual rudder)");
        nav.TurnShip(20f);
        yield return new WaitForSeconds(5f);

        Debug.Log("🔵 [TEST 5/9] Helm: Hard Port (Max deflection manual rudder)");
        nav.HardTurn(-1f);
        yield return new WaitForSeconds(6f);

        Debug.Log("🔵 [TEST 6/9] Helm: Rudder Midships (Straightening out)");
        nav.RudderMidships();
        yield return new WaitForSeconds(4f);

        Debug.Log("🔵 [TEST 7/9] Autopilot: Turn Port 90 degrees (Testing Autopilot)");
        nav.ChangeHeading(-90f);
        // Give the autopilot plenty of time to complete the 90 degree turn at full speed
        yield return new WaitForSeconds(10f);

        Debug.Log("🔵 [TEST 8/9] Engine: Stop (Decelerating)");
        nav.SetGear(ShipEngine.TelegraphGear.Stop);
        yield return new WaitForSeconds(4f);

        Debug.Log("🔵 [TEST 9/9] Engine: Full Astern (Reversing)");
        nav.SetGear(ShipEngine.TelegraphGear.FullAstern);
        yield return new WaitForSeconds(6f);

        Debug.Log("🟢 [TEST] --- TEST SEQUENCE COMPLETE ---");
        nav.SetGear(ShipEngine.TelegraphGear.Stop);
    }
}
