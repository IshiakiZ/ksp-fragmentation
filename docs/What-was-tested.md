# What was tested

All on 2026-10-09 and 10, on a Mac (Apple M5 Pro, KSP 1.12.5 under Rosetta, OpenGL, 1280 by 720), in the test save
"AI Sandbox", with Scatterer, EVE, TUFX, Waterfall, Natural Light and Volumetric Explosions 0.4.0 installed.
The craft is the test save's rocket (`fx_pad`: a Mk1 pod, a Terrier stage, two FL-T800 tanks over a Swivel,
two Hammer solid boosters on radial decouplers with nose cones, four fins; 20 parts, about 21 tonnes), thrown
at the ground by the development build's `Throw` (see [Building](Building.md)) and filmed frame by frame as
the game ran (`dev_record`, `live: true`).

## First round (development build of the afternoon)

| Test | What happened |
| --- | --- |
| Skimming onto the runway: 80 m/s, 3° down, level | It touched at 7 m/s into the ground: the two decouplers (the lightest parts at the bottom) were crushed, the FL-T400 split, leaked alight and went up 1.2 s later, some 90 m on; the fins tore up; the Terrier was crushed by a booster's nose cone it ran into; the FL-T800 split 4 s in and went up 1.3 s after that. The pod, chute and heat shield, still the craft being flown, slid on and came to rest 300 m from where it first touched. 79 pieces. |
| 10° down at 80 m/s, on a taxiway at the space centre | It hit at about 25 m/s into the ground: crushed at the front, slapped down at the back, and came apart over the next few seconds along the way, both Hammers going up 20 m/s into the ground, the tanks splitting and leaking; the heaviest section left (an FL-T800) tumbled on, slowing, split and leaked at the end. 24 parts broken up, 3 tanks split, 150 pieces (the limit). |
| Straight down at 230 m/s | Crushed from the nose up within a few hundredths of a second (each part taking the rest of the craft a little slower), every part cut into pieces, the tanks and the boosters going up together: one fireball, burning chunks thrown out, pieces sprayed out along the ground. 35 parts broken up. |

What was seen, and changed afterwards:

* **The dust of a slide was a white wall** lit as if by fire: the explosions mod lights the smoke of anything
  fed to it by how hard it is "running", as by its flame, and the dust had been fed as running hard. Dust is
  now said to be barely running, is browner, half as much, and none at all off paving.
* The space centre's lawn read as paving (it belongs to the same object as the runway). Paving is now what
  the game itself calls the runway or a pad, by its tags.
* Small parts were cut into too many pieces (a fin into 11). Now 2 to 12 a part, fewer for small parts.

## What it cost (first round)

The frame rate of the 10° crash on the taxiway, measured with the frame limit on and nothing recording, the
same throw each way:

| | Before (falling) | First second (flying in) | 1 to 4 s after | 4 to 7 s after |
| --- | --- | --- | --- | --- |
| Without this mod (the game's crash, Volumetric Explosions' fire) | 40 fps | 49 | 37 | 37 |
| With it | 40 fps | 48 | 20 | 16 |

That was far too dear, and was looked into in the second round (below).

## Second round: where the cost was

The same 10° throw at 80 m/s, onto grass north of the runway, run once for each way of running the mod (a
crash is never twice the same, so a few frames a second either way is noise). "The mod's time" is what its
own work took on the game's thread, a frame, in the three seconds after the impact.

| Run | 1 to 4 s after | 4 to 7 s after | The mod's time, 1 to 4 s |
| --- | --- | --- | --- |
| Without this mod | 31 fps | 28 | |
| With it, everything fed to the explosions mod | 18 | 19 | 6.6 ms its own work, 9.8 ms telling the explosions mod |
| Nothing fed to the explosions mod | 38 | 42 | 1.8 ms |
| At most 40 pieces | 21 | 17 | |
| No piece casting a shadow | 23 | 16 | |
| Burning pieces' smoke not fed | 26 | 23 | |
| Dust not fed | 22 | 18 | |
| Leaks' and wrecks' flames not fed | 31 | 22 | |
| Pieces not pushing smoke | 20 | 25 | |

