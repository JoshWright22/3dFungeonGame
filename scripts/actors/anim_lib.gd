class_name AnimLib
extends RefCounted
## The Universal Animation Library (UAL 1 + 2), retargeted onto any Quaternius humanoid.
##
## Every Quaternius character and Bestiary monster shares the UAL bone names and rest orientation,
## so rotation tracks transfer as-is. Only the pelvis carries a position track; it is scaled by the
## model's pelvis height so a 0.4 m Puglin doesn't hover at a 0.9 m mannequin's hip height.
## Tracks for bones the target lacks (monsters have 55 of the 65 bones) are dropped.

const SOURCES := [
	"res://assets/quaternius/animations/ual1/UAL1_Standard.glb",
	"res://assets/quaternius/animations/ual2/UAL2_Standard.glb",
]
const UAL_PELVIS_Y := 0.9167
const SKELETON_PATH := "Armature/Skeleton3D"

const LOOPING: Array[String] = [
	"Idle", "Walk", "Walk_Formal", "Jog_Fwd", "Sprint", "Crouch_Fwd", "Crouch_Idle", "Jump",
	"Sword_Idle", "Idle_Torch", "Idle_Lantern", "Idle_Shield", "Idle_FoldArms", "Idle_Talking",
	"Walk_Carry", "Zombie_Idle", "Zombie_Walk_Fwd", "Swim_Fwd", "Swim_Idle", "Spell_Simple_Idle",
	"Pistol_Idle", "Sitting_Idle", "NinjaJump_Idle", "Slide", "Dance", "Driving",
]

static var _sources: Dictionary = {}   # anim name -> Animation (UAL original)
static var _cache: Dictionary = {}     # cache key -> AnimationLibrary


## Library for a skeleton. Cached per (bone set, pelvis height), so all adventurers share one.
static func library_for(skeleton: Skeleton3D) -> AnimationLibrary:
	var pelvis := skeleton.find_bone("pelvis")
	var pelvis_y := skeleton.get_bone_global_rest(pelvis).origin.y if pelvis >= 0 else UAL_PELVIS_Y
	var key := "%d_%.2f" % [skeleton.get_bone_count(), pelvis_y]
	if _cache.has(key):
		return _cache[key]
	_load_sources()
	var ratio := pelvis_y / UAL_PELVIS_Y
	var lib := AnimationLibrary.new()
	for anim_name: String in _sources:
		var a: Animation = _sources[anim_name].duplicate(true)
		for t in range(a.get_track_count() - 1, -1, -1):
			var bone := String(a.track_get_path(t).get_concatenated_subnames())
			if skeleton.find_bone(bone) < 0:
				a.remove_track(t)
				continue
			if a.track_get_type(t) == Animation.TYPE_POSITION_3D:
				for k in a.track_get_key_count(t):
					a.track_set_key_value(t, k, a.track_get_key_value(t, k) * ratio)
		a.loop_mode = Animation.LOOP_LINEAR if anim_name in LOOPING else Animation.LOOP_NONE
		lib.add_animation(anim_name, a)
	_cache[key] = lib
	return lib


static func _load_sources() -> void:
	if not _sources.is_empty():
		return
	for path: String in SOURCES:
		var root: Node = load(path).instantiate()
		var ap: AnimationPlayer = root.find_children("*", "AnimationPlayer", true, false)[0]
		for anim_name in ap.get_animation_list():
			if anim_name != "A_TPose" and not _sources.has(anim_name):
				_sources[anim_name] = ap.get_animation(anim_name)
		root.free()
