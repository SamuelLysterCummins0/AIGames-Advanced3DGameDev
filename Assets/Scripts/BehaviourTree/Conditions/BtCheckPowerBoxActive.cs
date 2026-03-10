namespace Semester2
{
    /// <summary>
    /// Returns Success if a power box has been activated and needs investigating.
    /// Fails if HasLastKnownPosition is true — this forces the root Selector to fall
    /// through to the Search branch first. Once Search clears HasLastKnownPosition,
    /// this check passes and the NPC returns to investigate the power box.
    /// </summary>
    public class BtCheckPowerBoxActive : BtNode
    {
        private readonly NpcBlackboard _bb;

        public BtCheckPowerBoxActive(NpcBlackboard blackboard)
        {
            _bb = blackboard;
        }

        protected override NodeState OnTick()
        {
            if (_bb.HasLastKnownPosition) return NodeState.Failure;
            return _bb.PowerBoxActive ? NodeState.Success : NodeState.Failure;
        }
    }
}
