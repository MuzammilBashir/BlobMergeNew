using UnityEngine;

namespace BlobMerge
{
    /// <summary>
    /// Colours of the playfield (background, tank frame, field, water line, bubbles, danger bar). One asset per scene
    /// (Game = purple aquarium, Hexa = its own copy) so each variant can be restyled without touching code.
    /// Defaults are the original purple look.
    /// </summary>
    public enum FieldParticles { Bubbles, Stars }
    public enum BackdropStyle { Vignette, Gradient }

    [CreateAssetMenu(menuName = "BlobMerge/Board Theme", fileName = "BoardTheme")]
    public class BoardTheme : ScriptableObject
    {
        [Header("Background")]
        [Tooltip("Camera clear colour (outside everything).")]
        public Color cameraBackground = new Color32(0x1C, 0x10, 0x38, 0xFF);
        [Tooltip("Backdrop vignette: centre colour.")]
        public Color backgroundCenter = new Color32(0x2A, 0x1A, 0x5C, 0xFF);
        [Tooltip("Backdrop vignette: edge colour.")]
        public Color backgroundEdge = new Color32(0x17, 0x0C, 0x33, 0xFF);

        [Tooltip("Vignette = radial (centre -> edge colours above). Gradient = linear multi-colour gradient (galaxy).")]
        public BackdropStyle backdrop = BackdropStyle.Vignette;
        [Tooltip("Gradient colours, first = top, last = bottom (evenly spaced).")]
        public Color[] gradient =
        {
            new Color32(0x08, 0x0B, 0x26, 0xFF), new Color32(0x17, 0x1B, 0x4D, 0xFF), new Color32(0x36, 0x1F, 0x63, 0xFF),
            new Color32(0x58, 0x27, 0x68, 0xFF), new Color32(0x16, 0x12, 0x3A, 0xFF)
        };
        [Tooltip("Gradient tilt in degrees (0 = straight top -> bottom).")]
        [Range(-60f, 60f)] public float gradientAngle = 18f;

        [Header("Tank frame")]
        public Color frameDark = new Color32(0x45, 0x20, 0xA3, 0xFF);
        public Color frameMain = new Color32(0x5E, 0x2E, 0xCC, 0xFF);
        public Color frameHighlight = new Color32(0x7A, 0x4A, 0xE8, 0xFF);
        [Tooltip("Thin dark lip just inside the frame.")]
        public Color innerLip = new Color32(0x26, 0x13, 0x57, 0xFF);
        [Tooltip("Soft drop shadow under the tank (alpha = strength).")]
        public Color frameShadow = new Color(0.03f, 0.01f, 0.08f, 0.35f);
        [Tooltip("The two door bars in the side walls where lane pieces enter.")]
        public Color doors = new Color(0.85f, 0.78f, 1f, 0.8f);

        [Header("Field (inside the tank)")]
        [Tooltip("Field colours' alpha < 1 makes the tank tinted glass: the backdrop (gradient, nebula, stars) shows through.")]
        public Color fieldCenter = new Color32(0x4A, 0x2E, 0x9E, 0xFF);
        public Color fieldEdge = new Color32(0x2D, 0x1A, 0x6B, 0xFF);
        [Tooltip("Darker band behind the lane (above the water line).")]
        public Color laneShade = new Color(0.10f, 0.05f, 0.25f, 0.45f);
        public Color waveLine = new Color(0.78f, 0.70f, 1f, 0.55f);
        [Tooltip("Bubble colour (or star colour in Stars mode).")]
        public Color bubbles = new Color(0.80f, 0.72f, 1f, 0.22f);

        [Header("Particles")]
        [Tooltip("Bubbles = rising water bubbles (aquarium). Stars = soft distant stars in the backdrop (galaxy).")]
        public FieldParticles particles = FieldParticles.Bubbles;
        [Tooltip("Stars: how many over the tank area (seen through the glass).")]
        [Range(0, 200)] public int starCount = 45;
        [Tooltip("Stars: how many in the backdrop around the tank.")]
        [Range(0, 200)] public int backdropStarCount = 40;
        [Tooltip("Stars: overall opacity.")]
        [Range(0f, 1f)] public float starOpacity = 0.45f;
        [Tooltip("Stars: size multiplier.")]
        [Range(0.2f, 3f)] public float starSize = 1f;
        [Tooltip("Stars: how much they twinkle (0 = steady).")]
        [Range(0f, 1f)] public float twinkleAmount = 0.35f;
        [Tooltip("Stars: twinkle speed.")]
        [Range(0f, 4f)] public float twinkleSpeed = 0.6f;
        [Tooltip("Stars: share drawn as small 4-point diamond sparkles (the rest are dots).")]
        [Range(0f, 1f)] public float sparkleShare = 0f;
        [Tooltip("Stars: blink sharpness. 0 = smooth twinkle, 1 = quick blinks with longer steady gaps.")]
        [Range(0f, 1f)] public float blinkSharpness = 0f;

