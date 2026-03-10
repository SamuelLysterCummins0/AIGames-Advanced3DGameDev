namespace Semester2
{
    /// <summary>
    /// Base class for decorator nodes. Wraps a single child and modifies its behaviour.
    /// </summary>
    public abstract class BtDecorator : BtNode
    {
        protected readonly BtNode _child;

        protected BtDecorator(BtNode child)
        {
            _child = child;
        }
    }
}
