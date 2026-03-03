using UnityEngine;
using UnityEngine.AI;

namespace Semester2
{
    /// <summary>
    /// NPC Chase State - Chases the player, tracks last known position.
    /// When player escapes detection, NPC keeps running to last known position
    /// before transitioning to Search state.
    /// </summary>
    public class NpcChaseState : NpcStateBase
    {
        private float stateEnterTime = 0f;
        private const float MIN_STATE_TIME = 0.3f;

        private float lastDestinationUpdateTime = 0f;
        private const float DESTINATION_UPDATE_INTERVAL = 0.2f;

        // Track where we last saw the player
        private Vector3 lastKnownPlayerPosition;

        // Has the NPC lost sight of the player?
        private bool playerLost = false;
        private Vector3 chaseTargetPosition; // Where the NPC runs to after losing player

        /// <summary>
        /// Public access to last known player position for the Search state.
        /// </summary>
        public Vector3 LastKnownPlayerPosition => lastKnownPlayerPosition;

        public NpcChaseState(GameObject ownerGameObject, NpcConfig config)
            : base(ownerGameObject, config)
        {
        }

        public override void OnEnter()
        {
            Debug.Log($"[{npcName}] <color=yellow>CHASE STATE ENTERED</color>");

            stateEnterTime = Time.time;
            playerLost = false;

            // Store initial last known position
            if (player != null)
            {
                lastKnownPlayerPosition = player.position;
            }

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
                // Must also have clear line of sight - can't attack through walls
                float enterAttackThreshold = config.AttackRange - 0.5f;
                if (distanceToPlayer <= enterAttackThreshold && HasClearLineOfSight())
                {
                    Debug.Log($"[{npcName}] Close enough to attack (dist: {distanceToPlayer:F1} <= {enterAttackThreshold:F1})");
                    fsm?.ChangeState<NpcAttackState>();
                    return;
                }

                // Check if player escaped detection range
                float exitDetectionThreshold = config.DetectionRange + 1f;

                if (!playerLost && distanceToPlayer > exitDetectionThreshold)
                {
                    // Player just escaped - keep running in the direction we were already heading
                    // This is more natural than beelining to an exact position
                    playerLost = true;

                    float continueRunDistance = 8f;
                    Vector3 forwardDirection = owner.transform.forward;
                    chaseTargetPosition = owner.transform.position + forwardDirection * continueRunDistance;

                    // Make sure the target is on the NavMesh
                    NavMeshHit hit;
                    if (NavMesh.SamplePosition(chaseTargetPosition, out hit, 3f, NavMesh.AllAreas))
                    {
                        chaseTargetPosition = hit.position;
                    }
                    else
                    {
                        // If forward point isn't on NavMesh (e.g. wall ahead), just search from here
                        chaseTargetPosition = owner.transform.position;
                    }

                    Debug.Log($"[{npcName}] Lost sight of player - continuing forward to search area");

                    if (navMeshAgent != null && navMeshAgent.isActiveAndEnabled && navMeshAgent.isOnNavMesh)
                    {
                        navMeshAgent.SetDestination(chaseTargetPosition);
                    }
                }

                // If player lost, check if we've reached the chase target
                if (playerLost)
                {
                    // Only resume chase if we can actually SEE or HEAR the player again
                    // Not just distance - prevents chasing through walls
                    if (IsPlayerInDetectionRange())
                    {
                        playerLost = false;
                        Debug.Log($"[{npcName}] Player spotted again - resuming chase");
                    }
                    else
                    {
                        Vector3 heardPosition;
                        if (CanHearPlayer(out heardPosition))
                        {
                            playerLost = false;
                            Debug.Log($"[{npcName}] Heard player again - resuming chase");
                        }
                    }

                    // If still lost, check if we've arrived at the chase target
                    if (playerLost && navMeshAgent != null && !navMeshAgent.pathPending
                        && navMeshAgent.remainingDistance <= config.WaypointReachedThreshold)
                    {
                        Debug.Log($"[{npcName}] Reached chase target - starting search");
                        fsm?.ChangeState<NpcSearchState>();
                        return;
                    }

                    // Don't update destination while running to chase target
                    return;
                }
            }

            // Keep updating last known player position while we can see them
            if (player != null && !playerLost)
            {
                lastKnownPlayerPosition = player.position;
            }

            // Continue chasing: follow player, update navigation
            if (navMeshAgent != null && navMeshAgent.isActiveAndEnabled && navMeshAgent.isOnNavMesh && player != null)
            {
                if (Time.time - lastDestinationUpdateTime > DESTINATION_UPDATE_INTERVAL)
                {
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