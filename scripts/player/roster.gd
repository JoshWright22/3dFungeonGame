class_name Roster
extends RefCounted
## The Big Six. Stats and wiring for each playable monster (docs/gdd.md §5).
## Only the Imp and Puglin ship in the free Bestiary tier; the rest need the Source tier.

const BESTIARY := "res://assets/quaternius/bestiary/"

const MONSTERS := {
	"imp": {
		"name": "Vex", "title": "The Imp", "model": BESTIARY + "Imp.glb", "scale": 1.0,
		"hp": 120.0, "regen": 1.2, "armor": 0.0, "speed": 7.5, "jump": 10.0, "air_jumps": 0,
		"damage": 14.0, "radius": 0.38, "camera": 4.3,
		"loco": ["Idle", "Walk", "Jog_Fwd", "Sprint"], "kit": "res://scripts/player/kits/imp_kit.gd",
		"color": Color(0.85, 0.25, 0.2),
	},
	"puglin": {
		"name": "Biscuit", "title": "The Puglin", "model": BESTIARY + "Puglin.glb", "scale": 1.25,
		"hp": 95.0, "regen": 1.0, "armor": 0.0, "speed": 8.2, "jump": 10.5, "air_jumps": 1,
		"damage": 11.0, "radius": 0.34, "camera": 3.6,
		"loco": ["Idle", "Walk", "Jog_Fwd", "Sprint"], "kit": "res://scripts/player/kits/puglin_kit.gd",
		"color": Color(0.55, 0.75, 0.3),
	},
}


static func get_def(id: String) -> Dictionary:
	return MONSTERS[id]
