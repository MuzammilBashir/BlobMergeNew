using System;
using System.Collections.Generic;
using UnityEngine;

namespace BlobMerge
{
    /// <summary>
    /// Top lane: blobs squeeze in through the left/right wall doors and float in a loose cluster
    /// (no gravity, soft springs) until the player throws one. Keeps `laneCount` blobs available.
    /// </summary>
    [Serializable]
    public class LaneSpawner
    {
        [Tooltip("How many throwable blobs float in the lane.")]
        public int laneCount = 3;
        public int[] startValues = { 1, 2, 4 };
        public float firstSpawnDelay = 0.25f;
        public float startStagger = 0.3f;
        public float respawnDelay = 0.3f;
        public float entrySpeed = 7f;
        [Tooltip("Lane blobs are heavier so thrown blobs don't bulldoze them.")]
        public float laneMassScale = 3f;

        [Header("Float springs")]
        public float verticalSpring = 70f;
        public float verticalDamping = 11f;
        public float centeringSpring = 2.5f;
        public float horizontalDamping = 3.5f;
        [Tooltip("How far the held blob leans back against the aim (slingshot feel). 0 = stays put.")]
        public float aimPullAccel = 12f;
        [Tooltip("Pull between same-valued lane blobs so they drift together and auto-merge (0 = off). Fish: off unless Game's Lane Auto Merge.")]
        public float sameValueMagnet = 14f;
        [Tooltip("Max distance (world units) at which same-valued lane blobs attract each other.")]
        public float magnetRange = 4f;

        [Header("Value odds (by shots thrown)")]
        public float weight1Start = 1f, weight1Drop = 0.06f, weight1Min = 0.06f;
        public float weight2 = 1f, weight4 = 0.8f;
        public int eightAfterShots = 6;
        public float weight8PerShot = 0.1f, weight8Max = 0.7f;
        public int sixteenAfterShots = 24;
        public float weight16PerShot = 0.05f, weight16Max = 0.4f;

        [Header("Fairness (Fish progression only)")]
        [Tooltip("Never spawn the same value more than this many times in a row (0 = off).")]
        public int maxSameInRow = 2;

        [Header("Spawn table (Fish progression only, spec §0b)")]
        [Tooltip("Creatures in the pool at game start (Fry, Neon Tetra, Clownfish = 3), counted from the bar's first creature.")]
        public int poolStart = 4;
        [Tooltip("Biggest creature that may spawn = the biggest made this game minus this many tiers.")]
        public int poolLag = 2;
        [Tooltip("Max creatures in the pool.")]
        public int poolCap = 8;
        [Tooltip("Each tier up is this × as likely as the one below it.")]
        [Range(0.1f, 1f)] public float poolDecay = 0.88f;
        [Tooltip("Small-fade: once the biggest made this game passes this tier, the smallest creatures spawn less often.")]
        public int poolFadeFrom = 6;
        [Tooltip("Small-fade strength per tier below (1 = no fade).")]
        [Range(0.1f, 1f)] public float poolFade = 0.6f;

        [Header("Difficulty director (Fish progression only)")]
        [Tooltip("Off = the plain spawn table (no refusals, no locks).")]
        public bool difficultyOn = true;
        [Tooltip("Difficulty with the seahorse as the biggest made this game (R = 2).")]
        [Range(0f, 1f)] public float difficultyMin = 0.55f;
        [Tooltip("Difficulty with the whale made this game (R = 16). Base = lerp(min, max, (R-2)/14).")]
        [Range(0f, 1f)] public float difficultyMax = 1f;
        [Tooltip("A new biggest creature this game drops the difficulty by this much (the sawtooth's relief).")]
        [Range(0f, 1f)] public float relaxDepth = 0.15f;
        [Tooltip("Seconds the relief lasts after a new biggest creature.")]
        public float relaxSeconds = 8f;
        [Tooltip("Seconds to climb back from the relief to the base difficulty.")]
        public float climbSeconds = 20f;
        [Tooltip("First seconds of every game: difficulty at most Grace Difficulty.")]
        public float graceSeconds = 20f;
        [Range(0f, 1f)] public float graceDifficulty = 0.35f;
        [Tooltip("Helpfulness of a spawn that merges at once with an exposed tank creature (top of the pile). Refuse chance = D × help.")]
        [Range(0f, 1f)] public float helpExposed = 1f;
        [Tooltip("Helpfulness of a spawn that matches a lane blob (Fish: a deliberate throw merges them; 0.8 back when the magnet merged them for free).")]
        [Range(0f, 1f)] public float helpLane = 0.7f;
        [Tooltip("Candidates drawn per spawn; if every one is refused, the least helpful is taken.")]
        public int refusalTries = 4;
        [Tooltip("Lock length (spawns) at difficulty 0 / 1: the most needed creature does not spawn for round(lerp(min, max, D)) spawns.")]
        public int lockMinSpawns = 6, lockMaxSpawns = 16;
        [Tooltip("Spawns after a lock ends before the next lock may start.")]
        public int lockCooldown = 3;

