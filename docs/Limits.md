# Limits

What it does not do, or does only roughly. Nothing here is hidden from the other pages.

## Left to the game

* **Water.** A craft over water is never armed: what a splash does is worked out by the game from the same
  crash tolerance, and this mod leaves it alone. A wreck sliding off the land into the sea is handed back to
  the game at the shore, and the game's rules for splashing down apply from there.
* **Kerbals on foot, flags, asteroids and comets** are left as the game has them.
* **A craft that is not moving fast near anything** is the game's: only a craft that could hit something
  faster than its weakest part can take, within about two seconds, is watched.

## Roughly

* **The craft is pushed as one rigid body** in a blow and in the slide. A real airframe bends and buckles;
  here what bends is the torn skin as it is drawn, and what the game's own joints let bend (they are softened
  as a part is hurt).
* **A torn part is drawn, not rebuilt for the physics**: its collider is the convex hull of what is left of its
  skin (so a torn tank lies and slides as a lump with its underside gone, not as the tube it was, and a hollow
  shell is solid to the ground), and its mass goes down with its skin; its strength in later blows is worked out
  from what was done to it before, not from its new shape.
* **Every part of a kind tears along the same lines**: the panels are cut once for each kind of part, so two
  FL-T800s hurt the same way look alike (they are seldom hurt the same way).
* **A torn part stops moving its own pieces**: what the game moves inside a part (an engine's gimbal, a panel
  that unfolds) stays as it was when the part was first torn.
* **Engines are not cut**: an engine's bell, its pump and its mounting each come away whole (dented when
  crushed or ground), and grinding hardly takes them off: an engine torn loose on a runway slides on for hundreds
  of metres, scraping.
* **Pieces collide as the convex hulls round them** (a box round a small one): a curved panel lies on the
  ground as its hull does, not in its hollow, and pieces do not break further when they hit. Bits (shreds,
  rivets, foil, clods, slabs of turf) do not collide with anything but the ground beneath where they were thrown
  from, taken as flat.
* **A part whose meshes cannot be read** (some mods' parts are packed so), or that is hurt before its skin has
  been cut into panels (a craft that hits within a fraction of a second of coming within reach), breaks up the
  old way: whole, into a few pieces of its skin, when hurt past whole.
* **The ground is not changed**: the game rebuilds its terrain as the view moves, so a scar or a crater is a
  mesh laid on the ground. A mesh cannot be drawn below the ground's own surface, so a scar's floor is drawn at
  the ground's level, its depth only in its shading and its low lips, and a wreck sits down into the ground by as
  much as it has dug in. Seen from low down a scar reads as a torn brown band, not a trench. Far off, where the
  game draws the ground more coarsely, a mark may sink into it or stand a little proud of it.
* **A scar seen from a few metres** shows what it is made of: a mesh a point every 0.8 m along and eleven across, its
  lips up to 0.3 m high, so that close by they are broad flat facets, like folded brown sheeting, and where a wreck
  split, two scars cross at slightly different heights. From ten metres and more it reads as torn earth. A wreck that
  hops leaves its scar in pieces.
* **Friction and ploughing do not know the ground** beyond paving and soil: grass, sand, rock and snow are all
  soil, and the Mun is soil too. All of the space centre's grounds are paving, its lawns included (they are
  part of the same hard ground as its aprons in the game).
* **No sound of its own**: the explosions are the game's (or Volumetric Explosions'), and a slide is silent.

## Kept short

* **Pieces, bits and marks are not kept**: they go when the flight is left, saved or loaded, and pieces in
  high time warp and when they are more than 2.4 km from the craft being flown. A torn part stays torn while
  the flight lasts; saved and loaded, it is whole again (the game keeps no such thing).
* **At most so many pieces** ("Pieces at most", 150 as installed): in a very big crash the oldest go early.
  At most 900 bits, 24 scars and 16 craters at once.
* **The fire on a burning wreck** is puffs of flame and smoke laid into the air where it burns, not one of the
  explosions mod's riding flames (those cost a patch of air each): up close its flame is a cloud of burning puffs
  rather than a steady flame.
* **Smoke from pieces is laid only where there is a patch of air already** (an explosion's cloud, a wreck's
  trail): a piece flung far from any fire trails nothing (see How it works).

## Other mods

* While a craft is armed, its parts' crash tolerance is a very large number. A mod that shows crash tolerance
  (Kerbal Engineer, say) shows that while it lasts; a mod that reads it to decide something (a mod of its own
  for crashes, or for buoyancy) gets that number too. Mods that change crash tolerances themselves will have
  their change kept and given back, as long as they do not change it while the craft is armed.
* A torn part's own renderers are hidden while this mod draws it, and its own colliders switched off while its
  box stands in for them: a mod that changes a part's look or colliders in flight will not see its change on a
  torn part.
* Mods that destroy parts their own way (BDArmory's weapons, say) are not affected: this mod only decides what
  blows from running into things do.

## Where it has been run

On a Mac (Apple M5 Pro, KSP 1.12.5 under Rosetta, OpenGL), and on Windows 11 (KSP 1.12.5, Direct3D 11) on
2026-10-10 (0.1, before 0.2.0's tearing and trails): Kerbal X crashed by the pad with Volumetric Explosions, its parts dented and broken by this mod
(its lines in the game's log), the wreck sliding 26 m, the fire and smoke drawn as a volume; then (0.2.0) into
the assembly building (see [What was tested](What-was-tested.md)). It is plain C#
with no shader of its own. It has **not been run on Linux**.
