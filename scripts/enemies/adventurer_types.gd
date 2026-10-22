class_name AdventurerTypes
extends RefCounted
## The adventurers (docs/gdd.md §6). `cost` is what the director pays to spawn one; `gold` is the
## base payout on kill (× ratings multiplier). Stats are at difficulty 1 and scale up from there.

const CHARS := "res://assets/quaternius/characters/"
const OUTFITS := "res://assets/quaternius/outfits/"

const TYPES := {
	"peasant": {
		"name": "Peasant Adventurer", "outfit": "Peasant", "weapon": "Pickaxe_Bronze",
		"hp": 45.0, "armor": 0.0, "speed": 5.0, "damage": 4.5, "attack": "melee",
		"reach": 1.9, "windup": 0.55, "recover": 0.7, "clip": "Sword_Regular_A",
		"cost": 8.0, "gold": 4, "weight": 10.0, "mass": 55.0,
	},
	"fighter": {
		"name": "Fighter", "outfit": "Ranger", "weapon": "Sword_Bronze", "offhand": "Shield_Wooden",
		"hp": 120.0, "armor": 20.0, "speed": 4.4, "damage": 9.0, "attack": "melee",
		"reach": 2.2, "windup": 0.65, "recover": 0.8, "clip": "Sword_Heavy_Combo",
		"cost": 20.0, "gold": 10, "weight": 6.0, "mass": 80.0, "blocks": true,
	},
	"ranger": {
		"name": "Ranger", "outfit": "Ranger", "weapon": "Table_Knife",
		"hp": 55.0, "armor": 0.0, "speed": 5.2, "damage": 6.0, "attack": "knife",
		"reach": 16.0, "keep_away": 7.0, "windup": 0.7, "recover": 0.9, "clip": "OverhandThrow",
		"cost": 16.0, "gold": 8, "weight": 6.0, "mass": 55.0,
	},
	"wizard": {
		"name": "Wizard", "outfit": "Peasant", "weapon": "Torch_Metal",
		"hp": 50.0, "armor": 0.0, "speed": 4.2, "damage": 10.0, "attack": "spell",
		"reach": 18.0, "keep_away": 9.0, "windup": 1.0, "recover": 1.2, "clip": "Spell_Simple_Shoot",
		"cost": 24.0, "gold": 12, "weight": 4.0, "mass": 50.0,
	},
}

## Elite affixes, DM-flavoured. Multipliers apply on top of difficulty scaling.
const ELITES := {
	"veteran": {"name": "Veteran", "hp": 3.0, "damage": 1.5, "scale": 1.18, "cost": 3.0, "gold": 3.0, "tint": Color("b8a27a")},
	"blessed": {"name": "Blessed", "hp": 2.2, "damage": 1.3, "scale": 1.08, "cost": 3.0, "gold": 3.0, "tint": Color("f0d67a"), "regen": 4.0},
}


static func body_for(rng: RandomNumberGenerator) -> String:
	return "Male" if rng.randf() < 0.6 else "Female"


static func body_path(sex: String) -> String:
	return CHARS + "Superhero_%s_FullBody.gltf" % sex


static func outfit_path(sex: String, outfit: String) -> String:
	return OUTFITS + "%s_%s.gltf" % [sex, outfit]
