using System;
using System.Collections.Generic;
using Keystone;
using UnityEngine;

namespace Fragmentation
{
    /// <summary>
    /// (Since 0.3.0.) What a crash leaves, kept in the game's own save, so that a quicksave and a quickload, or leaving the flight
    /// and coming back to it, find the wreck as it was: the marks on the ground (scars and scrapes, craters, fuel stains: see
    /// Marks.Save), and the torn skin of every part of a wreck still there (see Hull.Saved: which panels are gone, where it was
    /// crushed and dented, what is bent back and burnt). Written whenever the game saves the flight, read when it loads one, and done
    /// again once the ground under the marks and each part's own shape are there. The loose pieces thrown off are not kept: they are
    /// gone when the flight is left, as they go in time anyway.
    /// </summary>
    [KSPScenario(ScenarioCreationOptions.AddToAllGames, GameScenes.FLIGHT)]
    public sealed class Kept : ScenarioModule
    {
        // (the torn skins read from the save and not yet done again: each waits for its part to be loaded and its shape cut)
        static readonly List<ConfigNode> waiting = new List<ConfigNode>();
        static float waitingSince;

        public override void OnSave(ConfigNode node)
        {
            try { Marks.Save(node); }
            catch (Exception ex) { Kit.Log("Fragmentation", "the marks on the ground could not be saved: " + ex.Message); }
            try { SaveHulls(node); }
            catch (Exception ex) { Kit.Log("Fragmentation", "the torn parts could not be saved: " + ex.Message); }
        }

        public override void OnLoad(ConfigNode node)
        {
            try { Marks.Load(node); }
            catch (Exception ex) { Kit.Log("Fragmentation", "the marks on the ground could not be read from the save: " + ex.Message); }
            LoadHulls(node);
        }

        /// <summary>Every torn part's skin near (the craft loaded), and those read from the save that were not near enough to be done again yet.</summary>
        public static void SaveHulls(ConfigNode node)
        {
            var saved = new HashSet<uint>();
            foreach (Vessel v in FlightGlobals.VesselsLoaded)
            {
                if (v == null) continue;
                foreach (Part p in v.parts)
                {
                    Hull h = Hull.Of(p, false);
                    ConfigNode n = h != null ? h.Saved() : null;
                    if (n == null) continue;
                    node.AddNode(n);
                    saved.Add(p.persistentId);
                }
            }
            foreach (ConfigNode n in waiting)
                if (uint.TryParse(n.GetValue("part"), out uint id) && !saved.Contains(id)) node.AddNode(n);
        }

        public static void LoadHulls(ConfigNode node)
        {
            waiting.Clear();
            if (node != null) waiting.AddRange(node.GetNodes("HULL"));
            waitingSince = Time.unscaledTime;
        }

        /// <summary>Every frame while any wait: each torn part's skin done again once its part is loaded and its shape cut (given up after two minutes).</summary>
        public static void Restore()
        {
            if (waiting.Count == 0) return;
            if (Time.unscaledTime - waitingSince > 120f) return;              // (kept in the list, and so in the next save, all the same)
            for (int i = waiting.Count - 1; i >= 0; i--)
            {
                if (!uint.TryParse(waiting[i].GetValue("part"), out uint id)) { waiting.RemoveAt(i); continue; }
                Part p = Find(id);
                if (p == null) continue;
                Hull h = Hull.Of(p);
                if (h == null) continue;                                       // (its shape is still being cut)
                if (h.Restore(waiting[i])) restored++;
                waiting.RemoveAt(i);
            }
        }

        internal static int restored;

        static Part Find(uint id)
        {
            foreach (Vessel v in FlightGlobals.VesselsLoaded)
                if (v != null) foreach (Part p in v.parts) if (p != null && p.persistentId == id) return p;
            return null;
        }

        /// <summary>(For the development build's readout.) How many torn parts wait to be done again, and how many have been.</summary>
        public static string Said => waiting.Count + " torn parts waiting, " + restored + " done again";
    }
}
