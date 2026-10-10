#if DEV
using System;
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
            Crashes.Scrape = Num(words, "scrape", Crashes.Scrape);
            Crashes.GrazeFriction = Num(words, "graze", Crashes.GrazeFriction);
            Crashes.FeedDust = Num(words, "dust", Crashes.FeedDust ? 1 : 0) > 0.5f;
            Crashes.FeedLeaks = Num(words, "leaks", Crashes.FeedLeaks ? 1 : 0) > 0.5f;
            Crashes.FeedMovers = Num(words, "movers", Crashes.FeedMovers ? 1 : 0) > 0.5f;
            Crashes.FeedBurning = Num(words, "burning", Crashes.FeedBurning ? 1 : 0) > 0.5f;
            Crashes.FeedFire = Num(words, "fire", Crashes.FeedFire ? 1 : 0) > 0.5f;
            Piece.ShadowsFrom = Num(words, "shadows", Piece.ShadowsFrom);
            Piece.MostOverride = (int)Num(words, "most", Piece.MostOverride);
            return Figures();
        }

        public static string Figures() =>
            "restitution " + Crashes.Restitution + ", toughness " + Crashes.Toughness + ", bite " + Crashes.Bite + "/" + Crashes.BitePaved + ", friction " + Crashes.Friction + "/" + Crashes.FrictionPaved +
            ", plough " + Crashes.Plough + "/" + Crashes.PloughPaved + ", scrape " + Crashes.Scrape + ", graze " + Crashes.GrazeFriction +
            "; feeding dust " + Crashes.FeedDust + ", leaks " + Crashes.FeedLeaks + ", movers " + Crashes.FeedMovers + ", burning " + Crashes.FeedBurning + ", fire " + Crashes.FeedFire +
            "; shadows from " + Piece.ShadowsFrom + " m, most " + Piece.Most;

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
