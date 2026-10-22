class_name Director
extends Node
## The DM's combat director, Risk of Rain 2-style. Credits accrue faster as difficulty climbs;
## the director spends them on adventurers at the spawn stairs. Difficulty rises with time and
## with ratings: showing off brings more guests (docs/gdd.md §3).
## Also scores kills: gold for everyone, ratings for style.

signal style(text: String, points: float)

const MAX_ALIVE := 28
const MULTI_WINDOW := 1.6
const DIFFICULTY_NAMES := ["Rehearsal", "Pilot", "Prime Time", "Sweeps Week", "Season Finale", "Cancelled?", "SYNDICATED", "LEGENDARY", "IT NEVER ENDS"]

var elapsed := 0.0
var credits := 20.0
var spawn_points: Array[Node3D] = []
var dm: DM
var enabled := true

var _spawn_t := 2.0
var _rng := RandomNumberGenerator.new()
var _recent_kills: Array[float] = []
var _kills := 0
var _quiet_t := 0.0


func setup(points: Array[Node3D], narrator: DM, seed_value: int) -> void:
	spawn_points = points
	dm = narrator
	_rng.seed = seed_value


## RoR2's coefficient shape: linear in minutes, plus the audience.
func difficulty() -> float:
	return 1.0 + elapsed / 60.0 * 0.32 + Ratings.value * 0.0025


func difficulty_name() -> String:
	return DIFFICULTY_NAMES[mini(int((difficulty() - 1.0) / 0.55), DIFFICULTY_NAMES.size() - 1)]


func chest_cost() -> int:
	return roundi(25.0 * pow(difficulty(), 1.25))


func alive() -> int:
	return get_tree().get_nodes_in_group("adventurers").size()


func _physics_process(delta: float) -> void:
	if not enabled:
		return
	elapsed += delta
	var players := maxi(get_tree().get_nodes_in_group("monsters").size(), 1)
	credits += delta * (0.9 + 0.75 * difficulty()) * (1.0 + 0.3 * (players - 1))
	_spawn_t -= delta
	if _spawn_t <= 0.0:
		_spawn_t = _rng.randf_range(2.5, 5.5)
		_spend()
	_quiet_t += delta
	if _quiet_t > 40.0:
		_quiet_t = 0.0
		dm.say("quiet", 60.0)
		var cast := get_tree().get_nodes_in_group("monsters")
		if not cast.is_empty():
			dm.bark((cast[_rng.randi() % cast.size()] as Monster).monster_id, "idle", 0.0, 4.0)
	for c in get_tree().get_nodes_in_group("chests"):
		if not (c as Chest).opened:
			(c as Chest).cost = chest_cost()


func _spend() -> void:
	if alive() >= MAX_ALIVE:
		return
	var origin := _spawn_origin()
	if origin == Vector3.INF:
		return
	var group := _rng.randi_range(1, 3 + int(difficulty()))
	for i in group:
		var id := _pick_type()
		if id == "":
			return
		var elite := ""
		var cost: float = AdventurerTypes.TYPES[id].cost
		if difficulty() > 1.6 and _rng.randf() < clampf((difficulty() - 1.6) * 0.15, 0.0, 0.35):
			elite = AdventurerTypes.ELITES.keys()[_rng.randi() % AdventurerTypes.ELITES.size()]
			cost *= AdventurerTypes.ELITES[elite].cost
			if cost > credits:
				elite = ""
				cost = AdventurerTypes.TYPES[id].cost
		credits -= cost
		spawn(id, elite, origin + Vector3(_rng.randf_range(-1.5, 1.5), 0.3, _rng.randf_range(-1.5, 1.5)))
		if elite != "":
			dm.say("elite", 20.0)


func _pick_type() -> String:
	var options: Array = []
	var total := 0.0
	for id in AdventurerTypes.TYPES:
		var t: Dictionary = AdventurerTypes.TYPES[id]
		if t.cost <= credits:
			options.append(id)
			total += t.weight
	if options.is_empty():
		return ""
	var r := _rng.randf() * total
	for id in options:
		r -= AdventurerTypes.TYPES[id].weight
		if r <= 0.0:
			return id
	return options[-1]


