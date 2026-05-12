using UnityEngine;
using UnityEngine.AI;

namespace Semester2
{
    /// <summary>
    /// In-game debug overlay for the custom BT-driven NPC.
    /// Press F1 to toggle on/off.
    ///
    /// Reads ActiveNodeName and Blackboard values directly from NpcController.
    /// </summary>
    [RequireComponent(typeof(NpcController))]
    public class NpcDebugOverlay : MonoBehaviour
    {
        [Header("Debug Overlay Settings")]
        [SerializeField] private KeyCode toggleKey         = KeyCode.F1;
        [SerializeField] private bool    showOverlay       = true;
        [SerializeField] private bool    showNavPath       = true;
        [SerializeField] private Color   navPathColor      = Color.cyan;
        [SerializeField] private float   labelHeightOffset = 2.5f;

        private NavMeshAgent      _agent;
        private NpcController     _controller;
        private PlayerAudioEmitter _audioEmitter;
        private LineRenderer      _pathLine;

        private GUIStyle _boxStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _headerStyle;
        private bool     _stylesInitialized = false;

        void Start()
        {
            _agent      = GetComponent<NavMeshAgent>();
            _controller = GetComponent<NpcController>();

            // Cache the player's audio emitter for live noise-level display
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
                _audioEmitter = player.GetComponent<PlayerAudioEmitter>();

            SetupPathLine();
        }

        private void SetupPathLine()
        {
            GameObject lineObj = new GameObject("DebugNavPath");
            lineObj.transform.SetParent(transform);
            lineObj.transform.localPosition = Vector3.zero;

            _pathLine = lineObj.AddComponent<LineRenderer>();
            _pathLine.startWidth    = 0.1f;
            _pathLine.endWidth      = 0.1f;
            _pathLine.material      = new Material(Shader.Find("Sprites/Default"));
            _pathLine.startColor    = navPathColor;
            _pathLine.endColor      = navPathColor;
            _pathLine.positionCount = 0;
        }

        void Update()
        {
            if (Input.GetKeyDown(toggleKey))
                showOverlay = !showOverlay;

            if (showOverlay && showNavPath)
                UpdateNavPath();
            else if (_pathLine != null)
                _pathLine.positionCount = 0;
        }

        private void UpdateNavPath()
        {
            if (_agent == null || _pathLine == null || !_agent.hasPath || _agent.path.corners.Length == 0)
            {
                if (_pathLine != null) _pathLine.positionCount = 0;
                return;
            }

            Vector3[] corners = _agent.path.corners;
            _pathLine.positionCount = corners.Length;
            for (int i = 0; i < corners.Length; i++)
                _pathLine.SetPosition(i, corners[i] + Vector3.up * 0.1f);
        }

