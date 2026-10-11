using System;
using System.Collections.Generic;
using UnityEngine;

namespace Fragmentation
{
    /// <summary>What the crashes put into the air of the Volumetric Explosions mod.</summary>
    public sealed partial class Crashes
    {
        public static bool FeedLeaks = true, FeedMovers = true, FeedTrails = true, FeedDust = true, FeedFire = true, RideFires = false;
        readonly List<Piece> moving = new List<Piece>();
        readonly List<Piece> smoking = new List<Piece>();
        static readonly Comparison<Piece> Bigger = (a, b) => b.reach.CompareTo(a.reach);
        static readonly Comparison<Piece> Smokier = (a, b) => (b.burns > 0f ? 2f : b.smoke).CompareTo(a.burns > 0f ? 2f : a.smoke);
        readonly HashSet<Vessel> riding = new HashSet<Vessel>();
        const int MostFires = 1;
        const int MostMovers = 40;           // pieces said to the explosions mod as moving through its air (it takes 48 a frame, and keeps wakes for 16 in each patch of air)
        Action<int, Vector3, Vector3, float, float, float> sayBit;

        /// <summary>
        /// Once a frame, when the Volumetric Explosions mod asks what is burning: each leak and each burning wreck (as flame and smoke going
        /// along with the wreck, as an engine's do), the smoke of every piece and every smoking bit (laid into the patch of air it is in), and
        /// the dust of each craft sliding over soil.
        /// </summary>
        void Asked()
        {
            if (Frag.On == null || !Frag.On || TestOff || !Frag.Fires || !Feed.Ready()) return;
            long began = System.Diagnostics.Stopwatch.GetTimestamp();
            try { Say(); }
            finally { AskedTicks += System.Diagnostics.Stopwatch.GetTimestamp() - began; }
        }

        // Where tanks went up lately (in the scene's axes, kept as places on the planet: the scene shifts), and when.
        struct Blown { public CelestialBody body; public double lat, lon, alt; public float at, reach; }
        readonly List<Blown> bursts = new List<Blown>();

        void Blasted(Vector3 at, float reach)
        {
            CelestialBody body = FlightGlobals.currentMainBody;
            if (body == null) return;
            if (bursts.Count > 16) bursts.RemoveAt(0);
            bursts.Add(new Blown { body = body, lat = body.GetLatitude(at), lon = body.GetLongitude(at), alt = body.GetAltitude(at), at = Time.time, reach = reach });
        }

