@tool
@abstract class_name FlowDefinition extends DialogueDefinition

var is_done: bool:
	get: return _is_done()

@abstract func _is_done() -> bool
@abstract func execute(dialogue_player: Node) -> FlowDefinition 


func get_next_flow() -> FlowDefinition:
	var flow = get_connected(false, 0)
	if flow is FlowDefinition:
		return flow
	
	return null 
	
	
