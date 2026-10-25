using System.Collections.Generic;
using UnityEngine;

namespace Fragmentation
{
    /// <summary>
    /// One piece of wreckage: a patch of a destroyed part's own skin (see Shatter), solid, with a mass of its own. It
    /// tumbles, falls, hits the ground and other pieces and comes to rest; then it lies still, costing nothing, until
    /// it is taken away.
    ///
    /// It is one of the game's own "physical objects" (as a fairing's panels are once they are cast off), so that the
    /// game moves it with everything else when it shifts the world about under the camera, and changes its speed with
    /// everything else when it changes the frame it reckons speeds in. The game gives such objects their weight only
    /// while the craft being flown is whole and running; when that craft has been destroyed in the crash this does it
    /// instead (see Step). The air's drag is this mod's own: a tumbling plate is held back by its area, not by a drag
    /// figure for a whole craft.
    /// </summary>
    public sealed class Piece : MonoBehaviour
    {
        static readonly List<Piece> all = new List<Piece>();
        /// <summary>Pieces at least this far from middle to edge (metres) cast shadows.</summary>
        public static float ShadowsFrom = 0.6f;
        /// <summary>(development build: at most this many pieces, whatever the setting says; less than nought: the setting)</summary>
        public static int MostOverride = -1;
        public static int Most => MostOverride >= 0 ? MostOverride : Mathf.RoundToInt(Frag.Most);
        static readonly HashSet<Collider> colliders = new HashSet<Collider>();
        static readonly Stack<Piece> pool = new Stack<Piece>();
        static int ids;
        static PhysicMaterial surface;

        public static IList<Piece> All => all;
        public static int Count => all.Count;
        /// <summary>Whether a collider is one of ours (so that what it hits is not counted as a crash: see Hits).</summary>
        public static bool Is(Collider c) => c != null && colliders.Contains(c);

        public int id;
        public Rigidbody rb;
        public BoxCollider box;
        public MeshCollider hull;            // a big piece stands on the hull round its own shape (a curved panel or half a tank is not its box)
        Mesh hullMesh;
        public MeshRenderer drawn;
        public MeshFilter filter;
        public Mesh mesh;
        /// <summary>What smoke it trails (see Crashes.Say): how much (0 to 1) and until when, how light and how brown.</summary>
        public float smoke, smokeUntil, shade, warm;
        public physicalObject po;
        public float born, life, heat, burns, area, mass, reach;
        public bool resting, white;
        public byte tint;
        public Part from;                    // the part it came off (while that is still there: not hit by it for a moment)
        float still, fading = -1f, shownHeat = -1f;
        int finalLayer;
        float layerUntil;
        Vector3 scale = Vector3.one;
        MaterialPropertyBlock block;
        int materials;

        /// <summary>A piece's objects, made afresh: what it is drawn with, its collider, its body.</summary>
        static Piece Fresh()
        {
            if (surface == null) surface = new PhysicMaterial("Fragmentation wreckage") { dynamicFriction = 0.55f, staticFriction = 0.75f, bounciness = 0.06f, frictionCombine = PhysicMaterialCombine.Average, bounceCombine = PhysicMaterialCombine.Minimum };
            var go = new GameObject("Fragmentation piece");
            go.AddComponent<MeshFilter>();
            MeshRenderer r = go.AddComponent<MeshRenderer>();
            r.receiveShadows = true;
            var shape = new GameObject("Fragmentation piece collider");
            shape.transform.SetParent(go.transform, false);
            BoxCollider box0 = shape.AddComponent<BoxCollider>();
            box0.sharedMaterial = surface;
            Rigidbody rb0 = go.AddComponent<Rigidbody>();
            rb0.useGravity = false;                                     // (the game's gravity, or ours: see Step)
            rb0.drag = 0f;
            rb0.angularDrag = 0.15f;
            rb0.maxAngularVelocity = 40f;
            // (pieces of one part start against each other: pushed apart gently, not flung)
            rb0.maxDepenetrationVelocity = 1.5f;
            Piece p = go.AddComponent<Piece>();
            p.rb = rb0; p.box = box0; p.drawn = r; p.filter = go.GetComponent<MeshFilter>();
            // (the game's own kind of loose object: see the class's notes)
            p.po = go.AddComponent<physicalObject>();
            p.po.maxDistance = 2400f;
            p.po.origDrag = 0f;
            p.po.rb = rb0;
            return p;
        }

