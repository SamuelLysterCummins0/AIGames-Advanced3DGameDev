using UnityEngine;
using UnityEngine.AI;

namespace Semester2
{
    /// <summary>
    /// NPC Search State - NPC lost sight of the player and investigates the area.
    /// 
    /// Behavior:
    /// 1. Moves to the player's last known position
    /// 2. Generates random search points around that area
    /// 3. Walks between search points, pausing briefly at each to look around
    /// 4. If player is spotted/heard during search, transitions to Chase/Attack
    /// 5. After checking all points (or time runs out), gives up and returns to Patrol
    /// 
    /// Transitions:
    /// - Search -> Chase: player spotted or heard during search
    /// - Search -> Attack: player very close during search
    /// - Search -> Patrol: search complete without finding player
    /// </summary>
    public class NpcSearchState : NpcStateBase
    {
        // Hysteresis
        private float stateEnterTime = 0f;
        private const float MIN_STATE_TIME = 0.5f;

        // Search area settings
        private Vector3 lastKnownPosition;
        private float searchRadius = 8f;         // How far from last known position to generate points
        private int searchPointCount = 4;         // Number of random points to check
        private Vector3[] searchPoints;
        private int currentSearchIndex = 0;

        // Search timing
        private float searchTimer = 0f;
        private float maxSearchDuration = 15f;    // Max total search time before giving up
        private float pauseDuration = 1.5f;       // How long to pause and look at each point

        // State tracking
        private bool movingToPoint = true;        // True = walking to point, False = pausing at point
        private float pauseTimer = 0f;

        // Look around while paused
        private float lookAroundTimer = 0f;
        private float lookAroundInterval = 0.8f;
        private Quaternion targetLookRotation;

        public NpcSearchState(GameObject ownerGameObject, NpcConfig config)
            : base(ownerGameObject, config)
        {
        }

        // Public accessors for debug gizmo drawing
        public Vector3[] SearchPoints => searchPoints;
        public int CurrentSearchIndex => currentSearchIndex;
        public Vector3 LastKnownPosition => lastKnownPosition;
        public bool IsSearching => searchPoints != null && searchPoints.Length > 0;

        public override void OnEnter()
        {
            Debug.Log($"[{npcName}] <color=cyan>SEARCH STATE ENTERED</color> - Investigating last known area");

            stateEnterTime = Time.time;
            searchTimer = 0f;
            currentSearchIndex = 0;
            movingToPoint = true;
            pauseTimer = 0f;
            lookAroundTimer = 0f;

            // Search around where the NPC is now (it just ran to the area after losing the player)
            lastKnownPosition = owner.transform.position;

            // Generate random search points around the last known position
            GenerateSearchPoints();

            // Start moving to the first search point (which is the last known position itself)
            MoveToCurrentSearchPoint();
        }

        public override void OnUpdate()
        {
            float timeSinceEnter = Time.time - stateEnterTime;
            searchTimer += Time.deltaTime;

            // Only check transitions after minimum state time
            if (timeSinceEnter > MIN_STATE_TIME)
            {
                // Check if player spotted again - interrupt search
                if (IsPlayerInAttackRange())
                {
                    Debug.Log($"[{npcName}] Player found during search - attacking!");
                    fsm?.ChangeState<NpcAttackState>();
                    return;
                }

                if (IsPlayerInDetectionRange())
                {
                    Debug.Log($"[{npcName}] Player spotted during search - chasing!");
                    fsm?.ChangeState<NpcChaseState>();
                    return;
                }

                // Check audio detection
                Vector3 heardPosition;
                if (CanHearPlayer(out heardPosition))
                {
                    Debug.Log($"[{npcName}] Heard player during search - chasing!");
                    fsm?.ChangeState<NpcChaseState>();
                    return;
                }
            }

            // Safety timeout - give up if searching too long
            if (searchTimer >= maxSearchDuration)
            {
                Debug.Log($"[{npcName}] Search timed out after {maxSearchDuration}s - returning to patrol");
                fsm?.ChangeState<NpcPatrolState>();
                return;
            }

            // Main search behavior
            if (movingToPoint)
            {
                // Check if we've reached the current search point
                if (HasReachedDestination())
                {
                    // Arrived at search point - stop and look around
                    movingToPoint = false;
                    pauseTimer = 0f;
                    lookAroundTimer = 0f;

                    if (navMeshAgent != null && navMeshAgent.isActiveAndEnabled && navMeshAgent.isOnNavMesh)
                    {
                        navMeshAgent.isStopped = true;
                    }

                    if (animator != null)
                    {
                        animator.SetFloat("Speed", 0f);
                    }

                    PickRandomLookDirection();
                    Debug.Log($"[{npcName}] Reached search point {currentSearchIndex + 1}/{searchPoints.Length} - looking around");
                }
            }
            else
            {
                // Pausing at search point - look around
                pauseTimer += Time.deltaTime;
                LookAround();

                // Done pausing - move to next point or finish search
                if (pauseTimer >= pauseDuration)
                {
                    currentSearchIndex++;

                    // Check if we've visited all search points
                    if (currentSearchIndex >= searchPoints.Length)
                    {
                        Debug.Log($"[{npcName}] Checked all search points - player not found, returning to patrol");
                        fsm?.ChangeState<NpcPatrolState>();
                        return;
                    }

                    // Move to the next search point
                    movingToPoint = true;
                    MoveToCurrentSearchPoint();
                }
            }
        }

