class_name SketchPost
extends RefCounted
## Attaches the Dark and Darker-style ink-sketch post-process (shaders/sketch_post.gdshader) to a
## camera, as a full-screen quad. Levels can tune it per painting via the returned material.

static func attach(camera: Camera3D) -> ShaderMaterial:
	var quad := MeshInstance3D.new()
	quad.name = "SketchPost"
	var mesh := QuadMesh.new()
	mesh.size = Vector2(2, 2)
	mesh.flip_faces = true
	quad.mesh = mesh
	var mat := ShaderMaterial.new()
	mat.shader = load("res://shaders/sketch_post.gdshader")
	mat.render_priority = 127
	quad.material_override = mat
	quad.extra_cull_margin = 16384.0
	quad.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	camera.add_child(quad)
	return mat
