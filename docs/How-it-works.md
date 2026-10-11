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
  faster than its weakest part can take, and is close enough to the ground to reach it within about two
  seconds, is **armed**; so is one coming together with another craft that fast. An armed craft's parts have
  their crash tolerance raised past anything the game could measure, so that the game no longer destroys
  them, and each part tells the mod what it touches. A craft that has come to rest, or has nothing within
  reach, is given its own tolerances back.
* **Over water** nothing is armed: what a splash does is the game's.
* **A craft that touches down within its parts' tolerances is not hurt**, as in the game. Only what is
  beyond a part's own tolerance counts against it.

## A part's own skin

The moment a craft is armed, each of its parts' skins is cut into the panels it would tear into, on another
thread while the craft is still falling, once for each kind of part (every FL-T800 shares the same panels):

* The part's meshes, as the game draws them, are taken into the part's own axes as loose triangles. This
  works for any part whose meshes can be read, from any mod: nothing is known in advance about any part.
* Every edge longer than a few centimetres is cut in two, the same way on both sides of it, and again, until
  none is (so that no corner of one triangle lies in the middle of its neighbour's side, which would open a
  crack in the skin when it is bent).
* Points are scattered over the skin, each with a size drawn so that there are few large panels and many
  small ones (the number bigger than a size goes as that size to the power -1.6, the fit NASA's breakup model
  uses: see [Research](Research.md)), and each triangle goes to the point nearest it, nearer counting for
  more the bigger the point's panel; the borders wander a little, each panel's its own way, so that they are
  not straight lines. Each panel knows its triangles, its neighbours, and the edges it shares with each,
  which is where a tear runs. A tank's skin comes to about forty panels, most a few tens of centimetres
  across.
* An engine is not cut: its bell, its pump and its mounting each come away whole. Nor is a small mesh on a
  big part (a hatch, a ladder, a light): it comes away whole too.

From the first time a part is hurt, the mod draws it in place of the game: the part's own renderers are
hidden and its panels are drawn as they now are, with what has happened to them (below), wherever a panel is
gone the inside of the skin and its torn edges, as thick as the wall, and blackened where it burned. The part
itself stays a part of the craft, joined to the parts beside it. As soon as any of its skin is gone its own
colliders give way to the convex hull of what is left of its skin (so that a tank that has lost its underside
sits lower, and a torn tank no longer rolls), set back into the ground by as much as it has dug in, and its
mass goes down with its skin. Drawing a torn skin is the dearest thing the mod does on the game's own thread,
so one part's skin is drawn torn for the first time each frame (a dozen parts hurt at once are all drawn torn
within a fifth of a second), and redrawing skins that change again is held to about three milliseconds a frame
on average (a part being ground along the ground is redrawn a few times a second).

## A blow

When a part runs into the ground, the mod works out how much of the craft's energy goes into the ground at
that point. Not the whole craft's: a blow at the end of a long rocket turns the rocket as well as stopping
it, so the mass the blow has to stop there is less than the whole (it is worked out from the craft's mass,
its middle and how hard it is to turn, as for any rigid body: see `Body.cs`). That mass, times the square
of the speed into the ground beyond what the part takes unharmed, is what the part has to take.

A part's structure takes so much before it is crushed right through: half its dry mass times the square of
its crash tolerance, times 35 (as set; "How tough parts are" scales it). What is in a part adds to the blow
and nothing to the strength, so a full tank bursts sooner than an empty one, as real ones do. A blow
**crushes the part by that share of its length, at the end that hit**: the skin there is pushed in, and
folds as a thin tube driven end on folds (in rings, bulging out more than in); the panels flattened right
down are torn off as crumpled pieces; a tank on its side is flattened from below. A small blow dents it.
If it holds, the craft's going into the ground at that point is stopped, with a little bounce (an eighth of
the speed), and the ground's bite takes some of its speed along it. If it does not, it is crushed through,
and the craft goes on into the ground that much slower, for the next part to take.

Every push is given to the craft **as one rigid body**: each part gets the motion it would have as a piece
of that body. On soil, a hard blow leaves a crater (as wide as the cube root of the blow's energy: a metre
for a gentle one, several metres for a hard one) with its rim thrown up, and throws clods of earth.

**Two craft running into each other** (in orbit, say): the softer of the two parts takes more of it, and a
tank, a thin wall full of liquid, is pierced rather than crushed. Dragged along the other craft in a graze,
the parts that touch are torn by the push between them times how fast they slide.

## The slide

A craft lying on the ground and moving along it is slowed where it touches:

