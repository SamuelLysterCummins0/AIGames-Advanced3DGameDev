using UnityEngine;
using UnityEngine.InputSystem;

namespace Semester2
{
    /// <summary>
    /// Example implementation of a minimalistic FSM for NPC behavior.
    /// Demonstrates automatic state transitions based on player proximity and line of sight.
    /// 
    /// States now handle their own transitions autonomously based on game conditions.
    /// This example script only handles setup, input testing, and visualization.
    /// 
    /// States:
    /// - Idle: Default state, transitions to Patrol after a delay
    /// - Patrol: Moving between waypoints, monitors for player
    /// - Chase: Player detected within detection range
    /// - Attack: Player within attack range
    /// 
    /// Input System:
    /// - Uses the new Unity Input System for manual state testing
    /// - Keyboard 1-4 keys to manually trigger state changes
    /// </summary>
    public class NpcController : MonoBehaviour
    {
        [Header("NPC Detection Settings")]
        [Tooltip("Distance at which the NPC detects and starts chasing the player")]
        [SerializeField] private float detectionRange = 5f;
        
        [Tooltip("Distance at which the NPC can attack the player")]
        [SerializeField] private float attackRange = 2f;
        
        [Header("NPC Vision Settings")]
        [Tooltip("Field of view angle in degrees (e.g., 90 = 90° cone in front)")]
        [SerializeField] private float fieldOfViewAngle = 90f;
        
        [Tooltip("If true, player must be in FOV and not occluded to be detected")]
        [SerializeField] private bool requireLineOfSight = true;
        
        [Tooltip("Layer mask for obstacles that block line of sight")]
        [SerializeField] private LayerMask obstacleLayerMask = ~0; // All layers by default
        
        [Tooltip("Height offset for NPC eye position for line of sight raycasts")]
        [SerializeField] private float npcEyeHeight = 1.6f;
        
        [Tooltip("Height offset for player center mass for line of sight raycasts")]
        [SerializeField] private float playerCenterHeight = 1f;

        [Header("NPC Audio Detection Settings")]
        [Tooltip("Maximum distance at which the NPC can hear the player")]
        [SerializeField] private float hearingRange = 8f;
        
        [Tooltip("Minimum noise threshold for detection (0-1 scale)")]
        [SerializeField] private float minNoiseThreshold = 0.2f;
        
        [Tooltip("If true, obstacles will muffle sound")]
        [SerializeField] private bool useOcclusionForSound = true;
        
        [Tooltip("Multiplier for sound when occluded (0 = completely blocked, 1 = no effect)")]
        [SerializeField] private float soundOcclusionMultiplier = 0.5f;

        [Header("NPC Movement Settings")]
        [Tooltip("Speed when patrolling between waypoints")]
        [SerializeField] private float walkSpeed = 5f;
        
        [Tooltip("Speed when chasing the player")]
        [SerializeField] private float runSpeed = 9f;
        
        [Tooltip("Distance threshold to consider waypoint as reached")]
        [SerializeField] private float waypointReachedThreshold = 0.5f;

        [Header("NPC Behavior Settings")]
        [Tooltip("How long the NPC stays idle before starting to patrol")]
        [SerializeField] private float idleDuration = 2f;
        
        [Tooltip("Time between attack executions")]
        [SerializeField] private float attackCooldown = 1.5f;
        
        [Tooltip("How fast the NPC rotates to face the target during attack")]
        [SerializeField] private float attackRotationSpeed = 10f;

        [Header("Patrol Settings")]
        [Tooltip("Array of waypoints for the NPC to patrol between")]
        [SerializeField] private Transform[] patrolWaypoints;
        
        [Tooltip("If true, automatically create waypoints around the NPC's starting position")]
        [SerializeField] private bool autoGenerateWaypoints = false;
        
        [Tooltip("Radius for auto-generated waypoints")]
        [SerializeField] private float waypointRadius = 10f;
        
        [Tooltip("Number of waypoints to auto-generate")]
        [SerializeField] private int autoWaypointCount = 4;

        [Header("Random Behavior Settings")]
        [Tooltip("Enable random idle breaks during patrol (stops randomly between waypoints)")]
        [SerializeField] private bool enableRandomIdleDuringPatrol = true;

        [Tooltip("Minimum time before random idle can occur (seconds)")]
        [SerializeField] private float randomIdleMinInterval = 10f;

        [Tooltip("Maximum time before random idle occurs (seconds)")]
        [SerializeField] private float randomIdleMaxInterval = 20f;

        [Tooltip("How long the NPC stays idle when randomly stopping (seconds)")]
        [SerializeField] private float randomIdleDuration = 3f;

