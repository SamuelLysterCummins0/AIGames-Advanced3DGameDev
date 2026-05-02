namespace Semester2
{
    /// <summary>
    /// Returns Success if the player is within attack range.
    /// Uses hysteresis so a player who is right at the boundary doesn't cause the
    /// attack sequence to enter and exit every tick — which would fire the Attack
    /// animation trigger on every BT tick and cause visible flickering.
    /// Enter threshold: attackRange. Exit threshold: attackRange * 1.2.
    /// </summary>
    public class BtCheckInAttackRange : BtNode
    {
        private readonly NpcBlackboard _bb;
        private readonly float _attackRange;
        private readonly float _exitRange; // must move further out before leaving attack mode

        private bool _inRange = false;

        public BtCheckInAttackRange(NpcBlackboard blackboard, float attackRange)
        {
            _bb         = blackboard;
            _attackRange = attackRange;
            _exitRange   = attackRange * 1.2f;
        }

        protected override NodeState OnTick()
        {
            float dist = _bb.DistanceToPlayer;

            if (!_inRange && dist <= _attackRange)
                _inRange = true;
            else if (_inRange && dist > _exitRange)
                _inRange = false;

            return _inRange ? NodeState.Success : NodeState.Failure;
        }
    }
}
