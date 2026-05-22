class_name Puzzle


enum Cell_State {
	EMPTY,
	MARKED,
	FILLED
}

var grid_size: int
var cells: PackedByteArray
var solution: PackedByteArray
var all_marked: PackedByteArray
var row_clues: Dictionary[int, Array]
var col_clues: Dictionary[int, Array]
var max_row_clues := 0
var max_col_clues := 0

func _init(init_grid_size: int, initial_state: String) -> void:
	grid_size = init_grid_size
	
	# initialize the puzzle that we'll use in normal play
	cells = []
	cells.resize(grid_size * grid_size)
	cells.fill(Cell_State.EMPTY)
	
	# initialize the "solved" version of the puzzle
	solution = []
	solution.resize(grid_size * grid_size)
	solution.fill(Cell_State.EMPTY)
	
	# set the "solved" state of the puzzle
	_initialize_solution(initial_state)
	
	# add clues to the top and left side of the puzzle
	_setup_clues()

func is_cell_filled(cell_index: int) -> bool:
	assert(cell_index >= 0 and cell_index < grid_size * grid_size, "Cell Index outside of grid bounds: %d" % cell_index)
	return cells[cell_index] == Cell_State.FILLED
	
func is_cell_marked(cell_index: int) -> bool:
	assert(cell_index >= 0 and cell_index < grid_size * grid_size, "Cell Index outside of grid bounds: %d" % cell_index)
	return cells[cell_index] == Cell_State.MARKED

func toggle_cell(cell_index: int) -> bool:
	if !is_valid_cell_index(cell_index):
		return false
		
	if cells[cell_index] == Cell_State.FILLED:
		cells[cell_index] = Cell_State.EMPTY
	else:
		cells[cell_index] = Cell_State.FILLED
	return true
	
func mark_cell(cell_index: int) -> bool:
	if !is_valid_cell_index(cell_index):
		return false

	cells[cell_index] = Cell_State.MARKED
	return true
	
func unmark_cell(cell_index: int) -> bool:
	if !is_valid_cell_index(cell_index):
		return false

	cells[cell_index] = Cell_State.EMPTY
	return true
	
func cell_index_from_location(x: int, y: int) -> int:
	return x + (y * grid_size)

func is_valid_cell_index(cell_index: int) -> bool:
	if cell_index < 0:
		return false
		
	return cell_index < grid_size * grid_size
	
func is_solved() -> bool:
	for i in range(0, grid_size * grid_size):
		if (solution[i] & Cell_State.FILLED) & (cells[i] ^ solution[i]):
			return false

	return true

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
					current_cell += count
				elif next_char == 'x':
					# starting from the current cell, fill the next <count> cells
					for cell in range(0, count):
						solution[cell_index_from_location(current_cell, row) + cell] = Cell_State.FILLED
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

func _setup_clues() -> void:
	var current_max := 0
	
	# figure out row clues
	for row in range(0, grid_size):
		var i := 0
		var current_clue := 0
		while i < grid_size:
			if solution[cell_index_from_location(i, row)]:
				current_clue += 1
			elif current_clue > 0:
				var current_count = _add_row_clue(row, current_clue)
				if current_count > current_max:
					current_max = current_count
				current_clue = 0
			i += 1
		
		# we may have made it to the end of the row with clues, so we need to add those here
		if current_clue > 0:
			var current_count = _add_row_clue(row, current_clue)
			if current_count > current_max:
				current_max	= current_count
		
		if current_max > max_row_clues:
			max_row_clues = current_max
		
	# figure out column clues
	for col in range(0, grid_size):
		var i := 0
		var current_clue := 0
		while i < grid_size:
			if solution[cell_index_from_location(col, i)]:
				current_clue += 1
			elif current_clue > 0:
				var current_count = _add_col_clue(col, current_clue)
				if current_count > current_max:
					current_max = current_count
				current_clue = 0
			i += 1
		
		# we may have made it to the end of the row with clues, so we need to add those here
		if current_clue > 0:
			var current_count = _add_col_clue(col, current_clue)
			if current_count > current_max:
				current_max	= current_count
		
		if current_max > max_col_clues:
			max_col_clues = current_max

		
# returns the number of clues currently in the array
func _add_row_clue(key: int, clue: int) -> int:
	if !row_clues.get(key):
		row_clues.set(key, [])
	row_clues[key].append(clue)
	return row_clues[key].size()
		
# returns the number of clues currently in the array
func _add_col_clue(key: int, clue: int) -> int:
	if !col_clues.get(key):
		col_clues.set(key, [])
	col_clues[key].append(clue)
	return col_clues[key].size()

func _fill_cell(cell_index: int) -> bool:
	if !is_valid_cell_index(cell_index):
		return false
	
	cells[cell_index] = true
	return true

#endregion
