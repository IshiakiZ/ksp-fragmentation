using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Fragmentation
{
    /// <summary>
    /// The bones of a torn tank: a stage's tanks are thin skins held round by rings (frames) and along by stringers, and when one is torn
    /// open these come out of it too, bent: arcs of ring and bent bars, bare metal. Made for a part whose skin is round (a tank, a
    /// fuselage), from its size.
    /// </summary>
    public static class Frames
    {
        static Material metal;

        /// <summary>Whether a part's skin is round about its own long axis (the game's parts stand along their second axis), and how big round.</summary>
        public static bool Round(HullPlan plan, out float radius, out float length)
        {
            Vector3 s = plan.hi - plan.lo;
            radius = 0.25f * (s.x + s.z);
            length = s.y;
            return s.x > 0.4f && Mathf.Abs(s.x - s.z) < 0.12f * s.x && length > 0.35f * s.x;
        }

        /// <summary>A few rings and stringers thrown out of a torn tank, at a place, going as fast as v and thrown out by so much.</summary>
        public static void Throw(HullPlan plan, Vector3 at, Quaternion partTurn, Vector3 v, float kick, int rings, int stringers, float soot, Part from)
        {
            if (!Round(plan, out float radius, out float length)) return;
            if (metal == null) metal = Hull.Inside();
            // (a ring is a channel some centimetres deep, a stringer a narrower one: about a twentieth of the radius for the ring's depth)
            float section = Mathf.Clamp(radius * 0.06f, 0.03f, 0.12f);
            for (int k = 0; k < rings + stringers; k++)
            {
                if (Piece.Count >= Piece.Most) return;
                bool ring = k < rings;
                // (torn out of a skin that tore: a ring breaks where the skin round it did, into arcs, bent and twisted out of its plane)
                Mesh mesh = ring ? Arc(radius * Random.Range(0.97f, 1.0f), Random.Range(30f, 120f), section, section * 0.35f, Random.Range(0.2f, 0.7f))
                                 : Bar(Mathf.Min(length, Random.Range(0.4f, 1.3f) * radius * 1.4f), section * 0.5f, Random.Range(-35f, 35f), Random.Range(-15f, 15f));
                var mats = new[] { metal };
                // (bare aluminium, blackened in places by the fire, not all over)
                float k2 = Mathf.Lerp(1f, 0.45f, soot * Random.Range(0.4f, 1f));
                var tints = new[] { new Color(0.86f * k2, 0.86f * k2, 0.84f * k2, 1f) };
                Vector3 out_ = Random.onUnitSphere;
                Vector3 place = at + out_ * radius * 0.5f;
                Quaternion turn = partTurn * Random.rotation;
                float mass = Mathf.Max(0.003f, (ring ? 0.012f : 0.006f) * radius);
                Piece p = Piece.Make(mesh, mats, tints, place, turn, v + out_ * kick * Random.Range(0.4f, 1f), Random.onUnitSphere * Random.Range(2f, 8f), mass, mesh.bounds.size.x * section * 2f, soot > 0.3f ? 0.3f : 0f, 0f, 0, false, from);
                p.smoke = soot > 0.3f ? Random.Range(0.2f, 0.4f) : 0.06f;
                p.smokeUntil = Time.time + (soot > 0.3f ? Random.Range(6f, 12f) : 3f);
                p.shade = soot > 0.3f ? 0.08f : 0.4f;
                p.warm = 0.25f;
            }
        }

        /// <summary>An arc of a ring: so far round (degrees), its section so wide (out from the middle) and so thick, bent out of round by so much.</summary>
        static Mesh Arc(float radius, float degrees, float wide, float thick, float bent)
        {
            int steps = Mathf.Max(4, Mathf.RoundToInt(degrees / 10f));
            var v = new List<Vector3>();
            var mids = new List<Vector3>();
            float kink = Random.Range(0.2f, 0.8f), twist = Random.Range(-1.2f, 1.2f);
            for (int i = 0; i <= steps; i++)
            {
                float s = i / (float)steps, a = (s - 0.5f) * degrees * Mathf.Deg2Rad;
                // (bent: out of round, and a kink where it was struck)
                float r = radius * (1f + bent * 0.25f * Mathf.Sin(s * Mathf.PI * 2f) - bent * 0.4f * Mathf.Exp(-Mathf.Pow((s - kink) * 6f, 2f)));
                Vector3 c = new Vector3(Mathf.Sin(a) * r, bent * 0.25f * radius * (Mathf.Sin(s * 5f) + (s - 0.5f) * twist), Mathf.Cos(a) * r);
                Vector3 outward = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                v.Add(c); v.Add(c - outward * wide); v.Add(c - outward * wide + Vector3.up * thick); v.Add(c + Vector3.up * thick);
                mids.Add(c - outward * wide * 0.5f + Vector3.up * thick * 0.5f);
            }
            return Finish(v, Tube(v, mids, steps));
        }

        /// <summary>The faces of a bar of four-cornered section (four corners at each step along it), each facing outward from the bar's middle line.</summary>
        static List<int> Tube(List<Vector3> v, List<Vector3> mids, int steps)
        {
            var t = new List<int>();
            for (int i = 0; i < steps; i++)
                for (int f = 0; f < 4; f++)
                {
                    int a = i * 4 + f, b = i * 4 + (f + 1) % 4, c = a + 4, d = b + 4;
                    Vector3 mid = (mids[i] + mids[i + 1]) * 0.5f;
                    Vector3 face = Vector3.Cross(v[c] - v[a], v[b] - v[a]);
                    Vector3 centre = (v[a] + v[b] + v[c] + v[d]) * 0.25f;
                    // (Unity draws the side whose corners go round clockwise as seen: so the normal of (a, c, b) must point away from the middle)
                    if (Vector3.Dot(face, centre - mid) >= 0f) { t.Add(a); t.Add(c); t.Add(b); t.Add(b); t.Add(c); t.Add(d); }
                    else { t.Add(a); t.Add(b); t.Add(c); t.Add(b); t.Add(d); t.Add(c); }
                }
            return t;
        }

        /// <summary>A stringer: a bar so long, of so thick a section, bent part way along by so many degrees each way.</summary>
        static Mesh Bar(float length, float thick, float bendA, float bendB)
        {
            int steps = 8;
            var v = new List<Vector3>();
            var mids = new List<Vector3>();
            float at = Random.Range(0.3f, 0.7f);
            Vector3 p = new Vector3(0f, 0f, -length * 0.5f), dir = Vector3.forward;
            for (int i = 0; i <= steps; i++)
            {
                float s = i / (float)steps;
                if (i > 0)
                {
                    if (Mathf.Abs(s - at) < 0.13f) dir = Quaternion.Euler(bendA * 0.5f, bendB * 0.5f, 0f) * dir;
                    p += dir * (length / steps);
                }
                Vector3 side = Vector3.Cross(dir, Vector3.up).normalized, up = Vector3.Cross(side, dir).normalized;
                v.Add(p - side * thick - up * thick * 0.5f); v.Add(p + side * thick - up * thick * 0.5f); v.Add(p + side * thick + up * thick * 0.5f); v.Add(p - side * thick + up * thick * 0.5f);
                mids.Add(p);
            }
            return Finish(v, Tube(v, mids, steps));
        }

        static Mesh Finish(List<Vector3> v, List<int> t)
        {
            // (centred on its own middle, as a piece's mesh is)
            Vector3 lo = Vector3.one * float.MaxValue, hi = Vector3.one * float.MinValue;
            foreach (Vector3 p in v) { lo = Vector3.Min(lo, p); hi = Vector3.Max(hi, p); }
            Vector3 c = (lo + hi) * 0.5f;
            for (int i = 0; i < v.Count; i++) v[i] -= c;
            var uv = new List<Vector2>(v.Count);
            for (int i = 0; i < v.Count; i++) uv.Add(new Vector2(0.5f, 0.9f));
            var m = new Mesh { name = "Fragmentation frame" };
            m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(t, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }
    }
}
