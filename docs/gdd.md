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

**Monster House is a first-person co-op wave-survival roguelite for 1–6 players.** You play the
monsters defending your home against escalating waves of adventurers, looting their bodies, stacking
items Risk of Rain 2–style, and deciding how long you dare keep the cameras rolling.

### The one-line genre

*Risk of Rain 2's run structure, in first person, with Overwatch's hero roster, R.E.P.O.'s
grab-anything physics and Darkest Dungeon's narrator — run in reverse Clank!.*

### Reference games and what we take

| From | We take |
|---|---|
| **Risk of Rain 2** | The run: stages, a difficulty clock that only climbs, gold → chests → stacking items, a "charge the objective while the horde comes" climax per stage, loops. |
| **Clank! (inverted)** | In Clank, noise wakes the dragon. Here, **ratings** — spectacle — bring *more adventurers*. Showing off makes the run harder and richer. |
| **Overwatch / TF2** | A fixed cast with readable silhouettes, one-per-team hero lock, and in-character voice lines that play off each other. |
| **R.E.P.O. / Lethal Company** | Physical hands: grab, carry and throw anything in the dungeon. Proximity voice. |
| **Darkest Dungeon** | The look (ink and shadow, warm torchlight on black) and a narrator who comments on everything. |

---

## 2. Pillars

1. **Everything is a weapon.** Any object in the dungeon can be picked up and swung or thrown: a
   lamp, a rock, a chair, a dead adventurer. Damage comes from mass and speed.
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
| 2 | **Episode** (stage) | A generated wing of the dungeon. Adventurers pour in from the surface stairs in waves. Kill them, loot them, open the dungeon's dusty old chests, find the **Spotlight**. |
| 3 | **The Main Event** | Activate the Spotlight. The DM goes live; a timed horde plus a **named Hero party** (the stage boss) descends. Survive until the broadcast ends. |
| 4 | **Encore?** | Bank the season's gold and end the run, **or** renew for another episode: deeper wing, harder clock, bigger multiplier. |
| 5 | **The Recap** | Highlight reel, confessional clips, MVP vote, and what the gold buys (cosmetics and new content for the pool — never stats). |

### The difficulty clock

Like Risk of Rain 2, difficulty rises with **time** — but here it also rises with **ratings**. The HUD
shows it as the DM's audience bar: *Local Interest → Regional Buzz → Kingdom-wide → Legendary*.

### Ratings — Clank, inverted

Ratings rise for spectacle: multi-kills, environmental kills, throw kills, killing with absurd objects,
juggling an adventurer, surviving at 1 HP, a teammate throwing you into a crowd.

- Ratings **multiply gold** dropped by adventurers.
- Ratings **raise the difficulty clock** — more and tougher adventurers, elite parties sooner.
- At ratings thresholds the DM **rolls a public d20 twist**: *"Commercial break! The floor is lava."*
  *"Plot twist: the Bard is buffing everyone."* *"Special guest: a paladin."* Low ratings bring twists
  too — *"Ratings are dying. Let's spice it up."*

---

## 4. Moment to moment

First person, with your monster's hands and weapon in view.

| Input | Action |
|---|---|
| WASD · Space · Shift · Ctrl | Move, jump, sprint, crouch |
| LMB | **Primary** — your monster's attack. With a grabbed object: swing it. |
| RMB | **Secondary** ability |
| Shift (tap) / Ctrl | **Utility** (each class differs: blink, pounce, roll…) |
| R | **Special** — the big cooldown |
| F | **Grab** whatever you're looking at / drop it. Hold to carry heavy things. |
| Q | **Throw** what you're holding. Hold to charge. |
| Tab | Scoreboard / items |

Risk of Rain 2 gives every survivor four skills (primary, secondary, utility, special); we keep that,
plus the universal grab/throw.

**Downed, not dead.** At 0 HP a monster ragdolls and is *downed*. A teammate can pick up the body and
carry it to a **Makeup Chair** (revive point), or revive in place by holding F. If the whole cast is
down, the episode is cancelled — run over.

