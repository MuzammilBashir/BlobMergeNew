using System;
using System.Collections.Generic;
using UnityEngine;

namespace BlobMerge
{
    /// <summary>
    /// Touch/mouse aiming: press on any throwable blob, drag TOWARD where it should go, release to throw.
    /// Drag length sets power. Draws a power arrow and a ballistic trajectory preview.
    /// Game logic plugs in through PickBlob / OnSelect / OnAim / OnCancel / OnLaunch.
    /// </summary>
    public class AimController : MonoBehaviour
    {
        public enum AimMode
        {
            [Tooltip("Blob flies from where it sits toward the finger; distance from the blob = power.")]
            TowardFinger,
            [Tooltip("Direction/power from the drag gesture (finger now minus where it first touched).")]
            DragDelta
        }

        [Header("Gesture")]
        public AimMode aimMode = AimMode.TowardFinger;
        [Tooltip("Drag shorter than this (world units, beyond the blob's edge) cancels the throw.")]
        public float minDrag = 0.35f;
        [Tooltip("Extra distance (beyond the dead zone) that gives full power.")]
        public float maxDrag = 3.4f;
        public float minSpeed = 6f;
        public float maxSpeed = 24f;
        [Tooltip("Power curve exponent (1 = linear).")]
        public float powerCurve = 1.15f;
        [Tooltip("Extra pick radius around a blob for fat fingers.")]
        public float pickPadding = 0.45f;

        [Header("Visuals")]
        public float arrowMinLength = 1.0f;
        public float arrowMaxLength = 3.6f;
        public Color arrowColor = Color.white;
        public Color arrowFullPowerColor = new Color(1f, 0.78f, 0.45f);
        [Tooltip("Fading arrow (Fish scene): one colour (Arrow Color, no full-power tint), opacity from Arrow Start Alpha " +
                 "at the blob to Arrow End Alpha at the arrowhead; its shadow fades with it.")]
        public bool arrowFade = false;
        [Range(0f, 1f)] public float arrowStartAlpha = 0.8f;
        [Range(0f, 1f)] public float arrowEndAlpha = 0.3f;
        public bool showTrajectory = true;
        public int trajectoryDots = 18;
        public float trajectoryTimeStep = 0.045f;

        // wiring
        public Func<Vector2, Blob> PickBlob;
        public Action<Blob> OnSelect;
        public Action<Blob, Vector2, float> OnAim;      // blob, direction, power 0..1
        public Action<Blob> OnCancel;
        public Action<Blob, Vector2, float> OnLaunch;   // blob, velocity, power
        /// <summary>(start, velocity, radius, secondsPerDot, maxDots, output) — fills predicted flight points.</summary>
        public Action<Vector2, Vector2, float, float, int, List<Vector2>> PredictPath;
        /// <summary>Maps a pointer seen through the shaking camera back to the steady world, so aim doesn't jitter.</summary>
        public Func<Vector2, Vector2> PointerCorrect;
        public Material material;
        public bool InputEnabled = true;

        // simulated pointer (debug autopilot)
        [NonSerialized] public bool SimEnabled, SimDown, SimUp;
        [NonSerialized] public Vector2 SimPos;

        /// <summary>A scripted pointer in screen pixels (origin bottom-left), e.g. the AdSim ad recorder's hand.</summary>
        public struct ScriptedPointer { public bool Pressed, Released; public Vector2 Screen; }
        /// <summary>When set, replaces device input; positions go through the same camera + un-shake path as touches.</summary>
        public static Func<ScriptedPointer> Override;

        public Blob Held { get; private set; }
        Vector2 dragStart, anchor;
        readonly List<Vector2> path = new List<Vector2>(64);
        Camera cam;
        Mesh mesh;
        MeshRenderer meshRenderer;
        readonly List<Vector3> v = new List<Vector3>(512);
        readonly List<Color> c = new List<Color>(512);
        readonly List<int> t = new List<int>(1024);

        void Awake()
        {
            var go = new GameObject("AimGuide");
            mesh = new Mesh { name = "AimGuide" };
            mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            meshRenderer = go.AddComponent<MeshRenderer>();
            meshRenderer.sortingOrder = 40;
        }

        void Start()
        {
            cam = Camera.main;
            meshRenderer.sharedMaterial = material;
        }

        bool Down() => Override != null ? Override().Pressed : SimEnabled ? SimDown : Input.GetMouseButtonDown(0);
        bool Up() => Override != null ? Override().Released : SimEnabled ? SimUp : Input.GetMouseButtonUp(0);
        Vector2 Pos()
        {
            if (SimEnabled && Override == null) return SimPos;
            Vector2 p = cam.ScreenToWorldPoint(Override != null ? (Vector3)Override().Screen : Input.mousePosition);
            return PointerCorrect != null ? PointerCorrect(p) : p;
        }

