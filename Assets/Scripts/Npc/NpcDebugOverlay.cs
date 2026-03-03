using UnityEngine;
using UnityEngine.AI;

namespace Semester2
{
    /// <summary>
    /// In-Game Debug Overlay for NPC AI
    /// 
    /// Displays real-time debug information during gameplay:
    /// - Current FSM state
    /// - Current target (player reference)
    /// - Current navigation destination
    /// - Distance to player
    /// - Navigation path visualisation (drawn as a line in the game world)
    /// 
    /// Toggle the overlay on/off with the F1 key.
    /// 
    /// Attach this to the same GameObject as NpcController.
    /// </summary>
    [RequireComponent(typeof(NpcController))]
    public class NpcDebugOverlay : MonoBehaviour
    {
        [Header("Debug Overlay Settings")]
        [Tooltip("Toggle key to show/hide the debug overlay")]
        [SerializeField] private KeyCode toggleKey = KeyCode.F1;

        [Tooltip("Show the debug overlay on start")]
        [SerializeField] private bool showOverlay = true;

        [Tooltip("Show navigation path line in game view")]
        [SerializeField] private bool showNavPath = true;

        [Tooltip("Color of the navigation path line")]
        [SerializeField] private Color navPathColor = Color.cyan;

        [Tooltip("Color of the state label above NPC head")]
        [SerializeField] private Color stateTextColor = Color.white;

        [Header("World Space Label")]
        [Tooltip("Height offset above NPC for the floating state label")]
        [SerializeField] private float labelHeightOffset = 2.5f;

        // Cached references
        private NavMeshAgent navMeshAgent;
        private MinimalisticFSM fsm;
        private Transform player;
        private LineRenderer pathLineRenderer;

        // GUI style (created once)
        private GUIStyle boxStyle;
        private GUIStyle labelStyle;
        private GUIStyle headerStyle;
        private bool stylesInitialized = false;

        void Start()
        {
            navMeshAgent = GetComponent<NavMeshAgent>();

            // Find player reference
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null)
            {
                player = playerObj.transform;
            }

            // Setup line renderer for nav path visualisation
            SetupPathLineRenderer();
        }

        /// <summary>
        /// Creates a LineRenderer component to draw the NavMesh path in the game view.
        /// </summary>
        private void SetupPathLineRenderer()
        {
            // Add a child object for the line renderer so it doesn't clutter the NPC
            GameObject lineObj = new GameObject("DebugNavPath");
            lineObj.transform.SetParent(transform);
            lineObj.transform.localPosition = Vector3.zero;

            pathLineRenderer = lineObj.AddComponent<LineRenderer>();
            pathLineRenderer.startWidth = 0.1f;
            pathLineRenderer.endWidth = 0.1f;
            pathLineRenderer.material = new Material(Shader.Find("Sprites/Default"));
            pathLineRenderer.startColor = navPathColor;
            pathLineRenderer.endColor = navPathColor;
            pathLineRenderer.positionCount = 0;
        }

        void Update()
        {
            // Toggle overlay with key
            if (Input.GetKeyDown(toggleKey))
            {
                showOverlay = !showOverlay;
                Debug.Log($"[{gameObject.name}] Debug overlay: {(showOverlay ? "ON" : "OFF")}");
            }

            // Update nav path visualisation
            if (showOverlay && showNavPath)
            {
                UpdateNavPathLine();
            }
            else if (pathLineRenderer != null)
            {
                pathLineRenderer.positionCount = 0;
            }
        }

        /// <summary>
        /// Updates the LineRenderer to show the current NavMesh path.
        /// </summary>
        private void UpdateNavPathLine()
        {
            if (navMeshAgent == null || pathLineRenderer == null)
                return;

            if (!navMeshAgent.hasPath || navMeshAgent.path.corners.Length == 0)
            {
                pathLineRenderer.positionCount = 0;
                return;
            }

            // Draw the path corners as a line
            Vector3[] corners = navMeshAgent.path.corners;

            // Offset slightly above ground so the line is visible
            pathLineRenderer.positionCount = corners.Length;
            for (int i = 0; i < corners.Length; i++)
            {
                pathLineRenderer.SetPosition(i, corners[i] + Vector3.up * 0.1f);
            }
        }

        /// <summary>
        /// Gets the FSM reference from NpcController using reflection-free approach.
        /// Called lazily since FSM is created in NpcController.Start().
        /// </summary>
        private string GetCurrentStateName()
        {
            // Access FSM through the NpcController
            // We check the NpcController's FSM via a public accessor we'll add
            NpcController controller = GetComponent<NpcController>();
            if (controller != null && controller.CurrentStateName != null)
            {
                return controller.CurrentStateName;
            }
            return "Unknown";
        }

