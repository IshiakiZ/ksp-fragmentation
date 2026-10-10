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
        public float riskUntil, touched = -100f, since, burning, burnBig, calmSince;
        public bool inContact, calm;
        public float slid, fastest, hardest;
        public Vector3 dustAt, dustNormal, dustGoing;
        public float dustSpeed, dustSize;
        public bool dustPaved;
        public string name = "";
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
    /// * **Who may crash.** A craft going faster than its weakest part can take, near enough to the ground (or to
    ///   another craft) to reach it within a second or so, is armed: the crash tolerance of each of its parts is raised
    ///   past anything the game could measure, so that the game no longer destroys a part the instant it is touched,
    ///   and each part reports what it touches (see Hits). A craft that has come to rest, or is out of reach of
    ///   anything, is given its tolerances back. Over water nothing is armed: what water does is the game's.
    /// * **A blow.** A part that runs into the ground is crushed by as much of the craft's energy as goes into the
    ///   ground at that point: half the mass that a push there has to move (less than the whole, at the end of a long
    ///   craft, which turns as it is stopped), times the square of the speed into the ground. A part takes so much
    ///   before it breaks up: its mass times the square of a few times its crash tolerance. If it holds, the craft's
    ///   going into the ground there is stopped, with a little bounce, and the ground's bite takes some of its speed
    ///   along it; if it does not, the part is crushed and the craft goes on into the ground a little slower, for the
    ///   next part to take. The craft is pushed as one rigid body (see Body). So a craft that comes in at a slant loses
    ///   its fall in the first few parts that hit, keeps most of its speed along the ground, and is turned over by the
    ///   blow; one that comes straight down is crushed part after part.
    /// * **The slide.** A craft lying on the ground and moving along it is slowed by friction and, on soil, by
    ///   ploughing into it, which grows with speed (see the wiki's Research page for where the numbers come from); the
    ///   drag acts where it touches, so it turns the craft over that point, and the parts that touch are ground away by
    ///   the work it does on them, shedding pieces as they go.
    /// * **What becomes of a part.** Hurt past half, a tank splits and leaks for a moment, then goes up with what is
    ///   left in it; a hurt part may tear off with whatever hangs from it; past whole, a part breaks up into pieces of
    ///   its own skin (see Shatter), and if it carried propellant, that is its explosion, the game's own (the
    ///   Volumetric Explosions mod makes it from what was in the part, and from this mod's word that it was a crash and
    ///   how fast).
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public sealed class Crashes : MonoBehaviour
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
        readonly Shatter.Skin skin = new Shatter.Skin();
        bool hooked, complained, noMovers;
        float burningShare = 0.4f;                     // (of the pieces of the part breaking up now, how many burn)
        float slowFrames = 0.02f;                      // how long the frames have lately been taking, seconds (see Fragments)
        readonly List<Piece> moving = new List<Piece>();
        static readonly Comparison<Piece> Bigger = (a, b) => b.reach.CompareTo(a.reach);
        public static string Report = "", Why = "";
        public static bool TestOff;                    // (development build: the mod stood aside, to compare with the game's own crashes, without touching the player's settings)
        public static int Broken, Torn, Split, Made;

        // ---- the figures the crashes are worked out by (see the wiki's Research page). Fields, so that the development build can try others.
        public static float Restitution = 0.12f;      // how much of its speed into the ground a craft keeps, bouncing off
        public static float Toughness = 6f;           // a part's structure takes, before it breaks up, the energy of its own dry mass at this many times its crash tolerance (times the setting)
        public static float Bite = 0.45f, BitePaved = 0.25f;               // the ground's grip in a blow: speed along it lost for each unit of speed into it stopped
        public static float Friction = 0.5f, FrictionPaved = 0.35f;        // sliding friction on soil and on paving
        public static float Plough = 0.22f, PloughPaved = 0.04f;           // and what ploughing adds, per metre a second of speed
        public static float Scrape = 250f;             // how much more grinding along than a blow of the same energy a part takes (most of the work of the slide goes into the ground)
        public static float GrazeFriction = 0.5f;      // metal dragged along metal: the share of the push between them that tears at them

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
            if (!Installed()) Why = "Without the Volumetric Explosions mod (0.4 or later) there are no leaks, burning pieces or dust: the explosions are the game's own.";
            else
            {
                try { Feed.Hook(Asked); hooked = true; }
                catch (Exception ex) { Why = "Leaks, burning pieces and dust need Volumetric Explosions 0.4 or later: the one installed is older (" + ex.GetType().Name + ")."; }
            }
            if (Why.Length > 0) Frag.Log(Why);
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

        /// <summary>
        /// Whether a collider is paving (a runway, a launch pad, the hard standing round them): smoother and harder than soil. The game
        /// tells where a craft has landed by the tag on what it touches ("Runway", "LaunchPad"); the space centre's lawn belongs to the
        /// same object as its runway, but is not tagged so.
        /// </summary>
        public static bool Paved(Collider c)
        {
            if (c == null) return false;
            if (paving.TryGetValue(c, out bool paved)) return paved;
            string tag = c.gameObject.tag;
            paved = tag == "Runway" || tag == "LaunchPad" || tag.IndexOf("Runway", StringComparison.OrdinalIgnoreCase) >= 0 || tag.IndexOf("Pad", StringComparison.OrdinalIgnoreCase) >= 0;
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
            // Water is the game's: what a splash does is worked out from the crash tolerance (see PartBuoyancy).
            // (near the sea, that is: a craft in orbit over the sea has the sea under it too)
            if (v.mainBody != null && v.mainBody.ocean && (v.Splashed || (v.terrainAltitude < 0.0 && v.altitude < 3000.0))) return false;
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
                    float height = (float)v.radarAltitude;
                    if (height < speed * 1.2f + size + 12f) risk = true;
                }
                // ...or another craft, coming together faster than the weaker can take
                for (int k = 0; k < loaded.Count; k++)
                {
                    Vessel o = loaded[k];
                    if (o == v || !Eligible(o)) continue;
                    float rel = (float)(v.obt_velocity - o.obt_velocity).magnitude;
                    if (rel < 0.7f * Mathf.Min(least, LeastTolerance(o))) continue;
                    float apart = (float)(v.CoMD - o.CoMD).magnitude - size - Size(o);
                    if (apart < rel * 1.5f + 15f) { risk = true; break; }
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

        Wreck Arm(Vessel v, float now)
        {
            if (!wrecks.TryGetValue(v, out Wreck w))
            {
                w = new Wreck { vessel = v, since = now, name = v.GetDisplayName() };
                wrecks[v] = w;
                Note("watching " + w.name + " (" + v.parts.Count + " parts, " + v.srf_velocity.magnitude.ToString("F0") + " m/s, " + v.radarAltitude.ToString("F0") + " m up)");
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
            if (w.slid > 1f || w.hardest > 0f) Note(w.name + " is left as it lies: slid " + w.slid.ToString("F0") + " m, hardest blow " + w.hardest.ToString("F0") + " m/s");
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
            try
            {
                Watch(now);
                stepping.Clear();
                stepping.AddRange(wrecks.Values);
                foreach (Wreck w in stepping) Step(w, dt, now);
                Leaks(dt, now);
                BreakSome();
                Piece.Step(dt);
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

        void Update()
        {
            slowFrames = Mathf.Lerp(slowFrames, Mathf.Min(0.2f, Time.unscaledDeltaTime), 0.1f);
            if (Time.unscaledTime < reportAt) return;
            reportAt = Time.unscaledTime + 0.5f;
            int moving = 0;
            foreach (Piece p in Piece.All) if (p != null && !p.resting) moving++;
            Report = (wrecks.Count > 0 ? wrecks.Count + " craft that may crash " + (wrecks.Count == 1 ? "is" : "are") + " being watched. " : "") +
                     (Piece.Count > 0 ? Piece.Count + " pieces of wreckage (" + moving + " moving). " : "") +
                     (Broken + Torn + Split > 0 ? "So far " + Broken + " parts broken up, " + Torn + " torn off, " + Split + " tanks split." : "No crash yet.");
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
                if (h == null || h.touches == 0) continue;
                if (h.gone || h.doomed) { h.Clear(); continue; }
                touching.Add(h);
            }
            if (touching.Count == 0) { w.inContact = false; w.calm = false; w.dustSpeed = 0f; return; }
            try
            {
                w.touched = now;
                w.inContact = true;
                if (!w.body.Of(v)) return;
                Body b = w.body;
                Vector3 V0 = b.V, W0 = b.W;
                Vector3 frame = (Vector3)Krakensbane.GetFrameVelocity();
                Vector3 g = (Vector3)FlightGlobals.getGeeForceAtPosition(b.C);
                w.fastest = Mathf.Max(w.fastest, (b.V + frame).magnitude);
                // (lying still on the ground: it will be given its tolerances back, see Watch)
                bool calm = (b.V + frame).sqrMagnitude < 0.64f && b.W.sqrMagnitude < 0.04f;
                if (calm && !w.calm) w.calmSince = now;
                w.calm = calm;

                // ---- blows
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
                        if (into > Floor(h)) Blow(w, h, c, n, into, still, now);
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
                            float work = 0.5f * GrazeFriction * h.pushed * h.slide;
                            h.damage += work / Capacity(h) * Mathf.Clamp01((h.slide - 0.5f * t) / t);
                        }
                    }
                }

                // ---- the slide
                Slide(w, dt, frame, g, now);

                b.Commit(v, V0, W0);
                v.IgnoreGForces(4);

                // ---- what is now to become of each part that was touched
                foreach (Hits h in touching) Decide(w, h, now);
            }
            finally
            {
                foreach (Hits h in touching) if (h != null) h.Clear();
            }
        }

        /// <summary>Below this speed into something, touching it is only touching it.</summary>
        static float Floor(Hits h) => Mathf.Max(2.5f, 0.45f * h.Tolerance);

        /// <summary>
        /// How much energy (kilojoules: tonnes and metres a second) a part takes, beyond what it takes unharmed, before it
        /// breaks up. Unharmed it takes a blow at its own crash tolerance, as in the game: whatever this mod does, a craft
        /// that touches down within its parts' tolerances is not hurt. Beyond that it takes as much again as a blow of a few
        /// times its tolerance would bring (the setting "how tough parts are" scales those few times).
        /// </summary>
        static float Capacity(Hits h)
        {
            Part p = h.part;
            float k = Toughness * Mathf.Max(0.1f, Frag.Tough), t = h.Tolerance;
            // (its structure takes the blow, so its own mass without what is in it: what is in it adds to the blow and nothing to the strength.
            // So a full tank, heavy and no stronger, bursts sooner than an empty one, as real ones do: see the Research page)
            float m = Mathf.Max(0.005f, p.mass);
            return m * 0.5f * Mathf.Max(0.5f, k * k - 1f) * t * t;
        }

        void Blow(Wreck w, Hits h, Vector3 c, Vector3 n, float into, float still, float now)
        {
            Body b = w.body;
            float m = b.MassAlong(c, n);
            // Only what is beyond the part's own tolerance hurts it (see Capacity).
            float t = h.Tolerance;
            float energy = into > t ? 0.5f * m * (into * into - t * t) : 0f;
            float cap = Capacity(h), left = cap * Mathf.Max(0f, 1f - h.damage);
            float J;
            string what;
            if (energy <= left)
            {
                h.damage += energy / cap;
                // its going into the ground is stopped there, with a little bounce
                J = Mathf.Max(0f, m * (still + Restitution * into));
                what = "dented";
            }
            else
            {
                // crushed: it takes what it can, and the craft goes on into the ground that much slower
                h.damage = 1f + (energy - left) / cap;
                float after = Mathf.Sqrt(Mathf.Max(0f, into * into - t * t - 2f * left / m));
                J = Mathf.Max(0f, m * (still - after));
                what = "crushed";
            }
            h.hardest = Mathf.Max(h.hardest, into);
            w.hardest = Mathf.Max(w.hardest, into);
            h.why = "the ground at " + into.ToString("F0") + " m/s";
            Vector3 impulse = n * J;
            // The ground bites into what hits it: some of the speed along the ground goes too.
            Vector3 u = b.At(c) + (Vector3)Krakensbane.GetFrameVelocity();
            Vector3 ut = u - n * Vector3.Dot(u, n);
            float st = ut.magnitude;
            if (st > 0.1f && J > 0f)
            {
                Vector3 along = ut / st;
                float jt = Mathf.Min((h.paved ? BitePaved : Bite) * J, b.MassAlong(c, along) * st * 0.9f);
                impulse -= along * jt;
            }
            b.Push(c, impulse);
            if (into > 6f) Note(Name(h) + " hit " + (h.paved ? "paving" : "the ground") + " at " + into.ToString("F0") + " m/s (" + (into > 0f ? (u.magnitude).ToString("F0") : "0") + " m/s in all): " + what + ", hurt " + h.damage.ToString("F2"));
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
            h.damage += energy / cap;
            h.hardest = Mathf.Max(h.hardest, into);
            w.hardest = Mathf.Max(w.hardest, into);
            h.why = (h.other != null && h.other.partInfo != null ? h.other.partInfo.title : "another craft") + " at " + into.ToString("F0") + " m/s";
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
            c /= count;
            n = n.normalized;
            Vector3 u = b.At(c) + frame;
            Vector3 ut = u - n * Vector3.Dot(u, n);
            float s = ut.magnitude;
            w.dustAt = c; w.dustNormal = n; w.dustGoing = u; w.dustSpeed = s; w.dustPaved = paved * 2 > count;
            w.dustSize = Mathf.Clamp(Size(w.vessel) * 0.25f, 0.5f, 3f);
            if (s < 0.5f) return;
            float slide = Mathf.Max(0.2f, Frag.Slide);
            float pressed = Mathf.Max(0f, -Vector3.Dot(g, n));
            float share = b.M / count;
            w.slid += s * dt;
            // Each place that touches drags by its share of the craft's weight, the way that place is going: so a craft that slews
            // round as it slides is slowed in its turning as well, and one end dragging harder than the other turns it.
            foreach (Hits h in touching)
            {
                if (!h.ground) continue;
                Vector3 at = h.point / h.touches, normal = (h.normal / h.touches).normalized;
                Vector3 there = b.At(at) + frame;
                Vector3 along = there - normal * Vector3.Dot(there, normal);
                float sh = along.magnitude;
                if (sh < 0.3f) continue;
                float a = ((h.paved ? FrictionPaved : Friction) * pressed + (h.paved ? PloughPaved : Plough) * sh) / slide;
                float J = Mathf.Min(share * a * dt, share * sh * 0.5f);
                b.Push(at, -along / sh * J);
                // The work the drag does is done on the part that touches: it is ground away by it. (Dragged along slower than it
                // could take a blow, it is only scuffed.)
                float t = h.Tolerance;
                h.damage += J * sh / (Capacity(h) * Scrape) * Mathf.Clamp01((sh - 0.5f * t) / t);
                if (h.lastTouch < 0f || now - h.lastTouch > 0.5f) h.firstTouch = now;
                h.lastTouch = now;
                // shedding pieces as it goes: more, the faster it slides and the worse it is hurt
                if (sh > 12f && h.damage > 0.12f && h.shed < 6)
                {
                    h.shedDue -= dt * sh / 18f * Mathf.Clamp01(h.damage * 1.5f);
                    if (h.shedDue <= 0f)
                    {
                        h.shedDue = Random.Range(0.6f, 1.4f);
                        Shed(w, h, at, normal);
                    }
                }
            }
        }

        // ================================================================== what becomes of a part

        void Decide(Wreck w, Hits h, float now)
        {
            if (h.gone || h.part == null) return;
            Part p = h.part;
            if (h.damage >= 1f) { Break(w, h, true); return; }
            if (h.damage >= 0.3f) Weaken(h);
            if (h.leaking) return;
            if (Frag.Fires && h.damage >= 0.5f && Propellant(p) > 20.0) { StartLeak(w, h, now); return; }
            if (!h.tearing && h.damage >= 0.65f && p.parent != null && Random.value < 0.08f)
            {
                h.tearing = true;
                Torn++;
                Note(Name(h) + " tore off");
                try { p.decouple(); } catch (Exception ex) { Note("could not tear off " + Name(h) + ": " + ex.Message); }
            }
        }

        /// <summary>
        /// A hurt part bends before it breaks: what holds it to the part it hangs from is softened as it is hurt, down to a twelfth of its
        /// stiffness, so that a booster droops and a fin folds under the loads of the slide before either tears off. (THE FINALS has a state
        /// between whole and loose for the same reason: see the Research page.) Parts held rigidly by the game are left as they are.
        /// </summary>
        static void Weaken(Hits h)
        {
            Part p = h.part;
            PartJoint joint = p.attachJoint;
            if (joint == null || joint.joints == null) return;
            float keep = 1f - 0.92f * Mathf.Clamp01((h.damage - 0.3f) / 0.45f);
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
                if (name == "ElectricCharge" || name == "Ablator" || name == "IntakeAir" || name == "Ore" || name == "XenonGas" || name.IndexOf("Water", StringComparison.OrdinalIgnoreCase) >= 0) continue;
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
            leak.until = now + Mathf.Lerp(1.6f, 0.4f, Mathf.Clamp01((h.damage - 0.5f) * 2f)) * Random.Range(0.8f, 1.2f);
            CelestialBody body = p.vessel != null ? p.vessel.mainBody : null;
            bool oxygen = body != null && body.atmosphere && body.atmosphereContainsOxygen && p.vessel.staticPressurekPa > 5.0;
            bool oxidiser = Oxidiser(p) > 1.0;
            // Whether it catches. In air there is everything it needs, and a crash makes sparks and hot metal enough, most times; not always
            // (a Boeing 777 that came down short at Heathrow in 2008 tore a tank open and nothing caught). Where there is no oxygen to burn in,
            // only a tank with its own oxidiser burns, and then it does: fuel and oxidiser sprayed together off torn metal.
            leak.lit = oxygen ? Random.value < 0.75f : oxidiser;
            // What is in it decides how it looks: solid propellant does not run out, it burns through the split, white; monopropellant burns pale and clean.
            double solid = Propellant(p, true), all = Math.Max(1.0, leak.had);
            if (solid > all * 0.5) { leak.lit = true; leak.tint = 1; leak.shade = 0.8f; leak.warm = 0.05f; }
            else if (Mono(p) > all * 0.5) { leak.tint = 2; leak.shade = 0.75f; leak.warm = 0f; }
            // One that does not catch runs out for longer and does not go up: what is left in the tank is still there when it stops.
            if (!leak.lit) leak.until = now + Random.Range(5f, 9f);
            leaks.Add(leak);
            Note(Name(h) + " split and is leaking (" + leak.had.ToString("F0") + " kg in it" + (leak.lit ? ", alight" : "") + ")");
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
                double lost = 0;
                float share = 1f - Mathf.Exp(-dt / 2.5f);
                if (p.Resources != null)
                    foreach (PartResource r in p.Resources)
                    {
                        if (r == null || r.info == null || r.amount <= 0 || r.resourceName == "ElectricCharge" || r.resourceName == "Ablator") continue;
                        double out_ = r.amount * share;
                        r.amount -= out_;
                        lost += out_ * r.info.density;           // tonnes
                    }
                leak.flow = (float)(lost / Math.Max(dt, 1e-4f));
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
                    if (leak.lit) Break(leak.wreck, h, false);
                    else Note(Name(h) + " has stopped leaking: nothing caught");
                }
            }
        }

        struct Doomed { public Wreck w; public Hits h; public bool blown; public Part other; }
        readonly Queue<Doomed> doomed = new Queue<Doomed>();

        /// <summary>A part is to break up. It does at the end of this step, or (when many break at once, as when a craft comes straight down) in one of the next few: cutting a part's skin into pieces takes a millisecond or two, and a crash should not stop the game.</summary>
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

        /// <summary>A part breaks up: pieces of its skin, and its explosion if it carried something that burns.</summary>
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
            burningShare = solid ? 0.85f : 0.4f;
            // The game's word for what happened, before the part goes: its log has it, and the explosions mod reads it to make
            // a crash's blast (thrown on ahead, a burn mark drawn out) and not a blast in the air.
            try
            {
                string title = p.partInfo != null ? p.partInfo.title : p.name;
                if (other != null) GameEvents.onCollision.Fire(new EventReport(FlightEvents.COLLISION, p, title, other.partInfo != null ? other.partInfo.title : "", 0, "", h.hardest));
                else GameEvents.onCrash.Fire(new EventReport(FlightEvents.CRASH, p, title, Localizer.Format("#autoLOC_438839"), 0, "", Mathf.Max(h.hardest, w != null ? w.body.V.magnitude : 0f)));
            }
            catch (Exception) { }
            int made = 0;
            try { made = Fragments(w, h, violence, burns); }
            catch (Exception ex) { Note("could not break " + Name(h) + " up: " + ex.Message); }
            // (once it is in pieces, it is not drawn whole as well; and the explosions mod, which makes pieces of its own from what a
            // destroyed part draws, makes none of this one)
            if (made > 0) Hide(p);
            Note(Name(h) + " broke up into " + made + " pieces" + (burns ? " and went up (" + fuel.ToString("F0") + " kg)" : "") + " after " + h.why);
            Vector3 at = p.transform.position;
            if (burns)
            {
                float fire = (float)fuel * 0.9f;
                float R = 1.75f * Mathf.Pow(Mathf.Max(1f, fire), 0.29f);
                Piece.Blast(at, R * 2.2f, Mathf.Min(30f, 5f + 3f * Mathf.Sqrt(fire / 100f)));
                ShoveCraft(at, R * 1.5f, Mathf.Min(5f, 1f + 0.5f * Mathf.Sqrt(fire / 200f)), p.vessel);
                if (w != null) { w.burning = Mathf.Max(w.burning, Time.time + Random.Range(6f, 14f)); w.burnBig = Mathf.Max(w.burnBig, R); }
                Blasted(at, Mathf.Max(25f, R * 2.2f));
                p.explode();
            }
            else
            {
                // A part with nothing in it that burns is crushed, not blown up: dust and sparks, and no fireball (the weakest of the game's blasts).
                if (blown) FXMonger.Explode(p, at, 0.0);
                p.Die();
            }
        }

        void Hide(Part p)
        {
            foreach (MeshRenderer r in p.FindModelComponents<MeshRenderer>()) if (r != null) r.enabled = false;
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

        // ================================================================== pieces

        /// <summary>Break a part's skin into pieces and set them going. How many, and how big, goes with how big the part is and how hard it was hit.</summary>
        int Fragments(Wreck w, Hits h, float violence, bool burns)
        {
            Part p = h.part;
            int room = Piece.Most + 10 - Piece.Count;
            if (room <= 0) return 0;
            if (!Shatter.Gather(p, skin, 4000)) return 0;
            float size = skin.size;
            int count = Mathf.Clamp(Mathf.RoundToInt((1.5f + 1.6f * size) * (0.6f + 0.25f * Mathf.Min(violence, 3f))), 2, 12);
            // When the frames are already slow (a big crash, a big fire), fewer and bigger pieces: down to a third of them by fifteen frames
            // a second. (Space Engineers drops its effects when its time is up, for the same reason: see the Research page.)
            float slow = Mathf.Clamp01((slowFrames - 1f / 30f) / (1f / 15f - 1f / 30f));
            count = Mathf.Max(2, Mathf.RoundToInt(count * (1f - 0.65f * slow)));
            count = Mathf.Min(count, room);
            float biggest = Mathf.Clamp(size * 0.5f / (1f + 0.25f * violence), 0.25f, 5f);
            float smallest = Mathf.Max(0.15f, biggest * 0.18f);
            Shatter.Split(skin, Mathf.Clamp(biggest * 0.3f, 0.1f, 1.2f), 7000);
            List<Shatter.Cell> cells = Shatter.Cut(skin, count, biggest, smallest);
            return Launch(w, h, cells, violence, burns, count, p.mass);
        }

        /// <summary>A few pieces torn off where a part grinds along the ground. The part itself stays as it is.</summary>
        void Shed(Wreck w, Hits h, Vector3 at, Vector3 n)
        {
            if (Piece.Count >= Piece.Most) return;
            Part p = h.part;
            if (p == null || !Shatter.Gather(p, skin, 3000)) return;
            float reach = Mathf.Clamp(skin.size * 0.18f, 0.25f, 0.9f);
            Shatter.Split(skin, reach * 0.5f, 5000);
            List<Shatter.Cell> cells = Shatter.Cut(skin, 2, reach, reach * 0.4f, at, reach);
            int made = Launch(w, h, cells, 0.3f, false, 2, Mathf.Max(0.005f, p.mass * 0.03f), true);
            if (made > 0) h.shed += made;
        }

        int Launch(Wreck w, Hits h, List<Shatter.Cell> cells, float violence, bool burns, int count, float massOfAll, bool shed = false)
        {
            Part p = h.part;
            Rigidbody prb = BodyOf(p);
            float total = 0f;
            foreach (Shatter.Cell cell in cells) total += cell.area;
            if (total <= 0f) return 0;
            CelestialBody body = p.vessel != null ? p.vessel.mainBody : FlightGlobals.currentMainBody;
            Vector3 up = body != null ? (Vector3)FlightGlobals.getUpAxis(body, p.transform.position) : Vector3.up;
            byte tint = (byte)(Propellant(p, true) > Propellant(p) * 0.5 ? 1 : 0);
            int made = 0;
            for (int k = 0; k < cells.Count && made < count; k++)
            {
                Shatter.Cell cell = cells[k];
                if (cell.area < 0.02f && k > 0) continue;
                Mesh mesh = Shatter.Build(skin, cell, out Quaternion turn, out Vector3 extent);
                var painted = new Material[Shatter.usedMaterials.Count];
                var tints = new Color[painted.Length];
                // Scorched where it burned, dirtied where it scraped.
                float soot = burns ? Random.Range(0.25f, 0.6f) : shed ? Random.Range(0.55f, 0.85f) : Random.Range(0.7f, 0.95f);
                for (int m = 0; m < painted.Length; m++)
                {
                    painted[m] = skin.materials[Shatter.usedMaterials[m]];
                    Color c = skin.colours[Shatter.usedMaterials[m]];
                    tints[m] = new Color(c.r * soot, c.g * soot * 0.98f, c.b * soot * 0.95f, c.a);
                }
                Vector3 at = cell.middle;
                Vector3 v = prb != null ? prb.GetPointVelocity(at) : (w != null ? w.body.At(at) : Vector3.zero);
                // Thrown off: away from the middle of the part, harder the harder it was hit.
                Vector3 away = at - skin.middle;
                away = away.sqrMagnitude > 1e-4f ? away.normalized : Random.onUnitSphere;
                float kick = Mathf.Min(35f, (1.5f + 5f * violence) * Random.Range(0.4f, 1.1f)) + (burns ? Random.Range(2f, 7f) : 0f);
                v += (away + Random.insideUnitSphere * 0.5f + up * 0.25f).normalized * kick;
                // Kept out of the ground, and not thrown into it: what is thrown at the ground skips along it.
                if (Physics.Raycast(at + up * 4f, -up, out RaycastHit ground, 8f + extent.magnitude, 1 << 15, QueryTriggerInteraction.Ignore))
                {
                    float above = Vector3.Dot(at - ground.point, ground.normal), need = 0.5f * Mathf.Max(0.06f, extent.y) + 0.05f;
                    if (above < need) at += ground.normal * (need - above);
                    float down = Vector3.Dot(v, ground.normal);
                    if (down < 0f)
                    {
                        // What was going into the ground is sprayed out along it, and a little of it thrown back up.
                        float hit = -down;
                        v -= ground.normal * down;
                        v += ground.normal * hit * Random.Range(0.04f, 0.25f);
                        Vector3 flat = Vector3.ProjectOnPlane(Random.onUnitSphere, ground.normal);
                        if (flat.sqrMagnitude > 1e-4f) v += flat.normalized * hit * Random.Range(0.1f, 0.4f);
                    }
                }
                Vector3 spin = Random.onUnitSphere * Random.Range(2f, 9f) * (0.6f + 0.4f * violence);
                float heat = burns ? Random.Range(0.15f, 0.5f) : 0f;
                float burning = burns && Random.value < burningShare ? Random.Range(3f, 10f) : 0f;
                float mass = Mathf.Max(0.003f, massOfAll * cell.area / total);
                Piece.Make(mesh, painted, tints, at, turn, v, spin, mass, cell.area, heat, burning, tint, tint == 1, p);
                made++;
                Made++;
            }
            return made;
        }

        // ================================================================== the explosions mod's air

        /// <summary>
        /// Once a frame, when the Volumetric Explosions mod asks what is burning: each leak, the biggest of the burning pieces,
        /// the dust of each craft sliding along the ground, and the fire on a wreck whose tank went up.
        /// </summary>
        void Asked()
        {
            if (Frag.On == null || !Frag.On || TestOff || !Frag.Fires || !Feed.Ready()) return;
            long began = System.Diagnostics.Stopwatch.GetTimestamp();
            try { Say(); }
            finally { AskedTicks += System.Diagnostics.Stopwatch.GetTimestamp() - began; }
        }

        // (what each part of the work costs on the game's own thread, for the development build's Stats; and switches for finding out)
        public static long FixedTicks, AskedTicks, BreakTicks;
        public static bool FeedLeaks = true, FeedMovers = true, FeedBurning = true, FeedDust = true, FeedFire = false;

        // Where tanks went up lately (in the scene's axes, kept as places on the planet: the scene shifts), and when.
        struct Burst { public CelestialBody body; public double lat, lon, alt; public float at, reach; }
        readonly List<Burst> bursts = new List<Burst>();

        void Blasted(Vector3 at, float reach)
        {
            CelestialBody body = FlightGlobals.currentMainBody;
            if (body == null) return;
            if (bursts.Count > 16) bursts.RemoveAt(0);
            bursts.Add(new Burst { body = body, lat = body.GetLatitude(at), lon = body.GetLongitude(at), alt = body.GetAltitude(at), at = Time.time, reach = reach });
        }

        bool NearBlast(Vector3 at, float now)
        {
            foreach (Burst b in bursts)
            {
                if (now - b.at > 20f || b.body != FlightGlobals.currentMainBody) continue;
                Vector3 there = b.body.GetWorldSurfacePosition(b.lat, b.lon, b.alt);
                if ((at - there).sqrMagnitude < b.reach * b.reach) return true;
            }
            return false;
        }

        void Say()
        {
            float t = Time.time;
            Vector3d frame = Krakensbane.GetFrameVelocity();
            // ---- leaks (the flames of two at most: each flame is a patch of air of its own, worked out and drawn every frame, and dear;
            // measured on 2026-10-09: see What-was-tested)
            int flames = 0;
            if (FeedLeaks) foreach (Leak leak in leaks)
            {
                Hits h = leak.h;
                if (h == null || h.part == null || h.gone) continue;
                if (leak.lit && ++flames > 2) continue;
                Part p = h.part;
                Vessel v = p.vessel;
                if (v == null) continue;
                Vector3 at = p.transform.TransformPoint(leak.local), way = p.transform.TransformDirection(leak.dir).normalized;
                Vector3 up = (Vector3)FlightGlobals.getUpAxis(v.mainBody, at);
                Rigidbody rb = p.rb != null ? p.rb : v.rootPart.rb;
                Vector3d going = (rb != null ? (Vector3d)rb.velocity : v.srf_velocity - frame) + frame;
                bool air = v.staticPressurekPa > 1.0;
                float size = Mathf.Clamp(p.prefabSize.magnitude * 0.08f, 0.1f, 0.5f);
                float strength = Mathf.Clamp01(leak.flow * 2f + 0.2f);
                Vector3 along = (way + up * 0.35f).normalized;
                Feed.Burn((int)p.flightID * 7 + 3, at + way * 0.2f, along, going, size, leak.lit ? 2.5f + 6f * size : 0.8f, strength,
                          air ? (leak.lit ? 0.35f : 0.5f) * strength : 0f, leak.lit ? leak.shade : 0.82f, leak.lit ? leak.warm : 0f, leak.lit ? 11f : 6f, leak.tint,
                          leak.lit ? 0.75f * strength : 0f, leak.lit ? v.transform : null);
            }
            // ---- pieces moving through the air: the biggest push the smoke aside as they go (an explosions mod too old to take them is not asked again)
            if (!noMovers && FeedMovers)
            {
                moving.Clear();
                // (the big ones only, and few: each is looked at by every bit of smoke near it)
                foreach (Piece piece in Piece.All) if (piece != null && !piece.resting && piece.rb != null && piece.reach > 0.6f && piece.rb.velocity.sqrMagnitude > 4f) moving.Add(piece);
                if (moving.Count > 8) moving.Sort(Bigger);
                try
                {
                    for (int k = 0; k < moving.Count && k < 8; k++)
                        Feed.Moving(moving[k].transform.position, (Vector3d)moving[k].rb.velocity + frame, Mathf.Max(0.2f, moving[k].reach * 0.8f));
                }
                catch (Exception) { noMovers = true; }
            }
            // ---- burning pieces: a few, and only near a blast lately, where their smoke joins the blast's own patch of air. (One flung
            // far off would have the explosions mod begin a patch of air of its own for it, and the dozen burning pieces of a big crash a
            // dozen: measured, that halved the frame rate. The blasts throw burning chunks of their own as well.)
            int shown = 0;
            if (FeedBurning) foreach (Piece piece in Piece.All)
            {
                if (piece == null || piece.burns <= 0f || piece.rb == null) continue;
                if (!NearBlast(piece.transform.position, t)) continue;
                if (shown++ >= 4) break;
                Vector3 at = piece.transform.position;
                Vector3 up = (Vector3)FlightGlobals.getUpAxis(at);
                Vector3d going = (Vector3d)piece.rb.velocity + frame;
                float fading = Mathf.Clamp01(piece.burns / 3f);
                Vector3 along = piece.resting ? up : (up - piece.rb.velocity.normalized * 0.6f).normalized;
                Feed.Burn(900000 + piece.id, at, along, going, Mathf.Clamp(piece.reach * 0.4f, 0.12f, 0.6f), 0.4f, 0.05f, 0.22f * fading, piece.white ? 0.6f : 0.05f, piece.white ? 0.05f : 0.3f, 8f, piece.tint, 0f, null);
            }
            // ---- the dust a craft raises sliding along the ground, and the fire on a wreck whose tank went up
            foreach (Wreck w in wrecks.Values)
            {
                Vessel v = w.vessel;
                if (v == null || v.packed) continue;
                // (none off paving: what a craft dragged along concrete throws up is grit and sparks, not a cloud)
                if (FeedDust && w.inContact && w.dustSpeed > 10f && !w.dustPaved)
                {
                    Vector3 back = -(w.dustGoing - w.dustNormal * Vector3.Dot(w.dustGoing, w.dustNormal));
                    if (back.sqrMagnitude > 1e-4f) back.Normalize();
                    // (laid just under the surface, so that every puff starts on the ground and takes the ground's own colour: see the explosions mod's
                    // Vents; and said to be "running" hardly at all, for the explosions mod lights the smoke of anything running as if by its flame)
                    Vector3 rim = w.dustAt - w.dustNormal * 0.3f;
                    float much = Mathf.Clamp01((w.dustSpeed - 10f) / 50f);
                    Feed.Burn((int)v.persistentId * 13 + 5, rim, (back * 0.8f - w.dustNormal * 0.6f).normalized, (Vector3d)w.dustGoing, w.dustSize, 0.5f, 0.04f,
                              0.3f * much, 0.2f, 0.85f, 12f, 0, 0f, null);
                }
                if (FeedFire && w.burning > t)
                {
                    float left = Mathf.Clamp01((w.burning - t) / 4f);
                    Vector3 up = (Vector3)FlightGlobals.getUpAxis(v.mainBody, v.CoM);
                    Vector3d going = v.srf_velocity;
                    Vector3 along = (up - ((Vector3)going) * 0.03f).normalized;
                    float size = Mathf.Clamp(w.burnBig * 0.12f, 0.4f, 1.6f);
                    Feed.Burn((int)v.persistentId * 13 + 9, v.CoM + up * (Size(v) * 0.2f), along, going, size, size * 5f, left, 0.5f * left, 0.05f, 0.3f, 14f, 0, 0.7f * left, v.transform);
                }
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
        }

        public static string Notes(int last)
        {
            int from = Mathf.Max(0, notes.Count - last);
            return string.Join("\n", notes.GetRange(from, notes.Count - from).ToArray());
        }

        public IEnumerable<Wreck> Wrecks => wrecks.Values;
    }
}
