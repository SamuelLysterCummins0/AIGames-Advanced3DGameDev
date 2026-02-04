using UnityEngine;
using UnityEngine.AI;

namespace Semester2
{

    public class NpcPatrolState : NpcStateBase
    {
        // Hysteresis to prevent state flickering
        private float stateEnterTime = 0f;
        private const float MIN_STATE_TIME = 0.5f; // Minimum time before allowing state transitions

        // Waypoint system
        private Transform[] waypoints;
        private int currentWaypointIndex = 0;
        private int waypointDirection = 1;

        // Random idle timing
        private float nextRandomIdleTime = 0f;
        private bool shouldCheckRandomIdle = false;

        public NpcPatrolState(GameObject ownerGameObject, NpcConfig config)
            : base(ownerGameObject, config)
        {
        }

        public void SetWaypoints(Transform[] patrolWaypoints)
        {
            waypoints = patrolWaypoints;
            currentWaypointIndex = 0;
            waypointDirection = 1;
        }

        public override void OnEnter()
        {
            Debug.Log($"[{npcName}] <color=green>PATROL STATE ENTERED</color>");

            stateEnterTime = Time.time; // Track when we entered this state

            // Resume NavMeshAgent and set walk speed
            if (navMeshAgent != null && navMeshAgent.isActiveAndEnabled && navMeshAgent.isOnNavMesh)
            {
                navMeshAgent.isStopped = false;
                navMeshAgent.speed = config.WalkSpeed;
            }

            // Play walk animation
            if (animator != null)
            {
                animator.SetFloat("Speed", config.WalkSpeed);
            }

            // Schedule next random idle if enabled
            if (config.EnableRandomIdleDuringPatrol)
            {
                float randomInterval = Random.Range(config.RandomIdleMinInterval, config.RandomIdleMaxInterval);
                nextRandomIdleTime = Time.time + randomInterval;
                shouldCheckRandomIdle = true;
            }

           
            // This prevents jumping to random waypoints when returning from Chase/Attack
            if (navMeshAgent != null && navMeshAgent.isActiveAndEnabled && navMeshAgent.isOnNavMesh)
            {
                if (!navMeshAgent.hasPath || navMeshAgent.remainingDistance < config.WaypointReachedThreshold)
                {
                    Debug.Log($"[{npcName}] No path or reached destination, moving to next waypoint");
                    MoveToNextWaypoint();
                }
                else
                {
                    Debug.Log($"[{npcName}] Resuming patrol to waypoint {currentWaypointIndex + 1}/{waypoints.Length} (distance: {navMeshAgent.remainingDistance:F1})");
                }
            }
        }

        public override void OnUpdate()
        {
            float timeSinceEnter = Time.time - stateEnterTime;

            // Only check transitions after minimum state time to prevent flickering
            if (timeSinceEnter < MIN_STATE_TIME)
                return;

            float distanceToPlayer = GetDistanceToPlayer();

            
            // Check visual detection
            if (IsPlayerInDetectionRange())
            {
                Debug.Log($"[{npcName}] Player spotted visually during patrol!");
                fsm?.ChangeState<NpcChaseState>();
                return;
            }

            // Check audio detection
            Vector3 heardPosition;
            if (CanHearPlayer(out heardPosition))
            {
                Debug.Log($"[{npcName}] Heard player noise during patrol at {heardPosition}!");
                fsm?.ChangeState<NpcChaseState>();
                return;
            }

            // Check for random idle (between waypoints)
            if (shouldCheckRandomIdle && Time.time >= nextRandomIdleTime)
            {
                Debug.Log($"[{npcName}] Random idle triggered during patrol");
                fsm?.ChangeState<NpcIdleState>();
                return;
            }

            // Continue patrol behavior: move between waypoints
            if (navMeshAgent != null && navMeshAgent.isActiveAndEnabled && navMeshAgent.isOnNavMesh
                && waypoints != null && waypoints.Length > 0)
            {
                // Check if we've reached the current waypoint
                if (HasReachedWaypoint())
                {
                    // Check for waypoint idle stop
                    if (config.EnableWaypointIdleStop)
                    {
                        float roll = Random.value;
                        if (roll < config.WaypointIdleChance)
                        {
                            Debug.Log($"[{npcName}] Stopping at waypoint (rolled {roll:F2}, needed < {config.WaypointIdleChance})");
                            float originalIdleDuration = config.IdleDuration;
                            config.IdleDuration = config.WaypointIdleDuration;
                            fsm?.ChangeState<NpcIdleState>();
                            config.IdleDuration = originalIdleDuration;
                            return;
                        }
                    }

                    MoveToNextWaypoint();
                }
            }
        }

        public override void OnExit()
        {
            Debug.Log($"[{npcName}] <color=green>PATROL STATE EXITED (waypoint {currentWaypointIndex + 1}/{waypoints.Length})</color>");

            
            if (navMeshAgent != null && navMeshAgent.isActiveAndEnabled && navMeshAgent.isOnNavMesh)
            {
                navMeshAgent.isStopped = true;
            }

            // Reset random idle check
            shouldCheckRandomIdle = false;
        }

        private bool HasReachedWaypoint()
        {
            if (!navMeshAgent.pathPending)
            {
                if (navMeshAgent.remainingDistance <= config.WaypointReachedThreshold)
                {
                    if (!navMeshAgent.hasPath || navMeshAgent.velocity.sqrMagnitude == 0f)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private void MoveToNextWaypoint()
        {
            if (waypoints == null || waypoints.Length == 0)
            {
                Debug.LogWarning($"[{npcName}] No waypoints set for patrol!");
                return;
            }

            if (config.EnableRandomDirectionChange)
            {
                float roll = Random.value;
                if (roll < config.DirectionChangeChance)
                {
                    waypointDirection *= -1;
                    Debug.Log($"[{npcName}] Changed patrol direction");
                }
            }

            Transform targetWaypoint = waypoints[currentWaypointIndex];

            if (targetWaypoint != null && navMeshAgent != null && navMeshAgent.isActiveAndEnabled && navMeshAgent.isOnNavMesh)
            {
                navMeshAgent.SetDestination(targetWaypoint.position);
                Debug.Log($"[{npcName}] Moving to waypoint {currentWaypointIndex + 1}/{waypoints.Length}");
            }

            currentWaypointIndex += waypointDirection;

            if (currentWaypointIndex >= waypoints.Length)
            {
                currentWaypointIndex = config.EnableRandomDirectionChange ? waypoints.Length - 2 : 0;
                if (config.EnableRandomDirectionChange) waypointDirection = -1;
            }
            else if (currentWaypointIndex < 0)
            {
                currentWaypointIndex = config.EnableRandomDirectionChange ? 1 : waypoints.Length - 1;
                if (config.EnableRandomDirectionChange) waypointDirection = 1;
            }
        }
    }
}