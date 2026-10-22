extends MonsterKit
## Vex, the Imp. The Villain: a club, fire, a blink, and burning the room down for ratings.

const CLUB_CLIPS := ["Sword_Regular_A", "Sword_Regular_B", "Sword_Regular_C"]


func _init() -> void:
	names = ["Spiked Club", "Hellfire", "Blink", "Arson Special"]
	descs = [
		"Three-hit combo. The third hit sends them flying.",
		"Hurl a fireball that explodes and ignites.",
		"Teleport a short way in the direction you're moving.",
		"Everything around you bursts into flames.",
	]
	cooldown = [0.42, 3.5, 5.0, 22.0]


func _primary() -> void:
	var step := _combo % 3
	m.face_aim(0.7)
	m.rig.play_upper(CLUB_CLIPS[step], 1.55 * m.attack_speed)
	var finisher := step == 2
	later(0.17 / m.attack_speed, func(): m.melee(2.3, 1.35, 1.8 if finisher else 1.0, 12.0 if finisher else 5.0))


func _secondary() -> bool:
	m.face_aim(0.6)
	m.rig.play_upper("Spell_Simple_Shoot", 1.5)
	var from := m.chest() + m.cam.forward_flat() * 0.6
	Projectile.fireball(m, from, m.aim_dir_from(from), m.roll_damage(2.6), Palette.TORCH_FALLOFF, Combat.LAYER_ENEMIES)
	return true


func _utility() -> bool:
	var dir := move_or_aim_dir()
	var d := clear_distance(dir, 9.0)
	if d < 1.0:
		return false
	Fx.burst(m.chest(), Palette.EMBER, 24, 3.0)
	m.global_position += dir * d
	m.health.invulnerable_until = Time.get_ticks_msec() / 1000.0 + 0.25
	Fx.burst(m.chest(), Palette.TORCH, 24, 3.0)
	Fx.flash(m.chest(), Palette.TORCH_FALLOFF, 3.0, 6.0, 0.25)
	return true


func _special() -> bool:
	var center := m.global_position + Vector3.UP
	m.rig.play_full("Spell_Simple_Shoot", 1.2)
	var hits := Combat.hit_sphere(m, center, 7.5, m.roll_damage(5.0), 15.0, Combat.LAYER_ENEMIES, Damage.Kind.FIRE, 5.0)
	Fx.burst(center, Palette.TORCH_FALLOFF, 80, 12.0, 0.5, 0.8)
	Fx.flash(center, Palette.TORCH, 8.0, 16.0, 0.6)
	Combat.shake(0.8)
	Ratings.add(8.0 + 3.0 * hits.size(), "arson")
	return true
