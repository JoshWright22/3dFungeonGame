class_name Monster
extends CharacterBody3D
## A playable monster. Risk of Rain 2-style third-person movement (sprint toggle, momentum kept in
## the air, slide, mantle), four skills from its Kit, and E to open chests.
## The body never rotates; the Rig child turns to face movement or the crosshair.

signal gold_changed(gold: int)
signal relics_changed
signal toast(text: String)

const GRAVITY := 30.0
const ACCEL := 80.0
const AIR_ACCEL := 28.0
const SPRINT_MULT := 1.45
const COYOTE := 0.12
const JUMP_BUFFER := 0.14
const SLIDE_TIME := 0.8
const SLIDE_BOOST := 1.3
const DOWNED_SECONDS := 6.0

var monster_id := ""
var def: Dictionary
var rig: Rig
var health: Health
var cam: CameraRig
var kit: MonsterKit

var gold := 0
var relics: Dictionary = {}       ## relic id -> stacks

# derived stats (recompute_stats)
var move_speed := 7.0
var jump_velocity := 10.0
var max_air_jumps := 0
var crit_chance := 0.01
var attack_speed := 1.0
var cooldown_mult := 1.0
var gold_mult := 1.0
var ignite_chance := 0.0
var _damage_mult := 1.0

# state
var sprinting := false
var sliding := false
var downed := false
var speed_buff := 1.0             ## kit buffs (Scurry)
var wall_climb := false           ## kit buffs (Scurry)
var aim_lock := 0.0               ## > 0: face the crosshair
var control_lock := 0.0           ## > 0: no steering (knocked)
var wish := Vector3.ZERO

var _coyote := 0.0
var _jump_buf := 0.0
var _air_jumps := 0
var _slide_t := 0.0
var _knock := Vector3.ZERO
var _downed_t := 0.0


func setup(id: String, camera: CameraRig) -> void:
	monster_id = id
	def = Roster.get_def(id)
	add_to_group("monsters")
	collision_layer = Combat.LAYER_PLAYERS
	collision_mask = Combat.LAYER_WORLD | Combat.LAYER_PROPS | Combat.LAYER_ENEMIES
	floor_max_angle = deg_to_rad(50.0)
	floor_snap_length = 0.45

	rig = Rig.new()
	rig.name = "Rig"
	add_child(rig)
	rig.build(def.model, [], def.loco, def.scale)
	var skin: int = Palette.SKINS.get(id, 1)
	rig.set_skin("res://assets/quaternius/bestiary/textures/T_%s_BaseColor_%d.png" % [id.capitalize(), skin])

	var cs := CollisionShape3D.new()
	var cap := CapsuleShape3D.new()
	cap.radius = def.radius
	cap.height = maxf(rig.height * 0.92, def.radius * 2.0 + 0.1)
	cs.shape = cap
	cs.position.y = cap.height * 0.5
	add_child(cs)

	health = Health.new()
	health.name = "Health"
	add_child(health)
	health.setup(def.hp)
	health.died.connect(_on_downed)
	health.damaged.connect(_on_damaged)

	kit = load(def.kit).new()
	kit.name = "Kit"
	kit.m = self
	add_child(kit)

	cam = camera
	cam.setup(self, rig.height * 0.85 + 0.35, def.camera)
	recompute_stats()


# -- stats ---------------------------------------------------------------------------

func recompute_stats() -> void:
	var r := func(id: String) -> int: return int(relics.get(id, 0))
	move_speed = def.speed * (1.0 + 0.14 * r.call("old_boots"))
	jump_velocity = def.jump
	max_air_jumps = int(def.air_jumps) + r.call("bat_wing")
	crit_chance = 0.01 + 0.1 * r.call("monocle")
	_damage_mult = 1.0 + 0.12 * r.call("rusty_spoon")
	attack_speed = 1.0 + 0.15 * r.call("energy_drink")
	gold_mult = 1.0 + 0.1 * r.call("fan_mail")
	ignite_chance = 0.1 * r.call("hot_coals")
	cooldown_mult = pow(0.9, r.call("cue_cards"))
	var hp_before := health.max_hp
	health.max_hp = def.hp + 25.0 * r.call("rock_lunch")
	health.hp += health.max_hp - hp_before
	health.regen = def.regen + 1.6 * r.call("stage_makeup")
	health.armor = def.armor
	relics_changed.emit()


func damage_mult() -> float:
	return _damage_mult


## Damage for one hit: base × multipliers, doubled on a crit (Risk of Rain 2 rules).
func roll_damage(mult: float) -> float:
	var d := float(def.damage) * mult * _damage_mult
	return d * 2.0 if randf() < crit_chance else d


