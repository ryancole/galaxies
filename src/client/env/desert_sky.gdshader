// Thin-atmosphere desert sky: near-black overhead, a dusty glow low on the
// horizon, twin suns from the first two directional lights, and a big banded
// planet with a small moon. Simple and readable, per etc/art-style.md.
shader_type sky;

uniform vec3 zenith_color : source_color = vec3(0.004, 0.006, 0.014);
uniform vec3 horizon_color : source_color = vec3(0.26, 0.14, 0.075);
uniform vec3 ground_color : source_color = vec3(0.06, 0.04, 0.03);
uniform float sun_disc_degrees = 1.4;
uniform vec3 planet_direction = vec3(0.45, 0.42, -0.78);
uniform float planet_radius_degrees = 8.5;
uniform vec3 planet_light : source_color = vec3(0.78, 0.6, 0.44);
uniform vec3 planet_dark : source_color = vec3(0.52, 0.34, 0.26);
uniform vec3 moon_direction = vec3(0.12, 0.62, -0.78);
uniform float moon_radius_degrees = 1.8;

float hash(vec3 p) {
	p = fract(p * 0.3183099 + 0.1);
	p *= 17.0;
	return fract(p.x * p.y * p.z * (p.x + p.y + p.z));
}

// Draws a lit sphere of the given angular radius; returns coverage in w.
vec4 body(vec3 dir, vec3 centre, float radius_deg, vec3 sun, vec3 light_col, vec3 dark_col, bool bands) {
	vec3 c = normalize(centre);
	float r = radians(radius_deg);
	float ang = acos(clamp(dot(dir, c), -1.0, 1.0));
	if (ang > r) {
		return vec4(0.0);
	}
	vec3 right = normalize(cross(vec3(0.0, 1.0, 0.0), c));
	vec3 up = cross(c, right);
	vec2 uv = vec2(dot(dir, right), dot(dir, up)) / sin(r);
	float z = sqrt(max(1.0 - dot(uv, uv), 0.0));
	vec3 n = normalize(right * uv.x + up * uv.y - c * z);
	float lit = clamp(dot(n, sun) * 1.2 + 0.05, 0.0, 1.0);
	vec3 col = light_col;
	if (bands) {
		// Uneven bands of three tones, gently warped.
		float lat = uv.y + 0.12 * uv.x + 0.04 * sin(uv.x * 7.0 + uv.y * 3.0);
		float b = floor(lat * 4.5 + 0.35 * sin(lat * 13.0));
		float pick = fract(sin(b * 12.9898) * 43758.5453);
		col = pick < 0.4 ? light_col : (pick < 0.75 ? mix(light_col, dark_col, 0.5) : dark_col);
	}
	float edge = 1.0 - smoothstep(r * 0.985, r, ang);
	return vec4(col * (0.02 + lit), edge);
}

void sky() {
	vec3 dir = EYEDIR;
	float h = dir.y;
	// The thin air only glows in a low band; above it is nearly space.
	vec3 col = mix(horizon_color, zenith_color, sqrt(smoothstep(0.0, 0.3, h)));
	if (h < 0.0) {
		col = mix(horizon_color * 0.5, ground_color, clamp(-h * 5.0, 0.0, 1.0));
	}
	// Dust glow and discs for each sun.
	if (LIGHT0_ENABLED) {
		float d = max(dot(dir, LIGHT0_DIRECTION), 0.0);
		float low = 1.0 - smoothstep(0.0, 0.4, h);
		col += LIGHT0_COLOR * (pow(d, 10.0) * 0.12 * low + pow(d, 200.0) * 0.5);
	}
	if (!AT_CUBEMAP_PASS && h > -0.02) {
		// Sparse stars, visible through the thin air higher up.
		vec3 cell = floor(dir * 220.0);
		if (hash(cell) > 0.9985) {
			col += vec3(0.6) * smoothstep(0.05, 0.4, h);
		}
		vec4 p = body(dir, planet_direction, planet_radius_degrees, LIGHT0_DIRECTION, planet_light, planet_dark, true);
		col = mix(col, p.rgb, p.w);
		vec4 m = body(dir, moon_direction, moon_radius_degrees, LIGHT0_DIRECTION, vec3(0.6, 0.6, 0.62), vec3(0.0), false);
		col = mix(col, m.rgb, m.w);
	}
	if (LIGHT0_ENABLED && acos(clamp(dot(dir, LIGHT0_DIRECTION), -1.0, 1.0)) < radians(sun_disc_degrees)) {
		col = LIGHT0_COLOR * 18.0;
	}
	if (LIGHT1_ENABLED && acos(clamp(dot(dir, LIGHT1_DIRECTION), -1.0, 1.0)) < radians(sun_disc_degrees * 0.75)) {
		col = LIGHT1_COLOR * 14.0;
	}
	COLOR = col;
}
