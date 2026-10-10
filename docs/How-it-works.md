# How it works

## What the game does, and what this mod does instead

The game gives every part a **crash tolerance**, a speed in metres a second. Whenever anything touches a
part faster than that (the speed counted in full, along the ground as well as into it), the part is
destroyed on the spot: its explosion is set off, it is gone, and whatever hung from it carries on as a
separate craft until it, too, touches something. A rocket that comes down at a slant is destroyed part by
part within a few tenths of a second where it first touched, and nothing of it goes on along the ground.
(In the game's code: `Part.OnCollisionEnter` and `OnCollisionStay` both lead to `Part.HandleCollision`,
which compares the speed with `crashTolerance` and calls `Part.explode`; the game's guard against a fast
part passing through the ground between two steps of the physics, `CollisionEnhancer`, does the same.)

This mod does not patch any of that. It works through the one figure the game's decision turns on:

* **Who may crash.** Every step of the physics (fifty a second) it looks at each craft. One that is moving
  faster than its weakest part can take, and is close enough to the ground to reach it within about a
  second, is **armed**; so is one coming together with another craft that fast. An armed craft's parts have
  their crash tolerance raised past anything the game could measure, so that the game no longer destroys
  them, and each part tells the mod what it touches (a small component on it hears the same contacts the
  part does). A craft that has come to rest, or has nothing within reach, is given its own tolerances back.
* **Over water** nothing is armed: what a splash does is the game's (it works it out from the same
  tolerance), and a craft sliding off the land into the sea is handed back to the game at the shore.
* **A craft that touches down within its parts' tolerances is not hurt**, as in the game. Only what is
  beyond a part's own tolerance counts against it.

## A blow

When a part runs into the ground, the mod works out how much of the craft's energy goes into the ground at
that point. Not the whole craft's: a blow at the end of a long rocket turns the rocket as well as stopping
it, so the mass the blow has to stop there is less than the whole (it is worked out from the craft's mass,
its middle and how hard it is to turn, as for any rigid body: see `Body.cs`). That mass, times the square
of the speed into the ground beyond what the part takes unharmed, is what the part has to take.

A part can take so much before it breaks up: half its dry mass (its structure, without what is in it) times
the square of its crash tolerance, times 35 (as set; "How tough parts are" scales it). What is in a part adds
to the blow and nothing to the strength, so a full tank, heavy and no stronger, bursts sooner than an empty
one, as real ones do (see [Research](Research.md)). If it holds, it is dented by that share,
and the craft's going into the ground at that point is stopped, with a little bounce (an eighth of the
speed), and the ground's bite takes some of its speed along the ground with it. If it does not hold, the
part is crushed: it takes what it can, and the craft goes on into the ground that much slower, for the next
part to take.

Every push is given to the craft **as one rigid body**: each part gets the motion it would have as a piece
of that body. The game holds a craft together part by part with stiff springs; pushing only the part that
was hit would strain them and set the whole craft ringing and bouncing.

So:

* a craft that comes in at a slant loses its fall in the first few parts that hit, keeps most of its speed
  along the ground, and is turned over by the blow (nose down, tail slapping down after it);
* one that comes straight down fast is crushed part after part from the bottom up, each part taking the
  craft a little slower before the next.

**Two craft running into each other** (in orbit, say) are left to the physics to push apart; what each part
takes is worked out from the two craft's masses and how fast they came together, beyond the part's own
tolerance. The softer of the two parts takes more of it, and a tank, a thin wall full of liquid, is pierced
rather than crushed (it takes 1.8 times its share). Dragged along the other craft in a graze, the parts that
touch are torn by the push between them times how fast they slide.

## The slide

A craft lying on the ground and moving along it is slowed, where it touches, by friction and, on soil, by
ploughing into it, which grows with speed: the deceleration is the friction coefficient times gravity plus
a ploughing term times the speed (on paving the ploughing is all but nothing, and friction lower). Because
it acts where the craft touches, below its middle, it turns the craft over that point: a long craft rolls
and slews and, when one end digs in, cartwheels. "How far a wreck slides" divides it. Where the numbers come
from is on the [Research](Research.md) page.

The work that drag does is done on the parts that touch: they are ground away by it (only the part of the
speed beyond about half their tolerance counts), and shed pieces of their skin as they go, more the faster
and the more they are hurt.

## What becomes of a part

* **Hurt past half, a tank splits.** What is in it comes out and pushes the craft the other way as it goes.
  Where there is oxygen in the air it catches three times in four; where there is none, only a tank that
  carries its own oxidiser burns, and then it does. Alight, the rest goes up a moment to a second and a half
  later (the worse the split, the sooner); not alight, it runs out over some seconds and nothing goes up.
  With Volumetric Explosions installed the leak is a jet of white vapour, or of fire and black smoke where it
  caught; in a vacuum, where there is no air to hold smoke, it shows only if it burns, as fire.
