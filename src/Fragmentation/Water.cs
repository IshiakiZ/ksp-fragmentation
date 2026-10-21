using System;
using UnityEngine;

namespace Fragmentation
{
    /// <summary>
    /// Where the water stands (since 0.3.0): the Natural Ocean mod's waves where it is installed (looked up by name: this mod does not
    /// need it), the sea's level otherwise; and how deep a place is in it.
    /// </summary>
    public static class Water
    {
        static Func<Vector3d, float> waves;
        static bool looked, keystones = true;
        static bool Keystones(Vector3d world, out float height)
        {
            bool there = Keystone.Water.Provider != null;
            height = there ? Keystone.Water.HeightAt(world) : 0f;
            return there;
        }

        /// <summary>How far under the water a place is (metres; less than nothing, over it). Nothing where the world has no sea.</summary>
        public static float Depth(Vector3d world)
        {
            CelestialBody body = FlightGlobals.currentMainBody;
            if (body == null || !body.ocean) return -1e9f;
            double alt = (world - body.position).magnitude - body.Radius;
            return Height(world) - (float)alt;
        }

        /// <summary>How high the water stands at a place over the sea's level (metres): the waves, where the Natural Ocean mod draws them.</summary>
        public static float Height(Vector3d world)
        {
            // (Keystone's, which a sea mod answers for, from Keystone 0.9.0; else the sea mod looked up by name, as before)
            if (keystones)
            {
                try { if (Keystones(world, out float h)) return h; }
                catch (Exception) { keystones = false; }        // (a Keystone older than 0.9.0)
            }
            if (!looked)
            {
                looked = true;
                try
                {
                    Type sea = null;
                    foreach (AssemblyLoader.LoadedAssembly a in AssemblyLoader.loadedAssemblies)
                        if (a?.assembly != null && (sea = a.assembly.GetType("NaturalOcean.Sea")) != null) break;
                    System.Reflection.MethodInfo m = sea?.GetMethod("HeightAt", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static, null, new[] { typeof(Vector3d) }, null);
                    if (m != null && m.ReturnType == typeof(float)) waves = (Func<Vector3d, float>)Delegate.CreateDelegate(typeof(Func<Vector3d, float>), m);
                }
                catch (Exception) { waves = null; }
            }
            if (waves == null) return 0f;
            try { return waves(world); }
            catch (Exception) { waves = null; return 0f; }
        }

        /// <summary>How dense the sea of the world is (tonnes a cubic metre).</summary>
        public static float Density
        {
            get
            {
                CelestialBody body = FlightGlobals.currentMainBody;
                return body != null && body.ocean && body.oceanDensity > 0.0 ? (float)body.oceanDensity : 1f;
            }
        }
    }
}
