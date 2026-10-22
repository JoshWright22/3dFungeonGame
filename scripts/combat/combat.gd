class_name Combat
extends RefCounted
## Shared combat helpers: area hits, hit-stop, camera shake, damage numbers.

const LAYER_WORLD := 1
const LAYER_PROPS := 2
const LAYER_PLAYERS := 4
const LAYER_ENEMIES := 8
const LAYER_HELD := 16

static var world: Node3D          ## where transient effects are parented; set by Main
static var camera_rig: Node       ## local CameraRig, for shake

static var _hitstop_until := 0.0


## Hit everything with Health inside a sphere, once each.
## `team_mask` picks who can be hurt (LAYER_ENEMIES for monsters' attacks, LAYER_PLAYERS for adventurers').
## Returns the Health components hit.
static func hit_sphere(attacker: Node3D, center: Vector3, radius: float, amount: float, knockback: float,
		team_mask: int, kind := Damage.Kind.MELEE, ignite := 0.0, weapon: Node = null) -> Array[Health]:
	var hits: Array[Health] = []
	var space := attacker.get_world_3d().direct_space_state
	var q := PhysicsShapeQueryParameters3D.new()
	var s := SphereShape3D.new()
	s.radius = radius
	q.shape = s
	q.transform = Transform3D(Basis(), center)
	q.collision_mask = team_mask
	q.exclude = [attacker.get_rid()] if attacker is CollisionObject3D else []
	for r in space.intersect_shape(q, 32):
		var col: Node3D = r.collider
		var away := (col.global_position - attacker.global_position)
		away.y = 0.0
		away = away.normalized() if away.length() > 0.01 else -attacker.global_basis.z
		var h := Health.of(col)
		if h == null or h.dead or hits.has(h):
			continue
		if not (col.collision_layer & team_mask):
			continue
		var d := Damage.make(amount, attacker, col.global_position + Vector3.UP, (away + Vector3.UP * 0.35) * knockback, kind)
		d.ignite = ignite
		d.weapon = weapon
		h.take(d)
		hits.append(h)
	return hits


## Freeze the game for a moment on a heavy hit. Real-time, so it can't stack into a slowdown.
static func hitstop(seconds: float) -> void:
	var now := Time.get_ticks_msec() / 1000.0
	if now < _hitstop_until or world == null:
		return
	_hitstop_until = now + seconds
	Engine.time_scale = 0.05
	await world.get_tree().create_timer(seconds, true, false, true).timeout
	Engine.time_scale = 1.0


static func shake(strength: float) -> void:
	if camera_rig and is_instance_valid(camera_rig):
		camera_rig.add_trauma(strength)


static func damage_number(dmg: Damage, target: Node) -> void:
	if world == null or dmg.amount < 0.5:
		return
	var l := Label3D.new()
	l.text = str(roundi(dmg.amount))
	l.billboard = BaseMaterial3D.BILLBOARD_ENABLED
	l.no_depth_test = true
	l.fixed_size = true
	l.pixel_size = 0.0012
	l.font_size = 40 if dmg.amount < 40 else 60
	l.outline_size = 10
	l.modulate = Color(1.0, 0.55, 0.2) if dmg.kind == Damage.Kind.FIRE else (Color(1, 0.3, 0.3) if target is Monster else Color(1, 1, 0.85))
	world.add_child(l)
	l.global_position = dmg.point + Vector3(randf_range(-0.3, 0.3), 0.3, randf_range(-0.3, 0.3))
	var t := l.create_tween().set_parallel()
	t.tween_property(l, "global_position:y", l.global_position.y + 1.0, 0.7).set_ease(Tween.EASE_OUT)
	t.tween_property(l, "modulate:a", 0.0, 0.7).set_delay(0.25)
	t.chain().tween_callback(l.queue_free)
