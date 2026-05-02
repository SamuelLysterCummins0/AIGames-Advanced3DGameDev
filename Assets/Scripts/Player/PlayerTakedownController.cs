using System.Collections;
using System.Reflection;
using UnityEngine;
using StarterAssets;

namespace Semester2
{
    /// <summary>
    /// Handles stealth takedown / assassination mechanics.
    ///
    /// Conditions for a takedown:
    ///   - Player is within takedownRange of an NPC
    ///   - Player is behind the NPC (dot product check on NPC's forward vs direction to player)
    ///   - NPC is not currently detecting the player (Blackboard.PlayerVisible and PlayerHeard are false)
    ///   - No takedown is already in progress
    ///
    /// On E press:
    ///   1. Player controls are locked (FirstPersonController + look input disabled)
    ///   2. Player is snapped to a position directly behind the NPC
    ///   3. Camera smoothly rotates to look forward, then tilts down as the NPC falls
    ///   4. PlayerBody GameObject is activated so the stab animation is visible
    ///   5. Takedown trigger fires on the player Animator; NpcController.StartTakedown() fires on NPC
    ///   6. After animationDuration seconds, PlayerBody is hidden and controls are restored
    /// </summary>
    public class PlayerTakedownController : MonoBehaviour
    {
        [Header("Takedown Detection")]
        [Tooltip("Maximum distance from the NPC to allow a takedown.")]
        [SerializeField] private float takedownRange = 2f;

        [Tooltip("Dot product threshold for 'behind'. 0 = side, -1 = directly behind. -0.3 = rear 120 arc.")]
        [SerializeField] private float behindDotThreshold = -0.3f;

        [Tooltip("How far behind the NPC (metres) to snap the player before animation starts.")]
        [SerializeField] private float snapOffset = 0.7f;

        [Header("Animation")]
        [Tooltip("Total takedown sequence length (seconds). Controls are locked for this entire duration. " +
                 "Set this to match your NPC collapse animation length.")]
        [SerializeField] private float animationDuration = 2.5f;

        [Tooltip("Seconds after the takedown starts before the player's hands are hidden. " +
                 "Set this to your stab animation clip length so the hands disappear right as the stab finishes.")]
        [SerializeField] private float playerBodyHideDuration = 1.2f;

        [Tooltip("Trigger parameter name on the player Animator controller.")]
        [SerializeField] private string takedownTrigger = "Takedown";

        [Tooltip("Key to press to execute the takedown when prompted.")]
        [SerializeField] private KeyCode takedownKey = KeyCode.E;

        [Header("Camera")]
        [Tooltip("PlayerCameraRoot — the CinemachineCameraTarget child of PlayerCapsule.")]
        [SerializeField] private Transform cameraRoot;

        [Tooltip("How long the camera takes to face forward at the start of the takedown.")]
        [SerializeField] private float cameraAlignDuration = 0.35f;

        [Tooltip("How long (seconds) the camera tracks the NPC's head as it falls after the stab connects.")]
        [SerializeField] private float headTrackDuration = 1.5f;

        [Tooltip("How long the camera takes to return to level before controls are restored.")]
        [SerializeField] private float headTrackReturnDuration = 0.5f;

        [Header("Audio")]
        [Tooltip("Clip that plays when the stab connects. Timed with stabSoundDelay.")]
        [SerializeField] private AudioClip stabSound;

        [Tooltip("Seconds after the takedown starts before the stab hit sound plays.")]
        [SerializeField] private float stabSoundDelay = 0.8f;

        [Header("References")]
        [Tooltip("The player character FBX child GO. Normally inactive — activated only during takedown.")]
        [SerializeField] private GameObject playerBody;

        [Tooltip("Animator on the PlayerBody child.")]
        [SerializeField] private Animator playerAnimator;

        [Tooltip("UI element that shows 'E - Takedown'.")]
        [SerializeField] private GameObject promptUI;

        // Cached components
        private FirstPersonController             _fpsController;
        private StarterAssets.StarterAssetsInputs _inputs;
        private CharacterController               _characterController;
        private AudioSource[]                     _allPlayerAudio;
        private AudioSource                       _stabAudio;
        private PlayerAudioEmitter                _playerAudioEmitter;

