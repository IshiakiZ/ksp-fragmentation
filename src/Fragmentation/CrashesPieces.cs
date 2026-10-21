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
        readonly Shatter.Skin skin = new Shatter.Skin();
        float burningShare = 0.4f;
        readonly List<int> cellList = new List<int>();

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
        /// makes one piece. Returns how many pieces were made.
        /// </summary>
        int Spawn(Wreck w, Hits h, Hull hull, bool all = false)
        {
            long began = Prof.Now;
            try { return SpawnNow(w, h, hull, all); }
            finally { Prof.Add(Prof.Spawn, began); }
        }

        int SpawnNow(Wreck w, Hits h, Hull hull, bool all)
        {
            if (hull.removed.Count == 0) return 0;
            Part p = h.part;
            Rigidbody prb = BodyOf(p);
            CelestialBody body = p.vessel != null ? p.vessel.mainBody : FlightGlobals.currentMainBody;
            Vector3 middle = p.transform.position;
            Vector3 up = body != null ? (Vector3)FlightGlobals.getUpAxis(body, middle) : Vector3.up;
            byte tint = (byte)(Propellant(p, true) > Propellant(p) * 0.5 && Propellant(p) > 1.0 ? 1 : 0);
            float full = p.partInfo != null && p.partInfo.partPrefab != null ? p.partInfo.partPrefab.mass : Mathf.Max(0.01f, p.mass);
            float perArea = Mathf.Max(0.0005f, full) / Mathf.Max(0.05f, hull.plan.area);
            bool fuelled = Propellant(p) > 20.0;
            bool foil = false;
            for (int k = 0; k < p.Modules.Count && !foil; k++) foil = p.Modules[k] is ModuleCommand && p.CrewCapacity == 0 || p.Modules[k] is ModuleScienceExperiment || p.Modules[k] is ModuleDeployableSolarPanel;
            // (when the frames are already slow, fewer pieces and more bits: down to a third by fifteen frames a second. Space Engineers
            // drops its effects when its time is up, for the same reason: see the Research page)
            float slow = Mathf.Clamp01((slowFrames - 1f / 30f) / (1f / 15f - 1f / 30f));
            int made = 0;
            var cut = new List<int>();
            // (so many pieces at most from one part at one time: a fin breaking up is a handful of pieces and a shower of bits, not twenty pieces)
            int most = all ? Mathf.Clamp(Mathf.RoundToInt(hull.plan.area * 1.2f), 2, 12) : 6;
            // (and so long making them: two milliseconds, four for a part going altogether; but always one piece, however late in the step it is,
            // so that a part never breaks up into nothing but bits because others broke up before it in the same step)
            long until = System.Diagnostics.Stopwatch.GetTimestamp() + System.Diagnostics.Stopwatch.Frequency * (all ? 4 : 2) / 1000;
            foreach (KeyValuePair<int, Torn> r in hull.removed)
            {
                int ci = r.Key;
                Torn why = r.Value;
                if (why == Torn.Cut) { cut.Add(ci); continue; }
                HullPlan.Cell cell = hull.plan.cells[ci];
                Vector3 at = hull.WorldMiddle(ci);
                Vector3 v = prb != null ? prb.GetPointVelocity(at) : (w != null ? w.body.At(at) : Vector3.zero);
                // ---- a piece, or not
                float chance = why == Torn.Ground ? 0.3f : why == Torn.Crushed ? 0.65f : why == Torn.Blown ? 0.85f : cell.area > 0.4f * hull.plan.edge * hull.plan.edge ? 0.9f : 0.5f;
                chance *= 1f - 0.65f * slow;
                bool big = cell.area > 0.025f || cell.whole;
                bool piece = big && (cell.whole || Random.value < chance) && made < most && Piece.Count < Piece.Most + 10 && (made == 0 || System.Diagnostics.Stopwatch.GetTimestamp() < until);
                if (piece)
                {
                    cellList.Clear();
                    cellList.Add(ci);
                    if (MakePiece(w, h, hull, cellList, why, prb, up, tint, perArea, fuelled) != null) made++;
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
                        // (left behind on the ground as the wreck goes on: thrown up a little, and soon still)
                        bv = v * Random.Range(0.1f, 0.45f) + up * 2f;
                        spread = 2.5f; smoke = fuelled || hull.Burnt(ci) ? 0.25f : 0.07f;
                        kind = Random.value < 0.45f ? Bits.Kind.Paint : Bits.Kind.Metal;
                        break;
                    case Torn.Blown:
                        n = Mathf.Clamp(Mathf.RoundToInt(cell.area * 16f), 2, 10);
                        Vector3 away = (at - spawnFrom).sqrMagnitude > 1e-4f ? (at - spawnFrom).normalized : Random.onUnitSphere;
                        bv = v * 0.8f + away * Random.Range(4f, 14f) + up * 3f;
                        spread = 9f; burning = 0.35f; smoke = 0.5f;
                        kind = Random.value < 0.5f ? Bits.Kind.Burnt : Bits.Kind.Metal;
                        break;
                    case Torn.Crushed:
                        n = Mathf.Clamp(Mathf.RoundToInt(cell.area * 12f), 1, 7);
                        bv = v * Random.Range(0.6f, 0.9f) + up * 2.5f;
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
                Bits.Emit(at, bv, spread, n, scale * 0.15f, scale, kind, burning, smoke, why == Torn.Ground ? 1.5f : 3f);
                // (a spacecraft's skin is lagged with foil blankets: shreds of it among the bits)
                if (foil) Bits.Emit(at, bv, spread * 1.3f, Mathf.Max(1, n / 2), scale * 0.3f, scale * 1.5f, Bits.Kind.Foil, 0f, 0f, 2f);
                if (Random.value < 0.35f) Bits.Emit(at, bv, spread, Mathf.Max(1, n / 4), 0.012f, 0.04f, Bits.Kind.Bolt, 0f, 0f);
            }
            // ---- what came away as one: each group of panels joined to one another, one piece
            while (cut.Count > 0)
            {
                cellList.Clear();
                var stack = new Stack<int>();
                stack.Push(cut[cut.Count - 1]);
                cut.RemoveAt(cut.Count - 1);
                while (stack.Count > 0)
                {
                    int c = stack.Pop();
                    cellList.Add(c);
                    foreach (int o in hull.plan.cells[c].near) { int k = cut.IndexOf(o); if (k >= 0) { cut.RemoveAt(k); stack.Push(o); } }
                }
                if (MakePiece(w, h, hull, cellList, Torn.Cut, prb, up, tint, perArea, fuelled) != null) made++;
            }
            hull.removed.Clear();
            return made;
        }

        /// <summary>One piece of a part's skin, made of some of its panels, set going the way what tore it off would throw it.</summary>
        Piece MakePiece(Wreck w, Hits h, Hull hull, List<int> cells, Torn why, Rigidbody prb, Vector3 up, byte tint, float perArea, bool fuelled)
        {
            bool whole = cells.Count == 1 && hull.Whole(cells[0]);
            bool burnt = false;
            foreach (int c in cells) if (hull.Burnt(c)) burnt = true;
            float crumple = why == Torn.Crushed ? Random.Range(0.6f, 1f) : why == Torn.Ground ? Random.Range(0.7f, 1f) : why == Torn.Blown ? Random.Range(0.35f, 0.9f) : why == Torn.Cut ? Random.Range(0f, 0.15f) : Random.Range(0.2f, 0.6f);
            // (an engine's bell, or anything else that comes away whole, is dented rather than folded)
            if (whole) crumple *= 0.25f;
            float soot = burnt || spawnHeat > 0f ? Random.Range(0.45f, 0.85f) : why == Torn.Ground ? Random.Range(0.15f, 0.35f) : Random.Range(0f, 0.12f);
            long t = Prof.Now;
            Mesh mesh = hull.PieceOf(cells, crumple, soot, out Vector3 at, out Quaternion turn, out Vector3 extent, out float area, out Material[] mats, out Color[] tints);
            Prof.Add(Prof.PieceMesh, t);
            if (mesh == null) return null;
            Vector3 v = prb != null ? prb.GetPointVelocity(at) : (w != null ? w.body.At(at) : Vector3.zero);
            Vector3 away = at - h.part.transform.position;
            away = away.sqrMagnitude > 1e-4f ? away.normalized : Random.onUnitSphere;
            switch (why)
            {
                case Torn.Ground:
                    // (torn off by the ground as the wreck goes over it: left behind)
                    v = v * Random.Range(0.25f, 0.6f) + up * Random.Range(0.5f, 3f) + Random.insideUnitSphere * 1.5f;
                    break;
                case Torn.Crushed:
                    v = v * Random.Range(0.7f, 0.95f) + away * Random.Range(2f, 7f) + up * Random.Range(1f, 4f);
                    break;
                case Torn.Blown:
                    Vector3 out_ = at - spawnFrom;
                    out_ = out_.sqrMagnitude > 1e-4f ? out_.normalized : away;
                    v = v * 0.8f + (out_ + Random.insideUnitSphere * 0.35f).normalized * Random.Range(6f, 20f) * (0.7f + 0.3f * spawnHeat) + up * Random.Range(2f, 8f);
                    break;
                case Torn.Cut:
                    v += Random.insideUnitSphere * 0.8f + spawnCutKick;
                    break;
                default:
                    v += (away + Random.insideUnitSphere * 0.5f + up * 0.25f).normalized * (Mathf.Min(35f, (1.5f + 5f * spawnViolence) * Random.Range(0.4f, 1.1f)) + (spawnHeat > 0f ? Random.Range(2f, 7f) : 0f));
                    break;
            }
            // Kept out of the ground, and not thrown into it: what is thrown at the ground skips along it.
            if (Physics.Raycast(at + up * 4f, -up, out RaycastHit ground, 8f + extent.magnitude, 1 << 15, QueryTriggerInteraction.Ignore))
            {
                float above = Vector3.Dot(at - ground.point, ground.normal), need = 0.5f * Mathf.Max(0.06f, Mathf.Min(extent.x, Mathf.Min(extent.y, extent.z))) + 0.05f;
                if (above < need) at += ground.normal * (need - above);
                float down = Vector3.Dot(v, ground.normal);
                if (down < 0f)
                {
                    float hit = -down;
                    v -= ground.normal * down;
                    v += ground.normal * hit * Random.Range(0.04f, 0.25f);
                    Vector3 flat = Vector3.ProjectOnPlane(Random.onUnitSphere, ground.normal);
                    if (flat.sqrMagnitude > 1e-4f) v += flat.normalized * hit * Random.Range(0.1f, 0.4f);
                }
            }
            Vector3 spin = Random.onUnitSphere * (why == Torn.Cut ? Random.Range(0.2f, 1.5f) : Random.Range(2f, 9f) * (0.6f + 0.4f * spawnViolence));
            float heat = spawnHeat > 0f ? Random.Range(0.15f, 0.5f) * spawnHeat : 0f;
            float burning = Random.value < spawnBurning ? Random.Range(3f, 10f) : 0f;
            float mass = Mathf.Max(0.003f, perArea * area);
            t = Prof.Now;
            Piece piece = Piece.Make(mesh, mats, tints, at, turn, v, spin, mass, area, heat, burning, tint, tint == 1, h.part);
            Prof.Add(Prof.PieceMake, t);
            Made++;
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
        /// <summary>(development build) What has been torn off a part by Tests.Tear becomes pieces and bits, as in a crash.</summary>
        public void TestSpawn(Hits h, Hull hull)
        {
            spawnFrom = h.part.transform.position;
            Spawn(null, h, hull, true);
        }

        /// <summary>(development build) A part split open as in a crash, its hole on one side (where the test tore it): it leaks, alight or
        /// not, and (since this is no crash) never goes up, so that what runs out and what it does on the ground can be looked at.</summary>
        public string TestLeak(Hits h, Vector3 hole, bool lit)
        {
            h.touches = 1; h.point = hole;
            float now = Time.time;
            StartLeak(null, h, now);
            if (leaks.Count == 0) return "no leak began";
            Leak leak = leaks[leaks.Count - 1];
            leak.lit = lit;
            leak.until = float.MaxValue;
            return "leaking " + leak.had.ToString("F0") + " kg" + (lit ? ", alight" : "");
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
