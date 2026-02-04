using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Semester2
{
    /// <summary>
    /// Simple Dissolve Effect Controller
    /// 
    /// Controls dissolve shader animation using MaterialPropertyBlock for performance.
    /// Supports play, pause, reset, and replay for demonstration.
    /// 
    /// </summary>
    [RequireComponent(typeof(Renderer))]
    public class DissolveEffect : MonoBehaviour
    {
        [Header("Dissolve Animation")]
        [Tooltip("Duration of dissolve effect in seconds")]
        [SerializeField] private float duration = 2f;

        [Tooltip("Start automatically when game starts")]
        [SerializeField] private bool playOnStart = false;

        [Tooltip("Keyboard key to trigger dissolve (None = disabled)")]
        [SerializeField] private KeyCode triggerKey = KeyCode.Space;

        [Header("Edge Appearance")]
        [Tooltip("Width of the burn edge")]
        [Range(0f, 0.5f)]
        [SerializeField] private float edgeWidth = 0.1f;

        [Tooltip("Inner edge color (hot)")]
        [SerializeField] private Color innerColor = new Color(1f, 0.8f, 0f); // Yellow

        [Tooltip("Outer edge color (cool)")]
        [SerializeField] private Color outerColor = new Color(1f, 0.2f, 0f); // Orange

        [Tooltip("Edge glow intensity")]
        [SerializeField] private float emissionStrength = 3f;

        // Component references
        private Renderer rend;
        private MaterialPropertyBlock propBlock;

        // Animation state
        private bool isPlaying = false;
        private float currentAmount = 0f;
        private float timer = 0f;

        // Shader property IDs 
        private static readonly int AmountID = Shader.PropertyToID("_Dissolution_Amount");
        private static readonly int EdgeWidthID = Shader.PropertyToID("_Edge_Width");
        private static readonly int InnerColorID = Shader.PropertyToID("_Dissolution_Inner_Colour");
        private static readonly int OuterColorID = Shader.PropertyToID("_Dissolution_Outer_Colour");
        private static readonly int EmissionID = Shader.PropertyToID("_Dissolution_Emission_Strenght");

        void Start()
        {
            // Setup
            rend = GetComponent<Renderer>();
            propBlock = new MaterialPropertyBlock();

            // Apply initial settings
            UpdateEdgeProperties();
            SetDissolveAmount(0f);

            // Auto-play if enabled
            if (playOnStart)
            {
                Play();
            }
        }

        void Update()
        {
            // Keyboard trigger
            if (triggerKey != KeyCode.None && Input.GetKeyDown(triggerKey))
            {
                Play();
            }

            // Update animation
            if (isPlaying)
            {
                timer += Time.deltaTime;
                float progress = Mathf.Clamp01(timer / duration);
                SetDissolveAmount(progress);

                // Stop when complete
                if (progress >= 1f)
                {
                    isPlaying = false;
                }
            }
        }

        /// <summary>
        /// Start the dissolve animation
        /// </summary>
        public void Play()
        {
            isPlaying = true;
            timer = 0f;
            Debug.Log($"[{name}] Dissolve started");
        }

        /// <summary>
        /// Reset to start (fully visible)
        /// </summary>
        public void Reset()
        {
            isPlaying = false;
            timer = 0f;
            SetDissolveAmount(0f);
            Debug.Log($"[{name}] Dissolve reset");
        }

        /// <summary>
        /// Replay - reset then play
        /// </summary>
        public void Replay()
        {
            Reset();
            Play();
        }

        /// <summary>
        /// Pause animation
        /// </summary>
        public void Pause()
        {
            isPlaying = false;
        }

        /// <summary>
        /// Set dissolve amount directly (for manual control)
        /// </summary>
        public void SetDissolveAmount(float amount)
        {
            currentAmount = Mathf.Clamp01(amount);
            propBlock.SetFloat(AmountID, currentAmount);
            rend.SetPropertyBlock(propBlock);
        }

        /// <summary>
        /// Update edge effect properties
        /// </summary>
        private void UpdateEdgeProperties()
        {
            propBlock.SetFloat(EdgeWidthID, edgeWidth);
            propBlock.SetColor(InnerColorID, innerColor);
            propBlock.SetColor(OuterColorID, outerColor);
            propBlock.SetFloat(EmissionID, emissionStrength);
            rend.SetPropertyBlock(propBlock);
        }

        /// <summary>
        /// Update edge properties when changed in Inspector
        /// </summary>
        void OnValidate()
        {
            if (Application.isPlaying && propBlock != null)
            {
                UpdateEdgeProperties();
            }
        }

        // Public read-only properties
        public float CurrentAmount => currentAmount;
        public bool IsPlaying => isPlaying;
    }

 
    //Inspector buttons 
#if UNITY_EDITOR
    /// <summary>
    /// Adds control buttons to DissolveEffect Inspector for easy testing.
    /// </summary>
    [CustomEditor(typeof(DissolveEffect))]
    public class DissolveEffectEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            DissolveEffect effect = (DissolveEffect)target;

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Controls", EditorStyles.boldLabel);

            if (Application.isPlaying)
            {
                // Control buttons
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Play", GUILayout.Height(30)))
                    effect.Play();
                if (GUILayout.Button("Pause", GUILayout.Height(30)))
                    effect.Pause();
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Reset", GUILayout.Height(30)))
                    effect.Reset();
                if (GUILayout.Button("Replay", GUILayout.Height(30)))
                    effect.Replay();
                EditorGUILayout.EndHorizontal();

                // Status
                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField($"Amount: {effect.CurrentAmount:F2} ({effect.CurrentAmount * 100:F0}%)");
                EditorGUILayout.LabelField($"Playing: {effect.IsPlaying}");
            }
            else
            {
                EditorGUILayout.HelpBox("Enter Play Mode to test controls", MessageType.Info);
            }
        }
    }
#endif
}