        private bool          _isAnimating  = false;
        private NpcController _nearbyTarget = null;

        /// <summary>True while a takedown animation is in progress.</summary>
        public bool IsAnimating => _isAnimating;

        void Start()
        {
            // Search the whole player hierarchy — in some prefab layouts these components
            // sit on a different GameObject from PlayerTakedownController, and a same-GO
            // GetComponent silently returns null, leaving the player able to move during
            // their own takedown animation.
            _fpsController       = GetComponent<FirstPersonController>()
                                ?? transform.root.GetComponentInChildren<FirstPersonController>(true);
            _inputs              = GetComponent<StarterAssets.StarterAssetsInputs>()
                                ?? transform.root.GetComponentInChildren<StarterAssets.StarterAssetsInputs>(true);
            _characterController = transform.root.GetComponentInChildren<CharacterController>(true);

            // Cache every AudioSource in the entire player hierarchy (root + all children) so
            // we can silence them all during a takedown. Using transform.root ensures we catch
            // footstep sources on child objects (e.g. PlayerBody, weapon) as well as parent ones.
            _allPlayerAudio     = transform.root.GetComponentsInChildren<AudioSource>(includeInactive: true);
            _stabAudio          = GetComponent<AudioSource>();
            _playerAudioEmitter = transform.root.GetComponentInChildren<PlayerAudioEmitter>();

            if (_fpsController == null)
                Debug.LogWarning("[PlayerTakedownController] FirstPersonController not found on this GameObject.");
            if (_inputs == null)
                Debug.LogWarning("[PlayerTakedownController] StarterAssetsInputs not found on this GameObject.");
            if (playerBody == null)
                Debug.LogWarning("[PlayerTakedownController] PlayerBody not assigned in Inspector.");
            if (promptUI == null)
                Debug.LogWarning("[PlayerTakedownController] PromptUI not assigned in Inspector.");
            if (cameraRoot == null)
                Debug.LogWarning("[PlayerTakedownController] CameraRoot not assigned — camera won't move during takedown.");

            // Defer by one frame: Fusion's Spawned() fires before Unity's Start(), so
            // NetworkPlayerSetup.Spawned() enables _localOnlyObjects (which may contain
            // promptUI) AFTER this Start() call. Waiting one frame guarantees we hide
            // the prompt last, after the hierarchy is fully activated.
            StartCoroutine(HidePromptAfterSpawn());
        }

        private IEnumerator HidePromptAfterSpawn()
        {
            yield return null;
            if (promptUI != null) promptUI.SetActive(false);
        }

        void OnDisable()
        {
            if (promptUI != null) promptUI.SetActive(false);
        }

        void Update()
        {
            if (_isAnimating) return;

            // Takedown is only available after the weapon has been picked up
            bool weaponReady = GameManager.Instance != null
                            && GameManager.Instance.State == GameState.TakedownGuards;

            if (!weaponReady)
            {
                if (promptUI != null) promptUI.SetActive(false);
                return;
            }

            _nearbyTarget = FindTakedownTarget();

            if (promptUI != null)
                promptUI.SetActive(_nearbyTarget != null);

            if (_nearbyTarget != null && Input.GetKeyDown(takedownKey))
            {
                Debug.Log("[PlayerTakedownController] <color=magenta>E pressed — starting takedown.</color>");
                StartCoroutine(ExecuteTakedown(_nearbyTarget));
            }
        }

        /// <summary>
        /// Scans nearby colliders for a valid takedown target.
        /// Returns the first NPC that is within range, behind the player, and not currently detecting them.
        /// </summary>
        private NpcController FindTakedownTarget()
        {
            Collider[] hits = Physics.OverlapSphere(transform.position, takedownRange);

            foreach (Collider hit in hits)
            {
                NpcController npc = hit.GetComponentInParent<NpcController>();
                if (npc == null) continue;

                // Skip NPCs that can currently see or hear the player — stealth only
                Vector3 dummy;
                if (npc.CanSeePlayer() || npc.CanHearPlayer(out dummy))
                    continue;

                Vector3 toPlayer = (transform.position - npc.transform.position).normalized;
                float   dot      = Vector3.Dot(npc.transform.forward, toPlayer);

                if (dot > behindDotThreshold)
                    continue;

                return npc;
            }

            return null;
        }

