@tool
extends "res://addons/dialogtool/Editor/Adapter/dialogue_editor_adapter.gd"

# Branch 노드는 슬롯이 .tscn에 정의돼 있어 빌드/캡처할 에디터 UI가 없다.
# (BranchDef._node_init/_capture는 둘 다 no-op이었다.) 기본 동작을 그대로 쓴다.
