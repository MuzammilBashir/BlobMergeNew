using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace AdSim
{
    /// <summary>
    /// Captures the capture camera every frame and pipes raw RGB into ffmpeg (H.264 MP4). Runs first in the frame, so it
    /// records the fully-updated state of the previous frame (particles included); the camera itself is disabled and
    /// rendered manually, which also works in batchmode where there is no game view. Use with Time.captureFramerate so
    /// game time advances exactly one frame per capture regardless of how long anything takes.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public class FrameRecorder : MonoBehaviour
    {
        Camera _cam;
        RenderTexture _rt;
        Texture2D _tex;
        byte[] _buf;
        Process _ff;
        Stream _stdin;
        readonly StringBuilder _ffLog = new StringBuilder();
        bool _armed;

        public int Frames { get; private set; }
        public string OutputPath { get; private set; }

        public bool Begin(Camera cam, RenderTexture rt, int fps, string outputPath, string ffmpeg, out string error)
        {
            error = null;
            _cam = cam;
            _rt = rt;
            OutputPath = Path.GetFullPath(outputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(OutputPath));
            _tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            _buf = new byte[rt.width * rt.height * 3];
            var psi = new ProcessStartInfo
            {
                FileName = string.IsNullOrEmpty(ffmpeg) ? "ffmpeg" : ffmpeg,
                Arguments = $"-y -loglevel error -f rawvideo -pix_fmt rgb24 -s {rt.width}x{rt.height} -r {fps} -i - " +
                            $"-vf vflip -c:v libx264 -preset medium -crf 17 -pix_fmt yuv420p -movflags +faststart \"{OutputPath}\"",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            };
            try
            {
                _ff = Process.Start(psi);
            }
            catch (Exception e)
            {
                error = "could not start ffmpeg: " + e.Message;
                return false;
            }
            _ff.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (_ffLog) _ffLog.AppendLine(e.Data); };
            _ff.BeginErrorReadLine();
            _stdin = _ff.StandardInput.BaseStream;
            _armed = true;
            return true;
        }

        void Update()
        {
            if (_armed) Capture();
        }

        public void Capture()
        {
            if (!_armed) return;
            _cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = _rt;
            _tex.ReadPixels(new Rect(0, 0, _rt.width, _rt.height), 0, 0, false);
            RenderTexture.active = prev;
            _tex.GetRawTextureData<byte>().CopyTo(_buf);
            try
            {
                _stdin.Write(_buf, 0, _buf.Length);
                Frames++;
            }
            catch (Exception e)
            {
                Debug.LogError("[AdSim] ffmpeg pipe failed: " + e.Message + "\n" + _ffLog);
                _armed = false;
            }
        }

        /// <summary>Capture the final frame, close the pipe and wait for the encoder.</summary>
        public bool End()
        {
            if (_ff == null) return false;
            Capture();
            _armed = false;
            try { _stdin.Flush(); _stdin.Close(); } catch { /* encoder already gone */ }
            bool ok = _ff.WaitForExit(120000) && _ff.ExitCode == 0;
            if (!ok) Debug.LogError("[AdSim] ffmpeg failed:\n" + _ffLog);
            _ff = null;
            Destroy(_tex);
            return ok;
        }

        void OnDestroy()
        {
            try { if (_ff != null && !_ff.HasExited) _ff.Kill(); } catch { /* gone */ }
        }
    }
}
