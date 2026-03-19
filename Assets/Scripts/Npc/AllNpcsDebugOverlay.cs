using UnityEngine;

namespace Semester2
{
    /// <summary>
    /// Draws a compact top-of-screen panel showing the BT state for every NPC
    /// in the scene simultaneously. Place this on any scene GameObject (e.g. GameManager).
    /// Press F2 to toggle (F1 still controls the per-NPC world-space overlay).
    /// </summary>
    public class AllNpcsDebugOverlay : MonoBehaviour
    {
        [SerializeField] private KeyCode toggleKey  = KeyCode.F2;
        [SerializeField] private bool    showOverlay = true;

        private NpcController[] _npcs;

        private GUIStyle _boxStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _headerStyle;
        private bool     _stylesReady = false;

        private const float COL_WIDTH   = 280f;
        private const float ROW_HEIGHT  = 20f;
        private const float PADDING     = 8f;
        private const float PANEL_H     = 130f;

        void Start()
        {
            _npcs = FindObjectsByType<NpcController>(FindObjectsSortMode.None);
        }

        void Update()
        {
            if (Input.GetKeyDown(toggleKey))
                showOverlay = !showOverlay;
        }

        void OnGUI()
        {
            if (!showOverlay || _npcs == null || _npcs.Length == 0) return;
            if (!_stylesReady) InitStyles();

            float totalWidth  = _npcs.Length * COL_WIDTH + PADDING * 2f;
            float startX      = (Screen.width - totalWidth) * 0.5f;

            // Background panel across the top
            GUI.Box(new Rect(startX, PADDING, totalWidth, PANEL_H), "", _boxStyle);

            for (int i = 0; i < _npcs.Length; i++)
            {
                NpcController npc = _npcs[i];
                if (npc == null) continue;

                var bb = npc.Blackboard;

                float colX = startX + PADDING + i * COL_WIDTH;
                float y    = PADDING + 6f;

                // NPC name header
                GUI.Label(new Rect(colX, y, COL_WIDTH - 8f, ROW_HEIGHT),
                    npc.name, _headerStyle);
                y += ROW_HEIGHT;

                // BT node — colour coded
                string node    = bb?.ActiveNodeName ?? "None";
                Color  col     = NodeColour(node);
                string hex     = ColorUtility.ToHtmlStringRGB(col);
                GUI.Label(new Rect(colX, y, COL_WIDTH - 8f, ROW_HEIGHT),
                    $"<color=#{hex}>● {node}</color>", _labelStyle);
                y += ROW_HEIGHT;

                // Sight / Hearing
                string sight = (bb?.PlayerVisible ?? false)
                    ? "<color=#00ff00>SEEN</color>" : "<color=#555555>---</color>";
                string heard = (bb?.PlayerHeard ?? false)
                    ? "<color=#ffff00>HEARD</color>" : "<color=#555555>---</color>";
                GUI.Label(new Rect(colX, y, COL_WIDTH - 8f, ROW_HEIGHT),
                    $"Sight: {sight}  Hearing: {heard}", _labelStyle);
                y += ROW_HEIGHT;

                // Distance + LKP
                float  dist   = bb?.DistanceToPlayer ?? 0f;
                string lkp    = (bb?.HasLastKnownPosition ?? false)
                    ? "<color=#00ffff>YES</color>" : "<color=#555555>NO</color>";
                GUI.Label(new Rect(colX, y, COL_WIDTH - 8f, ROW_HEIGHT),
                    $"Dist: {dist:F1}m   LKP: {lkp}", _labelStyle);
                y += ROW_HEIGHT;

                // Reinforcement
                string reinf = (bb?.ReinforcementTracking ?? false)
                    ? "<color=#ff6600>REINFORCING</color>" : "<color=#555555>idle</color>";
                GUI.Label(new Rect(colX, y, COL_WIDTH - 8f, ROW_HEIGHT),
                    reinf, _labelStyle);

                // Divider between NPCs (skip last)
                if (i < _npcs.Length - 1)
                {
                    float divX = colX + COL_WIDTH - 2f;
                    GUI.DrawTexture(new Rect(divX, PADDING + 4f, 1f, PANEL_H - 8f),
                        Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0,
                        new Color(1f, 1f, 1f, 0.2f), 0, 0);
                }
            }

            // Toggle hint bottom-right of panel
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
                fontStyle = FontStyle.Bold,
                richText  = true
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
