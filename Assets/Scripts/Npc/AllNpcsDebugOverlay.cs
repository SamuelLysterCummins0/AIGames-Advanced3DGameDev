using UnityEngine;
using UnityEngine.AI;

namespace Semester2
{
    /// <summary>
    /// Draws a compact top-of-screen panel showing the same info as NpcDebugOverlay
    /// but for every NPC at once. Place on any scene GameObject (e.g. GameManager).
    /// Press F2 to toggle. Each NPC also gets its own nav-path line renderer.
    /// </summary>
    public class AllNpcsDebugOverlay : MonoBehaviour
    {
        [SerializeField] private KeyCode toggleKey   = KeyCode.F2;
        [SerializeField] private bool    showOverlay = true;
        [SerializeField] private Color   navPathColor = Color.cyan;

        private NpcController[] _npcs;
        private NavMeshAgent[]  _agents;
        private LineRenderer[]  _pathLines;

        private GUIStyle _boxStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _headerStyle;
        private bool     _stylesReady = false;

        private const float COL_WIDTH  = 300f;
        private const float ROW_HEIGHT = 20f;
        private const float PADDING    = 8f;
        private const float PANEL_H    = 185f;  // enough for 8 rows

        void Start()
        {
            _npcs    = FindObjectsByType<NpcController>(FindObjectsSortMode.None);
            _agents  = new NavMeshAgent[_npcs.Length];
            _pathLines = new LineRenderer[_npcs.Length];

            for (int i = 0; i < _npcs.Length; i++)
            {
                _agents[i] = _npcs[i].GetComponent<NavMeshAgent>();
                _pathLines[i] = CreatePathLine(_npcs[i].gameObject);
            }
        }

        private LineRenderer CreatePathLine(GameObject parent)
        {
            GameObject obj = new GameObject("DebugNavPath_All");
            obj.transform.SetParent(parent.transform);
            obj.transform.localPosition = Vector3.zero;

            LineRenderer lr = obj.AddComponent<LineRenderer>();
            lr.startWidth    = 0.1f;
            lr.endWidth      = 0.1f;
            lr.material      = new Material(Shader.Find("Sprites/Default"));
            lr.startColor    = navPathColor;
            lr.endColor      = navPathColor;
            lr.positionCount = 0;
            return lr;
        }

        void Update()
        {
            if (Input.GetKeyDown(toggleKey))
                showOverlay = !showOverlay;

            if (_pathLines == null) return;

            for (int i = 0; i < _npcs.Length; i++)
            {
                if (_pathLines[i] == null || _agents[i] == null) continue;

                if (showOverlay && _agents[i].hasPath && _agents[i].path.corners.Length > 0)
                {
                    Vector3[] corners = _agents[i].path.corners;
                    _pathLines[i].positionCount = corners.Length;
                    for (int c = 0; c < corners.Length; c++)
                        _pathLines[i].SetPosition(c, corners[c] + Vector3.up * 0.1f);
                }
                else
                {
                    _pathLines[i].positionCount = 0;
                }
            }
        }

