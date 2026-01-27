using UnityEngine;
using UnityEngine.AI;

namespace Semester2
{
    /// <summary>
    /// Configuration data for NPC behavior parameters.
    /// Allows centralized control of all NPC state behavior values.
    /// </summary>
    public class NpcConfig
    {
        // Detection ranges
        public float DetectionRange { get; set; }
        public float AttackRange { get; set; }
        
        // Field of View settings
        public float FieldOfViewAngle { get; set; }
        public bool RequireLineOfSight { get; set; }
        public LayerMask ObstacleLayerMask { get; set; }
        
        // Line of sight raycast heights
        public float NpcEyeHeight { get; set; }
        public float PlayerCenterHeight { get; set; }
        
        // Movement speeds
        public float WalkSpeed { get; set; }
        public float RunSpeed { get; set; }
        
        // State-specific timings
        public float IdleDuration { get; set; }
        public float AttackCooldown { get; set; }
        
        // Navigation parameters
        public float WaypointReachedThreshold { get; set; }
        public float AttackRotationSpeed { get; set; }
        
        /// <summary>
        /// Creates default configuration values.
        /// </summary>
        public static NpcConfig Default()
        {
            return new NpcConfig
            {
                DetectionRange = 5f,
                AttackRange = 2f,
                FieldOfViewAngle = 90f,
                RequireLineOfSight = true,
                ObstacleLayerMask = LayerMask.GetMask("Default"),
                NpcEyeHeight = 1.6f,
                PlayerCenterHeight = 1f,
                WalkSpeed = 5f,
                RunSpeed = 9f,
                IdleDuration = 2f,
                AttackCooldown = 1.5f,
                WaypointReachedThreshold = 0.5f,
                AttackRotationSpeed = 10f
            };
        }
    }

    /// <summary>
    /// Base class for all NPC states.
    /// Provides common functionality and shared data access for NPC behavior.
    /// Handles transition logic based on player proximity.
    /// </summary>
    public abstract class NpcStateBase : IState
    {
        // Owner GameObject reference
        protected GameObject owner;
        protected string npcName;
        
        // Cached components
        protected NavMeshAgent navMeshAgent;
        protected Animator animator;
        
        // FSM reference for autonomous state transitions
        protected MinimalisticFSM fsm;
        
        // Shared data for all NPC states
        protected Transform player;
        protected NpcConfig config;
        
        /// <summary>
        /// Constructor that caches common references and components.
        /// </summary>
        /// <param name="ownerGameObject">The GameObject that owns this state (the NPC)</param>
        /// <param name="config">Configuration parameters for NPC behavior</param>
        public NpcStateBase(GameObject ownerGameObject, NpcConfig config)
        {
            owner = ownerGameObject;
            npcName = owner.name;
            this.config = config;
            
            // Cache commonly used components
            navMeshAgent = owner.GetComponent<NavMeshAgent>();
            animator = owner.GetComponentInChildren<Animator>();
            
            // Try to find the player
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null)
            {
                player = playerObj.transform;
            }
        }
        
        /// <summary>
        /// Provides the state with a reference to its FSM.
        /// This allows states to trigger their own transitions.
        /// </summary>
        public void SetFSM(MinimalisticFSM fsm)
        {
            this.fsm = fsm;
        }
        
        /// <summary>
        /// Gets the current distance to the player.
        /// Returns float.MaxValue if player is not found.
        /// </summary>
        protected float GetDistanceToPlayer()
        {
            if (player == null || owner == null)
            {
                return float.MaxValue;
            }
            
            return Vector3.Distance(owner.transform.position, player.position);
        }
        
        /// <summary>
        /// Checks if the player is within the NPC's field of view.
        /// </summary>
        /// <returns>True if player is within the FOV angle</returns>
        protected bool IsPlayerInFieldOfView()
        {
            if (player == null || owner == null)
            {
                return false;
            }
            
            // Calculate direction to player
            Vector3 directionToPlayer = (player.position - owner.transform.position).normalized;
            
            // Calculate the angle between NPC's forward direction and direction to player
            float angleToPlayer = Vector3.Angle(owner.transform.forward, directionToPlayer);
            
            // Check if angle is within half of the FOV (since Angle returns absolute value)
            return angleToPlayer <= config.FieldOfViewAngle / 2f;
        }
        
        /// <summary>
        /// Checks if there's a clear line of sight to the player (no obstacles blocking).
        /// Uses raycasting to detect occlusion.
        /// </summary>
        /// <returns>True if player is visible (not occluded)</returns>
        protected bool HasClearLineOfSight()
        {
            if (player == null || owner == null)
            {
                return false;
            }
            
            // Calculate direction and distance to player
            Vector3 npcEyePosition = owner.transform.position + Vector3.up * config.NpcEyeHeight;
            Vector3 playerPosition = player.position + Vector3.up * config.PlayerCenterHeight;
            Vector3 directionToPlayer = playerPosition - npcEyePosition;
            float distanceToPlayer = directionToPlayer.magnitude;
            
            // Perform raycast to check for obstacles
            RaycastHit hit;
            if (Physics.Raycast(npcEyePosition, directionToPlayer.normalized, out hit, distanceToPlayer, config.ObstacleLayerMask))
            {
                // Something is blocking the view
                return false;
            }
            
            // No obstacles blocking, clear line of sight
            return true;
        }
        
        /// <summary>
        /// Checks if the NPC has complete line of sight to the player.
        /// Combines distance, field of view, and occlusion checks.
        /// </summary>
        /// <param name="maxDistance">Maximum distance for detection</param>
        /// <returns>True if player is detectable (in range, in FOV, and not occluded)</returns>
        protected bool HasLineOfSightToPlayer(float maxDistance)
        {
            // First check distance (early exit for performance)
            if (GetDistanceToPlayer() > maxDistance)
            {
                return false;
            }
            
            // If line of sight is not required, just use distance
            if (!config.RequireLineOfSight)
            {
                return true;
            }
            
            // Check if player is in field of view
            if (!IsPlayerInFieldOfView())
            {
                return false;
            }
            
            // Check if there are obstacles blocking the view
            if (!HasClearLineOfSight())
            {
                return false;
            }
            
            // All checks passed - player is detectable
            return true;
        }
        
        /// <summary>
        /// Checks if player is within detection range with line of sight.
        /// </summary>
        protected bool IsPlayerInDetectionRange()
        {
            return HasLineOfSightToPlayer(config.DetectionRange);
        }
        
        /// <summary>
        /// Checks if player is within attack range with line of sight.
        /// </summary>
        protected bool IsPlayerInAttackRange()
        {
            return HasLineOfSightToPlayer(config.AttackRange);
        }
        
        // Abstract methods that child classes must implement
        public abstract void OnEnter();
        public abstract void OnUpdate();
        public abstract void OnExit();
    }
}
