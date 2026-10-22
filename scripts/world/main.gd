extends Node3D
## Test-arena bootstrap: arena, one local monster, HUD, director and DM.
##
## Dev keys: F1 swap monster (Imp / Puglin), F2 spawn a few adventurers in front of you.
## Command line (after `--`): --monster=puglin  --spawn=6  --autoshot=4  --shot=name  --bot
##   --nopost (no sketch pass)  --view=x,y,z,yaw,pitch (park the camera for a framed shot)
##   --autoshot saves out/<shot>.png after N seconds and quits (for visual checks).

var arena: TestStage
var cam: CameraRig
var player: Monster
var hud: Hud
var director: Director
var dm: DM

var _args := {}
var _bot := false
var _shot_t := -1.0


func _ready() -> void:
	for a in OS.get_cmdline_user_args():
		var kv := a.trim_prefix("--").split("=", true, 1)
		_args[kv[0]] = kv[1] if kv.size() > 1 else "1"
	Combat.world = self
	Game.phase = Game.Phase.DELVE
	Ratings.reset()

	arena = TestStage.new()
	add_child(arena)
	arena.build()

	cam = CameraRig.new()
	add_child(cam)
	dm = DM.new()
	add_child(dm)
	director = Director.new()
	add_child(director)
	director.setup(arena.spawn_points, dm, 1234)
	hud = Hud.new()
	add_child(hud)
	_spawn_player(_args.get("monster", "imp"))

	if _args.has("spawn"):
		director.enabled = false
		_spawn_in_front(int(_args.spawn))
	if _args.has("autoshot"):
		_shot_t = float(_args.autoshot)
		_bot = _args.has("bot")
	dm.say("open", 0.0, 5.0)
	var post: MeshInstance3D = cam.camera.get_node("SketchPost")
	if _args.has("nopost"):
		post.visible = false
	if _args.has("postdebug"):
		(post.material_override as ShaderMaterial).set_shader_parameter("debug_view", int(_args.postdebug))
	if _args.has("view"):
		# --view=x,y,z,yaw_deg,pitch_deg: park the player there for a framed screenshot
		var v: PackedFloat64Array = _args.view.split_floats(",")
		player.global_position = Vector3(v[0], v[1], v[2])
		cam.yaw = deg_to_rad(v[3])
		cam.pitch = deg_to_rad(v[4])
		director.enabled = false


func _spawn_player(id: String) -> void:
	var at := arena.player_spawn
	var yaw := 0.0
	if player:
		at = player.global_position
		yaw = cam.yaw
		Game.unregister_player(1)
		player.queue_free()
	player = Monster.new()
	add_child(player)
	player.global_position = at
	player.setup(id, cam)
	cam.yaw = yaw
	Game.register_player(1, player)
	player.add_gold(int(_args.get("gold", "0")))
	hud.bind(player, director, dm)
	dm.bark(id, "spawn", 0.0)


func _spawn_in_front(n: int) -> void:
	var fwd := cam.forward_flat()
	for i in n:
		var side := Vector3(-fwd.z, 0, fwd.x) * (i - n * 0.5) * 1.6
		var types := AdventurerTypes.TYPES.keys()
		director.spawn(types[i % types.size()], "veteran" if i == n - 1 and n > 3 else "", player.global_position + fwd * 9.0 + side + Vector3.UP * 0.3)


func _unhandled_input(event: InputEvent) -> void:
	if event.is_action_pressed("dev_swap"):
		_spawn_player("puglin" if player.monster_id == "imp" else "imp")
	elif event.is_action_pressed("dev_spawn"):
		_spawn_in_front(4)


func _process(delta: float) -> void:
	if _shot_t < 0.0:
		return
	if _bot:
		cam.yaw += delta * 0.15
		player.kit.primary_input()
	_shot_t -= delta
	if _shot_t <= 0.0:
		_shot_t = -1.0
		await RenderingServer.frame_post_draw
		var img := get_viewport().get_texture().get_image()
		DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path("res://out"))
		img.save_png(ProjectSettings.globalize_path("res://out/%s.png" % _args.get("shot", "shot")))
		get_tree().quit()
