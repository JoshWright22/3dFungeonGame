class_name Rig
extends Node3D
## A Quaternius humanoid (monster or adventurer) animated from the UAL through one AnimationTree:
##
##   move (blend space: idle/walk/jog/sprint) → air → slide → upper one-shot → full one-shot → dead
##
## The upper one-shot is filtered to the spine and above, so attacks play while the legs keep running
## (Risk of Rain 2 style). The full one-shot is for whole-body moves: rolls, pounces, hit reactions.
## The model is turned 180° so the rig's -Z is the character's forward, Godot convention.

## Render layer reserved for characters, so the camera's rim light touches only them.
const CHARACTER_LAYER := 1 << 19

static var _cine_mats: Dictionary = {}   # original material -> cinematic copy (shared)

var model: Node3D
var skeleton: Skeleton3D
var tree: AnimationTree
var height := 1.0             # model top in metres, after scale

var _root: AnimationNodeBlendTree
var _move := 0.0
var _air := 0.0
var _slide := 0.0
var _dead := false


## `model_path`: the base glTF. `extra_meshes`: glTFs on the same skeleton (outfits, hair) whose
## meshes are moved onto the base skeleton. `loco`: [idle, walk, jog, sprint] clip names.
func build(model_path: String, extra_meshes: Array = [], loco: Array = ["Idle", "Walk", "Jog_Fwd", "Sprint"], model_scale := 1.0) -> void:
	model = load(model_path).instantiate()
	model.rotation.y = PI
	model.scale = Vector3.ONE * model_scale
	add_child(model)
	skeleton = model.find_children("*", "Skeleton3D", true, false)[0]
	for path: String in extra_meshes:
		_graft(path)
	height = 0.0
	for m: MeshInstance3D in model.find_children("*", "MeshInstance3D", true, false):
		height = maxf(height, m.get_aabb().end.y * model_scale)
		_cinematic(m)
	_build_tree(loco)


## Cinematic character shading: on the rim-light layer, a touch glossier, with a material rim.
func _cinematic(m: MeshInstance3D) -> void:
	m.layers = 1 | CHARACTER_LAYER
	for s in m.mesh.get_surface_count():
		var src := m.get_active_material(s) as StandardMaterial3D
		if src == null:
			continue
		if not _cine_mats.has(src):
			var c: StandardMaterial3D = src.duplicate()
			c.rim_enabled = true
			c.rim = 0.6
			c.rim_tint = 0.65
			c.roughness = minf(c.roughness, 0.55)
			c.metallic_specular = 0.75
			_cine_mats[src] = c
		m.set_surface_override_material(s, _cine_mats[src])


func _graft(path: String) -> void:
	var extra: Node = load(path).instantiate()
	for m: MeshInstance3D in extra.find_children("*", "MeshInstance3D", true, false):
		m.owner = null
		m.get_parent().remove_child(m)
		skeleton.add_child(m)
		m.skeleton = NodePath("..")
	extra.free()


func _build_tree(loco: Array) -> void:
	tree = AnimationTree.new()
	tree.name = "AnimationTree"
	add_child(tree)
	tree.add_animation_library("", AnimLib.library_for(skeleton))
	tree.root_node = tree.get_path_to(model)

	_root = AnimationNodeBlendTree.new()
	var move := AnimationNodeBlendSpace1D.new()
	move.min_space = 0.0
	move.max_space = 3.0
	for i in loco.size():
		move.add_blend_point(_clip(loco[i]), float(i), -1, StringName(loco[i]))
	_root.add_node("move", move)
	_add_blend("air", "move", "Jump")
	_add_blend("slide", "air", "Slide")
	_add_oneshot("upper", "slide", true)
	_add_oneshot("full", "upper", false)
	_add_blend("dead", "full", "Death01")
	_root.connect_node("output", 0, "dead")
	tree.tree_root = _root
	tree.active = true


func _clip(anim_name: String) -> AnimationNodeAnimation:
	var a := AnimationNodeAnimation.new()
	a.animation = anim_name
	return a


func _add_blend(node_name: String, input: String, clip: String) -> void:
	_root.add_node(node_name + "_clip", _clip(clip))
	_root.add_node(node_name, AnimationNodeBlend2.new())
	_root.connect_node(node_name, 0, input)
	_root.connect_node(node_name, 1, node_name + "_clip")


