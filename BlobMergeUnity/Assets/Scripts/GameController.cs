using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;
using UnityEngine.UI;
using TMPro;

namespace BlobMerge
{
    /// <summary>
    /// Entry point. Owns the soft-body world, board, lane, aiming hookup, merges, juice, score and game over.
    /// Every tuning value is exposed in the inspector.
    /// </summary>
    public enum PieceShape { Blob, Hexagon, FishBubble }
    /// <summary>Flat two-tone pattern on hexagon pieces (vector "planet" look).</summary>
    public enum HexTexture { None, Camo, Stripes, Craters, MixByValue }

    [RequireComponent(typeof(AimController))]
    public class GameController : MonoBehaviour
    {
        [Header("Assets (assigned by Tools > BlobMerge > Setup)")]
        public Material material;
        [Tooltip("TMP font for all text (Barlow Bold SDF). If empty, loaded from Resources/Fonts.")]
        public TMP_FontAsset font;
        [Tooltip("The HUD prefab to spawn (edit it in Prefab Mode). If empty, Resources/UI/HUD is used.")]
        public HudView hudPrefab;
        [Tooltip("Colours of this scene's background, tank, water, danger bar and pieces. Empty = original purple look.")]
        public BoardTheme boardTheme;

        [Tooltip("Extra shift for the HUD's TopBar, up (+) / down (-), in canvas pixels (1080x1920 reference). Usually just move TopBar in the prefab instead.")]
        public float hudOffsetY = 0f;

        [Header("Board")]
        public float boardWidth = 8f;
        public float boardHeight = 14f;
        public float frameThickness = 0.6f;
        [Tooltip("World units kept free above the container for the score HUD.")]
        public float hudSpace = 3.1f;
        [Tooltip("Corner rounding of the container.")]
        public float frameCornerRadius = 0.3f;
        [Tooltip("How far below the lane centre the wavy water line sits (lane blobs float on it).")]
        public float waterLineBelowLane = 0.45f;
        [Tooltip("Decorative bubbles drifting up through the container.")]
        [Range(0, 40)] public int bubbleCount = 12;
        public float chamfer = 0.75f;
        public float laneFromTop = 1.35f;
        public float dangerLineFromFloor = 9.6f;
        public float dangerHoldSeconds = 1.6f;
        [Tooltip("Round bumpers sticking out of the side walls.")]
        public bool sideBumpers = false;
        public float bumperRadius = 0.6f;
        public float bumperHeightFromFloor = 8.4f;

        [Header("Blob size & look")]
        public float baseRadius = 0.5f;
        public float radiusPerTier = 0.115f;
        public float maxRadius = 1.75f;
        [Tooltip("How lumpy/irregular each blob's outline is (fraction of radius). 0 = perfect circle.")]
        [Range(0f, 0.4f)] public float silhouetteNoise = 0.16f;
        [Tooltip("Random stretch along a random axis, for pill/potato shapes (0.3 = up to 30% longer).")]
        [Range(0f, 0.8f)] public float elongation = 0.3f;
        public int pointsPerBlob = 20;
        public float density = 1f;
        [Tooltip("Height of the numbers on blobs (world units; a small blob is ~1 unit wide). Same on every blob, whatever its value or size.")]
        [Range(0.15f, 1f)] public float blobLabelHeight = 0.36f;
        [Tooltip("Off = constant number size everywhere (lane, flight, pile, merge pops). On = numbers scale with the blob.")]
        public bool blobLabelFollowsSize = false;

        [Header("Soft-body physics (water bubble)")]
        public float gravity = 20f;
        [Range(1, 8)] public int substeps = 4;
        [Range(1, 4)] public int iterations = 2;
        [Tooltip("Air drag per second. Slows the whole blob (flight + wobble).")]
        public float damping = 0.35f;
        [Tooltip("Damps only shape motion. High = soft & doughy (oozes back), low = jiggly. Doesn't affect flight.")]
        [Range(0f, 0.3f)] public float deformationDamping = 0.08f;
        [Tooltip("Air pressure inside the balloon. LOW = soft, slumps flat on the floor and squashes under neighbours. " +
                 "HIGH = tight, stays round. Measured: 1.5 melted puddle, 2.5 very soft, 3-4 soft balloon, 6 firm, 10 hard ball.")]
        [Range(0.5f, 12f)] public float pressure = 3.5f;
        [Tooltip("Bigger blobs get this × pressure (1 = same). Below 1 makes big blobs slump more, like real balloons.")]
        [Range(0.3f, 2f)] public float largeBlobPressureScale = 0.8f;
        [Tooltip("Skin stretchiness. Higher = skin can't stretch (blob keeps its size), lower = rubbery skin bulges under load.")]
        [Range(0f, 1f)] public float edgeStiffness = 0.8f;
        [Tooltip("Optional hard volume lock on top of pressure. 0 = pure balloon (recommended).")]
        [Range(0f, 1f)] public float areaStiffness = 0f;
        [Tooltip("Crush limit: a blob can't be squeezed below this fraction of its normal size.")]
        [Range(0f, 0.95f)] public float minVolume = 0.55f;
        [Tooltip("Lump memory: how strongly a blob keeps its irregular outline. Squash/stretch stays free, " +
                 "so this does NOT stop slumping. 0 = pressure rounds everything into smooth balloons.")]
        [Range(0f, 0.15f)] public float shapeStiffnessSmall = 0.03f;
        [Tooltip("Same, for the biggest tier.")]
        [Range(0f, 0.15f)] public float shapeStiffnessLarge = 0.03f;
        [Tooltip("Squash limit (long axis / short axis). Blobs flatten freely up to this, then get pulled back. " +
                 "Stops thin slivers/pancakes. 1.5 = barely squashable, 2 = soft, 3 = very floppy.")]
        [Range(1.1f, 4f)] public float maxSquash = 2f;

        [Header("Piece shape (theme)")]
        [Tooltip("Blob = lumpy pressure balloons. Hexagon = regular hexagons that squash & stretch, then spring back to the exact hexagon. " +
                 "Hexagons use the normal physics fields (the Hexa scene has its own values: Shape Stiffness = spring-back speed, " +
                 "Pressure fills dents, Max Squash = squash limit).")]
        public PieceShape pieceShape = PieceShape.Blob;
        [Tooltip("Hexagon corner rounding (fraction of a side). 0 = sharp corners.")]
        [Range(0f, 0.6f)] public float hexCornerRound = 0.22f;
        [Tooltip("Skin points per hexagon side (more = flatter faces, slower physics).")]
        [Range(3, 8)] public int hexPointsPerSide = 5;
        [Tooltip("Hexagons: flat two-tone texture. Camo = planet patches, Stripes = gas-giant bands, Craters = moon spots, " +
                 "Mix By Value = each number gets its own pattern. Textures turn and squash with the piece.")]
        public HexTexture hexTexture = HexTexture.None;
        [Tooltip("Hexagon texture contrast (how much darker/lighter the pattern is than the piece colour).")]
        [Range(0f, 0.5f)] public float hexTextureContrast = 0.2f;
        [Tooltip("Hexagons: 1 = always springs back to the exact hexagon. Lower lets some squash stay (sags under weight).")]
        [Range(0f, 1f)] public float hexRigidity = 1f;
        [Tooltip("Hexagons: once a piece has landed (touched the floor or the pile) it is held as an exact hexagon - " +
                 "straight sides, no squash. Squash & stretch then only happens in flight and on the merge pop.")]
        public bool hexRigidWhenLanded = true;
        [Tooltip("Hexagons: spring-back strength once landed (per solver pass). 1 = perfectly rigid.")]
        [Range(0.05f, 1f)] public float hexLandedStiffness = 0.7f;
        [Tooltip("Hexagons: seconds to snap back to the exact hexagon after landing (the landing squash).")]
        [Range(0f, 0.5f)] public float hexSettleTime = 0.12f;
        [Tooltip("Hexagons: max rebound after landing, as a fraction of the landing speed (first 0.4 s). The soft body " +
                 "springing back from its landing squash otherwise bounces like rubber (~40 %). 1 = no limit.")]
        [Range(0f, 1f)] public float hexLandingBounce = 0.2f;