        /// <summary>
        /// Draw the on-screen debug panel using OnGUI.
        /// Simple and reliable - works without Canvas setup.
        /// </summary>
        void OnGUI()
        {
            if (!showOverlay) return;

            // Initialize styles once
            if (!stylesInitialized)
            {
                InitStyles();
            }

            // Draw the debug panel in the top-left corner
            float panelWidth = 300f;
            float panelHeight = 160f;
            float padding = 10f;

            Rect panelRect = new Rect(padding, padding, panelWidth, panelHeight);

            // Background box
            GUI.Box(panelRect, "", boxStyle);

            // Content area
            float x = panelRect.x + 10f;
            float y = panelRect.y + 8f;
            float lineHeight = 22f;

            // Header
            GUI.Label(new Rect(x, y, panelWidth - 20f, lineHeight), $"NPC Debug: {gameObject.name}", headerStyle);
            y += lineHeight + 4f;

            // Current State
            string stateName = GetCurrentStateName();
            Color stateColor = GetStateColor(stateName);
            string stateHex = ColorUtility.ToHtmlStringRGB(stateColor);
            GUI.Label(new Rect(x, y, panelWidth - 20f, lineHeight),
                $"<color=#{stateHex}>State: {stateName}</color>", labelStyle);
            y += lineHeight;

            // Target
            string targetName = player != null ? player.gameObject.name : "None";
            float distToPlayer = player != null ? Vector3.Distance(transform.position, player.position) : 0f;
            GUI.Label(new Rect(x, y, panelWidth - 20f, lineHeight),
                $"Target: {targetName} (Dist: {distToPlayer:F1}m)", labelStyle);
            y += lineHeight;

            // Destination
            string destText = "None";
            if (navMeshAgent != null && navMeshAgent.hasPath)
            {
                Vector3 dest = navMeshAgent.destination;
                float destDist = navMeshAgent.remainingDistance;
                destText = $"({dest.x:F1}, {dest.z:F1}) - {destDist:F1}m away";
            }
            GUI.Label(new Rect(x, y, panelWidth - 20f, lineHeight),
                $"Destination: {destText}", labelStyle);
            y += lineHeight;

            // Agent speed
            float currentSpeed = navMeshAgent != null ? navMeshAgent.velocity.magnitude : 0f;
            GUI.Label(new Rect(x, y, panelWidth - 20f, lineHeight),
                $"Speed: {currentSpeed:F1} | Stopped: {(navMeshAgent != null ? navMeshAgent.isStopped.ToString() : "N/A")}", labelStyle);
            y += lineHeight;

            // Toggle hint
            GUI.Label(new Rect(x, y, panelWidth - 20f, lineHeight),
                $"<color=#888888>Press {toggleKey} to toggle</color>", labelStyle);
        }

        /// <summary>
        /// Initialise GUI styles for the debug panel.
        /// </summary>
        private void InitStyles()
        {
            boxStyle = new GUIStyle(GUI.skin.box);
            boxStyle.normal.background = MakeTexture(2, 2, new Color(0f, 0f, 0f, 0.75f));

            labelStyle = new GUIStyle(GUI.skin.label);
            labelStyle.fontSize = 13;
            labelStyle.normal.textColor = Color.white;
            labelStyle.richText = true;

            headerStyle = new GUIStyle(GUI.skin.label);
            headerStyle.fontSize = 14;
            headerStyle.fontStyle = FontStyle.Bold;
            headerStyle.normal.textColor = Color.white;

            stylesInitialized = true;
        }

        /// <summary>
        /// Creates a solid color texture for GUI backgrounds.
        /// </summary>
        private Texture2D MakeTexture(int width, int height, Color color)
        {
            Color[] pixels = new Color[width * height];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = color;
            }
            Texture2D texture = new Texture2D(width, height);
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        /// <summary>
        /// Returns a color matching the current state for easy visual identification.
        /// </summary>
        private Color GetStateColor(string stateName)
        {
            switch (stateName)
            {
                case "Idle": return Color.white;
                case "Patrol": return Color.green;
                case "Chase": return Color.yellow;
                case "Attack": return Color.red;
                default: return Color.grey;
            }
        }

        /// <summary>
        /// Draw the state label floating above the NPC in world space.
        /// Visible in the Game view so you can see NPC state at a glance.
        /// </summary>
        void OnDrawGizmos()
        {
            if (!showOverlay || !Application.isPlaying) return;

            // Draw state label above NPC head (visible in Scene view)
            string stateName = GetCurrentStateName();

#if UNITY_EDITOR
            GUIStyle worldStyle = new GUIStyle();
            worldStyle.normal.textColor = GetStateColor(stateName);
            worldStyle.fontSize = 14;
            worldStyle.fontStyle = FontStyle.Bold;
            worldStyle.alignment = TextAnchor.MiddleCenter;

            Vector3 labelPos = transform.position + Vector3.up * labelHeightOffset;
            UnityEditor.Handles.Label(labelPos, $"[{stateName}]", worldStyle);
#endif
        }
    }
}