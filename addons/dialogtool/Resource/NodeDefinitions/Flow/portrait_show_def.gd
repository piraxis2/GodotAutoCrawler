@tool
class_name PortraitShowDef extends PortraitDef

# Portrait Show: slot에 texture_path 이미지를 표시한다. actor/expression은 메타데이터.
# 노드 목록 표시 이름: "PortraitShow" (get_global_name().left(-3)).


func get_runtime_type() -> StringName:
	return &"portrait_show"
