using UnityEngine;
using Fusion;

namespace Semester2
{
    /// <summary>
    /// Player Audio Emitter Component
    /// Tracks and broadcasts the player's noise level based on movement and actions.
    /// 
    /// This component calculates how much noise the player is making in real-time,
    /// which can be detected by NPCs with audio detection capabilities.
    /// 
    /// Noise level is determined by:
    /// - Movement speed (walking vs running vs stationary)
    /// - Actions (can be extended for jumping, attacking, etc.)
    /// 
    /// NPCs can use this information to detect the player even when outside their field of view.
    /// 
    /// IMPORTANT: This component also plays actual 3D spatial audio that players can hear.
    /// Configure AudioSource settings for realistic sound falloff.
    /// 
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class PlayerAudioEmitter : MonoBehaviour
    {
        [Header("Audio Emission Settings")]
        [Tooltip("Noise level when completely still")]
        [SerializeField] private float idleNoiseLevel = 0.0f;

        [Tooltip("Noise level when walking (slow movement)")]
        [SerializeField] private float walkNoiseLevel = 0.3f;

        [Tooltip("Noise level when running (fast movement)")]
        [SerializeField] private float runNoiseLevel = 1.0f;

        [Tooltip("Movement speed threshold to be considered 'walking'")]
        [SerializeField] private float walkSpeedThreshold = 0.5f;

        [Tooltip("Movement speed threshold to be considered 'running'")]
        [SerializeField] private float runSpeedThreshold = 3.0f;

        [Header("Smoothing Settings")]
        [Tooltip("How quickly the noise level changes (higher = faster response)")]
        [SerializeField] private float noiseTransitionSpeed = 5f;

        [Header("3D Audio Settings")]
        [Tooltip("Enable actual footstep audio playback")]
        [SerializeField] private bool playFootstepSounds = true;

        [Tooltip("Footstep sound when walking")]
        [SerializeField] private AudioClip walkFootstepClip;

        [Tooltip("Footstep sound when running (usually louder/faster)")]
        [SerializeField] private AudioClip runFootstepClip;

        [Tooltip("Time between footstep sounds when walking")]
        [SerializeField] private float walkFootstepInterval = 0.5f;

        [Tooltip("Time between footstep sounds when running")]
        [SerializeField] private float runFootstepInterval = 0.3f;

        [Tooltip("Volume multiplier for walk sounds (0-1)")]
        [SerializeField] private float walkVolumeMultiplier = 0.5f;

        [Tooltip("Volume multiplier for run sounds (0-1)")]
        [SerializeField] private float runVolumeMultiplier = 0.8f;

        [Tooltip("Minimum movement speed to play footsteps")]
        [SerializeField] private float minSpeedForFootsteps = 0.1f;

        [Header("Spatial Audio Configuration")]
        [Tooltip("3D spatial blend (0 = 2D, 1 = full 3D)")]
        [SerializeField] private float spatialBlend = 1.0f;

        [Tooltip("Minimum distance for full volume")]
        [SerializeField] private float minDistance = 1f;

        [Tooltip("Maximum distance where sound can be heard")]
        [SerializeField] private float maxDistance = 15f;

        [Tooltip("Volume rolloff mode for 3D sound")]
        [SerializeField] private AudioRolloffMode rolloffMode = AudioRolloffMode.Logarithmic;

        [Header("Debug Visualization")]
        [Tooltip("Show noise level in scene view as a sphere")]
        [SerializeField] private bool showNoiseGizmo = true;

        [Tooltip("Color of the noise visualization sphere")]
        [SerializeField] private Color noiseGizmoColor = new Color(1f, 0.5f, 0f, 0.3f);

        // Cached components (auto-detects what's available)
        private CharacterController characterController;
        private Rigidbody rb;
        private UnityEngine.AI.NavMeshAgent navMeshAgent;
        private AudioSource audioSource;

        // Track last position for manual velocity calculation if needed
        private Vector3 lastPosition;

        // Current noise level (read by NPCs)
        private float currentNoiseLevel = 0f;

        // Target noise level (what we're transitioning to)
        private float targetNoiseLevel = 0f;

        // Footstep timing
        private float footstepTimer = 0f;
        private MovementState previousMovementState = MovementState.Idle;

        // Cached parent NetworkObject + sync component, used to swap CurrentNoiseLevel
        // between the locally-computed value (input authority) and the networked value
        // (remote peers). Without this, remote players' CCs report zero velocity and
        // NPCs can't hear them.
        private NetworkObject      _no;
        private NetworkPlayerSetup _nps;

        /// <summary>
        /// Locally-computed noise level. NetworkPlayerSetup reads this on the input
        /// authority and broadcasts it via SyncedNoiseLevel so remote peers can hear it.
        /// </summary>
        public float LocalNoiseLevel => currentNoiseLevel;

        /// <summary>
        /// Noise level used by NPCs for hearing checks.
        /// On the input-authority client, returns the locally-calculated value.
        /// On remote peers, returns the [Networked] value broadcast by the owning client —
        /// CharacterController.velocity is only meaningful on the owning client.
        /// Falls back to the local value when not networked at all.
        /// </summary>
        public float CurrentNoiseLevel
        {
            get
            {
                if (_no != null && _no.IsValid && !_no.HasInputAuthority && _nps != null)
                    return _nps.SyncedNoiseLevel;
                return currentNoiseLevel;
            }
        }

        /// <summary>
        /// Gets the current movement state of the player.
        /// </summary>
        public MovementState CurrentMovementState { get; private set; }

        /// <summary>
        /// Enum representing different movement states for easier debugging.
        /// </summary>
        public enum MovementState
        {
            Idle,
            Walking,
            Running
        }

        /// <summary>
        /// Initialize component references and configure AudioSource.
        /// </summary>
        void Start()
        {
            // Try to find whatever movement component is available (check self and children)
            characterController = GetComponentInChildren<CharacterController>();
            rb = GetComponentInChildren<Rigidbody>();
            navMeshAgent = GetComponentInChildren<UnityEngine.AI.NavMeshAgent>();

            // Cache the parent NetworkObject + setup so CurrentNoiseLevel can swap to
            // the networked value when this emitter belongs to a remote player.
            _no  = GetComponentInParent<NetworkObject>();
            _nps = GetComponentInParent<NetworkPlayerSetup>();

            lastPosition = transform.position;

            // Cache and configure AudioSource
            audioSource = GetComponent<AudioSource>();

            if (audioSource == null)
            {
                Debug.LogError($"[{gameObject.name}] PlayerAudioEmitter requires an AudioSource component!");
            }
            else
            {
                ConfigureAudioSource();
            }
        }

        /// <summary>
        /// Configure the AudioSource for 3D spatial audio.
        /// </summary>
        private void ConfigureAudioSource()
        {
            // Set spatial blend for 3D audio
            audioSource.spatialBlend = spatialBlend;

            // Configure distance attenuation
            audioSource.minDistance = minDistance;
            audioSource.maxDistance = maxDistance;
            audioSource.rolloffMode = rolloffMode;

            // Don't play on awake (we'll trigger sounds manually)
            audioSource.playOnAwake = false;

            // Don't loop (footsteps are individual sounds)
            audioSource.loop = false;

        }

        /// <summary>
        /// Update noise level and play footstep sounds based on player movement every frame.
        /// </summary>
        void Update()
        {
            UpdateNoiseLevel();
            UpdateFootstepSounds();
        }

        /// <summary>
        /// Calculates and updates the current noise level based on player movement speed.
        /// </summary>
        private void UpdateNoiseLevel()
        {
            // Get current velocity magnitude (speed) from whatever controller is available
            float currentSpeed = GetMovementSpeed();

            // Determine target noise level and movement state based on movement speed
            if (currentSpeed < walkSpeedThreshold)
            {
                // Player is idle/stationary
                targetNoiseLevel = idleNoiseLevel;
                CurrentMovementState = MovementState.Idle;
            }
            else if (currentSpeed < runSpeedThreshold)
            {
                // Player is walking
                targetNoiseLevel = walkNoiseLevel;
                CurrentMovementState = MovementState.Walking;
            }
            else
            {
                // Player is running
                targetNoiseLevel = runNoiseLevel;
                CurrentMovementState = MovementState.Running;
            }

            // Smoothly transition to target noise level
            currentNoiseLevel = Mathf.Lerp(currentNoiseLevel, targetNoiseLevel, Time.deltaTime * noiseTransitionSpeed);
        }

        /// <summary>
        /// Gets movement speed from whatever controller is attached.
        /// </summary>
        private float GetMovementSpeed()
        {
            // Try CharacterController first (most common for FPS)
            if (characterController != null)
            {
                return characterController.velocity.magnitude;
            }

            // Try Rigidbody
            if (rb != null)
            {
                return rb.linearVelocity.magnitude;
            }

            // Try NavMeshAgent
            if (navMeshAgent != null)
            {
                return navMeshAgent.velocity.magnitude;
            }

            // Fallback: Calculate velocity manually from position change
            float speed = (transform.position - lastPosition).magnitude / Time.deltaTime;
            lastPosition = transform.position;
            return speed;
        }

        /// <summary>
        /// Handles footstep sound playback based on movement state and timing.
        /// </summary>
        private void UpdateFootstepSounds()
        {
            if (!playFootstepSounds || audioSource == null)
            {
                return;
            }

            // Get current speed
            float currentSpeed = GetMovementSpeed();

            // Don't play footsteps if not moving
            if (currentSpeed < minSpeedForFootsteps)
            {
                footstepTimer = 0f;
                return;
            }

            // Update footstep timer
            footstepTimer += Time.deltaTime;

            // Determine footstep interval and clip based on movement state
            float currentInterval;
            AudioClip currentClip;
            float volumeMultiplier;

            if (CurrentMovementState == MovementState.Running)
            {
                currentInterval = runFootstepInterval;
                currentClip = runFootstepClip;
                volumeMultiplier = runVolumeMultiplier;
            }
            else if (CurrentMovementState == MovementState.Walking)
            {
                currentInterval = walkFootstepInterval;
                currentClip = walkFootstepClip;
                volumeMultiplier = walkVolumeMultiplier;
            }
            else
            {
                // Idle - no footsteps
                return;
            }

            // Play footstep sound if enough time has passed
            if (footstepTimer >= currentInterval)
            {
                footstepTimer = 0f;

                if (currentClip != null)
                {
                    audioSource.PlayOneShot(currentClip, volumeMultiplier);
                }
            }

            // Track state changes
            previousMovementState = CurrentMovementState;
        }

        /// <summary>
        /// Draw visual debugging gizmos in the Scene view.
        /// Shows the current noise level as a colored sphere.
        /// </summary>
        void OnDrawGizmosSelected()
        {
            if (!showNoiseGizmo)
            {
                return;
            }

            // Scale sphere radius based on noise level (0 to max hearing range)
            float visualRadius = Mathf.Lerp(0.5f, 8f, currentNoiseLevel);

            // Color intensity based on noise level
            Color gizmoColor = noiseGizmoColor;
            gizmoColor.a = Mathf.Lerp(0.1f, 0.5f, currentNoiseLevel);

            Gizmos.color = gizmoColor;
            Gizmos.DrawWireSphere(transform.position, visualRadius);
        }
    }
}