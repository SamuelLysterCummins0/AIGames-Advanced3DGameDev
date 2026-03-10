using System.Collections;
using UnityEngine;
using UnityEngine.AI;

namespace Semester2
{
    /// <summary>
    /// Drives the NPC using the custom pure-C# Behaviour Tree (Option A).
    ///
    /// This component owns:
    ///   - All perception logic (CanSeePlayer, CanHearPlayer)
    ///   - The NpcConfig built from inspector fields
    ///   - The NpcBlackboard updated each frame before the BT ticks
    ///   - Power box event handling (writes to Blackboard)
    ///   - The BT root node (BtSelector) and the full tree construction
    ///   - LateUpdate rotation reapplication for the Investigate action
    ///   - Takedown handling
    ///
    /// BT tree structure (priority order, root is a Selector):
    ///   1. Threat      — [PlayerVisible] → Selector → [InRange → Attack] | Chase
    ///   2. AudioSearch — [PlayerHeard]   → Search (tracks live sound, fans out when silent)
    ///   3. PostSearch  — CooldownDecorator(5 s) → [HasLastKnownPosition → Search]
    ///   4. PowerBox    — [PowerBoxActive &amp;&amp; !LkpFromChase] → Investigate
    ///   5. Patrol      — always Running (fallback)
    ///
    /// AudioSearch sits above PowerBox so hearing the player interrupts Investigate.
    /// When the player is newly heard the post-chase cooldown is reset so audio
    /// cues are never blocked by a leftover cooldown from a previous search.
    /// </summary>
    public class NpcController : MonoBehaviour
    {
        [Header("NPC Detection Settings")]
        [SerializeField] private float detectionRange = 10f;
        [SerializeField] private float attackRange    = 2.5f;

        [Header("NPC Vision Settings")]
        [SerializeField] private float     fieldOfViewAngle   = 90f;
        [SerializeField] private bool      requireLineOfSight = true;
        [SerializeField] private LayerMask obstacleLayerMask  = ~0;
        [SerializeField] private float     npcEyeHeight       = 1.6f;
        [SerializeField] private float     playerCenterHeight = 1f;

        [Header("NPC Audio Detection Settings")]
        [SerializeField] private float hearingRange         = 10f;
        [SerializeField] private float minNoiseThreshold    = 0.2f;
        [SerializeField] private bool  useOcclusionForSound = true;
        [SerializeField] private float soundOcclusionMult   = 0.5f;

        [Header("NPC Movement Settings")]
        [SerializeField] private float walkSpeed                = 3.5f;
        [SerializeField] private float runSpeed                 = 7f;
        [SerializeField] private float waypointReachedThreshold = 0.5f;

        [Header("NPC Behavior Settings")]
        [SerializeField] private float idleDuration        = 2f;
        [SerializeField] private float attackCooldown      = 1.5f;
        [SerializeField] private float attackRotationSpeed = 10f;

        [Header("Patrol Settings")]
        [SerializeField] private Transform[] patrolWaypoints;
        [SerializeField] private bool        autoGenerateWaypoints = false;
        [SerializeField] private float       waypointRadius        = 10f;
        [SerializeField] private int         autoWaypointCount     = 4;

        [Header("Waypoint Idle Settings")]
        [SerializeField] private bool  enableWaypointIdleStop = true;
        [Range(0f, 1f)]
        [SerializeField] private float waypointIdleChance     = 0.7f;
        [SerializeField] private float waypointIdleDuration   = 2f;

        [Header("Investigate State Settings")]
        [SerializeField] private float investigateLookDuration = 7f;
        [SerializeField] private float investigateFixDuration  = 12f;

        [Header("Death")]
        [SerializeField] private string      npcDeathTrigger   = "Death"; // must match Animator parameter
        [SerializeField] private AudioSource deathAudioSource;
        [SerializeField] private AudioClip   fallClip;
        [SerializeField] private float       fallSoundDelay = 1.8f;

        [Header("Debug Visualization")]
        [SerializeField] private bool showFieldOfView      = true;
        [SerializeField] private bool showDetectionRaycast = true;

        // ── Public API ─────────────────────────────────────────────────────────────

