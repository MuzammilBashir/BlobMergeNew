using System.Collections.Generic;
using UnityEngine;

namespace BlobMerge
{
    /// <summary>Procedurally generated sound effects (no audio assets needed) and Android haptics.</summary>
    public class Sfx : MonoBehaviour
    {
        [Range(0f, 1f)] public float volume = 0.8f;
        public bool haptics = true;

        AudioSource[] sources;
        int next;
        AudioClip pop, plop, whoosh, thud, tick, spawn, gameOver, uiClick, uiPop;
        AudioClip bPop, bBlub, bWhoosh, bSpawn, bPlink, bWin, ambience; // Fish theme (bubbly) set
        AudioClip[] bThrow; // Fish throw: light variants picked at random (user: the whoosh was loud and repetitive)
        public const int ThrowVariants = 4;
        AudioSource ambienceSource;

        /// <summary>Fish theme: bubble pops, bloops and an underwater ambience instead of the default set.
        /// Set before the component is added (GameController does it from its piece shape).</summary>
        public static bool Bubbly;
        readonly Dictionary<AudioClip, string> names = new Dictionary<AudioClip, string>();

        /// <summary>Raised for every sound played: (name in <see cref="Render"/>, final volume, pitch). Lets the ad
        /// recorder rebuild the gameplay sound track exactly (Unity audio isn't captured with the video).</summary>
        public static event System.Action<string, float, float> Played;
        public const int Rate = 44100;

        void Awake()
        {
            sources = new AudioSource[10];
            for (int i = 0; i < sources.Length; i++)
            {
                sources[i] = gameObject.AddComponent<AudioSource>();
                sources[i].playOnAwake = false;
            }
            var s = Bubbly ? RenderAll() : Render();
            pop = Clip(s["pop"]);
            plop = Clip(s["plop"]);
            whoosh = Clip(s["whoosh"]);
            thud = Clip(s["thud"]);
            tick = Clip(s["tick"]);
            spawn = Clip(s["spawn"]);
            gameOver = Clip(s["gameOver"]);
            uiClick = Clip(s["ui_click"]);
            uiPop = Clip(s["ui_pop"]);
            foreach (var kv in new[] { (pop, "pop"), (plop, "plop"), (whoosh, "whoosh"), (thud, "thud"), (tick, "tick"), (spawn, "spawn"), (gameOver, "gameOver"), (uiClick, "ui_click"), (uiPop, "ui_pop") })
                names[kv.Item1] = kv.Item2;
            if (Bubbly)
            {
                bPop = Clip(s["b_pop"]); bBlub = Clip(s["b_blub"]); bWhoosh = Clip(s["b_whoosh"]);
                bSpawn = Clip(s["b_spawn"]); bPlink = Clip(s["b_plink"]); ambience = Clip(s["ambience"]); bWin = Clip(s["b_win"]);
                foreach (var kv in new[] { (bPop, "b_pop"), (bBlub, "b_blub"), (bWhoosh, "b_whoosh"), (bSpawn, "b_spawn"), (bPlink, "b_plink"), (ambience, "ambience"), (bWin, "b_win") })
                    names[kv.Item1] = kv.Item2;
                bThrow = new AudioClip[ThrowVariants];
                for (int i = 0; i < ThrowVariants; i++) { bThrow[i] = Clip(s["b_throw" + i]); names[bThrow[i]] = "b_throw" + i; }
            }
        }

