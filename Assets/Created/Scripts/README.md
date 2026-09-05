# 3dFungeonGame

## Overview
3dFungeonGame is a cooperative, DnD-inspired 3D dungeon crawler made in Unity and C#. Players explore procedurally generated dungeons filled with traps, enemies, and mysterious loot. Every run offers new layouts, item combinations, and class synergies, encouraging teamwork and creative problem-solving.

The game focuses on tension, exploration, and emergent storytelling, where every item, encounter, and decision can shape the outcome of a run.

---

## Core Features
- **Procedural 3D Dungeons** – Dynamically generated levels built from modular room chunks for endless replayability.  
- **Descriptive, Generative Loot** – Items feature mechanical modifiers and flavorful text reflecting their origin and traits.  
- **Playable Classes** – Multiple unique archetypes with distinct skills, cooldown systems, and cooperative roles.  
- **Dynamic Encounters** – Rooms include traps, puzzles, and monsters that encourage communication and strategic choices.  
- **Meta-Progression** – Unlock new class variants, room types, and loot templates through successful runs.  
- **Moddable Design** – Content is defined through JSON and prefab data for easy expansion.

---

## Project Structure
```
Assets/
 ├── Scripts/       # Core C# scripts for the game (not all are mine; I am the only one making the game)
 ├── Prefabs/       # Modular room chunks, traps, and interactables
 ├── Data/          # JSON files defining loot, enemies, and classes
 ├── Editor/        # Custom Unity editor tools
 └── Scenes/        # Testing and gameplay scenes
```

---

## Getting Started
1. Open the project in Unity (recommended version listed in the project settings).  
2. Load the `Playtest_Scene` in the `Scenes` folder.  
3. Press **Play** to begin a local test run.  
4. Modify JSON data in `Assets/Data` to experiment with new loot or class configurations.

---

## Controls
Standard first-person or third-person movement. Controls are configurable in the Unity Input settings and support keyboard/mouse or gamepad.

---

## Development Notes
The game systems are designed to be data-driven for flexible iteration. New content can be added without code changes by expanding JSON templates or adding prefabs. Generation logic supports seeded randomization for repeatable testing.

---

## License
This project is released for educational and non-commercial development purposes. See the included license file for full terms.

---

## Credits
Game design and programming by the 3dFungeonGame team.  
Special thanks to testers and contributors for feedback and support.
# 3dFungeonGame

## Overview
3dFungeonGame is a cooperative, DnD-inspired 3D dungeon crawler made in Unity and C#. Players explore procedurally generated dungeons filled with traps, enemies, and mysterious loot. Every run offers new layouts, item combinations, and class synergies, encouraging teamwork and creative problem-solving.

The game focuses on tension, exploration, and emergent storytelling, where every item, encounter, and decision can shape the outcome of a run.

---

## Core Features
- **Procedural 3D Dungeons** – Dynamically generated levels built from modular room chunks for endless replayability.  
- **Descriptive, Generative Loot** – Items feature mechanical modifiers and flavorful text reflecting their origin and traits.  
- **Playable Classes** – Multiple unique archetypes with distinct skills, cooldown systems, and cooperative roles.  
- **Dynamic Encounters** – Rooms include traps, puzzles, and monsters that encourage communication and strategic choices.  
- **Meta-Progression** – Unlock new class variants, room types, and loot templates through successful runs.  
- **Moddable Design** – Content is defined through JSON and prefab data for easy expansion.

---

## Project Structure
```
Assets/
 ├── Scripts/       # Core C# scripts for the game (not all are mine; I am the only one making the game)
 ├── Prefabs/       # Modular room chunks, traps, and interactables
 ├── Data/          # JSON files defining loot, enemies, and classes
 ├── Editor/        # Custom Unity editor tools
 └── Scenes/        # Testing and gameplay scenes
```

---

## Getting Started
1. Open the project in Unity (recommended version listed in the project settings).  
2. Load the `Playtest_Scene` in the `Scenes` folder.  
3. Press **Play** to begin a local test run.  
4. Modify JSON data in `Assets/Data` to experiment with new loot or class configurations.

---

## Controls
Standard first-person or third-person movement. Controls are configurable in the Unity Input settings and support keyboard/mouse or gamepad.

---

## Development Notes
The game systems are designed to be data-driven for flexible iteration. New content can be added without code changes by expanding JSON templates or adding prefabs. Generation logic supports seeded randomization for repeatable testing.

---

## License
This project is released for educational and non-commercial development purposes. See the included license file for full terms.

---
