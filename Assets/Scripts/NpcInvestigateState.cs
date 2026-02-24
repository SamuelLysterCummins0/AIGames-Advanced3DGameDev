using UnityEngine;
using UnityEngine.AI;

namespace Semester2
{
    /// <summary>
    /// NPC Investigate State - triggered when a power box is activated.
    ///
    /// Phase flow:
    ///   MoveToBox  →  LookAround  →  Fixing  →  Complete (→ Patrol)
    ///
    /// At ANY phase, if the player is spotted or heard the NPC immediately
    /// switches to Chase, leaving the power box active.
    ///
    /// Patterns used:
    /// - State Pattern: internal InvestigatePhase enum drives behaviour
    /// - Observer Pattern: subscribes to PowerBoxInteractable events for external fix signals
    /// </summary>
    public class NpcInvestigateState : NpcStateBase
    {
        // ── Internal Phase ────────────────────────────────────────────────────────

        private enum InvestigatePhase { LookAround, MoveToBox, Fixing, Complete }
        private InvestigatePhase currentPhase;

        // ── Target ────────────────────────────────────────────────────────────────

        private PowerBoxInteractable targetBox;

        // ── Look Around ───────────────────────────────────────────────────────────

        private float lookAroundTimer;
        private float initialLookYRotation;  // Captured when look-around begins
        private const float MAX_LOOK_ANGLE = 90f;  // ±90° = full 180° sweep

        // ── Fixing ────────────────────────────────────────────────────────────────

        private float fixTimer;

        // ── Animation Strings ─────────────────────────────────────────────────────
        // Change these to match your Animator parameter names if different

        private const string ANIM_SPEED = "Speed";
        private const string ANIM_IS_FIXING = "IsFixing";
        private const string ANIM_FIX_TAG = "Fix";      // Tag on your fix animation state

        // ── Constructor ───────────────────────────────────────────────────────────

        public NpcInvestigateState(GameObject ownerGameObject, NpcConfig config)
            : base(ownerGameObject, config)
        {
        }

        // ── Public Setup ──────────────────────────────────────────────────────────

        /// <summary>
        /// Called by NpcController before ChangeState so the state knows which box to investigate.
        /// </summary>
        public void SetTargetBox(PowerBoxInteractable box)
        {
            targetBox = box;
        }

        /// <summary>
        /// Called from NpcController.LateUpdate() so rotation is applied AFTER Unity's
        /// Animator root-motion step (which runs between Update and LateUpdate).
        /// Without this, any animation that has un-baked root rotation will overwrite
        /// the look-around sweep and box-facing direction we set during Update each frame.
        /// </summary>
        public void ReapplyRotation()
        {
            switch (currentPhase)
            {
                case InvestigatePhase.LookAround:
                    float t         = lookAroundTimer / config.InvestigateLookDuration;
                    float sineAngle = Mathf.Sin(t * Mathf.PI * 4f) * MAX_LOOK_ANGLE;
                    owner.transform.rotation = Quaternion.Euler(0f, initialLookYRotation + sineAngle, 0f);
                    break;

                case InvestigatePhase.Fixing:
                    if (targetBox != null)
                        owner.transform.rotation =
                            Quaternion.LookRotation(targetBox.GetNpcFacingDirection());
                    break;
            }
        }

        // ── IState Implementation ─────────────────────────────────────────────────

        public override void OnEnter()
        {
            Debug.Log($"[{npcName}] <color=cyan>INVESTIGATE STATE ENTERED</color>");

            // Start by walking to the box immediately
            currentPhase = InvestigatePhase.MoveToBox;
            lookAroundTimer = 0f;
            fixTimer = 0f;

            if (navMeshAgent != null && navMeshAgent.isActiveAndEnabled && navMeshAgent.isOnNavMesh)
            {
                navMeshAgent.isStopped = false;
                navMeshAgent.updateRotation = true;
                navMeshAgent.speed = config.WalkSpeed;

                if (targetBox != null)
                {
                    // Set the destination directly — do NOT pass through SamplePosition
                    // first. SamplePosition snaps to the nearest baked triangle which can
                    // be offset sideways, causing the NPC to stop to the side of the box
                    // every time. The NavMeshAgent's own pathfinder will handle getting
                    // there correctly without the extra offset.
                    navMeshAgent.SetDestination(targetBox.GetNpcStandPosition());
                }
            }

            if (animator != null)
                animator.SetFloat(ANIM_SPEED, config.WalkSpeed);
        }

