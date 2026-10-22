extends MonsterKit
## Biscuit, the Puglin. The Underdog: fast jabs, a nose for weakness, wall-scurrying, and being
## too cute to hit.

const JAB_CLIPS := ["Punch_Jab", "Punch_Cross"]
const SNIFF_SECONDS := 8.0
const SCURRY_SECONDS := 3.5
const GOOD_BOY_RADIUS := 14.0


func _init() -> void:
	names = ["Stick Jabs", "Sniff", "Scurry", "Good Boy"]
	descs = [
		"Fast jabs with a stick.",
		"Adventurers and chests glow through walls. Sniffed adventurers take 25% more damage.",
		"Run faster and up walls for a few seconds.",
		"Nearby adventurers stop to coo. They take 50% more damage while distracted.",
	]
	cooldown = [0.28, 12.0, 7.0, 28.0]


func _primary() -> void:
	m.face_aim(0.5)
	m.rig.play_upper(JAB_CLIPS[_combo % 2], 1.9 * m.attack_speed)
	later(0.1 / m.attack_speed, func(): m.melee(1.9, 1.0, 0.8, 3.5))


func _secondary() -> bool:
	m.rig.play_upper("Interact", 1.4)
	for a in m.get_tree().get_nodes_in_group("adventurers"):
		(a as Adventurer).mark_sniffed(SNIFF_SECONDS)
	for c in m.get_tree().get_nodes_in_group("chests"):
		Highlight.apply(c, Palette.GOLD, SNIFF_SECONDS)
	Fx.burst(m.chest(), Palette.ARCANE, 30, 6.0, 0.15, 0.8)
	return true


func _utility() -> bool:
	m.speed_buff = 1.7
	m.wall_climb = true
	Fx.burst(m.global_position + Vector3.UP * 0.3, Palette.STONE_LIGHT, 16, 3.0)
	_end_scurry()
	return true


func _end_scurry() -> void:
	await get_tree().create_timer(SCURRY_SECONDS, false).timeout
	if is_instance_valid(m):
		m.speed_buff = 1.0
		m.wall_climb = false


func _special() -> bool:
	m.rig.play_full("Yes", 1.2)
	var n := 0
	for a in m.get_tree().get_nodes_in_group("adventurers"):
		var adv := a as Adventurer
		if adv.global_position.distance_to(m.global_position) < GOOD_BOY_RADIUS:
			adv.stun(5.0, true)
			n += 1
	Fx.burst(m.chest() + Vector3.UP * 0.5, Color("e88ba0"), 40, 5.0, 0.25, 1.0)
	Ratings.add(5.0 + 2.0 * n, "good boy")
	return true
