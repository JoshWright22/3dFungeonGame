class_name Hud
extends CanvasLayer
## In-run HUD. The DM's broadcast pieces (ON AIR, ratings, style feed) are the bright UI;
## everything else is parchment on black (docs/gdd.md §10).

var m: Monster
var director: Director

var _root: Control
var _hp_bar: ProgressBar
var _hp_label: Label
var _gold: Label
var _clock: Label
var _diff: Label
var _ratings_bar: ProgressBar
var _ratings_label: Label
var _subtitle: Label
var _prompt: Label
var _toast: Label
var _feed: VBoxContainer
var _relics: HBoxContainer
var _skills: Array = []          ## [panel, cooldown overlay, seconds label]
var _crosshair: Crosshair
var _subtitle_t := 0.0
var _toast_t := 0.0


class Crosshair extends Control:
	var hit_flash := 0.0

	func _draw() -> void:
		var c := size * 0.5
		var col := Color(Palette.PARCHMENT, 0.9)
		var gap := 6.0 + hit_flash * 4.0
		for d: Vector2 in [Vector2.UP, Vector2.DOWN, Vector2.LEFT, Vector2.RIGHT]:
			draw_line(c + d * gap, c + d * (gap + 7.0), col, 2.0)
		draw_circle(c, 1.5, col)


func bind(monster: Monster, dir: Director, dm: DM) -> void:
	if m:
		m.toast.disconnect(_on_toast)
		m.relics_changed.disconnect(_refresh_relics)
	m = monster
	director = dir
	m.toast.connect(_on_toast)
	m.relics_changed.connect(_refresh_relics)
	if not dm.line.is_connected(_on_dm_line):
		dm.line.connect(_on_dm_line)
		director.style.connect(_on_style)
	_build_skills()
	_refresh_relics()


func _ready() -> void:
	_root = Control.new()
	_root.set_anchors_preset(Control.PRESET_FULL_RECT)
	_root.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(_root)

	_crosshair = Crosshair.new()
	_crosshair.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_root.add_child(_crosshair)
	_place(_crosshair, Control.PRESET_CENTER, Vector2(-30, -30), Vector2(60, 60))

	# bottom left: health
	var hp_box := _panel(Control.PRESET_BOTTOM_LEFT, Vector2(24, -96), Vector2(360, 72))
	var v := VBoxContainer.new()
	hp_box.add_child(v)
	_hp_label = _label("", 18, Palette.PARCHMENT)
	v.add_child(_hp_label)
	_hp_bar = _bar(Palette.BLOOD.lightened(0.15), Vector2(340, 18))
	v.add_child(_hp_bar)

	# top left: gold
	_gold = _label("$0", 30, Palette.GOLD)
	_root.add_child(_gold)
	_place(_gold, Control.PRESET_TOP_LEFT, Vector2(28, 20), Vector2(300, 40))

	# top right: the broadcast
	var air := _panel(Control.PRESET_TOP_RIGHT, Vector2(-344, 18), Vector2(320, 104))
	var av := VBoxContainer.new()
	air.add_child(av)
	_clock = _label("", 20, Palette.ON_AIR.lightened(0.2))
	av.add_child(_clock)
	_diff = _label("", 16, Palette.PARCHMENT)
	av.add_child(_diff)
	_ratings_bar = _bar(Palette.ON_AIR, Vector2(300, 12))
	av.add_child(_ratings_bar)
	_ratings_label = _label("", 14, Palette.PARCHMENT)
	av.add_child(_ratings_label)

	# style feed, right edge
	_feed = VBoxContainer.new()
	_feed.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_feed.alignment = BoxContainer.ALIGNMENT_END
	_root.add_child(_feed)
	_place(_feed, Control.PRESET_CENTER_RIGHT, Vector2(-330, -200), Vector2(300, 200))

	# DM subtitle
	_subtitle = _label("", 22, Palette.PARCHMENT)
	_subtitle.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_subtitle.autowrap_mode = TextServer.AUTOWRAP_WORD
	_subtitle.modulate.a = 0.0
	_root.add_child(_subtitle)
	_place(_subtitle, Control.PRESET_CENTER_BOTTOM, Vector2(-450, -200), Vector2(900, 70))

	# interaction prompt and toasts, under the crosshair
	_prompt = _label("", 18, Palette.PARCHMENT)
	_prompt.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_root.add_child(_prompt)
	_place(_prompt, Control.PRESET_CENTER, Vector2(-300, 40), Vector2(600, 30))
	_toast = _label("", 20, Palette.TORCH)
	_toast.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_root.add_child(_toast)
	_place(_toast, Control.PRESET_CENTER, Vector2(-450, 90), Vector2(900, 30))

	# relic strip
	_relics = HBoxContainer.new()
	_relics.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_relics.alignment = BoxContainer.ALIGNMENT_CENTER
	_root.add_child(_relics)
	_place(_relics, Control.PRESET_CENTER_BOTTOM, Vector2(-400, -48), Vector2(800, 30))


func _build_skills() -> void:
	for s in _skills:
		s[0].queue_free()
	_skills.clear()
	var keys := ["LMB", "RMB", "SHIFT", "R"]
	for i in 4:
		var p := _panel(Control.PRESET_BOTTOM_RIGHT, Vector2(-24 - (4 - i) * 112, -112), Vector2(104, 88))
		var v := VBoxContainer.new()
		v.mouse_filter = Control.MOUSE_FILTER_IGNORE
		p.add_child(v)
		v.add_child(_label(keys[i], 13, Palette.STONE_LIGHT))
		var name_l := _label(m.kit.names[i], 15, Palette.PARCHMENT)
		name_l.autowrap_mode = TextServer.AUTOWRAP_WORD
		v.add_child(name_l)
		var cd := ColorRect.new()
		cd.color = Color(0, 0, 0, 0.6)
		cd.mouse_filter = Control.MOUSE_FILTER_IGNORE
		p.add_child(cd)
		var secs := _label("", 26, Palette.PARCHMENT)
		secs.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		secs.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
		secs.set_anchors_preset(Control.PRESET_FULL_RECT)
		p.add_child(secs)
		_skills.append([p, cd, secs])


