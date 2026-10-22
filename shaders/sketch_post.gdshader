shader_type spatial;
// Full-screen "ink sketch" pass, after Dark and Darker's class sketches: ink outlines from depth
// and normal edges, cross-hatching that builds up in the shadows, shadows lifted to a dark
// parchment so the hatching reads, a paper tint, vignette and grain.
// Drawn on a quad parented to the camera (see scripts/world/sketch_post.gd).
render_mode unshaded, depth_draw_never, depth_test_disabled, cull_disabled, fog_disabled, shadows_disabled;

uniform sampler2D screen_tex : hint_screen_texture, filter_linear_mipmap;
uniform sampler2D depth_tex : hint_depth_texture, filter_nearest;

uniform vec3 ink : source_color = vec3(0.043, 0.035, 0.031);
uniform vec3 paper : source_color = vec3(0.79, 0.72, 0.61);
uniform vec3 shadow_paper : source_color = vec3(0.10, 0.085, 0.07);
uniform float shadow_lift = 0.45;          // how far black is lifted toward shadow_paper
uniform float paper_tint = 0.08;

uniform float line_px = 1.6;
uniform float depth_sens = 1.0;
uniform float normal_sens = 1.0;
uniform float line_strength = 1.0;
uniform float line_fade_start = 18.0;
uniform float line_fade_end = 70.0;

uniform float hatch_spacing = 6.5;         // pixels between hatch lines
uniform float hatch_width = 0.2;          // 0..1 of the spacing
uniform float hatch_strength = 0.25;
uniform float hatch_l1 = 0.13;             // luminance under which each layer appears
uniform float hatch_l2 = 0.065;
uniform float hatch_l3 = 0.03;

uniform vec3 shadow_tint : source_color = vec3(0.36, 0.24, 0.45);   // plum
uniform vec3 highlight_tint : source_color = vec3(1.0, 0.70, 0.37);  // torch
uniform float split_strength = 0.08;

uniform vec3 glint_color : source_color = vec3(1.0, 0.86, 0.62);
uniform float glint_threshold = 2.2;      // HDR luminance where sparkles start
uniform float glint_strength = 0.06;
uniform float glint_px = 4.0;

uniform float vignette = 0.3;
uniform float grain = 0.012;
uniform int debug_view = 0;              // 1 edges, 2 hatch, 3 depth

void vertex() {
	POSITION = vec4(VERTEX.xy, 1.0, 1.0);
}

float linear_depth(vec2 uv, mat4 inv_proj) {
	float d = texture(depth_tex, uv).r;
	vec4 v = inv_proj * vec4(uv * 2.0 - 1.0, d, 1.0);
	return -v.z / v.w;
}

vec3 view_pos(vec2 uv, mat4 inv_proj) {
	float d = texture(depth_tex, uv).r;
	vec4 v = inv_proj * vec4(uv * 2.0 - 1.0, d, 1.0);
	return v.xyz / v.w;
}

float hatch_line(vec2 p, vec2 dir) {
	float v = dot(p, dir) / hatch_spacing;
	float f = abs(fract(v) - 0.5) * 2.0;           // 0 at line centre, 1 between lines
	return 1.0 - smoothstep(hatch_width * 0.6, hatch_width, 1.0 - f);
}

float hash(vec2 p) {
	return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453);
}

float value_noise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), f.x), mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), f.x), f.y);
}

