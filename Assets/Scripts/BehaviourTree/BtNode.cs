using UnityEngine;

namespace Semester2
{
    public enum NodeState { Success, Failure, Running }

    /// <summary>
    /// Base class for all Behaviour Tree nodes.
    /// Uses frame-count tracking to detect re-entry after an interruption,
    /// so OnEnter is called automatically whenever the node starts fresh.
    /// </summary>
    public abstract class BtNode
    {
        // Tracks the last frame this node was ticked.
        // A gap of more than one frame means the node was interrupted or is starting fresh.
        private int _lastTickFrame = -999;

        public NodeState Tick()
        {
            if (Time.frameCount - _lastTickFrame > 1)
                OnEnter();

            _lastTickFrame = Time.frameCount;

            NodeState result = OnTick();

            if (result != NodeState.Running)
                OnExit(result);

            return result;
        }

        // Called once when the node first runs (or re-runs after a gap).
        protected virtual void OnEnter() { }

        // Called every tick while running. Must be implemented by subclasses.
        protected abstract NodeState OnTick();

        // Called when the node finishes (Success or Failure).
        protected virtual void OnExit(NodeState result) { }
    }
}
