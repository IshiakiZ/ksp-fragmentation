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
            touchKnown = false;
            return "will throw at the next step: " + asked;
        }

        internal static void Pending()
        {
            Tracking();
            KeepMark();
            FirstTouch();
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
                // (over the sea, so high over the water)
                if (body.ocean && ground < 0.0) ground = Water.Height(moved);
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

        /// <summary>(The properties of shaders by name, "|" between names: for choosing what a mark is drawn with.)</summary>
        public static string ShaderSays(string names)
        {
            var say = new StringBuilder();
            foreach (string name in (names ?? "").Split('|'))
            {
                Shader sh = Shader.Find(name.Trim());
                say.Append(name.Trim()).Append(": ");
                if (sh == null) { say.Append("none\n"); continue; }
                say.Append("queue ").Append(sh.renderQueue).Append(sh.isSupported ? "" : " (unsupported)").Append("; ");
                for (int i = 0; i < sh.GetPropertyCount(); i++) say.Append(sh.GetPropertyName(i)).Append(" ").Append(sh.GetPropertyType(i)).Append(", ");
                say.Append("\n");
            }
            return say.ToString();
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

        static string asBuilt;

        static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);

        /// <summary>Every figure Set changes, as it is now, in the words Set takes.</summary>
        static string Words() =>
            "restitution=" + F(Crashes.Restitution) + " toughness=" + F(Crashes.Toughness) + " bite=" + F(Crashes.Bite) + " bitepaved=" + F(Crashes.BitePaved) + " friction=" + F(Crashes.Friction) +
            " frictionpaved=" + F(Crashes.FrictionPaved) + " plough=" + F(Crashes.Plough) + " ploughpaved=" + F(Crashes.PloughPaved) + " graze=" + F(Crashes.GrazeFriction) +
            " dust=" + (Crashes.FeedDust ? 1 : 0) + " leaks=" + (Crashes.FeedLeaks ? 1 : 0) + " movers=" + (Crashes.FeedMovers ? 1 : 0) + " trails=" + (Crashes.FeedTrails ? 1 : 0) + " fire=" + (Crashes.FeedFire ? 1 : 0) +
            " ride=" + (Crashes.RideFires ? 1 : 0) + " gouge=" + F(Crashes.Gouge) + " dig=" + F(Crashes.Dig) + " scoop=" + F(Crashes.Scoop) + " anchor=" + F(Crashes.Anchor) + " rolling=" + F(Crashes.Rolling) +
            " grindsoil=" + F(Crashes.GrindSoil) + " grindpaved=" + F(Crashes.GrindPaved) + " skin=" + F(Crashes.SkinShare) + " steel=" + F(Crashes.FrictionSteel) + " gougesteel=" + F(Crashes.GougeSteel) +
            " catch=" + F(Crashes.CatchPaved) + " wearpaved=" + F(Crashes.WearPaved) + " wearsoil=" + F(Crashes.WearSoil) + " vault=" + F(Crashes.Vault) + " hold=" + (Crashes.HoldWrecks ? 1 : 0) + " rolls=" + (Crashes.BrakeRolling ? 1 : 0) + " strikes=" + (Crashes.GripStrikes ? 1 : 0) +
            " hops=" + (Crashes.BridgeHops ? 1 : 0) + " spinlost=" + F(Crashes.SpinLost) + " rollplough=" + F(Crashes.RollPlough) + " lawn=" + (Crashes.TellLawn ? 1 : 0) + " ground=" + Crashes.TestGround +
            " shadows=" + F(Piece.ShadowsFrom) + " bits=" + (Bits.TestNone ? 0 : 1) + " modules=" + (Crashes.TestQuiet ? 0 : 1) + " most=" + Piece.MostOverride;

        /// <summary>
        /// Change the figures crashes are worked out by: "restitution=0.1 toughness=2 bite=0.4 friction=0.5 plough=0.2 catch=0.2 wearpaved=0.5".
        /// "reset": every one as this build has it (whatever was tried since it was swapped in). Not settings: nothing is written.
        /// </summary>
        public static string Set(string words)
        {
            if (asBuilt == null) asBuilt = Words();
            if ((words ?? "").Trim().ToLowerInvariant() == "reset") words = asBuilt;
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
            Crashes.FrictionSteel = Num(words, "steel", Crashes.FrictionSteel);
            Crashes.GougeSteel = Num(words, "gougesteel", Crashes.GougeSteel);
            Crashes.CatchPaved = Num(words, "catch", Crashes.CatchPaved);
            Crashes.WearPaved = Num(words, "wearpaved", Crashes.WearPaved);
            Crashes.WearSoil = Num(words, "wearsoil", Crashes.WearSoil);
            Crashes.Vault = Num(words, "vault", Crashes.Vault);
            Crashes.HoldWrecks = Num(words, "hold", Crashes.HoldWrecks ? 1 : 0) > 0.5f;
            Crashes.BrakeRolling = Num(words, "rolls", Crashes.BrakeRolling ? 1 : 0) > 0.5f;
            Crashes.GripStrikes = Num(words, "strikes", Crashes.GripStrikes ? 1 : 0) > 0.5f;
            Crashes.BridgeHops = Num(words, "hops", Crashes.BridgeHops ? 1 : 0) > 0.5f;
            Crashes.SpinLost = Num(words, "spinlost", Crashes.SpinLost);
            Crashes.RollPlough = Num(words, "rollplough", Crashes.RollPlough);
            Crashes.TellLawn = Num(words, "lawn", Crashes.TellLawn ? 1 : 0) > 0.5f;
            Crashes.TestGround = (int)Num(words, "ground", Crashes.TestGround);
            Piece.ShadowsFrom = Num(words, "shadows", Piece.ShadowsFrom);
            Bits.TestNone = Num(words, "bits", Bits.TestNone ? 0 : 1) < 0.5f;
            Crashes.TestQuiet = Num(words, "modules", Crashes.TestQuiet ? 0 : 1) < 0.5f;
            Piece.MostOverride = (int)Num(words, "most", Piece.MostOverride);
            return Figures();
        }

        public static string Figures() =>
            "restitution " + Crashes.Restitution + ", toughness " + Crashes.Toughness + ", bite " + Crashes.Bite + "/" + Crashes.BitePaved + ", friction " + Crashes.Friction + "/" + Crashes.FrictionPaved +
            ", plough " + Crashes.Plough + "/" + Crashes.PloughPaved + ", dig " + Crashes.Dig + ", scoop " + Crashes.Scoop + ", anchor " + Crashes.Anchor + ", rolling " + Crashes.Rolling + ", gouge " + Crashes.Gouge + ", grind " + Crashes.GrindSoil + "/" + Crashes.GrindPaved + ", skin " + Crashes.SkinShare + ", graze " + Crashes.GrazeFriction +
            ", steel " + Crashes.FrictionSteel + "/" + Crashes.GougeSteel + ", catch " + Crashes.CatchPaved + ", wear " + Crashes.WearPaved + "/" + Crashes.WearSoil + ", vault " + Crashes.Vault +
            (Crashes.HoldWrecks ? "" : ", wrecks not held") + (Crashes.BrakeRolling ? "" : ", rolling not braked") + (Crashes.GripStrikes ? "" : ", strikes not gripped") + (Crashes.BridgeHops ? "" : ", hops not bridged") + ", spin lost " + Crashes.SpinLost + ", rolling ploughs " + Crashes.RollPlough + (Crashes.TellLawn ? "" : ", lawns as paving") + (Crashes.TestGround >= 0 ? ", every ground " + (Ground.Kind)Crashes.TestGround : "") +
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
            // (and how high its middle stands over the craft's lowest point, for pointing a camera at it: Photo Studio's floor is there)
            Vector3 upNow = FlightGlobals.getUpAxis(v.mainBody, mid);
            float lowest = float.MaxValue;
            // (by the parts' solid shapes: what is drawn under them, an engine's flame say, reaches far lower)
            foreach (Part q in v.parts)
                if (q != null)
                    foreach (Collider c in q.GetComponentsInChildren<Collider>())
                        if (c != null && c.enabled && !c.isTrigger && c.gameObject.activeInHierarchy)
                        {
                            Bounds b = c.bounds;
                            lowest = Mathf.Min(lowest, Vector3.Dot(b.center, upNow) - (Mathf.Abs(b.extents.x * upNow.x) + Mathf.Abs(b.extents.y * upNow.y) + Mathf.Abs(b.extents.z * upNow.z)));
                        }
            return p.partInfo.title + ": " + removed + " panels torn off, " + (hull.Left * 100f).ToString("F0") + "% of its skin left; its middle " + (Vector3.Dot(mid, upNow) - lowest).ToString("F2") + " m over the craft's lowest point";
        }

        static float GrindDepth(Hull hull) => hull.GroundDepth;

        /// <summary>
        /// (Since 0.3.0.) A tank of the craft being flown split open as in a crash, without one: "part=FL-T400 side=1 lit=0". It leaks from the
        /// side given (its middle's height), alight or not, and never goes up: what runs out stains the ground under it (and burns there if
        /// alight). With "say" only: the stains there are.
        /// </summary>
        /// <summary>
        /// (Since 0.3.0.) The marks on the ground through a save and a load without one: "keep" writes them as a save would (a
        /// development build is never part of the game's own save); "clear" takes them away; "again" reads what was kept and lays it
        /// again (once the ground is there); "say" counts them.
        /// </summary>
        public static string Marked(string words)
        {
            string w = (words ?? "").Trim();
            if (w == "keep") { kept = new ConfigNode("SCENARIO"); Marks.Save(kept); Kept.SaveHulls(kept); return "kept: " + (kept.GetNode("MARKS") != null ? kept.GetNode("MARKS").CountNodes + " marks, " : "no marks, ") + kept.GetNodes("HULL").Length + " torn parts, " + kept.ToString().Length + " characters"; }
            if (w == "clear") { Marks.Clear(); return "cleared"; }
            if (w == "mend")
            {
                int mended = 0;
                if (FlightGlobals.ActiveVessel != null)
                    foreach (Part p in FlightGlobals.ActiveVessel.parts) { Hull h = Hull.Of(p, false); if (h != null) { h.Mend(); mended++; } }
                return mended + " parts mended";
            }
            if (w == "again") { if (kept == null) return "nothing kept"; Marks.Load(kept); Kept.LoadHulls(kept); return "to be laid again"; }
            return Marks.Count + " marks; " + Kept.Said;
        }

        static ConfigNode kept;

        public static string Leak(string words)
        {
            try
            {
                if ((words ?? "").Trim() == "say") return Marks.StainsSaid();
                Vessel v = FlightGlobals.ActiveVessel;
                if (v == null || Crashes.Instance == null) return "nothing is being flown";
                string want = Word(words ?? "", "part");
                Part p = null;
                // ("low=1": of the parts like it, the one lowest on the craft, nearest the ground; else the first)
                bool low = Num(words, "low", 0f) > 0.5f;
                Vector3 up = FlightGlobals.getUpAxis(v.mainBody, v.CoM);
                foreach (Part q in v.parts)
                    if (q != null && q.partInfo != null && (want.Length == 0 || q.partInfo.title.IndexOf(want, StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        if (!low) { p = q; break; }
                        if (p == null || Vector3.Dot(q.transform.position - p.transform.position, up) < 0f) p = q;
                    }
                if (p == null) return "no part like '" + want + "'";
                Hits h = p.GetComponent<Hits>() ?? p.gameObject.AddComponent<Hits>();
                h.part = p;
                float side = Num(words, "side", 1f);
                HullPlan plan = HullPlan.For(p);
                float half = plan != null && plan.ready ? 0.5f * (plan.hi.x - plan.lo.x) : 0.6f;
                Vector3 hole = p.transform.position + p.transform.right * (side * half);
                return p.partInfo.title + ": " + Crashes.Instance.TestLeak(h, hole, Num(words, "lit", 0f) > 0.5f);
            }
            catch (Exception ex) { return "failed: " + ex.Message; }
        }

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

        /// <summary>What the space centre's ground masks say under the craft being flown (see Ground.Masks).</summary>
        public static string Masks()
        {
            Vessel v = FlightGlobals.ActiveVessel;
            if (v == null) return "nothing is being flown";
            return Ground.Masks(v.CoM, (Vector3)FlightGlobals.getUpAxis(v.mainBody, v.CoMD));
        }

        /// <summary>
        /// A scar laid on the ground by the craft being flown, without a crash, to look at: "length=40 width=2.4 deep=0.25 east=20 north=0 heading=90"
        /// (it begins so far east and north of the craft and runs along that heading; "paved=1": a scrape).
        /// </summary>
        public static string Scar(string words)
        {
            Vessel v = FlightGlobals.ActiveVessel;
            if (v == null) return "nothing is being flown";
            words = words ?? "";
            CelestialBody body = v.mainBody;
            Vector3 up = (Vector3)FlightGlobals.getUpAxis(body, v.CoMD);
            Vector3 east = Vector3.Cross(up, (Vector3)body.transform.up).normalized, north = Vector3.Cross(east, up).normalized;
            float heading = Num(words, "heading", 90f) * Mathf.Deg2Rad;
            Vector3 way = north * Mathf.Cos(heading) + east * Mathf.Sin(heading);
            Vector3 from = v.CoM + east * Num(words, "east", 20f) + north * Num(words, "north", 0f);
            // (on the ground there, however high the craft is)
            if (Physics.Raycast(from + up * 100f, -up, out RaycastHit hit, 3000f, 1 << 15, QueryTriggerInteraction.Ignore)) from = hit.point + up * 0.5f;
            Material[] mats = Marks.Materials();
            return Marks.Lay(from, way, up, Num(words, "length", 40f), Num(words, "width", 2.4f), Num(words, "deep", 0.25f), Num(words, "paved", 0f) > 0.5f)
                   + "; the earth is drawn with " + (mats[0] != null && mats[0].shader != null ? mats[0].shader.name : "nothing");
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
                // (the mark goes only once the camera is off it: the flight camera hangs on what it watches, and a flight whose camera's
                // mark was destroyed under it is broken until the game is started again)
                if (flown == null) return "nothing is being flown: the camera stays where it is";
                FlightCamera.SetTarget(flown);
                markBody = null;
                if (lookMark != null) UnityEngine.Object.Destroy(lookMark);
                return "the camera on the craft flown";
            }
            Transform on = null;
            Vector3 at = Vector3.zero;
            string what = "";
            lookWay = Vector3.zero; lookUp = Vector3.zero; markBody = null; aimKnown = false;
            if (w.StartsWith("scar") || w.StartsWith("scrape"))
            {
                // ("scar 0.4": so far along the longest scar on soil; "scrape 0.4": along the longest scrape on paving. The mark is an
                // object of its own, kept where it is on the planet: nothing it could be fixed to outlasts a reload)
                string[] bits = w.Split(' ');
                float share = bits.Length > 1 && float.TryParse(bits[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ? f : 0.5f;
                if (!Marks.Along(w.StartsWith("scrape"), share, out at, out lookWay, out lookUp, out what)) return "no " + bits[0] + " to look at";
                if (lookMark == null) lookMark = new GameObject("Fragmentation camera mark");
                lookMark.transform.SetParent(null, false);
                lookMark.transform.position = at;
                markBody = FlightGlobals.currentMainBody;
                if (markBody != null) { markLat = markBody.GetLatitude(at); markLon = markBody.GetLongitude(at); markAlt = markBody.GetAltitude(at); }
                // (and the place itself, kept apart from the mark: the mark is moved about until the camera's pivot is on the place, see Eye)
                aimLat = markLat; aimLon = markLon; aimAlt = markAlt; aimKnown = markBody != null;
                FlightCamera.SetTarget(lookMark.transform);
                return "the camera on " + what;
            }
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
                    // ("engine": the heaviest craft near that has an engine among its parts and no tank)
                    if (w.StartsWith("engine") && !EngineOnly(o)) continue;
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

        static Vector3 lookWay, lookUp;
        static CelestialBody markBody;
        static double markLat, markLon, markAlt;
        static double aimLat, aimLon, aimAlt;
        static bool aimKnown;

        static bool EngineOnly(Vessel v)
        {
            bool engine = false;
            foreach (Part p in v.parts)
            {
                if (p == null) continue;
                if (Crashes.Propellant(p) > 100.0) return false;
                foreach (PartModule m in p.Modules) if (m is ModuleEngines) engine = true;
            }
            return engine;
        }

        /// <summary>(every step) a mark on a scar stays where it is on the planet as the scene is shifted about.</summary>
        static void KeepMark()
        {
            if (lookMark == null || markBody == null || lookMark.transform.parent != null) return;
            lookMark.transform.position = markBody.GetWorldSurfacePosition(markLat, markLon, markAlt);
        }

        /// <summary>
        /// Put the camera at a place given from what it was last pointed at (see Look): "along=-4 across=1 up=1.6" is so many metres along the
        /// way a scar runs there (or north, for anything else), across it (to the right, seen along it; or east), and up.
        /// </summary>
        public static string Eye(string words)
        {
            FlightCamera cam = FlightCamera.fetch;
            if (cam == null || lookMark == null) return "nothing is being looked at (see Look)";
            CelestialBody body = FlightGlobals.currentMainBody;
            if (body == null) return "no body";
            Vector3 mark = lookMark.transform.position;
            // (the place meant. With the craft flown in the air the camera's pivot settles some metres from what it is put on, 6.9 m
            // when this was written: the mark is moved, a part of the way at each asking, until the pivot is on the place; ask again
            // until the distance said is small)
            Vector3 aim = aimKnown && markBody != null ? (Vector3)markBody.GetWorldSurfacePosition(aimLat, aimLon, aimAlt) : mark;
            Vector3 off = cam.GetPivot().position - aim;
            if (aimKnown && markBody != null && off.magnitude > 0.2f && off.magnitude < 80f)
            {
                Vector3 moved = mark - off * 0.7f;
                markLat = markBody.GetLatitude(moved); markLon = markBody.GetLongitude(moved); markAlt = markBody.GetAltitude(moved);
                lookMark.transform.position = moved;
            }
            Vector3 up = lookUp.sqrMagnitude > 0.5f ? lookUp : (Vector3)FlightGlobals.getUpAxis(body, aim);
            Vector3 way = Vector3.ProjectOnPlane(lookWay.sqrMagnitude > 0.5f ? lookWay : (Vector3)body.transform.up, up).normalized;
            Vector3 right = Vector3.Cross(up, way).normalized;
            Vector3 eye = aim + way * Num(words ?? "", "along", 0f) + right * Num(words ?? "", "across", 0f) + up * Num(words ?? "", "up", 1.6f);
            cam.SetCamCoordsFromPosition(eye);
            return "the camera " + (eye - cam.GetPivot().position).magnitude.ToString("F1") + " m from what it looks at (its pivot is " + off.magnitude.ToString("F1") + " m from the mark)";
        }

        /// <summary>
        /// Cut a part of the craft being flown loose from it and fly that instead: "part=Swivel" (the first part whose title has those letters).
        /// For throwing one part by itself at the ground (an engine, say): Loose, then Throw.
        /// </summary>
        public static string Loose(string words)
        {
            Vessel v = FlightGlobals.ActiveVessel;
            if (v == null) return "nothing is being flown";
            string want = Word(words ?? "", "part");
            Part p = null;
            foreach (Part q in v.parts) if (q != null && q.partInfo != null && q.partInfo.title.IndexOf(want, StringComparison.OrdinalIgnoreCase) >= 0) { p = q; break; }
            if (p == null) return "no part like '" + want + "'";
            if (p.parent == null) return p.partInfo.title + " is the craft's first part: nothing to cut it from";
            try
            {
                p.decouple();
                if (p.vessel == null || p.vessel == v) return "it did not come away";
                FlightGlobals.ForceSetActiveVessel(p.vessel);
                // (and the rest of the craft taken away, with no explosion: it is the loose part that is being looked at)
                if (Num(words ?? "", "keep", 0f) < 0.5f) v.Die();
            }
            catch (Exception ex) { return "failed: " + ex.Message; }
            return p.partInfo.title + " is loose, a craft of " + p.vessel.parts.Count + " parts and " + p.vessel.totalMass.ToString("F2") + " t, and is the one flown";
        }

        /// <summary>Where everything is: each craft near, what it is, how far it lies from where the first of them touched after the last throw, and how fast it is going.</summary>
        public static string Where()
        {
            Vessel flown = FlightGlobals.ActiveVessel;
            var say = new StringBuilder();
            if (!touchKnown || touchBody == null) say.Append("(nothing has touched since the last throw: distances are from where the track began)\n");
            CelestialBody body = touchKnown ? touchBody : trackBody;
            if (body == null) return "no throw yet";
            double lat = touchKnown ? touchLat : trackLat, lon = touchKnown ? touchLon : trackLon;
            foreach (Vessel o in FlightGlobals.VesselsLoaded)
            {
                if (o == null || o.vesselType == VesselType.SpaceObject || o.vesselType == VesselType.Flag || o.isEVA || o.parts == null || o.parts.Count == 0) continue;
                double far = (o.CoMD - body.GetWorldSurfacePosition(lat, lon, o.altitude)).magnitude;
                if (far > 5000) continue;
                Part heaviest = null;
                foreach (Part p in o.parts) if (p != null && (heaviest == null || p.mass + p.GetResourceMass() > heaviest.mass + heaviest.GetResourceMass())) heaviest = p;
                say.Append(far.ToString("F0")).Append(" m: ").Append(o.GetDisplayName()).Append(o == flown ? " (flown)" : "").Append(", ").Append(heaviest != null && heaviest.partInfo != null ? heaviest.partInfo.title : "?")
                   .Append(o.parts.Count > 1 ? " and " + (o.parts.Count - 1) + " more" : "").Append(", ").Append(o.totalMass.ToString("F2")).Append(" t, ").Append(o.srf_velocity.magnitude.ToString("F1")).Append(" m/s, ")
                   .Append(o.radarAltitude.ToString("F1")).Append(" m up\n");
            }
            float farthest = 0f; int pieces = 0;
            foreach (Piece pc in Piece.All)
            {
                if (pc == null) continue;
                pieces++;
                farthest = Mathf.Max(farthest, (float)((Vector3d)pc.transform.position - body.GetWorldSurfacePosition(lat, lon, body.GetAltitude(pc.transform.position))).magnitude);
            }
            say.Append(pieces).Append(" pieces, the farthest ").Append(farthest.ToString("F0")).Append(" m; ").Append(Bits.Count).Append(" bits");
            return say.ToString();
        }

        static CelestialBody touchBody;
        static double touchLat, touchLon;
        static bool touchKnown;

        /// <summary>(every step) where the first craft to touch anything after a throw touched it.</summary>
        static void FirstTouch()
        {
            if (touchKnown || Crashes.Instance == null) return;
            foreach (Wreck w in Crashes.Instance.Wrecks)
            {
                if (w == null || !w.inContact || w.vessel == null || w.vessel.mainBody == null) continue;
                touchBody = w.vessel.mainBody;
                touchLat = touchBody.GetLatitude(w.vessel.CoMD); touchLon = touchBody.GetLongitude(w.vessel.CoMD);
                touchKnown = true;
                return;
            }
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
