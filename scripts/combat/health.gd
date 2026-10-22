class_name Health
extends Node
## Hit points for anything that can be hurt. Lives as a child named "Health".
## Armor uses Risk of Rain 2's curve: damage × 100 / (100 + armor).

signal damaged(dmg: Damage)
signal died(dmg: Damage)

var max_hp := 100.0
var hp := 100.0
var regen := 0.0            ## hp per second
var armor := 0.0
var damage_taken_mult := 1.0
var invulnerable_until := 0.0
var dead := false
var last_damage: Damage

var _burn_time := 0.0
var _burn_dps := 0.0
var _burn_source: Node
var _burn_tick := 0.0
var _fire_fx: Node3D


static func of(node: Node) -> Health:
	while node != null:
		var h := node.get_node_or_null("Health")
		if h is Health:
			return h
		node = node.get_parent()
	return null


func setup(hp_max: float) -> void:
	max_hp = hp_max
	hp = hp_max


func take(dmg: Damage) -> void:
	if dead or Time.get_ticks_msec() / 1000.0 < invulnerable_until:
		return
	dmg.amount *= damage_taken_mult * 100.0 / (100.0 + armor)
	hp -= dmg.amount
	last_damage = dmg
	if dmg.ignite > 0.0:
		ignite(dmg.ignite, maxf(dmg.amount * 0.35, 2.0), dmg.source)
	damaged.emit(dmg)
	Combat.damage_number(dmg, get_parent())
	if hp <= 0.0:
		hp = 0.0
		dead = true
		_set_fire_fx(false)
		died.emit(dmg)


func heal(amount: float) -> void:
	if not dead:
		hp = minf(max_hp, hp + amount)


func ignite(seconds: float, dps: float, source: Node) -> void:
	if dead:
		return
	_burn_time = maxf(_burn_time, seconds)
	_burn_dps = maxf(_burn_dps, dps)
	_burn_source = source
	_set_fire_fx(true)


func is_burning() -> bool:
	return _burn_time > 0.0


func _process(delta: float) -> void:
	if dead:
		return
	if regen > 0.0:
		heal(regen * delta)
	if _burn_time > 0.0:
		_burn_time -= delta
		_burn_tick -= delta
		if _burn_tick <= 0.0:
			_burn_tick = 0.5
			var p: Node3D = get_parent()
			take(Damage.make(_burn_dps * 0.5, _burn_source if is_instance_valid(_burn_source) else null, p.global_position + Vector3.UP, Vector3.ZERO, Damage.Kind.FIRE))
		if _burn_time <= 0.0:
			_set_fire_fx(false)


func _set_fire_fx(on: bool) -> void:
	if on and _fire_fx == null:
		_fire_fx = Fx.flames(1.0)
		get_parent().add_child(_fire_fx)
		_fire_fx.position = Vector3(0, 0.9, 0)
	elif not on and _fire_fx != null:
		_fire_fx.queue_free()
		_fire_fx = null
