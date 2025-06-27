extends Node

@onready var aniplayer : AnimationPlayer  = $AnimationPlayer

func _ready() -> void:
	aniplayer.animation_finished.connect(on_animation_finished)
	
func on_animation_finished(ani_name:StringName) -> void:
	if ani_name == "IceBolt":
		queue_free()
		pass

func playanimation(ani_name:StringName) -> void:
	aniplayer.play(ani_name)
