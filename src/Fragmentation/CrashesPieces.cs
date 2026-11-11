using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Fragmentation
{
    /// <summary>The pieces: what is torn off a part's skin, and (for a part whose skin cannot be read as panels) its old way of breaking up.</summary>
    public sealed partial class Crashes
    {
        float spawnHeat, spawnBurning, spawnViolence;  // for the panels about to be made: how hot, how many of them burning, how violently thrown
        Vector3 spawnFrom;                             // and from where (a burst's middle)
        Vector3 spawnCutKick;                          // and what is torn away whole is driven off so (a tank's end cap, along its axis)
        float spawnBurstSpeed;                         // and a tank going up throws its blown skin out at about this speed (see Crashes.BurstSpeedOf; 0, as it used to)
        readonly Shatter.Skin skin = new Shatter.Skin();
        float burningShare = 0.4f;
        readonly List<int> cellList = new List<int>();

        // ---- how a part's skin breaks up, by how hard it was struck (see BreakQ and Cluster)
        //
        // What a blow can do to a part goes with the energy it brings to each gram of it, half the square of its speed: at 15 m/s that is
        // about 0.1 J/g, a dent and a split; at 150 m/s about 11 J/g, a quarter of the 40 J/g the NASA standard breakup model takes for a
        // satellite to be broken up altogether, past which a part is shattered. Between those two, by the logarithm of the speed, the
        // break-up goes from 0 to 1. A burst tears a tank open as hard as a blow halfway between them, whatever the blow was.
        public static float BreakSlow = 15f, BreakFast = 150f, BurstBreak = 0.5f;
        // How much of what comes off goes in the largest piece: most of it at a gentle blow, little at a hard one. Fragments of shells and
        // casings broken up slowly keep 50 to 90 per cent of the mass in the largest, and the share falls smoothly as the energy rises
        // (Grady, Fragmentation of Rings and Shells, 2006).
        public static float BreakBiggest0 = 0.8f, BreakBiggest1 = 0.12f;
        // The rest come in a spread of sizes, the number bigger than a given size going as that size to the power -0.8 (Mott's and Grady's
        // fragment counts give 0.6 to 1); and pieces are made of no more than nine tenths of what comes off, the rest being bits.
        public static float BreakPower = 0.8f, BreakKept = 0.9f;
        // A piece grows no further once it would be stringier than a strip some eight times as long as it is wide (its edge's length,
        // squared, over its area: 4 pi for a disc, 16 for a square, 40 for a strip eight to one): a torn plate is not a ribbon.
        public static float Stringy = 40f;
        // Grouped into pieces so when this many panels or more come off a part at once by the same cause; fewer, each its own piece or not.
        public static int ClusterFrom = 2;
        // (off: every panel its own piece or not, as it used to be, for comparing)
        public static bool BreakUp = true;
#if DEV
        // (development build: the break-up taken as this, 0 to 1, whatever the blow; below 0, from the blow)
        internal static float TestBreakQ = -1f;
#endif

        // ---- how a piece is thrown, by its mass (see MakePiece)
        //
        // Debris from a crash keeps most of the speed it had and goes on the way the craft was going; what the blow adds to it is less the
        // heavier the piece, going roughly as its mass to the power -1/6 (Gurney's and Mott's figures for fragment speeds, and the spread
        // of aircraft wreckage along its track: the heavy pieces carry furthest). Taken beside a typical panel of the part (its mass per
        // area times its panels' edge squared), and held between 0.6 and 1.6 of it, so that a big remnant is pushed a little over half as
        // hard as a panel and a sliver a little over half again as hard.
        public static float KickPower = 1f / 6f, KickLeast = 0.6f, KickMost = 1.6f;
        // A crushed piece is pushed out sideways, away from the part's middle, by up to 2 m/s, and up by no more than 1.2 m/s (each at
        // least a quarter of that): the ground stops what was going down, it does not throw it up. Wreckage of aircraft that struck
        // shallow lies in a long, narrow trail down the track, not a ring about the crater.
        public static float CrushKick = 2f, CrushUp = 1.2f;
        // And its way over the ground fans out about the way it was going, by about this many degrees either way (the spread of a fair
        // spread of them, one standard deviation): debris fields of shallow crashes fan forward within some 15 to 30 degrees.
        public static float CrushFan = 12f;
        // What strikes the ground as it is made skips on along it, within this many degrees of the way it was going, not off to the side.
        public static float SkidFan = 15f;
        // How fast a piece turns: as fast as the part it came off was turning (this much of it), and as tearing it off turns it, about the
        // speed the tearing gave it over its own length (between 0.3 and 25 radians a second): a panel a metre across thrown off at a few
        // metres a second turns once or twice a second, a sliver a tenth that size ten times as fast.
        public static float SpinKept = 1f, TearSpinLeast = 0.3f, TearSpinMost = 25f;
        // (off: every piece thrown as it used to be, whatever its mass, for comparing)
        public static bool ThrownByMass = true;
#if DEV
        // (development build: how pieces were thrown since last cleared, see Tests.Thrown: how many, the least, most and mean share of the
        // kick they were given by their mass, the speed the throwing gave them beside their part's, how far the crushed were fanned, how
        // many skipped on the ground and how many of those went a random way, and how fast the small and the large ones turned and what
        // of that they had of their part)
        internal static int ThrownMade, ThrownFanned, ThrownSkids, ThrownSkidsRandom, ThrownSmall, ThrownMiddle, ThrownLarge;
        internal static float ThrownKickLeast = float.MaxValue, ThrownKickMost, ThrownKickSum, ThrownDvSum, ThrownFanSum, ThrownFanMost;
        internal static float ThrownSpinSmall, ThrownSpinMiddle, ThrownSpinLarge, ThrownKeptSmall, ThrownKeptLarge;
#endif

        // ---- what a part's torn skin weighs, and where that goes (see SpawnNow and Fold)
        //
        // Until 2026-11-03 a piece weighed only its own panels, flat, and every panel that did not become a piece became bits, which
        // weigh nothing: a part's skin leaves it a few panels at a time, over many steps, so that each time only a handful are grouped,
        // and the pieces of a fuel tank came to between four and a quarter of what it was built of, an engine once to less than a quarter.
        // But a crushed or torn panel does not vanish. Sheet metal crushed against the ground crumples and folds over on to the metal
        // beside it, and what comes away is that crumpled lump, carrying several times the metal its outline would show if it were laid
        // flat; what is truly lost is the fine stuff, the flakes of paint, the rivets, the slivers and the dust, a small share of the
        // whole (the debris of real crashes is mostly a few large crumpled pieces, by mass). So the metal of every panel torn off a part
        // at one time that did not become a piece goes into the pieces made of it at that time, shared among them by their area, all but
        // a fine share of it, which is lost as bits.
        /// <summary>Of a thin skin crushed, blown off, broken up or cut, the share lost as fine debris (paint, rivets, slivers): the rest is folded into the pieces.</summary>
        public static float FineShare = 0.15f;
        /// <summary>
        /// Of a thin skin the ground grinds away, the share lost: all of it, as before 2026-11-03. Abraded along the ground, it is left
        /// behind as shreds, flakes and a smear of metal, not folded into anything; a piece the grinding tears off whole still keeps its
        /// own panels.
        /// </summary>
        public static float FineGround = 1f;
        /// <summary>
        /// Of a part with no thin skin (an engine, a lump of steel: its meshes come away whole, see HullPlan.engine), the share lost by any
        /// cause, ground and all: a steel casing dragged along the ground is dented, gouged and torn into chunks, it is not worn to dust.
        /// </summary>
        public static float FineSolid = 0.03f;
        /// <summary>
        /// No piece weighs more than so many times its own area of the part's skin as built: crumpled up so tightly that it carries six
        /// times its outline's metal, a piece of aluminium sheet is already more a ball than a plate. What the pieces cannot take goes to
        /// the next of them, and what none of them can is lost.
        /// </summary>
        public static float DenseMost = 6f;
        /// <summary>
        /// (since 2026-11-04) What no piece made at one time can take is not lost but owed by the part: folded into its later pieces,
        /// whatever tears them off, and given to its own pieces still about, up to DenseMost while it holds together and up to DenseSettle
        /// when it goes altogether; only what even then none can take is lost. Until then a part torn a few panels at a time lost the
        /// metal of every time that made no piece, or whose pieces were full, or that came when the pieces about were at their most (in
        /// a shallow crash at 80 m/s, 1,530 kg of 6,338, three quarters of it at the pieces' cap). (off: lost at once, as before)
        /// </summary>
        public static bool CarryOwed = true;
        /// <summary>
        /// When a part goes altogether, its pieces may carry up to so many times their own area's metal: what it still owes is crumpled
        /// into the last of it. Fifteen times a panel's metal is a sheet folded into a lump; a part's skin, its stringers, its rings and its
        /// fittings are in it, and its fittings are no thin sheet.
        /// </summary>
        public static float DenseSettle = 15f;
        /// <summary>
        /// When nothing torn off a part at one time became a piece, but what would be folded into one comes to more than so many times
        /// the least a piece may weigh (3 kg), a piece is made of the largest group of those panels joined to one another, so that the
        /// metal is not lost (if the time and the pieces allowed still allow one).
        /// </summary>
        public static float KeepFrom = 3f;
        /// <summary>(off: every piece weighs its own panels only and the rest is lost, as before 2026-11-03, for comparing)</summary>
        public static bool KeepMass = true;
        const float LeastPiece = 0.003f;               // (tonnes: the least a piece weighs)

        // (the pieces made at one time, in the order made: each one; the frame it was thrown in (the ground's, or the game's); the part
        // of its throw that went by its mass, for a typical panel's mass (see KickPower), and the most that could be, if anything held
        // it down (a burst's BurstMost); the up of the ground under it, if any, and whether it was thrown at that ground and skipped
        // along it; its mass as made, its mass with what is folded into it, and what tore it off; and what is to be folded, by cause)
        static readonly List<Piece> callPieces = new List<Piece>();
        static readonly List<Vector3> callBasis = new List<Vector3>(), callKick = new List<Vector3>(), callGround = new List<Vector3>();
        static readonly List<float> callKickMost = new List<float>();
        static readonly List<bool> callSkid = new List<bool>();
        static readonly List<float> callFlat = new List<float>(), callMass = new List<float>();
        static readonly List<Torn> callWhy = new List<Torn>();
        static readonly float[] orphan = new float[5];  // (tonnes, by Torn)

        // (scratch, kept from one part to the next so that nothing is made anew each time: which panels are of the cause being grouped,
        // and whose piece each is; which went into a piece; which came away whole; stamped, so as never to be cleared)
        const int MostClusters = 16;
        static int[] breakIn = new int[0], breakOwner = new int[0], breakMade = new int[0], breakCut = new int[0], breakLeft = new int[0];
        static Torn[] breakWhy = new Torn[0];
        static int breakStamp;
        static readonly List<int> breakGroup = new List<int>(), cutCells = new List<int>(), cutStack = new List<int>();
        static readonly List<int>[] breakCells = Lists(MostClusters);
        static readonly float[] breakTarget = new float[MostClusters], breakArea = new float[MostClusters], breakEdge = new float[MostClusters];
        static readonly int[] breakHead = new int[MostClusters], breakOrder = new int[MostClusters];
        static readonly bool[] breakOpen = new bool[MostClusters];

        static List<int>[] Lists(int n)
        {
            var all = new List<int>[n];
            for (int k = 0; k < n; k++) all[k] = new List<int>();
            return all;
        }

        /// <summary>
        /// What has been torn off a part's skin since last time becomes pieces and bits:
        ///
        /// * ground away along the ground: shreds, flakes of paint and rivets left behind on the ground, and now and then a panel, folded up;
        /// * crushed: crumpled panels, and bits;
        /// * blown off by a burst: panels thrown out from the burst, many burning, blackened, with burning bits among them;
        /// * the whole part going: every panel left, as pieces;
        /// * torn right through: what came away as one piece, as it was.
        ///
        /// An engine's bell, its pump and its mounting each come away whole. A panel made into a piece costs a little time on the game's
        /// thread; past two milliseconds for one part (four for a part going altogether), what more comes off it is bits only, but it always
        /// makes one piece. The metal of every panel that went to bits is folded into the pieces made at the same time, all but a fine
        /// share (see FineShare and Fold), so that what a part was built of goes on in its wreckage. Returns how many pieces were made.
        /// </summary>
        int Spawn(Wreck w, Hits h, Hull hull, bool all = false)
        {
            long began = Prof.Now;
            try { return SpawnNow(w, h, hull, all); }
            finally { Prof.Add(Prof.Spawn, began); }
        }

        int SpawnNow(Wreck w, Hits h, Hull hull, bool all)
        {
#if DEV
            // (the part going altogether, its record marked gone before anything else: one whose skin had all been torn off before has
            // nothing left for BreakNow's TearAll to tear, and comes back at once below, before the mark further on. Looked up only, never
            // made: a part with no record tore nothing, and has nothing to show. Until 2026-11-04 such a part was shown "gone by the game")
            if (h.gone)
                for (int k = BrokeUps.Count - 1; k >= 0; k--)
                    if (ReferenceEquals(BrokeUps[k].h, h)) { BrokeUps[k].gone = true; break; }
#endif
            // (the part's mass as built, the same figure its strength and its skin's toughness are reckoned by: see FullMass)
            float perArea = Mathf.Max(0.0005f, FullMass(h)) / Mathf.Max(0.05f, hull.plan.area);
            if (hull.removed.Count == 0)
            {
                // (nothing left to tear, as when its skin had all been torn off before it went: what it owes given to its pieces still about)
                if (all && KeepMass && CarryOwed && h.owed > 0f) SettleOwed(h, perArea);
                return 0;
            }
            Part p = h.part;
            Rigidbody prb = BodyOf(p);
            CelestialBody body = p.vessel != null ? p.vessel.mainBody : FlightGlobals.currentMainBody;
            Vector3 middle = p.transform.position;
            Vector3 up = body != null ? (Vector3)FlightGlobals.getUpAxis(body, middle) : Vector3.up;
            byte tint = (byte)(Propellant(p, true) > Propellant(p) * 0.5 && Propellant(p) > 1.0 ? 1 : 0);
            // (what it owes from before, taken up now: folded into what is made now, and what is still not taken owed again, see Fold;
            // and the number of the last piece made before now, so that those made now are told from its older ones)
            float carried = KeepMass && CarryOwed ? h.owed : 0f;
            h.owed = 0f;
            int madeBefore = Piece.LastId;
            bool fuelled = Propellant(p) > 20.0;
            bool foil = false;
            for (int k = 0; k < p.Modules.Count && !foil; k++) foil = p.Modules[k] is ModuleCommand && p.CrewCapacity == 0 || p.Modules[k] is ModuleScienceExperiment || p.Modules[k] is ModuleDeployableSolarPanel;
            // (when the frames are already slow, fewer pieces and more bits: down to a third by fifteen frames a second. Space Engineers
            // drops its effects when its time is up, for the same reason: see the Research page)
            float slow = Mathf.Clamp01((slowFrames - 1f / 30f) / (1f / 15f - 1f / 30f));
            int made = 0;
            // (so many pieces at most from one part at one time: a fin breaking up is a handful of pieces and a shower of bits, not twenty pieces)
            int most = all ? Mathf.Clamp(Mathf.RoundToInt(hull.plan.area * 1.2f), 2, 12) : 6;
            // (and so long making them: two milliseconds, four for a part going altogether; but always one piece, however late in the step it is,
            // so that a part never breaks up into nothing but bits because others broke up before it in the same step)
            long until = System.Diagnostics.Stopwatch.GetTimestamp() + System.Diagnostics.Stopwatch.Frequency * (all ? 4 : 2) / 1000;
            // ---- crushed, blown off or broken up: the panels grouped into pieces by how hard the part was struck (see Cluster). Not what the
            // ground grinds off, which comes away as slivers, and not what was torn right through, which comes away as it is (below)
            BreakRoom(hull.plan.cells.Length);
            int inPiece = ++breakStamp;
            float q = BreakQ(h);
            Vector3 struck = h.touches > 0 ? h.point / h.touches : spawnFrom;
            // (an engine, or a part whose skin could not be read: steel, not thin sheet, and what comes off it is chunks, see FineSolid)
            bool solid = Solid(h);
            callPieces.Clear(); callBasis.Clear(); callKick.Clear(); callKickMost.Clear(); callGround.Clear(); callSkid.Clear(); callFlat.Clear(); callMass.Clear(); callWhy.Clear();
            for (int k = 0; k < orphan.Length; k++) orphan[k] = 0f;
            float fine = 0f;                           // (tonnes lost as fine debris at this time)
#if DEV
            BrokeUp record = RecordOf(h);
            record.q = q;
            // (the part going altogether: BreakNow marks it gone before it asks for its last pieces)
            record.gone |= h.gone;
            int recordFrom = record.kg.Count;
            float groundAway = 0f;                     // (of the fine, tonnes, what the ground ground away: see FineGround)
#endif
            int grouped = 0;
            if (BreakUp)
            {
                if (Cluster(w, h, hull, Torn.Crushed, q, struck, prb, up, tint, perArea, fuelled, most, until, slow, inPiece, ref made)) grouped |= 1 << (int)Torn.Crushed;
                if (Cluster(w, h, hull, Torn.Blown, q, struck, prb, up, tint, perArea, fuelled, most, until, slow, inPiece, ref made)) grouped |= 1 << (int)Torn.Blown;
                if (Cluster(w, h, hull, Torn.Broken, q, struck, prb, up, tint, perArea, fuelled, most, until, slow, inPiece, ref made)) grouped |= 1 << (int)Torn.Broken;
            }
            // (each panel's metal reckoned once, should it be listed twice; and those none took marked, for the piece made of them if none was, below)
            int counted = ++breakStamp, left = ++breakStamp;
            // (the friction of the ground its bits will slide on: paving if the part is on it, else what the ground is here; Emit takes
            // paving's wherever its own look under them finds paving, see Bits.Grip)
            float grip = Bits.Grip(h.paved ? null : w != null ? FeelFor(w, middle, Time.fixedTime) : null, h.paved);
            int isCut = ++breakStamp;
            cutCells.Clear();
            foreach (KeyValuePair<int, Torn> r in hull.removed)
            {
                int ci = r.Key;
                Torn why = r.Value;
                if (why == Torn.Cut) { if (breakCut[ci] != isCut) { breakCut[ci] = isCut; cutCells.Add(ci); } continue; }
                HullPlan.Cell cell = hull.plan.cells[ci];
                Vector3 at = hull.WorldMiddle(ci);
                Vector3 v = prb != null ? prb.GetPointVelocity(at) : (w != null ? w.body.At(at) : Vector3.zero);
                // ---- a piece, or not
                bool big = cell.area > 0.025f || cell.whole;
                bool piece;
                // (grouped above: in a piece if its group was made into one; if not, bits)
                if (big && !cell.whole && (grouped & (1 << (int)why)) != 0) piece = breakMade[ci] == inPiece;
                else
                {
                    float chance = why == Torn.Ground ? 0.3f : why == Torn.Crushed ? 0.65f : why == Torn.Blown ? 0.85f : cell.area > 0.4f * hull.plan.edge * hull.plan.edge ? 0.9f : 0.5f;
                    chance *= 1f - 0.65f * slow;
                    piece = big && (cell.whole || Random.value < chance) && made < most && Piece.Count < Piece.Most + 10 && (made == 0 || System.Diagnostics.Stopwatch.GetTimestamp() < until);
                    if (piece)
                    {
                        cellList.Clear();
                        cellList.Add(ci);
                        if (MakePiece(w, h, hull, cellList, why, prb, up, tint, perArea, fuelled) != null) made++;
                    }
                }
                // (its metal, if it is in no piece: all but the fine share of it to be folded into the pieces made now, see Fold)
                if (!piece && breakIn[ci] != counted)
                {
                    breakIn[ci] = counted;
                    breakWhy[ci] = why;
                    float kg = perArea * cell.area, lost = KeepMass ? FineOf(why, solid) : 1f;
                    // (only what keeps some metal may be made a piece of: not a skin the ground ground away)
                    if (lost < 1f) breakLeft[ci] = left;
                    orphan[(int)why] += kg * (1f - lost);
                    fine += kg * lost;
#if DEV
                    record.bitsArea += cell.area;
                    if (why == Torn.Ground) groundAway += kg * lost;
#endif
                }
                // ---- bits: shreds of skin, paint, rivets (and, from a part that carried foil, foil)
                float scale = Mathf.Clamp(hull.plan.edge * 0.35f, 0.05f, 0.25f);
                int n;
                Vector3 bv;
                float spread, burning = 0f, smoke;
                Bits.Kind kind;
                switch (why)
                {
                    case Torn.Ground:
                        n = Mathf.Clamp(Mathf.RoundToInt(cell.area * 18f), 1, 8);
                        // (left behind on the ground as the wreck goes on: thrown up a little, and soon still. Slowed about the ground, as
                        // the pieces torn off with them are, see MakePiece: above 750 m/s the game's frame goes with the craft)
                        {
                            Vector3 gv = GroundVelocity(h, at);
                            bv = (v - gv) * Random.Range(0.1f, 0.45f) + gv + up * 2f;
                        }
                        spread = 2.5f; smoke = fuelled || hull.Burnt(ci) ? 0.25f : 0.07f;
                        kind = Random.value < 0.45f ? Bits.Kind.Paint : Bits.Kind.Metal;
                        break;
                    case Torn.Blown:
                        n = Mathf.Clamp(Mathf.RoundToInt(cell.area * 16f), 2, 10);
                        Vector3 away = (at - spawnFrom).sqrMagnitude > 1e-4f ? (at - spawnFrom).normalized : Random.onUnitSphere;
                        // (a tank going up throws its shreds out a little slower than its petals, spread by a fifth of that speed)
                        if (spawnBurstSpeed > 0f) { bv = v * 0.8f + away * (spawnBurstSpeed * Random.Range(0.4f, 0.9f)) + up * 3f; spread = 0.2f * spawnBurstSpeed; }
                        else { bv = v * 0.8f + away * Random.Range(4f, 14f) + up * 3f; spread = 9f; }
                        burning = 0.35f; smoke = 0.5f;
                        kind = Random.value < 0.5f ? Bits.Kind.Burnt : Bits.Kind.Metal;
                        break;
                    case Torn.Crushed:
                        n = Mathf.Clamp(Mathf.RoundToInt(cell.area * 12f), 1, 7);
                        // (crushed against the ground, slowed about it; by another craft, about the game's frame, as their pieces are)
                        {
                            Vector3 gv = h.crushedOnGround ? GroundVelocity(h, at) : Vector3.zero;
                            bv = (v - gv) * Random.Range(0.6f, 0.9f) + gv + up * 2.5f;
                        }
                        spread = 4f; smoke = fuelled ? 0.2f : 0.07f; burning = spawnBurning * 0.3f;
                        kind = Random.value < 0.4f ? Bits.Kind.Paint : Bits.Kind.Metal;
                        break;
                    default:
                        n = Mathf.Clamp(Mathf.RoundToInt(cell.area * 10f), 1, 6);
                        bv = v + up * 2f;
                        spread = 3f + 4f * spawnViolence; smoke = spawnHeat > 0f ? 0.4f : 0.07f; burning = spawnBurning * 0.4f;
                        kind = spawnHeat > 0f && Random.value < 0.5f ? Bits.Kind.Burnt : Bits.Kind.Metal;
                        break;
                }
                if (piece) n = Mathf.Max(1, n / 2);
                Bits.Emit(at, bv, spread, n, scale * 0.15f, scale, kind, burning, smoke, why == Torn.Ground ? 1.5f : 3f, grip);
                // (a spacecraft's skin is lagged with foil blankets: shreds of it among the bits; foil and bolts slide on soil as shreds
                // do, on paving a little more and a little less easily, see Bits.GripFoil)
                if (foil) Bits.Emit(at, bv, spread * 1.3f, Mathf.Max(1, n / 2), scale * 0.3f, scale * 1.5f, Bits.Kind.Foil, 0f, 0f, 2f, grip);
                if (Random.value < 0.35f) Bits.Emit(at, bv, spread, Mathf.Max(1, n / 4), 0.012f, 0.04f, Bits.Kind.Bolt, 0f, 0f, 0f, grip);
            }
            // ---- what came away as one: each group of panels joined to one another, one piece (each panel struck off its stamp as it
            // is taken, so that finding the next is no search of the list)
            for (int j = 0; j < cutCells.Count; j++)
            {
                int first = cutCells[j];
                if (breakCut[first] != isCut) continue;
                breakCut[first] = 0;
                cellList.Clear();
                cutStack.Clear();
                cutStack.Add(first);
                while (cutStack.Count > 0)
                {
                    int c = cutStack[cutStack.Count - 1];
                    cutStack.RemoveAt(cutStack.Count - 1);
                    cellList.Add(c);
                    foreach (int o in hull.plan.cells[c].near) if (breakCut[o] == isCut) { breakCut[o] = 0; cutStack.Add(o); }
                }
                if (MakePiece(w, h, hull, cellList, Torn.Cut, prb, up, tint, perArea, fuelled) != null) made++;
                else
                {
                    // (none could be made of it: its metal folded into the others, as any panel's that is in no piece)
                    float a = 0f;
                    for (int c = 0; c < cellList.Count; c++) a += hull.plan.cells[cellList[c]].area;
                    float lost = KeepMass ? FineOf(Torn.Cut, solid) : 1f;
                    orphan[(int)Torn.Cut] += perArea * a * (1f - lost);
                    fine += perArea * a * lost;
                }
            }
            cutCells.Clear();
            // ---- nothing made a piece, but a fair weight of metal came off: a piece of the largest group of those panels, joined. (With
            // what the part owes counted when it goes altogether, the last time it can be given: before that it waits for the part's next
            // pieces, and no piece is made for it that would not have been made)
            if (KeepMass && callPieces.Count == 0 && ToFold() + (all ? carried : 0f) > KeepFrom * LeastPiece && made < most && Piece.Count < Piece.Most + 10)
            {
                Torn why = Largest(hull, left);
                if (cellList.Count > 0 && MakePiece(w, h, hull, cellList, why, prb, up, tint, perArea, fuelled) != null)
                {
                    made++;
                    // (its own panels no longer to be folded into it)
                    for (int c = 0; c < cellList.Count; c++)
                    {
                        int ci = cellList[c];
                        float kg = perArea * hull.plan.cells[ci].area, lost = FineOf(breakWhy[ci], solid);
                        orphan[(int)breakWhy[ci]] = Mathf.Max(0f, orphan[(int)breakWhy[ci]] - kg * (1f - lost));
                        fine = Mathf.Max(0f, fine - kg * lost);
#if DEV
                        record.bitsArea = Mathf.Max(0f, record.bitsArea - hull.plan.cells[ci].area);
                        // (a ground-torn panel is only ever in this piece if FineGround has been set below 1, see Tests.Set)
                        if (breakWhy[ci] == Torn.Ground) groundAway = Mathf.Max(0f, groundAway - kg * lost);
#endif
                    }
                }
            }
            // ---- and the metal of what is in no piece folded into those made, by their area
#if DEV
            // (asked before Fold, which leaves callPieces as it is: whether this time made any piece to fold into)
            int folded = callPieces.Count;
#endif
            float unfolded = Fold(perArea, hull.plan.edge, carried, h, all, madeBefore, out float settled, out int older);
            // (what is still not taken owed by the part, while it holds together: given at its next tearing, or when it goes)
            if (KeepMass && CarryOwed && !all) { h.owed = unfolded; unfolded = 0f; }
#if DEV
            for (int k = 0; k < callPieces.Count && recordFrom + k < record.kg.Count; k++) record.kg[recordFrom + k] = callMass[k] * 1000f;
            record.fineKg += fine * 1000f;
            record.groundKg += groundAway * 1000f;
            record.lostKg += unfolded * 1000f;
            record.settledKg += settled * 1000f;
            record.owedKg = h.owed * 1000f;
            // Why it was lost, which is what the loss's cure turns on. Pieces were made, or (owed, when the part went) its older pieces
            // were still about, and none had room left: "full". No piece was made and the pieces about were at their most (Piece.Most and
            // ten over: neither a panel's piece, a group's in Cluster nor the one made of what none took can be made then): "cap". No
            // piece was made for any other reason (a lone panel's roll failed, the panels were too small, what came off was under
            // KeepFrom pieces' worth): "none".
            if (folded > 0 || older > 0) record.lostFullKg += unfolded * 1000f;
            else if (Piece.Count >= Piece.Most + 10) record.lostCapKg += unfolded * 1000f;
            else record.lostNoneKg += unfolded * 1000f;
#endif
            callPieces.Clear();
            hull.removed.Clear();
            return made;
        }

        /// <summary>Of what came off a part by a cause, the share lost as fine debris (see FineShare): a solid part's by any cause, a thin skin's by the ground or otherwise.</summary>
        static float FineOf(Torn why, bool solid) => Mathf.Clamp01(solid ? FineSolid : why == Torn.Ground ? FineGround : FineShare);

        /// <summary>The metal still to be folded into pieces at this time, tonnes.</summary>
        static float ToFold()
        {
            float all = 0f;
            for (int k = 0; k < orphan.Length; k++) all += orphan[k];
            return all;
        }

        /// <summary>
        /// The largest group of the panels torn off at this time that none took (marked so), joined to one another, into cellList (each
        /// struck off its mark as it is taken, so that no panel is looked at twice); and what tore off most of its area.
        /// </summary>
        Torn Largest(Hull hull, int left)
        {
            HullPlan plan = hull.plan;
            cellList.Clear();
            float bestArea = -1f;
            Torn bestWhy = Torn.Crushed;
            for (int k = 0; k < hull.removed.Count; k++)
            {
                int first = hull.removed[k].Key;
                if (breakLeft[first] != left) continue;
                breakLeft[first] = 0;
                breakGroup.Clear();
                cutStack.Clear();
                cutStack.Add(first);
                float area = 0f, most = -1f;
                Torn why = breakWhy[first];
                while (cutStack.Count > 0)
                {
                    int c = cutStack[cutStack.Count - 1];
                    cutStack.RemoveAt(cutStack.Count - 1);
                    breakGroup.Add(c);
                    float a = plan.cells[c].area;
                    area += a;
                    if (a > most) { most = a; why = breakWhy[c]; }
                    foreach (int o in plan.cells[c].near) if (breakLeft[o] == left) { breakLeft[o] = 0; cutStack.Add(o); }
                }
                if (area > bestArea) { bestArea = area; bestWhy = why; cellList.Clear(); cellList.AddRange(breakGroup); }
            }
            return bestWhy;
        }

        /// <summary>
        /// The metal of the panels torn off at this time that are in no piece, folded into the pieces made at this time: first into those
        /// torn off by the same cause (a crushed panel crumples on to the crushed metal beside it), then into any; each by its area, and
        /// none past DenseMost times its own area's metal, what one cannot take going to the others. Then the part of each heavier piece's
        /// throw that went by its mass is made what its new mass would have had (the less for it, see KickPower), and nothing else of its
        /// going: not the share of its speed its cause left it with, not a crushed piece's lift, and not how it skipped off the ground.
        ///
        /// (Since 2026-11-04, see CarryOwed) What the part owed from before (carried) is folded in with the rest, into the pieces of any
        /// cause; and what this time's pieces cannot take is given to the part's pieces still about, its older ones (made before this time,
        /// numbered madeBefore or lower), up to DenseMost; when it goes altogether (all), this time's and the older up to DenseSettle. Says how
        /// much the older ones took (settled) and how many were still about (older).
        ///
        /// Returns what none could take, tonnes: lost, or (CarryOwed, the part holding together) owed again.
        /// </summary>
        float Fold(float perArea, float edge, float carried, Hits h, bool all, int madeBefore, out float settled, out int older)
        {
            int n = callPieces.Count;
            for (int k = 0; k < n; k++) callMass.Add(callFlat[k]);
            float spill = 0f;
            settled = 0f; older = 0;
            if (n > 0)
            {
                for (int c = 0; c < orphan.Length; c++)
                {
                    if (orphan[c] <= 0f) continue;
                    spill += Share(orphan[c], c, perArea, DenseMost);
                    orphan[c] = 0f;
                }
                // (what the part owed, after the shares by cause and before the share among any: it was torn off by causes long past,
                // and goes into whatever piece of it has room. After that share it would come back unshared, and be lost again)
                spill += carried;
                if (spill > 0f) spill = Share(spill, -1, perArea, DenseMost);
            }
            else spill = ToFold() + carried;
            if (KeepMass && CarryOwed && spill > 0f)
            {
                // (the part going: crumpled into the last of it, this time's pieces first, as they are thrown now)
                float dense = all ? Mathf.Max(DenseMost, DenseSettle) : DenseMost;
                if (all && n > 0 && dense > DenseMost) spill = Share(spill, -1, perArea, dense);
                if (spill > 0f) spill = Settle(h, spill, perArea, dense, madeBefore, out settled, out older);
            }
            float typical = Mathf.Max(1e-5f, perArea * edge * edge);
            for (int k = 0; k < n; k++)
            {
                Piece p = callPieces[k];
                if (p == null || p.rb == null || callMass[k] <= callFlat[k] + 1e-6f) continue;
                p.rb.mass = callMass[k];
                p.mass = callMass[k];
                Torn why = callWhy[k];
                if (ThrownByMass && why != Torn.Cut && why != Torn.Ground)
                {
                    float was = Mathf.Clamp(Mathf.Pow(callFlat[k] / typical, -KickPower), KickLeast, KickMost);
                    float now = Mathf.Clamp(Mathf.Pow(callMass[k] / typical, -KickPower), KickLeast, KickMost);
                    if (was <= 1e-4f || now >= was || callKick[k].sqrMagnitude < 1e-8f) continue;
                    // The blow's push, as it was and as it would have been: the same way, the less by the heavier mass, and held to the
                    // same most (a burst's sliver at BurstMost stays there while its new mass would still have reached it). Only that
                    // part of the throw is changed. Scaling the whole of the throw about the speed its cause left it with brought back
                    // a share of that speed's way into the ground, which MakePiece's skip had taken off: a piece of a nose that came down
                    // at 50 m/s, made six times heavier, went back into the ground at 7 m/s and bounced, where it should have skipped.
                    float most = callKickMost[k];
                    Vector3 before = Vector3.ClampMagnitude(callKick[k] * was, most), after = Vector3.ClampMagnitude(callKick[k] * now, most);
                    Vector3 change = after - before;
                    // With ground under it, the lesser push never takes it nearer the ground than it was set going; and if it was thrown
                    // at the ground and skipped off it, the speed it left the ground at was the bounce's, not the push's, and stays.
                    Vector3 g = callGround[k];
                    if (g.sqrMagnitude > 0.5f)
                    {
                        float into = Vector3.Dot(change, g);
                        if (into < 0f || callSkid[k]) change -= g * into;
                    }
                    // (and as a guard, never faster than it was in the frame it was thrown in: the lesser push only ever slows it)
                    Vector3 v = p.rb.velocity - callBasis[k], v2 = v + change;
                    if (v2.sqrMagnitude < v.sqrMagnitude) p.rb.velocity = v2 + callBasis[k];
                }
            }
            return spill;
        }

        /// <summary>So much metal (tonnes) shared among the pieces made at this time of a cause (below 0: of any), by their area, each up to so many times its own area's metal (DenseMost, or DenseSettle as the part goes); returns what none could take.</summary>
        float Share(float pool, int cause, float perArea, float dense)
        {
            float densest = Mathf.Max(1f, dense) * perArea;
            // (a few rounds: what the full ones could not take shared again among the rest)
            for (int round = 0; round < 4 && pool > 1e-7f; round++)
            {
                float weight = 0f;
                for (int k = 0; k < callPieces.Count; k++)
                {
                    if (cause >= 0 && (int)callWhy[k] != cause || callPieces[k] == null) continue;
                    if (densest * callPieces[k].area - callMass[k] > 1e-7f) weight += callPieces[k].area;
                }
                if (weight <= 0f) break;
                float given = 0f;
                for (int k = 0; k < callPieces.Count; k++)
                {
                    if (cause >= 0 && (int)callWhy[k] != cause || callPieces[k] == null) continue;
                    float room = densest * callPieces[k].area - callMass[k];
                    if (room <= 1e-7f) continue;
                    float add = Mathf.Min(room, pool * callPieces[k].area / weight);
                    callMass[k] += add;
                    given += add;
                }
                pool -= given;
            }
            return Mathf.Max(0f, pool);
        }

        /// <summary>
        /// What a part owes given to its own pieces still about that were made before this time (numbered madeBefore or lower: those made
        /// now are shared among by Share), by their area, each up to so many times its own area's metal; none that is fading away. Each
        /// is made the heavier at once, its body with it, and nothing else of how it goes changes: one in flight goes on at the speed it
        /// has (a heavier piece is no faster for it, and its throw was its own), one lying still lies still. Returns what none could
        /// take; says how much they took, and how many were still about.
        /// </summary>
        static float Settle(Hits h, float pool, float perArea, float dense, int madeBefore, out float given, out int about)
        {
            given = 0f; about = 0;
            if (h == null || h.pieces == null || pool <= 0f) return Mathf.Max(0f, pool);
            float densest = Mathf.Max(1f, dense) * perArea;
            for (int k = 0; k < h.piecesKept; k++) if (h.pieceIds[k] <= madeBefore && h.Still(k)) about++;
            // (a few rounds, as Share: what the full ones could not take shared again among the rest)
            for (int round = 0; round < 4 && pool > 1e-7f && about > 0; round++)
            {
                float weight = 0f;
                for (int k = 0; k < h.piecesKept; k++)
                {
                    if (h.pieceIds[k] > madeBefore || !h.Still(k)) continue;
                    Piece p = h.pieces[k];
                    if (densest * p.area - p.mass > 1e-7f) weight += p.area;
                }
                if (weight <= 1e-6f) break;
                float took = 0f;
                for (int k = 0; k < h.piecesKept; k++)
                {
                    if (h.pieceIds[k] > madeBefore || !h.Still(k)) continue;
                    Piece p = h.pieces[k];
                    float room = densest * p.area - p.mass;
                    if (room <= 1e-7f) continue;
                    float add = Mathf.Min(room, pool * p.area / weight);
                    p.mass += add;
                    p.rb.mass = p.mass;
                    took += add;
                }
                pool -= took;
                given += took;
            }
            return Mathf.Max(0f, pool);
        }

        /// <summary>
        /// A part going altogether with nothing left of its skin to tear: what it owes given to its pieces still about, up to DenseSettle,
        /// and what none can take lost (and said why, as SpawnNow does).
        /// </summary>
        static void SettleOwed(Hits h, float perArea)
        {
            float owed = h.owed;
            h.owed = 0f;
            float left = Settle(h, owed, perArea, Mathf.Max(DenseMost, DenseSettle), Piece.LastId, out float settled, out int about);
#if DEV
            BrokeUp record = RecordOf(h);
            record.settledKg += settled * 1000f;
            record.owedKg = 0f;
            record.lostKg += left * 1000f;
            if (about > 0) record.lostFullKg += left * 1000f;
            else if (Piece.Count >= Piece.Most + 10) record.lostCapKg += left * 1000f;
            else record.lostNoneKg += left * 1000f;
#endif
        }

        /// <summary>
        /// How hard a part's skin breaks up, from 0 (a blow at 15 m/s or less: it comes away in one big piece and a few small ones) to 1
        /// (150 m/s or more: many small pieces, none much bigger than the rest), by the logarithm of the hardest blow it has taken (the
        /// speed over the ground, the game's moving frame and all: see Blow). A burst breaks it up at least halfway.
        /// </summary>
        float BreakQ(Hits h)
        {
#if DEV
            if (TestBreakQ >= 0f) return Mathf.Clamp01(TestBreakQ);
#endif
            float slowest = Mathf.Max(0.1f, BreakSlow);
            float q = Mathf.Clamp01(Mathf.Log(Mathf.Max(1f, h.hardest) / slowest) / Mathf.Log(Mathf.Max(1.01f, BreakFast / slowest)));
            if (spawnHeat >= 1f) q = Mathf.Max(q, BurstBreak);
            return q;
        }

        /// <summary>The scratch for grouping panels, as long as this part has panels (made longer only for a part with more than any before it).</summary>
        static void BreakRoom(int cells)
        {
            if (breakIn.Length < cells)
            {
                int n = Mathf.Max(cells, breakIn.Length * 2);
                breakIn = new int[n]; breakOwner = new int[n]; breakMade = new int[n]; breakCut = new int[n]; breakLeft = new int[n]; breakWhy = new Torn[n];
                breakStamp = 0;
            }
            // (the stamps run out only after some two thousand million parts' pieces; then start again)
            if (breakStamp > int.MaxValue - 64)
            {
                System.Array.Clear(breakIn, 0, breakIn.Length); System.Array.Clear(breakMade, 0, breakMade.Length); System.Array.Clear(breakCut, 0, breakCut.Length);
                System.Array.Clear(breakLeft, 0, breakLeft.Length);
                breakStamp = 0;
            }
        }

        /// <summary>
        /// The panels a part has lost by one cause at one time (crushed, blown off, or broken up as it goes altogether), grouped into pieces
        /// the way things break up, and the pieces made. Returns false (and groups nothing) when too few panels came off to group.
        ///
        /// Real shells and casings broken by a blow do not come apart into equal plates. At a gentle blow most of what comes off stays as one
        /// big crumpled remnant with a few small pieces beside it; as the energy rises the largest piece's share falls, and at a hard blow there
        /// are many small pieces, the number bigger than any size going as that size to the power -0.8 (Mott; Grady). So: the first piece is
        /// given a share of the area that goes from 0.8 at q 0 to 0.12 at q 1; the others are drawn from that power law, between a panel's size
        /// and the first's, until nine tenths of the area is given out or there are as many as may be made. The first grows from the panel
        /// farthest from where the part was struck, since the struck end is the one shredded and the far end comes away whole; the others
        /// from panels taken at random. They grow a panel at a time each in turn, over panels that border one another, until each has its
        /// share, it has nowhere left to grow, or it would grow into a ribbon. What none of them took is bits.
        ///
        /// The pieces are made largest first, so that what the time allowed, the most pieces allowed and the one piece always made fall on the
        /// largest, and what is left over is the small ones, as bits. Every panel's bits are still thrown (see SpawnNow), fewer from those in a piece.
        /// </summary>
        bool Cluster(Wreck w, Hits h, Hull hull, Torn why, float q, Vector3 struck, Rigidbody prb, Vector3 up, byte tint, float perArea, bool fuelled, int most, long until, float slow, int inPiece, ref int made)
        {
            HullPlan plan = hull.plan;
            List<int> group = breakGroup;
            group.Clear();
            int mark = ++breakStamp;
            float area = 0f;
            for (int k = 0; k < hull.removed.Count; k++)
            {
                if (hull.removed[k].Value != why) continue;
                int ci = hull.removed[k].Key;
                HullPlan.Cell cell = plan.cells[ci];
                // (a panel that comes away whole, an engine's bell, is a piece of its own; one too small to be a piece is bits)
                if (cell.whole || cell.area <= 0.025f || breakIn[ci] == mark) continue;
                breakIn[ci] = mark;
                breakOwner[ci] = -1;
                group.Add(ci);
                area += cell.area;
            }
            if (group.Count == 0 || group.Count < ClusterFrom) return false;
            // ---- how big each piece is to be. (As many as may still be made from this part, fewer when the frames are slow, as SpawnNow does)
            int room = Mathf.Max(1, most - made);
            int pieces = Mathf.Min(group.Count, Mathf.Min(MostClusters, Mathf.Max(1, Mathf.RoundToInt(room * (1f - 0.65f * slow)))));
            float largest = area * Mathf.Clamp01(Mathf.Lerp(BreakBiggest0, BreakBiggest1, q));
            float least = Mathf.Max(plan.edge * plan.edge, 0.025f);
            float b = Mathf.Max(0.05f, BreakPower);
            breakTarget[0] = largest;
            float given = largest;
            int targets = 1;
            while (targets < pieces && given < BreakKept * area)
            {
                float a;
                if (least >= largest) a = largest;
                else
                {
                    // (drawn from the power law cut off at the panel's size and the first piece's: the share of pieces bigger than a goes as a^-b)
                    float lo = Mathf.Pow(least, -b), hi = Mathf.Pow(largest, -b);
                    a = Mathf.Pow(lo - Random.value * (lo - hi), -1f / b);
                }
                breakTarget[targets++] = a;
                given += a;
            }
            // ---- where each begins: the first farthest from the blow, the others anywhere not yet taken
            int seed = group[0];
            float farthest = -1f;
            for (int k = 0; k < group.Count; k++)
            {
                float d = (hull.WorldMiddle(group[k]) - struck).sqrMagnitude;
                if (d > farthest) { farthest = d; seed = group[k]; }
            }
            Begin(plan, 0, seed);
            for (int k = 1; k < targets; k++)
            {
                int pick = -1;
                for (int tries = 0; tries < 6 && pick < 0; tries++) { int c = group[Random.Range(0, group.Count)]; if (breakOwner[c] < 0) pick = c; }
                if (pick < 0)
                    for (int j = 0, from = Random.Range(0, group.Count); j < group.Count && pick < 0; j++) { int c = group[(from + j) % group.Count]; if (breakOwner[c] < 0) pick = c; }
                if (pick < 0) { targets = k; break; }
                Begin(plan, k, pick);
            }
            // ---- grown, a panel each in turn
            int stringy = 0, blocked = 0;
            bool growing = true;
            while (growing)
            {
                growing = false;
                for (int k = 0; k < targets; k++)
                {
                    if (!breakOpen[k]) continue;
                    int next = Border(plan, k, mark);
                    if (next < 0) { breakOpen[k] = false; blocked++; continue; }
                    float a = plan.cells[next].area;
                    // (stopped at its share: a panel more would take it further past its share than it is short of it)
                    if (breakArea[k] + 0.5f * a > breakTarget[k]) { breakOpen[k] = false; continue; }
                    float edge = breakEdge[k] + Joined(plan, next, k, mark), grown = breakArea[k] + a;
                    // (and stopped before it grows into a ribbon: past the limit, and stringier than it was)
                    if (edge * edge > Stringy * grown && edge * edge * breakArea[k] > breakEdge[k] * breakEdge[k] * grown) { breakOpen[k] = false; stringy++; continue; }
                    breakCells[k].Add(next);
                    breakOwner[next] = k;
                    breakArea[k] = grown;
                    breakEdge[k] = edge;
                    growing = true;
                }
            }
            // ---- made, largest first
            for (int k = 0; k < targets; k++)
            {
                int j = k;
                while (j > 0 && breakArea[breakOrder[j - 1]] < breakArea[k]) { breakOrder[j] = breakOrder[j - 1]; j--; }
                breakOrder[j] = k;
            }
            // (crushed or broken at a gentle blow, a piece is bent and dented rather than crumpled up: a hard blow crumples it right up)
            float crumpleBy = why == Torn.Blown ? 1f : 0.5f + 0.5f * q;
            int fromHere = 0;
            for (int j = 0; j < targets; j++)
            {
                if (!(made < most && Piece.Count < Piece.Most + 10 && (made == 0 || System.Diagnostics.Stopwatch.GetTimestamp() < until))) break;
                List<int> cells = breakCells[breakOrder[j]];
                if (MakePiece(w, h, hull, cells, why, prb, up, tint, perArea, fuelled, crumpleBy) == null) continue;
                made++; fromHere++;
                for (int c = 0; c < cells.Count; c++) breakMade[cells[c]] = inPiece;
            }
#if DEV
            BreakLastPart = Name(h); BreakLastWhy = why; BreakLastQ = q; BreakLastArea = area; BreakLastPanels = group.Count;
            BreakLastTargets = targets; BreakLastMade = fromHere; BreakLastStringy = stringy; BreakLastBlocked = blocked;
            int none = 0;
            for (int k = 0; k < group.Count; k++) if (breakOwner[group[k]] < 0) none++;
            BreakLastLeft = none;
            for (int k = 0; k < BreakLastAreas.Length; k++) BreakLastAreas[k] = k < targets ? breakArea[breakOrder[k]] : 0f;
            BreakGrouped++;
#endif
            return true;
        }

        /// <summary>A group's first panel.</summary>
        static void Begin(HullPlan plan, int k, int ci)
        {
            List<int> cells = breakCells[k];
            cells.Clear();
            cells.Add(ci);
            breakOwner[ci] = k;
            breakArea[k] = plan.cells[ci].area;
            breakEdge[k] = Joined(plan, ci, k, -1);
            breakHead[k] = 0;
            breakOpen[k] = true;
        }

        /// <summary>The next panel group k may grow into: one of the cause's not yet taken that borders one of its own, its oldest first
        /// (so that it grows out evenly all round, breadth first); -1 if there is none.</summary>
        static int Border(HullPlan plan, int k, int mark)
        {
            List<int> cells = breakCells[k];
            while (breakHead[k] < cells.Count)
            {
                int[] near = plan.cells[cells[breakHead[k]]].near;
                for (int j = 0; j < near.Length; j++) { int o = near[j]; if (breakIn[o] == mark && breakOwner[o] < 0) return o; }
                breakHead[k]++;
            }
            return -1;
        }

        /// <summary>How much longer group k's edge is with panel ci added to it, metres: the panel's own edge, less twice what it shares with the group.</summary>
        static float Joined(HullPlan plan, int ci, int k, int mark)
        {
            HullPlan.Cell c = plan.cells[ci];
            float longer = 0f;
            for (int e = 0; e < c.edgeA.Length; e++)
            {
                float length = (plan.p[c.edgeA[e]] - plan.p[c.edgeB[e]]).magnitude;
                int o = c.edgeOther[e];
                // (an edge shared with the group was its edge, and is now inside it: it comes off, as the panel's side of it is not added)
                longer += o >= 0 && breakIn[o] == mark && breakOwner[o] == k ? -length : length;
            }
            return longer;
        }

        /// <summary>Whether what tore this panel off was the ground (or the water): ground away along it, or crushed against it, and not by another craft.</summary>
        static bool ByGround(Hits h, Torn why) => why == Torn.Ground || (why == Torn.Crushed && h.crushedOnGround);

        /// <summary>
        /// The ground's own velocity at a place, in the game's frame: still once the frame the game floats the scene in is taken off
        /// (Krakensbane's, above some 750 m/s); and where the game's frame does not turn with the world, as it does not high above it,
        /// moving as the world's turning carries it there.
        /// </summary>
        static Vector3 GroundVelocity(Hits h, Vector3 at)
        {
            CelestialBody cb = h.part != null && h.part.vessel != null ? h.part.vessel.mainBody : FlightGlobals.currentMainBody;
            return -(Vector3)Krakensbane.GetFrameVelocity() + (cb != null && !cb.inverseRotation ? (Vector3)cb.getRFrmVel(at) : Vector3.zero);
        }

        /// <summary>One piece of a part's skin, made of some of its panels, set going the way what tore it off would throw it.</summary>
        Piece MakePiece(Wreck w, Hits h, Hull hull, List<int> cells, Torn why, Rigidbody prb, Vector3 up, byte tint, float perArea, bool fuelled, float crumpleBy = 1f)
        {
            bool whole = cells.Count == 1 && hull.Whole(cells[0]);
            bool burnt = false;
            foreach (int c in cells) if (hull.Burnt(c)) burnt = true;
            float crumple = why == Torn.Crushed ? Random.Range(0.6f, 1f) : why == Torn.Ground ? Random.Range(0.7f, 1f) : why == Torn.Blown ? Random.Range(0.35f, 0.9f) : why == Torn.Cut ? Random.Range(0f, 0.15f) : Random.Range(0.2f, 0.6f);
            crumple *= crumpleBy;
            // (an engine's bell, or anything else that comes away whole, is dented rather than folded)
            if (whole) crumple *= 0.25f;
            float soot = burnt || spawnHeat > 0f ? Random.Range(0.45f, 0.85f) : why == Torn.Ground ? Random.Range(0.15f, 0.35f) : Random.Range(0f, 0.12f);
            long t = Prof.Now;
            Mesh mesh = hull.PieceOf(cells, crumple, soot, out Vector3 at, out Quaternion turn, out Vector3 extent, out float area, out Material[] mats, out Color[] tints);
            Prof.Add(Prof.PieceMesh, t);
            if (mesh == null) return null;
            // What the ground tore off, or crushed against itself, is slowed in the frame of the ground, not the frame the game floats the
            // scene in. Above some 750 m/s the game moves its frame with the craft (Krakensbane) and the bodies' speeds are only what is
            // over and above the frame's: a panel the ground tore off, left with a quarter of that, still went on at the frame's 750 m/s
            // or more and kept up with the wreck, when it should have been left behind along its track. Everything else (crushed by
            // another craft, blown off by a burst, broken up or cut) is slowed about the frame, as before and as its bits are: a burst
            // keeps the speed of its middle, and two craft bumping in orbit at 10 m/s throw their pieces at a few metres a second, not at
            // a share of the 2,200 m/s they are going round Kerbin at. Below 750 m/s, low over a world, the frame stands still and the
            // ground with it, and the two are the same.
            Vector3 basis = ByGround(h, why) ? GroundVelocity(h, at) : Vector3.zero;
            Vector3 v = (prb != null ? prb.GetPointVelocity(at) : (w != null ? w.body.At(at) : Vector3.zero)) - basis;
            // (the speed it had as part of the part, before it is thrown: what the throwing gives it beside the share of this that its
            // cause leaves it with sets it turning, below)
            Vector3 had = v;
            float keep = 1f;
            Quaternion fanTurn = Quaternion.identity;
            Vector3 away = at - h.part.transform.position;
            away = away.sqrMagnitude > 1e-4f ? away.normalized : Random.onUnitSphere;
            float mass = Mathf.Max(0.003f, perArea * area);
            // (how hard the blow throws it, by its mass beside a typical panel's: see KickPower)
            float typical = Mathf.Max(1e-5f, perArea * hull.plan.edge * hull.plan.edge);
            float kickScale = ThrownByMass ? Mathf.Clamp(Mathf.Pow(mass / typical, -KickPower), KickLeast, KickMost) : 1f;
            // (the part of the throw that goes by the mass, for a typical panel, and the most it may be: kept for Fold, which makes it
            // what a heavier piece would have had, and changes nothing else of how the piece goes)
            Vector3 kickUnit = Vector3.zero;
            float kickMost = float.PositiveInfinity;
#if DEV
            float fanned = 0f;
            bool skidded = false, skidRandom = false;
#endif
            switch (why)
            {
                case Torn.Ground:
                    // (torn off by the ground as the wreck goes over it: left behind)
                    keep = Random.Range(0.25f, 0.6f);
                    v = v * keep + up * Random.Range(0.5f, 3f) + Random.insideUnitSphere * 1.5f;
                    break;
                case Torn.Crushed:
                    keep = Random.Range(0.7f, 0.95f);
                    if (!ThrownByMass) { v = v * keep + away * Random.Range(2f, 7f) + up * Random.Range(1f, 4f); break; }
                    // (it goes on much as it was going, pushed out sideways from the part's middle and a little up: see CrushKick)
                    Vector3 side = Vector3.ProjectOnPlane(away, up);
                    if (side.sqrMagnitude < 1e-4f) side = Vector3.ProjectOnPlane(Random.onUnitSphere, up);
                    side = side.sqrMagnitude > 1e-6f ? side.normalized : Vector3.zero;
                    kickUnit = side * (CrushKick * Random.Range(0.25f, 1f));
                    v = v * keep + kickUnit * kickScale + up * (CrushUp * Random.Range(0.25f, 1f));
                    // (and its way over the ground turned a little about the up, fanning the pieces forward: the sum of three evens less 1.5
                    // spreads as a bell, one standard deviation a half, so twice CrushFan of it is CrushFan degrees to one deviation)
                    float fan = (Random.value + Random.value + Random.value - 1.5f) * 2f * CrushFan;
                    Vector3 rise = up * Vector3.Dot(v, up);
                    fanTurn = Quaternion.AngleAxis(fan, up);
                    v = rise + fanTurn * (v - rise);
                    // (the push was along the ground, so it fans with the rest)
                    kickUnit = fanTurn * kickUnit;
#if DEV
                    fanned = fan;
#endif
                    break;
                case Torn.Blown:
                    Vector3 out_ = at - spawnFrom;
                    out_ = out_.sqrMagnitude > 1e-4f ? out_.normalized : away;
                    // (out from the burst at the speed what was in the tank gives its casing, the lighter pieces the faster: see
                    // Crashes.BurstBiprop; lifted only a little, as the burst throws them out, not up). The lightest go no faster than
                    // BurstMost even so, the fastest fragments found of burst vessels: a sliver's 1.6 times the kick of a panel, on top of
                    // the burst's own spread of 1.2, would otherwise throw it at nearly twice that
                    keep = 0.8f;
                    if (spawnBurstSpeed > 0f)
                    {
                        kickUnit = (out_ + Random.insideUnitSphere * 0.35f).normalized * (spawnBurstSpeed * Random.Range(0.6f, 1.2f));
                        kickMost = Mathf.Max(BurstLeast, BurstMost);
                        v = v * keep + Vector3.ClampMagnitude(kickUnit * kickScale, kickMost) + up * Random.Range(0f, 2f);
                    }
                    else
                        v = v * keep + (out_ + Random.insideUnitSphere * 0.35f).normalized * Random.Range(6f, 20f) * (0.7f + 0.3f * spawnHeat) + up * Random.Range(2f, 8f);
                    break;
                case Torn.Cut:
                    v += Random.insideUnitSphere * 0.8f + spawnCutKick;
                    break;
                default:
                    // (broken up: thrown out from the part's middle, the harder the lighter the piece, and no longer lifted as it goes)
                    kickUnit = (away + Random.insideUnitSphere * 0.5f + (ThrownByMass ? Vector3.zero : up * 0.25f)).normalized
                        * (Mathf.Min(35f, (1.5f + 5f * spawnViolence) * Random.Range(0.4f, 1.1f)) + (spawnHeat > 0f ? Random.Range(2f, 7f) : 0f));
                    v += kickUnit * kickScale;
                    break;
            }
            // (the speed it would have gone on at had nothing thrown it: the share of its speed its cause leaves it with, fanned as it
            // was. What the throwing gave it is the rest, and that, not the speed the ground or the blow took off it on purpose, is what
            // tears it round: a panel thrown off at a few metres a second, not one left behind at 30 m/s of an 80 m/s slide)
            Vector3 carried = had * keep;
            {
                Vector3 r = up * Vector3.Dot(carried, up);
                carried = r + fanTurn * (carried - r);
            }
            // Kept out of the ground, and not thrown into it: what is thrown at the ground skips along it.
            Vector3 groundUp = Vector3.zero;
            bool skipped = false;
            if (Physics.Raycast(at + up * 4f, -up, out RaycastHit ground, 8f + extent.magnitude, 1 << 15, QueryTriggerInteraction.Ignore))
            {
                groundUp = ground.normal;
                float above = Vector3.Dot(at - ground.point, ground.normal), need = 0.5f * Mathf.Max(0.06f, Mathf.Min(extent.x, Mathf.Min(extent.y, extent.z))) + 0.05f;
                if (above < need) at += ground.normal * (need - above);
                float down = Vector3.Dot(v, ground.normal);
                if (down < 0f)
                {
                    float hit = -down;
                    skipped = true;
                    v -= ground.normal * down;
                    v += ground.normal * hit * Random.Range(0.04f, 0.25f);
                    // (and skips on along the ground the way it was going, give or take SkidFan degrees; only if it was going nowhere
                    // along the ground, off some random way)
                    Vector3 flat = Vector3.ProjectOnPlane(v, ground.normal);
                    if (ThrownByMass && flat.sqrMagnitude > 0.25f) flat = Quaternion.AngleAxis(Random.Range(-SkidFan, SkidFan), ground.normal) * flat;
                    else
                    {
                        flat = Vector3.ProjectOnPlane(Random.onUnitSphere, ground.normal);
#if DEV
                        skidRandom = true;
#endif
                    }
                    if (flat.sqrMagnitude > 1e-4f) v += flat.normalized * hit * Random.Range(0.1f, 0.4f);
#if DEV
                    skidded = true;
#endif
                }
            }
            Vector3 spin;
            if (ThrownByMass)
            {
                // It turns as the part it came off was turning, and as being torn off turns it: about the speed the tearing gave it over
                // its own length. What is torn right through comes away whole, and besides its part's turning only rocks a little, as before.
                Vector3 kept = (prb != null ? prb.angularVelocity : (w != null ? w.body.W : Vector3.zero)) * SpinKept;
                float tearing = why == Torn.Cut ? Random.Range(0.2f, 1.5f)
                    : Mathf.Clamp((v - carried).magnitude / Mathf.Max(0.1f, extent.magnitude), TearSpinLeast, Mathf.Max(TearSpinLeast, TearSpinMost)) * Random.Range(0.4f, 1f);
                spin = kept + Random.onUnitSphere * tearing;
#if DEV
                float size = extent.magnitude;
                float turns = Mathf.Min(40f, spin.magnitude);
                if (size < 0.5f) { ThrownSmall++; ThrownSpinSmall += turns; ThrownKeptSmall += kept.magnitude; }
                else if (size > 1f) { ThrownLarge++; ThrownSpinLarge += turns; ThrownKeptLarge += kept.magnitude; }
                else { ThrownMiddle++; ThrownSpinMiddle += turns; }
#endif
            }
            else spin = Random.onUnitSphere * (why == Torn.Cut ? Random.Range(0.2f, 1.5f) : Random.Range(2f, 9f) * (0.6f + 0.4f * spawnViolence));
#if DEV
            ThrownMade++;
            ThrownKickLeast = Mathf.Min(ThrownKickLeast, kickScale); ThrownKickMost = Mathf.Max(ThrownKickMost, kickScale); ThrownKickSum += kickScale;
            ThrownDvSum += (v - carried).magnitude;
            if (why == Torn.Crushed && ThrownByMass) { ThrownFanned++; ThrownFanSum += Mathf.Abs(fanned); ThrownFanMost = Mathf.Max(ThrownFanMost, Mathf.Abs(fanned)); }
            if (skidded) { ThrownSkids++; if (skidRandom) ThrownSkidsRandom++; }
#endif
            float heat = spawnHeat > 0f ? Random.Range(0.15f, 0.5f) * spawnHeat : 0f;
            float burning = Random.value < spawnBurning ? Random.Range(3f, 10f) : 0f;
            t = Prof.Now;
            // (handed over in the game's frame again, as the pieces' bodies move in it: see Piece.Step. Of the part's own stuff, steel or
            // aluminium sheet, for how the water holds it up: not by its mass over its area, which a crumpled piece of sheet, carrying
            // three times its outline's metal, would have taken for steel, see Fold)
            Piece piece = Piece.Make(mesh, mats, tints, at, turn, v + basis, spin, mass, area, heat, burning, tint, tint == 1, h.part, Solid(h) ? Piece.Steel : Piece.Aluminium);
            Prof.Add(Prof.PieceMake, t);
            Made++;
            // (kept for the metal of the panels in no piece to be folded into it, see Fold)
            callPieces.Add(piece); callBasis.Add(basis); callKick.Add(kickUnit); callKickMost.Add(kickMost); callGround.Add(groundUp); callSkid.Add(skipped);
            callFlat.Add(piece.mass); callWhy.Add(why);
            // (and kept by its part, for what the part may owe later to be given to it: see Settle)
            if (KeepMass && CarryOwed) h.Keep(piece);
#if DEV
            RecordOf(h).kg.Add(piece.mass * 1000f);
#endif
            // ---- the smoke it trails: every piece some (see Say): a wisp off one torn out of the ground or crushed, smoke off one that came out of a fire
            float now = Time.time;
            bool hot = heat > 0f || burnt || burning > 0f;
            piece.smoke = hot ? Random.Range(0.25f, 0.6f) : fuelled ? Random.Range(0.12f, 0.25f) : Random.Range(0.05f, 0.12f);
            piece.smokeUntil = now + (hot ? Random.Range(8f, 16f) : Random.Range(2.5f, 5f));
            piece.shade = hot ? Random.Range(0.05f, 0.14f) : h.paved ? Random.Range(0.4f, 0.55f) : Random.Range(0.3f, 0.45f);
            piece.warm = hot ? 0.3f : h.paved ? 0.15f : 0.7f;
            return piece;
        }

#if DEV
        /// <summary>(development build) What one part's skin came to as it was torn off, since last cleared (see Tests.Breakup): how hard it last
        /// broke up, the mass of each piece made of it (with what was folded into it, see Fold), the area of the panels that went to
        /// bits instead, the metal lost as fine debris with them (and of that, what the ground ground away), and what no piece could take
        /// (kilograms), split by why: the pieces made were full (lostFullKg), none was made with the pieces about at their most
        /// (lostCapKg), or none was made for any other reason (lostNoneKg); and whether the part went altogether. (Since 2026-11-04, see
        /// CarryOwed) what it owed given later to its pieces made before (settledKg: in none of the masses of kg, which are as each was
        /// made), and what it still owes (owedKg: a part torn once and never again, or never gone, keeps it).</summary>
        internal sealed class BrokeUp
        {
            public Hits h;
            public string title;
            public float q, built, bitsArea, fineKg, lostKg;
            public float groundKg, lostNoneKg, lostFullKg, lostCapKg;
            public float settledKg, owedKg;
            public bool gone;
            public readonly List<float> kg = new List<float>();
        }

        internal static readonly List<BrokeUp> BrokeUps = new List<BrokeUp>();

        // (so many parts' records are kept, the oldest let go past it, and how many were let go since last cleared: Tests.Breakup lists only
        // the latest sixty-four, but sums all those kept, so that its "in all" is the whole crash's and not its last sixty-four parts'. Until
        // 2026-11-04 only sixty-four were kept, and the sums, over a crash tearing hundreds, were of its last parts alone, without a word)
        internal const int BrokeUpsKept = 1024;
        internal static int BrokeUpsLetGo;

        // (the last group of panels made into pieces: of which part, by what, how hard, how many panels and how much area, how many pieces it
        // was to be and how many were made, their areas largest first, how many groups stopped short of a ribbon or with nowhere to grow,
        // and how many panels none took; and how many times panels were grouped since last cleared)
        internal static string BreakLastPart = "";
        internal static Torn BreakLastWhy;
        internal static float BreakLastQ, BreakLastArea;
        internal static int BreakLastPanels, BreakLastTargets, BreakLastMade, BreakLastStringy, BreakLastBlocked, BreakLastLeft, BreakGrouped;
        internal static readonly float[] BreakLastAreas = new float[MostClusters];

        static BrokeUp RecordOf(Hits h)
        {
            for (int k = BrokeUps.Count - 1; k >= 0; k--) if (ReferenceEquals(BrokeUps[k].h, h)) return BrokeUps[k];
            var r = new BrokeUp { h = h, title = Name(h), built = FullMass(h) * 1000f };
            // (only the latest BrokeUpsKept parts are kept, and those let go counted: a big crash tears hundreds, and a long test more. One
            // let go and torn again later has a record anew, and its mass as built is in the sums twice; under BrokeUpsKept it never is)
            if (BrokeUps.Count >= BrokeUpsKept) { BrokeUps.RemoveAt(0); BrokeUpsLetGo++; }
            BrokeUps.Add(r);
            return r;
        }

        /// <summary>(development build) What has been torn off a part by Tests.Tear becomes pieces and bits, as in a crash.</summary>
        public void TestSpawn(Hits h, Hull hull)
        {
            spawnFrom = h.part.transform.position;
            Spawn(null, h, hull, true);
        }

        /// <summary>(development build) A part split open as in a crash, its hole on one side (where the test tore it): it leaks, alight or
        /// not, and (since this is no crash) never goes up, so that what runs out and what it does on the ground can be looked at; or, given
        /// a time not below 0, alight and going up after that many seconds, so that a burst can be looked at (see Tests.Burst).</summary>
        public string TestLeak(Hits h, Vector3 hole, bool lit, float upAfter = -1f)
        {
            h.touches = 1; h.point = hole;
            float now = Time.time;
            StartLeak(null, h, now);
            if (leaks.Count == 0) return "no leak began";
            Leak leak = leaks[leaks.Count - 1];
            bool up = upAfter >= 0f;
            leak.lit = lit || up;
            leak.until = up ? now + upAfter : float.MaxValue;
            return "leaking " + leak.had.ToString("F0") + " kg" + (leak.lit ? ", alight" : "") + (up ? ", going up in " + upAfter.ToString("F1") + " s" : "");
        }
#endif

        // ================================================================== a part whose skin cannot be read as panels: its old way

        /// <summary>Break a part's skin into pieces and set them going. How many, and how big, goes with how big the part is and how hard it was hit.</summary>
        int Fragments(Wreck w, Hits h, float violence, bool burns, bool solid)
        {
            Part p = h.part;
            burningShare = solid ? 0.85f : 0.4f;
            int room = Piece.Most + 10 - Piece.Count;
            if (room <= 0) return 0;
            if (!Shatter.Gather(p, skin, 4000)) return 0;
            float size = skin.size;
            int count = Mathf.Clamp(Mathf.RoundToInt((1.5f + 1.6f * size) * (0.6f + 0.25f * Mathf.Min(violence, 3f))), 2, 12);
            float slow = Mathf.Clamp01((slowFrames - 1f / 30f) / (1f / 15f - 1f / 30f));
            count = Mathf.Max(2, Mathf.RoundToInt(count * (1f - 0.65f * slow)));
            count = Mathf.Min(count, room);
            float biggest = Mathf.Clamp(size * 0.5f / (1f + 0.25f * violence), 0.25f, 5f);
            float smallest = Mathf.Max(0.15f, biggest * 0.18f);
            Shatter.Split(skin, Mathf.Clamp(biggest * 0.3f, 0.1f, 1.2f), 7000);
            List<Shatter.Cell> cells = Shatter.Cut(skin, count, biggest, smallest);
            return Launch(w, h, cells, violence, burns, count, p.mass);
        }

        int Launch(Wreck w, Hits h, List<Shatter.Cell> cells, float violence, bool burns, int count, float massOfAll)
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
                float soot = burns ? Random.Range(0.25f, 0.6f) : Random.Range(0.7f, 0.95f);
                for (int m = 0; m < painted.Length; m++)
                {
                    painted[m] = skin.materials[Shatter.usedMaterials[m]];
                    Color c = skin.colours[Shatter.usedMaterials[m]];
                    tints[m] = new Color(c.r * soot, c.g * soot * 0.98f, c.b * soot * 0.95f, c.a);
                }
                Vector3 at = cell.middle;
                Vector3 v = prb != null ? prb.GetPointVelocity(at) : (w != null ? w.body.At(at) : Vector3.zero);
                Vector3 away = at - skin.middle;
                away = away.sqrMagnitude > 1e-4f ? away.normalized : Random.onUnitSphere;
                float kick = Mathf.Min(35f, (1.5f + 5f * violence) * Random.Range(0.4f, 1.1f)) + (burns ? Random.Range(2f, 7f) : 0f);
                v += (away + Random.insideUnitSphere * 0.5f + up * 0.25f).normalized * kick;
                if (Physics.Raycast(at + up * 4f, -up, out RaycastHit ground, 8f + extent.magnitude, 1 << 15, QueryTriggerInteraction.Ignore))
                {
                    float above = Vector3.Dot(at - ground.point, ground.normal), need = 0.5f * Mathf.Max(0.06f, extent.y) + 0.05f;
                    if (above < need) at += ground.normal * (need - above);
                    float down = Vector3.Dot(v, ground.normal);
                    if (down < 0f) { v -= ground.normal * down * 1.1f; }
                }
                Vector3 spin = Random.onUnitSphere * Random.Range(2f, 9f) * (0.6f + 0.4f * violence);
                float heat = burns ? Random.Range(0.15f, 0.5f) : 0f;
                float burning = burns && Random.value < burningShare ? Random.Range(3f, 10f) : 0f;
                float mass = Mathf.Max(0.003f, massOfAll * cell.area / total);
                Piece piece = Piece.Make(mesh, painted, tints, at, turn, v, spin, mass, cell.area, heat, burning, tint, tint == 1, p);
                piece.smoke = burns ? Random.Range(0.25f, 0.5f) : Random.Range(0.05f, 0.12f);
                piece.smokeUntil = Time.time + (burns ? Random.Range(8f, 14f) : Random.Range(2.5f, 5f));
                piece.shade = burns ? 0.1f : 0.4f;
                piece.warm = 0.3f;
                made++;
                Made++;
            }
            return made;
        }
    }
}
