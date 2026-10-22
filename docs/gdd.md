# Monster House — Game Design Document

*Working title. Draft 2 — 21 Oct 2026. Replaces the Unity-era "Party Wipe" GDD, which lives on the
`unity` branch.*

---

## 1. The pitch

> *It has been one thousand years since a hero came down to raid the dungeon.*

The traps have rusted. The treasure has gathered dust. And the Dungeon Master is bored out of his
mind. So he has started **filming a reality show** — broadcast to every magic mirror on the surface —
to convince adventurers that his dungeon is worth raiding again.

It works. They are coming. Wave after wave of them.

The problem is his cast: **the Big Six**, the dungeon's six resident monsters, who did not sign up for
this, do not care about the show, and would very much like to go back to their naps. They fight
anyway — the adventurers are stealing their stuff, and the adventurers drop loot.

**Monster House is a third-person co-op wave-survival roguelite for 1–6 players.** You play the
monsters defending your home against escalating waves of adventurers, looting their bodies, stacking
items Risk of Rain 2–style, and deciding how long you dare keep the cameras rolling.

### The one-line genre

*Risk of Rain 2 — third-person camera, fast movement, open stages, run structure — with Overwatch's
hero roster and an extremely online cast, run in reverse Clank!, across hand-built levels that each
started as a painting.* At heart it plays like Risk of Rain 2; the twist is the show.

### Reference games and what we take

| From | We take |
|---|---|
| **Risk of Rain 2** | The camera and the run: over-the-shoulder third person, fast movement, hand-made stages with variants, a difficulty clock that only climbs, gold → chests → stacking items, a "charge the objective while the horde comes" climax per stage, loops. |
| **Clank! (inverted)** | In Clank, noise wakes the dragon. Here, **ratings** — spectacle — bring *more adventurers*. Showing off makes the run harder and richer. |
| **Overwatch / TF2** | A fixed cast with readable silhouettes, one-per-team hero lock, and in-character voice lines that play off each other. |
| **Darkest Dungeon** | The look (ink and shadow, warm torchlight on black) and a narrator who comments on everything. |

---

## 2. Pillars

1. **It plays like Risk of Rain 2.** Fast third-person movement, four skills, stacking items, open
   stages, a difficulty clock. No inventory juggling, no picking things up: you fight and you loot chests.
2. **Showing off is the push-your-luck.** Style earns ratings; ratings multiply loot and summon bigger
   waves. Playing it safe pays badly. Playing it loud gets you killed.
3. **Six monsters, six players, no duplicates.** Each character is one person's job. Hero-locked, like
   Overwatch's original one-of-each rule.
4. **The cast hates the show.** The comedy is reluctant monsters, an over-excited DM, and adventurers
   who think this is the greatest dungeon ever.

---

## 3. The run

A run is a **season**. Each stage is an **episode**. Target length: 30–45 minutes for a full season.

| # | Phase | What happens |
|---|---|---|
| 1 | **The Green Room** (hub) | Lobby and drop-in point. Pick your monster (one each). The confessional booth, the scoreboard, the DM's desk. The host starts the season. |
| 2 | **Episode** (stage) | A hand-built wing of the dungeon (§7). Adventurers pour in from the surface stairs in waves. Kill them, loot them, open the dungeon's dusty old chests, find the **Spotlight**. |
| 3 | **The Main Event** | Activate the Spotlight. The DM goes live; a timed horde plus a **named Hero party** (the stage boss) descends. Survive until the broadcast ends. |
| 4 | **Encore?** | Bank the season's gold and end the run, **or** renew for another episode: deeper wing, harder clock, bigger multiplier. |
| 5 | **The Recap** | Highlight reel, confessional clips, MVP vote, and what the gold buys (cosmetics and new content for the pool — never stats). |

### The difficulty clock

Like Risk of Rain 2, difficulty rises with **time** — but here it also rises with **ratings**. The HUD
shows it as the DM's audience bar: *Local Interest → Regional Buzz → Kingdom-wide → Legendary*.

### Ratings — Clank, inverted

