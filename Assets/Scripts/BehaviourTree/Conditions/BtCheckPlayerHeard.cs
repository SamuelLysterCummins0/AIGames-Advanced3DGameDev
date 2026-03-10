namespace Semester2
{
    /// <summary>
    /// Returns Success if the player is currently being heard but not seen.
    /// Used to trigger audio investigation without going through the post-chase cooldown.
    /// </summary>
    public class BtCheckPlayerHeard : BtNode
    {
        private readonly NpcBlackboard _bb;

        public BtCheckPlayerHeard(NpcBlackboard blackboard)
        {
            _bb = blackboard;
        }

        protected override NodeState OnTick()
        {
            // Only react to sound if the player isn't already visible.
            // If visible, the Threat branch handles it at higher priority.
            if (_bb.PlayerVisible) return NodeState.Failure;
            return _bb.PlayerHeard ? NodeState.Success : NodeState.Failure;
        }
    }
}
