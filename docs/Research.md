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

**→** (First round) On soil: friction 0.5 g plus ploughing that grows with speed (0.22 m/s² for every metre a
second); on paving 0.35 g plus very little ploughing. (Second round, after the user saw the wreck slide far and
roll, whole: see "The tube itself" and "Soft ground" below.) On soil a part now digs in as it slides, and ploughs
harder the deeper it is and the more its skin is torn (0.5 g plus 0.18 m/s² for every metre a second, times one
and a bit for every 18 cm dug in, times 0.4 for a smooth skin up to 1.6 for a torn one): about 1 g for a smooth
tank skating on at 50 m/s, 3 to 4 g for a torn one dug 25 cm in. On paving the friction grows from 0.35 g to
0.9 g as the skin is torn. "How far a wreck slides" divides the deceleration. (Later in the second round, after
the tube was seen still whole at the end of a 95 m slide: the skin is torn away from below five times as fast,
see "The tube itself"; a skin torn open ploughs at up to 3 times a smooth one's (0.4 + 2.6 times the share of
it torn, reaching its full share at two fifths), and dug in, its friction grows to up to 2.4 times (torn metal
holding in the earth), which is the AAIB's "ploughing and scooping of soil into the airframe resulting in high
deceleration levels": 3 to 5 g once torn, and the test rocket at 70 m/s stops in 60 to 80 m, as the 737 on soft
farmland at Schiphol stopped "relatively quickly".)

### The tube itself (second round)

Asked of the first version: "I don't mean each module, each piece. I mean the actual tube gets fragmented", and
"a rocket is huge and heavy. It shouldn't slide nearly at all without having any damage to the tube. The bottom
of the tube should be disintegrated, but there could be a little bit at the top." What real tubes do:

