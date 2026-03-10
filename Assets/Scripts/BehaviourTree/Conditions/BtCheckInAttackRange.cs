namespace Semester2
{
    /// <summary>
    /// Returns Success if the player is within attack range.
    /// Reads DistanceToPlayer from the Blackboard.
    /// </summary>
    public class BtCheckInAttackRange : BtNode
    {
        private readonly NpcBlackboard _bb;
        private readonly float _attackRange;

        public BtCheckInAttackRange(NpcBlackboard blackboard, float attackRange)
        {
            _bb          = blackboard;
            _attackRange = attackRange;
        }

        protected override NodeState OnTick()
        {
            return _bb.DistanceToPlayer <= _attackRange ? NodeState.Success : NodeState.Failure;
        }
    }
}
