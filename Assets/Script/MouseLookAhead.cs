using UnityEngine;

/// <summary>
/// Moves an object between the player and the mouse cursor, clamped to a max distance.
/// Use this object as the Follow target for your Cinemachine Camera.
/// </summary>
public class MouseLookAhead : MonoBehaviour
{
    [Tooltip("The actual ship/player to center around.")]
    [SerializeField] private Transform Player;

    [Tooltip("The maximum distance the camera can pan away from the player.")]
    [SerializeField] private float MaxPanDistance = 15f;

    [Tooltip("How strongly the mouse pulls the camera (0.5 means halfway to the mouse).")]
    [Range(0.1f, 1f)]
    [SerializeField] private float PanWeight = 0.35f;

    [Tooltip("How smoothly the camera target moves to the mouse position.")]
    [SerializeField] private float Smoothing = 5f;

    private Camera _mainCamera;

    private void Start()
    {
        _mainCamera = Camera.main;
        if (Player == null) 
        {
            Debug.LogWarning("[MouseLookAhead] Player Transform not assigned. Please assign your Ship to this slot.");
        }
    }

    private void Update()
    {
        if (_mainCamera == null || Player == null) return;

        // Cast a ray from the mouse position to a flat plane at the player's height
        Ray ray = _mainCamera.ScreenPointToRay(Input.mousePosition);
        Plane groundPlane = new Plane(Vector3.up, Player.position);
        
        if (groundPlane.Raycast(ray, out float enter))
        {
            Vector3 mouseWorldPosition = ray.GetPoint(enter);
            
            // Calculate the directional offset from the player to the mouse
            Vector3 offsetToMouse = mouseWorldPosition - Player.position;
            
            // Apply the weight (so it doesn't go all the way to the edge of the screen)
            Vector3 panOffset = offsetToMouse * PanWeight;
            
            // Clamp it so you can't look "too far" like Project Zomboid restricts
            if (panOffset.magnitude > MaxPanDistance)
            {
                panOffset = panOffset.normalized * MaxPanDistance;
            }

            // Smoothly move THIS object to the new calculated target position
            Vector3 targetPosition = Player.position + panOffset;
            transform.position = Vector3.Lerp(transform.position, targetPosition, Time.deltaTime * Smoothing);
        }
    }
}