* **The underside goes first, the top survives.** Of the 767 freighter that dived into a marsh at over 400 knots,
  "the largest pieces of airplane structure were from the upper part of the airplane; structures from the lower
  third of the fuselage ... were fragmented into smaller pieces" ([NTSB AAR-20/02](https://www.ntsb.gov/investigations/AccidentReports/Reports/AAR2002.pdf)).
  In NASA's drops of Fokker F28 fuselage sections onto soil at 8.8 m/s the frames at the very bottom failed first,
  in tension, as the tube went oval, and the sides bulged 13 cm ([NASA/TM-2018-219829](https://ntrs.nasa.gov/api/citations/20180004391/downloads/20180004391.pdf));
  in the Boeing 720 flown into the ground, the belly crushed 15 to 23 cm at 3.7 to 5 m/s of sink, and the nose took
  the hardest blow (14 g) and the tail the least (under 4 g) ([NASA TP-2589](https://ntrs.nasa.gov/api/citations/19860021249/downloads/19860021249.pdf)).
* **A tube breaks across itself, in a few places.** In 153 survivable airliner accidents, the fuselage broke in at
  least one place in about half; the gear came off in three in four, the engines in one in two
  ([DOT/FAA/CT-83/42](https://rosap.ntl.bts.gov/view/dot/92540/dot_92540_DS1.pdf)). The 737 at Kegworth broke into
  three, just ahead of and just behind the wing box, its nose crushed some 2.6 m, and the pieces went on only 21 to
  27 m after the main blow ([AAIB 4/90](https://rvs-bi.de/publications/Incidents/DOCS/ComAndRep/Kegworth/AAIB/COPY/gobmerep.htm));
  the 737 that came down tail first on soft farmland at Schiphol stopped "relatively quickly", its wreckage trail
  about 100 m long, in three pieces ([Dutch Safety Board](https://onderzoeksraad.nl/prelimenary_engels-pdf/));
  steel posts at Little Rock "peeled away the left side of the fuselage from the nose to the second coach row"
  ([NTSB AAR-01/02](https://www.ntsb.gov/investigations/AccidentReports/Reports/AAR0102.pdf)).
* **A whole tube slides far; a torn one ploughs.** An intact 777 sliding on its belly along a concrete runway went
  800 m in 32 s, about 0.2 to 0.25 g, and "remained intact" ([GCAA AIFN/0008/2016](https://www.gcaa.gov.ae/en/departments/airaccidentinvestigation/Lists/Incidents%20Investigation%20Reports/Attachments/110/2016-Published%20Final%20Report%20AIFN-0008-2016-UAE521%20on%206-Feb-2020.pdf));
  the 777 that came down on very soft grass at Heathrow lost about 0.39 g with its gear and engines dug in 0.3 to
  0.45 m, "scooping up soft soil", and the AAIB warns that in worse impacts "fuselage deformation may result in
  ploughing and scooping of soil into the airframe resulting in high deceleration levels"
  ([AAIB 1/2010](https://assets.publishing.service.gov.uk/media/5422f3dbe5274a1314000495/1-2010_G-YMMM.pdf)).
* **Fast, it is all small pieces.** The A320 flown into the Alps at about 180 m/s: "The largest parts of the
  aeroplane were about 3 to 4 metres long" (a tenth of its length), the debris over 4 hectares
  ([BEA2015-0125](https://www.bea.aero/uploads/tx_elydbrapports/BEA2015-0125.en-LR.pdf)).

* **A rocket has no belly.** An airliner's lower fuselage is built to be landed on: a keel beam, the floor's
  beams and frames every half metre or so, and a skin that, slid on grass or concrete, is scraped rather than
  torn through (the 777s above). A rocket's tank is built to carry its load along its length, and much of its
  stiffness in flight is its own pressure; on its side on the ground it is a shell of a few millimetres resting on
  one line, with nothing under it, and an unpressurised tank crushes at a third of its textbook buckling load
  ([NASA SP-8007](https://ntrs.nasa.gov/citations/19690013955)). A stage that merely tips over at no speed is
  wrecked by it (as Falcon 9 boosters toppling on a landing ship have been, in public footage).

**→** A part is torn, not taken off: its own skin is cut into panels (few large, many small) and a blow crushes the
end that hit, flattening the panels nearest it and folding the rest; dragged along, its underside is torn away
from below, panel by panel, while its top is kept (a fortieth of the drag's work on soil, a thirty-third on paving,
goes into tearing it: enough to take the bottom of a tank's skin within ten or twenty metres at 70 m/s; a panel
half below the plane it is torn to is torn off). The more of a part's skin is torn, the harder it ploughs and
gouges: a smooth skin on soil ploughs at four tenths, a torn one at up to 1.6 times; on paving a smooth skin
slides at about a third of its weight and a torn one at nine tenths, torn metal catching. A part torn right round
comes apart in two, and one that has lost the skin where it is joined to the next part comes away from it; that
is how a long rocket comes apart into a few sections along a slide, at its torn places.

### Ground scars (second round)

* Heathrow, 2008, very soft wet grass: "impact gouges with a depth of up to 0.45 m from the right gear and 0.36 m
  from the left gear"; the engines "dug into the ground" ([AAIB 1/2010](https://assets.publishing.service.gov.uk/media/5422f3dbe5274a1314000495/1-2010_G-YMMM.pdf)).
* NASA's F28 sections dropped flat onto silty sand at 8.8 m/s left "only ripples in the soil ... from the
  stringers", 6 to 38 mm deep: the fuselage took the energy, not the soil ([NASA/TM-2018-219829](https://ntrs.nasa.gov/api/citations/20180004391/downloads/20180004391.pdf)).
* United 93, 250 m/s at 40° into soft reclaimed ground: "a pit about 10 feet deep and 15-20 feet wide with an
  associated mounded pile of dirt opposite the initial impact point" ([AAFS 2003](https://www.aafs.org/sites/default/files/media/documents/AAFS-2003-H70.pdf);
  speed from the [NTSB flight path study](https://www.nps.gov/flni/learn/historyculture/upload/Flight_Path_Study_of_Flight_93_508.pdf)).
  Ethiopian 302, some 250 m/s at 40° into a farm field: a crater about 10 m deep, 28 m wide and 40 m long
  ([Ethiopian AIB](https://shk.se/download/18.2d6f089b18faca29dc815ae9/1698927126370/Aircraft-Accident-Investigation-Bureau-Preliminary-Report31MAR19-2.pdf)).

**→** A wreck sliding on soil leaves one scar, as wide as what of it touches and up to a few tens of centimetres
deep: its floor torn earth at the ground's level (a mesh cannot go below it), darkest at its edges, its lips about
half as high as it is deep (up to 0.3 m: most of the earth is thrown, as clods and slabs of turf), lumpy and
uneven, its outer edges torn turf (the first version drew banks as high as it was deep, up to 0.6 m, one furrow for
each part: seen close, four smooth brown ridges, nothing like a gouge); a flat drop at a few metres a second hardly marks firm ground, so
craters are made only by blows over 8 m/s into the ground, as wide as the cube root of the blow's energy (a metre
for 250 kJ, four and a half metres for 25 MJ: the crashes above are far beyond anything a game rocket does), longer
along the way the part came the shallower it came in, and with the earth heaped up on the far side.

### Soft ground, sparks, smoke and clods (second round)

* **Pressed into soft soil, a body stops in a few metres.** NASA dropped CH-46 helicopter airframes onto soft soil
  at 10 m/s forward and 7.6 m/s down: the first slid out 2.4 m; the second, over wetter soil, dug a crater 23 cm deep
  and "came to rest abruptly" after 1.3 m, its horizontal accelerations half as high again
  ([NASA TRACT](https://ntrs.nasa.gov/api/citations/20160006501/downloads/20160006501.pdf)). While a belly is being
  driven into soft soil the speed it loses along the ground is about what it loses into it: an effective friction
  of about one. A Cessna flown nose down into soft soil ploughed in with its nose gear, its tail buckled, and it
  flipped over ([Fasanella and Jackson, 2016](https://dynalook.com/conferences/14th-international-ls-dyna-conference/aerospace/crash-testing-and-simulation-of-a-cessna-172-aircraft-pitch-down-impact-onto-soft-soil)).
* **Rolling over soft ground** is held back by 0.05 (short grass) to 0.1 (soft sod) to 0.3 (mud, soft sand) of the
  weight, against 0.02 on concrete (rolling-resistance figures as summarised for aircraft on unpaved fields; the
  originals could not be read).
* **How hard the ground pushes back**: soil yields above about 100 to 200 kPa (sand, clay; rock about 600 kPa), so a
  heavy narrow body, or one pressed down hard, sinks and ploughs ([IBC 1806.2 presumptive values](https://gocodebook.com/library/us/ca/ca-building-code/chapter-18-soils-and-foundations/section-1806-presumptive-load-bearing-values-of-soils));
  below about 70 m/s soil resists by its strength, above that by its inertia (Sandia's penetration equations,
  [SAND97-2426](https://digital.library.unt.edu/ark:/67531/metadc697639/m2/1/high_res_d/562498.pdf)), and dense
  parts (engines) bury themselves while thin panels skid.
* **Sparks**: aluminium "is ordinarily non-sparking"; dragged along concrete it did not light petrol even at 20 m/s
  and 10 MPa, where steel did at 4.5 m/s and 0.14 MPa; titanium throws brighter sparks than steel
  ([University of Washington, vehicle fire research](https://depts.washington.edu/vehfire/ignition/mechanical/mechanical.html)).
* **What smokes**: fuel (black, dense, long), oil, paint, insulation and composites (smouldering); clean hot metal
  does not smoke; pieces thrown out of a burning cloud of fuel carry it, burning, for some seconds; a fuel fire on
  wreckage burns for many minutes (Sandia's jet fuel pool fires burned some 25 to 40 minutes).
* **Thrown earth**: most of what a crater throws out lands within one or two of its radii, the rim highest on the
  far side; a little fine dust leaves first, low and fast ([Housen and Holsapple, 2011](https://arxiv.org/pdf/2205.05816));
  a sliding body pushes a bow wave of earth ahead of it and leaves banks along its furrow.
* **Torn tanks**: a ruptured tank in a fire throws one to four big pieces; its first crack runs along it and then
  round it, so that an end cap with a length of shell comes away and may be driven off along the tank's axis by what
  escapes, while the shell unrolls into curled sheets that skip along the ground ([Holden and Reeves, IChemE 1985](https://www.icheme.org/media/12184/viii-paper-13.pdf));
  a tank torn open by a crash, struck hard, breaks into more. Real stage tanks are 1 to 6 mm of aluminium on grids of
  stringers and rings ([NASA](https://www.nasa.gov/sites/default/files/113020main_shuttle_lightweight.pdf); Saturn
  stages as quoted from their manuals), with 25 mm of foam on cryogenic ones; unpressurised, a tank crushes at a third
  of its textbook buckling load, folding like a can ([NASA SP-8007](https://ntrs.nasa.gov/citations/19690013955)).
  In the wreckage of Columbia and others, skins and foam became thousands of light pieces, while rings, domes,
  pressure bottles, engines and turbopumps stayed recognisable and flew furthest.

**→** On soil a part digs in as it slides and ploughs harder the deeper it is and the more it is torn; a round
part rolling over soil is held back by 0.15 of its weight and more as it sinks. Sparks come only where steel (an
engine) drags on paving, few where aluminium skin does. Every piece trails a little smoke: pieces from a part with
propellant in it or out of a fire trail smoke for some seconds, burning ones flame; a clean piece's wisp (a few
seconds of dust and vapour off it) is a little more than a real one would show, asked for by the user. Clods are
thrown forward and sideways, most within a few metres; a crater's rim is heaped up on the far side. A tank that
goes up leaves its rings and stringers among its torn panels; an engine comes away whole.

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
| What a part's structure takes beyond that, crushed right through | its dry mass × ½ × (6² − 1) × tolerance² | calibrated so that a full tank backed by the craft leaks at about 11 m/s into the ground and bursts at about 14-19 m/s (structure driven into tanks leaked them at 8-9 m/s: AAIB 1/2010, FAA 737 drop; rupture at about 20 m/s: estimate) |
| Energy of a blow | ½ × the mass a push there has to move × speed into the ground beyond the tolerance, squared | rigid-body mechanics |
| What a blow does | crushes the part by that share of its length at the end that hit, folding the skin within 1.6 times as far; panels flattened below 55% of the crush torn off; under 2% of it, a dent | Kegworth's nose crushed 2.6 m; the Boeing 720's belly 15-23 cm at 4-5 m/s of sink; tubes fold in rings when driven end on (NASA SP-8007) |
| A blow between two craft | ½ × the two masses' reduced mass × the speed beyond the tolerance, squared; the softer part takes more of it (in proportion to the other's tolerance); a part holding propellant takes 1.8 times its share | a part driven into a full tank leaked it at 8-9 m/s (AAIB 1/2010, FAA 737 drop); Progress M-34 holed Spektr at a few metres a second |
| Bounce | an eighth of the speed into the ground | Heathrow: bounced and hit again 53 m on |
| Ground's bite in a blow | 0.45 of the stopping push, along the ground (paving 0.25) | estimate; soil scooped up "increased the retardation" (AAIB) |
| Sliding on soil | 0.5 g × (1 + 1.4 × how torn × how dug in, each counted up to 40% torn and 15 cm), plus 0.18 m/s² per m/s of speed × (1 + depth dug in / 18 cm) × (0.4 for a smooth skin, up to 3.0 for one two fifths torn or more) | a smooth belly on soft grass 0.39 g (Heathrow); "ploughing and scooping of soil" by a torn belly "high deceleration levels" (AAIB); a body pressed into soft soil stops 10 m/s within 1.3-2.4 m, an effective friction of about one (NASA TRACT); the user: "It shouldn't slide nearly at all without having any damage to the tube" |
| Digging in | towards a quarter of the part's width (8 to 55 cm), most of the way within a dozen metres of sliding; the part sits down by as much | gear and engines dug in 0.36-0.45 m at Heathrow; TRACT's belly made a crater 23 cm deep |
| Rolling over soil | held back by 0.15 of the weight, and 1.5 times the depth dug in (in metres) more | rolling resistance 0.05-0.1 on grass, 0.3 in mud and soft sand |
| Sliding on paving | from 0.35 g for a smooth skin to 0.9 g for one torn 40% or more, plus 0.04 m/s² per m/s | an intact belly on concrete 0.2-0.26 g (Emirates 521); through posts 0.5 g (Little Rock), 0.7 g with posts and yaw (Boeing 720 test); a rocket's skin is not made to slide, and torn, it gouges |
| Grinding | of the drag's work on the part that touches, a fortieth on soil and a thirty-third on paving tears its underside away, against 0.38 kJ for each kilogram of its structure (a tank's skin 11 kJ a square metre), beyond half its tolerance in speed; a panel half below the plane is torn off | estimate, chosen so that a tank sliding on its side at 70 m/s has lost the bottom of its skin within ten or twenty metres (the first figures, a two-hundredth and a hundredth, left the test rocket's tanks whole after a 95 m slide); a rocket's tank has no keel under it as an airliner's belly has; the lower third of a fuselage is what breaks smallest (NTSB AAR-20/02) |
| A tank splits | any panel torn off, or crushed in more than 6% of its length | |
| It catches | 3 in 4 where there is oxygen; where there is none, if it carries oxidiser; crushed right through, 19 in 20 at once | Boeing 720 test (caught in 0.1 s); Heathrow (did not) |
| Then goes up | 0.4 to 1.6 s later (sooner, the worse the tear); crushed right through, within a tenth of a second | Boeing 720 test: 0.1 s with a running engine beside it; the user: "leak for a second then explode" |
| What is left of it | from two thirds of its skin for a few kilograms to a fifth for tonnes, less the harder it was crushed first; the top of it as it lies kept longest; the skin round the hole blackened and a third of its edge bent back; half the time its far end torn off whole and driven along its axis at 6-16 m/s | burst vessels: an end cap and a length of shell driven off along the axis, the shell in curled sheets (Holden and Reeves, IChemE 1985); the far side of a crash survives as the biggest panels (NTSB AAR-20/02) |
| Not alight | runs out over 5 to 9 s; nothing goes up | Heathrow |
| Solid booster broken up | 75% of its propellant leaves burning with its pieces | Challenger's boosters |
| A part's panels | about forty for a tank, few big and many small (sizes by a −1.6 power law, from 0.45 to 1.9 times a typical panel, a typical panel a sixth of the part across), the borders wandering; an engine's meshes whole | NASA Standard Breakup Model; engines and turbopumps stay recognisable (Columbia, Ethiopian 302) |
| Pieces from a panel | ground away: a third become pieces, the rest bits; crushed: two thirds; blown off: most; at most 6 pieces from one part at a time (a part going altogether: 1.2 for each square metre of its skin, 2 to 12); past 2 ms of making pieces for one part (4 for one going altogether), bits only, but always one piece; in a step already 5 ms busy, made in the next steps | Space Engineers' time budget |
| Bits | 10 to 18 for each square metre of panel torn off, 2 to 25 cm (smaller ones more often), a third with rivets, a spacecraft's with foil; at most 900 | skins and foam became thousands of light pieces (Columbia) |
| Rings and stringers | 1-2 arcs of ring and 2-3 stringers from a tank that goes up; 0-1 and 1-2 from one that goes altogether | Columbia, Challenger: ring frames among what stayed recognisable |
| A joint lets go | when less than a quarter of the skin within most of the part's width of it is left | the tube breaks across itself at its torn places (Kegworth, Schiphol) |
| A hurt part bends | what holds it softened as it is hurt and as the skin round its joint goes, to a twelfth of its stiffness | THE FINALS' state between whole and loose |
| Craters | over 8 m/s into soil; as wide as 0.15 × the cube root of the blow's energy in kJ (0.5 to 7 m); longer the shallower, rim highest on the far side | United 93: "a mounded pile of dirt opposite the initial impact point"; firm soil hardly marked at 9 m/s (F28 drop) |
| Scars | one a wreck, as wide as what of it touches the ground across its way; its floor at the ground's level, its lips half as high as it is deep (2 to 30 cm), lumpy; clods 2-12 cm and slabs of turf 5-20 cm thrown | Heathrow's gouges 0.36-0.45 m; most of what a crater throws lands within one or two radii (Housen and Holsapple) |
| Sparks | where an engine drags on paving faster than 8 m/s, a few a metre; an eighth as many from other parts | aluminium hardly sparks; steel lights petrol at 4.5 m/s (University of Washington) |
| Smoke of a piece | out of a fire, or a burning wreck: 8-16 s of dark smoke, some burning; from a part with propellant: lighter smoke; any other: 2.5-5 s of a pale wisp (the user's asking) | pieces thrown out of burning fuel trail smoke for some seconds; clean metal does not smoke |
| The wreck's fire | 14-26 s from when a tank went up, as puffs of flame and black smoke laid where it goes | fuel fires on wreckage burn many minutes (Sandia); shortened |
| This mod's own work on the game's thread | one skin drawn torn for the first time a frame; redrawing about 3 ms a frame on average; pieces 2 ms a part (4 going altogether), the rest of a busy step's in the next; rings, stringers and parting a step or two after a burst | measured (see What was tested) |
| Pieces at once | 150 (setting), the oldest and those lying still going first | Space Engineers (100 loose objects, oldest first) |
| Pieces lying still | out of the physics after 1.5 s still | Teardown |
| When frames are slow | fewer pieces, down to a third at 15 frames a second | Space Engineers |

## Not taken, or not yet

* **Merging settled rubble** into fewer pieces (Red Faction): not done; pieces lying still cost nothing in the
  physics, but are each still drawn.
* **Soft bodies** (BeamNG): too dear, and the game's parts are rigid. A torn part's skin is bent as it is drawn
  (crushed, folded, dented, flaps bent back); for the physics it is the convex hull round what is left of it.
* **Prepared fractures** (Red Faction, Battlefield, perhaps THE FINALS): since the second round, the panels of
  each kind of part are worked out once, when a craft is armed, from its own meshes (so any part from any mod has
  them), on another thread; what a crash then does is tear those panels off where it is hurt.
* **Changing the ground itself**: the game makes its terrain afresh as the view moves, so a scar or a crater is
  a mesh laid on it (a scar's floor at the ground's level, with low lips), and a wreck sits down into the ground by
  as much as it has dug in; nothing is dug out of the terrain, and the marks go when the flight is left. (Cutting a
  hole in the drawn ground with a depth mask would need a shader of this mod's own, which cannot be built for
  Windows here.)
* **Sound**: none of its own; the explosions are the game's (or Volumetric Explosions').
* **Breakup in the air** from aerodynamic loads (Proton-M, 2013, shed its upper stage and outer tanks before
  it hit: [RussianSpaceWeb](https://www.russianspaceweb.com/proton_glonass49.html)) is the game's own, by its
  joints, and is left to it.
* **Dense parts burying themselves** (engines found 10 m down at Ethiopian 302; Columbia's powerheads 4 m down):
  an engine comes away whole and tumbles; it does not bury itself.
