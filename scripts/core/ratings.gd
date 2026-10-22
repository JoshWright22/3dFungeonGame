extends Node
## Ratings: how much the surface is enjoying the show. Clank!, inverted (docs/gdd.md §3).
## Spectacle raises ratings; ratings multiply gold drops, push the difficulty clock, and trigger
## the DM's d20 twists at each tier. Autoload "Ratings".
##
## Anyone may call Ratings.add(); clients forward to the host, which owns `value`.

signal changed(value: float)
signal tier_changed(tier: int)

enum Tier { LOCAL_INTEREST, REGIONAL_BUZZ, KINGDOM_WIDE, LEGENDARY }
const TIER_AT := [0.0, 100.0, 300.0, 700.0]

var value: float = 0.0
var tier: int = Tier.LOCAL_INTEREST


func reset() -> void:
	value = 0.0
	tier = Tier.LOCAL_INTEREST
	changed.emit(value)
	tier_changed.emit(tier)


## Gold multiplier from ratings: 1x at zero, 2x at Kingdom-wide, 3x at Legendary.
func gold_multiplier() -> float:
	return 1.0 + clampf(value / 350.0, 0.0, 2.0)


## Award spectacle. `reason` feeds the recap and the DM's commentary.
func add(amount: float, reason: String = "") -> void:
	if multiplayer.multiplayer_peer == null or multiplayer.is_server():
		_set_value.rpc(value + amount)
	else:
		_server_add.rpc_id(1, clampf(amount, 0.0, 50.0), reason)


@rpc("any_peer", "call_remote", "reliable")
func _server_add(amount: float, reason: String) -> void:
	if multiplayer.is_server():
		add(amount, reason)


@rpc("authority", "call_local", "reliable")
func _set_value(v: float) -> void:
	value = v
	changed.emit(value)
	var t := tier_for(value)
	if t != tier:
		tier = t
		tier_changed.emit(tier)


func set_value_silent(v: float) -> void:
	value = v
	tier = tier_for(v)


static func tier_for(v: float) -> int:
	var t := 0
	for i in TIER_AT.size():
		if v >= TIER_AT[i]:
			t = i
	return t
