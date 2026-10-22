shader_type spatial;
// Stylised ground: moss and dirt on the flats, bare stone on slopes, world-space noise breakup.
// Colours come from the palette (scripts/core/palette.gd), set by the stage.

uniform vec3 moss : source_color = vec3(0.17, 0.19, 0.12);
uniform vec3 dirt : source_color = vec3(0.23, 0.18, 0.13);
uniform vec3 stone : source_color = vec3(0.29, 0.26, 0.23);
uniform vec3 stone_light : source_color = vec3(0.43, 0.40, 0.35);
uniform sampler2D noise : filter_linear_mipmap, repeat_enable;

varying vec3 world_pos;
varying vec3 world_normal;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	world_normal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}

void fragment() {
	float n1 = texture(noise, world_pos.xz * 0.035).r;
	float n2 = texture(noise, world_pos.xz * 0.21).r;
	float slope = 1.0 - clamp(world_normal.y, 0.0, 1.0);
	vec3 ground = mix(moss, dirt, smoothstep(0.35, 0.65, n1));
	ground *= 0.85 + 0.3 * n2;
	// stone on slopes, banded vertically like strata
	float strata = texture(noise, vec2(world_pos.y * 0.25, n1)).r;
	vec3 rock = mix(stone, stone_light, strata) * (0.8 + 0.35 * n2);
	float rock_mix = smoothstep(0.28, 0.5, slope + (n2 - 0.5) * 0.15);
	ALBEDO = mix(ground, rock, rock_mix);
	ROUGHNESS = 0.95;
}
