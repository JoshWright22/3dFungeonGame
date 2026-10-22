class_name Dressing
extends RefCounted
## Set dressing: props placed as sets that make sense together, composed big → medium → small.
##
## A set has one big anchor, medium pieces arranged around it with intent (seats face the table,
## barrels pack tight against the crates), and small items on the anchor's top surface or at the
## feet of the medium pieces. Sets are separated by empty ground; nothing is a lone random roll.
##
## Everything placed is static Decor: big pieces block, small clutter is visual only.

const SETS := {
	"camp": {
		"anchor": ["@fire"], "medium": ["Bench", "Stool", "Stool", "Anvil_Log"], "medium_count": [2, 4],
		"ring": [2.2, 2.8], "face": "in", "small": ["Mug", "Table_Plate", "Bucket_Wooden_1", "Bag", "Bottle_1"], "small_count": [2, 4],
	},
	"storage": {
		"anchor": ["Crate_Wooden"], "stack": 2, "medium": ["Barrel", "Barrel", "Crate_Metal", "FarmCrate_Empty"], "medium_count": [2, 4],
		"ring": [1.0, 1.4], "face": "aligned", "small": ["Bucket_Wooden_1", "Bag", "Pouch_Large", "Bucket_Metal"], "small_count": [2, 3],
	},
	"dining": {
		"anchor": ["Table_Large"], "medium": ["Chair_1", "Chair_1", "Stool", "Bench"], "medium_count": [3, 5],
		"ring": [1.3, 1.6], "face": "in", "top": ["Mug", "Table_Plate", "Bottle_1", "CandleStick", "Pot_1", "Table_Plate", "Mug"], "top_count": [3, 6],
	},
	"smithy": {
		"anchor": ["Anvil"], "on_log": true, "medium": ["Whetstone", "Workbench", "Barrel"], "medium_count": [2, 3],
		"ring": [1.6, 2.2], "face": "aligned", "small": ["Pickaxe_Bronze", "Sword_Bronze", "Axe_Bronze", "Bucket_Metal", "Chain_Coil"], "small_count": [2, 4],
	},
	"study": {
		"anchor": ["Table_Large"], "medium": ["Bookcase_2", "Chair_1", "CandleStick_Stand"], "medium_count": [2, 3],
		"ring": [1.5, 1.9], "face": "in", "top": ["Book_Stack_1", "Book_5", "Scroll_1", "CandleStick_Triple", "Book_5"], "top_count": [3, 5],
	},
	"apothecary": {
		"anchor": ["Cauldron"], "medium": ["Barrel", "Crate_Wooden", "Stool"], "medium_count": [1, 3],
		"ring": [1.3, 1.7], "face": "aligned", "small": ["Potion_1", "SmallBottles_1", "Bottle_1", "Pot_1", "Vase_4"], "small_count": [3, 5],
	},
	"farm": {
		"anchor": ["FarmCrate_Apple"], "stack": 2, "medium": ["Barrel_Apples", "FarmCrate_Empty", "Barrel"], "medium_count": [2, 3],
		"ring": [0.9, 1.3], "face": "aligned", "small": ["Bucket_Wooden_1", "Bag"], "small_count": [1, 2],
	},
	"hoard": {
		"anchor": ["@none"], "medium": ["CandleStick_Stand", "CandleStick_Stand"], "medium_count": [2, 2],
		"ring": [1.3, 1.3], "face": "aligned", "small": ["Coin_Pile", "Coin_Pile_2", "Chalice", "Key_Gold", "Potion_1"], "small_count": [2, 4],
	},
}

var host: Node3D
var rng: RandomNumberGenerator
var ground: Callable            ## (x, z) -> ground height
var fire_maker: Callable        ## (position) -> void, for "@fire" anchors


func _init(owner: Node3D, random: RandomNumberGenerator, height_at: Callable, make_fire: Callable) -> void:
	host = owner
	rng = random
	ground = height_at
	fire_maker = make_fire


