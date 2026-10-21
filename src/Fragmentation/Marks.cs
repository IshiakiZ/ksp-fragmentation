using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Fragmentation
{
    /// <summary>
    /// What a crash does to the ground: on soil, a scar of torn earth along the way a wreck dug its way, as wide as all of it that
    /// touched: a shallow gouge with low broken lips of the earth it pushed aside, clods and crumbs thrown on the grass beyond them;
    /// a crater with a raised rim where a part hit hard; on paving, a dark scrape.
    ///
    /// The game's terrain is built afresh as the view moves (its pieces are made and unmade with the camera), so it is not
    /// itself changed: these are meshes laid on it, each point set on the ground beneath it, coloured by the earth of the
    /// place and lit as the ground is. A mesh cannot go below the ground's own surface (the ground would be drawn over it), so
    /// the floor of a scar is drawn at the ground's level, moist earth scored the way the wreck went, darkest under its edges
    /// where the wall of a trench would be in shade; its lips are low (nine centimetres at most: most of the earth is thrown out
    /// as clods and slabs of turf, see Crashes.Slide) and broken; beyond them the picture is cut out into scattered clods and
    /// crumbs, so that the scar has no straight edge; and the wreck that dug it sits down in the ground by as much as it dug in
    /// (see Hull.Bite). Seen from the side, a scar is a torn brown band with ragged edges, not a bank. (Until 0.2.1 the lips
    /// were up to 0.3 m high on a mesh half as fine, and from a few metres they read as folded sheeting.)
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
            public readonly List<float> turns = new List<float>();       // how far along the earth's picture each point is (in turns of it)
            public readonly List<float[]> lift = new List<float[]>();    // each point's corners: the ground's height there, looked up once
            public Vector3 lastWorld;
            public float lastTime;
            public bool paved, dirty;
            public float spacing = 0.8f;                                 // metres between its points
            public float layer;                                          // a few millimetres more lift for each scar, so that two over one another do not flicker
            public int seed;
        }

        static readonly List<Furrow> furrows = new List<Furrow>();

        // (Since 0.3.0: fuel spilt on the ground from a split tank, see Spill)
        sealed class Stain
        {
            public object key;
            public GameObject go;
            public Mesh mesh;
            public Vector3 at, up;                                       // in the root's axes
            public float kg, burnt, drawn;                               // spilt, burnt away, and the radius its mesh was laid out for
            public bool lit, paved;
            public int id;
        }
        static readonly List<Stain> stains = new List<Stain>();
        static Material stainPaint;
        static int stainIds = 900000;
        static readonly List<GameObject> craters = new List<GameObject>();
        static readonly Dictionary<GameObject, float> craterShallow = new Dictionary<GameObject, float>();     // (since 0.3.0: for saving)
        static GameObject root;
        static CelestialBody body;
        static double lat, lon, alt;
        static Quaternion turned;              // the root's turn against the body's own, kept as the body turns
        static Material earth, scrape;
        static readonly Dictionary<object, Furrow> open = new Dictionary<object, Furrow>();
        static float dirtyAt;

        // A scar on soil: a point every 0.4 m along it, and across it, in half-widths of the gouge itself: the earth thrown out on the grass
        // beyond the lip (crumbs, thinning away: the texture is cut out there), the lip's foot, its crest, the wall, the edge of the floor,
        // the floor. (Until 0.2.1 a point every 0.8 m and lips up to 0.3 m high: from a few metres they were broad smooth facets, "like
        // folded sheeting".) The texture runs from 0 to 1 across all of it: the floor is its middle half.
        const float Fine = 0.4f, Coarse = 0.8f;
        static readonly float[] across = { -2f, -1.44f, -1.27f, -1.12f, -1f, -0.5f, 0f, 0.5f, 1f, 1.12f, 1.27f, 1.44f, 2f };
        static readonly float[] rise = { 0f, 0.3f, 1f, 0.55f, 0f, 0f, 0f, 0f, 0f, 0.55f, 1f, 0.3f, 0f };          // of the lip's height
        static readonly float[] texU = { 0f, 0.14f, 0.1825f, 0.22f, 0.25f, 0.375f, 0.5f, 0.625f, 0.75f, 0.78f, 0.8175f, 0.86f, 1f };
        // A scrape on paving: a flat band, a point every 0.8 m along it and eleven across.
        static readonly float[] acrossScrape = { -1.75f, -1.3f, -1.08f, -0.9f, -0.55f, 0f, 0.55f, 0.9f, 1.08f, 1.3f, 1.75f };

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
                // (the earth of this place, if it is another's that the marks were last drawn in: none is left that is drawn with it)
                Recolour();
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
            Vector3 flat = Vector3.ProjectOnPlane(along, up);
            if (flat.sqrMagnitude < 1e-4f) return;
            flat.Normalize();
            float now = Time.time;
            bool begin = !open.TryGetValue(key, out Furrow f) || f == null || now - f.lastTime > 0.6f || f.paved != paved;
            if (!begin)
            {
                // Where the wreck touches now, from the scar's end: how far on along its way, and how far to one side. The middle of what
                // touches jumps about as a wreck's parts touch in turn, the length of the wreck and more: a place behind the scar's end is
                // ground already marked (a scar laid back along itself lay folded, one fold over another); one far ahead or far aside is
                // a new scar (the wreck left the ground and came down again).
                Vector3 step = Vector3.ProjectOnPlane(ground - f.lastWorld, up);
                float on = Vector3.Dot(step, flat), aside = (step - flat * on).magnitude;
                if (on > 8f || aside > 6f) begin = true;
                else
                {
                    f.lastTime = now;
                    if (on < f.spacing) return;
                }
            }
            if (begin)
            {
                f = new Furrow { key = key, paved = paved, spacing = paved ? Coarse : Fine, layer = 0.004f * (furrows.Count % 8), seed = Random.Range(1, 1000) };
                f.lastWorld = ground - flat * f.spacing;
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
            if (f.at.Count >= (f.paved ? 500 : 1000)) return;
            // (the place that touches jumps from side to side as a wreck rocks and tumbles: the mark follows it only part of the way, or it
            // runs in zigzags)
            if (f.at.Count > 0)
            {
                Vector3 sideways = Vector3.ProjectOnPlane(ground - f.lastWorld, up);
                sideways -= flat * Vector3.Dot(sideways, flat);
                ground -= sideways * 0.6f;
            }
            Transform rt = root.transform;
            float wide = Mathf.Clamp(width, paved ? 0.15f : 0.25f, 9f), deep = paved ? 0f : Mathf.Clamp(bite * 0.9f + 0.04f, 0.04f, 0.6f);
            float wideWas = f.width.Count > 0 ? f.width[f.width.Count - 1] : wide, deepWas = f.deep.Count > 0 ? f.deep[f.deep.Count - 1] : deep;
            // (a fast wreck goes a metre and more in a step of the physics: a scar on soil is given its points every 0.4 m all the same,
            // the ones between laid along the straight line, so that its lips are as lumpy behind a fast wreck as behind a slow one)
            float length = (ground - f.lastWorld).magnitude;
            int pieces = f.paved || f.at.Count == 0 ? 1 : Mathf.Clamp(Mathf.FloorToInt(length / f.spacing), 1, 6);
            Vector3 from = f.lastWorld, upLocal = rt.InverseTransformDirection(up), sideLocal = rt.InverseTransformDirection(Vector3.Cross(up, flat).normalized);
            for (int k = 1; k <= pieces && f.at.Count < 1000; k++)
            {
                float t = k / (float)pieces;
                Vector3 here = Vector3.Lerp(from, ground, t);
                float w = Mathf.Lerp(wideWas, wide, t), gone = (here - f.lastWorld).magnitude;
                f.at.Add(rt.InverseTransformPoint(here));
                f.up.Add(upLocal);
                f.side.Add(sideLocal);
                f.width.Add(w);
                f.deep.Add(Mathf.Lerp(deepWas, deep, t));
                f.run.Add(f.run.Count == 0 ? 0f : f.run[f.run.Count - 1] + gone);
                // (the earth's picture is as wide on the ground as twice the gouge: it is run along the scar at the same scale, whatever the
                // scar's width there, so that its lumps are as long as they are broad)
                f.turns.Add(f.turns.Count == 0 ? Random.value : f.turns[f.turns.Count - 1] + gone / Mathf.Clamp(2f * w, 1.2f, 7f));
                f.lift.Add(null);
                f.lastWorld = here;
            }
            f.dirty = true;
            if (dirtyAt <= 0f) dirtyAt = Time.unscaledTime + 0.1f;
        }

        /// <summary>
        /// (Since 0.3.0.) Fuel run out of a split tank onto the ground below its hole: a stain that spreads as more comes. A liquid let
        /// out onto a flat floor spreads to a film a few millimetres deep (kerosene on concrete some three: a hundred kilograms, 125
        /// litres, covers some forty square metres); soil soaks it up as it spreads, so it covers less, the soaked earth as dark. It is
        /// drawn as the scrapes are, by the game's own decal shader, darkening the ground under it (wet concrete and soaked earth both go
        /// dark), its edge irregular as a spill's runs are. Where the leak is alight the pool burns (see Crashes.Pools: a pool fire
        /// burns away some 0.055 kg of kerosene a square metre a second, with a flame as tall as Heskestad's correlation has it, about
        /// as tall as a big pool is wide and two and a half times a small one's width), and what is left of it, burnt out, stays darker
        /// still.
        /// </summary>
        public static void Spill(object key, Vector3 at, Vector3 up, float kg, bool lit, bool paved)
        {
            if (key == null || kg <= 0f) return;
            if (!Under(at, up, out Vector3 ground, out Vector3 normal)) return;
            Anchor(ground);
            if (root == null) return;
            Transform rt = root.transform;
            Vector3 here = rt.InverseTransformPoint(ground);
            Stain st = null;
            foreach (Stain one in stains) if (one.key == key || (one.at - here).sqrMagnitude < 4f) { st = one; break; }
            if (st == null)
            {
                st = new Stain { key = key, at = here, up = rt.InverseTransformDirection(normal), paved = paved, id = stainIds++ };
                st.go = new GameObject("Fragmentation fuel stain") { layer = 15 };
                st.go.transform.SetParent(rt, false);
                st.go.AddComponent<MeshFilter>().sharedMesh = st.mesh = new Mesh { name = "Fragmentation fuel stain" };
                MeshRenderer r = st.go.AddComponent<MeshRenderer>();
                r.sharedMaterial = StainPaint();
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = true;
                stains.Add(st);
                if (stains.Count > 12) { Stain old = stains[0]; stains.RemoveAt(0); if (old.go != null) Object.Destroy(old.go); if (old.mesh != null) Object.Destroy(old.mesh); }
            }
            st.kg += kg;
            st.lit |= lit;
            float r0 = StainRadius(st);
            if (r0 > st.drawn * 1.1f + 0.05f) LayStain(st, r0);
        }

        /// <summary>How wide a stain has spread, by what was spilt (a film 3 mm deep on paving; soaking into soil, as if 8 mm), at most 14 m.</summary>
        static float StainRadius(Stain st) => Mathf.Min(14f, Mathf.Sqrt(st.kg / 800f / (st.paved ? 0.003f : 0.008f) / Mathf.PI));

        /// <summary>The stain's mesh out to so wide: rings of points set on the ground beneath each, a hair over it.</summary>
        static void LayStain(Stain st, float radius)
        {
            st.drawn = radius;
            Transform rt = root.transform;
            Vector3 up = st.up.normalized, side = Vector3.Cross(up, Mathf.Abs(up.y) < 0.9f ? Vector3.up : Vector3.forward).normalized, fore = Vector3.Cross(side, up);
            const int Rings = 6, Round = 28;
            var points = new List<Vector3>(Rings * Round + 1);
            var uv = new List<Vector2>(points.Capacity);
            var tris = new List<int>(Rings * Round * 6);
            Vector3 worldUp = rt.TransformDirection(up);
            for (int i = 0; i <= Rings; i++)
            {
                float rr = radius * i / Rings;
                int count = i == 0 ? 1 : Round;
                for (int j = 0; j < count; j++)
                {
                    float a = j * 2f * Mathf.PI / Round;
                    Vector3 local = st.at + (side * Mathf.Cos(a) + fore * Mathf.Sin(a)) * rr;
                    Vector3 w = rt.TransformPoint(local);
                    if (Under(w, worldUp, out Vector3 g, out Vector3 _)) local = rt.InverseTransformPoint(g);
                    points.Add(local + up * 0.015f);
                    uv.Add(new Vector2(0.5f + 0.5f * Mathf.Cos(a) * i / Rings, 0.5f + 0.5f * Mathf.Sin(a) * i / Rings));
                }
            }
            for (int j = 0; j < Round; j++) { tris.Add(0); tris.Add(1 + (j + 1) % Round); tris.Add(1 + j); }
            for (int i = 1; i < Rings; i++)
                for (int j = 0; j < Round; j++)
                {
                    int a = 1 + (i - 1) * Round + j, b = 1 + (i - 1) * Round + (j + 1) % Round, c = a + Round, d = b + Round;
                    tris.Add(a); tris.Add(b); tris.Add(d);
                    tris.Add(a); tris.Add(d); tris.Add(c);
                }
            st.mesh.Clear();
            st.mesh.SetVertices(points);
            st.mesh.SetUVs(0, uv);
            st.mesh.SetTriangles(tris, 0);
            st.mesh.RecalculateNormals();
            st.mesh.RecalculateBounds();
        }

        /// <summary>
        /// A spill's picture: dark in the middle, the dark not quite even (the liquid thicker in the low places), its edge running out in
        /// lobes and fingers as a spill on a floor does, and the last few centimetres of it a little darker (where a film's edge holds).
        /// Multiplied into the ground by the decal shader, as the scrapes are.
        /// </summary>
        static Material StainPaint()
        {
            if (stainPaint != null) return stainPaint;
            const int S = 128;
            var tex = new Texture2D(S, S, TextureFormat.RGB24, true) { name = "Fragmentation fuel stain", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
            var px = new Color[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float u = (x + 0.5f) / S * 2f - 1f, v = (y + 0.5f) / S * 2f - 1f, r = Mathf.Sqrt(u * u + v * v), a = Mathf.Atan2(v, u);
                    // (the edge: in and out by lobes round it, and fingers on those)
                    float edge = 0.78f + 0.11f * Noise(Mathf.Cos(a) * 2.1f + 3f, Mathf.Sin(a) * 2.1f + 3f, 21) + 0.06f * Noise(Mathf.Cos(a) * 6f + 9f, Mathf.Sin(a) * 6f + 9f, 22);
                    float inside = 1f - Mathf.SmoothStep(edge - 0.05f, edge + 0.02f, r);
                    float rim = Mathf.Exp(-Mathf.Pow((r - edge + 0.04f) / 0.03f, 2f));
                    float uneven = 0.5f + 0.5f * Noise(u * 3f + 1f, v * 3f + 1f, 23);
                    float d = Mathf.Clamp01(inside * (0.5f + 0.12f * uneven) + 0.12f * rim * inside);
                    px[y * S + x] = new Color(1f - 0.92f * d, 1f - 0.92f * d, 1f - 0.88f * d);
                }
            tex.SetPixels(px); tex.Apply(true, true);
            Shader decal = Shader.Find("KSP/Scenery/Decal/Multiply");
            if (decal != null && decal.isSupported) stainPaint = new Material(decal) { name = "Fragmentation fuel stain", mainTexture = tex, renderQueue = 2456 };
            else stainPaint = new Material(Shader.Find("KSP/Diffuse") ?? Shader.Find("Diffuse")) { name = "Fragmentation fuel stain", mainTexture = tex };
            return stainPaint;
        }

        /// <summary>
        /// The stains alight, one at a time (an index from nought; false past the last): where (in the world), which way is up, how
        /// wide, its own number for the explosions mod, and burning so much (kilograms) away this step, as a pool fire burns: about
        /// 0.055 kg a square metre a second. A stain burnt out goes out. And what it lies on, for its flame to be worked out with
        /// (the marks' own holder, which stays with the ground).
        /// </summary>
        public static bool Burning(int k, float dt, out Vector3 at, out Vector3 up, out float radius, out int id, out float left, out Transform lies)
        {
            at = up = Vector3.zero; radius = 0f; id = 0; left = 0f; lies = null;
            int seen = 0;
            foreach (Stain st in stains)
            {
                if (!st.lit || st.kg - st.burnt <= 0.5f || root == null) continue;
                if (seen++ != k) continue;
                radius = st.drawn;
                st.burnt = Mathf.Min(st.kg, st.burnt + 0.055f * Mathf.PI * radius * radius * dt);
                left = st.kg - st.burnt;
                at = root.transform.TransformPoint(st.at);
                up = root.transform.TransformDirection(st.up);
                id = st.id;
                lies = root.transform;
                return true;
            }
            return false;
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
            craterShallow[go] = shallow;
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
            // (since 0.3.0: clods of earth or of packed snow; sand, dust and ice go up as dust, not lumps; turf only where there is turf)
            Ground.Feel feel = Ground.FeelOf(KindHere());
            if (!feel.Clods) return;
            int n = Mathf.Clamp(Mathf.RoundToInt(R * 14f), 5, 60);
            float speed = Mathf.Sqrt(9.81f * R) * 1.6f;
            Bits.Emit(ground + up * 0.3f, velocity * 0.25f + up * speed * 0.7f, speed * 0.8f, n, 0.03f, Mathf.Clamp(R * 0.08f, 0.06f, 0.25f), Bits.Kind.Clod, 0f, 0f, speed * 0.5f);
            if (feel.Turf) Bits.Emit(ground + up * 0.3f, velocity * 0.2f + up * speed * 0.5f, speed * 0.7f, Mathf.Max(2, n / 3), 0.06f, Mathf.Clamp(R * 0.12f, 0.1f, 0.3f), Bits.Kind.Turf, 0f, 0f, speed * 0.4f);
        }

        /// <summary>A crater a unit across (scaled to its size): its floor, its raised rim, and the earth thrown out round it thinning away; each ring set on the ground there.</summary>
        static Mesh Bowl(Vector3 centre, Quaternion toWorld, float R, float shallow)
        {
            Vector3 up = toWorld * Vector3.up;
            const int around = 18;
            float[] ring = { 0f, 0.55f, 0.85f, 1f, 1.25f, 1.9f };
            float[] height = { 0.03f, 0.03f, 0.08f, 0.13f, 0.06f, 0f };    // of the radius (the rim kept low: a crater from a crash is mostly a scar)
            float[] u = { 0.5f, 0.4f, 0.27f, 0.19f, 0.13f, 0f };
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

        /// <summary>A mark's mesh, as far as it has got: a scar on soil (see BuildFine), or a scrape on paving, a flat band laid on the paving beneath it.</summary>
        static void Build(Furrow f)
        {
            f.dirty = false;
            int n = f.at.Count;
            if (n < 2 || root == null) return;
            if (!f.paved) { BuildFine(f); return; }
            float[] across = acrossScrape;
            int cols = across.Length;
            // (one set of lists for every mark, made again ten times a second while a wreck slides)
            List<Vector3> v = buildV; List<Vector2> uv = buildUV; List<int> tris = buildTris;
            v.Clear(); uv.Clear(); tris.Clear();
            Transform rt = root.transform;
            float[] wide = Graded(f);
            for (int i = 0; i < n; i++)
            {
                Vector3 c = f.at[i], s = f.side[i], u = f.up[i];
                float run = f.run[i];
                // (not quite a ruled band: a little wider and narrower as it goes; but not torn at its edges: it is metal dragged over a hard flat face)
                float half = wide[i] * 0.5f * (1f + 0.05f * Noise(run * 0.31f, 0.5f, f.seed));
                float[] lifts = f.lift[i];
                bool look = lifts == null;
                if (look) f.lift[i] = lifts = new float[cols];
                for (int k = 0; k < cols; k++)
                {
                    Vector3 p = c + s * (across[k] * half);
                    if (look)
                    {
                        Vector3 world = rt.TransformPoint(p), up = rt.TransformDirection(u);
                        lifts[k] = Physics.Raycast(world + up * 3f, -up, out RaycastHit hit, 8f, 1 << 15, QueryTriggerInteraction.Ignore) ? Vector3.Dot(hit.point - world, up) : 0f;
                    }
                    v.Add(p + u * (lifts[k] + 0.015f + f.layer));
                    uv.Add(new Vector2(k / (float)(cols - 1), run / 2.5f));
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

        static readonly float[] endLifts = new float[across.Length];
        static float[] graded = new float[64];

        /// <summary>
        /// A mark's widths along it, graded: never more than so much wider or narrower a metre on than a metre back (a flare of some
        /// thirty-five degrees each side), and never wider than the wreck made it. Where a wreck turned as it stopped and laid its broad
        /// side into the earth, the scar went from one metre wide to three between one point and the next, with an edge straight across
        /// it; dug earth and dragged metal widen over a metre or two.
        /// </summary>
        static float[] Graded(Furrow f)
        {
            int n = f.width.Count;
            if (graded.Length < n) graded = new float[Mathf.NextPowerOfTwo(n)];
            const float flare = 1.4f;                     // (metres of width a metre along: 0.7 m each side)
            for (int i = 0; i < n; i++) graded[i] = i == 0 ? f.width[0] : Mathf.Min(f.width[i], graded[i - 1] + flare * Mathf.Abs(f.run[i] - f.run[i - 1]));
            for (int i = n - 2; i >= 0; i--) graded[i] = Mathf.Min(graded[i], graded[i + 1] + flare * Mathf.Abs(f.run[i + 1] - f.run[i]));
            return graded;
        }

        static void BuildFine(Furrow f)
        {
            int n = f.at.Count, cols = across.Length;
            List<Vector3> v = buildV; List<Vector2> uv = buildUV; List<int> tris = buildTris;
            v.Clear(); uv.Clear(); tris.Clear();
            Transform rt = root.transform;
            float[] wide = Graded(f);
            for (int i = 0; i < n; i++)
            {
                Vector3 c = f.at[i], s = f.side[i], u = f.up[i];
                float run = f.run[i];
                // (it begins and ends in a point, over a metre and a half)
                float ends = Mathf.Clamp01(Mathf.Min(i, n - 1 - i) / 4f);
                float half = wide[i] * 0.5f * (1f + 0.16f * Noise(run * 0.33f, 0.5f, f.seed) + 0.07f * Noise(run * 1.1f, 8.5f, f.seed)) * (0.25f + 0.75f * ends);
                c += s * (0.07f * half * Noise(run * 0.19f, 1.5f, f.seed));
                // (the lips: a few centimetres, a quarter as high as the scar is deep, nine centimetres at most; lumps half a metre long, gone
                // altogether here and there, each side its own. The rest of the earth is thrown: clods, slabs of turf, see Crashes.Slide)
                float lip = Mathf.Clamp(0.22f * f.deep[i] + 0.015f, 0.015f, 0.09f);
                float lipLeft = lip * Mathf.Clamp01(0.55f + 0.9f * Noise(run * 1.9f, 2.5f, f.seed)), lipRight = lip * Mathf.Clamp01(0.55f + 0.9f * Noise(run * 1.9f, 5.5f, f.seed));
                // (the ground's height under each point, looked up once and kept; but not while the point is still within the scar's
                // narrowing end, where it moves outward as the scar grows on: those are looked up afresh each time)
                float[] lifts = f.lift[i];
                bool look = lifts == null;
                if (look)
                {
                    if (i < n - 4) f.lift[i] = lifts = new float[cols];
                    else lifts = endLifts;
                }
                for (int k = 0; k < cols; k++)
                {
                    float a = across[k];
                    // (the lip's crest and foot in and out a little, each side its own way: not a ruled line)
                    if (k == 1 || k == cols - 2) a *= 1f + 0.1f * Noise(run * 1.5f, k == 1 ? 6.5f : 7.5f, f.seed);
                    else if (k == 2 || k == cols - 3) a *= 1f + 0.06f * Noise(run * 2.3f, k == 2 ? 3.5f : 4.5f, f.seed);
                    Vector3 p = c + s * (a * half);
                    if (look)
                    {
                        Vector3 world = rt.TransformPoint(p), up = rt.TransformDirection(u);
                        lifts[k] = Physics.Raycast(world + up * 3f, -up, out RaycastHit hit, 8f, 1 << 15, QueryTriggerInteraction.Ignore) ? Vector3.Dot(hit.point - world, up) : 0f;
                    }
                    p += u * lifts[k];
                    v.Add(p + u * (0.025f + f.layer + (k < cols / 2 ? lipLeft : lipRight) * rise[k] * ends));
                    uv.Add(new Vector2(texU[k], f.turns[i]));
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
            Recolour();
            Earth();
            Scrape();
        }

        /// <summary>The materials the marks are drawn with (made if they are not yet).</summary>
        public static Material[] Materials() => new[] { Earth(), Scrape() };

#if DEV
        /// <summary>(development build) A scar laid without a crash, to look at: from a place, so far along a way, so wide and so deep.</summary>
        public static string Lay(Vector3 from, Vector3 way, Vector3 up, float length, float width, float deep, bool paved)
        {
            object key = new object();
            int before = furrows.Count;
            for (float d = 0f; d <= length; d += 0.2f)
            {
                // (as a wreck's does: not quite straight, deepening over the first metres, a little wider and narrower)
                Vector3 at = from + way * d + Vector3.Cross(up, way).normalized * (0.5f * Mathf.Sin(d * 0.11f));
                Track(key, at, up, way, 30f, deep * Mathf.Clamp01(d / 6f), width * (1f + 0.2f * Mathf.Sin(d * 0.23f)), paved);
            }
            open.Remove(key);
            return (furrows.Count - before) + " laid, " + (furrows.Count > 0 ? furrows[furrows.Count - 1].at.Count : 0) + " points";
        }
#endif

        static Color earthOf;                  // the colour of the soil the earth's picture was made in

        /// <summary>
        /// The earth's picture is in the colour of the soil of one place: where that is no longer the place (another world, another kind of
        /// country), it is made again in the new one's. Called where nothing is drawn with it (a flight's start, the first mark of a new place).
        /// </summary>
        static void Recolour()
        {
            if (earth == null || Soil() == earthOf) return;
            Texture main = earth.mainTexture, bumps = earth.HasProperty("_BumpMap") ? earth.GetTexture("_BumpMap") : null;
            Object.Destroy(earth);
            if (main != null) Object.Destroy(main);
            if (bumps != null) Object.Destroy(bumps);
            earth = null;
        }

        /// <summary>
        /// Torn earth, across a scar (u: the gouge's floor is the middle half) and along it (v, a turn as long as the picture is wide on the
        /// ground: twice the gouge's width). The floor: moist earth,
        /// darker than the ground's top, scored the way the wreck went, darkest under its edges where a trench's wall would be in shade. The
        /// lips: crumbled earth, lumps of it, a sod turned over here and there. Beyond them the picture is cut out: clods and crumbs thrown on
        /// the grass, fewer the further out, with the ground showing between them, so that the scar has no edge that a ruler could have drawn.
        /// Coloured by the earth of the place (see Soil).
        /// </summary>
        static Material Earth()
        {
            if (earth != null) return earth;
            Color soil = Soil(), grass = GrassColour();
            earthOf = soil;
            // (clods are moist earth from below the turf: a little darker than the ground's dry top, the colour of the scar's lips. At seven
            // tenths of the soil's colour they were black specks on the grass from fifteen metres)
            Bits.Earth(new Color(soil.r * 0.8f, soil.g * 0.76f, soil.b * 0.7f), Color.Lerp(grass, soil, 0.25f));
            Shader cut = Shader.Find("KSP/Alpha/Cutoff Bumped") ?? Shader.Find("KSP/Alpha/Cutoff");
            const int S = 256;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, true) { name = "Fragmentation earth", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            var bump = new Texture2D(S, S, TextureFormat.RGBA32, true, true) { name = "Fragmentation earth bumps", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear };
            var h = new float[S * S];
            var px = new Color[S * S];
            // (the lips' crumbled earth a little lighter than the floor's, no more: at nine tenths of the soil's own colour they were pale
            // borders either side of a dark band, and the scar read as a track with kerbs)
            Color moist = new Color(soil.r * 0.62f, soil.g * 0.58f, soil.b * 0.55f), crumbled = new Color(soil.r * 0.76f, soil.g * 0.72f, soil.b * 0.68f);
            // (since 0.3.0, by what the ground is: dry sand turned over is nearly as pale as its top, a little darker where it is
            // damp underneath; snow packed down in a trench is blue-grey in its own shade; the Moon's dust a little darker beneath its
            // sunlit top; ice scored white)
            switch (KindHere())
            {
                case Ground.Kind.Sand: moist = soil * 0.84f; crumbled = soil * 0.95f; break;
                case Ground.Kind.Snow: moist = new Color(soil.r * 0.7f, soil.g * 0.76f, soil.b * 0.86f); crumbled = soil * 0.93f; break;
                case Ground.Kind.Regolith: moist = soil * 0.72f; crumbled = soil * 0.86f; break;
                case Ground.Kind.Ice: moist = soil * 0.92f; crumbled = soil * 1.05f; break;
            }
            Color sod = Color.Lerp(grass, soil, 0.45f) * 0.8f;
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float u = x / (float)S, side = u < 0.5f ? 0f : 9f;
                    // (how far out from the middle, 0 to 1; the borders between floor, lip and scatter wander along the scar, each side its own way)
                    float wander = 0.035f * Tiled(side + 1.5f, y * 0.035f, S * 0.035f, 11) + 0.02f * Tiled(side + 4.5f, y * 0.11f, S * 0.11f, 12) + 0.012f * Tiled(x * 0.2f, y * 0.2f, S * 0.2f, 13);
                    float d = Mathf.Abs(u - 0.5f) * 2f + wander;
                    float lump = 0.6f * Tiled(x * 0.06f, y * 0.06f, S * 0.06f, 1) + 0.4f * Tiled(x * 0.16f, y * 0.16f, S * 0.16f, 2);       // lumps a hand and more across
                    // (crumbs and grains some texels across: nearer one texel each, they showed as little squares from four metres)
                    float grain = Tiled(x * 0.3f, y * 0.3f, S * 0.3f, 3);
                    float fine = Tiled(x * 0.55f, y * 0.55f, S * 0.55f, 6);
                    float streak = 0.6f * Tiled(x * 0.5f, y * 0.022f, S * 0.022f, 4) + 0.4f * Tiled(x * 1.1f, y * 0.05f, S * 0.05f, 7);     // long along, narrow across: scored
                    // (the floor's own edge in and out with the lumps of the lip that have fallen in over it)
                    float df = d + 0.045f * lump + 0.015f * grain;
                    float floor = 1f - Mathf.Clamp01((df - 0.5f) * 14f);                      // the floor of the gouge
                    float wall = Mathf.Clamp01(1f - Mathf.Abs(df - 0.5f) * 13f);              // in shade under its edge
                    float lipZone = Mathf.Clamp01((df - 0.5f) * 14f) * (1f - Mathf.Clamp01((d - 0.72f) * 12f));
                    float outer = Mathf.Clamp01((d - 0.7f) / 0.3f);                           // 0 at the lip's foot, 1 at the edge of the picture
                    // ---- colour
                    Color c = Color.Lerp(crumbled * (0.8f + 0.22f * lump + 0.1f * grain + 0.06f * fine), moist * (0.86f + 0.16f * streak + 0.05f * grain + 0.04f * fine + 0.14f * lump), floor);
                    c *= 1f - 0.4f * wall;
                    // (a sod turned over in the lip here and there: grass and roots showing through the earth)
                    float chunk = Mathf.Clamp01((Tiled(x * 0.09f, y * 0.09f, S * 0.09f, 5) - 0.45f) * 5f) * lipZone;
                    c = Color.Lerp(c, sod * (0.85f + 0.25f * grain), 0.7f * chunk);
                    // ---- cut out beyond the lip: clods (lumps' tops) and crumbs (grains' tops), fewer the further out; nothing at the very edge
                    float alpha = 1f;
                    if (outer > 0f)
                    {
                        // (ragged at every size: lumps, crumbs and grains together, against a bar that rises outward. Cut out by the lumps
                        // alone, the edge was smooth blobs a foot across, like spilt paint from four metres)
                        float keep = (0.45f * lump + 0.35f * grain + 0.2f * fine - Mathf.Lerp(-0.35f, 0.6f, Mathf.Pow(outer, 0.6f))) * 6f;
                        if (outer > 0.93f) keep = -1f;
                        alpha = Mathf.Clamp01(0.5f + keep);
                        c = Color.Lerp(sod, c, Mathf.Clamp01(0.5f + keep * 2f));
                    }
                    c.a = alpha;
                    px[y * S + x] = c;
                    // (the bumps from the lumps and the scoring, little from the crumbs: the crumbs are a few texels each, and with their
                    // bumps as strong as the lumps' the floor was lit in little squares, like brickwork, from four metres)
                    h[y * S + x] = floor * (0.3f * streak + 0.06f * grain + 0.25f * lump) + (1f - floor) * (1f * lump + 0.15f * grain + 0.05f * fine) + 0.5f * chunk - 0.5f * wall;
                }
            var nm = new Color[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float dx = h[y * S + (x + 1) % S] - h[y * S + (x + S - 1) % S];
                    float dy = h[((y + 1) % S) * S + x] - h[((y + S - 1) % S) * S + x];
                    Vector3 nv = new Vector3(-dx * 2.4f, -dy * 2.4f, 1f).normalized;
                    // (the game's bump maps are read as a normal map packed in the alpha and green channels)
                    nm[y * S + x] = new Color(nv.y * 0.5f + 0.5f, nv.y * 0.5f + 0.5f, nv.y * 0.5f + 0.5f, nv.x * 0.5f + 0.5f);
                }
            tex.SetPixels(px); tex.Apply(true, true);
            bump.SetPixels(nm); bump.Apply(true, true);
            // (the game's own cut-out shader, as its ladders and trusses are drawn; without it the scatter beyond the lips is drawn solid, in
            // the turf's colour)
            Shader s = cut ?? Shader.Find("KSP/Bumped") ?? Shader.Find("KSP/Diffuse") ?? Shader.Find("Diffuse");
            earth = new Material(s) { name = "Fragmentation earth", mainTexture = tex };
            if (earth.HasProperty("_BumpMap")) earth.SetTexture("_BumpMap", bump);
            if (earth.HasProperty("_Cutoff")) earth.SetFloat("_Cutoff", 0.5f);
            return earth;
        }

        /// <summary>
        /// A scrape on paving: torn metal and paint dragged over concrete leave dark smears and long streaks along the way the wreck
        /// went, thickest down the middle and fraying out at the sides, with the paving's own grain and markings showing through.
        /// Drawn as the game's own marks on the space centre's ground are, by multiplying what is there (its decal shader,
        /// KSP/Scenery/Decal/Multiply): white is the paving as it was, and as it is lit, by day or by night. (Until 0.3.0 an opaque
        /// dark band: from close by a flat black sheet with hard edges; and where a wreck turned as it stopped, a black bow tie.)
        /// </summary>
        static Material Scrape()
        {
            if (scrape != null) return scrape;
            Shader decal = Shader.Find("KSP/Scenery/Decal/Multiply");
            if (decal == null || !decal.isSupported)
            {
                // (the old way, where the game's decal shader is not to be had)
                const int S = 64;
                var flat = new Texture2D(S, S, TextureFormat.RGB24, true) { name = "Fragmentation scrape", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear };
                var fp = new Color[S * S];
                for (int y = 0; y < S; y++)
                    for (int x = 0; x < S; x++)
                    {
                        float streak = 0.5f + 0.5f * Tiled(x * 0.7f, y * 0.04f, S * 0.04f, 5);
                        float edge = Mathf.Abs(x / (float)S - 0.5f) * 2f;
                        float k = Mathf.Lerp(0.16f, 0.36f, Mathf.Clamp01(streak * 0.7f + edge * 0.6f));
                        fp[y * S + x] = new Color(k, k * 0.98f, k * 0.95f);
                    }
                flat.SetPixels(fp); flat.Apply(true, true);
                Shader s = Shader.Find("KSP/Diffuse") ?? Shader.Find("Diffuse");
                scrape = new Material(s) { name = "Fragmentation scrape", mainTexture = flat };
                return scrape;
            }
            // Across the band (its 3.5 half-widths: see acrossScrape) and 2.5 m along it, repeating along.
            const int W = 128, H = 256;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, true) { name = "Fragmentation scrape", filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            tex.wrapModeU = TextureWrapMode.Clamp;
            tex.wrapModeV = TextureWrapMode.Repeat;
            var px = new Color[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float u = (x + 0.5f) / W, v = (y + 0.5f) / H;
                    float a = Mathf.Abs(u - 0.5f) * 3.5f;
                    // (its edge frays, in and out by a fifth of a half-width over some tens of centimetres along, and finer)
                    float fray = 0.22f * Tiled(u * 9f, v * 24f, 24f, 11) + 0.1f * Tiled(u * 23f, v * 70f, 70f, 12);
                    float core = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.7f, 1.3f, a + fray));
                    // Long streaks along the way it went: many thin lines across, each running on for tens of centimetres to metres;
                    // finer scoring among them; and smears where it pressed harder.
                    float lines = 0.5f + 0.5f * Tiled(u * 70f, v * 3f, 3f, 13);
                    float fine = 0.5f + 0.5f * Tiled(u * 160f, v * 8f, 8f, 14);
                    float smear = 0.5f + 0.5f * Tiled(u * 5f, v * 5f, 5f, 15);
                    float d = Mathf.Clamp(core * (0.12f + 0.42f * Mathf.SmoothStep(0.35f, 0.8f, lines) + 0.16f * fine + 0.28f * smear), 0f, 0.85f);
                    // (black with a little brown in it: rubber, paint and scorched aluminium)
                    px[y * W + x] = new Color(1f - 0.8f * d, 1f - 0.82f * d, 1f - 0.86f * d);
                }
            tex.SetPixels(px); tex.Apply(true, true);
            // (drawn after everything solid and before anything see-through: smoke over a scrape is drawn over it)
            scrape = new Material(decal) { name = "Fragmentation scrape", mainTexture = tex, renderQueue = 2455 };
            return scrape;
        }

        /// <summary>What the ground of the place is (Ground.KindAt; the development build's test kind where one is set).</summary>
        static Ground.Kind KindHere()
        {
            CelestialBody b = body ?? FlightGlobals.currentMainBody;
            return Crashes.TestGround >= 0 ? (Ground.Kind)Crashes.TestGround : b == null ? Ground.Kind.Soil : Ground.KindAt(b, lat, lon);
        }

        /// <summary>The colour of the earth of the place (as the ground looks on the screen): by what the ground is there (Ground.KindAt) and the world.</summary>
        static Color Soil()
        {
            CelestialBody b = body ?? FlightGlobals.currentMainBody;
            if (b == null) return new Color(0.36f, 0.29f, 0.2f);
            Ground.Kind kind = KindHere();
            switch (kind)
            {
                case Ground.Kind.Snow: return new Color(0.8f, 0.83f, 0.88f);
                case Ground.Kind.Ice: return b.bodyName == "Minmus" ? new Color(0.62f, 0.78f, 0.74f) : new Color(0.7f, 0.78f, 0.84f);
                case Ground.Kind.Rock: return b.bodyName == "Moho" ? new Color(0.36f, 0.3f, 0.27f) : new Color(0.38f, 0.35f, 0.32f);
                case Ground.Kind.Sand:
                    if (b.bodyName == "Duna") return new Color(0.58f, 0.3f, 0.17f);
                    if (b.bodyName == "Laythe") return new Color(0.6f, 0.55f, 0.42f);
                    return new Color(0.55f, 0.46f, 0.32f);
                case Ground.Kind.Regolith:
                    if (b.bodyName == "Minmus") return new Color(0.56f, 0.7f, 0.64f);
                    if (b.bodyName == "Ike" || b.bodyName == "Dres") return new Color(0.42f, 0.4f, 0.38f);
                    return new Color(0.4f, 0.4f, 0.41f);
            }
            switch (b.bodyName)
            {
                case "Kerbin": return new Color(0.4f, 0.31f, 0.21f);
                case "Eve": return new Color(0.38f, 0.26f, 0.48f);
                case "Laythe": return new Color(0.44f, 0.4f, 0.32f);
            }
            return new Color(0.42f, 0.38f, 0.32f);
        }

        /// <summary>The ground's own top round a scar: Kerbin's grass where there is soil, the snow's or the sand's own surface elsewhere.</summary>
        static Color GrassColour()
        {
            CelestialBody b = body ?? FlightGlobals.currentMainBody;
            Ground.Kind kind = KindHere();
            if (kind == Ground.Kind.Snow) return new Color(0.9f, 0.92f, 0.95f);
            if (kind == Ground.Kind.Soil && b != null && b.bodyName == "Kerbin") return new Color(0.36f, 0.43f, 0.17f);
            return Soil() * 1.08f;
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
            craterShallow.Clear();
            foreach (Stain st in stains) { if (st.go != null) Object.Destroy(st.go); if (st.mesh != null) Object.Destroy(st.mesh); }
            stains.Clear();
            bowls.Clear();
            if (root != null) Object.Destroy(root);
            root = null;
            body = null;
        }

        public static int Count => furrows.Count + craters.Count + stains.Count;

        // ------------------------------------------------------------------------------------------------ (since 0.3.0) kept in the save

        static string V(Vector3 v) => v.x.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + " " + v.y.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + " " + v.z.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        static string F(float f) => f.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        static float[] Floats(string s)
        {
            if (string.IsNullOrEmpty(s)) return new float[0];
            string[] w = s.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
            var f = new float[w.Length];
            for (int i = 0; i < w.Length; i++) float.TryParse(w[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out f[i]);
            return f;
        }
        static Vector3 V3(string s) { float[] f = Floats(s); return f.Length >= 3 ? new Vector3(f[0], f[1], f[2]) : Vector3.zero; }

        /// <summary>
        /// (Since 0.3.0.) The marks on the ground written into the save (Scenario: the game's own save, so a quicksave, a scene's
        /// leaving and a quickload keep them): where they are anchored on which world, and each scar's or scrape's points along it
        /// (where, which way across and up, how wide and deep, how far along), each crater's place, turn, size and slant, each
        /// stain's place, what was spilt and burnt, and whether it burns.
        /// </summary>
        public static void Save(ConfigNode node)
        {
            if (root == null || body == null || Count == 0) return;
            ConfigNode m = node.AddNode("MARKS");
            m.AddValue("body", body.bodyName);
            m.AddValue("lat", lat.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            m.AddValue("lon", lon.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            m.AddValue("alt", alt.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            m.AddValue("turned", F(turned.x) + " " + F(turned.y) + " " + F(turned.z) + " " + F(turned.w));
            foreach (Furrow f in furrows)
            {
                if (f.at.Count < 2) continue;
                ConfigNode n = m.AddNode("SCAR");
                n.AddValue("paved", f.paved);
                n.AddValue("spacing", F(f.spacing)); n.AddValue("layer", F(f.layer)); n.AddValue("seed", f.seed);
                var b = new System.Text.StringBuilder();
                for (int i = 0; i < f.at.Count; i++)
                    b.Append(V(f.at[i])).Append(' ').Append(V(f.side[i])).Append(' ').Append(V(f.up[i])).Append(' ').Append(F(f.width[i])).Append(' ').Append(F(f.deep[i])).Append(' ').Append(F(f.run[i])).Append(' ').Append(F(f.turns[i])).Append(i + 1 < f.at.Count ? " " : "");
                n.AddValue("points", b.ToString());
            }
            foreach (GameObject c in craters)
            {
                if (c == null) continue;
                ConfigNode n = m.AddNode("CRATER");
                Quaternion q = c.transform.localRotation;
                n.AddValue("at", V(c.transform.localPosition));
                n.AddValue("turn", F(q.x) + " " + F(q.y) + " " + F(q.z) + " " + F(q.w));
                n.AddValue("size", F(c.transform.localScale.x));
                n.AddValue("shallow", F(craterShallow.TryGetValue(c, out float s) ? s : 0.5f));
            }
            foreach (Stain st in stains)
            {
                ConfigNode n = m.AddNode("STAIN");
                n.AddValue("at", V(st.at)); n.AddValue("up", V(st.up));
                n.AddValue("kg", F(st.kg)); n.AddValue("burnt", F(st.burnt));
                n.AddValue("lit", st.lit); n.AddValue("paved", st.paved);
            }
        }

        static ConfigNode waiting;
        static float waitingSince;

        /// <summary>(Since 0.3.0.) The marks in a save, to be laid again once the ground under them is there (see Restore).</summary>
        public static void Load(ConfigNode node)
        {
            waiting = node != null ? node.GetNode("MARKS") : null;
            waitingSince = Time.unscaledTime;
        }

        /// <summary>
        /// (Since 0.3.0.) Every frame until it is done: the saved marks laid again, once the ground under their anchor is there to set
        /// them on (the world's ground is built over the first seconds of a scene). Given up after a minute, or if the world is another.
        /// </summary>
        public static void Restore()
        {
            if (waiting == null) return;
            if (Time.unscaledTime - waitingSince > 60f) { waiting = null; return; }
            CelestialBody b = FlightGlobals.GetBodyByName(waiting.GetValue("body"));
            if (b == null || b != FlightGlobals.currentMainBody) { if (FlightGlobals.currentMainBody != null) waiting = null; return; }
            double.TryParse(waiting.GetValue("lat"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double la);
            double.TryParse(waiting.GetValue("lon"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double lo);
            double.TryParse(waiting.GetValue("alt"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double al);
            Vector3 where = b.GetWorldSurfacePosition(la, lo, al), up = (where - b.position).normalized;
            // (the ground there yet: a look straight down hits it)
            if (!Physics.Raycast(where + up * 50f, -up, 120f, 1 << 15, QueryTriggerInteraction.Ignore)) return;
            ConfigNode m = waiting;
            waiting = null;
            Clear();
            body = b; lat = la; lon = lo; alt = al;
            float[] q = Floats(m.GetValue("turned"));
            turned = q.Length >= 4 ? new Quaternion(q[0], q[1], q[2], q[3]) : Quaternion.Inverse(b.transform.rotation);
            root = new GameObject("Fragmentation marks") { layer = 15 };
            Recolour();
            root.transform.SetPositionAndRotation(where, b.transform.rotation * turned);
            Transform rt = root.transform;
            foreach (ConfigNode n in m.GetNodes("SCAR"))
            {
                bool paved = n.GetValue("paved") == "True";
                float[] p = Floats(n.GetValue("points"));
                if (p.Length < 26) continue;
                var f = new Furrow { key = new object(), paved = paved, spacing = Floats(n.GetValue("spacing")).Length > 0 ? Floats(n.GetValue("spacing"))[0] : (paved ? Coarse : Fine) };
                float[] l = Floats(n.GetValue("layer")); f.layer = l.Length > 0 ? l[0] : 0f;
                int.TryParse(n.GetValue("seed"), out f.seed);
                for (int i = 0; i + 12 < p.Length; i += 13)
                {
                    f.at.Add(new Vector3(p[i], p[i + 1], p[i + 2])); f.side.Add(new Vector3(p[i + 3], p[i + 4], p[i + 5])); f.up.Add(new Vector3(p[i + 6], p[i + 7], p[i + 8]));
                    f.width.Add(p[i + 9]); f.deep.Add(p[i + 10]); f.run.Add(p[i + 11]); f.turns.Add(p[i + 12]); f.lift.Add(null);
                }
                f.lastWorld = rt.TransformPoint(f.at[f.at.Count - 1]);
                f.lastTime = -100f;
                f.go = new GameObject(paved ? "Fragmentation scrape" : "Fragmentation scar") { layer = 15 };
                f.go.transform.SetParent(rt, false);
                f.go.AddComponent<MeshFilter>().sharedMesh = f.mesh = new Mesh { name = "Fragmentation scar" };
                f.mesh.MarkDynamic();
                MeshRenderer r = f.go.AddComponent<MeshRenderer>();
                r.sharedMaterial = paved ? Scrape() : Earth();
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = true;
                furrows.Add(f);
                f.dirty = true;
            }
            if (furrows.Count > 0) dirtyAt = Time.unscaledTime + 0.1f;
            foreach (ConfigNode n in m.GetNodes("CRATER"))
            {
                float[] tq = Floats(n.GetValue("turn")), sz = Floats(n.GetValue("size")), sh = Floats(n.GetValue("shallow"));
                var go = new GameObject("Fragmentation crater") { layer = 15 };
                go.transform.SetParent(rt, false);
                go.transform.localPosition = V3(n.GetValue("at"));
                go.transform.localRotation = tq.Length >= 4 ? new Quaternion(tq[0], tq[1], tq[2], tq[3]) : Quaternion.identity;
                float R = sz.Length > 0 ? sz[0] : 1f;
                go.transform.localScale = new Vector3(R, R, R);
                go.AddComponent<MeshFilter>();
                float shallow = sh.Length > 0 ? sh[0] : 0.5f;
                bowls.Add(new KeyValuePair<GameObject, float>(go, shallow));
                craterShallow[go] = shallow;
                MeshRenderer r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = Earth();
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = true;
                craters.Add(go);
            }
            foreach (ConfigNode n in m.GetNodes("STAIN"))
            {
                float[] kg = Floats(n.GetValue("kg")), burnt = Floats(n.GetValue("burnt"));
                var st = new Stain { key = new object(), at = V3(n.GetValue("at")), up = V3(n.GetValue("up")), kg = kg.Length > 0 ? kg[0] : 0f, burnt = burnt.Length > 0 ? burnt[0] : 0f, lit = n.GetValue("lit") == "True", paved = n.GetValue("paved") == "True", id = stainIds++ };
                st.go = new GameObject("Fragmentation fuel stain") { layer = 15 };
                st.go.transform.SetParent(rt, false);
                st.go.AddComponent<MeshFilter>().sharedMesh = st.mesh = new Mesh { name = "Fragmentation fuel stain" };
                MeshRenderer r = st.go.AddComponent<MeshRenderer>();
                r.sharedMaterial = StainPaint();
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = true;
                stains.Add(st);
                LayStain(st, StainRadius(st));
            }
            Keystone.Kit.Log("Fragmentation", "the marks on the ground laid again from the save: " + furrows.Count + " scars and scrapes, " + craters.Count + " craters, " + stains.Count + " stains");
        }

        /// <summary>(The stains, in words: for the development build's readouts.)</summary>
        public static string StainsSaid()
        {
            var said = new System.Text.StringBuilder(stains.Count + " stains");
            foreach (Stain st in stains)
                said.Append("; ").Append(st.kg.ToString("F0")).Append(" kg spilt, ").Append((2f * st.drawn).ToString("F1")).Append(" m across").Append(st.paved ? " on paving" : " on soil").Append(st.lit ? ", alight, " + st.burnt.ToString("F0") + " kg burnt" : "");
            return said.ToString();
        }

#if DEV
        /// <summary>(development build) A place so far along the longest scar on soil (or scrape on paving), which way the scar runs there and which way is up: for pointing the camera at it.</summary>
        public static bool Along(bool paved, float share, out Vector3 world, out Vector3 way, out Vector3 up, out string what)
        {
            world = way = up = Vector3.zero; what = "";
            Furrow best = null;
            foreach (Furrow f in furrows)
                if (f.paved == paved && f.at.Count >= 3 && (best == null || f.run[f.run.Count - 1] >= best.run[best.run.Count - 1])) best = f;
            if (best == null || root == null) return false;
            int i = Mathf.Clamp(Mathf.RoundToInt(share * (best.at.Count - 1)), 0, best.at.Count - 2);
            Transform rt = root.transform;
            world = rt.TransformPoint(best.at[i]);
            way = (rt.TransformPoint(best.at[i + 1]) - world).normalized;
            up = rt.TransformDirection(best.up[i]);
            what = (paved ? "the scrape" : "the scar") + ", " + best.at.Count + " points and " + best.run[best.run.Count - 1].ToString("F0") + " m long, here " + best.width[i].ToString("F1") + " m wide and " + best.deep[i].ToString("F2") + " m deep (" + furrows.Count + " scars and scrapes in all)";
            return true;
        }
#endif
    }
}
