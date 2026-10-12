using UnityEngine;

namespace BlobMerge
{
    /// <summary>
    /// Pressure soft body: a closed ring of Verlet points held together by edge springs,
    /// an area (volume) constraint and weak shape matching. Low shape stiffness = water-bubble wobble.
    /// </summary>
    public class SoftBody
    {
        public readonly int N;
        public readonly Vector2[] P, Prev;
        readonly Vector2[] rest;      // rest offsets from centre (counter-clockwise)
        readonly float[] edgeRest;    // rest length of edge i -> i+1
        public readonly float RestArea;
        public readonly float RestRadius;

        public float AreaScale = 1f;  // animated size (area multiplier)
        public float InvMass;         // per point
        public float MassScale = 1f;
        public bool UseGravity;
        public Vector2 ExtraAccel;
        public float ShapeStiffness = 0.05f;
        /// <summary>
        /// Gas pressure inside the balloon, in units of gravity: at 1 the outward push on each skin point
        /// roughly equals its weight. Low = soft balloon that slumps flat; high = tight, stays round.
        /// Follows the ideal gas law, so squeezing a blob raises its pressure.
        /// </summary>
        public float Pressure = 1.5f;
        readonly float restMeanEdge;
        Vector2[] accel;
        readonly Vector4 aqqInv;

        public object Owner;
        public int Id;
        public int Group;              // collision group (e.g. lane blobs)
        public int IgnoreGroup = -1;   // don't collide with bodies of this group (-1 = none)

        // updated by the world
        public Vector2 Center, Velocity;
        public Vector2 Min, Max;
        public float Impact;          // strongest wall/floor normal speed this step
        public Vector2 ImpactPoint;

        public float Mass => 1f / Mathf.Max(1e-6f, InvMass) * N;

        /// <param name="noise">Lumpiness of the outline (fraction of radius).</param>
        /// <param name="elongation">Max random stretch along a random axis (0.2 = up to 20% longer), for pill/potato shapes.</param>
        /// <param name="hexCorner">If ≥ 0, the rest outline is a regular hexagon (n must be a multiple of 6) whose corners
        /// are rounded by this fraction of a side; noise and elongation are ignored.</param>
        public SoftBody(int n, float radius, float noise, int seed, Vector2 center, float density, float elongation = 0f, float hexCorner = -1f)
        {
            N = n;
            RestRadius = radius;
            P = new Vector2[n];
            Prev = new Vector2[n];
            rest = new Vector2[n];
            edgeRest = new float[n];

            if (hexCorner >= 0f) HexOutline(radius, hexCorner);
            else LumpyOutline(radius, noise, seed, elongation);

            // centre the rest shape and normalise its area to a circle of 'radius' so sizes stay consistent
            Vector2 cen = Vector2.zero;
            for (int i = 0; i < n; i++) cen += rest[i];
            cen /= n;
            for (int i = 0; i < n; i++) rest[i] -= cen;
            float a1 = 0f;
            for (int i = 0; i < n; i++) { int j = (i + 1) % n; a1 += rest[i].x * rest[j].y - rest[j].x * rest[i].y; }
            float fit = Mathf.Sqrt(Mathf.PI * radius * radius / Mathf.Max(1e-5f, a1 * 0.5f));
            for (int i = 0; i < n; i++) rest[i] *= fit;

            // precompute inverse of Aqq = sum q q^T for affine shape matching
            float qxx = 0f, qxy = 0f, qyy = 0f;
            for (int i = 0; i < n; i++) { qxx += rest[i].x * rest[i].x; qxy += rest[i].x * rest[i].y; qyy += rest[i].y * rest[i].y; }
            float det = qxx * qyy - qxy * qxy;
            if (Mathf.Abs(det) < 1e-8f) det = 1e-8f;
            aqqInv = new Vector4(qyy / det, -qxy / det, -qxy / det, qxx / det); // (m00, m01, m10, m11)

            float area = 0f;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                edgeRest[i] = (rest[j] - rest[i]).magnitude;
                area += rest[i].x * rest[j].y - rest[j].x * rest[i].y;
            }
            RestArea = area * 0.5f;
            float perim = 0f;
            for (int i = 0; i < n; i++) perim += edgeRest[i];
            restMeanEdge = perim / n;
            accel = new Vector2[n];
            InvMass = n / (RestArea * density);
            Place(center, 1f);
        }

