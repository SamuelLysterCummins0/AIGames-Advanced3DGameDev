using UnityEngine;

namespace Semester2
{
    /// <summary>
    /// Decorator that aborts its child and returns Failure if the child has been
    /// running for longer than the allowed time. Prevents the NPC getting stuck
    /// in any single branch indefinitely.
    ///
    /// Used on the Chase and Investigate branches so they can't run forever
    /// if the player somehow becomes unreachable.
    /// </summary>
    public class BtTimeout : BtDecorator
    {
        private readonly float _maxDuration;
        private float _startTime;

        public BtTimeout(BtNode child, float maxDuration) : base(child)
        {
            _maxDuration = maxDuration;
        }

        protected override void OnEnter()
        {
            _startTime = Time.time;
        }

        protected override NodeState OnTick()
        {
            // If the child has been running too long, abort it
            if (Time.time - _startTime >= _maxDuration)
            {
                Debug.Log($"[BtTimeout] Timed out after {_maxDuration}s — aborting child");
                return NodeState.Failure;
            }

            return _child.Tick();
        }
    }
}
