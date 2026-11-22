// Worn ground round a terrain piece (Kai, 2026-11-18: terrain sitting IN the ground). The battlefield's own ground
// picture at the same scale, pushed towards trodden dirt, fading out to the edge with a ragged noise border.
shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_disabled;

uniform sampler2D detail : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D noise : filter_linear_mipmap, repeat_enable;
uniform float detail_inches = 3.0;
uniform vec4 dirt : source_color = vec4(0.52, 0.42, 0.31, 1.0);
uniform float strength = 0.85;

varying vec3 world;

void vertex() {
	world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	vec3 g = texture(detail, world.xz / detail_inches).rgb;
	float lum = dot(g, vec3(0.3, 0.55, 0.15));
	ALBEDO = mix(g, vec3(lum) * 1.4 * dirt.rgb, 0.8);
	ROUGHNESS = 1.0;
	// Always lit as ground facing up, whichever way the triangles are wound (a back face flips NORMAL and goes dark).
	NORMAL = normalize((VIEW_MATRIX * vec4(0.0, 1.0, 0.0, 0.0)).xyz);
	float n = texture(noise, world.xz / 14.0).r;
	float edge = COLOR.a;
	ALPHA = clamp(smoothstep(0.0, 1.0, edge * 1.6 - (1.0 - n) * 0.9), 0.0, 1.0) * strength;
}
