// Still oasis water: an opaque, dark teal mirror with slow crossing ripples that glints in the low
// suns. The desert environment has sky reflections off, so the water mirrors a simple copy of the
// sky gradient itself, stronger at grazing angles. Crisp, not realistic (see docs/art-style.md).
shader_type spatial;
render_mode cull_back, specular_schlick_ggx;

uniform vec3 water_color : source_color = vec3(0.04, 0.17, 0.15);
uniform float ripple_scale = 0.22;
uniform float ripple_speed = 0.5;
uniform float ripple_strength = 0.035;
// Matches env/desert_sky.gdshader.
uniform vec3 horizon_color : source_color = vec3(0.26, 0.14, 0.075);
uniform vec3 zenith_color : source_color = vec3(0.004, 0.006, 0.014);
uniform float reflection = 1.4;

varying vec3 world_pos;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	vec2 p = world_pos.xz * ripple_scale;
	float t = TIME * ripple_speed;
	// Slope of a few long sine swells running different ways.
	vec2 slope = vec2(0.0);
	slope += vec2(0.8, 0.6) * cos(dot(p, vec2(0.8, 0.6)) + t);
	slope += vec2(-0.5, 0.86) * 0.7 * cos(dot(p, vec2(-0.5, 0.86)) * 1.7 - t * 1.3);
	slope += vec2(0.2, -0.98) * 0.5 * cos(dot(p, vec2(0.2, -0.98)) * 2.9 + t * 1.9);
	vec3 n = normalize(vec3(-slope.x * ripple_strength, 1.0, -slope.y * ripple_strength));
	NORMAL = normalize((VIEW_MATRIX * vec4(n, 0.0)).xyz);
	vec3 eye = normalize(world_pos - CAMERA_POSITION_WORLD);
	vec3 r = reflect(eye, n);
	vec3 sky = mix(horizon_color, zenith_color, smoothstep(0.0, 0.35, r.y));
	float fresnel = 0.04 + 0.96 * pow(1.0 - clamp(dot(-eye, n), 0.0, 1.0), 5.0);
	EMISSION = sky * fresnel * reflection;
	ALBEDO = water_color * (1.0 - fresnel);
	ROUGHNESS = 0.08;
	SPECULAR = 0.6;
}
