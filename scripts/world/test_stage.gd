class_name TestStage
extends Node3D
## An open test stage in the shape of a Risk of Rain 2 map, standing in until the painted levels
## exist: a 200 m basin under the night sky, rolling ground, raised mesas with rock cliffs, a rim
## that bounds the map, brick ruins with fires, dead trees, chests spread wide, and clutter to throw.
## Everything is seeded.

const SIZE := 200                 ## metres, square
const RES := 1.0                  ## heightmap spacing (HeightMapShape3D uses 1 unit spacing)
const RIM := 18.0                 ## width of the rising border
const VILLAGE := "res://assets/quaternius/village/"
const NATURE := "res://assets/quaternius/nature/"

var player_spawn := Vector3.ZERO
var spawn_points: Array[Node3D] = []   ## empty: the director spawns around players instead

var nav: NavigationRegion3D
var _rng := RandomNumberGenerator.new()
var _noise := FastNoiseLite.new()
var _mesas: Array = []            ## [center Vector2, radius, height]
var _ruins: Array[Vector3] = []
var _footprints: Array = []        ## [centre, yaw, half-width] of each ruin plinth
var _camps: Array[Vector2] = []
var _dress: Dressing
var _nature: Callable
var _heights := PackedFloat32Array()
var _brick: StandardMaterial3D


func build(seed_value := 11) -> void:
	_rng.seed = seed_value
	_noise.seed = seed_value
	_noise.frequency = 0.012
	_noise.fractal_octaves = 4
	_brick = _triplanar("T_UnevenBrick")

	nav = NavigationRegion3D.new()
	var nm := NavigationMesh.new()
	nm.geometry_parsed_geometry_type = NavigationMesh.PARSED_GEOMETRY_STATIC_COLLIDERS
	nm.geometry_collision_mask = Combat.LAYER_WORLD
	nm.cell_size = 0.5
	nm.cell_height = 0.25
	nm.agent_radius = 0.5
	nm.agent_height = 1.75
	nm.agent_max_climb = 0.5
	nm.agent_max_slope = 42.0
	nm.filter_baking_aabb = AABB(Vector3(-SIZE * 0.5 + RIM * 0.5, -20, -SIZE * 0.5 + RIM * 0.5), Vector3(SIZE - RIM, 80, SIZE - RIM))
	nav.navigation_mesh = nm
	add_child(nav)
	NavigationServer3D.map_set_cell_size(get_world_3d().navigation_map, 0.5)
	NavigationServer3D.map_set_cell_height(get_world_3d().navigation_map, 0.25)

	_dress = Dressing.new(self, _rng, dress_height, _fire)
	_nature = _nature_model
	_environment()
	_plan_mesas()
	_terrain()
	_ruins_pass()
	_rocks_and_trees()
	_campfires()
	_chests()
	_clutter()
	player_spawn = Vector3(0, height_at(0, 30) + 0.5, 30)
	nav.bake_navigation_mesh(false)


# -- terrain --------------------------------------------------------------------------------------

func _plan_mesas() -> void:
	for i in 6:
		var a := _rng.randf() * TAU
		var d := _rng.randf_range(35.0, 70.0)
		_mesas.append([Vector2(cos(a), sin(a)) * d, _rng.randf_range(10.0, 20.0), _rng.randf_range(5.0, 11.0)])


## Ground height at world x, z.
func height_at(x: float, z: float) -> float:
	var h := _noise.get_noise_2d(x, z) * 4.0
	# a shallow bowl so the middle is the arena and the edges look down on it
	var r := Vector2(x, z).length() / (SIZE * 0.5)
	h += r * r * 4.0
	for m in _mesas:
		var d: float = Vector2(x, z).distance_to(m[0])
		var radius: float = m[1]
		h += float(m[2]) * (1.0 - smoothstep(radius - 3.0, radius + 1.5, d))
	# the rim: rises steeply into cliffs that bound the map
	var edge := SIZE * 0.5 - maxf(absf(x), absf(z))
	h += pow(clampf(1.0 - edge / RIM, 0.0, 1.0), 2.0) * 30.0
	return h


