# What was tested

All on 2026-10-09, on a Mac (Apple M5 Pro, KSP 1.12.5 under Rosetta, OpenGL, 1280 by 720), in the test save
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
