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
        
        // Audio detection settings
        public float HearingRange { get; set; }
        public float WalkNoiseLevel { get; set; }
        public float RunNoiseLevel { get; set; }
        public float MinNoiseThreshold { get; set; }
        public bool UseOcclusionForSound { get; set; }
        public float SoundOcclusionMultiplier { get; set; }
        
        // Movement speeds
        public float WalkSpeed { get; set; }
        public float RunSpeed { get; set; }
        
        // State-specific timings
        public float IdleDuration { get; set; }
        public float AttackCooldown { get; set; }
        
        // Navigation parameters
        public float WaypointReachedThreshold { get; set; }
        public float AttackRotationSpeed { get; set; }

        // Random behavior settings
        public bool EnableRandomIdleDuringPatrol { get; set; }
        public float RandomIdleMinInterval { get; set; }
        public float RandomIdleMaxInterval { get; set; }
        public float RandomIdleDuration { get; set; }
        public bool EnableWaypointIdleStop { get; set; }
        public float WaypointIdleChance { get; set; }
        public float WaypointIdleDuration { get; set; }
        public bool EnableRandomDirectionChange { get; set; }
        public float DirectionChangeChance { get; set; }

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
                HearingRange = 8f,
                WalkNoiseLevel = 0.3f,
                RunNoiseLevel = 1.0f,
                MinNoiseThreshold = 0.2f,
                UseOcclusionForSound = true,
                SoundOcclusionMultiplier = 0.5f,
                WalkSpeed = 5f,
                RunSpeed = 9f,
                IdleDuration = 2f,
                AttackCooldown = 1.5f,
                WaypointReachedThreshold = 0.5f,
                AttackRotationSpeed = 10f,
                EnableRandomIdleDuringPatrol = true,
                RandomIdleMinInterval = 10f,
                RandomIdleMaxInterval = 20f,
                RandomIdleDuration = 3f,
                EnableWaypointIdleStop = true,
                WaypointIdleChance = 0.7f,
                WaypointIdleDuration = 2f,
                EnableRandomDirectionChange = false,
                DirectionChangeChance = 0.3f
            };
        }
    }

    /// <summary>
    /// Base class for all NPC states.
    /// Provides common functionality and shared data access for NPC behavior.
    /// Handles transition logic based on player proximity and audio detection.
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
        
        // Audio detection components
        protected PlayerAudioEmitter playerAudioEmitter;
        
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
                // Try to get the audio emitter component
                playerAudioEmitter = playerObj.GetComponent<PlayerAudioEmitter>();
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
            // TEMPORARY DEBUG: Re-find player every frame
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null)
            {
                player = playerObj.transform;
            }

            if (player == null || owner == null)
            {
                return float.MaxValue;
            }

            Debug.Log($"[{npcName}] Player position: {player.position}");
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
        
        /// <summary>
        /// Checks if the NPC can hear the player based on 3D spatial audio.
        /// Takes into account distance, noise level, and optional occlusion.
        /// </summary>
        /// <param name="heardPosition">Outputs the position where the sound was heard</param>
        /// <returns>True if player noise is audible to the NPC</returns>
        protected bool CanHearPlayer(out Vector3 heardPosition)
        {
            heardPosition = Vector3.zero;
            
            // Cannot hear if player or audio emitter is not found
            if (player == null || owner == null || playerAudioEmitter == null)
            {
                return false;
            }
            
            // Get current distance to player
            float distance = GetDistanceToPlayer();
            
            // Player is too far away to hear
            if (distance > config.HearingRange)
            {
                return false;
            }
            
            // Get the current noise level from the player
            float currentNoiseLevel = playerAudioEmitter.CurrentNoiseLevel;
            
            // Player is not making enough noise
            if (currentNoiseLevel < config.MinNoiseThreshold)
            {
                return false;
            }
            
            // Calculate sound attenuation based on distance
            // Sound decreases with distance (inverse square law approximation)
            float distanceAttenuation = 1f - (distance / config.HearingRange);
            float effectiveNoiseLevel = currentNoiseLevel * distanceAttenuation;
            
            // Check for sound occlusion (walls, obstacles blocking sound)
            if (config.UseOcclusionForSound)
            {
                Vector3 npcEarPosition = owner.transform.position + Vector3.up * config.NpcEyeHeight;
                Vector3 playerSoundPosition = player.position + Vector3.up * config.PlayerCenterHeight;
                Vector3 directionToPlayer = playerSoundPosition - npcEarPosition;
                float distanceToPlayer = directionToPlayer.magnitude;
                
                // Check if there are obstacles between NPC and player
                RaycastHit hit;
                if (Physics.Raycast(npcEarPosition, directionToPlayer.normalized, out hit, distanceToPlayer, config.ObstacleLayerMask))
                {
                    // Sound is muffled by obstacles
                    effectiveNoiseLevel *= config.SoundOcclusionMultiplier;
                }
            }
            
            // Check if the final noise level is above threshold
            if (effectiveNoiseLevel >= config.MinNoiseThreshold)
            {
                heardPosition = player.position;
                return true;
            }
            
            return false;
        }
        
        /// <summary>
        /// Simple overload for checking if player can be heard without getting the position.
        /// </summary>
        /// <returns>True if player noise is audible to the NPC</returns>
        protected bool CanHearPlayer()
        {
            Vector3 heardPosition;
            return CanHearPlayer(out heardPosition);
        }
        
        // Abstract methods that child classes must implement
        public abstract void OnEnter();
        public abstract void OnUpdate();
        public abstract void OnExit();
    }
}
