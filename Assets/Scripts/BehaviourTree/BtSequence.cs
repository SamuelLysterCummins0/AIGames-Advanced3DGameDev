namespace Semester2
{
    /// <summary>
    /// Composite node that runs children left to right.
    /// Returns Failure as soon as one child fails.
    /// Returns Success only when all children succeed in order.
    /// Returns Running if a child is still running.
    /// </summary>
    public class BtSequence : BtNode
    {
        private readonly BtNode[] _children;

        public BtSequence(BtNode[] children)
        {
            _children = children;
        }

        protected override NodeState OnTick()
        {
            foreach (var child in _children)
            {
                NodeState result = child.Tick();
                if (result != NodeState.Success)
                    return result; // Failure or Running stops the sequence
            }
            return NodeState.Success;
        }
    }
}
