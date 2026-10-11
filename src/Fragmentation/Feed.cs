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

        // (what a source of smoke is, as the explosions mod's ExhaustKind has it: written here as numbers, so that nothing outside this class names that mod)
        public const int Jet = 1, Burning = 2, Dust = 3, Spray = 4;

        /// <summary>
        /// Something burning, leaking or raising dust at a place this frame (see the explosions mod's Exhaust for what each figure is), and what it is
        /// (the explosions mod's ExhaustKind: 1 a jet, as a burning leak is; 2 something burning with no jet, a wreck on fire; 3 dust; 4 a spray).
        /// </summary>
        public static bool Burn(int id, Vector3 rim, Vector3 along, Vector3d going, float nozzle, float flame, float running, float amount, float shade, float warm, float lasts, int tint, float fire, Transform rides, int kind)
        {
            var exhaust = new VolumetricExplosions.Exhaust
            {
                id = id, rim = (Vector3d)rim, along = (Vector3d)along, going = going, nozzle = nozzle, flame = flame, running = running,
                amount = amount, shade = shade, warm = warm, lasts = lasts, tint = tint, fire = fire, rides = rides, kind = kind,
            };
            return VolumetricExplosions.Sources.Burning(exhaust);
        }

        /// <summary>A piece of wreckage moving through the air, which the air goes round and is pushed aside by (the explosions mod's Movers, added to it with this mod: an older copy has none, and this throws).</summary>
        public static void Moving(Vector3 at, Vector3d going, float radius) => VolumetricExplosions.Movers.Moving((Vector3d)at, going, radius);

        /// <summary>The smoke (or flame and smoke) a small thing leaves as it goes, laid into the patch of air it is in, if any (the explosions mod's Trails, added to it with this mod: an older copy has none, and this throws). True if last frame's was taken.</summary>
        public static bool Trail(int id, Vector3 at, Vector3d going, float size, float smoke, float fire, float shade, float warm, int tint, float lasts)
        {
            var trail = new VolumetricExplosions.Trail { id = id, at = (Vector3d)at, going = going, size = size, smoke = smoke, fire = fire, shade = shade, warm = warm, tint = tint, lasts = lasts };
            return VolumetricExplosions.Trails.Smoking(trail);
        }

        /// <summary>What is in a part goes up, the part itself left standing (the explosions mod's Records.Burst: older throws). The game's explosion is set off by the caller.</summary>
        public static void Burst(Part p, Vector3 at, float impact, bool collision) => VolumetricExplosions.Records.Burst(p, (Vector3d)at, impact, collision);

        /// <summary>A hard blow with nothing going up: the explosions mod's dust and sparks of a crash at that speed (its Records.Burst without contents).</summary>
        public static void Thud(Part p, Vector3 at, float impact) => VolumetricExplosions.Records.Burst(p, (Vector3d)at, impact, false, false);

        /// <summary>The explosions mod's first patch of air of the scene begun and put away out of sight, so that the first explosion of a crash does not stop the game (its Trails.Warm).</summary>
        public static void Warm(Vector3 at) => VolumetricExplosions.Trails.Warm((Vector3d)at);
    }
}
