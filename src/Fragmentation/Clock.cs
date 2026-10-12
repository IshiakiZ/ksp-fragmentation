#if DEV
using System;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace Fragmentation
{
    /// <summary>
    /// For the development build: where each frame's time goes, frame by frame, through a crash. The game's own timing
    /// stages (TimingManager) run a callback first and last in every step of the physics, first in every frame's Update
    /// and last in its LateUpdate; the physics itself is simulated after the last of a step's callbacks, and the frame is
    /// drawn after the last LateUpdate. So, frame by frame: the scripts of the physics steps (the game's own and every
    /// mod's), the physics, the frame's scripts, and the drawing of the frame before (with the wait for the screen). With
    /// it, this mod's own share, the explosions mod's, the collections of the garbage collector, and what is in the scene.
    /// </summary>
    public static class Clock
    {
        struct Row
        {
            public int frame, steps, gc, notes, pieces, vessels, sites, particles;
            public float at, dt, fixedMs, physicsMs, updateMs, drawMs, mineMs, breakMs, askedMs, explosionsMs;
        }

        static readonly Row[] rows = new Row[3000];
        static readonly float[,] parts = new float[3000, Prof.Ticks.Length];
        static readonly long[] partsBefore = new long[Prof.Ticks.Length];
        static readonly float[,] theirs = new float[3000, 16];
        static readonly string[] theirNames = { "dead", "turn aside", "rest", "sprites", "frame", "blasts", "air", "fires", "pieces", "look ahead", "grid over", "tune", "glows", "start", "place", "marks" };
        static int count;
        static bool on, hooked;
        static long fixedBegan, fixedEnded, updateBegan, lateEnded;
        static int phase;                       // 1: in a step's scripts, 2: after a step, 3: in the frame's scripts, 4: after the frame
        static Row now;
        static float began;
        static int gcBefore;
        static long mineBefore, breakBefore, askedBefore;
        static FieldInfo sitesField;
        public static int NotesThisFrame;
        /// <summary>When a craft first touched anything since the timing began (seconds after it began; less than nought: not yet).</summary>
        public static float TouchAt = -1f;

        /// <summary>A craft being watched touched something in this step: the crash proper begins here (see Read).</summary>
        public static void Touched() { if (on && TouchAt < 0f) TouchAt = Time.unscaledTime - began; }
        static readonly double Ms = 1000.0 / System.Diagnostics.Stopwatch.Frequency;

        public static void Start()
        {
            count = 0;
            began = Time.unscaledTime;
            gcBefore = GC.CollectionCount(0);
            mineBefore = Crashes.FixedTicks; breakBefore = Crashes.BreakTicks; askedBefore = Crashes.AskedTicks;
            for (int k = 0; k < partsBefore.Length; k++) partsBefore[k] = Prof.Ticks[k];
            now = default;
            phase = 0;
            TouchAt = -1f;
            on = true;
            if (!hooked)
            {
                TimingManager.FixedUpdateAdd(TimingManager.TimingStage.ObscenelyEarly, FixedBegin);
                TimingManager.FixedUpdateAdd(TimingManager.TimingStage.BetterLateThanNever, FixedEnd);
                TimingManager.UpdateAdd(TimingManager.TimingStage.ObscenelyEarly, UpdateBegin);
                TimingManager.LateUpdateAdd(TimingManager.TimingStage.BetterLateThanNever, LateEnd);
                hooked = true;
            }
        }

        public static void Stop()
        {
            on = false;
            if (!hooked) return;
            TimingManager.FixedUpdateRemove(TimingManager.TimingStage.ObscenelyEarly, FixedBegin);
            TimingManager.FixedUpdateRemove(TimingManager.TimingStage.BetterLateThanNever, FixedEnd);
            TimingManager.UpdateRemove(TimingManager.TimingStage.ObscenelyEarly, UpdateBegin);
            TimingManager.LateUpdateRemove(TimingManager.TimingStage.BetterLateThanNever, LateEnd);
            hooked = false;
        }

        static long Now => System.Diagnostics.Stopwatch.GetTimestamp();

        static void FixedBegin()
        {
            if (!on) return;
            long t = Now;
            if (phase == 2) now.physicsMs += (float)((t - fixedEnded) * Ms);
            else if (phase == 4) now.drawMs += (float)((t - lateEnded) * Ms);
            fixedBegan = t;
            phase = 1;
        }

        static void FixedEnd()
        {
            if (!on) return;
            long t = Now;
            if (phase == 1) { now.fixedMs += (float)((t - fixedBegan) * Ms); now.steps++; }
            fixedEnded = t;
            phase = 2;
        }

        static void UpdateBegin()
        {
            if (!on) return;
            long t = Now;
            if (phase == 2) now.physicsMs += (float)((t - fixedEnded) * Ms);
            else if (phase == 4) now.drawMs += (float)((t - lateEnded) * Ms);
            updateBegan = t;
            phase = 3;
        }

        static void LateEnd()
        {
            if (!on) return;
            long t = Now;
            if (phase == 3) now.updateMs += (float)((t - updateBegan) * Ms);
            lateEnded = t;
            phase = 4;
            now.frame = Time.frameCount;
            now.at = Time.unscaledTime - began;
            now.dt = Time.unscaledDeltaTime * 1000f;
            int gc = GC.CollectionCount(0);
            now.gc = gc - gcBefore; gcBefore = gc;
            now.mineMs = (float)((Crashes.FixedTicks - mineBefore) * Ms); mineBefore = Crashes.FixedTicks;
            now.breakMs = (float)((Crashes.BreakTicks - breakBefore) * Ms); breakBefore = Crashes.BreakTicks;
            now.askedMs = (float)((Crashes.AskedTicks - askedBefore) * Ms); askedBefore = Crashes.AskedTicks;
            now.notes = NotesThisFrame; NotesThisFrame = 0;
            now.pieces = Piece.Count;
            now.vessels = FlightGlobals.VesselsLoaded != null ? FlightGlobals.VesselsLoaded.Count : 0;
            try
            {
                now.explosionsMs = VolumetricExplosions.Air.LastMs;
                now.particles = VolumetricExplosions.Air.Alive;
                if (sitesField == null) sitesField = typeof(VolumetricExplosions.Air).GetField("sites", BindingFlags.NonPublic | BindingFlags.Instance);
                var air = VolumetricExplosions.Air.Instance;
                now.sites = air != null && sitesField != null && sitesField.GetValue(air) is System.Collections.ICollection list ? list.Count : 0;
            }
            catch (Exception) { }
            if (count < rows.Length)
            {
                for (int k = 0; k < partsBefore.Length; k++) { parts[count, k] = (float)((Prof.Ticks[k] - partsBefore[k]) * Ms); partsBefore[k] = Prof.Ticks[k]; }
                // (the explosions mod's own parts of this frame's work: it counts them afresh each frame, see Air.Part)
                try { long[] vp = VolumetricExplosions.Air.Part; for (int k = 0; k < 16 && k < vp.Length; k++) theirs[count, k] = (float)(vp[k] * Ms); } catch (Exception) { }
                rows[count++] = now;
            }
            now = default;
        }

        /// <summary>A summary second by second, the twelve worst frames in full, and (all) every frame.</summary>
        public static string Read(bool all)
        {
            var say = new StringBuilder();
            say.Append(count).Append(" frames timed").Append(on ? " (still timing)" : "").Append(". Columns: ms a frame; steps; the steps' scripts; physics; the frame's scripts; drawing (the frame before); this mod (of which breaking up); telling the explosions mod; the explosions mod; collections; notes; pieces; craft; patches of air; particles\n");
            int second = -1;
            float sum = 0f, worst = 0f; int n = 0;
            var partSum = new float[partsBefore.Length];
            for (int k = 0; k <= count; k++)
            {
                int s = k < count ? Mathf.FloorToInt(rows[k].at) : int.MaxValue;
                if (s != second && n > 0)
                {
                    say.Append("second ").Append(second).Append(": ").Append(n).Append(" frames, ").Append((sum / n).ToString("F1")).Append(" ms on average (").Append((1000f * n / Mathf.Max(1f, sum)).ToString("F1")).Append(" fps), worst ").Append(worst.ToString("F1"));
                    say.Append(" | mine a frame:");
                    for (int q = 0; q < partSum.Length; q++) if (partSum[q] / n >= 0.05f) say.Append(' ').Append(Prof.Names[q]).Append(' ').Append((partSum[q] / n).ToString("F2"));
                    say.Append('\n');
                    sum = 0f; worst = 0f; n = 0;
                    for (int q = 0; q < partSum.Length; q++) partSum[q] = 0f;
                }
                if (k == count) break;
                second = s;
                sum += rows[k].dt; worst = Mathf.Max(worst, rows[k].dt); n++;
                for (int q = 0; q < partSum.Length; q++) partSum[q] += parts[k, q];
            }
            // (and counted from the first touch: the crash itself, whatever the flight before it took)
            if (TouchAt >= 0f)
            {
                say.Append("first touch ").Append(TouchAt.ToString("F2")).Append(" s in.");
                float[] edges = { 0f, 2f, 4f, 7f, 10f };
                for (int e = 0; e + 1 < edges.Length; e++)
                {
                    float total = 0f, most = 0f; int frames = 0;
                    for (int k = 0; k < count; k++)
                    {
                        float after = rows[k].at - TouchAt;
                        if (after < edges[e] || after >= edges[e + 1]) continue;
                        total += rows[k].dt; most = Mathf.Max(most, rows[k].dt); frames++;
                    }
                    if (frames > 0) say.Append(" From ").Append(edges[e].ToString("F0")).Append(" to ").Append(edges[e + 1].ToString("F0")).Append(" s after it: ").Append(frames).Append(" frames, ")
                                       .Append((1000f * frames / Mathf.Max(1f, total)).ToString("F1")).Append(" fps, worst ").Append(most.ToString("F0")).Append(" ms.");
                }
                say.Append('\n');
            }
            var order = new int[count];
            for (int k = 0; k < count; k++) order[k] = k;
            Array.Sort(order, (a, b) => rows[b].dt.CompareTo(rows[a].dt));
            say.Append("worst frames:\n");
            for (int k = 0; k < Mathf.Min(12, count); k++) { Line(say, rows[order[k]]); Parts(say, order[k]); }
            if (all) { say.Append("every frame:\n"); for (int k = 0; k < count; k++) Line(say, rows[k]); }
            return say.ToString();
        }

        /// <summary>This mod's time in that frame, part by part (those over a tenth of a millisecond).</summary>
        static void Parts(StringBuilder say, int row)
        {
            say.Append("      mine:");
            for (int k = 0; k < partsBefore.Length; k++)
                if (parts[row, k] >= 0.1f) say.Append(' ').Append(Prof.Names[k]).Append(' ').Append(parts[row, k].ToString("F1"));
            say.Append(";  explosions:");
            for (int k = 0; k < 16; k++)
                if (theirs[row, k] >= 0.5f) say.Append(' ').Append(theirNames[k]).Append(' ').Append(theirs[row, k].ToString("F1"));
            say.Append('\n');
        }

        static void Line(StringBuilder say, Row r)
        {
            say.Append(r.at.ToString("F2")).Append(" s: ").Append(r.dt.ToString("F1")).Append(" | ").Append(r.steps).Append(" | ").Append(r.fixedMs.ToString("F1")).Append(" | ").Append(r.physicsMs.ToString("F1"))
               .Append(" | ").Append(r.updateMs.ToString("F1")).Append(" | ").Append(r.drawMs.ToString("F1")).Append(" | ").Append(r.mineMs.ToString("F1")).Append(" (").Append(r.breakMs.ToString("F1")).Append(") | ")
               .Append(r.askedMs.ToString("F1")).Append(" | ").Append(r.explosionsMs.ToString("F1")).Append(" | ").Append(r.gc).Append(" | ").Append(r.notes).Append(" | ").Append(r.pieces).Append(" | ").Append(r.vessels)
               .Append(" | ").Append(r.sites).Append(" | ").Append(r.particles).Append('\n');
        }
    }
}
#endif
