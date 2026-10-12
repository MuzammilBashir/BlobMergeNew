using System.Collections.Generic;
using UnityEngine;

namespace BlobMerge
{
    public struct BodyContact
    {
        public SoftBody A, B;
        public Vector2 Point;
    }

    /// <summary>
    /// Sub-stepped position-based soft-body solver: bodies vs half-planes (walls/floor/chamfers),
    /// static circles (bumpers) and each other (point-in-polygon push-out).
    /// </summary>
    public class SoftBodyWorld
    {
        public readonly List<SoftBody> Bodies = new List<SoftBody>();
        public readonly List<BodyContact> Contacts = new List<BodyContact>();

        public Vector2 Gravity = new Vector2(0, -20f);
        public int Substeps = 4;
        public int Iterations = 2;
        public float Damping = 0.35f;          // per second
        public float DeformationDamping = 0f;  // per substep, shape motion only
        public float EdgeStiffness = 0.6f;
        public float AreaStiffness = 0f;       // optional hard volume constraint (0 = pressure only)
        public bool SlackSkin = true;          // skin resists stretch only (balloon)
        public float MinVolume = 0.55f;        // crush limit as a fraction of rest area
        public float Gap = 0f;                 // minimum empty space kept between bodies
        public float ContactSlop = 0.03f;      // bodies within Gap + this count as touching (for merges)
        public float Restitution = 0.25f;      // walls / floor / ceiling
        public float BumperRestitution = 0.6f; // round bumpers
        public float BodyRestitution = 0.25f;  // blob vs blob
        public float BounceMinSpeed = 2.5f;    // slower contacts don't bounce (keeps the pile calm)
        public float Friction = 0.06f;         // tangential loss per substep on walls
        public float ContactDamping = 0.04f;   // velocity loss for points pushed out of another body
        public float MaxSpeed = 30f;

        readonly List<(Vector2 n, float d)> planes = new List<(Vector2, float)>();
        readonly List<(Vector2 c, float r)> circles = new List<(Vector2, float)>();
        readonly HashSet<long> pairSeen = new HashSet<long>();

        public void AddPlane(Vector2 normal, Vector2 pointOnPlane)
        {
            normal.Normalize();
            planes.Add((normal, Vector2.Dot(normal, pointOnPlane)));
        }

        public void AddCircle(Vector2 c, float r) => circles.Add((c, r));

        /// <summary>
        /// Predicts a blob's centre path using the same integration as Step (gravity, per-substep damping,
        /// speed cap, wall/bumper bounces), treating the blob as a circle of 'radius'. Stops at the floor.
        /// </summary>
        public void PredictPath(Vector2 p, Vector2 vel, float radius, float frameDt, float secondsPerPoint, int maxPoints, List<Vector2> output)
        {
            float h = frameDt / Substeps;
            float damp = Mathf.Clamp01(1f - Damping * h);
            Vector2 v = vel * h;                    // per-substep displacement, like Verlet
            Vector2 g = Gravity * h * h;
            float maxStep = MaxSpeed * h;
            int perPoint = Mathf.Max(1, Mathf.RoundToInt(secondsPerPoint / h));
            int total = perPoint * maxPoints;

            for (int s = 1; s <= total; s++)
            {
                v *= damp;
                if (v.sqrMagnitude > maxStep * maxStep) v = v.normalized * maxStep;
                v += g;
                p += v;

                bool floor = false;
                for (int k = 0; k < planes.Count; k++)
                {
                    var (n, d) = planes[k];
                    float sd = Vector2.Dot(n, p) - (d + radius);
                    if (sd >= 0f) continue;
                    if (n.y > 0.5f) floor = true;
                    p -= n * sd;
                    float vn = Vector2.Dot(v, n);
                    if (vn < 0f) v -= n * vn * (1f + Restitution);
                }
                for (int k = 0; k < circles.Count; k++)
                {
                    var (c, r) = circles[k];
                    Vector2 dd = p - c;
                    float rr = r + radius;
                    if (dd.sqrMagnitude >= rr * rr || dd.sqrMagnitude < 1e-8f) continue;
                    Vector2 n = dd.normalized;
                    p = c + n * rr;
                    float vn = Vector2.Dot(v, n);
                    if (vn < 0f) v -= n * vn * (1f + BumperRestitution);
                }

                if (s % perPoint == 0) output.Add(p);
                if (floor) break;
            }
        }

