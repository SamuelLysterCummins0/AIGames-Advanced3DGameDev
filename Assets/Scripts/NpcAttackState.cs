using UnityEngine;
using UnityEngine.AI;

namespace Semester2
{
    
    public class NpcAttackState : NpcStateBase
    {
        private float lastAttackTime = 0f;
        private float shootingDistance = 4f;
        private float stateEnterTime = 0f;
        private const float MIN_STATE_TIME = 0.5f;

        public NpcAttackState(GameObject ownerGameObject, NpcConfig config)
            : base(ownerGameObject, config)
        {
            shootingDistance = config.AttackRange * 0.7f;
        }

        public override void OnEnter()
        {
            Debug.Log($"[{npcName}] <color=red>ATTACK STATE ENTERED</color>");

            stateEnterTime = Time.time;

            if (navMeshAgent != null && navMeshAgent.isActiveAndEnabled && navMeshAgent.isOnNavMesh)
            {
                navMeshAgent.isStopped = true;
                navMeshAgent.velocity = Vector3.zero;
            }

            if (animator != null)
            {
                animator.SetFloat("Speed", 0f);
            }

            lastAttackTime = Time.time - config.AttackCooldown;
        }

        public override void OnUpdate()
        {
            float timeSinceEnter = Time.time - stateEnterTime;
            float distanceToPlayer = GetDistanceToPlayer();

            if (timeSinceEnter > MIN_STATE_TIME)
            {
                // Only exit attack if player is truly far away (not just outside FOV)
                // Use a larger threshold than detection range to create hysteresis
                // This allows player to strafe, move behind NPC, etc. without breaking attack
                float attackExitThreshold = config.DetectionRange + 2f; // 5 + 2 = 7 units
                if (distanceToPlayer > attackExitThreshold)
                {
                    Debug.Log($"[{npcName}] Player escaped attack range (dist: {distanceToPlayer:F1} > {attackExitThreshold:F1})");
                    fsm?.ChangeState<NpcPatrolState>();
                    return;
                }

                // If player is between attack range and exit threshold, transition to chase
                float chaseThreshold = config.AttackRange + 1.5f; // 2 + 1.5 = 3.5 units
                if (distanceToPlayer > chaseThreshold && distanceToPlayer <= attackExitThreshold)
                {
                    Debug.Log($"[{npcName}] Player moved away, chasing (dist: {distanceToPlayer:F1})");
                    fsm?.ChangeState<NpcChaseState>();
                    return;
                }
            }

            if (distanceToPlayer < shootingDistance * 0.85f)
            {
                MoveAwayFromPlayer();
            }
            else if (distanceToPlayer > shootingDistance * 1.15f && distanceToPlayer < config.AttackRange)
            {
                MoveTowardPlayer();
            }
            else
            {
                StopMoving();
            }

            FaceTarget();

            if (Time.time >= lastAttackTime + config.AttackCooldown)
            {
                ExecuteAttack();
                lastAttackTime = Time.time;
            }
        }

        public override void OnExit()
        {
            Debug.Log($"[{npcName}] <color=red>ATTACK STATE EXITED</color>");
        }

        private void MoveAwayFromPlayer()
        {
            if (player == null || owner == null) return;

            if (navMeshAgent != null && navMeshAgent.isActiveAndEnabled && navMeshAgent.isOnNavMesh)
            {
                Vector3 directionAwayFromPlayer = (owner.transform.position - player.position).normalized;
                Vector3 retreatPosition = owner.transform.position + directionAwayFromPlayer * 1f;

                navMeshAgent.isStopped = false;
                navMeshAgent.SetDestination(retreatPosition);
            }
        }

        private void MoveTowardPlayer()
        {
            if (player == null) return;

            if (navMeshAgent != null && navMeshAgent.isActiveAndEnabled && navMeshAgent.isOnNavMesh)
            {
                navMeshAgent.isStopped = false;
                navMeshAgent.SetDestination(player.position);
            }
        }

        private void StopMoving()
        {
            if (navMeshAgent != null && navMeshAgent.isActiveAndEnabled && navMeshAgent.isOnNavMesh)
            {
                navMeshAgent.isStopped = true;
                navMeshAgent.velocity = Vector3.zero;
            }
        }

        private void FaceTarget()
        {
            if (player == null || owner == null) return;

            Vector3 directionToTarget = (player.position - owner.transform.position).normalized;
            directionToTarget.y = 0;

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

        private void ExecuteAttack()
        {
            Debug.Log($"[{npcName}] Firing weapon!");

            if (animator != null)
            {
                animator.SetTrigger("Attack");
            }
        }
    }
}