        [NonSerialized] public int Shots;
        /// <summary>Ad recordings only (AdSim director): returns the next lane value, or 0 for the normal roll.</summary>
        [NonSerialized] public Func<int> ValueOverride;
        /// <summary>Ad recordings only: when set, a spawn waits until it returns true (e.g. the tank has settled).</summary>
        [NonSerialized] public Func<bool> SpawnGate;
        /// <summary>Every lane spawn's value (bot statistics).</summary>
        [NonSerialized] public Action<int> Spawned;
        /// <summary>(tier) - a bigger creature than before in this game was made (spawn or merge): the sawtooth's reset.</summary>
        [NonSerialized] public Action<int> NewGameBest;
        [NonSerialized] public int StreakRerolls, Refusals, Locks, LockedSpawns;
        /// <summary>R: biggest tier made in this game (lane spawns + merges; a scene reload = a new game).</summary>
        public int GameBest { get; private set; }
        /// <summary>Difficulty (0..1) at the last director spawn.</summary>
        public float Difficulty { get; private set; }
        /// <summary>Seconds of this game (scaled clock: stops on game over / the orca panel; reset by the scene reload).</summary>
        public float GameClock => Time.time - gameStartAt;
        readonly List<int> recent = new List<int>();
        readonly List<Blob> tank = new List<Blob>(), targets = new List<Blob>();
        float[] weights;
        float gameStartAt, newBestAt;
        int lockedTier = -1, lockLeft, cooldownLeft;
        GameController game;
        Board board;
        float nextSpawnAt;
        int startIndex;
        int lastSide = 1;

        public void Init(GameController g, Board b)
        {
            game = g;
            board = b;
            Shots = 0;
            startIndex = 0;
            nextSpawnAt = Time.time + firstSpawnDelay;
            StreakRerolls = Refusals = Locks = LockedSpawns = 0;
            recent.Clear();
            gameStartAt = newBestAt = Time.time;
            GameBest = GameController.Tier(game.SpawnFloor); // nothing made yet: the bar's first creature
            lockedTier = -1;
            lockLeft = cooldownLeft = 0;
        }

        public void OnThrown()
        {
            Shots++;
            nextSpawnAt = Mathf.Max(nextSpawnAt, Time.time + respawnDelay);
        }

        public void Tick(List<Blob> blobs)
        {
            float inLane = 0f;
            float minX = float.MaxValue, maxX = float.MinValue;
            foreach (var b in blobs)
            {
                // a pair merging up top becomes one lane blob: count each half
                if (b.State == BlobState.Merging && b.Body.Center.y > board.DangerY) { inLane += 0.5f; continue; }
                if (b.State != BlobState.Lane && b.State != BlobState.Aiming) continue;
                inLane += 1f;
                minX = Mathf.Min(minX, b.Body.Min.x);
                maxX = Mathf.Max(maxX, b.Body.Max.x);
            }
            if (inLane >= laneCount - 0.01f || Time.time < nextSpawnAt) return;
            if (SpawnGate != null && startIndex >= startValues.Length && !SpawnGate()) return;

            int value;
            if (startIndex < startValues.Length)
            {
                value = startValues[startIndex++];
                int floor = game.SpawnFloor;
                if (floor > 1) value = QueueRules.Scale(value, floor, 16 * floor);
                if (game.ProgressionRules) Record(value); // start values count for the streak rule too
            }
            else value = NextValue(blobs);
            NoteValue(value, false);
            Spawned?.Invoke(value);
            float r = game.LaneRadiusFor(value);

            int side;
            if (minX > maxX) side = -lastSide; // no settled lane blobs to measure gaps against
            else
            {
                float gapL = minX + board.HalfW, gapR = board.HalfW - maxX;
                side = Mathf.Abs(gapL - gapR) < 0.4f ? -lastSide : (gapL > gapR ? -1 : 1);
            }
            lastSide = side;

            var pos = new Vector2(side * (board.HalfW - r * 0.55f), board.LaneY);
            game.SpawnLaneBlob(value, pos, new Vector2(-side * entrySpeed, 0f), side);
            nextSpawnAt = Time.time + (startIndex <= startValues.Length && Shots == 0 ? startStagger : 0.12f);
        }

