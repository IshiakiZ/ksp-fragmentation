using System;
using Keystone;
using UnityEngine;

#if !DEV
[assembly: KSPAssembly("Fragmentation", 0, 2)]
[assembly: KSPAssemblyDependency("Keystone", 0, 5)]
#endif

namespace Fragmentation
{
    /// <summary>
    /// Fragmentation: whatever crashes breaks up into real pieces of itself.
    ///
    /// Asked for as: "whatever crashes breaks up into many real pieces, parts from other mods included; with the
    /// explosions mod installed the pieces and the smoke act on one another, and hot or burning pieces trail smoke;
    /// and a crash carries on along the ground for a couple of hundred feet, as an aeroplane's does, wreck, pieces
    /// and smoke with it"; and later: "I want the fragmentation to actually come from the ship, so depending what
    /// gets hit, what hits the ground first, if it clips a space station in space then fuel would leak for a second
    /// then explode, and the main thing is having that realisticness where if the rocket crashes at an angle it
    /// travels for a couple hundred feet like an airplane would and it sort of dissolve into fragments as it slides
    /// and explodes."
    ///
    /// The game destroys a part the moment anything touches it faster than its crash tolerance. While a craft is
    /// near enough to the ground (or to another craft) to hit it, this mod raises those tolerances and decides for
    /// itself, from where and how hard each part is really touched (see Crashes and Hits): a hard blow crushes the
    /// part that takes it and stops the craft going into the ground, but not along it; what touches the ground as
    /// the craft slides is ground away; parts tear off, tanks split and leak and then go up, and every part that is
    /// destroyed falls apart into pieces of its own skin that are solid and come to rest (see Shatter and Piece).
    /// With the Volumetric Explosions mod installed, leaks, burning pieces and the dust of the slide go into its air
    /// (see Feed), and each tank that goes up is its explosion.
    /// </summary>
    public static class Frag
    {
        public const string Version = "0.2.2";

        public static Mod Page;
        public static Toggle On, Fires;
        public static Slider Slide, Tough, Most, Lasts;

        public static void Setup()
        {
            if (Page != null && Kit.Find("Fragmentation") == Page) return;
            Page = Kit.Register("Fragmentation", Version, "Crashes as they are: a craft that hits the ground breaks up from the parts that hit, slides on and comes apart as it goes, and leaves real pieces of itself.");
            On = Page.Toggle("on", "Fragmentation", true, "Off: crashes as the game has them (a part that is hit too hard is gone at once).");
            Slide = Page.Slider("slide", "How far a wreck slides", 1f, 0.3f, 3f, "", 2, "1: as far as a real wreck of that speed would, by the friction and the ploughing of the ground. More: further.");
            Tough = Page.Slider("tough", "How tough parts are", 1f, 0.3f, 3f, "", 2, "How hard a blow or how long a scrape a part takes before it breaks up. More: craft hold together longer.");
            Most = Page.Slider("most", "Pieces at most", 150f, 20f, 400f, "", 0, "Pieces of wreckage lying about at once. The oldest go first when there are more.");
            Lasts = Page.Slider("lasts", "How long pieces stay", 120f, 15f, 600f, " s", 0, "Seconds before a piece of wreckage is taken away.");
            Fires = Page.Toggle("fires", "Leaks, fires and dust", true, "Split tanks leak before they go up, pieces burn and the slide raises dust (in the air of the Volumetric Explosions mod, where it is installed).");
        }

        public static void Log(string text) => Kit.Log("Fragmentation", text);
    }

    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public sealed class FragAtStart : MonoBehaviour
    {
        void Awake() { Frag.Setup(); Destroy(gameObject); }
    }
}
