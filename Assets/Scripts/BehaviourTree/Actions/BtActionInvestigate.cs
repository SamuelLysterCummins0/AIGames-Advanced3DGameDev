using UnityEngine;
using UnityEngine.AI;

namespace Semester2
{
    /// <summary>
    /// Investigates and repairs the active power box.
    /// Phase flow: MoveToBox → LookAround → Fixing → Complete.
    ///
    /// The Threat branch (PlayerVisible) has higher priority in the root Selector,
    /// so the NPC will interrupt this and chase if the player is spotted.
    /// On re-entry after a chase, OnEnter resets the phase back to MoveToBox
    /// so the NPC returns to finish the repair.
    ///
    /// ReapplyRotation() must be called from NpcController.LateUpdate() so
    /// the manual rotation survives Animator root motion.
    /// </summary>
    public class BtActionInvestigate : BtNode
    {
        private readonly NpcBtContext _ctx;

        private enum Phase { MoveToBox, LookAround, Fixing, Complete }
        private Phase _phase;

        private float _lookStartTime;
        private float _initialLookY;
        private const float MAX_LOOK_ANGLE = 90f;

        private float _fixStartTime;

        private const string ANIM_SPEED     = "Speed";
        private const string ANIM_IS_FIXING = "IsFixing";
        private const string ANIM_FIX_TAG   = "Fix";

        public BtActionInvestigate(NpcBtContext ctx)
        {
            _ctx = ctx;
        }

        protected override void OnEnter()
        {
            Debug.Log($"[{_ctx.NpcName}] <color=cyan>BT: Investigate entered</color>");
            _ctx.Blackboard.ActiveNodeName = "Investigate";

            _phase         = Phase.MoveToBox;
            _lookStartTime = 0f;
            _fixStartTime  = 0f;

            if (_ctx.Agent != null && _ctx.Agent.isActiveAndEnabled && _ctx.Agent.isOnNavMesh)
            {
                _ctx.Agent.isStopped      = false;
                _ctx.Agent.updateRotation = true;
                _ctx.Agent.speed          = _ctx.Config.WalkSpeed;

                var box = _ctx.Blackboard.TargetPowerBox;
                if (box != null)
                    _ctx.Agent.SetDestination(box.GetNpcStandPosition());
            }

            if (_ctx.Anim != null)
                _ctx.Anim.SetFloat(ANIM_SPEED, _ctx.Config.WalkSpeed);
        }

        protected override NodeState OnTick()
        {
            _ctx.Blackboard.ActiveNodeName = "Investigate";

            var box = _ctx.Blackboard.TargetPowerBox;

            // Box was removed or fixed externally
            if (box == null || !box.IsActivated)
            {
                Debug.Log($"[{_ctx.NpcName}] Power box no longer active.");
                return NodeState.Success;
            }

            switch (_phase)
            {
                case Phase.MoveToBox:  UpdateMoveToBox(box);  break;
                case Phase.LookAround: UpdateLookAround(box); break;
                case Phase.Fixing:     UpdateFixing(box);     break;
                case Phase.Complete:   return NodeState.Success;
            }

            return NodeState.Running;
        }

        protected override void OnExit(NodeState result)
        {
            // Restore agent rotation so all other states work normally
            if (_ctx.Agent != null)
                _ctx.Agent.updateRotation = true;

            if (_ctx.Anim != null)
            {
                _ctx.Anim.SetBool(ANIM_IS_FIXING, false);
                _ctx.Anim.SetFloat(ANIM_SPEED, 0f);
            }
        }

