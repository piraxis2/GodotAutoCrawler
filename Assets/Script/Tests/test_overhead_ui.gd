extends Control

# ST-001 테스트용 OverheadUi test double.
# Health/Mana가 호출하는 init_health / set_health / init_mana / set_mana 계약만 만족한다.
# production overhead_ui.gd와 달리 HealthBar/ManaBar child나 get_tree() 타이머(await)에
# 의존하지 않아 SceneTree 밖 fixture에서도 SCRIPT ERROR 없이 동작한다.

var max_health := 0
var health := 0
var max_mana := 0
var mana := 0

var init_health_calls := 0
var set_health_calls := 0
var init_mana_calls := 0
var set_mana_calls := 0


func init_health(new_max_health: int) -> void:
	init_health_calls += 1
	max_health = new_max_health
	health = new_max_health


func set_health(new_health: int) -> void:
	set_health_calls += 1
	health = clamp(new_health, 0, max_health)


func init_mana(new_max_mana: int, default_mana: int) -> void:
	init_mana_calls += 1
	max_mana = new_max_mana
	mana = clamp(default_mana, 0, new_max_mana)


func set_mana(new_mana: int) -> void:
	set_mana_calls += 1
	mana = clamp(new_mana, 0, max_mana)