        /// <summary>Config built from inspector fields. Action scripts read speeds, ranges, etc.</summary>
        public NpcConfig Config { get; private set; }

        /// <summary>Shared blackboard — NpcDebugOverlay reads perception values from here.</summary>
        public NpcBlackboard Blackboard => _blackboard;

        /// <summary>The serialized patrol waypoints — used by BtActionPatrol.</summary>
        public Transform[] PatrolWaypoints => patrolWaypoints;

        /// <summary>The player transform cached at Start.</summary>
        public Transform PlayerTransform => _player;

        /// <summary>Current distance to the player (live calculation).</summary>
        public float DistanceToPlayer => _player != null
            ? Vector3.Distance(transform.position, _player.position)
            : float.MaxValue;

        /// <summary>
        /// The power box being investigated.
        /// Set by OnPowerBoxActivated, cleared by BtActionInvestigate on repair.
        /// </summary>
        public PowerBoxInteractable TargetPowerBox { get; set; }

        // ── Private fields ─────────────────────────────────────────────────────────

        private NpcBlackboard       _blackboard;
        private BtNode              _btRoot;
        private BtCooldown          _postChaseSearchCooldown; // kept so we can reset it on new audio cues
        private BtActionInvestigate _investigateAction; // kept so LateUpdate can call ReapplyRotation
        private bool                _isDead = false;
        private Transform           _player;
        private PlayerAudioEmitter  _audioEmitter;
        private Vector3             _lastPlayerPos;

        // ── Unity lifecycle ────────────────────────────────────────────────────────

        void Start()
        {
            Config = new NpcConfig
            {
                DetectionRange           = detectionRange,
                AttackRange              = attackRange,
                FieldOfViewAngle         = fieldOfViewAngle,
                RequireLineOfSight       = requireLineOfSight,
                ObstacleLayerMask        = obstacleLayerMask,
                NpcEyeHeight             = npcEyeHeight,
                PlayerCenterHeight       = playerCenterHeight,
                HearingRange             = hearingRange,
                WalkNoiseLevel           = 0.3f,
                RunNoiseLevel            = 1.0f,
                MinNoiseThreshold        = minNoiseThreshold,
                UseOcclusionForSound     = useOcclusionForSound,
                SoundOcclusionMultiplier = soundOcclusionMult,
                WalkSpeed                = walkSpeed,
                RunSpeed                 = runSpeed,
                IdleDuration             = idleDuration,
                AttackCooldown           = attackCooldown,
                WaypointReachedThreshold = waypointReachedThreshold,
                AttackRotationSpeed      = attackRotationSpeed,
                EnableWaypointIdleStop   = enableWaypointIdleStop,
                WaypointIdleChance       = waypointIdleChance,
                WaypointIdleDuration     = waypointIdleDuration,
                InvestigateLookDuration  = investigateLookDuration,
                InvestigateFixDuration   = investigateFixDuration
            };

            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null)
            {
                _player       = playerObj.transform;
                _audioEmitter = playerObj.GetComponent<PlayerAudioEmitter>();
            }

            if (autoGenerateWaypoints && (patrolWaypoints == null || patrolWaypoints.Length == 0))
                GenerateWaypoints();

            _blackboard = new NpcBlackboard();
            BuildBehaviourTree();

            if (_player != null) _lastPlayerPos = _player.position;
        }

        void Update()
        {
            if (_isDead) return;
            UpdatePerception();
            _btRoot?.Tick();
        }

