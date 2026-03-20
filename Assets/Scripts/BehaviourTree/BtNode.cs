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
        // Set this from NpcController to btTickInterval * 1.5f so re-entry detection
        // works correctly when the BT is throttled rather than ticking every frame.
        public static float ReEntryGap = 0.15f;

        private float _lastTickTime = -999f;

        public NodeState Tick()
        {
            if (Time.time - _lastTickTime > ReEntryGap)
                OnEnter();

            _lastTickTime = Time.time;

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
