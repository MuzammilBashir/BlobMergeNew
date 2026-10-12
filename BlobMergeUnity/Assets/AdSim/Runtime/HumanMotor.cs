using System.Collections;
using UnityEngine;

namespace AdSim
{
    /// <summary>
    /// Turns intentions into a human-looking pointer path. Primitives are iterators (yield null = next frame) that move
    /// <see cref="Base"/>; every frame <see cref="Step"/> adds Ornstein-Uhlenbeck jitter and writes the pointer.
    ///  * travel: minimum-jerk profile, sideways bow, overshoot + small correction (fast aimed movement);
    ///  * hover: slow drift around a spot (thinking);
    ///  * press/release with a short dwell.
    /// Game adapters drive drags themselves by setting <see cref="Base"/> each frame (they know the geometry).
    /// </summary>
    public class HumanMotor
    {
        public readonly VirtualPointer Pointer = new VirtualPointer();
        public Vector2 Base;
        public readonly float W, H;

        readonly Persona _p;
        readonly Rng _rng;
        Vector2 _jitter;

        public HumanMotor(Persona persona, Rng rng, float width, float height)
        {
            _p = persona;
            _rng = rng;
            W = width;
            H = height;
        }

        /// <summary>The persona's peak travel speed (W/s); scripted sequences use it to normalise their own pace.</summary>
        public float TravelSpeed => _p.travelSpeed;

        public void Step(float dt)
        {
            float a = Mathf.Exp(-dt / Mathf.Max(0.01f, _p.jitterTau));
            _jitter = _jitter * a + _rng.Gauss2() * (_p.jitter * W * Mathf.Sqrt(1f - a * a));
            Pointer.Position = Base + _jitter;
        }

        public static float MinJerk(float u)
        {
            u = Mathf.Clamp01(u);
            return u * u * u * (10f + u * (-15f + 6f * u));
        }

        /// <summary>Duration of a minimum-jerk move of <paramref name="distance"/> with the given peak speed.</summary>
        public static float MinJerkTime(float distance, float peakSpeed) => 1.875f * distance / Mathf.Max(1e-4f, peakSpeed);

        public IEnumerator Wait(float seconds)
        {
            for (float t = 0f; t < seconds; t += Time.deltaTime) yield return null;
        }

        /// <summary>Aimed movement to <paramref name="target"/> (pixels). Not precise = no correction (pointing, not touching).</summary>
        public IEnumerator MoveTo(Vector2 target, float speedScale = 1f, bool precise = true)
        {
            Vector2 start = Base;
            float d = Vector2.Distance(start, target);
            if (d < 1f) yield break;
            Vector2 dir = (target - start) / d, side = new Vector2(-dir.y, dir.x);
            Vector2 aim = target + dir * (d * _p.overshoot * _rng.Range(0.2f, 1.3f)) + _rng.Gauss2() * (_p.aimError * W);
            float bow = d * _p.curvature * _rng.Range(-1f, 1f);
            float T = Mathf.Max(_p.minTravelTime, MinJerkTime(d, _p.travelSpeed * W * speedScale));
            for (float t = 0f; ; )
            {
                t += Time.deltaTime;
                float s = MinJerk(t / T);
                Base = Vector2.Lerp(start, aim, s) + side * (bow * Mathf.Sin(Mathf.PI * s));
                if (t >= T) break;
                yield return null;
            }
            if (!precise) yield break;
            // corrective sub-movement onto the exact spot
            Vector2 from = Base;
            float c = Vector2.Distance(from, target);
            if (c < 1f) { Base = target; yield break; }
            float T2 = Mathf.Max(0.07f, MinJerkTime(c, 0.35f * W * speedScale));
            for (float t = 0f; ; )
            {
                t += Time.deltaTime;
                Base = Vector2.Lerp(from, target, MinJerk(t / T2));
                if (t >= T2) break;
                yield return null;
            }
        }

        /// <summary>Hover for <paramref name="seconds"/>, drifting slowly around the current spot.</summary>
        public IEnumerator Hover(float seconds)
        {
            Vector2 anchor = Base, drift = Vector2.zero;
            float amp = _p.idleDrift * W;
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                float a = Mathf.Exp(-Time.deltaTime / 1.2f);
                drift = drift * a + _rng.Gauss2() * (amp * Mathf.Sqrt(1f - a * a));
                Base = Vector2.Lerp(Base, anchor + drift, 1f - Mathf.Exp(-Time.deltaTime / 0.3f));
                yield return null;
            }
        }

        public IEnumerator Press()
        {
            Pointer.Down = true;
            yield return Wait(_rng.Range(_p.pressDwell));
        }

        public IEnumerator Release()
        {
            Pointer.Down = false;
            yield return null;
        }

        /// <summary>Tap = press + short dwell + release.</summary>
        public IEnumerator Tap()
        {
            yield return Press();
            yield return Wait(_rng.Range(0.04f, 0.1f));
            yield return Release();
        }
    }
}
