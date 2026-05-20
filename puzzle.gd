class_name Puzzle

var grid_size: int
var cells: Array[bool]
var row_clues: Dictionary[int, Array]
var col_clues: Dictionary[int, Array]

func _init(init_grid_size: int, init_row_clues: Dictionary[int, Array], init_col_clues: Dictionary[int, Array]) -> void:
	grid_size = init_grid_size
	
	cells = []
	cells.resize(grid_size * grid_size)
	cells.fill(0) # Puzzle starts as empty

func is_cell_filled(cell_index: int) -> bool:
	assert(cell_index >= 0 and cell_index < grid_size * grid_size, "Cell Index outside of grid bounds: %d" % cell_index)
	return cells[cell_index]

func fill_cell(cell_index: int) -> bool:
	if !is_valid_cell_index(cell_index):
		return false
	
	cells[cell_index] = true
	return true

func toggle_cell(cell_index: int) -> bool:
	if !is_valid_cell_index(cell_index):
		return false
		
	cells[cell_index] = !cells[cell_index]
	return true

func is_valid_cell_index(cell_index: int) -> bool:
	if cell_index < 0:
		return false
		
	return cell_index < grid_size * grid_size