func _terrain() -> void:
	var n := int(SIZE / RES) + 1
	_heights.resize(n * n)
	var half := SIZE * 0.5
	for zi in n:
		for xi in n:
			_heights[zi * n + xi] = height_at(-half + xi * RES, -half + zi * RES)

	var st := SurfaceTool.new()
	st.begin(Mesh.PRIMITIVE_TRIANGLES)
	for zi in n - 1:
		for xi in n - 1:
			var p00 := Vector3(-half + xi * RES, _heights[zi * n + xi], -half + zi * RES)
			var p10 := Vector3(-half + (xi + 1) * RES, _heights[zi * n + xi + 1], -half + zi * RES)
			var p01 := Vector3(-half + xi * RES, _heights[(zi + 1) * n + xi], -half + (zi + 1) * RES)
			var p11 := Vector3(-half + (xi + 1) * RES, _heights[(zi + 1) * n + xi + 1], -half + (zi + 1) * RES)
			for p in [p00, p10, p11, p00, p11, p01]:
				st.add_vertex(p)
	st.generate_normals()
	var mesh := st.commit()
	var mi := MeshInstance3D.new()
	mi.mesh = mesh
	var mat := ShaderMaterial.new()
	mat.shader = load("res://shaders/terrain.gdshader")
	var nt := NoiseTexture2D.new()
	nt.seamless = true
	nt.width = 512
	nt.height = 512
	nt.noise = FastNoiseLite.new()
	nt.noise.frequency = 0.02
	mat.set_shader_parameter("noise", nt)
	mat.set_shader_parameter("moss", Color("2b3020"))
	mat.set_shader_parameter("dirt", Palette.TIMBER)
	mat.set_shader_parameter("stone", Palette.STONE_DARK)
	mat.set_shader_parameter("stone_light", Palette.STONE)
	mi.material_override = mat
	add_child(mi)

	var body := StaticBody3D.new()
	body.collision_layer = Combat.LAYER_WORLD
	nav.add_child(body)
	var cs := CollisionShape3D.new()
	var hm := HeightMapShape3D.new()
	hm.map_width = n
	hm.map_depth = n
	hm.map_data = _heights
	cs.shape = hm
	body.add_child(cs)


# -- dressing ---------------------------------------------------------------------------------------

## Brick ruins: broken wall runs, an arch, a raised floor with stairs, a fire. Each one is a
## landmark and a prop cache.
func _ruins_pass() -> void:
	var spots: Array[Vector2] = [Vector2(0, 0)]
	for m in _mesas.slice(0, 3):
		spots.append(m[0])
	while spots.size() < 9:
		var p := Vector2(_rng.randf_range(-70, 70), _rng.randf_range(-70, 70))
		if spots.all(func(s: Vector2): return s.distance_to(p) > 28.0):
			spots.append(p)
	for s in spots:
		_ruin(s, s == Vector2.ZERO)


func _ruin(c: Vector2, big: bool) -> void:
	var y := height_at(c.x, c.y)
	var base := Vector3(c.x, y, c.y)
	_ruins.append(base)
	var yaw := _rng.randf() * TAU
	var basis := Basis(Vector3.UP, yaw)
	var w := 6 if big else _rng.randi_range(3, 5)
	_footprints.append([base, yaw, float(w)])
	# a floor plinth, so the ruin sits on level ground
	var plinth := _box(base + Vector3.UP * 0.2, Vector3(w * 2 + 1, 1.4, w * 2 + 1), basis)
	plinth.position.y = y - 0.4
	# broken walls on two or three sides
	for side in 4:
		if _rng.randf() < 0.3 and not big:
			continue
		var dirs: Array[Vector3] = [Vector3.FORWARD, Vector3.RIGHT, Vector3.BACK, Vector3.LEFT]
		var dir: Vector3 = basis * dirs[side]
		var along: Vector3 = basis * dirs[(side + 1) % 4]
		for i in w:
			if _rng.randf() < 0.25:
				continue   # gaps to run through
			var p := base + Vector3.UP * 0.3 + dir * w + along * (i * 2.0 - w + 1.0)
			var storeys := 1 if _rng.randf() < 0.6 else 2
			for s in storeys:
				_kit("Wall_UnevenBrick_Straight", p + Vector3.UP * s * 3.12, Basis(Vector3.UP, yaw + side * PI * -0.5) )
			_box(p + Vector3.UP * storeys * 1.56, Vector3(2.0, storeys * 3.12, 0.5), Basis(Vector3.UP, yaw + side * PI * -0.5), false)
	# inside: a hearth and two or three sets in the corners, the middle kept clear to fight in
	_fire(base + Vector3.UP * 0.3, 3.0 if big else 2.2, 16.0 if big else 12.0)
	var kinds := ["dining", "study", "storage", "smithy", "apothecary"]
	var corners := [Vector3(-1, 0, -1), Vector3(1, 0, -1), Vector3(1, 0, 1), Vector3(-1, 0, 1)]
	corners.shuffle()
	for i in (3 if big else 2):
		var corner: Vector3 = basis * (corners[i] * (w - 2.2))
		_dress.place(kinds[_rng.randi() % kinds.size()], base + corner, yaw + _rng.randi_range(0, 3) * PI * 0.5)
	if big:
		player_spawn = base + Vector3.UP + basis * Vector3(0, 0, 3)


