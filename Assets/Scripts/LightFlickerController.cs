using System.Collections;
using UnityEngine;

namespace Semester2
{
    /// <summary>
    /// Controls ceiling lights flickering when a power box is activated.
    /// Subscribes to PowerBoxInteractable static events - no direct reference to the power box needed.
    ///
    /// Natural flicker is achieved by:
    /// - Random per-light timing offsets so they don't all flash simultaneously
    /// - Random on/off durations within configured ranges
    /// - Occasional intensity dips (lights half-on) for variety
    /// </summary>
    public class LightFlickerController : MonoBehaviour
    {
        [Header("Lights")]
        [Tooltip("Drag all ceiling Light components here")]
        [SerializeField] private Light[] ceilingLights;

        [Header("Flicker Settings")]
        [Tooltip("Minimum time a light stays ON during a flicker cycle")]
        [SerializeField] private float minOnTime = 0.04f;

        [Tooltip("Maximum time a light stays ON during a flicker cycle")]
        [SerializeField] private float maxOnTime = 0.25f;

        [Tooltip("Minimum time a light stays OFF during a flicker cycle")]
        [SerializeField] private float minOffTime = 0.02f;

        [Tooltip("Maximum time a light stays OFF during a flicker cycle")]
        [SerializeField] private float maxOffTime = 0.12f;

        [Tooltip("Chance (0-1) each light flickers at reduced intensity instead of fully off")]
        [Range(0f, 1f)]
        [SerializeField] private float dimChance = 0.3f;

        [Tooltip("Intensity multiplier when a light dims instead of going fully off")]
        [Range(0f, 1f)]
        [SerializeField] private float dimIntensity = 0.2f;

        // Cached original intensities so we can restore them exactly
        private float[] originalIntensities;

        // One coroutine per light so they flicker independently
        private Coroutine[] flickerCoroutines;

        // ── Unity Lifecycle ───────────────────────────────────────────────────────

        void Awake()
        {
            CacheOriginalIntensities();
        }

        void OnEnable()
        {
            PowerBoxInteractable.OnPowerBoxActivated += HandlePowerBoxActivated;
            PowerBoxInteractable.OnPowerBoxFixed += HandlePowerBoxFixed;
        }

        void OnDisable()
        {
            // Always unsubscribe to prevent ghost callbacks after destruction
            PowerBoxInteractable.OnPowerBoxActivated -= HandlePowerBoxActivated;
            PowerBoxInteractable.OnPowerBoxFixed -= HandlePowerBoxFixed;
        }

        // ── Event Handlers ────────────────────────────────────────────────────────

        private void HandlePowerBoxActivated(PowerBoxInteractable box)
        {
            StartFlicker();
        }

        private void HandlePowerBoxFixed(PowerBoxInteractable box)
        {
            StopFlicker();
        }

        // ── Public API ────────────────────────────────────────────────────────────

        public void StartFlicker()
        {
            if (ceilingLights == null || ceilingLights.Length == 0) return;

            StopAllFlickers();
            flickerCoroutines = new Coroutine[ceilingLights.Length];

            for (int i = 0; i < ceilingLights.Length; i++)
            {
                if (ceilingLights[i] == null) continue;

                // Stagger start times so lights don't all flash at once - looks much more natural
                float startOffset = Random.Range(0f, 0.3f);
                flickerCoroutines[i] = StartCoroutine(FlickerLight(ceilingLights[i], i, startOffset));
            }

            Debug.Log("[LightFlicker] Flicker started");
        }

        public void StopFlicker()
        {
            StopAllFlickers();
            RestoreAllLights();
            Debug.Log("[LightFlicker] Lights restored");
        }

        // ── Private Helpers ───────────────────────────────────────────────────────

        private void CacheOriginalIntensities()
        {
            if (ceilingLights == null) return;

            originalIntensities = new float[ceilingLights.Length];
            for (int i = 0; i < ceilingLights.Length; i++)
            {
                if (ceilingLights[i] != null)
                    originalIntensities[i] = ceilingLights[i].intensity;
            }
        }

        private void StopAllFlickers()
        {
            if (flickerCoroutines == null) return;

            foreach (var coroutine in flickerCoroutines)
            {
                if (coroutine != null)
                    StopCoroutine(coroutine);
            }
        }

        private void RestoreAllLights()
        {
            if (ceilingLights == null || originalIntensities == null) return;

            for (int i = 0; i < ceilingLights.Length; i++)
            {
                if (ceilingLights[i] != null)
                {
                    ceilingLights[i].enabled = true;
                    ceilingLights[i].intensity = originalIntensities[i];
                }
            }
        }

        /// <summary>
        /// Per-light flicker coroutine. Randomly toggles the light on/off with
        /// occasional dim steps for a natural, unsteady electrical fault effect.
        /// </summary>
        private IEnumerator FlickerLight(Light light, int index, float startOffset)
        {
            // Stagger this light's start slightly
            if (startOffset > 0f)
                yield return new WaitForSeconds(startOffset);

            float originalIntensity = (originalIntensities != null && index < originalIntensities.Length)
                ? originalIntensities[index]
                : light.intensity;

            while (true)
            {
                // Lights ON phase
                light.enabled = true;

                // Occasionally dim instead of full brightness - natural flicker look
                if (Random.value < dimChance)
                    light.intensity = originalIntensity * dimIntensity;
                else
                    light.intensity = originalIntensity;

                yield return new WaitForSeconds(Random.Range(minOnTime, maxOnTime));

                // Lights OFF phase
                light.enabled = false;
                yield return new WaitForSeconds(Random.Range(minOffTime, maxOffTime));

                // Very occasionally: stay on at reduced intensity for a longer beat
                // This breaks up the rhythm so it never feels too regular
                if (Random.value < 0.15f)
                {
                    light.enabled = true;
                    light.intensity = originalIntensity * 0.4f;
                    yield return new WaitForSeconds(Random.Range(0.1f, 0.4f));
                }
            }
        }
    }
}
