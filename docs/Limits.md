# Limits

What it does not do, or does only roughly. Nothing here is hidden from the other pages.

## Left to the game

* **Water.** A craft over water is never armed: what a splash does is worked out by the game from the same
  crash tolerance, and this mod leaves it alone. A wreck sliding off the land into the sea is handed back to
  the game at the shore, and the game's rules for splashing down apply from there.
* **Kerbals on foot, flags, asteroids and comets** are left as the game has them.
* **A craft that is not moving fast near anything** is the game's: only a craft that could hit something
  faster than its weakest part can take, within about a second, is watched.

## Roughly

* **The craft is pushed as one rigid body** in a blow and in the slide. A real airframe bends and buckles;
  here what bends is only what the game's own joints let bend between one step and the next.
* **A part is whole, dented, split or gone.** Nothing is bent or crumpled in place: a part that survives a
  crash looks as it did (only its pieces show the damage). Parts are not dented as the Kerbal Krash System
  mod dents them.
* **Pieces collide as boxes** round them (a curved panel lies on the ground as the box round it does), and
  do not break further when they hit.
* **A part whose meshes cannot be read** (some mods' parts are packed so) leaves no pieces: it goes as the
  game would have it go.
* **Friction and ploughing do not know the ground** beyond paving and soil: grass, sand, rock and snow are all
  soil, and the Mun is soil too.
* **No sound of its own**: the explosions are the game's (or Volumetric Explosions'), and a slide is silent.
* **No gouge in the ground** where a wreck slid: the dust of the slide, and the explosions mod's burn marks
  where tanks went up, are all that is left on the ground.

## Kept short

* **Pieces are not kept**: they go when the flight is left, saved or loaded, in high time warp, and when
  they are more than 2.4 km from the craft being flown.
* **At most so many pieces** ("Pieces at most", 150 as installed): in a very big crash the oldest go early.
* **The fire on a burning wreck** is one of the explosions mod's flames, of which it draws four ships' at once.

## Other mods

* While a craft is armed, its parts' crash tolerance is a very large number. A mod that shows crash tolerance
  (Kerbal Engineer, say) shows that while it lasts; a mod that reads it to decide something (a mod of its own
  for crashes, or for buoyancy) gets that number too. Mods that change crash tolerances themselves will have
  their change kept and given back, as long as they do not change it while the craft is armed.
* Mods that destroy parts their own way (BDArmory's weapons, say) are not affected: this mod only decides what
  blows from running into things do.

## Where it has been run

Only on a Mac (Apple M5 Pro, KSP 1.12.5 under Rosetta, OpenGL). It is plain C# with no shader of its own, so
nothing in it should be particular to a Mac, but it has **not been run on Windows or Linux**.
