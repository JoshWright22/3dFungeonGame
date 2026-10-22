class_name Highlight
extends RefCounted
## See-through-walls glow (Puglin's Sniff): an unshaded, depth-test-off overlay on every mesh.

static var _mats: Dictionary = {}


static func apply(node: Node, color: Color, seconds: float) -> void:
	var mat: StandardMaterial3D = _mats.get(color.to_html())
	if mat == null:
		mat = StandardMaterial3D.new()
		mat.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
		mat.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
		mat.no_depth_test = true
		mat.albedo_color = Color(color, 0.35)
		mat.render_priority = 10
		_mats[color.to_html()] = mat
	var meshes := node.find_children("*", "MeshInstance3D", true, false)
	for mi: MeshInstance3D in meshes:
		mi.material_overlay = mat
	await node.get_tree().create_timer(seconds, false).timeout
	for mi: MeshInstance3D in meshes:
		if is_instance_valid(mi) and mi.material_overlay == mat:
			mi.material_overlay = null