        [Tooltip("Enable pausing at each waypoint (more predictable than random idle)")]
        [SerializeField] private bool enableWaypointIdleStop = true;

        [Tooltip("Chance of stopping at each waypoint (0-1, e.g., 0.7 = 70% chance)")]
        [Range(0f, 1f)]
        [SerializeField] private float waypointIdleChance = 0.7f;

        [Tooltip("How long to pause at waypoints (seconds)")]
        [SerializeField] private float waypointIdleDuration = 2f;

        [Tooltip("Enable random direction changes (patrol backwards occasionally)")]
        [SerializeField] private bool enableRandomDirectionChange = false;

        [Tooltip("Chance of changing direction at each waypoint (0-1, e.g., 0.3 = 30% chance)")]
        [Range(0f, 1f)]
        [SerializeField] private float directionChangeChance = 0.3f;

        [Header("Debug Visualization")]
        [Tooltip("Show field of view cone in scene view")]
        [SerializeField] private bool showFieldOfView = true;
        
        [Tooltip("Show detection raycast in scene view")]
        [SerializeField] private bool showDetectionRaycast = true;

        [Header("Input Settings (Optional - for manual testing)")]
        [Tooltip("Input action for forcing Idle state (default: Keyboard 1)")]
        [SerializeField] private InputAction idleStateInput;
        
        [Tooltip("Input action for forcing Patrol state (default: Keyboard 2)")]
        [SerializeField] private InputAction patrolStateInput;
        
        [Tooltip("Input action for forcing Chase state (default: Keyboard 3)")]
        [SerializeField] private InputAction chaseStateInput;
        
        [Tooltip("Input action for forcing Attack state (default: Keyboard 4)")]
        [SerializeField] private InputAction attackStateInput;

        // The FSM instance for this NPC
        private MinimalisticFSM fsm;
        
        // Reference to patrol state for waypoint setup
        private NpcPatrolState patrolState;
        
        // Cached player reference for gizmo drawing
        private Transform player;

        /// <summary>
        /// Initialize the FSM and register all states.
        /// States now receive configuration parameters for all behavior values.
        /// </summary>
        void Start()
        {
            // Auto-generate waypoints if enabled and none are set
            if (autoGenerateWaypoints && (patrolWaypoints == null || patrolWaypoints.Length == 0))
            {
                GenerateWaypoints();
            }

            // Try to find the player for visualization
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null)
            {
                player = playerObj.transform;
            }

            // Create a new FSM instance for this NPC (not using singleton)
            fsm = new MinimalisticFSM();

            // Create configuration with all behavior parameters
            NpcConfig config = new NpcConfig
            {
                DetectionRange = detectionRange,
                AttackRange = attackRange,
                FieldOfViewAngle = fieldOfViewAngle,
                RequireLineOfSight = requireLineOfSight,
                ObstacleLayerMask = obstacleLayerMask,
                NpcEyeHeight = npcEyeHeight,
                PlayerCenterHeight = playerCenterHeight,
                HearingRange = hearingRange,
                WalkNoiseLevel = 0.3f,
                RunNoiseLevel = 1.0f,
                MinNoiseThreshold = minNoiseThreshold,
                UseOcclusionForSound = useOcclusionForSound,
                SoundOcclusionMultiplier = soundOcclusionMultiplier,
                WalkSpeed = walkSpeed,
                RunSpeed = runSpeed,
                IdleDuration = idleDuration,
                AttackCooldown = attackCooldown,
                WaypointReachedThreshold = waypointReachedThreshold,
                AttackRotationSpeed = attackRotationSpeed,
                EnableRandomIdleDuringPatrol = enableRandomIdleDuringPatrol,
                RandomIdleMinInterval = randomIdleMinInterval,
                RandomIdleMaxInterval = randomIdleMaxInterval,
                RandomIdleDuration = randomIdleDuration,
                EnableWaypointIdleStop = enableWaypointIdleStop,
                WaypointIdleChance = waypointIdleChance,
                WaypointIdleDuration = waypointIdleDuration,
                EnableRandomDirectionChange = enableRandomDirectionChange,
                DirectionChangeChance = directionChangeChance
            };

            // Register all possible states this NPC can be in
            // Pass configuration so states can use the configured values
            fsm.AddState(new NpcIdleState(gameObject, config));
            
            // Create patrol state and configure waypoints
            patrolState = new NpcPatrolState(gameObject, config);
            if (patrolWaypoints != null && patrolWaypoints.Length > 0)
            {
                patrolState.SetWaypoints(patrolWaypoints);
            }
            fsm.AddState(patrolState);
            
