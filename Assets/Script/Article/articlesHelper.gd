extends Node
class_name articlesHelper

var neutral: Array[Variant]  = []
var opponent: Array[Variant] = []
var ally: Array[Variant]     = []


func _ready():
	for child in get_children():
		match child.name:
			"Neutral":
				neutral.append_array(child.get_children())
			"Opponent":
				opponent.append_array(child.get_children())
			"Ally":
				ally.append_array(child.get_children())
		
func getArticle(article: String) -> Array[Variant]:
	match article:
		"Neutral":
			return neutral
		"Opponent":
			return opponent
		"Ally":
			return ally
	return []

func getAllArticles() -> Array[Variant]:
	return neutral + opponent + ally
