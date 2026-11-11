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

        // (mu: how hard the ground it lands on holds it back, its friction there, -1 for its kind's own; felt: the real ground under it
        // has been looked for since it first touched (or, see FirstTouch, found where it was to come down); sure: and again where it came
        // to rest; ahead: where it will come down has been looked for, found or not, see Ahead; aheadFound: and its ground found there,
        // not yet touched, so its first touch looks again if it comes down more than AheadNear from there, see Slide)
        struct Bit
        {
            public Vector3 at, v, w, g, up, o, euler; public Quaternion turn; public float size, age, life, smoke, fire, drag, mu; public byte kind; public bool down, felt, sure, ahead, aheadFound; public int id;
#if DEV
            // (development build: how many times it wanted a ray and none was left that frame, at most 255; see Tests.Bits)
            public byte waited;
#endif
        }

        static readonly Bit[] all = new Bit[Most];
        static int count, ids;

        // ---- how they slide (since 2026-11-03: until then every frame on the ground took half a bit's speed, a clod's three quarters,
        // whatever the frame's length, and a bit stopped below 1.2 m/s, so that a bit went a metre or so whatever it struck at, and
        // further at a lower frame rate). Now a bit on the ground is held back by friction as anything sliding is, by so much of its
        // weight (mu times g) a second, and goes v squared over 2 mu g before it stops: some fifty metres from twenty metres a
        // second on concrete, half that on grass, a hundred and more on ice. That is why a real crash's debris lies in a long fan
        // down the track and not in a heap under each touchdown.

        /// <summary>Bits slide by friction over time (false: as they used to, each frame on the ground taking a share of their speed).</summary>
        public static bool Coulomb = true;
        /// <summary>
        /// The friction of the wreckage's shreds on paving (times a hard ground's grip, see Ground.Feel: ice about 0.12) and on soil
        /// (times its friction). Accident reconstruction's tables give metal scraping along asphalt or concrete 0.3 to 0.5 (a fallen
        /// motorcycle about 0.45), along grass or earth more, 0.5 to 0.8, as it digs in; aluminium on ice about a tenth. Clods and turf
        /// break up as they land and hardly slide at all.
        /// </summary>
        public static float GripPaved = 0.4f, GripSoil = 0.7f, GripEarth = 0.9f;
        /// <summary>A bit's friction where nothing has said what ground it lands on: a shred, a bolt (rolls a little), foil (crumples and catches).</summary>
        public static float GripMetal = 0.45f, GripBolt = 0.4f, GripFoil = 0.6f;
        /// <summary>Every bit's friction times this: above 1 they stop sooner (towards the heaps of before 2026-11-03), below 1 they slide further.</summary>
        public static float GripTimes = 1f;
        /// <summary>
        /// How much of its speed into the ground a bit keeps going back up (the coefficient of restitution): a shred of aluminium
        /// sheet on concrete about 0.3, a steel bolt or rivet about 0.45 (steel balls on concrete measure 0.5 to 0.6, a short rod
        /// striking on its end or side less), foil and clods of earth hardly at all.
        /// </summary>
        public static float BounceMetal = 0.3f, BounceBolt = 0.45f, BounceSoft = 0.05f;
        /// <summary>Slower than this (m/s) on the ground, a bit is at rest: it stays where it is.</summary>
        public static float RestSpeed = 0.3f;
        /// <summary>
        /// Slower than this into the ground (m/s), a bit lying on it is not bouncing but pressed onto it by its weight (at least half as
        /// much again as one frame's fall, so that a bit lying on the ground is never taken as landing afresh each frame): its friction
        /// then is its weight's, mu g every second, not the blow's. A bounce from 0.4 m/s at 0.3 rises under a millimetre.
        /// </summary>
        public static float Settle = 0.4f;
        /// <summary>A bit within so much (metres) of the ground is touching it: held back by it, its turning damped.</summary>
        public static float Touch = 0.02f;
        /// <summary>How fast a bit's turning dies away while it slides (a second's worth: e to the minus this): a shred dragged flat does not spin.</summary>
        public static float SpinDamp = 4f;
        /// <summary>How fast a shred or foil sliding on the ground is laid flat on it (as SpinDamp): it comes to rest lying, not standing on its edge.</summary>
        public static float LieRate = 6f;
        /// <summary>Bits are slowed by the air (by its density against Kerbin's at sea level, 1.225 kg/m3: on Duna they fall almost freely, on Eve foil drifts).</summary>
        public static bool Drag = true;
        /// <summary>
        /// The ground a bit lands on is the one under where it was thrown from, taken as flat (one ray for all the bits of a throw). So that
        /// one that slides far is not left floating over a hollow or sunk in a slope, the real ground under it is looked for as it first
        /// touches, again every so many metres it slides, and where it comes to rest: at most so many rays a frame, the rest waiting
        /// their turn.
        /// </summary>
        public static int RaysPerFrame = 8;
        public static float Refind = 10f;
        /// <summary>
        /// The farther a bit has gone from where its ground was last found, the farther above and below it that ground is looked for:
        /// 3 m above and 20 m below where it was found, and this many metres more each way for every metre it has gone since (0.7, a rise
        /// or fall of some 35 degrees), and below by the world's curving away too (a bit thrown 3 km across the Mun comes down 22 m below
        /// the flat its ground was taken as, d squared over twice the Mun's 200 km). A tank going up throws its shreds at 40 to 160 m/s,
        /// and with no air to slow them they come down kilometres off: looked for only from 3 m above to 20 m below, those that came down
        /// over a hollow were left floating, and those that came down inside a hill were left buried in it.
        /// </summary>
        public static float RaySlope = 0.7f;
        /// <summary>
        /// And a bit flying more than so many metres (FlightFrom) from where its ground was found is looked along as it goes, that it hits
        /// a hill it flies into instead of going through it to the flat beyond: so many such looks a frame at most, beside RaysPerFrame.
        /// </summary>
        public static int FlightRays = 6;
        public static float FlightFrom = 20f;
        /// <summary>
        /// (since 2026-11-04; off: as before) A bit's first touch of the ground never waits long, nor finds it already inside a rise. In
        /// the readings of 2026-11-04 a nose-first crash at the pad lifted 88 bits on to the ground as they first touched, one by 10.7 m:
        /// bits that came down under ground higher than the flat taken under where they were thrown, and went into it before their first
        /// ray, to pop out of it as they touched the flat inside it. So, with this:
        ///
        /// * a falling bit that will come down within FirstAhead seconds has the ground looked for where it will come down (on the flat
        ///   it was given, its fall and its way along worked out as if there were no air), before it gets there: a rise there lower
        ///   than the bit is its ground before it goes in, and a hollow its ground before it is left floating over it (see Ahead); its
        ///   first touch then casts no ray if it comes down within AheadNear of where that ground was found, and does if further (the
        ///   air holds light foil back short of where it was worked out to come down, and the plane found is true only near there);
        /// * rays go by time, not by frames: RaysPerFrame for each RaysFor-th of a second the frame took (never fewer, and at most four
        ///   times as many), so that a slow frame, as when a crash is filmed at 13 frames a second, does not leave the bits waiting;
        /// * FirstRays more a frame are kept for first touches and the looks ahead only, which the others may not take;
        /// * on sloping ground (more than some 14 degrees) a sliding bit's ground is looked for again every RefindSloped metres, not
        ///   Refind: the plane of a slope goes stale over a slide where the slope changes, and buries the bit in it (17 degrees' change
        ///   over 10 m buries it 3 m).
        /// </summary>
        public static bool FirstTouch = true;
        public static int FirstRays = 4;
        public static float RaysFor = 30f;
        public static float RefindSloped = 3f;
        public static float FirstAhead = 1f;
        public static float AheadNear = 1.5f;
        static int flightRays;
        static int rays, rayBudget = 8;
        // (whose turn it is for a ray: the bit being moved, the first that went without one this frame, and where next frame's begin)
        static int turn, denied = -1, rayTurn;