const REACH := 3.5

## The chest (or other interactable) under the crosshair within reach, or null.
func look_interactable() -> Node:
	var r := cam.look_ray(REACH + cam.distance + 1.0, Combat.LAYER_WORLD, [get_rid()])
	if r.is_empty() or r.position.distance_to(chest()) > REACH:
		return null
	var n: Node = r.collider
	while n != null and not n.is_in_group("interactable"):
		n = n.get_parent()
	return n


func add_gold(amount: int) -> void:
	gold += amount
	gold_changed.emit(gold)


func add_relic(id: String) -> void:
	relics[id] = int(relics.get(id, 0)) + 1
	recompute_stats()
	_bark("relic")


func _bark(event: String) -> void:
	for n in get_tree().get_nodes_in_group("narrator"):
		(n as DM).bark(monster_id, event, 0.0)


# -- helpers for kits ------------------------------------------------------------------------

func chest() -> Vector3:
	return global_position + Vector3.UP * rig.height * 0.62


func aim_dir_from(origin: Vector3) -> Vector3:
	return (cam.aim_point([get_rid()]) - origin).normalized()


func facing() -> Vector3:
	return -rig.global_basis.z


## Face the crosshair for `seconds` and stop sprinting (Risk of Rain 2: attacking cancels sprint).
func face_aim(seconds: float, cancel_sprint := true) -> void:
	aim_lock = maxf(aim_lock, seconds)
	rig.rotation.y = cam.yaw
	if cancel_sprint:
		sprinting = false


## Melee swing in front of the monster along the camera's flat forward.
func melee(reach: float, radius: float, mult: float, knockback: float, kind := Damage.Kind.MELEE) -> Array[Health]:
	var fwd := cam.forward_flat()
	var center := chest() + fwd * reach * 0.6
	var ign := 3.0 if randf() < ignite_chance else 0.0
	var hits := Combat.hit_sphere(self, center, radius, roll_damage(mult), knockback, Combat.LAYER_ENEMIES, kind, ign)
	if not hits.is_empty():
		Combat.shake(0.18 + 0.05 * hits.size())
		Combat.hitstop(0.035 if mult < 1.5 else 0.07)
	return hits


func apply_knockback(v: Vector3) -> void:
	_knock += v
	if v.length() > 6.0:
		control_lock = 0.25


# -- movement --------------------------------------------------------------------------------

func _physics_process(delta: float) -> void:
	if downed:
		_downed_tick(delta)
		return
	aim_lock -= delta
	control_lock -= delta
	var captured := Input.mouse_mode == Input.MOUSE_MODE_CAPTURED
	var input := Input.get_vector("move_left", "move_right", "move_forward", "move_back") if captured and control_lock <= 0.0 else Vector2.ZERO
	var fwd := cam.forward_flat()
	var right := Vector3(-fwd.z, 0.0, fwd.x)
	wish = (right * input.x - fwd * input.y).limit_length(1.0)

	if captured and Input.is_action_just_pressed("sprint"):
		sprinting = not sprinting
	if input.length() < 0.1 or input.y > 0.5:
		sprinting = false

	var on_floor := is_on_floor()
	if on_floor:
		_coyote = COYOTE
		_air_jumps = max_air_jumps
	else:
		_coyote -= delta
	_jump_buf = JUMP_BUFFER if (captured and Input.is_action_just_pressed("jump")) else _jump_buf - delta

	var speed := move_speed * speed_buff * (SPRINT_MULT if sprinting else 1.0)
	var hvel := Vector3(velocity.x, 0.0, velocity.z)

	if captured and Input.is_action_just_pressed("slide") and on_floor and not sliding and hvel.length() > move_speed * 0.8:
		sliding = true
		_slide_t = SLIDE_TIME
		hvel = hvel.normalized() * maxf(hvel.length(), speed) * SLIDE_BOOST

	if sliding:
		_slide_t -= delta
		hvel = hvel.move_toward(Vector3.ZERO, 6.0 * delta)
		if wish != Vector3.ZERO:
			hvel = hvel.slerp(wish * hvel.length(), 2.0 * delta)
		if _slide_t <= 0.0 or hvel.length() < move_speed * 0.55:
			sliding = false
	elif on_floor:
		hvel = hvel.move_toward(wish * speed, ACCEL * delta)
	elif wish != Vector3.ZERO:
		# airborne: steer freely, but never brake momentum you earned (slides, blinks, launches)
		if hvel.length() > speed and hvel.dot(wish) > 0.0:
			hvel = hvel.slerp(wish * hvel.length(), 3.0 * delta)
		else:
			hvel = hvel.move_toward(wish * speed, AIR_ACCEL * delta)

	velocity.x = hvel.x + _knock.x
	velocity.z = hvel.z + _knock.z
	velocity.y += _knock.y
	_knock = Vector3.ZERO

	if _jump_buf > 0.0:
		if _coyote > 0.0:
			_jump(false)
		elif _air_jumps > 0:
			_air_jumps -= 1
			_jump(true)

	if is_on_wall() and wish != Vector3.ZERO and wish.dot(-get_wall_normal()) > 0.4:
		if wall_climb:
			velocity.y = maxf(velocity.y, 6.5)
		elif not on_floor and velocity.y < 3.0 and _can_mantle():
			velocity.y = 7.8

	if not on_floor:
		velocity.y -= GRAVITY * delta
	move_and_slide()

	_update_facing(delta)
	_update_anim(delta)
	if captured:
		_handle_actions(delta)