* **On soil**, by friction (half its weight) and by ploughing, which grows with speed and with how deep each
  part has dug in. A part digs in as it slides, deeper the further it goes (most of the way within a dozen
  metres), down to a quarter of its own width; dug in, it ploughs harder (twice as hard 18 cm down), and it
  sits down into the ground by as much. **Torn open, it scoops the earth up and holds in it**: as its underside
  is torn away, its ploughing grows to three times a smooth skin's and more (seven and a half times once two
  fifths of its skin is gone), and dug in, its jagged edges hold in the earth, its friction up to two and a half
  times. So a smooth rocket skates for a few tens of metres, and once its underside is torn (within metres, see
  below) it stops fast: the test rocket thrown at the grass at 70 m/s came to rest 60 to 80 m from where it first
  touched, 3 to 5 g (see [What was tested](What-was-tested.md)). A round part rolling over soil is held back as a
  wheel in mud is, more as it sinks in.
* **On paving** (the runway, the pads, and the space centre's hard standing, all of it), by friction that
  grows from a third of its weight for a smooth skin to nine tenths for a torn one, torn metal gouging, and a
  little more for speed.

Because the drag acts where the craft touches, below its middle, it turns the craft over that point: a long
craft slews and, when one end digs in, cartwheels. "How far a wreck slides" divides it. Where the numbers
come from is on the [Research](Research.md) page.

**The drag's work tears the skin that touches away from below.** A share of it (a fortieth on soil, a
thirty-third on paving) goes into the skin there: the plane below which the skin is torn away moves up into
the part by as much as that work tears off (a square metre of a tank's skin takes some 11 kJ: by the weight
of its structure a square metre, so that a capsule is no harder to grind than a tank of the same weight of
metal). A panel half below the plane is torn off; one less below it is flattened up to it. So a tank sliding on
its side loses the bottom of its skin within metres and keeps its top, and leaves shreds, flakes of paint and
rivets behind it on the ground, and now and then a panel, folded up; with its underside gone it scoops the earth
up (above), and what held it to the parts beside it there is gone with it, so that a long rocket comes apart
into sections along its slide.

The ground is marked. **On soil, a scar**: one for each wreck, as wide as all of it that touches the ground
(measured across the way it goes, so that a wreck sliding sideways leaves a wide one), its floor torn earth,
dark and streaked the way the wreck went, darkest along its edges where the walls of a trench would be in
shade; its lips the earth pushed aside, about half as high as the scar is deep (up to 30 cm), lumpy, higher on
one side than the other here and there; its outer edges torn turf. The rest of the earth is thrown: clods,
and slabs of turf (grass on top, earth beneath) thrown aside at the scar's edges. A wreck that hops leaves a
scar in pieces. **On paving** a dark scrape, and sparks where steel drags (an engine, a heavy fitting: a tank's
aluminium skin hardly sparks). The game's terrain is built afresh as the view moves, so it is not itself
changed: these are meshes laid on it, each point set on the ground beneath it, in the earth's colour of the
place. A mesh cannot go below the ground's own surface (the ground would be drawn over it), so a scar's floor is
drawn at the ground's level and its depth is in its shading, its lips and the wreck sitting down in it.

## What becomes of a part

* **A tank torn open leaks.** Torn (any panel gone) or crushed in, it splits: what is in it comes out and
  pushes the craft the other way as it goes. Where there is oxygen in the air it catches three times in
  four; where there is none, only a tank that carries its own oxidiser burns, and then it does. Not alight,
  it runs out over some seconds and nothing goes up.
* **Alight, what is in it goes up** a moment to a second and a half later (the worse the tear, the sooner):
  the game's own explosion where the tear is, made by Volumetric Explosions (where installed) from what was in
  the tank, told it was a crash and how fast. The skin near the tear is blown away as burning, blackened
  panels, and with them arcs of the tank's rings and bent stringers; the more there was in it, the more goes
  (from two thirds of the skin left for a few kilograms to a fifth for tonnes), the top of it, as it lies,
  kept longest; round the hole the skin is blackened and bent back in petals. The wreck burns on where it went
  up and goes on burning as it slides (see below).
* **A split solid booster burns** through the split, white, and when it breaks up most of its propellant
  goes with its pieces, burning, rather than into the blast.
* **A hurt part bends before it breaks**: what holds it to the part it hangs from is softened as it is hurt
  and as the skin there is torn away, down to a twelfth of its stiffness, so that it sags and folds.
* **Torn through, it comes apart**: skin no longer joined, through panels still on, to the rest of the
  piece of skin it was part of comes away as one piece (a tank torn right round comes away in two). A part
  that has lost the skin where it is joined to the part beside it comes away from it, with whatever hangs
  from it, and carries on as a craft of its own. Coming apart where parts are joined is otherwise rare: what
  breaks is the part's own skin.
