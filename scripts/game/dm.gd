class_name DM
extends Node
## The Dungeon Master, narrating his own show, and the cast talking back. Subtitled.
## Lines are picked at random per event, with a per-event cooldown so nobody repeats themselves.
## The monsters have scrying-stone phones on the Crystal Net and talk like it (docs/gdd.md §8).

signal line(speaker: String, text: String, seconds: float)

const LINES := {
	"open": [
		"Ladies and gentlemonsters — after ONE THOUSAND YEARS — adventurers!",
		"We're live. Look scary. Or at least look awake.",
		"Places, everyone. The guests are on the stairs.",
	],
	"first_blood": ["And the first guest leaves in a box! Wonderful television.", "First blood! The surface is watching."],
	"fire_kill": ["Smell that? That's prime time.", "Someone call the fire brigade. Actually, don't."],
	"multi": ["A MULTI-KILL! Somebody clip that!", "Three at once! Mother would be proud.", "The tavern mirrors are going wild!"],
	"juggle": ["They're airborne! They're still airborne!", "Gravity: optional, apparently."],
	"downed": ["Oh no. Oh, that's great for ratings, though.", "Medic! ...We don't have a medic. Makeup!"],
	"chest": ["A relic! That's been in there since the Second Age.", "Ooh, treasure. Don't tell the adventurers."],
	"elite": ["Oh, this one's a VETERAN. Act natural.", "A blessed one! Holy light, in MY dungeon."],
	"tier_1": ["Regional Buzz! The neighbouring villages are tuning in!"],
	"tier_2": ["KINGDOM-WIDE! The king has a mirror! The KING!"],
	"tier_3": ["LEGENDARY! Bards will sing of this! Badly!"],
	"quiet": ["Ratings are dying. Somebody throw something.", "This is the boring part. Make it not the boring part.", "Could the cast PLEASE put their scrying stones away."],
}

## The cast. Keyed by monster id, then event. They are extremely online.
const CAST := {
	"imp": {
		"kill": ["Clip that. CLIP THAT.", "Imagine losing to an Imp. Couldn't be me.", "Like and subscribe, peasants.", "Main character energy."],
		"multi": ["Triple kill. Somebody post this before the DM takes credit.", "Going viral. Going VIRAL."],
		"fire_kill": ["Touch grass? I SET the grass on fire.", "It's giving... crispy."],
		"downed": ["Lag. That was lag.", "Reported. All of you. Reported.", "This is so not canon."],
		"relic": ["Unboxing time.", "Rate my drip.", "Loot drop! No cap."],
		"idle": ["Hold on, I'm doomscrolling.", "My follower count is up three. THREE.", "Can someone tell the DM the stream is muted?"],
		"spawn": ["Ugh. Fine. I'm live.", "Okay chat, we ball."],
	},
	"puglin": {
		"kill": ["W. Huge W.", "I'm literally just a little guy.", "Sorry! Sorry. Not sorry."],
		"multi": ["Biscuit used Good Boy! It's super effective!", "Nobody tell my mom."],
		"fire_kill": ["Smells like a skill issue.", "Who ordered the barbecue?"],
		"downed": ["I'm so cooked.", "Tell my followers... I was valid.", "Respawn pls."],
		"relic": ["Shiny! Mine now.", "Ooh, it's a whole vibe."],
		"idle": ["Did you guys see the cat on the scrying pool?", "Wait, heroes are real? I thought they were a psy-op.", "Mom said it's my turn on the crystal ball."],
		"spawn": ["Hi chat! Hi!", "Is the camera on? Oh no."],
	},
}

const CAST_NAMES := {"imp": "Vex", "puglin": "Biscuit"}

var _rng := RandomNumberGenerator.new()
var _last: Dictionary = {}


func _ready() -> void:
	add_to_group("narrator")
	_rng.randomize()
	Ratings.tier_changed.connect(func(t: int):
		if t > 0:
			say("tier_%d" % t, 0.0, 5.0))


## Speak a line for `event`, at most once per `cooldown` seconds.
func say(event: String, cooldown := 8.0, seconds := 3.5) -> void:
	var now := Time.get_ticks_msec() / 1000.0
	if now - float(_last.get(event, -1000.0)) < cooldown or not LINES.has(event):
		return
	_last[event] = now
	var options: Array = LINES[event]
	line.emit("DM", options[_rng.randi() % options.size()], seconds)


## A cast member's line for `event`. Shared cooldown per monster so they don't chatter nonstop.
func bark(monster_id: String, event: String, cooldown := 6.0, seconds := 3.0) -> void:
	var now := Time.get_ticks_msec() / 1000.0
	var key := "bark_" + monster_id
	if now - float(_last.get(key, -1000.0)) < cooldown or not CAST.has(monster_id):
		return
	var lines: Dictionary = CAST[monster_id]
	if not lines.has(event):
		return
	_last[key] = now
	var options: Array = lines[event]
	line.emit(CAST_NAMES[monster_id], options[_rng.randi() % options.size()], seconds)
