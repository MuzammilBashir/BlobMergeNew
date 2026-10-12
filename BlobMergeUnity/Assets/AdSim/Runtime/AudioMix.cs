using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Debug = UnityEngine.Debug;

namespace AdSim
{
    /// <summary>A sound placed on the take's timeline (clip path relative to the project root, or absolute).</summary>
    public struct AudioCue
    {
        public string Clip;
        public float Time;
        public float Volume;
        public float MaxLength; // > 0: trim with a short fade-out
    }

    /// <summary>Mixes timed cues into a silent MP4 with ffmpeg (video stream copied, AAC audio, padded to length).</summary>
    public static class AudioMix
    {
        public static bool Mix(string ffmpeg, string videoPath, List<AudioCue> cues, float seconds, out string error)
        {
            error = null;
            var usable = new List<AudioCue>();
            foreach (var c in cues)
            {
                if (string.IsNullOrEmpty(c.Clip) || !File.Exists(c.Clip)) Debug.LogWarning("[AdSim] audio clip not found: " + c.Clip);
                else usable.Add(c);
            }
            if (usable.Count == 0) return true;

            var inv = CultureInfo.InvariantCulture;
            var args = new StringBuilder($"-y -v error -i \"{videoPath}\"");
            foreach (var c in usable) args.Append($" -i \"{Path.GetFullPath(c.Clip)}\"");
            var fc = new StringBuilder();
            for (int i = 0; i < usable.Count; i++)
            {
                var c = usable[i];
                fc.Append($"[{i + 1}:a]aformat=sample_rates=48000:channel_layouts=stereo");
                if (c.MaxLength > 0f)
                {
                    float fade = Math.Min(0.3f, c.MaxLength * 0.5f);
                    fc.Append(string.Format(inv, ",atrim=0:{0:0.###},afade=t=out:st={1:0.###}:d={2:0.###}", c.MaxLength, c.MaxLength - fade, fade));
                }
                fc.Append(string.Format(inv, ",volume={0:0.###},adelay={1}:all=1[a{2}];", c.Volume, Math.Max(0, (int)Math.Round(c.Time * 1000f)), i));
            }
            for (int i = 0; i < usable.Count; i++) fc.Append($"[a{i}]");
            fc.Append(string.Format(inv, "amix=inputs={0}:normalize=0:dropout_transition=0,alimiter=limit=0.89:level=false,apad,atrim=0:{1:0.###}[aout]", usable.Count, seconds));

            string tmp = Path.Combine(Path.GetDirectoryName(videoPath), Path.GetFileNameWithoutExtension(videoPath) + ".mixing.mp4");
            args.Append($" -filter_complex \"{fc}\" -map 0:v -map \"[aout]\" -c:v copy -c:a aac -b:a 192k -movflags +faststart \"{tmp}\"");
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = string.IsNullOrEmpty(ffmpeg) ? "ffmpeg" : ffmpeg,
                    Arguments = args.ToString(),
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                };
                using (var p = Process.Start(psi))
                {
                    string err = p.StandardError.ReadToEnd();
                    p.WaitForExit(120000);
                    if (p.ExitCode != 0) { error = "ffmpeg audio mix failed: " + err; return false; }
                }
                File.Delete(videoPath);
                File.Move(tmp, videoPath);
                return true;
            }
            catch (Exception e)
            {
                error = "audio mix failed: " + e.Message;
                return false;
            }
        }
    }
}
