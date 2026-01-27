using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

/// <summary>
/// Point-and-Click Player Controller
/// 
/// This script allows the player to click anywhere in the game world and have
/// the player character navigate to that location using Unity's NavMesh system.
/// 
/// How it works:
/// 1. User clicks the mouse in the game view
/// 2. A ray is cast from the camera through the click position into the 3D world
/// 3. If the ray hits a valid surface, a target marker is placed at that point
/// 4. The NavMeshAgent component moves the player to that location
/// 
/// Requirements:
/// - NavMeshAgent component (automatically added by [RequireComponent])
/// - A GameObject in the scene tagged as "PointAndClickTarget" (visual marker)
/// - A baked NavMesh in your scene for pathfinding
/// - Unity's new Input System package installed
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class PointAndClickPlayer : MonoBehaviour
{
    // Reference to the NavMeshAgent component that handles pathfinding and movement
    NavMeshAgent agent;
    
    // Visual marker that shows where the player is moving to
    Transform PointAndClickTarget;
    
    [Header("Input Settings")]
    [Tooltip("Input action for mouse click (default: Mouse Left Button)")]
    [SerializeField] private InputAction clickAction;
    
    [Header("Raycast Settings")]
    [Tooltip("Layer mask for valid click surfaces (e.g., ground, terrain). Set this to only include walkable layers.")]
    [SerializeField] private LayerMask clickableLayer = ~0; // ~0 means "all layers"

    /// <summary>
    /// Start is called once before the first execution of Update.
    /// We use this to initialize our components and set up input.
    /// </summary>
    void Start()
    {
        // Try to find the visual target marker in the scene by its tag
        var pandc = GameObject.FindGameObjectWithTag("PointAndClickTarget");
        if (pandc != null)
            PointAndClickTarget = pandc.transform;
        else
            Debug.LogError("No PointAndClickTarget found in scene, make sure to add one with the correct tag.");
        
        // Get the NavMeshAgent component attached to this GameObject
        agent = GetComponent<NavMeshAgent>();
        
        // Setup default mouse click binding if not configured in Inspector
        // This uses Unity's new Input System to detect left mouse button clicks
        if (clickAction == null || clickAction.bindings.Count == 0)
        {
            clickAction = new InputAction("Click", binding: "<Mouse>/leftButton");
        }
        
        // Subscribe to the click event - when the button is pressed, call OnClick()
        clickAction.performed += OnClick;
        
        // Enable the input action so it starts listening for input
        clickAction.Enable();
    }

    /// <summary>
    /// Called when this GameObject is destroyed.
    /// Important: Always clean up input actions to prevent memory leaks!
    /// </summary>
    void OnDestroy()
    {
        // Clean up input action
        if (clickAction != null)
        {
            // Unsubscribe from the event
            clickAction.performed -= OnClick;
            
            // Disable the input action
            clickAction.Disable();
        }
    }

    /// <summary>
    /// Called when the player performs a click action (left mouse button by default).
    /// This is an event callback from the Input System.
    /// </summary>
    /// <param name="context">Contains information about the input event</param>
    private void OnClick(InputAction.CallbackContext context)
    {
        // STEP 1: Get the current mouse position on the screen (in pixels)
        Vector2 mousePosition = Mouse.current.position.ReadValue();
        
        // STEP 2: Convert the 2D screen position into a 3D ray shooting into the world
        // This ray starts at the camera and goes through the mouse position
        Ray ray = Camera.main.ScreenPointToRay(mousePosition);
        
        // STEP 3: Perform a raycast to see if we hit anything in the world
        RaycastHit hit; // This will store information about what we hit
        
        // Physics.Raycast shoots the ray and returns true if it hits something
        // - ray: The ray we created from the camera
        // - out hit: Stores the hit information (position, normal, object, etc.)
        // - Mathf.Infinity: Maximum distance (we want to check as far as possible)
        // - clickableLayer: Only hit objects on specific layers (e.g., ground, not UI)
        if (Physics.Raycast(ray, out hit, Mathf.Infinity, clickableLayer))
        {
            // STEP 4: Move the visual target marker to where we clicked
            // hit.point is the exact 3D position in the world where the ray hit
            if (PointAndClickTarget != null)
            {
                PointAndClickTarget.position = hit.point;
            }
            
            // STEP 5: Tell the NavMeshAgent to calculate a path and move to the clicked position
            // SetDestination() handles all the pathfinding automatically
            if (agent != null)
            {
                agent.SetDestination(hit.point);
            }
        }
        // If the raycast didn't hit anything (returns false), we do nothing
    }

    /// <summary>
    /// Update is called once per frame.
    /// Currently empty, but you could add logic here like:
    /// - Checking if the player has reached the destination
    /// - Playing movement animations
    /// - Handling rotation or speed adjustments
    /// </summary>
    void Update()
    {
        // Example ideas for future enhancements:
        // - Check if agent has reached destination: if (!agent.pathPending && agent.remainingDistance < 0.1f)
        // - Update animations based on agent.velocity.magnitude
        // - Show/hide the target marker when moving/stopped
    }
}
