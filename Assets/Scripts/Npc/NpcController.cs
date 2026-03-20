using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using Unity.Profiling;

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
        [SerializeField] private float walkNoiseLevel       = 0.3f;
        [SerializeField] private float runNoiseLevel        = 1.0f;
        [SerializeField] private float minNoiseThreshold    = 0.2f;
        [SerializeField] private bool  useOcclusionForSound = true;
        [SerializeField] private float soundOcclusionMult   = 0.5f;
        [Tooltip("Scales effective noise before the threshold check. 1 = full sensitivity. " +
                 "Lower this on NPCs that are on a different floor so they don't react to sounds from below.")]
        [Range(0f, 1f)]
        [SerializeField] private float hearingNoiseSensitivity = 1f;
        [Tooltip("Scales vertical distance in the hearing check, flattening the detection volume into an oblate spheroid. " +
                 "Higher = harder to hear through floors. Effective vertical range = Hearing Range / this value. " +
                 "Default 3 gives ~3.3 m vertical reach when Hearing Range is 10.")]
        [SerializeField] private float verticalHearingPenalty = 3f;

        [Header("NPC Movement Settings")]
        [SerializeField] private float walkSpeed                = 3.5f;
        [SerializeField] private float runSpeed                 = 7f;
        [SerializeField] private float waypointReachedThreshold = 0.5f;

        [Header("NPC Behavior Settings")]
        [SerializeField] private float idleDuration        = 2f;
        [SerializeField] private float attackCooldown      = 1.5f;
        [SerializeField] private float attackRotationSpeed = 10f;
        [Tooltip("Fraction of Attack Range at which the NPC holds its position. 0.7 = 70% of Attack Range.")]
        [SerializeField] private float shootingDistanceRatio = 0.7f;
        [Tooltip("Seconds of sustained LOS loss before the NPC stops firing in the Attack state.")]
        [SerializeField] private float losLostThreshold = 1f;
        [Tooltip("Damage dealt to the player on each attack hit.")]
        [SerializeField] private float attackDamage = 34f;

        [Header("Patrol Settings")]
        [SerializeField] private Transform[] patrolWaypoints;
        [SerializeField] private bool        autoGenerateWaypoints = false;
        [SerializeField] private float       waypointRadius        = 10f;
        [SerializeField] private int         autoWaypointCount     = 4;
        [Tooltip("Seconds to wait before the Behaviour Tree activates. " +
                 "Set a non-zero value on one of two mirrored-waypoint NPCs to prevent patrol sync.")]
        [SerializeField] private float patrolStartDelay = 0f;

        [Header("Waypoint Idle Settings")]
        [SerializeField] private bool  enableWaypointIdleStop = true;
        [Range(0f, 1f)]
        [SerializeField] private float waypointIdleChance     = 0.7f;
        [SerializeField] private float waypointIdleDuration   = 2f;

        [Header("Investigate State Settings")]
        [SerializeField] private float investigateLookDuration = 7f;
        [SerializeField] private float investigateFixDuration  = 12f;

        [Header("Search State Settings")]
        [SerializeField] private float searchRadius        = 8f;
        [SerializeField] private int   searchPointCount    = 4;
        [SerializeField] private float maxSearchDuration   = 15f;
        [SerializeField] private float searchPauseDuration = 1.5f;

        [Header("NPC Reinforcement Settings")]
        [Tooltip("Ellipsoid distance (same vertical flattening as hearing) within which this NPC receives " +
                 "another NPC's chase alert and heads to the area as reinforcement.")]
        [SerializeField] private float reinforceRange = 15f;
        [Tooltip("While chasing the player, how often (seconds) this NPC re-broadcasts its alert " +
                 "so nearby NPCs keep getting updated last-known positions as the player moves.")]
        [SerializeField] private float reinforceAlertInterval = 2f;

        [Header("Death")]
        [SerializeField] private string      npcDeathTrigger   = "Death"; // must match Animator parameter
        [SerializeField] private AudioSource deathAudioSource;
        [SerializeField] private AudioClip   fallClip;
        [SerializeField] private float       fallSoundDelay = 1.8f;

        [Header("Performance")]
        [Tooltip("How often the BT ticks in seconds. 0.1 = 10 times per second. Lower = more responsive but more CPU.")]
        [SerializeField] private float btTickInterval = 0.1f;

        [Header("Debug Visualization")]
        [SerializeField] private bool showFieldOfView      = true;
        [SerializeField] private bool showDetectionRaycast = true;

        // ── Public API ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Fired when this NPC visually spots the player.
        /// Other NpcControllers subscribe to this and check if they are close enough
        /// to respond as reinforcements. Uses the same ellipsoid distance shape as hearing
        /// so upper-floor NPCs are excluded automatically.
        /// </summary>
        public static event System.Action<NpcController, Vector3> OnNpcAlerting;

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
        /// Set by AssignPowerBoxRepair, cleared by BtActionInvestigate on repair.
        /// </summary>
        public PowerBoxInteractable TargetPowerBox { get; set; }

        /// <summary>True after StartTakedown() is called. PowerBoxInteractable uses this to detect handoff.</summary>
        public bool IsDead => _isDead;

        /// <summary>
        /// True when this NPC can be assigned to investigate a power box.
        /// Returns false if dead, BT not yet active, or currently chasing/attacking the player.
        /// NPCs in Search or Patrol are eligible — they will break off their current activity.
        /// </summary>
        public bool IsEligibleForPowerBoxRepair
        {
            get
            {
                if (_isDead || !_btActive) return false;
                string node = _blackboard?.ActiveNodeName ?? "None";
                return node != "Chase" && node != "Attack";
            }
        }

        // ── Private fields ─────────────────────────────────────────────────────────

        private NpcBlackboard       _blackboard;
        private BtNode              _btRoot;
        private BtCooldown          _postChaseSearchCooldown; // kept so we can reset it on new audio cues
        private BtActionInvestigate _investigateAction; // kept so LateUpdate can call ReapplyRotation
        private bool                _isDead   = false;
        private bool                _btActive = false; // false until patrolStartDelay expires
        private float               _lastAlertBroadcastTime  = -999f;
        private float               _reinforcementEndTime    = -1f;   // expires when alerts stop arriving
        private Transform           _player;
        private float               _btTickTimer = 0f;

        // Profiler marker so the BT cost shows up clearly in the Unity Profiler
        private static readonly ProfilerMarker s_btTickMarker =
            new ProfilerMarker("NPC.BehaviourTree.Tick");
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
                WalkNoiseLevel           = walkNoiseLevel,
                RunNoiseLevel            = runNoiseLevel,
                MinNoiseThreshold        = minNoiseThreshold,
                UseOcclusionForSound     = useOcclusionForSound,
                SoundOcclusionMultiplier = soundOcclusionMult,
                HearingNoiseSensitivity  = hearingNoiseSensitivity,
                VerticalHearingPenalty   = verticalHearingPenalty,
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
                InvestigateFixDuration   = investigateFixDuration,
                SearchRadius             = searchRadius,
                SearchPointCount         = searchPointCount,
                MaxSearchDuration        = maxSearchDuration,
                SearchPauseDuration      = searchPauseDuration,
                ShootingDistanceRatio    = shootingDistanceRatio,
                LosLostThreshold         = losLostThreshold,
                AttackDamage             = attackDamage
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

            // Delay BT activation so two mirrored-waypoint NPCs never start in sync.
            if (patrolStartDelay > 0f)
                StartCoroutine(ActivateBtAfterDelay(patrolStartDelay));
            else
                _btActive = true;
        }

        void Update()
        {
            if (_isDead || !_btActive) return;

            // Perception runs every frame so the NPC reacts quickly to the player
            UpdatePerception();

            // BT only ticks at btTickInterval to avoid spending the full frame budget on AI
            _btTickTimer += Time.deltaTime;
            if (_btTickTimer < btTickInterval) return;
            _btTickTimer = 0f;

            using (s_btTickMarker.Auto())
            {
                _btRoot?.Tick();
            }
        }

        private void UpdatePerception()
        {
            _blackboard.DistanceToPlayer = DistanceToPlayer;

            bool wasVisible = _blackboard.PlayerVisible; // capture before overwriting
            bool canSee     = CanSeePlayer();
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

                // Alert nearby NPCs: on first spot and periodically while chasing,
                // so reinforcing NPCs keep getting an updated last-known position.
                if (!wasVisible || Time.time - _lastAlertBroadcastTime >= reinforceAlertInterval)
                {
                    OnNpcAlerting?.Invoke(this, _player.position);
                    _lastAlertBroadcastTime = Time.time;
                }
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

            // Active while alerts keep arriving AND this NPC hasn't spotted the player itself.
            // Once this NPC gains sight, the Threat branch takes over so the flag is irrelevant.
            _blackboard.ReinforcementTracking = Time.time < _reinforcementEndTime
                                                && !_blackboard.PlayerVisible;

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
            // Tell BtNode how large a time gap counts as a re-entry (interruption).
            // Must be larger than btTickInterval but smaller than two tick intervals.
            BtNode.ReEntryGap = btTickInterval * 1.5f;

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
            // BtTimeout aborts the threat response if the player stays visible for over
            // 30 s without the NPC landing a kill — acts as a fail-safe so Chase never
            // runs indefinitely if the player becomes unreachable (e.g. climbed somewhere)
            var threatBranch = new BtSequence(new BtNode[]
            {
                new BtCheckPlayerVisible(_blackboard),
                new BtTimeout(threatResponse, 30f)
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
            // BtTimeout stops Investigate running forever if the box is never reachable.
            var powerBoxBranch = new BtSequence(new BtNode[]
            {
                new BtCheckPowerBoxActive(_blackboard),
                new BtTimeout(_investigateAction, 60f)
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
            // Note: OnPowerBoxActivated is NOT subscribed here — NPCs no longer self-assign
            // when the box fires. PowerBoxInteractable selects a single responder and calls
            // AssignPowerBoxRepair() directly on it.
            PowerBoxInteractable.OnPowerBoxFixed += OnPowerBoxFixed;
            OnNpcAlerting                        += OnReceiveNpcAlert;
        }

        void OnDisable()
        {
            PowerBoxInteractable.OnPowerBoxFixed -= OnPowerBoxFixed;
            OnNpcAlerting                        -= OnReceiveNpcAlert;
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

            // Build a flattened (oblate spheroid) hearing volume: full range horizontally,
            // reduced range vertically. This stops NPCs on upper floors hearing the player
            // through a floor/ceiling gap even when straight-line distance is short.
            // Effective vertical radius = hearingRange / verticalHearingPenalty.
            Vector3 toPlayer  = _player.position - transform.position;
            float   hDist     = Mathf.Sqrt(toPlayer.x * toPlayer.x + toPlayer.z * toPlayer.z);
            float   vDist     = Mathf.Abs(toPlayer.y);
            float   vScaled   = vDist * Config.VerticalHearingPenalty;
            float   adjustedDist = Mathf.Sqrt(hDist * hDist + vScaled * vScaled);

            float dist = DistanceToPlayer; // true 3D distance — kept for approxDist NavMesh snap only
            if (adjustedDist > hearingRange) return false;

            float noise = _audioEmitter.CurrentNoiseLevel;
            if (noise < minNoiseThreshold) return false;

            float effective = noise * Config.HearingNoiseSensitivity * (1f - adjustedDist / hearingRange);

            if (useOcclusionForSound)
            {
                Vector3 eye       = transform.position + Vector3.up * npcEyeHeight;
                Vector3 playerPos = _player.position   + Vector3.up * playerCenterHeight;
                Vector3 dir       = playerPos - eye;
                if (Physics.Raycast(eye, dir.normalized, dir.magnitude, obstacleLayerMask))
                    effective *= soundOcclusionMult;
            }

            if (effective < minNoiseThreshold) return false;

            float approxDist = Mathf.Clamp(dist * Random.Range(0.8f, 1.1f), 1f, hearingRange);
            float spreadDeg  = Mathf.Lerp(5f, 25f, adjustedDist / hearingRange);
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

        private IEnumerator ActivateBtAfterDelay(float delay)
        {
            yield return new WaitForSeconds(delay);
            _btActive = true;
            Debug.Log($"[{name}] <color=green>BT activated after {delay}s start delay.</color>");
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

        // ── Reinforcement Alert ────────────────────────────────────────────────────

        /// <summary>
        /// Received when another NPC visually spots the player.
        /// If that NPC is within reinforceRange (ellipsoid, same shape as hearing),
        /// this NPC updates its last-known position and heads to the area as reinforcement.
        /// It doesn't magically see the player — its own perception handles Chase/Attack.
        /// </summary>
        private void OnReceiveNpcAlert(NpcController alerter, Vector3 alertPosition)
        {
            if (alerter == this || _isDead || !_btActive) return;

            // Ellipsoid distance to the alerting NPC — mirrors the hearing volume shape
            // so NPCs on different floors are excluded without any extra configuration.
            Vector3 toAlerter = alerter.transform.position - transform.position;
            float   hDist     = Mathf.Sqrt(toAlerter.x * toAlerter.x + toAlerter.z * toAlerter.z);
            float   vDist     = Mathf.Abs(toAlerter.y);
            float   vScaled   = vDist * Config.VerticalHearingPenalty;
            float   alertDist = Mathf.Sqrt(hDist * hDist + vScaled * vScaled);

            if (alertDist > reinforceRange) return;

            // Give this NPC a last-known position so the Search branch fires.
            // Do NOT set LkpFromChase — this NPC didn't visually chase the player,
            // so it should still be allowed to investigate the PowerBox after searching.
            _blackboard.LastKnownPlayerPosition = alertPosition;
            _blackboard.HasLastKnownPosition    = true;

            // Reset the post-chase search cooldown so the search fires immediately.
            _postChaseSearchCooldown?.ResetCooldown();

            // Keep ReinforcementTracking active for slightly longer than the broadcast interval
            // so a slightly late alert doesn't cause a 1-frame gap that resets to fan-search.
            _reinforcementEndTime = Time.time + reinforceAlertInterval + 0.5f;

            Debug.Log($"[{name}] <color=orange>Reinforcement alert from {alerter.name} — moving to support.</color>");
        }

        // ── Power Box Events ───────────────────────────────────────────────────────

        /// <summary>
        /// Called by PowerBoxInteractable when this NPC has been selected as the sole responder.
        /// Sets the blackboard so the PowerBox branch in the BT fires on the next tick.
        /// </summary>
        public void AssignPowerBoxRepair(PowerBoxInteractable box)
        {
            TargetPowerBox             = box;
            _blackboard.PowerBoxActive = true;
            _blackboard.TargetPowerBox = box;
            Debug.Log($"[{name}] <color=orange>Assigned to fix power box.</color>");
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

            // Hearing volume is an oblate spheroid — draw horizontal disc (full range)
            // and two side discs (effective vertical range = hearingRange / penalty).
            float vertHearRange = (verticalHearingPenalty > 0f) ? hearingRange / verticalHearingPenalty : hearingRange;
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.5f);
            DrawGizmoCircle(transform.position, hearingRange,    Vector3.up);      // horizontal disc
            DrawGizmoCircle(transform.position, vertHearRange,   Vector3.right);   // side view XY
            DrawGizmoCircle(transform.position, vertHearRange,   Vector3.forward); // side view ZY

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

        /// <summary>
        /// Draws a wire circle in the plane defined by the given normal.
        /// Used to visualise the oblate-spheroid hearing volume in the Scene view.
        /// </summary>
        private void DrawGizmoCircle(Vector3 centre, float radius, Vector3 normal)
        {
            // Build two orthogonal axes that lie on the circle plane
            Vector3 tangent   = Vector3.Cross(normal, normal == Vector3.up ? Vector3.forward : Vector3.up).normalized;
            Vector3 bitangent = Vector3.Cross(normal, tangent);

            const int steps = 24;
            Vector3 prev = centre + tangent * radius;
            for (int i = 1; i <= steps; i++)
            {
                float   angle = i * Mathf.PI * 2f / steps;
                Vector3 next  = centre + (tangent * Mathf.Cos(angle) + bitangent * Mathf.Sin(angle)) * radius;
                Gizmos.DrawLine(prev, next);
                prev = next;
            }
        }
    }
}
