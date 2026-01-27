using UnityEngine;
using UnityEngine.AI;

namespace Semester2
{
    /// <summary>
    /// NPC Chase State - The NPC has detected a target and is pursuing it.
    /// Typical behavior: Follow the player at high speed with rifle running animation.
    /// Transitions: Chase -> Attack (player in range) or Chase -> Patrol (player out of range)
    /// </summary>
    public class NpcChaseState : NpcStateBase
    {
        /// <summary>
        /// Constructor that stores reference to the owner GameObject and caches components.
        /// </summary>
        /// <param name="ownerGameObject">The GameObject that owns this state (the NPC)</param>
        /// <param name="config">Configuration parameters for NPC behavior</param>
        public NpcChaseState(GameObject ownerGameObject, NpcConfig config) 
            : base(ownerGameObject, config)
        {
        }

        public override void OnEnter()
        {
            Debug.Log($"[{npcName}] NPC spotted target - Chasing!");

            // Resume NavMeshAgent and set run speed (faster than patrol)
            if (navMeshAgent != null && navMeshAgent.isActiveAndEnabled && navMeshAgent.isOnNavMesh)
            {
                navMeshAgent.isStopped = false;
                navMeshAgent.speed = config.RunSpeed;
            }

            // Play rifle running animation - set Speed parameter for running
            if (animator != null)
            {
                animator.SetFloat("Speed", config.RunSpeed);
            }
        }

        public override void OnUpdate()
        {
            // Check if player is now in attack range
            if (IsPlayerInAttackRange())
            {
                fsm?.ChangeState<NpcAttackState>();
                return;
            }
            
            // Check if player is out of detection range
            if (!IsPlayerInDetectionRange())
            {
                fsm?.ChangeState<NpcPatrolState>();
                return;
            }
            
            // Continue chasing: follow player, update navigation
            if (navMeshAgent != null && navMeshAgent.isActiveAndEnabled && navMeshAgent.isOnNavMesh && player != null)
            {
                // Continuously update destination to follow the moving target
                navMeshAgent.SetDestination(player.position);
            }
        }

        public override void OnExit()
        {
            Debug.Log($"[{npcName}] NPC lost target - stopping chase");

            // Stop movement when leaving chase state
            if (navMeshAgent != null && navMeshAgent.isActiveAndEnabled && navMeshAgent.isOnNavMesh)
            {
                navMeshAgent.isStopped = true;
            }
        }
    }
}