func _jump(air: bool) -> void:
	velocity.y = jump_velocity * (0.92 if air else 1.0)
	if air and wish != Vector3.ZERO:
		var h := Vector2(velocity.x, velocity.z).length()
		var redirected := wish * maxf(h, move_speed)
		velocity.x = redirected.x
		velocity.z = redirected.z
		rig.play_full("NinjaJump_Start", 1.6)
	_jump_buf = 0.0
	_coyote = 0.0
	sliding = false


func _can_mantle() -> bool:
	var space := get_world_3d().direct_space_state
	var fwd := -get_wall_normal()
	var top := global_position + Vector3.UP * (rig.height + 0.6)
	var clear := PhysicsRayQueryParameters3D.create(top, top + fwd * 0.9, Combat.LAYER_WORLD)
	if not space.intersect_ray(clear).is_empty():
		return false
	var down := PhysicsRayQueryParameters3D.create(top + fwd * 0.7, top + fwd * 0.7 + Vector3.DOWN * (rig.height + 0.5), Combat.LAYER_WORLD)
	var hit := space.intersect_ray(down)
	return not hit.is_empty() and hit.position.y > global_position.y + 0.4


func _update_facing(delta: float) -> void:
	var target_yaw := rig.rotation.y
	if aim_lock > 0.0:
		target_yaw = cam.yaw
	elif wish != Vector3.ZERO:
		target_yaw = atan2(-wish.x, -wish.z)
	rig.rotation.y = lerp_angle(rig.rotation.y, target_yaw, 1.0 - exp(-14.0 * delta))
	cam.fov_boost = 8.0 if sprinting or sliding else 0.0


func _update_anim(delta: float) -> void:
	var v := Vector2(velocity.x, velocity.z).length()
	var ratio := v / move_speed
	var loco := ratio * 2.0 if ratio < 1.0 else 2.0 + clampf((ratio - 1.0) / (SPRINT_MULT - 1.0), 0.0, 1.0)
	if v < 0.25:
		loco = 0.0
	rig.set_locomotion(loco, not is_on_floor(), sliding, delta)


func _handle_actions(delta: float) -> void:
	if Input.is_action_pressed("primary"):
		kit.primary_input()
	if Input.is_action_just_pressed("secondary"):
		kit.trigger(1)
	if Input.is_action_just_pressed("utility"):
		kit.trigger(2)
	if Input.is_action_just_pressed("special"):
		kit.trigger(3)
	if Input.is_action_just_pressed("interact"):
		var t := look_interactable()
		if t:
			t.interact(self)


# -- damage, downed ---------------------------------------------------------------------------

func _on_damaged(dmg: Damage) -> void:
	if dmg.impulse != Vector3.ZERO:
		apply_knockback(dmg.impulse * 0.6)
	Combat.shake(clampf(dmg.amount / 40.0, 0.15, 0.7))
	if dmg.amount > health.max_hp * 0.12:
		rig.play_full("Hit_Chest", 1.3)


func _on_downed(_dmg: Damage) -> void:
	downed = true
	sliding = false
	sprinting = false
	rig.play_death()
	_downed_t = DOWNED_SECONDS
	toast.emit("Downed!")
	_bark("downed")


## Single-player stand-in for being carried to the Makeup Chair: back up after a few seconds.
func _downed_tick(delta: float) -> void:
	velocity.x = move_toward(velocity.x, 0.0, 20.0 * delta)
	velocity.z = move_toward(velocity.z, 0.0, 20.0 * delta)
	velocity.y -= GRAVITY * delta
	move_and_slide()
	_downed_t -= delta
	if _downed_t <= 0.0:
		downed = false
		health.dead = false
		health.hp = health.max_hp * 0.5
		health.invulnerable_until = Time.get_ticks_msec() / 1000.0 + 2.0
		rig.revive()
		toast.emit("Back on air.")
