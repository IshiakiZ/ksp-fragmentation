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