        /// <summary>
        /// Called from NpcController.LateUpdate() to reapply manual rotation
        /// after Unity's Animator root-motion step overwrites it.
        /// </summary>
        public void ReapplyRotation()
        {
            var box = _ctx.Blackboard.TargetPowerBox;
            if (box == null) return;

            switch (_phase)
            {
                case Phase.LookAround:
                    float elapsed = Time.time - _lookStartTime;
                    float t       = elapsed / _ctx.Config.InvestigateLookDuration;
                    float angle   = Mathf.Sin(t * Mathf.PI * 4f) * MAX_LOOK_ANGLE;
                    _ctx.Owner.transform.rotation = Quaternion.Euler(0f, _initialLookY + angle, 0f);
                    break;

                case Phase.Fixing:
                    _ctx.Owner.transform.rotation = Quaternion.LookRotation(box.GetNpcFacingDirection());
                    break;
            }
        }

        private void UpdateMoveToBox(PowerBoxInteractable box)
        {
            if (_ctx.Agent == null || !_ctx.Agent.isOnNavMesh || _ctx.Agent.pathPending) return;

            bool close = _ctx.Agent.remainingDistance <= _ctx.Config.WaypointReachedThreshold + 1f;
            bool stuck = _ctx.Agent.pathStatus == NavMeshPathStatus.PathPartial
                         && _ctx.Agent.velocity.sqrMagnitude < 0.01f;

            if (close || stuck)
                TransitionToLookAround(box);
        }

        private void TransitionToLookAround(PowerBoxInteractable box)
        {
            Debug.Log($"[{_ctx.NpcName}] Arrived near box - searching area.");
            _phase         = Phase.LookAround;
            _lookStartTime = Time.time;

            _initialLookY = Quaternion.LookRotation(box.GetNpcFacingDirection()).eulerAngles.y;

            _ctx.Agent.isStopped      = true;
            _ctx.Agent.updateRotation = false; // Manual sine-sweep rotation takes over
            if (_ctx.Anim != null) _ctx.Anim.SetFloat(ANIM_SPEED, 0f);
        }

        private void UpdateLookAround(PowerBoxInteractable box)
        {
            if (Time.time - _lookStartTime >= _ctx.Config.InvestigateLookDuration)
            {
                _ctx.Owner.transform.rotation = Quaternion.LookRotation(box.GetNpcFacingDirection());
                TransitionToFixing(box);
            }
        }

        private void TransitionToFixing(PowerBoxInteractable box)
        {
            Debug.Log($"[{_ctx.NpcName}] Starting repair.");
            _phase        = Phase.Fixing;
            _fixStartTime = Time.time;

            _ctx.Agent.isStopped = true;
            _ctx.Owner.transform.rotation = Quaternion.LookRotation(box.GetNpcFacingDirection());

            if (_ctx.Anim != null)
            {
                _ctx.Anim.SetFloat(ANIM_SPEED, 0f);
                _ctx.Anim.SetBool(ANIM_IS_FIXING, true);
            }
        }

        private void UpdateFixing(PowerBoxInteractable box)
        {
            _ctx.Owner.transform.rotation = Quaternion.LookRotation(box.GetNpcFacingDirection());

            // Check animation tag first (most reliable)
            if (_ctx.Anim != null)
            {
                AnimatorStateInfo info = _ctx.Anim.GetCurrentAnimatorStateInfo(0);
                if (info.IsTag(ANIM_FIX_TAG) && info.normalizedTime >= 1f)
                {
                    CompleteFix(box);
                    return;
                }
            }

            // Fallback: use the config timer if no fix animation tag is set
            if (Time.time - _fixStartTime >= _ctx.Config.InvestigateFixDuration)
                CompleteFix(box);
        }

        private void CompleteFix(PowerBoxInteractable box)
        {
            Debug.Log($"[{_ctx.NpcName}] Power box repaired!");
            _phase = Phase.Complete;

            if (_ctx.Anim != null) _ctx.Anim.SetBool(ANIM_IS_FIXING, false);

            box.FixPowerBox();

            // Clear the blackboard so the PowerBox branch stops triggering
            _ctx.Blackboard.PowerBoxActive = false;
            _ctx.Blackboard.TargetPowerBox = null;
        }
    }
}
