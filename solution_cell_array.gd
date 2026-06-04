extends CellArray
class_name SolutionCellArray

const INT_MIN := -2 ^ 63
const INT_MAX := 2 ^ 64

var clues : Array[Clue]
var max_clue_value : int = INT_MIN
var min_clue_value : int = INT_MAX

func record_clue(in_index: int, in_start: int, in_value: int) -> int:
	max_clue_value = max(max_clue_value, in_value)
	min_clue_value = min(min_clue_value, in_value)

	clues.append(Clue.new(in_index, in_start, in_value))
	return clues.size()
