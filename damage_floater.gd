extends Node


func display(damage: int, position: Vector2, is_critical: bool):
	var number = Label.new()
	number.global_position = position - Vector2(0,10)
	number.text = str(damage)
	number.z_index = 5
	number.texture_filter = CanvasItem.TEXTURE_FILTER_NEAREST
	number.label_settings = LabelSettings.new()
	
	var color = "#FFF"
	if is_critical:
		color = "#B22"
	if damage == 0:
		color  = "#FFF8"
		
	number.label_settings.font_color = color
	number.label_settings.font_size = 10
	number.label_settings.outline_color = "#000"
	number.label_settings.outline_size = 2
	
	
	call_deferred("add_child", number)
	
	await number.resized
	number.pivot_offset = Vector2(number.size / 2)
	
	var tween = get_tree().create_tween()
	tween.tween_property(number, "position", number.position - Vector2(-1,15), 0.15).set_ease(Tween.EASE_OUT)
	tween.tween_property(number, "position", number.position + Vector2(3,-5), 0.5).set_ease(Tween.EASE_OUT).set_trans(Tween.TRANS_BOUNCE)
	tween.tween_property(number, "scale", Vector2.ZERO, 0.15).set_ease(Tween.EASE_IN).set_delay(0.15)
	
	await tween.finished
	tween.kill();
	number.queue_free()
	
	
	
		