#if DEV
        // (development build: what the rays found, and what went to make room, since last cleared; see Tests.Bits)
        internal static int DevRays, DevShort, DevAfloat, DevLifted, DevRested, DevDropped, DevDroppedFlying, DevMissed;
        internal static float DevLiftMost, DevLastGrip = -1f;
        // (and the farthest a ray reached, above and below, metres; how many bits a look along their flight caught in a hill; and the farthest
        // a bit had gone from where its ground was found when it was looked for)
        internal static float DevReachMost, DevFarMost;
        internal static int DevCaught, DevFlightLooks;
        internal static int LastId => ids;
        // (since 2026-11-04, see FirstTouch: how many times where a bit would come down was looked for ahead, how often the ground was
        // found there, how often it was found above the flat the bit had (a landing that would have been sunk) and by how much at most,
        // and how often it was found above the bit itself, and not taken (it will run into it side-on, and pops out of it as before); and
        // the most rays any frame had, and how many first touches and looks ahead took one of the rays kept for them)
        internal static int DevAhead, DevAheadFound, DevAheadRose, DevAheadUnder, DevRaysMost, DevFirstKept;
        internal static float DevAheadRiseMost, DevAheadUnderMost;

        // (development build, since 2026-11-04: to tell apart why bits are found sunk where they come to rest, see
        // research/fragmentation/mass-and-sunk-bits.md. A plane gone stale over a slide on sloping ground, or a bit gone through a wall
        // or into a structure: the first shows as lifts while sliding, and as sunk bits found by the first ray on the ground itself after
        // a long slide since their last ray; the second as sunk bits found on a building's or a kerb's collider after a short one.)
        // Lifts while sliding: the real ground found above the plane a bit slid on, by more than 5 cm, as it first touched (the plane of
        // its throw wrong where it landed) and as it was looked for again every Refind metres (the plane gone stale), and the most of each.
        internal static int DevSlideLiftedFirst, DevSlideLiftedAgain;
        internal static float DevSlideLiftFirstMost, DevSlideLiftAgainMost;
        // Of those found sunk where they came to rest, those by more than half a metre: how many, how many found by the first ray (from 3 m
        // above) and how many by the second (from higher), how many had ever waited for a ray, how many had had one before this, the
        // sum and most of how far each had gone from where its ground was last found and of how far it had slid since its last ray, how
        // many of each kind, and on what (each collider's name and layer, so many times: the first sixteen named, the rest as "others").
        internal static int DevDeep, DevDeepFirst, DevDeepSecond, DevDeepWaited, DevDeepFelt, DevDeepNoted;
        internal static float DevDeepFarSum, DevDeepFarMost, DevDeepSlidSum, DevDeepSlidMost;
        internal static readonly int[] DevDeepKinds = new int[Kinds];
        internal static readonly System.Collections.Generic.Dictionary<string, int> DevDeepOn = new System.Collections.Generic.Dictionary<string, int>();
        internal static readonly System.Collections.Generic.List<string> DevDeepFirstFew = new System.Collections.Generic.List<string>();
        // (what the last ray found, handed back by Ray without any cost to it: the collider, whether it took the second ray, and how far
        // the bit had gone from where its ground was last found)
        static Collider devRayOn;
        static bool devRaySecond;
        static float devRayFar;

        /// <summary>(development build) The counters of DevSlideLifted and DevDeep cleared, with the others, by Tests.Bits "reset".</summary>
        internal static void DevClearLifts()
        {
            DevSlideLiftedFirst = DevSlideLiftedAgain = 0;
            DevSlideLiftFirstMost = DevSlideLiftAgainMost = 0f;
            DevDeep = DevDeepFirst = DevDeepSecond = DevDeepWaited = DevDeepFelt = DevDeepNoted = 0;
            DevDeepFarSum = DevDeepFarMost = DevDeepSlidSum = DevDeepSlidMost = 0f;
            for (int k = 0; k < DevDeepKinds.Length; k++) DevDeepKinds[k] = 0;
            DevDeepOn.Clear();
            DevDeepFirstFew.Clear();
            DevAhead = DevAheadFound = DevAheadRose = DevAheadUnder = DevRaysMost = DevFirstKept = 0;
            DevAheadRiseMost = DevAheadUnderMost = 0f;
        }

        /// <summary>(development build) A bit found sunk more than half a metre where it came to rest, by 'deep' metres: what the ray found, and how it got there.</summary>
        static void DevDeepOne(ref Bit b, float deep, float slid, bool feltBefore)
        {
            DevDeep++;
            if (devRaySecond) DevDeepSecond++; else DevDeepFirst++;
            if (b.waited > 0) DevDeepWaited++;
            if (feltBefore) DevDeepFelt++;
            DevDeepFarSum += devRayFar; DevDeepFarMost = Mathf.Max(DevDeepFarMost, devRayFar);
            DevDeepSlidSum += slid; DevDeepSlidMost = Mathf.Max(DevDeepSlidMost, slid);
            if (b.kind < DevDeepKinds.Length) DevDeepKinds[b.kind]++;
            string on = devRayOn == null ? "(nothing)" : (devRayOn.transform.parent != null ? devRayOn.transform.parent.name + "/" : "") + devRayOn.name + " layer " + devRayOn.gameObject.layer;
            string key = DevDeepOn.ContainsKey(on) || DevDeepOn.Count < 16 ? on : "others";
            DevDeepOn.TryGetValue(key, out int seen);
            DevDeepOn[key] = seen + 1;
            string said = ((Kind)b.kind).ToString() + " sunk " + deep.ToString("F2") + " m, found by the " + (devRaySecond ? "second" : "first") + " ray on " + on + ", " + devRayFar.ToString("F1")
                + " m from where its ground was last found, slid " + slid.ToString("F1") + " m since its last ray, " + (b.waited > 0 ? "waited for a ray " + b.waited + " times" : "never waited for a ray")
                + (feltBefore ? ", had had a ray before" : ", its first ray") + ", " + b.age.ToString("F1") + " s old";
            if (DevDeepFirstFew.Count < 8) DevDeepFirstFew.Add(said);
            // (the first few over a metre in the notes as well, with when it was, beside the crash's other notes)
            if (deep > 1f && DevDeepNoted < 5)
            {
                DevDeepNoted++;
                Crashes.Note("a bit " + said);
            }
        }

        /// <summary>The bits numbered from first to last that are still about: where each is, how fast it goes, whether it is at rest. Returns how many.</summary>
        internal static int Find(int first, int last, Vector3[] at, Vector3[] v, bool[] down)
        {
            int n = 0;
            for (int k = 0; k < count && n < at.Length; k++)
            {
                if (all[k].id < first || all[k].id > last) continue;
                at[n] = all[k].at; v[n] = all[k].v; down[n] = all[k].down;
                n++;
            }
            return n;
        }
