@tool
class_name BranchDef extends FlowDefinition

func _get_dialogue_node() -> String:
	return "res://addons/dialogtool/Node/branch_node.tscn"

func _node_init(node: DialogueNode) -> void:
	return

func _capture() ->void:
	pass

func _run() ->void:
	pass
