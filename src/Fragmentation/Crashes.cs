using System;
using System.Collections.Generic;
using KSP.Localization;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Fragmentation
{
    /// <summary>A craft that this mod is watching because it may hit something.</summary>
    public sealed class Wreck
    {
        public Vessel vessel;
        public readonly Body body = new Body();
        public readonly List<Hits> hits = new List<Hits>();
        public int partsSeen = -1;
        public float riskUntil, touched = -100f, since, calmSince;
        public bool inContact, calm;
        public bool wrecked;                 // hurt by a blow or by being dragged, or a piece come off a craft that was: it no longer bounds or rolls (see Blow, Slide)
        public float lastGround = -100f;     // when it last touched the ground (Time.fixedTime)
        public float slid, fastest, hardest;
        public Vector3 dustAt, dustNormal, dustGoing;
        public float dustSpeed, dustSize;
        public bool dustPaved;
        // ---- what the ground under it is (since 0.3.0: see Ground.KindAt), looked up twice a second
        public Ground.Feel feel = Ground.Soft;
        public float feelAt = -10f;
        public string name = "";
        // ---- its fire, after a tank went up on it: where (on which part, in that part's axes), how big, and until when
        public Part firePart;
        public Vector3 fireLocal;
        public float fireUntil, fireBig, fireBegan;
        // ---- where it first touched anything (a place on the planet), for saying how far it went from there
        public bool firstKnown;
        public double firstLat, firstLon;
    }

    /// <summary>A tank split open: what is in it comes out for a moment before the rest goes up.</summary>
    public sealed class Leak
    {
        public Hits h;
        public Wreck wreck;
        public Vector3 local, dir;           // where the hole is and which way it faces, in the part's own axes
        public float began, until;
        public bool lit;
        public float flow;                   // tonnes a second, last step
        public double had;                   // kilograms of propellant when it split
        public int tint;                     // the fire's colours, as the explosions mod has them: 0 fuel, 1 solid propellant, 2 monopropellant
        public float shade = 0.06f, warm = 0.35f;     // its smoke where it burns: how light, how brown
    }

    /// <summary>
    /// The crashes themselves. Every step of the physics:
    ///
    /// * **Who may crash.** A craft going faster than its weakest part can take, near enough to the ground (or to another
    ///   craft) to reach it within a second or so, is armed: the crash tolerance of each of its parts is raised past
    ///   anything the game could measure, so that the game no longer destroys a part the instant it is touched, and each
    ///   part reports what it touches (see Hits). Its parts' skins are cut into panels meanwhile (see HullPlan). A craft
    ///   that has come to rest, or is out of reach of anything, is given its tolerances back. Over the sea likewise (since 0.3.0): see Water.
    /// * **A blow.** As much of the craft's energy goes into the ground at a part as half the mass a push there has to
    ///   move (less than the whole, at the end of a long craft, which turns as it is stopped) times the square of the speed
    ///   into the ground, beyond what the part takes unharmed. A part's structure takes so much before it is crushed right
    ///   through (its dry mass times the square of a few times its crash tolerance): a blow crushes it that share of its
    ///   length, at the end that hit (see Hull: folded up, the panels flattened right down torn off), and a small one dents
    ///   it. If it holds, the craft's going into the ground is stopped there; if not, it goes on, slower, for the next part.
    ///   The craft is pushed as one rigid body (see Body).
    /// * **The slide.** A craft on the ground moving along it is slowed by friction and, on soil, by ploughing, which grows
    ///   with speed and with how deep each part has dug in (it digs deeper the further it slides, and sits down in the
    ///   ground), and a round part rolling over soil is held back as a wheel in mud is; on paving the friction grows as the
    ///   skin is torn, torn metal gouging. The drag's work tears the touching part's underside away from below (see Hull):
    ///   a tank sliding on its side loses its bottom first and keeps its top, and sheds shreds and bits behind it.
    /// * **What becomes of a part.** A tank torn open leaks for a moment, and if it catches, what is in it goes up, the
    ///   game's own explosion (the Volumetric Explosions mod makes it from what was in it), blowing the skin near the
    ///   tear away and leaving the rest, blackened, its edges bent back; the fire then goes on with the wreck. A part torn
    ///   right through comes apart in two; one that has lost the skin that held it to the next part comes away from it;
    ///   one with almost nothing left goes altogether. Coming apart where the parts are joined is otherwise rare.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public sealed partial class Crashes : MonoBehaviour
    {
        public static Crashes Instance;
        readonly Dictionary<Vessel, Wreck> wrecks = new Dictionary<Vessel, Wreck>();
        readonly List<Wreck> stepping = new List<Wreck>();
        readonly List<Hits> touching = new List<Hits>();
        readonly List<Leak> leaks = new List<Leak>();
        readonly Dictionary<Vessel, float> tolerances = new Dictionary<Vessel, float>();
        readonly Dictionary<Vessel, float> sizes = new Dictionary<Vessel, float>();
        float sizesAt;
        static readonly Dictionary<Collider, Hits> owners = new Dictionary<Collider, Hits>();
        static readonly Dictionary<Collider, bool> paving = new Dictionary<Collider, bool>();
        bool hooked, complained, noMovers, noTrails, noBurst;
        float slowFrames = 0.02f;                      // how long the frames have lately been taking, seconds (see Spawn)
        public static string Report = "", Why = "";
        public static bool TestOff;                    // (development build: the mod stood aside, to compare with the game's own crashes, without touching the player's settings)
        public static int Broken, TornAway, Split, Made, Burst;

        // ---- the figures the crashes are worked out by (see the wiki's Research page). Fields, so that the development build can try others.
        public static float Restitution = 0.12f;      // how much of its speed into the ground a craft keeps, bouncing off
        public static float Toughness = 6f;           // a part's structure takes, before it is crushed through, the energy of its own dry mass at this many times its crash tolerance (times the setting)
        public static float Bite = 0.45f, BitePaved = 0.25f;               // the ground's grip in a blow: speed along it lost for each unit of speed into it stopped
        public static float Friction = 0.5f, FrictionPaved = 0.35f;        // sliding friction on soil and on paving (the latter growing to Gouge as the skin is torn)
        public static float Gouge = 0.9f;              // torn metal dragged along paving: the friction of a skin torn all over
        public static float Plough = 0.18f, PloughPaved = 0.04f;           // and what ploughing adds, per metre a second of speed (on soil, growing as it digs in: see Dig)
        public static float Dig = 0.18f;               // metres: dug in this deep, a part ploughs twice as hard as at the surface
        public static float Scoop = 2.6f;              // a skin torn open scoops the earth up: ploughing this many times harder (its full share once two fifths of it is gone)
        public static float Anchor = 1.4f;             // and torn and dug in, its jagged metal holds in the earth: friction up by this share of itself
        public static float Rolling = 0.15f;           // a round part rolling over soil: held back by this much of its weight (more as it sinks in)
        public static float GrindSoil = 0.025f, GrindPaved = 0.03f;        // of the work the drag does, the share that tears at the skin that touches
        public static float SkinShare = 0.6f;          // grinding a part's whole skin away takes this share of what crushing it right through would
        public static float GrazeFriction = 0.5f;      // metal dragged along metal: the share of the push between them that tears at them
        // ---- a part with no skin to tear (an engine: steel, its meshes whole)
        public static float FrictionSteel = 0.7f, GougeSteel = 1.2f;       // dragged along paving: its friction whole, and broken (jagged steel gouging the paving)
        public static float CatchPaved = 0.35f;        // and what its catching on the paving as it tumbles adds, per metre a second of speed
        public static float WearPaved = 0.4f, WearSoil = 0.3f;             // of the work the drag does on it, the share that breaks it up
        // (each of 0.2.2's rules for wrecks by itself, so that the development build can stand one aside and see what it does)
        public static bool HoldWrecks = true, BrakeRolling = true, GripStrikes = true, BridgeHops = true, TellLawn = true;
        public static float RollPlough = 0.35f;        // a wreck rolling or tumbling over soil ploughs this share of what one dragged through it does
        public static float SpinLost = 0.3f;           // and a wreck loses this share of its turning at each blow (shared among the parts that strike together)
        public static float Vault = 0.1f;              // a blow throws a craft up off the ground by no more than this share of the blow's speed (what struck gives way)
        public static int TestGround = -1;             // (development build: every ground that is not paving taken as this kind, Ground.Kind; -1 as the biome has it)

        /// <summary>(Since 0.3.0.) What the ground is under a wreck: looked up twice a second from the world's biome there (Ground.KindAt).</summary>
        static Ground.Feel FeelFor(Wreck w, Vector3 at, float now)
        {
            if (now - w.feelAt < 0.5f) return w.feel;
            w.feelAt = now;
            if (TestGround >= 0 && TestGround <= (int)Ground.Kind.Regolith) return w.feel = Ground.FeelOf((Ground.Kind)TestGround);
            CelestialBody body = w.vessel != null ? w.vessel.mainBody : FlightGlobals.currentMainBody;
            if (body == null) return w.feel;
            Vector3d world = (Vector3d)at;
            w.feel = Ground.FeelOf(Ground.KindAt(body, body.GetLatitude(world), body.GetLongitude(world)));
            return w.feel;
        }

        void Awake()
        {
            Instance = this;
            Frag.Setup();
            Frag.Page.Panel = Panel;
        }

        void Start()
        {
            Layers.Choose();
            GameEvents.OnCollisionEnhancerHit.Add(Punched);
            GameEvents.onVesselGoOnRails.Add(OnRails);
            // (Everything that names the explosions mod is in Feed, and Feed is not touched unless that mod is there: so
            // this one loads, and works, where it is not.)
            if (!Installed()) Why = "Without the Volumetric Explosions mod there are no leaks, smoke or dust: the explosions are the game's own.";
            else
            {
                try { Feed.Hook(Asked); hooked = true; }
                catch (Exception ex) { Why = "Leaks, smoke and dust need a newer Volumetric Explosions than the one installed (" + ex.GetType().Name + "): get the one in this mod's download."; }
            }
            if (Why.Length > 0) Frag.Log(Why);
            if (hooked) StartCoroutine(WarmUp());
        }

        /// <summary>
        /// A few seconds into the flight, the explosions mod's first patch of air of the scene is begun and put away out of sight: the first one
        /// takes it half a second and more (measured on 2026-10-09), and begun in a crash it stopped the game for that long at the first blast.
        /// </summary>
        System.Collections.IEnumerator WarmUp()
        {
            float until = Time.time + 30f;
            while (Time.time < until && (!FlightGlobals.ready || FlightGlobals.ActiveVessel == null || FlightGlobals.ActiveVessel.packed)) yield return new WaitForSeconds(0.5f);
            yield return new WaitForSeconds(2f);
            Vessel v = FlightGlobals.ActiveVessel;
            if (v == null) yield break;
            long began = System.Diagnostics.Stopwatch.GetTimestamp();
            try { Feed.Warm(v.CoM + (Vector3)FlightGlobals.getUpAxis(v.mainBody, v.CoMD) * 300f); }
            catch (Exception) { yield break; }
            // (and this mod's own materials and textures made now, not at the first blow)
            try { Hull.Inside(); Marks.Prepare(v.CoM); Bits.Prepare(); Sparks.Prepare(); Sounds.Prepare(); } catch (Exception ex) { Frag.Log("could not make ready: " + ex.Message); }
            // (and, near the space centre, its ground's masks: which of it is lawn and which tarmac, see Ground)
            try { Ground.Prepare(v.CoM); } catch (Exception ex) { Frag.Log("could not read the space centre's ground: " + ex.Message); }
            // (and the craft being flown armed once and let go again at once, its skin cut into panels on another thread as at any arming:
            // the first arming of a game, at the first lift-off, stopped the game for 47 ms of first-time work, measured 2026-10-18. A craft
            // already being watched is left as it is.)
            try { if (Frag.On && !TestOff && Eligible(v) && !wrecks.ContainsKey(v)) Disarm(Arm(v, Time.fixedTime, true)); }
            catch (Exception ex) { Frag.Log("could not arm ahead: " + ex.Message); }
            Frag.Log("the explosions mod's first patch of air begun and put away in " + ((System.Diagnostics.Stopwatch.GetTimestamp() - began) * 1000.0 / System.Diagnostics.Stopwatch.Frequency).ToString("F0") + " ms, before any crash");
            // (and each drawn once, too small and too far off to be seen: what drawing a material the first time costs is not paid in a crash)
            GameObject shown = null;
            try { shown = DrawOnce(); } catch (Exception ex) { Frag.Log("could not draw ahead: " + ex.Message); }
            yield return null;
            yield return null;
            yield return null;
            if (shown != null) Destroy(shown);
            if (drawnAhead != null) Destroy(drawnAhead);
        }

        static Mesh drawnAhead;

        static GameObject DrawOnce()
        {
            Camera eye = FlightCamera.fetch != null ? FlightCamera.fetch.mainCamera : Camera.main;
            if (eye == null) return null;
            Transform e = eye.transform;
            var holder = new GameObject("Fragmentation drawn ahead");
            var tri = drawnAhead = new Mesh { name = "Fragmentation drawn ahead" };
            tri.vertices = new[] { new Vector3(-1f, 0f, -1f), new Vector3(1f, 0f, -1f), new Vector3(0f, 0f, 1f) };
            tri.normals = new[] { Vector3.up, Vector3.up, Vector3.up };
            tri.tangents = new[] { new Vector4(1f, 0f, 0f, 1f), new Vector4(1f, 0f, 0f, 1f), new Vector4(1f, 0f, 0f, 1f) };
            tri.uv = new Vector2[3];
            tri.triangles = new[] { 0, 1, 2, 0, 2, 1 };
            var mats = new List<Material> { Hull.Inside() };
            mats.AddRange(Marks.Materials());
            for (int k = 0; k < mats.Count; k++)
            {
                if (mats[k] == null) continue;
                var go = new GameObject("drawn ahead") { layer = k == 0 ? 0 : 15 };
                go.transform.SetParent(holder.transform, false);
                go.transform.position = e.position + e.forward * 3f;
                go.transform.localScale = Vector3.one * 0.0004f;
                go.AddComponent<MeshFilter>().sharedMesh = tri;
                go.AddComponent<MeshRenderer>().sharedMaterial = mats[k];
            }
            foreach (Bits.Kind kind in Enum.GetValues(typeof(Bits.Kind))) Bits.Emit(e.position + e.forward * 60f, Vector3.zero, 0f, 1, 0.0005f, 0.0005f, kind, 0f, 0f);
            Sparks.Emit(e.position + e.forward * 600f, Vector3.zero, Vector3.up, 1);
            return holder;
        }

        static bool Installed()
        {
            foreach (System.Reflection.Assembly a in AppDomain.CurrentDomain.GetAssemblies())
                if (a.GetType("VolumetricExplosions.Sources", false) != null) return true;
            return false;
        }

        void OnDestroy()
        {
            GameEvents.OnCollisionEnhancerHit.Remove(Punched);
            GameEvents.onVesselGoOnRails.Remove(OnRails);
            if (hooked) { try { Feed.Unhook(Asked); } catch (Exception) { } }
            DisarmAll(true);
            Piece.Clear();
            Bits.Clear();
            Marks.Clear();
            Sparks.Clear();
            Sounds.Clear();
            Shatter.Forget();
            if (Frag.Page != null && Frag.Page.Panel == Panel) Frag.Page.Panel = null;
            if (Instance == this) Instance = null;
        }

        void Panel()
        {
            GUILayout.Label((Why.Length > 0 ? Why + "\n" : "") + Report, Keystone.Host.Small);
        }

        // ================================================================== who may crash

        /// <summary>The part whose collider this is (one with no body of its own is told what it touched through its parent's).</summary>
        public static Hits OwnerOf(Collider c) => c != null && owners.TryGetValue(c, out Hits h) && h != null ? h : null;

        /// <summary>A collider a part has been given (its torn skin's box: see Hull): what it touches is the part's.</summary>
        public static void Own(Collider c, Hits h) { if (c != null && h != null) owners[c] = h; }

        /// <summary>
        /// Whether a collider is paving (a runway, a launch pad, the hard standing round them): smoother and harder than soil. The game
        /// tells where a craft has landed by the tag on what it touches ("Runway", "LaunchPad"); the space centre's grounds are hard
        /// standing too, but not tagged so.
        /// </summary>
        public static bool Paved(Collider c)
        {
            if (c == null) return false;
            if (paving.TryGetValue(c, out bool paved)) return paved;
            string tag = c.gameObject.tag;
            paved = tag == "Runway" || tag == "LaunchPad" || tag.IndexOf("Runway", StringComparison.OrdinalIgnoreCase) >= 0 || tag.IndexOf("Pad", StringComparison.OrdinalIgnoreCase) >= 0
                    || tag.StartsWith("KSC", StringComparison.Ordinal);
            if (paving.Count > 2000) paving.Clear();
            paving[c] = paved;
#if DEV
            string seen = tag + " (" + c.name + (c.GetComponentInParent<PQSCity>() != null ? ", space centre" : c.GetComponentInParent<PQSCity2>() != null ? ", launch site" : "") + "): " + (paved ? "paving" : "soil");
            if (!Surfaces.Contains(seen) && Surfaces.Count < 40) Surfaces.Add(seen);
#endif
            return paved;
        }
#if DEV
        public static readonly List<string> Surfaces = new List<string>();
#endif

        static bool Eligible(Vessel v)
        {
            if (v == null || !v.loaded || v.packed || v.state == Vessel.State.DEAD || v.isEVA || v.rootPart == null || v.parts == null || v.parts.Count == 0) return false;
            if (v.vesselType == VesselType.Flag || v.vesselType == VesselType.SpaceObject || v.vesselType == VesselType.DeployedScienceController || v.vesselType == VesselType.DeployedSciencePart) return false;
            // (Over the sea as over land since 0.3.0: what the water does to a craft that strikes it is this mod's, see Water. Until then
            // water was the game's, and a rocket that went into the sea at a hundred metres a second was whole or gone, part by part.)
            return true;
        }

        float LeastTolerance(Vessel v)
        {
            if (tolerances.TryGetValue(v, out float t)) return t;
            t = float.MaxValue;
            foreach (Part p in v.parts)
            {
                if (p == null) continue;
                Hits h = p.GetComponent<Hits>();
                float own = h != null && h.armed ? h.original : p.crashTolerance;
                t = Mathf.Min(t, own);
            }
            if (t == float.MaxValue) t = 8f;
            tolerances[v] = t;
            return t;
        }

        float Size(Vessel v)
        {
            if (sizes.TryGetValue(v, out float s)) return s;
            s = 1.5f;
            Vector3 c = v.CoM;
            foreach (Part p in v.parts) if (p != null) s = Mathf.Max(s, (p.transform.position - c).magnitude + 1.5f);
            sizes[v] = s;
            return s;
        }

        /// <summary>Arm every craft that may hit something within a second or so; give back the tolerances of those that are past it.</summary>
        void Watch(float now)
        {
            if (now > sizesAt) { sizes.Clear(); tolerances.Clear(); sizesAt = now + 2f; }
            List<Vessel> loaded = FlightGlobals.VesselsLoaded;
            for (int n = 0; n < loaded.Count; n++)
            {
                Vessel v = loaded[n];
                bool known = v != null && wrecks.ContainsKey(v);
                if (!Eligible(v)) { if (known) Disarm(wrecks[v]); continue; }
                bool risk = false;
                float speed = (float)v.srf_velocity.magnitude, least = LeastTolerance(v), size = Size(v);
                if (speed > 0.7f * least)
                {
                    // (over the sea, how high over the water: the game's own figure there is taken over the sea bed)
                    float height = (float)v.radarAltitude;
                    if (v.mainBody != null && v.mainBody.ocean) height = Mathf.Min(height, (float)(v.altitude - Math.Max(0.0, v.terrainAltitude)) - Fragmentation.Water.Height(v.CoMD));
                    // (a little further ahead than the blow itself: the parts' skins are to be cut into panels before it, on another thread)
                    if (height < speed * 2f + size + 15f) risk = true;
                }
                // ...or another craft, coming together faster than the weaker can take
                for (int k = 0; k < loaded.Count; k++)
                {
                    Vessel o = loaded[k];
                    if (o == v || !Eligible(o)) continue;
                    float rel = (float)(v.obt_velocity - o.obt_velocity).magnitude;
                    if (rel < 0.7f * Mathf.Min(least, LeastTolerance(o))) continue;
                    float apart = (float)(v.CoMD - o.CoMD).magnitude - size - Size(o);
                    if (apart < rel * 2f + 20f) { risk = true; break; }
                }
                // (a piece just torn off an armed craft is armed already: its parts' tolerances are still raised)
                if (!known && !risk && !ArmedAlready(v)) continue;
                Wreck w = Arm(v, now);
                if (risk) w.riskUntil = now + 1.5f;
            }
            stepping.Clear();
            stepping.AddRange(wrecks.Values);
            foreach (Wreck w in stepping)
            {
                Vessel v = w.vessel;
                if (v == null || v.state == Vessel.State.DEAD || !v.loaded) { wrecks.Remove(v); continue; }
                // Past it: nothing near to hit, and nothing touched for a while, or come to rest where it lies.
                if (now > w.riskUntil && (now - w.touched > 1.5f || (w.calm && now - w.calmSince > 2f))) Disarm(w);
            }
        }

        static bool ArmedAlready(Vessel v)
        {
            Hits h = v.rootPart != null ? v.rootPart.GetComponent<Hits>() : null;
            return h != null && h.armed;
        }

        Wreck Arm(Vessel v, float now, bool quiet = false)
        {
            if (!wrecks.TryGetValue(v, out Wreck w))
            {
                // (a piece just come off a craft that was being watched is a wreck from the first)
                w = new Wreck { vessel = v, since = now, name = v.GetDisplayName(), wrecked = ArmedAlready(v) };
                wrecks[v] = w;
                if (!quiet) Note("watching " + w.name + " (" + v.parts.Count + " parts, " + v.srf_velocity.magnitude.ToString("F0") + " m/s, " + v.radarAltitude.ToString("F0") + " m up)");
            }
            if (w.partsSeen != v.parts.Count)
            {
                w.partsSeen = v.parts.Count;
                w.hits.Clear();
                if (owners.Count > 4000)
                {
                    // (the colliders of parts long since destroyed)
                    var dead = new List<Collider>();
                    foreach (KeyValuePair<Collider, Hits> pair in owners) if (pair.Key == null || pair.Value == null) dead.Add(pair.Key);
                    foreach (Collider c in dead) owners.Remove(c);
                }
                foreach (Part p in v.parts)
                {
                    if (p == null) continue;
                    Hits h = p.GetComponent<Hits>() ?? p.gameObject.AddComponent<Hits>();
                    h.Arm(p);
                    w.hits.Add(h);
                    foreach (Collider c in p.FindModelComponents<Collider>()) if (c != null) owners[c] = h;
                    // (its skin cut into panels, on another thread, before it is needed)
                    if (!h.planAsked) { h.planAsked = true; try { h.plan = HullPlan.For(p); } catch (Exception) { h.plan = null; } }
                }
            }
            return w;
        }

        void Disarm(Wreck w)
        {
            if (w.vessel != null)
                foreach (Part p in w.vessel.parts)
                {
                    Hits h = p != null ? p.GetComponent<Hits>() : null;
                    if (h != null) h.Disarm();
                }
            foreach (Hits h in w.hits) if (h != null) h.Disarm();
            if (w.vessel != null) wrecks.Remove(w.vessel);
            // (what it was and how far it went, for the log: a line that must never stop a craft being given its tolerances back)
            string what = "", went = "";
            try { what = What(w); went = Went(w); } catch (Exception) { }
            if (w.slid > 1f) Note(w.name + what + " is left as it lies: slid " + w.slid.ToString("F0") + " m" + went + ", fastest on the ground " + w.fastest.ToString("F0") + " m/s, hardest blow " + w.hardest.ToString("F0") + " m/s");
            else if (w.hardest > 0f) Note(w.name + what + " is left as it is: hardest blow " + w.hardest.ToString("F0") + " m/s");
        }

        /// <summary>What a craft is made of, for the log: its heaviest part, how many more, and its mass.</summary>
        static string What(Wreck w)
        {
            Vessel v = w.vessel;
            if (v == null || v.parts == null || v.parts.Count == 0) return "";
            Part heaviest = null;
            float mass = 0f;
            foreach (Part p in v.parts)
            {
                if (p == null) continue;
                float m = p.mass + p.GetResourceMass();
                mass += m;
                if (heaviest == null || m > heaviest.mass + heaviest.GetResourceMass()) heaviest = p;
            }
            if (heaviest == null) return "";
            return " (" + (heaviest.partInfo != null ? heaviest.partInfo.title : heaviest.name) + (v.parts.Count > 1 ? " and " + (v.parts.Count - 1) + " more" : "") + ", " + mass.ToString("F2") + " t)";
        }

        /// <summary>How far a craft now lies from where it first touched (along the ground), for the log.</summary>
        static string Went(Wreck w)
        {
            Vessel v = w.vessel;
            if (v == null || !w.firstKnown || v.mainBody == null) return "";
            double alt = v.mainBody.GetAltitude(v.CoMD);
            double far = (v.mainBody.GetWorldSurfacePosition(w.firstLat, w.firstLon, alt) - v.CoMD).magnitude;
            return " and lies " + far.ToString("F0") + " m from where it first touched";
        }

        void DisarmAll(bool remove)
        {
            foreach (Hits h in FindObjectsOfType<Hits>())
            {
                if (h == null) continue;
                h.Disarm();
                if (remove) Destroy(h);
            }
            wrecks.Clear();
            leaks.Clear();
            doomed.Clear();
            if (remove) owners.Clear();
        }

        void OnRails(Vessel v)
        {
            if (v != null && wrecks.TryGetValue(v, out Wreck w)) Disarm(w);
        }

        /// <summary>The game's guard against a fast part going clean through the ground between two steps has put it back on top: that was a blow.</summary>
        void Punched(Part p, RaycastHit hit)
        {
            if (p == null) return;
            Hits h = p.GetComponent<Hits>();
            if (h == null || !h.armed) return;
            Vector3 up = (Vector3)FlightGlobals.getUpAxis(p.transform.position);
            Vector3 normal = hit.normal;
            if (Vector3.Dot(normal, up) < 0f) normal = -normal;
            h.touches++;
            h.point += hit.point;
            h.normal += normal;
            h.fresh = true;
            h.ground = true;
            h.punched = true;
        }

        /// <summary>A part's torn skin: made the first time it is wanted, if its panels are cut by then (they are cut while the craft falls).</summary>
        static Hull HullFor(Hits h)
        {
            // (a part gone already, blown up by an earlier blow in the same step, has no skin to crush: its kept one is of nothing,
            // and crushing it threw)
            if (h.part == null) return null;
            if (h.hull != null) return h.hull;
            if (h.part == null || h.plan == null || !h.plan.ready)
            {
                // (said once: a part hurt before its skin is cut breaks up the old way, whole)
                if (h.part != null && !h.hullMissed) { h.hullMissed = true; Note(Name(h) + "'s skin is " + (h.plan == null ? "not to be read" : h.plan.failed ? "not to be cut (" + h.plan.problem + ")" : "not cut yet") + ": it breaks up whole"); }
                return null;
            }
            try { h.hull = Hull.Of(h.part); }
            catch (Exception ex) { Note("could not take " + Name(h) + "'s skin: " + ex.Message); h.plan = null; }
            if (h.hull == null && !h.hullMissed) { h.hullMissed = true; Note(Name(h) + "'s skin has changed since it was cut: it breaks up whole"); }
            return h.hull;
        }

        // ================================================================== every step

        void FixedUpdate()
        {
            if (Frag.On == null) return;
#if DEV
            Tests.Pending();
#endif
            if (!Frag.On || TestOff)
            {
                if (wrecks.Count > 0) DisarmAll(false);
                Piece.Step(Time.fixedDeltaTime);
                return;
            }
            float dt = Time.fixedDeltaTime, now = Time.fixedTime;
            long began = System.Diagnostics.Stopwatch.GetTimestamp();
            stepBegan = began;
            try
            {
                long t = Prof.Now;
                Watch(now);
                Prof.Add(Prof.Watch, t);
                stepping.Clear();
                stepping.AddRange(wrecks.Values);
                foreach (Wreck w in stepping) { Step(w, dt, now); Water(w, dt, now); }
                t = Prof.Now;
                Leaks(dt, now);
                Pools(dt);
                Prof.Add(Prof.Leaks, t);
                t = Prof.Now;
                BreakSome();
                RunLater();
                SpawnSome();
                Prof.Add(Prof.Break, t);
                t = Prof.Now;
                Piece.Step(dt);
                Prof.Add(Prof.Pieces, t);
                // (how each part is moving going into the next step: a blow is measured by how fast the part was going before the physics stopped it)
                foreach (Wreck w in wrecks.Values) Remember(w);
            }
            catch (Exception ex)
            {
                if (!complained) Debug.LogError("[Fragmentation] " + ex);
                complained = true;
            }
            FixedTicks += System.Diagnostics.Stopwatch.GetTimestamp() - began;
        }

        float reportAt;

#if DEV
        public static bool TestQuiet;           // (development build: the wrecks' hurt parts' own modules stopped, to see what they cost each frame)
        float quietAt;

        void Quiet()
        {
            if (!TestQuiet || Time.unscaledTime < quietAt) return;
            quietAt = Time.unscaledTime + 0.5f;
            foreach (Vessel v in FlightGlobals.VesselsLoaded)
            {
                if (v == null) continue;
                foreach (Part p in v.parts)
                {
                    Hits h = p != null ? p.GetComponent<Hits>() : null;
                    if (h == null || (h.hull == null && h.damage < 0.01f)) continue;
                    foreach (PartModule m in p.Modules)
                        if (m != null && m.enabled && !(m is ModuleCommand)) { m.isEnabled = false; m.enabled = false; }
                }
            }
        }
#endif

        void Update()
        {
#if DEV
            Quiet();
#endif
            slowFrames = Mathf.Lerp(slowFrames, Mathf.Min(0.2f, Time.unscaledDeltaTime), 0.1f);
            long began = System.Diagnostics.Stopwatch.GetTimestamp();
            try { long t = Prof.Now; Bits.Step(Time.deltaTime); Prof.Add(Prof.Bits, t); t = Prof.Now; Marks.Restore(); Kept.Restore(); Marks.Step(); Prof.Add(Prof.Marks, t); Sounds.Frame(); }
            catch (Exception ex) { if (!complained) Debug.LogError("[Fragmentation] " + ex); complained = true; }
            FixedTicks += System.Diagnostics.Stopwatch.GetTimestamp() - began;
            if (Time.unscaledTime < reportAt) return;
            reportAt = Time.unscaledTime + 0.5f;
            int moving = 0;
            foreach (Piece p in Piece.All) if (p != null && !p.resting) moving++;
            Report = (wrecks.Count > 0 ? wrecks.Count + " craft that may crash " + (wrecks.Count == 1 ? "is" : "are") + " being watched. " : "") +
                     (Piece.Count > 0 ? Piece.Count + " pieces of wreckage (" + moving + " moving), " + Bits.Count + " bits. " : "") +
                     (Broken + TornAway + Split + Burst > 0 ? "So far " + Broken + " parts broken up, " + TornAway + " torn away, " + Split + " tanks split, " + Burst + " gone up." : "No crash yet.");
        }

        static void Remember(Wreck w)
        {
            foreach (Hits h in w.hits)
            {
                if (h == null || h.part == null) continue;
                Rigidbody rb = BodyOf(h.part);
                if (rb == null) { h.lastKnown = false; continue; }
                h.lastVelocity = rb.velocity;
                h.lastSpin = rb.angularVelocity;
                h.lastCentre = rb.worldCenterOfMass;
                h.lastKnown = true;
            }
        }

        /// <summary>The rigid body a part moves with: its own, or (a small part the game hangs on its parent) its parent's.</summary>
        public static Rigidbody BodyOf(Part p)
        {
            if (p == null) return null;
            if (p.rb != null) return p.rb;
            Part holder = p.FindNonPhysicslessParent();
            return holder != null ? holder.rb : null;
        }

        void Step(Wreck w, float dt, float now)
        {
            Vessel v = w.vessel;
            touching.Clear();
            foreach (Hits h in w.hits)
            {
                if (h == null) continue;
                // (dug in no longer, once it is off the ground a while)
                if (h.bite > 0f && now - h.onSoil > 0.3f) { h.bite = Mathf.Max(0f, h.bite - dt * 0.5f); if (h.hull != null) h.hull.SetBite(h.bite); }
                if (h.touches == 0) continue;
                if (h.gone || h.doomed) { h.Clear(); continue; }
                touching.Add(h);
            }
            if (touching.Count == 0) { w.inContact = false; w.calm = false; w.dustSpeed = 0f; return; }
#if DEV
            Clock.Touched();
#endif
            try
            {
                w.touched = now;
                w.inContact = true;
                if (!w.body.Of(v)) return;
                Body b = w.body;
                // (where it first touched: how far it went from there is said when it is left, see Disarm)
                if (!w.firstKnown && v.mainBody != null) { w.firstKnown = true; w.firstLat = v.mainBody.GetLatitude(b.C); w.firstLon = v.mainBody.GetLongitude(b.C); }
                Vector3 V0 = b.V, W0 = b.W;
                Vector3 frame = (Vector3)Krakensbane.GetFrameVelocity();
                Vector3 g = (Vector3)FlightGlobals.getGeeForceAtPosition(b.C);
                w.fastest = Mathf.Max(w.fastest, (b.V + frame).magnitude);
                // (lying still on the ground: it will be given its tolerances back, see Watch)
                bool calm = (b.V + frame).sqrMagnitude < 0.64f && b.W.sqrMagnitude < 0.04f;
                if (calm && !w.calm) w.calmSince = now;
                w.calm = calm;

                // ---- blows
                long t0 = Prof.Now;
                // (how many of its parts strike the ground in this step: the ground's grip in the blow is shared among them, see Blow)
                strikes = 0;
                foreach (Hits h in touching)
                {
                    if (!h.ground) continue;
                    Vector3 c = h.point / h.touches, n = (h.normal / h.touches).normalized;
                    Vector3 before = h.lastKnown ? h.lastVelocity + Vector3.Cross(h.lastSpin, c - h.lastCentre) + g * dt : b.At(c);
                    if (-Vector3.Dot(before + frame, n) > Floor(h)) strikes++;
                }
                foreach (Hits h in touching)
                {
                    Vector3 c = h.point / h.touches, n = (h.normal / h.touches).normalized;
                    if (n.sqrMagnitude < 0.5f) continue;
                    if (h.ground)
                    {
                        // How fast that part was going into the ground before this step (the physics has stopped it since): its own speed then.
                        Vector3 before = h.lastKnown ? h.lastVelocity + Vector3.Cross(h.lastSpin, c - h.lastCentre) + g * dt : b.At(c);
                        float into = -Vector3.Dot(before + frame, n);
                        float still = -Vector3.Dot(b.At(c) + frame, n);
                        // (and how fast the part's own middle was coming down: a tumbling wreck's corners strike far faster than it falls)
                        float middle = h.lastKnown ? Mathf.Max(0f, -Vector3.Dot(h.lastVelocity + g * dt + frame, n)) : into;
                        if (into > Floor(h)) Blow(w, h, c, n, into, still, now, middle);
                        else if (w.wrecked && into > 1f) Hold(b, n, into);
                    }
                    else if (h.other != null)
                    {
                        Hits theirs = h.other.GetComponent<Hits>();
                        Vector3 mine = h.lastKnown ? h.lastVelocity : b.At(c);
                        Vector3 them = theirs != null && theirs.lastKnown ? theirs.lastVelocity : h.otherVelocity;
                        float into = -Vector3.Dot(mine - them, n);
                        if (into > Floor(h)) Collide(w, h, c, n, into, now);
                        // Dragged along the other craft (a graze): metal torn along metal. The work is the physics' own push
                        // between the two times how fast they slide, half of it this part's.
                        float t = h.Tolerance;
                        if (h.slide > 0.5f * t && h.pushed > 0f)
                        {
                            float work = 0.5f * GrazeFriction * h.pushed * h.slide * Mathf.Clamp01((h.slide - 0.5f * t) / t);
                            h.damage += work / Capacity(h);
                            Hull hull = HullFor(h);
                            if (hull != null) hull.Grind(c, n, work, SkinToughness(h));
                        }
                    }
                }

                Prof.Add(Prof.Blows, t0);

                // ---- the slide
                t0 = Prof.Now;
                Slide(w, dt, frame, g, now);

                b.Commit(v, V0, W0);
                v.IgnoreGForces(4);
                Prof.Add(Prof.Slide, t0);

                // ---- what is now to become of each part that was touched
                t0 = Prof.Now;
                foreach (Hits h in touching) Decide(w, h, now);
                Prof.Add(Prof.Decide, t0);
            }
            finally
            {
                foreach (Hits h in touching) if (h != null) h.Clear();
            }
        }

        /// <summary>
        /// The sea (since 0.3.0). Each part of a craft that could strike it, as it goes in under the water's surface (the waves', where the
        /// Natural Ocean mod is installed): the blow it takes there (see WaterBlow), and what then becomes of it. Water is not hard as the
        /// ground is, but at speed it hardly gives: going in, a part has to set going the water in front of it, which for something the
        /// water meets broadside is more than its own mass (the "added mass": about half a tonne a cubic metre of the cube of its width
        /// there). So a tank that strikes the sea lying down at fifty metres a second is crushed as it would be on soil, and one that goes
        /// in nose first slices in and is hurt far less. Once in, the water's drag and lift are the game's.
        /// </summary>
        void Water(Wreck w, float dt, float now)
        {
            Vessel v = w.vessel;
            if (v == null || v.mainBody == null || !v.mainBody.ocean || v.altitude > 5000.0) return;
            Body b = null;
            Vector3 V0 = Vector3.zero, W0 = Vector3.zero;
            Vector3 frame = (Vector3)Krakensbane.GetFrameVelocity();
            int struck = 0;
            foreach (Hits h in w.hits)
            {
                if (h == null || h.part == null || h.gone || h.doomed) continue;
                // (its lowest corner, by its colliders' box, against the water there)
                Collider c0 = h.part.collider;
                if (c0 == null) continue;
                Bounds box = c0.bounds;
                Vector3 up = (Vector3)(h.part.transform.position - (Vector3)v.mainBody.position).normalized;
                Vector3 low = box.center;
                float lowest = float.MaxValue;
                for (int k = 0; k < 8; k++)
                {
                    Vector3 corner = box.center + Vector3.Scale(box.extents, new Vector3((k & 1) == 0 ? -1f : 1f, (k & 2) == 0 ? -1f : 1f, (k & 4) == 0 ? -1f : 1f));
                    float along = Vector3.Dot(corner - box.center, up);
                    if (along < lowest) { lowest = along; low = corner; }
                }
                float deep = Fragmentation.Water.Depth((Vector3d)low);
                if (deep <= 0f) { if (deep < -0.5f) h.inWater = false; continue; }
                if (h.inWater) { w.touched = now; continue; }
                h.inWater = true;
                w.touched = now;
                if (b == null)
                {
                    if (!w.body.Of(v)) return;
                    b = w.body; V0 = b.V; W0 = b.W;
                }
                Vector3 point = low + up * Mathf.Min(deep, 0.5f);
                float into = -Vector3.Dot(b.At(point) + frame, up);
                if (into > Floor(h)) { WaterBlow(w, h, point, up, into, now); struck++; }
            }
            if (b == null) return;
            b.Commit(v, V0, W0);
            if (struck == 0) return;
            v.IgnoreGForces(4);
            foreach (Hits h in w.hits) if (h != null && h.struck) { Decide(w, h, now); h.struck = false; }
        }

        /// <summary>
        /// A part striking the water: what is beyond its tolerance of the energy the water takes from its going in (the water in front of
        /// it set going: its mass and the water's added mass together, see Water) crushes it as a blow on the ground does, at the end that
        /// struck; and the water takes that share of its going in at once. No marks, no grip along the surface, no burst of the ground's
        /// dust: the splash is the sea's (the Natural Ocean mod draws it, by the craft's own size and speed).
        /// </summary>
        void WaterBlow(Wreck w, Hits h, Vector3 c, Vector3 up, float into, float now)
        {
            Body b = w.body;
            float m = b.MassAlong(c, up);
            float face = FaceOn(h, up);
            float added = 0.48f * Fragmentation.Water.Density * face * Mathf.Sqrt(face);
            float meant = m * added / Mathf.Max(m + added, 1e-4f);
            float t = h.Tolerance;
            float energy = into > t ? 0.5f * meant * (into * into - t * t) : 0f;
            float cap = Capacity(h);
            float share = cap > 0f ? energy / cap : 0f;
            h.damage += share;
            h.crushed = Mathf.Min(1.2f, h.crushed + share);
            h.hardest = Mathf.Max(h.hardest, into);
            w.hardest = Mathf.Max(w.hardest, into);
            h.why = "the water at " + into.ToString("F0") + " m/s";
            Hull hull = HullFor(h);
            if (hull != null && share > 0f)
            {
                float length = Mathf.Max(0.2f, LengthAlong(h, up));
                if (share > 0.02f) { hull.CrushAt(c, up, Mathf.Min(share, 1f) * length); h.sever = true; }
                else hull.DentAt(c, up, Mathf.Clamp(share * 6f * length, 0.02f, 0.25f * length), Mathf.Clamp(0.35f * length, 0.3f, 1.5f));
                if (!hull.shown) hull.Show();
            }
            b.Push(c, up * (meant * into));
            h.struck = true;
            if (energy > 0f) w.wrecked = true;
            Sounds.Splash(c, into, m, face);
            if (into > 6f) Note(Name(h) + " hit the water at " + into.ToString("F0") + " m/s, " + face.ToString("F1") + " m2 of it meeting it (" + (meant / Mathf.Max(m, 1e-4f) * 100f).ToString("F0") + "% of its blow on hard ground): hurt " + h.damage.ToString("F2"));
        }

        /// <summary>How much of a part meets a surface it comes at along this way (square metres): its box, seen from that way.</summary>
        static float FaceOn(Hits h, Vector3 worldWay)
        {
            Part p = h.part;
            Vector3 way = p.transform.InverseTransformDirection(worldWay);
            Vector3 s = h.plan != null && h.plan.ready ? h.plan.hi - h.plan.lo : p.prefabSize;
            s = new Vector3(Mathf.Max(0.1f, s.x), Mathf.Max(0.1f, s.y), Mathf.Max(0.1f, s.z));
            return Mathf.Abs(way.x) * s.y * s.z + Mathf.Abs(way.y) * s.x * s.z + Mathf.Abs(way.z) * s.x * s.y;
        }

        /// <summary>
        /// A part with no skin to tear: an engine (its bell, its pump and its mounting are whole meshes), or one whose skin could not be read or
        /// was not cut in time. Grinding takes no panels off such a part, so the ground wears it as a whole instead (see Slide and Hull.Wear).
        /// </summary>
        static bool Solid(Hits h) => h.plan == null || h.plan.failed || h.plan.engine || h.hullMissed;

        /// <summary>
        /// What strikes gives way rather than throwing the wreck up: a wreck does not bound along the ground like a ball. Whatever speed away
        /// from what it struck the wreck's middle has after the blow, beyond a share of the blow's own speed, is taken from it (it went into
        /// crushing what struck: thin structure folds and stays folded). Only a craft already hurt: one that comes down within what its parts
        /// take bounces as the game has it.
        /// </summary>
        static void Hold(Body b, Vector3 n, float into)
        {
            if (!HoldWrecks) return;
            float away = Vector3.Dot(b.V + (Vector3)Krakensbane.GetFrameVelocity(), n), most = Vault * into + 0.3f;
            if (away > most) b.V -= n * (away - most);
        }

        /// <summary>Below this speed into something, touching it is only touching it.</summary>
        static float Floor(Hits h) => Mathf.Max(2.5f, 0.45f * h.Tolerance);

        /// <summary>
        /// How much energy (kilojoules: tonnes and metres a second) a part takes, beyond what it takes unharmed, before it is crushed
        /// right through. Unharmed it takes a blow at its own crash tolerance, as in the game: whatever this mod does, a craft that
        /// touches down within its parts' tolerances is not hurt. Beyond that it takes as much again as a blow of a few times its
        /// tolerance would bring (the setting "how tough parts are" scales those few times).
        /// </summary>
        static float Capacity(Hits h)
        {
            Part p = h.part;
            float k = Toughness * Mathf.Max(0.1f, Frag.Tough), t = h.Tolerance;
            // (its structure takes the blow, so its own mass without what is in it: what is in it adds to the blow and nothing to the strength.
            // So a full tank, heavy and no stronger, bursts sooner than an empty one, as real ones do: see the Research page)
            float m = Mathf.Max(0.005f, p.prefabMass > 0f ? p.prefabMass : p.mass);
            return m * 0.5f * Mathf.Max(0.5f, k * k - 1f) * t * t;
        }

        /// <summary>Kilojoules a square metre: what tearing a part's skin away along the ground takes.</summary>
        static float SkinToughness(Hits h)
        {
            // (by how heavy the structure is for its area, not by how hard a blow it takes: a capsule built to take a hard landing
            // is no harder to grind than a tank of the same weight of metal a square metre. At 0.38 kJ for each kilogram, a full-size
            // tank's skin, some 30 kg a square metre in the game, takes 11 kJ a square metre to tear away)
            Part p = h.part;
            float area = h.plan != null && h.plan.ready ? h.plan.area : 4f;
            float full = p.partInfo != null && p.partInfo.partPrefab != null ? p.partInfo.partPrefab.mass : Mathf.Max(0.005f, p.mass);
            return 0.38f * SkinShare / 0.6f * Mathf.Max(0.1f, Frag.Tough) * (full * 1000f) / Mathf.Max(0.05f, area);
        }

        void Blow(Wreck w, Hits h, Vector3 c, Vector3 n, float into, float still, float now, float middle)
        {
            Body b = w.body;
            float m = b.MassAlong(c, n);
            // Only what is beyond the part's own tolerance hurts it (see Capacity).
            float t = h.Tolerance;
            float energy = into > t ? 0.5f * m * (into * into - t * t) : 0f;
            float cap = Capacity(h), left = cap * Mathf.Max(0f, 1f - h.crushed);
            float J, stopped;          // (stopped: the speed into the ground that the ground took out of the craft there, the physics' share and this mod's)
            string what;
            if (energy <= left)
            {
                // its going into the ground is stopped there, with a little bounce
                J = Mathf.Max(0f, m * (still + Restitution * into));
                stopped = into * (1f + Restitution);
                what = energy > 0.01f * cap ? "crushed in" : "dented";
            }
            else
            {
                // crushed through: it takes what it can, and the craft goes on into the ground that much slower
                float after = Mathf.Sqrt(Mathf.Max(0f, into * into - t * t - 2f * left / m));
                J = Mathf.Max(0f, m * (still - after));
                stopped = Mathf.Max(0f, into - after);
                what = "crushed through";
            }
            float share = cap > 0f ? energy / cap : 0f;
            h.damage += share;
            h.crushed = Mathf.Min(1.2f, h.crushed + share);
            h.hardest = Mathf.Max(h.hardest, into);
            w.hardest = Mathf.Max(w.hardest, into);
            h.why = "the ground at " + into.ToString("F0") + " m/s";
            // What the blow does to the part itself: that share of its length crushed, at the end that hit; a small one only dents it.
            Hull hull = HullFor(h);
            if (hull != null && share > 0f)
            {
                float length = Mathf.Max(0.2f, LengthAlong(h, n));
                if (share > 0.02f) { hull.CrushAt(c, n, share * length); h.sever = true; }
                else hull.DentAt(c, n, Mathf.Clamp(share * 6f * length, 0.02f, 0.25f * length), Mathf.Clamp(0.35f * length, 0.3f, 1.5f));
                if (!hull.shown) hull.Show();
            }
            Vector3 impulse = n * J;
            // The ground bites into what hits it: some of the speed along the ground goes too.
            Vector3 u = b.At(c) + (Vector3)Krakensbane.GetFrameVelocity();
            Vector3 ut = u - n * Vector3.Dot(u, n);
            float st = ut.magnitude;
            if (st > 0.1f)
            {
                Vector3 along = ut / st;
                bool solid = Solid(h);
                float slide = Mathf.Max(0.2f, Frag.Slide);
                // On soil, a share of what this mod stops there. On paving, the paving's friction on the whole of what the ground pushed back
                // with in the blow, what the physics had stopped already as well: a wreck that bounds and tumbles along is held back at every
                // strike as one that slides is all the way. (Before, with the bite reckoned on this mod's share alone, a tumbling engine lost
                // next to nothing at each strike and went on for three hundred metres, most of them in the air.)
                // (since 0.3.0 by what the ground is: bare rock and ice are hard, as paving is; snow and sand grip less, see Ground.Feel)
                Ground.Feel feel = h.paved ? Ground.Soft : FeelFor(w, c, now);
                bool hard = h.paved || feel.Hard;
                float jt = (hard ? BitePaved : Bite) * feel.Bite * J;
                if (GripStrikes && hard)
                {
                    float torn = h.hull != null ? 1f - h.hull.Left : Mathf.Clamp01(h.damage);
                    if (solid) torn = Mathf.Max(torn, Mathf.Clamp01(h.damage));
                    float grip = solid ? Mathf.Lerp(FrictionSteel * feel.Steel, GougeSteel * feel.Steel, Mathf.Clamp01(torn * 1.5f)) : Mathf.Lerp(FrictionPaved * feel.Grip, Gouge * feel.Gouge, Mathf.Clamp01(torn * 2.5f));
                    // (each part that strikes in the same step is reckoned to stop its share of the craft: the push is the craft's, once.
                    // And no more than stopping the wreck's own fall takes: reckoned on how fast the corner that struck was going, a
                    // tumbling engine was spun up like a top by its first two strikes and smashed by the third, 26 m from its first touch)
                    jt = Mathf.Max(BitePaved * feel.Bite * J, grip * Mathf.Min(m * stopped, b.M * middle * (1f + Restitution)) / (slide * Mathf.Max(1, strikes)));
                }
                else if (GripStrikes && solid) jt = Mathf.Max(jt, Friction * feel.Friction * Mathf.Min(m * stopped, b.M * middle * (1f + Restitution)) / (slide * Mathf.Max(1, strikes)));
                jt = Mathf.Min(jt, b.MassAlong(c, along) * st * 0.9f);
                impulse -= along * jt;
                // (and that grip's work wears a part with no skin to tear, as the drag's does: see Slide)
                if (solid && cap > 0f) h.damage += (hard ? WearPaved : WearSoil) * jt * st / cap;
            }
            b.Push(c, impulse);
            h.struck = true;
            if (energy > 0f) w.wrecked = true;
            if (w.wrecked)
            {
                Hold(b, n, into);
                // (and where the ground gripped it, some of its turning goes into what gave way: a wreck tumbles, it does not spin up
                // like a wheel. Not a skinned wreck on soil, which is not gripped so: a long craft that digs an end in still cartwheels)
                if (HoldWrecks && GripStrikes && (h.paved || Solid(h))) b.W *= 1f - SpinLost / Mathf.Max(1, strikes);
            }
            // (and the ground: a crater where it hit soft ground hard, clods thrown out of it; and the dust and sparks of the blow)
            Ground.Feel struck = h.paved ? Ground.Soft : FeelFor(w, c, now);
            if (!h.paved && struck.Kind != Ground.Kind.Rock && into > 8f) Marks.Crater(c, n, energy, u, h);
            if (into > 9f && energy > 0.05f * cap) Thud(w, h, c, into, now);
            if (into > 3f && h.part != null) Sounds.Hit(c, into, h.part.mass, h.paved || struck.Hard, Solid(h));
            if (into > 6f) Note(Name(h) + " hit " + (h.paved ? "paving" : "the ground") + " at " + into.ToString("F0") + " m/s (" + u.magnitude.ToString("F0") + " m/s in all): " + what + ", hurt " + h.damage.ToString("F2"));
        }

        float thudAt = -10f;
        int strikes = 1;                     // how many parts of the craft being stepped strike the ground in this step

        /// <summary>
        /// A hard blow throws up dust and sparks, as the explosions mod makes them for a crash at that speed with the craft's weight behind it (and
        /// nothing going up); the patch of air it begins is where the smoke of the pieces of the blow goes. Not twice within a few tenths of a
        /// second, nor near a blast of the last few seconds.
        /// </summary>
        void Thud(Wreck w, Hits h, Vector3 c, float into, float now)
        {
            if (!hooked || noBurst || now - thudAt < 0.4f || h.part == null) return;
            foreach (Blown b in bursts)
                if (Time.time - b.at < 3f && b.body == FlightGlobals.currentMainBody && (b.body.GetWorldSurfacePosition(b.lat, b.lon, b.alt) - (Vector3d)c).sqrMagnitude < 25.0 * 25.0) return;
            thudAt = now;
            try { Feed.Thud(h.part, c, into); }
            catch (Exception) { noBurst = true; return; }
            FXMonger.Explode(h.part, c, 0.0);
            Blasted(c, 25f);
        }

        /// <summary>How long a part is along a way (metres): its skin's box, or the game's own measure of it.</summary>
        static float LengthAlong(Hits h, Vector3 worldWay)
        {
            Part p = h.part;
            Vector3 way = p.transform.InverseTransformDirection(worldWay);
            Vector3 s = h.plan != null && h.plan.ready ? h.plan.hi - h.plan.lo : p.prefabSize;
            return Mathf.Abs(way.x) * s.x + Mathf.Abs(way.y) * s.y + Mathf.Abs(way.z) * s.z;
        }

        void Collide(Wreck w, Hits h, Vector3 c, Vector3 n, float into, float now)
        {
            Body b = w.body;
            float m1 = b.MassAlong(c, n), m2 = m1;
            if (h.other != null && h.other.vessel != null && wrecks.TryGetValue(h.other.vessel, out Wreck them) && them.body.M > 0f) m2 = them.body.MassAlong(c, n);
            else if (h.other != null && h.other.vessel != null) m2 = (float)h.other.vessel.totalMass;
            float mu = m1 * m2 / Mathf.Max(1e-4f, m1 + m2);
            // what the two take between them beyond what the part takes unharmed, halved: the other part takes its half when its own craft is stepped
            float t = h.Tolerance;
            if (into <= t)
            {
                if (into > 2f) Note(Name(h) + " ran into " + (h.other != null && h.other.partInfo != null ? h.other.partInfo.title : "another craft") + " at " + into.ToString("F1") + " m/s: within what it takes (" + t.ToString("F0") + " m/s)");
                return;
            }
            // The softer of the two takes more of it; and a tank, a thin wall full of liquid, is pierced rather than crushed by a part driven
            // into it (a supply ship at a few metres a second holed Mir's Spektr module; a gear leg driven into a full tank leaked it: see the
            // Research page).
            float theirs = h.other != null ? Mathf.Max(0.5f, h.other.GetComponent<Hits>() is Hits o && o.armed ? o.original : h.other.crashTolerance) : t;
            float share = theirs / (t + theirs);
            float energy = 0.5f * mu * (into * into - t * t) * (1f - Restitution * Restitution) * share;
            if (Propellant(h.part) > 20.0) energy *= 1.8f;
            float cap = Capacity(h);
            float hurt = energy / cap;
            h.damage += hurt;
            h.crushed = Mathf.Min(1.2f, h.crushed + hurt);
            h.hardest = Mathf.Max(h.hardest, into);
            w.hardest = Mathf.Max(w.hardest, into);
            h.why = (h.other != null && h.other.partInfo != null ? h.other.partInfo.title : "another craft") + " at " + into.ToString("F0") + " m/s";
            Hull hull = HullFor(h);
            if (hull != null)
            {
                float length = Mathf.Max(0.2f, LengthAlong(h, n));
                if (hurt > 0.02f) { hull.CrushAt(c, n, hurt * length); h.sever = true; }
                else hull.DentAt(c, n, Mathf.Clamp(hurt * 6f * length, 0.02f, 0.25f * length), Mathf.Clamp(0.3f * length, 0.25f, 1.2f));
                if (!hull.shown) hull.Show();
            }
            if (into > 2f) Note(Name(h) + " ran into " + h.why + ": hurt " + h.damage.ToString("F2"));
        }

        void Slide(Wreck w, float dt, Vector3 frame, Vector3 g, float now)
        {
            Body b = w.body;
            Vector3 c = Vector3.zero, n = Vector3.zero;
            int count = 0, paved = 0;
            foreach (Hits h in touching)
            {
                if (!h.ground) continue;
                c += h.point / h.touches;
                n += (h.normal / h.touches).normalized;
                count++;
                if (h.paved) paved++;
            }
            if (count == 0) { w.dustSpeed = 0f; return; }
            // (a wreck that left the ground for a moment and is down again: how long it was off it, see the catching on paving below)
            float gap = now - w.lastGround, lapse = BridgeHops && w.wrecked && gap > dt && gap <= 0.3f ? gap : dt;
            w.lastGround = now;
            c /= count;
            n = n.normalized;
            Vector3 u = b.At(c) + frame;
            Vector3 ut = u - n * Vector3.Dot(u, n);
            float s = ut.magnitude;
            // (what the ground is that is not paving: since 0.3.0, see Ground.Feel)
            Ground.Feel feel = FeelFor(w, c, now);
            w.dustAt = c; w.dustNormal = n; w.dustGoing = u; w.dustSpeed = s; w.dustPaved = paved * 2 > count || (feel.Hard && feel.Dust < 0.5f);
            w.dustSize = Mathf.Clamp(Size(w.vessel) * 0.25f, 0.5f, 3f);
            // ---- a round part rolling over soil is held back, as a wheel in mud is (the more, the deeper it has sunk in)
            Vector3 centre = b.V + frame;
            Vector3 centreAlong = centre - n * Vector3.Dot(centre, n);
            float vc = centreAlong.magnitude;
            // (a craft that is whole: a wreck is held back harder, below)
            if (vc > 0.3f && s < 0.6f * vc && paved * 2 <= count && !feel.Hard && !(w.wrecked && BrakeRolling))
            {
                float sink = 0f;
                foreach (Hits h in touching) if (h.ground) sink = Mathf.Max(sink, h.bite);
                float a = (Rolling + 1.5f * sink) * Mathf.Max(0f, -Vector3.Dot(g, n)) / Mathf.Max(0.2f, Frag.Slide);
                float k = Mathf.Min(0.9f, a * dt / vc);
                Vector3 axis = Vector3.Cross(n, centreAlong / vc);
                b.V -= centreAlong / vc * (vc * k);
                b.W -= axis * (Vector3.Dot(b.W, axis) * k);
            }
            // (a wreck whose middle is going along the ground is held back whether the place that touches slides, rolls or bounds: see below)
            bool tumbles = BrakeRolling && w.wrecked && vc > 1f;
            if (s < 0.5f && !tumbles) return;
            float slide = Mathf.Max(0.2f, Frag.Slide);
            float pressed = Mathf.Max(0f, -Vector3.Dot(g, n));
            float share = b.M / count;
            w.slid += (tumbles ? Mathf.Max(s, vc) : s) * dt;
            // (what touches, measured across the way the wreck goes: the scar it leaves is as wide as all of it. The way its middle goes, not
            // the way of the place that touches, which swings about as a wreck tumbles and made a scrape that ran in zigzags)
            Vector3 going = vc > 1f ? centreAlong / vc : ut / Mathf.Max(s, 1e-3f);
            Vector3 across = Vector3.Cross(n, going).normalized;
            float markLo = float.MaxValue, markHi = float.MinValue, markDeep = 0f;
            int marked = 0, markedPaved = 0;
            float heard = 0f, heardSteel = 0f;      // (the work the ground's holding it back does this step, joules: for the sound of it, see Sounds)
            // Each place that touches drags by its share of the craft's weight, the way that place is going: so a craft that slews
            // round as it slides is slowed in its turning as well, and one end dragging harder than the other turns it.
            foreach (Hits h in touching)
            {
                if (!h.ground) continue;
                Vector3 at = h.point / h.touches, normal = (h.normal / h.touches).normalized;
                Vector3 there = b.At(at) + frame;
                Vector3 along = there - normal * Vector3.Dot(there, normal);
                float sh = along.magnitude;
                // Nothing of a wreck is a wheel. Where the place that touches is going slower than the wreck's middle (the wreck rolls over it,
                // or tumbles), the wreck is held back by its middle's speed all the same, and its turning with it: it comes down on its corners
                // and edges at every turn, and each one catches (in soil, digs in). (Until 0.2.2 a loose engine rolled and bounded three hundred
                // metres along the runway on its mounting ring, unmarked, and a pod as far over grass.)
                bool rolls = tumbles && sh < 0.8f * vc;
                if (rolls) { along = centreAlong; sh = vc; }
                if (sh < 0.3f) continue;
                // (a part's skin is taken only when something is done to it: each costs its own copy of its corners)
                Hull hull = h.hull;
                float torn = hull != null ? 1f - hull.Left : Mathf.Clamp01(h.damage);
                // (a part with no skin to tear, an engine: what stands for its tearing is how far it is worn and broken)
                bool solid = Solid(h);
                if (solid) torn = Mathf.Max(torn, Mathf.Clamp01(h.damage));
                float a, J, caught = 0f;      // (caught: the part of the drag that is an engine's catching, which holds the wreck's middle back and does not turn it over)
                // On paving a smooth skin slides (a third of its weight), a torn one gouges. An engine is steel, and all corners: it grips harder
                // from the first, harder still as it is broken, and catches on the paving as it goes, the more the faster (as set: see the wiki's
                // Limits page for what plain friction would do).
                Ground.Feel f = h.paved ? Ground.Soft : feel;
                bool hard = h.paved || f.Hard;
                if (hard)
                {
                    float grip = solid ? Mathf.Lerp(FrictionSteel * f.Steel, GougeSteel * f.Steel, Mathf.Clamp01(torn * 1.5f)) : Mathf.Lerp(FrictionPaved * f.Grip, Gouge * f.Gouge, Mathf.Clamp01(torn * 2.5f));
                    float more = (solid ? CatchPaved * f.Catch : PloughPaved * f.Grip) * sh;
                    a = (grip * pressed + more) / slide;
                    // (what bears on the paving there: its share of the craft's weight. A blow's own grip is reckoned in Blow. What the
                    // physics says the paving pushed back with in a touch was tried in its place: with it an engine doing 65 m/s stopped
                    // in 34 m, over 6 g. It was taken out without being tried by itself: a wreck held to the ground is pushed back at
                    // every step, and the pushes add up to far more than its weight)
                    // (its weight bears for as long as it was off the ground as well, if that was a moment: a wreck that skips along comes
                    // down each time as hard as the time it was up, and the paving's friction is owed on all of it. With friction only in the
                    // steps in which it touched, a pod was 180 m from where it first touched having touched for 13 m of them.)
                    float load = share * pressed * lapse;
                    // (and an engine's catching for as long likewise: what is meant to act all the way along must not act only in the
                    // steps in which something happens to touch)
                    float rub = (grip * load + (solid ? 0f : share * more * dt)) / slide;
                    caught = solid ? share * more * lapse / slide : 0f;
                    J = Mathf.Min(rub + caught, share * sh * 0.5f);
                    caught *= J / Mathf.Max(1e-6f, rub + caught);
                }
                else
                {
                    // On soil it digs in as it goes, deeper the further it slides (most of the way in within a dozen metres), down to a share
                    // of its own width; dug in, it ploughs harder. (A rocket is heavy for its width, and its skin does not ride over the
                    // ground as an aeroplane's belly does: see the Research page.)
                    // (sand lets it sink deeper, snow deeper still, packing down under it: see Ground.Feel)
                    float deepest = Mathf.Clamp(0.25f * Width(h) * f.Deep, 0.08f, 0.55f * f.Deep);
                    h.bite += (deepest - h.bite) * (1f - Mathf.Exp(-sh * dt / 12f));
                    h.onSoil = now;
                    if (hull != null) hull.Bite(-normal, h.bite);
                    // (and a torn skin scoops the earth up as it goes, which a smooth one rides over: the AAIB, of a belly torn open, "ploughing and
                    // scooping of soil into the airframe resulting in high deceleration levels"; a smooth aeroplane's belly on soft grass, about 0.4 g.
                    // Torn open and dug in, its jagged edges hold in the earth as well: a wreck that has torn its underside away does not slide far)
                    float open = Mathf.Clamp01(torn * 2.5f);
                    // (an engine's pipes, its mounting and the lip of its bell catch in the earth from the first: it is no smooth skin)
                    if (solid) open = Mathf.Max(open, 0.4f);
                    float grip = Friction * f.Friction * (1f + Anchor * f.Anchor * open * Mathf.Clamp01(h.bite / 0.15f));
                    // (a wreck that rolls or tumbles over the earth digs in at every turn and rolls out again: it ploughs, but not as
                    // one dragged through it does. Ploughing in full, the pieces of a rocket that had gone up at 70 m/s lay 25 m from where
                    // it first touched: stopped dead, at some 10 g)
                    float plough = Plough * f.Plough * sh * (1f + h.bite / (Dig * f.Deep)) * (0.4f + Scoop * f.Scoop * open) * (rolls ? RollPlough : 1f);
                    a = (grip * pressed + plough) / slide;
                    // (the friction owed for a moment off the ground, as on paving; the ploughing only while it is in the earth)
                    J = Mathf.Min(share * (a * dt + grip * pressed * (lapse - dt) / slide), share * sh * 0.5f);
                }
                if (rolls)
                {
                    // (its middle held back, and its turning by as much)
                    float k = Mathf.Min(0.9f, J / Mathf.Max(1e-6f, b.M * vc));
                    b.V -= centreAlong * k;
                    b.W *= 1f - k;
                }
                else
                {
                    // (friction where it touches, which turns the wreck over that place; the catching on its middle. The catching was
                    // first applied where the engine touched: thrown lying level, it was then off the ground again within a few tens of
                    // metres and bounded on for three hundred, see the wiki's What was tested)
                    b.Push(at, -along / sh * (J - caught));
                    if (caught > 0f) b.V -= along / sh * (caught / b.M);
                }
                heard += J * sh;
                if (solid) heardSteel += J * sh;
                // The work the drag does is done on the part that touches: it is ground away by it, from below. (Dragged along slower than
                // it could take a blow, it is only scuffed.)
                float t = h.Tolerance;
                float work = J * sh * Mathf.Clamp01((sh - 0.5f * t) / t);
                float grind = work * (hard ? GrindPaved : GrindSoil);
                // (a part with no skin to tear is worn as a whole, whichever way up it is as it tumbles: a good share of the drag's work
                // goes into breaking it, and its meshes come away one after another, see Hull.Wear; it goes altogether when it is worn through)
                float wear = solid ? work * (hard ? WearPaved : WearSoil) : 0f;
                if (work > 0f) w.wrecked = true;
                h.damage += solid ? wear / Capacity(h) : grind / Capacity(h) * 0.5f;
                h.slidOn += sh * dt;
                h.groundWork += solid ? wear : grind;
                if (work > 0f && (hull = hull ?? HullFor(h)) != null)
                {
                    if (hull.Grind(at, normal, grind, SkinToughness(h)) > 0) h.sever = true;
                    if (solid && hull.Wear(at, normal, h.damage) > 0) h.sever = true;
                    if (!hard) hull.Bite(-normal, h.bite);
                }
                if (h.lastTouch < 0f || now - h.lastTouch > 0.5f) h.firstTouch = now;
                h.lastTouch = now;
                // the ground it marks, across the way the wreck goes: one scar for the whole wreck, as wide as all that touches (see below)
                float wide = Width(h);
                float mark = hard ? Mathf.Clamp(wide * 0.35f, 0.15f, 1.2f) : Mathf.Clamp(wide * (0.45f + 0.9f * Mathf.Sqrt(Mathf.Max(0f, h.bite) / Mathf.Max(0.1f, wide))), 0.25f, 3f);
                float off = Vector3.Dot(at - c, across);
                markLo = Mathf.Min(markLo, off - 0.5f * mark); markHi = Mathf.Max(markHi, off + 0.5f * mark);
                if (!hard) markDeep = Mathf.Max(markDeep, h.bite);
                marked++;
                // (a scrape on bare rock, as on paving; ice is scored white, drawn as a shallow scar of its own colour)
                if (h.paved || f.Kind == Ground.Kind.Rock) markedPaved++;
                // what it raises and throws: clods of earth and torn turf from soil; from paving grit, and sparks where steel drags (an engine, a heavy fitting)
                // (clods of earth, and of packed snow; not of sand, dust or ice, which go up as dust: see CrashesSmoke)
                if (!hard && f.Clods && sh > 6f && Random.value < Mathf.Clamp01(sh * dt * (0.6f + 3f * h.bite)))
                {
                    Vector3 from = at - along / sh * 0.3f + normal * 0.2f, thrown = along * Random.Range(0.25f, 0.6f) + normal * Random.Range(1f, 5f);
                    Bits.Emit(from, thrown, 2f + sh * 0.06f, Random.Range(1, 4), 0.02f, 0.12f, Bits.Kind.Clod, 0f, 0f);
                    // (the turf is torn off in slabs at the edges of the scar, and thrown aside: where there is turf)
                    if (f.Turf && Random.value < 0.6f) Bits.Emit(from + across * Random.Range(-0.5f, 0.5f) * mark, thrown + across * Random.Range(-3f, 3f), 1.5f + sh * 0.04f, Random.Range(1, 3), 0.05f, 0.2f, Bits.Kind.Turf, 0f, 0f);
                }
                if ((h.paved || f.Sparks) && sh > 8f)
                {
                    // (steel dragged fast along paving throws a stream of sparks, the more the faster and the harder it is held back; a tank's
                    // aluminium hardly any)
                    float steel = h.plan != null && h.plan.engine ? 3f : 0.12f;
                    float due = sh * dt * 0.5f * steel;
                    int sparks = (int)due + (Random.value < due - (int)due ? 1 : 0);
                    if (sparks > 0) Sparks.Emit(at + normal * 0.05f, along, normal, Mathf.Min(sparks, 8));
                    // (and bits of it: chips of metal, bolts, as it is worn)
                    if (solid && Random.value < sh * dt * 0.12f)
                        Bits.Emit(at + normal * 0.1f, along * Random.Range(0.3f, 0.7f) + normal * Random.Range(1f, 4f), 3f, Random.Range(1, 3), 0.015f, 0.07f, Random.value < 0.3f ? Bits.Kind.Bolt : Bits.Kind.Metal, 0f, 0.05f);
                }
            }
            // the sound of it: as loud as the power the dragging takes
            if (dt > 0f) Sounds.Slide(w, c, Mathf.Max(s, vc), heard / dt, paved * 2 > count || feel.Hard, heardSteel / dt);
            // the ground marked by the wreck: a scar of torn earth on soil, a scrape on paving, as wide as what touches it
            if (marked > 0) Marks.Track(w, c + across * (0.5f * (markLo + markHi)), n, going, Mathf.Max(s, vc), markDeep, markHi - markLo, markedPaved * 2 > marked);
        }

        /// <summary>How wide a part is across the way it lies on the ground (metres): the narrower of its sides.</summary>
        static float Width(Hits h)
        {
            Vector3 s = h.plan != null && h.plan.ready ? h.plan.hi - h.plan.lo : h.part.prefabSize;
            float a = Mathf.Max(0.1f, s.x), b = Mathf.Max(0.1f, s.y), c = Mathf.Max(0.1f, s.z);
            // (the middle one of the three: a tank lying down is as wide as it is round)
            return Mathf.Max(Mathf.Min(a, b), Mathf.Min(Mathf.Max(a, b), c));
        }

        // ================================================================== what becomes of a part

        void Decide(Wreck w, Hits h, float now)
        {
            if (h.gone || h.part == null) return;
            Part p = h.part;
            Hull hull = h.hull;
            if (hull != null)
            {
                // what has been torn off becomes pieces; whatever is no longer joined to the rest of the skin comes away as one
                if (h.sever) foreach (List<int> loose in hull.CutLoose()) Note(Name(h) + " was torn right through");
                if (hull.removed.Count > 0)
                {
                    // (in a busy step, the pieces of what was torn off are made in the next ones: the panels are gone from the part at once,
                    // their pieces come a step or two later, where the part has got to by then)
                    if (System.Diagnostics.Stopwatch.GetTimestamp() - stepBegan > System.Diagnostics.Stopwatch.Frequency * 5 / 1000) { if (!spawnLater.Contains(h)) { spawnLater.Add(h); spawnLaterWreck.Add(w); } }
                    else SpawnTorn(w, h, hull);
                }
                // (a part about to go altogether takes its small parts with it: they are not let go first)
                // (and an engine, whose meshes come away whole, once it is worn or crushed right through)
                if (hull.Left < 0.15f || (hull.plan.engine && h.damage >= 1f)) { h.sever = false; Break(w, h, true); return; }
                if (h.sever) { h.sever = false; Sever(w, h, hull); if (h.part == null || h.gone) return; }
            }
            else if (h.damage >= 1f) { Break(w, h, true); return; }
            if (h.damage >= 0.3f || (hull != null && hull.Left < 0.85f)) Weaken(h);
            if (h.leaking) return;
            // A tank whose wall is breached leaks: crushed in, or holed.
            bool breached = hull != null ? (hull.Left < 0.985f || h.crushed > 0.06f) : h.damage >= 0.5f;
            if (Frag.Fires && breached && Propellant(p) > 20.0) StartLeak(w, h, now);
        }

        readonly List<Hits> spawnLater = new List<Hits>();
        readonly List<Wreck> spawnLaterWreck = new List<Wreck>();
        long stepBegan;

        /// <summary>What has been torn off a part becomes pieces and bits (torn off a burning wreck near its fire: hot, smoking, some of it alight).</summary>
        void SpawnTorn(Wreck w, Hits h, Hull hull)
        {
            Part p = h.part;
            bool fire = w != null && w.fireUntil > Time.time && w.firePart != null && w.firePart.transform != null && (w.firePart.transform.position - p.transform.position).sqrMagnitude < 36f;
            spawnHeat = fire ? 0.5f : 0f; spawnBurning = fire ? 0.15f : 0f; spawnFrom = p.transform.position;
            Sounds.Tear(p.transform.position, hull.removed.Count);
            Spawn(w, h, hull);
            spawnHeat = 0f; spawnBurning = 0f;
        }

        /// <summary>The pieces left over from busy steps: as many as there is time for in this one (at least one part's).</summary>
        void SpawnSome()
        {
            int done = 0;
            while (spawnLater.Count > 0 && (done == 0 || System.Diagnostics.Stopwatch.GetTimestamp() - stepBegan < System.Diagnostics.Stopwatch.Frequency * 6 / 1000))
            {
                Hits h = spawnLater[0];
                Wreck w = spawnLaterWreck[0];
                spawnLater.RemoveAt(0); spawnLaterWreck.RemoveAt(0);
                if (h == null || h.gone || h.part == null || h.hull == null || h.hull.removed.Count == 0) continue;
                try { SpawnTorn(w, h, h.hull); } catch (Exception ex) { Note("could not make " + Name(h) + "'s pieces: " + ex.Message); }
                done++;
            }
        }

        /// <summary>
        /// Whatever held this part to the parts beside it and is gone lets go of them: its parent, if the skin round where it is joined to
        /// it is torn away; any part joined to it, likewise. (A tank torn right through has its far end come away with what hangs from it.)
        /// </summary>
        void Sever(Wreck w, Hits h, Hull hull)
        {
            long began = Prof.Now;
            try { SeverNow(w, h, hull); }
            finally { Prof.Add(Prof.Sever, began); }
        }

        void SeverNow(Wreck w, Hits h, Hull hull)
        {
            Part p = h.part;
            // (judged against what was there: the skin round where they are joined, out to most of the part's width, all but gone. A ring or an
            // open end has no skin at the very middle to begin with, and is not let go for that)
            float reach = Mathf.Max(0.4f, 0.45f * Width(h));
            if (p.parent != null)
            {
                Vector3 at = JoinedAt(p, p.parent);
                if (hull.HeldAt(at, reach) < 0.25f)
                {
                    TornAway++;
                    Note(Name(h) + " is torn away from " + (p.parent.partInfo != null ? p.parent.partInfo.title : "the part it hung from"));
                    Sounds.Tear(p.transform.position, 8f);
                    try { p.decouple(); } catch (Exception ex) { Note("could not part " + Name(h) + ": " + ex.Message); }
                }
            }
            for (int k = p.children.Count - 1; k >= 0; k--)
            {
                Part child = p.children[k];
                if (child == null) continue;
                Vector3 at = JoinedAt(p, child);
                if (hull.HeldAt(at, reach) >= 0.25f) continue;
                TornAway++;
                Note((child.partInfo != null ? child.partInfo.title : "a part") + " is torn away from " + Name(h));
                try { child.decouple(); } catch (Exception ex) { Note("could not part them: " + ex.Message); }
            }
        }

        /// <summary>Where, in a part's own axes, another part beside it is joined to it.</summary>
        static Vector3 JoinedAt(Part p, Part other)
        {
            AttachNode node = p.FindAttachNodeByPart(other);
            if (node != null) return node.position;
            // (fixed to its side: where the other's own point of attachment is)
            if (other.srfAttachNode != null && other.parent == p) return p.transform.InverseTransformPoint(other.transform.TransformPoint(other.srfAttachNode.position));
            if (p.srfAttachNode != null && p.parent == other) return p.srfAttachNode.position;
            return p.transform.InverseTransformPoint(other.transform.position);
        }

        /// <summary>
        /// A hurt part bends before it breaks: what holds it to the part it hangs from is softened as it is hurt (and as the skin there is torn),
        /// down to a twelfth of its stiffness, so that a booster droops and a tank folds at a tear before either comes away. (THE FINALS
        /// has a state between whole and loose for the same reason: see the Research page.) Parts held rigidly by the game are left as they are.
        /// </summary>
        static void Weaken(Hits h)
        {
            Part p = h.part;
            PartJoint joint = p.attachJoint;
            if (joint == null || joint.joints == null) return;
            float hurt = Mathf.Clamp01((h.damage - 0.3f) / 0.45f);
            if (h.hull != null && p.parent != null) hurt = Mathf.Max(hurt, 1f - h.hull.HeldAt(JoinedAt(p, p.parent), Mathf.Max(0.3f, 0.8f * h.hull.plan.edge)));
            float keep = 1f - 0.92f * Mathf.Clamp01(hurt);
            if (keep > h.stiffness - 0.05f) return;
            h.stiffness = keep;
            float spring = joint.stiffness * keep;
            foreach (ConfigurableJoint j in joint.joints)
            {
                if (j == null) continue;
                JointDrive x = j.angularXDrive, yz = j.angularYZDrive;
                if (float.IsInfinity(x.positionSpring) || x.positionSpring <= 0f) continue;
                // (a little damping as well, so that it does not wobble about on a soft spring)
                x.positionSpring = spring; x.positionDamper = spring * 0.04f;
                yz.positionSpring = spring; yz.positionDamper = spring * 0.04f;
                j.angularXDrive = x; j.angularYZDrive = yz;
            }
        }

        static double Oxidiser(Part p)
        {
            double kg = 0;
            if (p != null && p.Resources != null)
                foreach (PartResource r in p.Resources)
                    if (r != null && r.info != null && r.amount > 0 && (r.resourceName == "Oxidizer" || r.resourceName.IndexOf("Oxygen", StringComparison.OrdinalIgnoreCase) >= 0 || r.resourceName == "LOX" || r.resourceName == "NTO"))
                        kg += r.amount * r.info.density * 1000.0;
            return kg;
        }

        static double Mono(Part p)
        {
            double kg = 0;
            if (p != null && p.Resources != null)
                foreach (PartResource r in p.Resources)
                    if (r != null && r.info != null && r.amount > 0 && (r.resourceName == "MonoPropellant" || r.resourceName.IndexOf("Hydrazine", StringComparison.OrdinalIgnoreCase) >= 0)) kg += r.amount * r.info.density * 1000.0;
            return kg;
        }

        /// <summary>(Since 0.3.0.) Whether a resource lies on the ground as a liquid when it runs out: not one kept frozen (liquid hydrogen,
        /// oxygen and methane boil away at once), nor a solid or a gas.</summary>
        static bool Pools(PartResource r)
        {
            string name = r.resourceName;
            return !(name == "SolidFuel" || name.IndexOf("Hydrogen", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Oxygen", StringComparison.OrdinalIgnoreCase) >= 0
                     || name.IndexOf("Methane", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Gas", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        static bool Burns(PartResource r)
        {
            if (r == null || r.info == null) return false;
            string name = r.resourceName;
            return !(name == "ElectricCharge" || name == "Ablator" || name == "IntakeAir" || name == "Ore" || name == "XenonGas" || name.IndexOf("Water", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        /// <summary>Kilograms of whatever in the part burns.</summary>
        public static double Propellant(Part p, bool solidOnly = false)
        {
            double kg = 0;
            if (p == null || p.Resources == null) return 0;
            foreach (PartResource r in p.Resources)
            {
                if (r == null || r.info == null || r.amount <= 0) continue;
                string name = r.resourceName;
                bool solid = name == "SolidFuel" || name.IndexOf("Solid", StringComparison.OrdinalIgnoreCase) >= 0;
                if (solidOnly && !solid) continue;
                if (!Burns(r)) continue;
                kg += r.amount * r.info.density * 1000.0;
            }
            return kg;
        }

        void StartLeak(Wreck w, Hits h, float now)
        {
            Part p = h.part;
            h.leaking = true;
            Split++;
            Vector3 c = h.touches > 0 ? h.point / h.touches : p.transform.position;
            Vector3 out_ = c - p.transform.position;
            if (out_.sqrMagnitude < 1e-4f) out_ = Random.onUnitSphere;
            var leak = new Leak
            {
                h = h, wreck = w, local = p.transform.InverseTransformPoint(c), dir = p.transform.InverseTransformDirection(out_.normalized),
                began = now, had = Propellant(p),
            };
            // The worse the split, the sooner what is left goes up: a moment to a second and a half. (In the 1984 crash of a Boeing 720
            // flown into the ground for the purpose, fuel from a torn tank caught a tenth of a second after the engine beside it was
            // smashed: see the Research page.)
            float worst = h.hull != null ? Mathf.Clamp01((1f - h.hull.Left) * 3f + h.crushed) : Mathf.Clamp01((h.damage - 0.5f) * 2f);
            leak.until = now + Mathf.Lerp(1.6f, 0.4f, worst) * Random.Range(0.8f, 1.2f);
            CelestialBody body = p.vessel != null ? p.vessel.mainBody : null;
            bool oxygen = body != null && body.atmosphere && body.atmosphereContainsOxygen && p.vessel.staticPressurekPa > 5.0;
            bool oxidiser = Oxidiser(p) > 1.0;
            // Whether it catches. In air there is everything it needs, and a crash makes sparks and hot metal enough, most times; not always
            // (a Boeing 777 that came down short at Heathrow in 2008 tore a tank open and nothing caught). Where there is no oxygen to burn in,
            // only a tank with its own oxidiser burns, and then it does: fuel and oxidiser sprayed together off torn metal.
            leak.lit = oxygen ? Random.value < 0.75f : oxidiser;
            // Crushed right through, a full tank does not leak for a moment: it bursts, and what comes out catches at once, as at the Boeing 720's
            // tenth of a second (a rocket driven straight into the ground goes up as it hits).
            if (h.crushed >= 1f && (oxygen || oxidiser)) { leak.lit = oxygen ? Random.value < 0.95f : true; leak.until = now + Random.Range(0.03f, 0.12f); }
            // What is in it decides how it looks: solid propellant does not run out, it burns through the split, white; monopropellant burns pale and clean.
            double solid = Propellant(p, true), all = Math.Max(1.0, leak.had);
            if (solid > all * 0.5) { leak.lit = true; leak.tint = 1; leak.shade = 0.8f; leak.warm = 0.05f; }
            else if (Mono(p) > all * 0.5) { leak.tint = 2; leak.shade = 0.75f; leak.warm = 0f; }
            // One that does not catch runs out for longer and does not go up: what is left in the tank is still there when it stops.
            if (!leak.lit) leak.until = now + Random.Range(5f, 9f);
            leaks.Add(leak);
            Note(Name(h) + " split and is leaking (" + leak.had.ToString("F0") + " kg in it" + (leak.lit ? ", alight" : "") + ")");
        }

        /// <summary>
        /// (Since 0.3.0.) Fuel spilt and alight burns where it lies, as a pool fire: a flame as wide as the pool and as tall as the lore of
        /// pool fires has it (Heskestad's mean flame height, L = 0.235 Q^0.4 - 1.02 D, Q the fire's heat in kilowatts: kerosene burning
        /// 0.055 kg a square metre a second, 43 MJ a kilogram, seven tenths of it in a sooty flame), some 31 m for a pool 28 m across and
        /// 5 m for one 2 m across, under thick black smoke that goes on up as a column; burning until the fuel is gone (see
        /// Marks.Burning). Through the explosions mod: without it, nothing burns.
        /// </summary>
        void Pools(float dt)
        {
            if (!hooked) return;
            for (int k = 0; k < 6; k++)
            {
                if (!Marks.Burning(k, dt, out Vector3 at, out Vector3 up, out float radius, out int id, out float left, out Transform lies)) break;
                float heat = 0.055f * Mathf.PI * radius * radius * 43000f * 0.7f;
                float tall = Mathf.Clamp(0.235f * Mathf.Pow(heat, 0.4f) - 2.04f * radius, 1.5f, 40f);
                // (the flame is worked out in a patch of air held to the ground under it: given nothing to go along with, the explosions
                // mod draws a fire's smoke alone, and the first pool fires were a low ball of smoke with no flame. Their smoke hung three
                // seconds, and never made a column)
                try { Feed.Burn(id, at + up * 0.1f, up, Vector3d.zero, Mathf.Max(0.3f, 0.85f * radius), tall, 1f, Mathf.Clamp01(left / 50f + 0.3f), 0.04f, 0.3f, 12f + 2f * Mathf.Min(radius, 14f), 0, 1f, lies, Feed.Burning); }
                catch (Exception) { hooked = false; return; }
            }
        }

        void Leaks(float dt, float now)
        {
            for (int n = leaks.Count - 1; n >= 0; n--)
            {
                Leak leak = leaks[n];
                Hits h = leak.h;
                if (h == null || h.part == null || h.gone || h.part.State == PartStates.DEAD) { leaks.RemoveAt(n); continue; }
                Part p = h.part;
                // What runs out: a third or so of it a second, the rest still in it when it goes.
                double lost = 0, liquid = 0;
                float share = 1f - Mathf.Exp(-dt / 2.5f);
                if (p.Resources != null)
                    foreach (PartResource r in p.Resources)
                    {
                        if (!Burns(r) || r.amount <= 0) continue;
                        double out_ = r.amount * share;
                        r.amount -= out_;
                        lost += out_ * r.info.density;           // tonnes
                        if (Pools(r)) liquid += out_ * r.info.density;
                    }
                leak.flow = (float)(lost / Math.Max(dt, 1e-4f));
                // (since 0.3.0: what runs out falls to the ground under the hole, if it is within forty metres, and stains it;
                // and alight, it burns there as a pool: see Marks.Spill)
                if (liquid > 0 && (Frag.Spills == null || Frag.Spills.Value))
                {
                    Vector3 hole = p.transform.TransformPoint(leak.local), up = FlightGlobals.getUpAxis(p.vessel != null ? p.vessel.mainBody : FlightGlobals.currentMainBody, hole);
                    if (Physics.Raycast(hole + up * 0.3f, -up, out RaycastHit below, 40f, 1 << 15, QueryTriggerInteraction.Ignore))
                        Marks.Spill(leak, below.point, up, (float)(liquid * 1000.0), leak.lit, Paved(below.collider));
                }
                // and it pushes the craft the other way, as anything let out under pressure does
                Rigidbody rb = BodyOf(p);
                if (rb != null && leak.flow > 0f)
                {
                    Vector3 at = p.transform.TransformPoint(leak.local), way = p.transform.TransformDirection(leak.dir);
                    rb.AddForceAtPosition(-way * leak.flow * 25f, at, ForceMode.Force);
                }
                if (now >= leak.until)
                {
                    leaks.RemoveAt(n);
                    // (alight, the rest goes up; not, it has simply run out, and the split tank is left as it is)
                    if (leak.lit) GoUp(leak.wreck, h, leak);
                    else Note(Name(h) + " has stopped leaking: nothing caught");
                }
            }
        }

        /// <summary>
        /// A split tank's propellant goes up: the game's own explosion where the tear is (made, with the explosions mod, from what was in it), the
        /// skin round the tear blown away, the rest left blackened and bent back, and the wreck burning. A tank with almost nothing left of its
        /// skin, or one this mod cannot tear, goes altogether.
        /// </summary>
        void GoUp(Wreck w, Hits h, Leak leak)
        {
            Part p = h.part;
            Hull hull = HullFor(h);
            if (hull == null || p == null) { Break(w, h, false); return; }
            double fuel = Propellant(p);
            Vector3 at = p.transform.TransformPoint(leak.local);
            float impact = Mathf.Max(h.hardest, w != null ? w.body.V.magnitude : 0f);
            Burst++;
            long t = Prof.Now;
            try
            {
                string title = p.partInfo != null ? p.partInfo.title : p.name;
                GameEvents.onCrash.Fire(new EventReport(FlightEvents.CRASH, p, title, Localizer.Format("#autoLOC_438839"), 0, "", impact));
            }
            catch (Exception) { }
            Prof.Add(Prof.Events, t);
            t = Prof.Now;
            if (hooked && !noBurst)
            {
                try { Feed.Burst(p, at, impact, false); }
                catch (Exception) { noBurst = true; }
            }
            FXMonger.Explode(p, at, Mathf.Max(0.3f, p.explosionPotential));
            Prof.Add(Prof.Explode, t);
            // What was in it is gone: burned, or thrown out burning. (Solid propellant broken up burns on with its pieces.)
            bool solid = Propellant(p, true) > fuel * 0.5;
            if (p.Resources != null)
                foreach (PartResource r in p.Resources)
                    if (Burns(r)) r.amount = 0;
            Vector3 up = p.vessel != null ? (Vector3)FlightGlobals.getUpAxis(p.vessel.mainBody, at) : Vector3.up;
            // The more there was, the more of the skin goes: from two thirds left for a few kilograms to a fifth for tonnes.
            float leave = Mathf.Clamp(0.62f - 0.11f * Mathf.Log10(1f + (float)fuel / 20f), 0.2f, 0.62f);
            // (and the harder it was crushed first, the less: a tank driven straight into the ground leaves next to nothing)
            leave *= 1f - 0.8f * Mathf.Clamp01((h.crushed - 0.4f) / 0.8f);
            leave = Mathf.Min(leave, hull.Left);
            t = Prof.Now;
            hull.BlowOff(at, leave, up);
            // (half the time a torn tank's far end comes away as one: its first crack runs along it and then round it, and the end, a dome and a
            // length of shell, is driven off along the tank's axis by what escapes, as burst pressure vessels' end caps are: see the Research page)
            Vector3 axisWorld = Vector3.zero;
            if (Random.value < 0.5f && Frames.Round(hull.plan, out float tankRadius, out float tankLength))
            {
                Vector3 axis = p.transform.up;
                float side = Vector3.Dot(at - p.transform.TransformPoint(hull.plan.mid), axis) >= 0f ? -1f : 1f;      // the end away from the tear
                if (hull.CutRing(p.transform.TransformPoint(hull.plan.mid) + axis * (side * tankLength * Random.Range(0.18f, 0.32f)), axis) > 0) axisWorld = axis * side;
            }
            hull.CutLoose();
            Prof.Add(Prof.BlowOff, t);
            spawnCutKick = axisWorld * Random.Range(6f, 16f);
            spawnHeat = 1f; spawnBurning = solid ? 0.85f : 0.55f; spawnFrom = at;
            Spawn(w, h, hull);
            spawnHeat = 0f; spawnBurning = 0f; spawnCutKick = Vector3.zero;
            float fire = (float)fuel * 0.9f;
            float R = 1.75f * Mathf.Pow(Mathf.Max(1f, fire), 0.29f);
            Piece.Blast(at, R * 2.2f, Mathf.Min(30f, 5f + 3f * Mathf.Sqrt(fire / 100f)));
            ShoveCraft(at, R * 1.5f, Mathf.Min(2f, 0.4f + 0.2f * Mathf.Sqrt(fire / 200f)), p.vessel);
            Blasted(at, Mathf.Max(25f, R * 2.2f));
            // The wreck burns on where it went up, and goes on burning as it slides: what spilled is alight in the torn structure.
            if (w != null)
            {
                float now = Time.time;
                if (w.fireUntil < now || R > w.fireBig * 0.8f) { w.firePart = p; w.fireLocal = leak.local; w.fireBegan = now; }
                w.fireUntil = Mathf.Max(w.fireUntil, now + Random.Range(14f, 26f));
                w.fireBig = Mathf.Max(w.fireBig, R);
            }
            hull.Scorch(at, 0.6f * hull.plan.size);
            // (and the bones of it: arcs of its rings and bent stringers)
            // (a step or two later: the burst and its panels in this one, the frame's other work and the craft parting in the next ones, so that
            // no one frame has it all to do)
            HullPlan plan = hull.plan;
            Vector3 local = p.transform.InverseTransformPoint(at);
            float kick = Mathf.Min(18f, 6f + 2f * Mathf.Sqrt((float)fuel / 100f));
            Later(() =>
            {
                if (p == null || p.State == PartStates.DEAD) return;
                Rigidbody prb = BodyOf(p);
                long t2 = Prof.Now;
                Frames.Throw(plan, p.transform.TransformPoint(local), p.transform.rotation, prb != null ? prb.velocity : Vector3.zero, kick, Random.Range(1, 3), Random.Range(2, 4), 0.6f, p);
                Prof.Add(Prof.Frames, t2);
            });
            h.sever = true;
            Note(Name(h) + " went up (" + fuel.ToString("F0") + " kg), " + (hull.Left * 100f).ToString("F0") + "% of its skin left");
            if (hull.Left < 0.15f) Break(w, h, false);
            else Later(() => { if (!h.gone && !h.doomed && h.part != null && h.part.State != PartStates.DEAD) { h.sever = false; Sever(w, h, hull); } });
        }

        // ---- what is left to do of a blow, a burst or a part going, a step or two later (one thing a step, or more while there is time)
        readonly Queue<Action> later = new Queue<Action>();

        void Later(Action a) => later.Enqueue(a);

        void RunLater()
        {
            long until = System.Diagnostics.Stopwatch.GetTimestamp() + System.Diagnostics.Stopwatch.Frequency * 2 / 1000;
            int done = 0;
            while (later.Count > 0 && (done == 0 || System.Diagnostics.Stopwatch.GetTimestamp() < until))
            {
                Action a = later.Dequeue();
                try { a(); } catch (Exception ex) { Note("could not finish: " + ex.Message); }
                done++;
            }
        }

        struct Doomed { public Wreck w; public Hits h; public bool blown; public Part other; }
        readonly Queue<Doomed> doomed = new Queue<Doomed>();

        /// <summary>A part is to go altogether. It does at the end of this step, or (when many go at once, as when a craft comes straight down) in one of the next few.</summary>
        void Break(Wreck w, Hits h, bool blown)
        {
            if (h.gone || h.doomed) return;
            h.doomed = true;
            doomed.Enqueue(new Doomed { w = w, h = h, blown = blown, other = h.other });
        }

        void BreakSome()
        {
            long began = System.Diagnostics.Stopwatch.GetTimestamp();
            long until = began + System.Diagnostics.Stopwatch.Frequency * 4 / 1000;
            int done = 0;
            while (doomed.Count > 0 && (done == 0 || System.Diagnostics.Stopwatch.GetTimestamp() < until))
            {
                Doomed d = doomed.Dequeue();
                try { BreakNow(d.w, d.h, d.blown, d.other); }
                catch (Exception ex) { Note("could not break a part up: " + ex.Message); }
                done++;
            }
            BreakTicks += System.Diagnostics.Stopwatch.GetTimestamp() - began;
        }

        /// <summary>A part goes altogether: what is left of its skin in pieces, and its explosion if it carried something that burns.</summary>
        void BreakNow(Wreck w, Hits h, bool blown, Part other)
        {
            if (h == null || h.gone) return;
            h.gone = true;
            Part p = h.part;
            if (p == null || p.State == PartStates.DEAD) return;
            Broken++;
            double fuel = Propellant(p);
            bool burns = fuel > 20.0;
            float violence = Mathf.Clamp(h.damage - 1f, 0f, 4f);
            // Solid propellant broken up burns, it does not go off: the boosters of Challenger flew on out of the fireball (see the
            // Research page). So most of it is not in the blast; it goes with the pieces, burning.
            bool solid = burns && Propellant(p, true) > fuel * 0.5;
            if (solid && p.Resources != null)
            {
                foreach (PartResource r in p.Resources)
                    if (r != null && r.info != null && (r.resourceName == "SolidFuel" || r.resourceName.IndexOf("Solid", StringComparison.OrdinalIgnoreCase) >= 0)) r.amount *= 0.25;
                fuel = Propellant(p);
            }
            // The game's word for what happened, before the part goes: its log has it, and the explosions mod reads it to make
            // a crash's blast (thrown on ahead, a burn mark drawn out) and not a blast in the air.
            long t = Prof.Now;
            try
            {
                string title = p.partInfo != null ? p.partInfo.title : p.name;
                if (other != null) GameEvents.onCollision.Fire(new EventReport(FlightEvents.COLLISION, p, title, other.partInfo != null ? other.partInfo.title : "", 0, "", h.hardest));
                else GameEvents.onCrash.Fire(new EventReport(FlightEvents.CRASH, p, title, Localizer.Format("#autoLOC_438839"), 0, "", Mathf.Max(h.hardest, w != null ? w.body.V.magnitude : 0f)));
            }
            catch (Exception) { }
            Prof.Add(Prof.Events, t);
            int made = 0;
            Hull hull = HullFor(h);
            try
            {
                if (hull != null)
                {
                    // (an engine worn through as it was dragged along the ground is strewn behind it, as what the ground tears off a skin is:
                    // its last pieces are left on the ground, not sent on at the speed it was going)
                    bool dragged = !burns && Solid(h) && h.slidOn > 2f && Time.fixedTime - h.lastTouch < 0.3f;
                    hull.TearAll(dragged ? Torn.Ground : Torn.Broken);
                    spawnHeat = burns ? 0.8f : 0f; spawnBurning = burns ? (solid ? 0.85f : 0.4f) : 0f; spawnFrom = p.transform.position; spawnViolence = violence;
                    made = Spawn(w, h, hull, true);
                    spawnHeat = 0f; spawnBurning = 0f; spawnViolence = 0f;
                    Rigidbody prb = BodyOf(p);
                    // (the part itself is gone by then: where it was, and how it was going)
                    HullPlan plan = hull.plan;
                    Vector3 where = p.transform.position, going = prb != null ? prb.velocity : Vector3.zero;
                    if (dragged) going *= 0.4f;
                    Quaternion turn = p.transform.rotation;
                    float kick = 3f + 3f * violence + (burns ? 6f : 0f), soot = burns ? 0.5f : 0f;
                    int rings = Random.Range(0, 2), bars = Random.Range(1, 3);
                    Later(() => { long t2 = Prof.Now; Frames.Throw(plan, where, turn, going, kick, rings, bars, soot, null); Prof.Add(Prof.Frames, t2); });
                }
                else made = Fragments(w, h, violence, burns, solid);
            }
            catch (Exception ex) { Note("could not break " + Name(h) + " up: " + ex.Message); }
            // (the small parts fixed to it go to pieces with it, rather than each flying off as a craft of its own: a fin or a decoupler on a tank
            // that is blown apart is torn apart with it; and every craft the game has to keep costs it something every frame)
            for (int k = p.children.Count - 1; k >= 0; k--)
            {
                Part child = p.children[k];
                if (child == null || child.State == PartStates.DEAD || child.children.Count > 0) continue;
                Hits ch = child.GetComponent<Hits>();
                if (ch == null || ch.gone || ch.doomed || !ch.armed) continue;
                float area = ch.plan != null && ch.plan.ready ? ch.plan.area : 99f;
                if (child.mass > 0.2f || area > 2.5f || Propellant(child) > 20.0) continue;
                ch.doomed = true;
                ch.why = "the part it was fixed to going";
                try { BreakNow(w, ch, false, null); } catch (Exception ex) { Note("could not break " + Name(ch) + " up: " + ex.Message); }
            }
            // (once it is in pieces, it is not drawn whole as well; and the explosions mod, which makes pieces of its own from what a
            // destroyed part draws, makes none of this one)
            Hide(p);
            Note(Name(h) + " broke up into " + made + " pieces" + (burns ? " and went up (" + fuel.ToString("F0") + " kg)" : "") + " after " + h.why);
            Vector3 at = p.transform.position;
            if (burns)
            {
                float fire = (float)fuel * 0.9f;
                float R = 1.75f * Mathf.Pow(Mathf.Max(1f, fire), 0.29f);
                Piece.Blast(at, R * 2.2f, Mathf.Min(30f, 5f + 3f * Mathf.Sqrt(fire / 100f)));
                ShoveCraft(at, R * 1.5f, Mathf.Min(2f, 0.4f + 0.2f * Mathf.Sqrt(fire / 200f)), p.vessel);
                Blasted(at, Mathf.Max(25f, R * 2.2f));
                if (w != null)
                {
                    float now = Time.time;
                    w.fireUntil = Mathf.Max(w.fireUntil, now + Random.Range(8f, 16f));
                    w.fireBig = Mathf.Max(w.fireBig, R);
                    if (w.firePart == null || w.firePart == p) { w.firePart = p.parent; w.fireLocal = Vector3.zero; w.fireBegan = now; }
                }
                p.explode();
            }
            else
            {
                // A part with nothing in it that burns is crushed, not blown up: dust and sparks, and no fireball (the weakest of the game's blasts).
                if (blown) FXMonger.Explode(p, at, 0.0);
                p.Die();
            }
        }

        static void Hide(Part p)
        {
            foreach (MeshRenderer r in p.FindModelComponents<MeshRenderer>()) if (r != null) r.enabled = false;
            Hull hull = p.GetComponent<Hull>();
            if (hull != null) { hull.Hide(); Destroy(hull); }
        }

        /// <summary>A blast shoves the craft near it (as one body each: see Body).</summary>
        void ShoveCraft(Vector3 at, float reach, float speed, Vessel source)
        {
            foreach (Wreck w in wrecks.Values)
            {
                Vessel v = w.vessel;
                if (v == null || v.packed || !w.body.Of(v)) continue;
                Vector3 away = w.body.C - at;
                float d = away.magnitude;
                if (d > reach + Size(v)) continue;
                float falls = Mathf.Clamp01(1f - d / (reach + Size(v)));
                Vector3 V0 = w.body.V, W0 = w.body.W;
                Vector3 way = d > 0.1f ? away / d : Vector3.up;
                // (pushed where it is nearest the blast, so that it turns as well)
                Vector3 near = w.body.C - way * Mathf.Min(d, Size(v) * 0.5f);
                w.body.Push(near, way * (w.body.M * speed * falls));
                w.body.Commit(v, V0, W0);
                v.IgnoreGForces(6);
            }
        }

        // ================================================================== the record

        static string Name(Hits h) => h.part != null && h.part.partInfo != null ? h.part.partInfo.title : "a part";

        static readonly List<string> notes = new List<string>();

        /// <summary>A line for the log (and, in the development build, for the list of what happened lately).</summary>
        public static void Note(string text)
        {
            string line = Time.time.ToString("F2") + " " + text;
            notes.Add(line);
            if (notes.Count > 120) notes.RemoveRange(0, notes.Count - 120);
            Frag.Log(text);
#if DEV
            Clock.NotesThisFrame++;
#endif
        }

        public static string Notes(int last)
        {
            int from = Mathf.Max(0, notes.Count - last);
            return string.Join("\n", notes.GetRange(from, notes.Count - from).ToArray());
        }

        public IEnumerable<Wreck> Wrecks => wrecks.Values;

        // (what each part of the work costs on the game's thread, for the development build's Stats; and switches for finding out)
        public static long FixedTicks, AskedTicks, BreakTicks;
    }
}
