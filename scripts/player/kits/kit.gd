class_name MonsterKit
extends Node
## A monster's four skills, Risk of Rain 2 shape: primary (held), secondary, utility, special.
## Subclasses set `names`, `descs`, `cooldown` and override _primary/_secondary/_utility/_special.

var m: Monster
var names: Array[String] = ["Primary", "Secondary", "Utility", "Special"]
var descs: Array[String] = ["", "", "", ""]
var cooldown: Array[float] = [0.5, 4.0, 5.0, 25.0]
var remaining: Array[float] = [0.0, 0.0, 0.0, 0.0]

var _combo := 0
var _last_primary := -10.0


func _physics_process(delta: float) -> void:
	for i in 4:
		remaining[i] = maxf(remaining[i] - delta, 0.0)


func primary_input() -> void:
	if remaining[0] > 0.0 or m.downed:
		return
	remaining[0] = cooldown[0] / m.attack_speed
	var now := Time.get_ticks_msec() / 1000.0
	if now - _last_primary > 1.0:
		_combo = 0
	_last_primary = now
	_primary()
	_combo += 1


func trigger(slot: int) -> void:
	if remaining[slot] > 0.0 or m.downed:
		return
	var used := false
	match slot:
		1: used = _secondary()
		2: used = _utility()
		3: used = _special()
	if used:
		remaining[slot] = cooldown[slot] * m.cooldown_mult


func _primary() -> void:
	pass


func _secondary() -> bool:
	return false


func _utility() -> bool:
	return false


func _special() -> bool:
	return false


## Run `cb` after `seconds` of game time, if the monster still exists.
func later(seconds: float, cb: Callable) -> void:
	await get_tree().create_timer(seconds, false).timeout
	if is_instance_valid(m) and not m.downed:
		cb.call()


## Ground-level direction the player is steering, or the camera's forward if idle.
func move_or_aim_dir() -> Vector3:
	return m.wish.normalized() if m.wish != Vector3.ZERO else m.cam.forward_flat()


## Furthest safe point along `dir` up to `dist` (stops short of walls).
func clear_distance(dir: Vector3, dist: float) -> float:
	var from := m.chest()
	var q := PhysicsRayQueryParameters3D.create(from, from + dir * dist, Combat.LAYER_WORLD)
	var r := m.get_world_3d().direct_space_state.intersect_ray(q)
	return dist if r.is_empty() else maxf(from.distance_to(r.position) - m.def.radius - 0.3, 0.0)
