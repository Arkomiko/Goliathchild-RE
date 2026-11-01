// Глитч-эффект: экран режется на горизонтальные полосы, часть полос сдвигается вбок,
// цветовые каналы расходятся. Воспроизводит эффект Glitch из прототипа (автор оригинала для Ren'Py — Vladya),
// но написан заново как шейдер Godot.
shader_type canvas_item;

// Копия того, что уже нарисовано на экране под этим прямоугольником.
uniform sampler2D screen_tex : hint_screen_texture, filter_nearest;
// Сила эффекта 0..1. 0 — эффект выключен.
uniform float strength : hint_range(0.0, 1.0) = 0.0;
// Случайное зерно: меняется кодом каждый кадр, чтобы полосы «дёргались».
uniform float seed = 0.0;
// Сколько полос по высоте экрана.
uniform float bands = 28.0;

float rand(vec2 co) {
	return fract(sin(dot(co, vec2(12.9898, 78.233))) * 43758.5453);
}

void fragment() {
	vec2 uv = SCREEN_UV;
	float band = floor(uv.y * bands);
	// Какие полосы сдвигаются: чем сильнее эффект, тем больше таких полос.
	float active = step(1.0 - strength * 0.85, rand(vec2(band, seed)));
	float shift = (rand(vec2(seed, band * 1.37)) - 0.5) * 0.25 * strength * active;
	vec2 suv = vec2(fract(uv.x + shift), uv.y);
	// Расхождение каналов: красный и синий смещаются в разные стороны.
	float split = 0.012 * strength * (0.4 + active);
	vec3 color;
	color.r = texture(screen_tex, suv + vec2(split, 0.0)).r;
	color.g = texture(screen_tex, suv).g;
	color.b = texture(screen_tex, suv - vec2(split, 0.0)).b;
	// Редкие «битые» полосы инвертируются — как в оригинальном эффекте.
	float broken = step(0.985, rand(vec2(band * 3.1, seed + 7.0))) * step(0.2, strength);
	color = mix(color, vec3(1.0) - color, broken);
	COLOR = vec4(color, 1.0);
}
