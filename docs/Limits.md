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
  crushed or ground). Dragged along the ground an engine is worn as a whole and loses them one after another
  (see [How it works](How-it-works.md)); it is never torn into panels as a tank is, and what is left of it at
  rest is whole meshes, a mounting ring with a pump on it, say.
* **On paving an engine is held back harder than plain friction would hold it.** Steel dragged over concrete is
  held back by half its weight or so, at most by about its whole weight where it gouges: a loose engine doing 65 m/s
  would go on for two to four hundred metres, and until 0.2.2 it did (240 to 330 m along the runway, most of it
  rolling and bounding, unmarked). What was asked for is a wreck that does not glide. So besides friction (seven
  to twelve tenths of its weight) the mod gives an engine what it calls catching, which grows with speed and is by
  far the greater part when it is fast (some 3 g in all at 67 m/s), and wears it to pieces by the work of both.
  That is a rule made by eye, not a measurement. "How far a wreck slides" divides all of it: at 3 an engine goes
  about as far as friction alone would let it.
* **A wreck does not bound, and does not roll** (since 0.2.2): what strikes is taken to give way, and a wreck that
  rolls is held back as one that slides. A real wreck does bounce a little, and a wheel torn off a real aeroplane
  rolls a long way; here a part that is round (a pod, an engine on its mounting ring) no longer does either once
  it is part of a wreck.
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
* **A scar is a picture laid on the ground**, a few centimetres proud of it: a mesh a point every 0.4 m along and
  thirteen across, its lips no more than nine centimetres high, its edge cut out of the picture into clods and
  crumbs. (Until 0.2.2: a point every 0.8 m, lips up to 0.3 m, and from four metres they were broad smooth facets,
  "like folded sheeting".) Its floor has no depth of its own. The crumbs beyond its lips are flat, and at a
  distance they are too small for the picture's coarser copies and are not drawn. Where a wreck split, two scars
  cross at slightly different heights. A turn of the picture is as long as the scar is wide twice over: along a
  long straight scar the same lumps come round again.
* **Friction and ploughing do not know the ground** beyond paving and soil: grass, sand, rock and snow are all
  soil, and the Mun is soil too. At the space centre the grass along the runway (its shoulders and the lawn beyond)
  is told from the tarmac and is soil since 0.2.2; the grounds round its buildings are all still paving, grass or
  not (looked at by the hangar: what a craft touches there is a collider that is not drawn, and the mod cannot
  read what is drawn over it). Other launch sites: whatever the
  game tags as a runway or a pad is paving, the rest soil.
* **No sound of its own**: the explosions are the game's (or Volumetric Explosions'), and a slide is silent.

## Kept short

* **Pieces, bits and marks are not kept**: they go when the flight is left, saved or loaded, and pieces in
  high time warp and when they are more than 2.4 km from the craft being flown. A torn part stays torn while
  the flight lasts; saved and loaded, it is whole again (the game keeps no such thing).
* **At most so many pieces** ("Pieces at most", 150 as installed): in a very big crash the oldest go early.
  At most 900 bits, 24 scars and 16 craters at once.
* **The fire on a burning wreck** is puffs of flame and smoke laid into the air where it burns, not one of the
  explosions mod's riding flames (those cost a patch of air each): up close its flame is a cloud of burning puffs
  rather than a steady flame. What switching them on costs was measured again on 2026-10-10, with Volumetric
  Explosions 0.6.0's new flames: the same crash, twice each way, ran at 28 and 33 frames a second in its first two
  seconds with them against 41 and 45 without, and at 29 to 39 against 42 to 66 in the eight seconds after. They
  stay off.
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
