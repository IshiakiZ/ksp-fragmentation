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
            touchWayKnown = false;
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
            // (the height asked for where it was put: the craft's own height above the ground is still where it was until the next step,
            // and read here said "1 m up" of every throw from the pad, 2026-11-10)
            Crashes.Note("thrown: " + speed.ToString("F0") + " m/s, " + (descent * Mathf.Rad2Deg).ToString("F0") + " degrees down, heading " + (heading * Mathf.Rad2Deg).ToString("F0") + ", "
                         + (height >= 0f ? height : (float)v.radarAltitude).ToString("F0") + " m up" + (back != 0f ? ", put " + Mathf.Abs(back).ToString("F0") + " m " + (back < 0f ? "on" : "back") + " along its heading" : ""));
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
            " shadows=" + F(Piece.ShadowsFrom) + " bits=" + (Fragmentation.Bits.TestNone ? 0 : 1) + " modules=" + (Crashes.TestQuiet ? 0 : 1) + " most=" + Piece.MostOverride + " singular=" + F(Mat3.Singular) + " partedmost=" + F(Crashes.PartedMost) + " partedspin=" + F(Crashes.PartedSpin) +
            " budget=" + (Crashes.EnergyBudget ? 1 : 0) + " latempty=" + F(Crashes.LatEmpty) + " latfull=" + F(Crashes.LatFull) + " sideleast=" + F(Crashes.SideLeast) +
            " louder=" + F(Sounds.LouderBy) + " inwindow=" + Sounds.InWindow + " hitcurve=" + F(Sounds.HitCurve) +
            " breakup=" + (Crashes.BreakUp ? 1 : 0) + " breakslow=" + F(Crashes.BreakSlow) + " breakfast=" + F(Crashes.BreakFast) + " burstbreak=" + F(Crashes.BurstBreak) +
            " biggest0=" + F(Crashes.BreakBiggest0) + " biggest1=" + F(Crashes.BreakBiggest1) + " breakpower=" + F(Crashes.BreakPower) + " breakkept=" + F(Crashes.BreakKept) +
            " stringy=" + F(Crashes.Stringy) + " groupfrom=" + Crashes.ClusterFrom + " breakq=" + F(Crashes.TestBreakQ) +
            " keepmass=" + (Crashes.KeepMass ? 1 : 0) + " fine=" + F(Crashes.FineShare) + " fineground=" + F(Crashes.FineGround) + " finesolid=" + F(Crashes.FineSolid) +
            " densemost=" + F(Crashes.DenseMost) + " keepfrom=" + F(Crashes.KeepFrom) + " carryowed=" + (Crashes.CarryOwed ? 1 : 0) + " densesettle=" + F(Crashes.DenseSettle) +
            " thrown=" + (Crashes.ThrownByMass ? 1 : 0) + " kickpower=" + F(Crashes.KickPower) + " kickleast=" + F(Crashes.KickLeast) + " kickmost=" + F(Crashes.KickMost) +
            " crushkick=" + F(Crashes.CrushKick) + " crushup=" + F(Crashes.CrushUp) + " crushfan=" + F(Crashes.CrushFan) + " skidfan=" + F(Crashes.SkidFan) +
            " spinkept=" + F(Crashes.SpinKept) + " tearspin0=" + F(Crashes.TearSpinLeast) + " tearspin1=" + F(Crashes.TearSpinMost) +
            " bursts=" + (Crashes.BurstByPropellant ? 1 : 0) + " burstbiprop=" + F(Crashes.BurstBiprop) + " burstmono=" + F(Crashes.BurstMono) + " burstsolid=" + F(Crashes.BurstSolid) +
            " burstfuel=" + F(Crashes.BurstFuel) + " burstleast=" + F(Crashes.BurstLeast) + " burstmost=" + F(Crashes.BurstMost) + " fuelleave=" + F(Crashes.FuelLeave) +
            " fuelblast=" + F(Crashes.FuelBlast) + " fuelshove=" + F(Crashes.FuelShove) + " catchbase=" + F(Crashes.CatchBase) + " catchrise=" + F(Crashes.CatchRise) +
            " catchslow=" + F(Crashes.CatchSlow) + " catchfast=" + F(Crashes.CatchFast) + " catchmost=" + F(Crashes.CatchMost) + " ringkg=" + F(Frames.RingKg) + " barkg=" + F(Frames.BarKg) +
            " bitslide=" + (Fragmentation.Bits.Coulomb ? 1 : 0) + " bitair=" + (Fragmentation.Bits.Drag ? 1 : 0) + " bitpaved=" + F(Fragmentation.Bits.GripPaved) + " bitsoil=" + F(Fragmentation.Bits.GripSoil) +
            " bitearth=" + F(Fragmentation.Bits.GripEarth) + " bitmetal=" + F(Fragmentation.Bits.GripMetal) + " bitbolt=" + F(Fragmentation.Bits.GripBolt) + " bitfoil=" + F(Fragmentation.Bits.GripFoil) +
            " bitgrip=" + F(Fragmentation.Bits.GripTimes) + " bitbounce=" + F(Fragmentation.Bits.BounceMetal) + " boltbounce=" + F(Fragmentation.Bits.BounceBolt) + " softbounce=" + F(Fragmentation.Bits.BounceSoft) +
            " bitrest=" + F(Fragmentation.Bits.RestSpeed) + " bitsettle=" + F(Fragmentation.Bits.Settle) + " bittouch=" + F(Fragmentation.Bits.Touch) + " bitspin=" + F(Fragmentation.Bits.SpinDamp) +
            " bitlie=" + F(Fragmentation.Bits.LieRate) + " bitrays=" + Fragmentation.Bits.RaysPerFrame + " bitrefind=" + F(Fragmentation.Bits.Refind) +
            " bitrayslope=" + F(Fragmentation.Bits.RaySlope) + " bitflightrays=" + Fragmentation.Bits.FlightRays + " bitflightfrom=" + F(Fragmentation.Bits.FlightFrom) +
            " bitfirst=" + (Fragmentation.Bits.FirstTouch ? 1 : 0) + " bitfirstrays=" + Fragmentation.Bits.FirstRays + " bitraysfor=" + F(Fragmentation.Bits.RaysFor) +
            " bitrefindslope=" + F(Fragmentation.Bits.RefindSloped) + " bitahead=" + F(Fragmentation.Bits.FirstAhead) + " bitaheadnear=" + F(Fragmentation.Bits.AheadNear);

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
            Fragmentation.Bits.TestNone = Num(words, "bits", Fragmentation.Bits.TestNone ? 0 : 1) < 0.5f;
            Crashes.TestQuiet = Num(words, "modules", Crashes.TestQuiet ? 0 : 1) < 0.5f;
            Piece.MostOverride = (int)Num(words, "most", Piece.MostOverride);
            Mat3.Singular = Num(words, "singular", Mat3.Singular);
            Crashes.PartedMost = Num(words, "partedmost", Crashes.PartedMost);
            Crashes.PartedSpin = Num(words, "partedspin", Crashes.PartedSpin);
            Crashes.EnergyBudget = Num(words, "budget", Crashes.EnergyBudget ? 1 : 0) > 0.5f;
            Crashes.LatEmpty = Num(words, "latempty", Crashes.LatEmpty);
            Crashes.LatFull = Num(words, "latfull", Crashes.LatFull);
            Crashes.SideLeast = Num(words, "sideleast", Crashes.SideLeast);
            Sounds.LouderBy = Num(words, "louder", Sounds.LouderBy);
            Sounds.InWindow = (int)Num(words, "inwindow", Sounds.InWindow);
            Sounds.HitCurve = Num(words, "hitcurve", Sounds.HitCurve);
            Crashes.BreakUp = Num(words, "breakup", Crashes.BreakUp ? 1 : 0) > 0.5f;
            Crashes.BreakSlow = Num(words, "breakslow", Crashes.BreakSlow);
            Crashes.BreakFast = Num(words, "breakfast", Crashes.BreakFast);
            Crashes.BurstBreak = Num(words, "burstbreak", Crashes.BurstBreak);
            Crashes.BreakBiggest0 = Num(words, "biggest0", Crashes.BreakBiggest0);
            Crashes.BreakBiggest1 = Num(words, "biggest1", Crashes.BreakBiggest1);
            Crashes.BreakPower = Num(words, "breakpower", Crashes.BreakPower);
            Crashes.BreakKept = Num(words, "breakkept", Crashes.BreakKept);
            Crashes.Stringy = Num(words, "stringy", Crashes.Stringy);
            Crashes.ClusterFrom = (int)Num(words, "groupfrom", Crashes.ClusterFrom);
            Crashes.TestBreakQ = Num(words, "breakq", Crashes.TestBreakQ);
            Crashes.KeepMass = Num(words, "keepmass", Crashes.KeepMass ? 1 : 0) > 0.5f;
            Crashes.FineShare = Num(words, "fine", Crashes.FineShare);
            Crashes.FineGround = Num(words, "fineground", Crashes.FineGround);
            Crashes.FineSolid = Num(words, "finesolid", Crashes.FineSolid);
            Crashes.DenseMost = Num(words, "densemost", Crashes.DenseMost);
            Crashes.KeepFrom = Num(words, "keepfrom", Crashes.KeepFrom);
            Crashes.CarryOwed = Num(words, "carryowed", Crashes.CarryOwed ? 1 : 0) > 0.5f;
            Crashes.DenseSettle = Num(words, "densesettle", Crashes.DenseSettle);
            Crashes.ThrownByMass = Num(words, "thrown", Crashes.ThrownByMass ? 1 : 0) > 0.5f;
            Crashes.KickPower = Num(words, "kickpower", Crashes.KickPower);
            Crashes.KickLeast = Num(words, "kickleast", Crashes.KickLeast);
            Crashes.KickMost = Num(words, "kickmost", Crashes.KickMost);
            Crashes.CrushKick = Num(words, "crushkick", Crashes.CrushKick);
            Crashes.CrushUp = Num(words, "crushup", Crashes.CrushUp);
            Crashes.CrushFan = Num(words, "crushfan", Crashes.CrushFan);
            Crashes.SkidFan = Num(words, "skidfan", Crashes.SkidFan);
            Crashes.SpinKept = Num(words, "spinkept", Crashes.SpinKept);
            Crashes.TearSpinLeast = Num(words, "tearspin0", Crashes.TearSpinLeast);
            Crashes.TearSpinMost = Num(words, "tearspin1", Crashes.TearSpinMost);
            Crashes.BurstByPropellant = Num(words, "bursts", Crashes.BurstByPropellant ? 1 : 0) > 0.5f;
            Crashes.BurstBiprop = Num(words, "burstbiprop", Crashes.BurstBiprop);
            Crashes.BurstMono = Num(words, "burstmono", Crashes.BurstMono);
            Crashes.BurstSolid = Num(words, "burstsolid", Crashes.BurstSolid);
            Crashes.BurstFuel = Num(words, "burstfuel", Crashes.BurstFuel);
            Crashes.BurstLeast = Num(words, "burstleast", Crashes.BurstLeast);
            Crashes.BurstMost = Num(words, "burstmost", Crashes.BurstMost);
            Crashes.FuelLeave = Num(words, "fuelleave", Crashes.FuelLeave);
            Crashes.FuelBlast = Num(words, "fuelblast", Crashes.FuelBlast);
            Crashes.FuelShove = Num(words, "fuelshove", Crashes.FuelShove);
            Crashes.CatchBase = Num(words, "catchbase", Crashes.CatchBase);
            Crashes.CatchRise = Num(words, "catchrise", Crashes.CatchRise);
            Crashes.CatchSlow = Num(words, "catchslow", Crashes.CatchSlow);
            Crashes.CatchFast = Num(words, "catchfast", Crashes.CatchFast);
            Crashes.CatchMost = Num(words, "catchmost", Crashes.CatchMost);
            Frames.RingKg = Num(words, "ringkg", Frames.RingKg);
            Frames.BarKg = Num(words, "barkg", Frames.BarKg);
            Fragmentation.Bits.Coulomb = Num(words, "bitslide", Fragmentation.Bits.Coulomb ? 1 : 0) > 0.5f;
            Fragmentation.Bits.Drag = Num(words, "bitair", Fragmentation.Bits.Drag ? 1 : 0) > 0.5f;
            Fragmentation.Bits.GripPaved = Num(words, "bitpaved", Fragmentation.Bits.GripPaved);
            Fragmentation.Bits.GripSoil = Num(words, "bitsoil", Fragmentation.Bits.GripSoil);
            Fragmentation.Bits.GripEarth = Num(words, "bitearth", Fragmentation.Bits.GripEarth);
            Fragmentation.Bits.GripMetal = Num(words, "bitmetal", Fragmentation.Bits.GripMetal);
            Fragmentation.Bits.GripBolt = Num(words, "bitbolt", Fragmentation.Bits.GripBolt);
            Fragmentation.Bits.GripFoil = Num(words, "bitfoil", Fragmentation.Bits.GripFoil);
            Fragmentation.Bits.GripTimes = Num(words, "bitgrip", Fragmentation.Bits.GripTimes);
            Fragmentation.Bits.BounceMetal = Num(words, "bitbounce", Fragmentation.Bits.BounceMetal);
            Fragmentation.Bits.BounceBolt = Num(words, "boltbounce", Fragmentation.Bits.BounceBolt);
            Fragmentation.Bits.BounceSoft = Num(words, "softbounce", Fragmentation.Bits.BounceSoft);
            Fragmentation.Bits.RestSpeed = Num(words, "bitrest", Fragmentation.Bits.RestSpeed);
            Fragmentation.Bits.Settle = Num(words, "bitsettle", Fragmentation.Bits.Settle);
            Fragmentation.Bits.Touch = Num(words, "bittouch", Fragmentation.Bits.Touch);
            Fragmentation.Bits.SpinDamp = Num(words, "bitspin", Fragmentation.Bits.SpinDamp);
            Fragmentation.Bits.LieRate = Num(words, "bitlie", Fragmentation.Bits.LieRate);
            Fragmentation.Bits.RaysPerFrame = (int)Num(words, "bitrays", Fragmentation.Bits.RaysPerFrame);
            Fragmentation.Bits.Refind = Num(words, "bitrefind", Fragmentation.Bits.Refind);
            Fragmentation.Bits.RaySlope = Num(words, "bitrayslope", Fragmentation.Bits.RaySlope);
            Fragmentation.Bits.FlightRays = (int)Num(words, "bitflightrays", Fragmentation.Bits.FlightRays);
            Fragmentation.Bits.FlightFrom = Num(words, "bitflightfrom", Fragmentation.Bits.FlightFrom);
            Fragmentation.Bits.FirstTouch = Num(words, "bitfirst", Fragmentation.Bits.FirstTouch ? 1 : 0) > 0.5f;
            Fragmentation.Bits.FirstRays = (int)Num(words, "bitfirstrays", Fragmentation.Bits.FirstRays);
            Fragmentation.Bits.RaysFor = Num(words, "bitraysfor", Fragmentation.Bits.RaysFor);
            Fragmentation.Bits.RefindSloped = Num(words, "bitrefindslope", Fragmentation.Bits.RefindSloped);
            Fragmentation.Bits.FirstAhead = Num(words, "bitahead", Fragmentation.Bits.FirstAhead);
            Fragmentation.Bits.AheadNear = Num(words, "bitaheadnear", Fragmentation.Bits.AheadNear);
            return Figures();
        }

        public static string Figures() =>
            "restitution " + Crashes.Restitution + ", toughness " + Crashes.Toughness + ", bite " + Crashes.Bite + "/" + Crashes.BitePaved + ", friction " + Crashes.Friction + "/" + Crashes.FrictionPaved +
            ", plough " + Crashes.Plough + "/" + Crashes.PloughPaved + ", dig " + Crashes.Dig + ", scoop " + Crashes.Scoop + ", anchor " + Crashes.Anchor + ", rolling " + Crashes.Rolling + ", gouge " + Crashes.Gouge + ", grind " + Crashes.GrindSoil + "/" + Crashes.GrindPaved + ", skin " + Crashes.SkinShare + ", graze " + Crashes.GrazeFriction +
            ", steel " + Crashes.FrictionSteel + "/" + Crashes.GougeSteel + ", catch " + Crashes.CatchPaved + ", wear " + Crashes.WearPaved + "/" + Crashes.WearSoil + ", vault " + Crashes.Vault +
            (Crashes.HoldWrecks ? "" : ", wrecks not held") + (Crashes.BrakeRolling ? "" : ", rolling not braked") + (Crashes.GripStrikes ? "" : ", strikes not gripped") + (Crashes.BridgeHops ? "" : ", hops not bridged") + ", spin lost " + Crashes.SpinLost + ", rolling ploughs " + Crashes.RollPlough + (Crashes.TellLawn ? "" : ", lawns as paving") + (Crashes.TestGround >= 0 ? ", every ground " + (Ground.Kind)Crashes.TestGround : "") +
            "; feeding dust " + Crashes.FeedDust + ", leaks " + Crashes.FeedLeaks + ", movers " + Crashes.FeedMovers + ", trails " + Crashes.FeedTrails + ", fire " + Crashes.FeedFire +
            "; shadows from " + Piece.ShadowsFrom + " m, most " + Piece.Most + "; bits " + !Fragmentation.Bits.TestNone + ", wrecks' modules " + !Crashes.TestQuiet + "; singular below " + Mat3.Singular + "; parts let go held within " + Crashes.PartedMost + " m/s and " + Crashes.PartedSpin + " rad/s of how they went" +
            (Crashes.EnergyBudget ? "" : "; blows not held to the craft's energy") + "; round tanks side-on " + Crashes.LatEmpty + " empty, " + Crashes.LatFull + " full, never below " + Crashes.SideLeast + " of end-on" +
            "; a blow heard within its window " + Sounds.LouderBy + " times as loud, " + Sounds.InWindow + " at most, loudness by energy to the power " + Sounds.HitCurve +
            (Crashes.BreakUp ? "; skins broken up from " + Crashes.BreakSlow + " to " + Crashes.BreakFast + " m/s (a burst at least " + Crashes.BurstBreak + "), the largest piece " + Crashes.BreakBiggest0 + " to " + Crashes.BreakBiggest1 +
                " of what comes off, the rest by size to the power -" + Crashes.BreakPower + ", " + Crashes.BreakKept + " at most in pieces, none stringier than " + Crashes.Stringy + ", grouped from " + Crashes.ClusterFrom + " panels" +
                (Crashes.TestBreakQ >= 0f ? ", every break-up taken as " + Crashes.TestBreakQ : "") : "; skins not broken up by the blow, each panel its own piece or not") +
            (Crashes.KeepMass ? "; the metal of panels in no piece folded into the pieces, " + Crashes.FineShare + " of it lost as fine debris (" + Crashes.FineGround + " ground away, " +
                Crashes.FineSolid + " of a solid part), no piece past " + Crashes.DenseMost + " times its own area's metal, a piece made of them if none was and more than " + Crashes.KeepFrom +
                " times the least piece's would be lost" + (Crashes.CarryOwed ? ", what none takes owed by the part, folded into its later pieces and given to those about, up to " +
                Crashes.DenseSettle + " times as it goes" : ", what none takes lost at once") : "; pieces weighing their own panels only, the rest lost") +
            (Crashes.ThrownByMass ? "; pieces thrown by their mass to the power -" + Crashes.KickPower + " (" + Crashes.KickLeast + " to " + Crashes.KickMost + " of a panel's kick), crushed ones out " + Crashes.CrushKick +
                " m/s and up " + Crashes.CrushUp + " m/s at most, fanned " + Crashes.CrushFan + " degrees, skipping within " + Crashes.SkidFan + " degrees, turning with " + Crashes.SpinKept + " of their part's turning and by their tearing, " +
                Crashes.TearSpinLeast + " to " + Crashes.TearSpinMost + " rad/s" : "; pieces thrown as they used to be, whatever their mass") +
            (Crashes.BurstByPropellant ? "; bursts by what is in the tank, " + Crashes.BurstBiprop + "/" + Crashes.BurstMono + "/" + Crashes.BurstSolid + "/" + Crashes.BurstFuel +
                " J a kg of fuel and oxidiser, monopropellant, solid and fuel alone, thrown at " + Crashes.BurstLeast + " to " + Crashes.BurstMost + " m/s; fuel alone leaving " + Crashes.FuelLeave +
                " of the skin, its blast " + Crashes.FuelBlast + " and shove " + Crashes.FuelShove + "; catching " + Crashes.CatchBase + " at " + Crashes.CatchSlow + " m/s, " +
                Crashes.CatchRise + " more by " + Crashes.CatchFast + " m/s, " + Crashes.CatchMost + " at most; rings " + Frames.RingKg + " and stringers " + Frames.BarKg + " kg a metre"
                : "; tanks going up as they used to, whatever is in them") +
            (Fragmentation.Bits.Coulomb ? "; bits sliding by friction " + Fragmentation.Bits.GripPaved + " on paving, " + Fragmentation.Bits.GripSoil + " on soil, " + Fragmentation.Bits.GripEarth + " earth (" +
                Fragmentation.Bits.GripMetal + "/" + Fragmentation.Bits.GripBolt + "/" + Fragmentation.Bits.GripFoil + " shreds, bolts, foil where the ground is not known), times " + Fragmentation.Bits.GripTimes +
                ", bouncing " + Fragmentation.Bits.BounceMetal + "/" + Fragmentation.Bits.BounceBolt + "/" + Fragmentation.Bits.BounceSoft + ", at rest below " + Fragmentation.Bits.RestSpeed + " m/s, lying on the ground below " +
                Fragmentation.Bits.Settle + " m/s into it and within " + Fragmentation.Bits.Touch + " m, spin damped " + Fragmentation.Bits.SpinDamp + " and laid flat " + Fragmentation.Bits.LieRate + " a second, " +
                Fragmentation.Bits.RaysPerFrame + " rays a frame for the ground" + (Fragmentation.Bits.FirstTouch ? " (as many a " + Fragmentation.Bits.RaysFor + "th of a second, up to four times that, and " +
                Fragmentation.Bits.FirstRays + " more for first touches only, each looked for where it will come down, within " + Fragmentation.Bits.FirstAhead + " s, and again as it touches if more than " + Fragmentation.Bits.AheadNear + " m from there)" : "") +
                ", again every " + Fragmentation.Bits.Refind + " m" + (Fragmentation.Bits.FirstTouch ? " (" + Fragmentation.Bits.RefindSloped + " m on sloping ground)" : "") + ", reaching " + Fragmentation.Bits.RaySlope +
                " m farther each way for each metre gone; " + Fragmentation.Bits.FlightRays + " looks a frame along the flight of those more than " + Fragmentation.Bits.FlightFrom + " m from their ground"
                : "; bits stopping as they used to, a share of their speed a frame") + (Fragmentation.Bits.Drag ? "" : "; bits not slowed by the air");

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

        /// <summary>(Since 0.4.0, the development build: each loaded craft, how far from the craft flown, its situation, whether it is
        /// on rails, and the distance on the ground it is put on rails at (350 m as the game has it; more for a watched wreck).)</summary>
        public static string Ranges()
        {
            Vessel me = FlightGlobals.ActiveVessel;
            var o = new System.Text.StringBuilder();
            foreach (Vessel v in FlightGlobals.VesselsLoaded)
            {
                if (v == null) continue;
                float d = me != null ? (float)(v.CoMD - me.CoMD).magnitude : 0f;
                o.Append(v.vesselName + ": " + d.ToString("F0") + " m off, " + v.situation + (v.packed ? ", ON RAILS" : "") + ", on the ground on rails from " + (v.vesselRanges != null ? v.vesselRanges.landed.pack.ToString("F0") : "?") + " m, " + v.srfSpeed.ToString("F1") + " m/s\n");
            }
            return o.Length > 0 ? o.ToString() : "nothing loaded";
        }

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
                    // (and what the hurt came to, kilojoules: each part's hurt times what it takes to crush it through, summed over the
                    // craft, beside half the craft's mass times the square of the hardest blow's speed, to check the blows' energy budget)
                    float spent = 0f;
                    foreach (Hits h in w.hits)
                        if (h != null && h.damage > 0.01f && h.part != null)
                        {
                            // (the energy that went into it, as each blow was reckoned: no longer its hurt times one strength, since what
                            // it takes now goes by the way it was struck, see Crashes.Capacity(h, way))
                            float kj = h.spent;
                            spent += kj;
                            say.Append("  ").Append(h.part.partInfo != null ? h.part.partInfo.title : h.part.name).Append(": hurt ").Append(h.damage.ToString("F2")).Append(" (").Append(kj.ToString("F1")).Append(" kJ), crushed ").Append(h.crushed.ToString("F2"))
                               .Append(h.leaking ? ", leaking" : "").Append(h.gone ? ", gone" : "").Append('\n');
                        }
                    float mass = v.GetTotalMass();
                    say.Append("  hurt in all ").Append(spent.ToString("F1")).Append(" kJ; its ").Append(mass.ToString("F2")).Append(" t now, at its hardest blow's speed, has ")
                       .Append((0.5f * mass * w.hardest * w.hardest).ToString("F1")).Append(" kJ\n");
                }
            say.Append(Budget("")).Append('\n');
            say.Append(Heard("")).Append('\n');
            say.Append(Breakup("")).Append('\n');
            say.Append(Thrown("")).Append('\n');
            say.Append(Burst("")).Append('\n');
            say.Append(Bits("")).Append('\n');
            int moving = 0, resting = 0, burning = 0;
            foreach (Piece p in Piece.All) { if (p == null) continue; if (p.resting) resting++; else moving++; if (p.burns > 0f) burning++; }
            say.Append("pieces: ").Append(moving).Append(" moving, ").Append(resting).Append(" at rest, ").Append(burning).Append(" burning\n");
            say.Append(Figures()).Append('\n');
            say.Append("heard: ground ").Append(Hits.HeardGround).Append(", craft ").Append(Hits.HeardCraft).Append(", its own craft ").Append(Hits.HeardSameCraft).Append(", other things ").Append(Hits.HeardOther)
               .Append(", pieces ").Append(Hits.HeardPieces).Append(", while not armed ").Append(Hits.HeardUnarmed).Append("; last not ground: ").Append(Hits.LastOther).Append('\n');
            // (how often a craft's turning could not be turned back and a stand-in was taken: about never, for any craft the game makes)
            say.Append("turnings inverted ").Append(Mat3.Inverted).Append(", taken as singular ").Append(Mat3.Fallbacks).Append('\n');
            say.Append("parts let go and held from being flung: ").Append(Crashes.PartedHeld).Append(", the worst would have been ").Append(Crashes.PartedWorst.ToString("F0")).Append(" m/s\n");
            say.Append(Crashes.Notes(25));
            return say.ToString();
        }

        public static string Notes() => Crashes.Notes(60);

        /// <summary>
        /// The blows' energy budget (see Crashes.energyScale): the last step with strikes beyond their tolerance, the share of its own energy
        /// each blow of it was given, what they asked between them and what the craft had, kilojoules; the least share since last cleared and
        /// how many steps were scaled below 0.95. "reset": cleared, before a test.
        /// </summary>
        public static string Budget(string words)
        {
            if ((words ?? "").Trim().ToLowerInvariant() == "reset")
            {
                Crashes.BudgetLast = Crashes.BudgetLeast = 1f;
                Crashes.BudgetAsked = Crashes.BudgetHad = 0f;
                Crashes.BudgetSteps = 0;
            }
            return "blows' budget " + (Crashes.EnergyBudget ? "on" : "off") + ": last step scaled by " + Crashes.BudgetLast.ToString("F2") + " (asked " + Crashes.BudgetAsked.ToString("F1") + " kJ, had " +
                Crashes.BudgetHad.ToString("F1") + " kJ), least " + Crashes.BudgetLeast.ToString("F2") + ", steps scaled below 0.95: " + Crashes.BudgetSteps;
        }

        /// <summary>
        /// How parts' skins broke up (see Crashes.Cluster): the last group of panels made into pieces, of which part, by what, how hard (q, 0
        /// to 1), its panels and area, how many pieces it was to be and how many were made, their areas largest first, how many groups were
        /// stopped short of a ribbon or had nowhere to grow, and how many panels went to bits for none taking them; then, for each part torn
        /// since last cleared, how hard it last broke up, its pieces' masses largest first (with the metal folded into them, see
        /// Crashes.Fold), the largest one's share of them, what they come to beside the part's mass as built, the metal lost as fine debris
        /// (and of it what the ground ground away) and any no piece could take, split by why (the pieces made were full at DenseMost; none
        /// was made, the pieces about being at their most; none was made for any other reason), the area of the panels that went to
        /// bits, and whether the part went altogether ("gone": broken up by us; "gone by the game": no longer there, as a part the game
        /// destroyed itself; else still on, and what it lost was only some of its skin), and (since 2026-11-04, see Crashes.CarryOwed) what it
        /// owed that was given later to its pieces still about (in what they come to) and what it still owes; and then all of that summed over those parts. Only
        /// the latest sixty-four parts have a line, but the sums are over every one kept (Crashes.BrokeUpsKept), and how many were left
        /// out of the lines, or let go altogether, is said.
        /// "reset": cleared, before a test.
        /// </summary>
        public static string Breakup(string words)
        {
            if ((words ?? "").Trim().ToLowerInvariant() == "reset")
            {
                Crashes.BrokeUps.Clear();
                Crashes.BrokeUpsLetGo = 0;
                Crashes.BreakGrouped = 0;
                Crashes.BreakLastPart = "";
            }
            var say = new StringBuilder();
            say.Append("panels grouped into pieces ").Append(Crashes.BreakGrouped).Append(" times");
            if (Crashes.BreakLastPart.Length > 0)
            {
                say.Append("; last ").Append(Crashes.BreakLastPart).Append(", ").Append(Crashes.BreakLastWhy).Append(" at q ").Append(Crashes.BreakLastQ.ToString("F2")).Append(": ")
                   .Append(Crashes.BreakLastPanels).Append(" panels, ").Append(Crashes.BreakLastArea.ToString("F2")).Append(" m2, to be ").Append(Crashes.BreakLastTargets).Append(" pieces, made ")
                   .Append(Crashes.BreakLastMade).Append(" (");
                for (int k = 0; k < Crashes.BreakLastTargets && k < Crashes.BreakLastAreas.Length; k++) say.Append(k > 0 ? ", " : "").Append(Crashes.BreakLastAreas[k].ToString("F2"));
                say.Append(" m2); stopped short of a ribbon ").Append(Crashes.BreakLastStringy).Append(", nowhere to grow ").Append(Crashes.BreakLastBlocked).Append(", panels none took ").Append(Crashes.BreakLastLeft);
            }
            // (the sums over the parts listed: pieces, built, fine, ground away, lost and its three causes, kilograms; how many parts and how many gone)
            float allKept = 0f, allBuilt = 0f, allFine = 0f, allGround = 0f, allLost = 0f, allNone = 0f, allFull = 0f, allCap = 0f, allSettled = 0f, allOwed = 0f;
            int parts = 0, partsGone = 0, allPieces = 0;
            // (every part kept is in the sums, but only the latest sixty-four have a line of their own: a big crash tears hundreds)
            const int listed = 64;
            int listFrom = Mathf.Max(0, Crashes.BrokeUps.Count - listed);
            for (int i = 0; i < Crashes.BrokeUps.Count; i++)
            {
                Crashes.BrokeUp r = Crashes.BrokeUps[i];
                float[] kg = r.kg.ToArray();
                Array.Sort(kg);
                Array.Reverse(kg);
                float sum = 0f;
                foreach (float m in kg) sum += m;
                // (and what it owed, given later to its pieces made before: in no piece's mass as made, see Crashes.Settle)
                float kept = sum + r.settledKg;
                // (a Hits is the part's own, and goes with it: one no longer there was destroyed, by us or by the game)
                bool goneByUs = r.gone, goneByGame = !r.gone && r.h == null;
                parts++;
                if (goneByUs || goneByGame) partsGone++;
                allPieces += kg.Length;
                allKept += kept; allBuilt += r.built; allFine += r.fineKg; allGround += r.groundKg;
                allLost += r.lostKg; allNone += r.lostNoneKg; allFull += r.lostFullKg; allCap += r.lostCapKg; allSettled += r.settledKg; allOwed += r.owedKg;
                if (i < listFrom) continue;
                say.Append('\n').Append("  ").Append(r.title).Append(goneByUs ? " (gone)" : goneByGame ? " (gone by the game)" : " (still on)").Append(": q ").Append(r.q.ToString("F2")).Append(", ").Append(kg.Length).Append(" pieces");
                if (kg.Length > 0)
                {
                    say.Append(" (");
                    for (int k = 0; k < kg.Length && k < 16; k++) say.Append(k > 0 ? ", " : "").Append(kg[k].ToString("F1"));
                    if (kg.Length > 16) say.Append(", ...");
                    say.Append(" kg), the largest ").Append((kg[0] / Mathf.Max(1e-6f, sum)).ToString("F2")).Append(" of them");
                }
                if (r.settledKg >= 0.05f) say.Append(", ").Append(r.settledKg.ToString("F1")).Append(" kg it owed given later to those about");
                if (r.owedKg >= 0.05f) say.Append(", still owed ").Append(r.owedKg.ToString("F1")).Append(" kg");
                say.Append("; in all ").Append(kept.ToString("F1")).Append(" kg of its ").Append(r.built.ToString("F1")).Append(" kg built (").Append((100f * kept / Mathf.Max(1e-6f, r.built)).ToString("F0"))
                   .Append("%), fine ").Append(r.fineKg.ToString("F1")).Append(" kg (ground away ").Append(r.groundKg.ToString("F1")).Append(" kg of it)");
                if (r.lostKg >= 0.05f)
                    say.Append(", ").Append(r.lostKg.ToString("F1")).Append(" kg more lost for no piece to take it (pieces full ").Append(r.lostFullKg.ToString("F1"))
                       .Append(", none made ").Append(r.lostNoneKg.ToString("F1")).Append(", none at the pieces' cap ").Append(r.lostCapKg.ToString("F1")).Append(')');
                say.Append("; panels to bits ").Append(r.bitsArea.ToString("F2")).Append(" m2");
            }
            if (parts > 0)
                say.Append('\n').Append("  in all, ").Append(parts).Append(" parts (").Append(partsGone).Append(" gone): ").Append(allPieces).Append(" pieces, ").Append(allKept.ToString("F1")).Append(" kg of ")
                   .Append(allBuilt.ToString("F1")).Append(" kg built (").Append((100f * allKept / Mathf.Max(1e-6f, allBuilt)).ToString("F0")).Append("%), fine ").Append(allFine.ToString("F1"))
                   .Append(" kg (ground away ").Append(allGround.ToString("F1")).Append(" kg of it), lost ").Append(allLost.ToString("F1")).Append(" kg (pieces full ").Append(allFull.ToString("F1"))
                   .Append(", none made ").Append(allNone.ToString("F1")).Append(", none at the pieces' cap ").Append(allCap.ToString("F1")).Append(')')
                   .Append("; of what was owed, given later to pieces about ").Append(allSettled.ToString("F1")).Append(" kg (in the pieces' sum), still owed ").Append(allOwed.ToString("F1")).Append(" kg");
            if (listFrom > 0) say.Append("; the latest ").Append(listed).Append(" listed above, ").Append(listFrom).Append(" earlier in the sums only");
            // (past BrokeUpsKept the oldest are let go, and are in neither: said, so that a sum short of them is never taken for the whole)
            if (Crashes.BrokeUpsLetGo > 0) say.Append('\n').Append("  ").Append(Crashes.BrokeUpsLetGo).Append(" parts earlier still let go, past the ").Append(Crashes.BrokeUpsKept).Append(" kept, in neither");
            return say.ToString();
        }

        /// <summary>
        /// How the pieces were thrown (see Crashes.MakePiece), since last cleared: how many; the share of a panel's kick they were given by
        /// their mass, least, mean and most; the speed the throwing gave them on average beside what they had as part of their part; how
        /// far the crushed were fanned about the way they were going, on average and at most; how many skipped on the ground as they were
        /// made, and how many of those went off a random way for going no way along it; and how fast they turned as made, on average, the
        /// small ones (under half a metre across, corner to corner), the middling and the large (over a metre), with what of that the
        /// small and the large had of their part's turning. "reset": cleared, before a test.
        /// </summary>
        public static string Thrown(string words)
        {
            if ((words ?? "").Trim().ToLowerInvariant() == "reset")
            {
                Crashes.ThrownMade = Crashes.ThrownFanned = Crashes.ThrownSkids = Crashes.ThrownSkidsRandom = Crashes.ThrownSmall = Crashes.ThrownMiddle = Crashes.ThrownLarge = 0;
                Crashes.ThrownKickLeast = float.MaxValue;
                Crashes.ThrownKickMost = Crashes.ThrownKickSum = Crashes.ThrownDvSum = Crashes.ThrownFanSum = Crashes.ThrownFanMost = 0f;
                Crashes.ThrownSpinSmall = Crashes.ThrownSpinMiddle = Crashes.ThrownSpinLarge = Crashes.ThrownKeptSmall = Crashes.ThrownKeptLarge = 0f;
            }
            int n = Crashes.ThrownMade;
            if (n == 0) return "pieces thrown " + (Crashes.ThrownByMass ? "by their mass" : "as they used to be") + ": none since last cleared";
            var say = new StringBuilder();
            say.Append("pieces thrown ").Append(Crashes.ThrownByMass ? "by their mass" : "as they used to be").Append(": ").Append(n).Append(", their kicks ").Append(Crashes.ThrownKickLeast.ToString("F2")).Append(" to ")
               .Append(Crashes.ThrownKickMost.ToString("F2")).Append(" of a panel's (on average ").Append((Crashes.ThrownKickSum / n).ToString("F2")).Append("), the throwing giving them ")
               .Append((Crashes.ThrownDvSum / n).ToString("F1")).Append(" m/s on average beside their part's speed");
            if (Crashes.ThrownFanned > 0)
                say.Append("; ").Append(Crashes.ThrownFanned).Append(" crushed fanned ").Append((Crashes.ThrownFanSum / Crashes.ThrownFanned).ToString("F1")).Append(" degrees on average, ").Append(Crashes.ThrownFanMost.ToString("F1")).Append(" at most");
            say.Append("; ").Append(Crashes.ThrownSkids).Append(" skipped on the ground, ").Append(Crashes.ThrownSkidsRandom).Append(" of them a random way");
            say.Append("; turning, under 0.5 m: ").Append(Crashes.ThrownSmall);
            if (Crashes.ThrownSmall > 0) say.Append(" at ").Append((Crashes.ThrownSpinSmall / Crashes.ThrownSmall).ToString("F1")).Append(" rad/s (").Append((Crashes.ThrownKeptSmall / Crashes.ThrownSmall).ToString("F1")).Append(" of it their part's)");
            say.Append(", 0.5 to 1 m: ").Append(Crashes.ThrownMiddle);
            if (Crashes.ThrownMiddle > 0) say.Append(" at ").Append((Crashes.ThrownSpinMiddle / Crashes.ThrownMiddle).ToString("F1")).Append(" rad/s");
            say.Append(", over 1 m: ").Append(Crashes.ThrownLarge);
            if (Crashes.ThrownLarge > 0) say.Append(" at ").Append((Crashes.ThrownSpinLarge / Crashes.ThrownLarge).ToString("F1")).Append(" rad/s (").Append((Crashes.ThrownKeptLarge / Crashes.ThrownLarge).ToString("F1")).Append(" of it their part's)");
            if (!Crashes.ThrownByMass) say.Append(" (turning not counted as they used to be thrown)");
            return say.ToString();
        }

        /// <summary>
        /// How tanks went up (see Crashes.GoUp and Crashes.BurstBiprop): the last one, what was in it, how much and its dry mass as built,
        /// the energy each kilogram of it gave its pieces and the speed that threw them at, the share of its skin it was to leave, its blast's
        /// push on the pieces about it and its shove on the craft, and the kick its rings and stringers were given; the pieces made since it
        /// went up (its own skin's first), how far they now lie from where it went up, along the ground, the middle one, on average and the
        /// farthest, and how many are still moving; the rings and stringers thrown since last cleared and what they weighed; and the leaks
        /// begun since last cleared, by how hard their tank was struck, under 20, 20 to 50 and over 50 m/s, how many began in air and how many
        /// of those caught, beside the chance of catching at a few speeds; the hypergolic, and those crushed through. "reset": cleared, before
        /// a test (call it before each throw or Leak).
        /// </summary>
        public static string Burst(string words)
        {
            if ((words ?? "").Trim().ToLowerInvariant() == "reset")
            {
                Crashes.BurstContents = -1; Crashes.BurstPart = ""; Crashes.BurstBody = null;
                for (int k = 0; k < 3; k++) Crashes.LeaksInAir[k] = Crashes.LeaksLit[k] = 0;
                Crashes.LeaksHypergolic = Crashes.LeaksCrushed = Crashes.LeaksNoAir = Crashes.LeaksNoAirLit = 0;
                Frames.RingsThrown = Frames.BarsThrown = 0;
                Frames.RingKgSum = Frames.RingKgMost = Frames.BarKgSum = Frames.BarKgMost = 0f;
            }
            var say = new StringBuilder();
            say.Append(Crashes.BurstByPropellant ? "bursts by what is in the tank: " : "bursts as they used to be: ");
            if (Crashes.BurstContents < 0) say.Append("nothing has gone up since last cleared");
            else
            {
                say.Append(Crashes.BurstPart).Append(" went up ").Append((Time.time - Crashes.BurstWhen).ToString("F1")).Append(" s ago, ").Append(Crashes.ContentsSaid[Crashes.BurstContents]).Append(", ")
                   .Append(Crashes.BurstFuelKg.ToString("F0")).Append(" kg in it, ").Append(Crashes.BurstDryKg.ToString("F0")).Append(" kg dry as built (").Append((Crashes.BurstFuelKg / Mathf.Max(1f, Crashes.BurstDryKg)).ToString("F1"))
                   .Append(" times)");
                if (Crashes.BurstByPropellant) say.Append(", ").Append(Crashes.BurstEnergy.ToString("F0")).Append(" J a kg into its pieces: thrown at ").Append(Crashes.BurstSpeed.ToString("F1")).Append(" m/s");
                say.Append("; its skin to leave ").Append(Crashes.BurstLeaveShare.ToString("F2")).Append(", its blast pushing ").Append(Crashes.BurstPush.ToString("F1")).Append(" m/s, its shove ")
                   .Append(Crashes.BurstShove.ToString("F2")).Append(" m/s, its frames kicked ").Append(Crashes.BurstFrameKick.ToString("F1")).Append(" m/s");
                // (the pieces since: how far they lie from where it went up, along the ground, as Where reckons distances)
                CelestialBody body = Crashes.BurstBody;
                if (body != null)
                {
                    var far = new List<float>();
                    int own = 0, moving = 0;
                    float sum = 0f;
                    foreach (Piece pc in Piece.All)
                    {
                        if (pc == null || pc.id <= Crashes.BurstFirstId) continue;
                        if (pc.id <= Crashes.BurstLastId) own++;
                        if (!pc.resting) moving++;
                        float d = (float)((Vector3d)pc.transform.position - body.GetWorldSurfacePosition(Crashes.BurstLat, Crashes.BurstLon, body.GetAltitude(pc.transform.position))).magnitude;
                        far.Add(d); sum += d;
                    }
                    far.Sort();
                    say.Append("\n  ").Append(far.Count).Append(" pieces made since (").Append(own).Append(" of its skin, of ").Append(Crashes.BurstLastId - Crashes.BurstFirstId).Append(" made)");
                    if (far.Count > 0)
                        say.Append(", lying from where it went up: the middle one ").Append(far[far.Count / 2].ToString("F0")).Append(" m, on average ").Append((sum / far.Count).ToString("F0"))
                           .Append(" m, the farthest ").Append(far[far.Count - 1].ToString("F0")).Append(" m; ").Append(moving).Append(" still moving");
                }
            }
            say.Append("\n  thrown since last cleared: ").Append(Frames.RingsThrown).Append(" rings");
            if (Frames.RingsThrown > 0) say.Append(" of ").Append((Frames.RingKgSum / Frames.RingsThrown).ToString("F1")).Append(" kg on average, ").Append(Frames.RingKgMost.ToString("F1")).Append(" at most");
            say.Append(", ").Append(Frames.BarsThrown).Append(" stringers");
            if (Frames.BarsThrown > 0) say.Append(" of ").Append((Frames.BarKgSum / Frames.BarsThrown).ToString("F1")).Append(" kg on average, ").Append(Frames.BarKgMost.ToString("F1")).Append(" at most");
            say.Append("\n  leaks since last cleared, in air, caught of begun: under 20 m/s ");
            for (int k = 0; k < 3; k++)
            {
                if (k == 1) say.Append(", 20 to 50 m/s ");
                if (k == 2) say.Append(", over 50 m/s ");
                say.Append(Crashes.LeaksLit[k]).Append(" of ").Append(Crashes.LeaksInAir[k]);
                if (Crashes.LeaksInAir[k] > 0) say.Append(" (").Append((100f * Crashes.LeaksLit[k] / Crashes.LeaksInAir[k]).ToString("F0")).Append("%)");
            }
            say.Append("; out of air ").Append(Crashes.LeaksNoAirLit).Append(" of ").Append(Crashes.LeaksNoAir).Append("; hypergolic ").Append(Crashes.LeaksHypergolic).Append(", crushed through ").Append(Crashes.LeaksCrushed);
            if (Crashes.BurstByPropellant)
                say.Append("; the chance of catching in air at 15, 25, 35, 50 and 70 m/s: ").Append(Crashes.Catch(15f).ToString("F2")).Append(", ").Append(Crashes.Catch(25f).ToString("F2")).Append(", ")
                   .Append(Crashes.Catch(35f).ToString("F2")).Append(", ").Append(Crashes.Catch(50f).ToString("F2")).Append(", ").Append(Crashes.Catch(70f).ToString("F2"));
            else say.Append("; catching in air three times in four");
            return say.ToString();
        }

        // (the last throw of bits made by Bits below: their numbers, where from and which way, when, how fast, on what ground and its friction)
        static int bitsFirst = 1, bitsLast;
        static Vector3 bitsFrom, bitsWay, bitsUp;
        static float bitsAt, bitsSpeed, bitsGrip = -1f, bitsG = 9.81f;
        static string bitsGround = "";
        static int fpsWas = int.MinValue;
        static Vector3[] bitsSeenAt, bitsSeenV;
        static bool[] bitsSeenDown;

        /// <summary>
        /// How far the bits slide (see Bits.Slide): "v=20 kind=metal n=50" throws so many bits of a kind ("metal", "paint", "burnt", "foil",
        /// "bolt", "clod", "turf") along the ground at so many metres a second, from "height=0.2" metres above the ground "ahead=15" metres
        /// before the craft being flown, along its heading (or "heading=90", degrees from north), "size=0.15" metres the biggest of them,
        /// with the friction of the ground found there as a crash's bits would have it; "fps=30" also holds the game to so many frames a
        /// second ("fps=0": as it was). Then "" (some six seconds on): how many of them are about, how many at rest, their distance along
        /// the ground from where they were thrown (mean, nine in ten within, farthest, and mean down the way they were thrown), the moving
        /// ones' mean speed, beside v squared over 2 mu g; the frame rate; and since last cleared, how many rays were cast for the ground
        /// under them, how often one waited for a ray, how many were found floating over the ground where they came to rest (and fell
        /// again) or sunk in it (and were set on it, by how much at most), how many came to rest, and how many went to make room (of
        /// them, still flying); the farthest a bit had gone from where its ground was found when it was looked for, and the longest
        /// ray that took (see Bits.RaySlope); and how many times bits thrown far were looked along in flight, and how many of those
        /// were caught in a hill they flew into. Then (since 2026-11-04) how many were lifted on to the real ground while sliding, as they
        /// first touched and as they were looked for again (and by how much at most); and of those found sunk more than half a metre where
        /// they came to rest, which ray found the ground, how many had waited for a ray and how many had had one before, how far they
        /// had gone from where their ground was last found and how far they had slid since their last ray (mean and most), their
        /// kinds, what they were found on (collider and layer), and the first eight of them one by one. And (since 2026-11-04, see
        /// Bits.FirstTouch) how many times where a bit would come down was looked for ahead, how often ground was found there, how often
        /// above the flat the bit had and above the bit itself (and by how much at most), how many first touches took a ray kept for them,
        /// and the most rays any frame cast. "reset": those cleared, before a test.
        /// </summary>
        public static string Bits(string words)
        {
            try { return BitsNow(words ?? ""); }
            catch (Exception ex) { return "failed: " + ex; }
        }

        static string BitsNow(string words)
        {
            if (words.Trim().ToLowerInvariant() == "reset")
            {
                Fragmentation.Bits.DevRays = Fragmentation.Bits.DevShort = Fragmentation.Bits.DevAfloat = Fragmentation.Bits.DevLifted = 0;
                Fragmentation.Bits.DevRested = Fragmentation.Bits.DevDropped = Fragmentation.Bits.DevDroppedFlying = 0;
                Fragmentation.Bits.DevLiftMost = Fragmentation.Bits.DevReachMost = Fragmentation.Bits.DevFarMost = 0f;
                Fragmentation.Bits.DevCaught = Fragmentation.Bits.DevFlightLooks = Fragmentation.Bits.DevMissed = 0;
                Fragmentation.Bits.DevClearLifts();
                return "cleared";
            }
            var say = new StringBuilder();
            float fps = Num(words, "fps", -1f);
            if (fps >= 0f)
            {
                if (fpsWas == int.MinValue) fpsWas = Application.targetFrameRate;
                Application.targetFrameRate = fps > 0f ? Mathf.RoundToInt(fps) : fpsWas;
                say.Append("frame rate held to ").Append(Application.targetFrameRate).Append(QualitySettings.vSyncCount > 0 ? " (but vertical sync is on, which overrides it)" : "").Append("; ");
            }
            string kindWord = Word(words, "kind");
            if (kindWord.Length > 0 || Num(words, "v", -1f) >= 0f || Num(words, "n", -1f) > 0f)
            {
                Vessel v = FlightGlobals.ActiveVessel;
                if (v == null) return "nothing is being flown";
                CelestialBody body = v.mainBody;
                Vector3 there = (Vector3)v.CoMD;
                Vector3 up = (Vector3)FlightGlobals.getUpAxis(body, there);
                Vector3 north = Vector3.ProjectOnPlane(body.transform.up, up).normalized;
                float heading = Num(words, "heading", float.NaN);
                Vector3 way = float.IsNaN(heading) ? Vector3.ProjectOnPlane(v.transform.up, up) : Quaternion.AngleAxis(heading, up) * north;
                if (way.sqrMagnitude < 0.01f) way = north;
                way.Normalize();
                Vector3 over = there + way * Num(words, "ahead", 15f);
                if (!Physics.Raycast(over + up * 50f, -up, out RaycastHit hit, 500f, 1 << 15, QueryTriggerInteraction.Ignore)) return say + "no ground found ahead of the craft";
                Fragmentation.Bits.Kind kind = Fragmentation.Bits.Kind.Metal;
                if (kindWord.Length > 0)
                {
                    try { kind = (Fragmentation.Bits.Kind)Enum.Parse(typeof(Fragmentation.Bits.Kind), kindWord, true); }
                    catch (Exception) { return say + "no kind of bit called '" + kindWord + "'"; }
                }
                bool earthen = kind == Fragmentation.Bits.Kind.Clod || kind == Fragmentation.Bits.Kind.Turf;
                // (the ground there as a crash's bits would find it: paving but not the space centre's lawns, else the ground of the place)
                bool paved = Crashes.Paved(hit.collider) && !(Crashes.TellLawn && Ground.Lawn(hit.point, up));
                Ground.Feel feel = paved ? null : Crashes.TestGround >= 0 && Crashes.TestGround <= (int)Ground.Kind.Regolith ? Ground.FeelOf((Ground.Kind)Crashes.TestGround)
                    : Ground.FeelOf(Ground.KindAt(body, body.GetLatitude((Vector3d)hit.point), body.GetLongitude((Vector3d)hit.point)));
                float grip = earthen ? -1f : Fragmentation.Bits.Grip(feel, paved);
                float biggest = Num(words, "size", kind == Fragmentation.Bits.Kind.Bolt ? 0.04f : 0.15f);
                bitsSpeed = Num(words, "v", 20f);
                bitsFrom = hit.point + up * Num(words, "height", 0.2f);
                bitsWay = way; bitsUp = up;
                bitsAt = Time.time;
                bitsG = ((Vector3)FlightGlobals.getGeeForceAtPosition(bitsFrom, body)).magnitude;
                bitsFirst = Fragmentation.Bits.LastId + 1;
                Fragmentation.Bits.Emit(bitsFrom, way * bitsSpeed, 0f, Mathf.Max(1, (int)Num(words, "n", 50f)), biggest * 0.15f, biggest, kind, 0f, 0f, 0f, grip);
                bitsLast = Fragmentation.Bits.LastId;
                bitsGrip = Fragmentation.Bits.DevLastGrip;
                bitsGround = (paved ? "paving" : feel != null ? feel.Kind.ToString().ToLowerInvariant() : "?") + (bitsGrip >= 0f ? ", friction " + bitsGrip.ToString("F2") : ", its kind's own friction");
                say.Append("threw ").Append(bitsLast - bitsFirst + 1).Append(' ').Append(kind).Append(" bits at ").Append(bitsSpeed.ToString("F1")).Append(" m/s from ").Append(Num(words, "height", 0.2f).ToString("F2"))
                   .Append(" m above ").Append(bitsGround).Append(" on ").Append(body.bodyName).Append(" (gravity ").Append(bitsG.ToString("F2")).Append(" m/s2): ask again in six seconds\n");
            }
            if (bitsLast >= bitsFirst)
            {
                if (bitsSeenAt == null) { bitsSeenAt = new Vector3[1000]; bitsSeenV = new Vector3[1000]; bitsSeenDown = new bool[1000]; }
                int n = Fragmentation.Bits.Find(bitsFirst, bitsLast, bitsSeenAt, bitsSeenV, bitsSeenDown);
                var far = new float[n];
                float sum = 0f, along = 0f, speeds = 0f;
                int resting = 0;
                for (int k = 0; k < n; k++)
                {
                    Vector3 off = Vector3.ProjectOnPlane(bitsSeenAt[k] - bitsFrom, bitsUp);
                    far[k] = off.magnitude;
                    sum += far[k];
                    along += Vector3.Dot(off, bitsWay);
                    if (bitsSeenDown[k]) resting++;
                    else speeds += bitsSeenV[k].magnitude;
                }
                Array.Sort(far);
                float mu = bitsGrip >= 0f ? bitsGrip * Fragmentation.Bits.GripTimes : -1f;
                say.Append("the last throw, ").Append((Time.time - bitsAt).ToString("F1")).Append(" s ago on ").Append(bitsGround).Append(": ").Append(n).Append(" of ").Append(bitsLast - bitsFirst + 1).Append(" about, ")
                   .Append(resting).Append(" at rest");
                if (n > resting) say.Append(", the rest at ").Append((speeds / (n - resting)).ToString("F1")).Append(" m/s on average");
                if (n > 0)
                    say.Append("; from where they were thrown ").Append((sum / n).ToString("F1")).Append(" m on average, nine in ten within ").Append(far[Mathf.Min(n - 1, Mathf.CeilToInt(0.9f * n) - 1)].ToString("F1"))
                       .Append(" m, the farthest ").Append(far[n - 1].ToString("F1")).Append(" m, ").Append((along / n).ToString("F1")).Append(" m on average down the way they were thrown");
                if (mu > 0f) say.Append("; v squared over 2 mu g: ").Append((bitsSpeed * bitsSpeed / (2f * mu * bitsG)).ToString("F1")).Append(" m").Append(Fragmentation.Bits.Drag ? " (less the air's drag: bitair=0 to leave it out)" : "");
                say.Append('\n');
            }
            say.Append("frame rate ").Append((1f / Mathf.Max(1e-4f, Time.smoothDeltaTime)).ToString("F0")).Append(" a second (held to ").Append(Application.targetFrameRate).Append(", vertical sync ").Append(QualitySettings.vSyncCount).Append("); bits ")
               .Append(Fragmentation.Bits.Count).Append(" about; rays for the ground ").Append(Fragmentation.Bits.DevRays).Append(", waited for one ").Append(Fragmentation.Bits.DevShort)
               .Append(", found floating where they rested ").Append(Fragmentation.Bits.DevAfloat).Append(", sunk ").Append(Fragmentation.Bits.DevLifted).Append(" (at most ").Append(Fragmentation.Bits.DevLiftMost.ToString("F2"))
               .Append(" m), no ground found under ").Append(Fragmentation.Bits.DevMissed).Append(" (left on the ground they were given), come to rest ").Append(Fragmentation.Bits.DevRested).Append(", gone to make room ").Append(Fragmentation.Bits.DevDropped).Append(" (still flying ").Append(Fragmentation.Bits.DevDroppedFlying).Append(')')
               .Append("; the farthest a bit had gone from its ground when looked for ").Append(Fragmentation.Bits.DevFarMost.ToString("F0")).Append(" m, the longest ray ").Append(Fragmentation.Bits.DevReachMost.ToString("F0"))
               .Append(" m; looked along in flight ").Append(Fragmentation.Bits.DevFlightLooks).Append(" times, caught in a hill ").Append(Fragmentation.Bits.DevCaught);
            // (why they were sunk: lifts while sliding, and the deep ones where they came to rest, see Bits.DevDeepOne)
            say.Append("\nlifted on to the ground while sliding: as they first touched ").Append(Fragmentation.Bits.DevSlideLiftedFirst).Append(" (at most ").Append(Fragmentation.Bits.DevSlideLiftFirstMost.ToString("F2"))
               .Append(" m), looked for again ").Append(Fragmentation.Bits.DevSlideLiftedAgain).Append(" (at most ").Append(Fragmentation.Bits.DevSlideLiftAgainMost.ToString("F2")).Append(" m)");
            // (since 2026-11-04, see Bits.FirstTouch: where they would come down looked for ahead, and the rays by time)
            say.Append("\nfirst touches ").Append(Fragmentation.Bits.FirstTouch ? "looked for ahead" : "as before (bitfirst=0)").Append(": where they would come down looked for ").Append(Fragmentation.Bits.DevAhead)
               .Append(" times, ground found ").Append(Fragmentation.Bits.DevAheadFound).Append(", above the flat they had ").Append(Fragmentation.Bits.DevAheadRose).Append(" (at most ").Append(Fragmentation.Bits.DevAheadRiseMost.ToString("F2"))
               .Append(" m), above the bit itself and not taken ").Append(Fragmentation.Bits.DevAheadUnder).Append(" (at most ").Append(Fragmentation.Bits.DevAheadUnderMost.ToString("F2"))
               .Append(" m); rays kept for first touches taken ").Append(Fragmentation.Bits.DevFirstKept).Append("; the most rays in a frame ").Append(Fragmentation.Bits.DevRaysMost);
            int deep = Fragmentation.Bits.DevDeep;
            say.Append("\nsunk over 0.5 m where they rested ").Append(deep);
            if (deep > 0)
            {
                say.Append(": found by the first ray ").Append(Fragmentation.Bits.DevDeepFirst).Append(", the second ").Append(Fragmentation.Bits.DevDeepSecond).Append("; had waited for a ray ").Append(Fragmentation.Bits.DevDeepWaited)
                   .Append(", had had a ray before ").Append(Fragmentation.Bits.DevDeepFelt).Append("; from where their ground was last found ").Append((Fragmentation.Bits.DevDeepFarSum / deep).ToString("F1"))
                   .Append(" m on average, the most ").Append(Fragmentation.Bits.DevDeepFarMost.ToString("F1")).Append(" m; slid since their last ray ").Append((Fragmentation.Bits.DevDeepSlidSum / deep).ToString("F1"))
                   .Append(" m on average, the most ").Append(Fragmentation.Bits.DevDeepSlidMost.ToString("F1")).Append(" m; of kinds");
                for (int k = 0; k < Fragmentation.Bits.DevDeepKinds.Length; k++)
                    if (Fragmentation.Bits.DevDeepKinds[k] > 0) say.Append(' ').Append((Fragmentation.Bits.Kind)k).Append(' ').Append(Fragmentation.Bits.DevDeepKinds[k]);
                say.Append("; on");
                bool firstOn = true;
                foreach (KeyValuePair<string, int> on in Fragmentation.Bits.DevDeepOn) { say.Append(firstOn ? " " : ", ").Append(on.Key).Append(' ').Append(on.Value); firstOn = false; }
                foreach (string one in Fragmentation.Bits.DevDeepFirstFew) say.Append("\n  ").Append(one);
            }
            return say.ToString();
        }

        /// <summary>
        /// The crash's sounds as they were let be heard (see Sounds.Let and Sounds.Play): of each kind, crunches with thuds, clangs and
        /// tears, how many were heard, how many held back by their window, how many heard in a window for being louder than its loudest,
        /// and the most heard in any one window (no more than inwindow); blows too faint to be heard; how many times a quieter sound
        /// gave way to a louder one with all ten sources playing, and how many sounds were not played because every one playing was
        /// louder; and the last blow's energy, the mass it stopped and how loud it was. "reset": cleared, before a test.
        /// </summary>
        public static string Heard(string words)
        {
            if ((words ?? "").Trim().ToLowerInvariant() == "reset")
            {
                for (int k = 0; k < 3; k++) Sounds.DevHeard[k] = Sounds.DevHeld[k] = Sounds.DevLouder[k] = Sounds.DevMost[k] = 0;
                Sounds.DevFaint = Sounds.DevTaken = Sounds.DevDropped = 0;
                Sounds.DevEnergy = Sounds.DevMass = Sounds.DevLoud = 0f;
            }
            var say = new StringBuilder();
            string[] kinds = { "crunches", "clangs", "tears" };
            for (int k = 0; k < 3; k++)
                say.Append(kinds[k]).Append(": heard ").Append(Sounds.DevHeard[k]).Append(", held back ").Append(Sounds.DevHeld[k]).Append(", heard for being louder ")
                   .Append(Sounds.DevLouder[k]).Append(", most in one window ").Append(Sounds.DevMost[k]).Append("; ");
            say.Append("too faint ").Append(Sounds.DevFaint).Append("; with all sources playing, a quieter one given way ").Append(Sounds.DevTaken).Append(", not played ").Append(Sounds.DevDropped)
               .Append("; last blow ").Append(Sounds.DevEnergy.ToString("F1")).Append(" kJ, ").Append(Sounds.DevMass.ToString("F2")).Append(" t stopped, loudness ").Append(Sounds.DevLoud.ToString("F2"));
            return say.ToString();
        }

        /// <summary>
        /// What the last part struck took by the way it was struck (see Crashes.Capacity(h, way)): what struck it, how far off its own axis
        /// the blow came, how full it is, what it takes that way and end-on, kilojoules, and how far it is crushed; and how many blows on round
        /// parts came within 30 degrees of end-on and how many within 30 degrees of side-on since last cleared. "reset": cleared, before a test.
        /// </summary>
        public static string Facing(string words)
        {
            if ((words ?? "").Trim().ToLowerInvariant() == "reset")
            {
                Crashes.FacedLast = null;
                Crashes.FacedSide = Crashes.FacedEnd = 0;
            }
            Hits h = Crashes.FacedLast;
            string counts = "blows on round parts: " + Crashes.FacedEnd + " end-on, " + Crashes.FacedSide + " side-on";
            if (h == null || h.part == null) return "no part struck since cleared; " + counts;
            float c = Mathf.Clamp01(Mathf.Abs(Vector3.Dot(Crashes.FacedWay, h.part.transform.up)));
            float side = Crashes.SideOn(h, out float fill, out float lat);
            float whole = Crashes.Capacity(h), way = Crashes.Capacity(h, Crashes.FacedWay);
            return (h.part.partInfo != null ? h.part.partInfo.title : h.part.name) + " struck by " + Crashes.FacedBy + " " + (Mathf.Acos(c) * Mathf.Rad2Deg).ToString("F0") + " degrees off its axis"
                + (side < 1f ? ": round, side-on " + side.ToString("F3") + " of end-on, full " + fill.ToString("F2") + ", flattened by " + lat.ToString("F2") + " of the end-on force" : ": not round, the same every way")
                + "; takes " + way.ToString("F2") + " kJ that way, " + whole.ToString("F2") + " end-on; crushed " + h.crushed.ToString("F2") + ", hurt " + h.damage.ToString("F2") + " (" + h.spent.ToString("F1") + " kJ in all); " + counts;
        }

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
                // ("empty=Oxidizer": that resource (any whose name has those letters, '_' for a space) let out of it first, to see the same
                // tank go up with fuel alone; "up=0.5": alight, and going up after so many seconds, see Burst)
                string empty = Word(words ?? "", "empty");
                string emptied = "";
                if (empty.Length > 0 && p.Resources != null)
                    for (int i = 0; i < p.Resources.Count; i++)
                        if (p.Resources[i] != null && p.Resources[i].resourceName.IndexOf(empty, StringComparison.OrdinalIgnoreCase) >= 0) { p.Resources[i].amount = 0; emptied += " " + p.Resources[i].resourceName; }
                if (empty.Length > 0) emptied = emptied.Length > 0 ? " (let out:" + emptied + ")" : " (nothing in it like '" + empty + "')";
                return p.partInfo.title + emptied + ": " + Crashes.Instance.TestLeak(h, hole, Num(words, "lit", 0f) > 0.5f, Num(words, "up", -1f));
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
               .Append("; bits ").Append(Fragmentation.Bits.Count).Append(", marks ").Append(Marks.Count).Append(", pieces ").Append(Piece.Count);
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
                // (what its strength is reckoned by: its mass as built, which should not fall as its skin is torn away, though the game's mass of it does)
                if (hits != null && hits.part != null)
                    say.Append("; strength ").Append(Crashes.Capacity(hits).ToString("F2")).Append(" kJ beyond its tolerance, crushed ").Append(hits.crushed.ToString("F2")).Append(", skin ").Append(Crashes.SkinToughness(hits).ToString("F1"))
                       .Append(" kJ/m2, built ").Append((Crashes.FullMass(hits) * 1000f).ToString("F1")).Append(" kg, now ").Append((p.prefabMass * 1000f).ToString("F1")).Append(" kg");
                // (and side-on, as a share of end-on, by how full it is: 1 for a part that is not round, see Crashes.Capacity(h, way))
                if (hits != null && hits.part != null)
                {
                    float side = Crashes.SideOn(hits, out float fill, out float lat);
                    if (side < 1f) say.Append("; side-on ").Append(side.ToString("F3")).Append(" of end-on (full ").Append(fill.ToString("F2")).Append(", flattened by ").Append(lat.ToString("F2")).Append(" of the end-on force)");
                    else say.Append("; the same every way");
                }
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

        /// <summary>
        /// (Since 0.5.0.) What a part of the craft being flown is drawn with: "part=Mk1_Command" (the first whose title has those letters; "all":
        /// every part with seats). Each mesh, whether its skin is torn by this mod (HullPlan.Drawn) and each material's shader and queue: what is
        /// not torn (glass) stays drawn as it is while the part is.
        /// </summary>
        public static string Looks(string words)
        {
            Vessel v = FlightGlobals.ActiveVessel;
            if (v == null) return "nothing is being flown";
            string want = Word(words ?? "", "part");
            bool all = (words ?? "").Contains("all");
            var say = new StringBuilder();
            foreach (Part p in v.parts)
            {
                if (p == null || p.partInfo == null) continue;
                if (all ? p.CrewCapacity <= 0 : (want.Length > 0 && p.partInfo.title.IndexOf(want, StringComparison.OrdinalIgnoreCase) < 0)) continue;
                List<MeshFilter> torn = HullPlan.Drawn(p);
                say.Append(p.partInfo.title).Append(" (").Append(p.partInfo.name).Append(", crew ").Append(p.protoModuleCrew.Count).Append('/').Append(p.CrewCapacity).Append(", tolerance ").Append(p.crashTolerance.ToString("F0")).Append("):\n");
                foreach (MeshRenderer r in p.FindModelComponents<MeshRenderer>())
                {
                    if (r == null) continue;
                    MeshFilter f = r.GetComponent<MeshFilter>();
                    say.Append("  ").Append(r.name).Append(r.enabled && r.gameObject.activeInHierarchy ? "" : " (off)").Append(f != null && torn.Contains(f) ? " torn" : " not torn").Append(": ");
                    foreach (Material m in r.sharedMaterials)
                        say.Append(m == null ? "none" : m.name + " [" + (m.shader != null ? m.shader.name : "?") + ", queue " + m.renderQueue + (HullPlan.Solid(m) ? "" : ", see-through") + "]").Append("; ");
                    say.Append('\n');
                }
                if (!all && want.Length > 0) break;
            }
            return say.Length > 0 ? say.ToString() : "no part like '" + want + "'";
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
            float farthest = 0f, nearest = float.MaxValue, speeds = 0f, fastest = 0f; int pieces = 0, moving = 0;
            // (their speeds over the ground: what their bodies have, with the game's floating frame added back, as it moves with the craft above some 750 m/s)
            Vector3 frame = (Vector3)Krakensbane.GetFrameVelocity();
            foreach (Piece pc in Piece.All)
            {
                if (pc == null) continue;
                pieces++;
                float d = (float)((Vector3d)pc.transform.position - body.GetWorldSurfacePosition(lat, lon, body.GetAltitude(pc.transform.position))).magnitude;
                farthest = Mathf.Max(farthest, d);
                nearest = Mathf.Min(nearest, d);
                if (pc.rb != null && !pc.resting)
                {
                    float speed = (pc.rb.velocity + frame).magnitude;
                    moving++; speeds += speed; fastest = Mathf.Max(fastest, speed);
                }
            }
            say.Append(pieces).Append(" pieces, the nearest ").Append(pieces > 0 ? nearest.ToString("F0") : "-").Append(" m, the farthest ").Append(farthest.ToString("F0")).Append(" m; ")
               .Append(moving).Append(" moving, at ").Append(moving > 0 ? (speeds / moving).ToString("F1") : "-").Append(" m/s on average, the fastest ").Append(fastest.ToString("F1")).Append(" m/s; ").Append(Fragmentation.Bits.Count).Append(" bits");
            say.Append('\n').Append(Field(body, lat, lon));
            return say.ToString();
        }

        /// <summary>
        /// The field the pieces at rest lie in, from where the first craft touched: how long it is along the way that craft was going as it
        /// touched and how wide across it (from the nearest to the farthest, and one standard deviation each way, which a stray piece moves
        /// less), how far down the track they lie on average, and on average by their mass (further down than the plain average when the
        /// heavy pieces carried furthest).
        /// </summary>
        static string Field(CelestialBody body, double lat, double lon)
        {
            if (!touchKnown || !touchWayKnown || body != touchBody) return "(no way known along the track: the field is not reckoned)";
            int n = 0;
            double sumA = 0, sumAA = 0, sumC = 0, sumCC = 0, sumM = 0, sumMA = 0, loA = double.MaxValue, hiA = double.MinValue, loC = double.MaxValue, hiC = double.MinValue;
            foreach (Piece pc in Piece.All)
            {
                if (pc == null || !pc.resting) continue;
                double alt = body.GetAltitude(pc.transform.position);
                Vector3d from = body.GetWorldSurfacePosition(lat, lon, alt);
                Vector3d up = (from - body.position).normalized;
                Vector3d way = body.GetWorldSurfacePosition(touchAheadLat, touchAheadLon, alt) - from;
                way = (way - up * Vector3d.Dot(way, up)).normalized;
                Vector3d across = Vector3d.Cross(up, way);
                Vector3d d = (Vector3d)pc.transform.position - from;
                double a = Vector3d.Dot(d, way), c = Vector3d.Dot(d, across);
                n++;
                sumA += a; sumAA += a * a; sumC += c; sumCC += c * c; sumM += pc.mass; sumMA += pc.mass * a;
                loA = Math.Min(loA, a); hiA = Math.Max(hiA, a); loC = Math.Min(loC, c); hiC = Math.Max(hiC, c);
            }
            if (n == 0) return "no pieces at rest: no field yet";
            double meanA = sumA / n, meanC = sumC / n;
            double sdA = Math.Sqrt(Math.Max(0, sumAA / n - meanA * meanA)), sdC = Math.Sqrt(Math.Max(0, sumCC / n - meanC * meanC));
            double length = hiA - loA, width = hiC - loC;
            return "field of the " + n + " pieces at rest: " + length.ToString("F0") + " m along the track by " + width.ToString("F0") + " m across (" + (length / Math.Max(0.1, width)).ToString("F2") +
                " to 1), one deviation " + sdA.ToString("F1") + " m along by " + sdC.ToString("F1") + " m across (" + (sdA / Math.Max(0.1, sdC)).ToString("F2") + " to 1); on average " + meanA.ToString("F0") +
                " m down the track, by their mass " + (sumM > 0 ? (sumMA / sumM).ToString("F0") : "-") + " m";
        }

        static CelestialBody touchBody;
        static double touchLat, touchLon;
        static bool touchKnown;
        // (and a point a hundred metres ahead of it along the ground, the way it was going as it touched: the way of the track, kept as a
        // place on the ground so that it holds however the game moves its frame)
        static double touchAheadLat, touchAheadLon;
        static bool touchWayKnown;

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
                // (the way it was going over the ground; if it was hardly going any way, the way from where the track began to here)
                Vector3d up = (w.vessel.CoMD - touchBody.position).normalized;
                Vector3d way = w.vessel.srf_velocity;
                way -= up * Vector3d.Dot(way, up);
                if (way.magnitude < 1 && trackBody == touchBody)
                {
                    way = w.vessel.CoMD - trackBody.GetWorldSurfacePosition(trackLat, trackLon, w.vessel.altitude);
                    way -= up * Vector3d.Dot(way, up);
                }
                touchWayKnown = way.magnitude > 0.5;
                if (touchWayKnown)
                {
                    Vector3d ahead = w.vessel.CoMD + way.normalized * 100.0;
                    touchAheadLat = touchBody.GetLatitude(ahead); touchAheadLon = touchBody.GetLongitude(ahead);
                }
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

        /// <summary>Start (or with "stop", end and read) a record of the heaviest craft near: seconds, metres from where it was when this began (along the ground), speed, height, how fast it turns, parts, pieces.</summary>
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
                 .Append(best.radarAltitude.ToString("F1")).Append(" m up ").Append(best.angularVelocity.magnitude.ToString("F2")).Append(" rad/s ").Append(best.parts.Count).Append(" parts ").Append(Piece.Count).Append(" pieces ").Append(best.GetDisplayName()).Append('\n');
            if (track.Length > 60000) tracking = false;
        }
    }
}
#endif