        public override void OnUpdate()
        {
            // ── Player detection exits at ANY time, even mid-fix ──────────────────

            if (IsPlayerInDetectionRange())
            {
                Debug.Log($"[{npcName}] Player spotted during investigation - switching to chase!");
                fsm?.ChangeState<NpcChaseState>();
                return;
            }

            Vector3 heardPosition;
            if (CanHearPlayer(out heardPosition))
            {
                Debug.Log($"[{npcName}] Player heard during investigation - switching to chase!");
                fsm?.ChangeState<NpcChaseState>();
                return;
            }

            // If the box was fixed externally (another NPC, destroyed, etc.) just go back to patrol
            if (targetBox == null || !targetBox.IsActivated)
            {
                if (currentPhase != InvestigatePhase.Complete)
                {
                    Debug.Log($"[{npcName}] Power box no longer active, returning to patrol");
                    fsm?.ChangeState<NpcPatrolState>();
                    return;
                }
            }

            // ── Phase Updates ─────────────────────────────────────────────────────

            switch (currentPhase)
            {
                case InvestigatePhase.LookAround: UpdateLookAround(); break;
                case InvestigatePhase.MoveToBox:  UpdateMoveToBox();  break;
                case InvestigatePhase.Fixing:     UpdateFixing();     break;
                case InvestigatePhase.Complete:   fsm?.ChangeState<NpcPatrolState>(); break;
            }
        }

        public override void OnExit()
        {
            Debug.Log($"[{npcName}] <color=cyan>INVESTIGATE STATE EXITED</color>");

            // Restore NavMeshAgent rotation control so every other state works normally.
            // (We disable it during LookAround to allow the manual sine-sweep rotation.)
            if (navMeshAgent != null)
                navMeshAgent.updateRotation = true;

            // Stop fix animation regardless of why we're leaving
            if (animator != null)
            {
                animator.SetBool(ANIM_IS_FIXING, false);
                animator.SetFloat(ANIM_SPEED, 0f);
            }

            if (navMeshAgent != null && navMeshAgent.isActiveAndEnabled && navMeshAgent.isOnNavMesh)
                navMeshAgent.isStopped = false;
        }

        // ── Phase: Look Around ────────────────────────────────────────────────────

        private void UpdateLookAround()
        {
            lookAroundTimer += Time.deltaTime;

            // ── Timer check FIRST ─────────────────────────────────────────────────
            // By checking before applying the sine rotation we guarantee the very
            // last frame of look-around always ends with the NPC facing the box,
            // not wherever the oscillation happened to land on that frame.
            if (lookAroundTimer >= config.InvestigateLookDuration)
            {
                // Snap to face the box using the same direction method used for the
                // sweep centre, so the end-of-sweep pose is always consistent.
                if (targetBox != null)
                    owner.transform.rotation =
                        Quaternion.LookRotation(targetBox.GetNpcFacingDirection());

                Debug.Log($"[{npcName}] Area searched - now fixing the power box");
                TransitionToFixing();
                return;
            }

            // ── Sine sweep ────────────────────────────────────────────────────────
            // t goes 0→1 over InvestigateLookDuration.
            // sweepCount full left-right cycles. At sweepCount=4 the sine also
            // returns to exactly 0 at t=1, so the rotation naturally returns to the
            // box-facing direction right as the timer expires.
            float t          = lookAroundTimer / config.InvestigateLookDuration;
            float sweepCount = 4f;
            float sineAngle  = Mathf.Sin(t * Mathf.PI * sweepCount) * MAX_LOOK_ANGLE;
            owner.transform.rotation = Quaternion.Euler(0f, initialLookYRotation + sineAngle, 0f);
        }

