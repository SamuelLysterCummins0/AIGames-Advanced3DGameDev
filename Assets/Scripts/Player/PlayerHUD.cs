using UnityEngine;
using UnityEngine.UI;

namespace Semester2
{
    /// <summary>
    /// Drives the player HUD — health bar fill and edge damage vignette.
    /// Subscribes to PlayerHealth.OnHealthChanged so it updates whenever
    /// health changes from damage or regeneration.
    ///
    /// Scene setup:
    ///   Canvas (Screen Space - Overlay)
    ///     HUD (this script lives here)
    ///       HealthBar/Fill  -- Image, Fill Method = Horizontal, wired into healthBarFill
    ///       Vignette        -- fullscreen Image, Raycast Target OFF, wired into vignetteImage
    /// </summary>
    public class PlayerHUD : MonoBehaviour
    {
        [Header("Health Bar")]
        [SerializeField] private Image healthBarFill;

        [Header("Damage Vignette")]
        [SerializeField] private Image  vignetteImage;

        [Tooltip("Red tint used at low damage (light, translucent).")]
        [SerializeField] private Color vignetteColorLow  = new Color(0.9f, 0f, 0f, 0.35f);

        [Tooltip("Red tint used at high damage (dark, more opaque).")]
        [SerializeField] private Color vignetteColorHigh = new Color(0.5f, 0f, 0f, 0.85f);

        [Tooltip("Normalised distance from screen edge at which the vignette starts to appear. " +
                 "0 = edges only, 0.5 = halfway to centre.")]
        [SerializeField] [Range(0f, 0.9f)] private float vignetteEdgeStart = 0.35f;

        // -----------------------------------------------------------------------

        void Awake()
        {
            if (vignetteImage != null)
                vignetteImage.sprite = CreateVignetteSprite(128, vignetteEdgeStart);
        }

        void OnEnable()
        {
            PlayerHealth.OnHealthChanged += UpdateHUD;
        }

        void OnDisable()
        {
            PlayerHealth.OnHealthChanged -= UpdateHUD;
        }

        // -----------------------------------------------------------------------

        private void UpdateHUD(float healthFraction)
        {
            // --- Health bar ---
            if (healthBarFill != null)
                healthBarFill.fillAmount = healthFraction;

            // --- Vignette ---
            if (vignetteImage == null) return;

            float damage = 1f - healthFraction;  // 0 = full health, 1 = dead

            // Lerp between the two tint colours; scale alpha by damage so the
            // vignette is fully invisible at full health.
            Color c = Color.Lerp(vignetteColorLow, vignetteColorHigh, damage);
            c.a *= damage;
            vignetteImage.color = c;
        }

        // -----------------------------------------------------------------------
        // Procedural vignette texture — white with edge alpha, tinted at runtime.
        // Uses the max of horizontal/vertical edge distance so all four screen
        // edges are hit evenly (no circular clipping on wide screens).
        // -----------------------------------------------------------------------

        private static Sprite CreateVignetteSprite(int size, float edgeStart)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;

            float range = Mathf.Max(1f - edgeStart, 0.001f); // falloff range

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (float)x / (size - 1);
                    float v = (float)y / (size - 1);

                    // Distance from nearest edge, 0 at centre, 1 at corners
                    float edgeDist = Mathf.Max(
                        Mathf.Abs(u - 0.5f) * 2f,
                        Mathf.Abs(v - 0.5f) * 2f
                    );

                    float t     = Mathf.Clamp01((edgeDist - edgeStart) / range);
                    float alpha = Mathf.SmoothStep(0f, 1f, t);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }

            tex.Apply();

            return Sprite.Create(
                tex,
                new Rect(0, 0, size, size),
                new Vector2(0.5f, 0.5f),
                pixelsPerUnit: size
            );
        }
    }
}