        private IEnumerator ExecuteTakedown(NpcController npc)
        {
            _isAnimating = true;
            Debug.Log($"[PlayerTakedownController] <color=magenta>Takedown on {npc.name}</color>");

            try
            {
                // ── 1. Lock controls + hide prompt immediately ────────────────────────────
                if (_fpsController != null) _fpsController.enabled = false;
                if (_inputs != null) { _inputs.move = Vector2.zero; _inputs.look = Vector2.zero; }
                // Belt-and-braces: even if FPC reference grab failed, disabling the CC
                // guarantees no Move() calls reach the capsule during the animation.
                if (_characterController != null) _characterController.enabled = false;
                if (promptUI != null) promptUI.SetActive(false);
                // Disable footstep emitter so it can't fire new PlayOneShot calls mid-animation
                if (_playerAudioEmitter != null) _playerAudioEmitter.enabled = false;

                // Stop any audio already playing in the player hierarchy
                if (_allPlayerAudio != null)
                    foreach (var src in _allPlayerAudio)
                        if (src != null) src.Stop();

                // ── 2. Snap position ──────────────────────────────────────────────────────
                Vector3 snapPos = npc.transform.position - npc.transform.forward * snapOffset;
                snapPos.y          = transform.position.y;
                transform.position = snapPos;
                transform.rotation = Quaternion.Euler(0f, npc.transform.eulerAngles.y, 0f);

                // ── 3. Camera: align forward, then track the NPC's head as it falls ────
                StartCoroutine(SmoothAlignCamera(npc.transform.eulerAngles.y, cameraAlignDuration));

                // GetBoneTransform works on any Humanoid-rigged avatar (all Mixamo characters).
                // If the NPC has no Humanoid rig, headBone is null and TrackNpcHead exits safely.
                Animator npcAnim = npc.GetComponentInChildren<Animator>();
                Transform headBone = npcAnim != null
                    ? npcAnim.GetBoneTransform(HumanBodyBones.Head)
                    : null;
                StartCoroutine(TrackNpcHead(headBone));

                // ── 4. Show body + trigger animations ────────────────────────────────────
                if (playerBody != null)
                {
                    playerBody.SetActive(true);
                    // Hide the hands as soon as the stab clip finishes, independently of
                    // animationDuration (which keeps controls locked until the NPC fully collapses).
                    StartCoroutine(HidePlayerBodyAfter(playerBodyHideDuration));
                }
                if (playerAnimator != null) playerAnimator.SetTrigger(takedownTrigger);

                // Network the kill — every peer runs StartTakedown locally, so the NPC
                // actually dies for the host too, not just on the killer's screen.
                npc.RPC_StartTakedown(animationDuration);

                // ── 5. Stab sound at the moment the blade connects ────────────────────────
                if (stabSound != null && _stabAudio != null)
                    StartCoroutine(PlayDelayed(_stabAudio, stabSound, stabSoundDelay));
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[PlayerTakedownController] Takedown setup error: {e.Message}");
            }

            yield return new WaitForSeconds(animationDuration);

            // PlayerBody is hidden by HidePlayerBodyAfter (fired above). Force-deactivate here
            // as a safety net in case playerBodyHideDuration was set longer than animationDuration.
            if (playerBody != null) playerBody.SetActive(false);
            if (promptUI   != null) promptUI.SetActive(false);
            if (_characterController != null) _characterController.enabled = true;
            if (_fpsController != null) _fpsController.enabled = true;
            if (_playerAudioEmitter != null) _playerAudioEmitter.enabled = true;

            _isAnimating  = false;
            _nearbyTarget = null;

            Debug.Log("[PlayerTakedownController] <color=magenta>Takedown complete — controls restored.</color>");
        }

        private IEnumerator HidePlayerBodyAfter(float delay)
        {
            yield return new WaitForSeconds(delay);
            if (playerBody != null) playerBody.SetActive(false);
        }

