class_name Adventurer
extends CharacterBody3D
## An adventurer who has come to raid the dungeon. Host-simulated AI:
## chase the nearest monster on the navmesh, telegraph, attack, recover. Knockback launches them;
## on death they collapse, then sink into the ground (Risk of Rain 2-style).

signal killed(adv: Adventurer, dmg: Damage)

enum S { CHASE, WINDUP, RECOVER, STUN, LAUNCHED, DEAD }

const GRAVITY := 30.0

var type_id := ""
var type: Dictionary
var elite := ""
var rig: Rig
var health: Health
var agent: NavigationAgent3D
var target: Monster
var state := S.CHASE
var damage := 10.0
var speed := 5.0
var launched_by: Node                ## who knocked them airborne (juggle credit)

var _t := 0.0
var _repath := 0.0
var _retarget := 0.0
var _sniffed_until := 0.0
var _stun_cute := false
var _weapon_bone: BoneAttachment3D


func setup(id: String, elite_id: String, difficulty: float, rng: RandomNumberGenerator) -> void:
	type_id = id
	type = AdventurerTypes.TYPES[id]
	elite = elite_id
	add_to_group("adventurers")
	collision_layer = Combat.LAYER_ENEMIES
	collision_mask = Combat.LAYER_WORLD | Combat.LAYER_PROPS | Combat.LAYER_PLAYERS | Combat.LAYER_ENEMIES
	floor_max_angle = deg_to_rad(50.0)

	var e: Dictionary = AdventurerTypes.ELITES.get(elite_id, {})
	var sex := AdventurerTypes.body_for(rng)
	rig = Rig.new()
	rig.name = "Rig"
	add_child(rig)
	rig.build(AdventurerTypes.body_path(sex), [AdventurerTypes.outfit_path(sex, type.outfit)], ["Idle", "Walk", "Jog_Fwd", "Sprint"], float(e.get("scale", 1.0)))
	_attach("hand_r", type.weapon)
	if type.has("offhand"):
		_attach("hand_l", type.offhand)
	if e.has("tint"):
		var glow := ShaderMaterial.new()
		glow.shader = load("res://shaders/elite_rim.gdshader")
		glow.set_shader_parameter("tint", e.tint)
		for mi in rig.meshes():
			mi.material_overlay = glow

	var cs := CollisionShape3D.new()
	var cap := CapsuleShape3D.new()
	cap.radius = 0.36
	cap.height = rig.height * 0.95
	cs.shape = cap
	cs.position.y = cap.height * 0.5
	add_child(cs)

	health = Health.new()
	health.name = "Health"
	add_child(health)
	health.setup(type.hp * (1.0 + 0.3 * (difficulty - 1.0)) * float(e.get("hp", 1.0)))
	health.armor = type.armor
	health.regen = float(e.get("regen", 0.0))
	health.damaged.connect(_on_damaged)
	health.died.connect(_on_died)
	damage = type.damage * (1.0 + 0.2 * (difficulty - 1.0)) * float(e.get("damage", 1.0))
	speed = type.speed * rng.randf_range(0.92, 1.08)

	agent = NavigationAgent3D.new()
	agent.path_desired_distance = 0.7
	agent.target_desired_distance = 1.0
	agent.radius = 0.4
	agent.height = 1.8
	add_child(agent)


func _attach(bone: String, prop_id: String) -> void:
	var ba := BoneAttachment3D.new()
	ba.bone_name = bone
	rig.skeleton.add_child(ba)
	var model: Node3D = load(Decor.path(prop_id)).instantiate()
	ba.add_child(model)
	# props are modelled upright; lay them along the hand's grip
	model.rotation_degrees = Vector3(0, 0, -90) if bone == "hand_r" else Vector3(0, 90, -90)
	if bone == "hand_r":
		_weapon_bone = ba


# -- public ----------------------------------------------------------------------------------

func mark_sniffed(seconds: float) -> void:
	_sniffed_until = Time.get_ticks_msec() / 1000.0 + seconds
	health.damage_taken_mult = 1.25
	Highlight.apply(self, Palette.ARCANE, seconds)


func stun(seconds: float, cute := false) -> void:
	if state == S.DEAD:
		return
	state = S.STUN
	_t = seconds
	_stun_cute = cute
	if cute:
		health.damage_taken_mult = 1.5
		rig.play_full("Idle_Talking", 1.0)


func apply_knockback(v: Vector3) -> void:
	if state == S.DEAD:
		return
	velocity += v
	if v.length() > 7.0 or v.y > 4.0:
		state = S.LAUNCHED
		_t = 0.25


func is_airborne_launched() -> bool:
	return state == S.LAUNCHED


# -- AI ---------------------------------------------------------------------------------------

func _physics_process(delta: float) -> void:
	if state == S.DEAD:
		return
	_t -= delta
	_retarget -= delta
	if _retarget <= 0.0 or not is_instance_valid(target) or target.downed:
		_retarget = 0.5
		target = _nearest_monster()
	if Time.get_ticks_msec() / 1000.0 > _sniffed_until and not (state == S.STUN and _stun_cute):
		health.damage_taken_mult = 1.0

	var hvel := Vector3(velocity.x, 0.0, velocity.z)
	match state:
		S.CHASE:
			hvel = _chase(delta)
		S.WINDUP:
			hvel = hvel.move_toward(Vector3.ZERO, 30.0 * delta)
			_face(target.global_position if target else global_position - rig.global_basis.z, delta, 10.0)
			if _t <= 0.0:
				_attack()
				state = S.RECOVER
				_t = type.recover
		S.RECOVER:
			hvel = hvel.move_toward(Vector3.ZERO, 30.0 * delta)
			if _t <= 0.0:
				state = S.CHASE
		S.STUN:
			hvel = hvel.move_toward(Vector3.ZERO, 30.0 * delta)
			if _t <= 0.0:
				state = S.CHASE
				rig.stop_actions()
		S.LAUNCHED:
			if is_on_floor() and _t <= 0.0:
				state = S.CHASE
				rig.play_full("LayToIdle", 1.6)
				hvel *= 0.2

	velocity.x = hvel.x
	velocity.z = hvel.z
	if not is_on_floor():
		velocity.y -= GRAVITY * delta
	move_and_slide()
	var v := hvel.length()
	rig.set_locomotion(0.0 if v < 0.3 else clampf(v / speed * 2.0, 0.0, 3.0), state == S.LAUNCHED, false, delta)


