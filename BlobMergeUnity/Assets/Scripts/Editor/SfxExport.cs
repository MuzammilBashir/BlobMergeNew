using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace BlobMerge.EditorTools
{
    /// <summary>
    /// Writes the game's procedural sounds (<see cref="Sfx.Render"/>) to 16-bit WAV files, plus ad/endcard cues pitched
    /// the way the game plays them. Menu: Tools → BlobMerge → Export Sfx (→ ../Branding/sfx).
    /// Batch: Unity -batchmode -nographics -quit -projectPath … -executeMethod BlobMerge.EditorTools.SfxExport.Export -sfxOut &lt;dir&gt;
    /// </summary>
    public static class SfxExport
    {
        [MenuItem("Tools/BlobMerge/Export Sfx")]
        public static void Export()
        {
            string dir = Arg("-sfxOut") ?? Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Branding", "sfx"));
            Directory.CreateDirectory(dir);
            var s = Sfx.RenderAll(); // default + Fish (bubbly) set
            foreach (var kv in s) Write(Path.Combine(dir, kv.Key + ".wav"), kv.Value);

            // endcard cues: merge pops rising like a tier 4 → 5 → 6 combo (Sfx.Merge pitch = 1.0595^(2·tier) · 0.7)
            for (int i = 0; i < 3; i++)
                Write(Path.Combine(dir, $"ec_pop_{i + 1}.wav"), Pitch(s["pop"], Mathf.Pow(1.0595f, 2 * (4 + i)) * 0.7f));
            Write(Path.Combine(dir, "ec_tap.wav"), Mix(s["tick"], 1f, Pitch(s["plop"], 1.2f), 0.6f)); // Sfx.Select + a soft plop
            Write(Path.Combine(dir, "ec_whoosh.wav"), Pitch(s["whoosh"], 0.9f));
            Debug.Log($"[BlobMerge] sfx exported: {s.Count} sounds + 5 endcard cues -> {dir}");
        }

        static float[] Mix(float[] a, float va, float[] b, float vb)
        {
            var o = new float[Mathf.Max(a.Length, b.Length)];
            for (int i = 0; i < o.Length; i++)
                o[i] = Mathf.Clamp((i < a.Length ? a[i] * va : 0f) + (i < b.Length ? b[i] * vb : 0f), -1f, 1f);
            return o;
        }

        static float[] Pitch(float[] src, float pitch) => WavUtil.Pitch(src, pitch);

        static void Write(string path, float[] data) => WavUtil.Write(path, data, Sfx.Rate);

        static string Arg(string name)
        {
            var a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++)
                if (a[i] == name) return a[i + 1];
            return null;
        }
    }
}
