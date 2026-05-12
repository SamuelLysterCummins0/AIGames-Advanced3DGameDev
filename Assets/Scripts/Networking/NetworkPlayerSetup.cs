using Fusion;
using StarterAssets;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Semester2
{
    /// <summary>
    /// Runs on each spawned player. Enables the camera and input only for the
    /// local player — remote players have their camera and input components
    /// disabled to prevent Cinemachine Brain hijack and PlayerInput device conflicts.
    /// </summary>
    public class NetworkPlayerSetup : NetworkBehaviour
    {
        [SerializeField] private GameObject _cameraRoot;          // MainCamera (Camera + CinemachineBrain)
        [SerializeField] private GameObject _virtualCamera;        // PlayerFollowCamera (CinemachineVirtualCamera)
        [SerializeField] private GameObject _localOnlyObjects;     // HUD, crosshair etc if on prefab

        private FirstPersonController _fpc;
#if ENABLE_INPUT_SYSTEM
        private PlayerInput _playerInput;
#endif
        private Transform           _capsule; // PlayerCapsule child — this is what actually moves
        private PlayerAudioEmitter  _audioEmitter;

        // Smoothed remote-player position. SyncedPosition only updates on Fusion ticks
        // (30 Hz default) but we render at 60+ FPS, so without smoothing the visible
        // capsule on remote peers steps in chunks. SmoothDamp converges in ~50 ms which
        // is barely perceptible while still tracking close to the live position.
        private Vector3             _smoothVelocity;
        private const float         SMOOTH_TIME       = 0.05f;
        private const float         TELEPORT_DISTANCE = 5f; // beyond this, snap rather than smear

        /// <summary>
        /// World position of the PlayerCapsule, synced every Fusion tick by the local client.
        /// The NetworkObject root never moves (CharacterController.Move() drives the capsule
        /// child), so NetworkTransform on the root always sends the spawn position. This
        /// [Networked] variable carries the real in-game position to all peers without
        /// touching any transforms, which avoids CharacterController physics conflicts.
        /// NPCs read this to get the correct world position of a remote player on the host.
        /// </summary>
        [Networked] public Vector3 SyncedPosition { get; set; }

        /// <summary>
        /// Player noise level, broadcast every Fusion tick by the input-authority client.
        /// PlayerAudioEmitter computes velocity from CharacterController.velocity, which is
        /// only valid on the local client (remote CCs never receive Move() calls). Without
        /// this synced value the host can't hear Player 2 because P2's emitter reports 0.
        /// PlayerAudioEmitter on remote players reads from here instead of computing locally.
        /// </summary>
        [Networked] public float SyncedNoiseLevel { get; set; }

        private void Awake()
        {
            _fpc = GetComponentInChildren<FirstPersonController>(true);
#if ENABLE_INPUT_SYSTEM
            _playerInput = GetComponentInChildren<PlayerInput>(true);
#endif
            var cc = GetComponentInChildren<CharacterController>(true);
            _capsule      = cc != null ? cc.transform : null;
            _audioEmitter = GetComponentInChildren<PlayerAudioEmitter>(true);
            // Disable both camera objects immediately so neither the Camera component nor
            // the VirtualCamera registers with Cinemachine Brain before Spawned() runs.
            if (_cameraRoot != null)    _cameraRoot.SetActive(false);
            if (_virtualCamera != null) _virtualCamera.SetActive(false);
            if (_localOnlyObjects != null) _localOnlyObjects.SetActive(false);

            // Disable movement and input immediately for all instances.
            // Remote players must never run FirstPersonController or hold a PlayerInput
            // device — two active PlayerInput components on the same device triggers
            // Unity's auto-switch and steals input from the local player, freezing both.
            if (_fpc != null)         _fpc.enabled = false;
#if ENABLE_INPUT_SYSTEM
            if (_playerInput != null) _playerInput.enabled = false;
#endif
        }

        public override void Spawned()
        {
            // CharacterController blocks transform.position changes while enabled.
            // Disable it briefly so Fusion can place the player at the spawn position.
            var cc = GetComponentInChildren<CharacterController>();
            if (cc != null)
            {
                cc.enabled = false;
                transform.position = transform.position; // flush pending position
                cc.enabled = true;
            }

            // Seed SyncedPosition immediately so NPCs have a valid world position from the
            // first frame — before FixedUpdateNetwork fires the first tick from this client.
            if (HasInputAuthority && _capsule != null)
                SyncedPosition = _capsule.position;

            if (HasInputAuthority)
            {
                // Re-enable both camera objects for this client's own player.
                if (_cameraRoot != null)
                    _cameraRoot.SetActive(true);
                else
                    Debug.LogWarning("[NetworkPlayerSetup] _cameraRoot not assigned — drag MainCamera into this field on the player prefab.");

                if (_virtualCamera != null)
                    _virtualCamera.SetActive(true);
                else
                    Debug.LogWarning("[NetworkPlayerSetup] _virtualCamera not assigned — drag PlayerFollowCamera into this field on the player prefab.");

                if (_localOnlyObjects != null)
                    _localOnlyObjects.SetActive(true);

                // Re-enable movement and input only for the local player.
                if (_fpc != null)         _fpc.enabled = true;
#if ENABLE_INPUT_SYSTEM
                if (_playerInput != null) _playerInput.enabled = true;
#endif
                Debug.Log("[NetworkPlayerSetup] <color=cyan>Local player camera and input activated.</color>");
            }
            else
            {
                Debug.Log("[NetworkPlayerSetup] <color=grey>Remote player — camera and input disabled.</color>");
            }
        }

        /// <summary>
        /// Publish the capsule's current world position into SyncedPosition each Fusion tick.
        /// No transforms are modified — this is a pure value write, so the CharacterController
        /// is never disturbed and movement remains smooth on both clients.
        /// </summary>
        public override void FixedUpdateNetwork()
        {
            if (!HasInputAuthority) return;

            if (_capsule != null)
                SyncedPosition = _capsule.position;

            // Broadcast our locally-computed noise level so remote peers (the host viewing
            // P2, etc.) can read a real value — their local emitter would report 0 because
            // CharacterController.velocity is only meaningful on the owning client.
            if (_audioEmitter != null)
                SyncedNoiseLevel = _audioEmitter.LocalNoiseLevel;
        }

        /// <summary>
        /// Drive the visible capsule from SyncedPosition on remote peers. The
        /// CharacterController only runs on the input-authority client — on every
        /// other peer the capsule transform never updates by itself, so without this
        /// remote players would visually freeze at spawn. Runs in Update so the visible
        /// movement matches the local render frame rate rather than the Fusion tick.
        /// </summary>
        private void Update()
        {
            // Gate on Object.IsValid — [Networked] fields throw if read before Spawned().
            // Update fires from the first frame the GameObject is enabled, which is
            // before Fusion has finished spawning the network object on this client.
            if (Object == null || !Object.IsValid) return;
            if (HasInputAuthority) return;          // local player drives their own movement
            if (_capsule == null) return;
            if (SyncedPosition == Vector3.zero) return; // not yet seeded by the owning client

            // Snap if the gap is large (respawn, teleport etc.) — otherwise SmoothDamp
            // will smear the capsule across the level for half a second.
            if (Vector3.Distance(_capsule.position, SyncedPosition) > TELEPORT_DISTANCE)
            {
                _capsule.position = SyncedPosition;
                _smoothVelocity   = Vector3.zero;
                return;
            }

            // Normal play — soft-track the synced position. Removes the per-frame chunky
            // movement that comes from reading a 30 Hz value at 60+ FPS.
            _capsule.position = Vector3.SmoothDamp(
                _capsule.position,
                SyncedPosition,
                ref _smoothVelocity,
                SMOOTH_TIME);
        }
    }
}