        /// <summary>
        /// Pieces made ahead and put by out of sight, a few a frame up to so many, while a craft is watched that may crash
        /// (Crashes.Update): at an impact the pool was empty, and each piece's objects were made afresh in the physics step,
        /// about a third of a millisecond each, seventy or eighty of them in two or three frames (seen on the PC, 2026-10-24).
        /// </summary>
        public static void MakeAhead(int most, int aFrame)
        {
            for (int k = 0; k < aFrame && pool.Count < most; k++)
            {
                Piece p = Fresh();
                p.gameObject.SetActive(false);
                pool.Push(p);
            }
        }

        /// <summary>A piece made from a mesh already laid out about its own middle, put at a place in the scene and set moving.</summary>
        public static Piece Make(Mesh mesh, Material[] painted, Color[] tints, Vector3 at, Quaternion turn, Vector3 velocity, Vector3 spin, float mass, float area,
                                 float heat, float burns, byte tint, bool white, Part from)
        {
            int final = Layers.Final, first = Layers.Spawn >= 0 ? Layers.Spawn : final;
            Piece p = null;
            // (one that has gone before, or made ahead, put to use: making the objects afresh for every piece cost more than the rest of it)
            while (pool.Count > 0 && p == null) p = pool.Pop();
            if (p == null) p = Fresh();
            GameObject g = p.gameObject;
            // (what is drawn is on the craft's own layer from the first moment, which the cameras draw; the collider, on an
            // object of its own, starts on a layer that runs into the ground alone: see Layers)
            g.layer = final;
            p.box.gameObject.layer = first;
            g.transform.SetPositionAndRotation(at, turn);
            g.transform.localScale = Vector3.one;
            if (p.mesh != null && p.mesh != mesh) Destroy(p.mesh);
            p.filter.sharedMesh = mesh;
            p.drawn.sharedMaterials = painted;
            p.drawn.SetPropertyBlock(null);
            // (the small ones cast no shadow: each piece that does is drawn again for every band of the sun's shadow it lies in)
            p.drawn.shadowCastingMode = mesh.bounds.extents.magnitude >= ShadowsFrom ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            Bounds b = mesh.bounds;
            p.box.center = b.center;
            // (a panel of skin is thinner than anything the physics can hold a box of: it is given some body)
            p.box.size = new Vector3(Mathf.Max(b.size.x, 0.06f), Mathf.Max(b.size.y, 0.06f), Mathf.Max(b.size.z, 0.06f));
            bool shaped = b.extents.magnitude > 0.6f && p.Shape(mesh);
            p.box.enabled = !shaped;
            if (p.hull != null) p.hull.enabled = shaped;
            g.SetActive(true);
            Rigidbody rb = p.rb;
            rb.isKinematic = false;
            rb.mass = Mathf.Max(0.002f, mass);
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.velocity = velocity;
            rb.angularVelocity = spin;
            p.id = ++ids;
            p.mesh = mesh;
            p.mass = rb.mass; p.area = area; p.heat = heat; p.burns = burns; p.tint = tint; p.white = white; p.from = from;
            p.reach = b.extents.magnitude;
            p.born = Time.time;
            p.life = Frag.Lasts * Random.Range(0.8f, 1.2f);
            p.finalLayer = final;
            p.layerUntil = Time.time + (first != final ? 0.35f : 0f);
            p.materials = painted.Length;
            p.resting = false; p.still = 0f; p.fading = -1f; p.shownHeat = -1f;
            p.scale = Vector3.one;
            p.smoke = 0f; p.smokeUntil = 0f; p.shade = 0.4f; p.warm = 0.2f;
            p.SetTints(tints);
            all.Add(p);
            colliders.Add(p.box);
            if (p.hull != null) colliders.Add(p.hull);
            return p;
        }

        static readonly List<Vector3> shapePoints = new List<Vector3>();
        static readonly List<int> shapeTris = new List<int>();

