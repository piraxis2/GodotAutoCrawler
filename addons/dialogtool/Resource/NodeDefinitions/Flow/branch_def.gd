@tool
class_name BranchDef extends FlowDefinition


func get_runtime_type() -> StringName:
	return &"branch"


func _get_dialogue_node() -> String:
	return "res://addons/dialogtool/Node/branch_node.tscn"

func _is_done() -> bool:
	return true
	
func execute(dialogue_player: Node) -> FlowDefinition:
	return null	

func _node_init(node: DialogueNode) -> void:
	var adapter := get_editor_adapter()
	if adapter:
		adapter.apply_params(node, {})

func _capture(node: DialogueNode) ->void:
	pass

	
func get_next_flow() -> FlowDefinition:
	var port: int = 0 if get_connected(true, 0).value else 1
	return get_connected(false, port)
	
