extends ProgressBar

@onready var damage_bar = $DamageBar

var my_value = 0 : set = _set_value

# 노출 여부는 overhead_ui가 결정한다(HP/MP 중 하나라도 쓰이면 둘 다 노출). 여기서 켜면
# 형제 바보다 먼저 켜져 VBoxContainer 레이아웃에 혼자 끼어든다.
func _set_value(new_my_value):
	var prev_value = my_value
	my_value = min(max_value, new_my_value)
	value = my_value

	if my_value <= 0:
		return

	if my_value < prev_value:
		_damage_bar_affect(my_value)
	else:
		damage_bar.value = my_value



func init_ui(new_max_value, default_value):
	max_value = new_max_value
	damage_bar.max_value = new_max_value
	my_value = default_value
	damage_bar.value = my_value
	set_visible(false);


func _damage_bar_affect(_my_value) -> void:
	await get_tree().create_timer(0.2).timeout
	damage_bar.value = _my_value