        /// <summary>
        /// Regular flat-topped hexagon with rounded corners (a smaller hexagon grown by a disc), sampled evenly by arc
        /// length starting at the middle of a corner, so every corner gets a point on its apex and faces stay flat.
        /// </summary>
        void HexOutline(float radius, float corner)
        {
            const float cos30 = 0.8660254f;
            float rc = Mathf.Clamp(corner, 0f, 0.9f) * radius * cos30;     // fillet radius
            float ri = (radius * cos30 - rc) / cos30;                      // circumradius of the inner hexagon
            float arc = rc * Mathf.PI / 3f, unit = arc + ri, total = 6f * unit;
            for (int i = 0; i < N; i++)
            {
                float u = Mathf.Repeat(i / (float)N * total - arc * 0.5f, total);
                int k = Mathf.Min(5, (int)(u / unit));
                float rem = u - k * unit;
                Vector2 ck = Dir(60f * k) * ri;
                if (rem < arc)
                    rest[i] = ck + Dir(60f * k - 30f + 60f * rem / Mathf.Max(1e-6f, arc)) * rc;
                else
                {
                    Vector2 nrm = Dir(60f * k + 30f) * rc;
                    Vector2 ck1 = Dir(60f * (k + 1)) * ri;
                    rest[i] = Vector2.Lerp(ck + nrm, ck1 + nrm, (rem - arc) / Mathf.Max(1e-6f, ri));
                }
            }
        }

        static Vector2 Dir(float deg) => new Vector2(Mathf.Cos(deg * Mathf.Deg2Rad), Mathf.Sin(deg * Mathf.Deg2Rad));