void fragment() {
	vec2 uv = SCREEN_UV;
	vec2 px = 1.0 / VIEWPORT_SIZE;
	vec3 col = textureLod(screen_tex, uv, 0.0).rgb;
	float d0 = linear_depth(uv, INV_PROJECTION_MATRIX);
	bool sky = d0 > 900.0;

	// -- ink outlines -----------------------------------------------------------------------
	float edge = 0.0;
	if (!sky) {
		// Geometric normal from the depth buffer (normal maps never reach it), then two tests:
		// silhouettes (a neighbour is much nearer) and creases (neighbours leave the surface plane).
		vec3 p0 = view_pos(uv, INV_PROJECTION_MATRIX);
		vec2 dx = vec2(px.x, 0.0) * line_px;
		vec2 dy = vec2(0.0, px.y) * line_px;
		vec3 pr = view_pos(uv + dx, INV_PROJECTION_MATRIX);
		vec3 pl = view_pos(uv - dx, INV_PROJECTION_MATRIX);
		vec3 pu = view_pos(uv + dy, INV_PROJECTION_MATRIX);
		vec3 pd = view_pos(uv - dy, INV_PROJECTION_MATRIX);
		// pick the smaller difference each way so the normal ignores the far side of an edge
		vec3 hx = abs(pr.z - p0.z) < abs(p0.z - pl.z) ? pr - p0 : p0 - pl;
		vec3 hy = abs(pu.z - p0.z) < abs(p0.z - pd.z) ? pu - p0 : p0 - pd;
		vec3 n0 = normalize(cross(hx, hy));
		float dd = max(-p0.z + pr.z, 0.0) + max(-p0.z + pl.z, 0.0) + max(-p0.z + pu.z, 0.0) + max(-p0.z + pd.z, 0.0);
		float plane = abs(dot(pr - p0, n0)) + abs(dot(pl - p0, n0)) + abs(dot(pu - p0, n0)) + abs(dot(pd - p0, n0));
		float e_depth = smoothstep(0.015, 0.05, dd / max(d0, 0.1) * depth_sens);
		float e_norm = smoothstep(0.004, 0.012, plane / max(d0, 0.1) * normal_sens);
		float fade = 1.0 - smoothstep(line_fade_start, line_fade_end, d0);
		edge = max(e_depth, e_norm * 0.8) * fade * line_strength;
	}

	// -- tone: lift the blacks to dark parchment, tint toward paper -----------------------------
	float lum = dot(col, vec3(0.299, 0.587, 0.114));
	vec3 toned = mix(col, shadow_paper + col * (1.0 - shadow_paper), shadow_lift);
	// split-tone: shadows to plum, highlights to torch
	float tl = dot(toned, vec3(0.299, 0.587, 0.114));
	float split = sky ? 0.0 : split_strength;
	toned = mix(toned, toned * shadow_tint * 2.2, (1.0 - smoothstep(0.0, 0.3, tl)) * split);
	toned = mix(toned, toned * highlight_tint * 1.5, smoothstep(0.35, 1.4, tl) * split);
	toned = mix(toned, toned * paper * 1.25, paper_tint);

	// -- hatching: screen-space, hand-wobbled, more layers as it gets darker ----------------------
	float hatch = 0.0;
	if (!sky) {
		vec2 p = FRAGCOORD.xy;
		p += vec2(sin(p.y * 0.045 + d0 * 0.3), cos(p.x * 0.05)) * 1.4;
		float h1 = hatch_line(p, normalize(vec2(1.0, 1.0)));
		float h2 = hatch_line(p, normalize(vec2(1.0, -1.0)));
		float h3 = hatch_line(p + 2.5, normalize(vec2(1.0, 0.15)));
		hatch = max(hatch, h1 * (1.0 - smoothstep(hatch_l1 * 0.6, hatch_l1, lum)));
		hatch = max(hatch, h2 * (1.0 - smoothstep(hatch_l2 * 0.6, hatch_l2, lum)));
		hatch = max(hatch, h3 * (1.0 - smoothstep(hatch_l3 * 0.6, hatch_l3, lum)));
		// break the pattern into hand-drawn stroke patches so it never reads as a screen overlay
		float strokes = smoothstep(0.3, 0.55, value_noise(FRAGCOORD.xy / 38.0 + vec2(d0 * 0.05)));
		hatch *= strokes;
		hatch *= 1.0 - smoothstep(line_fade_start * 0.5, line_fade_end, d0);
	}

	vec3 outc = mix(toned, ink, hatch * hatch_strength);
	outc = mix(outc, ink, clamp(edge, 0.0, 1.0));

	// star glints on the hottest highlights
	float glint = 0.0;
	for (int i = 1; i <= 6; i++) {
		float w = 1.0 - float(i) / 7.0;
		vec2 ox = vec2(px.x * glint_px * float(i), 0.0);
		vec2 oy = vec2(0.0, px.y * glint_px * float(i));
		float s = 0.0;
		s += max(dot(textureLod(screen_tex, uv + ox, 0.0).rgb, vec3(0.333)) - glint_threshold, 0.0);
		s += max(dot(textureLod(screen_tex, uv - ox, 0.0).rgb, vec3(0.333)) - glint_threshold, 0.0);
		s += max(dot(textureLod(screen_tex, uv + oy, 0.0).rgb, vec3(0.333)) - glint_threshold, 0.0);
		s += max(dot(textureLod(screen_tex, uv - oy, 0.0).rgb, vec3(0.333)) - glint_threshold, 0.0);
		glint += s * w * w;
	}
	outc += glint_color * glint * glint_strength;

	vec2 q = uv - 0.5;
	outc *= 1.0 - vignette * dot(q, q) * 2.0;
	outc += (hash(FRAGCOORD.xy + fract(TIME) * 97.0) - 0.5) * grain;
	if (debug_view == 1) { outc = vec3(1.0 - edge); }
	else if (debug_view == 2) { outc = vec3(1.0 - hatch); }
	else if (debug_view == 3) { outc = vec3(fract(d0 / 20.0)); }
	ALBEDO = outc;
}
