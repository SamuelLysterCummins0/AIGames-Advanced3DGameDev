using UnityEngine;
using UnityEngine.AI;

namespace Semester2
{
    /// <summary>
    /// Searches around the last known player position.
    /// Generates points in different directions and walks between them.
    /// Returns Failure when the search times out or all points are checked,
    /// and clears HasLastKnownPosition so the BT falls back to Patrol.
    ///
    /// The BtCooldown decorator wrapping this branch prevents the NPC
    /// from immediately re-entering Search after a failed search.
    /// </summary>
    public class BtActionSearch : BtNode
    {
        private readonly NpcBtContext _ctx;

        private const float SEARCH_RADIUS       = 8f;
        private const int   SEARCH_POINT_COUNT  = 4;
        private const float MAX_SEARCH_DURATION = 15f;
        private const float PAUSE_DURATION      = 1.5f;
        private const float LOOK_INTERVAL       = 0.8f;

        private Vector3[] _searchPoints;
        private int       _currentIndex  = 0;
        private bool      _movingToPoint = true;
        private float     _searchTimer   = 0f;
        private float     _pauseTimer    = 0f;
        private float     _lookTimer     = 0f;
        private Quaternion _targetLook;

        public BtActionSearch(NpcBtContext ctx)
        {
            _ctx = ctx;
        }

        protected override void OnEnter()
        {
            Debug.Log($"[{_ctx.NpcName}] <color=cyan>BT: Search entered</color>");
            _ctx.Blackboard.ActiveNodeName = "Search";

            _searchTimer   = 0f;
            _currentIndex  = 0;
            _movingToPoint = true;
            _pauseTimer    = 0f;
            _lookTimer     = 0f;

            GenerateSearchPoints(_ctx.Blackboard.LastKnownPlayerPosition);
            MoveToCurrentPoint();
        }

        protected override NodeState OnTick()
        {
            _ctx.Blackboard.ActiveNodeName = "Search";
            _searchTimer += Time.deltaTime;

            // Timeout — give up and return to patrol
            if (_searchTimer >= MAX_SEARCH_DURATION)
            {
                Debug.Log($"[{_ctx.NpcName}] Search timed out.");
                _ctx.Blackboard.HasLastKnownPosition = false;
                return NodeState.Failure;
            }

            if (_movingToPoint)
            {
                if (HasReachedDestination())
                {
                    _movingToPoint = false;
                    _pauseTimer    = 0f;
                    _lookTimer     = 0f;

                    if (_ctx.Agent != null) _ctx.Agent.isStopped = true;
                    if (_ctx.Anim  != null) _ctx.Anim.SetFloat("Speed", 0f);

                    PickRandomLookDirection();
                    Debug.Log($"[{_ctx.NpcName}] Reached search point {_currentIndex + 1}/{_searchPoints.Length}");
                }
            }
            else
            {
                // Pausing at the point — look around while we wait
                _pauseTimer += Time.deltaTime;
                LookAround();

                if (_pauseTimer >= PAUSE_DURATION)
                {
                    _currentIndex++;
                    if (_currentIndex >= _searchPoints.Length)
                    {
                        Debug.Log($"[{_ctx.NpcName}] All search points checked - player not found.");
                        _ctx.Blackboard.HasLastKnownPosition = false;
                        return NodeState.Failure;
                    }
                    _movingToPoint = true;
                    MoveToCurrentPoint();
                }
            }

            return NodeState.Running;
        }

        protected override void OnExit(NodeState result)
        {
            if (_ctx.Agent != null && _ctx.Agent.isActiveAndEnabled && _ctx.Agent.isOnNavMesh)
                _ctx.Agent.isStopped = true;
        }

