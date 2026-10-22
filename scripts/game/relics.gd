class_name Relics
extends RefCounted
## Stacking passive items, Risk of Rain 2 rules. The dungeon's own treasure, 1000 years dusty.
## Effects are read by Monster.recompute_stats().

const ALL := {
	"rock_lunch": {"name": "Rock Lunch", "rarity": "common", "desc": "+25 max health. Pebble packed it for you."},
	"old_boots": {"name": "Old Boots", "rarity": "common", "desc": "+14% movement speed."},
	"rusty_spoon": {"name": "Rusty Spoon", "rarity": "common", "desc": "+12% damage. Allegedly cut a dragon in half."},
	"stage_makeup": {"name": "Stage Makeup", "rarity": "common", "desc": "+1.6 health regeneration."},
	"fan_mail": {"name": "Fan Mail", "rarity": "common", "desc": "+10% gold."},
	"energy_drink": {"name": "Grog", "rarity": "common", "desc": "+15% attack speed."},
	"monocle": {"name": "Cracked Monocle", "rarity": "common", "desc": "+10% chance to crit for double damage."},
	"bat_wing": {"name": "Bat Wing", "rarity": "uncommon", "desc": "+1 extra jump."},
	"hot_coals": {"name": "Hot Coals", "rarity": "uncommon", "desc": "10% chance for attacks to ignite."},
	"cue_cards": {"name": "Cue Cards", "rarity": "legendary", "desc": "Skill cooldowns -10%."},
}

## Small chest odds from Risk of Rain 2: 79.2% common, 19.8% uncommon, 0.99% legendary.
static func roll(rng: RandomNumberGenerator) -> String:
	var r := rng.randf()
	var tier := "common" if r < 0.792 else ("uncommon" if r < 0.99 else "legendary")
	var pool: Array = ALL.keys().filter(func(k): return ALL[k].rarity == tier)
	return pool[rng.randi() % pool.size()]


static func color(id: String) -> Color:
	return Palette.RARITY[ALL[id].rarity]
