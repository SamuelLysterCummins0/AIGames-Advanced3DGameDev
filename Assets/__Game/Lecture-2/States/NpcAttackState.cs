using UnityEngine;
using UnityEngine.AI;

namespace Semester2
{
    /// <summary>
    /// NPC Attack State - The NPC is within attack range and shooting at the target.
    /// Typical behavior: Maintain shooting distance, face target, play firing animation.
    /// For gun-wielding NPCs: keeps distance instead of getting close.
    /// Transitions: Attack -> Chase (player moved out of range) or Attack -> Patrol (player escaped)
    /// </summary>
    public class NpcAttackState : NpcStateBase
    {
        private float lastAttackTime = 0f;
        private float shootingDistance = 4f; // Preferred distance to shoot from

        /// <summary>
        /// Constructor that stores reference to the owner GameObject and caches components.
        /// </summary>
        /// <param name="ownerGameObject">The GameObject that owns this state (the NPC)</param>
        /// <param name="config">Configuration parameters for NPC behavior</param>
        public NpcAttackState(GameObject ownerGameObject, NpcConfig config) 
            : base(ownerGameObject, config)
        {
            // Set shooting distance (keep some distance for gun combat)
            shootingDistance = config.AttackRange * 0.7f; // Stay at about 70% of max attack range
        }

        public override void OnEnter()
        {
            Debug.Log($"[{npcName}] NPC is Attacking - Taking position!");
            
            // Stop the NavMeshAgent for shooting
            if (navMeshAgent != null && navMeshAgent.isActiveAndEnabled && navMeshAgent.isOnNavMesh)
            {
                navMeshAgent.isStopped = true;
                navMeshAgent.velocity = Vector3.zero;
            }
            
            // Set animation to idle/standing (speed 0) since we stop to shoot
            if (animator != null)
            {
                animator.SetFloat("Speed", 0f);
            }
            
            // Reset attack cooldown - allow immediate attack
            lastAttackTime = Time.time - config.AttackCooldown;
        }

        public override void OnUpdate()
        {
            // Check if player is still in detection range
            if (!IsPlayerInDetectionRange())
            {
                // Player completely escaped, return to patrol
                fsm?.ChangeState<NpcPatrolState>();
                return;
            }

            // Get current distance to player
            float distanceToPlayer = GetDistanceToPlayer();

            // If player moved out of attack range but still in detection, chase them
            if (distanceToPlayer > config.AttackRange)
            {
                fsm?.ChangeState<NpcChaseState>();
                return;
            }

            // Check if we need to adjust our position (too close or too far for good shooting)
            if (distanceToPlayer < shootingDistance * 0.8f)
            {
                // Player is too close, back up slightly
                MoveAwayFromPlayer();
            }
            else if (distanceToPlayer > shootingDistance * 1.2f)
            {
                // Player is a bit far, move closer to good shooting distance
                MoveTowardPlayer();
            }
            else
            {
                // Good distance for shooting - stop and shoot
                StopMoving();
            }

            // Always face the target when attacking
            FaceTarget();

            // Check attack cooldown and shoot
            if (Time.time >= lastAttackTime + config.AttackCooldown)
            {
                ExecuteAttack();
                lastAttackTime = Time.time;
            }
        }

        public override void OnExit()
        {
            Debug.Log($"[{npcName}] NPC finished attacking");
        }

        /// <summary>
        /// Moves the NPC away from the player (backing up).
        /// </summary>
        private void MoveAwayFromPlayer()
        {
            if (player == null || owner == null) return;

            if (navMeshAgent != null && navMeshAgent.isActiveAndEnabled && navMeshAgent.isOnNavMesh)
            {
                // Calculate direction away from player
                Vector3 directionAwayFromPlayer = (owner.transform.position - player.position).normalized;
                Vector3 retreatPosition = owner.transform.position + directionAwayFromPlayer * 1f;

                navMeshAgent.isStopped = false;
                navMeshAgent.SetDestination(retreatPosition);
            }
        }

        /// <summary>
        /// Moves the NPC toward the player to get in shooting range.
        /// </summary>
        private void MoveTowardPlayer()
        {
            if (player == null) return;

            if (navMeshAgent != null && navMeshAgent.isActiveAndEnabled && navMeshAgent.isOnNavMesh)
            {
                navMeshAgent.isStopped = false;
                navMeshAgent.SetDestination(player.position);
            }
        }

        /// <summary>
        /// Stops the NPC movement for shooting.
        /// </summary>
        private void StopMoving()
        {
            if (navMeshAgent != null && navMeshAgent.isActiveAndEnabled && navMeshAgent.isOnNavMesh)
            {
                navMeshAgent.isStopped = true;
                navMeshAgent.velocity = Vector3.zero;
            }
        }

        /// <summary>
        /// Rotates the NPC to face the target.
        /// </summary>
        private void FaceTarget()
        {
            if (player == null || owner == null) return;

            Vector3 directionToTarget = (player.position - owner.transform.position).normalized;
            directionToTarget.y = 0; // Keep rotation on horizontal plane

            if (directionToTarget != Vector3.zero)
            {
                Quaternion lookRotation = Quaternion.LookRotation(directionToTarget);
                owner.transform.rotation = Quaternion.Slerp(
                    owner.transform.rotation,
                    lookRotation,
                    Time.deltaTime * config.AttackRotationSpeed
                );
            }
        }

        /// <summary>
        /// Execute the attack - play firing animation.
        /// </summary>
        private void ExecuteAttack()
        {
            Debug.Log($"[{npcName}] Firing weapon!");

            // Play firing animation
            if (animator != null)
            {
                animator.SetTrigger("Attack");
            }

            
        }
    }
}