        // Generate search points fanned out in the direction the player was last moving.
        // Point 0 is the last known position itself.
        // Points 1-3 fan forward: straight ahead, angled left, angled right.
        // Each point gets progressively further out so the NPC sweeps the area ahead.
        // Falls back to a random spread if no movement direction is known.
        private void GenerateSearchPoints(Vector3 center)
        {
            _searchPoints    = new Vector3[SEARCH_POINT_COUNT];
            _searchPoints[0] = center;

            Vector3 moveDir = _ctx.Blackboard.LastKnownPlayerMoveDir;
            bool hasDir = moveDir.sqrMagnitude > 0.01f;

            if (hasDir)
            {
                // Base angle in degrees from the player's last movement direction.
                // Small random jitter on each angle so the pattern isn't perfectly predictable.
                float baseAngleDeg = Mathf.Atan2(moveDir.x, moveDir.z) * Mathf.Rad2Deg;

                // Offsets: straight ahead, left of travel, right of travel
                float[] angleOffsets = { 0f, -50f, 50f };

                for (int i = 1; i < SEARCH_POINT_COUNT; i++)
                {
                    float jitter   = Random.Range(-20f, 20f);
                    float angleDeg = baseAngleDeg + angleOffsets[i - 1] + jitter;
                    float angleRad = angleDeg * Mathf.Deg2Rad;

                    float dist = Mathf.Lerp(SEARCH_RADIUS * 0.6f, SEARCH_RADIUS, (float)i / (SEARCH_POINT_COUNT - 1));
                    dist += Random.Range(-1f, 1f);

                    Vector3 dir = new Vector3(Mathf.Sin(angleRad), 0f, Mathf.Cos(angleRad));
                    Vector3 pos = center + dir * dist;

                    NavMeshHit hit;
                    _searchPoints[i] = NavMesh.SamplePosition(pos, out hit, 3f, NavMesh.AllAreas)
                        ? hit.position
                        : center;
                }
            }
            else
            {
                // Fallback: random spread when no movement direction is available
                float angleStep  = 360f / (SEARCH_POINT_COUNT - 1);
                float startAngle = Random.Range(0f, 360f);

                for (int i = 1; i < SEARCH_POINT_COUNT; i++)
                {
                    float angle = (startAngle + angleStep * (i - 1)) * Mathf.Deg2Rad;
                    float dist  = Mathf.Lerp(SEARCH_RADIUS * 0.5f, SEARCH_RADIUS, (float)i / (SEARCH_POINT_COUNT - 1));
                    dist += Random.Range(-1f, 1f);

                    Vector3 pos = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * dist;

                    NavMeshHit hit;
                    _searchPoints[i] = NavMesh.SamplePosition(pos, out hit, 3f, NavMesh.AllAreas)
                        ? hit.position
                        : center;
                }
            }
        }

        private void MoveToCurrentPoint()
        {
            if (_ctx.Agent == null || !_ctx.Agent.isActiveAndEnabled || !_ctx.Agent.isOnNavMesh) return;
            _ctx.Agent.isStopped = false;
            _ctx.Agent.speed     = _ctx.Config.RunSpeed;
            _ctx.Agent.SetDestination(_searchPoints[_currentIndex]);
            if (_ctx.Anim != null) _ctx.Anim.SetFloat("Speed", _ctx.Config.RunSpeed);
        }

        private bool HasReachedDestination()
        {
            if (_ctx.Agent == null || !_ctx.Agent.isActiveAndEnabled || !_ctx.Agent.isOnNavMesh) return false;
            if (!_ctx.Agent.pathPending && _ctx.Agent.remainingDistance <= _ctx.Config.WaypointReachedThreshold)
                if (!_ctx.Agent.hasPath || _ctx.Agent.velocity.sqrMagnitude == 0f)
                    return true;
            return false;
        }

        private void LookAround()
        {
            _lookTimer += Time.deltaTime;
            if (_lookTimer >= LOOK_INTERVAL)
            {
                _lookTimer = 0f;
                PickRandomLookDirection();
            }
            _ctx.Owner.transform.rotation = Quaternion.Slerp(
                _ctx.Owner.transform.rotation, _targetLook, Time.deltaTime * 3f);
        }

        private void PickRandomLookDirection()
        {
            _targetLook = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        }
    }
}
