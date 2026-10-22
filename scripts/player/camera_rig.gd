class_name CameraRig
extends Node3D
## Risk of Rain 2-style over-the-shoulder camera with a centre crosshair.
## Follows its target smoothly, collides with walls via a SpringArm, and shakes on "trauma".

const SENSITIVITY := 0.0022
const PITCH_MIN := -1.35
const PITCH_MAX := 1.2

var target: Node3D
var height := 1.6               ## pivot height above the target's origin
var shoulder := 0.75            ## right offset
var distance := 4.3
var yaw := 0.0
var pitch := -0.15
var fov_boost := 0.0            ## added while sprinting

var camera: Camera3D
var post: ShaderMaterial          ## the sketch / grade pass, tunable per level
var rim_light: DirectionalLight3D ## character-only backlight, tunable per level
var _arm: SpringArm3D
var _pivot: Node3D
var _trauma := 0.0
var _noise := FastNoiseLite.new()
var _t := 0.0


func _ready() -> void:
	top_level = true
	_pivot = Node3D.new()
	add_child(_pivot)
	_arm = SpringArm3D.new()
	_arm.spring_length = distance
	_arm.margin = 0.2
	_arm.collision_mask = Combat.LAYER_WORLD
	var s := SphereShape3D.new()
	s.radius = 0.25
	_arm.shape = s
	_pivot.add_child(_arm)
	camera = Camera3D.new()
	camera.fov = 75.0
	camera.far = 300.0
	_arm.add_child(camera)
	post = SketchPost.attach(camera)
	rim_light = DirectionalLight3D.new()
	rim_light.name = "RimLight"
	rim_light.light_color = Palette.TORCH
	rim_light.light_energy = 3.2
	rim_light.light_specular = 1.6
	rim_light.light_cull_mask = Rig.CHARACTER_LAYER
	rim_light.shadow_enabled = false
	rim_light.rotation = Vector3(deg_to_rad(-28.0), PI, 0.0)   # from behind and above, toward the camera
	_pivot.add_child(rim_light)
	# soft, painterly background
	var attrs := CameraAttributesPractical.new()
	attrs.dof_blur_far_enabled = true
	attrs.dof_blur_far_distance = 38.0
	attrs.dof_blur_far_transition = 30.0
	attrs.dof_blur_amount = 0.07
	camera.attributes = attrs
	_noise.frequency = 2.5
	Input.mouse_mode = Input.MOUSE_MODE_CAPTURED
	Combat.camera_rig = self


func setup(t: Node3D, h: float, dist: float) -> void:
	target = t
	height = h
	distance = dist
	if _arm:
		_arm.spring_length = dist
	if t is CollisionObject3D:
		_arm.add_excluded_object(t.get_rid())
	global_position = t.global_position + Vector3.UP * height
	yaw = t.global_rotation.y


func _input(event: InputEvent) -> void:
	if event is InputEventMouseMotion and Input.mouse_mode == Input.MOUSE_MODE_CAPTURED:
		yaw -= event.relative.x * SENSITIVITY
		pitch = clampf(pitch - event.relative.y * SENSITIVITY, PITCH_MIN, PITCH_MAX)
	elif event.is_action_pressed("pause"):
		Input.mouse_mode = Input.MOUSE_MODE_VISIBLE if Input.mouse_mode == Input.MOUSE_MODE_CAPTURED else Input.MOUSE_MODE_CAPTURED
	elif event is InputEventMouseButton and event.pressed and Input.mouse_mode != Input.MOUSE_MODE_CAPTURED:
		Input.mouse_mode = Input.MOUSE_MODE_CAPTURED


func add_trauma(amount: float) -> void:
	_trauma = minf(_trauma + amount, 1.0)


func _process(delta: float) -> void:
	if not is_instance_valid(target):
		return
	_t += delta
	var goal := target.global_position + Vector3.UP * height
	# snappy horizontally, a little lag vertically so jumps read
	var p := global_position
	p.x = goal.x
	p.z = goal.z
	p.y = lerpf(p.y, goal.y, 1.0 - exp(-14.0 * delta))
	global_position = p
	_pivot.rotation = Vector3(pitch, yaw, 0.0)
	_arm.position = Vector3(shoulder, 0.0, 0.0)
	_arm.spring_length = distance
	camera.fov = lerpf(camera.fov, 75.0 + fov_boost, 1.0 - exp(-8.0 * delta))
	_trauma = maxf(_trauma - delta * 1.6, 0.0)
	var s := _trauma * _trauma
	camera.rotation = Vector3(_noise.get_noise_2d(_t * 40.0, 0.0) * 0.06 * s, _noise.get_noise_2d(0.0, _t * 40.0) * 0.06 * s, _noise.get_noise_2d(_t * 40.0, 50.0) * 0.08 * s)


## World point under the crosshair (or far along the view ray).
func aim_point(exclude: Array[RID] = []) -> Vector3:
	var from := camera.global_position
	var dir := -camera.global_basis.z
	var q := PhysicsRayQueryParameters3D.create(from, from + dir * 200.0, Combat.LAYER_WORLD | Combat.LAYER_ENEMIES | Combat.LAYER_PROPS)
	q.exclude = exclude
	var r := get_world_3d().direct_space_state.intersect_ray(q)
	return r.position if r else from + dir * 200.0


## Raycast from the camera through the crosshair, for grabbing and interacting.
func look_ray(max_dist: float, mask: int, exclude: Array[RID] = []) -> Dictionary:
	var from := camera.global_position
	var q := PhysicsRayQueryParameters3D.create(from, from - camera.global_basis.z * max_dist, mask)
	q.exclude = exclude
	return get_world_3d().direct_space_state.intersect_ray(q)


func forward_flat() -> Vector3:
	return Vector3(-sin(yaw), 0.0, -cos(yaw))