### Items

- **Gold** drops from adventurers (×ratings multiplier) and from smashing dungeon props.
- **Chests** — the dungeon's own treasure, untouched for 1000 years — cost gold. They give **relics**:
  passive stacking items, Risk of Rain 2 rules (common/uncommon/legendary, stacks scale linearly or
  hyperbolically).
- **Adventurer gear** — swords, shields, staves, potions, torches — drops as **physical objects**.
  Grab and use them, throw them, or carry them to the **Pawn Table** for gold.

### Combat

- Your monster's primary is reliable damage. Grabbed objects are the spectacle: damage =
  mass × impact speed, so a thrown barrel is great and a thrown candle is a joke.
- Adventurers ragdoll on death and stay as props — grab one and swing it at its friends.
- Hits have weight: hit-stop, knockback, camera shake, physical reactions.

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

Each episode is a generated **wing**: Crypt, Flooded Cellars, Forge, Overgrown Garden, the Great Hall,
and the Surface Village for the finale. Built from Quaternius modular kits on a 2 m × 3 m grid.

What a wing needs for this game (generation priorities):

1. **Arenas joined by chokepoints** — big rooms to fight hordes, corridors to funnel them.
2. **Loops** — you must be able to kite a horde in a circle (RoR2 survival depends on it).
3. **Verticality** — balconies, stairs, pits. Throwing adventurers off ledges is premium content.
4. **Spawn stairs** — adventurers enter from surface stairways at the map's edges, so the fight
   flows inward.
5. **Props everywhere** — every room stocked with things to grab and throw.
6. **A Spotlight stage** — one large, readable arena per wing for the Main Event.

Generation is seeded and deterministic: every client builds the same wing from the same seed.

---

## 8. Dialogue

In-character barks, Overwatch-style, plus the DM as narrator.

- **Trigger → conditions → line**, with cooldowns and priorities. Triggers: episode start, kills,
  multi-kills, a teammate downed, an absurd object grabbed, ratings thresholds, boss arrival.
- **Call and response**: a line can request a reply from a specific monster if they are in earshot.
  Rivalries and friendships get their own exchanges.
- **The DM** speaks as subtitled narration, Darkest Dungeon style.
- **Adventurers bark too** — awed, overconfident, and badly informed about the dungeon.
- Monster lines play positionally and **duck under player voice chat**.
- Until there is voice acting, monsters speak creature gibberish with subtitles.

Samples:

> **DM:** Ladies and gentlemonsters — after ONE THOUSAND YEARS —
> **Brock:** Nobody asked for this.
> **Pebble:** I packed lunches.
> **Vex:** Pebble, those are rocks.

> *Pebble picks up an adventurer*
> **Pebble:** I'm so sorry.
> *(throws him)*

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

Darkest Dungeon's palette on Quaternius' sculpted, hand-painted kits: near-black ambient, warm
torchlight, ink outlines (depth/normal edge detection), crushed blacks, vignette and film grain, as a
full-screen post-process shader. The DM's broadcast elements — the ratings bar, twist cards, the
on-air light — are the only clean, bright UI.

---

## 11. Engine and assets

- **Godot 4.7**, GDScript, Jolt physics.
- `tools/setup.ps1` downloads the Quaternius packs and GodotSteam into gitignored folders — the
  Quaternius licence allows use in games but not redistributing the assets.
- Same-style Quaternius packs in use: Bestiary (monsters), Universal Base Characters and Modular
  Character Outfits (adventurers), Universal Animation Library 1 & 2 (all animation), Medieval Village
  MegaKit (architecture), Fantasy Props MegaKit (props, weapons, loot), Stylized Nature MegaKit.

## 12. Open questions

1. How do relics read in first person — do you see them on your monster's hands?
2. Can players pick each other up while healthy (Golem Teammate Toss says yes — does everyone)?
3. Friendly fire: on for thrown objects only?
4. Does the Surface Village finale exist in a first release, or later?
5. Does the season have a final boss (the hero who raided the dungeon 1000 years ago)?