func _chase(delta: float) -> Vector3:
	if target == null:
		return Vector3.ZERO
	var to := target.global_position - global_position
	var dist := Vector2(to.x, to.z).length()
	var reach: float = type.reach
	var ranged: bool = type.attack != "melee"
	if dist <= reach and _has_los() and (not ranged or dist > 2.5):
		state = S.WINDUP
		_t = type.windup
		rig.play_upper(type.clip, rig.clip_length(type.clip) / maxf(type.windup + 0.3, 0.1))
		return Vector3.ZERO
	_repath -= delta
	if _repath <= 0.0:
		_repath = 0.3
		agent.target_position = target.global_position
	var next := agent.get_next_path_position()
	var dir := next - global_position
	dir.y = 0.0
	if dir.length() < 0.05:
		dir = Vector3(to.x, 0.0, to.z)
	dir = dir.normalized()
	if ranged and dist < float(type.get("keep_away", 0.0)):
		dir = -Vector3(to.x, 0.0, to.z).normalized()
	_face(global_position + dir, delta, 8.0)
	return dir * speed


func _attack() -> void:
	if target == null:
		return
	var hand := _weapon_bone.global_position if _weapon_bone else global_position + Vector3.UP * 1.4
	match type.attack:
		"melee":
			var fwd := -rig.global_basis.z
			Combat.hit_sphere(self, global_position + Vector3.UP * 1.0 + fwd * type.reach * 0.6, 1.1, damage, 7.0, Combat.LAYER_PLAYERS)
		"knife":
			var aim := target.chest() + target.velocity * 0.25
			Projectile.thrown_model(self, hand, (aim - hand).normalized(), damage, "Table_Knife", Combat.LAYER_PLAYERS)
		"spell":
			var aim := target.chest()
			Projectile.fireball(self, hand, (aim - hand).normalized(), damage, Palette.ARCANE, Combat.LAYER_PLAYERS, 16.0, 2.4)


func _face(point: Vector3, delta: float, rate: float) -> void:
	var d := point - global_position
	if Vector2(d.x, d.z).length() < 0.01:
		return
	rig.rotation.y = lerp_angle(rig.rotation.y, atan2(-d.x, -d.z), 1.0 - exp(-rate * delta))


func _has_los() -> bool:
	var from := global_position + Vector3.UP * 1.5
	var q := PhysicsRayQueryParameters3D.create(from, target.chest(), Combat.LAYER_WORLD)
	return get_world_3d().direct_space_state.intersect_ray(q).is_empty()


func _nearest_monster() -> Monster:
	var best: Monster = null
	var best_d := INF
	for n in get_tree().get_nodes_in_group("monsters"):
		var mo := n as Monster
		if mo.downed:
			continue
		var d := mo.global_position.distance_squared_to(global_position)
		if d < best_d:
			best_d = d
			best = mo
	return best


# -- damage and death ---------------------------------------------------------------------------

func _on_damaged(dmg: Damage) -> void:
	if type.get("blocks", false) and state != S.STUN and dmg.kind == Damage.Kind.MELEE and dmg.source is Node3D:
		var from_dir := ((dmg.source as Node3D).global_position - global_position).normalized()
		if from_dir.dot(-rig.global_basis.z) > 0.5:
			health.hp += dmg.amount * 0.5   # shield took half
			Fx.burst(dmg.point, Palette.BONE, 10, 4.0, 0.1, 0.25)
	if dmg.impulse != Vector3.ZERO:
		if dmg.impulse.length() > 7.0:
			launched_by = dmg.source
		apply_knockback(dmg.impulse)
	if state == S.WINDUP and dmg.amount > health.max_hp * 0.25:
		state = S.RECOVER   # heavy hits interrupt
		_t = 0.4
	Fx.burst(dmg.point, Palette.BLOOD.lightened(0.15), 8, 3.5, 0.12, 0.35)


func _on_died(dmg: Damage) -> void:
	state = S.DEAD
	remove_from_group("adventurers")
	killed.emit(self, dmg)
	collision_layer = 0
	collision_mask = Combat.LAYER_WORLD
	rig.play_death()
	velocity = dmg.impulse * 0.6
	var t := create_tween()
	t.tween_interval(2.5)
	t.tween_property(self, "position:y", position.y - 2.2, 1.5).set_ease(Tween.EASE_IN)
	t.tween_callback(queue_free)
	set_physics_process(false)
	_settle()


## Let the body finish its fall before it sinks.
func _settle() -> void:
	for i in 40:
		await get_tree().physics_frame
		if not is_inside_tree():
			return
		velocity.x = move_toward(velocity.x, 0.0, 0.6)
		velocity.z = move_toward(velocity.z, 0.0, 0.6)
		velocity.y -= GRAVITY / 60.0
		move_and_slide()
