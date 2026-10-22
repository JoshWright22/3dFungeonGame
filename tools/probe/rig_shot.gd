extends SceneTree
## Dev probe: renders the rigs side by side playing a clip, saves out/rig_shot.png.
## godot --path . --script tools/probe/rig_shot.gd -- [clip]

var _frames := 0
var _rigs: Array = []

func _init() -> void:
	var clip: String = OS.get_cmdline_user_args()[0] if OS.get_cmdline_user_args().size() > 0 else "Jog_Fwd"
	var world := Node3D.new()
	root.add_child(world)
	var env := WorldEnvironment.new()
	env.environment = Environment.new()
	env.environment.background_mode = Environment.BG_COLOR
	env.environment.background_color = Color(0.25, 0.25, 0.28)
	env.environment.ambient_light_color = Color(0.6, 0.6, 0.65)
	env.environment.ambient_light_energy = 0.6
	world.add_child(env)
	var sun := DirectionalLight3D.new()
	sun.rotation_degrees = Vector3(-40, -30, 0)
	world.add_child(sun)
	var cam := Camera3D.new()
	cam.position = Vector3(0, 1.1, 4.2)
	cam.rotation_degrees.x = -6
	world.add_child(cam)
	var specs := [
		["res://assets/quaternius/bestiary/Imp.glb", []],
		["res://assets/quaternius/bestiary/Puglin.glb", []],
		["res://assets/quaternius/characters/Superhero_Male_FullBody.gltf", ["res://assets/quaternius/outfits/Male_Peasant.gltf"]],
		["res://assets/quaternius/characters/Superhero_Female_FullBody.gltf", ["res://assets/quaternius/outfits/Female_Ranger.gltf"]],
	]
	for i in specs.size():
		var rig := Rig.new()
		world.add_child(rig)
		rig.build(specs[i][0], specs[i][1])
		rig.position = Vector3(-2.4 + i * 1.6, 0, 0)
		rig.rotation.y = PI * 0.15   # three-quarter view toward camera (+Z)
		rig.rotation.y += PI
		_rigs.append(rig)
		print(specs[i][0].get_file(), " height=", rig.height)
	for r in _rigs:
		r.set_locomotion(2.0, false, false, 1.0)
		if clip != "Jog_Fwd":
			r.play_full(clip)


func _process(_delta: float) -> bool:
	_frames += 1
	if _frames == 40:
		var img := root.get_viewport().get_texture().get_image()
		DirAccess.make_dir_recursive_absolute("res://out")
		img.save_png("res://out/rig_shot.png")
		print("saved")
		return true
	return false
