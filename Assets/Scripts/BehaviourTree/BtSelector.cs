namespace Semester2
{
    /// <summary>
    /// Composite node that tries children left to right.
    /// Returns Success as soon as one child succeeds.
    /// Returns Failure only when all children fail.
    /// Returns Running if a child is still running.
    ///
    /// Because this re-evaluates from child[0] every tick, higher-priority
    /// branches automatically interrupt lower-priority ones — this is the
    /// interrupt mechanism used in this BT.
    /// </summary>
    public class BtSelector : BtNode
    {
        private readonly BtNode[] _children;

        public BtSelector(BtNode[] children)
        {
            _children = children;
        }

        protected override NodeState OnTick()
        {
            foreach (var child in _children)
            {
                NodeState result = child.Tick();
                if (result != NodeState.Failure)
                    return result; // Success or Running: this child wins this tick
            }
            return NodeState.Failure;
        }
    }
}
