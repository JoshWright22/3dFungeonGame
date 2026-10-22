class_name Palette
extends RefCounted
## The game's colour palette, Dark and Darker-style: near-black warm shadows, stone and timber in
## between, and torchlight as the only real colour. Cold moonlight is a whisper, never a fill.
## Creature skins are the Bestiary variants that sit inside this range (docs/gdd.md §10).

# -- world ------------------------------------------------------------------------
const VOID := Color("0b0a09")            ## unlit space, fog far colour
const SHADOW := Color("15161a")          ## cold shadow fill
const STONE_DARK := Color("2a2622")
const STONE := Color("4a433b")
const STONE_LIGHT := Color("6e655a")
const TIMBER := Color("3b2a1d")
const BONE := Color("c9bba0")

# -- light ------------------------------------------------------------------------
const TORCH := Color("ffb25e")           ## torch core
const TORCH_FALLOFF := Color("e07a2e")
const EMBER := Color("b8471c")
const MOONLIGHT := Color("3e5468")       ## rare cold fill through grates and shafts
const ARCANE := Color("6b8f86")          ## spell glows, the DM's magic

# -- accents ----------------------------------------------------------------------
const BLOOD := Color("6e1a16")
const GOLD := Color("d8a83f")
const ON_AIR := Color("c2362b")          ## the DM's broadcast UI
const PARCHMENT := Color("c9b79c")       ## UI text

# -- relic rarity, Dark and Darker's ladder --------------------------------------
const RARITY := {
	"poor": Color("8a8580"), "common": Color("e6e0d4"), "uncommon": Color("6fa34a"),
	"rare": Color("4a7fc1"), "epic": Color("9a5cc6"), "legendary": Color("e08a2e"),
	"unique": Color("f0d67a"),
}

# -- creatures: which Bestiary colour variant each monster wears --------------------
## variant index matches T_<Monster>_BaseColor_<n>.png
const SKINS := {
	"imp": 1,     ## blood red (#813434 / #491823): a demon under torchlight
	"puglin": 1,  ## moss olive (#515b00 / #5a5c24): grimy goblinoid, separates from the Imp by hue
}
## Alternates kept for elites and cosmetics: Imp 3 (slate violet), Puglin 3 (deep teal).
## Rejected for the main cast: Imp 2 (acid green) and Puglin 2 (red, clashes with the Imp).
