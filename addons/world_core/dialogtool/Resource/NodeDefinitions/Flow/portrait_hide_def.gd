@tool
class_name PortraitHideDef extends PortraitDef

# Portrait Hide: slot의 Portrait를 숨긴다. UI는 slot/transition만 노출한다.
# texture_path/actor/expression 필드는 베이스에서 상속하지만 UI에 노출하지 않으며,
# 기존 Definition 값은 편집/재저장 시 보존된다.
# 노드 목록 표시 이름: "PortraitHide".


func get_runtime_type() -> StringName:
	return &"portrait_hide"