        public void Step(float dt)
        {
            Contacts.Clear();
            pairSeen.Clear();
            for (int i = 0; i < Bodies.Count; i++) Bodies[i].Impact = 0f;

            float h = dt / Substeps;
            float damp = Mathf.Clamp01(1f - Damping * h);
            float maxStep = MaxSpeed * h;
            float gUnit = Mathf.Max(1f, Gravity.magnitude);

            for (int s = 0; s < Substeps; s++)
            {
                for (int i = 0; i < Bodies.Count; i++)
                {
                    Bodies[i].DampDeformation(DeformationDamping);
                    Bodies[i].Integrate(h, Gravity, damp, maxStep, gUnit);
                }

                for (int it = 0; it < Iterations; it++)
                {
                    for (int i = 0; i < Bodies.Count; i++)
                    {
                        var b = Bodies[i];
                        b.SolveEdges(EdgeStiffness, SlackSkin);
                        if (AreaStiffness > 0f) b.SolveArea(AreaStiffness);
                        b.SolveMinArea(MinVolume);
                        b.SolveShape(b.ShapeStiffness);
                    }
                }

                for (int i = 0; i < Bodies.Count; i++) Bodies[i].RecalcBounds();

                for (int i = 0; i < Bodies.Count; i++)
                    for (int j = i + 1; j < Bodies.Count; j++)
                        CollidePair(Bodies[i], Bodies[j], h);

                for (int i = 0; i < Bodies.Count; i++) CollideStatic(Bodies[i], h);
            }

            for (int i = 0; i < Bodies.Count; i++)
            {
                var b = Bodies[i];
                Vector2 c = Vector2.zero, v = Vector2.zero;
                for (int k = 0; k < b.N; k++) { c += b.P[k]; v += b.P[k] - b.Prev[k]; }
                b.Center = c / b.N;
                b.Velocity = v / (b.N * h);
                b.RecalcBounds();
            }
        }

        void CollidePair(SoftBody a, SoftBody b, float h)
        {
            float reach = Gap + ContactSlop;
            if (a.Max.x + reach < b.Min.x || b.Max.x + reach < a.Min.x || a.Max.y + reach < b.Min.y || b.Max.y + reach < a.Min.y) return;
            if (a.IgnoreGroup == b.Group || b.IgnoreGroup == a.Group) return;
            float ia = a.InvMass / a.MassScale, ib = b.InvMass / b.MassScale;
            float wa = ia / (ia + ib);
            bool hit = PushOut(a, b, wa, 1f - wa, h, out var pa);
            hit |= PushOut(b, a, 1f - wa, wa, h, out var pb);
            if (!hit) return;

            long key = a.Id < b.Id ? ((long)a.Id << 32) | (uint)b.Id : ((long)b.Id << 32) | (uint)a.Id;
            if (pairSeen.Add(key))
                Contacts.Add(new BodyContact { A = a, B = b, Point = pa != Vector2.zero ? pa : pb });
        }

        /// <summary>
        /// Keep points of 'a' at least Gap outside polygon 'b' (pushing inside points out, and outside points
        /// that are closer than Gap away). Returns true if any point is within Gap + ContactSlop (= touching).
        /// </summary>
        bool PushOut(SoftBody a, SoftBody b, float wa, float wb, float h, out Vector2 point)
        {
            point = Vector2.zero;
            bool any = false;
            var bp = b.P;
            int bn = b.N;
            float gap = Gap, reach = Gap + ContactSlop;
            for (int i = 0; i < a.N; i++)
            {
                Vector2 p = a.P[i];
                if (p.x < b.Min.x - reach || p.x > b.Max.x + reach || p.y < b.Min.y - reach || p.y > b.Max.y + reach) continue;
                bool inside = b.Contains(p);
                if (!inside && reach <= 0f) continue;

                float best = float.MaxValue, bt = 0f;
                int bj = 0;
                Vector2 bq = p;
                for (int j = 0; j < bn; j++)
                {
                    Vector2 e0 = bp[j], e1 = bp[j + 1 == bn ? 0 : j + 1];
                    Vector2 e = e1 - e0;
                    float l2 = e.sqrMagnitude;
                    float t = l2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - e0, e) / l2) : 0f;
                    Vector2 q = e0 + e * t;
                    float d = (q - p).sqrMagnitude;
                    if (d < best) { best = d; bj = j; bt = t; bq = q; }
                }