* **A split solid booster burns** through the split, white, and when it breaks up most of its propellant
  goes with its pieces, burning, rather than into the blast.
* **A hurt part bends before it breaks**: what holds it to the part it hangs from is softened as it is hurt,
  down to a twelfth of its stiffness, so that it sags and folds under the loads of the slide.
* **A hurt part may tear off**, with whatever hangs from it, and carry on as a craft of its own.
* **Hurt past whole, a part breaks up** into pieces of its own skin (below). If it carried something that
  burns, that is its explosion: the game's own explosion, set off as the game sets it off, which Volumetric
  Explosions (where installed) makes from what was in the part. Before it goes the mod tells the game it was
  a crash and how fast, which the game's flight log records, and which the explosions mod reads to throw the
  fire on ahead along the slide and draw the burn mark out the same way. A part with nothing in it that
  burns is crushed, not blown up: dust and sparks, and no fireball.
* The blast of a tank throws the pieces near it (light thin ones furthest) and shoves the craft near it.
* After a tank has gone up, the wreck it was on burns for some seconds (a fire on it, in the explosions
  mod's air).

## The pieces

When a part breaks up, its meshes, as the game draws them at that moment, are cut into pieces. This works
for any part whose meshes can be read, from any mod: nothing is known in advance about any part.

* Its skin is taken as loose triangles. Any triangle longer than a piece should be wide is halved, and
  halved again, so that a cut can fall anywhere (the side of a fuel tank is often a few triangles as tall as
  the tank).
* Points are scattered over the surface, each with a size drawn so that there are few large pieces and
  many small ones: the number of pieces bigger than a size goes as that size to the power -1.6, the fit
  NASA's breakup model uses for things that broke up (see [Research](Research.md)). Every triangle goes to
  the point it is nearest, nearer counting for more the bigger the point's piece (a "power diagram": each
  cell is convex, so each piece is one patch). A part leaves one big piece, and more and smaller pieces the
  bigger it is and the harder it was hit (2 to 12); fewer when the frames are already slow (down to a third
  at fifteen frames a second). A part with very many triangles gives every so many of them, all over it.
* The cutting is done within four milliseconds a step of the physics (at least one part a step): when many
  parts break at once, as when a craft comes straight down, the rest wait a step or two.
* Each piece gets a back face, so that the inside of the skin shows, and is darkened: scorched where it
  burned, dirtied where it scraped. Pieces from a fire glow dull red for a few seconds and some burn,
  trailing smoke.
* Each is a solid body with its share of the part's mass, a box round it as its collider, and the speed
  that point of the part had, plus a kick away from the part's middle (harder the harder the blow). What
  was going into the ground is sprayed out along it, with a little thrown back up.
* They hit the ground, buildings, craft and each other. They are the game's own kind of loose object (as a
  fairing's panels are once cast off), so the game moves them with everything else when it shifts the world
  about, and changes their speed with everything else when it changes the frame it reckons speeds in. The
  game gives such objects their weight only while the craft being flown is whole; once that craft has been
  destroyed in the crash, the mod gives it instead. The air's drag on them is the mod's own (a tumbling
  plate, by its area).
* For their first third of a second they are on a layer of the physics that hits the ground and nothing
  else, so that the pieces of one part, which lie against each other and against the part next to them,
  do not throw each other apart at once; then on the layer of craft themselves, so that the explosions
  mod's smoke goes round them as it goes round craft.
* A piece that has lain still for a second and a half is left lying where it is, out of the physics: it
  costs nothing until a blast nearby sets it moving again. There are at most so many ("Pieces at most",
  150 as installed): when there are more, the oldest go, those lying still first, sinking into the ground
  as they shrink. Each goes after "How long pieces stay" (two minutes as installed) anyway.
* A part that broke up into pieces is not drawn whole as well; and Volumetric Explosions, which makes
  pieces of its own (not solid) from what a destroyed part draws, makes none of a part this mod broke up.

## With Volumetric Explosions

Nothing of the explosions mod is changed. Its explosions come from the game's own, as always. Through the
way in it has for other mods since its version 0.4.0 (`Sources.Burning`, the one Rocket Smoke and Engine
Flames use), this mod says once a frame what else is burning or rising:

* each leak (white vapour, or fire and black smoke where it caught; fire alone in a vacuum);
* the ten biggest burning pieces (black smoke, white for solid propellant);
* the dust a craft raises sliding along soil faster than 10 m/s (laid just under the surface, so that it
  takes the ground's own colour), more the faster it slides; none on paving, where what a craft dragged along
  throws up is grit and sparks, not a cloud;
* the fire on a wreck whose tank went up.

The explosions mod's smoke does not go through pieces (it goes round craft, and pieces are on the craft's
layer), and the wreck's parts, which are still craft, shove the smoke aside as they go through it.
