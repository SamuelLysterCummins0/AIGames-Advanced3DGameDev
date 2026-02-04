using UnityEngine;
using UnityEngine.AI;

namespace Semester2
{
    /// <summary>
    /// NPC Idle State - The NPC is standing still, not engaged with anything.
    /// Typical behavior: Look around, play idle animations, wait for events.
    /// Transitions: Idle -> Patrol (after a short delay) or Idle -> Chase (if player detected)
    /// </summary>
    public class NpcIdleState : NpcStateBase
    {
        // Hysteresis to prevent state flickering
        private float stateEnterTime = 0f;
        private const float MIN_STATE_TIME = 0.5f; // Minimum time before allowing state transitions

        private float idleTimer = 0f;

        /// <summary>
        /// Constructor that stores reference to the owner GameObject and caches components.
        /// </summary>
        /// <param name="ownerGameObject">The GameObject that owns this state (the NPC)</param>
        /// <param name="config">Configuration parameters for NPC behavior</param>
        public NpcIdleState(GameObject ownerGameObject, NpcConfig config)
            : base(ownerGameObject, config)
        {
        }

        public override void OnEnter()
        {
            Debug.Log($"[{npcName}] NPC is now Idle - Looking around");

            stateEnterTime = Time.time; // Track when we entered this state

            // Stop the NavMeshAgent if available and active
            if (navMeshAgent != null && navMeshAgent.isActiveAndEnabled && navMeshAgent.isOnNavMesh)
            {
                navMeshAgent.isStopped = true;
                navMeshAgent.velocity = Vector3.zero;
            }

            // Play idle animation if animator is available
            if (animator != null)
            {
                animator.SetFloat("Speed", 0f);
            }

            // Reset idle timer
            idleTimer = 0f;
        }

        public override void OnUpdate()
        {
            float timeSinceEnter = Time.time - stateEnterTime;

            // HYSTERESIS: Only check transitions after minimum state time to prevent flickering
            if (timeSinceEnter < MIN_STATE_TIME)
                return;

            // Check for player proximity first (higher priority)
            if (IsPlayerInAttackRange())
            {
                fsm?.ChangeState<NpcAttackState>();
                return;
            }

            // Check visual detection
            if (IsPlayerInDetectionRange())
            {
                Debug.Log($"[{npcName}] Player spotted visually while idle!");
                fsm?.ChangeState<NpcChaseState>();
                return;
            }

            // Check audio detection
            Vector3 heardPosition;
            if (CanHearPlayer(out heardPosition))
            {
                Debug.Log($"[{npcName}] Heard player noise while idle at {heardPosition}!");
                fsm?.ChangeState<NpcChaseState>();
                return;
            }

            // If no player detected, wait for idle duration then start patrolling
            idleTimer += Time.deltaTime;
            if (idleTimer >= config.IdleDuration)
            {
                fsm?.ChangeState<NpcPatrolState>();
            }
        }

        public override void OnExit()
        {
            Debug.Log($"[{npcName}] NPC leaving Idle state");
        }
    }
}
