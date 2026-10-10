# Research

What this mod is based on: how other games break things, and how real aircraft and rockets break up when
they crash. Asked for in these words: "research how fragmentation works in other games, like the finals, or
other destructive games", and "how real things break down when they crash like that, and all the physics
and science behind it".

Each finding below ends with what the mod takes from it (**→**). The table at the end puts every number the
mod uses beside where it came from. Sources marked *(reported)* were seen only in search results or second
hand, not read at the source.

## How the game does it now

Every part has a crash tolerance, a speed. Anything touching a part faster than that destroys the part on the
spot. A craft that hits the ground at a slant is destroyed part by part, within a few tenths of a second,
where it first touched. Nothing slides; nothing is left but the parts that happened not to touch anything
fast. ([How it works](How-it-works.md) has where in the game's code this happens.)

## How games break things

**THE FINALS** (Embark Studios). Buildings break on the game's servers, in two layers: one that keeps track of
how the building holds together ("Strain"), and ordinary rigid-body physics for what falls. Until now a piece
was either attached or falling; an update announced for 20 October 2026 adds a third state, a piece still
weakly linked, which bends, sways and sags before it falls
([report](https://ixbt.games/en/news/2026/10/05/roman-smirnov-pokazal-ulucsennoe-razrusenie-okruzeniia-v-the-finals-suter-avtorov-arc-raiders-polucit-bolsoe-obnovlenie.html)).
Its engineers' talk at GDC 2024 covers "constructing and fracturing structures" ahead of play
([GDC Vault](https://gdcvault.com/play/1034280/Engineering-Mayhem-Technical-Deep-Dive); members only, so
whether the fracturing is done in advance is not confirmed). It runs on Unreal Engine 5
([interview](https://www.thefinals.wiki/wiki/March_2023_Interview)).
**→** Breakup decided from how the structure holds together (here, the craft's own tree of parts), not from a
list of prepared pieces. And the middle state: a hurt part's hold on the part it hangs from is softened as it
is hurt (down to a twelfth of its stiffness), so that it sags and folds before it tears off.

**Teardown** (Dennis Gustafsson). Every object is a grid of voxels and its own rigid body; there is no stress
simulation, which he judges impractical at that scale and not better to play. After a cut, a search for what
is still connected decides what has come loose; tiny leftovers of a split are deleted; things below a
"debris" size are a cheaper class; bodies that stop moving are taken out of the simulation
([interview transcript](https://softwareengineeringdaily.com/wp-content/uploads/2024/12/SED1772-Teardown.txt),
[modding API](https://teardowngame.com/modding/api.html)). Fire is heat per voxel, passed to neighbours.
**→** Pieces that have lain still are taken out of the physics; tiny cells of a cut part are dropped; the
connectivity search is the game's own (a part torn off takes what hangs from it).

**BeamNG.drive.** A vehicle is mass points joined by beams, simulated two thousand times a second; each beam
has a force at which it bends for good and one at which it breaks. Parts come off by "break groups": when one
beam of a group breaks, the rest of the group goes too, so a whole panel comes off rather than half of it
([beams](https://documentation.beamng.com/modding/vehicle/sections/beams/)). Fire is per mass point: a flash
point, stored energy, heat from collisions; breaking a fuel container ruptures it and sprays fuel, which as
vapour lights more easily ([fire](https://documentation.beamng.com/modding/vehicle/sections/nodes/fire)).
**→** A ruptured tank sprays before anything else happens to it, and the spray is what catches. The soft
body itself is far too dear for a game that already spends its time on many parts: not taken.

**Red Faction: Guerrilla** (Geo-Mod 2.0). Buildings are pre-broken meshes that answer stress and blows; take
out the supports and they come down ([wiki](https://www.redfactionwiki.com/wiki/GeoMod_2.0)). In the GDC 2009
talk: a tool chose how many pieces each thing needed to look convincing without overloading the processor,
and after the first breakup the rubble was represented by fewer pieces, which players did not notice
([report](https://multiplayer.it/articoli/gdc-2009-red-faction-guerrilla-anteprima.html)).
**→** A budget of pieces for each part (2 to 12, by its size and how hard it was hit), and one for the whole
scene. Merging settled rubble into fewer pieces is not done yet.

**Battlefield** (Frostbite). Destruction is layered: a prepared piece of the building is removed, detail
meshes dress the hole, a damage mask darkens what is left, and particles and some loose meshes are added "for
a more dramatic look" ([Kihl, SIGGRAPH 2010](https://www.advances.realtimerendering.com/s2010/Kihl-Destruction%20in%20Frostbite(SIGGRAPH%202010%20Advanced%20RealTime%20Rendering%20Course).pdf)).
Battlefield 3 mixed dynamic small destruction near the player with scripted set pieces
([interview](https://gamingbolt.com/dice-on-battlefield-3-some-destruction-will-be-scripted/amp)).
**→** The explosions mod's sparks, shards, dust and burn marks do the dressing; this mod's pieces are the
real, solid part of it. Pieces are darkened, scorched where they burned.

**Space Engineers.** A ship is one rigid body; when blocks are destroyed, the disconnected groups become new
ships in one batch, with their speeds kept
([source](https://raw.githubusercontent.com/KeenSoftwareHouse/SpaceEngineers/master/Sources/Sandbox.Game/Game/Entities/Cube/MyCubeGrid.cs)).
At most 100 loose objects by default, the oldest removed when a new one needs room; small far wrecks are
cleaned away; when the processor stays over 90% the game stops some deformation
([settings](https://spaceengineers.wiki.gg/wiki/Modding/Reference/Saves/SessionSettings)). Its own advice
names destroyed parts floating away, and many contact points, as causes of lag
([performance](https://www.spaceengineersgame.com/performance-advice/)).
**→** At most so many pieces, the oldest (and those lying still) going first; fewer and bigger pieces when
the frames are already slow; break-ups done within a few milliseconds a step, the rest the next step.

**From the Depths.** Nothing usable was found on how it handles damage or debris.

**Kerbal Space Program mods.** *Kerbal Krash System* dents parts instead of destroying them: it scales crash
tolerances, and only blows above the scaled value still destroy; damaged tanks leak, engines overheat sooner
([repository](https://github.com/EnzoMeertens/KerbalKrashSystem)). *BDArmory* gives parts hit points from
their size, can make a penetrated tank explode, burns a part's own fuel at the hit point (oxidiser makes it
burn harder; no fire in near-vacuum without oxidiser, nor under water; fires on the ground go out above
120 m/s), and sizes an explosion from the propellant left
([hit points](https://github.com/BrettRyland/BDArmory/wiki/1.2.1-Hitpoints-&-Armor),
[fire](https://raw.githubusercontent.com/BrettRyland/BDArmory/master/BDArmory/FX/FireFX.cs)). No KSP1 mod was
found that leaves lasting wreckage or makes wrecks slide. *KSP2* reportedly chose explosions by fuel, what was
hit and vacuum, and merged parts exploding together into one blast *(reported)*.
**→** Work through the crash tolerance (raised while a craft may crash, so that the game leaves the decision
to this mod), as Kerbal Krash System does; size each blast by what is left in the tank, as BDArmory does
(here the explosions mod does it from the part's contents); no fire where there is no oxygen unless the tank
carries its own oxidiser.

## How real aircraft and rockets break up

### The structure

The first part to touch takes a short, sharp blow; then the craft pitches down and slams the ground a second
time ("slapdown"), and the second blow is often the hardest. In NASA's crashes of three 2,700 kg twin-engine
aircraft at 27 m/s along paths 15°, 30° and 45° below the horizon, every test had the nose hit first and the
cabin and wing spar second, with the highest cabin loads in the second; the nose was severely damaged at 30°
and 45°; at 45° the wing structure pierced both fuel bladders, rivets sheared along a window ledge and the
nose came off at the firewall ([NASA TP-1210](https://ntrs.nasa.gov/api/citations/19780018551/downloads/19780018551.pdf)).
Joints and fittings give way before skin: when a Boeing 777 came down short at Heathrow in 2008 at 7.6 m/s
sink, the left gear's fuse pins broke in their designed order "leaving the fuel tanks intact"; the right
gear was driven into the rear spar and ruptured it, and fuel leaked ([AAIB 1/2010](https://assets.publishing.service.gov.uk/media/5422f3dbe5274a1314000495/1-2010_G-YMMM.pdf)).
In an FAA drop of a Boeing 737 fuselage section at 9.1 m/s, a fuel tank inside was "punctured in numerous
places" ([DOT/FAA/AR-00/56](https://rosap.ntl.bts.gov/view/dot/42293)). An empty propellant tank is tough: a
250 kg one from a Delta 2 "survived reentry relatively intact" ([ESA](https://www.esa.int/ESA_Multimedia/Images/2015/12/Debris_landed_in_Texas)).

**→** The craft is pushed as one rigid body in a blow, so a blow at one end turns it and brings the other end
down: the slapdown comes of itself. A part's strength is its dry structure's, and what is in it adds to the
blow and not to the strength: a full tank, heavy and no stronger, splits sooner than an empty one. Light
fittings between parts (decouplers, small attachments) are crushed or torn first, being light.

### Shallow and steep

Accident investigators read the angle from the wreckage: a level, fast impact spreads wreckage in a fan over a
large area; a steep, slow one leaves a compact site; a fast nose-down one breaks the aircraft up much more
([AAIB investigator, PBS NOVA](https://www.pbs.org/wgbh/nova/vanished/wreckage.html)). Real crashes near
airports are mostly shallow: the US Department of Energy's standard for aircraft crashes into facilities
(DOE-STD-3014) gives mean impact angles of about 6° for airliners and 7° for light aircraft, with mean skids
of 440 m and 18 m ([ORNL](https://info.ornl.gov/sites/publications/Files/Pub108141.pdf); the
[NRC copy](https://www.nrc.gov/docs/ML0110/ML011000369.pdf) has the two rows swapped).

**→** What breaks goes by the speed into the ground, not the whole speed: at a slant most of the speed is
along the ground, and the craft keeps it. Straight down, every part is crushed in turn.

### The slide

* Heathrow, 2008: 53 m/s over the ground; it bounced and hit again 53 m on; the engines scooped up soft soil;
  it stopped 372 m from first impact: about 0.39 g on average ([AAIB 1/2010](https://assets.publishing.service.gov.uk/media/5422f3dbe5274a1314000495/1-2010_G-YMMM.pdf)).
* Asiana 214, San Francisco, 2013: hit the seawall at 106 knots (55 m/s); the tail came off, the fuselage
  tipped nose down, pivoted about 330° and struck again; it stopped about 730 m away, 16 s later: about
  0.35 g ([NTSB AAR-14/01](https://www.ntsb.gov/investigations/AccidentReports/Reports/AAR1401.pdf)).
* The Boeing 720 flown into the ground for the purpose by NASA and the FAA in 1984 (the Controlled Impact
  Demonstration): 149 knots, wheels up, 2° nose up and 13° yawed; it slid yawing to about 40° and stopped
  11 s after first contact: about 0.7 g on gravel, with posts in its way
  ([FAA summary](https://rosap.ntl.bts.gov/view/dot/92854/dot_92854_DS1.pdf), [NASA](https://www.nasa.gov/centers/dryden/pdf/315492main_FS-2009-02-003-CID.pdf)).
* Light aircraft dig in: 27 m/s stopped within 18 m is about 2 g (from DOE-STD-3014's mean skid). NASA added
  "anti-plowing" features to its test aircraft so that they would skid along soil as on concrete, digging in
  being what made the high loads ([NASA, IDRF history](https://ntrs.nasa.gov/api/citations/20040191337/downloads/20040191337.pdf)).

So a wreck that slides on its belly loses about 0.35 to 0.4 g, and one that ploughs in loses several times
that. A rocket has no belly: its engines, fins and round tanks dig in.

**→** On soil: friction 0.5 g plus ploughing that grows with speed (0.22 m/s² for every metre a second): about
0.6 g at 10 m/s, 1.6 g at 50 and 2.3 g at 80. On paving: 0.35 g plus very little ploughing (0.04): about
0.4 g at 10 m/s and 0.7 g at 80. A wreck sliding from 30, 50 and 80 m/s comes to rest after about 50, 110 and
210 m on soil and 110, 265 and 590 m on paving, before what the blows and the parts it loses take off it
(and the game's own friction, which acts as well). "How far a wreck slides" divides the deceleration. The
user's "a couple of hundred feet" (60 m) is what a craft that digs in does from about 35 m/s.

### Tumbling

Asiana's fuselage pivoted 330° and hit again; the Boeing 720 swung to side-on; light aircraft slap down after
the nose. **→** The drag of the ground acts where the craft touches, each place by its own share and its own
way: one end dragging harder turns the craft, a craft slewing round is slowed in its turning, and an end that
digs in pitches it over.

### How many pieces, and how big

NASA's model of how things break up in orbit (the Standard Breakup Model) gives the number of fragments
bigger than a size L (metres) as 6·S·L^-1.6 for explosions and 0.1·M^0.75·L^-1.71 for collisions; on top of
that power law there are 2 to 8 fragments bigger than a metre (equipment shelves, tanks, nozzle bells) which
"account for the bulk of fragment mass" ([Orbital Debris Quarterly News 15-4](https://orbitaldebris.jsc.nasa.gov/quarterly-news/pdfs/odqnv15i4.pdf)).
Launch vehicles' explosion fragments are "larger, fewer, and slower" than a high-explosive model predicts
([Blackwood et al., NASA 2015](https://ntrs.nasa.gov/api/citations/20150002957/downloads/20150002957.pdf)).

**→** The craft's own parts, torn off whole, are the few large pieces that carry most of the mass. A part that
breaks up is cut into pieces whose sizes follow the power law with the explosions' exponent, 1.6: few large,
many small, the largest about half the part. A ground crash makes far fewer pieces than the orbital law
(which is fitted to kilometres a second): 2 to 12 a part.

### Propellant

* **When it catches.** In the Boeing 720 test a wing cutter stopped an engine, severed fuel lines and tore
  into the main tank, and "one-tenth of a second later, ignition occurred"
  ([FAA summary](https://rosap.ntl.bts.gov/view/dot/92854/dot_92854_DS1.pdf)). At Heathrow a large leak did
  not catch at all. At San Francisco fire started in a separated engine after the aircraft had stopped.
* **Several explosions, not one.** In accidents on the pad, propellants "mixed, exploded, re-contacted, and
  exploded again": 3 to 12 separate explosions; in flight, 1 to 3. Propellants ignite themselves before much
  can mix, which caps what goes off at once. The blast is a flash of a few milliseconds; after it comes the
  big visible fireball, with no pressure wave to speak of
  ([Blackwood et al., NASA 2015](https://ntrs.nasa.gov/api/citations/20150002957/downloads/20150002957.pdf)).
* **How big.** A fireball is about 5.8·m^⅓ metres across (m in kilograms of fuel) and lasts about 0.45·m^⅓
  seconds below 30 tonnes ([HSE RR1185](https://www.hse.gov.uk/research/assets/docs/rr1185.pdf)). For siting,
  liquid oxygen and kerosene count as 10% of their weight in TNT ([DoD table](https://ndia.dtic.mil/wp-content/uploads/2018/intexpsafety/Bangham.pdf)),
  a deliberately cautious figure.
* **Solid motors burn.** When Challenger broke up, its solid boosters "flew out of the fireball" and flew on
  until range safety destroyed them ([Rogers Commission](https://www.nasa.gov/history/rogersrep/v1ch3.htm)).

**→** A tank hurt past half splits and leaks; where there is oxygen in the air it catches three times in four
(not always: Heathrow); where there is none, only a tank that carries its own oxidiser burns. Alight, the rest
goes up a moment to a second and a half later (the worse the split, the sooner); not alight, it runs out over
some seconds and nothing goes up. Each tank goes up on its own, where it is when it goes, so a slide leaves
several explosions along its way. A broken solid booster burns: three quarters of its propellant goes with its
pieces, most of which burn, and only the rest is in the blast. The fireball itself is Volumetric Explosions',
which already sizes it by the same cube-root law (a little smaller).

### Leaks, and a vacuum

* A gas leaking into a vacuum flows at the speed of sound in the hole; a tank empties over a time that goes as
  its volume over the hole's area: a cubic metre through a square centimetre takes about 50 s
  ([NASA Glenn, choked flow](https://www.grc.nasa.gov/www/k-12/airplane/mflchk.html); computed). A liquid
  under 3 bar comes out at about 25 m/s.
* Apollo 13: one oxygen tank failed within about a second; the other, damaged, drained "over a period of 130
  minutes", and the venting turned the spacecraft
  ([chronology](https://www.nasa.gov/history/detailed-chronology-of-events-surrounding-the-apollo-13-accident/)).
* Mir, 25 June 1997: the supply ship Progress M-34, flown in "very fast" by hand, hit the Spektr module's solar
  array and radiator and holed its hull; the station's pressure fell, and it spun at about 1° a second; the
  supply ship drifted off at about half a metre a second
  ([NASA](https://web.mit.edu/16.423/www/MirMishapWeb/POSAJun25%2797.html), [NASA history](https://www.nasa.gov/history/SP-4225/multimedia/progress-collision.htm)).
  A collision at a few metres a second is enough to hole a pressure vessel.
* Liquid dumped into a vacuum flashes partly to vapour and freezes partly into ice (a Shuttle water dump made a
  visible trail 2.5 km long: [Pike 1990](https://ntrs.nasa.gov/citations/19900037879)). With no air, pieces fly
  on at the speed they were given.

**→** A clipped tank in orbit splits from the blow or from being dragged along the other craft (metal torn
along metal), leaks, and pushes its craft the other way while it does (the jet's push is the flow times about
25 m/s). In a vacuum the leak shows only if it burns, as a flame (the explosions mod has no smoke where there
is no air). A real tank would usually leak for longer than this mod lets it before it goes; the user asked for
"leak for a second then explode". Pieces in a vacuum have no drag and fly on.

## The numbers the mod uses

| Rule | Value as installed | From |
| --- | --- | --- |
| A part is not hurt by a blow within its own crash tolerance | as the game | the game's own rule, kept |
| What a part's structure takes beyond that | its dry mass × ½ × (6² − 1) × tolerance² | calibrated so that a full tank backed by the craft leaks at about 11 m/s into the ground and bursts at about 14-19 m/s (structure driven into tanks leaked them at 8-9 m/s: AAIB 1/2010, FAA 737 drop; rupture at about 20 m/s: estimate) |
| Energy of a blow | ½ × the mass a push there has to move × speed into the ground beyond the tolerance, squared | rigid-body mechanics |
| A blow between two craft | ½ × the two masses' reduced mass × the speed beyond the tolerance, squared; the softer part takes more of it (in proportion to the other's tolerance); a part holding propellant takes 1.8 times its share | a part driven into a full tank leaked it at 8-9 m/s (AAIB 1/2010, FAA 737 drop); Progress M-34 holed Spektr at a few metres a second |
| Bounce | an eighth of the speed into the ground | Heathrow: bounced and hit again 53 m on |
| Ground's bite in a blow | 0.45 of the stopping push, along the ground (paving 0.25) | estimate; soil scooped up "increased the retardation" (AAIB) |
| Sliding on soil | 0.5 g + 0.22 m/s² per m/s | between a belly slide (0.35-0.4 g: Heathrow, San Francisco) and digging in (about 2 g: DOE-STD-3014 light aircraft) |
| Sliding on paving | 0.35 g + 0.04 m/s² per m/s | belly slides 0.35-0.4 g; 0.7 g with yaw (Boeing 720 test) |
| Grinding | the drag's work on the parts that touch, a 250th of it against their strength, beyond half their tolerance in speed | estimate, chosen so that tanks last a second or two of grinding |
| A tank splits | hurt past half | |
| It catches | 3 in 4 where there is oxygen; where there is none, if it carries oxidiser | Boeing 720 test (caught); Heathrow (did not) |
| Then goes up | 0.4 to 1.6 s later (sooner, the worse the split) | Boeing 720 test: 0.1 s with a running engine beside it; the user: "leak for a second then explode" |
| Not alight | runs out over 5 to 9 s; nothing goes up | Heathrow |
| Solid booster broken up | 75% of its propellant leaves burning with its pieces | Challenger's boosters |
| Pieces of a part | 2 to 12, sizes by a −1.6 power law, the largest about half the part | NASA Standard Breakup Model |
| Pieces at once | 150 (setting), the oldest and those lying still going first | Space Engineers (100 loose objects, oldest first) |
| Pieces lying still | out of the physics after 1.5 s still | Teardown |
| When frames are slow | fewer pieces, down to a third at 15 frames a second | Space Engineers |
| A hurt part bends | what holds it softened from hurt 0.3, to a twelfth of its stiffness at 0.75 | THE FINALS' state between whole and loose |

## Not taken, or not yet

* **Merging settled rubble** into fewer pieces (Red Faction): not done; pieces lying still cost nothing in the
  physics, but are each still drawn.
* **Soft bodies** (BeamNG): too dear, and the game's parts are rigid.
* **Prepared fractures** (Red Faction, Battlefield, perhaps THE FINALS): a mod that must break any part from
  any mod cannot prepare them, so it cuts each part's own mesh when it breaks.
* **Dents** (Kerbal Krash System): a part that survives looks as it did.
* **A gouge in the ground** along the slide, and the sound of it: not done.
* **Breakup in the air** from aerodynamic loads (Proton-M, 2013, shed its upper stage and outer tanks before
  it hit: [RussianSpaceWeb](https://www.russianspaceweb.com/proton_glonass49.html)) is the game's own, by its
  joints, and is left to it.
