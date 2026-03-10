using UnityEngine;

namespace Semester2
{
    /// <summary>
    /// Decorator that blocks its child from running again for a set time
    /// after the child finishes (Success or Failure).
    /// Returns Failure while the cooldown is active.
    ///
    /// Used on the Search branch to prevent the NPC immediately re-entering
    /// search mode after a failed search ends.
    /// </summary>
    public class BtCooldown : BtDecorator
    {
        private readonly float _cooldownTime;
        private float _nextAllowedTime = 0f;

        public BtCooldown(BtNode child, float cooldownTime) : base(child)
        {
            _cooldownTime = cooldownTime;
        }

        protected override NodeState OnTick()
        {
            // Block the child while cooling down
            if (Time.time < _nextAllowedTime)
                return NodeState.Failure;

            NodeState result = _child.Tick();

            // Start cooldown when the child finishes
            if (result == NodeState.Success || result == NodeState.Failure)
                _nextAllowedTime = Time.time + _cooldownTime;

            return result;
        }
    }
}
