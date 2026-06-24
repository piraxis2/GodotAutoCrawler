@tool
class_name DialogueEditorAdapter extends RefCounted

# 노드의 *에디터 UI* 책임을 DialogueDefinition에서 분리하는 공통 인터페이스.
# Definition은 _node_init/_capture를 자신의 어댑터에 위임하므로, UI를 만들고
# 읽는 코드가 최종적으로 Definition 밖으로 빠져나갈 수 있다.
#
# 모든 것을 단순한 `params` Dictionary로 표현한다 — 런타임(get_runtime_params)과
# 같은 형태라서 에디터/런타임 표현이 정렬되고 직렬화에도 유리하다.

# `params`로부터 `node`의 에디터 UI를 구성한다 (_node_init에서 호출).
func apply_params(_node: DialogueNode, _params: Dictionary) -> void:
	pass


# 에디터 UI를 다시 params Dictionary로 읽어온다 (_capture에서 호출).
func capture_params(_node: DialogueNode) -> Dictionary:
	return {}
