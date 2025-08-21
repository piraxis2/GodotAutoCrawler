extends Node2D

@onready var thunderline : Line2D = $Line2D
@onready var animation_player : AnimationPlayer = $AnimationPlayer



func showLine(points: Array) :
	var localpoints : Array
	for point in points:
		localpoints.append(thunderline.to_local(point))
	
	thunderline.points = localpoints
	animation_player.play("discharge", -1, 0)
	