* **Almost nothing left** (less than a sixth of its skin), a part goes altogether: what is left of it in
  pieces, and its explosion if it still carried anything that burns. A part with nothing in it that burns
  is crushed, not blown up: dust and sparks, and no fireball.
* A part that is hurt before its skin is cut (a craft that hit within a fraction of a second of being armed),
  or whose meshes cannot be read, breaks up the old way: when hurt past whole, its skin is cut there and then
  into a few pieces and it goes.

## The pieces

* **Panels**: each panel torn off becomes a piece made of its own triangles, with the inside of the skin and
  its torn edges, as thick as the wall, all round it; bent about a line across it and crumpled by how it was
  torn (ground away and crushed panels most, a piece torn right off whole least), blackened where it burned,
  dirtied where it was ground. An engine's bell and its machinery come away whole, dented rather than folded.
* **Bits**: with every panel, shreds of skin, flakes of paint and rivets (and from a spacecraft's skin,
  shreds of its foil blanket): too many and too small to be pieces each with a body of its own, they are
  moved by the mod, fall, bounce once or twice off the ground and stop, and stay a minute or so (those lying
  still cost next to nothing). A part takes two milliseconds at most making pieces (four when it goes
  altogether) and always makes one; what more comes off it then is bits only, and so is more of it when the
  frames are already slow. In a busy step (more than five milliseconds of this mod's work already) what was
  torn off is made into pieces in the next steps, where the part has got to by then; so are a burst tank's
  rings and stringers, and a torn part's letting go of the parts beside it.
* **Rings and stringers** from a torn tank (one whose skin is round about its long axis): arcs of its frames
  and bent bars, bare metal.
* Each piece is a solid body with its share of the part's mass, a collider round it (the convex hull of a big
  piece, a box round a small one), and the speed that point of the part had, thrown as what tore it off would
  throw it: what the ground tore off is left behind, what a blow crushed is thrown out from the part, what a
  burst blew off is thrown out from the burst. What was going into the ground is sprayed out along it.
* They are the game's own kind of loose object (as a fairing's panels are once cast off), so the game moves
  them with everything else when it shifts the world about. The air's drag on them is the mod's own (a
  tumbling plate, by its area); their weight is the game's while the craft being flown is whole, the mod's
  after.
* For their first third of a second they are on a layer of the physics that hits the ground and nothing
  else, so that pieces of one part do not throw each other apart at once.
* A piece that has lain still for a second and a half is left lying, out of the physics, until a blast sets
  it moving again. There are at most so many ("Pieces at most", 150 as installed): when there are more, the
  oldest go, those lying still first, sinking into the ground as they shrink. Each goes after "How long
  pieces stay" (two minutes as installed) anyway. A piece that goes is kept, hidden, for the next one to use.

## With Volumetric Explosions

The explosions come from the game's own, as always. Through the ways in the explosions mod has for other
mods, this mod says once a frame what else is burning or rising:

* **Every piece trails a little smoke**: a wisp for a few seconds off one torn out of the ground or crushed,
  smoke for longer off one that came out of a fire, flame and smoke off one that burns; and so do the bits
  that came out of a fire, and a hundred of the others. These go through the way in added for this mod
  (`Trails`): each is laid as puffs straight into the patch of air it is in or near, and never begins a patch
  of its own (beginning one is the dear part of the explosions mod's work; see What was tested).
* **The wreck's fire goes with it**: after a tank has gone up, the wreck burns where it went up and goes on
  burning as it slides, dying down over twenty seconds or so: puffs of flame and black smoke laid where the
  burning part is, frame by frame, into the air it is in (the same way in as the pieces' smoke), so that its
  smoke streams out behind it along its way. Each leak likewise: white vapour, or flame and black smoke where it
  caught; fire alone in a vacuum. (The explosions mod can also draw a fire as an engine's flame, in a patch of air
  of its own going along with the craft; this mod asks for that only in its development build: each such patch
  is worked out and drawn every frame, and four against one cost 30 ms a frame. Asked so, it says what each is:
  a burning leak a jet, a wreck on fire something burning with no jet, whose smoke rises off it as a plume.)
* **The dust and earth** a craft throws up sliding over soil: brown, in the patch of air it is in.
* **A tank that goes up and stays** is noted to the explosions mod as a part that died would be, with what
  was in it, through the way in added for this mod (`Records.Burst`), so that its blast is made from what was
  in it although the tank itself is still there.

The explosions mod's smoke goes round the bigger pieces as they fly (`Movers`), and round craft.
