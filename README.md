# Monster House

A first-person co-op wave-survival roguelite for 1–6 players. It has been a thousand years since a
hero raided the dungeon, so the Dungeon Master is filming a reality show to lure adventurers back.
It works. You play the Big Six — the dungeon's monsters, who never asked for any of this — fighting
off wave after wave of adventurers, looting their bodies, and picking up anything in the dungeon to
hit them with.

Design: [docs/gdd.md](docs/gdd.md).

**Engine:** Godot 4.7 (GDScript, Jolt physics) · **Networking:** GodotSteam (`SteamMultiplayerPeer`), ENet for LAN

The original Unity project is preserved on the [`unity`](../../tree/unity) branch.

## Getting started

1. Install [Godot 4.7](https://godotengine.org/download) (standard build, not .NET).
2. From the repo root, fetch third-party content:

   ```powershell
   powershell -ExecutionPolicy Bypass -File tools/setup.ps1
   ```

   This downloads the Quaternius packs and GodotSteam into gitignored folders. The Quaternius
   licence allows use in games but not redistributing the assets, so they are never committed.
3. Open the project in Godot. The first import takes a few minutes.

The Bestiary monsters beyond the Imp and Puglin are in the paid Source tier of
[Bestiary — Dungeon Monsters Kit](https://quaternius.itch.io/bestiary-dungeon-monsters-kit). Put the
purchased zip in `.cache/downloads/bestiary-dungeon-monsters-kit/` and re-run the setup script.

## Layout

```
addons/godotsteam/   GodotSteam GDExtension (fetched)
assets/quaternius/   Quaternius packs (fetched)
docs/                Design
scenes/              Scenes
scripts/core/        Autoloads: Game (run state), Ratings (the show's audience)
tools/               setup.ps1
```
