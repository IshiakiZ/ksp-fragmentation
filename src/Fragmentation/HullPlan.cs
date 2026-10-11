using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace Fragmentation
{
    /// <summary>
    /// The skin of one kind of part, cut into the panels it would tear into: worked out once for each kind of part (on
    /// another thread, while the craft is still falling), and shared by every part of that kind.
    ///
    /// The part's meshes, as the game draws them, are taken into the part's own axes as loose triangles. Triangles longer
    /// than a few centimetres are cut in two, and again, the same way on both sides of every edge (so that the surface
    /// stays closed when it is bent); then points are scattered over the skin with sizes drawn as broken things are (few
    /// large pieces, many small: the number bigger than a size goes as that size to the power -1.6), and each triangle
    /// goes to the nearest point, nearer counting for more the bigger the point's panel, the borders wandering a little
    /// so that they are not straight (a "power diagram"). Each panel knows its triangles, its neighbours and the edges it
    /// shares with each, which is what a tear runs along.
    ///
    /// An engine is not cut: its bell, its pump and its mounting each come away whole. Nor is a small mesh on a big part
    /// (a ladder, a hatch, a light): it comes away whole too.
    /// </summary>
    public sealed class HullPlan
    {
        public sealed class Cell
        {
            public int[] tris;                 // the plan's triangles
            public Vector3 mid, normal;        // area-weighted middle, and the way it faces (the part's axes)
            public float area;
            public Vector3 lo, hi;             // the box round its corners
            public int comp;                   // which of the part's meshes it is of
            public bool whole;                 // a mesh that comes away whole
            public int[] edgeA, edgeB, edgeOther;   // its border: the corners (into p) at the ends of each edge, and the panel beyond (-1: the mesh's own edge)
            public int[] near;                 // the panels it borders
            public int island;                 // which piece of skin, whole, it is part of (a part's meshes need not touch one another: each is its own)
        }

        // ---- the skin, in the part's own axes: loose triangles, three corners each
        public Vector3[] p, n;
        public Vector2[] uv;
        public Vector4[] t;
        public Vector2[] uv2;                  // (made by Hull when first wanted) the texture places and tangents of the corners outside and then inside
        public Vector4[] t2;
        public int[] slot;                     // each triangle's material, as (mesh, submesh): see slotFilter, slotSub
        public int[] cellOf;                   // each triangle's panel
        public Cell[] cells;
        public int[] slotFilter, slotSub;
        public int filters;                    // how many meshes were taken (an instance must find as many, in the same order)
        public Vector3 lo, hi, mid;
        public float area, size, thick, edge;
        public bool engine;
        public volatile bool ready, failed;
        public string key = "", problem = "";
        public float tookMs;

        // ---- what was gathered, before the work on the other thread
        List<Vector3> gp = new List<Vector3>(), gn = new List<Vector3>();
        List<Vector2> guv = new List<Vector2>();
        List<Vector4> gt = new List<Vector4>();
        List<int> gslot = new List<int>(), gcomp = new List<int>();
        List<int> sf = new List<int>(), ss = new List<int>();

        static readonly Dictionary<string, HullPlan> plans = new Dictionary<string, HullPlan>();
        public static int Built, Failed;

        /// <summary>The meshes of a part that are drawn and solid, in the order the game lists them.</summary>
        public static List<MeshFilter> Drawn(Part part)
        {
            var found = new List<MeshFilter>();
            List<MeshFilter> all = part.FindModelComponents<MeshFilter>();
            if (all == null) return found;
            foreach (MeshFilter f in all)
            {
                if (f == null || !f.gameObject.activeInHierarchy || f.sharedMesh == null) continue;
                MeshRenderer r = f.GetComponent<MeshRenderer>();
                if (r == null || !r.enabled || r.forceRenderingOff) continue;
                Material[] mats = r.sharedMaterials;
                if (mats == null || mats.Length == 0) continue;
                bool any = false;
                foreach (Material m in mats) if (Solid(m)) { any = true; break; }
                if (!any) continue;
                found.Add(f);
            }
            return found;
        }

        /// <summary>Whether a material draws something solid (not a glow, a plume or a see-through panel).</summary>
        public static bool Solid(Material m)
        {
            if (m == null || m.shader == null) return false;
            if (m.renderQueue >= 2500) return false;
            string name = m.shader.name;
            return !(name.StartsWith("Waterfall", StringComparison.Ordinal) || name.IndexOf("Particle", StringComparison.OrdinalIgnoreCase) >= 0
                     || name.IndexOf("Additive", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Translucent", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        static string KeyOf(Part part, List<MeshFilter> filters)
        {
            var key = new System.Text.StringBuilder(part.partInfo != null ? part.partInfo.name : part.name);
            Matrix4x4 toPart = part.transform.worldToLocalMatrix;
            foreach (MeshFilter f in filters)
            {
                Matrix4x4 m = toPart * f.transform.localToWorldMatrix;
                key.Append('|').Append(f.sharedMesh.GetInstanceID()).Append(':').Append(Mathf.RoundToInt(m.m03 * 100f)).Append(',').Append(Mathf.RoundToInt(m.m13 * 100f)).Append(',').Append(Mathf.RoundToInt(m.m23 * 100f))
                   .Append(',').Append(Mathf.RoundToInt(m.m00 * 100f)).Append(',').Append(Mathf.RoundToInt(m.m11 * 100f));
            }
            return key.ToString();
        }

        /// <summary>The plan for a part: kept from before for its kind, or begun now (not ready at once: see ready). Null where the part has nothing that can be read.</summary>
        public static HullPlan For(Part part)
        {
            if (part == null) return null;
            List<MeshFilter> filters = Drawn(part);
            if (filters.Count == 0) return null;
            string key;
            try { key = KeyOf(part, filters); }
            catch (Exception) { return null; }
            if (plans.TryGetValue(key, out HullPlan plan)) return plan;
            if (plans.Count > 200) plans.Clear();
            plan = new HullPlan { key = key };
            plans[key] = plan;
            try
            {
                if (!plan.Gather(part, filters)) { plan.failed = true; Failed++; return plan; }
            }
            catch (Exception ex) { plan.failed = true; plan.problem = ex.Message; Failed++; return plan; }
            Task.Run(() =>
            {
                try { plan.Build(); }
                catch (Exception ex) { plan.problem = ex.GetType().Name + ": " + ex.Message; plan.failed = true; Failed++; }
            });
            return plan;
        }

        public static void Forget() => plans.Clear();

        /// <summary>(on the game's thread) the meshes' numbers, into the part's axes.</summary>
        /// <summary>A mesh's numbers as read, and how it sits in the part, for the other thread to put into the part's axes.</summary>
        sealed class Raw
        {
            public Vector3[] p, n;
            public Vector2[] uv;
            public Vector4[] t;
            public Matrix4x4 m;
            public int filter;
            public readonly List<int[]> tris = new List<int[]>();
            public readonly List<int> slots = new List<int>();
        }

        List<Raw> raws = new List<Raw>();

        /// <summary>
        /// (on the game's thread) the meshes' numbers, copied as they are: what is done with them, triangle by triangle, is done on the other
        /// thread (done here, it took tens of milliseconds of the frame in which the craft was armed, the second before a crash).
        /// </summary>
        bool Gather(Part part, List<MeshFilter> filters)
        {
            Matrix4x4 toPart = part.transform.worldToLocalMatrix;
            filters = filters ?? Drawn(part);
            this.filters = filters.Count;
            int triangles = 0;
            for (int fi = 0; fi < filters.Count; fi++)
            {
                MeshFilter f = filters[fi];
                Mesh mesh = f.sharedMesh;
                if (!mesh.isReadable) continue;
                Material[] mats = f.GetComponent<MeshRenderer>().sharedMaterials;
                var raw = new Raw { p = mesh.vertices, n = mesh.normals, uv = mesh.uv, t = mesh.tangents, m = toPart * f.transform.localToWorldMatrix, filter = fi };
                if (raw.n == null || raw.n.Length != raw.p.Length) raw.n = null;
                if (raw.uv == null || raw.uv.Length != raw.p.Length) raw.uv = null;
                if (raw.t == null || raw.t.Length != raw.p.Length) raw.t = null;
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    Material mat = mats[Mathf.Min(sub, mats.Length - 1)];
                    if (!Solid(mat)) continue;
                    if (mesh.GetTopology(sub) != MeshTopology.Triangles) continue;
                    int[] tris = mesh.GetTriangles(sub);
                    raw.slots.Add(sf.Count);
                    sf.Add(fi); ss.Add(sub);
                    raw.tris.Add(tris);
                    triangles += tris.Length / 3;
                }
                if (raw.tris.Count > 0) raws.Add(raw);
            }
            // (an engine: one that carries next to nothing itself. A solid booster is an engine too, but it is a tank of propellant first, and is torn as one)
            for (int k = 0; k < part.Modules.Count; k++) if (part.Modules[k] is ModuleEngines) { engine = true; break; }
            if (engine && part.Resources != null)
            {
                double kg = 0;
                foreach (PartResource r in part.Resources) if (r != null && r.info != null && r.resourceName != "ElectricCharge") kg += r.maxAmount * r.info.density * 1000.0;
                if (kg > 100.0) engine = false;
            }
            return triangles >= 2;
        }

        /// <summary>(on the other thread) the meshes as loose triangles in the part's axes.</summary>
        void Unpack()
        {
            foreach (Raw raw in raws)
            {
                Matrix4x4 m = raw.m;
                Vector3[] vp = raw.p, vn = raw.n;
                Vector2[] vuv = raw.uv;
                Vector4[] vt = raw.t;
                for (int s2 = 0; s2 < raw.tris.Count; s2++)
                {
                    int[] tris = raw.tris[s2];
                    int slotIndex = raw.slots[s2];
                    for (int k = 0; k + 2 < tris.Length; k += 3)
                    {
                        if (gslot.Count >= 30000) break;
                        for (int c = 0; c < 3; c++)
                        {
                            int v = tris[k + c];
                            gp.Add(m.MultiplyPoint3x4(vp[v]));
                            Vector3 nn = vn != null ? m.MultiplyVector(vn[v]) : Vector3.up;
                            gn.Add(nn.sqrMagnitude > 1e-12f ? nn.normalized : Vector3.up);
                            guv.Add(vuv != null ? vuv[v] : Vector2.zero);
                            if (vt != null)
                            {
                                Vector3 t3 = m.MultiplyVector(new Vector3(vt[v].x, vt[v].y, vt[v].z));
                                t3 = t3.sqrMagnitude > 1e-12f ? t3.normalized : Vector3.right;
                                gt.Add(new Vector4(t3.x, t3.y, t3.z, vt[v].w < 0f ? -1f : 1f));
                            }
                            else gt.Add(new Vector4(1f, 0f, 0f, 1f));
                        }
                        gslot.Add(slotIndex);
                        gcomp.Add(raw.filter);
                    }
                }
            }
            raws = null;
        }

        // ================================================================== on the other thread

        void Build()
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            Unpack();
            if (gslot.Count < 2) { failed = true; problem = "nothing solid"; return; }
            var random = new System.Random(key.GetHashCode());
            // ---- the size of the part, and so of its panels and of the triangles they are made of
            Vector3 a0 = gp[0], a1 = gp[0];
            for (int i = 1; i < gp.Count; i++) { a0 = Vector3.Min(a0, gp[i]); a1 = Vector3.Max(a1, gp[i]); }
            float sz = (a1 - a0).magnitude;
            edge = Mathf.Clamp(sz * 0.16f, 0.22f, 1.1f);            // a typical panel's width
            int comps = 0;
            foreach (int c in gcomp) comps = Math.Max(comps, c + 1);
            float[] compArea = new float[comps];
            int[] compTris = new int[comps];
            float all = 0f;
            for (int k = 0; k < gslot.Count; k++)
            {
                float ar = Area(gp[k * 3], gp[k * 3 + 1], gp[k * 3 + 2]);
                compArea[gcomp[k]] += ar; compTris[gcomp[k]]++; all += ar;
            }
            if (all <= 1e-6f) { failed = true; problem = "no area"; return; }
            var whole = new bool[comps];
            for (int c = 0; c < comps; c++) whole[c] = engine || compArea[c] < 0.06f * all || compTris[c] <= 8;
            // (a part that is all small meshes is cut after all, its biggest mesh at least)
            bool anyCut = false;
            for (int c = 0; c < comps; c++) if (!whole[c]) anyCut = true;
            if (!anyCut && !engine)
            {
                int big = 0;
                for (int c = 1; c < comps; c++) if (compArea[c] > compArea[big]) big = c;
                if (compTris[big] > 8) whole[big] = false;
            }

            // ---- cut long triangles: every edge longer than the limit in two, on both sides alike, until none is (or there would be too many)
            float limit = Mathf.Clamp(edge / 3f, 0.08f, 0.32f);
            Refine(limit * limit, whole, 5000);

            // ---- into arrays
            int T = gslot.Count;
            p = gp.ToArray(); n = gn.ToArray(); uv = guv.ToArray(); t = gt.ToArray(); slot = gslot.ToArray();
            int[] comp = gcomp.ToArray();
            slotFilter = sf.ToArray(); slotSub = ss.ToArray();
            gp = gn = null; guv = null; gt = null; gslot = gcomp = null;
            lo = a0; hi = a1; mid = (a0 + a1) * 0.5f; size = sz; area = all;
            thick = Mathf.Clamp(0.004f * sz + 0.004f, 0.006f, 0.03f);

            // ---- the points the panels grow from, mesh by mesh
            var tri = new float[T];
            var centre = new Vector3[T];
            for (int k = 0; k < T; k++) { tri[k] = Area(p[k * 3], p[k * 3 + 1], p[k * 3 + 2]); centre[k] = (p[k * 3] + p[k * 3 + 1] + p[k * 3 + 2]) / 3f; }
            var seeds = new List<Vector3>();
            var weight = new List<float>();
            var seedComp = new List<int>();
            var compSeeds = new List<int>[comps];
            for (int c = 0; c < comps; c++)
            {
                compSeeds[c] = new List<int>();
                if (compTris[c] == 0) continue;
                if (whole[c]) { compSeeds[c].Add(seeds.Count); seeds.Add(Vector3.zero); weight.Add(0f); seedComp.Add(c); continue; }
                int count = Mathf.Clamp(Mathf.RoundToInt(compArea[c] / (edge * edge)), 2, 90);
                float smallest = edge * 0.45f, biggest = edge * 1.9f, alpha = 1.6f, low = Mathf.Pow(smallest / biggest, alpha);
                // (a list of this mesh's triangles by how much of it each is, to pick places on it)
                var run = new List<float>();
                var which = new List<int>();
                float sum = 0f;
                for (int k = 0; k < T; k++) if (comp[k] == c) { sum += tri[k]; run.Add(sum); which.Add(k); }
                for (int s = 0; s < count; s++)
                {
                    float pick = (float)random.NextDouble() * sum;
                    int lo2 = 0, hi2 = run.Count - 1;
                    while (lo2 < hi2) { int m2 = (lo2 + hi2) >> 1; if (run[m2] < pick) lo2 = m2 + 1; else hi2 = m2; }
                    int k = which[lo2];
                    float r1 = (float)random.NextDouble(), r2 = (float)random.NextDouble();
                    if (r1 + r2 > 1f) { r1 = 1f - r1; r2 = 1f - r2; }
                    Vector3 at = p[k * 3] + (p[k * 3 + 1] - p[k * 3]) * r1 + (p[k * 3 + 2] - p[k * 3]) * r2;
                    float size2 = smallest * Mathf.Pow(1f - (float)random.NextDouble() * (1f - low), -1f / alpha);
                    compSeeds[c].Add(seeds.Count);
                    seeds.Add(at); weight.Add(0.5f * Mathf.Min(size2, biggest)); seedComp.Add(c);
                }
            }

            // ---- each triangle to its panel: the nearest point, by power, of its own mesh; among the three nearest the border wanders with a noise of each point's own
            cellOf = new int[T];
            float freq = 1f / (0.7f * edge), wobble = 0.3f * edge * 0.3f * edge * 2f;
            for (int k = 0; k < T; k++)
            {
                List<int> mine = compSeeds[comp[k]];
                if (whole[comp[k]] || mine.Count == 1) { cellOf[k] = mine[0]; continue; }
                Vector3 c = centre[k];
                int b0 = -1, b1 = -1, b2 = -1;
                float s0 = float.MaxValue, s1 = float.MaxValue, s2 = float.MaxValue;
                foreach (int q in mine)
                {
                    Vector3 d = c - seeds[q];
                    float score = d.x * d.x + d.y * d.y + d.z * d.z - weight[q] * weight[q];
                    if (score < s0) { b2 = b1; s2 = s1; b1 = b0; s1 = s0; b0 = q; s0 = score; }
                    else if (score < s1) { b2 = b1; s2 = s1; b1 = q; s1 = score; }
                    else if (score < s2) { b2 = q; s2 = score; }
                }
                int best = b0;
                float bestScore = s0 + wobble * Noise(c * freq, b0);
                if (b1 >= 0) { float sc = s1 + wobble * Noise(c * freq, b1); if (sc < bestScore) { bestScore = sc; best = b1; } }
                if (b2 >= 0) { float sc = s2 + wobble * Noise(c * freq, b2); if (sc < bestScore) { best = b2; } }
                cellOf[k] = best;
            }

            // ---- the panels themselves (numbered afresh, without the empty ones)
            var map = new int[seeds.Count];
            for (int q = 0; q < map.Length; q++) map[q] = -1;
            var lists = new List<List<int>>();
            for (int k = 0; k < T; k++)
            {
                int q = cellOf[k];
                if (map[q] < 0) { map[q] = lists.Count; lists.Add(new List<int>()); }
                cellOf[k] = map[q];
                lists[map[q]].Add(k);
            }
            cells = new Cell[lists.Count];
            for (int ci = 0; ci < lists.Count; ci++)
            {
                var cell = new Cell { tris = lists[ci].ToArray() };
                Vector3 lo3 = Vector3.one * float.MaxValue, hi3 = Vector3.one * float.MinValue, m = Vector3.zero, f = Vector3.zero;
                float ar = 0f;
                foreach (int k in cell.tris)
                {
                    ar += tri[k];
                    m += centre[k] * tri[k];
                    f += Vector3.Cross(p[k * 3 + 1] - p[k * 3], p[k * 3 + 2] - p[k * 3]);
                    for (int c = 0; c < 3; c++) { lo3 = Vector3.Min(lo3, p[k * 3 + c]); hi3 = Vector3.Max(hi3, p[k * 3 + c]); }
                }
                cell.area = Mathf.Max(ar, 1e-6f);
                cell.mid = m / cell.area;
                cell.normal = f.sqrMagnitude > 1e-12f ? f.normalized : Vector3.up;
                cell.lo = lo3; cell.hi = hi3;
                cell.comp = comp[cell.tris[0]];
                cell.whole = whole[cell.comp];
                cells[ci] = cell;
            }

            // ---- the borders: each edge of each triangle, matched with the same edge of the triangle beside it
            var open = new Dictionary<long, int>(T * 2);
            var ea = new List<int>[cells.Length];
            var eb = new List<int>[cells.Length];
            var eo = new List<int>[cells.Length];
            for (int ci = 0; ci < cells.Length; ci++) { ea[ci] = new List<int>(); eb[ci] = new List<int>(); eo[ci] = new List<int>(); }
            for (int k = 0; k < T; k++)
            {
                for (int e = 0; e < 3; e++)
                {
                    int i0 = k * 3 + e, i1 = k * 3 + (e + 1) % 3;
                    long key2 = EdgeKey(p[i0], p[i1]);
                    if (open.TryGetValue(key2, out int other))
                    {
                        open.Remove(key2);
                        int ok = other / 3, oe = other % 3;
                        int c1 = cellOf[k], c2 = cellOf[ok];
                        if (c1 == c2) continue;
                        ea[c1].Add(i0); eb[c1].Add(i1); eo[c1].Add(c2);
                        int j0 = ok * 3 + oe, j1 = ok * 3 + (oe + 1) % 3;
                        ea[c2].Add(j0); eb[c2].Add(j1); eo[c2].Add(c1);
                    }
                    else open[key2] = k * 3 + e;
                }
            }
            foreach (KeyValuePair<long, int> pair in open)
            {
                int k = pair.Value / 3, e = pair.Value % 3;
                int c1 = cellOf[k];
                ea[c1].Add(k * 3 + e); eb[c1].Add(k * 3 + (e + 1) % 3); eo[c1].Add(-1);
            }
            for (int ci = 0; ci < cells.Length; ci++)
            {
                cells[ci].edgeA = ea[ci].ToArray(); cells[ci].edgeB = eb[ci].ToArray(); cells[ci].edgeOther = eo[ci].ToArray();
                var near = new List<int>();
                foreach (int o in eo[ci]) if (o >= 0 && !near.Contains(o)) near.Add(o);
                cells[ci].near = near.ToArray();
            }
            // ---- the pieces of skin the part is made of, whole: panels joined to one another through their borders
            int islands = 0;
            var marked = new bool[cells.Length];
            var stack = new Stack<int>();
            for (int ci = 0; ci < cells.Length; ci++)
            {
                if (marked[ci]) continue;
                stack.Push(ci); marked[ci] = true;
                while (stack.Count > 0)
                {
                    int c = stack.Pop();
                    cells[c].island = islands;
                    foreach (int o in cells[c].near) if (!marked[o]) { marked[o] = true; stack.Push(o); }
                }
                islands++;
            }
            tookMs = (float)clock.Elapsed.TotalMilliseconds;
            Built++;
            ready = true;
        }

        static float Area(Vector3 a, Vector3 b, Vector3 c) => Vector3.Cross(b - a, c - a).magnitude * 0.5f;

        static long EdgeKey(Vector3 a, Vector3 b)
        {
            long ha = PointKey(a), hb = PointKey(b);
            if (ha > hb) { long s = ha; ha = hb; hb = s; }
            unchecked { return ha * -7046029254386353131L ^ (hb + (hb << 21) + 0x632BE59BD9B4E019L); }
        }

        static long PointKey(Vector3 a)
        {
            unchecked
            {
                long x = (long)Math.Round(a.x * 8000.0), y = (long)Math.Round(a.y * 8000.0), z = (long)Math.Round(a.z * 8000.0);
                return (x * 73856093L) ^ (y * 19349663L) ^ (z * 83492791L) ^ (x << 40) ^ (y << 20);
            }
        }

        /// <summary>A smooth noise between -1 and 1, a different one for each number (each panel's own, so that its border wanders its own way).</summary>
        public static float Noise(Vector3 x, int which)
        {
            float ox = which * 17.31f, oy = which * 5.17f, oz = which * 11.73f;
            float fx = x.x + ox, fy = x.y + oy, fz = x.z + oz;
            int ix = Mathf.FloorToInt(fx), iy = Mathf.FloorToInt(fy), iz = Mathf.FloorToInt(fz);
            float tx = fx - ix, ty = fy - iy, tz = fz - iz;
            tx = tx * tx * (3f - 2f * tx); ty = ty * ty * (3f - 2f * ty); tz = tz * tz * (3f - 2f * tz);
            float c000 = Hash(ix, iy, iz), c100 = Hash(ix + 1, iy, iz), c010 = Hash(ix, iy + 1, iz), c110 = Hash(ix + 1, iy + 1, iz);
            float c001 = Hash(ix, iy, iz + 1), c101 = Hash(ix + 1, iy, iz + 1), c011 = Hash(ix, iy + 1, iz + 1), c111 = Hash(ix + 1, iy + 1, iz + 1);
            float x00 = c000 + (c100 - c000) * tx, x10 = c010 + (c110 - c010) * tx, x01 = c001 + (c101 - c001) * tx, x11 = c011 + (c111 - c011) * tx;
            float y0 = x00 + (x10 - x00) * ty, y1 = x01 + (x11 - x01) * ty;
            return (y0 + (y1 - y0) * tz) * 2f - 1f;
        }

        static float Hash(int x, int y, int z)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + z * 1274126177);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777216f;
            }
        }

        /// <summary>
        /// Cut every edge longer than the limit in two, the same on both sides of it (so that no corner of one triangle lies in the
        /// middle of its neighbour's side, which would open a crack when the skin is bent), until none is, or one more round would
        /// make more triangles than may be.
        /// </summary>
        void Refine(float limit2, bool[] whole, int most)
        {
            for (int round = 0; round < 10; round++)
            {
                int T = gslot.Count, after = 0;
                bool any = false;
                for (int k = 0; k < T; k++)
                {
                    if (whole[gcomp[k]]) { after++; continue; }
                    int bits = Mask(k, limit2);
                    after += 1 + Bits(bits);
                    if (bits != 0) any = true;
                }
                if (!any || after > most) return;
                var np = new List<Vector3>(after * 3); var nn = new List<Vector3>(after * 3);
                var nuv = new List<Vector2>(after * 3); var nt = new List<Vector4>(after * 3);
                var nslot = new List<int>(after); var ncomp = new List<int>(after);
                for (int k = 0; k < T; k++)
                {
                    int bits = whole[gcomp[k]] ? 0 : Mask(k, limit2);
                    int b = k * 3;
                    if (bits == 0) { Copy(b, b + 1, b + 2, np, nn, nuv, nt); nslot.Add(gslot[k]); ncomp.Add(gcomp[k]); continue; }
                    // corners 0 1 2 (as b, b+1, b+2) and the middles of edges 0-1 (3), 1-2 (4), 2-0 (5): made as extra corners
                    int baseIndex = gp.Count;
                    Mid(b, b + 1); Mid(b + 1, b + 2); Mid(b + 2, b);
                    int c0 = b, c1 = b + 1, c2 = b + 2, m0 = baseIndex, m1 = baseIndex + 1, m2 = baseIndex + 2;
                    switch (bits)
                    {
                        case 1: Copy(c0, m0, c2, np, nn, nuv, nt); Copy(m0, c1, c2, np, nn, nuv, nt); break;
                        case 2: Copy(c0, c1, m1, np, nn, nuv, nt); Copy(c0, m1, c2, np, nn, nuv, nt); break;
                        case 4: Copy(c0, c1, m2, np, nn, nuv, nt); Copy(c1, c2, m2, np, nn, nuv, nt); break;
                        case 3: Copy(m0, c1, m1, np, nn, nuv, nt); Copy(c0, m0, m1, np, nn, nuv, nt); Copy(c0, m1, c2, np, nn, nuv, nt); break;
                        case 6: Copy(m1, c2, m2, np, nn, nuv, nt); Copy(c0, c1, m1, np, nn, nuv, nt); Copy(c0, m1, m2, np, nn, nuv, nt); break;
                        case 5: Copy(c0, m0, m2, np, nn, nuv, nt); Copy(m0, c1, c2, np, nn, nuv, nt); Copy(m0, c2, m2, np, nn, nuv, nt); break;
                        default: Copy(c0, m0, m2, np, nn, nuv, nt); Copy(m0, c1, m1, np, nn, nuv, nt); Copy(m2, m1, c2, np, nn, nuv, nt); Copy(m0, m1, m2, np, nn, nuv, nt); break;
                    }
                    int pieces = 1 + Bits(bits);
                    for (int q = 0; q < pieces; q++) { nslot.Add(gslot[k]); ncomp.Add(gcomp[k]); }
                }
                gp = np; gn = nn; guv = nuv; gt = nt; gslot = nslot; gcomp = ncomp;
            }
        }

        int Mask(int k, float limit2)
        {
            int b = k * 3, bits = 0;
            if ((gp[b] - gp[b + 1]).sqrMagnitude > limit2) bits |= 1;
            if ((gp[b + 1] - gp[b + 2]).sqrMagnitude > limit2) bits |= 2;
            if ((gp[b + 2] - gp[b]).sqrMagnitude > limit2) bits |= 4;
            return bits;
        }

        static int Bits(int b) => (b & 1) + ((b >> 1) & 1) + ((b >> 2) & 1);

        /// <summary>A corner half way along an edge, put at the end of the gathered lists (it is copied from there into the new ones).</summary>
        void Mid(int a, int b)
        {
            gp.Add((gp[a] + gp[b]) * 0.5f);
            Vector3 nm = gn[a] + gn[b];
            gn.Add(nm.sqrMagnitude > 1e-8f ? nm.normalized : gn[a]);
            guv.Add((guv[a] + guv[b]) * 0.5f);
            Vector4 tm = (gt[a] + gt[b]) * 0.5f;
            tm.w = gt[a].w;
            gt.Add(tm);
        }

        void Copy(int a, int b, int c, List<Vector3> np, List<Vector3> nn, List<Vector2> nuv, List<Vector4> nt)
        {
            np.Add(gp[a]); np.Add(gp[b]); np.Add(gp[c]);
            nn.Add(gn[a]); nn.Add(gn[b]); nn.Add(gn[c]);
            nuv.Add(guv[a]); nuv.Add(guv[b]); nuv.Add(guv[c]);
            nt.Add(gt[a]); nt.Add(gt[b]); nt.Add(gt[c]);
        }
    }
}