        private void UpdatePerception()
        {
            _blackboard.DistanceToPlayer = DistanceToPlayer;

            bool canSee = CanSeePlayer();
            _blackboard.PlayerVisible = canSee;

            if (canSee && _player != null)
            {
                // Track player movement direction for directional search points
                Vector3 vel = (_player.position - _lastPlayerPos) / Time.deltaTime;
                if (vel.sqrMagnitude > 0.01f)
                    _blackboard.LastKnownPlayerMoveDir = vel.normalized;

                _blackboard.LastKnownPlayerPosition = _player.position;
                _blackboard.HasLastKnownPosition    = true;
                _blackboard.LkpFromChase            = true; // visual contact — defer Investigate
            }

            Vector3 heardAt;
            bool canHear = CanHearPlayer(out heardAt);

            // Reset the post-chase search cooldown the moment the player is newly heard.
            // Without this, a 5 s cooldown left over from a previous search would block
            // the NPC from reacting to fresh footsteps.
            bool wasHeard = _blackboard.PlayerHeard;
            if (canHear && !wasHeard)
                _postChaseSearchCooldown?.ResetCooldown();

            _blackboard.PlayerHeard = canHear;

            if (canHear && !canSee)
            {
                _blackboard.LastKnownPlayerPosition = heardAt;
                _blackboard.HasLastKnownPosition    = true;
                // LkpFromChase stays as-is — hearing alone doesn't mark it as a chase LKP
            }

            if (_player != null) _lastPlayerPos = _player.position;

            // Perception visualisation — visible in Game view when Gizmos are enabled.
            // Green  = player seen (in FOV + LOS clear)
            // Yellow = player heard but not seen
            // Red    = player in range but blocked/outside FOV
            if (_player != null)
            {
                Vector3 eye    = transform.position + Vector3.up * npcEyeHeight;
                Vector3 target = _player.position   + Vector3.up * playerCenterHeight;
                Color   rayCol = canSee ? Color.green : (canHear ? Color.yellow : Color.red);
                Debug.DrawLine(eye, target, rayCol);
            }
        }

        private void BuildBehaviourTree()
        {
            var ctx = new NpcBtContext(gameObject, Config, _blackboard);

            _investigateAction = new BtActionInvestigate(ctx);

            // ── Threat branch (highest priority) ─────────────────────────────────
            // Inner Selector: attack if in range, else chase
            var attackSequence = new BtSequence(new BtNode[]
            {
                new BtCheckInAttackRange(_blackboard, Config.AttackRange),
                new BtActionAttack(ctx)
            });
            var threatResponse = new BtSelector(new BtNode[]
            {
                attackSequence,
                new BtActionChase(ctx)
            });
            var threatBranch = new BtSequence(new BtNode[]
            {
                new BtCheckPlayerVisible(_blackboard),
                threatResponse
            });

            // ── Audio search branch — higher priority than PowerBox ───────────────
            // When the player is heard, this fires immediately and overtakes whatever
            // was running, including Investigate. BtActionSearch tracks the live heard
            // position while PlayerHeard == true, then fans out when sound stops.
            // PostChaseSearch (below) picks up the fan search once this branch fails.
            var audioSearchBranch = new BtSequence(new BtNode[]
            {
                new BtCheckPlayerHeard(_blackboard),
                new BtActionSearch(ctx)
            });

            // ── Post-chase search with cooldown decorator ─────────────────────────
            // Handles searching after audio stops or after losing sight of the player.
            // The 5 s cooldown prevents instantly re-entering search. It is reset by
            // UpdatePerception whenever the player is newly heard.
            var searchSequence = new BtSequence(new BtNode[]
            {
                new BtCheckHasLastKnownPosition(_blackboard),
                new BtActionSearch(ctx)
            });
            _postChaseSearchCooldown = new BtCooldown(searchSequence, 5f);

            // ── Power-box investigation branch ────────────────────────────────────
            // Only reached when neither search branch has work to do. LkpFromChase
            // defers this until any post-chase search clears the visual LKP.
            var powerBoxBranch = new BtSequence(new BtNode[]
            {
                new BtCheckPowerBoxActive(_blackboard),
                _investigateAction
            });

            // ── Patrol fallback (always Running) ──────────────────────────────────
            var patrolBranch = new BtActionPatrol(ctx, patrolWaypoints);

            // ── Root Selector — re-evaluates from top every tick ──────────────────
            _btRoot = new BtSelector(new BtNode[]
            {
                threatBranch,
                audioSearchBranch,        // heard → interrupt everything (incl. Investigate)
                _postChaseSearchCooldown, // fan search after sound/sight lost (cooldown resets on audio)
                powerBoxBranch,           // no active search → fix the box
                patrolBranch
            });
        }

        void OnEnable()
        {
            PowerBoxInteractable.OnPowerBoxActivated += OnPowerBoxActivated;
            PowerBoxInteractable.OnPowerBoxFixed      += OnPowerBoxFixed;
        }

