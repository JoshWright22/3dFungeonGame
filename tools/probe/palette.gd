extends SceneTree
## Dev probe: dominant colours of textures (coarse hue/value buckets), as hex.
## godot --headless --script tools/probe/palette.gd -- <png> ...

func _init() -> void:
	for path in OS.get_cmdline_user_args():
		var img := Image.load_from_file(path)
		img.resize(64, 64)
		var buckets := {}
		for y in img.get_height():
			for x in img.get_width():
				var c := img.get_pixel(x, y)
				if c.a < 0.5: continue
				var key := "%d_%d_%d" % [int(c.h * 12), int(c.s * 4), int(c.v * 5)]
				if not buckets.has(key): buckets[key] = [0, Color(0, 0, 0, 0)]
				buckets[key][0] += 1
				buckets[key][1] += c
		var arr := buckets.values()
		arr.sort_custom(func(a, b): return a[0] > b[0])
		var out := []
		for b in arr.slice(0, 6):
			var avg: Color = b[1] / float(b[0])
			out.append("#%s(%d%%)" % [avg.to_html(false), int(100.0 * b[0] / 4096.0)])
		print(path.get_file(), ": ", " ".join(out))
	quit()
