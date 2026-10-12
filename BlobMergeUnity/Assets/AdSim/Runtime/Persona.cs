using System;
using UnityEngine;

namespace AdSim
{
    /// <summary>
    /// A human player model for one ad take: how they think, err and move. Game-agnostic: distances are fractions of
    /// the capture width W, times are seconds, drag amounts/speeds are in the game's drag unit (degrees for rotation
    /// puzzles). Ranges are Vector2 (x = min, y = max), sampled uniformly. Load with <see cref="FromJson"/>; missing
    /// fields keep these defaults (calibrated on the reference ad, see Documentation~/RESEARCH.md).
    /// </summary>
    [Serializable]
    public class Persona
    {
        public string name = "reference";
        public string description = "";

        [Header("Thinking")]
        public Vector2 thinkTime = new Vector2(0.25f, 0.7f);          // before each move
        public Vector2 afterSuccessPause = new Vector2(0.15f, 0.45f); // after a piece is cleared
        public float lookAtTargetRate = 0.4f;                         // point at the related piece before acting
        public float hesitationRate = 0.15f;                          // drift towards another piece, come back

        [Header("Mistakes")]
        public float probeRate = 0.25f;           // touch a wrong piece first (no-op / blocked)
        public float wrongDirectionRate = 0.1f;   // start the right piece the wrong way, then reverse
        public float missRate = 0.03f;            // press on empty space next to the piece
        public float wanderRate = 0f;             // a real wrong move that changes the board (re-planned after)
        public float bankShotRate = 1f;           // share of moves allowed to use a rebound/trick shot (1 = whenever best)
        public float bankPreference = 0f;         // 0..1: how strongly to prefer a rebound/trick shot when it still succeeds (pro style)
        public float wrongBurst = 0f;             // mean length of a run of wrong moves (0 = independent); clean streaks fill the rest
        public Vector2 probeDrag = new Vector2(10f, 28f);
        public Vector2 wrongDirectionDrag = new Vector2(15f, 40f);
        public Vector2 realizeTime = new Vector2(0.15f, 0.4f);        // pause after noticing a mistake

        [Header("Travel")]
        public float travelSpeed = 1.3f;          // peak speed, W per second
        public float minTravelTime = 0.16f;
        public float overshoot = 0.08f;           // fraction of the travel distance, then a correction
        public float aimError = 0.012f;           // W
        public float curvature = 0.1f;            // sideways bow, fraction of the travel distance

        [Header("Drag")]
        public float dragSpeed = 280f;            // peak drag-unit per second
        public float maxStroke = 170f;            // longest single stroke before regripping
        public float stallRate = 0.15f;           // chance of a mid-stroke stall
        public Vector2 stallTime = new Vector2(0.12f, 0.35f);
        public Vector2 pressDwell = new Vector2(0.04f, 0.1f);
        public Vector2 holdBeforeRelease = new Vector2(0.04f, 0.2f);
        public float followThrough = 0.7f;        // keep moving after a success, then lift

        [Header("Noise")]
        public float jitter = 0.0012f;            // slow hand wobble (Ornstein-Uhlenbeck sigma), W; ~1 px at 720
        public float jitterTau = 0.3f;            // seconds; short = vibrating tremor, keep >= 0.2
        public float idleDrift = 0.012f;          // hover wander, W

        [Header("Presentation")]
        public float handScale = 0.17f;           // visible hand width, fraction of W
        public bool entrance = true;              // hand swoops in, scaled up, at the start
        public float maxSeconds = 150f;           // safety cap for the whole take
        public float tailSeconds = 1.2f;          // keep recording after the end

        public static Persona FromJson(string json)
        {
            var p = new Persona();
            if (!string.IsNullOrWhiteSpace(json)) JsonUtility.FromJsonOverwrite(json, p);
            return p;
        }
    }

    /// <summary>Seeded randomness for reproducible takes.</summary>
    public class Rng
    {
        readonly System.Random _r;
        public Rng(int seed) { _r = new System.Random(seed); }
        public float Value => (float)_r.NextDouble();
        public float Range(float a, float b) => a + (b - a) * Value;
        public float Range(Vector2 r) => Range(r.x, r.y);
        public int Range(int a, int bExclusive) => _r.Next(a, bExclusive);
        public bool Chance(float p) => Value < p;
        public float Sign => Value < 0.5f ? -1f : 1f;

        public float Gauss()
        {
            float u1 = Mathf.Max(1e-6f, Value), u2 = Value;
            return Mathf.Sqrt(-2f * Mathf.Log(u1)) * Mathf.Cos(2f * Mathf.PI * u2);
        }

        public Vector2 Gauss2() => new Vector2(Gauss(), Gauss());
    }
}
