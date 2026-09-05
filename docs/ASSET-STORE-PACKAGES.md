# Asset Store packages (not in git)

`Assets/Imports/` holds ~1.3 GB of Asset Store content that is deliberately
gitignored. After a fresh clone, re-import these from **Window → Package Manager
→ My Assets** before opening `Assets/Created/Scenes/SampleScene.unity`, or
prefabs will show missing meshes and missing script references.

Import each one into `Assets/Imports/<name>/` so the existing GUID references resolve.

| Package | Folder | Size | Used for |
| --- | --- | --- | --- |
| Epic Toon FX | `Epic Toon FX/` | 553 MB | Spell and impact VFX |
| Low Poly Animated Fantasy Creatures (Polyperfect) | `polyperfect/` | 295 MB | Monsters |
| POLYGON Fantasy Hero Characters (Synty) | `PolygonFantasyHeroCharacters/` | 277 MB | Player characters |
| Starter Assets: Third Person (Unity) | `StarterAssets/` | 91 MB | Locomotion animation set |
| POLYGON Dungeon (Synty) | `PolygonDungeon/` | 89 MB | Dungeon kit meshes |
| POLYGON Starter Pack (Synty) | `PolygonStarter/` | 8 MB | Prototype props |

## Kept in git

- `Assets/Imports/KinematicCharacterController/` — the KCC sample scripts carry
  local Netcode modifications (`ExamplePlayer` is a `NetworkBehaviour` with an
  `IsOwner` gate). Do **not** overwrite these by re-importing KCC.
- `Assets/Imports/TextMesh Pro/` — small, and holds the TMP settings asset the
  UI prefabs reference by GUID.