        void OnGUI()
        {
            if (!showOverlay) return;
            if (!_stylesInitialized) InitStyles();

            float panelWidth  = 340f;
            float padding     = 10f;
            float lineHeight  = 22f;

            // Non-authority clients don't run the BT — show a brief informational panel
            // instead of an empty blackboard, so it's obvious why values are all zero.
            bool isAuthority = _controller != null && _controller.IsStateAuthority;
            if (!isAuthority)
            {
                Rect nonAuthRect = new Rect(padding, padding, panelWidth, lineHeight * 4f + 16f);
                GUI.Box(nonAuthRect, "", _boxStyle);
                float nx = nonAuthRect.x + 10f;
                float ny = nonAuthRect.y + 8f;
                GUI.Label(new Rect(nx, ny, panelWidth - 20f, lineHeight),
                    $"NPC Debug: {gameObject.name}", _headerStyle);
                ny += lineHeight + 4f;
                GUI.Label(new Rect(nx, ny, panelWidth - 20f, lineHeight),
                    "<color=#ffff00>Non-authority client</color>", _labelStyle);
                ny += lineHeight;
                GUI.Label(new Rect(nx, ny, panelWidth - 20f, lineHeight),
                    "BT runs on host — blackboard not synced.", _labelStyle);
                return;
            }

            float panelHeight = 340f;

            Rect panelRect = new Rect(padding, padding, panelWidth, panelHeight);
            GUI.Box(panelRect, "", _boxStyle);

            float x = panelRect.x + 10f;
            float y = panelRect.y + 8f;

            // Header
            GUI.Label(new Rect(x, y, panelWidth - 20f, lineHeight),
                $"NPC Debug: {gameObject.name}", _headerStyle);
            y += lineHeight + 4f;

            // Blackboard values — read directly from NpcController
            var bb = _controller?.Blackboard;

            // Active node — written by BT action nodes to the Blackboard each tick
            string nodeName  = bb?.ActiveNodeName ?? "None";
            Color  nodeColor = GetNodeColor(nodeName);
            string nodeHex   = ColorUtility.ToHtmlStringRGB(nodeColor);
            GUI.Label(new Rect(x, y, panelWidth - 20f, lineHeight),
                $"<color=#{nodeHex}>BT Node: {nodeName}</color>", _labelStyle);
            y += lineHeight;
            bool playerVisible  = bb?.PlayerVisible          ?? false;
            bool playerHeard    = bb?.PlayerHeard            ?? false;
            bool hasLkp         = bb?.HasLastKnownPosition   ?? false;
            bool powerBoxActive = bb?.PowerBoxActive         ?? false;
            float distToPlayer  = bb?.DistanceToPlayer       ?? 0f;

            // Perception row — shows sight AND hearing state (satisfies perception visualisation requirement)
            string visStr  = playerVisible ? "<color=#00ff00>SEEN</color>"  : "<color=#888888>---</color>";
            string heardStr = playerHeard  ? "<color=#ffff00>HEARD</color>" : "<color=#888888>---</color>";
            GUI.Label(new Rect(x, y, panelWidth - 20f, lineHeight),
                $"Sight: {visStr}   Hearing: {heardStr}", _labelStyle);
            y += lineHeight;

            string lkpStr = hasLkp ? "<color=#00ffff>YES</color>" : "<color=#888888>NO</color>";
            GUI.Label(new Rect(x, y, panelWidth - 20f, lineHeight),
                $"Distance: {distToPlayer:F1}m   Has LKP: {lkpStr}", _labelStyle);
            y += lineHeight;

            string pbStr = powerBoxActive ? "<color=#ff8800>ACTIVE</color>" : "<color=#888888>none</color>";
            GUI.Label(new Rect(x, y, panelWidth - 20f, lineHeight),
                $"PowerBox: {pbStr}", _labelStyle);
            y += lineHeight;

            bool   reinforcing = bb?.ReinforcementTracking ?? false;
            string reinStr     = reinforcing ? "<color=#ff6600>ACTIVE</color>" : "<color=#888888>---</color>";
            GUI.Label(new Rect(x, y, panelWidth - 20f, lineHeight),
                $"Reinforcing: {reinStr}", _labelStyle);
            y += lineHeight;

            // Spot timer — replaces the old suspicion bar. Fills while the player is
            // visible; fill rate scales with distance and angle. Crosses the Search
            // threshold (NPC investigates) before the Chase threshold (NPC engages).
            float spot      = bb?.SpotTimer ?? 0f;
            float spotMax   = _controller?.Config?.SpotTimerChaseThreshold ?? 3f;
            float spotPct   = spotMax > 0f ? Mathf.Clamp01(spot / spotMax) : 0f;
            Color spotCol   = Color.Lerp(Color.green, Color.red, spotPct);
            string spotHex  = ColorUtility.ToHtmlStringRGB(spotCol);
            string spotBar  = BuildBar(spotPct, 16);
            GUI.Label(new Rect(x, y, panelWidth - 20f, lineHeight),
                $"Spot: <color=#{spotHex}>{spotBar} {spot:F1}s / {spotMax:F1}s</color>", _labelStyle);
            y += lineHeight;

            // Current noise level emitted by the player (perception input, not NPC output)
            float  noise    = _audioEmitter != null ? _audioEmitter.CurrentNoiseLevel : 0f;
            string noiseBar = BuildBar(noise, 16);
            string noiseHex = ColorUtility.ToHtmlStringRGB(Color.Lerp(Color.green, Color.yellow, noise));
            GUI.Label(new Rect(x, y, panelWidth - 20f, lineHeight),
                $"Player Noise: <color=#{noiseHex}>{noiseBar} {noise:F2}</color>", _labelStyle);
            y += lineHeight;

            // Nav destination
            string destText = "None";
            if (_agent != null && _agent.hasPath)
            {
                Vector3 dest = _agent.destination;
                destText = $"({dest.x:F1}, {dest.z:F1}) — {_agent.remainingDistance:F1}m away";
            }
            GUI.Label(new Rect(x, y, panelWidth - 20f, lineHeight),
                $"Destination: {destText}", _labelStyle);
            y += lineHeight;

            float speed = _agent != null ? _agent.velocity.magnitude : 0f;
            GUI.Label(new Rect(x, y, panelWidth - 20f, lineHeight),
                $"Speed: {speed:F1}m/s", _labelStyle);
            y += lineHeight;

            GUI.Label(new Rect(x, y, panelWidth - 20f, lineHeight),
                $"<color=#888888>Press {toggleKey} to toggle</color>", _labelStyle);
        }

        /// <summary>Builds a fixed-width ASCII progress bar, e.g. "[████████░░░░░░░░]".</summary>
        private static string BuildBar(float value01, int width)
        {
            int filled = Mathf.RoundToInt(Mathf.Clamp01(value01) * width);
            System.Text.StringBuilder sb = new System.Text.StringBuilder("[", width + 2);
            for (int i = 0; i < width; i++)
                sb.Append(i < filled ? '\u2588' : '\u2591');
            sb.Append(']');
            return sb.ToString();
        }

        private void InitStyles()
        {
            _boxStyle = new GUIStyle(GUI.skin.box);
            _boxStyle.normal.background = MakeTexture(2, 2, new Color(0f, 0f, 0f, 0.75f));

            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                richText = true
            };
            _labelStyle.normal.textColor = Color.white;

            _headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize  = 14,
                fontStyle = FontStyle.Bold
            };
            _headerStyle.normal.textColor = Color.white;

            _stylesInitialized = true;
        }

        private Texture2D MakeTexture(int w, int h, Color color)
        {
            Color[]   pixels = new Color[w * h];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
            Texture2D tex = new Texture2D(w, h);
            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }

        private Color GetNodeColor(string nodeName)
        {
            switch (nodeName)
            {
                case "Patrol":      return Color.green;
                case "Chase":       return Color.yellow;
                case "Attack":      return Color.red;
                case "Search":      return Color.cyan;
                case "Investigate": return new Color(1f, 0.5f, 0f);
                default:            return Color.grey;
            }
        }

        void OnDrawGizmos()
        {
            if (!showOverlay || !Application.isPlaying) return;

            string nodeName = _controller?.Blackboard?.ActiveNodeName ?? "None";

#if UNITY_EDITOR
            GUIStyle worldStyle = new GUIStyle
            {
                fontSize  = 14,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            worldStyle.normal.textColor = GetNodeColor(nodeName);

            Vector3 labelPos = transform.position + Vector3.up * labelHeightOffset;
            UnityEditor.Handles.Label(labelPos, $"[{nodeName}]", worldStyle);
#endif
        }
    }
}
