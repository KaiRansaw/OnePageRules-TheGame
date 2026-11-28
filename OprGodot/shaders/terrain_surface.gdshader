// Terrain surfaces (Kai, 2026-11-27: real materials for water, swamp, lava, fields and hills - no made-up looks).
// The pictures are CC0 photo textures (terrain/surfaces/License.txt); this shader only lays them on the piece's shape:
// world-position UVs so every piece tiles the same way whatever its size, and the vertex colour's alpha fades the
// piece's outer edge into the mat.
//   mode 0: ground (albedo + normal map)
//   mode 1: water - two drifting ripple normals over the bed picture, darker and clearer with depth
//   mode 2: lava - slowly flowing, glowing (emission picture), cooling to crust at the edge
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_disabled;

uniform int mode = 0;
uniform sampler2D albedo_tex : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D normal_tex : hint_normal, filter_linear_mipmap, repeat_enable;
uniform sampler2D emission_tex : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D crust_tex : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D water_normal : hint_normal, filter_linear_mipmap, repeat_enable;
uniform float tile_inches = 6.0;
uniform vec4 tint : source_color = vec4(1.0);
uniform vec4 water_colour : source_color = vec4(0.10, 0.26, 0.33, 1.0);
uniform float depth = 0.5;          // water: 0 = clear shallow, 1 = deep
uniform float glow = 2.2;           // lava: emission strength
uniform float flow = 0.04;          // lava / water: drift speed, tiles per second

varying vec3 world_pos;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	vec2 uv = world_pos.xz / tile_inches;
	float edge = COLOR.a;
	if (mode == 1) {
		// Water: the bed shows through the shallows; two ripple layers drift across each other.
		vec3 bed = texture(albedo_tex, uv * 0.8).rgb * tint.rgb;
		float d = clamp(depth * (0.55 + 0.45 * edge), 0.0, 1.0);
		vec2 r1 = uv * 0.6 + vec2(TIME * flow, TIME * flow * 0.6);
		vec2 r2 = uv * 0.9 - vec2(TIME * flow * 0.7, -TIME * flow * 0.4);
		vec3 n1 = texture(water_normal, r1).rgb, n2 = texture(water_normal, r2).rgb;
		NORMAL_MAP = normalize(mix(n1, n2, 0.5));
		NORMAL_MAP_DEPTH = 0.6;
		vec3 col = mix(bed, water_colour.rgb, mix(0.6, 0.88, d));
		ALBEDO = col;
		// A little of the water's own colour as light, so it reads as water (not a hole) under any sky or angle.
		EMISSION = water_colour.rgb * 0.1;
		ROUGHNESS = 0.12;
		METALLIC = 0.0;
		SPECULAR = 0.5;
		ALPHA = mix(0.82, 0.97, d) * smoothstep(0.0, 1.0, edge);
	} else if (mode == 2) {
		// Lava: the molten picture flows slowly; near the edge it cools into the crust picture.
		vec2 f = uv * 0.7 + vec2(TIME * flow, TIME * flow * 0.35);
		vec3 molten = texture(albedo_tex, f).rgb;
		vec3 hot = texture(emission_tex, f).rgb;
		vec3 crust = texture(crust_tex, uv).rgb;
		float cool = 1.0 - smoothstep(0.15, 0.85, edge);
		ALBEDO = mix(molten, crust, cool) * tint.rgb;
		EMISSION = hot * glow * (1.0 - cool * 0.85) * (0.85 + 0.15 * sin(TIME * 1.3 + world_pos.x * 0.4));
		NORMAL_MAP = texture(normal_tex, f).rgb;
		ROUGHNESS = mix(0.45, 0.9, cool);
		ALPHA = smoothstep(0.0, 0.35, edge);
	} else {
		ALBEDO = texture(albedo_tex, uv).rgb * tint.rgb;
		NORMAL_MAP = texture(normal_tex, uv).rgb;
		ROUGHNESS = 0.95;
		ALPHA = smoothstep(0.0, 0.6, edge);
	}
}
