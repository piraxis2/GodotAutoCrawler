@tool
@abstract class_name DataDefinition extends DialogueDefinition

var value: Variant:
	get:
		return _get_data_output(0)

@abstract func _get_data_output(port: int) -> Variant