        /// <summary>Per-physics-step forces holding lane blobs in their floating band.</summary>
        public void ApplyForces(Blob b, Vector2 aimDir, float aimPower, List<Blob> all = null)
        {
            var body = b.Body;
            Vector2 c = body.Center, v = body.Velocity;
            float ay = verticalSpring * (board.LaneY - c.y) - verticalDamping * v.y;
            float ax = centeringSpring * (0f - c.x) - horizontalDamping * v.x;

            // magnet: drift toward the nearest same-valued lane blob (never the one being aimed)
            if (all != null && sameValueMagnet > 0f && b.State == BlobState.Lane)
            {
                float best = magnetRange;
                float dir = 0f;
                foreach (var o in all)
                {
                    if (o == b || o.Value != b.Value || o.State != BlobState.Lane) continue;
                    float dx = o.Body.Center.x - c.x;
                    if (Mathf.Abs(dx) < best) { best = Mathf.Abs(dx); dir = Mathf.Sign(dx); }
                }
                if (dir != 0f) ax += dir * sameValueMagnet;
            }
            Vector2 a = new Vector2(ax, ay);
            if (b.State == BlobState.Aiming) a -= aimDir * aimPower * aimPullAccel;
            body.ExtraAccel = a;
            body.UseGravity = false;
            body.MassScale = laneMassScale;
        }

        int NextValue(List<Blob> blobs)
        {
            if (ValueOverride != null)
            {
                int ov = ValueOverride();
                if (ov > 0) return ov;
            }
            if (!game.ProgressionRules)
            {
                int min = game.SpawnFloor; // 1 outside the Fish progression: the original odds, unchanged
                return QueueRules.Scale(Roll(), min, 16 * min);
            }
            int v = 1 << DirectorTier(blobs);
            Record(v);
            return v;
        }

        /// <summary>
        /// Fish (spec §0b): sample the base table for R, then the director refuses helpful candidates with chance D × help
        /// (up to <see cref="refusalTries"/> draws, else the least helpful) and may lock the most needed creature.
        /// </summary>
        int DirectorTier(List<Blob> blobs)
        {
            int ws = GameController.Tier(game.SpawnFloor);
            weights = SpawnTable.Weights(GameBest, ws, TableShape, weights);
            int streakTier = recent.Count > 0 && QueueRules.Streak(recent[recent.Count - 1], recent, maxSameInRow)
                ? GameController.Tier(recent[recent.Count - 1]) : -1;
            if (streakTier >= 0 && weights[streakTier] > 0f) StreakRerolls++;

            if (!difficultyOn)
            {
                Difficulty = 0f;
                int t0 = SpawnTable.Sample(weights, UnityEngine.Random.value, streakTier);
                return t0 >= 0 ? t0 : ws; // empty table (bad tunables): the window's first creature
            }

            float baseD = SpawnTable.Base(GameBest, difficultyMin, difficultyMax);
            Difficulty = SpawnTable.Difficulty(baseD, Time.time - newBestAt, GameClock, relaxDepth, relaxSeconds, climbSeconds,
                                               graceSeconds, graceDifficulty);
            FindTank(blobs);
            UpdateLock(SpawnTable.PoolTop(GameBest, ws, TableShape), ws);
            if (lockedTier >= 0) LockedSpawns++;

            int pick = -1;
            float pickHelp = float.MaxValue;
            int tries = Mathf.Max(1, refusalTries);
            for (int i = 0; i < tries; i++)
            {
                int t = SpawnTable.Sample(weights, UnityEngine.Random.value, streakTier, lockedTier);
                if (t < 0) t = ws; // empty table (bad tunables): the window's first creature
                float h = Help(1 << t, blobs);
                if (h < pickHelp) { pick = t; pickHelp = h; }
                if (UnityEngine.Random.value >= Difficulty * h) return t;
                Refusals++;
            }
            return pick >= 0 ? pick : ws; // every draw refused: the least helpful one
        }

        SpawnTable.Shape TableShape => new SpawnTable.Shape
        {
            start = Mathf.Max(1, poolStart), lag = poolLag, cap = Mathf.Max(1, poolCap), // never an empty pool
            decay = poolDecay, fadeFrom = poolFadeFrom, fade = poolFade
        };