## Ground height for dressing: the ruin plinths are level floors above the terrain.
func dress_height(x: float, z: float) -> float:
	for f in _footprints:
		var local: Vector3 = Basis(Vector3.UP, f[1]).inverse() * (Vector3(x, 0, z) - Vector3(f[0].x, 0, f[0].z))
		if absf(local.x) <= f[2] + 0.5 and absf(local.z) <= f[2] + 0.5:
			return f[0].y + 0.3
	return height_at(x, z)


func _rocks_and_trees() -> void:
	# cliff outcrops around mesa edges: boulder, medium rocks, pebbles
	for m in _mesas:
		var center: Vector2 = m[0]
		var radius: float = m[1]
		for i in int(radius / 4.0):
			var a := TAU * i / int(radius / 4.0) + _rng.randf_range(-0.3, 0.3)
			var p := center + Vector2(cos(a), sin(a)) * (radius + _rng.randf_range(-1.0, 1.0))
			_dress.rocks(Vector3(p.x, height_at(p.x, p.y) - 1.0, p.y), _rng.randf_range(2.4, 3.4), _nature)
	# groves and outcrops in the open, with empty ground between them
	var used: Array[Vector2] = []
	var tries := 0
	while used.size() < 22 and tries < 400:
		tries += 1
		var p := Vector2(_rng.randf_range(-82, 82), _rng.randf_range(-82, 82))
		if _near_ruin(p, 16.0) or used.any(func(u: Vector2): return u.distance_to(p) < 14.0):
			continue
		used.append(p)
		var at := Vector3(p.x, height_at(p.x, p.y) - 0.3, p.y)
		if _rng.randf() < 0.45:
			_dress.rocks(at, _rng.randf_range(1.4, 2.4), _nature)
		else:
			_dress.grove(at, "DeadTree" if _rng.randf() < 0.75 else "Pine", _nature)


## Nature model by name, for the Dressing helpers.
func _nature_model(id: String, at: Vector3, s: float, solid: bool) -> void:
	_static_model(NATURE + id + ".gltf", Vector3(at.x, height_at(at.x, at.z) - (0.3 if solid else 0.0), at.z), s, solid)


## A campfire / brazier: flames and a warm, shadowed light, ringed with stones.
func _fire(at: Vector3, energy: float = 1.8, reach: float = 11.0) -> void:
	var f := Fx.flames(0.7)
	add_child(f)
	f.position = at + Vector3.UP * 0.4
	var l := OmniLight3D.new()
	l.light_color = Palette.TORCH
	l.light_energy = energy
	l.omni_range = reach
	l.omni_attenuation = 1.6
	l.shadow_enabled = true
	add_child(l)
	l.position = at + Vector3.UP * 1.6
	for i in 7:
		var a := TAU * i / 7.0
		var sp := at + Vector3(cos(a), 0.0, sin(a)) * 0.65
		_static_model(NATURE + "Pebble_Round_%d.gltf" % _rng.randi_range(1, 5), sp, 1.8, false)


## Adventurer camps in the open: a fire with seats, maybe a supply pile beside it.
func _campfires() -> void:
	var placed := 0
	var tries := 0
	while placed < 8 and tries < 200:
		tries += 1
		var p := Vector2(_rng.randf_range(-78, 78), _rng.randf_range(-78, 78))
		if _near_ruin(p, 18.0):
			continue
		_camps.append(p)
		var yaw := _rng.randf() * TAU
		_dress.place("camp", Vector3(p.x, 0, p.y), yaw)
		if _rng.randf() < 0.6:
			var side := Vector3(cos(yaw), 0, sin(yaw)) * 5.0
			_dress.place(["storage", "farm"][_rng.randi() % 2], Vector3(p.x, 0, p.y) + side, yaw)
		placed += 1


## Chests sit in their own little hoards: candle stands either side, coins at the feet.
func _chests() -> void:
	var placed := 0
	while placed < 12:
		var p := Vector2(_rng.randf_range(-80, 80), _rng.randf_range(-80, 80))
		var yaw := _rng.randf() * TAU
		var c := Chest.new()
		add_child(c)
		c.position = Vector3(p.x, dress_height(p.x, p.y), p.y)
		c.rotation.y = yaw
		_dress.place("hoard", c.position, yaw)
		placed += 1


func _clutter() -> void:
	pass   # everything is placed as sets now (ruins, camps, hoards)


# -- helpers ------------------------------------------------------------------------------------------

func _near_ruin(p: Vector2, dist: float) -> bool:
	for r in _ruins:
		if Vector2(r.x, r.z).distance_to(p) < dist:
			return true
	return false


