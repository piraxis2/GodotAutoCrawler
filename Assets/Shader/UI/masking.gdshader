shader_type canvas_item;

// --- 원본 텍스처 조절용 변수 ---
uniform float texture_scale : hint_range(0.1, 5.0, 0.01) = 1.0;
uniform vec2 texture_offset = vec2(0.0, 0.0);

// --- 마스크용 변수 ---
uniform sampler2D mask_texture;

uniform float mask_scale : hint_range(0.1, 5.0, 0.01) = 1.0;
uniform vec2 mask_offset = vec2(0.0, 0.0);



void fragment() {
    // 1. 원본 텍스처의 UV를 변형합니다.
    vec2 transformed_uv = (UV - vec2(0.5)) / texture_scale + vec2(0.5) - texture_offset;

    // 2. (★수정된 부분) UV 좌표가 0.0 ~ 1.0 범위를 벗어나지 않도록 고정합니다.
    vec2 clamped_uv = clamp(transformed_uv, vec2(0.0), vec2(1.0));

    // 3. 이제 '반복'되지 않는 clamped_uv를 사용하여 텍스처 색상을 가져옵니다.
    vec4 original_color = texture(TEXTURE, clamped_uv);

    // 4. 마스크 텍스처의 R 채널 값을 알파 마스크로 사용합니다.

	float frame_mask = texture(mask_texture, UV).r;
	float pattern_mask = texture(mask_texture, UV + mask_offset).r;
	float final_mask = frame_mask * pattern_mask;
    // 5. 원본 색상의 알파 값에 마스크 값을 곱합니다.
    original_color.a *= final_mask;

    // 6. 최종 색상을 적용합니다.
    COLOR = original_color;
}