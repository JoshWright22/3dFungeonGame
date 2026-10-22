class_name Damage
extends RefCounted
## One hit. Created by whatever hurts something and passed to Health.take().

enum Kind { MELEE, THROWN, FIRE, SPELL, PROJECTILE }

var amount: float
var source: Node            ## the attacker (Monster / Adventurer), or null
var point: Vector3
var impulse: Vector3        ## knockback, m/s added to the target
var kind: int = Kind.MELEE
var weapon: Node            ## the projectile or object that landed the hit, if any
var ignite := 0.0           ## seconds of burn to apply
var tags: Array[StringName] = []


static func make(amount: float, source: Node, point: Vector3, impulse := Vector3.ZERO, kind := Kind.MELEE) -> Damage:
	var d := Damage.new()
	d.amount = amount
	d.source = source
	d.point = point
	d.impulse = impulse
	d.kind = kind
	return d
