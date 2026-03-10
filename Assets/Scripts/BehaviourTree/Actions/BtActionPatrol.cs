using UnityEngine;
using UnityEngine.AI;

namespace Semester2
{
    /// <summary>
    /// Moves between patrol waypoints indefinitely.
    /// Always returns Running — the root Selector handles interruption
    /// when a higher-priority branch (threat, investigate, search) takes over.
    /// </summary>
    public class BtActionPatrol : BtNode
    {
        private readonly NpcBtContext _ctx;
        private readonly Transform[] _waypoints;

        private int _currentIndex = 0;
        private bool _pausing     = false;
        private float _pauseTimer = 0f;

        public BtActionPatrol(NpcBtContext ctx, Transform[] waypoints)
        {
            _ctx       = ctx;
            _waypoints = waypoints;
        }

        protected override void OnEnter()
        {
            Debug.Log($"[{_ctx.NpcName}] <color=green>BT: Patrol entered</color>");
            _ctx.Blackboard.ActiveNodeName = "Patrol";

            _pausing   = false;
            _pauseTimer = 0f;

            if (_ctx.Agent == null || !_ctx.Agent.isActiveAndEnabled || !_ctx.Agent.isOnNavMesh)
                return;

            _ctx.Agent.isStopped      = false;
            _ctx.Agent.speed          = _ctx.Config.WalkSpeed;
            _ctx.Agent.updateRotation = true;

            // Snap to the closest waypoint so the NPC doesn't walk back across the map
            if (_waypoints != null && _waypoints.Length > 0)
            {
                _currentIndex = FindClosestWaypointIndex();
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
                _pauseTimer += Time.deltaTime;
                if (_pauseTimer >= _ctx.Config.WaypointIdleDuration)
                {
                    _pausing = false;
                    AdvanceWaypoint();
                    MoveToCurrentWaypoint();
                }
                return NodeState.Running;
            }

            if (HasReachedWaypoint())
            {
                // Optionally pause at the waypoint
                if (_ctx.Config.EnableWaypointIdleStop && Random.value < _ctx.Config.WaypointIdleChance)
                {
                    _pausing    = true;
                    _pauseTimer = 0f;
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
            _currentIndex = (_currentIndex + 1) % _waypoints.Length;
        }

        private void MoveToCurrentWaypoint()
        {
            if (_waypoints[_currentIndex] == null) return;
            _ctx.Agent.isStopped = false;
            _ctx.Agent.speed     = _ctx.Config.WalkSpeed;
            _ctx.Agent.SetDestination(_waypoints[_currentIndex].position);
            if (_ctx.Anim != null) _ctx.Anim.SetFloat("Speed", _ctx.Config.WalkSpeed);
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
