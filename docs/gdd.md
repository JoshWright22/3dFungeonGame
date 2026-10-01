# Party Wipe — Game Design Document

> A co-op dungeon delve where the dice are public, the voice chat is positional, and the most
> dangerous thing in the dungeon is the friend holding the torch.

| | |
|---|---|
| **Working title** | Party Wipe (repo name stands until decided) |
| **Repo** | JoshWright22/3dFungeonGame |
| **Engine** | Unity 6000.3.23f1 LTS |
| **Pipeline** | Built-in render pipeline in practice. The URP 17.3.0 package is installed, but no render pipeline asset is assigned (checked 30 Sep 2026) |
| **Netcode** | Netcode for GameObjects 2.1.1 · Facepunch / Steam |
| **Party** | 4 players |
| **Rules basis** | *Dungeon Crawl Classics RPG* (Goodman Games, 2012), minus the multi-character funnel. The book is in `docs/dungeoncrawlclas0000good.pdf`. See §13. |
| **Art basis** | The same book: the painted colour plates for the game world, the pen-and-ink pages for menus. See §14. |
| **Doc status** | Draft 3 · 30 Sep 2026 |

## Contents

1. [The pitch](#1-the-pitch)
2. [Design pillars](#2-design-pillars)
3. [The expedition loop](#3-the-expedition-loop)
4. [Characters](#4-characters)
5. [Hands, bodies & physics](#5-hands-bodies--physics)
6. [The dungeon](#6-the-dungeon)
7. [Multiplayer architecture](#7-multiplayer-architecture)
8. [Proximity voice](#8-proximity-voice)
9. [Light & fear](#9-light--fear)
10. [Build status](#10-build-status)
11. [Roadmap](#11-roadmap)
12. [Open questions](#12-open-questions)
13. [Rules basis: Dungeon Crawl Classics](#13-rules-basis-dungeon-crawl-classics)
14. [Art direction](#14-art-direction)

Appendices

- [A. POLYGON demo audit & cave generation spec](#appendix-a-polygon-demo-audit--cave-generation-spec)
- [B. Asset Store packages (not in git)](#appendix-b-asset-store-packages-not-in-git)
- [C. Fix log](#appendix-c-fix-log)

> Section numbers 5 and 8 are cited from code comments ("design doc S5", "S8"). Keep them stable
> when reorganising.

---

## 1. The pitch

*The stairs end in a room the map does not have. Someone's torch is already down to a stub. Behind
you, at the edge of hearing, your cleric is explaining to nobody in particular that he rolled a
seven for Strength and cannot open the door.*

*You have eleven minutes before the way out closes.*

**Party Wipe** is a four-player cooperative dungeon crawl that takes the shape of a tabletop delve —
rolled stats, a dungeon master's grid, loot you have to physically carry out — and runs it at the
pressure and tempo of a shift-based horror co-op, simulated as rigid bodies from the floor up.

Three references, held in tension:

- **Lethal Company** — the shift, and its voice.
- **Dungeon Crawl Classics** — the character sheet, and its public failure. DCC is the rulebook the
  game is built on (§13) and the look it is drawn from (§14): random, brutal, low-level, and proud
  of it — *"You're no hero."*
- **A physics sandbox** — every sword, brazier, loose rock and corpse is a real object that somebody
  can pick up, swing, drop or throw at someone else.

The comedy and the horror come from the same source: you can hear each other, positionally, all the
time, nobody is good at their job, and nothing in the world is bolted down.

### What it is

- Short runs — one dungeon, one extraction window, roughly twenty minutes.
- Character sheets rolled fresh each run. No builds, no loadouts, no meta.
- Physical inventory: two hands, and what's in them is what you have.
- Everything is an object. Props are rigid bodies — grabbable, throwable, and able to hurt whoever is
  standing where they land. Corpses included.
- Combat resolved by mass and momentum, not by an attack animation playing out.
- Voice as a mechanic. Distance, walls and torchlight all change what you can hear.

### What it isn't

- Not a combat game first. Fights are a failure state you sometimes survive.
- Not a progression treadmill. Meta-unlocks widen the content pool, never the power curve.
- Not a tactics sim. There is no turn order and no grid the player can see.

---

## 2. Design pillars

Four rules. When a feature can't be justified against one of them, it doesn't ship.

### I. The dice are public

Every roll that matters is announced in the world — a die-clatter cue at the character's position,
audible to anyone in earshot. Failure is never private, which means failure is always social. This
is the pillar the whole D&D framing exists to serve.

### II. Voice is the interface

There is no ping system, no map marker, no shared waypoint. Coordination happens by shouting down a
corridor and hoping. Every system that could be solved with UI should be solved with the fact that
people can talk to each other and don't.

### III. Competence is unevenly distributed

3d6 in order, no rerolls, no point buy. Someone in the party is going to be genuinely bad, and the
run has to route around them. That person is different every time.

### IV. The dungeon does not scale to you

One seed builds one layout with one distribution of threats, before it knows who is coming. It
doesn't rubber-band, it doesn't tune to party strength, and it will happily generate a run the party
cannot win.

---

## 3. The expedition loop

One run, start to finish. Phases are a real sequence — each hands specific state to the next — and
the durations are tuning targets, not measurements.

| # | Phase | Duration | What happens |
|---|---|---|---|
| 1 | **Muster** | 60–90 s · lobby | Steam lobby fills to four. The host's seed is fixed and broadcast. Every player's sheet is rolled server-side and shown to the whole party at once — you see how bad everyone else got it before you see the dungeon. |
| 2 | **Descent** | 30 s · fixed | The one scripted beat in the run. Party rides down together, torches lit, no threats. Exists purely so everyone is in voice range once before they scatter. |
| 3 | **Delve** | 8–12 min · open | Explore, loot, avoid. The party splits almost immediately because two hands isn't enough carry capacity, and splitting is what makes proximity voice bite. |
| 4 | **The turn** | triggered | A threshold event — quota met, a specific room opened, a timer — flips the dungeon's behaviour. Doors that were open close. Something starts moving toward the loudest player. This is the moment the run stops being a looting game. |
| 5 | **Extraction** | 2–4 min · timed | Back to the entry stair with whatever is in your hands. Carry weight fights you. Dropping loot to run is always the correct call and nobody ever makes it. |
| 6 | **Tally** | 45 s · lobby | Value extracted against quota. Survivors keep nothing but the story. Meta currency unlocks new dungeon furniture and item templates for the pool — never stats. |

---

## 4. Characters

A delver is a DCC 0-level character, built by DCC's rules (§13), with one change: **one character
per player**, not a funnel of three or four. Rolled at spawn, on the server, never edited by the
player.

### Delver — player character, rolled fresh each expedition

| Step | DCC rule | Here |
|---|---|---|
| Abilities | 3d6 **in order**: Strength, Agility, Stamina, Charisma, Intelligence, Luck (DCC calls Charisma *Personality*; we use Charisma) | Same. No rerolls, no point buy, no swapping. |
| Modifiers | 3 → -3 · 4–5 → -2 · 6–8 → -1 · 9–12 → 0 · 13–15 → +1 · 16–17 → +2 · 18 → +3 | Same table, everywhere a roll is made. |
| Hit points | 1d4 + Stamina modifier, minimum 1 | Same. A delver is fragile. |
| Birth augur | d30 "lucky roll": one kind of roll the Luck modifier applies to, for life | Same, shown on the sheet ("Born on the battlefield: damage rolls"). |
| Occupation | d% on the occupation table: a trained weapon and some trade goods | **The starting loadout.** The weapon and the goods spawn in your hands at the Descent. A cheesemaker starts holding a cudgel and a stinky cheese. |
| Alignment | Lawful, Neutral or Chaotic | Chosen in the lobby. Hooks for later (patrons, Luck recovery). |
| Saving throws | Fortitude (Stamina), Reflex (Agility), Willpower (Charisma) | Same; the dungeon rolls them against you (§13). |
| Starting money | 5d12 copper | Replaced by the quota economy (§3). |

| | |
|---|---|
| Ability range | 3–18 · mean 10.5 · 1 in 216 for an 18 |
| Authority | Server rolls once on spawn, replicated read-only |
| Rerolls | None. Ever. |
| Classes | Emergent at first — set by what you pick up (§5). DCC classes at level 1 are an open question (§12). |

**Code is behind this.** `PlayerStats` still rolls STR, DEX, INT and HP = max(STR, DEX) + 1d4. It
needs the six DCC abilities, the modifier table, 1d4 + Stamina HP, the birth augur and the
occupation roll.

The whole party's sheets are visible: hold **Tab** for the character sheet HUD, which shows yours in
full and everyone else's beside it (pillar I).

### What abilities actually do

DCC says what each ability modifies at the table; this is how each one reaches the real-time game.
Proposal, not final.

| Ability | DCC | In game |
|---|---|---|
| **Strength** | Melee attack and damage; STR 5 or less can carry a weapon *or* a shield, not both | Swing force and damage; carry weight before slowdown; forcing doors and lids. STR ≤ 5: can't hold two heavy things at once. |
| **Agility** | AC, missile attacks, initiative, Reflex | Sprint speed ceiling, throw accuracy, how often a trap or a swing misses you. |
| **Stamina** | Hit points, Fortitude; STA ≤ 5 takes double poison damage | HP, stamina-bar size and recovery, poison and gas resistance. |
| **Charisma** | DCC's *Personality*: Willpower; clerics' divine power | Resisting fear and charm effects. Possibly voice range (the loud one carries). |
| **Intelligence** | Languages, wizard spells; INT ≤ 7 Common only, ≤ 5 can't read | How much an item's description tells you. INT ≤ 5 can't read scrolls, signs or labels. The smart one has to read them out loud (pillar II). |
| **Luck** | Crits, fumbles, the birth augur roll; can be burned | Same: crit and fumble odds, the augur, and **Luck burn** (§13). |

INT-as-information is still the pick worth taking the risk on: it makes a bad stat funny and useful
to the party rather than just a debuff.

### Movement

Walk / sprint / crouch, tuned for corridors. Sprint burns stamina that only recovers when you slow
down, and what you are carrying slows you down — so the arm-or-loot decision (§5) is felt in the
movement, not just in an inventory screen. The stamina bar fades out when you're rested and turns
red while sprint is locked out.

---

## 5. Hands, bodies & physics

There is no inventory screen and no interact prompt. You have a left hand and a right hand, bound to
the left and right mouse buttons. A button pressed with an **empty** hand grabs what you are looking
at; pressed with a **full** hand it uses what that hand holds. That is the whole verb set — two
buttons, two hands, and the physics does the rest.

| Input | Hand empty | Hand full |
|---|---|---|
| LMB | Grab into left hand | Use left-hand item |
| RMB | Grab into right hand | Use right-hand item |
| Two-handed | Fills both hands | Either button drives it |

### What you can pick up

Everything. Not a curated list of interactables — if it is in the dungeon it has a handle. Loose
rocks. The brazier, still lit, dripping as you carry it. A sword an enemy dropped when it died. A
torch off the wall. The quest item. And each other: a downed friend and an enemy corpse are objects
like any other, so a body can be dragged to the exit, used as a shield, or thrown down a stairwell
at whatever is following you.

This is the pillar the two-hand rule exists to bite against. Every hand holding a weapon is a hand
not carrying loot — and now also not carrying your unconscious cleric.

### Ragdoll combat

Swings are physical. A weapon in hand is a moving mass with a collider, and what it does on contact
is a function of that mass and how fast you moved it — not of a damage number attached to the item.
Consequences that fall out of this for free, and are the reason to do it:

- A brazier is a poor sword and an excellent way to set a room on fire.
- Reach and swing arc are real, so fighting in a corridor differs from fighting in a crypt hall
  without a single line of code that knows what a corridor is.
- You can hit your friends. You will hit your friends.
- Anything killed becomes a ragdoll and stays in the world as a physics prop, which is the same
  system as loot — no separate corpse handling.

### Animation classes

Items are `ObjectModelSO` scriptable objects carrying a display name, a description string, a
prefab, a size, and — the interesting field — an **animation class**. The animation class is the
closest thing the game has to a character class, and it is decided by what you picked up:

| Animation class | Reads as | Hands |
|---|---|---|
| Carry | Not a fighter. Hauling loot. | 1 or 2 |
| SwordAndShield | The one who goes first | 2 × 1H |
| TwoHandWeapon | Committed. Cannot carry loot. | 2H |
| Bow | Ranged, needs the corridor clear | 2H |
| Wizard | Whatever the staff does | 1H |
| Rogue | Fast hands, no reach | 1H |

The tension this creates is the core inventory decision of the run: every hand holding a weapon is a
hand not carrying loot. A party that arms itself properly extracts nothing. A party that fills its
hands with treasure has no answer when the turn comes.

Descriptions are flavour text with mechanical consequences via INT (§4). They should be written to
be read aloud badly, over voice chat, by someone who is running.

### Engineering position — unbuilt, and one decision blocks it

None of this section exists in the project yet. `PickupObject` is an empty stub holding a size and an
`ObjectModelSO` reference, and there is no ragdoll, joint or `ArticulationBody` code anywhere in
`Assets/Created/Scripts/`.

There is a real conflict to resolve first. The delver runs on **KinematicCharacterController** — a
kinematic motor that moves by sweeping, does not respond to forces, and already carries the Netcode
ownership modifications this project depends on. A kinematic player can push objects but can never
be knocked over by one, which is precisely the half of ragdoll combat that is funny. The choice:

1. **Keep KCC.** Items and enemies are fully physical; the player is not. Players shove the world,
   the world never shoves back. Cheapest, keeps the netcode, and quietly drops half the pitch.
2. **Keep KCC, add a ragdoll state.** Normal movement stays kinematic; on a hard enough hit the motor
   hands off to a ragdoll and back again. Most of the feel, at the cost of a fiddly state handoff to
   get right over the network.
3. **Move to a physics-driven controller.** Honest to the pitch, throws away the KCC netcode work,
   and makes movement feel worse until it is tuned.

**Option 2 is the one to build.** It should be settled before any item work, because it decides
whether items need to apply forces to players at all.

---

## 6. The dungeon

### 6.1 Generation pipeline

`Generator3D` builds a level on a `Grid3D<CellType>`. One grid cell is **5 world units**; cells are
typed `None`, `Room`, `BottomFloorRoom`, `Hallway` or `Stairs`. Layout is computed in full before
anything is built, so unreachable rooms are pruned before they get geometry.

| Stage | Method | What it does |
|---|---|---|
| Place rooms | Rejection sampling | Rooms drawn from weighted archetypes (§6.3), discarded on intersection, until `roomCount` is met. Natural caverns are reserved *before* masonry rooms. |
| Triangulate | `Delaunay3D` | Tetrahedralisation over room centres — every plausible connection between neighbours, no long-distance nonsense. |
| Select edges | Prim MST + loops | MST guarantees full connectivity, then a fraction of discarded edges are added back. Those extra edges are what make the dungeon a place you can get lost in rather than a tree you can back out of. |
| Carve hallways | `DungeonPathfinder3D` · A* | Prices stairs high and already-carved hallway low, so corridors converge and share. Vertical movement is expensive by design — the dungeon sprawls before it descends. |
| Reachability | Prune + seal | Unreachable rooms are deleted; unreachable space is sealed off. |
| Build & dress | Kit placement, `Generator3DDressing`, `Generator3DComposition` | Walls, doors, stairwells, ceilings; landmarks, props, formations, fixture lights. |
| Caves | `Generator3DCaves`, `Generator3DCaveKit` | CA chambers, worm tunnels, rivers; rock assembled from the POLYGON cave kit (Appendix A). |

Per-dungeon tuning lives in a `DungeonProfile` asset (currently `Crypt`: 64 × 4 × 64 grid, 34 rooms).

**Stairwells** are treated as tubes: walled on the sides and back, open along their axis, with the
space beneath them floored.

**Doors** swing away from a delver who comes near and close behind them. They have no RPC: every
client built the same door from the same seed and sees the same replicated player positions, so
every client reaches the same answer — synchronised by construction.

### 6.2 Seeding and determinism

Generation is fully deterministic from a single integer. `Generate(int seed)` is the entry point.
The network handshake (`DungeonRunner`): host picks a seed → seed is replicated → every client calls
`Generate(seed)` locally → clients report generation complete → host releases spawn. **Geometry is
never sent.**

### 6.3 Room types

A room's *type* is **a structural shell + a dressing palette + a size band**, not a bespoke template.

#### Shells

Strip the dressing and there are only five structural shells in the POLYGON Dungeon demo:

1. **Masonry hall** — `Wall_01` courses, stone trim, flat or curved stone ceiling, square pillars.
2. **Timbered basement** — `Basement_WallPanel_01`, support poles, beams. Always low, one storey.
3. **Vaulted channel** — curved roof over a sunken canal. Sewer only.
4. **Rock cavern** — the cave kit.
5. **Open air** — entrance and exterior.

In code, `RoomShell` is `MasonryHall`, `Gallery` (narrow, arcaded), `Basement`, `Chamber` (small,
plain, functional). Caves are a separate size-class list (Pocket / Side chamber / Cavern).

#### Archetypes in the Crypt profile today

Weighted, with an optional `guaranteed` quota so a floor always gets the big spaces it is built
around:

| Archetype | Shell | Weight |
|---|---|---|
| Chamber | MasonryHall | 4 |
| Cell | Chamber | 3 |
| Cellar | Basement | 3 |
| Gallery | Gallery | 2 |
| Great hall | MasonryHall | 1 |
| *Cave:* Pocket / Side chamber / Cavern | Rock | 5 / 3 / 1 |

#### Types in the reference demo

Re-measured 30 Sep 2026 from `polygon-demo-inventory.json` and the live demo scene. "Cells" is
the footprint in 5 m grid cells. "Walls" is the wall-course mix; "layers" are the pieces laid over or
around the bare walls; "lights" counts point lights inside the room's footprint.

| Type | Footprint | Storeys | Walls | Structure & layers | Dressing | Lights |
|---|---|---|---|---|---|---|
| **Great hall** (`Long_Hall`) | 37 × 75 m · ~7 × 15 cells | 4–5, with a sunken level | Wall_01 ×49, 01_Alt ×22, 04 ×15, 03 ×10, Wood_Beams ×7 | Trim_Top ×44, Trim_Edge ×29, outer corners ×26, inner ×24, Wall_Pillar_Large ×24, pillar bases ×18, railings ×14 (balcony); flat + curved stone ceiling | Banners, candles, ornate torches, fire FX, rubble | 30 |
| **Junction hall** (`Center_Hall`) | 35 × 36 m · 7 × 7 | 5 (floors at 0–20 m) | Wall_01 ×33, Wood_Beams ×18, 04 ×13, 03 ×8 | Pillar_Square_01 ×28, Trim_Top ×28, railings ×13 + railing pillars ×20, round large pillars, alcoves, stairs | Vases (whole + broken), barrels | 10 |
| **Entrance** | 26 × 26 m · 5 × 5 | 4 | Wall_01 ×39 (almost uniform) | Flat rock, cracked rock, stairs ×8 | Twisted trees | 2 |
| **Torture room** | 35 × 26 m · 7 × 5 | 3 | Wall_01 ×18 + Basement_WallPanel ×12 over it | Basement ceiling ×9, beams ×6 + bolts, support walls ×8, floor trim ×11 | Cages ×9, chains ×15, stocks, grinder | 2 |
| **Cell block** | 20 × 15 m · 4 × 3 | 1 | Wall_01 ×9, 03 ×4 | Door frames ×12 + round ×5, inner corners ×16, bars ×6, portcullis ×4, floor grates ×6 | Skeletons, shackles, swing-blade and saw traps | 6 |
| **Barracks** | 20 × 15 m · 4 × 3 | 1 | Basement_WallPanel ×14 over Wall_01 ×8 | Support poles ×12, basement ceiling ×8, support walls ×6 | Beds ×5, chests ×6, stools, weapon rack, rug, chandeliers ×2 | 2 (range 15) |
| **Laboratory** | 25 × 10 m · 5 × 2 | 1 | Wall_01 ×13 only | Archways ×8, Pillar_Square_05 ×7, Ceiling_Arch ×6, Trim_Edge ×9 | Bookcases, potions, cauldron, flame/acid traps | 2 |
| **Cellar** | 15 × 15 m · 3 × 3 | 1 (sunken) | Basement_WallPanel ×9 over Wall_01 ×7 | Support poles ×8, basement ceiling ×6, support walls ×6 | Barrels (whole + broken), chains | 1 |
| **Furnace** | 10 × 15 m · 2 × 3 | 1–2 | Wall_01 ×6, 03 ×2 | Archways ×5, Pillar_Square_05 ×5, Ceiling_Arch ×4, fireplace wall set | Forge, vent pipes, metal crates | 1 (range 20, bright) |
| **Sewer** | 35 × 40 m · 7 × 8 | 2 | Wall_01 ×17, 03 ×9, 04 ×9 | Curved roof ×9, canal ×8 | Water | 9 |
| **Ritual site** | 12 × 11 m | 1 | none | Obelisks on cracked rock | A **landmark placed inside another space** | — |
| **Cave system** | — | — | — | Crystal (lush) and Goblin (inhabited) palettes. See Appendix A. | | |

What the numbers say:

- **Every room has one dominant wall** (Wall_01, 55–100% of its courses) with a few accent variants.
  Wall_05 and Wall_06 are never used.
- **Bare walls are a minority of the structure.** Trims, corner caps, pillars, beam frames and
  wainscot panels outnumber the plain wall courses in every masonry hall.
- **The basement look is a layer, not a different wall.** Basement_WallPanel (5 × 2 m) is a
  wainscot over ordinary Wall_01, plus poles every cell and a timber ceiling.
- **Small rooms are 2–5 cells on a side.** Only the two hubs and the torture room reach 7.
- **Ambient is black, and there's no fog or skybox.** The demo is lit only by its own fixtures:
  78 of its 93 lights are warm point lights at range ~10 and intensity ~3, roughly one per
  50–100 m² of floor, plus candle chandeliers.

#### Types to add

In rough order of how much new machinery each needs:

| Type | Shell | New machinery required |
|---|---|---|
| Barracks | Timbered basement | Furniture placed relative to walls rather than scattered. |
| Cell block | Masonry | Sub-partitioning a room into cells off a spine corridor (BSP inside a room). |
| Sewer | Vaulted channel | Rivers are cut; needs the barrel vault and canal kerb. |
| Crypt / catacomb | Masonry, low | Niche and sarcophagus placement along walls. |
| Collapsed room | Masonry, broken | Rubble volumes, broken stairs, partial ceiling. Blocks a route, forces a detour. |
| Mine workings | Rock cavern | Track pieces, timber supports, a shaft. |
| Flooded chamber | Any | Water at floor level; wading changes the pacing. |
| Vault / treasury | Masonry, small | Single locked entrance — needs a key/lock system first. |

### 6.4 Connection rules

A connection is defined by **what the transition is made of**, because that is what breaks when it
is wrong (the see-through seam at the cave/masonry border was exactly this failing).

#### Transition kinds

| Kind | Built from | Used between |
|---|---|---|
| Door | `Wall_DoorFrame_*` + leaf | Two masonry rooms; anything to a cell block. |
| Archway | `Wall_Archway_01`, no leaf | Same-shell, same-storey rooms; hall to gallery. |
| Tunnel mouth | `Wall_Tunnel_Entrance_01` on the masonry side; rock kit leaves that face open | Any built room to a cave. |
| Stair | `Stairs_01/02`, walled on three sides | Any type to any type across storeys. |
| Natural passage | Rock kit only | Cave to cave. |

#### Allowed pairs

Row → column. The storey rule is enforced today; the rest is proposal.

| | Great hall | Junction | Basement | Gallery | Cell block | Sewer | Cave |
|---|---|---|---|---|---|---|---|
| **Great hall** | archway | archway | door | archway | door | stair | tunnel |
| **Junction** | archway | — | door | archway | door | stair | tunnel |
| **Basement** | door | door | door | door | door | stair | tunnel |
| **Gallery** | archway | archway | door | archway | door | stair | tunnel |
| **Cell block** | door | door | door | door | — | — | — |
| **Sewer** | stair | stair | stair | stair | — | stair | tunnel |
| **Cave** | tunnel | tunnel | tunnel | tunnel | — | tunnel | natural |

#### Rules behind the table

1. **A cell block is a leaf.** One entrance, through a door. A corridor that runs *through* a prison
   is not a prison.
2. **A vault is a leaf too**, when it exists.
3. **Caves never connect to a cell block.** A hole in the wall defeats the room's only purpose.
4. **The sewer is entered from above.** Stairs, or a tunnel from a cave — never level-to-level.
5. **Archways only between equals.** Use a door anywhere the character of the space changes.
6. **Every cave-to-built transition is a tunnel mouth.** Never a bare hole, never a door.
7. **Cave to cave is a natural passage**, carved by the worm, never a corridor.
8. **Stairs are universal but expensive.** That keeps a storey sprawling before it descends.
9. **Landmarks are not connections.** They go *inside* a room and reserve their own clearing.
10. **One entrance is the entry room** — always masonry, top storey, and lit. The run does not open
    in darkness.

#### Degree rules

- Every type takes at least one connection, or the reachability pass deletes it.
- Cell block, vault, furnace: exactly one.
- Great hall and junction hall: three or more — they are the hubs.
- Cave chambers: at least one natural passage *and* at least one tunnel mouth to built space.
- Everything else: one to three.

#### Implemented vs proposed

- **Built:** masonry shell, gallery/basement/chamber archetypes with size bands, rock-cavern kit;
  doors, stairs, cave mouths, natural worm passages.
- **Not built:** vaulted-channel shell; archway-vs-door distinction; degree rules; leaf rule for
  cell blocks and vaults; connection choice keyed off room type.

### 6.5 Why our rooms look off

The biggest problem right now. Compared against the demo measurements in §6.3 (seed 202, Crypt
profile, 30 Sep 2026):

| # | Ours | Demo | Fix |
|---|---|---|---|
| 1 | **Room shell is never read by the builder.** `RoomArchetype.shell` picks a size band only; a Cellar and a Great hall get identical stone. | Shell decides the wall layer, ceiling, poles and props. | Per-shell kits in `DungeonProfile`: wall set, overlay layer, ceiling, pillar/pole, prop palette. |
| 2 | **Walls are a uniform random draw per 5 m segment** from six variants, including Wall_05/06. Produces a patchwork. | One dominant wall, accents in short runs. | Weighted wall set per shell (e.g. Wall_01 70%, 01_Alt 15%, 03/04 15%), drop 05/06, pick accents per run, not per segment. |
| 3 | **Floor tiles are a uniform random draw** from Tiles_01–06 per cell. | A dominant tile per room with patches. | Pick a room tile and an accent tile per room; accent at ~15%. |
| 4 | **No layer pieces.** Bare walls only, no trims, corner caps or beams. | Trim_Top on every course top, inner/outer corner caps at every corner, Wood_Beams frames, wainscot panels, floor trim. | Add a layer pass after walls: corner caps, top trim per course, shell overlay (beams / basement panel / pillar every N cells). |
| 5 | **One flat ceiling everywhere.** | Flat, curved, arched or timber depending on room. | Ceiling per shell (MasonryHall flat/curved, Gallery `Ceiling_Arch`, Basement `Basement_Ceiling`). |
| 6 | **Tall rooms show open space above the first wall course.** Seen in a 3-storey great hall, where cave rock and sky showed above a single course. Needs checking in play mode. | Walls stacked per storey with trim between. | Verify; stack wall courses to the room's full height. |
| 7 | **Lighting reads cold and grey.** Blue-grey ambient (0.035, 0.037, 0.045) plus blue-black fog; sparse fixtures at 0.22 per wall cell. | Black ambient, dense warm fixtures and chandeliers. | Decide the light question in §12 first, then tune density and colour. |
| 8 | **One global prop list.** Torture cages can turn up in any room. | Props belong to the room's purpose. | Prop palette per archetype. |

Screenshots of both are in `Assets/Screenshots/` (`demo_*.png`, `ours_*.png`; gitignored).

**Progress (30 Sep 2026): the Cellar looks like the demo.**

- `RoomKit` assets (`Assets/Created/Dungeon Profiles/Room Kits/`) are referenced from an archetype's
  `kit` field. They fix points 1, 2, 3, 5 and 8 for any room that has one. `Cellar.asset` is built
  from the demo cellar's pieces: timber band hung from the ceiling line, posts with braces at every
  wall joint, timber ceiling, kegs lined down the longest wall, barrel heaps, shelves, chains,
  rubble, and candle stands spread across the room.
- `Default.asset` is used for corridors and every room without a kit of its own. It has weighted
  Wall_01-dominant walls and a dominant floor tile, which removes the patchwork everywhere.
- Fixtures thin out with distance from the entry (`fullyLitDistance` / `darkDistance` on the profile).
- **Layout fix:** a corridor running alongside a room used to turn every face it touched into a
  doorway. Doorways now go only where the corridor actually enters the room. Other faces are
  walled.
- Cellar footprint capped at 4 × 4 cells; the demo's is 3 × 3.
- **Still open:** corner caps and top trim for masonry (point 4); the tall-room gap (point 6); kits
  for the other archetypes; the profile's generic torches ignore the distance falloff and still
  place ~150 real-time lights per dungeon.

**Progress (30 Sep 2026, later): every room type has a kit.**

| Room type | Kit | Built from the demo's |
|---|---|---|
| Cellar | `Cellar` | Cellar: timber band, posts and braces, kegs, barrel heaps, candle stands |
| Barracks *(new)* | `Barracks` | Barracks: timber frame, bunks lined down a wall, footlockers, weapon rack, table with stools, rug, chandeliers |
| Gallery | `Laboratory` | Laboratory: archways on every interior cell boundary, round and square columns, stone plinth, arch vault, bookcases, workbench, cauldron |
| Cell | `Cell` | Cells: corner caps, barred doors, wall sconce, shackled and slumped skeletons, drain grate |
| Chamber | `Chamber` | Center Hall walls: corner caps, course trim, wall pillars, sconces, pots, crates, candles |
| Great hall | `GreatHall` | Long Hall: pilasters up every storey, course trim, 7.6 m banners, sconces |
| Torture room *(new)* | `TortureRoom` | Torture Room: timber frame, cages, stocks, iron maiden, the rack, chains, brazier |
| Furnace *(new)* | `Furnace` | Furnace: the arcade, metal crates, engine, workbench, lit by a forge |

Kit features added along the way: inner corner caps, wall-base plinth, course trim, posts at every
storey, interior arcades, floor/ceiling/wall-mounted lights, middle-of-room sets (a table and its
stools), walk-through props (rugs, grates), wall-hung props (banners), per-kit door leaves (barred
cell doors). Props are kept from overlapping one another (stools may tuck under their own table).

**Variation.** Each room is rolled from its archetype's size range (e.g. Barracks 3×3–4×4, Torture
room 4×3–6×5) and every kit choice is a seeded random draw: wall and floor variants by weight per
segment and tile, floor tiles turned at random, each prop set's chance, piece count, wall, spot and
angle, and light count by size and distance from the entry. Two cellars share a style, never a
layout. What does not vary yet: each room type has one kit, so every cellar has the timber band and
every gallery the arcade. Alternative kits per type would be the next step.

**Not done:** Entrance, Junction hall (needs balconies), Sewer (needs the water channel).

**Caverns.** Each cavern now gets themed vignettes (§6.3 kit list in `DungeonProfile.caveVignettes`)
spread through it at one per ~14 cells: crystal stands (cyan glow), mushroom groves (green), goblin
camps and roasts (firelight), abandoned digs, bone heaps (unlit), fallen shrines (magenta), standing
stones (green) and rare hoards (gold gleam). Landmarks may now be 7 m, and their approach lanes no
longer reserve a whole row and column of the cavern.

**Corridors.** Corridor torches are wall sconces (`corridorSconce`). The old floor braziers stood in
the walking line of a quarter of all corridor cells; now none do.

**Performance.** The camera's far plane follows the fog (34 m + 4), and fixture lights switch off
beyond 50 m + their range. Measured on seed 202 from the spawn, CPU render cost per frame went from
7.9 ms to 1.7 ms. Static batching was tried and measured slower with this many point lights, so it
is not used.

**Fixes, round three (30 Sep 2026):**

- **Doors:** every door wall is lined with its `Door_Frame` insert, as in the demo; the open sides
  of the opening are gone, and the leaf is fitted to the frame.
- **Cave mouths:** only where a corridor path actually enters a cave (31 on seed 202, down from 97).
  Every other built-to-cave face is a caved-in passage: the tunnel wall with a rockfall in it.
- **Cave walls:** gaps are tested to the full height of the cavern, and a thin slab fills faces
  where no full panel fits (rays escaping a cave wall on seed 202: 94 → 26).
- **Variation:** each room favours one wall, floor and ceiling variant of its kit (3× weight), so
  rooms of the same type differ; kits gained the demo's unused timber-framed, thick and alcove
  walls, two more flat ceilings (turned at random) and two floor tiles; new props (coffins, altar,
  throne, standards, skull shield, rugs, lab tanks, torture machinery, fireplace); six more cave
  vignettes (glowing orbs, twisted grove, goblin watchtower, collapsed scaffold, crypt, rock shelf)
  at double the density, and denser cave floor scatter.

**Fixes, round four (30 Sep 2026):**

- `Wall_Wood_Beams_01` and `Wall_Alcove_Small_01` are *not* walls: the first is a bare timber frame
  (see- and walk-through), the second is 1.9 m deep. Both are out of every kit, as is the 1.3 m
  `Wall_02`. The timber frame could come back as an overlay on a real wall.
- Walls are registered per boundary, so no boundary is walled twice (the flicker); free wall ends
  are capped with `Wall_End_Coner_Outer_01`.
- Built-to-cave faces with no corridor through them are a plain double-sided wall with a rockfall
  heaped on the cave side. The thick tunnel piece doubled up had bulged into corridors.
- Caves get the old contoured mesh again, pushed 0.45 m into the rock behind the kit, as a sealed
  backing. Rays escaping a cave on seed 202: 13 of 3,691, walls and roof together.
- Everything placed in groups is graded **big, medium, small**: cave rock clusters, room rubble,
  vignette scatter, and prop heaps marked `gradeSizes`. A heavily weighted "rock columns" vignette
  breaks up open cavern floor.

**Direction (30 Sep 2026): the world is a generated version of the demo.** Every room, cave,
water feature and set piece in the demo should have a generated counterpart. Generated, not stamped:
assets in different places each run, but the demo's feel. New combinations come later, once it
feels like the demo.

**Caves rebuilt after the demo's two caves** (second pass, after play-test feedback: floors
clipping into hallways, repetitive generated terrain, floating and see-through pieces, bridges not
connecting). All cave terrain is now pack assets; the generated rock mesh is off.

- **Ground, as the demo lays it:** a few huge `CrackedRock_01`/`_03` plates per cave (up to
  ~30 m, any yaw), largest first, each as big as fits without reaching into built space. Under
  them a hidden base of `Rock_Flat` cut to each cell, so there is never a hole. Low `GroundMound`s
  on top; rough and edge mounds banked against the foot of walls.
- **Chasm** (the Goblin Cave), in the demo's three layers: a ledge at walking height by every
  way in; the rest of the floor dropped one storey to a cracked-rock pit floor; islands of
  `Rock_Flat_Platform` pillars standing on the pit floor with tops level with the ledge. Bridges
  (rope or mine-track trestle) join every ledge and island, laid between where the ground actually
  ends on each side. Torch sticks on islands, boulder piles on the pit floor, dark fog in the pit.
  Pit walls stop under the ledge.
- **Wall edges, as the demo dresses them:** graded clusters at the foot of every cave wall, by
  theme. Lush: giant mushrooms, crystals, stalagmites, grass, with glowing orbs. Goblin: fences,
  spikes, bone heaps, with torch sticks. Plain: rock piles and stalagmites.
- **Columns:** rock masses stood in the open middle of large floored caves, so they read as
  passages round rock rather than an empty hall.
- **Settling:** anything in a cave that neither stands on nor hangs from something is removed.
- **Fixes:** the old dungeon was still in physics while the new one generated (props landed on
  floors that then vanished); floor plates were in the rock scatter list (12 m slabs stacked as
  "rocks"); a legacy cladding pass hung single-sided roof slabs mid-air.

**Comparison tool:** `Tools > Dungeon > Compare With Demo` renders each demo area and its
generated counterpart from matching viewpoints into `Comparisons/<seed>/` (gitignored). Rows: the
demo, ours as played, ours fully lit. Each generation also logs a cave audit (holes in cave
ground, cave ground above corridor/room floors) and chasm stats (islands, bridges, unsupported
bridge points).

**Dungeon checks** (logged after every generation in the editor; `Tools > Dungeon > Audit Current
Dungeon` runs all of them, including the slow walk, on whatever is generated, in edit or play mode).
They measure the generated geometry only: nothing invisible is used to stop players or sight-lines,
and only drawn geometry counts as closing a hole.

| Check | What it does | A non-zero result means |
|---|---|---|
| **Seal** | From crouched and standing eye height (0.95 m, 1.62 m) at 5 points over every floor cell, sight-lines go out level all round, up 30° and 60°, down 30°, and straight up and down. | A sight-line met nothing drawn: a hole in a wall, floor or roof a player can see the void through. Reports the cell and direction. |
| **Escape** | A player capsule (Player.prefab's: 0.35 m radius, crouched 1.05 m, 0.45 m step, ~1 m jump, 60° slopes) starts on every floor cell and walks a 1 m grid everywhere it can. | "Out in rock": it got behind the cave rock into solid rock space. "Drop into the void": it stepped off an edge with no floor anywhere below. Also reports coverage, so a stuck walk never reads as a pass. |
| **Overlap** | Middle third of every solid prop against masonry walls and other room props; same prefab, position and turn twice; cave rock against room and corridor cells. | Clipping, a doubled piece (flicker), or cave rock in built space. Cave rock against cave rock, wall-mounted pieces and the kits' timber framing are excluded on purpose. |
| **Cave ground** | Every metre of cave floor away from ledge edges and river banks; room and corridor floor beside caves. | A mid-floor hole you look into, or cave rock standing above a hallway floor. |

The generator also repairs from the player's view: after everything is built, it fires the seal
check's sight-lines from every cave floor cell and puts pack rock wherever one leaves the cave (roof
piece, wall piece or floor plate).

Results, seeds 7 / 101 / 202 (1 Oct 2026): seal 63 / 6 / 23 sight-lines of ~1.2 M; escape 32 / 36 / 37
spots behind rock and 8 / 0 / 4 drops, with 94–96 % of floor walked; overlap 0 duplicates, 15–35 props
clipping; cave ground 2–12 mid-floor holes of ~20–30 k samples, 0 cave rock above hallway floors.
Generation now takes 13–21 s in the editor (the repair and gap passes are ray-heavy).

**Demo coverage still to build:** the crystal cave's pool and light fog; the sewer (water channel,
barrel vault); the entrance (where outside becomes inside); the junction hall's balconies and
railings; the ritual site as an in-room landmark; mine tunnels with blocked ends.

**Decided (30 Sep 2026):**

- **Approach: smarter kit rules**, not hand-built room prefabs. Each shell gets its own kit
  (dominant wall, overlay layer, corner caps, trims, ceiling, poles/pillars, props, lighting).
- **Lighting: lit near the entry, darker deeper.** Dense warm fixtures in the entry and hub rooms,
  thinning with depth until the far rooms are torch-only.
- **Milestone: one room type that looks like the demo**, then expand to more types. First target is
  the Cellar (timbered basement shell).

### 6.6 Mining / destructible rock (direction)

Chosen direction: voxel rock everywhere, Deep Rock Galactic style. The density field already exists
per generation; it is currently discarded. Not started.

---

## 7. Multiplayer architecture

### Network stack (as built)

| | |
|---|---|
| Topology | Client-hosted. Host is server and player. |
| Library | Netcode for GameObjects 2.1.1 |
| Transport | FacepunchTransport — Steam P2P, no dedicated servers |
| Matchmaking | Steam lobbies · invite, friends list, or SteamID64 |
| Party size | 4 (`StartHost(4)`) |
| Transforms | `ClientNetworkTransform` — client-authoritative |
| Animation | `OwnerNetworkAnimator` — client-authoritative |
| Character | Kinematic Character Controller, owner-gated input |
| Identity | `DelverIdentity` — name, appearance seed, ready flag; registry works for late joiners |

### The authority position

Movement and animation are client-authoritative. That is the correct trade for a four-player
friends-only game over Steam P2P — it makes movement feel right and costs nothing that matters,
because a cheater in a private lobby is only ruining their own evening. It is **not** a position that
survives public matchmaking, and should be revisited the moment strangers can join a lobby.

Must stay server-authoritative regardless: ability score rolls, the dungeon seed, loot spawn tables,
quota accounting, and the extraction timer.

### Known gap: item pickup is local-only

Picking an item up instantiates a copy in the local player's hand and destroys the world object on
that client only. Every other player still sees the item on the floor and can take it. This needs a
server-authoritative pickup RPC before any multiplayer playtest is meaningful.

---

## 8. Proximity voice

Not built yet. It is also the single feature the design leans on hardest, so the specification comes
before the implementation choice.

### Three voice modes

Push-to-talk selects a mode; the mode sets the falloff curve and how much attention it costs you.
Shouting reaches the whole wing and tells the dungeon exactly where you are.

| Mode | Full volume to | Silent past |
|---|---|---|
| Whisper | 0.5 m | 4 m |
| Talk | 1.5 m | 14 m |
| Shout | 3 m | 30 m |

Starting curves for tuning. Each mode holds full volume out to its plateau then falls to **true
silence** at its cutoff rather than trailing off — so "out of range" is a place you can stand, not a
gradient.

### Occlusion and the dungeon

Straight distance is not enough in a dungeon made of walls. Voice should be **filtered**, not just
attenuated, when the path is blocked: a low-pass through stone at roughly a third volume, full
clarity through an open doorway. Because hallways are carved as grid cells, the generator already
knows the topology — voice occlusion can query the same grid rather than raycasting per frame.

### Implementation options

| Option | Cost | Fit | Read |
|---|---|---|---|
| **Steam Voice via Facepunch** | free | Already in the stack | **Start here.** `SteamUser.VoiceRecord` plus a Netcode RPC into a positioned `AudioSource`. No new dependency. Manual work: opus framing, jitter buffering, occlusion filtering. |
| Dissonance | ~$70 | NGO integration ships | Solves positional audio, jitter and codecs. The pragmatic buy if hand-rolled Steam voice stalls for more than a week. |
| Vivox | metered | Unity first-party | Adds a UGS dependency and a per-user bill to a game whose networking is free P2P. Wrong shape. |

---

## 9. Light & fear

The dungeon is unlit — no sun, no skybox, no ambient fill. Every light source in a run is carried by
a player or fixed in the dungeon, which makes light a hand-slot cost like everything else (§5) and
ties the horror directly to the inventory pressure.

- **Torches burn down.** A run is longer than a torch. Somebody will be in the dark before
  extraction, and it will not be the person who planned for it. The last fifth of the fuel gutters
  visibly before it goes out.
- **Light is a beacon.** Whatever the turn (§3) releases navigates toward light and sound. Being
  visible and being findable are the same property.
- **Dropped torches keep burning.** A torch on the floor is a breadcrumb, a decoy, and a way to give
  up a hand slot without giving up the light.
- **Carried light is short-ranged.** The party should only see a room at a time, and the dark past
  the torch edge should be a real place you can walk into.
- **Fixture lights** (braziers, sconces, lanterns) are added by the generator, warm for inhabited
  landmarks, cooler for natural ones.
- **Lit near the entry, darker deeper.** Fixtures are dense in the entry and hub rooms (roughly the
  demo's one warm light per 50–100 m²) and thin out with distance from the entry, until the
  deepest rooms are torch-only. The first minutes look like the demo; the turn happens in the dark.

Real-time shadows on carried lights only. (The project currently renders with the Built-in pipeline; see the header table.) The performance ceiling is four dynamic
shadow-casting point lights, which is exactly the party size — a constraint worth designing into
rather than around.

---

## 10. Build status

As of 30 Sep 2026.

| System | State | Notes |
|---|---|---|
| Dungeon generation | Working | Rooms, Delaunay, MST, A* hallways, stairwells, reachability pruning, seeded. |
| Room archetypes | Rough | Weighted archetypes with size bands in `DungeonProfile`. Connection rules (§6.4) not keyed off type yet. |
| Room kits | Working | Eight room types with kits (§6.5). Entrance, junction hall and sewer not yet. |
| Cavern vignettes | Working | Themed, lit points of interest spread through every cavern. |
| Rendering performance | Working | Far plane tied to fog, distant fixture lights culled. ~4.6× cheaper per frame. |
| Doors | Working | Proximity doors, synchronised by construction. |
| Caves | Working | CA chambers, worm tunnels, rivers; rock assembled from the POLYGON cave kit. |
| Dressing & composition | Rough | Landmarks with accent light, props, formations. Reservations are a hard mask (see Appendix A). |
| Seed replication | Working | `DungeonRunner` handshake: host seed → local generate → report → release spawn. |
| Steam lobbies / muster | Working | Lobby screen with party list, rolls, ready flags; host starts the descent. |
| Character movement | Working | KCC, walk/sprint/crouch, stamina, carry slowdown. Camera currently first-person. |
| Ability scores | Rough | Roll and replicate; visible on the Tab sheet. Nothing consumes them. |
| Item pickup | Local only | No RPC — other clients still see the item on the floor. |
| Torches & light | Rough | Dungeon is unlit; carried torch with flicker; burn-down built but disabled (`burnSeconds = 0`). |
| Proximity voice | Not started | §8. Highest-value unbuilt system. |
| Enemies | Not started | `Scripts/Enemies/` is empty. |
| Physics items & ragdolls | Not started | §5. Blocked on the KCC decision. |
| Mining / destructible rock | Not started | §6.6. |
| Quota & extraction | Not started | Phases 4–6 of §3 have no code. |

### Dead code worth deleting

These compile but sit on no prefab in the shipping scene — leftovers from the Rigidbody movement
prototype that KCC replaced: `PlayerMovement`, `CameraMove`, `CameraRotate`, `StairClimb`,
`PlayerController`, `ClientConnector`, `DisableForOwner`.

---

## 11. Roadmap

Ordered by what unblocks the most. Each milestone ends in something playable with three friends,
because a co-op game that can't be played with friends can't be designed.

| Milestone | Ships | Playtest question |
|---|---|---|
| **M1 · Shared world** | ~~Seed handshake~~ (done), server-authoritative pickup RPC, networked item spawns | Do four people see the same dungeon and the same loot? |
| **M2 · Voice** | Steam voice over NGO into positional AudioSources; three modes; grid-based occlusion | Is splitting up scary and funny, or just annoying? |
| **M3 · The loop** | Quota, extraction timer, the turn, tally screen, torch burn-down on | Does anyone ever drop the loot to survive? |
| **M4 · Something in the dark** | First enemy — navigates to light and sound, not to players. Sources: POLYGON Dungeon's goblins, skeletons, ghosts, rock golem and tormented soul (static meshes; animate with Mixamo on their humanoid rigs), Polyperfect's animated orcs, skeletons, gargoyles and dragons, and POLYGON Fantasy Rivals. Stats and behaviour from the rulebook's monster chapter (p. 376 on). | Does the party go quiet on their own, without being told to? |

Ability score consumers (§4) slot into M3, once there is enough game for a bad Strength roll to
actually cost something.

---

## 12. Open questions

Decisions that need making before the systems above harden around a guess.

- **What do ability scores buy?** §4 proposes carry / speed / description detail. A proposal, not a
  decision, and everything in M3 depends on it.
- **First or third person?** Item data carries both first- and third-person hand transforms, and the
  player prefab has an Arms rig and a third-person camera. The camera is currently first-person.
  Proximity voice and torch-carrying both read better in first person.
- **KCC: which physics option?** §5 recommends option 2 (kinematic + ragdoll state). Confirm before
  item work.
- **One shared quota or four purses?** Shared quota makes the loop cooperative. Individual purses
  make it a comedy. They are different games.
- **Death: spectate, or stay in voice as something else?** Lethal Company's answer — dead players
  keep talking — fits pillar II exactly. Worth stealing deliberately rather than by default.
- **Public lobbies, ever?** Currently `lobby.SetPublic()` with client-authoritative movement. If
  public lobbies are the plan, §7's authority model has to change before anyone finds it.
- **Final title?**

---

## 13. Rules basis: Dungeon Crawl Classics

The game's rules come from the *Dungeon Crawl Classics RPG* (Goodman Games, 2012). The book is
`docs/dungeoncrawlclas0000good.pdf`; page numbers below are the book's printed ones. Where the two
disagree, **the rulebook wins unless this section says otherwise.**

### Kept

- **3d6 in order, six abilities** (p. 18), modifier table 1-1, and the birth augur (table 1-2, p. 19).
- **0-level characters** (p. 21): 1d4 HP + Stamina, one trained weapon and trade goods from the
  occupation table (table 1-3, pp. 22–23), a random piece of equipment (table 3-4).
- **The d20 core mechanic** (p. 16): roll d20 + modifiers ≥ DC or AC. Natural 1 always fails and
  usually fumbles; natural 20 always succeeds and usually crits.
- **The dice chain** (p. 17): d3–d4–d5–d6–d7–d8–d10–d12–d14–d16–d20–d24–d30. Penalties and bonuses
  step the die rather than adding numbers: an untrained weapon, a squeeze, being entangled are -1d.
- **Fumbles** (table 4-2, p. 80): fumble die by armour (none d4, light d8, medium d12, heavy d16),
  minus Luck. Most results are physical comedy: drop your weapon, trip, wound yourself, *swing at an
  ally*.
- **Crits** (p. 82 on), adjusted by Luck.
- **Luck** (p. 19): modifies crits, fumbles and the augur roll; any character can **burn** Luck
  permanently for a one-off bonus to a roll; Luck checks are d20 ≤ Luck.
- **Saving throws**: Fortitude, Reflex, Willpower (p. 20).
- **Alignment**: Law, Neutrality, Chaos (p. 24).
- **XP** (p. 26): 0–4 XP per encounter *survived*, not per kill; 10 XP to reach level 1.

### Changed

- **No funnel.** DCC has each player run three or four 0-level peasants and expects most to die.
  Here each player runs **one**. Death is still frequent; what happens to a dead player is an open
  question (§12).
- **Real time, not rounds.** There is no initiative or turn order (p. 77). Movement is the character
  controller; a "round" is roughly the time a swing takes.
- **Physics decides contact, dice decide the result.** A swing that physically connects rolls d20 +
  modifiers against the target's AC. A natural 1 rolls on the fumble table even if the swing
  connected; a natural 20 rolls a crit. Damage is the weapon die + Strength. Mass and momentum (§5)
  decide *whether* you hit; the dice decide *how well*. A fumble's "swing at an ally" is real: it
  redirects into the nearest ally.
- **The judge is the dungeon.** DC checks the book leaves to the judge (doors, traps, saves) come
  from the generator's rooms and traps, with fixed DCs.

### Why it fits

Pillar I (public dice) is DCC's fumble table read aloud; pillar III (uneven competence) is DCC's 3d6
in order; *"You're no hero"* (p. 15) is the pitch. Every roll stays public: the die, the result and
any fumble text are announced in the world.

### Licensing

DCC's game mechanics are published under the Open Game License 1.0a. Its **art, trade dress, the
name *Dungeon Crawl Classics* and its setting are not**. The game can follow the rules and take its
look from the art (§14). It must not use the book's illustrations, logo, typefaces or name. Check
the OGL notice in the book before shipping anything commercial.

---

## 14. Art direction

The rulebook carries two distinct art styles, and each has a job.

### In game: the painted colour plates

The colour pages (cover, endpapers, plates) are thick, saturated paintings that agree on a palette:

| Role | Colour | Seen as |
|---|---|---|
| Shadow and stone | Deep teal to cyan, never grey | Cave walls, the dark between torches, distant space |
| Fire and life | Hot orange and amber | Torches, lanterns, flesh in firelight |
| The otherworldly | Acid green, magenta and violet | Magic, portals, the eyes of things, cultists' glow |
| Blood | Saturated red against the teal | Wounds, a cloak, a banner |

The look is **complementary teal and orange, painted, high contrast**: every lit surface is warm and
every shadow is cold and coloured. That suits a dungeon lit only by fire (§9).

What that means for the build:

- **Shadows go teal.** Ambient and fog move from today's blue-grey to a dark teal; a grading LUT
  pushes shadows toward cyan and highlights toward orange.
- **Fire stays orange** (the demo's (1, 0.69, 0.14) already matches). **Magic gets green and
  magenta** lights, so anything otherworldly reads by colour alone.
- **A painted finish.** A post-process pass to break up Synty's flat shading: a painterly filter
  (Kuwahara or similar), a faint paper or canvas texture, darkened edges, a heavy vignette.
- **Saturation up, not brightness.** The plates are dark overall but intensely coloured where lit.

### Menus and UI: the black-and-white pages

The interior pages are pen and ink: dense crosshatching, heavy black fills, white paper.

- **Black ink on off-white paper.** No gradients, no glow. Panels are ruled boxes with a double rule
  (as table 1-3), or torn parchment banners and scrolls held up by hands, poles or monsters (the
  contents and credits pages).
- **Hand-lettered display type.** Chapter titles in a heavy, slightly irregular serif; body in a
  plain book serif; illuminated drop caps. Use licensed or open fonts in that spirit, not the book's.
- **The character sheet is a sheet of paper.** The Tab HUD and the lobby should read as a filled-in
  DCC-style sheet: ruled boxes, dice results in pen, the birth augur and occupation as a line of text.
- **Crosshatched illustrations.** Loading and tally screens can carry ink illustrations of the
  party's deaths, in the book's comic register (the fumble-table cartoons).

**Code is behind this too.** `UIKit` builds dark panels with a flat palette; it needs a paper-and-ink
theme.

---

## Appendix A. POLYGON demo audit & cave generation spec

### Evidence and scope

Reference scene: `Assets/Imports/PolygonDungeon/Scenes/Demo.unity`. A full inventory (prefab source
paths, parent groups, world transforms, rendered bounds, lights) was extracted through the Unity
Editor into **`docs/polygon-demo-inventory.json`** — 2 MB of raw data, kept as a separate file
rather than pasted here. Counts refer to outermost prefab instances.

The scene contains 3,462 prefab instances, 65 cave modules, 59 stalagmites, 33 stalactites, 92
realtime point lights and one directional light. Major groups: Long Hall (704), Outside (614), Center
Hall (456), Crystal Caves (446), Goblin Cave (366), Torture Room (183), Cells (131), Barracks (114),
Entrance (105), Laboratory (93), Sewer (85), Cellar (77), Furnace (50), Ritual. These are authored
compositions, not evidence of a particular procedural algorithm.

Minecraft and Deep Rock Galactic are references for exploration scale, branching routes, height and
reveal moments. The generator remains a finite, seeded, cell-based dungeon.

### What makes the demo work

| Factor | Observed | Procedural rule |
|---|---|---|
| Large enclosing forms | Cave background pieces ~20 m across alongside smaller pieces. | Reserve cavern space before masonry. Use real cave assets as cladding over a continuous collision shell. |
| Distinct environments | Crystal Caves: grass, flowers, spikes. Goblin Cave: platforms, mushrooms, fences, fire, mine track. | One coherent dressing palette per cavern. |
| Compression and reveal | Narrow entrances into wide, tall spaces. | Connect large chambers with winding passages that vary in width. Don't trim the big room into leftovers. |
| Visual hierarchy | Thrones, statues, obelisks, a pentagram. | Reserve a focal area per major space; place landmark, directed light and negative space before small props. |
| Foreground / middle / background | Flat rocks on the ground, formations break silhouettes, background modules enclose. | Separate floor dressing, perimeter cladding, roof dressing and focal placement. |
| Formation families | Floor and roof formations in several sizes. | Clusters of a dominant formation plus companions. Probe the actual roof. |
| Readable navigation | Doorways, tunnel entrances, stairs, bridges, tracks, railings. | Keep openings continuous; portal collars at masonry/cave joins; never let decorative meshes be the only backing of a boundary. |
| Structural repetition | Masonry repeats floors, pillars, trims, beams, ceilings. | Built rooms keep a coherent kit; landmark rooms are just larger. |
| Water as a channel | `FX_Water_Sewer`, 5 m planes, sunken. | Keep the sunken bed and bank walls. |
| Lighting hierarchy | Mostly realtime local lights. | Warm inhabited landmarks, cool natural ones, subdued surroundings, reliable carried light. |
| Scale / orientation variation | Cave pieces at varied rotations and scales. | Fit large modules by transformed bounds and available clearance. |
| Deliberate absence of clutter | Rooms have distinct uses. | Keep lanes and sightlines clear; sparse local groups, not uniform noise. |
| Occlusion and backing | Overlapping cave, roof and background pieces. | Overlap at transitions, both-sided backing. Never fix a leak by making it dark. |

### Constraints and acceptance checks

- All carving is seeded, bounded, grid-safe and connected by the reachability pass.
- Caves keep large connected floor areas after stair trimming and pruning.
- A cave and a room on different storeys stay separated. Portal centre rays pass; floor, side and
  roof rays hit structure.
- Cave art, focal objects and formations never obstruct protected entrance/approach cells.
  Stalactites need an actual roof hit and minimum headroom.
- Water has a bed and banks; doors keep frame-specific sizing; the player's torch lights nearby cave
  surfaces.
- Validate with Unity-generated layouts and renders, not just compilation.

### Cave kit: building caves from the pack, not from a mesh

`Generator3DCaveKit.cs`, gated on `DungeonProfile.caveUseAssetKit`. No cave geometry is generated.

How the demo assembles rock (measured):

- `Cave_Background_01/02` and corners are 20–47 m across at arbitrary Y rotations — a ring of large
  masses that overlap heavily, not tiles.
- `Cave_Roof_01` is 23.2 × 3.7 × 20 m, always rotation 0, tiled flat.
- `Cave_Large_01` (11.6 × 13.6 × 2.5 m) and `Cave_Curved_01` (10.7 × 17 × 8.1 m) are the wall pieces.
- `Cave_01/02` are small loose detail at wild rotations.

**Overlap is the technique.** Two intersecting rock masses are invisible from inside; two butted
edge-to-edge leave a seam you can see through. Every piece is larger than the space it covers.

| Stage | Piece | Rule |
|---|---|---|
| Ground | `Rock_Flat_01` | One plate per cave cell, lapped 1.6 m past each edge, top on the floor line. 0–4.5 cm stagger stops z-fighting. |
| Walls | `Cave_Large_01`, `Cave_Curved_01`, `Cave_01/02` | Straight boundary stretches merged into runs; panels every 7 m so they overlap. Depth clamped to the rock behind; cave-facing face aligned to the boundary. |
| Roof | `Cave_Roof_01` | 15 m grid at 1.8× oversize, extended one block past the cave. Height sampled over everything a slab covers. |
| Backdrop | `Cave_Background_01/02`, `Corner_01` | Ringed outside the walls at random yaw, collision stripped. |

**No kit piece may occupy a cell the built dungeon owns.** `IntrudesOnBuiltSpace` tests a piece's
world bounds against the grid (not colliders); anything that intrudes is destroyed and its
neighbours cover the gap.

### Measured results

Seeds 101 / 202 / 303.

First pass (marching-cubes shell, 48 × 4 × 48 grid):

| Metric | Before | After |
|---|---|---|
| Largest cavern floor (cells) | 49 / 56 / 56 | 148 / 169 / 123 |
| Total cave floor (cells) | 134 / 138 / 209 | 558 / 641 / 488 |
| Natural cave tunnels | 3 / 2 / 4 | 7 / 6 / 3 |
| Landmarks with accent lighting | 1 / 2 / 5 | 5 / 6 / 7 |
| Transition collars | 22 / 15 / 13 | 49 / 50 / 39 |

Largest chamber ≈ 3,700 m² of floor under 15 m of rock. Across 140 cave/masonry seams: 0 with no
floor beneath, 0 flank leaks, every cave mouth passable.

Cave kit pass (64 × 4 × 64 grid, rooms 5–14 cells square, up to 3 storeys):

| Piece | Placed | Intruding into built space |
|---|---|---|
| Cave wall | 155 / 227 / 177 | 0 / 0 / 0 |
| Cave roof | 152 / 214 / 215 | 0 / 0 / 0 |
| Cave backdrop | 2 / 10 / 7 | 0 / 0 / 0 |
| Floor plate | 632 / 820 / 683 | 126 / 41 / 55 lapping 0.5 m onto adjacent floor, by design |

### Known remaining faults

- Occasional slivers of sky where a roof slab's edge meets a wall run below it.
- Floor plate seams visible as faint lines; plates overlap but don't interlock.
- Wall panels that would intrude are discarded rather than retried smaller, so boundaries beside
  rooms are walled more sparsely.
- Reservation set is a hard mask rather than a falloff — cells around mouths and landmarks are bare
  rather than sparsely dressed.
- Theme palettes alternate strictly (lush, inhabited, lush…) instead of being chosen per cavern.

### Needs gameplay systems, not generation

Prison logic, rituals, mine carts, destructible terrain and multi-level bridges. The demo's
set-piece arrangements can supply theme palettes, but copying them as random props won't reproduce
their meaning.

---

## Appendix B. Asset Store packages (not in git)

`Assets/Imports/` holds ~1.3 GB of Asset Store content that is deliberately gitignored. After a fresh
clone, re-import these from **Window → Package Manager → My Assets** before opening
`Assets/Created/Scenes/SampleScene.unity`, or prefabs will show missing meshes and scripts. Import
each into `Assets/Imports/<name>/` so existing GUID references resolve.

| Package | Folder | Size | Used for |
|---|---|---|---|
| Epic Toon FX | `Epic Toon FX/` | 553 MB | Spell and impact VFX |
| Low Poly Animated Fantasy Creatures (Polyperfect) | `polyperfect/` | 295 MB | Monsters |
| POLYGON Fantasy Hero Characters (Synty) | `PolygonFantasyHeroCharacters/` | 277 MB | Player characters |
| Starter Assets: Third Person (Unity) | `StarterAssets/` | 91 MB | Locomotion animation set |
| POLYGON Dungeon (Synty) | `PolygonDungeon/` | 89 MB | Dungeon kit meshes |
| POLYGON Starter Pack (Synty) | `PolygonStarter/` | 8 MB | Prototype props |
| POLYGON Fantasy Rivals (Synty) — *planned* | `PolygonFantasyRivals/` | — | Enemies. https://syntystore.com/products/polygon-fantasy-rivals-pack |

### Kept in git

- `Assets/Imports/KinematicCharacterController/` — the KCC scripts carry local Netcode modifications
  (`ExamplePlayer` is a `NetworkBehaviour` with an `IsOwner` gate). **Do not overwrite by
  re-importing KCC.**
- `Assets/Imports/TextMesh Pro/` — small, and holds the TMP settings asset the UI references by GUID.

---

## Appendix C. Fix log

Bugs found and fixed while writing draft 1 (4 Sep 2026).

| Bug | File | Cause → fix |
|---|---|---|
| Ability scores were never rolled — every stat was 0 | `PlayerStats.cs` | `OnNetworkSpawn()` on a plain MonoBehaviour is never called. Now a `NetworkBehaviour` with a real override, replicated via `NetworkVariable`s. |
| Every die rolled one short | `PlayerStats.cs` | `Random.Range(1, 6)` is max-exclusive (a d5). Replaced with `Roll(count, sides)` using `Range(1, sides + 1)`. |
| Every dungeon was the same dungeon | `Generator3D.cs` | Hard-coded `new Random(0)`. Added serialized seed, randomise toggle, `Generate(int seed)`; `placedHallways` cleared per generation. |
| Joining a friend over Steam could never work | `SteamClientConnector.cs` | Join used the local player's own SteamId. Now parses the input as SteamID64 with clear errors. |
| Accepting a Steam invite connected twice | `GameNetworkManager.cs` | Invite handler and `OnLobbyEntered` both called `StartClient`. Invite now only joins the lobby; `StartClient` is idempotent. |
| Every client ran every player's pickup raycast | `PlayerPickupDetector.cs` | Dead `OnNetworkSpawn()` meant no owner gate; `GetKey` in `FixedUpdate`. Now owner-resolved at runtime, `GetKeyDown` in `Update`, layer-masked raycast. |
| `.gitignore` was UTF-16 | `.gitignore` | Git couldn't read any rule. Rewritten as UTF-8 with standard Unity rules plus `.gitattributes` for Unity YAML merge. |
| Only the scripts folder was under version control | repo layout | Repo promoted to project root with history intact. |
| Project wouldn't open on 6.3 | `Packages/manifest.json` | Removed obsolete `light-transport` pin; URP bumped 17.0.3 → 17.3.0. |