        void OnGUI()
        {
            if (!showOverlay || _npcs == null || _npcs.Length == 0) return;
            if (!_stylesReady) InitStyles();

            float totalWidth = _npcs.Length * COL_WIDTH + PADDING * 2f;
            float startX     = (Screen.width - totalWidth) * 0.5f;

            GUI.Box(new Rect(startX, PADDING, totalWidth, PANEL_H), "", _boxStyle);

            for (int i = 0; i < _npcs.Length; i++)
            {
                NpcController npc = _npcs[i];
                if (npc == null) continue;

                var          bb     = npc.Blackboard;
                NavMeshAgent agent  = _agents[i];

                float colX = startX + PADDING + i * COL_WIDTH;
                float y    = PADDING + 6f;

                // Header — NPC name
                GUI.Label(new Rect(colX, y, COL_WIDTH - 8f, ROW_HEIGHT), npc.name, _headerStyle);
                y += ROW_HEIGHT;

                // BT node — colour coded
                string node = bb?.ActiveNodeName ?? "None";
                Color  col  = NodeColour(node);
                string hex  = ColorUtility.ToHtmlStringRGB(col);
                GUI.Label(new Rect(colX, y, COL_WIDTH - 8f, ROW_HEIGHT),
                    $"<color=#{hex}>BT Node: {node}</color>", _labelStyle);
                y += ROW_HEIGHT;

                // Sight / Hearing
                string sight = (bb?.PlayerVisible ?? false)
                    ? "<color=#00ff00>SEEN</color>"  : "<color=#555555>---</color>";
                string heard = (bb?.PlayerHeard ?? false)
                    ? "<color=#ffff00>HEARD</color>" : "<color=#555555>---</color>";
                GUI.Label(new Rect(colX, y, COL_WIDTH - 8f, ROW_HEIGHT),
                    $"Sight: {sight}   Hearing: {heard}", _labelStyle);
                y += ROW_HEIGHT;

                // Distance + LKP
                float  dist = bb?.DistanceToPlayer ?? 0f;
                string lkp  = (bb?.HasLastKnownPosition ?? false)
                    ? "<color=#00ffff>YES</color>" : "<color=#555555>NO</color>";
                GUI.Label(new Rect(colX, y, COL_WIDTH - 8f, ROW_HEIGHT),
                    $"Distance: {dist:F1}m   Has LKP: {lkp}", _labelStyle);
                y += ROW_HEIGHT;

                // PowerBox
                string pb = (bb?.PowerBoxActive ?? false)
                    ? "<color=#ff8800>ACTIVE</color>" : "<color=#555555>none</color>";
                GUI.Label(new Rect(colX, y, COL_WIDTH - 8f, ROW_HEIGHT),
                    $"PowerBox: {pb}", _labelStyle);
                y += ROW_HEIGHT;

                // Reinforcing
                string reinf = (bb?.ReinforcementTracking ?? false)
                    ? "<color=#ff6600>ACTIVE</color>" : "<color=#555555>---</color>";
                GUI.Label(new Rect(colX, y, COL_WIDTH - 8f, ROW_HEIGHT),
                    $"Reinforcing: {reinf}", _labelStyle);
                y += ROW_HEIGHT;

                // Destination
                string dest = "None";
                if (agent != null && agent.hasPath)
                {
                    Vector3 d = agent.destination;
                    dest = $"({d.x:F1}, {d.z:F1}) — {agent.remainingDistance:F1}m";
                }
                GUI.Label(new Rect(colX, y, COL_WIDTH - 8f, ROW_HEIGHT),
                    $"Dest: {dest}", _labelStyle);
                y += ROW_HEIGHT;

                // Speed
                float speed = agent != null ? agent.velocity.magnitude : 0f;
                GUI.Label(new Rect(colX, y, COL_WIDTH - 8f, ROW_HEIGHT),
                    $"Speed: {speed:F1}m/s", _labelStyle);

                // Divider between columns
                if (i < _npcs.Length - 1)
                {
                    float divX = colX + COL_WIDTH - 2f;
                    GUI.DrawTexture(new Rect(divX, PADDING + 4f, 1f, PANEL_H - 8f),
                        Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0,
                        new Color(1f, 1f, 1f, 0.2f), 0, 0);
                }
            }

            // Toggle hint
            GUI.Label(
                new Rect(startX + totalWidth - 110f, PADDING + PANEL_H - ROW_HEIGHT - 2f, 105f, ROW_HEIGHT),
                $"<color=#444444>F2 toggle</color>", _labelStyle);
        }

        private void InitStyles()
        {
            _boxStyle = new GUIStyle(GUI.skin.box);
            _boxStyle.normal.background = MakeTex(2, 2, new Color(0f, 0f, 0f, 0.78f));

            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                richText = true
            };
            _labelStyle.normal.textColor = Color.white;

            _headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize  = 13,
                fontStyle = FontStyle.Bold
            };
            _headerStyle.normal.textColor = Color.white;

            _stylesReady = true;
        }

        private static Texture2D MakeTex(int w, int h, Color c)
        {
            Color[]   px  = new Color[w * h];
            for (int i = 0; i < px.Length; i++) px[i] = c;
            Texture2D tex = new Texture2D(w, h);
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        private static Color NodeColour(string name)
        {
            switch (name)
            {
                case "Patrol":      return Color.green;
                case "Chase":       return Color.yellow;
                case "Attack":      return Color.red;
                case "Search":      return Color.cyan;
                case "Investigate": return new Color(1f, 0.5f, 0f);
                default:            return Color.grey;
            }
        }
    }
}
