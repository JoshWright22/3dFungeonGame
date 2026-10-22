extends Node
## Run state: phase, seeds, party gold, player registry. Server-authoritative; clients mirror it.
## Autoload "Game".

signal phase_changed(phase: int)
signal expedition_started(seed: int)
signal expedition_ended(survived: bool)
signal gold_changed(gold: int)
signal player_registered(peer_id: int, player: Node)
signal player_unregistered(peer_id: int)

enum Phase { VILLAGE, DELVE, UNLEASHED }

## The dungeon is built far from the village so their lights and sounds never mix.
const DUNGEON_ORIGIN := Vector3(4000, 0, 0)
const SAVE_PATH := "user://party_save.json"

var phase: int = Phase.VILLAGE
var dungeon_seed: int = 0
var village_seed: int = 1337
var party_gold: int = 0
var expeditions: int = 0
var players: Dictionary = {}   # peer_id -> Node3D (player)
var world: Node = null         # set by scripts/world/world.gd


func _ready() -> void:
	_register_inputs()
	_load_save()


# -- players ------------------------------------------------------------------

func register_player(peer_id: int, player: Node) -> void:
	players[peer_id] = player
	player_registered.emit(peer_id, player)


func unregister_player(peer_id: int) -> void:
	players.erase(peer_id)
	player_unregistered.emit(peer_id)


func local_player() -> Node:
	return players.get(multiplayer.get_unique_id())


func living_players() -> Array:
	return players.values().filter(func(p): return is_instance_valid(p) and not p.get("is_dead"))


# -- expedition -----------------------------------------------------------------

## Host only. Picks a seed and tells everyone to build that dungeon.
func start_expedition(seed: int = 0) -> void:
	if not multiplayer.is_server():
		return
	if seed == 0:
		seed = randi_range(1, 2_000_000_000)
	_begin_expedition.rpc(seed)


@rpc("authority", "call_local", "reliable")
func _begin_expedition(seed: int) -> void:
	dungeon_seed = seed
	expeditions += 1
	Ratings.reset()
	_set_phase(Phase.DELVE)
	expedition_started.emit(seed)


## Host only. Everyone is back in the village (or dead): close the dungeon.
func end_expedition(survived: bool) -> void:
	if not multiplayer.is_server():
		return
	_end_expedition.rpc(survived)


@rpc("authority", "call_local", "reliable")
func _end_expedition(survived: bool) -> void:
	_set_phase(Phase.VILLAGE)
	expedition_ended.emit(survived)
	_save()


func set_unleashed() -> void:
	if phase == Phase.DELVE:
		_set_phase(Phase.UNLEASHED)


func _set_phase(p: int) -> void:
	if phase == p:
		return
	phase = p
	phase_changed.emit(p)


# -- economy --------------------------------------------------------------------

## Host only. Loot sold in the village.
func add_gold(amount: int) -> void:
	if not multiplayer.is_server():
		return
	_set_gold.rpc(party_gold + amount)


@rpc("authority", "call_local", "reliable")
func _set_gold(g: int) -> void:
	party_gold = g
	gold_changed.emit(g)
	if multiplayer.is_server():
		_save()


# -- drop-in state ----------------------------------------------------------------

## Everything a late joiner needs before the world can be built. See Net._on_peer_connected.
func get_state() -> Dictionary:
	return {
		"phase": phase, "dungeon_seed": dungeon_seed, "village_seed": village_seed,
		"gold": party_gold, "expeditions": expeditions, "ratings": Ratings.value,
	}


func apply_state(s: Dictionary) -> void:
	village_seed = s.village_seed
	party_gold = s.gold
	expeditions = s.expeditions
	dungeon_seed = s.dungeon_seed
	phase = s.phase
	Ratings.set_value_silent(s.ratings)
	gold_changed.emit(party_gold)
	phase_changed.emit(phase)
	if phase != Phase.VILLAGE:
		expedition_started.emit(dungeon_seed)


# -- persistence (host) -------------------------------------------------------------

func _save() -> void:
	var f := FileAccess.open(SAVE_PATH, FileAccess.WRITE)
	if f:
		f.store_string(JSON.stringify({"gold": party_gold, "expeditions": expeditions, "village_seed": village_seed}))


func _load_save() -> void:
	if not FileAccess.file_exists(SAVE_PATH):
		return
	var d = JSON.parse_string(FileAccess.get_file_as_string(SAVE_PATH))
	if d is Dictionary:
		party_gold = int(d.get("gold", 0))
		expeditions = int(d.get("expeditions", 0))
		village_seed = int(d.get("village_seed", village_seed))


# -- input ----------------------------------------------------------------------------

func _register_inputs() -> void:
	var keys := {
		"move_forward": [KEY_W], "move_back": [KEY_S], "move_left": [KEY_A], "move_right": [KEY_D],
		"jump": [KEY_SPACE], "sprint": [KEY_CTRL], "utility": [KEY_SHIFT], "special": [KEY_R],
		"interact": [KEY_E], "scoreboard": [KEY_TAB], "pause": [KEY_ESCAPE],
		"voice": [KEY_V], "slide": [KEY_C], "dev_swap": [KEY_F1], "dev_spawn": [KEY_F2],
	}
	for action in keys:
		_add_action(action)
		for k in keys[action]:
			var ev := InputEventKey.new()
			ev.physical_keycode = k
			InputMap.action_add_event(action, ev)
	for pair in [["primary", MOUSE_BUTTON_LEFT], ["secondary", MOUSE_BUTTON_RIGHT]]:
		_add_action(pair[0])
		var mb := InputEventMouseButton.new()
		mb.button_index = pair[1]
		InputMap.action_add_event(pair[0], mb)


func _add_action(action: String) -> void:
	if InputMap.has_action(action):
		InputMap.action_erase_events(action)
	else:
		InputMap.add_action(action)
