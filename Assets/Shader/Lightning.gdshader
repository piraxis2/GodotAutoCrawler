shader_type canvas_item;
render_mode blend_add, unshaded;

uniform float vanishingValue : hint_range(0.0, 1.0);
uniform vec2 speed;
uniform float pixelation_amount : hint_range(1.0, 100.0);
uniform sampler2D basic_texture : source_color, filter_nearest, repeat_enable;



void fragment() {
// Input:10
	vec4 n_out10p0 = COLOR;


// FloatParameter:9
	float n_out9p0 = vanishingValue;


// Input:5
	float n_out5p0 = TIME;


// Vector2Parameter:7
	vec2 n_out7p0 = speed;


// VectorOp:6
	vec3 n_out6p0 = vec3(n_out5p0) * vec3(n_out7p0, 0.0);


// UVFunc:4
	vec2 n_in4p1 = vec2(1.00000, 1.00000);
	vec2 n_out4p0 = vec2(n_out6p0.xy) * n_in4p1 + UV;


// FloatParameter:15
	float n_out15p0 = pixelation_amount;


// VectorOp:13
	vec2 n_out13p0 = n_out4p0 * vec2(n_out15p0);


// VectorFunc:12
	vec2 n_out12p0 = floor(n_out13p0);


// VectorOp:14
	vec2 n_out14p0 = n_out12p0 / vec2(n_out15p0);


	vec4 n_out3p0;
// Texture2D:3
	n_out3p0 = texture(basic_texture, n_out14p0);
	float n_out3p1 = n_out3p0.r;


// SmoothStep:8
	float n_in8p1 = 1.00000;
	float n_out8p0 = smoothstep(n_out9p0, n_in8p1, n_out3p1);


// VectorOp:11
	vec3 n_out11p0 = vec3(n_out10p0.xyz) * vec3(n_out8p0);


// Output:0
	COLOR.rgb = n_out11p0;


}