        void LumpyOutline(float radius, float noise, int seed, float elongation)
        {
            int n = N;
            var rnd = new System.Random(seed);
            float R() => (float)rnd.NextDouble();
            // random harmonic lumps, each blob gets its own mix
            float p1 = R() * 6.28f, p2 = R() * 6.28f, p3 = R() * 6.28f, p4 = R() * 6.28f;
            float w2 = 0.5f + R() * 0.5f, w3 = 0.25f + R() * 0.5f, w4 = R() * 0.35f, w5 = R() * 0.25f;
            float norm = 1f / (w2 + w3 + w4 + w5);
            float stretch = 1f + elongation * R();
            float axis = R() * Mathf.PI;
            float ca = Mathf.Cos(axis), sa = Mathf.Sin(axis);
            for (int i = 0; i < n; i++)
            {
                float a = i / (float)n * Mathf.PI * 2f;
                float k = (Mathf.Sin(a * 2f + p1) * w2 + Mathf.Sin(a * 3f + p2) * w3 + Mathf.Sin(a * 4f + p3) * w4 + Mathf.Sin(a * 5f + p4) * w5) * norm;
                float r = radius * (1f + k * noise);
                Vector2 q = new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r);
                // stretch along the random axis (area kept by squeezing the other axis)
                float u = q.x * ca + q.y * sa, w = -q.x * sa + q.y * ca;
                u *= stretch; w /= stretch;
                rest[i] = new Vector2(u * ca - w * sa, u * sa + w * ca);
            }
        }

        /// <summary>Reset all points to the rest shape at 'center', scaled linearly by 'scale', at rest.</summary>
        public void Place(Vector2 center, float scale)
        {
            for (int i = 0; i < N; i++) P[i] = Prev[i] = center + rest[i] * scale;
            Center = center;
            Velocity = Vector2.zero;
            RecalcBounds();
        }

        /// <summary>Adds the same velocity change to every point (keeps spin and deformation).</summary>
        public void AddVelocity(Vector2 dv, float h)
        {
            for (int i = 0; i < N; i++) Prev[i] -= dv * h;
            Velocity += dv;
        }

        public void SetVelocity(Vector2 v, float h)
        {
            for (int i = 0; i < N; i++) Prev[i] = P[i] - v * h;
            Velocity = v;
        }

        /// <summary>Elongate along a direction (launch stretch); constraints spring it back = jiggle.</summary>
        public void Stretch(Vector2 dir, float amount)
        {
            Vector2 c = Centroid();
            for (int i = 0; i < N; i++)
            {
                Vector2 d = P[i] - c;
                float along = Vector2.Dot(d, dir);
                Vector2 off = dir * along * amount - (d - dir * along) * amount * 0.5f;
                P[i] += off;
                Prev[i] += off;
            }
        }

        public Vector2 Centroid()
        {
            Vector2 c = Vector2.zero;
            for (int i = 0; i < N; i++) c += P[i];
            return c / N;
        }

        public float Area()
        {
            float a = 0f;
            for (int i = 0, j = N - 1; i < N; j = i++) a += P[j].x * P[i].y - P[i].x * P[j].y;
            return a * 0.5f;
        }

        public void RecalcBounds()
        {
            Vector2 mn = P[0], mx = P[0];
            for (int i = 1; i < N; i++) { mn = Vector2.Min(mn, P[i]); mx = Vector2.Max(mx, P[i]); }
            Min = mn; Max = mx;
        }

        /// <param name="pressureUnit">Acceleration that Pressure = 1 corresponds to (gravity magnitude).</param>
        public void Integrate(float h, Vector2 gravity, float damp, float maxStep, float pressureUnit)
        {
            Vector2 acc = (UseGravity ? gravity : Vector2.zero) + ExtraAccel;
            float max2 = maxStep * maxStep;

            // ideal-gas pressure: outward push on each skin point, proportional to its share of the skin
            float area = Mathf.Max(Area(), 1e-4f);
            float ratio = Mathf.Min(RestArea * AreaScale / area, 4f);
            float k = Pressure * pressureUnit * ratio / (restMeanEdge * Mathf.Sqrt(AreaScale));
            for (int i = 0; i < N; i++)
            {
                Vector2 t = P[i + 1 == N ? 0 : i + 1] - P[i == 0 ? N - 1 : i - 1];
                accel[i] = new Vector2(t.y, -t.x) * (0.5f * k); // = outward normal * local edge length * k
            }

            for (int i = 0; i < N; i++)
            {
                Vector2 v = (P[i] - Prev[i]) * damp;
                if (v.sqrMagnitude > max2) v = v.normalized * maxStep;
                Prev[i] = P[i];
                P[i] += v + (acc + accel[i]) * (h * h);
            }
        }

        /// <summary>
        /// Damps only the deformation motion (points relative to the body's mean velocity), so squashed blobs
        /// ooze back into shape instead of wobbling, without slowing the blob's flight.
        /// </summary>
        public void DampDeformation(float k)
        {
            if (k <= 0f) return;
            Vector2 mean = Vector2.zero;
            for (int i = 0; i < N; i++) mean += P[i] - Prev[i];
            mean /= N;
            for (int i = 0; i < N; i++)
            {
                Vector2 v = P[i] - Prev[i];
                Prev[i] = P[i] - (mean + (v - mean) * (1f - k));
            }
        }

        /// <param name="slack">Skin only resists stretching, never compression, so it can go slack and slump.</param>
        public void SolveEdges(float k, bool slack)
        {
            float s = Mathf.Sqrt(AreaScale);
            for (int i = 0; i < N; i++)
            {
                int j = i + 1 == N ? 0 : i + 1;
                Vector2 d = P[j] - P[i];
                float len = d.magnitude;
                if (len < 1e-6f) continue;
                float restLen = edgeRest[i] * s;
                // slack skin: only a faint push when compressed, just enough to keep points evenly spaced
                float kk = slack && len < restLen ? k * 0.12f : k;
                float diff = (len - restLen) / len * 0.5f * kk;
                P[i] += d * diff;
                P[j] -= d * diff;
            }
        }

        /// <summary>Safety net: never let a soft balloon be crushed below a fraction of its volume.</summary>
        public void SolveMinArea(float minFraction)
        {
            float a = Area(), target = RestArea * AreaScale * minFraction;
            if (a >= target) return;
            float perim = 0f;
            for (int i = 0; i < N; i++) perim += (P[(i + 1) % N] - P[i]).magnitude;
            if (perim < 1e-5f) return;
            float dil = (target - a) / perim;
            for (int i = 0; i < N; i++)
            {
                Vector2 t = P[(i + 1) % N] - P[(i + N - 1) % N];
                float l = t.magnitude;
                if (l > 1e-6f) tmpNormals[i] = new Vector2(t.y, -t.x) / l;
            }
            for (int i = 0; i < N; i++) P[i] += tmpNormals[i] * dil;
        }

        public void SolveArea(float k)
        {
            float perim = 0f;
            for (int i = 0; i < N; i++) perim += (P[(i + 1) % N] - P[i]).magnitude;
            if (perim < 1e-5f) return;
            float dil = (RestArea * AreaScale - Area()) / perim * k;
            // compute all normals first so the correction is symmetric
            for (int i = 0; i < N; i++)
            {
                Vector2 t = P[(i + 1) % N] - P[(i + N - 1) % N];
                float l = t.magnitude;
                if (l < 1e-6f) continue;
                tmpNormals[i] = new Vector2(t.y, -t.x) / l;
            }
            for (int i = 0; i < N; i++) P[i] += tmpNormals[i] * dil;
        }

        Vector2[] tmpNormals => normals ??= new Vector2[N];
        Vector2[] normals;

        /// <summary>
        /// Affine shape matching (Müller et al. 2005): pulls points toward the best-fit *affine* copy of the
        /// lumpy rest outline. Squash, stretch, rotation and size are free (pressure/gravity decide those);
        /// only the lumps are remembered. So blobs keep their character AND still slump flat.
        /// </summary>
        public void SolveShape(float k)
        {
            if (k <= 0f) return;
            Vector2 c = Centroid();
            float a00 = 0f, a01 = 0f, a10 = 0f, a11 = 0f;
            for (int i = 0; i < N; i++)
            {
                Vector2 p = P[i] - c, q = rest[i];
                a00 += p.x * q.x; a01 += p.x * q.y;
                a10 += p.y * q.x; a11 += p.y * q.y;
            }
            var m = aqqInv;
            float A00 = a00 * m.x + a01 * m.z, A01 = a00 * m.y + a01 * m.w;
            float A10 = a10 * m.x + a11 * m.z, A11 = a10 * m.y + a11 * m.w;
            if (Rigidity > 0f)
            {
                // rotation-only fit at the current size: blend toward it so the body always springs back to its rest shape
                float th = Mathf.Atan2(a10 - a01, a00 + a11);
                float s = Mathf.Sqrt(AreaScale), rc = Mathf.Cos(th) * s, rs = Mathf.Sin(th) * s;
                if (A00 * A11 - A01 * A10 < 0.05f) { A00 = rc; A01 = -rs; A10 = rs; A11 = rc; }
                else
                {
                    ClampAspect(ref A00, ref A01, ref A10, ref A11, MaxSquash);
                    float r = Rigidity;
                    A00 += (rc - A00) * r; A01 += (-rs - A01) * r;
                    A10 += (rs - A10) * r; A11 += (rc - A11) * r;
                }
            }
            else
            {
                if (A00 * A11 - A01 * A10 < 0.05f) return; // degenerate / inverted fit: skip this pass
                ClampAspect(ref A00, ref A01, ref A10, ref A11, MaxSquash);
            }
            for (int i = 0; i < N; i++)
            {
                Vector2 q = rest[i];
                Vector2 target = c + new Vector2(A00 * q.x + A01 * q.y, A10 * q.x + A11 * q.y);
                P[i] += (target - P[i]) * k;
            }
        }

        /// <summary>Max stretch ratio (long axis / short axis) the shape target allows; beyond it blobs get pulled back.</summary>
        public float MaxSquash = 2f;

        /// <summary>
        /// 0 = shape target is the affine fit (blobs: squash/sag stay). 1 = rotation-only fit, so any squash springs back
        /// to the exact rest outline (hexagons).
        /// </summary>
        public float Rigidity;

        /// <summary>Distance of the points from the best rotated rest outline at the current size: (mean, max), as fractions of the radius.</summary>
        public Vector2 ShapeError()
        {
            Vector2 c = Centroid();
            float a00 = 0f, a01 = 0f, a10 = 0f, a11 = 0f;
            for (int i = 0; i < N; i++)
            {
                Vector2 p = P[i] - c, q = rest[i];
                a00 += p.x * q.x; a01 += p.x * q.y;
                a10 += p.y * q.x; a11 += p.y * q.y;
            }
            float th = Mathf.Atan2(a10 - a01, a00 + a11);
            float s = Mathf.Sqrt(Mathf.Max(1e-4f, Area() / RestArea)), rc = Mathf.Cos(th) * s, rs = Mathf.Sin(th) * s;
            float sum = 0f, max = 0f;
            for (int i = 0; i < N; i++)
            {
                Vector2 q = rest[i];
                float e = (P[i] - c - new Vector2(rc * q.x - rs * q.y, rs * q.x + rc * q.y)).magnitude;
                sum += e; max = Mathf.Max(max, e);
            }
            float r = RestRadius * s;
            return new Vector2(sum / N / r, max / r);
        }

        /// <summary>Everything the solver changes on a body, for what-if simulations (save → step → restore).</summary>
        public sealed class Snapshot
        {
            public Vector2[] P, Prev;
            public float AreaScale, MassScale, Impact;
            public bool UseGravity;
            public Vector2 ExtraAccel, Center, Velocity, Min, Max, ImpactPoint;
            public int Group, IgnoreGroup;
        }

        public void Save(Snapshot s)
        {
            if (s.P == null || s.P.Length != N) { s.P = new Vector2[N]; s.Prev = new Vector2[N]; }
            System.Array.Copy(P, s.P, N);
            System.Array.Copy(Prev, s.Prev, N);
            s.AreaScale = AreaScale; s.MassScale = MassScale; s.Impact = Impact; s.UseGravity = UseGravity;
            s.ExtraAccel = ExtraAccel; s.Center = Center; s.Velocity = Velocity; s.Min = Min; s.Max = Max;
            s.ImpactPoint = ImpactPoint; s.Group = Group; s.IgnoreGroup = IgnoreGroup;
        }

        public void Load(Snapshot s)
        {
            System.Array.Copy(s.P, P, N);
            System.Array.Copy(s.Prev, Prev, N);
            AreaScale = s.AreaScale; MassScale = s.MassScale; Impact = s.Impact; UseGravity = s.UseGravity;
            ExtraAccel = s.ExtraAccel; Center = s.Center; Velocity = s.Velocity; Min = s.Min; Max = s.Max;
            ImpactPoint = s.ImpactPoint; Group = s.Group; IgnoreGroup = s.IgnoreGroup;
        }

        /// <summary>Rotation (radians) of the best rigid fit of the rest outline onto the current points.</summary>
        public float FitRotation()
        {
            Vector2 c = Centroid();
            float a00 = 0f, a01 = 0f, a10 = 0f, a11 = 0f;
            for (int i = 0; i < N; i++)
            {
                Vector2 p = P[i] - c, q = rest[i];
                a00 += p.x * q.x; a01 += p.x * q.y;
                a10 += p.y * q.x; a11 += p.y * q.y;
            }
            return Mathf.Atan2(a10 - a01, a00 + a11);
        }

        /// <summary>Rest outline point i (offset from the centre at AreaScale 1).</summary>
        public Vector2 RestPoint(int i) => rest[i];

        /// <summary>
        /// Best-fit affine map rest → current (centroid c, matrix (m00, m01, m10, m11)), i.e. how the body is moved,
        /// rotated, scaled and squashed right now. Used to draw textures that deform with the body.
        /// </summary>
        public void FitAffine(out Vector2 c, out Vector4 m)
        {
            c = Centroid();
            float a00 = 0f, a01 = 0f, a10 = 0f, a11 = 0f;
            for (int i = 0; i < N; i++)
            {
                Vector2 p = P[i] - c, q = rest[i];
                a00 += p.x * q.x; a01 += p.x * q.y;
                a10 += p.y * q.x; a11 += p.y * q.y;
            }
            var k = aqqInv;
            m = new Vector4(a00 * k.x + a01 * k.z, a00 * k.y + a01 * k.w, a10 * k.x + a11 * k.z, a10 * k.y + a11 * k.w);
        }

        /// <summary>Long axis / short axis of the current point cloud (1 = undistorted for a symmetric outline).</summary>
        public float Aspect()
        {
            Vector2 c = Centroid();
            float xx = 0f, xy = 0f, yy = 0f;
            for (int i = 0; i < N; i++) { Vector2 d = P[i] - c; xx += d.x * d.x; xy += d.x * d.y; yy += d.y * d.y; }
            float tr = xx + yy, root = Mathf.Sqrt((xx - yy) * (xx - yy) * 0.25f + xy * xy);
            float l1 = tr * 0.5f + root, l2 = Mathf.Max(1e-6f, tr * 0.5f - root);
            return Mathf.Sqrt(l1 / l2);
        }

        /// <summary>
        /// Polar-decompose A = R·S, then limit the ratio of S's principal stretches to maxRatio
        /// (keeping their product, i.e. area). Squash up to the limit is untouched.
        /// </summary>
        static void ClampAspect(ref float a00, ref float a01, ref float a10, ref float a11, float maxRatio)
        {
            if (maxRatio <= 1f) maxRatio = 1.0001f;
            // rotation part
            float th = Mathf.Atan2(a10 - a01, a00 + a11);
            float c = Mathf.Cos(th), s = Mathf.Sin(th);
            // S = R^T A (symmetric up to numerical noise)
            float s00 = c * a00 + s * a10, s11 = -s * a01 + c * a11;
            float s01 = 0.5f * ((c * a01 + s * a11) + (-s * a00 + c * a10)); // symmetrise
            // eigen-decomposition of symmetric 2x2
            float tr = s00 + s11, d = s00 - s11;
            float root = Mathf.Sqrt(d * d * 0.25f + s01 * s01);
            float l1 = tr * 0.5f + root, l2 = tr * 0.5f - root;
            if (l2 <= 1e-4f || l1 / l2 <= maxRatio) return;
            float prod = l1 * l2;
            float n1 = Mathf.Sqrt(prod * maxRatio), n2 = Mathf.Sqrt(prod / maxRatio);
            float phi = 0.5f * Mathf.Atan2(2f * s01, d);
            float cp = Mathf.Cos(phi), sp = Mathf.Sin(phi);
            // S' = V diag(n1, n2) V^T
            float t00 = n1 * cp * cp + n2 * sp * sp;
            float t11 = n1 * sp * sp + n2 * cp * cp;
            float t01 = (n1 - n2) * cp * sp;
            // A' = R S'
            a00 = c * t00 - s * t01; a01 = c * t01 - s * t11;
            a10 = s * t00 + c * t01; a11 = s * t01 + c * t11;
        }

        public bool Contains(Vector2 p)
        {
            if (p.x < Min.x || p.x > Max.x || p.y < Min.y || p.y > Max.y) return false;
            bool inside = false;
            for (int i = 0, j = N - 1; i < N; j = i++)
            {
                if ((P[i].y > p.y) != (P[j].y > p.y) &&
                    p.x < (P[j].x - P[i].x) * (p.y - P[i].y) / (P[j].y - P[i].y) + P[i].x)
                    inside = !inside;
            }
            return inside;
        }
    }
}
