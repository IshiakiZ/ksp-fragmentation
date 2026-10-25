using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Fragmentation
{
    /// <summary>Why a panel came off: it decides what it becomes (see Crashes.Spawn).</summary>
    public enum Torn { Ground, Crushed, Blown, Broken, Cut }

    /// <summary>
    /// One part's own skin, torn where it is hurt: the part itself stays where it is, joined to the parts beside it, and
    /// is drawn by this in its place, panel by panel (see HullPlan), with what has happened to it:
    ///
    /// * **crushed** where it hit: the end that hit is pushed in, the skin within so far of it folded up as a thin tube
    ///   folds when it is driven end on (rings of folds, bulging out), the panels flattened right down torn off;
    /// * **ground away** where it is dragged along the ground: everything below a plane that moves up into the part as
    ///   the drag does its work is torn off, and the skin just above it is flattened, so that a tank sliding on its side
    ///   loses its underside first and keeps its top;
    /// * **dented** where a blow did not break it;
    /// * **blown open** where what was in it went up: the panels near the burst torn off, those round the hole bent back
    ///   like petals and blackened;
    /// * and wherever a panel is gone the inside of the skin shows, and its torn edges, as thick as the wall.
    ///
    /// What is torn off becomes pieces (see Crashes.Spawn). The part's collider becomes a box round what is left of its
    /// skin as soon as any of it is gone (so that a tank that has lost its underside sits lower, and a torn one no longer
    /// rolls), and its mass goes down with its skin.
    /// </summary>
    public sealed class Hull : MonoBehaviour
    {
        struct Crush { public Vector3 at, way; public float depth, zone; }
        struct Dent { public Vector3 at, way; public float depth, radius; }
        struct Peel { public Vector3 hinge, axis; public float angle; }

        public Part part;
        public HullPlan plan;
        public bool shown, boxed;
        public float leftArea;
        bool[] gone, burnt;
        readonly List<Crush> crushes = new List<Crush>();
        readonly List<Dent> dents = new List<Dent>();
        readonly Dictionary<int, Peel> peels = new Dictionary<int, Peel>();
        /// <summary>Panels torn off since this was last emptied, and why (see Crashes.Spawn).</summary>
        public readonly List<KeyValuePair<int, Torn>> removed = new List<KeyValuePair<int, Torn>>();
        Material[] slotMats;
        Color[] slotColours;
        GameObject drawnObject, boxObject;
        MeshRenderer drawn;
        Mesh mesh;
        Collider box;                        // what stands in for the part's own colliders once it is torn: the hull round what is left of its skin
        MeshCollider hullShape;
        Mesh shapeMesh;
        float shapeAt = -10f;
        float[] fold;                        // each corner's fold, worked out the first time it is wanted (the noise is dear, and the same every time)
        readonly List<Collider> hidden = new List<Collider>();
        readonly List<Renderer> hiddenRenderers = new List<Renderer>();
        float prefabMass = -1f;
        bool dirty;                          // something to draw afresh: the shape of some panels (see reshape), or which panels there are (topology)
        bool topology = true;
        bool[] reshape;                      // the panels whose corners are to be worked out again
        float shapeDue;
        Vector3 biteWay;
        float bite;

        // ---- the skin as last drawn, in the part's axes (each corner of each triangle)
        Vector3[] pos, nrm;
        // ---- what the mesh is made from: each corner outside, the same inside, then the torn edges. Shared by every part's skin (each
        // redrawing fills them afresh from its own corners): a megabyte or more each, they were made for every torn part, and so much made
        // at once in a crash brought the garbage collector's long pauses on sooner
        static Vector3[] vb, nb;
        static Vector2[] ub;
        static Vector4[] tb;
        int vertexCount;
        List<int>[] lists;
        static Material inside;
        static readonly int ColorId = Shader.PropertyToID("_Color");
        MaterialPropertyBlock block;

        public float Left => plan != null && plan.area > 0f ? leftArea / plan.area : 1f;

        /// <summary>For the development build's readouts: how deep it has been ground away from below, and crushed in, at most (metres).</summary>
        public string Damage()
        {
            float ground = 0f, crushed = 0f;
            foreach (Crush c in crushes) { if (c.zone <= 0.05f) ground = Mathf.Max(ground, c.depth); else crushed = Mathf.Max(crushed, c.depth); }
            return "ground away " + ground.ToString("F2") + " m, crushed " + crushed.ToString("F2") + " m, dents " + dents.Count + ", bite " + bite.ToString("F2");
        }
        public bool Gone(int cell) => gone[cell];
        public int Cells => plan.cells.Length;

        /// <summary>The hull of a part, made now if its plan is ready and it has none. Null where the part cannot be torn (nothing could be read of it, or its plan is not ready yet).</summary>
        public static Hull Of(Part p, bool make = true)
        {
            if (p == null) return null;
            Hull h = p.GetComponent<Hull>();
            if (h != null || !make) return h;
            HullPlan plan = HullPlan.For(p);
            if (plan == null || !plan.ready) return null;
            List<MeshFilter> filters = HullPlan.Drawn(p);
            if (filters.Count != plan.filters) return null;           // (it has changed since: a variant switched, a shroud cast off)
            h = p.gameObject.AddComponent<Hull>();
            if (!h.Begin(p, plan, filters)) { Destroy(h); return null; }
            return h;
        }

        bool Begin(Part p, HullPlan pl, List<MeshFilter> filters)
        {
            part = p; plan = pl;
            gone = new bool[plan.cells.Length];
            burnt = new bool[plan.cells.Length];
            leftArea = plan.area;
            slotMats = new Material[plan.slotFilter.Length];
            slotColours = new Color[slotMats.Length];
            for (int s = 0; s < slotMats.Length; s++)
            {
                int f = plan.slotFilter[s];
                if (f >= filters.Count) return false;
                Material[] mats = filters[f].GetComponent<MeshRenderer>().sharedMaterials;
                Material m = mats[Mathf.Min(plan.slotSub[s], mats.Length - 1)];
                slotMats[s] = m;
                slotColours[s] = m != null && m.HasProperty(ColorId) ? m.GetColor(ColorId) : Color.white;
            }
            foreach (MeshFilter f in filters) { MeshRenderer r = f.GetComponent<MeshRenderer>(); if (r != null) hiddenRenderers.Add(r); }
            pos = new Vector3[plan.p.Length];
            nrm = new Vector3[plan.p.Length];
            Array.Copy(plan.p, pos, pos.Length);
            Array.Copy(plan.n, nrm, nrm.Length);
            reshape = new bool[plan.cells.Length];
            for (int ci = 0; ci < reshape.Length; ci++) reshape[ci] = true;
            return true;
        }

        // ================================================================== what happens to it

        /// <summary>Take over the drawing of the part: its own renderers are hidden and this draws it, panel by panel.</summary>
        public void Show()
        {
            if (shown) return;
            shown = true;
            // (drawn ahead while the craft was watched: only what has been struck since is drawn again; see Prepare)
            if (prepared) { dirty = true; shapeDue = 0f; return; }
            Make();
        }

        /// <summary>
        /// (Since 2026-10-24) The first drawing made ahead, while the craft is watched and nothing has touched this part yet: the mesh
        /// laid out and filled once, its renderer off and the part drawn by its own as ever, until the first blow (Show). A part's first
        /// drawing is the most work its skin ever takes (every panel laid out, its corners, triangles, maps and materials: 3 to 12 ms of
        /// a crash's frame on the PC); a drawing after it moves the corners of what was struck and hands them over. Made one part a
        /// frame by Crashes.Prepare; let go again (Crashes.Disarm) if the craft is left untouched.
        /// </summary>
        public void Prepare()
        {
            if (shown || prepared || part == null) return;
            Make();
            Rebuild();
            prepared = true;
        }

        public bool prepared;

        void Make()
        {
            drawnObject = new GameObject("Fragmentation hull") { layer = part.gameObject.layer };
            drawnObject.transform.SetParent(part.transform, false);
            drawnObject.AddComponent<MeshFilter>().sharedMesh = mesh = new Mesh { name = "Fragmentation hull" };
            mesh.MarkDynamic();
            if (plan.p.Length * 2 > 60000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            drawn = drawnObject.AddComponent<MeshRenderer>();
            drawn.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            drawn.receiveShadows = true;
            drawn.enabled = false;
            // (drawn from the next frame: the part's own renderers are hidden when this has something to draw in their place)
            dirty = true;
            shapeDue = 0f;
        }

        /// <summary>Its collider becomes a box round what is left of its skin (the part's own colliders are switched off).</summary>
        public void UseBox()
        {
            if (boxed || part == null) return;
            boxed = true;
            PhysicMaterial surface = null;
            foreach (Collider c in part.GetComponentsInChildren<Collider>(false))
            {
                if (c == null || !c.enabled || c.isTrigger) continue;
                if (surface == null) surface = c.sharedMaterial;
                c.enabled = false;
                hidden.Add(c);
            }
            boxObject = new GameObject("Fragmentation hull collider") { layer = part.gameObject.layer };
            boxObject.transform.SetParent(part.transform, false);
            // (the hull round what is left, not a box: a box round a round tank stands out of it at its corners, and a torn tank lying on one of
            // them was thrown up off the ground when it took it)
            hullShape = boxObject.AddComponent<MeshCollider>();
            hullShape.convex = true;
            hullShape.sharedMaterial = surface;
            box = hullShape;
            Fit();
            Crashes.Own(box, part.GetComponent<Hits>());
            try { if (part.vessel != null) CollisionManager.IgnoreCollidersOnVessel(part.vessel, box); } catch (Exception) { }
        }

        /// <summary>How far it has dug into soft ground, and which way the ground is (the part's axes): the box is that much shallower on that side, so that it sits down in the ground.</summary>
        public void Bite(Vector3 worldDown, float depth)
        {
            Vector3 way = part.transform.InverseTransformDirection(worldDown);
            if (Mathf.Abs(depth - bite) < 0.02f && Vector3.Dot(way, biteWay) > 0.95f) return;
            biteWay = way.normalized; bite = depth;
            if (boxed) Fit();
        }

        /// <summary>A blow: the skin within so far of where it hit is pushed in by so much, along the way it was hit (into the part).</summary>
        public int CrushAt(Vector3 worldAt, Vector3 worldInto, float depth)
        {
            if (depth <= 0.005f) return 0;
            Vector3 at = part.transform.InverseTransformPoint(worldAt), way = part.transform.InverseTransformDirection(worldInto).normalized;
            float length = Extent(way);
            depth = Mathf.Min(depth, length);
            int k = -1;
            for (int i = 0; i < crushes.Count; i++)
                if (crushes[i].zone > 0.05f && Vector3.Dot(crushes[i].way, way) > 0.9f && Lateral(at - crushes[i].at, way) < 0.35f * length + 0.3f) { k = i; break; }
            Crush c = k >= 0 ? crushes[k] : new Crush { at = at, way = way };
            c.depth = Mathf.Min(length, c.depth + depth);
            c.zone = Mathf.Clamp(c.depth * 1.6f + 0.15f, 0.2f, Mathf.Max(0.2f, length));
            if (k >= 0) crushes[k] = c; else { if (crushes.Count >= 6) crushes.RemoveAt(0); crushes.Add(c); }
            // (flattened right down: torn off and shredded; round the tear some of the skin left bent back)
            int count = 0;
            for (int ci = 0; ci < gone.Length; ci++)
            {
                if (gone[ci]) continue;
                float top = Highest(ci, c.at, c.way);
                if (top < c.depth * 0.55f) { Remove(ci, Torn.Crushed); count++; }
            }
            if (count > 0) Flaps(0.2f, 20f, 70f);
            Touch(c.at, c.way, c.zone + 0.1f);
            return count;
        }

        /// <summary>
        /// Dragged along the ground with so much work done on the skin there (kilojoules): the plane below which it is torn away moves up into
        /// it by as much as that work tears off. Returns the number of panels torn off.
        /// </summary>
        public int Grind(Vector3 worldAt, Vector3 worldUp, float work, float toughness)
        {
            if (work <= 0f) return 0;
            Vector3 at = part.transform.InverseTransformPoint(worldAt), way = part.transform.InverseTransformDirection(worldUp).normalized;
            int k = -1;
            for (int i = 0; i < crushes.Count; i++)
                if (crushes[i].zone <= 0.05f && Vector3.Dot(crushes[i].way, way) > 0.85f) { k = i; break; }
            Crush c;
            if (k >= 0) c = crushes[k];
            else
            {
                // (a new plane, at the lowest point of the skin that way: where it touches)
                float lowest = float.MaxValue;
                for (int ci = 0; ci < gone.Length; ci++) if (!gone[ci]) lowest = Mathf.Min(lowest, Lowest(ci, at, way));
                c = new Crush { at = at + way * (lowest < float.MaxValue ? lowest : 0f), way = way, depth = 0f, zone = 0f };
            }
            // How much of the skin a metre further up holds: what the box round it covers, seen from below.
            float swept = Mathf.Max(0.15f, Footprint(way) * 0.5f);
            float deeper = work / Mathf.Max(1e-3f, toughness * swept);
            c.depth = Mathf.Min(Extent(way), c.depth + Mathf.Min(deeper, 0.25f * plan.edge + 0.02f));
            if (k >= 0) crushes[k] = c; else { if (crushes.Count >= 6) crushes.RemoveAt(0); crushes.Add(c); }
            int count = 0;
            for (int ci = 0; ci < gone.Length; ci++)
            {
                if (gone[ci]) continue;
                // (a panel half ground away is torn off: its edge caught, it does not stay on as a flattened strip)
                if (0.5f * (Lowest(ci, c.at, c.way) + Highest(ci, c.at, c.way)) < c.depth) { Remove(ci, Torn.Ground); count++; }
            }
            // (torn skin dragged along: now and then a flap of it bent back, hanging from the edge of the tear)
            if (count > 0) Flaps(0.03f, 15f, 50f);
            // (only the panels the plane now cuts: those wholly above it are as they were)
            Touch(c.at, c.way, c.depth + 0.03f);
            return count;
        }

        /// <summary>
        /// A part whose meshes come away whole (an engine: its bell, its pump, its mounting), worn by being dragged and knocked along the
        /// ground: they go one after another as the wear grows (0 whole, 1 worn right through), the one nearest the ground first, until one
        /// is left (it goes when the part does). Returns how many came away this time.
        /// </summary>
        public int Wear(Vector3 worldAt, Vector3 worldUp, float worn)
        {
            if (plan.area <= 0f || worn <= 0f) return 0;
            Vector3 at = part.transform.InverseTransformPoint(worldAt), way = part.transform.InverseTransformDirection(worldUp).normalized;
            int count = 0;
            for (int guard = 0; guard < 6; guard++)
            {
                int pick = -1, on = 0;
                float lowest = float.MaxValue;
                for (int ci = 0; ci < gone.Length; ci++)
                {
                    if (gone[ci] || !plan.cells[ci].whole) continue;
                    on++;
                    float low = 0.5f * (Lowest(ci, at, way) + Highest(ci, at, way));
                    if (low < lowest) { lowest = low; pick = ci; }
                }
                if (on <= 1 || pick < 0) break;
                // (it goes once the wear has got half way through its share of the part)
                if (1f - Left + 0.5f * plan.cells[pick].area / plan.area > worn) break;
                Remove(pick, Torn.Ground);
                count++;
            }
            return count;
        }

        /// <summary>A dent where a blow did not break it.</summary>
        public void DentAt(Vector3 worldAt, Vector3 worldInto, float depth, float radius)
        {
            if (depth < 0.01f) return;
            if (dents.Count >= 8) dents.RemoveAt(0);
            var d = new Dent { at = part.transform.InverseTransformPoint(worldAt), way = part.transform.InverseTransformDirection(worldInto).normalized, depth = Mathf.Min(depth, 0.3f * plan.size), radius = radius };
            dents.Add(d);
            for (int ci = 0; ci < gone.Length; ci++)
            {
                HullPlan.Cell c = plan.cells[ci];
                Vector3 nearest = Vector3.Max(c.lo, Vector3.Min(c.hi, d.at));
                if ((nearest - d.at).sqrMagnitude < radius * radius) reshape[ci] = true;
            }
            dirty = true;
        }

        /// <summary>
        /// What was in it went up, from a place: the panels nearest that place are torn off (more, the more there was), until no more than
        /// so much of the skin is left; those round the hole are bent back and blackened. The top of it, as it lies, is kept longest.
        /// </summary>
        public List<int> BlowOff(Vector3 worldAt, float leaveShare, Vector3 worldUp)
        {
            var torn = new List<int>();
            Vector3 at = part.transform.InverseTransformPoint(worldAt), up = part.transform.InverseTransformDirection(worldUp).normalized;
            float want = Mathf.Clamp01(leaveShare) * plan.area;
            var order = new List<KeyValuePair<float, int>>();
            for (int ci = 0; ci < gone.Length; ci++)
            {
                if (gone[ci]) continue;
                HullPlan.Cell cell = plan.cells[ci];
                // nearest first; those high up (as the part lies) and facing up last
                float rank = (cell.mid - at).magnitude * Random.Range(0.7f, 1.3f) - 0.35f * plan.size * (Vector3.Dot(cell.mid - plan.mid, up) / Mathf.Max(0.1f, plan.size) + 0.3f * Vector3.Dot(cell.normal, up));
                order.Add(new KeyValuePair<float, int>(rank, ci));
            }
            order.Sort((a, b) => a.Key.CompareTo(b.Key));
            foreach (KeyValuePair<float, int> o in order)
            {
                if (leftArea <= want) break;
                Remove(o.Value, Torn.Blown);
                torn.Add(o.Value);
            }
            // blackened round the hole, and some of its edge bent back
            float reach = 0.5f * plan.size;
            for (int ci = 0; ci < gone.Length; ci++)
            {
                if (gone[ci]) continue;
                if ((plan.cells[ci].mid - at).magnitude < reach * Random.Range(0.8f, 1.6f)) burnt[ci] = true;
                if (Random.value < 0.35f && Borders(ci)) PeelBack(ci, Random.Range(35f, 110f));
            }
            dirty = true; topology = true;
            return torn;
        }

        /// <summary>Blacken the panels near a place (a fire on it or beside it).</summary>
        public void Scorch(Vector3 worldAt, float reach)
        {
            Vector3 at = part.transform.InverseTransformPoint(worldAt);
            bool any = false;
            for (int ci = 0; ci < gone.Length; ci++)
                if (!gone[ci] && !burnt[ci] && (plan.cells[ci].mid - at).magnitude < reach * Random.Range(0.7f, 1.3f)) { burnt[ci] = true; any = true; }
            if (any) { dirty = true; topology = true; }
        }

        /// <summary>Every panel still on goes (the part is breaking up altogether).</summary>
        public List<int> TearAll(Torn why)
        {
            var torn = new List<int>();
            for (int ci = 0; ci < gone.Length; ci++) if (!gone[ci]) { Remove(ci, why); torn.Add(ci); }
            return torn;
        }

        /// <summary>
        /// Panels no longer joined, through panels still on, to the rest of the piece of skin they were part of: they are a piece of their own
        /// now (a tank torn right round comes away in two; the smaller part goes). Each group comes back as one list; they are counted gone.
        /// (A part's separate meshes are separate pieces of skin from the start, and are not loose for that.)
        /// </summary>
        public List<List<int>> CutLoose()
        {
            var loose = new List<List<int>>();
            int cells = gone.Length;
            var seen = new bool[cells];
            var bestOf = new Dictionary<int, List<int>>();
            var bestArea = new Dictionary<int, float>();
            var all = new List<List<int>>();
            for (int ci = 0; ci < cells; ci++)
            {
                if (gone[ci] || seen[ci]) continue;
                var group = new List<int>();
                var stack = new Stack<int>();
                stack.Push(ci); seen[ci] = true;
                float area = 0f;
                while (stack.Count > 0)
                {
                    int c = stack.Pop();
                    group.Add(c);
                    area += plan.cells[c].area;
                    foreach (int o in plan.cells[c].near) if (!gone[o] && !seen[o]) { seen[o] = true; stack.Push(o); }
                }
                all.Add(group);
                int island = plan.cells[ci].island;
                if (!bestArea.TryGetValue(island, out float most) || area > most) { bestArea[island] = area; bestOf[island] = group; }
            }
            foreach (List<int> g in all) if (bestOf[plan.cells[g[0]].island] != g) loose.Add(g);
            foreach (List<int> g in loose) foreach (int c in g) Remove(c, Torn.Cut);
            return loose;
        }

        /// <summary>A tear right round the part across a plane (world place and way): the panels the plane passes through are torn off. Returns how many.</summary>
        public int CutRing(Vector3 worldAt, Vector3 worldWay)
        {
            Vector3 at = part.transform.InverseTransformPoint(worldAt), way = part.transform.InverseTransformDirection(worldWay).normalized;
            int count = 0;
            for (int ci = 0; ci < gone.Length; ci++)
            {
                if (gone[ci]) continue;
                if (Lowest(ci, at, way) < 0f && Highest(ci, at, way) > 0f) { Remove(ci, Torn.Blown); count++; }
            }
            return count;
        }

        /// <summary>Whether any of the skin is left within so far of a place (the part's axes): what holds it to a part joined to it there.</summary>
        public bool Holds(Vector3 local, float reach)
        {
            for (int ci = 0; ci < gone.Length; ci++)
            {
                if (gone[ci]) continue;
                HullPlan.Cell c = plan.cells[ci];
                Vector3 nearest = Vector3.Max(c.lo, Vector3.Min(c.hi, local));
                if ((nearest - local).sqrMagnitude < reach * reach) return true;
            }
            return false;
        }

        /// <summary>How much of the skin is left within so far of a place (0 to 1, by area against what was there).</summary>
        public float HeldAt(Vector3 local, float reach)
        {
            float had = 0f, left = 0f;
            for (int ci = 0; ci < gone.Length; ci++)
            {
                HullPlan.Cell c = plan.cells[ci];
                Vector3 nearest = Vector3.Max(c.lo, Vector3.Min(c.hi, local));
                if ((nearest - local).sqrMagnitude >= reach * reach) continue;
                had += c.area;
                if (!gone[ci]) left += c.area;
            }
            return had > 0f ? left / had : 1f;
        }

        void Remove(int ci, Torn why)
        {
            if (gone[ci]) return;
            gone[ci] = true;
            leftArea -= plan.cells[ci].area;
            peels.Remove(ci);
            removed.Add(new KeyValuePair<int, Torn>(ci, why));
            measured = false;
            shapeStale = true;
            // (its collider given way to the hull round what is left in a frame or two: making one takes a millisecond or so, and a dozen
            // parts are torn in the first step of a crash; until then the part's own colliders stand)
            if (!boxed) wantBox = true;
            if (!shown) Show();
            dirty = true; topology = true;
        }

        // ================================================================== (since 0.3.0) kept in the save

        static string F(float f) => f.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        static string V(Vector3 v) => F(v.x) + " " + F(v.y) + " " + F(v.z);
        static float[] Floats(string s)
        {
            if (string.IsNullOrEmpty(s)) return new float[0];
            string[] w = s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var f = new float[w.Length];
            for (int i = 0; i < w.Length; i++) float.TryParse(w[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out f[i]);
            return f;
        }

        /// <summary>
        /// What has happened to this skin, for the game's save (see Kept): which panels are gone and which are burnt, where it was
        /// crushed and dented and how far, which panels are bent back and how, how far it has dug into the ground. All of it in the
        /// part's own axes, as it is kept here. Nothing if nothing has happened to it.
        /// </summary>
        public ConfigNode Saved()
        {
            if (part == null || (!shown && crushes.Count == 0 && dents.Count == 0)) return null;
            var n = new ConfigNode("HULL");
            n.AddValue("part", part.persistentId);
            n.AddValue("name", part.partInfo != null ? part.partInfo.name : part.name);
            n.AddValue("cells", gone.Length);
            var g = new System.Text.StringBuilder(); var b = new System.Text.StringBuilder();
            for (int ci = 0; ci < gone.Length; ci++) { if (gone[ci]) g.Append(ci).Append(' '); if (burnt[ci]) b.Append(ci).Append(' '); }
            n.AddValue("gone", g.ToString().Trim());
            n.AddValue("burnt", b.ToString().Trim());
            foreach (Crush c in crushes) n.AddValue("crush", V(c.at) + " " + V(c.way) + " " + F(c.depth) + " " + F(c.zone));
            foreach (Dent d in dents) n.AddValue("dent", V(d.at) + " " + V(d.way) + " " + F(d.depth) + " " + F(d.radius));
            foreach (KeyValuePair<int, Peel> pe in peels) n.AddValue("peel", pe.Key + " " + V(pe.Value.hinge) + " " + V(pe.Value.axis) + " " + F(pe.Value.angle));
            n.AddValue("bite", F(bite) + " " + V(biteWay));
            n.AddValue("boxed", boxed || wantBox);
            return n;
        }

        /// <summary>
        /// (Since 0.3.0.) The same done again to this skin from the save, on loading: the panels gone are simply not there (nothing is
        /// thrown: what was is gone), the rest crushed, dented, bent back and burnt as it was, and drawn so.
        /// </summary>
        public bool Restore(ConfigNode n)
        {
            if (n == null || plan == null) return false;
            int.TryParse(n.GetValue("cells"), out int cells);
            if (cells != gone.Length) return false;                   // (another shape of part since: a variant, a mod changed)
            foreach (float f in Floats(n.GetValue("gone"))) { int ci = (int)f; if (ci >= 0 && ci < gone.Length && !gone[ci]) { gone[ci] = true; leftArea -= plan.cells[ci].area; } }
            foreach (float f in Floats(n.GetValue("burnt"))) { int ci = (int)f; if (ci >= 0 && ci < burnt.Length) burnt[ci] = true; }
            crushes.Clear(); dents.Clear(); peels.Clear();
            foreach (string s in n.GetValues("crush")) { float[] v = Floats(s); if (v.Length >= 8) crushes.Add(new Crush { at = new Vector3(v[0], v[1], v[2]), way = new Vector3(v[3], v[4], v[5]), depth = v[6], zone = v[7] }); }
            foreach (string s in n.GetValues("dent")) { float[] v = Floats(s); if (v.Length >= 8) dents.Add(new Dent { at = new Vector3(v[0], v[1], v[2]), way = new Vector3(v[3], v[4], v[5]), depth = v[6], radius = v[7] }); }
            foreach (string s in n.GetValues("peel")) { float[] v = Floats(s); if (v.Length >= 8 && (int)v[0] < gone.Length) peels[(int)v[0]] = new Peel { hinge = new Vector3(v[1], v[2], v[3]), axis = new Vector3(v[4], v[5], v[6]), angle = v[7] }; }
            float[] bw = Floats(n.GetValue("bite"));
            if (bw.Length >= 4) { bite = bw[0]; biteWay = new Vector3(bw[1], bw[2], bw[3]); }
            for (int ci = 0; ci < reshape.Length; ci++) reshape[ci] = true;
            measured = false; shapeStale = true; topology = true; dirty = true;
            Show();
            if (n.GetValue("boxed") == "True" && !boxed) wantBox = true;
            return true;
        }

        /// <summary>The panels a change below so far from a place, along a way, may have moved: to be worked out again.</summary>
        void Touch(Vector3 at, Vector3 way, float reach)
        {
            for (int ci = 0; ci < gone.Length; ci++) if (!gone[ci] && Lowest(ci, at, way) < reach) reshape[ci] = true;
            dirty = true;
        }

        /// <summary>Of the panels at the edge of a tear, so many (a share) bent back by between so many degrees.</summary>
        void Flaps(float share, float least, float most)
        {
            for (int ci = 0; ci < gone.Length; ci++)
                if (!gone[ci] && !peels.ContainsKey(ci) && Random.value < share && Borders(ci)) PeelBack(ci, Random.Range(least, most));
        }

        bool Borders(int ci)
        {
            foreach (int o in plan.cells[ci].near) if (gone[o]) return true;
            return false;
        }

        /// <summary>Bend a panel back about the longest of its edges that it still shares with a panel that is on and not bent (it is torn along the rest).</summary>
        void PeelBack(int ci, float angle)
        {
            HullPlan.Cell c = plan.cells[ci];
            int best = -1;
            float longest = 0f;
            for (int e = 0; e < c.edgeA.Length; e++)
            {
                int o = c.edgeOther[e];
                if (o < 0 || gone[o] || peels.ContainsKey(o)) continue;
                float l = (plan.p[c.edgeA[e]] - plan.p[c.edgeB[e]]).sqrMagnitude;
                if (l > longest) { longest = l; best = e; }
            }
            if (best < 0) return;
            Vector3 a = plan.p[c.edgeA[best]], b = plan.p[c.edgeB[best]];
            Vector3 axis = (b - a).normalized;
            // (outwards: so that the far side of the panel goes the way the panel faces)
            Vector3 lever = c.mid - a;
            lever -= axis * Vector3.Dot(lever, axis);
            float sign = Vector3.Dot(Vector3.Cross(axis, lever), c.normal) >= 0f ? 1f : -1f;
            peels[ci] = new Peel { hinge = a, axis = axis * sign, angle = angle };
            burnt[ci] = burnt[ci] || Random.value < 0.6f;
            reshape[ci] = true;
            dirty = true; topology = true;
        }

        // ================================================================== shape

        float Extent(Vector3 way)
        {
            Vector3 s = plan.hi - plan.lo;
            return Mathf.Abs(way.x) * s.x + Mathf.Abs(way.y) * s.y + Mathf.Abs(way.z) * s.z;
        }

        float Footprint(Vector3 way)
        {
            Vector3 s = plan.hi - plan.lo;
            return Mathf.Abs(way.x) * s.y * s.z + Mathf.Abs(way.y) * s.x * s.z + Mathf.Abs(way.z) * s.x * s.y;
        }

        static float Lateral(Vector3 d, Vector3 way) => (d - way * Vector3.Dot(d, way)).magnitude;

        float Highest(int ci, Vector3 at, Vector3 way)
        {
            HullPlan.Cell c = plan.cells[ci];
            Vector3 d0 = c.lo - at, d1 = c.hi - at;
            return Mathf.Max(d0.x * way.x, d1.x * way.x) + Mathf.Max(d0.y * way.y, d1.y * way.y) + Mathf.Max(d0.z * way.z, d1.z * way.z);
        }

        float Lowest(int ci, Vector3 at, Vector3 way)
        {
            HullPlan.Cell c = plan.cells[ci];
            Vector3 d0 = c.lo - at, d1 = c.hi - at;
            return Mathf.Min(d0.x * way.x, d1.x * way.x) + Mathf.Min(d0.y * way.y, d1.y * way.y) + Mathf.Min(d0.z * way.z, d1.z * way.z);
        }

        static int rebuildFrame, rebuildsNow, firstsWanted, firstsWaiting;
        const float BudgetMs = 3f;
        static float budgetMs = BudgetMs;
        public static long RebuildTicks;
        public static int Rebuilds;
        bool built;

        bool wantBox;
        static int boxFrame, boxesNow;

        void Update()
        {
            if (plan == null || part == null) return;
            if (wantBox && !boxed)
            {
                if (Time.frameCount != boxFrame) { boxFrame = Time.frameCount; boxesNow = 0; }
                if (boxesNow < 2)
                {
                    boxesNow++;
                    wantBox = false;
                    long t = Prof.Now;
                    UseBox();
                    Prof.Add(Prof.Shape, t);
                }
            }
            if (dirty && shown && Time.unscaledTime >= shapeDue)
            {
                // (a few times a second at most, however often it is hurt: each time is every corner of the skin; and in a big crash a
                // few parts a frame, those not yet drawn at all first)
                // (one a frame: a part's first drawing, the most work, before any other's redrawing; a dozen parts hurt at once are all
                // drawn torn within a fifth of a second, and no frame takes them all)
                // (and redrawings no more than about three milliseconds a frame on average: a dozen parts being ground along the ground at once
                // are redrawn in turn, each a few times a second, rather than one every frame. A first drawing is not held back for that)
                if (Time.frameCount != rebuildFrame)
                {
                    rebuildFrame = Time.frameCount; rebuildsNow = 0; firstsWaiting = firstsWanted; firstsWanted = 0;
                    budgetMs = Mathf.Min(budgetMs + BudgetMs, 3f * BudgetMs);
                }
                if (!built) firstsWanted++;
                if (rebuildsNow >= 1 || (built && (firstsWaiting > 0 || budgetMs <= 0f))) return;
                rebuildsNow++;
                if (!built && firstsWaiting > 0) firstsWaiting--;
                bool firstTime = !built;
                shapeDue = Time.unscaledTime + 0.2f;
                long began = System.Diagnostics.Stopwatch.GetTimestamp();
                Rebuild();
                long tm = Prof.Now;
                Measure();
                if (!built)
                {
                    built = true;
                    drawn.enabled = true;
                    foreach (Renderer r in hiddenRenderers) if (r != null) r.enabled = false;
                }
                if (boxed) Fit();
                Prof.Add(Prof.Measure, tm);
                tm = Prof.Now;
                if (prefabMass < 0f) prefabMass = part.prefabMass;
                part.prefabMass = Mathf.Max(0.0005f, prefabMass * Mathf.Max(0.08f, Left));
                try { part.UpdateMass(); } catch (Exception) { }
                Prof.Add(Prof.Mass, tm);
                long took = System.Diagnostics.Stopwatch.GetTimestamp() - began;
                budgetMs -= (float)(took * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
                Prof.Ticks[firstTime ? Prof.FirstSkin : Prof.Skin] += took;
                Crashes.FixedTicks += took;
                RebuildTicks += took;
                Rebuilds++;
            }
        }

        Vector3 boxLo, boxHi;
        bool measured, anyLeft = true;

        /// <summary>The box round what is left of the skin as it is now drawn (worked out again when it changes, not every step).</summary>
        void Measure()
        {
            Vector3 lo = Vector3.one * float.MaxValue, hi = Vector3.one * float.MinValue;
            bool any = false;
            for (int ci = 0; ci < gone.Length; ci++)
            {
                // (a flap bent back is not counted: it would stand out of the box into the ground, and the ground throw the part up)
                if (gone[ci] || peels.ContainsKey(ci)) continue;
                foreach (int k in plan.cells[ci].tris)
                    for (int c = 0; c < 3; c++) { Vector3 v = pos[k * 3 + c]; lo = Vector3.Min(lo, v); hi = Vector3.Max(hi, v); }
                any = true;
            }
            // (and no bigger than the part was: folds bulging out are drawn, not stood on)
            if (any) { lo = Vector3.Max(lo, plan.lo); hi = Vector3.Min(hi, plan.hi); hi = Vector3.Max(hi, lo + Vector3.one * 0.05f); }
            boxLo = lo; boxHi = hi; anyLeft = any; measured = true;
        }

        /// <summary>The bite kept, its way as it was (it is wearing off).</summary>
        public void SetBite(float depth)
        {
            if (Mathf.Abs(depth - bite) < 0.02f) return;
            bite = depth;
            if (boxed) Fit();
        }

        /// <summary>
        /// What stands in for the part's colliders: the convex hull round what is left of its skin (a few corners of each panel still on, and
        /// as many on its inside, within the part as it was), made again when panels come off (a few times a second at most); set back into the
        /// part on the side that has dug into the ground by as much as it has, so that it sits down in the ground.
        /// </summary>
        void Fit()
        {
            if (box == null) return;
            if (!measured) Measure();
            if (!anyLeft) { box.enabled = false; return; }
            if (hullShape != null && (shapeMesh == null || shapeStale) && Time.unscaledTime - shapeAt > 0.25f)
            {
                long t = Prof.Now;
                shapeAt = Time.unscaledTime;
                shapeStale = false;
                if (!Shape()) { Destroy(hullShape); hullShape = null; BoxCollider b = boxObject.AddComponent<BoxCollider>(); b.sharedMaterial = box.sharedMaterial; box = b; Crashes.Own(b, part.GetComponent<Hits>()); try { if (part.vessel != null) CollisionManager.IgnoreCollidersOnVessel(part.vessel, b); } catch (Exception) { } }
                Prof.Add(Prof.Shape, t);
            }
            if (box is BoxCollider bc)
            {
                Vector3 lo = boxLo, hi = boxHi;
                bc.center = (lo + hi) * 0.5f;
                bc.size = Vector3.Max(hi - lo, Vector3.one * 0.1f);
            }
            boxObject.transform.localPosition = bite > 0.01f ? -biteWay * bite : Vector3.zero;
        }

        bool shapeStale = true;
        static readonly List<Vector3> shapePoints = new List<Vector3>();
        static readonly List<int> shapeTris = new List<int>();

        /// <summary>The hull's points: some corners of each panel still on (not those bent back), and the same a wall's thickness and more inside.</summary>
        bool Shape()
        {
            shapePoints.Clear();
            int cells = 0;
            for (int ci = 0; ci < gone.Length; ci++) if (!gone[ci] && !peels.ContainsKey(ci)) cells++;
            if (cells == 0) return false;
            int each = Mathf.Clamp(120 / cells, 2, 8);
            float inward = Mathf.Max(plan.thick, 0.04f);
            for (int ci = 0; ci < gone.Length; ci++)
            {
                if (gone[ci] || peels.ContainsKey(ci)) continue;
                int[] tris = plan.cells[ci].tris;
                int step = Mathf.Max(1, tris.Length * 3 / each);
                for (int j = 0; j < tris.Length * 3; j += step)
                {
                    int i = tris[j / 3] * 3 + j % 3;
                    Vector3 v = Vector3.Max(plan.lo, Vector3.Min(plan.hi, pos[i]));
                    shapePoints.Add(v);
                    shapePoints.Add(v - nrm[i] * inward);
                }
            }
            if (shapePoints.Count < 4) return false;
            shapeTris.Clear();
            for (int k = 0; k + 2 < shapePoints.Count; k += 3) { shapeTris.Add(k); shapeTris.Add(k + 1); shapeTris.Add(k + 2); }
            if (shapeTris.Count == 0) return false;
            try
            {
                if (shapeMesh == null) shapeMesh = new Mesh { name = "Fragmentation hull shape" };
                shapeMesh.Clear();
                shapeMesh.SetVertices(shapePoints);
                shapeMesh.SetTriangles(shapeTris, 0);
                hullShape.sharedMesh = null;
                hullShape.sharedMesh = shapeMesh;
                return hullShape.sharedMesh != null && hullShape.bounds.size.sqrMagnitude > 1e-6f;
            }
            catch (Exception) { return false; }
        }

        static readonly int[] crushOn = new int[8], dentOn = new int[8];

        /// <summary>
        /// Where the corners of one panel are now (the part's axes), and which way each faces, into pos and nrm and the mesh's own copies. Only
        /// what bears on this panel is gone through for each corner (the crushes it lies within, the dents it is near); a panel nothing bears on
        /// is as it was.
        /// </summary>
        void ReshapeCell(int ci, int V, float th)
        {
            HullPlan.Cell cell = plan.cells[ci];
            int nc = 0, nd = 0;
            for (int k = 0; k < crushes.Count && nc < crushOn.Length; k++)
            {
                Crush c = crushes[k];
                if (Lowest(ci, c.at, c.way) < (c.zone <= 0.05f ? c.depth : c.zone)) crushOn[nc++] = k;
            }
            for (int k = 0; k < dents.Count && nd < dentOn.Length; k++)
            {
                Dent e = dents[k];
                Vector3 nearest = Vector3.Max(cell.lo, Vector3.Min(cell.hi, e.at));
                if ((nearest - e.at).sqrMagnitude < e.radius * e.radius) dentOn[nd++] = k;
            }
            bool turned = peels.TryGetValue(ci, out Peel pe);
            int[] tris = cell.tris;
            if (nc == 0 && nd == 0 && !turned)
            {
                for (int t = 0; t < tris.Length; t++)
                {
                    int i = tris[t] * 3;
                    for (int q = 0; q < 3; q++)
                    {
                        int j = i + q;
                        pos[j] = plan.p[j]; nrm[j] = plan.n[j];
                    }
                }
                return;
            }
            Quaternion turn = turned ? Quaternion.AngleAxis(pe.angle, pe.axis) : Quaternion.identity;
            float freq = 2.2f / Mathf.Max(0.15f, plan.edge * 0.5f);
            // (Since 0.3.0: an engine's meshes are whole, not panels, so its folds were reckoned from a whole bell's size, a metre or
            // more long: a crushed bell was bent, not crumpled. Thin steel crushed folds in lengths of about 1.8 times the square root
            // of its radius times its wall, under a tenth of a metre for a bell; on a bell's few points, folds a few to the metre and
            // a little deeper show as a crumpled cone.)
            bool steel = plan.engine;
            if (steel) freq = Mathf.Max(freq, 12f);
            if (fold == null) { fold = new float[plan.p.Length]; for (int f = 0; f < fold.Length; f++) fold[f] = float.NaN; }
            for (int t = 0; t < tris.Length; t++)
            {
                int i = tris[t] * 3;
                float m0 = 0f, m1 = 0f, m2 = 0f;
                for (int q = 0; q < 3; q++)
                {
                    int j = i + q;
                    Vector3 x = plan.p[j], n = plan.n[j];
                    float dx = 0f, dy = 0f, dz = 0f, crumple = 0f;
                    for (int a = 0; a < nc; a++)
                    {
                        Crush c = crushes[crushOn[a]];
                        float h = (x.x - c.at.x) * c.way.x + (x.y - c.at.y) * c.way.y + (x.z - c.at.z) * c.way.z;
                        if (c.zone <= 0.05f)
                        {
                            // ground away to a plane: what is below it is brought up to it, a little torn
                            if (h < c.depth) { float u = c.depth - h; dx += c.way.x * u; dy += c.way.y * u; dz += c.way.z * u; crumple += 0.25f * u + 0.02f; }
                        }
                        else if (h < c.zone)
                        {
                            float s = c.depth * (1f - (h > 0f ? h : 0f) / c.zone);
                            dx += c.way.x * s; dy += c.way.y * s; dz += c.way.z * s;
                            crumple += s;
                        }
                    }
                    for (int a = 0; a < nd; a++)
                    {
                        Dent e = dents[dentOn[a]];
                        float ex = x.x - e.at.x, ey = x.y - e.at.y, ez = x.z - e.at.z, r2 = ex * ex + ey * ey + ez * ez, R2 = e.radius * e.radius;
                        if (r2 >= R2) continue;
                        float f = 1f - r2 / R2;
                        f *= f;
                        float s = e.depth * f;
                        dx += e.way.x * s; dy += e.way.y * s; dz += e.way.z * s;
                        crumple += 0.5f * s;
                    }
                    if (crumple > 0f)
                    {
                        // Folds: thin skin pushed in does not stay smooth. Bulging out more than in, as a tube driven end on folds.
                        float bend = fold[j];
                        if (float.IsNaN(bend)) bend = fold[j] = HullPlan.Noise(x * freq, 3);
                        float s = (crumple < 0.6f ? crumple : 0.6f) * (0.45f * bend + 0.15f) * (steel ? 1.35f : 1f);
                        dx += n.x * s; dy += n.y * s; dz += n.z * s;
                    }
                    float moved = Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
                    Vector3 y = new Vector3(x.x + dx, x.y + dy, x.z + dz);
                    if (turned) { y = pe.hinge + turn * (y - pe.hinge); moved += 0.05f; }
                    pos[j] = y;
                    if (q == 0) m0 = moved; else if (q == 1) m1 = moved; else m2 = moved;
                }
                Vector3 p0 = pos[i], p1 = pos[i + 1], p2 = pos[i + 2];
                Vector3 face = Vector3.Cross(p1 - p0, p2 - p0);
                face = face.sqrMagnitude > 1e-14f ? face.normalized : plan.n[i];
                nrm[i] = Blend(turned ? turn * plan.n[i] : plan.n[i], face, m0);
                nrm[i + 1] = Blend(turned ? turn * plan.n[i + 1] : plan.n[i + 1], face, m1);
                nrm[i + 2] = Blend(turned ? turn * plan.n[i + 2] : plan.n[i + 2], face, m2);
            }
        }

        /// <summary>
        /// Draw the skin as it now is: every panel still on, its inside (once there is a hole to see it through), and the torn edges of every
        /// hole. Only the panels a change touched have their corners worked out again; the lists of which triangles are drawn with what are
        /// made again only when panels have come off, been bent back or blackened (and the torn edges whenever either changed).
        /// </summary>
        void Rebuild()
        {
            dirty = false;
            if (mesh == null) return;
            int V = plan.p.Length, slots = slotMats.Length;
            float th = plan.thick;
            if (vb == null || vb.Length < V * 2 + 4096) Grow(V * 2 + 4096);
            // ---- where the corners of the panels that were touched are now
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            for (int ci = 0; ci < gone.Length; ci++)
            {
                if (gone[ci] || !reshape[ci]) continue;
                reshape[ci] = false;
                ReshapeCell(ci, V, th);
            }
            // (every corner, outside and in, from this skin's own: the buffers were last filled by another part's skin)
            for (int i = 0; i < V; i++)
            {
                Vector3 p = pos[i], n = nrm[i];
                vb[i] = p; nb[i] = n;
                vb[V + i] = new Vector3(p.x - n.x * th, p.y - n.y * th, p.z - n.z * th); nb[V + i] = new Vector3(-n.x, -n.y, -n.z);
            }
            long t1 = System.Diagnostics.Stopwatch.GetTimestamp();
            if (inside == null) inside = Inside();
            bool open = leftArea < plan.area * 0.9999f || peels.Count > 0;
            int count = slots * 2 + 1;
            if (lists == null || lists.Length != count) { lists = new List<int>[count]; for (int s2 = 0; s2 < count; s2++) lists[s2] = new List<int>(); topology = true; }
            List<int> inner = lists[count - 1];
            int was = vertexCount;
            // (the torn edges written again at every redrawing, as the corners are)
            bool edges = open;
            if (topology)
            {
                foreach (List<int> l in lists) l.Clear();
                for (int ci = 0; ci < gone.Length; ci++)
                {
                    if (gone[ci]) continue;
                    bool black = burnt[ci];
                    foreach (int k in plan.cells[ci].tris)
                    {
                        int i = k * 3;
                        List<int> outer = lists[plan.slot[k] + (black ? slots : 0)];
                        outer.Add(i); outer.Add(i + 1); outer.Add(i + 2);
                        if (open) { inner.Add(V + i); inner.Add(V + i + 2); inner.Add(V + i + 1); }
                    }
                }
                innerOwn = inner.Count;
            }
            else if (edges && inner.Count > innerOwn) inner.RemoveRange(innerOwn, inner.Count - innerOwn);    // (the inside's own triangles kept; the edges made again)
            if (edges)
            {
                int strips = V * 2;
                for (int ci = 0; ci < gone.Length; ci++)
                {
                    if (gone[ci]) continue;
                    HullPlan.Cell c = plan.cells[ci];
                    bool bent = peels.ContainsKey(ci);
                    for (int e = 0; e < c.edgeA.Length; e++)
                    {
                        int o = c.edgeOther[e];
                        if (o < 0) continue;
                        if (!gone[o] && peels.ContainsKey(o) == bent) continue;
                        if (strips + 4 > vb.Length) Grow(strips + 4096);
                        int a = c.edgeA[e], b = c.edgeB[e];
                        int s0 = strips;
                        vb[s0] = pos[a]; vb[s0 + 1] = pos[b]; vb[s0 + 2] = pos[b] - nrm[b] * th; vb[s0 + 3] = pos[a] - nrm[a] * th;
                        Vector3 side = Vector3.Cross(pos[b] - pos[a], nrm[a] + nrm[b]);
                        side = side.sqrMagnitude > 1e-12f ? side.normalized : Vector3.up;
                        for (int q = 0; q < 4; q++) { nb[s0 + q] = side; tb[s0 + q] = new Vector4(1f, 0f, 0f, 1f); }
                        ub[s0] = new Vector2(0.1f, 0.85f); ub[s0 + 1] = new Vector2(0.9f, 0.85f); ub[s0 + 2] = new Vector2(0.9f, 0.95f); ub[s0 + 3] = new Vector2(0.1f, 0.95f);
                        // (both ways round: which way the hole is from this edge is not worth working out)
                        inner.Add(s0); inner.Add(s0 + 1); inner.Add(s0 + 2); inner.Add(s0); inner.Add(s0 + 2); inner.Add(s0 + 3);
                        inner.Add(s0); inner.Add(s0 + 2); inner.Add(s0 + 1); inner.Add(s0); inner.Add(s0 + 3); inner.Add(s0 + 2);
                        strips += 4;
                    }
                }
                vertexCount = strips;
            }
            else if (topology || vertexCount == 0) vertexCount = V * 2;
            long t2 = System.Diagnostics.Stopwatch.GetTimestamp();
            // (which triangles there are changed: the mesh is laid out afresh; only corners moved: their places are handed over again, and nothing else)
            bool relist = topology || edges || vertexCount != was;
            if (vertexCount > 65000 && mesh.indexFormat != UnityEngine.Rendering.IndexFormat.UInt32) { mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32; relist = true; }
            if (relist) mesh.Clear();
            mesh.SetVertices(vb, 0, vertexCount);
            mesh.SetNormals(nb, 0, vertexCount);
            if (relist)
            {
                PlanUV(V);
                mesh.SetUVs(0, ub, 0, vertexCount);
                mesh.SetTangents(tb, 0, vertexCount);
                // ---- only the lists that have something in them, each with its material
                int used = 0;
                foreach (List<int> l in lists) if (l.Count > 0) used++;
                mesh.subMeshCount = Mathf.Max(1, used);
                var mats = new Material[Mathf.Max(1, used)];
                var tints = new Color[mats.Length];
                int sub = 0;
                for (int s2 = 0; s2 < count; s2++)
                {
                    if (lists[s2].Count == 0) continue;
                    mesh.SetTriangles(lists[s2], sub, false);
                    if (s2 == count - 1) { mats[sub] = inside; tints[sub] = new Color(0.86f, 0.85f, 0.82f, 1f); }
                    else
                    {
                        int slot = s2 % slots;
                        mats[sub] = slotMats[slot];
                        Color c = slotColours[slot];
                        // (blackened: soot over paint; the darker the paint the less it shows)
                        tints[sub] = s2 >= slots ? new Color(c.r * 0.3f + 0.035f, c.g * 0.28f + 0.033f, c.b * 0.26f + 0.03f, c.a) : c;
                    }
                    sub++;
                }
                drawn.sharedMaterials = mats;
                block = block ?? new MaterialPropertyBlock();
                for (int s2 = 0; s2 < mats.Length; s2++)
                {
                    drawn.GetPropertyBlock(block, s2);
                    block.SetColor(ColorId, tints[s2]);
                    drawn.SetPropertyBlock(block, s2);
                }
            }
            mesh.RecalculateBounds();
            topology = false;
            long t3 = System.Diagnostics.Stopwatch.GetTimestamp();
            ShapeTicks += t1 - t0; ListTicks += t2 - t1; UploadTicks += t3 - t2;
        }

        public static long ShapeTicks, ListTicks, UploadTicks;

        int innerOwn;

        static void Grow(int room)
        {
            Array.Resize(ref vb, room); Array.Resize(ref nb, room); Array.Resize(ref ub, room); Array.Resize(ref tb, room);
        }

        /// <summary>The corners' texture places and tangents, outside and in, into the shared buffers: this kind of part's own, worked out once.</summary>
        void PlanUV(int V)
        {
            if (plan.uv2 == null || plan.uv2.Length != V * 2)
            {
                var u = new Vector2[V * 2];
                var t = new Vector4[V * 2];
                for (int i = 0; i < V; i++)
                {
                    u[i] = plan.uv[i]; t[i] = plan.t[i];
                    Vector3 q = plan.p[i];
                    u[V + i] = new Vector2((q.x + q.z) * 0.9f, Mathf.Repeat(q.y * 0.63f, 0.7f));
                    t[V + i] = new Vector4(1f, 0f, 0f, -1f);
                }
                plan.uv2 = u; plan.t2 = t;
            }
            Array.Copy(plan.uv2, ub, V * 2);
            Array.Copy(plan.t2, tb, V * 2);
        }

        static Vector3 Blend(Vector3 smooth, Vector3 face, float moved)
        {
            float k = Mathf.Clamp01(moved / 0.05f);
            if (k <= 0f) return smooth;
            Vector3 n = Vector3.Lerp(smooth, Vector3.Dot(face, smooth) < 0f ? -face : face, k);
            return n.sqrMagnitude > 1e-12f ? n.normalized : smooth;
        }

        /// <summary>
        /// The inside of a skin, and its torn edges: bare metal, grey and a little mottled (the edges, in the top band of the picture,
        /// lighter). (Since 0.3.0, the structure inside: a tank's wall is held round by ring frames, flanges standing in from the
        /// wall every metre or so, with lighter ribs between, which a torn tank shows through its holes. Painted on the inside as
        /// it comes round every 1.1 m up the part (its picture's height there): each frame its lit top edge, its face and the
        /// shadow it throws down the wall, so that it follows every dent and tear of the wall it is on.)
        /// </summary>
        public static Material Inside()
        {
            if (inside != null) return inside;
            Shader s = Shader.Find("KSP/Specular") ?? Shader.Find("KSP/Diffuse") ?? Shader.Find("Diffuse");
            const int W = 64, H = 128;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, true) { name = "Fragmentation inside", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            var px = new Color[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float n = 0.5f + 0.5f * HullPlan.Noise(new Vector3(x * 0.2f, y * 0.1f, 0f), 7) * 0.6f + 0.15f * HullPlan.Noise(new Vector3(x * 0.9f, y * 0.45f, 0f), 9);
                    float u = y / (float)H;
                    float v;
                    if (u >= 0.82f) v = 0.8f + 0.12f * n;                                   // (the torn edges: bright bare metal)
                    else
                    {
                        v = 0.5f + 0.16f * n;
                        // (the inside's own height runs 0 to 0.7 of the picture, one frame in each: its flange's lit top, its face,
                        // the shadow under it; and two lighter ribs between)
                        float f = u - 0.3f;
                        if (f >= 0f && f < 0.008f) v = 0.78f + 0.08f * n;
                        else if (f >= 0.008f && f < 0.03f) v = 0.6f + 0.1f * n;
                        else if (f >= 0.03f && f < 0.05f) v *= Mathf.Lerp(0.55f, 1f, (f - 0.03f) / 0.02f);
                        foreach (float rib in new[] { 0.07f, 0.53f })
                        {
                            float g = u - rib;
                            if (g >= 0f && g < 0.005f) v = Mathf.Max(v, 0.66f + 0.06f * n);
                            else if (g >= 0.005f && g < 0.016f) v *= 0.82f;
                        }
                    }
                    px[y * W + x] = new Color(v, v * 0.99f, v * 0.96f);
                }
            tex.SetPixels(px);
            tex.Apply(true, true);
            var m = new Material(s) { name = "Fragmentation inside" };
            m.mainTexture = tex;
            if (m.HasProperty("_SpecColor")) m.SetColor("_SpecColor", new Color(0.35f, 0.35f, 0.35f));
            if (m.HasProperty("_Shininess")) m.SetFloat("_Shininess", 0.25f);
            return m;
        }

        // ================================================================== pieces

        /// <summary>
        /// A piece made of some of the panels (as they are now): its mesh laid out about its own middle (the second axis the way it faces),
        /// with its inside and its torn edges, bent and crumpled by so much (0 to 1), and blackened by so much; and where it is, how it is
        /// turned, how big it is, and what it is drawn with.
        /// </summary>
        // (what PieceOf builds a piece in, kept from one piece to the next: made afresh for each, the lists, the set, the map and a
        // little array a triangle came to tens of kilobytes a piece, seventy pieces in two or three frames of a crash (2026-10-24,
        // the PC). PieceOf runs on the game's thread alone.)
        static readonly List<int> pieceTris = new List<int>();
        static readonly HashSet<int> pieceCells = new HashSet<int>();
        static readonly List<Vector3> pieceV = new List<Vector3>(), pieceN = new List<Vector3>();
        static readonly List<Vector2> pieceU = new List<Vector2>();
        static readonly List<Vector4> pieceT = new List<Vector4>();
        static List<int>[] pieceSubs = new List<int>[0];
        static int[] pieceAt = new int[0], pieceAtStamp = new int[0];
        static int pieceStamp;

        public Mesh PieceOf(List<int> cells, float crumple, float soot, out Vector3 worldMiddle, out Quaternion turn, out Vector3 extent, out float area, out Material[] mats, out Color[] tints)
        {
            int slots = slotMats.Length;
            List<int> tris = pieceTris;
            tris.Clear();
            area = 0f;
            Vector3 mid = Vector3.zero, facing = Vector3.zero;
            HashSet<int> inSet = pieceCells;
            inSet.Clear();
            foreach (int ci in cells) inSet.Add(ci);
            foreach (int ci in cells)
            {
                HullPlan.Cell c = plan.cells[ci];
                tris.AddRange(c.tris);
                area += c.area;
                mid += c.mid * c.area;
                facing += c.normal * c.area;
            }
            mid /= Mathf.Max(1e-6f, area);
            Vector3 up = facing.sqrMagnitude > 1e-8f ? facing.normalized : Vector3.up;
            Vector3 far = Vector3.zero;
            float farthest = -1f;
            foreach (int k in tris)
                for (int c = 0; c < 3; c++)
                {
                    Vector3 d = pos[k * 3 + c] - mid;
                    d -= up * Vector3.Dot(d, up);
                    if (d.sqrMagnitude > farthest) { farthest = d.sqrMagnitude; far = d; }
                }
            Vector3 right = far.sqrMagnitude > 1e-8f ? far.normalized : Vector3.Cross(up, Mathf.Abs(up.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 forward = Vector3.Cross(right, up);
            Quaternion local = Quaternion.LookRotation(forward, up);
            Quaternion back = Quaternion.Inverse(local);
            // ---- bent: folded about a line across it, by up to seventy degrees; and crumpled
            Vector3 foldAxis = Quaternion.AngleAxis(Random.Range(0f, 180f), Vector3.up) * Vector3.right;
            Vector3 foldAcross = Vector3.Cross(Vector3.up, foldAxis);
            float foldAt = Random.Range(-0.3f, 0.3f) * Mathf.Sqrt(farthest), foldAngle = crumple * Random.Range(15f, 70f) * (Random.value < 0.5f ? -1f : 1f);
            Quaternion fold = Quaternion.AngleAxis(foldAngle, foldAxis);
            float wrinkle = crumple * Mathf.Clamp(Mathf.Sqrt(Mathf.Max(farthest, 1e-6f)) * 0.08f, 0.01f, 0.12f);
            int seed = Random.Range(0, 10000);
            float th = plan.thick;
            List<Vector3> v = pieceV, nn = pieceN;
            List<Vector2> u = pieceU;
            List<Vector4> tg = pieceT;
            v.Clear(); nn.Clear(); u.Clear(); tg.Clear();
            if (pieceSubs.Length < slots + 1) Array.Resize(ref pieceSubs, slots + 1);
            for (int s = 0; s <= slots; s++) { if (pieceSubs[s] == null) pieceSubs[s] = new List<int>(); else pieceSubs[s].Clear(); }
            List<int>[] subs = pieceSubs;
            Vector3 lo = Vector3.one * float.MaxValue, hi = Vector3.one * float.MinValue;
            Vector3 Bend(Vector3 x)
            {
                if (crumple <= 0f) return x;
                Vector3 y = x;
                if (Vector3.Dot(x, foldAcross) > foldAt) y = foldAcross * foldAt + fold * (x - foldAcross * foldAt);
                y += Vector3.up * (wrinkle * HullPlan.Noise(x * 4.5f, seed));
                return y;
            }
            // (where each of the skin's corners went in the piece, by the corner's number: stamped, so as not to be cleared each time)
            if (pieceAt.Length < pos.Length) { pieceAt = new int[pos.Length]; pieceAtStamp = new int[pos.Length]; pieceStamp = 0; }
            if (pieceStamp == int.MaxValue) { Array.Clear(pieceAtStamp, 0, pieceAtStamp.Length); pieceStamp = 0; }
            int stamp = ++pieceStamp;
            foreach (int k in tris)
            {
                int i = k * 3, start = v.Count;
                Vector3 a = Bend(back * (pos[i] - mid)), b = Bend(back * (pos[i + 1] - mid)), c = Bend(back * (pos[i + 2] - mid));
                Vector3 face = Vector3.Cross(b - a, c - a);
                face = face.sqrMagnitude > 1e-14f ? face.normalized : back * nrm[i];
                for (int q = 0; q < 3; q++)
                {
                    Vector3 corner = q == 0 ? a : q == 1 ? b : c;
                    Vector3 n0 = back * nrm[i + q];
                    Vector3 n1 = crumple > 0f ? Blend(n0, face, crumple * 0.06f + 0.01f) : n0;
                    v.Add(corner); nn.Add(n1); u.Add(plan.uv[i + q]); tg.Add(plan.t[i + q]);
                    lo = Vector3.Min(lo, corner); hi = Vector3.Max(hi, corner);
                }
                for (int q = 0; q < 3; q++)
                {
                    Vector3 corner = q == 0 ? a : q == 1 ? b : c;
                    v.Add(corner - nn[start + q] * th); nn.Add(-nn[start + q]);
                    Vector3 pp = plan.p[i + q];
                    u.Add(new Vector2((pp.x + pp.z) * 0.9f, Mathf.Repeat(pp.y * 0.63f, 0.7f))); tg.Add(new Vector4(1f, 0f, 0f, -1f));
                }
                List<int> outer = subs[plan.slot[k]];
                outer.Add(start); outer.Add(start + 1); outer.Add(start + 2);
                subs[slots].Add(start + 3); subs[slots].Add(start + 5); subs[slots].Add(start + 4);
                pieceAt[i] = start; pieceAt[i + 1] = start + 1; pieceAt[i + 2] = start + 2;
                pieceAtStamp[i] = stamp; pieceAtStamp[i + 1] = stamp; pieceAtStamp[i + 2] = stamp;
            }
            // ---- its torn edges, all round
            foreach (int ci in cells)
            {
                HullPlan.Cell c = plan.cells[ci];
                for (int e = 0; e < c.edgeA.Length; e++)
                {
                    int o = c.edgeOther[e];
                    if (o >= 0 && inSet.Contains(o)) continue;
                    int ea = c.edgeA[e], eb = c.edgeB[e];
                    if (pieceAtStamp[ea] != stamp || pieceAtStamp[eb] != stamp) continue;
                    int ia = pieceAt[ea], ib = pieceAt[eb];
                    int s0 = v.Count;
                    v.Add(v[ia]); v.Add(v[ib]); v.Add(v[ib + 3]); v.Add(v[ia + 3]);
                    Vector3 side = Vector3.Cross(v[ib] - v[ia], nn[ia] + nn[ib]);
                    side = side.sqrMagnitude > 1e-12f ? side.normalized : Vector3.up;
                    for (int q = 0; q < 4; q++) { nn.Add(side); tg.Add(new Vector4(1f, 0f, 0f, 1f)); }
                    u.Add(new Vector2(0.1f, 0.85f)); u.Add(new Vector2(0.9f, 0.85f)); u.Add(new Vector2(0.9f, 0.95f)); u.Add(new Vector2(0.1f, 0.95f));
                    List<int> l = subs[slots];
                    l.Add(s0); l.Add(s0 + 1); l.Add(s0 + 2); l.Add(s0); l.Add(s0 + 2); l.Add(s0 + 3);
                    l.Add(s0); l.Add(s0 + 2); l.Add(s0 + 1); l.Add(s0); l.Add(s0 + 3); l.Add(s0 + 2);
                }
            }
            extent = hi - lo;
            Vector3 centre = (lo + hi) * 0.5f;
            for (int q = 0; q < v.Count; q++) v[q] -= centre;
            var m = new Mesh { name = "Fragmentation piece" };
            if (v.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(v); m.SetNormals(nn); m.SetUVs(0, u); m.SetTangents(tg);
            int used = 0;
            for (int s = 0; s <= slots; s++) if (subs[s].Count > 0) used++;
            m.subMeshCount = Mathf.Max(1, used);
            mats = new Material[Mathf.Max(1, used)];
            tints = new Color[mats.Length];
            if (inside == null) inside = Inside();
            int sub = 0;
            for (int s = 0; s <= slots; s++)
            {
                if (subs[s].Count == 0) continue;
                m.SetTriangles(subs[s], sub);
                if (s == slots) { mats[sub] = inside; tints[sub] = new Color(0.86f * Mathf.Lerp(1f, 0.3f, soot), 0.85f * Mathf.Lerp(1f, 0.29f, soot), 0.82f * Mathf.Lerp(1f, 0.28f, soot), 1f); }
                else
                {
                    mats[sub] = slotMats[s];
                    Color c = slotColours[s];
                    float k = Mathf.Lerp(1f, 0.2f, soot);
                    tints[sub] = new Color(c.r * k + 0.02f * soot, c.g * k * 0.98f + 0.02f * soot, c.b * k * 0.95f + 0.02f * soot, c.a);
                }
                sub++;
            }
            m.RecalculateBounds();
            turn = part.transform.rotation * local;
            worldMiddle = part.transform.TransformPoint(mid + local * centre);
            return m;
        }

        public bool Burnt(int cell) => burnt[cell];

        /// <summary>How deep the deepest grinding has gone (metres into the part).</summary>
        public float GroundDepth { get { float d = 0f; foreach (Crush c in crushes) if (c.zone <= 0.05f) d = Mathf.Max(d, c.depth); return d; } }

        public bool Whole(int cell) => plan.cells[cell].whole;

        public Vector3 WorldMiddle(int cell) => part.transform.TransformPoint(plan.cells[cell].mid);

        public Vector3 WorldFacing(int cell) => part.transform.TransformDirection(plan.cells[cell].normal);

        /// <summary>Not drawn any more, at once (the part is going: see Crashes.Hide).</summary>
        public void Hide()
        {
            if (drawn != null) drawn.enabled = false;
            if (drawnObject != null) drawnObject.SetActive(false);
        }

#if DEV
        /// <summary>(development build) The part as it was before anything happened to its skin: its own renderers and colliders back, this gone (for trying the save's damage done again).</summary>
        public void Mend()
        {
            foreach (Renderer r in hiddenRenderers) if (r != null) r.enabled = true;
            foreach (Collider c in hidden) if (c != null) c.enabled = true;
            Destroy(this);
        }
#endif

        void OnDestroy()
        {
            if (drawnObject != null) Destroy(drawnObject);
            if (boxObject != null) Destroy(boxObject);
            if (mesh != null) Destroy(mesh);
        }
    }
}
