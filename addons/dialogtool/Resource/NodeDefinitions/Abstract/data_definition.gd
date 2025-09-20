@tool
@abstract class_name DataDefinition extends NodeDefinition

var value: Variant:
	get:
		return _get_data_output(0)

@abstract func _get_data_output(port: int) -> Variant