            fsm.AddState(new NpcChaseState(gameObject, config));
            fsm.AddState(new NpcAttackState(gameObject, config));

            // Set the initial state
            fsm.ChangeState<NpcIdleState>();
        }

        /// <summary>
        /// Automatically generates waypoints in a circle around the NPC's starting position.
        /// </summary>
        private void GenerateWaypoints()
        {
            GameObject waypointParent = new GameObject($"{gameObject.name}_Waypoints");
            waypointParent.transform.position = transform.position;
            
            patrolWaypoints = new Transform[autoWaypointCount];
            
            float angleStep = 360f / autoWaypointCount;
            
            for (int i = 0; i < autoWaypointCount; i++)
            {
                // Calculate position in a circle
                float angle = i * angleStep * Mathf.Deg2Rad;
                Vector3 waypointPosition = transform.position + new Vector3(
                    Mathf.Cos(angle) * waypointRadius,
                    0f,
                    Mathf.Sin(angle) * waypointRadius
                );
                
                // Create waypoint GameObject
                GameObject waypoint = new GameObject($"Waypoint_{i + 1}");
                waypoint.transform.position = waypointPosition;
                waypoint.transform.parent = waypointParent.transform;
                
                patrolWaypoints[i] = waypoint.transform;
            }
            
            Debug.Log($"[{gameObject.name}] Auto-generated {autoWaypointCount} waypoints at radius {waypointRadius}m");
        }

        /// <summary>
        /// Enable input actions when the component is enabled.
        /// </summary>
        void OnEnable()
        {
            // Setup default key bindings if not configured in Inspector
            if (idleStateInput == null || idleStateInput.bindings.Count == 0)
            {
                idleStateInput = new InputAction("IdleState", binding: "<Keyboard>/1");
            }
            if (patrolStateInput == null || patrolStateInput.bindings.Count == 0)
            {
                patrolStateInput = new InputAction("PatrolState", binding: "<Keyboard>/2");
            }
            if (chaseStateInput == null || chaseStateInput.bindings.Count == 0)
            {
                chaseStateInput = new InputAction("ChaseState", binding: "<Keyboard>/3");
            }
            if (attackStateInput == null || attackStateInput.bindings.Count == 0)
            {
                attackStateInput = new InputAction("AttackState", binding: "<Keyboard>/4");
            }

            // Subscribe to input events
            idleStateInput.performed += OnIdleStateInput;
            patrolStateInput.performed += OnPatrolStateInput;
            chaseStateInput.performed += OnChaseStateInput;
            attackStateInput.performed += OnAttackStateInput;

            // Enable all input actions
            idleStateInput.Enable();
            patrolStateInput.Enable();
            chaseStateInput.Enable();
            attackStateInput.Enable();
        }

        /// <summary>
        /// Disable input actions when the component is disabled.
        /// Includes null checks to prevent errors during scene cleanup.
        /// </summary>
        void OnDisable()
        {
            // Safely unsubscribe from input events with null checks
            if (idleStateInput != null)
            {
                idleStateInput.performed -= OnIdleStateInput;
                idleStateInput.Disable();
            }
            
            if (patrolStateInput != null)
            {
                patrolStateInput.performed -= OnPatrolStateInput;
                patrolStateInput.Disable();
            }
            
            if (chaseStateInput != null)
            {
                chaseStateInput.performed -= OnChaseStateInput;
                chaseStateInput.Disable();
            }
            
            if (attackStateInput != null)
            {
                attackStateInput.performed -= OnAttackStateInput;
                attackStateInput.Disable();
            }
        }

        /// <summary>
        /// Update the FSM. States now handle their own transitions autonomously.
        /// </summary>
        void Update()
        {
            // Update the current state's logic
            // States will handle their own transition logic based on game conditions
            fsm?.Update();
        }

        // ===== INPUT SYSTEM CALLBACKS =====
        // These methods are called when the corresponding input actions are triggered
        // Useful for manual testing and debugging state behavior

        /// <summary>
        /// Called when Idle state input is triggered (default: Keyboard 1).
        /// </summary>
        private void OnIdleStateInput(InputAction.CallbackContext context)
        {
            fsm?.ChangeState<NpcIdleState>();
        }

        /// <summary>
        /// Called when Patrol state input is triggered (default: Keyboard 2).
        /// </summary>
        private void OnPatrolStateInput(InputAction.CallbackContext context)
        {
            fsm?.ChangeState<NpcPatrolState>();
        }

        /// <summary>
        /// Called when Chase state input is triggered (default: Keyboard 3).
        /// </summary>
        private void OnChaseStateInput(InputAction.CallbackContext context)
        {
            fsm?.ChangeState<NpcChaseState>();
        }

