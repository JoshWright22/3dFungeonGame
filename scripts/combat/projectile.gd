class_name Projectile
extends Node3D
## A thing in flight: fireballs, thrown knives, spells. Moves itself, raycasts each physics step
## along its path, and either explodes (radius > 0) or hits one target.

var source: Node3D
var velocity := Vector3.ZERO
var gravity := 0.0
var damage := 10.0
var radius := 0.0                ## > 0: explodes
var knockback := 6.0
var ignite := 0.0
var team_mask := Combat.LAYER_ENEMIES
var kind := Damage.Kind.PROJECTILE
var color := Color.WHITE
var life := 4.0


## Explosive, igniting ball of fire (Imp's Hellfire, adventurer wizards with a different colour).
static func fireball(src: Node3D, from: Vector3, dir: Vector3, dmg: float, col: Color, mask: int, speed := 32.0, blast := 3.2) -> Projectile:
	var p := Projectile.new()
	p.source = src
	p.velocity = dir * speed
	p.damage = dmg
	p.radius = blast
	p.ignite = 3.0
	p.knockback = 9.0
	p.team_mask = mask
	p.kind = Damage.Kind.FIRE
	p.color = col
	var core := MeshInstance3D.new()
	var sphere := SphereMesh.new()
	sphere.radius = 0.22
	sphere.height = 0.44
	var mat := StandardMaterial3D.new()
	mat.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	mat.albedo_color = col.lightened(0.4)
	mat.emission_enabled = true
	mat.emission = col
	mat.emission_energy_multiplier = 4.0
	sphere.material = mat
	core.mesh = sphere
	p.add_child(core)
	var fl := Fx.flames(0.5)
	p.add_child(fl)
	Combat.world.add_child(p)
	p.global_position = from
	return p


## A spinning prop model thrown as a projectile (ranger knives).
static func thrown_model(src: Node3D, from: Vector3, dir: Vector3, dmg: float, prop_id: String, mask: int, speed := 26.0) -> Projectile:
	var p := Projectile.new()
	p.source = src
	p.velocity = dir * speed
	p.gravity = 4.0
	p.damage = dmg
	p.team_mask = mask
	p.knockback = 3.0
	var model: Node3D = load(Decor.path(prop_id)).instantiate()
	model.scale = Vector3.ONE * 1.6
	p.add_child(model)
	Combat.world.add_child(p)
	p.global_position = from
	return p


func _physics_process(delta: float) -> void:
	life -= delta
	if life <= 0.0:
		queue_free()
		return
	velocity.y -= gravity * delta
	var from := global_position
	var to := from + velocity * delta
	var q := PhysicsRayQueryParameters3D.create(from, to, Combat.LAYER_WORLD | team_mask)
	if is_instance_valid(source) and source is CollisionObject3D:
		q.exclude = [(source as CollisionObject3D).get_rid()]
	var hit := get_world_3d().direct_space_state.intersect_ray(q)
	if hit.is_empty():
		global_position = to
		if radius <= 0.0 and velocity.length() > 0.1:
			rotate_object_local(Vector3.RIGHT, 18.0 * delta)
		return
	_impact(hit.position, hit.collider)


func _impact(pos: Vector3, collider: Node) -> void:
	var src: Node3D = source if is_instance_valid(source) else self
	if radius > 0.0:
		Combat.hit_sphere(src, pos, radius, damage, knockback, team_mask, kind, ignite)
		Fx.burst(pos, color, 40, 7.0, 0.3, 0.5)
		Fx.flash(pos, color, 5.0, 9.0, 0.35)
		Combat.shake(0.25)
	else:
		var h := Health.of(collider)
		if h and not h.dead and collider is CollisionObject3D and ((collider as CollisionObject3D).collision_layer & team_mask):
			var d := Damage.make(damage, src, pos, velocity.normalized() * knockback, kind)
			h.take(d)
		Fx.burst(pos, Color(0.9, 0.85, 0.7), 8, 3.0, 0.1, 0.25)
	queue_free()
