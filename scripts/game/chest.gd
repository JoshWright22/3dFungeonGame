class_name Chest
extends StaticBody3D
## The dungeon's own treasure. Costs gold (scales with difficulty), gives a relic.

var cost := 25
var opened := false
var _rng := RandomNumberGenerator.new()
var _visual: Node3D


func _ready() -> void:
	add_to_group("interactable")
	add_to_group("chests")
	collision_layer = Combat.LAYER_WORLD
	_visual = load(Decor.path("Chest_Wood")).instantiate()
	add_child(_visual)
	var cs := CollisionShape3D.new()
	var box := BoxShape3D.new()
	box.size = Vector3(1.3, 0.9, 0.85)
	cs.shape = box
	cs.position.y = 0.45
	add_child(cs)
	var glint := OmniLight3D.new()
	glint.light_color = Palette.GOLD
	glint.light_energy = 0.6
	glint.omni_range = 2.5
	glint.position.y = 1.0
	glint.name = "Glint"
	add_child(glint)
	_rng.seed = hash(global_position)


func prompt() -> String:
	return "" if opened else "Open chest  $%d" % cost


func interact(m: Monster) -> void:
	if opened:
		return
	if m.gold < cost:
		m.toast.emit("Need $%d" % cost)
		return
	m.add_gold(-cost)
	opened = true
	get_node("Glint").queue_free()
	var t := create_tween()
	t.tween_property(_visual, "scale", Vector3(1.15, 0.8, 1.15), 0.08)
	t.tween_property(_visual, "scale", Vector3.ONE, 0.2).set_trans(Tween.TRANS_ELASTIC)
	var id := Relics.roll(_rng)
	Fx.burst(global_position + Vector3.UP, Relics.color(id), 40, 5.0, 0.2, 0.8)
	Fx.flash(global_position + Vector3.UP, Relics.color(id), 4.0, 6.0, 0.6)
	_fly_relic(id, m)


func _fly_relic(id: String, m: Monster) -> void:
	var orb := MeshInstance3D.new()
	var s := SphereMesh.new()
	s.radius = 0.18
	s.height = 0.36
	var mat := StandardMaterial3D.new()
	mat.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	mat.albedo_color = Relics.color(id)
	s.material = mat
	orb.mesh = s
	get_parent().add_child(orb)
	orb.global_position = global_position + Vector3.UP * 1.2
	var t := orb.create_tween()
	t.tween_property(orb, "global_position:y", orb.global_position.y + 1.2, 0.35).set_ease(Tween.EASE_OUT)
	t.tween_method(_home_orb.bind(orb, m), 0.0, 1.0, 0.45)
	t.tween_callback(_give_relic.bind(orb, m, id))


func _home_orb(k: float, orb: Node3D, m: Monster) -> void:
	if is_instance_valid(m):
		orb.global_position = orb.global_position.lerp(m.chest(), k)


func _give_relic(orb: Node3D, m: Monster, id: String) -> void:
	orb.queue_free()
	if is_instance_valid(m):
		m.add_relic(id)
		m.toast.emit("%s: %s" % [Relics.ALL[id].name, Relics.ALL[id].desc])