## Place a whole set centred at `at` (only x/z are used), anchor facing `yaw`.
func place(set_name: String, at: Vector3, yaw: float) -> void:
	var s: Dictionary = SETS[set_name]
	var center := Vector3(at.x, ground.call(at.x, at.z), at.z)
	var anchor: Node3D = null
	var anchor_id: String = _pick(s.anchor)
	if anchor_id == "@fire":
		fire_maker.call(center)
	elif anchor_id != "@none":
		if s.get("on_log", false):
			var log_p := _prop("Anvil_Log", center, yaw)
			center.y += _aabb(log_p).end.y
		anchor = _prop(anchor_id, center, yaw)
		for i in int(s.get("stack", 0)) - 1:
			var top := center + Vector3.UP * (_aabb(anchor).end.y * (i + 1) + 0.02)
			_prop(anchor_id, top, yaw + rng.randf_range(-0.25, 0.25))

	# medium pieces in a ring, either facing the anchor or squared to it
	var n := rng.randi_range(s.medium_count[0], s.medium_count[1])
	var start := rng.randf() * TAU
	var mediums: Array[Node3D] = []
	for i in n:
		var a := start + TAU * i / n + rng.randf_range(-0.25, 0.25)
		var r := rng.randf_range(s.ring[0], s.ring[1])
		var p := center + Vector3(cos(a), 0.0, sin(a)) * r
		p.y = ground.call(p.x, p.z)
		var face := atan2(center.x - p.x, center.z - p.z) + PI if s.face == "in" else yaw + rng.randf_range(-0.2, 0.2)
		mediums.append(_prop(_pick(s.medium), p, face))

	# small things: on the anchor's top surface, or at the feet of the medium pieces
	if s.has("top") and anchor != null:
		var box := _aabb(anchor)
		for i in rng.randi_range(s.top_count[0], s.top_count[1]):
			var local := Vector3(rng.randf_range(box.position.x * 0.75, box.end.x * 0.75), box.end.y + 0.03, rng.randf_range(box.position.z * 0.7, box.end.z * 0.7))
			_prop(_pick(s.top), anchor.global_position + Basis(Vector3.UP, yaw) * local, rng.randf() * TAU)
	if s.has("small"):
		for i in rng.randi_range(s.small_count[0], s.small_count[1]):
			var base: Vector3 = mediums[rng.randi() % mediums.size()].global_position if not mediums.is_empty() else center
			var a := rng.randf() * TAU
			var p := base + Vector3(cos(a), 0.0, sin(a)) * rng.randf_range(0.45, 0.9)
			p.y = ground.call(p.x, p.z)
			_prop(_pick(s.small), p, rng.randf() * TAU)


## A rock outcrop: one boulder, a few medium rocks leaning on it, pebbles at the base.
func rocks(at: Vector3, big_scale: float, place_static: Callable) -> void:
	place_static.call("Rock_Medium_%d" % rng.randi_range(1, 3), at, big_scale, true)
	for i in rng.randi_range(2, 3):
		var a := rng.randf() * TAU
		var p := at + Vector3(cos(a), 0.0, sin(a)) * big_scale * rng.randf_range(1.1, 1.6)
		place_static.call("Rock_Medium_%d" % rng.randi_range(1, 3), p, big_scale * rng.randf_range(0.3, 0.5), true)
	for i in rng.randi_range(4, 7):
		var a := rng.randf() * TAU
		var p := at + Vector3(cos(a), 0.0, sin(a)) * big_scale * rng.randf_range(1.4, 2.4)
		place_static.call("Pebble_Round_%d" % rng.randi_range(1, 5), p, rng.randf_range(1.5, 3.0), false)


## A grove: one big tree, smaller ones around it, undergrowth at their feet.
func grove(at: Vector3, kind: String, place_static: Callable) -> void:
	place_static.call("%s_%d" % [kind, rng.randi_range(1, 5)], at, rng.randf_range(1.1, 1.3), true)
	for i in rng.randi_range(2, 4):
		var a := rng.randf() * TAU
		var p := at + Vector3(cos(a), 0.0, sin(a)) * rng.randf_range(4.0, 7.0)
		place_static.call("%s_%d" % [kind, rng.randi_range(1, 5)], p, rng.randf_range(0.6, 0.85), true)
		for j in rng.randi_range(1, 3):
			var b := rng.randf() * TAU
			var q := p + Vector3(cos(b), 0.0, sin(b)) * rng.randf_range(0.8, 2.0)
			place_static.call(["Grass_Wispy_Tall", "Fern_1", "Mushroom_Common", "Grass_Common_Short"][rng.randi() % 4], q, rng.randf_range(0.9, 1.4), false)


func _pick(options: Array) -> String:
	return options[rng.randi() % options.size()]


func _prop(id: String, at: Vector3, yaw: float) -> Node3D:
	var p := Decor.create(id)
	host.add_child(p)
	p.global_position = at
	p.rotation.y = yaw
	return p


func _aabb(p: Node3D) -> AABB:
	return p.get_meta("aabb", AABB(Vector3(-0.3, 0, -0.3), Vector3(0.6, 0.6, 0.6)))
