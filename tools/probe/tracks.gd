extends SceneTree
## Dev probe: track types of one UAL animation, and bone rest heights of a model.

func _init() -> void:
	var ual: Node = load("res://assets/quaternius/animations/ual1/UAL1_Standard.glb").instantiate()
	var ap: AnimationPlayer = ual.find_children("*", "AnimationPlayer", true, false)[0]
	var a := ap.get_animation("Jog_Fwd")
	var kinds := {}
	for i in a.get_track_count():
		var k := "%d" % a.track_get_type(i)
		kinds[k] = kinds.get(k, 0) + 1
		if a.track_get_type(i) == Animation.TYPE_POSITION_3D:
			print("pos track: ", a.track_get_path(i))
	print("track types: ", kinds)
	for path in ["res://assets/quaternius/animations/ual1/UAL1_Standard.glb", "res://assets/quaternius/bestiary/Imp.glb", "res://assets/quaternius/bestiary/Puglin.glb"]:
		var r: Node = load(path).instantiate()
		var sk: Skeleton3D = r.find_children("*", "Skeleton3D", true, false)[0]
		var p := sk.find_bone("pelvis")
		print(path.get_file(), " pelvis rest y=", sk.get_bone_global_rest(p).origin.y, " head y=", sk.get_bone_global_rest(sk.find_bone("Head")).origin.y, " skel scale=", sk.global_transform.basis.get_scale() if sk.is_inside_tree() else sk.transform.basis.get_scale(), " armature=", sk.get_parent().transform.basis.get_scale())
		r.free()
	ual.free()
	quit()
