extends Node

@onready var aniplayer = $AnimationPlayer
@onready var audioStreamPlayer = $AudioStreamPlayer2D 

func playSound(fxName, format = "mp3")->void:
	var path = "res://Assets/Audio/{0}.{1}".format([fxName, format])
	var _audio = load(path)
	if _audio :
		var ani = aniplayer.get_animation("play") 
		ani.length = _audio.get_length()
		ani.clear()
		var track_idx =	ani.add_track(Animation.TrackType.TYPE_AUDIO)
		ani.track_set_path(track_idx, "AudioStreamPlayer2D")
		ani.audio_track_insert_key(track_idx, 0.0, _audio)
		aniplayer.animation_finished.connect(func(aniName) : if aniName == "play" : queue_free())
	
		aniplayer.play("play", -1, 0)
	else:
		print_debug("worng audio : ", path)
		queue_free()


	
	
	
	
