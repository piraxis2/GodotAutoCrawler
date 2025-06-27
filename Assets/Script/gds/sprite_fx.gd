extends Node

@export var fxMap : Dictionary


func display_sprite_fx(fxname: StringName, position: Vector2) -> void :
	var fx =  fxMap.get(fxname)
	if not fx :
		pass
	var newfx = fx.instantiate()
	newfx.z_index = 4
	newfx.global_position = position
	newfx.call("playanimation", fxname)
	
	
