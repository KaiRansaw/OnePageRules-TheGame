// The battlefield ground (Kai, 2026-11-18: "like the models are life size"). Two layers, laid out by WORLD position so the
// table and the land round it are one surface:
//  - detail: the ground picture at true miniature scale (detail_inches per repeat), sampled twice at a turned and
//    shifted scale and blended by noise, so the repeat doesn't show as wallpaper;
//  - macro: big soft patches of drier / greener / darker ground across the field, so it reads as real land from afar.
shader_type spatial;
render_mode cull_back;

uniform sampler2D detail : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D detail_normal : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform bool has_normal = false;
uniform sampler2D macro : filter_linear_mipmap, repeat_enable;
uniform float detail_inches = 60.0;
uniform float macro_inches = 420.0;
uniform float macro_strength = 1.0;
uniform bool mix_layers = true;
uniform vec4 tint : source_color = vec4(1.0);
uniform vec4 dry : source_color = vec4(0.92, 0.82, 0.62, 1.0);
uniform vec4 lush : source_color = vec4(0.82, 0.95, 0.7, 1.0);

varying vec3 world;

void vertex() {
	world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	vec2 p = world.xz;
	vec2 uv1 = p / detail_inches;
	float a = 0.61;
	mat2 turn = mat2(vec2(cos(a), sin(a)), vec2(-sin(a), cos(a)));
	vec2 uv2 = turn * p / (detail_inches * 1.31) + vec2(0.37, 0.71);
	vec3 c1 = texture(detail, uv1).rgb;
	vec3 col = c1;
	float blend = 0.0;
	if (mix_layers) {
		blend = smoothstep(0.35, 0.65, texture(macro, p / (detail_inches * 7.0) + vec2(0.5)).r);
		col = mix(c1, texture(detail, uv2).rgb, blend);
		float m = texture(macro, p / macro_inches).r;
		float m2 = texture(macro, p / (macro_inches * 0.27) + vec2(0.13, 0.42)).r;
		vec3 patchc = mix(dry.rgb, lush.rgb, smoothstep(0.3, 0.7, m));
		col *= mix(vec3(1.0), patchc, macro_strength);
		col *= mix(1.0, mix(0.82, 1.08, m2), macro_strength);
	}
	ALBEDO = col * tint.rgb * COLOR.rgb;
	ROUGHNESS = 0.95;
	if (has_normal) {
		vec3 n1 = texture(detail_normal, uv1).rgb;
		NORMAL_MAP = mix_layers ? mix(n1, texture(detail_normal, uv2).rgb, blend) : n1;
		NORMAL_MAP_DEPTH = 0.6;
	}
}
