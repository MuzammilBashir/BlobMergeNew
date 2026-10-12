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
    /// The base handles fixed-rate time, capture, the hand overlay, the take log and finishing.
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
        protected float TimeLimit => Mathf.Min(Persona.maxSeconds, Request.maxSeconds - Persona.tailSeconds - 0.8f);
        protected float W => Request.width;
        protected float H => Request.height;

        /// <summary>Raised once when the take is over (true = recorded and encoded).</summary>
        public event Action<bool> Finished;

        HandProfile _handProfile;
        Texture2D _idle, _click;
        FrameRecorder _recorder;
        RenderTexture _rt;
        readonly Stack<IEnumerator> _stack = new Stack<IEnumerator>();
        readonly TakeLog _log = new TakeLog();
        int _frame;
        bool _started, _done;
        protected bool Completed;

        public void Begin(AdRequest request, Persona persona, HandProfile hand, Texture2D idle, Texture2D click)
        {
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
            Pump();
            Motor.Step(Time.deltaTime);
            Motor.Pointer.Commit();
            var p = Motor.Pointer;
            _log.path.Add(Elapsed); _log.path.Add(p.Position.x / W); _log.path.Add(p.Position.y / W); _log.path.Add(p.Down ? 1 : 0);
            if (_stack.Count == 0) Finish(true);
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
            _rt.Create();
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
            _stack.Push(Root());
            Log("start", $"persona={Persona.name} seed={Request.seed} {Request.args}");
            return true;
        }

        IEnumerator Root()
        {
            yield return Perform();
            yield return Motor.Hover(Persona.tailSeconds);
        }

        void Pump()
        {
            for (int guard = 0; _stack.Count > 0 && guard < 10000; guard++)
            {
                var it = _stack.Peek();
                if (!it.MoveNext()) { _stack.Pop(); continue; }
                if (it.Current is IEnumerator sub) { _stack.Push(sub); continue; }
                return; // yielded a frame
            }
        }

        void Finish(bool ok)
        {
            if (_done) return;
            _done = true;
            if (_recorder != null) ok &= _recorder.End();
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
