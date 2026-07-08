extends Control

@onready var hp_bar = $VBoxContainer/HealthBar
@onready var mana_bar = $VBoxContainer/ManaBar

# 대응하는 StatusElement가 있어서 init_*로 초기화된 바만 노출 대상이다.
var _has_health = false
var _has_mana = false
var _revealed = false


func _ready():
	# 초기화 전에는 씬에 박힌 더미 값이 보이지 않도록 숨긴 채로 시작한다.
	hp_bar.set_visible(false)
	mana_bar.set_visible(false)


func init_health(max_health):
	_has_health = true
	hp_bar.init_ui(max_health, max_health)

func init_mana(max_mana, default_mana):
	_has_mana = true
	mana_bar.init_ui(max_mana, default_mana)



func set_health(health):
	# ProgressBar.set_value()가 아니라 my_value setter를 거쳐야 DamageBar 연출이 돈다.
	hp_bar.my_value = health
	_reveal()

func set_mana(mana):
	mana_bar.my_value = mana
	_reveal()


# HP든 MP든 처음 값이 바뀌는 순간 가진 바를 한꺼번에 노출한다. 하나만 먼저 켜면
# VBoxContainer가 숨은 형제를 레이아웃에서 빼기 때문에 뒤늦게 켜진 바가 자리를 옮긴다.
func _reveal():
	if _revealed:
		return
	_revealed = true
	hp_bar.set_visible(_has_health)
	mana_bar.set_visible(_has_mana)