Ratings rise for spectacle: multi-kills, mid-air kills, burning kills, elite kills, juggling an
adventurer, surviving at 1 HP, a teammate tossing you into a crowd (Pebble's Teammate Toss).

- Ratings **multiply gold** dropped by adventurers.
- Ratings **raise the difficulty clock** — more and tougher adventurers, elite parties sooner.
- At ratings thresholds the DM **rolls a public d20 twist**: *"Commercial break! The floor is lava."*
  *"Plot twist: the Bard is buffing everyone."* *"Special guest: a paladin."* Low ratings bring twists
  too — *"Ratings are dying. Let's spice it up."*

---

## 4. Moment to moment

Third person, Risk of Rain 2 style: an over-the-shoulder camera with a centre crosshair, so you see
your monster — the class is the silhouette — and aim where the crosshair points.

| Input | Action |
|---|---|
| WASD · Space · Ctrl | Move, jump, sprint toggle |
| LMB | **Primary** — your monster's attack (hold to repeat). |
| RMB | **Secondary** ability |
| Shift | **Utility** — the class's movement skill (blink, pounce, grapple, roll…) |
| R | **Special** — the big cooldown |
| E | **Interact** — open a chest, use a shrine, revive a teammate. |
| Tab | Scoreboard / items |

Risk of Rain 2 gives every survivor four skills (primary, secondary, utility, special); we keep that.

### Movement

Movement is the skill floor and the fun. RoR2's baseline, with more on top, and every monster moves
differently:

- **Shared:** sprint (no stamina, cancels on attack like RoR2), jump, air control, a ledge mantle,
  slide out of a sprint, and momentum kept off ramps and stairs. Fall damage off: falling is free,
  so verticality is a toy, not a tax.
- **Movement items:** relics stack movement the RoR2 way — extra jumps, speed, a dash charge.
- **Per monster** (the Utility slot, plus a passive):

| Monster | Movement |
|---|---|
| Vex (Imp) | *Blink* short teleport; slow fall on wing-flaps |
| Biscuit (Puglin) | *Scurry*: runs up short walls, fits through gaps nobody else can |
| Nix (Goblin) | *Grapple*: hook to anchors, ledges and adventurers; swing |
| Old Rattles (Skeleton) | *Fall Apart*: collapse into a pile to dodge, reassemble where the skull lands |
| Brock (Werewolf) | *Pounce* leap; wall-jump; all-fours sprint is the fastest ground speed |
| Pebble (Golem) | *Charge* through enemies and breakable walls; ground-slam out of any jump |

Levels are built for this movement: high routes, gaps only some monsters can cross, grapple anchors,
walls to scurry.

**Downed, not dead.** At 0 HP a monster is *downed*. A teammate revives them by standing over them
and holding E, or they get back up at a **Makeup Chair** after the wave. If the whole cast is
down, the episode is cancelled — run over.

### Items

- **Gold** drops from adventurers (×ratings multiplier), shared by the whole cast as in Risk of Rain 2.
- **Chests** — the dungeon's own treasure, untouched for 1000 years — cost gold. They give **relics**:
  passive stacking items, Risk of Rain 2 rules (common/uncommon/legendary, stacks scale linearly or
  hyperbolically).

### Combat

- Each monster's kit is its whole toolset. Hits have weight: hit-stop, knockback that can launch
  adventurers into the air, camera shake, damage numbers. Crits double damage (base 1%).
- Adventurers collapse on death and sink away, Risk of Rain 2-style.

---

## 5. The Big Six

One player per monster. Placeholder names. Kits follow the RoR2 four-slot shape.

| Monster | Name | Archetype | Primary | Secondary | Utility | Special |
|---|---|---|---|---|---|---|
| **Imp** | Vex | The Villain — wants to be famous, pretends he doesn't | Spiked club combo | *Hellfire*: ignite what he's holding or touching | *Blink* | *Arson Special*: everything nearby catches fire |
| **Puglin** | Biscuit | The Underdog — born after the last hero, thinks heroes are a myth | Stick jabs, fast | *Sniff*: loot and adventurers glow through walls | *Scurry*: tiny, fast, fits through gaps | *Good Boy*: adventurers stop to coo; free hits for 6 s |
| **Goblin** | Nix | The Schemer — pitching the DM a spin-off | Shiv | *Tripwire*: place a trap | *Grapple* | *Contraption*: deploys a turret built from junk |
| **Skeleton** | Old Rattles | The Veteran — only one who remembers the last hero; tells it differently every time | Sword | *Throw Arm*: boomerangs | *Fall Apart*: collapse to dodge, reassemble | *Encore*: revives every downed teammate |
| **Werewolf** | Brock | The Hothead — "could've taken the last hero" | Claws | *Pounce* | *All Fours*: fast sprint, empty hands | *Full Moon*: huge, fast, unstoppable for 10 s |
| **Golem** | Pebble | The Gentle Giant — anxious, packs everyone rock lunches | Slam | *Teammate Toss*: throw an ally | *Wall Smash* | *Big Hug*: grab a hero and hold them while everyone wails on them |

Carry strength differs by monster: Pebble lifts anything (cows, statues, adventurers in armour);
Biscuit can barely lift a chair.

**Assets:** the Imp and Puglin are in the free tier of Quaternius' *Bestiary — Dungeon Monsters Kit*.
The Goblin, Skeleton, Werewolf and Golem need the paid Source tier ($20,
https://quaternius.itch.io/bestiary-dungeon-monsters-kit). Every Bestiary monster shares the Universal
Animation Library rig, so all ~120 library animations play on every monster.

---

## 6. Adventurers

The enemies. Built from Quaternius Universal Base Characters + Modular Outfits, armed with Fantasy
Props weapons. They talk: they think this is the best dungeon ever.

| Class | Behaviour |
|---|---|
| Peasant Adventurer | Fodder. Pitchfork, torch. Runs from fire. |
| Fighter | Melee, shield block from the front. |
| Rogue | Flanks, steals loot off the floor and runs for the exit with it. |
| Ranger | Ranged; keeps distance. |
| Wizard | Area spells; fragile. |
| Cleric | Heals and revives other adventurers — kill first. |
| Bard | Buffs the party; plays louder as ratings rise. |

**Elites** (RoR2 affixes, DM-flavoured): *Blessed* (holy aura), *Enchanted* (magic shield),
*Veteran* (bigger, armoured), *Influencer* (summons fans).

**Bosses** are **named Hero parties** that arrive at the Main Event: e.g. *Sir Gallant and the
Sunrise Company*. Each has a leader with a big readable move set and a support crew.

---

## 7. The dungeon (maps)

Each episode is a **hand-built, open stage**, like Risk of Rain 2's: roughly 200–400 m across, mostly
open ground with high routes and cover, not corridors. The "dungeon" is huge — sunken courtyards,
collapsed caverns open to the sky, flooded halls the size of a valley. Built in Godot from the
Quaternius kits, plus terrain and custom geometry where a painting asks for it. Adventurers spawn
around the players on the navmesh, RoR2-style, rather than from fixed doors.

### Levels from paintings

Every level starts from a painting supplied by the designer. The painting is the brief:

1. **Read the painting.** Focal point, light source(s) and their colour, palette, depth layers
   (foreground / midground / far), the shapes that read at a glance, the mood.
2. **Write a level brief** (`docs/levels/<name>.md`): which painting, what to keep, the play spaces it
   implies, where the camera's postcard view is, spawn stairs, the Spotlight arena.
3. **Block out** the level in Godot with simple shapes to test movement and fights.
4. **Dress** it with kit pieces and props until the postcard view matches the painting's composition.
5. **Light and grade** it from the painting: sun, fill, fog and ambient colours sampled from the
   painting itself, and the post-process grade tuned to its palette.
6. **The painting is the title card.** Each episode opens on the painting, then dissolves into the
   matching camera view of the level.

Each level ships with **variants** (RoR2's stage variants): the same map with different time of
day, prop layout, open/closed routes and spawn stairs, so it doesn't play the same twice.

What every level needs for this game:

1. **Arenas joined by chokepoints** — big rooms to fight hordes, corridors to funnel them.
2. **Loops** — you must be able to kite a horde in a circle (RoR2 survival depends on it).
3. **Verticality** — mesas, balconies, stairs, pits. Knocking adventurers off ledges is premium content.
4. **Spawn stairs** — adventurers enter from surface stairways at the map's edges, so the fight
   flows inward.
5. **Set dressing that makes sense** — props in sets (a camp, a dining table, a smithy, a hoard),
   composed big → medium → small, with empty ground between sets. Scenery, not loot.
6. **A Spotlight stage** — one large, readable arena per wing for the Main Event.
7. **Movement routes** — high paths, grapple anchors, scurry walls and gaps that reward each
   monster's movement.

Chest, prop and spawn placement within a level is seeded, so every client agrees from one integer.

---

## 8. Dialogue

In-character barks, Overwatch-style, plus the DM as narrator.

- **Trigger → conditions → line**, with cooldowns and priorities. Triggers: episode start, kills,
  multi-kills, a teammate downed, a relic opened, ratings thresholds, boss arrival, idle moments.
- **Call and response**: a line can request a reply from a specific monster if they are in earshot.
  Rivalries and friendships get their own exchanges.
- **The DM** speaks as subtitled narration, Darkest Dungeon style.
- **The cast is extremely online.** The monsters have scrying-stone phones on the *Crystal Net* and
  talk like it: "Clip that", "skill issue", "main character energy", "ratio'd by a barrel", "touch
  grass? I SET the grass on fire". They doomscroll between waves, check follower counts, and treat
  the DM's show as content for their own channels. It's why they don't care about his show: they
  have their own. Adventurers, by contrast, have never heard of the Crystal Net.
- **Adventurers bark too** — awed, overconfident, and badly informed about the dungeon.
- Monster lines play positionally and **duck under player voice chat**.
- Until there is voice acting, monsters speak creature gibberish with subtitles.

Samples:

> **DM:** Ladies and gentlemonsters — after ONE THOUSAND YEARS —
> **Brock:** Nobody asked for this.
> **Pebble:** I packed lunches.
> **Vex:** Pebble, those are rocks.

> *Pebble Big Hugs a hero*
> **Pebble:** I'm so sorry.
> *(does not let go)*

> *Old Rattles falls apart*
> **Old Rattles (skull):** Somebody grab the hip. No — the *other* hip.
> **Biscuit:** I'm not touching your hip.

> *Adventurer, entering*
> **Fighter:** It's real! Mom, it's *real!*

### The Green Room confessional

In the hub, step into the confessional booth and talk to the camera; your voice clip is recorded and
cut into the episode recap. The booth prompts you (*"Who's dead weight this season?"*). This is the
"Meet the Team" moment, made by players.

---

## 9. Multiplayer

- 1–6 players, drop-in / drop-out, one of each monster. Joining mid-episode takes a free monster;
  if all six are taken you spectate.
- **Steam** via GodotSteam (`SteamMultiplayerPeer`); lobbies are friends-only or invite. ENet for
  LAN and local testing.
- Host-authoritative: adventurer AI, damage, loot and props simulate on the host. Player movement
  is client-authoritative for feel.
- The host's save holds the party's unlocks and gold.
- Proximity voice chat.

## 10. Look

**Dark and Darker's palette** on Quaternius' sculpted, hand-painted kits. Near-black warm shadows,
stone and timber in between, and torchlight as the only real colour in a room: the fire and the
monsters are the only saturated things on screen. Cold moonlight appears only through grates and
shafts. Each level's own grade comes from its painting (§7), inside this range. Constants live in
`scripts/core/palette.gd`.

| Group | Colours |
|---|---|
| World | Void `#0b0a09` · Shadow `#15161a` · Stone dark `#2a2622` · Stone `#4a433b` · Stone light `#6e655a` · Timber `#3b2a1d` · Bone `#c9bba0` |
| Light | Torch `#ffb25e` · Torch falloff `#e07a2e` · Ember `#b8471c` · Moonlight `#3e5468` (rare, low) · Arcane `#6b8f86` |
| Accents and UI | Blood `#6e1a16` · Gold `#d8a83f` · On Air `#c2362b` (the DM's broadcast UI) · Parchment `#c9b79c` (text) |
| Relic rarity | Dark and Darker's ladder: Poor, Common, Uncommon, Rare, Epic, Legendary, Unique |

**Creature skins.** Each Bestiary monster ships in three colour variants. The cast wears the one that
sits in the palette; the others become elites and cosmetics.

| Monster | Main | Alternate (elite / cosmetic) | Not used |
|---|---|---|---|
| Imp (Vex) | 1, blood red `#813434` | 3, slate violet | 2, acid green: too cartoony |
| Puglin (Biscuit) | 1, moss olive `#515b00` | 3, deep teal | 2, red: clashes with the Imp |

Red and moss keep the two free monsters apart by hue under the same torchlight. The other four pick
once the Source tier is in.

**Lighting is flat and readable first** (Risk of Rain 2 has to be legible at a glance): strong sky
ambient, one soft key light, no crushed blacks. Fires are accents, not the only light.

**Post-process** (`shaders/sketch_post.gdshader`), after Dark and Darker's class sketches: ink outlines
from silhouettes and geometric creases (reconstructed from depth, so texture detail never gets inked),
light cross-hatching in the darkest areas only, shadows lifted toward dark parchment, a gentle
plum/torch split-tone, star glints on hot highlights, soft vignette and grain. Characters get a
character-only rim light from behind (`CameraRig.rim_light`) and glossier materials, so they read
against any background. The DM's broadcast elements (the ratings bar, twist cards, the
on-air light) are the only clean, bright UI.

## 11. Engine and assets

- **Godot 4.7**, GDScript, Jolt physics.
- `tools/setup.ps1` downloads the Quaternius packs and GodotSteam into gitignored folders — the
  Quaternius licence allows use in games but not redistributing the assets.
- Same-style Quaternius packs in use: Bestiary (monsters), Universal Base Characters and Modular
  Character Outfits (adventurers), Universal Animation Library 1 & 2 (all animation), Medieval Village
  MegaKit (architecture), Fantasy Props MegaKit (props, weapons, loot), Stylized Nature MegaKit.

## 12. Open questions

1. How do relics read on the monster in third person — worn on the model, like RoR2's item displays?
2. Can players pick each other up while healthy (Golem Teammate Toss says yes — does everyone)?
3. Friendly fire: off entirely, or on for a DM twist ("Plot twist: friendly fire is ON")?
4. Does the Surface Village finale exist in a first release, or later?
5. Does the season have a final boss (the hero who raided the dungeon 1000 years ago)?
