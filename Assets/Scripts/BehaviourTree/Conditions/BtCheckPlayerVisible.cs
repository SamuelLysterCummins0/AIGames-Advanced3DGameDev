namespace Semester2
{
    /// <summary>
    /// Returns Success if the player is currently visible (LOS + FOV check).
    /// Reads from the Blackboard — perception is updated by NpcController before each BT tick.
    /// </summary>
    public class BtCheckPlayerVisible : BtNode
    {
        private readonly NpcBlackboard _bb;

        public BtCheckPlayerVisible(NpcBlackboard blackboard)
        {
            _bb = blackboard;
        }

        protected override NodeState OnTick()
        {
            return _bb.PlayerVisible ? NodeState.Success : NodeState.Failure;
        }
    }
}
