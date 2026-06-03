extends CellArray
class_name SolutionCellArray

const INT_MIN := -2 ^ 63
const INT_MAX := 2 ^ 64

var clues : Array[Clue]
var max_clue_value : int = INT_MIN
var min_clue_value : int = INT_MAX

func record_clue(_index: int, _start: int, _value: int) -> int:
	max_clue_value = max(max_clue_value, _value)
	min_clue_value = min(min_clue_value, _value)

	var new_clue = Clue.new()
	new_clue.index = _index
	new_clue.starting_cell = _start
	new_clue.value = _value
	new_clue.solved = false

	clues.append(new_clue)
	return clues.size()