        [Header("Space props (backdrop, subtle animation)")]
        [Tooltip("Soft nebula clouds drifting slowly behind everything.")]
        [Range(0, 12)] public int nebulaClouds = 0;
        [Tooltip("Nebula colours (alpha = strength; keep low, ~0.05-0.15).")]
        public Color[] nebulaColors =
        {
            new Color(0.48f, 0.25f, 0.89f, 0.12f), new Color(0.88f, 0.28f, 0.62f, 0.08f), new Color(0.18f, 0.77f, 0.84f, 0.06f)
        };
        [Tooltip("Nebula drift/breathing speed.")]
        [Range(0f, 2f)] public float nebulaSpeed = 0.5f;
        [Tooltip("A faint distant spiral galaxy that slowly rotates.")]
        public bool distantGalaxy = false;
        [Tooltip("Distant galaxy: position (world units; board centre = 0,0).")]
        public Vector2 galaxyPosition = new Vector2(2.6f, 4.2f);
        [Range(0.5f, 6f)] public float galaxySize = 2.2f;
        public Color galaxyColor = new Color(0.75f, 0.7f, 1f, 0.35f);
        [Tooltip("Seconds between shooting stars (0 = none).")]
        [Range(0f, 30f)] public float shootingStarEvery = 0f;
        public Color shootingStarColor = new Color(0.85f, 0.92f, 1f, 0.4f);
        [Tooltip("If set, each shooting star picks one of these colours (otherwise Shooting Star Color).")]
        public Color[] shootingStarColors = new Color[0];
        [Tooltip("Shooting star: crosses the whole screen side to side in this many seconds (0 = short local streak).")]
        [Range(0f, 4f)] public float shootingStarCrossTime = 0f;
        [Tooltip("Shooting star tail length (world units).")]
        [Range(0.3f, 5f)] public float shootingStarLength = 1.1f;
        [Tooltip("Shooting star head thickness (world units).")]
        [Range(0.01f, 0.2f)] public float shootingStarWidth = 0.036f;

        [Header("Light rays (underwater)")]
        [Tooltip("Soft light shafts from the surface, swaying slowly (0 = none).")]
        [Range(0, 12)] public int lightRays = 0;
        public Color lightRayColor = new Color(0.75f, 0.95f, 1f, 0.10f);
        [Tooltip("Ray sway speed.")]
        [Range(0f, 2f)] public float lightRaySpeed = 0.4f;

        [Header("Floating debris (rocks)")]
        [Range(0, 30)] public int debrisCount = 0;
        [Tooltip("Rock body colour.")]
        public Color debrisColor = new Color32(0x2B, 0x12, 0x3B, 0xFF);
        [Tooltip("Shadowed facet (darker).")]
        public Color debrisShade = new Color32(0x1D, 0x0B, 0x2A, 0xFF);
        [Tooltip("Rim highlight along the lit (upper-left) edge.")]
        public Color debrisRim = new Color32(0xF2, 0x73, 0x4F, 0xFF);
        [Range(0.05f, 1.2f)] public float debrisMinSize = 0.12f;
        [Range(0.05f, 1.5f)] public float debrisMaxSize = 0.55f;
        [Tooltip("Drift / spin speed.")]
        [Range(0f, 2f)] public float debrisSpeed = 0.5f;

        [Header("Danger bar")]
        public Color danger = new Color32(0x7C, 0x55, 0xDD, 0xFF);
        [Tooltip("Pulse colour while the game-over countdown runs.")]
        public Color dangerHot = new Color32(0xFF, 0x4F, 0x9A, 0xFF);

        [Header("Pieces")]
        [Tooltip("Colour per value: 1, 2, 4, 8, 16, 32, 64, 128, 256, 512+ (the last one repeats for higher values).")]
        public Color[] pieceColors =
        {
            new Color32(0xEC, 0x2B, 0x3F, 0xFF), // 1  red
            new Color32(0xFF, 0xE3, 0x3D, 0xFF), // 2  yellow
            new Color32(0x52, 0xF5, 0x2E, 0xFF), // 4  green
            new Color32(0x2C, 0xF2, 0xC2, 0xFF), // 8  cyan
            new Color32(0x3D, 0x7D, 0xF2, 0xFF), // 16 blue
            new Color32(0x9B, 0x2B, 0xF0, 0xFF), // 32 purple
            new Color32(0xF5, 0x1D, 0xAD, 0xFF), // 64 pink
            new Color32(0xF2, 0x6A, 0x35, 0xFF), // 128 orange
            new Color32(0xD6, 0xFF, 0x3B, 0xFF), // 256 lime
            new Color32(0xF2, 0xF2, 0xF2, 0xFF), // 512+ white
        };

        [Header("Glossy board (Fish Fantasy art pass; off = the original bevelled frame)")]
        [Tooltip("Painted backdrop behind the tank (covers the screen; replaces the gradient/vignette).")]
        public Sprite backgroundPlate;
        [Tooltip("Glass-tube frame: navy outline, cyan tube with a white top-left highlight, teal inner bevel.")]
        public bool glossyFrame;
        public Color glossOutline = new Color32(0x00, 0x1B, 0x5C, 0xFF);
        public Color glossTubeEdge = new Color32(0x02, 0xC9, 0xED, 0xFF);
        public Color glossTube = new Color32(0x11, 0xE3, 0xFD, 0xFF);
        public Color glossHighlight = new Color32(0xD8, 0xFB, 0xFF, 0xFF);
        public Color glossBevel = new Color32(0x00, 0x5A, 0x84, 0xFF);
        public Color glossBevelDark = new Color32(0x00, 0x2B, 0x56, 0xFF);

        [Header("UI art (empty = the procedural HUD; Fish: Assets/Art/UI)")]
        [Tooltip("Score panel art (replaces the rounded panel; its SCORE tab label stays).")]
        public Sprite uiScorePlate;
        public Sprite uiCrown;
        public Sprite uiSettingsButton;
        [Tooltip("Game-over popup card (set = the Fish fail screen: art, score, Play again button).")]
        public Sprite uiPopup;
        public Sprite uiButton;
        public Sprite uiGameOverArt;
        [Tooltip("Splash shown once per app launch (full-screen art + logo + Loading...).")]
        public Sprite splashBackground;
        public Sprite splashLogo;

        static BoardTheme fallback;
        /// <summary>The original purple look (used when a scene has no theme assigned).</summary>
        public static BoardTheme Default => fallback != null ? fallback : (fallback = CreateInstance<BoardTheme>());
    }
}
