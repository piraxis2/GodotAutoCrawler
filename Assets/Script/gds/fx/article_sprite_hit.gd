extends AnimatedSprite2D

func hit() ->void :
	material.set_shader_parameter("active", true)
	await get_tree().create_timer(0.175).timeout
	material.set_shader_parameter("active", false)
