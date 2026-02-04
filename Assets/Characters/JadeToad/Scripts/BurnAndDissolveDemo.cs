using UnityEngine;

namespace Semester2
{
    /// <summary>
    /// Demonstrates the dissolve effect by automatically animating the _Dissolution_Amount parameter
    /// back and forth between 0 and 1 over time.
    /// </summary>
    [RequireComponent(typeof(Renderer))]
    public class BurnAndDissolveDemo : MonoBehaviour
    {
        [Header("Animation Settings")]
        [Tooltip("Time in seconds for a complete cycle (0?1?0)")]
        [Range(1f, 10f)]
        public float cycleDuration = 4f;

        [Tooltip("Animation curve to control the dissolve progression")]
        public AnimationCurve dissolveCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Tooltip("Delay before starting the animation")]
        [Range(0f, 5f)]
        public float startDelay = 0f;

        [Tooltip("Pause duration at fully dissolved state")]
        [Range(0f, 2f)]
        public float pauseAtComplete = 0.5f;

        [Tooltip("Pause duration at fully materialized state")]
        [Range(0f, 2f)]
        public float pauseAtStart = 0.5f;

        [Header("Debug")]
        [Tooltip("Show current dissolution value in console")]
        public bool showDebugInfo = false;

        private Material materialInstance;
        private Renderer meshRenderer;
        private float currentTime;

        private const string DISSOLUTION_PROPERTY = "_Dissolution_Amount";

        private void Start()
        {
            // Get the renderer and create a material instance
            meshRenderer = GetComponent<Renderer>();
            
            if (meshRenderer == null)
            {
                Debug.LogError($"No Renderer found on {gameObject.name}. BurnAndDissolveDemo requires a Renderer component.");
                enabled = false;
                return;
            }

            // Create material instance to avoid modifying the shared material
            materialInstance = meshRenderer.material;

            // Check if material has the dissolution property
            if (!materialInstance.HasProperty(DISSOLUTION_PROPERTY))
            {
                Debug.LogWarning($"Material on {gameObject.name} does not have a '{DISSOLUTION_PROPERTY}' property. " +
                               "Make sure you're using the Dissolving URP Lit Shader.");
            }

            // Apply start delay
            currentTime = -startDelay;

            // Initialize at fully materialized
            SetDissolutionAmount(0f);
        }

        private void Update()
        {
            AnimateDissolve();
        }

        private void AnimateDissolve()
        {
            currentTime += Time.deltaTime;

            // Skip if still in start delay
            if (currentTime < 0f)
            {
                return;
            }

            // Calculate the total cycle time including pauses
            float totalCycleTime = cycleDuration + pauseAtComplete + pauseAtStart;
            float normalizedTime = (currentTime % totalCycleTime) / totalCycleTime;

            float dissolutionValue;

            // First half: 0 ? 1 (dissolving)
            if (normalizedTime < 0.5f)
            {
                float dissolvePhaseTime = cycleDuration * 0.5f + pauseAtStart;
                float phaseNormalizedTime = (normalizedTime * totalCycleTime) / dissolvePhaseTime;

                if (phaseNormalizedTime <= pauseAtStart / dissolvePhaseTime)
                {
                    // Pause at start (fully materialized)
                    dissolutionValue = 0f;
                }
                else
                {
                    // Dissolving
                    float t = (phaseNormalizedTime - (pauseAtStart / dissolvePhaseTime)) / (1f - (pauseAtStart / dissolvePhaseTime));
                    dissolutionValue = dissolveCurve.Evaluate(t);
                }
            }
            // Second half: 1 ? 0 (materializing)
            else
            {
                float materializePhaseTime = cycleDuration * 0.5f + pauseAtComplete;
                float phaseNormalizedTime = ((normalizedTime - 0.5f) * totalCycleTime) / materializePhaseTime;

                if (phaseNormalizedTime <= pauseAtComplete / materializePhaseTime)
                {
                    // Pause at complete (fully dissolved)
                    dissolutionValue = 1f;
                }
                else
                {
                    // Materializing
                    float t = (phaseNormalizedTime - (pauseAtComplete / materializePhaseTime)) / (1f - (pauseAtComplete / materializePhaseTime));
                    dissolutionValue = dissolveCurve.Evaluate(1f - t);
                }
            }

            SetDissolutionAmount(dissolutionValue);
        }

        private void SetDissolutionAmount(float value)
        {
            if (materialInstance != null && materialInstance.HasProperty(DISSOLUTION_PROPERTY))
            {
                materialInstance.SetFloat(DISSOLUTION_PROPERTY, value);

                if (showDebugInfo)
                {
                    Debug.Log($"[{Time.time:F2}] Dissolution Amount: {value:F3}");
                }
            }
        }

        private void OnDestroy()
        {
            // Clean up the material instance to prevent memory leaks
            if (materialInstance != null)
            {
                Destroy(materialInstance);
            }
        }

        #region Public Methods

        /// <summary>
        /// Set dissolution to a specific value programmatically
        /// </summary>
        /// <param name="value">Dissolution amount (0 = materialized, 1 = dissolved)</param>
        public void SetDissolution(float value)
        {
            SetDissolutionAmount(Mathf.Clamp01(value));
        }

        /// <summary>
        /// Reset animation to start
        /// </summary>
        public void ResetAnimation()
        {
            currentTime = -startDelay;
            SetDissolutionAmount(0f);
        }

        #endregion
    }
}