        public override void OnExit()
        {
            Debug.Log($"[{npcName}] <color=cyan>SEARCH STATE EXITED</color>");

            if (navMeshAgent != null && navMeshAgent.isActiveAndEnabled && navMeshAgent.isOnNavMesh)
            {
                navMeshAgent.isStopped = true;
            }
        }

        /// <summary>
        /// Generates search points spread in different directions around the last known position.
        /// First point is the last known position, remaining points are evenly spaced in angle
        /// and get progressively further out so the NPC covers more ground.
        /// Uses NavMesh.SamplePosition to make sure points are on walkable ground.
        /// </summary>
        private void GenerateSearchPoints()
        {
            searchPoints = new Vector3[searchPointCount];

            // First point is always the last known player position
            searchPoints[0] = lastKnownPosition;

            // Spread remaining points evenly around the circle in different directions
            // Each point gets further from the center so the NPC fans outward
            int remainingPoints = searchPointCount - 1;
            float angleStep = 360f / remainingPoints;
            // Random starting angle so the pattern isn't always the same
            float startAngle = Random.Range(0f, 360f);

            for (int i = 1; i < searchPointCount; i++)
            {
                // Each point is at a different angle, evenly spaced
                float angle = startAngle + (angleStep * (i - 1));
                float angleRad = angle * Mathf.Deg2Rad;

                // Distance increases for each point (min half radius, max full radius)
                float distance = Mathf.Lerp(searchRadius * 0.5f, searchRadius, (float)i / remainingPoints);

                // Add some randomness to the distance so it doesn't look robotic
                distance += Random.Range(-1f, 1f);

                Vector3 direction = new Vector3(Mathf.Cos(angleRad), 0f, Mathf.Sin(angleRad));
                Vector3 targetPosition = lastKnownPosition + direction * distance;

                // Make sure the point is on the NavMesh
                NavMeshHit hit;
                if (NavMesh.SamplePosition(targetPosition, out hit, 3f, NavMesh.AllAreas))
                {
                    searchPoints[i] = hit.position;
                }
                else
                {
                    // Fallback - try a closer point in the same direction
                    targetPosition = lastKnownPosition + direction * (distance * 0.5f);
                    if (NavMesh.SamplePosition(targetPosition, out hit, 3f, NavMesh.AllAreas))
                    {
                        searchPoints[i] = hit.position;
                    }
                    else
                    {
                        searchPoints[i] = lastKnownPosition;
                    }
                }
            }

            Debug.Log($"[{npcName}] Generated {searchPointCount} search points spread around last known position");
        }

        /// <summary>
        /// Starts the NavMeshAgent moving toward the current search point.
        /// </summary>
        private void MoveToCurrentSearchPoint()
        {
            if (navMeshAgent != null && navMeshAgent.isActiveAndEnabled && navMeshAgent.isOnNavMesh)
            {
                navMeshAgent.isStopped = false;
                navMeshAgent.speed = config.WalkSpeed;
                navMeshAgent.SetDestination(searchPoints[currentSearchIndex]);
            }

            if (animator != null)
            {
                animator.SetFloat("Speed", config.WalkSpeed);
            }
        }

        /// <summary>
        /// Checks if the NavMeshAgent has reached its current destination.
        /// </summary>
        private bool HasReachedDestination()
        {
            if (navMeshAgent == null || !navMeshAgent.isActiveAndEnabled || !navMeshAgent.isOnNavMesh)
                return false;

            if (!navMeshAgent.pathPending && navMeshAgent.remainingDistance <= config.WaypointReachedThreshold)
            {
                if (!navMeshAgent.hasPath || navMeshAgent.velocity.sqrMagnitude == 0f)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Rotates the NPC to look in random directions while paused at a search point.
        /// </summary>
        private void LookAround()
        {
            lookAroundTimer += Time.deltaTime;

            if (lookAroundTimer >= lookAroundInterval)
            {
                lookAroundTimer = 0f;
                PickRandomLookDirection();
            }

            // Smoothly rotate toward the target look direction
            if (owner != null)
            {
                owner.transform.rotation = Quaternion.Slerp(
                    owner.transform.rotation,
                    targetLookRotation,
                    Time.deltaTime * 3f
                );
            }
        }

        /// <summary>
        /// Picks a random horizontal direction for the NPC to look toward.
        /// </summary>
        private void PickRandomLookDirection()
        {
            float randomAngle = Random.Range(0f, 360f);
            targetLookRotation = Quaternion.Euler(0f, randomAngle, 0f);
        }
    }
}