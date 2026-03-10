namespace Semester2
{
    /// <summary>
    /// Returns Success if a power box has been activated and needs investigating.
    ///
    /// Fails if LkpFromChase is true — the NPC was actively chasing the player
    /// and still has a visual last-known position to search. Once Search clears
    /// LkpFromChase, this check passes and the NPC returns to investigate.
    ///
    /// We check LkpFromChase rather than HasLastKnownPosition so that a brief
    /// audio cue (e.g. the player pressing E on the box) does NOT block Investigate.
    /// Only a real chase (visual contact) defers the investigation.
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
            if (_bb.LkpFromChase) return NodeState.Failure;
            return _bb.PowerBoxActive ? NodeState.Success : NodeState.Failure;
        }
    }
}