        public void Cancel()
        {
            if (Held != null) OnCancel?.Invoke(Held);
            Held = null;
            mesh.Clear();
        }

        void Update()
        {
            if (!InputEnabled) { if (Held != null) Cancel(); return; }

            if (Held == null && Down())
            {
                var b = PickBlob?.Invoke(Pos());
                if (b != null)
                {
                    Held = b;
                    dragStart = Pos();
                    anchor = b.Body.Center; // fixed origin: the blob's spot when grabbed
                    OnSelect?.Invoke(b);
                }
            }

            if (Held == null) return;

            if (Held.State != BlobState.Aiming) { Held = null; mesh.Clear(); return; } // stolen (merged etc.)

            Vector2 finger = Pos();
            Vector2 origin = aimMode == AimMode.TowardFinger ? anchor : dragStart;
            float dead = aimMode == AimMode.TowardFinger ? minDrag + Held.CurrentRadius * 0.6f : minDrag;
            Vector2 drag = finger - origin;
            float len = drag.magnitude;
            bool valid = len >= dead;
            Vector2 dir = len > 1e-4f ? drag / len : Vector2.down;
            float power = Mathf.Pow(Mathf.Clamp01((len - dead) / Mathf.Max(0.01f, maxDrag)), powerCurve);

            OnAim?.Invoke(Held, valid ? dir : Vector2.zero, valid ? power : 0f);
            Draw(Held, origin, finger, dir, power, valid);

            if (Up())
            {
                var b = Held;
                Held = null;
                mesh.Clear();
                if (valid) OnLaunch?.Invoke(b, dir * Mathf.Lerp(minSpeed, maxSpeed, power), power);
                else OnCancel?.Invoke(b);
            }
        }

        void Draw(Blob b, Vector2 origin, Vector2 finger, Vector2 dir, float power, bool valid)
        {
            v.Clear(); c.Clear(); t.Clear();
            if (valid)
            {
                Vector2 centre = b.Body.Center;
                Color col = arrowFade ? arrowColor : Color.Lerp(arrowColor, arrowFullPowerColor, power * power);

                if (showTrajectory && PredictPath != null)
                {
                    // same solver settings as the real flight (gravity, damping, walls, bumpers)
                    path.Clear();
                    PredictPath(centre, dir * Mathf.Lerp(minSpeed, maxSpeed, power), b.CurrentRadius * 0.9f, trajectoryTimeStep, trajectoryDots, path);
                    for (int i = 0; i < path.Count; i++)
                    {
                        float k = (i + 1) / (float)(trajectoryDots + 1);
                        float a = (1f - k) * 0.75f;
                        float s = 0.075f * (1f - k * 0.6f);
                        MeshUtil.AddDisc(v, c, t, path[i], s, s, 0f, 8, new Color(col.r, col.g, col.b, a));
                    }
                }

                // arrow from the blob toward the finger; in TowardFinger mode its tip sits under the finger
                float length = aimMode == AimMode.TowardFinger
                    ? Mathf.Clamp(Vector2.Distance(centre, finger) - b.CurrentRadius * 0.35f, arrowMinLength, arrowMaxLength)
                    : Mathf.Lerp(arrowMinLength, arrowMaxLength, power);
                Vector2 from = centre + dir * (b.CurrentRadius * 0.35f);
                float w = Mathf.Lerp(0.14f, 0.26f, power);
                // soft shadow + arrow
                if (arrowFade)
                {
                    MeshUtil.AddArrowFaded(v, c, t, from, dir, length + 0.06f, w + 0.1f, 0.62f, 0.78f + 0.25f * power, new Color(0, 0, 0, 0.35f), arrowStartAlpha, arrowEndAlpha);
                    MeshUtil.AddArrowFaded(v, c, t, from, dir, length, w, 0.55f, 0.66f + 0.25f * power, col, arrowStartAlpha, arrowEndAlpha);
                }
                else
                {
                    MeshUtil.AddArrow(v, c, t, from, dir, length + 0.06f, w + 0.1f, 0.62f, 0.78f + 0.25f * power, new Color(0, 0, 0, 0.35f));
                    MeshUtil.AddArrow(v, c, t, from, dir, length, w, 0.55f, 0.66f + 0.25f * power, col);
                }
            }
            mesh.Clear();
            mesh.SetVertices(v);
            mesh.SetColors(c);
            mesh.SetTriangles(t, 0);
            mesh.RecalculateBounds();
        }
    }
}