        void Say()
        {
            float t = Time.time;
            Vector3d frame = Krakensbane.GetFrameVelocity();
            // ---- whose flames are drawn as an engine's are, going along with the craft (RideFires): one craft's at most (the biggest fire's, or
            // failing that a burning leak's), since each such is a patch of air of its own, worked out and drawn every frame, and its smoke begins
            // more patches along the way. As installed, none: every fire and leak burns as puffs of flame and smoke laid into the air it is in (see
            // Trails), which costs next to nothing and begins no patch (measured on 2026-10-09: four patches against one, 30 ms a frame more).
            riding.Clear();
            Vessel chosen = null;
            float biggest = 0f;
            if (RideFires)
            {
                foreach (Wreck w in wrecks.Values)
                    if (w.vessel != null && !w.vessel.packed && w.fireUntil > t && t - w.fireBegan >= 0.35f && w.fireBig > biggest) { biggest = w.fireBig; chosen = w.vessel; }
                if (chosen == null)
                    foreach (Leak leak in leaks)
                        if (leak.lit && leak.h != null && leak.h.part != null && leak.h.part.vessel != null) { chosen = leak.h.part.vessel; break; }
            }
            // ---- leaks: the flame or the spray of each, out of the tear and up
            if (FeedLeaks) foreach (Leak leak in leaks)
            {
                Hits h = leak.h;
                if (h == null || h.part == null || h.gone) continue;
                Part p = h.part;
                Vessel v = p.vessel;
                if (v == null) continue;
                Vector3 at = p.transform.TransformPoint(leak.local), way = p.transform.TransformDirection(leak.dir).normalized;
                Vector3 up = (Vector3)FlightGlobals.getUpAxis(v.mainBody, at);
                Rigidbody rb = BodyOf(p);
                Vector3d going = (rb != null ? (Vector3d)rb.velocity : v.srf_velocity - frame) + frame;
                bool air = v.staticPressurekPa > 1.0;
                float size = Mathf.Clamp(p.prefabSize.magnitude * 0.08f, 0.1f, 0.5f);
                float strength = Mathf.Clamp01(leak.flow * 2f + 0.2f);
                // (a tear on the underside sprays out sideways along the ground and rises, not down into the ground)
                Vector3 side = Vector3.ProjectOnPlane(way, up);
                if (side.sqrMagnitude < 0.04f) side = Vector3.ProjectOnPlane(p.transform.right, up);
                Vector3 along = Vector3.Dot(way, up) < 0f ? (side.normalized * 0.75f + up * 0.6f).normalized : (way + up * 0.35f).normalized;
                if (leak.lit && v == chosen)
                {
                    riding.Add(v);
                    // (a burning leak is a jet: fuel driven out of the split under pressure)
                    Feed.Burn((int)p.flightID * 7 + 3, at + way * 0.2f, along, going, size, 2.5f + 6f * size, strength, air ? 0.35f * strength : 0f, leak.shade, leak.warm, 11f, leak.tint, 0.75f * strength, v.transform, Feed.Jet);
                }
                else if (!noTrails)
                {
                    // (a burning leak on another craft: flame and smoke laid where it goes; one not alight: a white spray of vapour)
                    try { Feed.Trail((int)p.flightID * 7 + 3, at + along * (0.3f + size), going, size * 1.5f, leak.lit ? 0.7f : 0.3f * strength, leak.lit ? 0.8f * strength : 0f, leak.lit ? leak.shade : 0.82f, leak.lit ? leak.warm : 0f, leak.tint, leak.lit ? 10f : 5f); }
                    catch (Exception) { noTrails = true; }
                }
            }
            // ---- the fire on a wreck whose tank went up: it goes on with the wreck as it slides, flame and black smoke streaming behind it,
            // and dies down over twenty seconds or so
            if (FeedFire)
                foreach (Wreck w in wrecks.Values)
                {
                    Vessel v = w.vessel;
                    // (from a moment after the blast: its own fire is the fire at first, and the explosions mod has its patch of air to begin then)
                    if (v == null || v.packed || w.fireUntil <= t || t - w.fireBegan < 0.35f) continue;
                    Part on = w.firePart != null && w.firePart.vessel == v && w.firePart.State != PartStates.DEAD ? w.firePart : null;
                    Vector3 at = on != null ? on.transform.TransformPoint(w.fireLocal) : v.CoM;
                    float left = Mathf.Clamp01((w.fireUntil - t) / 6f) * Mathf.Clamp01((t - w.fireBegan) / 0.4f + 0.3f);
                    Vector3 up = (Vector3)FlightGlobals.getUpAxis(v.mainBody, at);
                    Rigidbody rb = on != null ? BodyOf(on) : v.rootPart != null ? v.rootPart.rb : null;
                    Vector3d going = (rb != null ? (Vector3d)rb.velocity : v.srf_velocity - frame) + frame;
                    float size = Mathf.Clamp(w.fireBig * 0.14f, 0.4f, 1.8f);
                    if (v == chosen)
                    {
                        // (the flame leans back from the way the wreck is going, the faster the more)
                        Vector3 back = -(Vector3)going;
                        float speed = back.magnitude;
                        Vector3 along = (up + (speed > 0.5f ? back / speed * Mathf.Min(1.5f, speed / 15f) : Vector3.zero)).normalized;
                        riding.Add(v);
                        // (a wreck on fire has no jet: its flame goes with it, and its smoke rises off it as a plume; laid at the burning part
                        // itself, which the explosions mod now draws as flames on the hull: lifted half a size, it stood off the wreck, and the
                        // user saw it "just above the rocket. It's not actually on the rocket")
                        Feed.Burn((int)v.persistentId * 13 + 9, at, along, going, size, size * 4.5f, 0.35f * left, 0.75f * left, 0.05f, 0.3f, 16f, 0, 0.8f * left, v.transform, Feed.Burning);
                    }
                    else if (!noTrails)
                    {
                        try { Feed.Trail((int)v.persistentId * 13 + 9, at + up * (size * 0.2f), going, size, Mathf.Max(0.5f, left), 0.85f * left, 0.05f, 0.3f, 0, 14f); }
                        catch (Exception) { noTrails = true; }
                    }
                }
            // ---- every piece trails a little smoke: a wisp for a few seconds off one torn out of the ground, smoke off one that came out of a
            // fire, flame and smoke off one that burns (each laid into the patch of air it is in, if there is one: none is begun for it)
            if (FeedTrails && !noTrails)
            {
                try
                {
                    smoking.Clear();
                    foreach (Piece piece in Piece.All)
                        if (piece != null && piece.rb != null && (piece.burns > 0f || (piece.smokeUntil > t && piece.smoke > 0f))) smoking.Add(piece);
                    if (smoking.Count > 140) smoking.Sort(Smokier);
                    for (int k = 0; k < smoking.Count && k < 140; k++)
                    {
                        Piece piece = smoking[k];
                        Vector3d going = (Vector3d)piece.rb.velocity + frame;
                        bool fire = piece.burns > 0f;
                        float fading = Mathf.Clamp01((piece.smokeUntil - t) / 2f);
                        float smoke = fire ? Mathf.Max(0.5f, piece.smoke) : piece.smoke * fading * (piece.resting ? 0.5f : 1f);
                        Feed.Trail(900000 + piece.id, piece.transform.position, going, Mathf.Clamp(piece.reach * 0.35f, 0.06f, 0.6f), smoke, fire ? Mathf.Clamp01(piece.burns / 3f) : 0f,
                                   fire ? 0.06f : piece.shade, piece.warm, piece.tint, fire ? 8f : 6f);
                    }
                    if (sayBit == null) sayBit = (id, at, v, size, smoke, fire) => Feed.Trail(1800000 + id, at, (Vector3d)v + Krakensbane.GetFrameVelocity(), size, smoke, fire, fire > 0f ? 0.06f : 0.25f, 0.3f, 0, 4f);
                    Bits.Smoking(sayBit, 100);
                }
                catch (Exception) { noTrails = true; }
            }
            // ---- pieces moving through the air: the biggest push the smoke aside as they go (an explosions mod too old to take them is not asked again)
            if (!noMovers && FeedMovers)
            {
                moving.Clear();
                foreach (Piece piece in Piece.All) if (piece != null && !piece.resting && piece.rb != null && piece.reach > 0.12f && piece.rb.velocity.sqrMagnitude > 4f) moving.Add(piece);
                if (moving.Count > MostMovers) moving.Sort(Bigger);
                try
                {
                    for (int k = 0; k < moving.Count && k < MostMovers; k++)
                        Feed.Moving(moving[k].transform.position, (Vector3d)moving[k].rb.velocity + frame, Mathf.Max(0.2f, moving[k].reach * 0.8f));
                }
                catch (Exception) { noMovers = true; }
            }
            // ---- the dust and earth a craft throws up sliding over soil: brown, in the patch of air it is in (none off paving: what a craft
            // dragged along concrete throws up is grit, not a cloud)
            if (FeedDust && !noTrails)
                foreach (Wreck w in wrecks.Values)
                {
                    Vessel v = w.vessel;
                    if (v == null || v.packed || !w.inContact || w.dustSpeed < 6f || w.dustPaved) continue;
                    float much = Mathf.Clamp01((w.dustSpeed - 6f) / 40f);
                    try
                    {
                        Feed.Trail((int)v.persistentId * 13 + 5, w.dustAt + w.dustNormal * 0.4f, (Vector3d)(w.dustGoing * 0.3f), w.dustSize * 0.6f, 0.25f + 0.5f * much, 0f, 0.3f, 0.85f, 0, 10f);
                    }
                    catch (Exception) { noTrails = true; }
                }
        }
    }
}