        void OnDisable()
        {
            PowerBoxInteractable.OnPowerBoxActivated -= OnPowerBoxActivated;
            PowerBoxInteractable.OnPowerBoxFixed      -= OnPowerBoxFixed;
        }

        void LateUpdate()
        {
            // Only reapply investigate rotation while that branch is active
            if (_blackboard?.ActiveNodeName == "Investigate")
                _investigateAction?.ReapplyRotation();
        }

        // ── Public perception methods ──────────────────────────────────────────────

        /// <summary>Full vision check: distance, FOV cone, and LOS raycast.</summary>
        public bool CanSeePlayer()
        {
            if (_player == null) return false;
            if (DistanceToPlayer > detectionRange) return false;
            if (!requireLineOfSight) return true;

            Vector3 dir   = (_player.position - transform.position).normalized;
            float   angle = Vector3.Angle(transform.forward, dir);
            if (angle > fieldOfViewAngle / 2f) return false;

            Vector3 eye      = transform.position + Vector3.up * npcEyeHeight;
            Vector3 target   = _player.position   + Vector3.up * playerCenterHeight;
            Vector3 toTarget = target - eye;

            return !Physics.Raycast(eye, toTarget.normalized, toTarget.magnitude, obstacleLayerMask);
        }

        /// <summary>
        /// Hearing check with distance attenuation and optional occlusion.
        /// heardAt is an approximate NavMesh-snapped position, not the exact player location.
        /// </summary>
        public bool CanHearPlayer(out Vector3 heardAt)
        {
            heardAt = Vector3.zero;
            if (_player == null || _audioEmitter == null) return false;

            float dist = DistanceToPlayer;
            if (dist > hearingRange) return false;

            float noise = _audioEmitter.CurrentNoiseLevel;
            if (noise < minNoiseThreshold) return false;

            float effective = noise * (1f - dist / hearingRange);

            if (useOcclusionForSound)
            {
                Vector3 eye       = transform.position + Vector3.up * npcEyeHeight;
                Vector3 playerPos = _player.position   + Vector3.up * playerCenterHeight;
                Vector3 dir       = playerPos - eye;
                if (Physics.Raycast(eye, dir.normalized, dir.magnitude, obstacleLayerMask))
                    effective *= soundOcclusionMult;
            }

            if (effective < minNoiseThreshold) return false;

            Vector3 toPlayer   = _player.position - transform.position;
            float   approxDist = Mathf.Clamp(dist * Random.Range(0.8f, 1.1f), 1f, hearingRange);
            float   spreadDeg  = Mathf.Lerp(5f, 25f, dist / hearingRange);
            Vector3 approxDir  = Quaternion.Euler(0f, Random.Range(-spreadDeg, spreadDeg), 0f) * toPlayer.normalized;
            Vector3 approxPos  = transform.position + approxDir * approxDist;

            NavMeshHit navHit;
            heardAt = NavMesh.SamplePosition(approxPos, out navHit, 3f, NavMesh.AllAreas)
                ? navHit.position
                : _player.position;

            return true;
        }

        // ── Takedown ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Called by PlayerTakedownController. Sets _isDead so the BT stops ticking.
        /// </summary>
        public void StartTakedown(float duration)
        {
            _isDead = true;
            Debug.Log($"[{name}] <color=magenta>Takedown triggered.</color>");

            NavMeshAgent nav = GetComponent<NavMeshAgent>();
            if (nav != null && nav.isActiveAndEnabled && nav.isOnNavMesh)
            {
                nav.isStopped = true;
                nav.velocity  = Vector3.zero;
            }

            Animator anim = GetComponentInChildren<Animator>();
            if (anim != null)
            {
                anim.SetBool("IsFixing", false);
                anim.SetFloat("Speed", 0f);
                anim.ResetTrigger("Attack");
                if (!string.IsNullOrEmpty(npcDeathTrigger))
                    anim.SetTrigger(npcDeathTrigger);
            }

            if (fallClip != null && deathAudioSource != null)
                StartCoroutine(PlayFallAudio());

            StartCoroutine(DeactivateAfterDelay(duration));
        }

        private IEnumerator DeactivateAfterDelay(float delay)
        {
            yield return new WaitForSeconds(delay);
            gameObject.SetActive(false);
        }