        [Header("Fish bubble (Fish scene)")]
        [Tooltip("Fish width as a fraction of the bubble's diameter.")]
        [Range(0.3f, 1.2f)] public float fishSize = 0.74f;
        [Tooltip("Fish scene: bubble growth per creature up to Fish Big From Tier (17 creatures, 2026-10-13). Other scenes use Radius Per Tier.")]
        public float fishTierStep = 0.075f;
        [Tooltip("Fish scene: from this tier on (10 = dolphin) the bubbles grow by Fish Big Tier Step, so the whale (16) still fits the tank.")]
        public int fishBigFromTier = 10;
        [Tooltip("Fish scene: growth per creature from Fish Big From Tier on (dolphin -> whale).")]
        public float fishBigTierStep = 0.04f;
        [Tooltip("Show the value number on fish bubbles. Off = the creature alone tells the value; merges still pop \"+N\".")]
        public bool fishShowNumbers = false;
        [Tooltip("How far below the bubble centre the number sits (fraction of the radius).")]
        [Range(0f, 0.9f)] public float fishLabelDrop = 0.52f;
        [Tooltip("Bubble body tint strength (alpha of the piece colour inside the bubble).")]
        [Range(0f, 0.6f)] public float bubbleTint = 0.18f;
        [Tooltip("Bubble rim brightness (the glassy edge).")]
        [Range(0f, 1f)] public float bubbleRim = 0.6f;
        [Tooltip("Fish bob amplitude (fraction of the radius) and speed.")]
        [Range(0f, 0.2f)] public float fishBob = 0.05f;
        [Range(0f, 4f)] public float fishBobSpeed = 1.6f;
        [Tooltip("Idle swim motion strength (tail & fins / tentacles / flippers). 0 = still sprite.")]
        [Range(0f, 3f)] public float fishSwim = 1f;
        [Tooltip("Underwater ambience loop volume (Fish theme sounds: bubble pops, bloops, whooshes). 0 = off.")]
        [Range(0f, 1f)] public float ambienceVolume = 0f; // user: no ambience (music in ads instead)
        [Tooltip("Seconds between eye blinks (random in this range).")]
        [Range(0.5f, 10f)] public float fishBlinkMin = 2f;
        [Range(0.5f, 10f)] public float fishBlinkMax = 5f;

        [Header("Bounce")]
        [Tooltip("Bounce off the side walls, floor and ceiling. 0 = splat, 1 = full bounce.")]
        [FormerlySerializedAs("wallRestitution")]
        [Range(0f, 1f)] public float wallBounce = 0.3f;
        [Tooltip("Bounce off the round side bumpers.")]
        [Range(0f, 1.5f)] public float bumperBounce = 0.6f;
        [Tooltip("Bounce between blobs. 0 = they just squish together.")]
        [Range(0f, 1f)] public float blobBounce = 0.25f;
        [Tooltip("Blobs approaching slower than this (units/s) don't bounce, so the pile stays calm.")]
        public float bounceMinSpeed = 2.5f;

        [Header("Friction")]
        [Range(0f, 0.5f)] public float wallFriction = 0.05f;
        [Range(0f, 0.5f)] public float contactDamping = 0.035f;
        public float maxSpeed = 32f;

        [Header("Size spring (spawn / merge pop)")]
        public float popSpring = 240f;
        public float popDamping = 13f;
        public float laneSpawnStartArea = 0.2f;

        [Header("Blob scale (lane -> pile)")]
        [Tooltip("START scale: size of blobs waiting in the top lane (1 = full size).")]
        [Range(0.2f, 1.5f)] public float laneScale = 0.5f;
        [Tooltip("END scale: size of blobs once thrown into play (pile, merges).")]
        [Range(0.2f, 1.5f)] public float landedScale = 1f;
        [Tooltip("Seconds a thrown blob takes to tween from start to end scale. It also never lags behind its fall: " +
                 "it's fully grown by the time it reaches the floor.")]
        [Range(0.05f, 2f)] public float scaleTweenDuration = 0.45f;
        public float mergeStartArea = 0.55f;

        [Header("Merge")]
        public float mergeDuration = 0.12f;
        public float mergeAttraction = 220f;
        [Tooltip("Same-valued blobs in the top lane merge when they drift together (and a thrown blob merges with a matching lane blob). The result stays in the lane.")]
        public bool mergeInLane = true;
        [Tooltip("Fish theme: lane blobs merge with each other on their own (spawning in, drifting, the magnet). Off = only a " +
                 "thrown blob merges with a matching lane blob (user 2026-10-12). Blobs/Hexa always auto-merge.")]
        public bool laneAutoMerge = false;

        [Header("Spacing")]
        [Tooltip("Empty space always kept between blobs (world units; smallest blob radius is ~0.5). Blobs this close still count as touching for merges.")]
        [Range(0f, 0.4f)] public float blobGap = 0.06f;

        [Header("Throw")]
        [Tooltip("Seconds a thrown blob passes through the other lane blobs, so neighbours can't knock it off its aim.")]
        public float launchPassThroughTime = 0.25f;
        [Tooltip("A thrown blob slower than this (units/s) with its top up in the lane band...")]
        public float dropThroughSpeed = 1.2f;
        [Tooltip("...for this long (s) sinks through the lane blobs instead of resting on them.")]
        public float dropThroughDelay = 0.25f;

        [Header("Juice")]
        public float launchStretch = 0.22f;
        [Tooltip("Fish theme: volume of the light throw puff (0 = silent; try 0.25). Variant and pitch change on every throw.")]
        [Range(0f, 1f)] public float throwSoundVolume = 0f; // user 2026-10-12: off for now (0.25 = the light puff)
        public float impactFxSpeed = 7f;

        [Header("Camera shake")]
        [Tooltip("Camera move (world units) at full shake. Shake strength = amount² so small amounts stay subtle.")]
        public float shakeMaxOffset = 0.5f;
        [Tooltip("Camera roll (degrees) at full shake.")]
        public float shakeMaxAngle = 2.5f;
        [Tooltip("How fast shakes die out (per second).")]
        public float shakeDecay = 1.8f;
        public float shakeFrequency = 32f;
        [Tooltip("Multiplies every camera shake (move and roll). 0.25 = a quarter of the tuned values (user 2026-10-13).")]
        [Range(0f, 1f)] public float shakeScale = 0.25f;
        [Tooltip("Shake when two blobs merge in the container (pile).")]
        [Range(0f, 1f)] public float pileMergeShake = 0.45f;
        [Tooltip("Extra pile-merge shake per value tier (a 256 merge shakes harder than a 4).")]
        [Range(0f, 0.2f)] public float pileMergeShakePerTier = 0.05f;
        [Tooltip("Shake for merges in the top lane.")]
        [Range(0f, 1f)] public float laneMergeShake = 0.15f;
        [Tooltip("Aim power (0..1) that counts as max stretch.")]
        [Range(0.5f, 1f)] public float aimMaxThreshold = 0.97f;
        [Tooltip("One-off kick when the aim first reaches max stretch.")]
        [Range(0f, 1f)] public float aimMaxKick = 0.4f;
        [Tooltip("Continuous rumble while holding at max stretch.")]
        [Range(0f, 1f)] public float aimMaxRumble = 0.28f;
        [Tooltip("Shake on release, scaled by throw power.")]
        [Range(0f, 1f)] public float launchShake = 0.2f;
        public float trailSpeed = 9f;

        [Header("Lane")]
        public LaneSpawner lane = new LaneSpawner();

        [Header("Progression (Fish scene)")]
        public Progression progression = new Progression();
        [Tooltip("World units kept free below the container for the creature bar (Fish scene only).")]
        public float progressBarSpace = 2.2f;
        [Tooltip("Orca made for the first time: slow-mo, panel timing, tap lock, confetti (Fish scene only).")]
        public LevelCelebration celebration = new LevelCelebration();

