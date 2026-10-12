using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace AdSim
{
    /// <summary>
    /// Base of a game-specific ad "player". The game adapter answers three questions:
    /// which camera to capture (<see cref="AcquireCamera"/>), how to prepare the scene (<see cref="PrepareScene"/>,
    /// called once the camera renders at the capture size), and what to do (<see cref="Perform"/>: an iterator using
    /// <see cref="Motor"/> primitives; yield an IEnumerator to run it inline, yield null to wait a frame).
    /// The base handles fixed-rate time, capture, the hand overlay, the optional endcard, audio cues, the take log and
    /// finishing.
    /// Execution order: this runs before game scripts, so the game reads this frame's pointer.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public abstract class AdDirector : MonoBehaviour
    {
        protected AdRequest Request { get; private set; }
        protected Persona Persona { get; private set; }
        protected Rng Rng { get; private set; }
        protected HumanMotor Motor { get; private set; }
        protected HandOverlay Hand { get; private set; }
        protected Camera Cam { get; private set; }
        protected float Elapsed { get; private set; }
        /// <summary>When the decision loop should wrap up: leaves room for an outro and the tail before the hard cap.</summary>
        protected float TimeLimit => Mathf.Min(Persona.maxSeconds, Request.maxSeconds - Persona.tailSeconds - 0.8f - EndcardSeconds);
        /// <summary>True once the endcard plays: the game must ignore the hand from then on.</summary>
        protected bool EndcardActive { get; private set; }
        protected float W => Request.width;
        protected float H => Request.height;

        /// <summary>Raised once when the take is over (true = recorded and encoded).</summary>
        public event Action<bool> Finished;

        /// <summary>Loads a texture by asset path (set by the launcher; the editor uses the AssetDatabase).</summary>
        public Func<string, Texture2D> LoadTexture;
        /// <summary>Loads a font by asset path (set by the launcher).</summary>
        public Func<string, Font> LoadFont;

        HandProfile _handProfile;
        Texture2D _idle, _click;
        FrameRecorder _recorder;
        RenderTexture _rt;
        readonly IteratorRunner _runner = new IteratorRunner();
        readonly List<AudioCue> _cues = new List<AudioCue>();
        EndcardSpec _endcard;
        float EndcardSeconds => _endcard != null ? _endcard.duration : 0f;
        readonly TakeLog _log = new TakeLog();
        int _frame;
        bool _started, _done;
        protected bool Completed;

        public void Begin(AdRequest request, Persona persona, HandProfile hand, Texture2D idle, Texture2D click,
                          string endcardJson = null)
        {
            if (!string.IsNullOrWhiteSpace(endcardJson)) _endcard = EndcardSpec.FromJson(endcardJson);
            Request = request;
            Persona = persona;
            _handProfile = hand;
            _idle = idle;
            _click = click;
            Rng = new Rng(request.seed);
            _log.persona = persona.name;
            _log.seed = request.seed;
            _log.args = request.args;
            Time.captureFramerate = Mathf.Max(1, request.fps);
        }

        protected abstract Camera AcquireCamera();
        protected abstract void PrepareScene();
        protected abstract IEnumerator Perform();
        protected virtual void Cleanup() { }

        /// <summary>Play <paramref name="clip"/> (project-relative path) now; mixed into the MP4 at the end.</summary>
        /// <summary>Place a sound on the take's timeline: now, or at <paramref name="at"/> seconds when given (≥ 0).</summary>
        protected void AddAudioCue(string clip, float volume = 1f, float maxLength = 0f, float at = -1f) =>
            _cues.Add(new AudioCue { Clip = clip, Time = at >= 0f ? at : Elapsed, Volume = volume, MaxLength = maxLength });

        /// <summary>Called once the video is encoded, before the audio mix: last chance to add cues (e.g. a rendered
        /// gameplay sound track placed at 0 s).</summary>
        protected virtual void BeforeMix() { }

        /// <summary>The encoded video's path (for writing side files such as a rendered sound track).</summary>
        protected string OutputPath => _recorder != null ? _recorder.OutputPath : Request.output;

        protected void Log(string kind, string detail = "")
        {
            _log.events.Add(new TakeLog.Event { t = Elapsed, kind = kind, detail = detail });
            Debug.Log($"[AdSim] {Elapsed,6:0.00}s {kind} {detail}");
        }

        void Update()
        {
            if (_done || Request == null) return;
            if (!_started)
            {
                if (++_frame < 3) return; // let the scene run its Start()
                if (!StartTake()) { Finish(false); return; }
                _started = true;
                return;
            }
            Elapsed += Time.deltaTime;
            if (_endcard != null && !EndcardActive && Elapsed + EndcardSeconds + 2f / Request.fps >= Request.maxSeconds)
            {
                Log("cap", "cut to endcard");
                _runner.Start(PlayEndcard());
            }
            _runner.Step();
            Motor.Step(Time.deltaTime);
            Motor.Pointer.Commit();
            var p = Motor.Pointer;
            _log.path.Add(Elapsed); _log.path.Add(p.Position.x / W); _log.path.Add(p.Position.y / W); _log.path.Add(p.Down ? 1 : 0);
            if (!_runner.Running) Finish(true);
            else if (Request.maxSeconds > 0f && Elapsed + 2f / Request.fps >= Request.maxSeconds) // End() adds a frame
            {
                Log("cap", $"{Request.maxSeconds:0.#}s");
                Finish(true);
            }
        }

        bool StartTake()
        {
            Cam = AcquireCamera();
            if (Cam == null) { Debug.LogError("[AdSim] no camera"); return false; }
            _rt = new RenderTexture(Request.width, Request.height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            _rt.antiAliasing = Request.msaa >= 8 ? 8 : Request.msaa >= 4 ? 4 : Request.msaa >= 2 ? 2 : 1; // must be a power of two
            _rt.Create();
            Debug.Log($"[AdSim] capture {Request.width}x{Request.height} @ {Request.fps} fps, MSAA {_rt.antiAliasing}x");
            Cam.targetTexture = _rt;
            Cam.aspect = Request.width / (float)Request.height;
            Cam.enabled = false; // rendered by the recorder

            Motor = new HumanMotor(Persona, Rng, Request.width, Request.height);
            Motor.Base = new Vector2(W * 1.2f, -H * 0.1f); // off-screen bottom-right
            PrepareScene();

            Hand = new GameObject("AdSim Hand").AddComponent<HandOverlay>();
            Hand.Init(Cam, Motor.Pointer, _handProfile, _idle, _click, Persona.handScale, W);

            _recorder = gameObject.AddComponent<FrameRecorder>();
            if (!_recorder.Begin(Cam, _rt, Request.fps, Request.output, Request.ffmpeg, out var error))
            {
                Debug.LogError("[AdSim] " + error);
                return false;
            }
            _runner.Start(Root());
            Log("start", $"persona={Persona.name} seed={Request.seed} {Request.args}");
            return true;
        }

        IEnumerator Root()
        {
            yield return Perform();
            yield return Motor.Hover(_endcard != null ? Mathf.Min(Persona.tailSeconds, 0.6f) : Persona.tailSeconds);
            if (_endcard != null) yield return PlayEndcard();
        }

        IEnumerator PlayEndcard()
        {
            EndcardActive = true;
            Motor.Pointer.Down = false;
            Log("endcard", _endcard.name);
            var player = new EndcardPlayer(_endcard, Cam, Motor, LoadTexture, LoadFont, (clip, volume, maxLength) => AddAudioCue(clip, volume, maxLength));
            yield return player.Play();
        }

        void Finish(bool ok)
        {
            if (_done) return;
            _done = true;
            if (_recorder != null) ok &= _recorder.End();
            if (ok)
            {
                try { BeforeMix(); }
                catch (Exception e) { Debug.LogError("[AdSim] BeforeMix: " + e); }
            }
            if (ok && _cues.Count > 0)
            {
                if (AudioMix.Mix(Request.ffmpeg, _recorder.OutputPath, _cues, _recorder.Frames / (float)Request.fps, out var mixError))
                    Log("audio", $"{_cues.Count} cue(s) mixed");
                else Debug.LogError("[AdSim] " + mixError);
            }
            _log.frames = _recorder != null ? _recorder.Frames : 0;
            _log.seconds = Elapsed;
            _log.completed = Completed;
            try
            {
                if (_recorder != null)
                    File.WriteAllText(Path.ChangeExtension(_recorder.OutputPath, ".json"), JsonUtility.ToJson(_log));
            }
            catch (Exception e) { Debug.LogWarning("[AdSim] could not write take log: " + e.Message); }
            Cleanup();
            Time.captureFramerate = 0;
            if (Cam != null) { Cam.targetTexture = null; Cam.enabled = true; }
            Debug.Log($"[AdSim] take finished ok={ok} frames={_log.frames} seconds={Elapsed:0.0} completed={Completed} -> {Request.output}");
            Finished?.Invoke(ok);
        }
    }
}