func _process(delta: float) -> void:
	if m == null:
		return
	_hp_bar.max_value = m.health.max_hp
	_hp_bar.value = m.health.hp
	_hp_label.text = "%s, %s     %d / %d" % [m.def.name, m.def.title, ceili(m.health.hp), roundi(m.health.max_hp)]
	_gold.text = "$%d" % m.gold
	var secs := int(director.elapsed)
	_clock.text = "●  ON AIR   %02d:%02d" % [secs / 60, secs % 60]
	_diff.text = director.difficulty_name()
	var tier := Ratings.tier
	var lo: float = Ratings.TIER_AT[tier]
	var hi: float = Ratings.TIER_AT[tier + 1] if tier + 1 < Ratings.TIER_AT.size() else lo + 500.0
	_ratings_bar.max_value = hi - lo
	_ratings_bar.value = Ratings.value - lo
	_ratings_label.text = "%s   ×%.1f gold" % [["Local Interest", "Regional Buzz", "Kingdom-wide", "LEGENDARY"][tier], Ratings.gold_multiplier()]

	for i in 4:
		var rem: float = m.kit.remaining[i]
		var full: float = m.kit.cooldown[i]
		var s: Array = _skills[i]
		var cd: ColorRect = s[1]
		cd.color.a = 0.25 + 0.45 * rem / full if i > 0 and rem > 0.0 else 0.0
		(s[2] as Label).text = "%d" % ceili(rem) if i > 0 and rem > 0.0 else ""

	_crosshair.hit_flash = maxf(_crosshair.hit_flash - delta * 6.0, 0.0)
	_crosshair.queue_redraw()
	_prompt.text = _prompt_text()

	_subtitle_t -= delta
	_subtitle.modulate.a = clampf(_subtitle_t * 2.0, 0.0, 1.0)
	_toast_t -= delta
	_toast.modulate.a = clampf(_toast_t * 2.0, 0.0, 1.0)


func _prompt_text() -> String:
	var t := m.look_interactable()
	if t is Chest:
		var p := (t as Chest).prompt()
		return "" if p == "" else "[E] " + p
	return ""


func _refresh_relics() -> void:
	for c in _relics.get_children():
		c.queue_free()
	for id in m.relics:
		var chip := _label("%s ×%d" % [Relics.ALL[id].name, m.relics[id]], 14, Relics.color(id))
		_relics.add_child(chip)


func _on_dm_line(speaker: String, text: String, seconds: float) -> void:
	_subtitle.text = "%s: \"%s\"" % [speaker, text]
	_subtitle.add_theme_color_override("font_color", Palette.PARCHMENT if speaker == "DM" else Palette.TORCH)
	_subtitle_t = seconds


func _on_toast(text: String) -> void:
	_toast.text = text
	_toast_t = 2.5


func _on_style(text: String, points: float) -> void:
	_crosshair.hit_flash = 1.0
	var l := _label("%s  +%d" % [text, roundi(points)], 18, Palette.ON_AIR.lightened(0.35))
	l.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
	_feed.add_child(l)
	var t := l.create_tween()
	t.tween_interval(2.2)
	t.tween_property(l, "modulate:a", 0.0, 0.6)
	t.tween_callback(l.queue_free)
	while _feed.get_child_count() > 6:
		_feed.get_child(0).free()


# -- widgets --------------------------------------------------------------------------------------

func _label(text: String, size: int, color: Color) -> Label:
	var l := Label.new()
	l.text = text
	l.add_theme_font_size_override("font_size", size)
	l.add_theme_color_override("font_color", color)
	l.add_theme_color_override("font_outline_color", Palette.VOID)
	l.add_theme_constant_override("outline_size", 6)
	l.mouse_filter = Control.MOUSE_FILTER_IGNORE
	return l


func _bar(fill: Color, min_size: Vector2) -> ProgressBar:
	var b := ProgressBar.new()
	b.mouse_filter = Control.MOUSE_FILTER_IGNORE
	b.custom_minimum_size = min_size
	b.show_percentage = false
	var bg := StyleBoxFlat.new()
	bg.bg_color = Color(Palette.VOID, 0.85)
	bg.border_color = Palette.STONE
	bg.set_border_width_all(1)
	var fg := StyleBoxFlat.new()
	fg.bg_color = fill
	b.add_theme_stylebox_override("background", bg)
	b.add_theme_stylebox_override("fill", fg)
	return b


func _panel(preset: int, offset: Vector2, panel_size: Vector2) -> PanelContainer:
	var p := PanelContainer.new()
	var sb := StyleBoxFlat.new()
	sb.bg_color = Color(Palette.VOID, 0.72)
	sb.border_color = Color(Palette.STONE, 0.8)
	sb.set_border_width_all(1)
	sb.set_content_margin_all(8)
	p.add_theme_stylebox_override("panel", sb)
	p.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_root.add_child(p)
	_place(p, preset, offset, panel_size)
	return p


## Anchor `c` to a preset corner/edge and place it `offset` from that anchor.
func _place(c: Control, preset: int, offset: Vector2, box: Vector2) -> void:
	c.set_anchors_preset(preset)
	c.offset_left = offset.x
	c.offset_top = offset.y
	c.offset_right = offset.x + box.x
	c.offset_bottom = offset.y + box.y