func _environment() -> void:
	var we := WorldEnvironment.new()
	var e := Environment.new()
	var sky := Sky.new()
	var sm := ProceduralSkyMaterial.new()
	sm.sky_top_color = Color("3a4250")
	sm.sky_horizon_color = Color("8a8478")
	sm.ground_bottom_color = Palette.VOID
	sm.ground_horizon_color = Color("6e655a")
	sm.sun_angle_max = 8.0
	sm.sky_energy_multiplier = 1.0
	sky.sky_material = sm
	e.background_mode = Environment.BG_SKY
	e.sky = sky
	e.ambient_light_source = Environment.AMBIENT_SOURCE_SKY
	e.ambient_light_energy = 1.5
	e.tonemap_mode = Environment.TONE_MAPPER_FILMIC
	e.tonemap_exposure = 1.0
	e.ssao_enabled = true
	e.ssao_intensity = 1.0
	e.glow_enabled = true
	e.glow_intensity = 1.2
	e.glow_bloom = 0.12
	e.glow_blend_mode = Environment.GLOW_BLEND_MODE_SOFTLIGHT
	e.glow_hdr_threshold = 1.1
	e.fog_enabled = true
	e.fog_light_color = Color("6e6a64")
	e.fog_density = 0.004
	e.fog_aerial_perspective = 0.4
	e.volumetric_fog_enabled = false
	e.volumetric_fog_density = 0.006
	e.volumetric_fog_albedo = Palette.STONE
	e.volumetric_fog_emission = Color("0b0d12")
	e.adjustment_enabled = true
	e.adjustment_saturation = 0.85
	e.adjustment_contrast = 0.95
	we.environment = e
	add_child(we)
	var moon := DirectionalLight3D.new()
	moon.light_color = Color("c9cfd8")
	moon.light_energy = 1.3
	moon.rotation_degrees = Vector3(-50, 145, 0)
	moon.shadow_enabled = true
	moon.directional_shadow_max_distance = 120.0
	add_child(moon)


func _triplanar(tex: String) -> StandardMaterial3D:
	var m := StandardMaterial3D.new()
	m.albedo_texture = load(VILLAGE + tex + "_BaseColor.png")
	m.normal_enabled = true
	m.normal_texture = load(VILLAGE + tex + "_Normal.png")
	m.uv1_triplanar = true
	m.uv1_world_triplanar = true
	m.uv1_scale = Vector3(0.5, 0.5, 0.5)
	m.albedo_color = Color(0.8, 0.76, 0.72)
	return m


func _box(center: Vector3, size: Vector3, basis := Basis(), show := true) -> StaticBody3D:
	var b := StaticBody3D.new()
	b.collision_layer = Combat.LAYER_WORLD
	nav.add_child(b)
	b.transform = Transform3D(basis, center)
	var cs := CollisionShape3D.new()
	var shape := BoxShape3D.new()
	shape.size = size
	cs.shape = shape
	b.add_child(cs)
	if show:
		var mi := MeshInstance3D.new()
		var bm := BoxMesh.new()
		bm.size = size
		mi.mesh = bm
		mi.material_override = _brick
		b.add_child(mi)
	return b


## A kit piece, visual only (collision is added separately as a box).
func _kit(id: String, at: Vector3, basis: Basis) -> void:
	var n: Node3D = load(VILLAGE + id + ".gltf").instantiate()
	add_child(n)
	n.transform = Transform3D(basis, at)


static var _trimesh_cache: Dictionary = {}

## A scenery model; `solid` gives it trimesh collision that the navmesh also avoids.
func _static_model(path: String, at: Vector3, s: float, solid: bool) -> void:
	var n: Node3D = load(path).instantiate()
	var root: Node3D = n
	if solid:
		var body := StaticBody3D.new()
		body.collision_layer = Combat.LAYER_WORLD
		nav.add_child(body)
		body.add_child(n)
		root = body
		for mi: MeshInstance3D in n.find_children("*", "MeshInstance3D", true, false):
			if not _trimesh_cache.has(mi.mesh):
				_trimesh_cache[mi.mesh] = mi.mesh.create_trimesh_shape()
			var cs := CollisionShape3D.new()
			cs.shape = _trimesh_cache[mi.mesh]
			cs.transform = mi.transform
			body.add_child(cs)
	else:
		add_child(n)
	root.position = at
	root.rotation.y = _rng.randf() * TAU
	root.scale = Vector3.ONE * s


func _place_prop(id: String, at: Vector3) -> Node3D:
	var p := Decor.create(id)
	add_child(p)
	p.position = at
	return p
