namespace Semester2
{
    /// <summary>
    /// Returns Success if the NPC has a last known player position to search around.
    /// This is set whenever the player is seen or heard, and cleared when search ends.
    /// </summary>
    public class BtCheckHasLastKnownPosition : BtNode
    {
        private readonly NpcBlackboard _bb;

        public BtCheckHasLastKnownPosition(NpcBlackboard blackboard)
        {
            _bb = blackboard;
        }

        protected override NodeState OnTick()
        {
            return _bb.HasLastKnownPosition ? NodeState.Success : NodeState.Failure;
        }
    }
}