        /// <summary>
        /// Every sound as raw mono samples at <see cref="Rate"/>, rendered in a fixed order from a fixed seed. The game
        /// plays these; the Editor exporter (Tools → BlobMerge → Export Sfx) writes the same samples to WAV files.
        /// </summary>
        public static Dictionary<string, float[]> Render()
        {
            rng = new System.Random(7);
            var d = new Dictionary<string, float[]> // initialisers run in order: keeps the noise sequence stable
            {
                { "pop", Synth(0.22f, (t, k) => Bubble(t, 420f, 900f, 26f) + Noise() * 0.05f * Env(t, 60f)) },
                { "plop", Synth(0.12f, (t, k) => Bubble(t, 260f, 520f, 40f) * 0.8f) },
                { "whoosh", Synth(0.25f, (t, k) => Noise() * Mathf.Sin(k * Mathf.PI) * 0.35f * (0.5f + 0.5f * Mathf.Sin(t * 90f))) },
                { "thud", Synth(0.14f, (t, k) => (Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(140f, 70f, k) * t) * 0.9f + Noise() * 0.15f) * Env(t, 30f)) },
                { "tick", Synth(0.05f, (t, k) => Mathf.Sin(2f * Mathf.PI * 1400f * t) * Env(t, 90f) * 0.5f) },
                { "spawn", Synth(0.16f, (t, k) => Bubble(t, 600f, 380f, 30f) * 0.5f) },
                { "gameOver", Synth(0.9f, (t, k) => (Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(330f, 110f, k) * t) * 0.6f + Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(220f, 80f, k) * t) * 0.4f) * Env(t, 3f)) },
            };
            // UI sounds (every scene): no noise, so the seeded sounds above stay bit-identical
            // button: a soft rounded "tok" with a tiny bright tick on top
            d["ui_click"] = Normalize(Synth(0.1f, (t, k) =>
                SoftBubble(t, 520f, 880f, 48f, 400f) * 0.7f + Mathf.Sin(2f * Mathf.PI * 1700f * t) * Env(t, 160f) * 0.18f), 0.7f);
            // popup: a round "bloop" rising, with a small high bubble landing just after
            d["ui_pop"] = Normalize(Synth(0.3f, (t, k) =>
                SoftBubble(t, 300f, 820f, 17f, 220f) * 0.65f
                + (t > 0.055f ? SoftBubble(t - 0.055f, 760f, 1350f, 30f, 260f) * 0.22f : 0f)), 0.75f);
            return d;
        }

        /// <summary>Default set + the Fish theme's bubbly set (own seed, so the default sounds never change).</summary>
        public static Dictionary<string, float[]> RenderAll()
        {
            var all = Render();
            foreach (var kv in RenderBubbly()) all[kv.Key] = kv.Value;
            return all;
        }

        /// <summary>Fish theme sounds: real-bubble pops, soft bloops, an underwater whoosh, trickles and the ambience loop.</summary>
        public static Dictionary<string, float[]> RenderBubbly()
        {
            rng = new System.Random(31);
            var d = new Dictionary<string, float[]>
            {
                // merge (user: softer, lower): no click; a round low "blub" with a gentle "plip" on top and a soft droplet
                { "b_pop", Normalize(Synth(0.42f, (t, k) =>
                    SoftBubble(t, 520f, 1250f, 30f, 180f) * 0.45f
                    + SoftBubble(t, 170f, 430f, 13f, 150f) * 0.65f
                    + (t > 0.08f ? SoftBubble(t - 0.08f, 900f, 760f, 40f, 200f) * 0.10f : 0f)), 0.85f) },
                // landing: a soft low bloop
                { "b_blub", Synth(0.2f, (t, k) => Bubble(t, 170f, 430f, 22f) * 0.75f + Bubble(t, 520f, 760f, 40f) * 0.15f) },
                // throw: muffled water swell with a little bubble trail
                { "b_whoosh", Swell(0.42f) },
                // new lane piece: three quick small bubbles
                { "b_spawn", Synth(0.3f, (t, k) =>
                    Bubble(t, 700f, 1300f, 45f) * 0.35f
                    + Bubble(Mathf.Max(0f, t - 0.07f), 900f, 1700f, 50f) * 0.3f * (t > 0.07f ? 1f : 0f)
                    + Bubble(Mathf.Max(0f, t - 0.13f), 800f, 1500f, 50f) * 0.25f * (t > 0.13f ? 1f : 0f)) },
                // grab: a small high plink
                { "b_plink", Synth(0.08f, (t, k) => Bubble(t, 1300f, 1900f, 70f) * 0.45f) },
                // underwater ambience: 24 s seamless loop
                { "ambience", Ambience(24f) },
                // level complete: rising fanfare (no noise: keeps the seeded sounds above unchanged)
                { "b_win", Fanfare() },
            };
            // throw (user 2026-10-12: the old whoosh was loud and every throw sounded the same): short light puffs of
            // water, each with a soft tonal plip on a different note so the variation is audible. Rendered after every
            // other sound so the seeded ones above stay bit-identical.
            float[] plipHz = { 660f, 784f, 880f, 988f };   // E5 G5 A5 B5
            float[] cutoff = { 0.10f, 0.14f, 0.08f, 0.12f }; // one-pole low-pass: different "airiness"
            for (int v = 0; v < ThrowVariants; v++)
                d["b_throw" + v] = Normalize(Puff(0.18f + 0.02f * v, cutoff[v], plipHz[v]), 0.6f);
            return d;
        }

        /// <summary>
        /// Level-complete fanfare: a quick rising C-major arpeggio of soft bell tones (C5 E5 G5) landing on a held C6
        /// chord with a gentle vibrato shimmer, plus little bubble plips riding up with it.
        /// </summary>
        static float[] Fanfare()
        {
            float[] notes = { 523.25f, 659.25f, 783.99f, 1046.5f };
            float[] starts = { 0f, 0.09f, 0.18f, 0.29f };
            return Normalize(Synth(1.6f, (t, k) =>
            {
                float v = 0f;
                for (int i = 0; i < notes.Length; i++)
                {
                    float tt = t - starts[i];
                    if (tt <= 0f) continue;
                    bool last = i == notes.Length - 1;
                    float decay = last ? 2.2f : 9f;
                    float f = notes[i] * (last ? 1f + 0.004f * Mathf.Sin(tt * 32f) * Mathf.Clamp01(tt * 3f) : 1f);
                    float tone = Mathf.Sin(2f * Mathf.PI * f * tt) + 0.35f * Mathf.Sin(4f * Mathf.PI * f * tt) + 0.12f * Mathf.Sin(6f * Mathf.PI * f * tt);
                    v += tone * Env(tt, decay) * Mathf.Clamp01(tt * 300f) * (last ? 0.55f : 0.4f);
                    if (last) // the landing chord: E6 + G5 underneath
                        v += (Mathf.Sin(2f * Mathf.PI * 1318.5f * tt) * 0.18f + Mathf.Sin(2f * Mathf.PI * 783.99f * tt) * 0.22f) * Env(tt, 2.6f) * Mathf.Clamp01(tt * 200f);
                    v += Bubble(tt, notes[i] * 0.9f, notes[i] * 1.6f, 45f) * 0.12f; // a plip on each note
                }
                return v;
            }), 0.8f);
        }

        /// <summary>Bubble with an exact (phase-integrated) pitch glide f0 → f1 and a soft attack — rounder than <see cref="Bubble"/>.</summary>
        static float SoftBubble(float t, float f0, float f1, float decay, float attack)
        {
            float phase = 2f * Mathf.PI * (f1 * t + (f0 - f1) * (1f - Mathf.Exp(-t * 25f)) / 25f);
            return Mathf.Sin(phase) * Env(t, decay) * Mathf.Clamp01(t * attack);
        }

        static float[] Normalize(float[] d, float peak)
        {
            float m = 0f;
            foreach (float v in d) m = Mathf.Max(m, Mathf.Abs(v));
            if (m > 1e-5f) for (int i = 0; i < d.Length; i++) d[i] *= peak / m;
            return d;
        }

        /// <summary>A light throw: a quick soft puff of low-passed noise (fast attack, short decay) and a quiet tonal plip.</summary>
        static float[] Puff(float seconds, float cutoff, float plipHz)
        {
            int n = Mathf.CeilToInt(seconds * Rate);
            var data = new float[n];
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                lp += (Noise() - lp) * cutoff;
                float air = lp * 2.2f * Mathf.Clamp01(t * 200f) * Env(t, 22f);
                float plip = SoftBubble(t, plipHz * 0.8f, plipHz, 26f, 250f) * 0.35f;
                data[i] = Mathf.Clamp(air + plip, -1f, 1f) * Mathf.Clamp01((n - i) / (Rate * 0.01f));
            }
            return data;
        }

        /// <summary>Low-passed noise swell (water rushing past) with a short bubble trail.</summary>
        static float[] Swell(float seconds)
        {
            int n = Mathf.CeilToInt(seconds * Rate);
            var data = new float[n];
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate, k = i / (float)n;
                lp += (Noise() - lp) * 0.06f; // one-pole low-pass: muffled, underwater
                float swell = Mathf.Sin(k * Mathf.PI);
                float trail = 0f;
                for (int b = 0; b < 4; b++)
                {
                    float tb = t - 0.05f - b * 0.06f;
                    if (tb > 0f) trail += Bubble(tb, 800f + b * 180f, 1500f + b * 200f, 60f) * 0.12f;
                }
                data[i] = Mathf.Clamp(lp * 1.6f * swell + trail, -1f, 1f) * Mathf.Clamp01((n - i) / (Rate * 0.01f));
            }
            return data;
        }

        /// <summary>
        /// Deep muffled rumble (twice low-passed noise) with slow swells, plus sparse distant bubble trickles.
        /// The last second is cross-faded into the start, so it loops without a seam.
        /// </summary>
        static float[] Ambience(float seconds)
        {
            int n = Mathf.CeilToInt(seconds * Rate), fade = Rate;
            var data = new float[n + fade];
            float lp1 = 0f, lp2 = 0f;
            for (int i = 0; i < data.Length; i++)
            {
                float t = i / (float)Rate;
                lp1 += (Noise() - lp1) * 0.02f;
                lp2 += (lp1 - lp2) * 0.02f;
                float swell = 0.75f + 0.25f * Mathf.Sin(t * 0.5f) * Mathf.Sin(t * 0.21f + 1f);
                data[i] = lp2 * 5f * swell;
            }
            // bubble trickles: a few small rising plips, quiet
            for (int e = 0; e < 14; e++)
            {
                float start = (float)rng.NextDouble() * (seconds - 1.5f);
                int count = 2 + rng.Next(5);
                for (int b = 0; b < count; b++)
                {
                    float st = start + b * (0.05f + (float)rng.NextDouble() * 0.08f);
                    float f0 = 500f + (float)rng.NextDouble() * 900f;
                    int s0 = Mathf.RoundToInt(st * Rate);
                    for (int j = 0; j < Rate / 6 && s0 + j < data.Length; j++)
                        data[s0 + j] += Bubble(j / (float)Rate, f0, f0 * 1.8f, 45f) * 0.05f;
                }
            }
            var loop = new float[n];
            for (int i = 0; i < n; i++)
            {
                float v = data[i];
                if (i < fade) { float w = i / (float)fade; v = data[i] * w + data[n + i] * (1f - w); }
                loop[i] = Mathf.Clamp(v, -1f, 1f);
            }
            return loop;
        }

        static System.Random rng = new System.Random(7);
        static float Noise() => (float)rng.NextDouble() * 2f - 1f;
        static float Env(float t, float decay) => Mathf.Exp(-t * decay);

        /// <summary>Rising-pitch "bloop", the classic bubble pop.</summary>
        static float Bubble(float t, float f0, float f1, float decay)
        {
            float f = Mathf.Lerp(f0, f1, 1f - Mathf.Exp(-t * 25f));
            float attack = Mathf.Clamp01(t * 400f);
            return Mathf.Sin(2f * Mathf.PI * f * t) * Env(t, decay) * attack;
        }

        static float[] Synth(float seconds, System.Func<float, float, float> fn)
        {
            int n = Mathf.CeilToInt(seconds * Rate);
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float fade = Mathf.Clamp01((n - i) / (Rate * 0.01f));
                data[i] = Mathf.Clamp(fn(t, i / (float)n), -1f, 1f) * fade;
            }
            return data;
        }

        static AudioClip Clip(float[] data)
        {
            var clip = AudioClip.Create("sfx", data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        void Play(AudioClip clip, float vol, float pitch)
        {
            var s = sources[next];
            next = (next + 1) % sources.Length;
            s.pitch = pitch;
            s.volume = vol * volume;
            s.clip = clip;
            s.Play();
            Played?.Invoke(names[clip], s.volume, pitch);
        }

        public void Merge(int tier, int chain)
        {
            float p = Mathf.Pow(1.0595f, tier * 2 + chain * 3); // up a whole tone per tier
            if (Bubbly) Play(bPop, 0.9f, Mathf.Clamp(p * 0.5f, 0.4f, 2.2f)); // lower base pitch (user)
            else Play(pop, 0.9f, Mathf.Clamp(p * 0.7f, 0.5f, 3f));
            Haptic(tier >= 7 ? 40 : 18);
        }

        // Fish throw sound: pitch picked from small chromatic steps, -2..+3 semitones (never the same step twice in a row),
        // so repeated throws don't sound identical; volume set by GameController.throwSoundVolume (user: lighter, quieter,
        // less repetitive).
        static readonly float[] PitchSteps = { 0.891f, 0.944f, 1f, 1.059f, 1.122f, 1.189f }; // -2, -1, 0, +1, +2, +3 semitones
        int lastStep = -1, lastVariant = -1;

        public void Launch(float power, float bubblyVolume = 0.25f)
        {
            if (Bubbly)
            {
                if (bubblyVolume <= 0f) { Haptic(12); return; } // user 2026-10-12: no throw sound for now
                int step = Random.Range(0, PitchSteps.Length - 1);
                if (step >= lastStep && lastStep >= 0) step++; // skip the previous step
                lastStep = step;
                int variant = Random.Range(0, ThrowVariants - 1);
                if (variant >= lastVariant && lastVariant >= 0) variant++; // never the same puff twice in a row
                lastVariant = variant;
                Play(bThrow[variant], bubblyVolume * (0.75f + power * 0.25f), PitchSteps[step]);
            }
            else Play(whoosh, 0.35f + power * 0.4f, 0.8f + power * 0.6f);
            Haptic(12);
        }

        public void Impact(float strength)
        {
            if (Bubbly) Play(bBlub, Mathf.Clamp01(strength) * 0.65f, Random.Range(0.8f, 1.25f));
            else Play(Random.value < 0.5f ? thud : plop, Mathf.Clamp01(strength) * 0.6f, Random.Range(0.85f, 1.15f));
        }

        public void Select() { Play(Bubbly ? bPlink : tick, 0.5f, 1f); Haptic(8); }
        public void MaxPower()
        {
            if (!Bubbly) { Play(tick, 0.8f, 1.6f); Play(thud, 0.35f, 0.7f); } // Fish: silent max stretch (user 2026-10-12), haptic only
            Haptic(30);
        }
        public void Spawn() => Play(Bubbly ? bSpawn : spawn, Bubbly ? 0.45f : 0.35f, Random.Range(0.9f, 1.2f));

        /// <summary>Starts the looping underwater ambience (Fish theme). Not reported to <see cref="Played"/>: the ad
        /// recorder lays the loop under the whole take itself.</summary>
        public void StartAmbience(float vol)
        {
            if (!Bubbly || ambience == null || vol <= 0f) return;
            if (ambienceSource == null) ambienceSource = gameObject.AddComponent<AudioSource>();
            ambienceSource.clip = ambience;
            ambienceSource.loop = true;
            ambienceSource.volume = vol * volume;
            ambienceSource.Play();
        }
        /// <summary>Level won (Fish): the fanfare + a long haptic.</summary>
        public void LevelWin()
        {
            if (Bubbly && bWin != null) Play(bWin, 0.9f, 1f);
            else Play(pop, 0.9f, 1.4f);
            Haptic(150);
        }
        public void GameOver() { Play(gameOver, 0.9f, 1f); Haptic(120); }

        // ---------------------------------------------------------------- UI
        /// <summary>The live instance, so UI (popups) can play without a reference to the game.</summary>
        public static Sfx Current { get; private set; }
        void OnEnable() => Current = this;
        void OnDestroy() { if (Current == this) Current = null; }

        /// <summary>Button press.</summary>
        public void Click() => Play(uiClick, 0.6f, 1f);
        /// <summary>A popup appearing.</summary>
        public void PopUp() => Play(uiPop, 0.65f, 1f);

        /// <summary>Click sound on every Button under <paramref name="root"/> (inactive ones too — popups start hidden).
        /// Call once, after the popups are installed: it runs after each button's own handler, so toggling Sound off is silent
        /// and turning it on is heard.</summary>
        public static void HookButtons(Transform root)
        {
            foreach (var b in root.GetComponentsInChildren<UnityEngine.UI.Button>(true))
                b.onClick.AddListener(() => { if (Current != null) Current.Click(); });
        }

        // ---------------------------------------------------------------- haptics
        public static bool forceLegacyVibrate; // keeps Handheld.Vibrate referenced so Unity adds the VIBRATE permission
#if UNITY_ANDROID && !UNITY_EDITOR
        static AndroidJavaObject vibrator;
        static bool vibInit;
#endif
        void Haptic(long ms)
        {
            if (!haptics || !GameSettings.HapticsOn) return;
#if UNITY_ANDROID || UNITY_IOS
            if (forceLegacyVibrate) { Handheld.Vibrate(); return; }
#endif
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                if (!vibInit)
                {
                    vibInit = true;
                    using (var up = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                    {
                        var act = up.GetStatic<AndroidJavaObject>("currentActivity");
                        vibrator = act.Call<AndroidJavaObject>("getSystemService", "vibrator");
                    }
                }
                vibrator?.Call("vibrate", ms);
            }
            catch (System.Exception) { vibrator = null; }
#endif
        }
    }
}
