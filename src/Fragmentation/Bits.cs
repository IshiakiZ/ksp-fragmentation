using System;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Fragmentation
{
    /// <summary>
    /// The small bits of a wreck (shreds of skin, bolts, foil) and the clods a crash throws out of soft ground: too many and too
    /// small to be pieces each with a body of its own (see Piece). Each is moved here, falls, bounces once or twice off the ground
    /// under where it was thrown from and comes to rest; they are drawn as small meshes, lit as the parts are, through the game's own
    /// particle system (which only draws them). A bit that burns trails a little flame and smoke, one that came out of a fire a wisp
    /// of smoke for a moment (see Crashes.Say).
    /// </summary>
    public static class Bits
    {
        public enum Kind { Metal, Paint, Burnt, Foil, Bolt, Clod, Turf }
        const int Kinds = 7, Most = 900;

        struct Bit { public Vector3 at, v, w, g, up, euler; public Quaternion turn; public float size, age, life, smoke, fire, drag; public byte kind; public bool down; public int id; }

        static readonly Bit[] all = new Bit[Most];
        static int count, ids;
        static ParticleSystem[] systems;
        static ParticleSystem.Particle[][] buffers;
        static int[] shown;
        // (which kinds' bits changed since they were last handed over: those lying still are not handed over again every frame)
        static readonly bool[] changed = new bool[Kinds], gone = new bool[Kinds];
        static bool shifted;
        static GameObject root;
        static bool hooked;
        static Color soil = new Color(0.3f, 0.24f, 0.16f), turf = new Color(0.3f, 0.34f, 0.14f);

        public static int Count => count;
        public static bool TestNone;            // (development build: no bits at all, to see what they cost)

        /// <summary>The colour of the earth here (the clods are drawn in it), and of its turf (the slabs torn off the top).</summary>
        public static void Earth(Color c, Color grass)
        {
            soil = c;
            turf = grass;
            if (turfTexture != null) { turfTexture.SetPixels(new[] { soil * 0.85f, turf }); turfTexture.Apply(); }
            if (systems == null) return;
            if (systems[(int)Kind.Clod] != null)
            {
                var r = systems[(int)Kind.Clod].GetComponent<ParticleSystemRenderer>();
                if (r != null && r.sharedMaterial != null) r.sharedMaterial.color = c;
            }

        }

        /// <summary>
        /// Throw so many bits from a place: going as fast as v, spread by so much (metres a second, every way), with sizes between the
        /// smallest and the biggest (few big, many small), of a kind; so many of them burning (0 to 1), and how much smoke a bit trails.
        /// </summary>
        public static void Emit(Vector3 at, Vector3 v, float spread, int n, float smallest, float biggest, Kind kind, float burning, float smoking, float upward = 0f)
        {
            if (n <= 0 || TestNone) return;
            Ready();
            CelestialBody body = FlightGlobals.currentMainBody;
            Vector3 up = body != null ? (Vector3)FlightGlobals.getUpAxis(body, at) : Vector3.up;
            // (the ground they will land on: under the place they come from, taken as flat round it)
            Vector3 g = at - up * 200f;
            if (Physics.Raycast(at + up * 3f, -up, out RaycastHit hit, 400f, 1 << 15, QueryTriggerInteraction.Ignore)) { g = hit.point; up = hit.normal; }
            float alpha = 1.6f, low = Mathf.Pow(Mathf.Clamp01(smallest / Mathf.Max(biggest, 1e-3f)), alpha);
            for (int k = 0; k < n; k++)
            {
                if (count >= Most) Drop();
                ref Bit b = ref all[count++];
                b.id = ++ids;
                b.at = at + Random.insideUnitSphere * Mathf.Min(0.6f, biggest * 2f);
                b.v = v + Random.insideUnitSphere * spread + up * (upward * Random.Range(0.3f, 1f));
                b.w = Random.onUnitSphere * Random.Range(4f, 25f);
                b.turn = Random.rotation;
                b.euler = b.turn.eulerAngles;
                b.g = g; b.up = up;
                b.size = smallest * Mathf.Pow(1f - Random.value * (1f - low), -1f / alpha);
                b.size = Mathf.Min(b.size, biggest);
                b.kind = (byte)kind;
                // (thin light things are slowed by the air far more than heavy ones: foil flutters down)
                bool earth = kind == Kind.Clod || kind == Kind.Turf;
                b.drag = kind == Kind.Foil ? 2.5f / Mathf.Max(0.02f, b.size) * 0.02f + 0.6f : earth ? 0.02f : 0.12f / Mathf.Max(0.02f, b.size) * 0.05f;
                b.age = 0f;
                // (clods and sods lie by the scar they came out of for a minute or more: lying still they cost next to nothing)
                b.life = earth ? Random.Range(60f, 100f) : Random.Range(45f, 90f);
                b.fire = Random.value < burning ? Random.Range(2f, 7f) : 0f;
                b.smoke = smoking * Random.Range(0.6f, 1.2f);
                b.down = false;
            }
        }

        /// <summary>The oldest that has come to rest goes, to make room (or the oldest of all).</summary>
        static void Drop()
        {
            int pick = 0;
            for (int k = 0; k < count; k++) if (all[k].down) { pick = k; break; }
            gone[all[pick].kind] = true;
            all[pick] = all[--count];
        }

        /// <summary>Every frame: move them, and hand them to be drawn.</summary>
        public static void Step(float dt)
        {
            if (systems == null) return;
            if (count == 0) { for (int s = 0; s < Kinds; s++) if (shown[s] > 0) { systems[s].SetParticles(buffers[s], 0); shown[s] = 0; } return; }
            CelestialBody body = FlightGlobals.currentMainBody;
            Vector3 gravity = body != null ? (Vector3)FlightGlobals.getGeeForceAtPosition(all[0].at, body) : Vector3.down * 9.81f;
            bool air = body != null && body.atmosphere && FlightGlobals.getStaticPressure(all[0].at, body) > 1.0;
            for (int s = 0; s < Kinds; s++) { changed[s] = shifted || gone[s]; gone[s] = false; }
            shifted = false;
            // ---- moved: those not yet lying still
            for (int k = count - 1; k >= 0; k--)
            {
                ref Bit b = ref all[k];
                b.age += dt;
                if (b.age > b.life) { changed[b.kind] = true; all[k] = all[--count]; continue; }
                if (b.fire > 0f) b.fire -= dt;
                if (b.down) { if (b.life - b.age < 1.5f) changed[b.kind] = true; continue; }
                changed[b.kind] = true;
                {
                    b.v += gravity * dt;
                    if (air) b.v *= 1f / (1f + b.drag * b.v.magnitude * dt * 0.1f);
                    b.at += b.v * dt;
                    if (b.w.sqrMagnitude > 1e-4f) b.turn = Quaternion.AngleAxis(b.w.magnitude * dt * Mathf.Rad2Deg, b.w.normalized) * b.turn;
                    float h = Vector3.Dot(b.at - b.g, b.up) - b.size * 0.3f;
                    if (h < 0f)
                    {
                        b.at -= b.up * h;
                        float into = Vector3.Dot(b.v, b.up);
                        if (into < 0f)
                        {
                            // (it bounces a little, skids, and stops; a clod breaks up as it lands and stays)
                            bool earth = b.kind == (byte)Kind.Clod || b.kind == (byte)Kind.Turf;
                            b.v -= b.up * into * (earth ? 1.05f : 1.25f);
                            b.v *= earth ? 0.25f : 0.5f;
                            b.w *= 0.5f;
                        }
                        if (b.v.sqrMagnitude < 1.5f) { b.down = true; b.v = Vector3.zero; b.w = Vector3.zero; }
                    }
                    b.euler = b.turn.eulerAngles;
                }
            }
            // ---- handed over to be drawn: the kinds any of whose bits changed
            for (int s = 0; s < Kinds; s++) if (changed[s]) shown[s] = 0;
            for (int k = 0; k < count; k++)
            {
                ref Bit b = ref all[k];
                int kind = b.kind;
                if (!changed[kind]) continue;
                ParticleSystem.Particle[] buf = buffers[kind];
                int i = shown[kind]++;
                // (going as its time is up: it shrinks away)
                float scale = b.life - b.age < 1.5f ? (b.life - b.age) / 1.5f : 1f;
                buf[i].position = b.at;
                buf[i].rotation3D = b.euler;
                buf[i].startSize = b.size * 2f * scale;
                buf[i].startColor = Color.white;
                buf[i].startLifetime = 1000f;
                buf[i].remainingLifetime = 999f;
                buf[i].randomSeed = (uint)b.id;
                buf[i].velocity = Vector3.zero;
                buf[i].angularVelocity3D = Vector3.zero;
            }
            for (int s = 0; s < Kinds; s++) if (changed[s]) systems[s].SetParticles(buffers[s], shown[s]);
        }

        /// <summary>The bits that smoke just now, for the explosions mod: each one's number, place, motion, size, how much smoke and how much fire.</summary>
        public static void Smoking(Action<int, Vector3, Vector3, float, float, float> say, int most)
        {
            int said = 0;
            // (the newest first, those still flying before those lying still: a bit in the air trails its wisp; one lying still, only if it burns)
            for (int k = count - 1; k >= 0 && said < most; k--)
            {
                ref Bit b = ref all[k];
                float smoke = b.smoke * Mathf.Exp(-b.age / 2.5f);
                bool fire = b.fire > 0f;
                if (!fire && (smoke < 0.02f || b.down)) continue;
                say(b.id, b.at, b.v, Mathf.Max(0.06f, b.size), fire ? Mathf.Max(0.5f, smoke) : smoke, fire ? Mathf.Clamp01(b.fire / 3f) : 0f);
                said++;
            }
        }

        /// <summary>The world has been moved under the camera (the game's floating origin): everything goes with it.</summary>
        static void Shifted(Vector3d offset, Vector3d nonFrame)
        {
            Vector3 o = (Vector3)offset;
            for (int k = 0; k < count; k++) { all[k].at -= o; all[k].g -= o; }
            shifted = true;
        }

        /// <summary>The game's events call only methods that belong to an object: this one's are the bits'.</summary>
        sealed class Ears { public void Shifted(Vector3d offset, Vector3d nonFrame) => Bits.Shifted(offset, nonFrame); }
        static Ears ears;

        public static void Prepare() => Ready();

        static void Ready()
        {
            if (!hooked) { ears = ears ?? new Ears(); GameEvents.onFloatingOriginShift.Add(ears.Shifted); hooked = true; }
            if (systems != null && root != null) return;
            root = new GameObject("Fragmentation bits");
            // (the bits go with the game's moving frame, as the craft do, and are moved every frame: where Smooth Running carries
            // the craft on between the game's physics steps, the bits are carried on with that frame, all at once by their root;
            // their particles are kept in the root's own space for that, which is the scene's while it stands at its middle)
            try { Ride(root.transform); } catch (Exception) { }          // (a Keystone older than 0.9.0 has no Motion: nothing is carried)
            systems = new ParticleSystem[Kinds];
            buffers = new ParticleSystem.Particle[Kinds][];
            shown = new int[Kinds];
            Mesh shard = Shard(), bolt = Bolt(), clod = Clod(1f), foil = Foil(), sod = Sod();
            Make(Kind.Metal, shard, new Color(0.62f, 0.62f, 0.6f), true);
            Make(Kind.Paint, shard, new Color(0.86f, 0.86f, 0.84f), false);
            Make(Kind.Burnt, shard, new Color(0.09f, 0.085f, 0.08f), false);
            Make(Kind.Foil, foil, new Color(0.78f, 0.66f, 0.38f), true);
            Make(Kind.Bolt, bolt, new Color(0.5f, 0.5f, 0.5f), true);
            Make(Kind.Clod, clod, soil, false);
            Make(Kind.Turf, sod, Color.white, false);
            // (a slab of turf: grass on top, earth on its sides and underneath; its mesh picks which by where its faces look)
            systems[(int)Kind.Turf].GetComponent<ParticleSystemRenderer>().sharedMaterial.mainTexture = TurfTexture();
        }

        static void Ride(Transform t) => Keystone.Motion.RideWithFrame(t);

        static Texture2D turfTexture;

        /// <summary>Two colours side by side, the earth's and the grass's: a slab of turf's faces take one or the other.</summary>
        static Texture2D TurfTexture()
        {
            if (turfTexture != null) return turfTexture;
            turfTexture = new Texture2D(2, 1, TextureFormat.RGB24, false) { name = "Fragmentation turf", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Point };
            turfTexture.SetPixels(new[] { soil * 0.85f, turf });
            turfTexture.Apply();
            return turfTexture;
        }

        /// <summary>A slab of turf: a flattened lump, its upward faces grass and the rest earth.</summary>
        static Mesh Sod()
        {
            Mesh m = Clod(0.4f);
            Vector3[] v = m.vertices;
            var uv = new Vector2[v.Length];
            for (int k = 0; k + 2 < v.Length; k += 3)
            {
                Vector3 n = Vector3.Cross(v[k + 1] - v[k], v[k + 2] - v[k]);
                float u = n.y > 0.35f * n.magnitude ? 0.75f : 0.25f;
                uv[k] = uv[k + 1] = uv[k + 2] = new Vector2(u, 0.5f);
            }
            m.uv = uv;
            m.name = "Fragmentation sod";
            return m;
        }

        static void Make(Kind kind, Mesh mesh, Color colour, bool shiny)
        {
            var holder = new GameObject("Fragmentation bits " + kind) { layer = 0 };
            holder.transform.SetParent(root.transform, false);
            var system = holder.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = system.main;
            main.playOnAwake = false;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startSpeed = 0f;
            main.gravityModifier = 0f;
            main.startRotation3D = true;
            main.maxParticles = Most;
            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = false;
            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = false;
            var renderer = holder.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = mesh;
            renderer.alignment = ParticleSystemRenderSpace.World;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = true;
            Shader s = (shiny ? Shader.Find("KSP/Specular") : null) ?? Shader.Find("KSP/Diffuse") ?? Shader.Find("Diffuse");
            var m = new Material(s) { name = "Fragmentation bits " + kind, color = colour };
            m.mainTexture = Speckle();
            if (m.HasProperty("_SpecColor")) m.SetColor("_SpecColor", new Color(0.4f, 0.4f, 0.4f));
            if (m.HasProperty("_Shininess")) m.SetFloat("_Shininess", 0.3f);
            renderer.sharedMaterial = m;
            system.Play(false);
            systems[(int)kind] = system;
            buffers[(int)kind] = new ParticleSystem.Particle[Most];
        }

        static Texture2D speckle;
        static Texture2D Speckle()
        {
            if (speckle != null) return speckle;
            speckle = new Texture2D(16, 16, TextureFormat.RGB24, false) { name = "Fragmentation speckle", wrapMode = TextureWrapMode.Repeat };
            var px = new Color[256];
            for (int k = 0; k < 256; k++) { float v = 0.8f + 0.2f * Random.value; px[k] = new Color(v, v, v); }
            speckle.SetPixels(px);
            speckle.Apply();
            return speckle;
        }

        // ---- the shapes, a metre or so across (each bit is drawn at its own size)

        /// <summary>A shred of skin: an irregular flat piece, seen from either side.</summary>
        static Mesh Shard()
        {
            var v = new Vector3[10];
            float[] r = { 0.5f, 0.32f, 0.45f, 0.28f, 0.4f };
            for (int k = 0; k < 5; k++)
            {
                float a = k * Mathf.PI * 2f / 5f + (k % 2) * 0.3f;
                v[k] = v[k + 5] = new Vector3(Mathf.Cos(a) * r[k], (k % 2) * 0.04f, Mathf.Sin(a) * r[k]);
            }
            var t = new int[] { 0, 2, 1, 0, 3, 2, 0, 4, 3, 5, 6, 7, 5, 7, 8, 5, 8, 9 };
            var m = new Mesh { name = "Fragmentation shard", vertices = v, triangles = t, uv = new Vector2[10] };
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        /// <summary>Crinkled foil (the insulation blanket of a spacecraft): a sheet with folds in it, seen from either side.</summary>
        static Mesh Foil()
        {
            var verts = new System.Collections.Generic.List<Vector3>();
            var norms = new System.Collections.Generic.List<Vector3>();
            var tris = new System.Collections.Generic.List<int>();
            float[,] h = { { 0f, 0.12f, -0.05f }, { 0.1f, -0.12f, 0.08f }, { -0.06f, 0.09f, 0f } };
            for (int side = 0; side < 2; side++)
            {
                int start = verts.Count;
                for (int z = 0; z < 3; z++) for (int x = 0; x < 3; x++) { verts.Add(new Vector3(x * 0.5f - 0.5f, h[z, x], z * 0.5f - 0.5f)); norms.Add(side == 0 ? Vector3.up : Vector3.down); }
                for (int z = 0; z < 2; z++)
                    for (int x = 0; x < 2; x++)
                    {
                        int a = start + z * 3 + x, b = a + 1, c = a + 3, d = a + 4;
                        if (side == 0) { tris.Add(a); tris.Add(c); tris.Add(b); tris.Add(b); tris.Add(c); tris.Add(d); }
                        else { tris.Add(a); tris.Add(b); tris.Add(c); tris.Add(b); tris.Add(d); tris.Add(c); }
                    }
            }
            var m = new Mesh { name = "Fragmentation foil" };
            m.SetVertices(verts); m.SetNormals(norms); m.SetTriangles(tris, 0); m.uv = new Vector2[verts.Count];
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        /// <summary>A bolt or a rivet: a short six-sided rod.</summary>
        static Mesh Bolt()
        {
            var verts = new System.Collections.Generic.List<Vector3>();
            var tris = new System.Collections.Generic.List<int>();
            for (int k = 0; k < 6; k++)
            {
                float a0 = k * Mathf.PI / 3f, a1 = (k + 1) * Mathf.PI / 3f;
                Vector3 p0 = new Vector3(0f, Mathf.Cos(a0) * 0.18f, Mathf.Sin(a0) * 0.18f), p1 = new Vector3(0f, Mathf.Cos(a1) * 0.18f, Mathf.Sin(a1) * 0.18f);
                int s = verts.Count;
                verts.Add(p0 + Vector3.left * 0.5f); verts.Add(p1 + Vector3.left * 0.5f); verts.Add(p1 + Vector3.right * 0.5f); verts.Add(p0 + Vector3.right * 0.5f);
                tris.Add(s); tris.Add(s + 1); tris.Add(s + 2); tris.Add(s); tris.Add(s + 2); tris.Add(s + 3);
            }
            var m = new Mesh { name = "Fragmentation bolt" };
            m.SetVertices(verts); m.SetTriangles(tris, 0); m.uv = new Vector2[verts.Count];
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        /// <summary>A clod of earth: a lumpy ball, faceted (squashed to so much of its height: a slab of turf is flat).</summary>
        static Mesh Clod(float tall)
        {
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            Vector3[] c = { new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0), new Vector3(0, -1, t), new Vector3(0, 1, t),
                            new Vector3(0, -1, -t), new Vector3(0, 1, -t), new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1) };
            int[] f = { 0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8, 3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1 };
            var r = new float[12];
            for (int k = 0; k < 12; k++) r[k] = Random.Range(0.32f, 0.55f) / c[k].magnitude;
            var verts = new Vector3[f.Length];
            var tris = new int[f.Length];
            for (int k = 0; k < f.Length; k++) { verts[k] = Vector3.Scale(c[f[k]] * r[f[k]], new Vector3(1f, 0.75f * tall, 1f)); tris[k] = k; }
            var m = new Mesh { name = "Fragmentation clod", vertices = verts, triangles = tris, uv = new Vector2[verts.Length] };
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        public static void Clear()
        {
            count = 0;
            if (hooked && ears != null) { GameEvents.onFloatingOriginShift.Remove(ears.Shifted); hooked = false; }
            if (root != null) UnityEngine.Object.Destroy(root);
            root = null;
            systems = null;
        }
    }
}
