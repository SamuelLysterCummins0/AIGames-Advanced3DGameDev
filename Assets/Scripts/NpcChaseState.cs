using UnityEngine;
using UnityEngine.AI;

namespace Semester2
{
    /// <summary>
    /// NPC Chase State - FIXED version with hysteresis to prevent state flickering
    /// </summary>
    public class NpcChaseState : NpcStateBase
    {
        private float stateEnterTime = 0f;
        private const float MIN_STATE_TIME = 0.3f; // Minimum time before allowing state transitions

        private float lastDestinationUpdateTime = 0f;
        private const float DESTINATION_UPDATE_INTERVAL = 0.2f; // Update destination every 0.2 seconds

        public NpcChaseState(GameObject ownerGameObject, NpcConfig config)
            : base(ownerGameObject, config)
        {
        }

        public override void OnEnter()
        {
            Debug.Log($"[{npcName}] <color=yellow>CHASE STATE ENTERED</color>");

            stateEnterTime = Time.time;

            // Resume NavMeshAgent and set run speed
            if (navMeshAgent != null && navMeshAgent.isActiveAndEnabled && navMeshAgent.isOnNavMesh)
            {
                navMeshAgent.isStopped = false;
                navMeshAgent.speed = config.RunSpeed;
            }

            // Play run animation
            if (animator != null)
            {
                animator.SetFloat("Speed", config.RunSpeed);
            }
        }

        public override void OnUpdate()
        {
            float timeSinceEnter = Time.time - stateEnterTime;
            float distanceToPlayer = GetDistanceToPlayer();

            // Only check transitions after minimum state time
            if (timeSinceEnter > MIN_STATE_TIME)
            {
                // Use attack range - buffer to enter (prevents flickering)
                float enterAttackThreshold = config.AttackRange - 0.5f; // 0.5 unit buffer
                if (distanceToPlayer <= enterAttackThreshold)
                {
                    Debug.Log($"[{npcName}] Close enough to attack (dist: {distanceToPlayer:F1} <= {enterAttackThreshold:F1})");
                    fsm?.ChangeState<NpcAttackState>();
                    return;
                }

                // Check if player escaped
                // Use detection range + buffer to stay in chase longer
                // Once in chase, only use distance check (not FOV) so NPC continues chasing even if player moves behind
                float exitDetectionThreshold = config.DetectionRange + 1f; // 1 unit buffer (reduced from 2)

                // Exit only if player is beyond threshold - don't require FOV/hearing checks
                // This allows chase to continue even when player is behind the NPC
                if (distanceToPlayer > exitDetectionThreshold)
                {
                    Debug.Log($"[{npcName}] Lost track of player - too far away (dist: {distanceToPlayer:F1} > {exitDetectionThreshold:F1})");
                    fsm?.ChangeState<NpcPatrolState>();
                    return;
                }
            }

            // Continue chasing: follow player, update navigation
            // Only update destination periodically to prevent NavMesh path recalculation spam
            if (navMeshAgent != null && navMeshAgent.isActiveAndEnabled && navMeshAgent.isOnNavMesh && player != null)
            {
                if (Time.time - lastDestinationUpdateTime > DESTINATION_UPDATE_INTERVAL)
                {
                    // Only update if not already calculating a path
                    if (!navMeshAgent.pathPending)
                    {
                        navMeshAgent.SetDestination(player.position);
                        lastDestinationUpdateTime = Time.time;
                    }
                }
            }
        }

        public override void OnExit()
        {
            Debug.Log($"[{npcName}] <color=yellow>CHASE STATE EXITED</color>");

            // Stop movement when leaving chase state
            if (navMeshAgent != null && navMeshAgent.isActiveAndEnabled && navMeshAgent.isOnNavMesh)
            {
                navMeshAgent.isStopped = true;
            }
        }
    }
}