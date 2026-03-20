using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Semester2
{
    /// <summary>
    /// Moves between patrol waypoints indefinitely.
    /// Always returns Running — the root Selector handles interruption
    /// when a higher-priority branch (threat, investigate, search) takes over.
    ///
    /// Uses a static waypoint reservation dictionary so that multiple NPCs sharing
    /// the same waypoint set are never assigned the same destination at the same time.
    /// </summary>
    public class BtActionPatrol : BtNode
    {
        private readonly NpcBtContext _ctx;
        private readonly Transform[] _waypoints;

        private int   _currentIndex    = 0;
        private bool  _pausing         = false;
        private float _pauseStartTime  = 0f;

        // ── Waypoint reservation — shared across all BtActionPatrol instances ────────
        // Maps each waypoint Transform to the NPC GameObject that has claimed it.
        // Prevents multiple NPCs from being assigned the same patrol point simultaneously.
        private static readonly Dictionary<Transform, GameObject> _reservedWaypoints
            = new Dictionary<Transform, GameObject>();

        // The waypoint this NPC currently holds a reservation on.
        private Transform _currentWaypointClaim;

        public BtActionPatrol(NpcBtContext ctx, Transform[] waypoints)
        {
            _ctx       = ctx;
            _waypoints = waypoints;
        }

        protected override void OnEnter()
        {
            Debug.Log($"[{_ctx.NpcName}] <color=green>BT: Patrol entered</color>");
            _ctx.Blackboard.ActiveNodeName = "Patrol";

            _pausing        = false;
            _pauseStartTime = 0f;

            if (_ctx.Agent == null || !_ctx.Agent.isActiveAndEnabled || !_ctx.Agent.isOnNavMesh)
                return;

            _ctx.Agent.isStopped      = false;
            _ctx.Agent.speed          = _ctx.Config.WalkSpeed;
            _ctx.Agent.updateRotation = true;

            if (_waypoints != null && _waypoints.Length > 0)
            {
                // Release any stale claim from a previous Patrol session before picking a new point.
                ReleaseReservation();
                _currentIndex = FindClosestAvailableWaypointIndex();
                ClaimWaypoint(_waypoints[_currentIndex]);
                MoveToCurrentWaypoint();
            }

            if (_ctx.Anim != null)
                _ctx.Anim.SetFloat("Speed", _ctx.Config.WalkSpeed);
        }

        protected override NodeState OnTick()
        {
            _ctx.Blackboard.ActiveNodeName = "Patrol";

            if (_waypoints == null || _waypoints.Length == 0)
                return NodeState.Running;

            if (_ctx.Agent == null || !_ctx.Agent.isActiveAndEnabled || !_ctx.Agent.isOnNavMesh)
                return NodeState.Running;

            if (_pausing)
            {
                if (Time.time - _pauseStartTime >= _ctx.Config.WaypointIdleDuration)
                {
                    _pausing = false;
                    AdvanceWaypoint();
                    MoveToCurrentWaypoint();
                }
                return NodeState.Running;
            }

            // Keep animator in sync with actual agent speed each tick
            if (_ctx.Anim != null)
                _ctx.Anim.SetFloat("Speed", _ctx.Agent.velocity.magnitude);

            if (HasReachedWaypoint())
            {
                // Optionally pause at the waypoint
                if (_ctx.Config.EnableWaypointIdleStop && Random.value < _ctx.Config.WaypointIdleChance)
                {
                    _pausing        = true;
                    _pauseStartTime = Time.time;
                    _ctx.Agent.isStopped = true;
                    if (_ctx.Anim != null) _ctx.Anim.SetFloat("Speed", 0f);
                }
                else
                {
                    AdvanceWaypoint();
                    MoveToCurrentWaypoint();
                }
            }

            return NodeState.Running;
        }

        protected override void OnExit(NodeState result)
        {
            // Always release our waypoint claim so other NPCs can use this point.
            ReleaseReservation();

            if (_ctx.Agent != null && _ctx.Agent.isActiveAndEnabled && _ctx.Agent.isOnNavMesh)
                _ctx.Agent.isStopped = true;
        }

        private bool HasReachedWaypoint()
        {
            if (_ctx.Agent.pathPending) return false;
            if (_ctx.Agent.remainingDistance <= _ctx.Config.WaypointReachedThreshold)
                if (!_ctx.Agent.hasPath || _ctx.Agent.velocity.sqrMagnitude == 0f)
                    return true;
            // Treat partial paths as reached so the NPC doesn't freeze
            if (_ctx.Agent.pathStatus == NavMeshPathStatus.PathPartial && _ctx.Agent.velocity.sqrMagnitude == 0f)
                return true;
            return false;
        }

        private void AdvanceWaypoint()
        {
            // Release the current reservation before claiming the next point.
            ReleaseReservation();
            _currentIndex = (_currentIndex + 1) % _waypoints.Length;
            ClaimWaypoint(_waypoints[_currentIndex]);
        }

        private void MoveToCurrentWaypoint()
        {
            if (_waypoints[_currentIndex] == null) return;
            _ctx.Agent.isStopped = false;
            _ctx.Agent.speed     = _ctx.Config.WalkSpeed;
            _ctx.Agent.SetDestination(_waypoints[_currentIndex].position);
            if (_ctx.Anim != null) _ctx.Anim.SetFloat("Speed", _ctx.Agent.velocity.magnitude);
        }

        // ── Reservation helpers ──────────────────────────────────────────────────────

        private void ClaimWaypoint(Transform wp)
        {
            if (wp == null) return;
            _currentWaypointClaim = wp;
            _reservedWaypoints[wp] = _ctx.Owner;
        }

        private void ReleaseReservation()
        {
            if (_currentWaypointClaim == null) return;
            // Only remove if this NPC still owns the entry (guards against double-release).
            if (_reservedWaypoints.TryGetValue(_currentWaypointClaim, out GameObject owner)
                && owner == _ctx.Owner)
            {
                _reservedWaypoints.Remove(_currentWaypointClaim);
            }
            _currentWaypointClaim = null;
        }

        /// <summary>
        /// Returns true if the waypoint is unclaimed, claimed by this NPC, or claimed by a
        /// destroyed NPC (stale entry). Cleans up stale entries in place.
        /// </summary>
        private bool IsWaypointAvailable(Transform wp)
        {
            if (!_reservedWaypoints.TryGetValue(wp, out GameObject owner)) return true;  // unclaimed
            if (owner == null) { _reservedWaypoints.Remove(wp); return true; }            // stale (owner destroyed)
            return owner == _ctx.Owner;                                                    // claimed by self — still fine
        }

        /// <summary>
        /// Finds the closest waypoint that is not claimed by another NPC.
        /// Falls back to the simple closest if every waypoint is reserved
        /// (e.g. more NPCs than waypoints).
        /// </summary>
        private int FindClosestAvailableWaypointIndex()
        {
            int   closest     = -1;
            float closestDist = float.MaxValue;

            for (int i = 0; i < _waypoints.Length; i++)
            {
                if (_waypoints[i] == null) continue;
                if (!IsWaypointAvailable(_waypoints[i])) continue;  // skip — another NPC owns it

                float d = Vector3.Distance(_ctx.Owner.transform.position, _waypoints[i].position);
                if (d < closestDist) { closestDist = d; closest = i; }
            }

            // Fallback: all waypoints are claimed — just use the geometrically closest one.
            if (closest == -1)
            {
                Debug.Log($"[{_ctx.NpcName}] All waypoints reserved — using simple closest (fallback).");
                closest = FindClosestWaypointIndex();
            }

            return closest;
        }

        private int FindClosestWaypointIndex()
        {
            int   closest     = 0;
            float closestDist = float.MaxValue;
            for (int i = 0; i < _waypoints.Length; i++)
            {
                if (_waypoints[i] == null) continue;
                float d = Vector3.Distance(_ctx.Owner.transform.position, _waypoints[i].position);
                if (d < closestDist) { closestDist = d; closest = i; }
            }
            return closest;
        }
    }
}
