func _init(in_index: int, in_start: int, in_value: int) -> void:
	_index = in_index
	_starting_cell = in_start
	_value = in_value
	_solved = false

func reset() -> void:
	_solved = false

func toggle_solved() -> void:
	_solved = !_solved

func is_solved() -> bool:
	return _solved

var _index : int				# index of the clue in the clues array
var _starting_cell : int		# first index of the clue in thee row/column
var _value : int				# the clue value

var _solved : bool			# whether or not this clue has been filled in
