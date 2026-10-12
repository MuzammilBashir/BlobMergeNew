using System.IO;
using UnityEngine;

namespace BlobMerge
{
    /// <summary>Tiny mono PCM helpers for offline audio (SFX export, ad sound tracks). Not used at game runtime.</summary>
    public static class WavUtil
    {
        /// <summary>Resample like AudioSource.pitch: higher pitch = shorter and higher.</summary>
        public static float[] Pitch(float[] src, float pitch)
        {
            int n = Mathf.Max(1, Mathf.FloorToInt(src.Length / Mathf.Max(0.01f, pitch)));
            var o = new float[n];
            for (int i = 0; i < n; i++)
            {
                float p = i * pitch;
                int a = Mathf.Min((int)p, src.Length - 1), b = Mathf.Min(a + 1, src.Length - 1);
                o[i] = Mathf.Lerp(src[a], src[b], p - a);
            }
            return o;
        }

        /// <summary>Adds <paramref name="src"/> × <paramref name="volume"/> into <paramref name="dst"/> at sample <paramref name="at"/>.</summary>
        public static void MixInto(float[] dst, float[] src, int at, float volume)
        {
            for (int i = 0; i < src.Length; i++)
            {
                int j = at + i;
                if (j < 0) continue;
                if (j >= dst.Length) break;
                dst[j] += src[i] * volume;
            }
        }

        /// <summary>16-bit mono WAV.</summary>
        public static void Write(string path, float[] data, int rate)
        {
            using var w = new BinaryWriter(File.Create(path));
            int bytes = data.Length * 2;
            w.Write(new[] { 'R', 'I', 'F', 'F' }); w.Write(36 + bytes); w.Write(new[] { 'W', 'A', 'V', 'E' });
            w.Write(new[] { 'f', 'm', 't', ' ' }); w.Write(16); w.Write((short)1); w.Write((short)1);
            w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
            w.Write(new[] { 'd', 'a', 't', 'a' }); w.Write(bytes);
            foreach (float f in data) w.Write((short)Mathf.RoundToInt(Mathf.Clamp(f, -1f, 1f) * 32767f));
        }
    }
}
