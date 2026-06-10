@tool
class_name TestDef extends DialogueDefinition

func _node_init(node: DialogueNode) -> void:
	node.set_slot(0, true, node.port_type.data, node.color_dic["input"], false, 0, Color.WHITE)
	var tempbutton = Button.new()
	tempbutton.text = "test"
	var tempfunc =  func(): print(node.get_connected_node(true, 0).definition._get_data_output(0))
	tempbutton.pressed.connect(tempfunc)
	node.add_child(tempbutton)
	pass

func _capture(node: DialogueNode) ->void:
	pass