#endif

        /// <summary>
        /// The friction of the wreckage's bits on a ground (see GripPaved): on paving, or on a hard ground (ice, bare rock) by its grip,
        /// on soil, sand, snow and dust by its friction; -1 where the ground is not known (each kind's own, see GripMetal).
        /// </summary>
        public static float Grip(Ground.Feel f, bool paved)
        {
            if (paved) return GripPaved;
            if (f == null) return -1f;
            return f.Hard ? GripPaved * f.Grip : GripSoil * f.Friction;
        }

        static float GripOf(byte kind)
        {
            switch ((Kind)kind)
            {
                case Kind.Bolt: return GripBolt;
                case Kind.Foil: return GripFoil;
                case Kind.Clod: case Kind.Turf: return GripEarth;
                default: return GripMetal;
            }
        }

        static float BounceOf(byte kind)
        {
            switch ((Kind)kind)
            {
                case Kind.Bolt: return BounceBolt;
                case Kind.Foil: case Kind.Clod: case Kind.Turf: return BounceSoft;
                default: return BounceMetal;
            }
        }

        /// <summary>Whether a kind is a thin sheet (a shred of skin, of paint, burnt, or foil), that comes to rest lying flat; bolts and clods keep the way they fell.</summary>
        static bool Flat(byte kind) => kind <= (byte)Kind.Foil;
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
        /// smallest and the biggest (few big, many small), of a kind; so many of them burning (0 to 1), and how much smoke a bit trails;
        /// and the friction of the ground they will land on (see Grip; -1, their kind's own). Where the ground under the place they come
        /// from is paving, the wreckage's bits slide as on paving whatever is said (a part struck over the runway may not be touching it).
        /// </summary>
        public static void Emit(Vector3 at, Vector3 v, float spread, int n, float smallest, float biggest, Kind kind, float burning, float smoking, float upward = 0f, float grip = -1f)
        {
            if (n <= 0 || TestNone) return;
            // (the wreckage's bits fewer and larger than asked, two and a half times fewer, the smallest twice as big: read as
            // pieces of a craft, not a glitter of specks in the fire's light, the owner 2026-10-28, "a whole bunch of small little
            // dots". The earth's are left as they are; and the tiny ones thrown once to have every kind drawn before a crash.)
            bool earthen = kind == Kind.Clod || kind == Kind.Turf;
            // (as many more or fewer as the page's fine details ask)
            if (biggest >= 0.005f)
            {
                n = Frag.Times(n, Frag.Times(earthen ? Frag.EarthCount : Frag.BitCount));
                if (n <= 0) return;
            }
            if (!earthen && biggest >= 0.005f)
            {
                float fewer = n / 2.5f;
                n = (int)fewer + (Random.value < fewer - (int)fewer ? 1 : 0);
                if (n <= 0) return;
                smallest *= 2f;
                biggest *= 1.5f;
            }
            Ready();
            CelestialBody body = FlightGlobals.currentMainBody;
            Vector3 up = body != null ? (Vector3)FlightGlobals.getUpAxis(body, at) : Vector3.up;
            // (the ground they will land on: under the place they come from, taken as flat round it. If the place is more than 3 m in the
            // ground, as a wreck pressed into soft ground may have its panels, the ray began inside it and found nothing, and its bits fell
            // 200 m through the world to a ground of nothing: looked for again from 50 m up)
            Vector3 g = at - up * 200f;
            bool found = Physics.Raycast(at + up * 3f, -up, out RaycastHit hit, 400f, 1 << 15, QueryTriggerInteraction.Ignore)
                || Physics.Raycast(at + up * 50f, -up, out hit, 450f, 1 << 15, QueryTriggerInteraction.Ignore);
            if (found)
            {
                // (paving under it, and not the space centre's lawns, which are tagged as its tarmac is: see Hits)
                if (!earthen && Crashes.Paved(hit.collider) && !(Crashes.TellLawn && Ground.Lawn(hit.point, up))) grip = GripPaved;
                // (and not the plane of a wall, a kerb's edge or a cliff, steeper than sixty degrees, as Ray: a plane that steep runs
                // metres into the ground a few metres off, and every bit that slid on it before its own ground was found was sunk in it)
                g = hit.point;
                if (Vector3.Dot(hit.normal, up) >= 0.5f) up = hit.normal;
            }
#if DEV
            DevLastGrip = grip;
#endif
            float alpha = 1.6f, low = Mathf.Pow(Mathf.Clamp01(smallest / Mathf.Max(biggest, 1e-3f)), alpha);
            if (count + n > Most) Drop(count + n - Most);
            for (int k = 0; k < n; k++)
            {
                if (count >= Most) Drop(1);
                ref Bit b = ref all[count++];
                b.id = ++ids;
                b.at = at + Random.insideUnitSphere * Mathf.Min(0.6f, biggest * 2f);
                b.v = v + Random.insideUnitSphere * spread + up * (upward * Random.Range(0.3f, 1f));
                b.w = Random.onUnitSphere * Random.Range(4f, 25f);
                b.turn = Random.rotation;
                b.euler = b.turn.eulerAngles;
                b.g = g; b.up = up; b.o = g;
                b.size = smallest * Mathf.Pow(1f - Random.value * (1f - low), -1f / alpha);
                b.size = Mathf.Min(b.size, biggest);
                b.kind = (byte)kind;
                // (thin light things are slowed by the air far more than heavy ones: foil flutters down)
                b.drag = kind == Kind.Foil ? 2.5f / Mathf.Max(0.02f, b.size) * 0.02f + 0.6f : earthen ? 0.02f : 0.12f / Mathf.Max(0.02f, b.size) * 0.05f;
                b.age = 0f;
                // (clods and sods lie by the scar they came out of for a minute or more: lying still they cost next to nothing)
                b.life = earthen ? Random.Range(60f, 100f) : Random.Range(45f, 90f);
                b.fire = Random.value < burning ? Random.Range(2f, 7f) : 0f;
                b.smoke = smoking * Random.Range(0.6f, 1.2f);
                b.mu = grip;
                b.down = b.felt = b.sure = b.ahead = b.aheadFound = false;
#if DEV
                b.waited = 0;
#endif
            }
        }

        // (Drop's scratch: the indices of the bits it is to take, and their ages, oldest first)
        const int DropMost = 32;
        static readonly int[] dropAt = new int[DropMost];
        static readonly float[] dropAge = new float[DropMost];

        /// <summary>
        /// So many go, to make room: the oldest of those that have come to rest, and only if too few are at rest the oldest of those still
        /// flying (until 2026-11-03 the first at rest in the list went, which after bits had gone from the middle of it was not the oldest,
        /// and the first of all when none was at rest, even one just thrown). One look through them all for as many as a throw needs.
        /// </summary>
        static void Drop(int m)
        {
            while (m > 0 && count > 0)
            {
                int take = Mathf.Min(m, DropMost);
                int found = Oldest(take, true, 0);
                if (found < take) found = Oldest(take, false, found);
                // (taken from the end of the list first, so that the bit moved into each one's place is never one still to be taken)
                for (int i = 1; i < found; i++) { int at = dropAt[i], j = i; while (j > 0 && dropAt[j - 1] > at) { dropAt[j] = dropAt[j - 1]; j--; } dropAt[j] = at; }
                for (int j = found - 1; j >= 0; j--)
                {
                    int k = dropAt[j];
#if DEV
                    DevDropped++;
                    if (!all[k].down) DevDroppedFlying++;
#endif
                    gone[all[k].kind] = true;
                    all[k] = all[--count];
                }
                m -= found;
                if (found == 0) break;
            }
        }

        /// <summary>Drop's look through them: the oldest of those at rest (or of those not), kept oldest first in its scratch after the 'from' it holds already.</summary>
        static int Oldest(int take, bool resting, int from)
        {
            int found = from;
            for (int k = 0; k < count; k++)
            {
                if (all[k].down != resting) continue;
                float age = all[k].age;
                if (found == take && age <= dropAge[take - 1]) continue;
                int j = found == take ? take - 1 : found++;
                while (j > from && dropAge[j - 1] < age) { dropAge[j] = dropAge[j - 1]; dropAt[j] = dropAt[j - 1]; j--; }
                dropAge[j] = age; dropAt[j] = k;
            }
            return found;
        }

        /// <summary>Every frame: move them, and hand them to be drawn.</summary>
        public static void Step(float dt)
        {
            if (systems == null) return;
            if (count == 0) { for (int s = 0; s < Kinds; s++) if (shown[s] > 0) { systems[s].SetParticles(buffers[s], 0); shown[s] = 0; } return; }
            CelestialBody body = FlightGlobals.currentMainBody;
            Vector3 gravity = body != null ? (Vector3)FlightGlobals.getGeeForceAtPosition(all[0].at, body) : Vector3.down * 9.81f;
            Vector3 sky = gravity.sqrMagnitude > 1e-6f ? -gravity.normalized : Vector3.up;
            // (the air's density where they are, read once a frame at the first of them, as the pieces' is, see Piece.Step: until 2026-11-03
            // the air was on or off at a hundredth of an atmosphere, so that Duna's thin air held them back as Eve's thick air did)
            float thick = 0f;
            if (Drag && body != null && body.atmosphere)
            {
                double pressure = FlightGlobals.getStaticPressure(all[0].at, body);
                if (pressure > 0) thick = (float)FlightGlobals.getAtmDensity(pressure, FlightGlobals.getExternalTemperature(all[0].at, body), body) / 1.225f;
            }
            rays = 0;
            flightRays = 0;
            // (by the time the frame took, see FirstTouch: as many as ever at 30 frames a second or more, more at fewer)
            rayBudget = FirstTouch ? Mathf.Clamp(Mathf.RoundToInt(RaysPerFrame * dt * Mathf.Max(1f, RaysFor)), RaysPerFrame, RaysPerFrame * 4) : RaysPerFrame;
            for (int s = 0; s < Kinds; s++) { changed[s] = shifted || gone[s]; gone[s] = false; }
            shifted = false;
            // ---- first, those that came to rest when no ray was left that frame: the ground under them looked for now, before any
            // other has a ray, as where a bit lies is what is seen of it for a minute and more; one sliding goes on a frame or two on
            // the ground it was given
            if (Coulomb)
            {
                int k = 0;
                for (; k < count && rays < rayBudget; k++)
                {
                    ref Bit b = ref all[k];
                    if (b.down && !b.sure && b.age + dt <= b.life) { Seek(ref b, sky); changed[b.kind] = true; }
                }
#if DEV
                // (those this did not reach for want of a ray wait another frame, on the plane they slid on: each such frame counted, so
                // that a bit's waits are all of them, see DevDeepOne. Until 2026-11-04 only the frame it came to rest in was, see Rest)
                for (; k < count; k++)
                {
                    ref Bit b = ref all[k];
                    if (b.down && !b.sure && b.age + dt <= b.life && b.waited < 255) b.waited++;
                }
#endif
            }
            // ---- moved: those not yet lying still. The rays left are handed out in turn, from the first that went without one last
            // frame (until 2026-11-03 always from the newest: while a crash was still throwing, the bits it had thrown first waited
            // seconds for a ray, sliding all the while on the flat taken under where they were thrown, into rises and over hollows).
            // Those whose time is up are taken out after, so that none is moved out of its turn
            int n = count;
            int from = rayTurn < n ? rayTurn : n - 1;
            denied = -1;
            bool expired = false;
            for (int i = 0; i < n; i++)
            {
                int k = from - i;
                if (k < 0) k += n;
                ref Bit b = ref all[k];
                turn = k;
                b.age += dt;
                if (b.age > b.life) { changed[b.kind] = true; expired = true; continue; }
                if (b.fire > 0f) b.fire -= dt;
                if (b.down)
                {
                    if (b.life - b.age < 1.5f) changed[b.kind] = true;
                    continue;
                }
                changed[b.kind] = true;
                {
                    b.v += gravity * dt;
                    if (thick > 0f) b.v *= 1f / (1f + b.drag * thick * b.v.magnitude * dt * 0.1f);
                    Vector3 was = b.at;
                    b.at += b.v * dt;
                    if (b.w.sqrMagnitude > 1e-4f) b.turn = Quaternion.AngleAxis(b.w.magnitude * dt * Mathf.Rad2Deg, b.w.normalized) * b.turn;
                    float h = Vector3.Dot(b.at - b.g, b.up) - b.size * 0.3f;
                    // (thrown far from where its ground was found, it is looked along as it goes: a hill it flies into is its ground, and not
                    // the flat beyond it, inside which it would be lost. Not a wall or a cliff, steeper than sixty degrees, as Ray)
                    if (Coulomb && h >= Touch && flightRays < FlightRays && Vector3.ProjectOnPlane(b.at - b.o, b.up).sqrMagnitude > FlightFrom * FlightFrom)
                    {
                        flightRays++;
#if DEV
                        DevFlightLooks++;
#endif
                        if (Physics.Linecast(was, b.at, out RaycastHit hill, 1 << 15, QueryTriggerInteraction.Ignore) && Vector3.Dot(hill.normal, sky) >= 0.5f)
                        {
                            b.g = hill.point; b.up = hill.normal; b.o = hill.point;
                            b.felt = true;
                            h = Vector3.Dot(b.at - b.g, b.up) - b.size * 0.3f;
#if DEV
                            DevCaught++;
#endif
                        }
                    }
                    // (falling, and coming down soon: the ground looked for where it will, before it gets there, see FirstTouch)
                    if (Coulomb && FirstTouch && !b.felt && !b.ahead && h >= Touch && Vector3.Dot(b.v, b.up) < 0f && rays < rayBudget + FirstRays) h = Ahead(ref b, h, gravity, sky);
                    if (Coulomb) { if (h < Touch) Slide(ref b, h, gravity, sky, dt); }
                    else if (h < 0f)
                    {
                        b.at -= b.up * h;
                        float into = Vector3.Dot(b.v, b.up);
                        if (into < 0f)
                        {
                            // (as before 2026-11-03: it bounces a little, skids, and stops; a clod breaks up as it lands and stays)
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
            if (expired) for (int k = count - 1; k >= 0; k--) if (all[k].age > all[k].life) all[k] = all[--count];
#if DEV
            DevRaysMost = Mathf.Max(DevRaysMost, rays);
#endif
            // (next frame's rays from the first that went without one: no bit waits more than a few frames for its turn)
            rayTurn = denied >= 0 ? denied : from;
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

        /// <summary>
        /// A bit on the ground, or within Touch of it (h, how far above it, less its half height): it lands, bouncing back by its kind's
        /// restitution and losing to friction mu times the blow (the ground's push on it times mu, the Coulomb friction of an impact);
        /// or it lies on it and slides, losing mu g every second; and it comes to rest below RestSpeed. The same however long the frame:
        /// each frame's fall onto the ground is taken as its weight pressing on it, not as a landing (see Settle).
        /// </summary>
        static void Slide(ref Bit b, float h, Vector3 gravity, Vector3 sky, float dt)
        {
            // (the real ground under it, as it first touches and every Refind metres it slides: see RaysPerFrame. On sloping ground every
            // RefindSloped metres, and a first touch may take one of the rays kept for first touches: see FirstTouch)
            float again = FirstTouch && Vector3.Dot(b.up, sky) < 0.97f ? Mathf.Min(Refind, RefindSloped) : Refind;
            // (its ground found ahead, and this its first touch: that ground is its own only near where it was found, see Ahead. A
            // first touch still, and so it may take a ray kept for first touches)
            bool fresh = !b.felt || b.aheadFound;
            if (h < 0f && b.aheadFound) again = Mathf.Min(again, AheadNear);
            if (h < 0f && (!b.felt || Vector3.ProjectOnPlane(b.at - b.g, b.up).sqrMagnitude > again * again))
            {
                if (rays < rayBudget || FirstTouch && fresh && rays < rayBudget + FirstRays)
                {
#if DEV
                    if (rays >= rayBudget) DevFirstKept++;
                    // (where it would lie on the plane it slid on, and whether this is its first touch, before the ray changes them)
                    bool first = fresh;
                    Vector3 layWas = b.at - b.up * Vector3.Dot(b.at - b.g, b.up);
#endif
                    b.felt = true;
                    b.aheadFound = false;
                    if (Ray(ref b, sky))
                    {
#if DEV
                        // (the real ground above that plane where it is: it was in the ground, and is lifted on to it, as Seek counts sunk)
                        float lift = -Vector3.Dot(layWas - b.g, b.up);
                        if (lift > 0.05f)
                        {
                            if (first) { DevSlideLiftedFirst++; DevSlideLiftFirstMost = Mathf.Max(DevSlideLiftFirstMost, lift); }
                            else { DevSlideLiftedAgain++; DevSlideLiftAgainMost = Mathf.Max(DevSlideLiftAgainMost, lift); }
                        }
#endif
                        h = Vector3.Dot(b.at - b.g, b.up) - b.size * 0.3f;
                        if (h >= Touch) return;              // (the ground is lower here: it falls on to it)
                    }
                }
                else
                {
                    if (denied < 0) denied = turn;
#if DEV
                    DevShort++;
                    if (b.waited < 255) b.waited++;
#endif
                }
            }
            // (come down near where its ground was found ahead: that ground was right, and no ray was needed)
            else if (h < 0f) b.aheadFound = false;
            if (h < 0f) b.at -= b.up * h;
            float vn = Vector3.Dot(b.v, b.up);
            Vector3 vt = b.v - b.up * vn;
            // (its weight's share pressing it on to the ground: all of it on the level, less on a slope, where the rest pulls it down the slope)
            float gn = Mathf.Max(0f, -Vector3.Dot(gravity, b.up));
            float mu = (b.mu >= 0f ? b.mu : GripOf(b.kind)) * GripTimes;
            float lose, settle = Mathf.Max(Settle, 1.5f * gn * dt);
            if (vn < -settle)
            {
                if (h >= 0f) return;                         // (still falling the last centimetre or so on to it)
                // a landing: it goes back up at so much of the speed it came down at, and is held back along the ground by mu times
                // the push that turned it (1 + e times its speed into the ground); a hard landing takes half its turning
                float e = BounceOf(b.kind);
                lose = mu * (1f + e) * -vn;
                if (vn < -1f) b.w *= 0.5f;
                vn = -e * vn;
            }
            else if (vn < settle)
            {
                // lying on it: pressed on to it by its weight and dragged along it, its turning dying away, a shred laid flat
                if (h < 0f && vn < 0f) vn = 0f;
                lose = mu * gn * dt;
                b.w *= Mathf.Exp(-SpinDamp * dt);
                if (Flat(b.kind)) b.turn = Quaternion.Slerp(b.turn, Lying(b.turn, b.up, out bool _), 1f - Mathf.Exp(-LieRate * dt));
            }
            else return;                                     // (going up off it, from a bounce)
            float st = vt.magnitude;
            if (st > 1e-6f) vt *= Mathf.Max(0f, st - lose) / st;
            b.v = vt + b.up * vn;
            if (b.v.sqrMagnitude < RestSpeed * RestSpeed) Rest(ref b, sky);
        }

        /// <summary>
        /// It comes to rest: still, the real ground under it looked for (if a ray is left this frame; if not, it is looked for first
        /// next frame, see Step), and it laid on it. One already found floating and let fall on to the real ground under it has that ground
        /// already, and is laid on it without another look.
        /// </summary>
        static void Rest(ref Bit b, Vector3 sky)
        {
            b.down = true;
            b.v = Vector3.zero;
            b.w = Vector3.zero;
#if DEV
            DevRested++;
#endif
            if (b.sure) Place(ref b);
            else if (rays < rayBudget) Seek(ref b, sky);
            else
            {
                if (denied < 0) denied = turn;
#if DEV
                // (it comes to rest with no ray left: it lies on the plane it slid on until the next frame's look, see Step)
                if (b.waited < 255) b.waited++;
#endif
                if (Flat(b.kind)) Lay(ref b);
            }
        }

        /// <summary>
        /// Where a bit has come to rest, the real ground under it. How far the ground it was lying on (the plane it was given, under it)
        /// was above or below that is how far it was floating or sunk: one left over a hollow it slid across falls again on to it; one
        /// sunk into a rise is lifted out on to it; and either way it is laid on the real ground, a shred flat. (Until 2026-11-03 this was
        /// reckoned from the bit's middle less 0.3 of its size, which a shred laid flat, a fiftieth of its size above the ground, was
        /// short of by a quarter of its size: every big one laid before its look was counted sunk.)
        /// </summary>
        static void Seek(ref Bit b, Vector3 sky)
        {
            b.sure = true;
            Vector3 lay = b.at - b.up * Vector3.Dot(b.at - b.g, b.up);
#if DEV
            // (how far it slid since its last ray, or since its throw if it had none, and whether it had one: before this one changes them)
            float slid = Vector3.ProjectOnPlane(b.at - b.g, b.up).magnitude;
            bool feltBefore = b.felt;
#endif
            if (Ray(ref b, sky))
            {
                b.felt = true;
                float off = Vector3.Dot(lay - b.g, b.up);
                if (off > 0.05f)
                {
                    b.down = false;
#if DEV
                    DevAfloat++;
#endif
                    return;
                }
#if DEV
                if (off < -0.05f) { DevLifted++; DevLiftMost = Mathf.Max(DevLiftMost, -off); }
                if (off < -0.5f) DevDeepOne(ref b, -off, slid, feltBefore);
#endif
            }
#if DEV
            else DevMissed++;
#endif
            Place(ref b);
        }

        /// <summary>A bit at rest put on its ground: a shred or foil laid flat on it, a bolt or a clod with its middle 0.3 of its size above it, as it slides (see Step).</summary>
        static void Place(ref Bit b)
        {
            if (Flat(b.kind)) Lay(ref b);
            else b.at += b.up * (b.size * 0.3f - Vector3.Dot(b.at - b.g, b.up));
            b.euler = b.turn.eulerAngles;
        }

        /// <summary>The real ground under a bit (straight down, from 3 m above it to 20 m below, farther each way the farther it has gone: see RaySlope): its plane taken as the bit's. Not a wall or a cliff, steeper than sixty degrees.</summary>
        static bool Ray(ref Bit b, Vector3 sky)
        {
            rays++;
#if DEV
            DevRays++;
#endif
            // (looked for the farther above and below it the farther it has gone from where its ground was last found, and below by the
            // world's curving away beneath the flat that was taken for it: see RaySlope)
            float far = Vector3.ProjectOnPlane(b.at - b.o, b.up).magnitude;
            CelestialBody body = FlightGlobals.currentMainBody;
            float bend = body != null && body.Radius > 1.0 ? far * far / (2f * (float)body.Radius) : 0f;
            float above = 3f + RaySlope * far, below = 20f + RaySlope * far + bend;
#if DEV
            DevReachMost = Mathf.Max(DevReachMost, above + below);
            DevFarMost = Mathf.Max(DevFarMost, far);
#endif
            // (first from 3 m above it, as ever, so that one lying beside a building is not set on its roof; only if that finds nothing, as
            // when it is sunk deep in a hill, from higher. None found: its plane kept, but looked for again only when it has slid another
            // Refind metres from here; where it was last found is kept, so that the next look reaches the farther)
            RaycastHit hit;
            bool found = Physics.Raycast(b.at + sky * 3f, -sky, out hit, 3f + below, 1 << 15, QueryTriggerInteraction.Ignore) && Vector3.Dot(hit.normal, sky) >= 0.5f;
#if DEV
            devRaySecond = false;
            devRayFar = far;
#endif
            if (!found && above > 3.5f)
            {
                rays++;
#if DEV
                DevRays++;
                devRaySecond = true;
#endif
                found = Physics.Raycast(b.at + sky * above, -sky, out hit, above + below, 1 << 15, QueryTriggerInteraction.Ignore) && Vector3.Dot(hit.normal, sky) >= 0.5f;
            }
#if DEV
            devRayOn = found ? hit.collider : null;
#endif
            if (!found)
            {
                b.g = b.at - b.up * Vector3.Dot(b.at - b.g, b.up);
                return false;
            }
            b.g = hit.point;
            b.up = hit.normal;
            b.o = hit.point;
            return true;
        }

        /// <summary>
        /// (see FirstTouch) A bit falling, not yet on the ground: where it will come down on the flat it was given, if that is within
        /// FirstAhead seconds (from its height over the flat, its speed and gravity, as if there were no air); and the real ground looked
        /// for there as Ray looks for it under a bit, first from the bit's own height over that point (at least 3 m above the flat: from
        /// lower, a rise more than 3 m high there would have the ray begin inside it, where it is not seen, and the ground found be
        /// whatever is under it), and only if that finds nothing from higher. Found below the bit, that ground is its own, as its first
        /// touch's ray would have made it, and it comes down on it, not into it; it then needs no ray as it touches, unless it comes down
        /// more than AheadNear from there (see Slide). Found above the bit, it is not taken: the bit will run into it
        /// side-on (into a wall or the face of a rise, which bits go through as ever, see the research page), and lifting it up there in
        /// the air would only pop it up sooner. Near where the ground of its throw was found (2 m), that is its ground already, and no
        /// ray is cast. Looked for once, found or not; not found, its first touch looks as before. Returns its height over its ground.
        /// </summary>
        static float Ahead(ref Bit b, float h, Vector3 gravity, Vector3 sky)
        {
            float vn = Vector3.Dot(b.v, b.up), gn = Vector3.Dot(gravity, b.up);
            // (nothing pulling it down on to that flat: no fall to work out; its first touch looks, as before)
            if (gn > -0.05f) { b.ahead = true; return h; }
            // (h + vn t + gn t^2 / 2 = 0: the root ahead, gn being below nought)
            float t = (vn + Mathf.Sqrt(Mathf.Max(0f, vn * vn - 2f * gn * h))) / -gn;
            // (not soon enough: looked at again next frame, nearer, as its way through the air bends from what this would work out)
            if (t > FirstAhead) return h;
            b.ahead = true;
            Vector3 land = b.at + b.v * t + gravity * (0.5f * t * t);
            land -= b.up * Vector3.Dot(land - b.g, b.up);
            float far = Vector3.ProjectOnPlane(land - b.o, b.up).magnitude;
            if (far < 2f) return h;
            CelestialBody body = FlightGlobals.currentMainBody;
            float bend = body != null && body.Radius > 1.0 ? far * far / (2f * (float)body.Radius) : 0f;
            float above = 3f + RaySlope * far, below = 20f + RaySlope * far + bend;
            // (from as high as the bit is over where it will come down: nothing below the bit there that it could come down on is
            // missed for the ray beginning inside it)
            float top = Mathf.Max(3f, Vector3.Dot(b.at - land, sky) + 0.5f);
#if DEV
            DevAhead++;
            if (rays >= rayBudget) DevFirstKept++;
            DevRays++;
#endif
            rays++;
            RaycastHit hit;
            bool found = Physics.Raycast(land + sky * top, -sky, out hit, top + below, 1 << 15, QueryTriggerInteraction.Ignore) && Vector3.Dot(hit.normal, sky) >= 0.5f;
            // (the second only within the rays kept for first touches, as the first was)
            if (!found && above > top + 0.5f && rays < rayBudget + FirstRays)
            {
#if DEV
                if (rays >= rayBudget) DevFirstKept++;
                DevRays++;
#endif
                rays++;
                found = Physics.Raycast(land + sky * above, -sky, out hit, above + below, 1 << 15, QueryTriggerInteraction.Ignore) && Vector3.Dot(hit.normal, sky) >= 0.5f;
            }
            if (!found) return h;
            float now = Vector3.Dot(b.at - hit.point, hit.normal) - b.size * 0.3f;
#if DEV
            DevAheadFound++;
            float rise = Vector3.Dot(hit.point - land, b.up);
            if (rise > 0.05f) { DevAheadRose++; DevAheadRiseMost = Mathf.Max(DevAheadRiseMost, rise); }
            if (now < 0f) { DevAheadUnder++; DevAheadUnderMost = Mathf.Max(DevAheadUnderMost, -now); }
#endif
            if (now < 0f) return h;
            b.g = hit.point; b.up = hit.normal; b.o = hit.point;
            b.felt = b.aheadFound = true;
            return now;
        }

        /// <summary>The way a shred turned so lies flat on a ground: on whichever of its faces is nearer to lying on it already (so half of them face down).</summary>
        static Quaternion Lying(Quaternion turn, Vector3 up, out bool over)
        {
            Vector3 face = turn * Vector3.up;
            over = Vector3.Dot(face, up) < 0f;
            return Quaternion.FromToRotation(over ? -face : face, up) * turn;
        }

        /// <summary>
        /// A shred or foil laid flat on the ground where it rests, just clear of it: a shard's mesh rises 0.04 of its width on one side (drawn
        /// at twice its size), so it is lifted by a little on its back and by that much more face down; foil is crinkled a quarter of its
        /// size either way.
        /// </summary>
        static void Lay(ref Bit b)
        {
            b.turn = Lying(b.turn, b.up, out bool over);
            float lift = (b.kind == (byte)Kind.Foil ? 0.25f : over ? 0.1f : 0.02f) * b.size;
            b.at += b.up * (lift - Vector3.Dot(b.at - b.g, b.up));
            b.euler = b.turn.eulerAngles;
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
            for (int k = 0; k < count; k++) { all[k].at -= o; all[k].g -= o; all[k].o -= o; }
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
            // (darker and duller than new metal since 2026-10-28: torn, scorched and sooted, a dull sheen at most, so that a crash's
            // fire lights them as wreckage rather than setting them glittering; the burnt ones near black and matte)
            Make(Kind.Metal, shard, new Color(0.4f, 0.4f, 0.39f), 0.1f);
            Make(Kind.Paint, shard, new Color(0.64f, 0.64f, 0.62f), 0f);
            Make(Kind.Burnt, shard, new Color(0.045f, 0.042f, 0.04f), 0f);
            Make(Kind.Foil, foil, new Color(0.6f, 0.5f, 0.3f), 0.15f);
            Make(Kind.Bolt, bolt, new Color(0.34f, 0.34f, 0.34f), 0.1f);
            Make(Kind.Clod, clod, soil, 0f);
            Make(Kind.Turf, sod, Color.white, 0f);
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

        static void Make(Kind kind, Mesh mesh, Color colour, float sheen)
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
            Shader s = (sheen > 0f ? Shader.Find("KSP/Specular") : null) ?? Shader.Find("KSP/Diffuse") ?? Shader.Find("Diffuse");
            var m = new Material(s) { name = "Fragmentation bits " + kind, color = colour };
            m.mainTexture = Speckle();
            // (a broad faint sheen, not a sharp highlight: one that winks on and off as a small bit tumbles is the glitter)
            if (m.HasProperty("_SpecColor")) m.SetColor("_SpecColor", new Color(sheen, sheen, sheen));
            if (m.HasProperty("_Shininess")) m.SetFloat("_Shininess", 0.08f);
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
