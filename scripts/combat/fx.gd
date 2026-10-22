class_name Fx
extends RefCounted
## Cheap procedural particle effects. Everything here is a CPUParticles3D built in code.

static var _mats: Dictionary = {}


static func _mat(color: Color, additive: bool) -> StandardMaterial3D:
	var key := "%s%s" % [color.to_html(), additive]
	if _mats.has(key):
		return _mats[key]
	var m := StandardMaterial3D.new()
	m.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	m.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
	m.blend_mode = BaseMaterial3D.BLEND_MODE_ADD if additive else BaseMaterial3D.BLEND_MODE_MIX
	m.billboard_mode = BaseMaterial3D.BILLBOARD_PARTICLES
	m.vertex_color_use_as_albedo = true
	m.albedo_color = color
	_mats[key] = m
	return m


static func _particles(color: Color, additive := true, size := 0.25) -> CPUParticles3D:
	var p := CPUParticles3D.new()
	var q := QuadMesh.new()
	q.size = Vector2(size, size)
	q.material = _mat(color, additive)
	p.mesh = q
	var ramp := Gradient.new()
	ramp.set_color(0, Color(1, 1, 1, 1))
	ramp.set_color(1, Color(1, 1, 1, 0))
	p.color_ramp = ramp
	return p


## Looping flames, attached to a burning thing.
static func flames(radius: float) -> Node3D:
	var p := _particles(Color(1.0, 0.45, 0.1), true, 0.35)
	p.amount = 24
	p.lifetime = 0.6
	p.emission_shape = CPUParticles3D.EMISSION_SHAPE_SPHERE
	p.emission_sphere_radius = radius * 0.4
	p.direction = Vector3.UP
	p.spread = 15.0
	p.gravity = Vector3(0, 3.0, 0)
	p.initial_velocity_min = 0.5
	p.initial_velocity_max = 1.5
	p.scale_amount_min = 0.6
	p.scale_amount_max = 1.4
	var light := OmniLight3D.new()
	light.light_color = Color(1.0, 0.5, 0.2)
	light.light_energy = 1.5
	light.omni_range = 4.0
	p.add_child(light)
	return p


## One-shot burst at a world position; frees itself.
static func burst(pos: Vector3, color: Color, amount := 24, speed := 5.0, size := 0.2, lifetime := 0.5) -> void:
	if Combat.world == null:
		return
	var p := _particles(color, true, size)
	p.one_shot = true
	p.explosiveness = 0.95
	p.amount = amount
	p.lifetime = lifetime
	p.spread = 180.0
	p.initial_velocity_min = speed * 0.4
	p.initial_velocity_max = speed
	p.gravity = Vector3(0, -6, 0)
	Combat.world.add_child(p)
	p.global_position = pos
	p.emitting = true
	p.finished.connect(p.queue_free)


## A flash of light, faded out.
static func flash(pos: Vector3, color: Color, energy := 4.0, light_range := 8.0, duration := 0.3) -> void:
	if Combat.world == null:
		return
	var l := OmniLight3D.new()
	l.light_color = color
	l.light_energy = energy
	l.omni_range = light_range
	Combat.world.add_child(l)
	l.global_position = pos
	var t := l.create_tween()
	t.tween_property(l, "light_energy", 0.0, duration)
	t.tween_callback(l.queue_free)