                int bj1 = bj + 1 == bn ? 0 : bj + 1;
                float dist = Mathf.Sqrt(best);
                if (!inside && dist >= reach) continue;
                any = true;
                point = bq;
                if (!inside && dist >= gap) continue; // touching within slop: counts as contact, no push

                // push direction: away from b (outward edge normal when the point sits exactly on the edge)
                Vector2 edge = bp[bj1] - bp[bj];
                Vector2 outward = new Vector2(edge.y, -edge.x).normalized;
                Vector2 dir = dist > 1e-5f ? (inside ? (bq - p) : (p - bq)) / dist : outward;
                Vector2 pen = dir * (inside ? dist + gap : gap - dist);
                float denom = (1f - bt) * (1f - bt) + bt * bt;
                float s = wb / denom;

                // relative normal velocity (per substep) before resolving, for restitution
                float penLen = pen.magnitude;
                Vector2 n = penLen > 1e-6f ? pen / penLen : Vector2.zero;
                Vector2 va = a.P[i] - a.Prev[i];
                Vector2 vb = Vector2.Lerp(bp[bj] - b.Prev[bj], bp[bj1] - b.Prev[bj1], bt);
                float vn = Vector2.Dot(va - vb, n); // < 0 = approaching

                a.P[i] += pen * wa;
                bp[bj] -= pen * s * (1f - bt);
                bp[bj1] -= pen * s * bt;

                if (vn < 0f && -vn / h > BounceMinSpeed && BodyRestitution > 0f)
                {
                    // reflect the approach velocity: change velocity by writing Prev (v = P - Prev)
                    Vector2 dv = n * (-(1f + BodyRestitution) * vn);
                    a.Prev[i] -= dv * wa;
                    b.Prev[bj] += dv * s * (1f - bt);
                    b.Prev[bj1] += dv * s * bt;
                }
                else
                {
                    a.Prev[i] = Vector2.Lerp(a.Prev[i], a.P[i], ContactDamping);
                }
            }
            return any;
        }

        void CollideStatic(SoftBody b, float h)
        {
            for (int i = 0; i < b.N; i++)
            {
                Vector2 p = b.P[i];
                Vector2 v = p - b.Prev[i];
                bool touched = false;

                for (int k = 0; k < planes.Count; k++)
                {
                    var (n, d) = planes[k];
                    float sd = Vector2.Dot(n, p) - d;
                    if (sd >= 0f) continue;
                    p -= n * sd;
                    float vn = Vector2.Dot(v, n);
                    Vector2 vt = v - n * vn;
                    if (vn < 0f)
                    {
                        float speed = -vn / h;
                        if (speed > b.Impact) { b.Impact = speed; b.ImpactPoint = p; }
                        vn = -vn * Restitution;
                    }
                    v = n * vn + vt * (1f - Friction);
                    touched = true;
                }

                for (int k = 0; k < circles.Count; k++)
                {
                    var (c, r) = circles[k];
                    Vector2 d = p - c;
                    float m2 = d.sqrMagnitude;
                    if (m2 >= r * r || m2 < 1e-8f) continue;
                    float m = Mathf.Sqrt(m2);
                    Vector2 n = d / m;
                    p = c + n * r;
                    float vn = Vector2.Dot(v, n);
                    if (vn < 0f)
                    {
                        float speed = -vn / h;
                        if (speed > b.Impact) { b.Impact = speed; b.ImpactPoint = p; }
                        v -= n * vn * (1f + BumperRestitution);
                    }
                    touched = true;
                }

                if (touched)
                {
                    b.P[i] = p;
                    b.Prev[i] = p - v;
                }
            }
        }
    }
}
