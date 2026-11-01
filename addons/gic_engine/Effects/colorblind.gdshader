// Фильтр для дальтоников (ответ 37). Метод «дальтонизации»: вычисляем, как цвет видит человек
// с данным типом дальтонизма, и переносим потерянное различие в каналы, которые он различает лучше.
shader_type canvas_item;

uniform sampler2D screen_tex : hint_screen_texture, filter_linear;
// 1 — протанопия, 2 — дейтеранопия, 3 — тританопия.
uniform int mode = 0;
// Сила коррекции 0..1.
uniform float amount : hint_range(0.0, 1.0) = 1.0;

// Перевод RGB ↔ LMS (пространство колбочек глаза).
const mat3 RGB_TO_LMS = mat3(
	vec3(17.8824, 3.45565, 0.0299566),
	vec3(43.5161, 27.1554, 0.184309),
	vec3(4.11935, 3.86714, 1.46709));
const mat3 LMS_TO_RGB = mat3(
	vec3(0.0809444479, -0.0102485335, -0.000365296938),
	vec3(-0.130504409, 0.0540193266, -0.00412161469),
	vec3(0.116721066, -0.113614708, 0.693511405));

void fragment() {
	vec3 rgb = texture(screen_tex, SCREEN_UV).rgb;
	vec3 lms = RGB_TO_LMS * rgb;
	vec3 sim = lms;
	if (mode == 1) {
		sim = vec3(2.02344 * lms.y - 2.52581 * lms.z, lms.y, lms.z);
	} else if (mode == 2) {
		sim = vec3(lms.x, 0.494207 * lms.x + 1.24827 * lms.z, lms.z);
	} else if (mode == 3) {
		sim = vec3(lms.x, lms.y, -0.395913 * lms.x + 0.801109 * lms.y);
	}
	vec3 seen = LMS_TO_RGB * sim;
	vec3 error = rgb - seen;
	// Перераспределение ошибки в каналы, которые человек различает.
	vec3 shift = vec3(0.0, error.r * 0.7 + error.g, error.r * 0.7 + error.b);
	vec3 corrected = clamp(rgb + shift, 0.0, 1.0);
	COLOR = vec4(mix(rgb, corrected, mode == 0 ? 0.0 : amount), 1.0);
}
