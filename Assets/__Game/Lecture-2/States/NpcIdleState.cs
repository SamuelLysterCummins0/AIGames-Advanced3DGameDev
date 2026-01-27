using UnityEngine;
using UnityEngine.AI;

namespace Semester2
{
    /// <summary>
    /// NPC Idle State - The NPC is standing still, not engaged with anything.
    /// Typical behavior: Look around, play idle animations, wait for events.
    /// Transitions: Idle -> Patrol (after a short delay)
    /// </summary>
    public class NpcIdleState : NpcStateBase
    {
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

            // Stop the NavMeshAgent if available and active
            if (navMeshAgent != null && navMeshAgent.isActiveAndEnabled && navMeshAgent.isOnNavMesh)
            {
                navMeshAgent.isStopped = true;
                navMeshAgent.velocity = Vector3.zero;
            }

            // Play idle animation
            if (animator != null)
            {
                animator.SetFloat("Speed", 0f);
            }

            // Reset idle timer
            idleTimer = 0f;
        }

        public override void OnUpdate()
        {
            // Check for player proximity first (higher priority)
            if (IsPlayerInAttackRange())
            {
                fsm?.ChangeState<NpcAttackState>();
                return;
            }

            if (IsPlayerInDetectionRange())
            {
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
