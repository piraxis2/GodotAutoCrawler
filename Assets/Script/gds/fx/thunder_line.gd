extends Node2D

@onready var thunderline : Line2D = $Line2D
@onready var animation_player : AnimationPlayer = $AnimationPlayer

func _ready() -> void:
	animation_player.animation_finished.connect(_animationfinished)
	
func _animationfinished(name: StringName):
	if name == "end":
		queue_free()	

func showLine(points: Array) :
	var localpoints : Array
	for point in points:
		localpoints.append(thunderline.to_local(point))
	
	thunderline.points = localpoints
	animation_player.play("end", -1, 0)
	
