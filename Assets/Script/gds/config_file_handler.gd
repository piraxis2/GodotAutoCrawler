extends Node

var config = ConfigFile.new()
const SETTINGS_FILE_PATH = "user://settings.ini"


func _ready() -> void:
	if !FileAccess.file_exists(SETTINGS_FILE_PATH):
		config.set_value("video", "resolution", Vector2(1920, 1080))
		config.save(SETTINGS_FILE_PATH)
	else:
		config.load(SETTINGS_FILE_PATH)


func save_video_setting(key: String, value) -> void:
	config.set_value("video", key, value)
	config.save(SETTINGS_FILE_PATH)

func load_video_setting() -> Dictionary:
	var video_setting = {}
	for key in config.get_section_keys("video"):
		video_setting[key] = config.get_value("video", key)
	return video_setting
