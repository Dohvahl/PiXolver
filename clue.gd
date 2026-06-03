class_name Clue

func _init() -> void:
	pass

var index : int				# index of the clue in the clues array
var starting_cell : int		# first index of the clue in thee row/column
var value : int				# the clue value

var solved : bool			# whether or not this clue has been filled in