        /// <summary>The hull round a big piece's own shape (some of its corners, and the same a few centimetres further along the way it faces, for body). False if it could not be made.</summary>
        bool Shape(Mesh shape)
        {
            try
            {
                Vector3[] v = shape.vertices;
                if (v == null || v.Length < 4) return false;
                shapePoints.Clear();
                int step = Mathf.Max(1, v.Length / 90);
                for (int i = 0; i < v.Length; i += step) { shapePoints.Add(v[i]); shapePoints.Add(v[i] + Vector3.up * 0.04f); }
                shapeTris.Clear();
                for (int k = 0; k + 2 < shapePoints.Count; k += 3) { shapeTris.Add(k); shapeTris.Add(k + 1); shapeTris.Add(k + 2); }
                if (hullMesh == null) hullMesh = new Mesh { name = "Fragmentation piece shape" };
                hullMesh.Clear();
                hullMesh.SetVertices(shapePoints);
                hullMesh.SetTriangles(shapeTris, 0);
                if (hull == null)
                {
                    hull = box.gameObject.AddComponent<MeshCollider>();
                    hull.convex = true;
                    hull.sharedMaterial = surface;
                }
                hull.sharedMesh = null;
                hull.sharedMesh = hullMesh;
                return hull.sharedMesh != null;
            }
            catch (System.Exception) { return false; }
        }

        void SetTints(Color[] tints)
        {
            if (tints == null) return;
            block = block ?? new MaterialPropertyBlock();
            for (int k = 0; k < tints.Length && k < materials; k++)
            {
                drawn.GetPropertyBlock(block, k);
                block.SetColor(ColorId, tints[k]);
                drawn.SetPropertyBlock(block, k);
            }
        }

        static readonly int ColorId = Shader.PropertyToID("_Color"), HeatId = Shader.PropertyToID("_TemperatureColor");

