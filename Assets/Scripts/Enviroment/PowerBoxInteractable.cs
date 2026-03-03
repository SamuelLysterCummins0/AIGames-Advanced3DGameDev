using System;
using UnityEngine;

namespace Semester2
{
    /// <summary>
    /// Handles player interaction with a power box.
    /// When activated, starts electrical sparks and notifies all listeners (NPC, lights)
    /// via static events - no direct references needed (Observer Pattern).
    /// </summary>
    public class PowerBoxInteractable : MonoBehaviour
    {
        [Header("Interaction")]
        [Tooltip("How close the player must be to see the interact prompt")]
        [SerializeField] private float interactDistance = 2.5f;

        [Tooltip("The UI GameObject showing 'Distraction [E] Interact' - assign a world/screen space canvas")]
        [SerializeField] private GameObject interactPromptUI;

        [Tooltip("Key the player presses to interact")]
        [SerializeField] private KeyCode interactKey = KeyCode.E;

        [Header("NPC Interaction Point")]
        [Tooltip("Optional: place an empty child GameObject here and position it where the NPC should stand " +
                 "to fix the box (in front of the panel). If left empty the code uses 1.5 m along the box's " +
                 "forward axis as a fallback.")]
        [SerializeField] private Transform interactPoint;

        [Header("Spark Particles")]
        [Tooltip("Assign your electricity spark ParticleSystem here - must be set manually")]
        [SerializeField] private ParticleSystem sparkParticles;

        // ── Static Events (Observer Pattern) ─────────────────────────────────────
        // Any system (NPC, lights, audio) can subscribe without knowing about each other.
        public static event Action<PowerBoxInteractable> OnPowerBoxActivated;
        public static event Action<PowerBoxInteractable> OnPowerBoxFixed;

        // ── State ─────────────────────────────────────────────────────────────────
        private bool isActivated = false;
        private Transform player;

        /// <summary>World transform of the box itself (used for facing direction on fix).</summary>
        public Transform BoxTransform => transform;

        /// <summary>True while the power box is sparking and needs fixing.</summary>
        public bool IsActivated => isActivated;

        /// <summary>
        /// Returns the world position the NPC should navigate to before fixing this box.
        /// Uses the explicit interactPoint Transform if assigned in the Inspector;
        /// otherwise falls back to 1.5 m along the box's local forward axis so the NPC
        /// always approaches the front face of the panel, not the side or back.
        /// </summary>
        public Vector3 GetNpcStandPosition()
        {
            if (interactPoint != null)
                return interactPoint.position;

            // Fallback: step out 1.5 m from the box centre in its facing direction.
            // Make sure the box's blue (forward/Z) arrow in the Scene view points away
            // from the wall so this lands in the open space in front of the panel.
            return transform.position + transform.forward * 1.5f;
        }

        /// <summary>
        /// Returns the world-space horizontal direction the NPC should face when
        /// standing at GetNpcStandPosition() and looking at the box.
        ///
        /// When interactPoint is assigned we derive the direction from interactPoint
        /// toward the box pivot — this is independent of the box model's local axes
        /// so it works regardless of how the asset is oriented in the scene.
        /// Fallback uses -transform.forward for boxes that have no interactPoint.
        /// </summary>
        public Vector3 GetNpcFacingDirection()
        {
            if (interactPoint != null)
            {
                Vector3 dir = transform.position - interactPoint.position;
                dir.y = 0f;
                if (dir.sqrMagnitude > 0.001f)
                    return dir.normalized;
            }

            // Fallback: -forward is the direction from the stand offset back to the box.
            Vector3 fallback = -transform.forward;
            fallback.y = 0f;
            return fallback.sqrMagnitude > 0.001f ? fallback.normalized : -Vector3.forward;
        }

        void Start()
        {
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null)
                player = playerObj.transform;

            if (interactPromptUI != null)
                interactPromptUI.SetActive(false);

            if (sparkParticles == null)
            {
                Debug.LogWarning($"[PowerBox] {name} has no Spark Particles assigned - assign one in the Inspector!");
            }
            else
            {
                // Force-stop in case Play On Awake is still checked in the Inspector
                sparkParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        void Update()
        {
            // Once activated, hide prompt and skip interaction checks
            if (isActivated || player == null)
            {
                if (interactPromptUI != null) interactPromptUI.SetActive(false);
                return;
            }

            float dist = Vector3.Distance(transform.position, player.position);
            bool inRange = dist <= interactDistance;

            if (interactPromptUI != null)
                interactPromptUI.SetActive(inRange);

            if (inRange && Input.GetKeyDown(interactKey))
                Activate();
        }

        private void Activate()
        {
            isActivated = true;

            if (interactPromptUI != null)
                interactPromptUI.SetActive(false);

            if (sparkParticles != null)
                sparkParticles.Play();

            Debug.Log($"[PowerBox] {name} activated - sparking!");
            OnPowerBoxActivated?.Invoke(this);
        }

        /// <summary>
        /// Called by NpcInvestigateState when the NPC finishes the fix animation.
        /// Stops sparks and fires the Fixed event so lights can restore.
        /// </summary>
        public void FixPowerBox()
        {
            if (!isActivated) return;

            isActivated = false;

            if (sparkParticles != null)
                sparkParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            Debug.Log($"[PowerBox] {name} fixed - sparks stopped!");
            OnPowerBoxFixed?.Invoke(this);
        }

    }
}
