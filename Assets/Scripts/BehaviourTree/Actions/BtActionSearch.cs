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

        private const float LOOK_INTERVAL = 0.8f;

        private Vector3[] _searchPoints;
        private int       _currentIndex  = 0;
        private bool      _movingToPoint = true;
        private float     _searchTimer   = 0f;
        private float     _pauseTimer    = 0f;
        private float     _lookTimer     = 0f;
        private Quaternion _targetLook;

        // Sound-tracking state.
        // While the player is being heard we navigate directly toward the live heard
        // position (updated every 0.2 s) instead of committing to static search points.
        // When hearing stops, _wasHearingLastTick triggers a fan-search reset from the
        // final heard position.
        private bool  _wasHearingLastTick   = false;
        private float _lastSoundTrackUpdate = 0f;

        public BtActionSearch(NpcBtContext ctx)
        {
            _ctx = ctx;
        }

        protected override void OnEnter()
        {
            Debug.Log($"[{_ctx.NpcName}] <color=cyan>BT: Search entered</color>");
            _ctx.Blackboard.ActiveNodeName = "Search";

            // BtActionInvestigate.OnExit is never called when the Selector abandons
            // it mid-Fixing phase — so the fixing animation and the agent rotation
            // lock are never restored. We clean both up here since Search takes over.
            if (_ctx.Anim  != null) _ctx.Anim.SetBool("IsFixing", false);
            if (_ctx.Agent != null) _ctx.Agent.updateRotation = true; // re-enable so NPC turns to face movement direction

            _searchTimer          = 0f;
            _currentIndex         = 0;
            _movingToPoint        = true;
            _pauseTimer           = 0f;
            _lookTimer            = 0f;
            _wasHearingLastTick   = false;
            _lastSoundTrackUpdate = 0f;

            // trackingActive is true when sound is ongoing OR when reinforcement alerts
            // are still arriving from a chasing NPC. Both cases use live LKP navigation.
            bool trackingActive = _ctx.Blackboard.PlayerHeard || _ctx.Blackboard.ReinforcementTracking;
            if (trackingActive)
            {
                // Navigate toward the live last-known position (updated each alert / each heard tick)
                // instead of committing to static points that would be stale if the player moves.
                _wasHearingLastTick = true;
                NavigateToSound();
            }
            else
            {
                // No active tracking — fan out from the last known position
                GenerateSearchPoints(_ctx.Blackboard.LastKnownPlayerPosition);
                MoveToCurrentPoint();
            }
        }

        protected override NodeState OnTick()
        {
            _ctx.Blackboard.ActiveNodeName = "Search";
            _searchTimer += Time.deltaTime;

            // Timeout — give up and return to patrol
            if (_searchTimer >= _ctx.Config.MaxSearchDuration)
            {
                Debug.Log($"[{_ctx.NpcName}] Search timed out.");
                _ctx.Blackboard.HasLastKnownPosition = false;
                _ctx.Blackboard.LkpFromChase         = false;
                return NodeState.Failure;
            }

            // ── Live-tracking mode ─────────────────────────────────────────────────
            // Active while the player is heard OR while reinforcement alerts keep arriving
            // from a chasing NPC. Navigates toward the current LKP each tick so this NPC
            // follows the chase area rather than fanning around a stale first position.
            bool trackingActive = _ctx.Blackboard.PlayerHeard || _ctx.Blackboard.ReinforcementTracking;
            if (trackingActive)
            {
                _wasHearingLastTick = true;
                NavigateToSound();
                return NodeState.Running;
            }

            // Tracking just stopped (sound ended or reinforcement alerts expired) —
            // switch to fan search from the final received LKP.
            // This fires exactly once on the first frame tracking ends.
            if (_wasHearingLastTick)
            {
                Debug.Log($"[{_ctx.NpcName}] Sound lost — switching to fan search.");
                _wasHearingLastTick = false;
                _searchTimer   = 0f;   // full timeout from now so the fan gets time
                _currentIndex  = 0;
                _movingToPoint = true;
                _pauseTimer    = 0f;
                _lookTimer     = 0f;
                GenerateSearchPoints(_ctx.Blackboard.LastKnownPlayerPosition);
                MoveToCurrentPoint();
            }

            // ── Fan search ─────────────────────────────────────────────────────────
            if (_movingToPoint)
            {
                // Keep animator in sync with actual agent speed each tick
                if (_ctx.Anim != null)
                    _ctx.Anim.SetFloat("Speed", _ctx.Agent.velocity.magnitude);

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

                if (_pauseTimer >= _ctx.Config.SearchPauseDuration)
                {
                    _currentIndex++;
                    if (_currentIndex >= _searchPoints.Length)
                    {
                        Debug.Log($"[{_ctx.NpcName}] All search points checked - player not found.");
                        _ctx.Blackboard.HasLastKnownPosition = false;
                        _ctx.Blackboard.LkpFromChase         = false;
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
            _searchPoints    = new Vector3[_ctx.Config.SearchPointCount];
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

                for (int i = 1; i < _ctx.Config.SearchPointCount; i++)
                {
                    float jitter   = Random.Range(-20f, 20f);
                    float angleDeg = baseAngleDeg + angleOffsets[i - 1] + jitter;
                    float angleRad = angleDeg * Mathf.Deg2Rad;

                    float dist = Mathf.Lerp(_ctx.Config.SearchRadius * 0.6f, _ctx.Config.SearchRadius, (float)i / (_ctx.Config.SearchPointCount - 1));
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
                float angleStep  = 360f / (_ctx.Config.SearchPointCount - 1);
                float startAngle = Random.Range(0f, 360f);

                for (int i = 1; i < _ctx.Config.SearchPointCount; i++)
                {
                    float angle = (startAngle + angleStep * (i - 1)) * Mathf.Deg2Rad;
                    float dist  = Mathf.Lerp(_ctx.Config.SearchRadius * 0.5f, _ctx.Config.SearchRadius, (float)i / (_ctx.Config.SearchPointCount - 1));
                    dist += Random.Range(-1f, 1f);

                    Vector3 pos = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * dist;

                    NavMeshHit hit;
                    _searchPoints[i] = NavMesh.SamplePosition(pos, out hit, 3f, NavMesh.AllAreas)
                        ? hit.position
                        : center;
                }
            }
        }

        /// <summary>
        /// Navigates toward the latest heard position (updated at most every 0.2 s
        /// to avoid recalculating the NavMesh path every frame).
        /// </summary>
        private void NavigateToSound()
        {
            if (_ctx.Agent == null || !_ctx.Agent.isActiveAndEnabled || !_ctx.Agent.isOnNavMesh) return;

            _ctx.Agent.isStopped = false;
            _ctx.Agent.speed     = _ctx.Config.RunSpeed;
            if (_ctx.Anim != null) _ctx.Anim.SetFloat("Speed", _ctx.Agent.velocity.magnitude);

            // Rate-limit path recalculation
            if (Time.time - _lastSoundTrackUpdate < 0.2f) return;
            _lastSoundTrackUpdate = Time.time;

            _ctx.Agent.SetDestination(_ctx.Blackboard.LastKnownPlayerPosition);
        }

        private void MoveToCurrentPoint()
        {
            if (_ctx.Agent == null || !_ctx.Agent.isActiveAndEnabled || !_ctx.Agent.isOnNavMesh) return;
            _ctx.Agent.isStopped = false;
            _ctx.Agent.speed     = _ctx.Config.RunSpeed;
            _ctx.Agent.SetDestination(_searchPoints[_currentIndex]);
            if (_ctx.Anim != null) _ctx.Anim.SetFloat("Speed", _ctx.Agent.velocity.magnitude);
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