        // ------------------------------------------------------------------ runtime
        const float PhysicsStep = 1f / 60f;
        const int LaneGroup = 1;
        // throw-into-lane merge (no lane auto-merge): min closing speed (units/s) and cos of the max angle off the partner
        const float ThrowMergeMinClosing = 0.5f, ThrowMergeMinCos = 0.6f;
        const int MergeGroupBase = 1 << 20;
        public const string BestKey = "BlobMerge.Best";

        SoftBodyWorld world;
        Board board;
        AimController aim;
        Fx fx;
        Sfx sfx;
        Camera cam;
        Vector3 camBase;

        readonly List<Blob> blobs = new List<Blob>();
        readonly Stack<Blob> pool = new Stack<Blob>();
        readonly HashSet<long> prevContacts = new HashSet<long>();
        readonly HashSet<long> curContacts = new HashSet<long>();

        class MergeJob { public Blob a, b; public float t; public int chain; public bool toLane; }
        readonly List<MergeJob> merges = new List<MergeJob>();
        /// <summary>Diagnostics (bot): the blob is half of a running merge.</summary>
        public bool InMergeJob(Blob b) => merges.Exists(m => m.a == b || m.b == b);

        float accumulator;
        int nextId = 1;
        int score, best;
        float dangerTimer;
        bool gameOver;
        bool splashing; // the launch splash covers the screen: no aiming yet
        float gameOverAt;
        Vector2 aimDir;
        float aimPower;
        bool aimAtMax;

        // UI
        HudView hud;
        CreatureBarView bar;
        float lastAspect;
        float hudHeightScale = 1f;   // camera size / the height-fit size: > 1 only when the screen is so narrow the width limits
        Vector2? hudRefResolution;   // the HUD prefab's CanvasScaler reference, kept so the override never compounds

        public SoftBodyWorld World => world;
        public Board Board => board;
        /// <summary>Fish scene's lower-HUD creature bar (null in other scenes).</summary>
        public CreatureBarView CreatureBar => bar;
        public AimController Aim => aim;
        /// <summary>Runtime-only switch (AdSim reference stills): equal blobs touch without merging.</summary>
        [System.NonSerialized] public bool MergesEnabled = true;
        public IReadOnlyList<Blob> Blobs => blobs;
        public bool IsGameOver => gameOver;
        /// <summary>Smoothed cost of one physics tick (ms), for profiling.</summary>
        public float PhysicsMs { get; private set; }
        public int PhysicsTicks { get; private set; }
        /// <summary>(new value, happened in the top lane)</summary>
        public event System.Action<int, bool> Merged;
        public event System.Action AimMaxReached;
        /// <summary>Current camera shake displacement (world units), for diagnostics.</summary>
        public float ShakeMagnitude => fx != null ? fx.ShakeOffset.magnitude : 0f;
        readonly System.Diagnostics.Stopwatch physicsWatch = new System.Diagnostics.Stopwatch();

        public static int Tier(int value) => Mathf.RoundToInt(Mathf.Log(Mathf.Max(1, value), 2f));
        public float RadiusForTier(int tier) => FishMode
            ? Mathf.Min(maxRadius, baseRadius + fishTierStep * Mathf.Min(tier, fishBigFromTier) + fishBigTierStep * Mathf.Max(0, tier - fishBigFromTier))
            : Mathf.Min(maxRadius, baseRadius + radiusPerTier * tier);
        public Color ColorForTier(int tier)
        {
            var pc = Theme.pieceColors;
            if (pc == null || pc.Length == 0) pc = BoardTheme.Default.pieceColors;
            return pc[Mathf.Clamp(tier, 0, pc.Length - 1)];
        }
        /// <summary>This scene's colours (falls back to the original purple look).</summary>
        public BoardTheme Theme => boardTheme != null ? boardTheme : BoardTheme.Default;
        public bool Hex => pieceShape == PieceShape.Hexagon;
        public bool FishMode => pieceShape == PieceShape.FishBubble;
        /// <summary>Lane blobs merge with each other by themselves (+ magnet). Fish: only if <see cref="laneAutoMerge"/>.</summary>
        public bool LaneAutoMerge => !FishMode || laneAutoMerge;
        /// <summary>Creature bar + lifetime progress: Fish scene with progression enabled. Other scenes never run any of it.</summary>
        public bool ProgressionOn => FishMode && progression.enabled;
        /// <summary>Queue rules (spawn table, difficulty director, streak; spec §0b): off while the ad director scripts the lane, and in ad takes.</summary>
        public bool ProgressionRules => ProgressionOn && lane.ValueOverride == null && !AdTake;
        /// <summary>Smallest lane value: 2^WindowStart in the Fish scene (this game's window: the creatures off the bar never
        /// spawn), unless the ad director scripts the lane; 1 elsewhere.</summary>
        public int SpawnFloor => ProgressionLive && lane.ValueOverride == null ? ProgressionMath.SpawnFloor(progression.WindowStart) : 1;
        /// <summary>ProgressionOn and the saved progress + bar were set up at Awake. A live toggle in Play mode (progression
        /// on, or the shape switched to fish) never runs on unloaded state: it would save defaults over the real progress.</summary>
        public bool ProgressionLive => progressionReady && ProgressionOn;
        bool progressionReady, progressionDirty;
        /// <summary>Set by the ad director: no queue rules, no save, no orca panel (the bar and new-fish celebrations still play).</summary>
        [System.NonSerialized] public bool AdTake;
        /// <summary>(value) - a creature never made before appeared (lane spawn or merge).</summary>
        public event System.Action<int> NewFish;
        /// <summary>(new window start) - the creature bar starts sliding one creature along.</summary>
        public event System.Action<int> WindowShifted;
        /// <summary>(count, value) - blobs of the creature that dropped off the bar were popped.</summary>
        public event System.Action<int, int> Culled;
        /// <summary>The orca celebration started.</summary>
        public event System.Action OrcaCelebration;
        bool celebrating, celebrationFrozen;
        int enableAimFrame = -1;
        LevelCompleteView orcaView; // built hidden once the hammerhead exists (orca not made yet), played once
        /// <summary>Real seconds (GameTime) since the orca merge; drives the slow-mo, the panel and its animation.</summary>
        public float WinClock { get; private set; }
        /// <summary>The orca panel is up and accepts the tap that resumes the game.</summary>
        public bool CelebrationTapReady => celebrationFrozen && WinClock >= celebration.tapAfter;

        Sprite[] fishSprites;
        Material fishMaterial;
        public Material FishMaterial => fishMaterial != null ? fishMaterial : (fishMaterial = Resources.Load<Material>("Materials/FishSprite"));

