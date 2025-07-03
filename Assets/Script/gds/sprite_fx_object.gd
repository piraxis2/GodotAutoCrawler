extends Node

@onready var aniplayer : AnimationPlayer  = $AnimationPlayer

func playanimation(ani_name:StringName) -> void:
	aniplayer.play(ani_name)
	await aniplayer.animation_finished
	queue_free()