        /// <summary>
        /// Called when Attack state input is triggered (default: Keyboard 4).
        /// </summary>
        private void OnAttackStateInput(InputAction.CallbackContext context)
        {
            fsm?.ChangeState<NpcAttackState>();
        }

        /// <summary>
        /// Clean up the FSM when this GameObject is destroyed.
        /// </summary>
        void OnDestroy()
        {
            fsm?.Clear();
        }

        /// <summary>
        /// Draw visual debugging gizmos in the Scene view.
        /// Yellow sphere = detection range
        /// Red sphere = attack range
        /// Orange sphere = hearing range
        /// Green cone = field of view
        /// Blue line = line of sight to player
        /// Green spheres = waypoints
        /// Cyan lines = patrol path
        /// </summary>
        void OnDrawGizmosSelected()
        {
            // Draw detection range (yellow)
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, detectionRange);
            
            // Draw attack range (red)
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, attackRange);
            
            // Draw hearing range (orange)
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.5f); // Orange
            Gizmos.DrawWireSphere(transform.position, hearingRange);
            
            // Draw field of view cone
            if (showFieldOfView)
            {
                DrawFieldOfViewGizmo();
            }
            
            // Draw line of sight to player
            if (showDetectionRaycast && player != null)
            {
                DrawLineOfSightGizmo();
            }
            
            // Draw waypoints and patrol path
            if (patrolWaypoints != null && patrolWaypoints.Length > 0)
            {
                Gizmos.color = Color.green;
                
                // Draw each waypoint
                foreach (Transform waypoint in patrolWaypoints)
                {
                    if (waypoint != null)
                    {
                        Gizmos.DrawWireSphere(waypoint.position, 0.5f);
                    }
                }
                
                // Draw lines between waypoints to show patrol path
                Gizmos.color = Color.cyan;
                for (int i = 0; i < patrolWaypoints.Length; i++)
                {
                    if (patrolWaypoints[i] != null)
                    {
                        Transform nextWaypoint = patrolWaypoints[(i + 1) % patrolWaypoints.Length];
                        if (nextWaypoint != null)
                        {
                            Gizmos.DrawLine(patrolWaypoints[i].position, nextWaypoint.position);
                        }
                    }
                }
            }
        }
        
        /// <summary>
        /// Draws the field of view cone in the scene view.
        /// </summary>
        private void DrawFieldOfViewGizmo()
        {
            float halfFOV = fieldOfViewAngle / 2f;
            Vector3 leftBoundary = Quaternion.Euler(0, -halfFOV, 0) * transform.forward * detectionRange;
            Vector3 rightBoundary = Quaternion.Euler(0, halfFOV, 0) * transform.forward * detectionRange;
            
            Gizmos.color = new Color(0, 1, 0, 0.2f); // Semi-transparent green
            
            // Draw the boundary lines
            Gizmos.DrawLine(transform.position, transform.position + leftBoundary);
            Gizmos.DrawLine(transform.position, transform.position + rightBoundary);
            
            // Draw arc for the field of view
            Vector3 previousPoint = transform.position + leftBoundary;
            int segments = 20;
            for (int i = 1; i <= segments; i++)
            {
                float angle = -halfFOV + (fieldOfViewAngle * i / segments);
                Vector3 direction = Quaternion.Euler(0, angle, 0) * transform.forward * detectionRange;
                Vector3 point = transform.position + direction;
                Gizmos.DrawLine(previousPoint, point);
                previousPoint = point;
            }
        }
        
        /// <summary>
        /// Draws the line of sight raycast to the player.
        /// Green if visible, red if occluded.
        /// </summary>
        private void DrawLineOfSightGizmo()
        {
            Vector3 npcEyePosition = transform.position + Vector3.up * npcEyeHeight;
            Vector3 playerPosition = player.position + Vector3.up * playerCenterHeight;
            Vector3 directionToPlayer = playerPosition - npcEyePosition;
            float distanceToPlayer = directionToPlayer.magnitude;
            
            // Check for occlusion
            RaycastHit hit;
            bool isOccluded = Physics.Raycast(npcEyePosition, directionToPlayer.normalized, out hit, distanceToPlayer, obstacleLayerMask);
            
            // Draw line - green if clear, red if occluded
            Gizmos.color = isOccluded ? Color.red : Color.green;
            Gizmos.DrawLine(npcEyePosition, playerPosition);
            
            // Draw small sphere at player position
            Gizmos.DrawWireSphere(playerPosition, 0.2f);
        }
    }
}