        private IEnumerator PlayFallAudio()
        {
            yield return new WaitForSeconds(fallSoundDelay);
            deathAudioSource.PlayOneShot(fallClip);
        }

        // ── Power Box Events ───────────────────────────────────────────────────────

        private void OnPowerBoxActivated(PowerBoxInteractable box)
        {
            Debug.Log($"[{name}] Power box activated — queued for investigation.");
            TargetPowerBox              = box;
            _blackboard.PowerBoxActive  = true;
            _blackboard.TargetPowerBox  = box;
        }

        private void OnPowerBoxFixed(PowerBoxInteractable box)
        {
            if (TargetPowerBox == box)
            {
                TargetPowerBox              = null;
                _blackboard.PowerBoxActive  = false;
                _blackboard.TargetPowerBox  = null;
            }
        }

        // ── Waypoint Generation ────────────────────────────────────────────────────

        private void GenerateWaypoints()
        {
            GameObject parent = new GameObject($"{gameObject.name}_Waypoints");
            parent.transform.position = transform.position;
            patrolWaypoints = new Transform[autoWaypointCount];
            float angleStep = 360f / autoWaypointCount;

            for (int i = 0; i < autoWaypointCount; i++)
            {
                float   angle = i * angleStep * Mathf.Deg2Rad;
                Vector3 pos   = transform.position + new Vector3(
                    Mathf.Cos(angle) * waypointRadius, 0f, Mathf.Sin(angle) * waypointRadius);

                GameObject wp = new GameObject($"Waypoint_{i + 1}");
                wp.transform.position = pos;
                wp.transform.parent   = parent.transform;
                patrolWaypoints[i]    = wp.transform;
            }
        }

        // ── Debug Gizmos ───────────────────────────────────────────────────────────

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, detectionRange);

            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, attackRange);

            Gizmos.color = new Color(1f, 0.5f, 0f, 0.5f);
            Gizmos.DrawWireSphere(transform.position, hearingRange);

            if (showFieldOfView)                   DrawFOVGizmo();
            if (showDetectionRaycast && _player != null) DrawLOSGizmo();

            if (patrolWaypoints != null && patrolWaypoints.Length > 0)
            {
                Gizmos.color = Color.green;
                foreach (Transform wp in patrolWaypoints)
                    if (wp != null) Gizmos.DrawWireSphere(wp.position, 0.5f);

                Gizmos.color = Color.cyan;
                for (int i = 0; i < patrolWaypoints.Length; i++)
                {
                    if (patrolWaypoints[i] == null) continue;
                    Transform next = patrolWaypoints[(i + 1) % patrolWaypoints.Length];
                    if (next != null) Gizmos.DrawLine(patrolWaypoints[i].position, next.position);
                }
            }
        }

        private void DrawFOVGizmo()
        {
            float   half  = fieldOfViewAngle / 2f;
            Vector3 left  = Quaternion.Euler(0, -half, 0) * transform.forward * detectionRange;
            Vector3 right = Quaternion.Euler(0,  half, 0) * transform.forward * detectionRange;

            Gizmos.color = new Color(0, 1, 0, 0.2f);
            Gizmos.DrawLine(transform.position, transform.position + left);
            Gizmos.DrawLine(transform.position, transform.position + right);

            Vector3 prev = transform.position + left;
            for (int i = 1; i <= 20; i++)
            {
                float   a = -half + fieldOfViewAngle * i / 20f;
                Vector3 p = transform.position + Quaternion.Euler(0, a, 0) * transform.forward * detectionRange;
                Gizmos.DrawLine(prev, p);
                prev = p;
            }
        }

        private void DrawLOSGizmo()
        {
            Vector3 eye    = transform.position + Vector3.up * npcEyeHeight;
            Vector3 target = _player.position   + Vector3.up * playerCenterHeight;
            Vector3 dir    = target - eye;
            bool occluded  = Physics.Raycast(eye, dir.normalized, dir.magnitude, obstacleLayerMask);
            Gizmos.color   = occluded ? Color.red : Color.green;
            Gizmos.DrawLine(eye, target);
            Gizmos.DrawWireSphere(target, 0.2f);
        }
    }
}
