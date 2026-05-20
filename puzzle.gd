class_name Puzzle

var grid_size: int
var cells: Array[bool]
var solution: Array[bool]
var row_clues: Dictionary[int, Array]
var col_clues: Dictionary[int, Array]

func _init(init_grid_size: int, initial_state: String) -> void:
	grid_size = init_grid_size
	
	# initialize the puzzle that we'll use in normal play
	cells = []
	cells.resize(grid_size * grid_size)
	
	# initialize the "solved" version of the puzzle
	solution = []
	solution.resize(grid_size * grid_size)
	
	# set the "solved" state of the puzzle
	_initialize_solution(initial_state)

func is_cell_filled(cell_index: int) -> bool:
	assert(cell_index >= 0 and cell_index < grid_size * grid_size, "Cell Index outside of grid bounds: %d" % cell_index)
	return cells[cell_index]

func toggle_cell(cell_index: int) -> bool:
	if !is_valid_cell_index(cell_index):
		return false
		
	cells[cell_index] = !cells[cell_index]
	return true
	
func cell_index_from_location(x: int, y: int) -> int:
	return x + (y * grid_size)

func is_valid_cell_index(cell_index: int) -> bool:
	if cell_index < 0:
		return false
		
	return cell_index < grid_size * grid_size
	
func is_solved() -> bool:
	return cells == solution

#region "Private" Functions

func _initialize_solution(solved_state: String) -> void:
	# setup needed variables
	var count := 0
	var row := 0
	
	# iterate over each row state
	var row_states := solved_state.split("/")
	for row_state in row_states:
		var index := 0
		var current_cell := 0
		while index < row_state.length():
			var next_char := row_state[index]
			if next_char.is_valid_int():
				# this is a number with either one or two digits, so get the full number
				var result := _get_count(index, row_state)
				count = result[0]
				index += result[1]
			else:
				# row_state[index] should be either a '-' or an 'x'
				if next_char == '-': # empty cell(s)
					# no need to do anything, so just skip the empty cells
					current_cell += count
				elif next_char == 'x':
					# starting from the current cell, fill the next <count> cells
					for cell in range(0, count):
						solution[cell_index_from_location(current_cell, row) + cell] = true
					current_cell += 1
				else:
					assert(false, "Invalid character found %c" % next_char)
				count = 0
				index += 1
		row += 1

# returns an array where:
# 	result[0] -> count
# 	result[1] -> number of digits in count
func _get_count(starting_index: int, row_state: String) -> Array[int]:
	var digits_check := 0
	while row_state[starting_index + digits_check].is_valid_int(): digits_check += 1
	return [int(row_state.substr(starting_index, digits_check)), digits_check]

func _fill_cell(cell_index: int) -> bool:
	if !is_valid_cell_index(cell_index):
		return false
	
	cells[cell_index] = true
	return true

#endregion