        /// <summary>Gone: put back, out of sight, for another piece to use.</summary>
        void PutAway()
        {
            all.Remove(this);
            if (box != null) colliders.Remove(box);
            if (hull != null) colliders.Remove(hull);
            if (mesh != null) { Destroy(mesh); mesh = null; filter.sharedMesh = null; }
            from = null;
            if (rb != null) { rb.velocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
            gameObject.SetActive(false);
            if (pool.Count < 160) pool.Push(this);
            else Destroy(gameObject);
        }

        void OnDestroy()
        {
            all.Remove(this);
            if (box != null) colliders.Remove(box);
            if (hull != null) colliders.Remove(hull);
            if (mesh != null) Destroy(mesh);
            if (hullMesh != null) Destroy(hullMesh);
        }

        /// <summary>Whether the game is giving loose objects their weight just now (it does that while the craft being flown is whole and its physics running).</summary>
        static bool GameWeighs()
        {
            Vessel a = FlightGlobals.ActiveVessel;
            FlightIntegrator fi = FlightIntegrator.ActiveVesselFI;
            return a != null && a.state != Vessel.State.DEAD && a.loaded && !a.packed && fi != null && fi.isRunning && fi.Vessel == a;
        }

        /// <summary>Every step of the physics: weight where the game gives none, the air's drag, coming to rest, and going away when its time is up.</summary>
        public static void Step(float dt)
        {
            if (all.Count == 0) return;
            float now = Time.time;
            bool weigh = !GameWeighs();
            Vector3 g = Vector3.zero, frame = (Vector3)Krakensbane.GetFrameVelocity();
            float rho = 0f;
            CelestialBody body = FlightGlobals.currentMainBody;
            if (all[0] != null && body != null)
            {
                Vector3d at = all[0].transform.position;
                g = (Vector3)FlightGlobals.getGeeForceAtPosition(at, body);
                if (body.atmosphere)
                {
                    double pressure = FlightGlobals.getStaticPressure(at, body);
                    rho = pressure > 0 ? (float)FlightGlobals.getAtmDensity(pressure, FlightGlobals.getExternalTemperature(at, body), body) : 0f;
                }
            }
            all.RemoveAll(p => p == null);
            bool water = body != null && body.ocean;
            // ---- too many: the oldest go, those lying still first (the list is oldest first)
            int over = all.Count - Most;
            foreach (Piece p in all) if (p.fading >= 0f) over--;
            for (int pass = 0; pass < 2 && over > 0; pass++)
                foreach (Piece p in all)
                {
                    if (over <= 0) break;
                    if (p.fading >= 0f || (pass == 0 && !p.resting)) continue;
                    p.fading = now;
                    over--;
                }
            for (int n = all.Count - 1; n >= 0; n--)
            {
                Piece p = all[n];
                float age = now - p.born;
                // ---- its time is up
                if (p.fading < 0f && age > p.life) p.fading = now;
                if (p.rb == null) continue;
                if (p.layerUntil > 0f && now >= p.layerUntil) { if (p.box != null) p.box.gameObject.layer = p.finalLayer; p.layerUntil = 0f; }
                if (p.resting) continue;
                Vector3 v = p.rb.velocity;
                if (weigh) p.rb.AddForce(g, ForceMode.Acceleration);
                if (rho > 0f)
                {
                    // A tumbling plate: drag coefficient about one, on its area (half of it, on average, faces the air).
                    Vector3 air = v + frame;
                    float speed2 = air.sqrMagnitude;
                    if (speed2 > 0.25f)
                    {
                        float kN = 0.5f * rho * 1.1f * 0.5f * p.area * speed2 / 1000f;
                        float most_ = p.rb.mass * Mathf.Sqrt(speed2) / Mathf.Max(dt, 1e-3f) * 0.5f;   // (never more than would stop it in two steps)
                        p.rb.AddForce(-air.normalized * Mathf.Min(kN, most_), ForceMode.Force);
                    }
                }
                // ---- under the water (since 0.3.0): held up by what it puts aside (aluminium is 2.7 times as dense as water, steel 7.8: a
                // piece of skin weighs six tenths of itself there) and slowed hard by the water's drag, so that it sinks slowly, rocking, to the
                // bottom; and put out, if it was alight.
                if (water && Fragmentation.Water.Depth((Vector3d)p.rb.worldCenterOfMass) > 0f)
                {
                    float dense = p.mass / Mathf.Max(p.area, 0.01f) < 0.05f ? 2.7f : 7.8f;      // (a sheet of skin, or a lump of steel)
                    p.rb.AddForce(-g * (Fragmentation.Water.Density / dense), ForceMode.Acceleration);
                    Vector3 through = v + frame;
                    float s2 = through.sqrMagnitude;
                    if (s2 > 1e-4f)
                    {
                        float kN = 0.5f * Fragmentation.Water.Density * 1000f * 1.1f * 0.5f * p.area * s2 / 1000f;
                        float most_ = p.rb.mass * Mathf.Sqrt(s2) / Mathf.Max(dt, 1e-3f) * 0.5f;
                        p.rb.AddForce(-through.normalized * Mathf.Min(kN, most_), ForceMode.Force);
                    }
                    p.rb.angularVelocity *= Mathf.Exp(-3f * dt);
                    p.heat = 0f; p.burns = 0f; p.smokeUntil = 0f;
                }
                // ---- coming to rest: still for a second and a half, it is left lying where it is, out of the physics
                if (v.sqrMagnitude < 0.06f && p.rb.angularVelocity.sqrMagnitude < 0.09f && age > 1.5f)
                {
                    p.still += dt;
                    if (p.still > 1.5f) p.Rest();
                }
                else p.still = 0f;
                // (fast things are watched for going through other things between one step and the next; slow ones need not be. Changed
                // with some way between the two, not to and fro at every step)
                float fast2 = v.sqrMagnitude;
                if (fast2 > 625f && p.rb.collisionDetectionMode != CollisionDetectionMode.ContinuousDynamic) p.rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                else if (fast2 < 225f && p.rb.collisionDetectionMode != CollisionDetectionMode.Discrete) p.rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
            }
        }

        void Rest()
        {
            resting = true;
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.None;
        }

        /// <summary>Set it moving again (a blast near it).</summary>
        public void Wake()
        {
            if (!resting || rb == null) return;
            resting = false;
            still = 0f;
            rb.isKinematic = false;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
        }

        void Update()
        {
            float now = Time.time;
            if (fading >= 0f)
            {
                // It sinks into the ground as it goes, shrinking, rather than vanishing.
                float left = 1f - (now - fading) / 1.5f;
                if (left <= 0f) { PutAway(); return; }
                transform.localScale = scale * left;
            }
            if (heat > 0f || shownHeat > 0f)
            {
                // Hot from the fire it came out of: it glows dull red and cools in a few seconds.
                heat = Mathf.Max(0f, heat - Time.deltaTime * (rb != null && !resting ? 0.09f : 0.06f));
                if (Mathf.Abs(heat - shownHeat) > 0.01f || (heat <= 0f && shownHeat > 0f))
                {
                    shownHeat = heat;
                    block = block ?? new MaterialPropertyBlock();
                    for (int k = 0; k < materials; k++)
                    {
                        drawn.GetPropertyBlock(block, k);
                        block.SetColor(HeatId, new Color(1f, 0.32f, 0.05f, heat));
                        drawn.SetPropertyBlock(block, k);
                    }
                }
            }
            if (burns > 0f) burns -= Time.deltaTime;
        }

        /// <summary>A blast at a place throws the pieces near it: light thin ones further than heavy ones.</summary>
        public static void Blast(Vector3 at, float radius, float push)
        {
            if (radius <= 0f || push <= 0f) return;
            float r2 = radius * radius;
            foreach (Piece p in all)
            {
                if (p == null || p.rb == null) continue;
                Vector3 away = p.transform.position - at;
                float d2 = away.sqrMagnitude;
                if (d2 > r2) continue;
                p.Wake();
                float d = Mathf.Sqrt(d2), falls = 1f - d / radius;
                // (what the blast pushes on is the area; what it has to move is the mass)
                float perMass = Mathf.Clamp(p.area / Mathf.Max(0.002f, p.mass) / 40f, 0.3f, 2.5f);
                Vector3 way = d > 0.05f ? away / d : Random.onUnitSphere;
                way = (way + Random.insideUnitSphere * 0.3f).normalized;
                p.rb.AddForce(way * Mathf.Min(45f, push * falls * falls * perMass), ForceMode.VelocityChange);
                p.rb.AddTorque(Random.onUnitSphere * push * 0.3f * falls, ForceMode.VelocityChange);
            }
        }

        /// <summary>Everything this mod has lying about goes (a new copy of the mod is starting, or the flight is ending).</summary>
        public static void Clear()
        {
            foreach (Piece p in all.ToArray()) if (p != null) Destroy(p.gameObject);
            foreach (Piece p in pool) if (p != null) Destroy(p.gameObject);
            pool.Clear();
            all.Clear();
            colliders.Clear();
            // (and any left behind by an older copy of the mod, found by name)
            foreach (GameObject go in FindObjectsOfType<GameObject>())
                if (go != null && go.name == "Fragmentation piece" && go.transform.parent == null) Destroy(go);
        }
    }

