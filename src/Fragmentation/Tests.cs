#if DEV
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace Fragmentation
{
    /// <summary>
    /// For the development build: setting up crashes and reading what they did, from the shell
    /// (tools/rpc dev_call '{"type":"Fragmentation.Tests","method":"Throw","arg":"speed=100 descent=12"}').
    /// </summary>
    public static class Tests
    {
        static string asked;
        static float askedAt;

        /// <summary>
        /// Throw the craft being flown at the ground: words such as "speed=100 descent=12 heading=90 pitch=-12 height=30
        /// back=0 spin=0". The craft is put so high above the ground where it is, moved back along its heading by "back"
        /// metres, turned so that its long axis points along "pitch" degrees above the horizon (nose first; "pitch=90" is
        /// standing up, "side=1" lays it on its side across its way), and set going at "speed" along a path "descent"
        /// degrees below the horizon. Done at the next step of the physics in which the craft is off the rails.
        /// </summary>
        public static string Throw(string words)
        {
            asked = words ?? "";
            askedAt = Time.time;
            return "will throw at the next step: " + asked;
        }

        internal static void Pending()
        {
            Tracking();
            PendingGraze();
            WatchGraze();
            if (asked == null) return;
            if (Time.time - askedAt > 30f) { asked = null; return; }
            Vessel v = FlightGlobals.ActiveVessel;
            if (v == null || v.packed || !v.loaded || v.rootPart == null || v.rootPart.rb == null) return;
            string words = asked;
            asked = null;
            float speed = Num(words, "speed", 80f), descent = Num(words, "descent", 12f) * Mathf.Deg2Rad, heading = Num(words, "heading", 90f) * Mathf.Deg2Rad;
            float pitch = Num(words, "pitch", -Num(words, "descent", 12f)) * Mathf.Deg2Rad, height = Num(words, "height", -1f), back = Num(words, "back", 0f), spin = Num(words, "spin", 0f);
            bool side = Num(words, "side", 0f) > 0.5f;
            CelestialBody body = v.mainBody;
            Vector3d at = v.transform.position;
            Vector3 up = (Vector3)FlightGlobals.getUpAxis(body, at);
            Vector3 east = Vector3.Cross(up, (Vector3)body.transform.up).normalized;        // (Unity's axes are left-handed: this is east)
            if (Vector3.Dot(east, (Vector3)(body.GetWorldSurfacePosition(body.GetLatitude(at), body.GetLongitude(at) + 0.01, 0) - body.GetWorldSurfacePosition(body.GetLatitude(at), body.GetLongitude(at), 0))) < 0f) east = -east;
            Vector3 north = Vector3.Cross(east, up).normalized;
            if (Vector3.Dot(north, (Vector3)body.transform.up) < 0f) north = -north;
            Vector3 level = north * Mathf.Cos(heading) + east * Mathf.Sin(heading);
            Vector3 way = level * Mathf.Cos(descent) - up * Mathf.Sin(descent);
            Vector3 axis = side ? Vector3.Cross(level, up).normalized : (level * Mathf.Cos(pitch) + up * Mathf.Sin(pitch)).normalized;
            Vector3 face = Vector3.ProjectOnPlane(up, axis);
            if (face.sqrMagnitude < 1e-4f) face = Vector3.ProjectOnPlane(level, axis);
            Quaternion turn = Quaternion.LookRotation(face.normalized, axis);
            v.SetRotation(turn);
            Vector3d moved = v.transform.position;
            if (back != 0f) moved -= (Vector3d)(level * back);
            if (height >= 0f)
            {
                double ground = body.TerrainAltitude(body.GetLatitude(moved), body.GetLongitude(moved), true);
                double alt = body.GetAltitude(moved);
                moved += (Vector3d)up * (ground + height - alt);
            }
            v.SetPosition(moved, true);
            Vector3 frame = (Vector3)Krakensbane.GetFrameVelocity();
            foreach (Part p in v.parts)
            {
                if (p == null || p.rb == null) continue;
                p.rb.velocity = way * speed - frame;
                p.rb.angularVelocity = spin != 0f ? level * spin : Vector3.zero;
            }
            v.IgnoreGForces(20);
            // ("fuel=0.1": tanks a tenth full, for crashes whose pieces are not hidden by their own fire)
            float fuel = Num(words, "fuel", -1f);
            if (fuel >= 0f)
                foreach (Part p in v.parts)
                    if (p != null && p.Resources != null)
                        foreach (PartResource r in p.Resources) if (r != null && r.resourceName != "ElectricCharge") r.amount = r.maxAmount * Mathf.Clamp01(fuel);
            Crashes.Note("thrown: " + speed.ToString("F0") + " m/s, " + (descent * Mathf.Rad2Deg).ToString("F0") + " degrees down, heading " + (heading * Mathf.Rad2Deg).ToString("F0") + ", " + v.radarAltitude.ToString("F0") + " m up");
        }

        static string grazing;
        static float grazingAt;

        /// <summary>
        /// Set the craft being flown drifting into the nearest other craft: "speed=6 miss=2 from=40" (metres a second
        /// towards it, metres to one side of its middle that it is aimed at, metres away it starts). Both must be loaded.
        /// </summary>
        public static string Graze(string words)
        {
            grazing = words ?? "";
            grazingAt = Time.time;
            return "will set it going at the next step: " + grazing;
        }

        static void PendingGraze()
        {
            if (grazing == null) return;
            if (Time.time - grazingAt > 30f) { grazing = null; return; }
            Vessel v = FlightGlobals.ActiveVessel;
            if (v == null || v.packed || v.rootPart == null || v.rootPart.rb == null) return;
            Vessel target = null;
            double best = double.MaxValue;
            foreach (Vessel o in FlightGlobals.VesselsLoaded)
            {
                if (o == null || o == v || o.vesselType == VesselType.SpaceObject || o.vesselType == VesselType.Flag || o.rootPart == null) continue;
                double d = (o.CoMD - v.CoMD).sqrMagnitude;
                if (d < best) { best = d; target = o; }
            }
            if (target == null || target.packed || target.rootPart.rb == null) return;
            string words = grazing;
            grazing = null;
            float speed = Num(words, "speed", 6f), miss = Num(words, "miss", 2f), from = Num(words, "from", 40f);
            Vector3 there = target.CoM, here = v.CoM;
            Vector3 way = (here - there).sqrMagnitude > 1f ? (here - there).normalized : Vector3.up;
            Vector3 side = Vector3.Cross(way, Mathf.Abs(Vector3.Dot(way, Vector3.up)) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 start = there + way * from;
            v.SetPosition((Vector3d)v.transform.position + (Vector3d)(start - here), false);
            Vector3 aim = there + side * miss;
            Vector3 towards = (aim - start).normalized;
            Vector3 theirs = target.rootPart.rb.velocity;
            foreach (Part p in v.parts)
            {
                if (p == null || p.rb == null) continue;
                p.rb.velocity = theirs + towards * speed;
                p.rb.angularVelocity = Vector3.zero;
            }
            Crashes.Note("set " + v.GetDisplayName() + " going at " + target.GetDisplayName() + ": " + speed.ToString("F1") + " m/s from " + from.ToString("F0") + " m, aimed " + miss.ToString("F1") + " m to one side of its middle");
            grazer = v; grazed = target; grazeUntil = Time.time + 12f; closest = float.MaxValue; closestAt = 0f; grazeFrom = Time.time;
        }

        static Vessel grazer, grazed;
        static float grazeUntil, closest = float.MaxValue, closestAt, grazeFrom;

        /// <summary>(how near the two craft's nearest parts came, while a graze is being watched)</summary>
        static void WatchGraze()
        {
            if (grazer == null || grazed == null || Time.time > grazeUntil) return;
            foreach (Part a in grazer.parts)
                foreach (Part b in grazed.parts)
                {
                    if (a == null || b == null) continue;
                    float d = (a.transform.position - b.transform.position).magnitude;
                    if (d < closest) { closest = d; closestAt = Time.time - grazeFrom; }
                }
        }

        public static string Graze() => grazer == null ? "no graze" : "the nearest parts of the two came within " + closest.ToString("F2") + " m of each other's middles, " + closestAt.ToString("F2") + " s after it was set going";

        static float Num(string words, string key, float otherwise)
        {
            foreach (string word in words.Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = word.IndexOf('=');
                if (eq > 0 && word.Substring(0, eq).Equals(key, StringComparison.OrdinalIgnoreCase) && float.TryParse(word.Substring(eq + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out float value)) return value;
            }
            return otherwise;
        }

        /// <summary>Change the figures crashes are worked out by: "restitution=0.1 toughness=2 bite=0.4 friction=0.5 plough=0.2 scrape=25".</summary>
        public static string Set(string words)
        {
            Crashes.Restitution = Num(words, "restitution", Crashes.Restitution);
            Crashes.Toughness = Num(words, "toughness", Crashes.Toughness);
            Crashes.Bite = Num(words, "bite", Crashes.Bite);
            Crashes.BitePaved = Num(words, "bitepaved", Crashes.BitePaved);
            Crashes.Friction = Num(words, "friction", Crashes.Friction);
            Crashes.FrictionPaved = Num(words, "frictionpaved", Crashes.FrictionPaved);
            Crashes.Plough = Num(words, "plough", Crashes.Plough);
            Crashes.PloughPaved = Num(words, "ploughpaved", Crashes.PloughPaved);
            Crashes.GrazeFriction = Num(words, "graze", Crashes.GrazeFriction);
            Crashes.FeedDust = Num(words, "dust", Crashes.FeedDust ? 1 : 0) > 0.5f;
            Crashes.FeedLeaks = Num(words, "leaks", Crashes.FeedLeaks ? 1 : 0) > 0.5f;
            Crashes.FeedMovers = Num(words, "movers", Crashes.FeedMovers ? 1 : 0) > 0.5f;
            Crashes.FeedTrails = Num(words, "trails", Crashes.FeedTrails ? 1 : 0) > 0.5f;
            Crashes.FeedFire = Num(words, "fire", Crashes.FeedFire ? 1 : 0) > 0.5f;
            Crashes.RideFires = Num(words, "ride", Crashes.RideFires ? 1 : 0) > 0.5f;
            Crashes.Gouge = Num(words, "gouge", Crashes.Gouge);
            Crashes.Dig = Num(words, "dig", Crashes.Dig);
            Crashes.Scoop = Num(words, "scoop", Crashes.Scoop);
            Crashes.Anchor = Num(words, "anchor", Crashes.Anchor);
            Crashes.Rolling = Num(words, "rolling", Crashes.Rolling);
            Crashes.GrindSoil = Num(words, "grindsoil", Crashes.GrindSoil);
            Crashes.GrindPaved = Num(words, "grindpaved", Crashes.GrindPaved);
            Crashes.SkinShare = Num(words, "skin", Crashes.SkinShare);
            Piece.ShadowsFrom = Num(words, "shadows", Piece.ShadowsFrom);
            Bits.TestNone = Num(words, "bits", Bits.TestNone ? 0 : 1) < 0.5f;
            Crashes.TestQuiet = Num(words, "modules", Crashes.TestQuiet ? 0 : 1) < 0.5f;
            Piece.MostOverride = (int)Num(words, "most", Piece.MostOverride);
            return Figures();
        }

        public static string Figures() =>
            "restitution " + Crashes.Restitution + ", toughness " + Crashes.Toughness + ", bite " + Crashes.Bite + "/" + Crashes.BitePaved + ", friction " + Crashes.Friction + "/" + Crashes.FrictionPaved +
            ", plough " + Crashes.Plough + "/" + Crashes.PloughPaved + ", dig " + Crashes.Dig + ", scoop " + Crashes.Scoop + ", anchor " + Crashes.Anchor + ", rolling " + Crashes.Rolling + ", gouge " + Crashes.Gouge + ", grind " + Crashes.GrindSoil + "/" + Crashes.GrindPaved + ", skin " + Crashes.SkinShare + ", graze " + Crashes.GrazeFriction +
            "; feeding dust " + Crashes.FeedDust + ", leaks " + Crashes.FeedLeaks + ", movers " + Crashes.FeedMovers + ", trails " + Crashes.FeedTrails + ", fire " + Crashes.FeedFire +
            "; shadows from " + Piece.ShadowsFrom + " m, most " + Piece.Most + "; bits " + !Bits.TestNone + ", wrecks' modules " + !Crashes.TestQuiet;

        static int costFrame;
        static float costTime;

        /// <summary>What the mod's own work on the game's thread has cost since this was last asked, a frame; and the frame rate.</summary>
        public static string Cost()
        {
            int frames = Mathf.Max(1, Time.frameCount - costFrame);
            float seconds = Time.realtimeSinceStartup - costTime;
            double ms = 1000.0 / System.Diagnostics.Stopwatch.Frequency / frames;
            string say = frames + " frames in " + seconds.ToString("F2") + " s (" + (frames / Mathf.Max(0.01f, seconds)).ToString("F1") + " a second): each step's work " + (Crashes.FixedTicks * ms).ToString("F2") +
                         " ms a frame (of which breaking up " + (Crashes.BreakTicks * ms).ToString("F2") + "), telling the explosions mod " + (Crashes.AskedTicks * ms).ToString("F2") + "; " + Piece.Count + " pieces";
            Crashes.FixedTicks = Crashes.AskedTicks = Crashes.BreakTicks = 0;
            costFrame = Time.frameCount; costTime = Time.realtimeSinceStartup;
            return say;
        }

        public static string Surfaces() => string.Join(" | ", Crashes.Surfaces.ToArray());

        public static string Layers() => Fragmentation.Layers.Table();

        /// <summary>"off": the mod stands aside (crashes as the game has them, for comparing); "on": back. Not a setting: nothing is written.</summary>
        public static string Switch(string words)
        {
            Crashes.TestOff = (words ?? "").Trim().ToLowerInvariant() == "off";
            return Crashes.TestOff ? "standing aside" : "on";
        }

        /// <summary>What is going on: the craft being watched, the parts that are hurt, the pieces, and what happened lately.</summary>
        public static string Stats()
        {
            var say = new StringBuilder(Crashes.Report).Append('\n');
            Crashes c = Crashes.Instance;
            if (c != null)
                foreach (Wreck w in c.Wrecks)
                {
                    Vessel v = w.vessel;
                    if (v == null) continue;
                    say.Append(w.name).Append(": ").Append(v.parts.Count).Append(" parts, ").Append(v.srf_velocity.magnitude.ToString("F1")).Append(" m/s, ").Append(v.radarAltitude.ToString("F1")).Append(" m up, slid ")
                       .Append(w.slid.ToString("F0")).Append(" m, hardest ").Append(w.hardest.ToString("F0")).Append(" m/s").Append(w.inContact ? ", on the ground" : "").Append('\n');
                    foreach (Hits h in w.hits)
                        if (h != null && h.damage > 0.01f && h.part != null)
                            say.Append("  ").Append(h.part.partInfo != null ? h.part.partInfo.title : h.part.name).Append(": hurt ").Append(h.damage.ToString("F2")).Append(h.leaking ? ", leaking" : "").Append(h.gone ? ", gone" : "").Append('\n');
                }
            int moving = 0, resting = 0, burning = 0;
            foreach (Piece p in Piece.All) { if (p == null) continue; if (p.resting) resting++; else moving++; if (p.burns > 0f) burning++; }
            say.Append("pieces: ").Append(moving).Append(" moving, ").Append(resting).Append(" at rest, ").Append(burning).Append(" burning\n");
            say.Append(Figures()).Append('\n');
            say.Append("heard: ground ").Append(Hits.HeardGround).Append(", craft ").Append(Hits.HeardCraft).Append(", its own craft ").Append(Hits.HeardSameCraft).Append(", other things ").Append(Hits.HeardOther)
               .Append(", pieces ").Append(Hits.HeardPieces).Append(", while not armed ").Append(Hits.HeardUnarmed).Append("; last not ground: ").Append(Hits.LastOther).Append('\n');
            say.Append(Crashes.Notes(25));
            return say.ToString();
        }

        public static string Notes() => Crashes.Notes(60);

        /// <summary>
        /// How long the explosions mod takes to begin a patch of air (its work on the game's thread), and to put it away: "count=3 height=40"
        /// patches begun in turn so high over the craft being flown, each timed, and each put away again at once.
        /// </summary>
        public static string SiteCost(string words)
        {
            Vessel v = FlightGlobals.ActiveVessel;
            if (v == null) return "nothing is being flown";
            int count = (int)Num(words ?? "", "count", 3f);
            float height = Num(words ?? "", "height", 40f);
            var say = new StringBuilder();
            double ms = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            Vector3 up = (Vector3)FlightGlobals.getUpAxis(v.mainBody, v.CoMD);
            for (int k = 0; k < count; k++)
            {
                Vector3d at = v.CoMD + (Vector3d)(up * height) + (Vector3d)(Vector3.Cross(up, Vector3.forward).normalized * (60f * k));
                long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                VolumetricExplosions.Plan plan = VolumetricExplosions.Plan.Where(at);
                long t1 = System.Diagnostics.Stopwatch.GetTimestamp();
                var site = new VolumetricExplosions.Site(plan);
                long t2 = System.Diagnostics.Stopwatch.GetTimestamp();
                site.Dispose();
                long t3 = System.Diagnostics.Stopwatch.GetTimestamp();
                say.Append("patch ").Append(k).Append(": the plan ").Append(((t1 - t0) * ms).ToString("F1")).Append(" ms, begun in ").Append(((t2 - t1) * ms).ToString("F1")).Append(" ms, put away in ").Append(((t3 - t2) * ms).ToString("F1")).Append(" ms\n");
            }
            return say.ToString();
        }

        /// <summary>
        /// Tear a part of the craft being flown without a crash, to look at it: "part=FL-T800 grind=0.5 crush=0.3 dent=0.15 blow=0.4 side=1 pieces=1".
        /// The part is the first whose title has those letters in it. From one side of it (across its long axis, "side=1" or the other way
        /// with -1; "end=1" along it from the top, -1 from the bottom): grind so many metres away, crush it so many metres in, dent it so
        /// deep; blow it open leaving that share of its skin. With "pieces=1" what comes off becomes pieces and bits (otherwise it is only gone).
        /// </summary>
        public static string Tear(string words)
        {
            try { return TearNow(words); }
            catch (Exception ex) { return "failed: " + ex; }
        }

        static string TearNow(string words)
        {
            Vessel v = FlightGlobals.ActiveVessel;
            if (v == null) return "nothing is being flown";
            string want = Word(words ?? "", "part");
            Part p = null;
            foreach (Part q in v.parts) if (q != null && q.partInfo != null && (want.Length == 0 || q.partInfo.title.IndexOf(want, StringComparison.OrdinalIgnoreCase) >= 0)) { p = q; break; }
            if (p == null) return "no part like '" + want + "'";
            HullPlan plan = HullPlan.For(p);
            if (plan == null) return "nothing of " + p.partInfo.title + " can be read";
            if (!plan.ready) return "its skin is still being cut (" + (plan.failed ? "failed: " + plan.problem : "try again in a moment") + ")";
            Hull hull = Hull.Of(p);
            if (hull == null) return "no hull";
            Hits h = p.GetComponent<Hits>() ?? p.gameObject.AddComponent<Hits>();
            h.part = p; h.plan = plan; h.hull = hull;
            float end = Num(words, "end", 0f), side = Num(words, "side", 1f);
            Vector3 axis = p.transform.up, across = p.transform.right;
            Vector3 way = end != 0f ? axis * -Mathf.Sign(end) : across * -Mathf.Sign(side);       // (into the part, from the side it is torn from)
            Vector3 mid = p.transform.TransformPoint(plan.mid);
            float half = 0.5f * Vector3.Dot(plan.hi - plan.lo, new Vector3(Mathf.Abs(p.transform.InverseTransformDirection(way).x), Mathf.Abs(p.transform.InverseTransformDirection(way).y), Mathf.Abs(p.transform.InverseTransformDirection(way).z)));
            Vector3 face = mid - way * half;                                                       // the face it is torn from
            float grind = Num(words, "grind", 0f), crush = Num(words, "crush", 0f), dent = Num(words, "dent", 0f), blow = Num(words, "blow", -1f);
            hull.Show();
            if (dent > 0f) hull.DentAt(face, way, dent, Mathf.Max(0.3f, half));
            if (crush > 0f) hull.CrushAt(face, way, crush);
            if (grind > 0f) { for (int k = 0; k < 200 && hull.Grind(face, way, 1e6f, 1f) >= 0; k++) { if (GrindDepth(hull) >= grind) break; } }
            if (blow >= 0f) hull.BlowOff(face + way * 0.2f, blow, (Vector3)FlightGlobals.getUpAxis(v.mainBody, mid));
            int removed = hull.removed.Count;
            if (Num(words, "pieces", 0f) > 0.5f && Crashes.Instance != null) Crashes.Instance.TestSpawn(h, hull);
            else hull.removed.Clear();
            return p.partInfo.title + ": " + removed + " panels torn off, " + (hull.Left * 100f).ToString("F0") + "% of its skin left";
        }

        static float GrindDepth(Hull hull) => hull.GroundDepth;

        static string Word(string words, string key)
        {
            foreach (string word in words.Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = word.IndexOf('=');
                if (eq > 0 && word.Substring(0, eq).Equals(key, StringComparison.OrdinalIgnoreCase)) return word.Substring(eq + 1).Replace('_', ' ');
            }
            return "";
        }

        /// <summary>The skins of the parts of the craft being flown: whether each is cut into panels yet, into how many, and how long that took.</summary>
        public static string Hulls() => Hulls("");

        /// <summary>The same, with "all": of every craft near, what is left of each part, how deep it was ground and crushed, how far it slid on what.</summary>
        public static string Hulls(string words)
        {
            Vessel flown = FlightGlobals.ActiveVessel;
            if (flown == null) return "nothing is being flown";
            var say = new StringBuilder();
            var craft = new List<Vessel>();
            if ((words ?? "").Contains("all"))
            {
                foreach (Vessel o in FlightGlobals.VesselsLoaded)
                    if (o != null && o.vesselType != VesselType.SpaceObject && o.vesselType != VesselType.Flag && !o.isEVA && (o.CoMD - flown.CoMD).magnitude < 3000) craft.Add(o);
            }
            else craft.Add(flown);
            foreach (Vessel v in craft)
            {
                if (craft.Count > 1) say.Append("== ").Append(v.GetDisplayName()).Append(v == flown ? " (flown)" : "").Append(": ").Append(v.parts.Count).Append(" parts, ").Append(v.totalMass.ToString("F2")).Append(" t, ").Append(v.srf_velocity.magnitude.ToString("F1")).Append(" m/s\n");
                foreach (Part p in v.parts) HullLine(say, p);
            }
            say.Append("plans cut so far ").Append(HullPlan.Built).Append(", failed ").Append(HullPlan.Failed).Append("; skins redrawn ").Append(Hull.Rebuilds).Append(" times, ")
               .Append((Hull.RebuildTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency / Mathf.Max(1, Hull.Rebuilds)).ToString("F2")).Append(" ms each (corners ").Append((Hull.ShapeTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency / Mathf.Max(1, Hull.Rebuilds)).ToString("F2"))
               .Append(", lists ").Append((Hull.ListTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency / Mathf.Max(1, Hull.Rebuilds)).ToString("F2"))
               .Append(", handing over ").Append((Hull.UploadTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency / Mathf.Max(1, Hull.Rebuilds)).ToString("F2")).Append(")")
               .Append("; bits ").Append(Bits.Count).Append(", marks ").Append(Marks.Count).Append(", pieces ").Append(Piece.Count);
            return say.ToString();
        }

        static void HullLine(StringBuilder say, Part p)
        {
            {
                if (p == null) return;
                Hits hits = p.GetComponent<Hits>();
                // (the plan cut when the part was armed; read afresh only if there is none: once its skin is drawn by this mod its own meshes are hidden)
                HullPlan plan = hits != null && hits.plan != null ? hits.plan : HullPlan.For(p);
                say.Append("  ").Append(p.partInfo != null ? p.partInfo.title : p.name).Append(": ");
                if (plan == null) say.Append("nothing to read");
                else if (plan.failed) say.Append("failed (").Append(plan.problem).Append(")");
                else if (!plan.ready) say.Append("being cut");
                else say.Append(plan.cells.Length).Append(" panels of ").Append(plan.p.Length / 3).Append(" triangles, ").Append(plan.area.ToString("F1")).Append(" m2, panels ").Append(plan.edge.ToString("F2")).Append(" m, cut in ").Append(plan.tookMs.ToString("F0")).Append(" ms").Append(plan.engine ? " (an engine: its meshes whole)" : "");
                Hull hull = p.GetComponent<Hull>();
                if (hull != null) say.Append("; torn: ").Append((hull.Left * 100f).ToString("F0")).Append("% left").Append(hull.shown ? ", drawn" : "").Append(hull.boxed ? ", boxed" : "").Append("; ").Append(hull.Damage());
                if (hits != null) say.Append("; hurt ").Append(hits.damage.ToString("F2")).Append(hits.armed ? ", armed" : "").Append(", slid on it ").Append(hits.slidOn.ToString("F0")).Append(" m, grinding ").Append(hits.groundWork.ToString("F1")).Append(" kJ");
                say.Append('\n');
            }
        }

        /// <summary>"start": time every frame from now on (see Clock); "read": what was timed, frame by frame, and the worst frames; "stop".</summary>
        public static string Clock(string words)
        {
            string w = (words ?? "").Trim().ToLowerInvariant();
            if (w == "start") { Fragmentation.Clock.Start(); return "timing every frame"; }
            if (w == "stop") { Fragmentation.Clock.Stop(); return "stopped"; }
            return Fragmentation.Clock.Read(w.Contains("all"));
        }

        /// <summary>
        /// What the ground is at a place ("lat lon", or under the craft being flown): the collider a ray straight down meets, its
        /// tag, layer and object, and the material drawn there; and what this mod takes it for.
        /// </summary>
        public static string Under(string words)
        {
            Vessel v = FlightGlobals.ActiveVessel;
            CelestialBody body = v != null ? v.mainBody : FlightGlobals.currentMainBody;
            if (body == null) return "no body";
            double lat = Num(words ?? "", "lat", float.NaN), lon = Num(words ?? "", "lon", float.NaN);
            if (double.IsNaN(lat) || double.IsNaN(lon)) { if (v == null) return "say lat= lon="; lat = body.GetLatitude(v.CoMD); lon = body.GetLongitude(v.CoMD); }
            double ground = body.TerrainAltitude(lat, lon, true);
            Vector3d top = body.GetWorldSurfacePosition(lat, lon, ground + 300.0);
            Vector3 up = (Vector3)FlightGlobals.getUpAxis(body, top);
            var say = new StringBuilder();
            RaycastHit[] hits = Physics.RaycastAll((Vector3)top, -up, 600f, (1 << 15) | (1 << 0), QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            say.Append("at ").Append(lat.ToString("F5")).Append(", ").Append(lon.ToString("F5")).Append(" (terrain ").Append(ground.ToString("F1")).Append(" m): ").Append(hits.Length).Append(" hits\n");
            foreach (RaycastHit hit in hits)
            {
                Collider c = hit.collider;
                if (c == null) continue;
                say.Append("  ").Append((300.0 - hit.distance).ToString("F2")).Append(" m above the terrain height: ").Append(c.GetType().Name).Append(" '").Append(c.name).Append("' tag ").Append(c.tag).Append(" layer ").Append(c.gameObject.layer);
                Transform t = c.transform;
                string path = t.name;
                for (int k = 0; k < 4 && t.parent != null; k++) { t = t.parent; path = t.name + "/" + path; }
                say.Append(" (").Append(path).Append(")");
                say.Append(" -> ").Append(Crashes.Paved(c) ? "paving" : "soil").Append(", surface '").Append(Ground.Surface(hit)).Append("' ").Append(Ground.Corners(hit)).Append("\n");
            }
            return say.ToString();
        }

        static GameObject lookMark;

        /// <summary>
        /// Point the camera at what a crash left, for pictures from close by: "wreck" (the heaviest craft near, but the one flown), "flown"
        /// (the craft flown, at its middle), "piece" (the heaviest loose piece), "back" (as the game had it). The screenshot then sets how far and from where.
        /// </summary>
        public static string Look(string words)
        {
            string w = (words ?? "").Trim().ToLowerInvariant();
            Vessel flown = FlightGlobals.ActiveVessel;
            if (w == "" || w == "back")
            {
                if (flown != null) FlightCamera.SetTarget(flown);
                if (lookMark != null) UnityEngine.Object.Destroy(lookMark);
                return "the camera on the craft flown";
            }
            Transform on = null;
            Vector3 at = Vector3.zero;
            string what = "";
            if (w.StartsWith("piece"))
            {
                // (the biggest of those at rest; "piece2", "piece3": the next biggest)
                int rank = w.Length > 5 && char.IsDigit(w[5]) ? w[5] - '0' : 1;
                var order = new List<Piece>();
                foreach (Piece pc in Piece.All) if (pc != null && pc.rb != null && pc.gameObject.activeInHierarchy) order.Add(pc);
                order.Sort((x, y) => (y.resting ? 1 : 0).CompareTo(x.resting ? 1 : 0) != 0 ? (y.resting ? 1 : 0).CompareTo(x.resting ? 1 : 0) : y.area.CompareTo(x.area));
                if (order.Count == 0) return "no pieces";
                Piece best = order[Mathf.Clamp(rank - 1, 0, order.Count - 1)];
                on = best.transform; at = best.rb.worldCenterOfMass; what = "a piece of " + best.area.ToString("F2") + " m2, " + (best.rb.mass * 1000f).ToString("F0") + " kg" + (best.resting ? ", at rest" : "");
            }
            else
            {
                Vessel best = null;
                foreach (Vessel o in FlightGlobals.VesselsLoaded)
                {
                    if (o == null || o.vesselType == VesselType.SpaceObject || o.vesselType == VesselType.Flag || o.isEVA || o.rootPart == null) continue;
                    if (w.StartsWith("wreck") && o == flown && FlightGlobals.VesselsLoaded.Count > 1) continue;
                    if (w.StartsWith("flown") && o != flown) continue;
                    if (best == null || o.totalMass > best.totalMass) best = o;
                }
                if (best == null) return "nothing there";
                on = best.rootPart.transform; at = best.CoM; what = best.GetDisplayName() + " (" + best.parts.Count + " parts, " + best.totalMass.ToString("F1") + " t)";
            }
            if (lookMark == null) lookMark = new GameObject("Fragmentation camera mark");
            lookMark.transform.SetParent(on, false);
            lookMark.transform.position = at;
            FlightCamera.SetTarget(lookMark.transform);
            return "the camera on " + what;
        }

        // ---- following the wreck: where the heaviest piece of the craft is, every tenth of a second
        static readonly StringBuilder track = new StringBuilder();
        static Vector3d trackFrom;
        static CelestialBody trackBody;
        static double trackLat, trackLon;
        static float trackAt, trackNext;
        static bool tracking;

        /// <summary>Start (or with "stop", end and read) a record of the heaviest craft near: seconds, metres from where it was when this began (along the ground), speed, height, parts, pieces.</summary>
        public static string Track(string words)
        {
            if ((words ?? "").Trim() == "stop") { tracking = false; return track.ToString(); }
            Vessel v = FlightGlobals.ActiveVessel;
            if (v == null) return "nothing is being flown";
            track.Length = 0;
            trackBody = v.mainBody;
            trackLat = trackBody.GetLatitude(v.CoMD); trackLon = trackBody.GetLongitude(v.CoMD);
            trackAt = Time.fixedTime; trackNext = 0f;
            tracking = true;
            return "tracking";
        }

        internal static void Tracking()
        {
            if (!tracking || Time.fixedTime < trackNext) return;
            trackNext = Time.fixedTime + 0.1f;
            Vessel best = null;
            double mass = 0;
            foreach (Vessel o in FlightGlobals.VesselsLoaded)
            {
                if (o == null || o.packed || o.vesselType == VesselType.SpaceObject || o.vesselType == VesselType.Flag || o.isEVA) continue;
                if (o.totalMass > mass && (trackBody == null || (o.CoMD - trackBody.GetWorldSurfacePosition(trackLat, trackLon, o.altitude)).magnitude < 3000)) { mass = o.totalMass; best = o; }
            }
            if (best == null || trackBody == null) return;
            Vector3d start = trackBody.GetWorldSurfacePosition(trackLat, trackLon, best.altitude);
            double along = (best.CoMD - start).magnitude;
            track.Append((Time.fixedTime - trackAt).ToString("F1")).Append(' ').Append(along.ToString("F1")).Append(" m ").Append(best.srf_velocity.magnitude.ToString("F1")).Append(" m/s ")
                 .Append(best.radarAltitude.ToString("F1")).Append(" m up ").Append(best.parts.Count).Append(" parts ").Append(Piece.Count).Append(" pieces ").Append(best.GetDisplayName()).Append('\n');
            if (track.Length > 60000) tracking = false;
        }
    }
}
#endif
