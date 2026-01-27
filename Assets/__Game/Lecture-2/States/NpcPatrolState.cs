using UnityEngine;
using UnityEngine.AI;

namespace Semester2
{
    /// <summary>
    /// NPC Patrol State - The NPC is moving along predefined waypoints.
    /// Typical behavior: Move between patrol points with walking animation.
    /// Transitions: Patrol -> Chase (player detected) or Patrol -> Attack (player in range)
    /// </summary>
    public class NpcPatrolState : NpcStateBase
    {
        // Waypoint system
        private Transform[] waypoints;
        private int currentWaypointIndex = 0;

        /// <summary>
        /// Constructor that stores reference to the owner GameObject and caches components.
        /// </summary>
        /// <param name="ownerGameObject">The GameObject that owns this state (the NPC)</param>
        /// <param name="config">Configuration parameters for NPC behavior</param>
        public NpcPatrolState(GameObject ownerGameObject, NpcConfig config) 
            : base(ownerGameObject, config)
        {
        }
        
        /// <summary>
        /// Sets the waypoints for this patrol state.
        /// </summary>
        /// <param name="patrolWaypoints">Array of Transform waypoints to patrol between</param>
        public void SetWaypoints(Transform[] patrolWaypoints)
        {
            waypoints = patrolWaypoints;
            currentWaypointIndex = 0;
        }

        public override void OnEnter()
        {
            Debug.Log($"[{npcName}] NPC started Patrolling");
            
            // Resume NavMeshAgent and set walk speed
            if (navMeshAgent != null && navMeshAgent.isActiveAndEnabled && navMeshAgent.isOnNavMesh)
            {
                navMeshAgent.isStopped = false;
                navMeshAgent.speed = config.WalkSpeed;
            }
            
            // Play walk animation - set Speed parameter for walking
            if (animator != null)
            {
                animator.SetFloat("Speed", config.WalkSpeed);
            }
            
            // Move to first waypoint
            MoveToNextWaypoint();
        }

        public override void OnUpdate()
        {
            // Check for player proximity first (higher priority than patrol)
            if (IsPlayerInAttackRange())
            {
                fsm?.ChangeState<NpcAttackState>();
                return;
            }
            
            if (IsPlayerInDetectionRange())
            {
                fsm?.ChangeState<NpcChaseState>();
                return;
            }
            
            // Continue patrol behavior: move between waypoints
            if (navMeshAgent != null && navMeshAgent.isActiveAndEnabled && navMeshAgent.isOnNavMesh 
                && waypoints != null && waypoints.Length > 0)
            {
                // Check if we've reached the current waypoint
                if (HasReachedWaypoint())
                {
                    // Move to the next waypoint
                    MoveToNextWaypoint();
                }
            }
        }

        public override void OnExit()
        {
            Debug.Log($"[{npcName}] NPC stopped Patrolling");
            
            // Stop movement when leaving patrol state
            if (navMeshAgent != null && navMeshAgent.isActiveAndEnabled && navMeshAgent.isOnNavMesh)
            {
                navMeshAgent.isStopped = true;
            }
        }
        
        /// <summary>
        /// Checks if the NPC has reached the current waypoint.
        /// </summary>
        /// <returns>True if the NPC is close enough to the waypoint</returns>
        private bool HasReachedWaypoint()
        {
            if (!navMeshAgent.pathPending)
            {
                // Check if we're close enough to the waypoint
                if (navMeshAgent.remainingDistance <= config.WaypointReachedThreshold)
                {
                    // Also check if the agent has stopped or is moving very slowly
                    if (!navMeshAgent.hasPath || navMeshAgent.velocity.sqrMagnitude == 0f)
                    {
                        return true;
                    }
                }
            }
            return false;
        }
        
        /// <summary>
        /// Moves the NPC to the next waypoint in the patrol route.
        /// </summary>
        private void MoveToNextWaypoint()
        {
            // If no waypoints are set, log a warning and return
            if (waypoints == null || waypoints.Length == 0)
            {
                Debug.LogWarning($"[{npcName}] No waypoints set for patrol!");
                return;
            }
            
            // Set destination to current waypoint
            Transform targetWaypoint = waypoints[currentWaypointIndex];
            
            if (targetWaypoint != null && navMeshAgent != null && navMeshAgent.isActiveAndEnabled && navMeshAgent.isOnNavMesh)
            {
                navMeshAgent.SetDestination(targetWaypoint.position);
                Debug.Log($"[{npcName}] Moving to waypoint {currentWaypointIndex + 1}/{waypoints.Length}");
            }
            
            // Move to next waypoint index (loop back to start if at the end)
            currentWaypointIndex = (currentWaypointIndex + 1) % waypoints.Length;
        }
    }
}
