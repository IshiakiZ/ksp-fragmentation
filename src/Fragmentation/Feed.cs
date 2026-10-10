using System;
using UnityEngine;

namespace Fragmentation
{
    /// <summary>
    /// Everything that names the Volumetric Explosions mod. Kept apart, and in the bodies of methods only, so that this
    /// mod loads and works where that one is missing: nothing here is looked at until it is called. (Its explosions need
    /// no word from here: a part this mod destroys goes up through the game's own explosion, which that mod replaces.)
    /// </summary>
    static class Feed
    {
        public static void Hook(Action asked) { VolumetricExplosions.Sources.Asked += asked; }
        public static void Unhook(Action asked) { VolumetricExplosions.Sources.Asked -= asked; }
        public static bool Ready() => VolumetricExplosions.Sources.Ready;

        /// <summary>Something burning, leaking or raising dust at a place this frame (see the explosions mod's Exhaust for what each figure is).</summary>
        public static bool Burn(int id, Vector3 rim, Vector3 along, Vector3d going, float nozzle, float flame, float running, float amount, float shade, float warm, float lasts, int tint, float fire, Transform rides)
        {
            var exhaust = new VolumetricExplosions.Exhaust
            {
                id = id, rim = (Vector3d)rim, along = (Vector3d)along, going = going, nozzle = nozzle, flame = flame, running = running,
                amount = amount, shade = shade, warm = warm, lasts = lasts, tint = tint, fire = fire, rides = rides,
            };
            return VolumetricExplosions.Sources.Burning(exhaust);
        }

        /// <summary>A piece of wreckage moving through the air, which the air goes round and is pushed aside by (the explosions mod's Movers, added to it with this mod: an older copy has none, and this throws).</summary>
        public static void Moving(Vector3 at, Vector3d going, float radius) => VolumetricExplosions.Movers.Moving((Vector3d)at, going, radius);
    }
}
