using UnityEngine;

namespace Semester2
{
    /// <summary>
    /// Chases the player at run speed.
    /// The Blackboard's LastKnownPlayerPosition is kept up to date by NpcController.
    /// This action just navigates toward the live player position while the
    /// Threat branch is active. The BT Selector handles the Chase → Attack switch
    /// automatically by re-evaluating the attack range condition every tick.
    /// </summary>
    public class BtActionChase : BtNode
    {
        private readonly NpcBtContext _ctx;

        private float _lastDestinationUpdate = 0f;
        private const float DEST_UPDATE_INTERVAL = 0.2f;

        public BtActionChase(NpcBtContext ctx)
        {
            _ctx = ctx;
        }

        protected override void OnEnter()
        {
            Debug.Log($"[{_ctx.NpcName}] <color=yellow>BT: Chase entered</color>");
            _ctx.Blackboard.ActiveNodeName = "Chase";

            if (_ctx.Agent != null && _ctx.Agent.isActiveAndEnabled && _ctx.Agent.isOnNavMesh)
            {
                _ctx.Agent.isStopped      = false;
                _ctx.Agent.speed          = _ctx.Config.RunSpeed;
                _ctx.Agent.updateRotation = true; // Ensure agent controls rotation (investigate disables this)
            }

            if (_ctx.Anim != null)
            {
                // Clear the fixing animation in case we interrupted Investigate.
                // OnExit on Investigate is never called when the Selector switches branches mid-tick.
                _ctx.Anim.SetBool("IsFixing", false);
                _ctx.Anim.SetFloat("Speed", _ctx.Config.RunSpeed);
            }

            _lastDestinationUpdate = 0f;
        }

        protected override NodeState OnTick()
        {
            _ctx.Blackboard.ActiveNodeName = "Chase";

            if (_ctx.Player == null || _ctx.Agent == null) return NodeState.Running;

            // Rate-limit destination updates to avoid recalculating the path every frame
            if (Time.time - _lastDestinationUpdate > DEST_UPDATE_INTERVAL && !_ctx.Agent.pathPending)
            {
                _ctx.Agent.SetDestination(_ctx.PlayerWorldPosition);
                _lastDestinationUpdate = Time.time;
            }

            // Keep animator in sync with actual agent speed each tick
            if (_ctx.Anim != null)
                _ctx.Anim.SetFloat("Speed", _ctx.Agent.velocity.magnitude);

            return NodeState.Running;
        }

        protected override void OnExit(NodeState result)
        {
            if (_ctx.Agent != null && _ctx.Agent.isActiveAndEnabled && _ctx.Agent.isOnNavMesh)
                _ctx.Agent.isStopped = true;
        }
    }
}
