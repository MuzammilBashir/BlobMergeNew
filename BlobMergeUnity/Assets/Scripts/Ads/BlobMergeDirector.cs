#if UNITY_EDITOR // AdSim runs in the Editor only (its runtime asmdef is constrained to UNITY_EDITOR), so nothing ships in the APK
using System.Collections;
using System.Collections.Generic;
using AdSim;
using UnityEngine;

namespace BlobMerge.Ads
{
    /// <summary>
    /// AdSim adapter: plays BlobMerge on camera with a persona-driven hand, through the real <see cref="AimController"/>
    /// input path (<see cref="AimController.Override"/>), so arrow, trajectory, max-stretch shake and merges are genuine.
    /// Truth source: every candidate throw (lane blob × direction × power) is simulated with the game's own
    /// <c>PredictPath</c>; the first blob the flight touches decides the outcome (same value = merge).
    /// Persona knobs in BlobMerge terms:
    ///  * probeRate: grab another lane blob, pull a little (under the dead zone, so it cancels), let go;
    ///  * wrongDirectionRate: aim off by <c>wrongDirectionDrag</c> degrees first, notice (<c>realizeTime</c>), re-aim;
    ///  * missRate: press empty water next to the blob; wanderRate: a throw that misses — aimed at the real target
    ///    but a few degrees / a bit of power off, so it lands beside it (no target: a poor placement);
    ///  * lookAtTargetRate / hesitationRate: point at the target in the pile / drift to another lane blob first;
    ///  * dragSpeed: 280 = 0.9 W/s peak finger speed while aiming; stallRate/stallTime: pause mid-aim.
    /// Request args (optional): "verbose=1" logs every plan; "prefill=16,8,4" starts with those blobs in the tank.
    /// </summary>
    public class BlobMergeDirector : AdDirector
    {
        GameController game;
        AimController aim;
        bool hadBest, verbose;
        bool verify;          // check planned throws with the real solver (GameController.PreviewThrow)
        const int VerifyTop = 20;
        int verified, rejected;
        int savedBest, throws, merges, maxMerge;
        float prefPower = 0.55f;
        bool allowBank = true;
        int thrown, wandered; // for the wrong-move share
        readonly List<Vector2> path = new List<Vector2>(256);

        struct Shot
        {
            public Blob blob;      // lane blob to throw
            public Vector2 dir;
            public float power;
            public Blob hit;       // first blob the flight touches (null = floor)
            public bool merge;
            public float score;
            public Vector2 end;    // where the flight first touches something
            public bool bank;      // rebounds off a wall on the way
        }

        // ------------------------------------------------------------------ AdDirector
        protected override Camera AcquireCamera()
        {
            game = FindFirstObjectByType<GameController>();
            if (game == null) return null;
            aim = game.Aim;
            var bot = game.GetComponent<DebugAutopilot>();
            if (bot != null) bot.enabled = false;
            return Camera.main;
        }