        /// <summary>Fish for a value: Resources/Fish/fish_00 (1) … fish_16 (65536+).</summary>
        public Sprite FishSprite(int tier)
        {
            fishSprites ??= new Sprite[ProgressionMath.TopTier + 1];
            int i = Mathf.Clamp(tier, 0, fishSprites.Length - 1);
            if (fishSprites[i] == null)
            {
                string path = "Fish/fish_" + i.ToString("00");
                fishSprites[i] = Resources.Load<Sprite>(path);
                if (fishSprites[i] == null)
                {
                    var tex = Resources.Load<Texture2D>(path);
                    if (tex != null) fishSprites[i] = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), tex.width);
                }
            }
            return fishSprites[i];
        }

        /// <summary>
        /// Fish scene starting values (used when the scene is generated): round soft bubbles (the original balloon
        /// physics without lumps) with a sea creature inside. Tune freely in the Fish scene.
        /// </summary>
        public void ApplyFishPreset()
        {
            pieceShape = PieceShape.FishBubble;
            silhouetteNoise = 0.02f; // round bubbles, a hint of wobble
            elongation = 0.05f;
            pressure = 7f;           // a bubble stays round (measured: 6 -> w/h 1.1, 10 -> 1.0); squash on impacts only
            largeBlobPressureScale = 1f;
            maxSquash = 1.5f;
            minVolume = 0.75f;       // never crushed flat under the pile
            var am = GetComponent<AimController>();
            if (am != null) { am.arrowFade = true; am.arrowColor = Color.white; am.arrowStartAlpha = 0.8f; am.arrowEndAlpha = 0.3f; }
        }
        public float ShapeStiffnessForTier(int tier) => Mathf.Lerp(shapeStiffnessSmall, shapeStiffnessLarge, tier / 9f);
        public float PressureForTier(int tier) => pressure * Mathf.Lerp(1f, largeBlobPressureScale, tier / 9f);
        public float MaxSquashNow => maxSquash;
        public float RigidityNow => Hex ? hexRigidity : 0f;
        /// <summary>
        /// Hexa scene starting values (used when the scene is generated): hexagon pieces and the physics that suits them.
        /// Everything else starts like the blob game; tune freely in the Hexa scene.
        /// </summary>
        public void ApplyHexaPreset()
        {
            pieceShape = PieceShape.Hexagon;
            pressure = 1f;               // only fills dents; the hexagon comes from the spring-back
            largeBlobPressureScale = 1f;
            shapeStiffnessSmall = shapeStiffnessLarge = 0.08f; // spring-back speed (sweep: 0.05 dents, 0.12 stiff)
            maxSquash = 1.6f;
            hexRigidity = 1f;
            // the user's tuned Hexa values (Hexa.unity, 2026-10-08)
            damping = 0.2f;
            deformationDamping = 0.06f;
            edgeStiffness = 0.9f;
            areaStiffness = 0.005f;
            wallBounce = 0.06f;  // user: half the bounce again (0.2 -> 0.12 -> 0.06; blob 0.15 -> 0.08 -> 0.04)
            bumperBounce = 0.15f;
            blobBounce = 0.04f;
            hexRigidWhenLanded = true;
            hexLandingBounce = 0.2f;
            laneScale = 0.85f;
            landedScale = 1.3f;
            blobLabelHeight = 0.432f; // +20% numbers on hexagons (user)
        }

        /// <summary>Skin points of a new piece (hexagons need a multiple of 6).</summary>
        public int PointsPerPiece => Hex ? 6 * hexPointsPerSide : pointsPerBlob;

        // ================================================================== setup
        void Awake()
        {
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
            Time.timeScale = 1f;
            best = PlayerPrefs.GetInt(BestKey, 0);

            if (material == null) material = new Material(Shader.Find("BlobMerge/VertexColor"));

            world = new SoftBodyWorld();
            ApplyPhysicsSettings();
            board = new Board(this, world);

            var fxGo = new GameObject("Fx");
            fx = fxGo.AddComponent<Fx>();
            fx.Init(material);
            fx.KillBelowY = board.FloorY;
            Sfx.Bubbly = FishMode; // Fish theme: bubbly sound set (must be set before the component wakes)
            sfx = fxGo.AddComponent<Sfx>();
            sfx.StartAmbience(ambienceVolume);

            if (ProgressionOn) progression.Load(); // before the bar is built (every game's bar starts at the fry)
            SetupCamera();
            TextStyle.Init(font);
            BuildUI();
            progressionReady = ProgressionOn && bar != null;
            SetupAim();
            if (splashing) aim.InputEnabled = false;
            lane.Init(this, board);
        }

        void Start() => PrepareOrcaView();

        /// <summary>
        /// The orca's first-ever celebration, built hidden (textures + ~200 UI objects) as soon as the orca is the next
        /// creature (hammerhead made), so the orca merge frame only shows it. Earlier games never pay for it.
        /// now = build regardless (the orca arrived before the panel was pre-built, e.g. a debug state).
        /// </summary>
        void PrepareOrcaView(bool now = false)
        {
            if (orcaView != null || !ProgressionLive || !celebration.enabled) return;
            if (!now && progression.Highest != ProgressionMath.TopTier - 1) return;
            orcaView = LevelCompleteView.Create(this, hud, celebration, "ALL CREATURES", "FOUND!",
                                                FishSprite(ProgressionMath.TopTier),
                                                FishLook.Name(ProgressionMath.TopTier).ToUpperInvariant() + " UNLOCKED!",
                                                Theme.celebrationPrefab);
        }

        /// <summary>Progress is saved outside the physics tick (PlayerPrefs.Save writes to disk/registry).</summary>
        void SaveProgressIfDirty()
        {
            if (!progressionDirty) return;
            progressionDirty = false;
            progression.Save();
        }

        void OnDestroy() => SaveProgressIfDirty(); // scene reload / quit in the same frame as a new fish

        /// <summary>Copies the inspector physics values into the solver and existing blobs.</summary>
        void ApplyPhysicsSettings()
        {
            world.Gravity = new Vector2(0, -gravity);
            world.Substeps = substeps;
            world.Iterations = iterations;
            world.Damping = damping;
            world.DeformationDamping = deformationDamping;
            world.EdgeStiffness = edgeStiffness;
            world.AreaStiffness = areaStiffness;
            world.MinVolume = minVolume;
            world.Gap = blobGap;
            world.Restitution = wallBounce;
            world.BumperRestitution = bumperBounce;
            world.BodyRestitution = blobBounce;
            world.BounceMinSpeed = bounceMinSpeed;
            world.Friction = wallFriction;
            world.ContactDamping = contactDamping;
            world.MaxSpeed = maxSpeed;
            foreach (var b in blobs)
            {
                if (b.Hex != Hex) continue; // shape switched live: old pieces keep their own settings
                b.Body.ShapeStiffness = ShapeStiffnessForTier(b.Tier);
                b.Body.Pressure = PressureForTier(b.Tier);
                b.Body.MaxSquash = MaxSquashNow;
                b.Body.Rigidity = RigidityNow;
            }
        }

        void SetupAim()
        {
            aim = GetComponent<AimController>();
            if (aim == null) aim = gameObject.AddComponent<AimController>(); // scenes made by older setup
            aim.material = material;
            aim.PredictPath = (p, v, r, dt, n, output) => world.PredictPath(p, v, r, PhysicsStep, dt, n, output);
            aim.PointerCorrect = UnshakePoint;
            aim.PickBlob = PickBlob;
            aim.OnSelect = b =>
            {
                b.SetState(BlobState.Aiming);
                b.Selected = true;
                b.AreaTarget = LaneArea * 1.08f;
                sfx.Select();
            };
            aim.OnAim = (b, d, p) =>
            {
                aimDir = d;
                aimPower = p;
                // max stretch: one kick + haptic when first reached, then a rumble while held there
                bool atMax = p >= aimMaxThreshold;
                if (atMax && !aimAtMax)
                {
                    fx.Shake(aimMaxKick);
                    sfx.MaxPower();
                    AimMaxReached?.Invoke();
                    b.Flash = Mathf.Max(b.Flash, 0.5f);
                }
                if (atMax) fx.Sustain(aimMaxRumble);
                aimAtMax = atMax;
            };
            aim.OnCancel = b =>
            {
                if (b.State == BlobState.Aiming) b.SetState(BlobState.Lane);
                b.Selected = false;
                b.AreaTarget = LaneArea;
                aimPower = 0f;
                aimAtMax = false;
            };
            aim.OnLaunch = Launch;
        }

        /// <summary>Converts a point seen through the shaken/rolled camera to where it is with a steady camera.</summary>
        Vector2 UnshakePoint(Vector2 p)
        {
            Vector2 c = cam.transform.position;
            float a = -cam.transform.eulerAngles.z * Mathf.Deg2Rad; // the roll the camera actually has right now
            Vector2 d = p - c;
            Vector2 local = new Vector2(d.x * Mathf.Cos(a) - d.y * Mathf.Sin(a), d.x * Mathf.Sin(a) + d.y * Mathf.Cos(a));
            return (Vector2)camBase + local;
        }

        /// <summary>Inverse of <see cref="UnshakePoint"/> then to screen pixels: where a steady-world point is on screen now.</summary>
        public Vector2 SteadyToScreen(Vector2 p)
        {
            float a = cam.transform.eulerAngles.z * Mathf.Deg2Rad;
            Vector2 d = p - (Vector2)camBase;
            Vector2 seen = (Vector2)cam.transform.position + new Vector2(d.x * Mathf.Cos(a) - d.y * Mathf.Sin(a), d.x * Mathf.Sin(a) + d.y * Mathf.Cos(a));
            return cam.WorldToScreenPoint(seen);
        }

        Blob PickBlob(Vector2 p)
        {
            if (gameOver) return null;
            Blob bestBlob = null;
            float bestD = float.MaxValue;
            foreach (var b in blobs)
            {
                if (b.State != BlobState.Lane) continue;
                float d = Vector2.Distance(p, b.Body.Center);
                if (d < b.CurrentRadius + aim.pickPadding && d < bestD) { bestD = d; bestBlob = b; }
            }
            return bestBlob;
        }

        void SetupCamera()
        {
            cam = Camera.main;
            if (cam == null) cam = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Theme.cameraBackground;
            FitCamera();
        }

        void FitCamera()
        {
            lastAspect = cam.aspect; // follows the screen, or a capture texture (AdSim) when one is set
            float padX = frameThickness + 0.1f;
            float hudTop = hudSpace, bottom = frameThickness + 0.25f + (ProgressionOn ? progressBarSpace : 0f);
            float sizeByH = (boardHeight + hudTop + bottom) * 0.5f;
            float sizeByW = (boardWidth + padX * 2f) * 0.5f / lastAspect;
            cam.orthographicSize = Mathf.Max(sizeByH, sizeByW);
            hudHeightScale = cam.orthographicSize / sizeByH;
            FitHudScale();
            float top = boardHeight * 0.5f + hudTop, bot = -boardHeight * 0.5f - bottom;
            camBase = new Vector3(0, (top + bot) * 0.5f, -10);
            cam.transform.position = camBase;
            board.FitPlate(cam);
        }

        /// <summary>The camera lays the board out in world units, so the HUD's top strip (hudSpace) and bottom band (creature
        /// bar) are fixed fractions of the screen HEIGHT. The prefab's scaler (match 0.5) instead made the canvas shorter on
        /// wide screens (tablet 3:4: 1663 vs 1920 units), squeezing the 276-unit bar and the top bar out of their bands. Match
        /// height with a reference that grows only when the camera is width-limited keeps canvas units = world units
        /// (1080x1920 is unchanged).</summary>
        void FitHudScale()
        {
            var scaler = hud != null ? hud.GetComponent<CanvasScaler>() : null;
            if (scaler == null) return;
            if (hudRefResolution == null) hudRefResolution = scaler.referenceResolution;
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;
            scaler.referenceResolution = new Vector2(hudRefResolution.Value.x, hudRefResolution.Value.y * hudHeightScale);
        }

        // ================================================================== blobs
        Blob GetBlob()
        {
            Blob b;
            if (pool.Count > 0) { b = pool.Pop(); b.gameObject.SetActive(true); }
            else b = new GameObject("Blob").AddComponent<Blob>();
            return b;
        }

        Blob Spawn(int value, Vector2 pos, float startArea)
        {
            var b = GetBlob();
            b.Init(this, value, pos, startArea, nextId++);
            b.DropThrough = false; b.SlowInLane = 0f; // pooled blobs
            blobs.Add(b);
            world.Bodies.Add(b.Body);
            return b;
        }

        void Release(Blob b)
        {
            blobs.Remove(b);
            world.Bodies.Remove(b.Body);
            if (aim.Held == b) aim.Cancel();
            b.gameObject.SetActive(false);
            pool.Push(b);
        }

        /// <summary>Debug/test: drop a blob straight into play.</summary>
        public Blob SpawnFree(int value, Vector2 pos)
        {
            var b = Spawn(value, pos, LandedArea);
            b.AreaTarget = LandedArea;
            b.SetState(BlobState.Free);
            b.Body.UseGravity = true;
            return b;
        }

        public void SpawnLaneBlob(int value, Vector2 pos, Vector2 velocity, int side)
        {
            var b = Spawn(value, pos, laneSpawnStartArea * LaneArea);
            b.SetState(BlobState.Lane);
            b.AreaTarget = LaneArea;
            b.Body.Group = LaneGroup;
            b.Body.SetVelocity(velocity, PhysicsStep / substeps);
            b.Flash = 0.6f;
            board.FlashDoor(side);
            if (FishMode) fx.Bubbles(pos + new Vector2(side * 0.3f, 0f), 7, 0.3f, 2f, 0.04f, 0.09f);
            else fx.Splash(pos + new Vector2(side * 0.3f, 0f), b.BaseColor, 8, 2f, 5f, 0.05f, 0.11f, 4f, new Vector2(-side * 3f, 0));
            sfx.Spawn();
            OnProgressValue(value, pos);
        }

        void Launch(Blob b, Vector2 velocity, float power)
        {
            aimAtMax = false;
            fx.Shake(launchShake * power);
            b.Selected = false;
            b.SetState(BlobState.Free);
            b.Body.Group = 0;
            b.Body.IgnoreGroup = LaneGroup; // slip out of the lane cluster without being nudged off-line
            b.Growing = true;               // tweens lane -> landed scale during flight
            b.LaunchY = b.Body.Center.y;
            b.GrowFrom = b.Body.AreaScale;
            b.Body.UseGravity = true;
            b.Body.ExtraAccel = Vector2.zero;
            b.Body.MassScale = 1f;
            b.Body.SetVelocity(velocity, PhysicsStep / substeps);
            Vector2 dir = velocity.normalized;
            b.Body.Stretch(dir, launchStretch * (0.4f + power));
            aimPower = 0f;
            lane.OnThrown();

            float r = b.CurrentRadius;
            if (FishMode) fx.Bubbles(b.Body.Center - dir * r * 0.6f, 5 + Mathf.RoundToInt(power * 6), r * 0.4f, 2f, 0.04f, 0.08f);
            else fx.Splash(b.Body.Center - dir * r * 0.6f, b.BaseColor, 6 + Mathf.RoundToInt(power * 8), 1.5f, 4f, 0.04f, 0.09f, 2f, -dir * 2f);
            fx.Ring(b.Body.Center, new Color(1, 1, 1, 0.35f), r * 0.9f, r * 1.7f, 0.25f, 0.06f);
            sfx.Launch(power, throwSoundVolume);
        }

        public struct ThrowPreview
        {
            public Blob hit;      // first pile blob the thrown blob touches (null = came to rest without touching one)
            public bool merge;    // hit has the same value
            public bool bank;     // rebounded off a side wall first
            public bool settled;  // the outcome is known (touched something or came to rest) within the tick budget
            public Vector2 end;   // thrown blob's centre at that moment
        }

        readonly List<Blob.SimSnapshot> simSnaps = new List<Blob.SimSnapshot>();
        public int PreviewCount { get; private set; }

        /// <summary>
        /// What-if for the ad director: launches <paramref name="b"/> exactly like a real throw (physics only — no FX,
        /// sound, score or lane bookkeeping), runs the real solver until it first touches a pile blob or comes to rest,
        /// then restores every blob. Unlike <see cref="SoftBodyWorld.PredictPath"/> (a round point) this sees the real
        /// shape: hexagon corners/faces, squash, the in-flight grow tween.
        /// </summary>
        public ThrowPreview PreviewThrow(Blob b, Vector2 velocity, float power, int maxTicks = 180)
        {
            PreviewCount++;
            while (simSnaps.Count < blobs.Count) simSnaps.Add(new Blob.SimSnapshot());
            for (int i = 0; i < blobs.Count; i++) blobs[i].SaveSim(simSnaps[i]);
            var res = new ThrowPreview();

            // the physics half of Launch()
            b.State = BlobState.Free;
            b.StateTime = 0f;
            b.Body.Group = 0;
            b.Body.IgnoreGroup = LaneGroup;
            b.Growing = true;
            b.GrowT = 0f;
            b.LaunchY = b.Body.Center.y;
            b.GrowFrom = b.Body.AreaScale;
            b.Body.UseGravity = true;
            b.Body.ExtraAccel = Vector2.zero;
            b.Body.MassScale = 1f;
            b.Body.SetVelocity(velocity, PhysicsStep / substeps);
            b.Body.Stretch(velocity.normalized, launchStretch * (0.4f + power));

            float h = PhysicsStep, lastVx = velocity.x;
            for (int t = 0; t < maxTicks && !res.settled; t++)
            {
                foreach (var o in blobs) o.TickPhysics(h);
                if (b.Growing) TickGrow(b);
                b.StateTime += h;
                if (b.Body.IgnoreGroup == LaneGroup && b.StateTime > launchPassThroughTime) b.Body.IgnoreGroup = -1;
                world.Step(h);

                float vx = b.Body.Velocity.x;
                if (Mathf.Abs(lastVx) > 0.5f && vx * lastVx < 0f && Mathf.Abs(b.Body.Center.x) > board.HalfW - b.CurrentRadius * 1.6f)
                    res.bank = true;
                lastVx = vx;

                foreach (var ct in world.Contacts)
                {
                    var o = ct.A == b.Body ? ct.B.Owner as Blob : ct.B == b.Body ? ct.A.Owner as Blob : null;
                    if (o == null || o.State != BlobState.Free) continue;
                    res.hit = o;
                    res.merge = o.Value == b.Value;
                    res.end = b.Body.Center;
                    res.settled = true;
                    break;
                }
                if (!res.settled && t > 20 && b.Body.Velocity.sqrMagnitude < 0.25f)
                {
                    res.end = b.Body.Center;
                    res.settled = true;
                }
            }

            for (int i = 0; i < blobs.Count; i++) blobs[i].LoadSim(simSnaps[i]);
            return res;
        }

        // scales are linear (size); the soft body works in area, hence squared
        public float LaneArea => laneScale * laneScale;
        float LandedArea => landedScale * landedScale;

        /// <summary>
        /// Smooth start->end scale tween for a thrown blob. Progress is the larger of elapsed time / duration and
        /// how far it has fallen toward the floor, so it's always full-size on touchdown (no grow-after-landing pop).
        /// </summary>
        void TickGrow(Blob b)
        {
            float byTime = b.StateTime / Mathf.Max(0.01f, scaleTweenDuration);
            // the surface it will land on: the floor, or the top of any pile blob underneath it
            float x = b.Body.Center.x, endR = b.Radius * landedScale;
            float surface = board.FloorY;
            foreach (var o in blobs)
            {
                if (o == b || (o.State != BlobState.Free && o.State != BlobState.Merging) || o.Growing) continue;
                if (o.Body.Max.y < b.Body.Center.y && o.Body.Max.x > x - endR && o.Body.Min.x < x + endR)
                    surface = Mathf.Max(surface, o.Body.Max.y);
            }
            float fallSpan = Mathf.Max(0.3f, b.LaunchY - (surface + endR));
            float byFall = (b.LaunchY - b.Body.Center.y) / fallSpan;
            float t = Mathf.Clamp01(Mathf.Max(byTime, byFall));
            b.GrowT = Mathf.Max(b.GrowT, t); // never shrink back (e.g. bouncing up)
            float e = b.GrowT * b.GrowT * (3f - 2f * b.GrowT); // smoothstep
            b.SetAreaDirect(Mathf.Lerp(b.GrowFrom, LandedArea, e));
            if (b.GrowT >= 1f) { b.Growing = false; b.AreaTarget = LandedArea; }
        }

        // ================================================================== loop
        void Update()
        {
            if (Mathf.Abs(cam.aspect - lastAspect) > 0.001f) FitCamera();

            if (celebrating) WinClock += GameTime.UnscaledDelta;
            if (Time.frameCount == enableAimFrame) aim.InputEnabled = true; // a frame after the resume tap (it must not grab a blob)
            SaveProgressIfDirty(); // new fish from last frame's physics
            if (gameOver)
            {
                // the Fish popup restarts from its Play again button only
                if (hud.playAgainButton == null && Input.GetMouseButtonDown(0) && Time.unscaledTime > gameOverAt + 0.8f) Restart();
            }
            else if (celebrationFrozen)
            {
                if (Input.GetMouseButtonDown(0) && CelebrationTapReady) ContinueAfterCelebration();
            }
            else
            {
                ApplyPhysicsSettings(); // inspector changes apply live
                accumulator += Time.deltaTime;
                int steps = 0;
                while (accumulator >= PhysicsStep && steps < 3)
                {
                    physicsWatch.Restart();
                    PhysicsTick(PhysicsStep);
                    PhysicsTicks++;
                    physicsWatch.Stop();
                    PhysicsMs = Mathf.Lerp(PhysicsMs, (float)physicsWatch.Elapsed.TotalMilliseconds, 0.05f);
                    accumulator -= PhysicsStep;
                    steps++;
                }
                if (steps == 3) accumulator = 0f;

                if (celebrating)
                {
                    // orca merge slow-mo (real time), then the panel freezes the game
                    Time.timeScale = WinClock < celebration.slowMoSeconds ? celebration.slowMoScale : 1f;
                    if (WinClock >= celebration.panelAt) FreezeForCelebration();
                }
                else
                {
                    lane.Tick(blobs);
                    CheckDanger();
                }
            }

            float dt = Time.deltaTime;
            foreach (var b in blobs) b.TickVisual(dt);
            board.Tick(GameTime.UnscaledDelta);

            fx.MaxShakeOffset = shakeMaxOffset * shakeScale;
            fx.MaxShakeAngle = shakeMaxAngle * shakeScale;
            fx.ShakeDecay = shakeDecay;
            fx.ShakeFrequency = shakeFrequency;
            cam.transform.position = camBase + (Vector3)fx.ShakeOffset;
            cam.transform.rotation = Quaternion.Euler(0f, 0f, fx.ShakeAngle);

        }

        void PhysicsTick(float h)
        {
            // forces & animation
            foreach (var b in blobs)
            {
                b.TickPhysics(h);
                if (b.Growing && b.State == BlobState.Free) TickGrow(b);
                if (b.State == BlobState.Free) TickDropThrough(b, h);
                if (b.DropThrough) b.Body.IgnoreGroup = LaneGroup;
                else if (b.Body.IgnoreGroup == LaneGroup && b.StateTime > launchPassThroughTime) b.Body.IgnoreGroup = -1;
                if (b.State == BlobState.Lane || b.State == BlobState.Aiming)
                    lane.ApplyForces(b, b.State == BlobState.Aiming ? aimDir : Vector2.zero, b.State == BlobState.Aiming ? aimPower : 0f,
                                     LaneAutoMerge ? blobs : null); // null = no same-value magnet
            }
            for (int i = merges.Count - 1; i >= 0; i--) TickMerge(merges[i], h, i);
            if (Hex && hexRigidWhenLanded)
                foreach (var b in blobs)
                {
                    if (!b.Hex) continue;
                    if (b.Landed && b.State == BlobState.Free) b.LandedTime += h;
                    float settle = b.Landed && hexSettleTime > 0f ? Mathf.Clamp01(b.LandedTime / hexSettleTime) : b.Landed ? 1f : 0f;
                    b.Body.ShapeStiffness = Mathf.Lerp(ShapeStiffnessForTier(b.Tier), hexLandedStiffness, settle);
                }

            world.Step(h);

            // contacts: impacts + merges
            curContacts.Clear();
            foreach (var ct in world.Contacts)
            {
                var a = (Blob)ct.A.Owner;
                var b = (Blob)ct.B.Owner;
                long key = Key(ct.A.Id, ct.B.Id);
                curContacts.Add(key);
                bool fresh = !prevContacts.Contains(key);

                if (fresh)
                {
                    float rel = (ct.A.Velocity - ct.B.Velocity).magnitude;
                    if (rel > impactFxSpeed) ImpactFx(a, ct.Point, rel);
                }
                TryMerge(a, b);
                if (a.State == BlobState.Free && b.State == BlobState.Free) { Land(a); Land(b); }
            }
            prevContacts.Clear();
            foreach (var k in curContacts) prevContacts.Add(k);

            // no lane auto-merge: a just-thrown blob still passes through the lane group (no contacts), so merge it on
            // overlap - but only when it is flying INTO the partner (a twin sitting next to it at launch is touched from
            // the start; a throw down into the tank must not swallow it)
            if (!LaneAutoMerge && mergeInLane)
                foreach (var a in blobs)
                {
                    if (a.State != BlobState.Free || a.Body.IgnoreGroup != LaneGroup) continue;
                    foreach (var o in blobs)
                    {
                        if (o.State != BlobState.Lane || o.Value != a.Value) continue;
                        Vector2 delta = o.Body.Center - a.Body.Center;
                        float d = a.CurrentRadius + o.CurrentRadius + blobGap;
                        if (delta.sqrMagnitude >= d * d) continue;
                        Vector2 rel = a.Body.Velocity - o.Body.Velocity;
                        float closing = Vector2.Dot(rel, delta.normalized);
                        if (closing > ThrowMergeMinClosing && closing > rel.magnitude * ThrowMergeMinCos) { TryMerge(a, o); break; }
                    }
                }

            // hexagons land on the floor (incl. the bottom chamfers)
            foreach (var b in blobs)
                if (b.Hex && !b.Landed && b.State == BlobState.Free && b.Body.Min.y < board.FloorY + chamfer + 0.05f &&
                    (b.Body.Min.y < board.FloorY + 0.05f || b.Body.Impact > 0f))
                    Land(b);

            // hexagons: no rubber rebound off the landing (cap the upward speed for a moment)
            foreach (var b in blobs)
            {
                if (b.Hex && b.Landed && b.LandSpeed > 2f && b.LandedTime < 0.4f && b.State == BlobState.Free)
                {
                    float cap = hexLandingBounce * b.LandSpeed;
                    if (b.Body.Velocity.y > cap) b.Body.AddVelocity(new Vector2(0f, cap - b.Body.Velocity.y), h / substeps);
                }
                b.LastVel = b.Body.Velocity;
            }

            // wall / floor impacts
            foreach (var b in blobs)
                if (b.Body.Impact > impactFxSpeed) ImpactFx(b, b.Body.ImpactPoint, b.Body.Impact);

            // flight trail
            foreach (var b in blobs)
                if (b.State == BlobState.Free && b.Body.Velocity.sqrMagnitude > trailSpeed * trailSpeed && Random.value < 0.6f)
                {
                    Vector2 tp = b.Body.Center - b.Body.Velocity.normalized * b.Radius * 0.7f;
                    if (FishMode) fx.BubbleTrail(tp, b.Radius * 0.13f); // small white air bubbles behind it
                    else fx.Trail(tp, b.BaseColor, b.Radius * 0.22f);
                }
        }

        void Land(Blob b)
        {
            if (b.Landed || !b.Hex) { b.Landed = true; return; }
            b.Landed = true;
            b.LandedTime = 0f;
            b.LandSpeed = Mathf.Max(0f, -b.LastVel.y); // falling into it (a merge pop going up = 0: keeps its hop)
        }

        static long Key(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;

        void ImpactFx(Blob b, Vector2 point, float speed)
        {
            if (Time.time - b.LastImpactFx < 0.15f) return;
            b.LastImpactFx = Time.time;
            float k = Mathf.InverseLerp(impactFxSpeed, 26f, speed);
            if (FishMode) fx.WaterSplash(point, 3 + Mathf.RoundToInt(k * 8), 1.5f, 3f + k * 4f, 0.04f, 0.07f + k * 0.04f);
            else fx.Splash(point, b.BaseColor, 3 + Mathf.RoundToInt(k * 9), 1.5f, 3f + k * 5f, 0.04f, 0.08f + k * 0.05f);
            sfx.Impact(0.25f + k);
            if (k > 0.5f) fx.Shake(0.08f * k);
        }

        // ================================================================== merge
        /// <summary>A value appeared (lane spawn or merge): a creature never made before flies into the creature bar; the
        /// bar's last creature (first time this game) shifts the bar for this game.</summary>
        void OnProgressValue(int value, Vector2 pos)
        {
            if (!ProgressionLive) return;
            var e = progression.OnValue(value);
            if (!e.newFish)
            {
                if (e.shift) bar.QueueShift(); // known creature: no fly-in, the bar just slides (+ cull)
                return;
            }
            if (!AdTake) progressionDirty = true; // saved next Update, not inside the physics tick
            int tier = Mathf.Min(Tier(value), ProgressionMath.TopTier);
            bar.Celebrate(tier, pos, e.shift);
            NewFish?.Invoke(value);
            if (AdTake || celebrating) return;
            if (tier == ProgressionMath.TopTier)
            {
                PrepareOrcaView(true); // normally pre-built since the hammerhead
                if (orcaView != null) BeginOrcaCelebration();
            }
            else PrepareOrcaView();
        }

        /// <summary>The bar slid one creature along: every blob of the dropped creature pops (bubble burst, no score).</summary>
        void OnBarShiftDone(int droppedTier)
        {
            int value = 1 << droppedTier, n = 0;
            for (int i = blobs.Count - 1; i >= 0; i--)
            {
                var b = blobs[i];
                if (b.Value != value || b.State == BlobState.Merging) continue; // a merging pair already became the next creature
                Vector2 c = b.Body.Center;
                float r = b.CurrentRadius;
                fx.WaterSplash(c, 10, 2f, 5f, 0.04f, 0.09f);
                fx.Bubbles(c, 8, r * 0.8f, 2f, 0.04f, 0.09f);
                fx.Ring(c, new Color(1f, 1f, 1f, 0.7f), r * 0.6f, r * 1.8f, 0.3f, 0.08f);
                Release(b);
                n++;
            }
            if (n > 0) sfx.Impact(0.6f);
            Culled?.Invoke(n, value);
        }

        /// <summary>The orca, first ever: slow-mo, then the full-screen celebration freezes play until a tap.</summary>
        void BeginOrcaCelebration()
        {
            celebrating = true;
            WinClock = 0f;
            aim.Cancel();
            aim.InputEnabled = false;
            fx.Shake(0.8f);
            sfx.LevelWin();
            Time.timeScale = celebration.slowMoScale;
            orcaView.Play();
            OrcaCelebration?.Invoke();
        }

        void FreezeForCelebration()
        {
            celebrationFrozen = true;
            Time.timeScale = 0f;
        }

        /// <summary>Tap on the orca panel (bot: called directly): the same game carries on, no reload.</summary>
        public void ContinueAfterCelebration()
        {
            if (!celebrationFrozen) return;
            celebrating = celebrationFrozen = false;
            Time.timeScale = 1f;
            Destroy(orcaView.gameObject);
            orcaView = null;
            enableAimFrame = Time.frameCount + 1;
        }

        bool settingsOpen;

        /// <summary>The Settings popup wants to open: allowed in plain play only (not over the splash, the celebration
        /// or game over, which freeze time themselves). Freezes the game and the aim until <see cref="ResumeFromSettings"/>.</summary>
        public bool TryPauseForSettings()
        {
            if (gameOver || celebrating || splashing || settingsOpen) return false;
            settingsOpen = true;
            aim.Cancel();
            aim.InputEnabled = false;
            Time.timeScale = 0f;
            return true;
        }

        public void ResumeFromSettings()
        {
            if (!settingsOpen) return;
            settingsOpen = false;
            Time.timeScale = 1f;
            enableAimFrame = Time.frameCount + 1; // the Continue tap must not grab a blob
        }

        /// <summary>Reloads the scene after a game over: a fresh board (Fish progress is saved and stays).</summary>
        public void Restart()
        {
            SaveProgressIfDirty();
            Time.timeScale = 1f;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        // a blob being held for aiming never merges (it would vanish from under the finger)
        bool Mergeable(Blob b) => b.State == BlobState.Free || (mergeInLane && b.State == BlobState.Lane);

        void TryMerge(Blob a, Blob b)
        {
            if (!MergesEnabled || a.Value != b.Value || !Mergeable(a) || !Mergeable(b)) return;
            if (!LaneAutoMerge && a.State == BlobState.Lane && b.State == BlobState.Lane) return; // Fish: only by a throw
            bool fromLane = a.State == BlobState.Lane || b.State == BlobState.Lane;
            // chain = how many merges in a row produced these blobs (raises the pop pitch)
            int chain = Mathf.Max(a.StateTime < 1f ? a.Chain : 0, b.StateTime < 1f ? b.Chain : 0);
            a.SetState(BlobState.Merging);
            b.SetState(BlobState.Merging);
            a.Selected = b.Selected = false;
            a.Growing = b.Growing = false;
            a.AreaTarget = a.Body.AreaScale * 0.7f; // shrink relative to current size (lane blobs are small)
            b.AreaTarget = b.Body.AreaScale * 0.7f;
            // the pair ignores each other's collision (and the gap) so they can fuse together
            int g = MergeGroupBase + a.Body.Id;
            a.Body.Group = b.Body.Group = a.Body.IgnoreGroup = b.Body.IgnoreGroup = g;
            merges.Add(new MergeJob { a = a, b = b, chain = chain, toLane = fromLane });
            sfx.Impact(0.3f);
        }

        void TickMerge(MergeJob m, float h, int index)
        {
            m.t += h;
            var A = m.a.Body; var B = m.b.Body;
            float ma = A.Mass, mb = B.Mass;
            Vector2 mid = (A.Center * ma + B.Center * mb) / (ma + mb);
            A.UseGravity = B.UseGravity = false;
            A.ExtraAccel = (mid - A.Center) * mergeAttraction;
            B.ExtraAccel = (mid - B.Center) * mergeAttraction;
            if (m.t < mergeDuration) return;

            merges.RemoveAt(index);
            Vector2 vel = (A.Velocity * ma + B.Velocity * mb) / (ma + mb);
            int value = m.a.Value * 2;
            Color oldCol = m.a.BaseColor;
            Release(m.a);
            Release(m.b);

            bool inLane = m.toLane && mid.y > board.DangerY;
            var n = Spawn(value, mid, mergeStartArea * (inLane ? LaneArea : LandedArea));
            if (inLane)
            {
                // merged at the top: stays floating (lane-sized) in the lane, throwable
                n.SetState(BlobState.Lane);
                n.AreaTarget = LaneArea;
                n.Body.Group = LaneGroup;
                n.Body.SetVelocity(vel * 0.3f, h / substeps);
            }
            else
            {
                n.SetState(BlobState.Free);
                n.AreaTarget = LandedArea;
                n.Body.UseGravity = true;
                n.Body.SetVelocity(vel * 0.4f + Vector2.up * 1.5f, h / substeps);
            }
            n.Flash = 1f;
            n.Chain = m.chain + 1;

            // juice
            float r = n.Radius * (inLane ? laneScale : landedScale); // FX sized to how big the result will be
            int tier = n.Tier;
            if (FishMode)
            {
                // bubble pop: clear water splash + a puff of little air bubbles rising out of it
                fx.WaterSplash(mid, 14 + tier * 2, 3f, 7f + tier * 0.4f, 0.05f, 0.11f + tier * 0.006f);
                fx.Bubbles(mid, 8 + tier, r * 0.8f, 2.5f, 0.05f, 0.1f + tier * 0.006f);
            }
            else
            {
                fx.Splash(mid, oldCol, 12 + tier * 2, 3f, 7f + tier * 0.4f, 0.05f, 0.12f + tier * 0.008f);
                fx.Splash(mid, n.BaseColor, 8 + tier, 2f, 5f, 0.06f, 0.14f);
            }
            fx.Sparkles(mid, 6 + tier, 6f, r);
            fx.Ring(mid, new Color(1, 1, 1, 0.85f), r * 0.6f, r * 2.3f, 0.35f, 0.12f);
            fx.Ring(mid, new Color(n.BaseColor.r, n.BaseColor.g, n.BaseColor.b, 0.6f), r * 0.4f, r * 1.7f, 0.45f, 0.2f);
            fx.FloatingText("+" + value, mid + Vector2.up * (r + 0.2f), Color.white, 0.9f + tier * 0.05f);
            fx.Shake(inLane ? laneMergeShake : pileMergeShake + pileMergeShakePerTier * tier);
            sfx.Merge(tier, m.chain);

            Merged?.Invoke(value, n.State == BlobState.Lane);
            lane.NoteValue(value, true); // R (biggest made this game) for the Fish spawn table + difficulty
            OnProgressValue(value, mid);
            score += value;
            if (score > best) { best = score; PlayerPrefs.SetInt(BestKey, best); }
            hud.SetScore(score, true);
            hud.SetBest(best);
        }

        /// <summary>The lane bumps thrown blobs but is a floating queue, not a shelf: a Free blob that stays slow with its top
        /// up in the lane band (banked off the ceiling onto the spring-held lane blobs, wedged at a wall door) sinks through
        /// the lane until it is below it - it used to rest there above the danger line (false game over, 2026-10-12).</summary>
        void TickDropThrough(Blob b, float h)
        {
            bool inBand = b.Body.Max.y > board.WaterY;
            if (!inBand) { b.DropThrough = false; b.SlowInLane = 0f; return; }
            b.SlowInLane = b.StateTime > launchPassThroughTime && b.Body.Velocity.sqrMagnitude < dropThroughSpeed * dropThroughSpeed
                ? b.SlowInLane + h : 0f;
            if (b.SlowInLane > dropThroughDelay) b.DropThrough = true;
        }

        // ================================================================== game over
        void CheckDanger()
        {
            bool over = false;
            foreach (var b in blobs)
            {
                if (b.State != BlobState.Free || b.StateTime < 1.2f) continue;
                if (b.Body.Center.y + b.Radius * 0.5f > board.DangerY && b.Body.Velocity.sqrMagnitude < 1.5f) { over = true; break; }
            }
            dangerTimer = over ? dangerTimer + Time.deltaTime : Mathf.Max(0f, dangerTimer - Time.deltaTime * 2f);
            board.SetDanger(Mathf.Clamp01(dangerTimer / dangerHoldSeconds * 2f));
            if (dangerTimer >= dangerHoldSeconds) EndGame();
        }

        /// <summary>Test hook (bot "-blobover"): the normal game over, now.</summary>
        public void EndGameNow() => EndGame();

        void EndGame()
        {
            gameOver = true;
            gameOverAt = Time.unscaledTime;
            aim.Cancel();
            aim.InputEnabled = false;
            Time.timeScale = 0f;
            hud.ShowGameOver(score);
            sfx.GameOver();
            sfx.PopUp(); // the popup card appearing
        }

        // ================================================================== UI
        /// <summary>Spawns the HUD prefab (edit Assets/Resources/UI/HUD.prefab), or builds the default layout if it doesn't exist yet.</summary>
        void BuildUI()
        {
            var prefab = hudPrefab != null ? hudPrefab : Resources.Load<HudView>("UI/HUD");
            hud = prefab != null ? Instantiate(prefab) : HudView.Build(UiSprites.RoundedRect, UiSprites.Crown, UiSprites.Gear);
            hud.name = "HUD";
            FitHudScale();
            TextStyle.CopyFrom(hud.WorldTextStyle); // blob numbers / floating scores match the HUD prefab's text style
            hud.SetOffsetY(hudOffsetY);
            hud.SetScore(score, false);
            hud.SetBest(best);
            GameOverPopup.Install(hud, Theme.gameOverPrefab);
            SettingsPopup.Install(this, hud);
            if (hud.playAgainButton != null) hud.playAgainButton.onClick.AddListener(Restart);
            splashing = !AdTake && SplashView.Show(Theme.splashPrefab, hud.transform, () =>
            {
                splashing = false;
                if (aim != null) aim.InputEnabled = true;
            });
            if (ProgressionOn)
            {
                bar = CreatureBarView.Create(this, hud, Theme.creatureBarPrefab);
                bar.ShiftStarted += start => WindowShifted?.Invoke(start);
                bar.ShiftDone += OnBarShiftDone;
            }
            Sfx.HookButtons(hud.transform); // click sound on every HUD / popup button (after their own handlers are wired)
        }
    }
}
