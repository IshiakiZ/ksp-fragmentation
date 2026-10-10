using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Fragmentation
{
    /// <summary>
    /// A destroyed part falls apart into pieces of its own skin: its meshes, as the game draws them at that moment
    /// (any part, from any mod, whose meshes can be read), cut into patches.
    ///
    /// How: the part's surface is taken as loose triangles in the scene's axes; any triangle longer than a piece
    /// should be wide is halved, and halved again, so that a cut can fall anywhere (the side of a tank is often
    /// a handful of triangles as tall as the tank); then points are scattered over the surface, each with a size
    /// drawn so that there are few large pieces and many small ones, and every triangle goes to the point it is
    /// nearest to, nearer counting for more the bigger the point's piece (a "power diagram": each cell is convex,
    /// so each piece is one patch). Each patch becomes a mesh with a back face, so that its inside shows.
    /// </summary>
    public static class Shatter
    {
        /// <summary>A mesh's numbers, read once and kept: a craft is mostly copies of a few parts.</summary>
        sealed class Reading
        {
            public Vector3[] p, n;
            public Vector2[] uv;
            public Vector4[] t;
            public int[][] subs;
        }

        static readonly Dictionary<int, Reading> kept = new Dictionary<int, Reading>();

        static Reading Read(Mesh mesh)
        {
            int id = mesh.GetInstanceID();
            if (kept.TryGetValue(id, out Reading r)) return r;
            if (kept.Count > 40) kept.Clear();
            r = new Reading { p = mesh.vertices, n = mesh.normals, uv = mesh.uv, t = mesh.tangents, subs = new int[mesh.subMeshCount][] };
            for (int s = 0; s < r.subs.Length; s++) r.subs[s] = mesh.GetTriangles(s);
            if (r.n == null || r.n.Length != r.p.Length) r.n = null;
            if (r.uv == null || r.uv.Length != r.p.Length) r.uv = null;
            if (r.t == null || r.t.Length != r.p.Length) r.t = null;
            kept[id] = r;
            return r;
        }

        /// <summary>A part's skin as loose triangles: three corners each, in the scene's axes.</summary>
        public sealed class Skin
        {
            public readonly List<Vector3> p = new List<Vector3>(6000), n = new List<Vector3>(6000);
            public readonly List<Vector2> uv = new List<Vector2>(6000);
            public readonly List<Vector4> t = new List<Vector4>(6000);
            public readonly List<int> mat = new List<int>(2000);              // for each triangle: which of the materials
            public readonly List<Material> materials = new List<Material>();
            public readonly List<Color> colours = new List<Color>();
            public int Count => mat.Count;
            public Vector3 middle;
            public float area, size;

            public void Clear() { p.Clear(); n.Clear(); uv.Clear(); t.Clear(); mat.Clear(); materials.Clear(); colours.Clear(); area = size = 0f; }
        }

        /// <summary>
        /// Gather a part's skin. What is not solid is left out: anything drawn see-through (glows, plumes, the flames of
        /// other mods), shrouds already cast off, anything switched off or hidden. False where nothing could be read.
        /// </summary>
        public static bool Gather(Part part, Skin s, int most)
        {
            s.Clear();
            List<MeshFilter> filters = part.FindModelComponents<MeshFilter>();
            if (filters == null) return false;
            var bounds = new Bounds();
            bool any = false;
            // (a part with more triangles than may be taken gives every so many of them, all over it, not the first so many: its pieces
            // are then sparser, but there is no hole where the rest of it was)
            int all = 0;
            foreach (MeshFilter f in filters)
            {
                if (f == null || !f.gameObject.activeInHierarchy || f.sharedMesh == null || !f.sharedMesh.isReadable) continue;
                for (int sub = 0; sub < f.sharedMesh.subMeshCount; sub++) all += (int)f.sharedMesh.GetIndexCount(sub) / 3;
            }
            int every = Mathf.Max(1, Mathf.CeilToInt(all / (float)Mathf.Max(1, most)));
            int seen = 0;
            foreach (MeshFilter f in filters)
            {
                if (f == null || !f.gameObject.activeInHierarchy) continue;
                Mesh mesh = f.sharedMesh;
                MeshRenderer r = f.GetComponent<MeshRenderer>();
                if (mesh == null || !mesh.isReadable || r == null || !r.enabled || r.forceRenderingOff) continue;
                Material[] mats = r.sharedMaterials;
                if (mats == null || mats.Length == 0) continue;
                Reading read;
                try { read = Read(mesh); }
                catch (Exception) { continue; }
                Matrix4x4 m = f.transform.localToWorldMatrix;
                for (int sub = 0; sub < read.subs.Length; sub++)
                {
                    Material mat = mats[Mathf.Min(sub, mats.Length - 1)];
                    if (!Solid(mat)) continue;
                    int which = s.materials.IndexOf(mat);
                    if (which < 0)
                    {
                        which = s.materials.Count;
                        s.materials.Add(mat);
                        s.colours.Add(mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.white);
                    }
                    int[] tris = read.subs[sub];
                    for (int k = 0; k + 2 < tris.Length; k += 3)
                    {
                        if (s.Count >= most) break;
                        if (every > 1 && (seen++ % every) != 0) continue;
                        for (int c = 0; c < 3; c++)
                        {
                            int v = tris[k + c];
                            Vector3 at = m.MultiplyPoint3x4(read.p[v]);
                            s.p.Add(at);
                            s.n.Add(read.n != null ? m.MultiplyVector(read.n[v]).normalized : Vector3.up);
                            s.uv.Add(read.uv != null ? read.uv[v] : Vector2.zero);
                            if (read.t != null)
                            {
                                Vector4 t0 = read.t[v];
                                Vector3 t3 = m.MultiplyVector(new Vector3(t0.x, t0.y, t0.z)).normalized;
                                s.t.Add(new Vector4(t3.x, t3.y, t3.z, t0.w));
                            }
                            else s.t.Add(new Vector4(1f, 0f, 0f, 1f));
                            if (!any) { bounds = new Bounds(at, Vector3.zero); any = true; }
                            else bounds.Encapsulate(at);
                        }
                        s.mat.Add(which);
                    }
                }
            }
            if (s.Count < 2) return false;
            s.middle = bounds.center;
            s.size = bounds.size.magnitude;
            return true;
        }

        /// <summary>Whether a material draws something solid (not a glow, a plume or a see-through panel).</summary>
        static bool Solid(Material m)
        {
            if (m == null || m.shader == null) return false;
            if (m.renderQueue >= 2500) return false;
            string name = m.shader.name;
            return !(name.StartsWith("Waterfall", StringComparison.Ordinal) || name.IndexOf("Particle", StringComparison.OrdinalIgnoreCase) >= 0
                     || name.IndexOf("Additive", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Translucent", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        /// <summary>Halve every triangle longer than so much along its longest side, until none is or there are as many as may be.</summary>
        public static void Split(Skin s, float longest, int most)
        {
            float limit = longest * longest;
            for (int tri = 0; tri < s.Count && s.Count < most; tri++)
            {
                int guard = 0;
                while (guard++ < 12 && s.Count < most)
                {
                    int a = tri * 3, b = a + 1, c = a + 2;
                    float ab = (s.p[a] - s.p[b]).sqrMagnitude, bc = (s.p[b] - s.p[c]).sqrMagnitude, ca = (s.p[c] - s.p[a]).sqrMagnitude;
                    float edge = Mathf.Max(ab, Mathf.Max(bc, ca));
                    if (edge <= limit) break;
                    // Rotate the corners so that the longest side is the first (from corner 0 to corner 1), then cut it in two.
                    int i0, i1, i2;
                    if (edge == ab) { i0 = a; i1 = b; i2 = c; }
                    else if (edge == bc) { i0 = b; i1 = c; i2 = a; }
                    else { i0 = c; i1 = a; i2 = b; }
                    Vector3 p0 = s.p[i0], p1 = s.p[i1], p2 = s.p[i2];
                    Vector3 n0 = s.n[i0], n1 = s.n[i1], n2 = s.n[i2];
                    Vector2 u0 = s.uv[i0], u1 = s.uv[i1], u2 = s.uv[i2];
                    Vector4 t0 = s.t[i0], t1 = s.t[i1], t2 = s.t[i2];
                    Vector3 pm = (p0 + p1) * 0.5f, nm = (n0 + n1).normalized;
                    Vector2 um = (u0 + u1) * 0.5f;
                    Vector4 tm = (t0 + t1) * 0.5f;
                    if (nm.sqrMagnitude < 0.5f) nm = n0;
                    // this triangle becomes (0, middle, 2); a new one is (middle, 1, 2): the same way round as before
                    s.p[a] = p0; s.p[b] = pm; s.p[c] = p2;
                    s.n[a] = n0; s.n[b] = nm; s.n[c] = n2;
                    s.uv[a] = u0; s.uv[b] = um; s.uv[c] = u2;
                    s.t[a] = t0; s.t[b] = tm; s.t[c] = t2;
                    s.p.Add(pm); s.p.Add(p1); s.p.Add(p2);
                    s.n.Add(nm); s.n.Add(n1); s.n.Add(n2);
                    s.uv.Add(um); s.uv.Add(u1); s.uv.Add(u2);
                    s.t.Add(tm); s.t.Add(t1); s.t.Add(t2);
                    s.mat.Add(s.mat[tri]);
                }
            }
        }

        /// <summary>One piece-to-be: the triangles that went to one point.</summary>
        public sealed class Cell
        {
            public readonly List<int> tris = new List<int>();
            public Vector3 middle, facing;
            public float area;
        }

        static readonly List<Vector3> seeds = new List<Vector3>();
        static readonly List<float> weights = new List<float>();
        static float[] areas = new float[0];

        /// <summary>
        /// Cut the skin into about so many cells, the largest about so wide and the smallest about so wide, with sizes
        /// spread as broken things are: the number of pieces bigger than a size goes as that size to the power -1.6
        /// (the fit NASA uses for the pieces of things that blew up in orbit; see the wiki's Research page).
        /// Only within reach of a place, where one is given (reach > 0): what is torn off where a part scraped along.
        /// </summary>
        public static List<Cell> Cut(Skin s, int count, float biggest, float smallest, Vector3 near = default, float reach = 0f)
        {
            var cells = new List<Cell>();
            int tris = s.Count;
            if (tris < 2 || count < 1) return cells;
            if (areas.Length < tris) areas = new float[tris + 1024];
            float total = 0f;
            float reach2 = reach * reach;
            for (int k = 0; k < tris; k++)
            {
                int a = k * 3;
                Vector3 c = (s.p[a] + s.p[a + 1] + s.p[a + 2]) / 3f;
                float area = reach > 0f && (c - near).sqrMagnitude > reach2 ? 0f : Vector3.Cross(s.p[a + 1] - s.p[a], s.p[a + 2] - s.p[a]).magnitude * 0.5f;
                areas[k] = area;
                total += area;
            }
            if (total <= 1e-5f) return cells;
            s.area = reach > 0f ? s.area : total;
            seeds.Clear(); weights.Clear();
            float ratio = Mathf.Clamp01(smallest / Mathf.Max(biggest, 1e-3f)), alpha = 1.6f, low = Mathf.Pow(ratio, alpha);
            for (int k = 0; k < count; k++)
            {
                // a point on the surface, more likely where there is more of it
                float pick = Random.value * total, run = 0f;
                int at = 0;
                for (; at < tris - 1; at++) { run += areas[at]; if (run >= pick) break; }
                if (areas[at] <= 0f) continue;
                int i = at * 3;
                float r1 = Random.value, r2 = Random.value;
                if (r1 + r2 > 1f) { r1 = 1f - r1; r2 = 1f - r2; }
                seeds.Add(s.p[i] + (s.p[i + 1] - s.p[i]) * r1 + (s.p[i + 2] - s.p[i]) * r2);
                // a size from the power law, between the smallest and the biggest (the first is always the biggest: a part leaves one large piece)
                float size = k == 0 ? biggest : smallest * Mathf.Pow(1f - Random.value * (1f - low), -1f / alpha);
                weights.Add(0.5f * Mathf.Min(size, biggest));
                cells.Add(new Cell());
            }
            if (cells.Count == 0) return cells;
            int seedCount = seeds.Count;
            for (int k = 0; k < tris; k++)
            {
                if (areas[k] <= 0f) continue;
                int a = k * 3;
                Vector3 c = (s.p[a] + s.p[a + 1] + s.p[a + 2]) / 3f;
                int best = 0;
                float bestScore = float.MaxValue;
                for (int q = 0; q < seedCount; q++)
                {
                    Vector3 d = c - seeds[q];
                    float score = d.x * d.x + d.y * d.y + d.z * d.z - weights[q] * weights[q];
                    if (score < bestScore) { bestScore = score; best = q; }
                }
                Cell cell = cells[best];
                cell.tris.Add(k);
                cell.area += areas[k];
                cell.middle += c * areas[k];
                cell.facing += Vector3.Cross(s.p[a + 1] - s.p[a], s.p[a + 2] - s.p[a]);
            }
            for (int q = cells.Count - 1; q >= 0; q--)
            {
                if (cells[q].tris.Count == 0 || cells[q].area <= 1e-6f) { cells.RemoveAt(q); continue; }
                cells[q].middle /= cells[q].area;
            }
            cells.Sort((x, y) => y.area.CompareTo(x.area));
            return cells;
        }

        /// <summary>
        /// The mesh of one cell, laid out about the cell's middle in axes of its own (the second axis along the way the
        /// patch faces, the first along its longest reach), with a back face. The turn that puts those axes into the
        /// scene comes back as well.
        /// </summary>
        public static Mesh Build(Skin s, Cell cell, out Quaternion turn, out Vector3 extent)
        {
            Vector3 up = cell.facing.sqrMagnitude > 1e-8f ? cell.facing.normalized : Vector3.up;
            Vector3 far = Vector3.zero;
            float farthest = -1f;
            foreach (int k in cell.tris)
                for (int c = 0; c < 3; c++)
                {
                    Vector3 d = s.p[k * 3 + c] - cell.middle;
                    d -= up * Vector3.Dot(d, up);
                    float dd = d.sqrMagnitude;
                    if (dd > farthest) { farthest = dd; far = d; }
                }
            Vector3 right = far.sqrMagnitude > 1e-8f ? far.normalized : Vector3.Cross(up, Mathf.Abs(up.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 forward = Vector3.Cross(right, up);
            turn = Quaternion.LookRotation(forward, up);
            Quaternion back = Quaternion.Inverse(turn);
            int n = cell.tris.Count;
            var points = new Vector3[n * 6];
            var normals = new Vector3[n * 6];
            var uvs = new Vector2[n * 6];
            var tangents = new Vector4[n * 6];
            // (one list of corners for each material the patch is drawn with)
            int mats = s.materials.Count;
            var lists = new List<int>[mats];
            Vector3 lo = Vector3.one * float.MaxValue, hi = Vector3.one * float.MinValue;
            for (int j = 0; j < n; j++)
            {
                int k = cell.tris[j], a = k * 3, at = j * 6;
                for (int c = 0; c < 3; c++)
                {
                    Vector3 local = back * (s.p[a + c] - cell.middle);
                    Vector3 normal = back * s.n[a + c];
                    Vector4 t4 = s.t[a + c];
                    Vector3 t3 = back * new Vector3(t4.x, t4.y, t4.z);
                    points[at + c] = points[at + 3 + c] = local;
                    normals[at + c] = normal;
                    normals[at + 3 + c] = -normal;
                    uvs[at + c] = uvs[at + 3 + c] = s.uv[a + c];
                    tangents[at + c] = new Vector4(t3.x, t3.y, t3.z, t4.w);
                    tangents[at + 3 + c] = new Vector4(t3.x, t3.y, t3.z, -t4.w);
                    lo = Vector3.Min(lo, local); hi = Vector3.Max(hi, local);
                }
                int m = s.mat[k];
                if (lists[m] == null) lists[m] = new List<int>(n * 6);
                lists[m].Add(at); lists[m].Add(at + 1); lists[m].Add(at + 2);
                // the back face, the other way round
                lists[m].Add(at + 3); lists[m].Add(at + 5); lists[m].Add(at + 4);
            }
            extent = hi - lo;
            var mesh = new Mesh { name = "Fragmentation piece" };
            if (points.Length > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = points;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.tangents = tangents;
            int used = 0;
            for (int m = 0; m < mats; m++) if (lists[m] != null) used++;
            mesh.subMeshCount = used;
            int sub = 0;
            usedMaterials.Clear();
            for (int m = 0; m < mats; m++)
            {
                if (lists[m] == null) continue;
                mesh.SetTriangles(lists[m], sub++);
                usedMaterials.Add(m);
            }
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Which of the skin's materials the mesh last built uses, submesh by submesh.</summary>
        public static readonly List<int> usedMaterials = new List<int>();

        public static void Forget() => kept.Clear();
    }
}