## Where the next group appears. Levels with spawn stairs use the stairs furthest from the
## monsters; open levels spawn on the navmesh 25-45 m from a random monster, like Risk of Rain 2.
func _spawn_origin() -> Vector3:
	if not spawn_points.is_empty():
		return _pick_spawn().global_position
	var monsters := get_tree().get_nodes_in_group("monsters")
	if monsters.is_empty():
		return Vector3.INF
	var m := monsters[_rng.randi() % monsters.size()] as Node3D
	var map := m.get_world_3d().navigation_map
	for attempt in 8:
		var a := _rng.randf() * TAU
		var want := m.global_position + Vector3(cos(a), 0.0, sin(a)) * _rng.randf_range(25.0, 45.0)
		var p := NavigationServer3D.map_get_closest_point(map, want)
		if p != Vector3.ZERO and Vector2(p.x - want.x, p.z - want.z).length() < 6.0:
			return p
	return Vector3.INF


func _pick_spawn() -> Node3D:
	var monsters := get_tree().get_nodes_in_group("monsters")
	var scored: Array = []
	for p in spawn_points:
		var nearest := INF
		for m in monsters:
			nearest = minf(nearest, p.global_position.distance_to((m as Node3D).global_position))
		scored.append([nearest + _rng.randf() * 6.0, p])
	scored.sort_custom(func(a, b): return a[0] > b[0])
	return scored[0][1]


func spawn(id: String, elite: String, at: Vector3) -> Adventurer:
	var a := Adventurer.new()
	get_parent().add_child(a)
	a.global_position = at
	a.setup(id, elite, difficulty(), _rng)
	a.killed.connect(_on_killed)
	return a


func _on_killed(adv: Adventurer, dmg: Damage) -> void:
	_kills += 1
	_quiet_t = 0.0
	var t: Dictionary = adv.type
	var e: Dictionary = AdventurerTypes.ELITES.get(adv.elite, {})
	var base_gold := float(t.gold) * float(e.get("gold", 1.0)) * (1.0 + 0.25 * (difficulty() - 1.0))
	for n in get_tree().get_nodes_in_group("monsters"):
		var m := n as Monster
		m.add_gold(roundi(base_gold * Ratings.gold_multiplier() * m.gold_mult))
	Fx.burst(adv.global_position + Vector3.UP, Palette.GOLD, 14, 4.0, 0.12, 0.6)

	var pts := 3.0
	_style("KILL", 3.0, false)
	if _kills == 1:
		dm.say("first_blood", 999.0)
	if dmg.kind == Damage.Kind.FIRE:
		_style("ROASTED", 5.0)
		dm.say("fire_kill", 15.0)
	if adv.is_airborne_launched():
		_style("MID-AIR", 7.0)
		dm.say("juggle", 15.0)
	if adv.elite != "":
		_style("ELITE", 10.0)
	var now := Time.get_ticks_msec() / 1000.0
	_recent_kills.append(now)
	_recent_kills = _recent_kills.filter(func(k): return now - k < MULTI_WINDOW)
	if _recent_kills.size() >= 3:
		_style("MULTI-KILL x%d" % _recent_kills.size(), 6.0 * _recent_kills.size())
		dm.say("multi", 10.0)
	Ratings.add(pts)
	_cast_react(dmg)


## Whoever got the kill says something about it.
func _cast_react(dmg: Damage) -> void:
	var m := dmg.source as Monster
	if m == null:
		return
	var event := "kill"
	if _recent_kills.size() >= 3:
		event = "multi"
	elif dmg.kind == Damage.Kind.FIRE:
		event = "fire_kill"
	dm.bark(m.monster_id, event, 4.0 if event == "multi" else 9.0)


func _style(text: String, points: float, show := true) -> void:
	if show:
		style.emit(text, points)
		Ratings.add(points, text)