        protected override void PrepareScene()
        {
            verbose = Request.Arg("verbose") == "1";
            // hexagons: corners/faces rebound unlike PredictPath's round point, so confirm shots with the real physics
            verify = Request.Arg("verify", game.Hex ? "1" : "0") == "1";
            if (float.TryParse(Request.Arg("sfx", "1"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var sv))
                gameSfxVolume = sv; // "sfx=0" mutes the gameplay track
            hadBest = PlayerPrefs.HasKey(GameController.BestKey);
            savedBest = PlayerPrefs.GetInt(GameController.BestKey, 0);
            Random.InitState(Request.seed);

            // Fish: a take runs no queue rules, saves nothing and never shows the orca panel (GameController.AdTake);
            // the creature bar shows the saved window and new-fish celebrations still play (ad content).
            game.AdTake = true;
            SplashView.Dismiss();
            if (game.ProgressionOn)
                Log("fish", $"adTake=1 highest={1 << game.progression.Highest} window={game.progression.WindowStart}");

            // overlay canvases aren't rendered into the capture texture: draw the HUD through the capture camera
            foreach (var c in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                if (c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay)
                {
                    c.renderMode = RenderMode.ScreenSpaceCamera;
                    c.worldCamera = Cam;
                    c.planeDistance = 1f;
                    c.sortingOrder = 1000; // above blobs/FX like the overlay was; the hand (32000) stays on top
                }

            var ptr = Motor.Pointer;
            AimController.Override = () => EndcardActive ? default : new AimController.ScriptedPointer // the endcard's tap must not throw
                { Pressed = ptr.Pressed, Released = ptr.Released, Screen = ptr.Position };
            game.Merged += OnMerged;
            Sfx.Played += OnSfx;
            Completed = true; // until a game over

            // "prefill=16,8,4": start with these blobs resting in the tank (a busier opening shot)
            var fill = Request.Arg("prefill");
            if (!string.IsNullOrEmpty(fill))
            {
                var vals = fill.Split(',');
                if (vals.Length <= 5) // one row: drop all at once
                {
                    for (int i = 0; i < vals.Length; i++)
                        if (int.TryParse(vals[i], out int v))
                            game.SpawnFree(v, new Vector2(Mathf.Lerp(-2.6f, 2.6f, (i + 0.5f) / vals.Length), game.Board.FloorY + 1.5f + (i % 2) * 1.2f));
                }
                else StartCoroutine(DropIn(vals)); // big prefills: one at a time, overlapping spawns explode
            }
            if (Request.Arg("still") == "1") game.MergesEnabled = false; // reference stills keep every blob

            // ad-only lane bias: new lane blobs usually get a value already in the tank, so a merge is (almost) always
            // possible — otherwise ~1 throw in 3 has no match for anyone. "laneBias=0" turns it off.
            laneBias = float.TryParse(Request.Arg("laneBias", "0.85"), System.Globalization.NumberStyles.Float,
                                      System.Globalization.CultureInfo.InvariantCulture, out var lb) ? lb : 0.85f;
            if (laneBias > 0f)
            {
                game.lane.ValueOverride = BiasedLaneValue;
                game.lane.SpawnGate = TankSettled; // pick values after chain merges finish, not mid-cascade
            }
        }

        protected override void Cleanup()
        {
            AimController.Override = null;
            Sfx.Played -= OnSfx;
            if (game != null) { game.lane.ValueOverride = null; game.lane.SpawnGate = null; }
            if (game != null) game.Merged -= OnMerged;
            // a take must not change the player's saved best score
            if (hadBest) PlayerPrefs.SetInt(GameController.BestKey, savedBest);
            else PlayerPrefs.DeleteKey(GameController.BestKey);
            PlayerPrefs.Save();
            Log("summary", $"throws={throws} merges={merges} maxMerge={maxMerge} gameOver={game != null && game.IsGameOver}");
            if (verify) Log("verify", $"previews={game.PreviewCount} confirmed={verified} rejected={rejected}");
        }

        void LateUpdate()
        {
            // a game over freezes time; captured time (and with it the endcard) must keep running
            if (EndcardActive && Time.timeScale == 0f) Time.timeScale = 1f;
        }

        /// <summary>Drops prefill blobs one by one above the pile (spawning them overlapped makes the solver explode).</summary>
        IEnumerator DropIn(string[] vals)
        {
            for (int i = 0; i < vals.Length; i++)
            {
                if (!int.TryParse(vals[i], out int v)) continue;
                game.SpawnFree(v, new Vector2(Mathf.Lerp(-2.4f, 2.4f, (i * 0.618f) % 1f), game.Board.FloorY + 8.5f));
                yield return new WaitForSeconds(0.3f);
            }
        }

        // ------------------------------------------------------------------ gameplay sound track
        // Unity's audio isn't captured with the video: every Sfx play is logged and the track is rebuilt offline from
        // the same samples (Sfx.Render), then mixed in at 0 s with the endcard cues.
        readonly List<(float t, string name, float vol, float pitch)> sounds = new List<(float, string, float, float)>();

        void OnSfx(string name, float volume, float pitch)
        {
            if (EndcardActive) return; // the game keeps running under the endcard: keep it silent
            sounds.Add((Elapsed + 1f / Request.fps, name, volume, pitch)); // frames show the previous update: +1 frame
        }

        protected override void BeforeMix()
        {
            // "music=<prepared wav>;musicVol=0.5": background track under everything, faded out at the cap
            string music = Request.Arg("music");
            if (!string.IsNullOrEmpty(music) && System.IO.File.Exists(music))
            {
                float mv = float.TryParse(Request.Arg("musicVol", "0.5"), System.Globalization.NumberStyles.Float,
                                          System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0.5f;
                AddAudioCue(music, mv, Elapsed + 1f / Request.fps, 0f);
                Log("music", $"{System.IO.Path.GetFileName(music)} at {mv:0.##}");
            }
            bool ambient = game.FishMode && game.ambienceVolume > 0f;
            if (sounds.Count == 0 && !ambient) return;
            var clips = Sfx.RenderAll(); // default + Fish (bubbly) sounds
            var track = new float[Mathf.CeilToInt((Elapsed + 1f) * Sfx.Rate)];
            foreach (var s in sounds)
                if (clips.TryGetValue(s.name, out var src))
                    WavUtil.MixInto(track, Mathf.Approximately(s.pitch, 1f) ? src : WavUtil.Pitch(src, s.pitch), Mathf.RoundToInt(s.t * Sfx.Rate), s.vol);
            if (ambient && clips.TryGetValue("ambience", out var amb)) // Fish: underwater loop under the whole take
            {
                float av = game.ambienceVolume * 0.8f; // × Sfx.volume, like the game
                for (int at = 0; at < track.Length; at += amb.Length) WavUtil.MixInto(track, amb, at, av);
            }
            float peak = 0f;
            foreach (float x in track) peak = Mathf.Max(peak, Mathf.Abs(x));
            float gain = peak > 0.89f ? 0.89f / peak : 1f; // keep stacked pops from clipping
            for (int i = 0; i < track.Length; i++) track[i] *= gain;
            string path = System.IO.Path.ChangeExtension(OutputPath, null) + "_sfx.wav";
            WavUtil.Write(path, track, Sfx.Rate);
            AddAudioCue(path, gameSfxVolume, 0f, 0f);
            Log("sfx", $"{sounds.Count} game sounds -> {System.IO.Path.GetFileName(path)} (peak {peak:0.00}, gain {gain:0.00})");
        }

        float gameSfxVolume = 1f;

        float laneBias;
        int pairValue;

        /// <summary>
        /// A lane value that will still have a target when this blob's turn comes: start from the tank's values, let each
        /// lane blob claim one match (and add the doubled value its merge will create), then pick from what's left (≤ 16,
        /// like normal spawns). 0 = normal roll.
        /// </summary>
        int BiasedLaneValue()
        {
            if (pairValue > 0) // a forced placement just landed: send its partner (set up → merge)
            {
                int pv = pairValue;
                pairValue = 0;
                if (verbose) Log("lane", $"pair -> {pv}");
                return pv;
            }
            if (!Rng.Chance(laneBias)) return 0;
            var supply = new Dictionary<int, int>();
            var tank = new List<int>();
            foreach (var b in game.Blobs)
                if (b.State == BlobState.Free && (!verify || Exposed(b))) { supply[b.Value] = supply.TryGetValue(b.Value, out var n) ? n + 1 : 1; tank.Add(b.Value); }
            foreach (var b in game.Blobs)
            {
                if (b.State != BlobState.Lane && b.State != BlobState.Aiming) continue;
                if (supply.TryGetValue(b.Value, out var n) && n > 0)
                {
                    supply[b.Value] = n - 1;
                    supply[b.Value * 2] = supply.TryGetValue(b.Value * 2, out var m) ? m + 1 : 1; // its merge result
                }
            }
            var pool = new List<int>();
            foreach (var kv in supply) if (kv.Value > 0 && kv.Key <= 16) pool.Add(kv.Key);
            int v = pool.Count > 0 ? pool[Rng.Range(0, pool.Count)] : 0;
            if (verbose) Log("lane", v > 0 ? $"biased -> {v} (tank {string.Join(",", tank)})" : "normal roll (no free target)");
            return v;
        }

        float settleWait, waitedForMatch;

        /// <summary>Diagnostics: per ready lane blob, does the tank hold its value at all (and how many)?</summary>
        string WhyNoMerge()
        {
            var parts = new List<string>();
            foreach (var b in game.Blobs)
            {
                if (!Ready(b)) continue;
                int n = 0;
                foreach (var o in game.Blobs) if (o.State == BlobState.Free && o.Value == b.Value) n++;
                parts.Add(n == 0 ? $"{b.Value}:none" : $"{b.Value}:{n} unreachable");
            }
            var tank = new List<int>();
            foreach (var o in game.Blobs) if (o.State == BlobState.Free) tank.Add(o.Value);
            return string.Join(" ", parts) + " | tank " + string.Join(",", tank);
        }

        /// <summary>A lane blob that isn't ready yet (still entering) has a matching blob in the tank.</summary>
        bool IncomingMatch()
        {
            foreach (var b in game.Blobs)
            {
                if (b.State != BlobState.Lane || Ready(b)) continue;
                foreach (var o in game.Blobs)
                    if (o.State == BlobState.Free && o.Value == b.Value) return true;
            }
            return false;
        }

        /// <summary>No merge running, nothing flying or growing (gives up after 1.5 s so the lane never starves).</summary>
        bool TankSettled()
        {
            settleWait += Time.deltaTime;
            if (settleWait > 1.5f) { settleWait = 0f; return true; }
            foreach (var b in game.Blobs)
            {
                if (b.State == BlobState.Merging) return false;
                if (b.State == BlobState.Free && (b.Growing || b.Body.Velocity.sqrMagnitude > 2.25f)) return false;
            }
            settleWait = 0f;
            return true;
        }

        void OnMerged(int value, bool lane)
        {
            merges++;
            maxMerge = Mathf.Max(maxMerge, value);
            Log("merge", $"-> {value}{(lane ? " (lane)" : "")}");
        }

        // ------------------------------------------------------------------ decisions
        protected override IEnumerator Perform()
        {
            if (Request.Arg("still") == "1") // "still=1": no hand, no play (clean frames for art references)
            {
                yield return Motor.Wait(TimeLimit);
                yield break;
            }
            if (Persona.entrance) Hand.PlayEntrance(0.8f);
            yield return Motor.MoveTo(Px(new Vector2(0.6f, game.Board.LaneY - 3.5f)), 0.8f, false);
            Log("clock", $"deltaTime={Time.deltaTime:0.0000} visualDelta={GameTime.UnscaledDelta:0.0000} (Unity unscaled {Time.unscaledDeltaTime:0.0000})");

            // keep playing until the base cuts to the endcard at exactly (cap - endcard): every take is the full 59 s
            while (!game.IsGameOver)
            {
                allowBank = Rng.Chance(Persona.bankShotRate); // wall rebounds look skilled: casual players rarely try them
                if (!Plan(false, out var shot))
                {
                    yield return Motor.Hover(0.15f); // lane still filling
                    continue;
                }
                // nothing ready can merge, but a blob still floating in can: wait for it like a person would (≤ 1 s)
                if (!shot.merge && waitedForMatch < 1f && IncomingMatch())
                {
                    waitedForMatch += 0.15f;
                    yield return Motor.Hover(0.15f);
                    continue;
                }
                waitedForMatch = 0f;
                if (!shot.merge && verbose) Log("nomerge", WhyNoMerge());
                yield return Motor.Hover(Rng.Range(Persona.thinkTime));
                if (game.IsGameOver) break;

                if (shot.hit != null && Rng.Chance(Persona.lookAtTargetRate))
                {
                    Log("look", $"at {shot.hit.Value}");
                    // point down toward it from above, not all the way: the tank is tall and the hand returns to the lane
                    Vector2 tp = Px(shot.hit.Body.Center), from = Motor.Base;
                    tp.y = Mathf.Max(tp.y, from.y - 0.45f * W);
                    yield return Motor.MoveTo(tp + Rng.Gauss2() * (0.02f * W), 1.2f, false);
                    yield return Motor.Hover(Rng.Range(0.2f, 0.5f));
                }
                if (Rng.Chance(Persona.hesitationRate) && Other(shot.blob) is Blob other)
                {
                    Log("hesitate", $"{other.Value}");
                    Vector2 a = Px(other.Body.Center), b = Px(shot.blob.Body.Center);
                    yield return Motor.MoveTo(Vector2.Lerp(b, a, Rng.Range(0.5f, 0.85f)), 0.9f, false);
                    yield return Motor.Hover(Rng.Range(0.15f, 0.4f));
                }
                if (Rng.Chance(Persona.probeRate) && Other(shot.blob) is Blob probe)
                    yield return Probe(probe);
                if (Rng.Chance(Persona.missRate))
                    yield return Miss(shot.blob);

                bool wander = NextIsWrong();
                yield return Throw(shot.blob, wander, Rng.Chance(Persona.wrongDirectionRate));
            }

            if (game.IsGameOver)
            {
                Log("gameover");
                Completed = false;
                Time.timeScale = 1f; // the game freezes time on game over; captured time must keep running for the tail
            }
        }

        int burstLeft, cleanLeft, pendingBurst, pendingClean, wrongDebt;

        /// <summary>
        /// Whether the next move is a wrong one. With <c>wrongBurst</c> > 0 mistakes come in runs (~wrongBurst long) between
        /// clean streaks, like real ads; otherwise independent rolls, corrected so the take stays near <c>wanderRate</c>.
        /// </summary>
        bool NextIsWrong()
        {
            float r = Persona.wanderRate;
            if (r <= 0f) return false;
            if (Persona.wrongBurst > 0f && r < 1f)
            {
                if (burstLeft == 0 && cleanLeft == 0)
                {
                    // queue one run of mistakes + one clean streak sized so the share stays ≈ r; struggle first when behind
                    int burst = Mathf.Max(1, Mathf.RoundToInt(Persona.wrongBurst + Rng.Range(-1f, 1f)));
                    int clean = Mathf.Max(1, Mathf.RoundToInt(burst * (1f - r) / r * Rng.Range(0.7f, 1.3f)));
                    int paid = Mathf.Min(burst, wrongDebt); // forced non-merges already spent part of this run
                    burst -= paid; wrongDebt -= paid;
                    bool burstFirst = thrown == 0 ? Rng.Chance(0.5f) : wandered / (float)thrown < r;
                    if (burst == 0) { cleanLeft = clean; }
                    else if (burstFirst) { burstLeft = burst; pendingClean = clean; }
                    else { cleanLeft = clean; pendingBurst = burst; }
                }
                if (burstLeft > 0)
                {
                    if (--burstLeft == 0) { cleanLeft = pendingClean; pendingClean = 0; }
                    return true;
                }
                if (--cleanLeft == 0) { burstLeft = pendingBurst; pendingBurst = 0; }
                return false;
            }
            float share = thrown > 0 ? wandered / (float)thrown : r; // independent rolls drift a lot over ~15 throws
            return share > r + 0.08f ? false : share < r - 0.08f ? true : Rng.Chance(r);
        }

        /// <summary>Best throw over all ready lane blobs (or a deliberately poor one for <paramref name="wander"/>).</summary>
        bool Plan(bool wander, out Shot best, Blob only = null, Shot? near = null)
        {
            best = default;
            best.score = float.NegativeInfinity;
            prefPower = near?.power ?? Rng.Range(0.3f, 0.85f); // vary how hard this player pulls; a settle keeps it
            var poor = new List<Shot>();
            var good = new List<Shot>();
            foreach (var b in game.Blobs)
            {
                if (only != null ? b != only : !Ready(b)) continue;
                bool hasPartner = false;
                foreach (var o in game.Blobs)
                    if (o != b && o.State == BlobState.Lane && o.Value == b.Value) hasPartner = true;
                for (int ai = 0; ai <= 40; ai++)
                {
                    float ang = Mathf.Lerp(-172f, -8f, ai / 40f) * Mathf.Deg2Rad;
                    var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                    for (int pi = 0; pi <= 13; pi++)
                    {
                        float power = Mathf.Lerp(0.04f, 1f, pi / 13f);
                        if (!OnScreen(Finger(b, b.Body.Center, dir, power))) continue;
                        if (!Simulate(b, dir, power, out var s)) continue;
                        if (s.bank && !allowBank) continue;
                        if (hasPartner && !s.merge && game.LaneAutoMerge) s.score -= 10f; // leave lane pairs to the magnet
                        if (near.HasValue) s.score -= 0.5f * Vector2.Angle(dir, near.Value.dir); // settle = small correction
                        if (!s.merge) poor.Add(s); else good.Add(s);
                        if (s.score > best.score) best = s;
                    }
                }
            }
            if (verify && !wander && good.Count > 0)
            {
                // the circle planner proposes, the real solver decides: best-scored shot that truly merges
                good.Sort((x, y) => y.score.CompareTo(x.score));
                bool found = false;
                for (int k = 0; k < good.Count && k < VerifyTop; k++)
                {
                    var c = good[k];
                    if (!Confirm(ref c) || !c.merge || (c.bank && !allowBank)) { rejected++; continue; }
                    verified++;
                    best = c;
                    found = true;
                    break;
                }
                if (!found && poor.Count > 0)
                {
                    poor.Sort((x, y) => y.score.CompareTo(x.score));
                    best = poor[0]; // nothing truly merges: a placement (spends the mistake budget like any forced move)
                    if (verbose) Log("verify", "no planned merge survived the real physics");
                }
            }
            if (wander && poor.Count > 0)
            {
                var safe = poor.FindAll(SafeMiss); // a wrong move that rolls into a match isn't wrong
                if (safe.Count > 0) poor = safe;
                best = best.merge && NearMiss(best, poor, out var miss) ? miss : poor[Rng.Range(0, poor.Count)];
                // a miss must really miss: the real physics can still roll it into a match
                for (int k = 0; verify && k < 8 && Confirm(ref best) && best.merge; k++)
                    best = poor[Rng.Range(0, poor.Count)];
            }
            if (verbose && best.blob != null)
                Log("plan", $"{best.blob.Value} -> {(best.hit != null ? best.hit.Value.ToString() : "floor")} merge={best.merge} power={best.power:0.00} score={best.score:0.0}");
            return best.blob != null;
        }

        /// <summary>
        /// A human miss of <paramref name="aimed"/>: same blob, a few degrees / a bit of power off, no merge, landing close
        /// to the intended target (picked among the closest few, so it reads as a real attempt).
        /// </summary>
        bool NearMiss(Shot aimed, List<Shot> poor, out Shot miss)
        {
            var near = new List<Shot>();
            foreach (var s in poor)
            {
                float da = Vector2.Angle(s.dir, aimed.dir), dp = Mathf.Abs(s.power - aimed.power);
                if (s.blob == aimed.blob && da <= 25f && dp <= 0.3f && (da >= 4f || dp >= 0.1f)) near.Add(s);
            }
            Vector2 target = aimed.hit.Body.Center;
            // clearly beside it: a landing that touches the target would just roll in and merge anyway
            float clear = aimed.hit.CurrentRadius + aimed.blob.Radius * game.landedScale + 0.7f;
            near.RemoveAll(s => Vector2.Distance(s.end, target) < clear);
            near.Sort((x, y) => Vector2.Distance(x.end, target).CompareTo(Vector2.Distance(y.end, target)));
            miss = near.Count > 0 ? near[Rng.Range(0, Mathf.Min(4, near.Count))] : default;
            return near.Count > 0;
        }

        /// <summary>True when the throw lands clearly away from every blob it could merge with.</summary>
        bool SafeMiss(Shot s)
        {
            foreach (var o in game.Blobs)
                if (o != s.blob && o.State == BlobState.Free && o.Value == s.blob.Value &&
                    Vector2.Distance(s.end, o.Body.Center) < o.CurrentRadius + s.blob.Radius * game.landedScale + 0.7f) return false;
            return true;
        }

        /// <summary>Flies <paramref name="b"/> with the game's predictor; the first touched blob decides the outcome.</summary>
        bool Simulate(Blob b, Vector2 dir, float power, out Shot s)
        {
            s = new Shot { blob = b, dir = dir, power = power };
            Vector2 v = dir * Mathf.Lerp(aim.minSpeed, aim.maxSpeed, power);
            path.Clear();
            game.World.PredictPath(b.Body.Center, v, b.CurrentRadius * 0.9f, 1f / 60f, 1f / 60f, 150, path);
            var board = game.Board;
            for (int i = 0; i < path.Count; i++)
            {
                Vector2 p = path[i];
                // a flip of horizontal travel = it rebounded off a side wall
                if (i >= 2 && (path[i].x - path[i - 1].x) * (path[i - 1].x - path[i - 2].x) < -1e-6f) s.bank = true;
                float grow = Mathf.SmoothStep(0f, 1f, (i + 1) / 60f / Mathf.Max(0.05f, game.scaleTweenDuration));
                float r = Mathf.Lerp(b.CurrentRadius, b.Radius * game.landedScale, grow);
                foreach (var o in game.Blobs)
                {
                    if (o == b || o.State != BlobState.Free) continue;
                    float ro = o.CurrentRadius * 0.95f;
                    if ((p - o.Body.Center).sqrMagnitude > (r + ro) * (r + ro)) continue;
                    s.hit = o;
                    s.end = p;
                    s.merge = o.Value == b.Value;
                    s.score = s.merge
                        ? 100f + 10f * o.Tier + (HasNear(o, o.Value * 2) ? 15f : 0f) - 8f * Mathf.Abs(power - prefPower) - (i > 90 ? 5f : 0f)
                          + (s.bank ? 40f * Persona.bankPreference : 0f) // pro: rebound into the exact merge
                        : 10f - 2f * (p.y - board.FloorY) + (o.Value == b.Value * 2 ? 6f : 0f) - (o.Value < b.Value ? 4f : 0f);
                    if (p.y > board.DangerY - 1f) s.score -= 30f;
                    s.score += Rng.Range(0f, 1.5f);
                    return true;
                }
                if (p.y <= board.FloorY + r + 0.05f)
                {
                    s.end = p;
                    s.score = 12f - 2f * (p.y - board.FloorY) - 4f * Mathf.Abs(power - prefPower) + Rng.Range(0f, 1.5f);
                    return true;
                }
            }
            return false; // still flying / rolling: unpredictable, skip
        }

        /// <summary>Re-runs the shot with the real solver and replaces its outcome (hit / merge / end / bank).</summary>
        bool Confirm(ref Shot s)
        {
            Vector2 v = s.dir * Mathf.Lerp(aim.minSpeed, aim.maxSpeed, s.power);
            var pv = game.PreviewThrow(s.blob, v, s.power);
            if (!pv.settled) return false;
            s.hit = pv.hit;
            s.merge = pv.merge;
            s.end = pv.end;
            s.bank = pv.bank;
            return true;
        }

        /// <summary>A pile blob the next throw can actually reach: nothing rests on top of it.</summary>
        bool Exposed(Blob o)
        {
            foreach (var p in game.Blobs)
            {
                if (p == o || (p.State != BlobState.Free && p.State != BlobState.Merging)) continue;
                if (p.Body.Center.y > o.Body.Center.y + 0.2f &&
                    Mathf.Abs(p.Body.Center.x - o.Body.Center.x) < (o.CurrentRadius + p.CurrentRadius) * 0.7f) return false;
            }
            return true;
        }

        bool HasNear(Blob o, int value)
        {
            foreach (var n in game.Blobs)
                if (n != o && n.State == BlobState.Free && n.Value == value &&
                    Vector2.Distance(n.Body.Center, o.Body.Center) < (n.CurrentRadius + o.CurrentRadius) * 1.6f) return true;
            return false;
        }

        bool Ready(Blob b) => b.State == BlobState.Lane && b.StateTime > 0.35f &&
                              Mathf.Abs(b.Body.Center.x) < game.Board.HalfW - b.CurrentRadius * 0.8f;

        Blob Other(Blob not)
        {
            Blob pick = null;
            foreach (var b in game.Blobs)
                if (b != not && Ready(b) && (pick == null || Rng.Chance(0.5f))) pick = b;
            return pick;
        }

        // ------------------------------------------------------------------ aim geometry (mirrors AimController.Update)
        float DeadZone(Blob b) => aim.minDrag + (aim.Held == b ? b.CurrentRadius : b.CurrentRadius * 1.04f) * 0.6f; // grabbing grows it to 1.08 area

        Vector2 Finger(Blob b, Vector2 anchor, Vector2 dir, float power)
        {
            float len = DeadZone(b) + aim.maxDrag * Mathf.Pow(Mathf.Clamp01(power), 1f / aim.powerCurve);
            if (power >= 0.999f) len += 0.35f; // a real player over-pulls to be sure (max-stretch kick)
            return anchor + dir * len;
        }

        bool OnScreen(Vector2 world)
        {
            Vector2 s = Px(world);
            return s.x > 0.05f * W && s.x < 0.95f * W && s.y > 0.04f * H && s.y < 0.9f * H;
        }

        Vector2 Px(Vector2 world) => game.SteadyToScreen(world);

        // ------------------------------------------------------------------ actions
        IEnumerator Throw(Blob b, bool wander, bool wrongDir)
        {
            if (!Ready(b)) { Log("skip", "blob left the lane"); yield break; }
            float far = Vector2.Distance(Motor.Base, Px(b.Body.Center)) > 0.5f * W ? 1.4f : 1f; // long trips back up are quicker
            yield return Motor.MoveTo(Px(b.Body.Center), far);
            yield return Motor.MoveTo(Px(b.Body.Center), 1.5f); // it bobs: settle onto where it is now
            yield return Motor.Press();
            if (aim.Held != b)
            {
                Log("fumble", aim.Held == null ? "nothing grabbed" : $"grabbed {aim.Held.Value}");
                yield return Motor.Release();
                yield break;
            }
            Vector2 anchor = b.Body.Center;

            if (!Plan(wander, out var shot, b)) { Log("cancel", "no shot"); yield return Motor.Release(); yield break; }

            if (wrongDir)
            {
                float off = Rng.Range(Persona.wrongDirectionDrag) * Rng.Sign;
                Vector2 wd = Rotate(shot.dir, off);
                Log("wrongdir", $"{off:0}°");
                yield return Drag(Px(Finger(b, anchor, wd, Mathf.Clamp(shot.power + Rng.Range(-0.2f, 0.2f), 0.2f, 1f))));
                yield return Motor.Wait(Rng.Range(Persona.realizeTime));
            }

            yield return Drag(Px(Finger(b, anchor, shot.dir, shot.power)));
            // final small correction with the live radius / pile (the arrow "settles" like a careful player)
            if (!wander && aim.Held == b && Plan(false, out var fine, b, shot) && fine.merge == shot.merge)
                shot = fine;
            yield return Drag(Px(Finger(b, anchor, shot.dir, shot.power)), 0.4f);
            yield return Motor.Wait(Rng.Range(Persona.holdBeforeRelease));
            if (aim.Held != b) { Log("lost", "blob stolen while aiming"); yield return Motor.Release(); yield break; }

            throws++;
            thrown++;
            if (wander || !shot.merge) wandered++; // BlobMerge: every non-merge is a wrong move
            if (!wander && !shot.merge) // forced (nothing could merge): it spends the mistake budget
            {
                pairValue = b.Value; // ...and sets up the next merge: the next lane blob gets the same value
                if (burstLeft > 0) burstLeft--;
                else if (pendingBurst > 0) pendingBurst--;
                else wrongDebt++;
            }
            Log("throw", $"{b.Value} -> {(shot.hit != null ? shot.hit.Value.ToString() : "floor")} merge={shot.merge} power={shot.power:0.00}{(wander ? " (miss)" : !shot.merge ? " (forced)" : "")}{(shot.bank ? " (bank)" : "")}");
            yield return Motor.Release();

            // follow-through, then watch it land
            Vector2 ft = Motor.Base + shot.dir * (Persona.followThrough * 0.06f * W);
            yield return Motor.MoveTo(ft, 0.7f, false);
            for (float t = 0f; t < 0.5f && b.State == BlobState.Free && (b.Growing || b.Body.Velocity.sqrMagnitude > 4f); t += Time.deltaTime)
                yield return null;
            yield return Motor.Hover(Rng.Range(Persona.afterSuccessPause));
        }

        /// <summary>Grab a different lane blob, pull a little (inside the dead zone: no throw), let go.</summary>
        IEnumerator Probe(Blob other)
        {
            Log("probe", $"{other.Value}");
            yield return Motor.MoveTo(Px(other.Body.Center));
            yield return Motor.Press();
            if (aim.Held == other)
            {
                Vector2 d = Rotate(Vector2.down, Rng.Range(-50f, 50f));
                float len = DeadZone(other) * Rng.Range(0.3f, 0.75f);
                yield return Drag(Px(other.Body.Center + d * len), 0.6f);
                yield return Motor.Wait(Rng.Range(Persona.realizeTime));
                yield return Drag(Px(aim.Held != null ? aim.Held.Body.Center : other.Body.Center), 0.5f); // back: cancels
            }
            yield return Motor.Release();
            yield return Motor.Wait(Rng.Range(Persona.realizeTime));
        }

        /// <summary>Press on empty water beside the blob (nothing happens), realise, lift.</summary>
        IEnumerator Miss(Blob b)
        {
            float off = (b.CurrentRadius + aim.pickPadding + 0.35f) * (b.Body.Center.x > 0f ? -1f : 1f);
            Vector2 p = b.Body.Center + new Vector2(off, -0.6f);
            Log("miss", $"beside {b.Value}");
            yield return Motor.MoveTo(Px(p));
            yield return Motor.Tap();
            yield return Motor.Wait(Rng.Range(Persona.realizeTime));
        }

        /// <summary>Pressed finger glides to <paramref name="to"/> (pixels) with a minimum-jerk profile and optional stall.</summary>
        IEnumerator Drag(Vector2 to, float speedScale = 1f)
        {
            Vector2 from = Motor.Base;
            float peak = Persona.dragSpeed / 280f * 0.9f * W * speedScale;
            float T = Mathf.Max(0.12f, HumanMotor.MinJerkTime(Vector2.Distance(from, to), peak));
            float stallAt = Rng.Chance(Persona.stallRate) && speedScale >= 1f ? Rng.Range(0.35f, 0.7f) : 2f;
            for (float t = 0f; t < T; )
            {
                t += Time.deltaTime;
                float u = t / T;
                Motor.Base = Vector2.Lerp(from, to, HumanMotor.MinJerk(u));
                yield return null;
                if (u >= stallAt)
                {
                    stallAt = 2f;
                    yield return Motor.Wait(Rng.Range(Persona.stallTime));
                }
            }
            Motor.Base = to;
        }

        static Vector2 Rotate(Vector2 v, float degrees)
        {
            float a = degrees * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }
    }
}
#endif