        /// <summary>
        /// Lock bookkeeping, once per director spawn: count down a running lock (then the cooldown), or lock the most
        /// needed creature (the biggest exposed tank creature within the pool) with chance D.
        /// </summary>
        void UpdateLock(int poolTop, int ws)
        {
            if (lockedTier >= 0)
            {
                if (--lockLeft >= 0) return;
                ReleaseLock();
            }
            if (cooldownLeft > 0) { cooldownLeft--; return; }
            int need = -1;
            foreach (var b in targets)
            {
                int t = b.Tier;
                if (t >= ws && t <= poolTop && t > need && Exposed(b)) need = t;
            }
            if (need < 0 || UnityEngine.Random.value >= Difficulty) return;
            lockedTier = need;
            lockLeft = SpawnTable.LockLength(Difficulty, lockMinSpawns, lockMaxSpawns) - 1; // this spawn is the first
            Locks++;
        }

        void ReleaseLock()
        {
            lockedTier = -1;
            lockLeft = 0;
            cooldownLeft = lockCooldown;
        }

        /// <summary>
        /// How much a spawn of <paramref name="v"/> would help right now: <see cref="helpExposed"/> if it merges at once with
        /// an exposed tank creature, else <see cref="helpLane"/> if a lane blob has the same value (a throw merges them), else 0.
        /// </summary>
        float Help(int v, List<Blob> blobs)
        {
            foreach (var b in targets) if (b.Value == v && Exposed(b)) return helpExposed;
            foreach (var b in blobs) if (b.State == BlobState.Lane && b.Value == v) return helpLane;
            return 0f;
        }

        /// <summary>
        /// Tank = every Free blob (merging pairs are their own state, so excluded). Match targets = tank blobs below the
        /// danger line that are at rest (&lt; 2 units/s) or were thrown / made less than 1 s ago (a just-thrown blob or a
        /// fresh merge result counts at once: it is where the next throw can hit).
        /// </summary>
        void FindTank(List<Blob> blobs)
        {
            tank.Clear();
            targets.Clear();
            foreach (var b in blobs)
            {
                if (b.State != BlobState.Free) continue;
                tank.Add(b);
                if (b.Body.Center.y < board.DangerY && (b.StateTime < 1f || b.Body.Velocity.sqrMagnitude < 4f)) targets.Add(b);
            }
        }

        /// <summary>
        /// "Exposed" = a throw can reach it from above: no other tank blob (resting, falling or just thrown) lies on top of
        /// it (higher centre, horizontal offset under 0.75 x the summed radii, and touching: centres closer than 1.25 x the
        /// summed radii).
        /// </summary>
        bool Exposed(Blob b)
        {
            Vector2 c = b.Body.Center;
            float r = b.CurrentRadius;
            foreach (var o in tank)
            {
                if (o == b) continue;
                Vector2 oc = o.Body.Center;
                float rr = r + o.CurrentRadius;
                if (oc.y > c.y && Mathf.Abs(oc.x - c.x) < 0.75f * rr && (oc - c).sqrMagnitude < 1.5625f * rr * rr) return false;
            }
            return true;
        }

        /// <summary>A value appeared this game (lane spawn, or a merge when <paramref name="merged"/>): tracks R, ends locks.</summary>
        public void NoteValue(int value, bool merged)
        {
            int tier = Mathf.Min(GameController.Tier(value), ProgressionMath.TopTier);
            if (merged && tier == lockedTier) ReleaseLock(); // the player built the locked creature himself
            if (tier <= GameBest) return;
            GameBest = tier;
            newBestAt = Time.time;
            NewGameBest?.Invoke(tier);
        }

        /// <summary>Streak bookkeeping for one rules-governed spawn.</summary>
        void Record(int v)
        {
            recent.Add(v);
            if (recent.Count > 8) recent.RemoveAt(0);
        }

        /// <summary>The original shot-based odds (1..16).</summary>
        int Roll()
        {
            int s = Shots;
            float w1 = Mathf.Max(weight1Min, weight1Start - weight1Drop * s);
            float w8 = s >= eightAfterShots ? Mathf.Min(weight8Max, weight8PerShot * (s - eightAfterShots + 1)) : 0f;
            float w16 = s >= sixteenAfterShots ? Mathf.Min(weight16Max, weight16PerShot * (s - sixteenAfterShots + 1)) : 0f;
            float total = w1 + weight2 + weight4 + w8 + w16;
            float r = UnityEngine.Random.value * total;
            if ((r -= w1) < 0) return 1;
            if ((r -= weight2) < 0) return 2;
            if ((r -= weight4) < 0) return 4;
            if ((r -= w8) < 0) return 8;
            return 16;
        }
    }
}
