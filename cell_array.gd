# represents a row/column in the puzzle
class_name CellArray

var length : int		# number of cells in the row
var marked_cells : int	# bit-mask representing the marked cells
var filled_cells : int	# bit-mask representing the filled-in cells

func _init(num_cells: int) -> void:
	length = num_cells
	marked_cells = 0
	filled_cells = 0

func is_cell_marked(cell: int) -> bool:
	return marked_cells & (1<<cell)

func is_cell_filled(cell: int) -> bool:
	return filled_cells & (1<<cell)

func mark_cell(cell: int) -> void:
	marked_cells = marked_cells | (1<<cell)

func unmark_cell(cell: int) -> void:
	marked_cells = marked_cells & ~(1<<cell)

func fill_cell(cell: int) -> void:
	unmark_cell(cell)
	filled_cells = filled_cells | (1<<cell)

func empty_cell(cell: int) -> void:
	filled_cells = filled_cells & ~(1<<cell)

func mark_empty_cells() -> void:
	marked_cells = ~filled_cells

func equals(other: CellArray) -> bool:
	var match_filled := filled_cells == other.filled_cells
	var match_marked := marked_cells == other.marked_cells
	return match_filled and match_marked
