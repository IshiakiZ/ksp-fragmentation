using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Fragmentation
{
    /// <summary>
    /// What a crash does to the ground: on soil, a scar of torn earth along the way a wreck dug its way, as wide as all of it that
    /// touched, with low ragged lips of the earth and turf it pushed aside; a crater with a raised rim where a part hit hard; on
    /// paving, a dark scrape.
    ///
    /// The game's terrain is built afresh as the view moves (its pieces are made and unmade with the camera), so it is not
    /// itself changed: these are meshes laid on it, each point set on the ground beneath it, coloured by the earth of the
    /// place and lit as the ground is. A mesh cannot go below the ground's own surface (the ground would be drawn over it), so
    /// the floor of a scar is drawn at the ground's level, dark torn earth streaked the way the wreck went, darkest along its edges
    /// where the walls of a trench would be in shade; its lips are low (a tenth of a metre or two, the most of the earth thrown out
    /// as clods and slabs of turf, see Crashes.Slide); and the wreck that dug it sits down in the ground by as much as it dug in
    /// (see Hull.Bite). Seen from the side, a scar is a torn brown band with ragged edges, not a bank.
    /// </summary>
    public static class Marks
    {
        sealed class Furrow
        {
            public object key;
            public GameObject go;
            public Mesh mesh;
            public readonly List<Vector3> at = new List<Vector3>();      // in the root's axes: the middle of the scar at each step
            public readonly List<Vector3> side = new List<Vector3>();    // across it, one long
            public readonly List<Vector3> up = new List<Vector3>();
            public readonly List<float> width = new List<float>(), deep = new List<float>(), run = new List<float>();
            public readonly List<float[]> lift = new List<float[]>();    // each point's corners: the ground's height there, looked up once
            public Vector3 lastWorld;
            public float lastTime;
            public bool paved, dirty;
            public float layer;                                          // a few millimetres more lift for each scar, so that two over one another do not flicker
            public int seed;
        }

        static readonly List<Furrow> furrows = new List<Furrow>();
        static readonly List<GameObject> craters = new List<GameObject>();
        static GameObject root;
        static CelestialBody body;
        static double lat, lon, alt;
        static Quaternion turned;              // the root's turn against the body's own, kept as the body turns
        static Material earth, scrape;
        static readonly Dictionary<object, Furrow> open = new Dictionary<object, Furrow>();
        static float dirtyAt;

        const float Spacing = 0.8f;            // metres between the points of a scar
        // Across a scar, in half-widths: the ragged outer edge of torn turf, the lip, the dark edge of the floor, the floor.
        static readonly float[] across = { -1.75f, -1.3f, -1.08f, -0.9f, -0.55f, 0f, 0.55f, 0.9f, 1.08f, 1.3f, 1.75f };
        static readonly float[] rise = { 0f, 0.5f, 1f, 0.3f, 0.04f, 0f, 0.04f, 0.3f, 1f, 0.5f, 0f };              // of the lip's height
        static readonly float[] texU = { 0f, 0.12f, 0.2f, 0.27f, 0.38f, 0.5f, 0.62f, 0.73f, 0.8f, 0.88f, 1f };

        /// <summary>The ground under a place: where it is and which way is up, from a look straight down.</summary>
        static bool Under(Vector3 near, Vector3 up, out Vector3 point, out Vector3 normal)
        {
            if (Physics.Raycast(near + up * 3f, -up, out RaycastHit hit, 12f, 1 << 15, QueryTriggerInteraction.Ignore))
            {
                point = hit.point; normal = hit.normal;
                return true;
            }
            point = near; normal = up;
            return false;
        }

        static void Anchor(Vector3 world)
        {
            CelestialBody b = FlightGlobals.currentMainBody;
            if (b == null) return;
            if (root == null || body != b || (root.transform.position - world).sqrMagnitude > 5000f * 5000f)
            {
                Clear();
                body = b;
                root = new GameObject("Fragmentation marks") { layer = 15 };
                lat = b.GetLatitude(world); lon = b.GetLongitude(world); alt = b.GetAltitude(world);
                root.transform.position = b.GetWorldSurfacePosition(lat, lon, alt);
                root.transform.rotation = Quaternion.identity;
                turned = Quaternion.Inverse(b.transform.rotation);
            }
        }

        static readonly List<KeyValuePair<GameObject, float>> bowls = new List<KeyValuePair<GameObject, float>>();

        /// <summary>Every frame: the marks stay where they are on the planet, however the scene is moved and turned under the camera.</summary>
        public static void Step()
        {
            if (root == null || body == null) return;
            root.transform.SetPositionAndRotation(body.GetWorldSurfacePosition(lat, lon, alt), body.transform.rotation * turned);
            // (a crater begun last frame: its mesh, one a frame)
            if (bowls.Count > 0)
            {
                GameObject go = bowls[0].Key;
                float shallow = bowls[0].Value;
                bowls.RemoveAt(0);
                if (go != null) go.GetComponent<MeshFilter>().sharedMesh = Bowl(go.transform.position, go.transform.rotation, go.transform.localScale.x, shallow);
            }
            if (dirtyAt > 0f && Time.unscaledTime >= dirtyAt)
            {
                dirtyAt = 0f;
                foreach (Furrow f in furrows) if (f.dirty) Build(f);
            }
        }

        /// <summary>
        /// A wreck (the key) dragged along the ground this step, touching it across so wide a band about a place: its scar (or scrape)
        /// goes on, as deep as the deepest part of it has dug in.
        /// </summary>
        public static void Track(object key, Vector3 at, Vector3 normal, Vector3 along, float speed, float bite, float width, bool paved)
        {
            if (key == null || speed < 1.5f) return;
            if (!Under(at, normal, out Vector3 ground, out Vector3 up)) return;
            Anchor(ground);
            if (root == null) return;
            float now = Time.time;
            if (!open.TryGetValue(key, out Furrow f) || f == null || now - f.lastTime > 0.6f || f.paved != paved || (ground - f.lastWorld).sqrMagnitude > 64f)
            {
                f = new Furrow { key = key, paved = paved, lastWorld = ground - along * Spacing, layer = 0.004f * (furrows.Count % 8), seed = Random.Range(1, 1000) };
                f.go = new GameObject(paved ? "Fragmentation scrape" : "Fragmentation scar") { layer = 15 };
                f.go.transform.SetParent(root.transform, false);
                f.go.AddComponent<MeshFilter>().sharedMesh = f.mesh = new Mesh { name = "Fragmentation scar" };
                f.mesh.MarkDynamic();
                MeshRenderer r = f.go.AddComponent<MeshRenderer>();
                r.sharedMaterial = paved ? Scrape() : Earth();
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = true;
                furrows.Add(f);
                open[key] = f;
                if (furrows.Count > 24) { Furrow old = furrows[0]; furrows.RemoveAt(0); if (old.go != null) Object.Destroy(old.go); if (old.mesh != null) Object.Destroy(old.mesh); }
            }
            f.lastTime = now;
            if ((ground - f.lastWorld).sqrMagnitude < Spacing * Spacing) return;
            if (f.at.Count >= 500) return;
            Vector3 flat = Vector3.ProjectOnPlane(along, up);
            if (flat.sqrMagnitude < 1e-4f) return;
            flat.Normalize();
            Transform rt = root.transform;
            f.at.Add(rt.InverseTransformPoint(ground));
            f.up.Add(rt.InverseTransformDirection(up));
            f.side.Add(rt.InverseTransformDirection(Vector3.Cross(up, flat).normalized));
            f.width.Add(Mathf.Clamp(width, paved ? 0.15f : 0.25f, 9f));
            f.deep.Add(paved ? 0f : Mathf.Clamp(bite * 0.9f + 0.04f, 0.04f, 0.6f));
            f.run.Add(f.run.Count == 0 ? 0f : f.run[f.run.Count - 1] + (ground - f.lastWorld).magnitude);
            f.lift.Add(null);
            f.lastWorld = ground;
            f.dirty = true;
            if (dirtyAt <= 0f) dirtyAt = Time.unscaledTime + 0.1f;
        }

        /// <summary>A hard blow on soil: a crater as big as the blow (its rim thrown up, earth thrown out round it), and clods of earth flying.</summary>
        public static void Crater(Vector3 at, Vector3 normal, float energy, Vector3 velocity, Hits h)
        {
            if (energy <= 1f || h == null) return;
            if (!Under(at, normal, out Vector3 ground, out Vector3 up)) return;
            Anchor(ground);
            if (root == null) return;
            // (as big as the cube root of the energy; a gentle blow leaves a dent a metre across, a hard one a crater several metres across: see the Research page)
            float R = Mathf.Clamp(0.15f * Mathf.Pow(energy, 1f / 3f), 0.5f, 7f);
            // one near a crater made already makes that one bigger instead
            Transform rt = root.transform;
            Vector3 local = rt.InverseTransformPoint(ground);
            foreach (GameObject c in craters)
                if (c != null && (c.transform.localPosition - local).magnitude < c.transform.localScale.x * 0.8f)
                {
                    float grown = Mathf.Min(7f, Mathf.Pow(Mathf.Pow(c.transform.localScale.x, 3f) + R * R * R, 1f / 3f));
                    c.transform.localScale = new Vector3(grown, grown, grown);
                    Clods(ground, up, velocity, R);
                    return;
                }
            // (laid along the way it was going: the earth it throws is heaped up on the far side, as at Shanksville, where a steep fast impact
            // left a pit with "a mounded pile of dirt opposite the initial impact point": see the Research page)
            Vector3 downrange = Vector3.ProjectOnPlane(velocity, up);
            if (downrange.sqrMagnitude < 0.01f) downrange = Vector3.ProjectOnPlane(Random.onUnitSphere, up);
            downrange.Normalize();
            float shallow = Mathf.Clamp01(Vector3.ProjectOnPlane(velocity, up).magnitude / Mathf.Max(0.1f, velocity.magnitude));
            Quaternion orient = Quaternion.LookRotation(Vector3.Cross(downrange, up), up);
            var go = new GameObject("Fragmentation crater") { layer = 15 };
            go.transform.SetParent(rt, false);
            go.transform.localPosition = local;
            go.transform.localRotation = Quaternion.Inverse(rt.rotation) * orient;
            go.transform.localScale = new Vector3(R, R, R);
            // (its mesh made in the next frame, each of its rings set on the ground there: a hard blow's frame has enough to do)
            go.AddComponent<MeshFilter>();
            bowls.Add(new KeyValuePair<GameObject, float>(go, shallow));
            MeshRenderer r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = Earth();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = true;
            craters.Add(go);
            if (craters.Count > 16) { if (craters[0] != null) Object.Destroy(craters[0]); craters.RemoveAt(0); }
            Clods(ground, up, velocity, R);
        }

        static void Clods(Vector3 ground, Vector3 up, Vector3 velocity, float R)
        {
            int n = Mathf.Clamp(Mathf.RoundToInt(R * 14f), 5, 60);
            float speed = Mathf.Sqrt(9.81f * R) * 1.6f;
            Bits.Emit(ground + up * 0.3f, velocity * 0.25f + up * speed * 0.7f, speed * 0.8f, n, 0.03f, Mathf.Clamp(R * 0.08f, 0.06f, 0.25f), Bits.Kind.Clod, 0f, 0f, speed * 0.5f);
            Bits.Emit(ground + up * 0.3f, velocity * 0.2f + up * speed * 0.5f, speed * 0.7f, Mathf.Max(2, n / 3), 0.06f, Mathf.Clamp(R * 0.12f, 0.1f, 0.3f), Bits.Kind.Turf, 0f, 0f, speed * 0.4f);
        }

        /// <summary>A crater a unit across (scaled to its size): its floor, its raised rim, and the earth thrown out round it thinning away; each ring set on the ground there.</summary>
        static Mesh Bowl(Vector3 centre, Quaternion toWorld, float R, float shallow)
        {
            Vector3 up = toWorld * Vector3.up;
            const int around = 18;
            float[] ring = { 0f, 0.55f, 0.85f, 1f, 1.25f, 1.9f };
            float[] height = { 0.03f, 0.03f, 0.08f, 0.13f, 0.06f, 0f };    // of the radius (the rim kept low: a crater from a crash is mostly a scar)
            float[] u = { 0.5f, 0.42f, 0.3f, 0.2f, 0.12f, 0f };
            var v = new List<Vector3>();
            var uv = new List<Vector2>();
            var tris = new List<int>();
            for (int r = 0; r < ring.Length; r++)
                for (int a = 0; a <= around; a++)
                {
                    float ang = a * Mathf.PI * 2f / around;
                    float wob = r == 0 ? 0f : 1f + 0.12f * Mathf.Sin(ang * 3f + r) + 0.08f * Mathf.Sin(ang * 7f + 1.3f * r);
                    // (longer along the way it was going the shallower it came in; its rim and the earth thrown out higher on the far side)
                    float far = Mathf.Max(0f, Mathf.Cos(ang));
                    Vector3 flat = new Vector3(Mathf.Cos(ang) * (1f + 0.5f * shallow * far), 0f, Mathf.Sin(ang)) * ring[r] * wob;
                    // (each point on the ground beneath it, as the ground lies)
                    Vector3 world = centre + toWorld * (flat * R);
                    float lift = 0f;
                    if (Physics.Raycast(world + up * 4f, -up, out RaycastHit hit, 10f, 1 << 15, QueryTriggerInteraction.Ignore)) lift = Vector3.Dot(hit.point - centre, up) / R;
                    v.Add(new Vector3(flat.x, lift + height[r] * (1f + 0.8f * far) + 0.03f / R, flat.z));
                    uv.Add(new Vector2(u[r], a / (float)around * 3f));
                }
            for (int r = 0; r + 1 < ring.Length; r++)
                for (int a = 0; a < around; a++)
                {
                    int i0 = r * (around + 1) + a, i1 = i0 + 1, j0 = i0 + around + 1, j1 = j0 + 1;
                    tris.Add(i0); tris.Add(j0); tris.Add(i1);
                    tris.Add(i1); tris.Add(j0); tris.Add(j1);
                }
            var m = new Mesh { name = "Fragmentation crater" };
            m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.RecalculateTangents();
            m.RecalculateBounds();
            return m;
        }

        static readonly List<Vector3> buildV = new List<Vector3>();
        static readonly List<Vector2> buildUV = new List<Vector2>();
        static readonly List<int> buildTris = new List<int>();

        /// <summary>A scar's mesh, as far as it has got: across it the ragged edges, the low lips and the floor, each point on the ground beneath it.</summary>
        static void Build(Furrow f)
        {
            f.dirty = false;
            int n = f.at.Count;
            if (n < 2 || root == null) return;
            int cols = across.Length;
            // (one set of lists for every scar, made again ten times a second while a wreck slides)
            List<Vector3> v = buildV; List<Vector2> uv = buildUV; List<int> tris = buildTris;
            v.Clear(); uv.Clear(); tris.Clear();
            Transform rt = root.transform;
            for (int i = 0; i < n; i++)
            {
                Vector3 c = f.at[i], s = f.side[i], u = f.up[i];
                float run = f.run[i];
                // (not a ruled band: it wanders a little, wider and narrower, its lips higher and lower, its edges torn)
                float half = f.width[i] * 0.5f * (1f + (f.paved ? 0.05f : 0.14f) * Noise(run * 0.31f, 0.5f, f.seed));
                if (!f.paved) c += s * (0.06f * half * Noise(run * 0.17f, 1.5f, f.seed));
                // (the earth pushed aside heaped on either side, lumpy, higher on one side than the other here and there: about half as high as the
                // scar is deep, the rest thrown out as clods and slabs of turf)
                float lip = f.paved ? 0f : Mathf.Clamp(0.5f * f.deep[i] + 0.02f, 0.02f, 0.3f);
                float lipLeft = lip * (0.8f + 0.45f * Noise(run * 0.6f, 2.5f, f.seed)), lipRight = lip * (0.8f + 0.45f * Noise(run * 0.6f, 5.5f, f.seed));
                // (it begins and ends softly: the first and last points have no lips)
                float ends = Mathf.Clamp01(Mathf.Min(i, n - 1 - i) / 2f);
                float[] lifts = f.lift[i];
                bool look = lifts == null;
                if (look) f.lift[i] = lifts = new float[cols];
                for (int k = 0; k < cols; k++)
                {
                    float a = across[k];
                    // (the outer edge torn: in and out by a fifth of the half-width, differently on each side)
                    // (a scrape on paving is not torn at its edges: it is metal dragged over a hard flat face)
                    if (!f.paved && (k == 0 || k == cols - 1)) a *= 1f + 0.35f * Noise(run * 1.6f, k == 0 ? 3.5f : 4.5f, f.seed);
                    else if (!f.paved && (k == 1 || k == cols - 2)) a *= 1f + 0.15f * Noise(run * 1.3f, k == 1 ? 6.5f : 7.5f, f.seed);
                    Vector3 p = c + s * (a * half);
                    if (look)
                    {
                        Vector3 world = rt.TransformPoint(p), up = rt.TransformDirection(u);
                        lifts[k] = Physics.Raycast(world + up * 3f, -up, out RaycastHit hit, 8f, 1 << 15, QueryTriggerInteraction.Ignore) ? Vector3.Dot(hit.point - world, up) : 0f;
                    }
                    p += u * lifts[k];
                    float lift = f.paved ? 0.015f + f.layer : 0.03f + f.layer + (k < cols / 2 ? lipLeft : lipRight) * rise[k] * ends;
                    v.Add(p + u * lift);
                    uv.Add(new Vector2(f.paved ? (k / (float)(cols - 1)) : texU[k], run / 2.5f));
                }
            }
            for (int i = 0; i + 1 < n; i++)
                for (int k = 0; k + 1 < cols; k++)
                {
                    int a = i * cols + k, b = a + 1, c = a + cols, d = c + 1;
                    tris.Add(a); tris.Add(c); tris.Add(b);
                    tris.Add(b); tris.Add(c); tris.Add(d);
                }
            f.mesh.Clear();
            f.mesh.SetVertices(v);
            f.mesh.SetUVs(0, uv);
            f.mesh.SetTriangles(tris, 0);
            f.mesh.RecalculateNormals();
            f.mesh.RecalculateTangents();
            f.mesh.RecalculateBounds();
        }

        /// <summary>The materials made ready before any crash (making the earth's texture takes some milliseconds): the earth of the place given.</summary>
        public static void Prepare(Vector3 near)
        {
            CelestialBody b = FlightGlobals.currentMainBody;
            if (b != null && body == null) { lat = b.GetLatitude(near); lon = b.GetLongitude(near); }
            Earth();
            Scrape();
        }

        /// <summary>The materials the marks are drawn with (made if they are not yet).</summary>
        public static Material[] Materials() => new[] { Earth(), Scrape() };

        /// <summary>
        /// Torn earth, across a scar (u) and along it (v, a tile every two and a half metres): the floor dark and streaked the way the wreck
        /// went, darkest at its edges; the lips lumpy, lighter, with slabs of turf turned over in them; the outer edge torn turf, grass and
        /// earth mixed. Coloured by the ground of the place.
        /// </summary>
        static Material Earth()
        {
            if (earth != null) return earth;
            Color soil = Soil(), grass = GrassColour();
            // (clods are moist earth from below the turf: darker than the ground's dry top)
            Bits.Earth(new Color(soil.r * 0.7f, soil.g * 0.66f, soil.b * 0.62f), Color.Lerp(grass, soil, 0.25f));
            const int S = 128;
            var tex = new Texture2D(S, S, TextureFormat.RGB24, true) { name = "Fragmentation earth", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            var bump = new Texture2D(S, S, TextureFormat.RGBA32, true, true) { name = "Fragmentation earth bumps", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear };
            var h = new float[S * S];
            var px = new Color[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float u = x / (float)S, d = Mathf.Abs(u - 0.5f) * 2f;          // 0 in the middle of the floor, 1 at the outer edge
                    // (tiling along the scar: the noise repeats every tile)
                    float lump = 0.6f * Tiled(x * 0.11f, y * 0.11f, S * 0.11f, 1) + 0.4f * Tiled(x * 0.31f, y * 0.31f, S * 0.31f, 2);
                    float fine = Tiled(x * 0.9f, y * 0.9f, S * 0.9f, 3);
                    float streak = Tiled(x * 0.75f, y * 0.045f, S * 0.045f, 4);         // long along, narrow across: dragged metal's striations
                    float floor = 1f - Mathf.Clamp01((d - 0.55f) * 6f);                    // the floor (to about the dark edge)
                    float wall = Mathf.Clamp01(1f - Mathf.Abs(d - 0.55f) * 7f);            // the dark edge where a trench's wall would be in shade
                    float lipZone = Mathf.Clamp01(1f - Mathf.Abs(d - 0.74f) * 5f);         // the lips
                    float outer = Mathf.Clamp01((d - 0.82f) * 5.5f);                       // torn turf at the edge
                    float chunk = Mathf.Clamp01((Tiled(x * 0.17f, y * 0.17f, S * 0.17f, 5) - 0.35f) * 4f);    // slabs of turf turned over
                    float dark = floor * (0.62f + 0.12f * streak) + (1f - floor) * (0.85f + 0.2f * lump);
                    dark *= 1f - 0.35f * wall;
                    dark *= 0.85f + 0.25f * lump + 0.08f * fine;
                    Color c = soil * dark;
                    float green = Mathf.Max(outer * Mathf.Clamp01(0.45f + lump), lipZone * chunk * 0.7f);
                    c = Color.Lerp(c, Color.Lerp(grass, soil, 0.25f) * (0.75f + 0.3f * fine), green);
                    c.a = 1f;
                    px[y * S + x] = c;
                    h[y * S + x] = floor * (0.3f * streak + 0.25f * fine) + (1f - floor) * (lump + 0.3f * fine) + 0.4f * lipZone * chunk;
                }
            var nm = new Color[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float dx = h[y * S + (x + 1) % S] - h[y * S + (x + S - 1) % S];
                    float dy = h[((y + 1) % S) * S + x] - h[((y + S - 1) % S) * S + x];
                    Vector3 nv = new Vector3(-dx * 2.5f, -dy * 2.5f, 1f).normalized;
                    // (the game's bump maps are read as a normal map packed in the alpha and green channels)
                    nm[y * S + x] = new Color(nv.y * 0.5f + 0.5f, nv.y * 0.5f + 0.5f, nv.y * 0.5f + 0.5f, nv.x * 0.5f + 0.5f);
                }
            tex.SetPixels(px); tex.Apply(true, true);
            bump.SetPixels(nm); bump.Apply(true, true);
            Shader s = Shader.Find("KSP/Bumped") ?? Shader.Find("KSP/Diffuse") ?? Shader.Find("Diffuse");
            earth = new Material(s) { name = "Fragmentation earth", mainTexture = tex };
            if (earth.HasProperty("_BumpMap")) earth.SetTexture("_BumpMap", bump);
            return earth;
        }

        /// <summary>A scrape on paving: dark streaks of torn metal and burnt rubber-black, with the paving showing between.</summary>
        static Material Scrape()
        {
            if (scrape != null) return scrape;
            const int S = 64;
            var tex = new Texture2D(S, S, TextureFormat.RGB24, true) { name = "Fragmentation scrape", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear };
            var px = new Color[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float streak = 0.5f + 0.5f * Tiled(x * 0.7f, y * 0.04f, S * 0.04f, 5);
                    float edge = Mathf.Abs(x / (float)S - 0.5f) * 2f;
                    float k = Mathf.Lerp(0.16f, 0.36f, Mathf.Clamp01(streak * 0.7f + edge * 0.6f));
                    px[y * S + x] = new Color(k, k * 0.98f, k * 0.95f);
                }
            tex.SetPixels(px); tex.Apply(true, true);
            Shader s = Shader.Find("KSP/Diffuse") ?? Shader.Find("Diffuse");
            scrape = new Material(s) { name = "Fragmentation scrape", mainTexture = tex };
            return scrape;
        }

        /// <summary>The colour of the earth of the place (as the ground looks on the screen).</summary>
        static Color Soil()
        {
            CelestialBody b = body ?? FlightGlobals.currentMainBody;
            string biome = "";
            try { if (b != null) biome = ScienceUtil.GetExperimentBiome(b, lat, lon) ?? ""; } catch { }
            biome = biome.ToLowerInvariant();
            if (b == null) return new Color(0.36f, 0.29f, 0.2f);
            if (biome.Contains("ice") || biome.Contains("pole") || biome.Contains("snow")) return new Color(0.6f, 0.62f, 0.66f);
            switch (b.bodyName)
            {
                case "Kerbin":
                    if (biome.Contains("desert") || biome.Contains("badlands") || biome.Contains("shore") || biome.Contains("beach")) return new Color(0.55f, 0.46f, 0.32f);
                    if (biome.Contains("tundra")) return new Color(0.42f, 0.4f, 0.36f);
                    if (biome.Contains("mountain")) return new Color(0.38f, 0.35f, 0.32f);
                    return new Color(0.4f, 0.31f, 0.21f);
                case "Mun": return new Color(0.4f, 0.4f, 0.41f);
                case "Minmus": return new Color(0.56f, 0.7f, 0.64f);
                case "Duna": return new Color(0.58f, 0.3f, 0.17f);
                case "Eve": return new Color(0.38f, 0.26f, 0.48f);
                case "Laythe": return new Color(0.44f, 0.4f, 0.32f);
            }
            return new Color(0.42f, 0.38f, 0.32f);
        }

        static Color GrassColour()
        {
            CelestialBody b = body ?? FlightGlobals.currentMainBody;
            if (b != null && b.bodyName == "Kerbin") return new Color(0.36f, 0.43f, 0.17f);
            return Soil() * 1.1f;
        }

        static float Noise(float x, float y, int which) => HullPlan.Noise(new Vector3(x, y, 0.37f * which), which);

        /// <summary>Value noise that repeats every so many units along y (so that a texture made of it tiles along a scar), -1 to 1.</summary>
        static float Tiled(float x, float y, float period, int which)
        {
            int p = Mathf.Max(1, Mathf.RoundToInt(period));
            float scale = p / Mathf.Max(0.001f, period);
            y *= scale;
            int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
            float tx = x - ix, ty = y - iy;
            tx = tx * tx * (3f - 2f * tx); ty = ty * ty * (3f - 2f * ty);
            int y0 = ((iy % p) + p) % p, y1 = (y0 + 1) % p;
            float a = Hash(ix, y0, which), b = Hash(ix + 1, y0, which), c = Hash(ix, y1, which), d = Hash(ix + 1, y1, which);
            return (Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty)) * 2f - 1f;
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

        public static void Clear()
        {
            foreach (Furrow f in furrows) { if (f.go != null) Object.Destroy(f.go); if (f.mesh != null) Object.Destroy(f.mesh); }
            furrows.Clear();
            open.Clear();
            foreach (GameObject c in craters) if (c != null) Object.Destroy(c);
            craters.Clear();
            bowls.Clear();
            if (root != null) Object.Destroy(root);
            root = null;
            body = null;
        }

        public static int Count => furrows.Count + craters.Count;
    }
}
