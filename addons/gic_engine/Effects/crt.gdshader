// «Киношный» слой поверх главного меню: плёночный шум, сканлайны и виньетка.
// Рисуется полупрозрачным поверх фона, сам по себе экран не читает — поэтому дешёвый.
shader_type canvas_item;

uniform float noise_amount : hint_range(0.0, 1.0) = 0.06;
uniform float scanline_amount : hint_range(0.0, 1.0) = 0.12;
uniform float vignette_amount : hint_range(0.0, 2.0) = 0.9;
uniform float scanline_count = 720.0;

float rand(vec2 co) {
	return fract(sin(dot(co, vec2(12.9898, 78.233))) * 43758.5453);
}

void fragment() {
	vec2 uv = UV;
	// Шум меняется каждые 1/24 секунды — как зерно киноплёнки.
	float frame = floor(TIME * 24.0);
	float n = rand(uv * vec2(1920.0, 1080.0) + frame);
	float scan = 0.5 + 0.5 * sin(uv.y * scanline_count * 3.14159);
	float vignette = smoothstep(0.35, 1.0, length(uv - 0.5) * 1.35) * vignette_amount;
	float dark = clamp(scan * scanline_amount + vignette, 0.0, 1.0);
	COLOR = vec4(vec3(n), n * noise_amount);
	// Затемнение рисуем отдельным слоем: смешиваем шум с чёрным по маске сканлайнов и виньетки.
	COLOR = mix(COLOR, vec4(0.0, 0.0, 0.0, 1.0), dark);
}
