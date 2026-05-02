using UnityEngine;

namespace Semester2
{
    /// <summary>
    /// Attacks the player. Maintains optimal shooting distance and fires on cooldown.
    /// The BT attack branch (BtCheckInAttackRange → BtActionAttack) keeps returning
    /// Running while the player stays in range. The BtSelector automatically switches
    /// back to Chase if BtCheckInAttackRange fails (player moved out of range).
    /// </summary>
    public class BtActionAttack : BtNode
    {
        private readonly NpcBtContext _ctx;
        private readonly GunAudio    _gunAudio; // optional — null if NPC has no GunAudio component

        private float _lastAttackTime  = 0f;
        private float _shootingDistance;

        // Track when LOS was lost — only stop firing after a sustained loss
        private float _losLostStartTime = -1f;

        public BtActionAttack(NpcBtContext ctx)
        {
            _ctx              = ctx;
            _shootingDistance = ctx.Config.AttackRange * ctx.Config.ShootingDistanceRatio;
            // Search the NPC hierarchy for a GunAudio component (e.g. on a weapon child GO).
            // GetComponentInChildren also checks the root, so it works whether it's on the
            // NPC itself or a child weapon object.
            _gunAudio = ctx.Owner.GetComponentInChildren<GunAudio>(includeInactive: true);
        }

        protected override void OnEnter()
        {
            Debug.Log($"[{_ctx.NpcName}] <color=red>BT: Attack entered</color>");
            _ctx.Blackboard.ActiveNodeName = "Attack";

            _losLostStartTime = -1f;

            if (_ctx.Agent != null && _ctx.Agent.isActiveAndEnabled && _ctx.Agent.isOnNavMesh)
            {
                _ctx.Agent.isStopped      = true;
                _ctx.Agent.velocity       = Vector3.zero;
                _ctx.Agent.updateRotation = true; // Restore in case Investigate had disabled this
            }

            if (_ctx.Anim != null)
            {
                // Clear any other animation state but do NOT fire the Attack trigger here.
                // ExecuteAttack() fires it on cooldown — firing it on every OnEnter causes
                // animation flicker when the attack sequence rapidly enters/exits at the
                // range boundary (especially visible for networked remote players).
                _ctx.Anim.SetBool("IsFixing", false);
                _ctx.Anim.SetFloat("Speed", 0f);
                _ctx.Anim.ResetTrigger("Attack");
            }

            // _lastAttackTime is NOT reset here so the cooldown from the previous
            // attack carries over if the player runs away and re-enters range quickly.
        }

        protected override NodeState OnTick()
        {
            _ctx.Blackboard.ActiveNodeName = "Attack";

            float dist = _ctx.Blackboard.DistanceToPlayer;

            // Strafe to maintain optimal attack distance
            if (dist < _shootingDistance * 0.85f)
                MoveAwayFromPlayer();
            else if (dist > _shootingDistance * 1.15f && dist < _ctx.Config.AttackRange)
                MoveTowardPlayer();
            else
                StopMoving();

            FacePlayer();

            // Fire on cooldown when we have line of sight
            bool hasLOS = CheckLineOfSight();
            if (!hasLOS)
            {
                if (_losLostStartTime < 0f) _losLostStartTime = Time.time;
            }
            else
            {
                _losLostStartTime = -1f;
                if (Time.time >= _lastAttackTime + _ctx.Config.AttackCooldown)
                {
                    ExecuteAttack();
                    _lastAttackTime = Time.time;
                }
            }

            return NodeState.Running;
        }

        protected override void OnExit(NodeState result)
        {
            if (_ctx.Anim != null)
                _ctx.Anim.ResetTrigger("Attack");
        }

        private bool CheckLineOfSight()
        {
            if (_ctx.Player == null) return false;
            Vector3 eye    = _ctx.Owner.transform.position + Vector3.up * _ctx.Config.NpcEyeHeight;
            Vector3 target = _ctx.PlayerWorldPosition + Vector3.up * _ctx.Config.PlayerCenterHeight;
            Vector3 dir    = target - eye;
            return !Physics.Raycast(eye, dir.normalized, dir.magnitude, _ctx.Config.ObstacleLayerMask);
        }

        private void MoveAwayFromPlayer()
        {
            if (_ctx.Player == null || _ctx.Agent == null) return;
            Vector3 dir = (_ctx.Owner.transform.position - _ctx.PlayerWorldPosition).normalized;
            _ctx.Agent.isStopped = false;
            _ctx.Agent.SetDestination(_ctx.Owner.transform.position + dir);
            if (_ctx.Anim != null) _ctx.Anim.SetFloat("Speed", _ctx.Agent.velocity.magnitude);
        }

        private void MoveTowardPlayer()
        {
            if (_ctx.Player == null || _ctx.Agent == null) return;
            _ctx.Agent.isStopped = false;
            _ctx.Agent.SetDestination(_ctx.PlayerWorldPosition);
            if (_ctx.Anim != null) _ctx.Anim.SetFloat("Speed", _ctx.Agent.velocity.magnitude);
        }

        private void StopMoving()
        {
            if (_ctx.Agent == null) return;
            _ctx.Agent.isStopped = true;
            _ctx.Agent.velocity  = Vector3.zero;
            if (_ctx.Anim != null) _ctx.Anim.SetFloat("Speed", 0f);
        }

        private void FacePlayer()
        {
            if (_ctx.Player == null) return;
            Vector3 dir = (_ctx.PlayerWorldPosition - _ctx.Owner.transform.position).normalized;
            dir.y = 0f;
            if (dir == Vector3.zero) return;
            _ctx.Owner.transform.rotation = Quaternion.Slerp(
                _ctx.Owner.transform.rotation,
                Quaternion.LookRotation(dir),
                Time.deltaTime * _ctx.Config.AttackRotationSpeed
            );
        }

        private void ExecuteAttack()
        {
            Debug.Log($"[{_ctx.NpcName}] <color=red>Attacking!</color>");

            // Notify NpcController to increment AttackTick — Render() on all peers fires
            // the Attack trigger locally so the animation plays on every client's screen.
            _ctx.OnAttackFired?.Invoke();

            _gunAudio?.PlayShot();

            // Route damage via RPC so the correct client's PlayerHealth is updated.
            // In Shared Mode each player owns their own NetworkObject, so TakeDamage
            // must reach the StateAuthority of the target player — not just the host.
            if (_ctx.Player != null)
            {
                PlayerHealth health = _ctx.Player.GetComponent<PlayerHealth>()
                                   ?? _ctx.Player.GetComponentInParent<PlayerHealth>()
                                   ?? _ctx.Player.GetComponentInChildren<PlayerHealth>();

                if (health != null && health.Runner != null)
                    health.RPC_TakeDamage(_ctx.Config.AttackDamage);
                else
                    health?.TakeDamage(_ctx.Config.AttackDamage); // non-networked fallback
            }
        }
    }
}
