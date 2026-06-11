@tool
extends "res://addons/dialogtool/Editor/Adapter/dialogue_editor_adapter.gd"

# Choice 노드의 캡처 (ChoiceDef._capture에서 추출). UI(슬롯/항목)는 choice_node.gd가
# 슬라이더로 직접 만들므로 apply_params는 비워 둔다.

func capture_params(node: DialogueNode) -> Dictionary:
	# 자식 순서대로 캡처해 N번째 ChoiceItem이 출력 포트 N에 대응되게 한다.
	var list: Array[String] = []
	for child in node.get_children():
		if child is ChoiceItem:
			list.append(child.text_edit.text)
	return {"choices": list}
