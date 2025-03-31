extends ProgressBar

@onready var damage_bar = $DamageBar

var health = 0 : set = _set_health

func _set_health(new_health):
	var prev_heath = health
	health = min(max_value, new_health)
	value = health
	
	print("healthbar: ", health)
	
	if not is_visible():
		set_visible(true)
	
	if health <= 0:
		return	
		
	if health < prev_heath:
		_damage_bar_affect(health)
	else:
		damage_bar.value = health
		
		
	
func init_health(_health):
	health = _health
	max_value = health
	value = health
	damage_bar.max_value = health
	damage_bar.value = health
	set_visible(false);
	

func _damage_bar_affect(_health) -> void:
	await get_tree().create_timer(0.4).timeout
	damage_bar.value = _health
