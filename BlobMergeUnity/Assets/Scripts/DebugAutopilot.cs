using System.Collections.Generic;
using UnityEngine;

namespace BlobMerge
{
    /// <summary>
    /// Command-line driven bot for automated visual checks: "-blobauto &lt;seconds&gt; -blobshot &lt;pathPrefix&gt;".
    /// Plays through the real AimController input path (press on a lane blob, drag toward a target, release)
    /// and saves screenshots at fixed moments. Does nothing unless the arguments are present.
    /// </summary>
    [DefaultExecutionOrder(-50)] // sets simulated input before AimController reads it
    public class DebugAutopilot : MonoBehaviour
    {
        float duration = -1f;
        string shotPrefix;
        GameController game;
        AimController aim;
        bool continueMode;
        static float sessionStart = -1f;
        float start, nextShotAt;
        int shotIdx;
        float[] shotTimes;

        enum Phase { Idle, Dragging }
        Phase phase;
        float phaseT, nextThrow;
        Vector2 from, to;

        void Awake()
        {
            var a = System.Environment.GetCommandLineArgs();
            if (System.Array.IndexOf(a, "-blobselftest") >= 0)
            {
                SelfTests.RunAll();
                enabled = false;
                Application.Quit();
                return;
            }
            for (int i = 0; i < a.Length - 1; i++)
            {
                if (a[i] == "-blobauto") float.TryParse(a[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out duration);
                if (a[i] == "-blobshot") shotPrefix = a[i + 1];
                if (a[i] == "-blobover") float.TryParse(a[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out forceOverAt);
                if (a[i] == "-blobpressure") float.TryParse(a[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out pressureOverride);
            }
            continueMode = System.Array.IndexOf(a, "-blobcontinue") >= 0;
            winShots = System.Array.IndexOf(a, "-blobwinshots") >= 0;
            splashShot = System.Array.IndexOf(a, "-blobsplashshot") >= 0;
            sagTest = System.Array.IndexOf(a, "-blobsag") >= 0;
            settingsTest = System.Array.IndexOf(a, "-blobsettings") >= 0;
            if (settingsTest) Sfx.Played += (n, v, p) => { if (n.StartsWith("ui_")) Debug.Log($"[BlobMerge] ui sound: {n} vol={v:F2} (soundOn={GameSettings.SoundOn})"); };
            fillTest = System.Array.IndexOf(a, "-blobfilltest") >= 0;
            twinTest = System.Array.IndexOf(a, "-blobtwintest") >= 0;
            rapid = System.Array.IndexOf(a, "-blobrapid") >= 0;
            ceiling = System.Array.IndexOf(a, "-blobceiling") >= 0;
            doorTest = System.Array.IndexOf(a, "-blobdoortest") >= 0;
            throwTest = System.Array.IndexOf(a, "-blobthrowtest") >= 0;
            SoftBodyWorld.SeparateDeepOverlaps = System.Array.IndexOf(a, "-blobnodeepfix") < 0;
            cornerTest = System.Array.IndexOf(a, "-blobcornertest") >= 0;
            doorTest |= throwTest | cornerTest; // same setup: scripted lane only, no bot throws
            if (System.Array.IndexOf(a, "-blobhex") >= 0) GetComponent<GameController>().ApplyHexaPreset(); // hexagons in any scene
            if (System.Array.IndexOf(a, "-blobfish") >= 0) GetComponent<GameController>().ApplyFishPreset(); // fish bubbles in any scene
            for (int i = 0; i < a.Length - 1; i++)
                if (a[i] == "-hextex" && System.Enum.TryParse(a[i + 1], out HexTexture tex))
                    GetComponent<GameController>().hexTexture = tex; // texture concepts
            if (System.Array.IndexOf(a, "-fishresetgames") >= 0)
                GetComponent<GameController>().progression.DeleteSaved(); // calibration: every game a first-time player (lifetime highest 2)
            if (!fishArgsUsed)
            {
                // Fish progress, first scene load only: later loads (game over) keep the saved lifetime highest
                fishArgsUsed = true;
                var prog = GetComponent<GameController>().progression;
                if (System.Array.IndexOf(a, "-fishreset") >= 0) prog.DeleteSaved();
                for (int i = 0; i < a.Length - 1; i++)
                {
                    var parts = a[i + 1].Split(',');
                    if (a[i] == "-fishstart" && parts.Length >= 1 && int.TryParse(parts[0], out int hi))
                    {
                        prog.Highest = hi; // saved lifetime highest
                        prog.Save();
                        // optional window: the first game only starts there (Start)
                        fishStartWindow = parts.Length == 2 && int.TryParse(parts[1], out int ws) ? ws : -1;
                        fishStartHighest = hi;
                    }
                    if (a[i] == "-fishspawn")
                        foreach (var v in parts) if (int.TryParse(v, out int fv)) fishSpawn.Add(fv);
                }
            }
            fishShots = System.Array.IndexOf(a, "-blobfishshots") >= 0;
            if (pressureOverride > 0f) GetComponent<GameController>().pressure = pressureOverride;
            for (int i = 0; i < a.Length - 1; i++)
                if (a[i] == "-blobgap" && float.TryParse(a[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float gp))
                    GetComponent<GameController>().blobGap = gp;
            for (int i = 0; i < a.Length - 1; i++)
                if (a[i] == "-blobspeed") float.TryParse(a[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out speed);
            if (System.Array.IndexOf(a, "-blobnodirector") >= 0) GetComponent<GameController>().lane.difficultyOn = false;
            for (int i = 0; i < a.Length - 1; i++)
                if (a[i] == "-blobshape" && float.TryParse(a[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float sh))
                {
                    var gc = GetComponent<GameController>();
                    gc.shapeStiffnessSmall = gc.shapeStiffnessLarge = sh;
                }
            if (sagTest) duration = 4f;
            if (doorTest)
            {
                var lane = GetComponent<GameController>().lane; // only the scripted blobs
                lane.startValues = new int[0];
                lane.SpawnGate = () => false;
            }
            if (fillTest)
            {
                duration = 14f;
                var lane = GetComponent<GameController>().lane; // no lane blobs: they would merge with the chain and skew the measurement
                lane.startValues = new int[0];
                lane.SpawnGate = () => false;
            }
            if (duration <= 0f) { enabled = false; return; }
            game = GetComponent<GameController>();
            aim = GetComponent<AimController>();
            aim.SimEnabled = true;
            if (twinTest) { game.lane.startValues = new[] { 2, 2, 2 }; game.lane.ValueOverride = () => 2; } // all lane blobs 2s: twins side by side
            start = Time.time;
            if (sessionStart < 0f) sessionStart = Time.realtimeSinceStartup;
            gameNo++; // every scene load is a new game (continue mode restarts after game over)
            nextDifficultyLog = 30f;
            nextThrow = 1.5f;
            shotTimes = new[] { 1.8f, 2.08f, 2.3f, duration * 0.35f, duration * 0.7f, duration };
        }

        bool sagTest, sagSpawned;
        bool twinTest; int twinThrows; // "-blobtwintest": alternately throw a lane 2 down (no merge) / into its twin (merge)
        // "-blobrapid": fast throws, mostly sideways in the lane (into a same-valued lane blob, or at the wall doors)
        bool rapid;
        // "-blobceiling": the user's style - bank every lane blob off the ceiling at the fish entering from the other door,
        // back to back (random 45-80 deg up toward the far side, random power, ~2 throws/s)
        bool ceiling;
        float nextStuckLog;
        float forceOverAt; // "-blobover <seconds>": end the game then (game-over screen check)
        bool stuckShot;
        bool laneBandShot;
        static int laneBandStuck; // slow Free blobs logged up in the lane band (all games of the session)
        // "-blobdoortest": lane blobs spawned on top of each other at the wall doors (same and different values) - do they separate?
        bool doorTest, throwTest, cornerTest; int doorStep;
        int lastUntangles = SoftBodyWorld.Untangles;
        Blob throwBlob;

        /// <summary>"-blobthrowtest": a weak sideways throw from the lane into a different-valued lane neighbour: the
        /// pass-through (0.25 s) ends with the thrown blob inside the neighbour.</summary>
        void ThrowTest(float t)
        {
            var bd = game.Board;
            int v = game.SpawnFloor;
            if (doorStep == 0 && t > 1f)
            {
                doorStep++;
                game.SpawnLaneBlob(v * 4, new Vector2(0.4f, bd.LaneY), Vector2.zero, 1);
                game.SpawnLaneBlob(v, new Vector2(-1.2f, bd.LaneY), Vector2.zero, -1);
            }
            if (doorStep == 1 && t > 2.5f)
            {
                doorStep++;
                foreach (var b in game.Blobs) if (b.Value == v) throwBlob = b;
                from = throwBlob.Body.Center;
                to = from + new Vector2(1f, 0.3f).normalized * (aim.minDrag + throwBlob.CurrentRadius * 0.6f + 0.05f); // just past the dead zone, a bit up against gravity
                aim.SimDown = true;
                aim.SimPos = from;
                phase = Phase.Dragging;
                phaseT = 0f;
                Debug.Log($"[BlobMerge] throw test: weak sideways throw into the lane neighbour from={from} to={to}");
            }
            if (doorStep == 2 && t > 3.3f)
            {
                doorStep++;
                foreach (var b in game.Blobs) Debug.Log("[BlobMerge] throw test +0.3s: " + Describe(b));
                if (!string.IsNullOrEmpty(shotPrefix)) ScreenCapture.CaptureScreenshot(shotPrefix + "throw_a.png");
            }
            if (doorStep == 3 && t > 6f)
            {
                doorStep++;
                foreach (var b in game.Blobs) Debug.Log("[BlobMerge] throw test 3s: " + Describe(b));
                if (!string.IsNullOrEmpty(shotPrefix)) ScreenCapture.CaptureScreenshot(shotPrefix + "throw_b.png");
            }
        }

        /// <summary>"-blobcornertest": the user's picture - a tank-sized Free piece banked back up into the top corner, on
        /// top of a lane piece at the wall door (left), and over the middle of a lane piece against the ceiling (right).</summary>
        void CornerTest(float t)
        {
            var bd = game.Board;
            int v = game.SpawnFloor;
            float rl = game.RadiusForTier(GameController.Tier(v * 2)) * game.laneScale;
            float rf = game.RadiusForTier(GameController.Tier(v * 4)) * game.landedScale;
            if (doorStep == 0 && t > 1f)
            {
                doorStep++;
                // left: a banked clownfish comes down onto the lane piece at the door and its neighbour (a two-piece shelf);
                // right: onto a single lane piece entering at the door
                float rl1 = game.RadiusForTier(GameController.Tier(v)) * game.laneScale;
                float xDoor = -bd.HalfW + rl, xNext = xDoor + rl + rl1 + game.blobGap;
                game.SpawnLaneBlob(v * 2, new Vector2(xDoor, bd.LaneY), Vector2.zero, -1);
                game.SpawnLaneBlob(v, new Vector2(xNext, bd.LaneY), Vector2.zero, -1);
                game.SpawnFree(v * 4, new Vector2((xDoor + xNext) * 0.5f, bd.TopY - rf));
                float rf8 = game.RadiusForTier(GameController.Tier(v * 8)) * game.landedScale;
                game.SpawnFree(v * 8, new Vector2(bd.HalfW - rf8, bd.TopY - rf8));
                game.SpawnLaneBlob(v * 2, new Vector2(bd.HalfW - rl * 0.55f, bd.LaneY), new Vector2(-game.lane.entrySpeed, 0f), 1);
                Debug.Log("[BlobMerge] corner test: spawned");
            }
            if ((doorStep == 1 && t > 2.5f) || (doorStep == 2 && t > 6f))
            {
                doorStep++;
                foreach (var b in game.Blobs) Debug.Log($"[BlobMerge] corner test t={t:F1}: {Describe(b)} touches=[{Touches(b)}]");
                if (!string.IsNullOrEmpty(shotPrefix)) ScreenCapture.CaptureScreenshot(shotPrefix + "corner" + doorStep + ".png");
            }
        }

        void DoorTest(float t)
        {
            if (!doorTest) return;
            if (throwTest) { ThrowTest(t); return; }
            if (cornerTest) { CornerTest(t); return; }
            var bd = game.Board;
            int v = game.SpawnFloor * 4; // clownfish
            float r = game.RadiusForTier(GameController.Tier(v)) * game.laneScale;
            Vector2 R = new Vector2(bd.HalfW - r * 0.55f, bd.LaneY), L = new Vector2(-R.x, bd.LaneY);
            if (doorStep == 0 && t > 1f)
            {
                doorStep++;
                game.SpawnLaneBlob(v, R, new Vector2(-7f, 0f), 1);  // same value, same door, same frame
                game.SpawnLaneBlob(v, R, new Vector2(-7f, 0f), 1);
                game.SpawnLaneBlob(v, L, new Vector2(7f, 0f), -1);  // different values at the other door
                game.SpawnLaneBlob(v * 2, L, new Vector2(7f, 0f), -1);
                Debug.Log("[BlobMerge] door test: spawned overlapping pairs");
            }
            if (doorStep == 1 && t > 4f)
            {
                doorStep++;
                foreach (var b in game.Blobs) Debug.Log("[BlobMerge] door test 3s: " + Describe(b));
                if (!string.IsNullOrEmpty(shotPrefix)) ScreenCapture.CaptureScreenshot(shotPrefix + "door.png");
            }
        }

        /// <summary>Two non-merging blobs whose centres are closer than 0.6 x their summed radii (interpenetrating).</summary>
        void LogOverlaps(float t, float limit = 0.6f)
        {
            var list = game.Blobs;
            for (int i = 0; i < list.Count; i++)
                for (int j = i + 1; j < list.Count; j++)
                {
                    Blob a = list[i], b = list[j];
                    if (a.State == BlobState.Merging || b.State == BlobState.Merging) continue;
                    float d = Vector2.Distance(a.Body.Center, b.Body.Center), rr = a.CurrentRadius + b.CurrentRadius;
                    if (d < limit * rr)
                        Debug.Log($"[BlobMerge] OVERLAP t={t:F1} d/r={d / rr:F2} A[{Describe(a)}] B[{Describe(b)}]");
                }
        }

        string Describe(Blob b) =>
            $"{b.Value} {b.State} c=({b.Body.Center.x:F2},{b.Body.Center.y:F2}) v=({b.Body.Velocity.x:F2},{b.Body.Velocity.y:F2}) " +
            $"grav={b.Body.UseGravity} acc=({b.Body.ExtraAccel.x:F1},{b.Body.ExtraAccel.y:F1}) grp={b.Body.Group} ign={b.Body.IgnoreGroup} " +
            $"mass={b.Body.MassScale:F1} job={game.InMergeJob(b)} st={b.StateTime:F1} scale={Mathf.Sqrt(b.Body.AreaScale):F2} " +
            $"grow={b.Growing} landed={b.Landed}";

        /// <summary>Blobs within reach of b (centre offset from b, state, value, overlap depth = radius sum - distance).</summary>
        string Touches(Blob b)
        {
            var sb = new System.Text.StringBuilder();
            var bd = game.Board;
            if (b.Body.Max.y > bd.TopY - 0.05f) sb.Append("CEILING ");
            if (b.Body.Min.x < -bd.HalfW + 0.05f) sb.Append("WALL-L ");
            if (b.Body.Max.x > bd.HalfW - 0.05f) sb.Append("WALL-R ");
            foreach (var o in game.Blobs)
            {
                if (o == b) continue;
                Vector2 d = o.Body.Center - b.Body.Center;
                float depth = b.CurrentRadius + o.CurrentRadius - d.magnitude;
                if (depth > -0.15f)
                {
                    int inside = 0; // b's skin points inside o: a hooked/folded pair has many
                    for (int k = 0; k < b.Body.N; k++) if (o.Body.Contains(b.Body.P[k])) inside++;
                    sb.Append($"{o.Value}{o.State.ToString()[0]} off=({d.x:F2},{d.y:F2}) depth={depth:F2} in={o.Body.Contains(b.Body.Center) || b.Body.Contains(o.Body.Center)} ptsIn={inside}; ");
                }
            }
            return sb.ToString();
        }

        /// <summary>Tank fill: area of the Free blobs below the danger line / the tank area below it.</summary>
        float TankFill()
        {
            var bd = game.Board;
            float covered = 0f;
            foreach (var b in game.Blobs)
                if (b.State == BlobState.Free && b.Body.Center.y < bd.DangerY) covered += b.Body.Area();
            return covered / (2f * bd.HalfW * (bd.DangerY - bd.FloorY) - game.chamfer * game.chamfer);
        }

        /// <summary>Every second: blobs that are not where their state says (stuck above the danger line, a merge that
        /// should have finished, a lane blob far off the lane line).</summary>
        void WatchStuck(float t)
        {
            if (game.IsGameOver || t < nextStuckLog) return;
            nextStuckLog = t + 1f;
            LogOverlaps(t);
            var bd = game.Board;
            foreach (var b in game.Blobs)
            {
                bool slowHigh = (b.State == BlobState.Free || b.State == BlobState.Merging) && b.Body.Center.y > bd.DangerY &&
                                b.Body.Velocity.sqrMagnitude < 1.5f && b.StateTime > 0.5f;
                bool lateMerge = b.State == BlobState.Merging && b.StateTime > 0.3f;
                bool offLane = b.State == BlobState.Lane && b.StateTime > 1f && Mathf.Abs(b.Body.Center.y - bd.LaneY) > 0.5f;
                bool inLaneBand = slowHigh && b.State == BlobState.Free && b.Body.Center.y > bd.WaterY - 1f; // the user's bug: up among the lane
                if (inLaneBand)
                {
                    laneBandStuck++;
                    if (ceiling && nextThrow < t + 4f) { nextThrow = t + 4f; Debug.Log($"[BlobMerge] LANEBAND hands off 4 s t={t:F1}"); } // does it come down by itself?
                }
                if (inLaneBand && !laneBandShot && !string.IsNullOrEmpty(shotPrefix))
                {
                    laneBandShot = true; // one per game
                    ScreenCapture.CaptureScreenshot(shotPrefix + "laneband" + gameNo + ".png");
                }
                if (slowHigh && !stuckShot && !string.IsNullOrEmpty(shotPrefix))
                {
                    stuckShot = true; // one per game (scene load)
                    ScreenCapture.CaptureScreenshot(shotPrefix + "stuck" + gameNo + ".png");
                }
                if (slowHigh || lateMerge || offLane)
                    Debug.Log($"[BlobMerge] STUCK t={t:F1} {(inLaneBand ? "LANEBAND " : "")}{(slowHigh ? "slowHigh " : "")}{(lateMerge ? "lateMerge " : "")}{(offLane ? "offLane " : "")}" +
                              $"{Describe(b)} r={b.CurrentRadius:F2} laneY={bd.LaneY:F2} dangerY={bd.DangerY:F2} halfW={bd.HalfW:F2} fill={TankFill():P0} " +
                              $"touches=[{Touches(b)}]");
            }
        }
        bool fillTest, fillLogged;
        int fillSpawned;
        float pressureOverride = -1f;
        // "-blobspeed 2": game clock runs this many times faster (physics still steps at 1/60 s, max 3 ticks per frame)
        float speed = 1f;
        // calibration (spec §0b): game number in this session, periodic difficulty log + "late" screenshot
        static int gameNo;
        bool overUiShot;
        float nextDifficultyLog;
        int ticksAtLog;
        float tAtLog;
        static bool fishArgsUsed;
        // "-fishstart h,w": first game's bar window w and game highest h (applied in Start, after Progression.Load)
        int fishStartWindow = -1, fishStartHighest;
        // "-fishspawn 512,512": drop these side by side at 1 s (they touch and merge: a quick way to a new creature)
        readonly List<int> fishSpawn = new List<int>();
        bool fishSpawned;

        // "-blobfishshots": <shotPrefix>fish<k>_<0..3>.png at these seconds after every new fish / window shift (event k)
        bool fishShots;
        int fishEvents;
        static readonly float[] FishShotTimes = { 0.1f, 0.4f, 0.8f, 1.4f };
        readonly List<(float at, string name)> fishShotsDue = new List<(float, string)>();

        void QueueFishShots()
        {
            if (!fishShots || string.IsNullOrEmpty(shotPrefix)) return;
            int k = fishEvents++;
            for (int i = 0; i < FishShotTimes.Length; i++)
                fishShotsDue.Add((Time.realtimeSinceStartup + FishShotTimes[i], shotPrefix + "fish" + k + "_" + i + ".png"));
        }

        /// <summary>One capture per frame (ScreenCapture keeps only the last request of a frame).</summary>
        void CaptureFishShots()
        {
            for (int i = 0; i < fishShotsDue.Count; i++)
            {
                if (Time.realtimeSinceStartup < fishShotsDue[i].at) continue;
                ScreenCapture.CaptureScreenshot(fishShotsDue[i].name);
                Debug.Log($"[BlobMerge] fish shot {System.IO.Path.GetFileName(fishShotsDue[i].name)}");
                fishShotsDue.RemoveAt(i);
                return;
            }
        }

        void SpawnFishPair(float t)
        {
            if (fishSpawned || fishSpawn.Count == 0 || t < 1f) return;
            fishSpawned = true;
            float x = -0.5f * (fishSpawn.Count - 1);
            foreach (var v in fishSpawn)
            {
                float r = game.RadiusForTier(GameController.Tier(v)) * game.landedScale;
                game.SpawnFree(v, new Vector2(x * r * 2.02f, game.Board.FloorY + r + 0.4f));
                x += 1f;
            }
            Debug.Log($"[BlobMerge] fish spawn {string.Join(",", fishSpawn)} t={t:F1}");
        }

        // "-blobwinshots": orca celebration frames at these WinClock times (<shotPrefix>win0..5.png)
        bool winShots, won;
        bool splashShot;
        int winShotIdx;
        static readonly float[] WinShotTimes = { 0.1f, 0.4f, 0.9f, 1.3f, 1.7f, 2.6f };
        bool WinShotsPending => winShots && won && !string.IsNullOrEmpty(shotPrefix) && winShotIdx < WinShotTimes.Length;

        void CaptureWinShots()
        {
            if (!WinShotsPending || string.IsNullOrEmpty(shotPrefix) || game.WinClock < WinShotTimes[winShotIdx]) return;
            ScreenCapture.CaptureScreenshot(shotPrefix + "win" + winShotIdx + ".png");
            Debug.Log($"[BlobMerge] win shot {winShotIdx} at winClock={game.WinClock:F2}s timeScale={Time.timeScale:F2} gameOver={game.IsGameOver}");
            winShotIdx++;
        }

        /// <summary>"-blobfilltest": drops the chain a whale attempt must hold (narwhal down to the last bar's first creature) and measures it.</summary>
        void FillTest(float t)
        {
            var bd = game.Board;
            int chain = ProgressionMath.TopTier - ProgressionMath.MaxWindowStart(game.progression.WindowSize);
            while (fillSpawned < chain && t > 0.5f + fillSpawned * 0.8f)
            {
                int v = 1 << (ProgressionMath.TopTier - 1 - fillSpawned);
                float x = ((fillSpawned % 3) - 1) * bd.HalfW * 0.5f;
                game.SpawnFree(v, new Vector2(x, bd.DangerY + 0.3f));
                fillSpawned++;
            }
            if (!fillLogged && t > 12.5f)
            {
                fillLogged = true;
                float covered = 0f, top = float.MinValue;
                foreach (var b in game.Blobs)
                {
                    if (b.State != BlobState.Free) continue;
                    covered += b.Body.Area();
                    top = Mathf.Max(top, b.Body.Max.y);
                }
                float field = 2f * bd.HalfW * (bd.DangerY - bd.FloorY) - game.chamfer * game.chamfer;
                Debug.Log($"[BlobMerge] fill test: covered={covered / field:P0} top={top:F2} danger={bd.DangerY:F2} fits={top < bd.DangerY}");
                if (!string.IsNullOrEmpty(shotPrefix)) ScreenCapture.CaptureScreenshot(shotPrefix + "fill.png");
            }
            if (t > duration) Application.Quit();
        }

        /// <summary>"-blobsag": drop a few blobs on the floor and photograph them at rest (pressure tuning).</summary>
        void SagTest(float t)
        {
            if (!sagSpawned && t > 0.4f)
            {
                sagSpawned = true;
                game.SpawnFree(256, new Vector2(-2.1f, -1f));
                game.SpawnFree(64, new Vector2(1.9f, -1f));
                game.SpawnFree(16, new Vector2(-0.2f, 2f));
                game.SpawnFree(4, new Vector2(2.6f, 2.5f));
            }
            if (shotIdx == 0 && t > 3.5f)
            {
                ScreenCapture.CaptureScreenshot(shotPrefix + "sag_" + game.pressure.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + ".png");
                foreach (var b in game.Blobs)
                    if (b.State == BlobState.Free)
                    {
                        var e = b.Body.ShapeError();
                        Debug.Log($"[BlobMerge] shapeErr {b.Value}: mean={e.x:P1} max={e.y:P1} aspect={b.Body.Aspect():F2}");
                        Debug.Log($"[BlobMerge] sag p={game.pressure:F1} {b.Value}: width/height={(b.Body.Max.x - b.Body.Min.x) / (b.Body.Max.y - b.Body.Min.y):F2} area={b.Body.Area() / b.Body.RestArea:F2}");
                    }
                shotIdx++;
            }
            if (t > 4f) Application.Quit();
        }

        /// <summary>Hexagon theme: how far resting pieces are from a perfect hexagon (fraction of radius).</summary>
        void LogHexShape()
        {
            if (!game.Hex) return;
            float sum = 0f, worst = 0f; int n = 0;
            foreach (var b in game.Blobs)
            {
                if (b.State != BlobState.Free || b.StateTime < 1f || b.Body.Velocity.magnitude > 0.3f) continue;
                var e = b.Body.ShapeError();
                sum += e.x; worst = Mathf.Max(worst, e.y); n++;
            }
            if (n > 0) Debug.Log($"[BlobMerge] hex shape at rest: n={n} meanErr={sum / n:P1} maxErr={worst:P1} peakAspectSoFar={peakAspect:F2} squashEvents={squashEvents}");
        }

        float peakAspect = 1f; int squashEvents; readonly HashSet<Blob> squashed = new HashSet<Blob>();

        // lane queue: every spawned value, in order (Fish progression fairness)
        readonly List<int> spawnSeq = new List<int>();

        void LogSummary()
        {
            Debug.Log($"[BlobMerge] summary: laneMerges={laneMerges} pileMerges={pileMerges} maxStretchKicks={maxKicks} throws={throws} minGap={MinGap():F3} untangles={SoftBodyWorld.Untangles} laneBandStuck={laneBandStuck}");
            LogQueue();
        }

        void LogQueue()
        {
            int maxRun = 0, run = 0;
            for (int i = 0; i < spawnSeq.Count; i++)
            {
                run = i > 0 && spawnSeq[i] == spawnSeq[i - 1] ? run + 1 : 1;
                maxRun = Mathf.Max(maxRun, run);
            }
            Debug.Log($"[BlobMerge] queue: spawns={spawnSeq.Count} maxRun={maxRun} rerolls={game.lane.StreakRerolls} " +
                      $"refusals={game.lane.Refusals} locks={game.lane.Locks} lockedSpawns={game.lane.LockedSpawns} seq={string.Join(",", spawnSeq)}");
        }

        // rebound measurement: speed into the first landing vs. the highest upward speed in the 0.4 s after it
        readonly Dictionary<Blob, Vector2> lastVel = new Dictionary<Blob, Vector2>();
        readonly Dictionary<Blob, (float vin, float vup, float t)> landing = new Dictionary<Blob, (float, float, float)>();
        readonly List<float> rebounds = new List<float>();

        void TrackRebound()
        {
            foreach (var b in game.Blobs)
            {
                Vector2 v = b.Body.Velocity;
                if (b.Landed && b.State == BlobState.Free && !landing.ContainsKey(b) && lastVel.TryGetValue(b, out var pv) && pv.y < -2f)
                    landing[b] = (-pv.y, 0f, Time.time);
                lastVel[b] = v;
            }
            foreach (var key in new List<Blob>(landing.Keys))
            {
                var (vin, vup, t) = landing[key];
                if (t < 0f || key == null || !key.isActiveAndEnabled) continue;
                vup = Mathf.Max(vup, key.Body.Velocity.y);
                if (Time.time - t > 0.4f) { rebounds.Add(vup / vin); landing[key] = (vin, vup, -1f); }
                else landing[key] = (vin, vup, t);
            }
        }

        void LogRebound()
        {
            if (rebounds.Count == 0) return;
            var r = new List<float>(rebounds); r.Sort();
            Debug.Log($"[BlobMerge] rebound: landings={r.Count} median={r[r.Count / 2]:P0} p90={r[(int)(r.Count * 0.9f)]:P0} max={r[r.Count - 1]:P0} (upward speed after landing / speed into it)");
        }

        /// <summary>Peak squash/stretch (aspect) of moving pieces, and how many visibly deformed (> 1.12).</summary>
        void TrackSquash()
        {
            foreach (var b in game.Blobs)
            {
                if (b.State == BlobState.Merging) continue;
                float asp = b.Body.Aspect();
                peakAspect = Mathf.Max(peakAspect, asp);
                if (asp > 1.12f) { if (squashed.Add(b)) squashEvents++; }
                else if (asp < 1.03f) squashed.Remove(b);
            }
        }

        // "-blobsettings": gear -> Settings popup -> Sound off -> Haptics off -> Continue, with a screenshot per state.
        // Runs on the real-time clock (the game is frozen while the popup is open) and restores both switches at the end:
        // the test player shares PlayerPrefs with the Editor on this machine.
        bool settingsTest;
        int setStep;
        float setAt = -1f;

        void SettingsTest()
        {
            float now = Time.unscaledTime;
            if (setAt < 0f) setAt = now;
            var popup = FindAnyObjectByType<SettingsPopup>(FindObjectsInactive.Include);
            string P(string n) => string.IsNullOrEmpty(shotPrefix) ? null : shotPrefix + n + ".png";
            void Shot(string n) { if (P(n) != null) ScreenCapture.CaptureScreenshot(P(n)); }
            void State(string what) => Debug.Log($"[BlobMerge] settings test: {what}: timeScale={Time.timeScale} popupActive={popup != null && popup.gameObject.activeSelf} " +
                $"soundOn={GameSettings.SoundOn} listener={AudioListener.volume} hapticsOn={GameSettings.HapticsOn} " +
                $"slashSound={(popup != null && popup.soundOff != null && popup.soundOff.activeSelf)} slashHaptics={(popup != null && popup.hapticsOff != null && popup.hapticsOff.activeSelf)} " +
                $"prefsSound={PlayerPrefs.GetInt("blobmerge.sound", -9)} prefsHaptics={PlayerPrefs.GetInt("blobmerge.haptics", -9)} aim={aim.InputEnabled}");
            if (popup == null) { if (now - setAt > 3f && setStep == 0) { Debug.Log("[BlobMerge] settings test: NO SettingsPopup in the scene"); setStep = 99; } return; }
            switch (setStep)
            {
                case 0 when now - setAt > 4f:
                    State("before");
                    FindAnyObjectByType<HudView>().settingsButton.onClick.Invoke(); // the gear
                    setStep = 1; setAt = now; break;
                // a screenshot is taken at the end of its frame, so each click waits for the shot of the state before it
                case 1 when now - setAt > 1f: State("open"); Shot("set_open"); setStep = 2; setAt = now; break;
                case 2 when now - setAt > 0.3f: popup.soundButton.onClick.Invoke(); setStep = 3; setAt = now; break;
                case 3 when now - setAt > 0.4f: State("sound off"); Shot("set_sound_off"); setStep = 4; setAt = now; break;
                case 4 when now - setAt > 0.3f: popup.hapticsButton.onClick.Invoke(); setStep = 5; setAt = now; break;
                case 5 when now - setAt > 0.4f: State("both off"); Shot("set_both_off"); setStep = 6; setAt = now; break;
                case 6 when now - setAt > 0.3f: popup.closeButton.onClick.Invoke(); setStep = 7; setAt = now; break;
                case 7 when now - setAt > 0.4f:
                    State("closed");
                    GameSettings.SoundOn = true; GameSettings.HapticsOn = true; // leave the machine's prefs as found
                    State("restored");
                    setStep = 8; break;
            }
        }

        void Update()
        {
            float t = Time.time - start;
            aim.SimDown = aim.SimUp = false;
            if (settingsTest) SettingsTest();
            if (splashShot && SplashView.Elapsed >= 1.2f && !string.IsNullOrEmpty(shotPrefix))
            {
                splashShot = false; // "-blobsplashshot": the launch splash, mid-hold
                ScreenCapture.CaptureScreenshot(shotPrefix + "splash.png");
            }
            if (sagTest) { SagTest(t); return; }
            if (fillTest) { FillTest(t); return; }

            SpawnFishPair(t);
            if (speed > 1f && !game.IsGameOver && Time.timeScale == 1f) Time.timeScale = speed; // GameController resets it to 1 on load
            LogDifficulty(t);
            if (game.CelebrationTapReady && !WinShotsPending)
            {
                Debug.Log($"[BlobMerge] orca celebration: tap to continue t={t:F1}");
                game.ContinueAfterCelebration(); // the bot's "tap": the same game carries on
            }
            if (!game.IsGameOver)
            {
                if (phase == Phase.Idle && t >= nextThrow && !doorTest)
                {
                    if (ChooseShot())
                    {
                        aim.SimDown = true;
                        aim.SimPos = from;
                        phase = Phase.Dragging;
                        phaseT = 0f;
                    }
                    nextThrow = t + (ceiling ? 0.05f : rapid ? 0.6f : 1.25f);
                }
                else if (phase == Phase.Dragging)
                {
                    phaseT += Time.deltaTime;
                    float dragT = ceiling ? 0.2f : 0.4f;
                    aim.SimPos = Vector2.Lerp(from, to, Mathf.SmoothStep(0f, 1f, phaseT / dragT));
                    if (phaseT > dragT + 0.1f) { aim.SimUp = true; phase = Phase.Idle; }
                }
            }

            if (!string.IsNullOrEmpty(shotPrefix) && shotIdx < shotTimes.Length && t >= shotTimes[shotIdx])
            {
                ScreenCapture.CaptureScreenshot(shotPrefix + shotIdx + ".png");
                Debug.Log($"[BlobMerge] t={t:F1} blobs={game.Blobs.Count} physicsMs={game.PhysicsMs:F2} fps={1f / Time.smoothDeltaTime:F0}");
                // rendered digit height of every blob's number (world units) — should all be equal
                var sb = new System.Text.StringBuilder();
                foreach (var bl in game.Blobs)
                {
                    var tl = bl.GetComponentInChildren<TMPro.TMP_Text>();
                    if (tl == null) continue;
                    tl.ForceMeshUpdate();
                    sb.Append($" {bl.Value}({bl.State.ToString()[0]})={tl.textBounds.size.y * tl.transform.lossyScale.y:F3}");
                }
                Debug.Log("[BlobMerge] label heights:" + sb);
                LogHexShape();
                LogRebound();
                if (shotIdx == 0 && game.Blobs.Count > 0)
                {
                    var lbl = game.Blobs[0].GetComponentInChildren<TMPro.TMP_Text>();
                    if (lbl != null)
                        Debug.Log($"[BlobMerge] blob label style: font={lbl.font?.name} material={lbl.fontSharedMaterial?.name} style={lbl.fontStyle} color={lbl.color}");
                }
                shotIdx++;
            }
            CaptureWinShots();
            CaptureFishShots();
            CheckPrediction();
            DoorTest(t);
            if (SoftBodyWorld.Untangles != lastUntangles)
            {
                lastUntangles = SoftBodyWorld.Untangles; // a tangled pair was found this frame: who?
                Debug.Log($"[BlobMerge] UNTANGLE #{lastUntangles} t={t:F1}");
                LogOverlaps(t, 0.8f);
            }
            WatchStuck(t);
            TrackShake();
            TrackSquash();
            TrackRebound();
            if (forceOverAt > 0f && t >= forceOverAt && !game.IsGameOver) game.EndGameNow(); // "-blobover <s>": UI check

            if (game.IsGameOver)
            {
                // time is frozen on game over: capture it once (unscaled clock) and quit
                if (overAt < 0f)
                {
                    overAt = Time.unscaledTime;
                    Debug.Log($"[BlobMerge] game {gameNo}: over fill={TankFill():P0} blobs={game.Blobs.Count}");
                    foreach (var b in game.Blobs)
                        Debug.Log($"[BlobMerge] over: {Describe(b)} area={b.Body.Area() / b.Body.RestArea:F2}");
                    if (!string.IsNullOrEmpty(shotPrefix)) ScreenCapture.CaptureScreenshot(shotPrefix + (continueMode ? "over" + gameNo : "over") + ".png");
                    if (game.ProgressionOn)
                        Debug.Log($"[BlobMerge] game {gameNo}: game over t={t:F1}s R={game.lane.GameBest} throws={game.lane.Shots} " +
                                  $"highest={1 << game.progression.Highest} window={game.progression.WindowStart} D={game.lane.Difficulty:F2} " +
                                  $"locks={game.lane.Locks} refusals={game.lane.Refusals}");
                }
                else if (!overUiShot && Time.unscaledTime - overAt > 0.7f)
                {
                    overUiShot = true; // the game-over popup after its pop-in
                    if (!string.IsNullOrEmpty(shotPrefix)) ScreenCapture.CaptureScreenshot(shotPrefix + (continueMode ? "overui" + gameNo : "overui") + ".png");
                }
                else if (Time.unscaledTime - overAt > 1f)
                {
                    LogSummary(); // every scene run ends with summary + queue, game over or timeout
                    if (continueMode) game.Restart(); // fresh board, Fish progress kept (the bot keeps playing)
                    else Application.Quit();
                }
            }
            if (continueMode ? Time.realtimeSinceStartup - sessionStart > duration + 1.5f : t > duration + 1.5f)
            {
                if (game.ProgressionOn && !game.IsGameOver)
                    Debug.Log($"[BlobMerge] game {gameNo}: end (timeout) t={t:F1}s R={game.lane.GameBest} throws={game.lane.Shots} " +
                              $"D={game.lane.Difficulty:F2} locks={game.lane.Locks} refusals={game.lane.Refusals}");
                LogSummary();
                Application.Quit();
            }
        }

        /// <summary>Fish calibration: every 30 game seconds log D / locks / refusals (+ physics ticks per game second) and
        /// overwrite a "late" screenshot of this game.</summary>
        void LogDifficulty(float t)
        {
            if (!game.ProgressionOn || game.IsGameOver || t < nextDifficultyLog) return;
            float rate = (game.PhysicsTicks - ticksAtLog) / Mathf.Max(0.01f, t - tAtLog);
            ticksAtLog = game.PhysicsTicks;
            tAtLog = t;
            nextDifficultyLog = t + 30f;
            int free = 0;
            foreach (var b in game.Blobs) if (b.State == BlobState.Free) free++;
            Debug.Log($"[BlobMerge] game {gameNo}: difficulty D={game.lane.Difficulty:F2} R={game.lane.GameBest} locks={game.lane.Locks} " +
                      $"refusals={game.lane.Refusals} lockedSpawns={game.lane.LockedSpawns} t={t:F0}s throws={game.lane.Shots} tank={free} " +
                      $"ticks/s={rate:F0}");
            if (!string.IsNullOrEmpty(shotPrefix)) ScreenCapture.CaptureScreenshot(shotPrefix + "g" + gameNo + "_late.png");
        }

        /// <summary>Smallest distance between any two non-merging blob outlines (points vs polygon).</summary>
        float MinGap()
        {
            float best = float.MaxValue;
            var list = game.Blobs;
            for (int i = 0; i < list.Count; i++)
                for (int j = 0; j < list.Count; j++)
                {
                    if (i == j || list[i].State == BlobState.Merging || list[j].State == BlobState.Merging) continue;
                    var a = list[i].Body; var b = list[j].Body;
                    for (int k = 0; k < a.N; k++)
                    {
                        Vector2 p = a.P[k];
                        if (b.Contains(p)) { best = Mathf.Min(best, 0f); continue; }
                        for (int e = 0; e < b.N; e++)
                        {
                            Vector2 e0 = b.P[e], e1 = b.P[(e + 1) % b.N], d = e1 - e0;
                            float tt = Mathf.Clamp01(Vector2.Dot(p - e0, d) / Mathf.Max(1e-8f, d.sqrMagnitude));
                            best = Mathf.Min(best, Vector2.Distance(p, e0 + d * tt));
                        }
                    }
                }
            return best;
        }

        // ---- trajectory preview accuracy check: predicted vs actual centre 0.36 s after launch ----
        float overAt = -1f;
        int laneMerges, pileMerges, maxKicks, throws;
        // peak camera shake in the 0.25 s after an event
        float peakWindow, peak;
        bool peakLane;
        int peakValue;

        void TrackShake()
        {
            if (peakWindow <= 0f) return;
            peak = Mathf.Max(peak, game.ShakeMagnitude);
            peakWindow -= Time.deltaTime;
            if (peakWindow <= 0f)
                Debug.Log(peakValue < 0
                    ? $"[BlobMerge] shake after MAX STRETCH: peak {peak:F3} units"
                    : $"[BlobMerge] shake after {(peakLane ? "lane" : "PILE")} merge -> {peakValue}: peak {peak:F3} units");
        }
        Blob tracked;
        Vector2 predictedAt;
        int launchTick;
        const int CheckDots = 4;
        const int CheckAfterTicks = 11; // 4 dots x 11 substeps (h = 1/240) = 44 substeps = 11 ticks

        void Start()
        {
            if (!enabled) return;
            if (fishStartWindow >= 0 && game.ProgressionLive)
            {
                game.progression.WindowStart = fishStartWindow; // before the bar's first LateUpdate builds its icons
                // as if this game had made everything below the bar's last creature (so making it still shifts)
                game.progression.GameHighest = Mathf.Min(fishStartHighest, fishStartWindow + game.progression.WindowSize - 2);
            }
            game.Merged += (v, lane) =>
            {
                if (lane) laneMerges++; else pileMerges++;
                if (lane && !game.LaneAutoMerge) Debug.Log("[BlobMerge] lane merge by throw"); // Fish: lane pairs never merge by themselves
                Debug.Log($"[BlobMerge] merged -> {v} {(lane ? "IN LANE" : "in play")}");
                peakWindow = 0.25f; peakLane = lane; peakValue = v; peak = 0f;
            };
            game.AimMaxReached += () => { maxKicks++; peakWindow = 0.25f; peakLane = false; peakValue = -1; peak = 0f; };
            game.lane.Spawned += v => spawnSeq.Add(v);
            game.lane.NewGameBest += tier =>
                Debug.Log($"[BlobMerge] game {gameNo}: new R {tier} at t={Time.time - start:F1}s throws={game.lane.Shots}");
            game.NewFish += v =>
            {
                Debug.Log($"[BlobMerge] new fish {v} t={Time.time - start:F1} throws={game.lane.Shots} highest={1 << game.progression.Highest} " +
                          $"window={game.progression.WindowStart}");
                QueueFishShots();
            };
            game.WindowShifted += ws =>
            {
                Debug.Log($"[BlobMerge] window shift -> {ws} t={Time.time - start:F1} floor={game.SpawnFloor}");
                QueueFishShots();
                if (fishShots && !string.IsNullOrEmpty(shotPrefix)) // plus one mid-slide (0.3 s of the 0.6 s slide)
                    fishShotsDue.Add((Time.realtimeSinceStartup + 0.3f, shotPrefix + "fish" + (fishEvents - 1) + "_slide.png"));
            };
            game.Culled += (n, v) => Debug.Log($"[BlobMerge] culled {n} of {v}");
            game.OrcaCelebration += () =>
            {
                won = true;
                Debug.Log($"[BlobMerge] orca celebration t={Time.time - start:F1}");
            };
            var original = aim.OnLaunch;
            aim.OnLaunch = (b, vel, p) =>
            {
                original?.Invoke(b, vel, p);
                growWatch = b;
                var pts = new System.Collections.Generic.List<Vector2>();
                game.World.PredictPath(b.Body.Center, vel, b.CurrentRadius * 0.9f, 1f / 60f, 0.045f, CheckDots, pts);
                if (pts.Count == CheckDots) { tracked = b; predictedAt = pts[CheckDots - 1]; launchTick = game.PhysicsTicks; }
            };
        }

        Blob growWatch;

        /// <summary>Logs when a thrown blob finishes its scale tween and how high above the floor it still was.</summary>
        void CheckGrow()
        {
            if (growWatch == null) return;
            if (!growWatch.gameObject.activeSelf || growWatch.State != BlobState.Free) { growWatch = null; return; }
            if (growWatch.Growing)
            {
                // report if it touches something in play while still growing (the thing we want to avoid)
                foreach (var ct in game.World.Contacts)
                {
                    var other = ct.A == growWatch.Body ? ct.B : ct.B == growWatch.Body ? ct.A : null;
                    if (other == null) continue;
                    var ob = (Blob)other.Owner;
                    if (ob.State != BlobState.Free) continue;
                    Debug.Log($"[BlobMerge] LANDED WHILE GROWING at scale {Mathf.Sqrt(growWatch.Body.AreaScale):F2}");
                    growWatch = null;
                    return;
                }
                if (growWatch.Body.Impact > 0.5f && growWatch.Body.ImpactPoint.y < -game.boardHeight * 0.5f + 1f)
                {
                    Debug.Log($"[BlobMerge] LANDED ON FLOOR WHILE GROWING at scale {Mathf.Sqrt(growWatch.Body.AreaScale):F2}");
                    growWatch = null;
                }
                return;
            }
            float bottom = growWatch.Body.Min.y - (-game.boardHeight * 0.5f);
            Debug.Log($"[BlobMerge] grow done after {growWatch.StateTime:F2}s, bottom {bottom:F2} above floor, scale={Mathf.Sqrt(growWatch.Body.AreaScale):F2}");
            growWatch = null;
        }

        void CheckPrediction()
        {
            CheckGrow();
            if (tracked == null) return;
            if (game.PhysicsTicks - launchTick < CheckAfterTicks) return;
            if (tracked.State == BlobState.Free && tracked.gameObject.activeSelf)
                Debug.Log($"[BlobMerge] aim check: predicted={predictedAt} actual={tracked.Body.Center} error={Vector2.Distance(predictedAt, tracked.Body.Center):F2}");
            tracked = null;
        }

        bool TwinShot()
        {
            foreach (var l in game.Blobs)
                foreach (var o in game.Blobs)
                {
                    if (l == o || l.Value != o.Value || l.State != BlobState.Lane || o.State != BlobState.Lane) continue;
                    float gap = Vector2.Distance(l.Body.Center, o.Body.Center) - l.CurrentRadius - o.CurrentRadius;
                    if (gap > game.blobGap + 0.1f) // not touching yet (lumpy outlines: centre gap a bit above blobGap): nudge them together (no magnet in Fish), try again next turn
                    {
                        Vector2 n = (o.Body.Center - l.Body.Center).normalized;
                        l.Body.AddVelocity(n * 2f, 1f / 240f);
                        o.Body.AddVelocity(-n * 2f, 1f / 240f);
                        return false;
                    }
                    bool side = twinThrows++ % 2 == 1;
                    from = l.Body.Center;
                    to = from + (side ? (o.Body.Center - from).normalized * 1.6f : Vector2.down * 2.5f);
                    Debug.Log($"[BlobMerge] twin test: throw {(side ? "SIDE into" : "DOWN past")} its twin (gap {gap:F3}) laneMergesSoFar={laneMerges}");
                    return true;
                }
            return false;
        }

        /// <summary>"-blobceiling": any lane blob, thrown 45-80 deg up toward the far wall (it banks off the ceiling back
        /// into the lane / at the door on the other side), random power.</summary>
        bool CeilingShot()
        {
            var lanes = new List<Blob>();
            foreach (var l in game.Blobs) if (l.State == BlobState.Lane && l.StateTime > 0.15f) lanes.Add(l);
            if (lanes.Count == 0) return false;
            Blob pick = lanes[Random.Range(0, lanes.Count)];
            if (Random.value < 0.6f) foreach (var l in lanes) if (l.StateTime < pick.StateTime) pick = l; // the one that just came in
            from = pick.Body.Center;
            var bd = game.Board;
            float side = from.x > 0.3f ? -1f : from.x < -0.3f ? 1f : (Random.value < 0.5f ? -1f : 1f); // toward the far side
            Vector2 dir;
            float roll = Random.value;
            if (roll < 0.5f) // bank off the ceiling half way to the other door: comes down on the fish entering there
                dir = (new Vector2((from.x + side * bd.HalfW * Random.Range(0.6f, 1f)) * 0.5f, bd.TopY) - from).normalized;
            else
            {
                float ang = (roll < 0.85f ? Random.Range(45f, 80f) : Random.Range(80f, 89f)) * Mathf.Deg2Rad;
                dir = new Vector2(side * Mathf.Cos(ang), Mathf.Sin(ang));
            }
            float dead = aim.minDrag + pick.CurrentRadius * 0.6f;
            to = from + dir * (dead + (Random.value < 0.5f ? 1.1f : Random.Range(0.15f, 1f)) * (aim.maxDrag + 0.3f)); // half at full power
            throws++;
            return true;
        }

        /// <summary>Prefer throwing a lane blob at a resting blob with the same value; otherwise random.</summary>
        bool ChooseShot()
        {
            Blob pick = null, target = null;
            if (twinTest) return TwinShot();
            if (ceiling) return CeilingShot();
            foreach (var l in game.Blobs)
            {
                if (l.State != BlobState.Lane) continue;
                if (pick == null || Random.value < 0.4f) pick = l;
                foreach (var f in game.Blobs)
                    if (f.State == BlobState.Free && f.Value == l.Value && Random.value < 0.7f) { pick = l; target = f; }
                if (target != null) break;
            }
            if (pick == null) return false;
            // Fish (no lane auto-merge): now and then sling a lane blob into its same-valued lane neighbour
            if (target == null && !game.LaneAutoMerge && Random.value < (rapid ? 0.8f : 0.3f))
                foreach (var l in game.Blobs)
                    foreach (var o in game.Blobs)
                        if (l != o && l.State == BlobState.Lane && o.State == BlobState.Lane && l.Value == o.Value) { pick = l; target = o; }

            from = pick.Body.Center;
            Vector2 goal = target != null ? target.Body.Center : new Vector2(Random.Range(-3f, 3f), Random.Range(-6f, -2f));
            if (rapid && target == null && Random.value < 0.4f) // sideways at a wall door, a bit below the lane
                goal = new Vector2((Random.value < 0.5f ? -1f : 1f) * game.Board.HalfW, game.Board.LaneY - Random.Range(0f, 1f));
            Vector2 dir = (goal - from).normalized;
            float dist = Vector2.Distance(goal, from);
            float len = Mathf.Clamp(dist * 0.4f, 1.3f, aim.maxDrag + 1f);
            // exercise the max-stretch path: the first throw and ~1 in 4 after that go to full power
            if (throws++ == 0 || Random.value < 0.25f) len = aim.maxDrag + aim.minDrag + 2f;
            to = from + dir * len;
            return true;
        }
    }
}
