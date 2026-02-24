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

            stateEnterTime = Time.time;

            if (navMeshAgent != null && navMeshAgent.isActiveAndEnabled && navMeshAgent.isOnNavMesh)
            {
                navMeshAgent.isStopped = false;
                navMeshAgent.speed = config.WalkSpeed;
            }

            if (animator != null)
                animator.SetFloat("Speed", config.WalkSpeed);

            if (config.EnableRandomIdleDuringPatrol)
            {
                float randomInterval = Random.Range(config.RandomIdleMinInterval, config.RandomIdleMaxInterval);
                nextRandomIdleTime = Time.time + randomInterval;
                shouldCheckRandomIdle = true;
            }

            // Always snap to the closest waypoint when (re-)entering patrol.
            // This means an NPC that just lost the player on the far side of the map
            // won't walk all the way back to where it was before the chase started.
            if (waypoints != null && waypoints.Length > 0)
            {
                int closestIndex = FindClosestWaypointIndex();

                // If we're already standing on the closest waypoint (e.g. re-entering
                // patrol after an idle stop that fired AT a waypoint), advance one step
                // forward. Without this, MoveToNextWaypoint() sends the NPC to the
                // waypoint it is already at, HasReachedWaypoint() fires immediately,
                // and if the idle-stop chance rolls true again we get an infinite loop.
                if (waypoints[closestIndex] != null)
                {
                    float distToClosest = Vector3.Distance(
                        owner.transform.position,
                        waypoints[closestIndex].position);

                    if (distToClosest <= config.WaypointReachedThreshold + 0.5f)
                        closestIndex = (closestIndex + 1) % waypoints.Length;
                }

                currentWaypointIndex = closestIndex;
                waypointDirection = 1;
                Debug.Log($"[{npcName}] Resuming patrol — heading to waypoint {currentWaypointIndex + 1}/{waypoints.Length}");
            }

            if (navMeshAgent != null && navMeshAgent.isActiveAndEnabled && navMeshAgent.isOnNavMesh)
                MoveToNextWaypoint();
        }

        public override void OnUpdate()
        {
            float timeSinceEnter = Time.time - stateEnterTime;

            // Only check transitions after minimum state time to prevent flickering
            if (timeSinceEnter < MIN_STATE_TIME)
                return;

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
                            // Transition to idle state
                            // Note: Waypoint idle uses normal idle duration - adjust config.IdleDuration if you want different timing
                            fsm?.ChangeState<NpcIdleState>();
                            return;
                        }
                    }

                    MoveToNextWaypoint();
                }
            }
        }

        public override void OnExit()
        {
            Debug.Log($"[{npcName}] <color=green>PATROL STATE EXITED (waypoint {currentWaypointIndex + 1}/{waypoints?.Length ?? 0})</color>");


            if (navMeshAgent != null && navMeshAgent.isActiveAndEnabled && navMeshAgent.isOnNavMesh)
            {
                navMeshAgent.isStopped = true;
            }

            // Reset random idle check
            shouldCheckRandomIdle = false;
        }

        private bool HasReachedWaypoint()
        {
            if (navMeshAgent.pathPending) return false;

            if (navMeshAgent.remainingDistance <= config.WaypointReachedThreshold)
            {
                if (!navMeshAgent.hasPath || navMeshAgent.velocity.sqrMagnitude == 0f)
                    return true;
            }

            // If the path is partial (destination blocked by carved obstacle),
            // treat it as reached so the NPC doesn't freeze waiting for an unreachable point.
            if (navMeshAgent.pathStatus == NavMeshPathStatus.PathPartial &&
                navMeshAgent.velocity.sqrMagnitude == 0f)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Returns the index of the waypoint closest to the NPC's current world position.
        /// Used when re-entering patrol after a chase so the NPC doesn't travel back
        /// across the map to resume from where it was before the chase started.
        /// </summary>
        private int FindClosestWaypointIndex()
        {
            int   closestIndex    = 0;
            float closestDistance = float.MaxValue;

            for (int i = 0; i < waypoints.Length; i++)
            {
                if (waypoints[i] == null) continue;

                float dist = Vector3.Distance(owner.transform.position, waypoints[i].position);
                if (dist < closestDistance)
                {
                    closestDistance = dist;
                    closestIndex    = i;
                }
            }

            return closestIndex;
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