func _add_oneshot(node_name: String, input: String, upper_body: bool) -> void:
	var shot := AnimationNodeOneShot.new()
	shot.fadein_time = 0.06
	shot.fadeout_time = 0.18
	if upper_body:
		shot.filter_enabled = true
		var spine := skeleton.find_bone("spine_01")
		for b in skeleton.get_bone_count():
			if _descends_from(b, spine):
				shot.set_filter_path(NodePath("%s:%s" % [AnimLib.SKELETON_PATH, skeleton.get_bone_name(b)]), true)
	_root.add_node(node_name + "_clip", _clip("Idle"))
	_root.add_node(node_name + "_speed", AnimationNodeTimeScale.new())
	_root.connect_node(node_name + "_speed", 0, node_name + "_clip")
	_root.add_node(node_name, shot)
	_root.connect_node(node_name, 0, input)
	_root.connect_node(node_name, 1, node_name + "_speed")


func _descends_from(bone: int, ancestor: int) -> bool:
	while bone >= 0:
		if bone == ancestor:
			return true
		bone = skeleton.get_bone_parent(bone)
	return false


# -- driving ---------------------------------------------------------------------------------

## speed: 0 idle, 1 walk, 2 jog, 3 sprint (blended). Call every frame.
func set_locomotion(speed: float, airborne: bool, sliding: bool, delta: float) -> void:
	_move = lerpf(_move, speed, 1.0 - exp(-12.0 * delta))
	_air = move_toward(_air, 1.0 if airborne else 0.0, delta * 6.0)
	_slide = move_toward(_slide, 1.0 if sliding else 0.0, delta * 8.0)
	tree.set("parameters/move/blend_position", _move)
	tree.set("parameters/air/blend_amount", _air)
	tree.set("parameters/slide/blend_amount", _slide)


## Upper-body action (attacks, throws, casts) over whatever the legs are doing.
func play_upper(clip: String, speed := 1.0) -> void:
	_fire("upper", clip, speed)


## Whole-body action (roll, pounce, hit reaction).
func play_full(clip: String, speed := 1.0) -> void:
	_fire("full", clip, speed)


func stop_actions() -> void:
	tree.set("parameters/upper/request", AnimationNodeOneShot.ONE_SHOT_REQUEST_FADE_OUT)
	tree.set("parameters/full/request", AnimationNodeOneShot.ONE_SHOT_REQUEST_FADE_OUT)


func clip_length(clip: String) -> float:
	return tree.get_animation(clip).length if tree.has_animation(clip) else 0.5


## Swap the base-colour texture on every mesh that uses the model's main material (Bestiary
## colour variants). Materials are duplicated so other instances keep their skin.
func set_skin(texture_path: String) -> void:
	if not ResourceLoader.exists(texture_path):
		return
	var tex: Texture2D = load(texture_path)
	for m: MeshInstance3D in model.find_children("*", "MeshInstance3D", true, false):
		for s in m.mesh.get_surface_count():
			var mat := m.get_active_material(s) as StandardMaterial3D
			if mat and mat.albedo_texture:
				var key := "%s|%s" % [mat.get_instance_id(), texture_path]
				if not _cine_mats.has(key):
					var c: StandardMaterial3D = mat.duplicate()
					c.albedo_texture = tex
					_cine_mats[key] = c
				m.set_surface_override_material(s, _cine_mats[key])


## Every mesh in the model, for highlights and tints.
func meshes() -> Array[MeshInstance3D]:
	var out: Array[MeshInstance3D] = []
	out.assign(model.find_children("*", "MeshInstance3D", true, false))
	return out


func revive() -> void:
	_dead = false
	tree.set("parameters/dead/blend_amount", 0.0)
	play_full("LayToIdle", 1.4)


## Stop animating and hold the current pose (corpses).
func freeze_pose() -> void:
	tree.active = false


func play_death() -> void:
	if _dead:
		return
	_dead = true
	stop_actions()
	var t := create_tween()
	t.tween_method(func(v): tree.set("parameters/dead/blend_amount", v), 0.0, 1.0, 0.15)


func _fire(shot: String, clip: String, speed: float) -> void:
	if not tree.has_animation(clip):
		push_warning("Rig: no clip '%s'" % clip)
		return
	(_root.get_node(shot + "_clip") as AnimationNodeAnimation).animation = clip
	tree.set("parameters/%s_speed/scale" % shot, speed)
	tree.set("parameters/%s/request" % shot, AnimationNodeOneShot.ONE_SHOT_REQUEST_FIRE)
