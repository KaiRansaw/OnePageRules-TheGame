// The battlefield ground (Kai, 2026-11-18: "like the models are life size"). Laid out by WORLD position so the table and
// the land round it are one surface. Up to three ground pictures (a style's grounds, mixed per map - Kai, "both"):
//  - each at true miniature scale (detail_inches per repeat), sampled twice at a turned and shifted scale and blended by
//    noise, so the repeat doesn't show as wallpaper;
//  - the second and third grounds come through in big natural patches (mix_inches across), like bare dirt in a field;
//  - macro: big soft patches of drier / greener / darker ground over it all, so it reads as real land from afar.
shader_type spatial;
render_mode cull_back;

uniform sampler2D detail : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D detail2 : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D detail3 : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D detail_normal : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D detail2_normal : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D detail3_normal : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform int layers = 1;
uniform bool has_normal = false;
uniform sampler2D macro : filter_linear_mipmap, repeat_enable;
uniform float detail_inches = 3.0;
uniform float macro_inches = 420.0;
uniform float mix_inches = 110.0;
uniform float macro_strength = 1.0;
uniform bool mix_layers = true;
uniform vec4 tint : source_color = vec4(1.0);
uniform vec4 dry : source_color = vec4(0.92, 0.82, 0.62, 1.0);
uniform vec4 lush : source_color = vec4(0.82, 0.95, 0.7, 1.0);

varying vec3 world;

void vertex() {
	world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

vec3 two(sampler2D t, vec2 uv1, vec2 uv2, float blend) {
	vec3 a = texture(t, uv1).rgb;
	return mix_layers ? mix(a, texture(t, uv2).rgb, blend) : a;
}

void fragment() {
	vec2 p = world.xz;
	vec2 uv1 = p / detail_inches;
	float a = 0.61;
	mat2 turn = mat2(vec2(cos(a), sin(a)), vec2(-sin(a), cos(a)));
	vec2 uv2 = turn * p / (detail_inches * 1.31) + vec2(0.37, 0.71);
	float blend = mix_layers ? smoothstep(0.35, 0.65, texture(macro, p / (detail_inches * 7.0) + vec2(0.5)).r) : 0.0;

	vec3 col = two(detail, uv1, uv2, blend);
	vec3 nrm = has_normal ? two(detail_normal, uv1, uv2, blend) : vec3(0.5, 0.5, 1.0);
	// The other grounds show through in patches: their own noise, a soft edge, the second one more often than the third.
	if (layers >= 2) {
		float w2 = smoothstep(0.48, 0.62, texture(macro, p / mix_inches + vec2(0.21, 0.83)).r);
		col = mix(col, two(detail2, uv1, uv2, blend), w2);
		if (has_normal) nrm = mix(nrm, two(detail2_normal, uv1, uv2, blend), w2);
	}
	if (layers >= 3) {
		float w3 = smoothstep(0.56, 0.68, texture(macro, p / (mix_inches * 0.8) + vec2(0.67, 0.12)).r);
		col = mix(col, two(detail3, uv1, uv2, blend), w3);
		if (has_normal) nrm = mix(nrm, two(detail3_normal, uv1, uv2, blend), w3);
	}
	if (mix_layers) {
		float m = texture(macro, p / macro_inches).r;
		float m2 = texture(macro, p / (macro_inches * 0.27) + vec2(0.13, 0.42)).r;
		vec3 patchc = mix(dry.rgb, lush.rgb, smoothstep(0.3, 0.7, m));
		col *= mix(vec3(1.0), patchc, macro_strength);
		col *= mix(1.0, mix(0.82, 1.08, m2), macro_strength);
	}
	ALBEDO = col * tint.rgb * COLOR.rgb;
	ROUGHNESS = 0.95;
	if (has_normal) {
		NORMAL_MAP = nrm;
		NORMAL_MAP_DEPTH = 0.6;
	}
}