    /// <summary>
    /// Which of the physics' layers pieces are put on. The game decides which layers run into which; this looks once
    /// for what suits: in the end the layer of the parts of craft themselves, so that pieces hit craft, the ground, and
    /// each other, and the smoke of the explosions mod goes round them as it goes round craft; and for the first moment
    /// after a part breaks up, a layer that runs into the ground and nothing else, so that pieces made of one part,
    /// which lie against each other and against the part next to it, do not push each other away at once.
    /// </summary>
    public static class Layers
    {
        public static int Final = 0, Spawn = -2;

        public static void Choose()
        {
            if (Spawn != -2) return;
            Spawn = -1;
            // (best a layer that hits the ground alone; failing that one that hits itself as well, so that the pieces of one part may
            // touch each other, gently (see Piece.Make), but not the craft they came off. In KSP 1.12 it is the second: layer 28.)
            for (int pass = 0; pass < 2 && Spawn < 0; pass++)
                for (int layer = 8; layer < 32; layer++)
                {
                    if (layer == 15 || layer == 0) continue;
                    if (Physics.GetIgnoreLayerCollision(layer, 15)) continue;            // must hit the ground
                    if (!Physics.GetIgnoreLayerCollision(layer, 0)) continue;            // and not the parts of craft
                    if (pass == 0 && !Physics.GetIgnoreLayerCollision(layer, layer)) continue;    // and, at best, not itself
                    if (!Physics.GetIgnoreLayerCollision(layer, 17) || !Physics.GetIgnoreLayerCollision(layer, 16)) continue;    // nor kerbals
                    Spawn = layer;
                    break;
                }
            Frag.Log("pieces go on layer " + Final + (Spawn >= 0 ? ", and for their first moment on layer " + Spawn + " (" + LayerMask.LayerToName(Spawn) + ")" : ", from the first moment (no layer runs into the ground alone)"));
        }

#if DEV
        public static string Table()
        {
            var say = new System.Text.StringBuilder();
            int[] rows = { 0, 15, 16, 17, 19, 21, 26, 27, 28, 29 };
            foreach (int a in rows)
            {
                say.Append(a).Append(" ").Append(LayerMask.LayerToName(a)).Append(": hits");
                for (int b = 0; b < 32; b++) if (!Physics.GetIgnoreLayerCollision(a, b)) say.Append(' ').Append(b);
                say.Append('\n');
            }
            say.Append("chosen: final ").Append(Final).Append(", spawn ").Append(Spawn);
            return say.ToString();
        }
#endif
    }
}