So **the pieces themselves are cheap**: a hundred of them, falling, colliding and lying about, with nothing
fed to the explosions mod, made the crash run faster than the game's own crash with that mod's fire (its own
non-solid debris and the game's explosion of every part are gone). Fewer pieces, or no shadows, did not help.
**The cost was in the explosions mod's air**: every burning piece flung away from the fire had it begin a
patch of air of its own (a patch is a grid worked out and drawn every frame, and beginning one is work on the
game's own thread: that was the 9.8 ms), and each flame is a patch of its own as well.

Changed: burning pieces' smoke is fed only near a blast of the last 20 seconds (where it joins the blast's own
patch) and for four pieces at most; the flames of two leaks at most; no flame on a sliding wreck (each tank's
blast leaves a fire burning on the ground of its own); eight pieces at most push smoke aside, the big ones.
After that, the same crash twice, against the game's own once more:

| Run | 1 to 4 s after | 4 to 7 s after |
| --- | --- | --- |
| Without this mod | 31 fps | 29 |
| With it (first) | 27 | 38 |
| With it (second) | 32 | 27 |

About the same as the game's own crash with the explosions mod.

## Second round: other crashes

| Test | What happened |
| --- | --- |
| Skimming onto grass, 70 m/s, 4° down, tanks a tenth full | It touched 44 m after it was thrown; the first blows took 10 m/s off it; the fins tore up at 12 m/s into the ground and the FL-T400 went up (240 kg). The main wreck (both FL-T800s, the engines and the boosters, 13 parts) then slid **53 m (175 feet), from 52 m/s to rest in 2.0 s**, about 2.6 g, as a craft that digs in does (see [Research](Research.md)). The pod broke away and tumbled on by itself; its heat shield and parachute were crushed as it rolled. Behind them a brown cloud of dust, the ground's own colour. |
| Dropped onto the launch pad, 25 m/s nose down | The FL-T800 at the bottom crushed and went up at once (4,000 kg); both Hammers split, burned through the splits, white, and went up 1.5 and 1.8 s later with a ninth of their propellant (the rest left burning with their pieces); a decoupler crushed. |
| Set drifting into another craft in orbit (`rdv_done`: the test rocket and the Mun Lander, 161 m apart) | Not hurt at all, at first: neither craft was armed. A craft in orbit over the sea had been taken for one on the water (the ground under it is below sea level). Mended. Then, at 6 m/s, they touched and bounced apart unhurt: within the tanks' own tolerance (6 m/s), as in the game (see the third round). |

## Third round (the evening)

| Test | What happened |
| --- | --- |
| In orbit, the rocket set at the Mun Lander at 16 m/s | Its nose (the parachute) hit the lander's FL-T800 at 14 m/s into it. The tank was hurt 0.22: no split. Real tanks are holed at lower speeds than that (a part driven into a full tank leaks it at 8-9 m/s; a supply ship at a few metres a second holed Mir's Spektr module), so blows between craft were changed: the softer of the two parts takes more of the blow, and a tank, a thin wall full of liquid, is pierced rather than crushed (1.8 times). |
| The same again | The FL-T800 was hurt 0.52 and **split, leaking alight (1,166 kg in it); 1.3 s later it broke up into six pieces and went up** with the 688 kg left, and the lander came apart. What the user asked for: "if it clips a space station in space then fuel would leak for a second then explode". (Pictures from that run were against the sun and show little; the game's log is the record.) |
| Skimming onto grass in the morning (the test save warped on by 15,600 s), 70 m/s, 4° down, tanks a tenth full | It touched 45 m after the throw and lost 13 m/s in the first blows; the fins and a decoupler crushed, the FL-T400 split and went up; the main wreck slid **70 m (230 feet) in 2.4 s** and stopped, 20 parts down to 13; the pod broke away and rolled on 35 m. The README's picture is from this run. |
| The same throw with this mod standing aside (the development build's switch), in a scene loaded afresh | Every part that touched went up where it touched; one cloud of fire and dust; nothing slid on. The README's picture sets the two side by side, the same moments of each. |

## Round two (the night of 9 to 10 October)

Asked of the first version, in the user's words: "every piece that flies out, even the tiniest ones, should have
at least a tiny bit of smoke"; of the slide across the grass, "It should kind of dig into the grass"; "the smoke
doesn't follow the rocket at all"; "a rocket is huge and heavy. It shouldn't slide nearly at all without having
any damage to the tube. The bottom of the tube should be disintegrated, but there could be a little bit at the
top"; and "I mean the actual tube gets fragmented. The actual pieces, not the attachment points". The same test
rocket and save; the films recorded frame by frame at 60 a second (`dev_record`), the pictures of what was left
taken from close by with the camera on the wreck or a piece (`Look`), the timings with `Clock` and nothing
recording. Each picture set side by side was recorded alone, in a scene loaded afresh.

### The grass slide

The throws: 70 m/s, 3 to 4 degrees down, onto the grass north of the runway; "lean" is the same with the tanks a
tenth full, so that the fire does not hide the rest.

| Test | What happened |
| --- | --- |
| Lean, early in the round (torn skins, grinding a two-hundredth of the drag's work) | It touched at 7 m/s into the ground; the fins were torn off; then **the whole rocket, 18 of its 20 parts, slid 95 m and stopped, its tanks whole**: the tanks never touched (the rocket lay on its boosters and its pod) and what did touch was hardly ground. Behind it four smooth brown ridges a metre high, one for each part that touched: nothing like a gouge. This is what the user had seen. |
| Lean, grinding a fortieth, a torn skin scooping up the earth and holding in it, one scar for the wreck | It touched at 7 m/s; a decoupler and the fins crushed, the FL-T400 split, alight, and went up; the tanks' undersides were ground 0.4 m into their 1.25 m and both split and leaked; the parachute was torn off the pod and the pod section broke away and tumbled on. **The main wreck (14 parts) stopped 57 m from where it first touched**, from 70 to 17 m/s in 1.5 s (4 to 5 g); it burned on where it stopped. The scar: a brown band in pieces (the wreck hopped). |
| Lean, a panel half ground away torn off | One FL-T800 slid on its side lost half its skin (51% left), a Hammer five sixths of its own; the other FL-T800 was dragged on its end and lost only the end (96% left). Close by: the torn tanks blackened and holed, torn panels, the Terrier's bell, clods and slabs of turf (the slabs then drawn as green crystals and the clods as bright brown dice: made two-tone, darker and smaller after). |
| Lean, as delivered (the film on the front page) | It touched at 68 m/s along the ground; both Hammers and both FL-T800s split, alight, one after another, and the FL-T400; the Hammers went up with 13% and 45% of their skins left, an FL-T800 with 49%. The wreck slid from 68 to 13 m/s in 2.1 s, coming apart as it went (19 parts, then 13, 7, and at last one), and **stopped 80 m from where it first touched**, burning, at the end of its scar; the pod section rolled on 34 m. |
| Full tanks | Burst into one fireball within a second of touching; three parts left after a second, the boosters tumbling on; torn panels, rings and an engine from 60 to 180 m along the way; the pod sitting upright on the grass, unhurt. |

### The runway

| Test | What happened |
| --- | --- |
| 80 m/s, 5 degrees down, onto the runway | The tanks and boosters split and went up within a second and a half (the FL-T400 at once, broken into pieces; one FL-T800 four tenths of a second after splitting; a Hammer torn right through, its pieces burning); the pod slid off onto the grass. The heaviest piece left, the Swivel with its mount (1.5 t, steel, not torn), slid on along the runway from 67 m/s for some 400 m, about 0.7 g, a dark scrape behind it (the sparks it throws were not looked for; an engine is not cut into panels: its bell and machinery are whole meshes, which grinding hardly takes off; see Limits). |

### What it cost

The same 4 degree throw onto the grass with full tanks, with and without this mod, in turn, each in a scene loaded
afresh, nothing recording; frames a second, counted from the throw (the first touch half a second to a second after it).
A crash is never twice the same, and different hours of the night ran different builds of the other mods and
different weather: only figures from the same hour are set against each other.

| Run | Worst frame | 0 to 2 s | 2 to 4 s | 4 to 7 s | 7 to 10 s |
| --- | --- | --- | --- | --- | --- |
| 9 Oct 23:18, the first timing of the round, with it | 552 ms | 20 | 28 | 22 | 30 |
| 9 Oct 23:18, the first timing of the round, without it | 121 ms | 41 | 35 | 34 | 31 |
| 23:40, panels reshaped only by what bears on them; small parts going with their part, with it | 220 ms | 31 | 31 | 33 | 35 |
| 23:40, panels reshaped only by what bears on them; small parts going with their part, without it | 117 ms | 42 | 39 | 36 | 31 |
| 10 Oct 00:41, after the slide that tears and stops (more torn, more pieces), with it | 289 ms | 33 | 21 | 25 | 27 |
| 10 Oct 00:41, after the slide that tears and stops (more torn, more pieces), without it | 206 ms | 35 | 31 | 27 | 28 |
| 01:50, pieces two milliseconds a part; skins redrawn within a budget; bits lying still left alone, with it | 266 ms | 21 | 24 | 27 | 27 |
| 01:50, pieces two milliseconds a part; skins redrawn within a budget; bits lying still left alone, without it | 177 ms | 28 | 27 | 28 | 25 |
| 04:25, as delivered: a busy step's pieces made in the next; shared mesh buffers; colliders a frame later, with it | 170 ms | 33 | 31 | 38 | 32 |
| 04:25, as delivered: a busy step's pieces made in the next; shared mesh buffers; colliders a frame later, without it | 147 ms | 35 | 29 | 29 | 28 |

So, through the round, the worst frame at the crash came down from 552 ms to 170 ms (the game's own crash: 117 to
206 ms, most of it the explosions mod beginning its fire), and the seconds after it from about 20 frames a second
to the game's own or better. What the timings found, and what was done about each (the development build's clock
times every part of this mod, and the explosions mod's, frame by frame):

* **Drawing torn skins** was the dearest: a skin's first drawing 10 to 15 ms, and every redrawing worked out every
  corner of the skin. Now only the panels a change bears on are worked out again, one skin is drawn for the first
  time each frame, and redrawing is held to about three milliseconds a frame; the buffers a skin's mesh is made
  from (a megabyte or more each) are shared by every skin instead of made for each.
* **Making pieces** in the step of the blow: a fin broken up late in a busy step had come out as nothing but bits.
  Now each part has two milliseconds for its pieces (four going altogether) and always makes one, and in a step
  already five milliseconds busy the pieces are made in the next steps; a burst tank's rings and stringers, and a
  torn part letting go of the parts beside it, a step or two after the burst; a torn part's colliders give way to
  the hull round what is left a frame or two later, two parts a frame; a crater's mesh in the frame after.
* **The explosions mod's riding flames**: each burning wreck drawn as an engine's flame is a patch of air of its
  own, worked out and drawn every frame (four patches against one cost 30 ms a frame). Fires and leaks are now laid
  as puffs into the air they are in.
* **The first patch of air of a session** cost the explosions mod half a second and more: begun a few seconds into
  the flight, out of sight (and this mod's own materials drawn once, too small to see, at the same time).
* **Small parts flying off as craft of their own**: every craft costs the game something each frame; a fin or a
  decoupler on a part that goes altogether now goes to pieces with it.
* **Bits lying still** were handed over to be drawn again every frame; now only the kinds any of whose bits moved.

Stopping the hurt parts' own modules (engines, decouplers and the rest, as a test) made no difference that could
be told from one crash to the next (two runs each way, in the same session). The garbage collector's pauses of
about 200 ms come with or without this mod (the game's own collector, every few tens of seconds); this mod now
makes much less garbage at a crash, so it brings them on less.

## Round three (2026-10-10, evening): 0.2.2

Asked: a loose engine slid some 400 m along the runway ("a rocket is huge and heavy. It shouldn't slide nearly at all
without having any damage to the tube"); the scar from four metres looked like folded sheeting; a steep crash, a
drop onto the pad and a crash in orbit had not been run on the final build; and whether the wreck's fire could be
drawn as riding flames now. The same test rocket, at midday, every run in a scene loaded afresh, with
`tools/fragtest.py` (see [Building](Building.md)); frame rates are counted from the first touch, with the frame
limit on and nothing recording. 0.2.1 was run in the late afternoon; 0.2.2 between ten and eleven that night, on
the other mods as they then were (Volumetric Explosions 0.6.1, Natural Light 0.10.3): 35 runs, no exception in
any of them.

### As 0.2.1 was (run)

| Test | What happened |
| --- | --- |
| The Swivel (1.5 t) cut loose and thrown alone along the runway at 67 m/s, 3 degrees down, lying level (twice) | It slid on its side: its bell and more were ground off it (half its skin: 49% left, 0.73 t) and it lay 83 and 79 m from where it first touched, on its mounting ring, a dark scrape behind it. |
| The same, tumbling (nose up 35 degrees, turning) | **326 m** from where it first touched, 143 m of them on the ground: it rolled and bounded on its mounting ring, left the tarmac, and came to rest upright on the lawn, nothing off it (hurt 0.10), a grey scrape in zigzags behind it across the grass. |
| The same, on end (80 degrees) | 313 m, 93 m of them on the ground; nothing off it (hurt 0.18). |
| The whole rocket along the runway, 80 m/s, 5 degrees down | Tanks and boosters split and went up in the first second and a half. The Swivel came loose doing 64 m/s and lay 238 m from where it then first touched (350 m from the rocket's first touch), unmarked. The pod with its heat shield (0.98 t) lay **413 m** from the first touch, 75 m of them on the ground: the rest in bounds. Pieces to 446 m. |
| The whole rocket at the lawn by the hangar (the films' "grass"), 70 m/s, 4 degrees down, tanks full (twice) and a tenth full | Nearly every blow was on "paving": that lawn is the hangar's ground, tagged as the space centre's. The pod section went 197, 398 and 431 m; no scar of torn earth was left. |
| Straight down: 110 m/s, 35 degrees down, at the lawn by the hangar | Pod, tanks and boosters went up within the first second, as before. The Swivel hit at 54 m/s, "crushed through, hurt 5.50", and was left whole: it lay 262 m on, 1.5 t. (A plain fault: an engine's meshes were only taken off by the plane of a crush, never by how hurt it was.) |
| In orbit: set at the Mun Lander at 16 m/s | Its nose hit the lander's FL-T800 at 13 m/s: hurt 0.39, "split and is leaking (1166 kg in it, alight)"; hit again 0.3 s later; **0.6 s after splitting it went up** (910 kg), 37% of its skin left, and was torn away from its decoupler. No exception. |
| A drop onto the pad | Not a test: thrown from where it stood on the pad, the rocket was torn apart by the throw itself (parts at 400 to 540 m/s). `tools/fragtest.py` now lifts it over the pad first: see the last table of this round. |

So the long slides were of three kinds: a round part **rolling** (an engine on its mounting ring is a wheel, and
nothing held a wheel back on paving); wreckage **bounding** (the mod's drag acts only while something touches, and
its grip in a blow was a quarter of the small part of the blow that the mod itself stops); and **lawn taken for
paving**. An engine that slid flat on its side was already ground down and stopped in 80 m.

### What it cost (run, 0.2.1)

The lawn by the hangar, tanks full, each twice:

| Run | 0 to 2 s after the first touch | worst frame | 2 to 4 s | 4 to 7 s | 7 to 10 s |
| --- | --- | --- | --- | --- | --- |
| 0.2.1 | 41 and 45 fps | 79 and 56 ms | 42 and 55 | 57 and 66 | 63 and 66 |
| 0.2.1, the wreck's fire as riding flames | 28 and 33 | 85 and 59 ms | 31 and 29 | 39 and 33 | 39 and 35 |

Riding flames cost 12 to 13 frames a second in the first two seconds and 10 to 33 after: they stay off. (Along the
runway, once: 38 fps in the first two seconds, worst frame 111 ms. Straight down: 30, 104 ms. In orbit: 53, 72 ms.)

### What 0.2.2's rules cost (run, 2026-10-11)

The night 0.2.2 was made, the same crash ran at 20 to 40 frames a second whichever rules were on, and nothing could
be said. So it was run again with nothing else using the machine: the lawn by the hangar, tanks full, sixteen times in
one sitting, the new rules and the old (every development switch of 0.2.2 off, the old scar) by turns, eight runs with
the new first of each pair and eight with the old first.

| Rules | 0 to 2 s after the first touch, each run | mean | 2 to 4 s, mean |
| --- | --- | --- | --- |
| 0.2.2 | 22.6, 29.1, 33.1, 37.0, 40.1, 38.1, 39.4, 33.0 fps | 34.1 (35.7 without the first, the first crash after a build was put in) | 34.0 |
| as 0.2.1 | 29.7, 32.8, 42.2, 41.9, 27.2, 39.6, 41.6, 34.1 | 36.1 | 34.2 |

One run and the next differ by up to fifteen frames a second with nothing changed; the two sets of rules differ by one
or two, in the first two seconds, and by nothing after. The worst frame was 46 to 91 ms in every run but the first
(365 ms). No exception in any. So: no cost that can be told from the spread; and "41 and 45" that afternoon were two
runs, no more to be relied on than any other two.

### The first pass (run)

An engine worn as a whole by the drag's work, held back by steel's friction and by catching, the space centre's
lawn told from its tarmac, the scrape laid along the way the wreck's middle goes. The loose Swivel again, twice:
no exception; the lawn was soil ("hit the ground" where it left the tarmac, and a brown scar there in place of the
grey scrape); it lost a fifth of its skin (79% left). But it lay **249 and 319 m** from where it first touched, with
only 32 and 26 m of them on the ground: held back harder where it touched, it tumbled at once and bounded all the
way. And the whole rocket on open ground 200 m north of the runway (70 m/s, 4 degrees down, tanks a tenth full):
soil; its main pieces stopped 54 to 57 m from the first touch, as before, but the pod with two parts more went on to
294 m, 13 m of them on the ground: wreckage bounded over soil as well.

### The second pass as first written (run)

A wreck's bounds taken from it (what strikes gives way); the paving's grip in a blow reckoned on all that the
ground pushed back with at the place that struck; in a touch, friction on what the physics said the paving pushed
back with; a wreck that rolls held back by its middle's speed. The loose Swivel again, at 67 m/s:

| Test | What happened |
| --- | --- |
| Tumbling | It struck the paving at 6 m/s, then at 17, then at 33 (itself doing only 16 m/s by then): each strike's grip had spun it faster, and the third broke it up, **26 m** from its first touch and four tenths of a second after it. |
| The same, the touches' friction stood aside | Struck at 16 m/s, then at 58; broke up 26 m on. |
| The same, the strikes' grip stood aside as well | 95 m, 62 of them on the ground, half of it worn off: about what was meant. |
| Lying level | From 65 m/s to rest in **34 m**: over 6 g. (The same with the touches' friction stood aside was not a test: thrown from 0.3 m over the runway, the engine was not seen again, twice that night. It is thrown from 1.5 m now.) |

Two rules, then, were far too hard, and were softened. The grip in a blow, reckoned on how fast the corner that
struck was going, spins a tumbling wreck up until it smashes itself: it is now on no more than stopping the
wreck's own fall takes, and a wreck loses three tenths of its turning at each blow. And the friction in a touch,
reckoned on what the physics says the paving pushed back with, was taken for what stopped the level engine in 34 m
(with a wreck held to the ground the physics pushes back hard at every step; this was not shown by a run of its
own): it is by the wreck's weight again, owed as well for the moments (up to 0.3 s) the wreck was off the ground.

### As it is: a loose engine on the runway (run)

| Test | What happened |
| --- | --- |
| The Swivel alone at 67 m/s, tumbling (three times) | **71, 65 and 72 m** from where it first touched (46 m of the 71 on the ground): it tumbles along throwing sparks, its mounting ring comes off and rolls away, then its bell and its pipes; what is left (0.41 t of 1.5) stops at the end of a dark scrape. Hardest blow 8 m/s. |
| Lying level | 78 m, 0.29 t left; one piece of it 109 m on. |
| On end | 60 m, 10 of them on the ground, 0.77 t left; one strike at 32 m/s. |
| At 40 m/s, tumbling | 35 m, a fifth of it gone (1.20 t left). |
| The whole rocket along the runway, 80 m/s, 5 degrees down | The Swivel came loose, was worn through and broke up; the pod lay **152 m** from the first touch (it was 413 m), 75 of them on the ground; the tank section 145 m from where it first touched; pieces to 579 m (thrown by the tanks going up). |
| The whole rocket at the lawn by the hangar (paving to the mod), tanks full (six times) | Where the Swivel came loose it lay 43 to 51 m from where it first touched, worn to 0.31 to 0.77 t. The pod section lay 180 to 234 m from the first touch (0.2.1: 197 to 431 m), on the ground for 13 to 139 m of them: it is thrown clear when the tanks go up, and that lawn is paving. |

A frame of the film (`filmtumble`, 30 frames a second): the ring rolling off behind, orange sparks streaming from
the engine and from the ring. The sparks were made brighter than white and longer for this: as they were, they
could not be seen by day on the pale runway.

### As it is: on soil (run)

| Test | What happened |
| --- | --- |
| The whole rocket on open ground, 70 m/s, 4 degrees down, tanks a tenth full, pieces that roll ploughing in full | Soil; tanks and a booster went up; the pieces lay 23 to 26 m from the first touch: stopped dead, at some 10 g. Too hard: a wreck that rolls over soil now ploughs a third as hard as one dragged through it. |
| The same, as it is | The pieces lay **54 to 74 m** from the first touch (the Swivel 54 m, a fifth worn; the pod with two parts more 69 m, where it had gone 294 m; a booster with five parts more 69 m), at the end of one scar 65 m long. |
| The same, a wreck that rolls ploughing a fifth as hard | 48 to 57 m (one crash is not another: no difference to speak of). |
| The same with no fuel at all | Nothing went up: the whole rocket (4.8 t dry) slid 54 m in one piece and lay at the end of its scar. |
| The loose Swivel over open ground | Not run. |

### The scar (run)

Scars laid on open ground without a crash (`scarlook`: 42 m long, 2.4 m and 0.9 m wide), the old drawing and the
new side by side, each looked at from 4 m (across it, and along it at its end), 15 m and 60 m at midday. The old:
a brown strip with a raised hem either side, broad smooth facets with saw-toothed pale edges, from every distance
(the "folded sheeting"). The new: a shallow gouge, its floor dark earth scored along its length, its lips low
crumbled earth with a sod here and there, its edge ragged, crumbs and clods lying out on the grass, ending in a
point; from 60 m three thin torn gouges beside three strips of cloth. Three things were mended on the way: a scar
behind a real wreck was laid back and forth along itself wherever the middle of what touched jumped back (a
65 m slide had a scar "171 m long", in folds with black creases: it is now never laid backwards); cut out by its
lumps alone, the edge was smooth blobs a foot across, like spilt paint (worked out in a flat view from 4 m before
it was seen in the game: it is now ragged at every size); and with its crumbs' bumps as strong as its lumps', the floor was
lit in little squares, like brickwork, from 4 m (the crumbs' bumps were taken nearly out). The old drawing is
gone from the mod.

### What it cost (run, that night)

The lawn by the hangar, tanks full; frames a second in the first two seconds after the first touch, and the worst
frame:

| Rules | Runs |
| --- | --- |
| 0.2.2 | 34 (73 ms), 34 (103), 25 (106), 24 (100), 21 (113), 38 (72) |
| 0.2.1's, in the same build (each new rule stood aside, the old figures set) | 23 (89), 22 (110), 40 (60), 39 (61) |
| 0.2.1 that afternoon (the other mods as they then were) | 41 (79), 45 (56) |

The same crash ran at anything between 20 and 40 frames a second that night, whichever rules were on: the
difference between one run and the next is far bigger than any between the rules, and nothing can be said beyond
that. What was looked at instead is this mod's own work a frame: in the fast runs 6 to 8 ms through the first
three seconds, in the slow ones 11 to 18 ms, with either set of rules; in both nearly all of it is making torn
skins and pieces (as before), while the slide's own work, where the new rules are, is 0.1 to 0.4 ms a frame (0.1
to 0.2 with the old rules) and the marks' 0.1 to 0.4 ms (0.1 to 0.2). Along the runway: 38 and 37 frames a second in the
first two seconds (it was 38), the worst frame 116 and 77 ms (111). Straight down: 34, 98 ms (30, 104).

### Steep, pad, orbit on the final build (run)

| Test | What happened |
| --- | --- |
| Straight down: 110 m/s, 35 degrees down, the lawn by the hangar | Pod, tanks and boosters up in the first second, as before. The Swivel hit at 76 m/s, "crushed through, hurt 11.00", and broke up into two pieces (it had lain whole). The Terrier, dented three times, lay 84 m from where it first touched. |
| A drop onto the pad: lifted over it, thrown nose down at 25 m/s from 45 m | The parachute and the pod were crushed through at 35 to 37 m/s; the pod split, alight, and went up; the FL-T400 was crushed through, "split and is leaking (2000 kg in it, alight)" and went up with 1,906 kg, 6% of its skin left; its decoupler broke. The lower stage, 15 parts and 18 t with both FL-T800s full, came down on what was left of the upper one and was left lying on the pad unbroken (hardest blow 10 m/s): the upper stage took the fall. A tank going up beside full tanks does not set them off: a blast's harm to the parts round it is the game's, and it does little. 50 frames a second, the worst frame 59 ms. |
| In orbit: set at the Mun Lander at 16 m/s | The lander's FL-T800 was hit at 13 m/s, hurt 0.40, "split and is leaking (1166 kg in it, alight)", hit again, and **0.8 s after splitting went up** (833 kg), 24% of its skin left, torn away from its decoupler. |

## On Windows (2026-10-10)

Windows 11, KSP 1.12.5 on Direct3D 11, the development build with every mod of the family, Kerbal X set on the
apron 50 m east of the assembly building and thrown west at its wall (`Throw`, "speed=60 descent=0 heading=270
pitch=0 height=25"):

* It met "KSC_VAB_Main_Building (model_vab_exterior_main_v30, space centre)", taken as paving: the pod broke up
  "after the ground at 60 m/s" and went up (116 kg, 10% of its skin left); the X200-16 "split and is leaking (8000 kg
  in it, alight)" and went up a tenth of a second later (7,564 kg); the X200-32 split and leaked (11,727 kg); a
  decoupler broke into eight pieces, a wheel was torn away; then the wreck fell to the paving below. In all 78
  pieces of wreckage and 900 bits, "10 parts broken up, 1 torn away, 3 tanks split, 2 gone up". Smoke climbed the
  building's face.
* 65 frames a second just after (the game's cap not lifted, 1280 by 720, an RTX 4080 SUPER); a second throw into
  the smoke of the first, 53.
* Once, a part already gone up in a step was crushed again by a later blow in the same step, and the mod caught a
  `NullReferenceException` in `Hull.CrushAt` (the crash went on). A gone part is now passed over.

**Again on 2026-10-11** (0.2.2 with Volumetric Explosions 0.6.3, the smoke's shader built for Direct3D again that
day; the development build, by day). A craft put so high over the ground by the pad (the bridge's
`dev_set_position` with `ease: false`) and let fall, filmed frame by frame from a place held beside it:

* Kerbal X from 32, 110 and 150 m onto the grass: "Rockomax X200-32 Fuel Tank broke up into 4 pieces", "FL-T400
  Fuel Tank split and is leaking (2000 kg in it, alight)", "FL-T400 Fuel Tank is torn away from FL-T400 Fuel
  Tank", debris "left as it lies: slid 3 m and lies 6 m from where it first touched". In the pictures: the flash,
  a ball of fire with flames in brown smoke that grew to fill most of the picture from 190 m off, its shadow on the
  grass, fires burning under the smoke a quarter of a minute on. From 70 and 85 m the camera was inside the smoke.
* Kerbal 1 (a pod on a tank and two Hammers) from 45 m onto the road, from 38 m off: two boosters and a tank
  "split and is leaking ... alight", a booster "went up (2724 kg), 0% of its skin left"; earth and clods thrown
  up in a ring, a scorched scar, and at the end three fires on the ground at pieces of the wreck, each under its
  own smoke, a piece of the wreck lying between them.
* No exception in the game's log. Smoke trailing from single pieces in flight was not made out in any of these:
  the blast's own smoke hid them from where the camera stood.
