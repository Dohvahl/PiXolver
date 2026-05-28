extends CellArray
class_name SolutionCellArray

class Clue:
	func _init() -> void:
		pass

	var index : int				# index of the clue in the clues array
	var starting_cell : int		# first index of the clue in thee row/column
	var value : int				# the clue value

	var solved : bool			# whether or not this clue has been filled in

var clues : Array[Clue]

func record_clue(_index: int, _start: int, _value: int) -> void:
	var new_clue = Clue.new()
	new_clue.index = _index
	new_clue.starting_cell = _start
	new_clue.value = _value
	new_clue.solved = false

	clues.append(new_clue)
