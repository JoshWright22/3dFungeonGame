extends SceneTree
## Dev probe: prints node trees and animation names of imported assets.
## godot --headless --path . --script tools/probe/inspect.gd -- res://path.glb ...

func _init() -> void:
	for path in OS.get_cmdline_user_args():
		var scene: PackedScene = load(path)
		var root := scene.instantiate()
		print("== ", path)
		_dump(root, 0)
		for ap in root.find_children("*", "AnimationPlayer", true, false):
			print("  anims: ", ap.get_animation_list().size(), " ", ap.get_animation_list().slice(0, 200))
			var a: Animation = ap.get_animation(ap.get_animation_list()[0])
			print("  track0: ", a.track_get_path(0), " len ", a.length)
		root.free()
	quit()

func _dump(n: Node, d: int) -> void:
	if d > 4: return
	var extra := ""
	if n is Skeleton3D: extra = " bones=%d" % n.get_bone_count()
	if n is MeshInstance3D: extra = " aabb=%s" % n.get_aabb().size
	print("  ".repeat(d), n.name, " <", n.get_class(), ">", extra)
	for c in n.get_children(): _dump(c, d + 1)