        // ── Phase: Move To Box ────────────────────────────────────────────────────

        private void UpdateMoveToBox()
        {
            if (targetBox == null || navMeshAgent == null || !navMeshAgent.isOnNavMesh) return;
            if (navMeshAgent.pathPending) return;

            // remainingDistance is the distance along the path to the destination -
            // much more reliable than raw Vector3.Distance to the box centre, because
            // the NavMeshAgent stops at the edge of the box's collider, not its centre.
            bool closeEnough = navMeshAgent.remainingDistance <= config.WaypointReachedThreshold + 1f;

            // PathPartial means the agent got as close as the NavMesh allows but still
            // hasn't reached the destination (box is surrounded by non-walkable surface).
            bool stuckOnPartialPath = navMeshAgent.pathStatus == NavMeshPathStatus.PathPartial
                                      && navMeshAgent.velocity.sqrMagnitude < 0.01f;

            if (closeEnough || stuckOnPartialPath)
                TransitionToLookAround();
        }

        private void TransitionToLookAround()
        {
            Debug.Log($"[{npcName}] Arrived near power box - searching the area");
            currentPhase = InvestigatePhase.LookAround;
            lookAroundTimer = 0f;

            // Centre the sweep on GetNpcFacingDirection() — the direction the NPC
            // should face when looking straight at the box panel. Using the raw
            // direction to BoxTransform.position is unreliable because most box models
            // have their pivot at the mesh centre or back face, not the interactive
            // front. GetNpcFacingDirection() always returns the correct perpendicular.
            initialLookYRotation = targetBox != null
                ? Quaternion.LookRotation(targetBox.GetNpcFacingDirection()).eulerAngles.y
                : owner.transform.eulerAngles.y;

            navMeshAgent.isStopped = true;
            // Disable agent rotation so manual sine-sweep isn't overwritten each frame.
            navMeshAgent.updateRotation = false;

            if (animator != null)
                animator.SetFloat(ANIM_SPEED, 0f);
        }

        private void TransitionToFixing()
        {
            Debug.Log($"[{npcName}] Arrived at power box - starting repair");
            currentPhase = InvestigatePhase.Fixing;
            fixTimer = 0f;

            navMeshAgent.isStopped = true;

            if (targetBox != null)
            {
                // Face the box using the same reliable direction used for the sweep centre,
                // so the NPC always looks perpendicular to the panel face regardless of
                // where the model pivot sits.
                owner.transform.rotation =
                    Quaternion.LookRotation(targetBox.GetNpcFacingDirection());
            }

            // Start fix animation
            if (animator != null)
            {
                animator.SetFloat(ANIM_SPEED, 0f);
                animator.SetBool(ANIM_IS_FIXING, true);
            }
        }

        // ── Phase: Fixing ─────────────────────────────────────────────────────────

        private void UpdateFixing()
        {
            fixTimer += Time.deltaTime;

            // Re-apply facing every frame so animation root motion cannot rotate the
            // NPC away from the box panel during the repair clip.
            if (targetBox != null)
                owner.transform.rotation =
                    Quaternion.LookRotation(targetBox.GetNpcFacingDirection());

            // Primary: check if the fix animation has completed
            if (animator != null)
            {
                AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(0);

                // IsTag("Fix") lets you tag your fix animation state in the Animator
                // so this works regardless of the exact state name
                if (stateInfo.IsTag(ANIM_FIX_TAG) && stateInfo.normalizedTime >= 1f)
                {
                    CompleteFix();
                    return;
                }
            }

            // Fallback: if no animation tag is set, use the config fix duration timer
            if (fixTimer >= config.InvestigateFixDuration)
                CompleteFix();
        }

        private void CompleteFix()
        {
            Debug.Log($"[{npcName}] Power box repaired!");
            currentPhase = InvestigatePhase.Complete;

            if (animator != null)
                animator.SetBool(ANIM_IS_FIXING, false);

            // Signal the power box - this fires OnPowerBoxFixed which stops sparks and lights
            if (targetBox != null)
                targetBox.FixPowerBox();
        }
    }
}
