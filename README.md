# 3dFungeonGame

A co-op, D&D-flavoured 3D dungeon crawler for Unity 6.3 — procedurally generated
dungeons, rolled ability scores, and four friends who mostly make things worse for
each other. Think a tabletop dungeon delve run at *Lethal Company* tension levels,
with proximity voice chat doing most of the comedy and most of the horror.

**Engine:** Unity `6000.3.23f1` (LTS) · URP 17.3.0
**Networking:** Netcode for GameObjects 2.1.1 over the Facepunch (Steam) transport

---

## Repository layout

```
Assets/
├── Created/            # Everything hand-authored for this game
│   ├── Animation/      # Retargeted character animation
│   ├── Prefabs/        # Player, dungeon tiles, doors, stairs, pickups
│   ├── Scenes/         # SampleScene — the playable scene
│   └── Scripts/
│       ├── BlueRaja/          # Priority queue library used by the pathfinder
│       ├── Dungeon Generator/ # Seeded 3D generator (see below)
│       ├── Network/           # Steam lobbies, transport, ownership helpers
│       ├── PickUpItems/       # Item scriptable objects and pickup detection
│       ├── PlayerMovement/    # Camera and locomotion helpers
│       ├── PlayerStats/       # Ability scores, HP, equipped items
│       └── RoomGeneration/    # BSP room splitting
├── Imports/            # Third-party packages — mostly gitignored, see docs/
Packages/               # UPM manifest
ProjectSettings/        # Unity project settings
docs/                   # Design and setup documentation
```

## Getting started

1. Install Unity **6000.3.23f1** via Unity Hub.
2. Re-import the Asset Store packages listed in
   [docs/ASSET-STORE-PACKAGES.md](docs/ASSET-STORE-PACKAGES.md) — they are not in
   this repo (~1.3 GB of binaries). The project will show missing meshes and
   missing scripts until you do.
3. Open `Assets/Created/Scenes/SampleScene.unity`.
4. Press **Play**. Use the Host / Join buttons; joining over Steam needs the
   host's SteamID64 pasted into the field.

Steam features require Steam running and a valid `steam_appid.txt` / app ID
configured for the Facepunch transport.

## Dungeon generation

`Generator3D` builds a level in four stages on a `Grid3D<CellType>`:

1. **Place rooms** — random non-intersecting `BoundsInt` volumes.
2. **Triangulate** — `Delaunay3D` tetrahedralisation over the room centres.
3. **Select edges** — `Prim` minimum spanning tree, plus a few reintroduced
   edges so the graph has loops rather than being a pure tree.
4. **Carve hallways** — `DungeonPathfinder3D`, an A* that prices stairs and
   hallway reuse so corridors merge instead of running in parallel.

Generation is seeded. Call `Generate(int seed)` to build a specific layout; the
`Seeding` fields on the component control what `Start()` uses. Every client must
build from the same seed — see the design doc for the planned host handshake.

## Documentation

- [docs/ASSET-STORE-PACKAGES.md](docs/ASSET-STORE-PACKAGES.md) — what to
  re-import after a fresh clone.

## License

Released for educational and non-commercial development purposes.
