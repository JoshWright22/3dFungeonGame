class_name Decor
extends RefCounted
## Static scenery from the Fantasy Props MegaKit. Pieces big enough to matter get convex
## collision (on the world layer, so they block movement, shots and the navmesh); small clutter
## is visual only, so it never snags a player mid-sprint.

const DIR := "res://assets/quaternius/props/"
const SOLID_SIZE := 0.7          ## longest side (m) above which a piece gets collision

static var _shape_cache: Dictionary = {}


static func path(id: String) -> String:
	return DIR + id + ".gltf"


## Instance a piece. Returns a StaticBody3D for solid pieces, a plain Node3D otherwise.
## The piece's local AABB is stored as meta "aabb" for stacking and table-tops.
static func create(id: String) -> Node3D:
	var visual: Node3D = load(path(id)).instantiate()
	var aabb := AABB()
	var first := true
	for m: MeshInstance3D in visual.find_children("*", "MeshInstance3D", true, false):
		var box := _local_xform(visual, m) * m.get_aabb()
		aabb = box if first else aabb.merge(box)
		first = false
	if aabb.get_longest_axis_size() < SOLID_SIZE:
		visual.set_meta("aabb", aabb)
		return visual
	var body := StaticBody3D.new()
	body.collision_layer = Combat.LAYER_WORLD
	body.add_child(visual)
	if not _shape_cache.has(id):
		var shapes := []
		for m: MeshInstance3D in visual.find_children("*", "MeshInstance3D", true, false):
			shapes.append([m.mesh.create_convex_shape(true, true), _local_xform(visual, m)])
		_shape_cache[id] = shapes
	for pair in _shape_cache[id]:
		var cs := CollisionShape3D.new()
		cs.shape = pair[0]
		cs.transform = pair[1]
		body.add_child(cs)
	body.set_meta("aabb", aabb)
	return body


static func _local_xform(root: Node3D, m: Node3D) -> Transform3D:
	var xf := Transform3D.IDENTITY
	var n: Node = m
	while n != root and n is Node3D:
		xf = (n as Node3D).transform * xf
		n = n.get_parent()
	return xf