        private IEnumerator PlayDelayed(AudioSource src, AudioClip clip, float delay)
        {
            yield return new WaitForSeconds(delay);
            src.PlayOneShot(clip);
        }

        /// <summary>
        /// Smoothly faces the camera forward at the start of the takedown, then syncs
        /// the FPC's internal yaw so controls return without a snap.
        /// </summary>
        private IEnumerator SmoothAlignCamera(float targetYawDeg, float duration)
        {
            if (cameraRoot == null) yield break;

            Quaternion start = cameraRoot.localRotation;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, elapsed / duration);
                cameraRoot.localRotation = Quaternion.Slerp(start, Quaternion.identity, t);
                yield return null;
            }
            cameraRoot.localRotation = Quaternion.identity;

            SyncFpcYaw(targetYawDeg);
        }

        /// <summary>
        /// After the stab connects, smoothly rotates the camera to track the NPC's
        /// head bone as it falls. Requires the NPC to use a Humanoid avatar (all
        /// Mixamo characters qualify). Returns to level before controls are restored.
        /// </summary>
        private IEnumerator TrackNpcHead(Transform headBone)
        {
            if (cameraRoot == null || headBone == null) yield break;

            // Wait until the blade actually hits before we start tracking
            yield return new WaitForSeconds(stabSoundDelay);

            // Phase 1: continuously look toward the NPC head as it falls
            float elapsed = 0f;
            while (elapsed < headTrackDuration)
            {
                elapsed += Time.deltaTime;

                Vector3 toHead = headBone.position - cameraRoot.position;
                if (toHead.sqrMagnitude > 0.001f)
                {
                    // Convert world direction to local space of cameraRoot's parent (PlayerCapsule)
                    Vector3 localDir = cameraRoot.parent.InverseTransformDirection(toHead.normalized);

                    // Pitch: negative X = look down in Unity's camera convention
                    float hDist      = Mathf.Sqrt(localDir.x * localDir.x + localDir.z * localDir.z);
                    float targetPitch = -Mathf.Atan2(localDir.y, hDist) * Mathf.Rad2Deg;
                    targetPitch      = Mathf.Clamp(targetPitch, -70f, 20f);

                    Quaternion targetRot = Quaternion.Euler(targetPitch, 0f, 0f);

                    // Smooth pursuit — 6 deg/s-ish feel, framerate independent
                    cameraRoot.localRotation = Quaternion.Slerp(
                        cameraRoot.localRotation, targetRot, Time.deltaTime * 6f);
                }

                yield return null;
            }

            // Brief hold so the player sees the NPC on the ground
            yield return new WaitForSeconds(0.2f);

            // Phase 2: return to level before controls come back
            Quaternion fromRot = cameraRoot.localRotation;
            elapsed = 0f;
            while (elapsed < headTrackReturnDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, elapsed / headTrackReturnDuration);
                cameraRoot.localRotation = Quaternion.Slerp(fromRot, Quaternion.identity, t);
                yield return null;
            }
            cameraRoot.localRotation = Quaternion.identity;

            // Sync FPC pitch to 0 so controls return without a camera snap
            SyncFpcYaw(cameraRoot.parent.eulerAngles.y);
        }

        /// <summary>
        /// Updates FirstPersonController's internal yaw and pitch via reflection so that
        /// when the FPC re-enables it doesn't snap the camera back to the pre-takedown angle.
        /// </summary>
        private void SyncFpcYaw(float yawDeg)
        {
            if (_fpsController == null) return;
            try
            {
                var flags = BindingFlags.NonPublic | BindingFlags.Instance;
                typeof(FirstPersonController)
                    .GetField("_cinemachineTargetYaw", flags)
                    ?.SetValue(_fpsController, yawDeg);
                typeof(FirstPersonController)
                    .GetField("_cinemachineTargetPitch", flags)
                    ?.SetValue(_fpsController, 0f);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[PlayerTakedownController] Could not sync FPC camera: {e.Message}");
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0f, 0.5f, 0.3f);
            Gizmos.DrawWireSphere(transform.position, takedownRange);
        }
    }
}
