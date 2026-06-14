# DT-006 Step 2 검증용 헤드리스 테스트.
# 실행:
#   godot --headless --path <project> res://Assets/Script/gds/world_state/tests/dt006_step2_autoload_test.tscn
#
# 검증 범위:
# - 부팅 시 단일 autoload `/root/WorldState`가 ready이며 bootstrap default를 읽을 수 있다
# - autoload 이름이 class_name 및 기존 autoload와 충돌하지 않는다
# - invalid Schema는 명시적 not-ready로 관찰된다(주입 인스턴스, autoload와 분리)
# - transient scene churn에도 autoload가 중복 생성·재초기화되지 않는다
extends Node

const VT := StateDefinition.StateValueType
const AUTOLOAD := "/root/WorldState"

var _failures: int = 0


func _ready() -> void:
	_test_autoload_ready()
	_test_single_instance_no_collision()
	_test_invalid_schema_fixture()
	await _test_persist_across_transient_scene()

	if _failures == 0:
		print("[DT-006 Step2] ALL PASS")
		get_tree().quit(0)
	else:
		print("[DT-006 Step2] FAILED: %d assertion(s)" % _failures)
		get_tree().quit(1)


func _check(name: String, actual, expected) -> void:
	if str(actual) == str(expected):
		print("  PASS %s -> %s" % [name, str(actual)])
	else:
		_failures += 1
		print("  FAIL %s -> got %s, expected %s" % [name, str(actual), str(expected)])


func _check_true(name: String, cond: bool) -> void:
	_check(name, cond, true)


# --- 시나리오 ---------------------------------------------------------

func _test_autoload_ready() -> void:
	print("[A] autoload ready + bootstrap default")
	var ws = get_node_or_null(AUTOLOAD)
	_check_true("A.exists", ws != null)
	if ws == null:
		return
	_check_true("A.is_store", ws is WorldStateStore)
	_check("A.ready", ws.is_store_ready(), true)
	_check("A.stage_default", ws.get_value(&"quest.main.stage"), 0)
	_check("A.health_default", ws.get_value(&"player.health"), 100.0)
	_check("A.channel_typeof", typeof(ws.get_value(&"world.build.channel")), TYPE_STRING_NAME)


func _test_single_instance_no_collision() -> void:
	print("[B] 단일 인스턴스 + 이름 충돌 없음")
	# root 직속 자식 중 'WorldState' 이름은 정확히 1개.
	var count := 0
	for child in get_tree().root.get_children():
		if child.name == "WorldState":
			count += 1
	_check("B.single", count, 1)
	# 기존 autoload와 공존·구분.
	_check_true("B.dialogue_manager", get_node_or_null("/root/DialogueManager") != null)
	_check_true("B.distinct", get_node_or_null(AUTOLOAD) != get_node_or_null("/root/DialogueManager"))
	# autoload는 현재 scene이 아니라 root에 parented(scene 교체와 독립).
	_check_true("B.root_parented", get_node_or_null(AUTOLOAD).get_parent() == get_tree().root)


func _test_invalid_schema_fixture() -> void:
	print("[C] invalid Schema는 명시적 not-ready (주입 인스턴스)")
	# autoload와 섞지 않는 별도 주입 Store. invalid key 'Bad' -> schema invalid.
	var bad := StateSchema.new()
	var d := StateDefinition.new()
	d.key = &"Bad"
	d.value_type = VT.INT
	d.default_value = 0
	var defs: Array[StateDefinition] = [d]
	bad.definitions = defs
	var store := WorldStateStore.new()
	store.schema = bad
	add_child(store)  # _ready -> initialize -> invalid
	_check("C.not_ready", store.is_store_ready(), false)
	_check("C.get_null", store.get_value(&"quest.main.stage"), null)
	# autoload는 영향 없음.
	_check("C.autoload_still_ready", get_node_or_null(AUTOLOAD).is_store_ready(), true)
	store.queue_free()


func _test_persist_across_transient_scene() -> void:
	print("[D] transient scene churn에도 autoload 미재생성·미재초기화")
	var ws = get_node_or_null(AUTOLOAD)
	ws.set_value(&"quest.main.stage", 5)
	# transient 자식(맵/scene 수명주기 흉내)을 붙였다 제거.
	var transient := Node.new()
	add_child(transient)
	transient.queue_free()
	await get_tree().process_frame
	# 같은 인스턴스가 값과 ready를 유지(재초기화되면 default 0으로 돌아갔을 것).
	var ws2 = get_node_or_null(AUTOLOAD)
	_check_true("D.same_instance", ws2 == ws)
	_check("D.value_persists", ws2.get_value(&"quest.main.stage"), 5)
	_check("D.still_ready", ws2.is_store_ready(), true)
	ws.set_value(&"quest.main.stage", 0)  # 정리(같은 프로세스 내 다른 검사 영향 방지)
