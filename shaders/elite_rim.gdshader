shader_type spatial;
// Elite adventurers: a coloured rim glow, used as material_overlay. Leaves the model's own
// shading intact (a flat overlay washed them out in the dark).
render_mode unshaded, blend_add, depth_draw_never, cull_back;

uniform vec4 tint : source_color = vec4(1.0, 0.85, 0.5, 1.0);
uniform float power = 2.5;
uniform float strength = 1.6;

void fragment() {
	float rim = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), power);
	ALBEDO = tint.rgb * rim * strength;
}
