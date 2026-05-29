class_name Puzzle


var grid_size: int
var rows: Array[CellArray]
var columns: Array[CellArray]
var solution_rows: Array[SolutionCellArray]
var solution_columns: Array[SolutionCellArray]

var all_marked: PackedByteArray
var row_clues: Dictionary[int, Array]
var col_clues: Dictionary[int, Array]
var max_row_clues := 0
var max_col_clues := 0

func _init(init_grid_size: int, initial_state: String) -> void:
	grid_size = init_grid_size

	# initialize the puzzle that we'll use in normal play
	rows.resize(grid_size)
	columns.resize(grid_size)
	for i in range(0, grid_size):
		rows[i] = CellArray.new(grid_size)
		columns[i] = CellArray.new(grid_size)

	# initialize the "solved" version of the puzzle
	solution_rows.resize(grid_size)
	solution_columns.resize(grid_size)
	for i in range(0, grid_size):
		solution_rows[i] = SolutionCellArray.new(grid_size)
		solution_columns[i] = SolutionCellArray.new(grid_size)

	# set the "solved" state of the puzzle
	_initialize_solution(initial_state)

	# add clues to the top and left side of the puzzle
	_setup_clues()

func is_cell_filled(x: int, y: int) -> bool:
	assert(is_valid_cell_index(x, y),
		"Cell Index outside of grid bounds: [%d, %d]" % [x,y])
	return rows[y].is_cell_filled(x)

func is_cell_marked(x: int, y: int) -> bool:
	assert(is_valid_cell_index(x, y),
		"Cell Index outside of grid bounds: [%d, %d]" % [x,y])
	return rows[y].is_cell_marked(x)

func is_cell_empty(x: int, y: int) -> bool:
	assert(is_valid_cell_index(x, y),
		"Cell Index outside of grid bounds: [%d, %d]" % [x,y])
	return !rows[y].is_cell_marked(x) && !rows[y].is_cell_filled(x)

func toggle_cell(x: int, y: int) -> bool:
	if !is_valid_cell_index(x, y):
		return false

	if rows[y].is_cell_filled(x):
		rows[y].empty_cell(x)
		columns[x].empty_cell(y)
	else:
		rows[y].fill_cell(x)
		columns[x].fill_cell(y)
	return true

func mark_cell(x: int, y: int) -> bool:
	if !is_valid_cell_index(x, y):
		return false

	rows[y].mark_cell(x)
	columns[x].mark_cell(y)
	return true

func unmark_cell(x: int, y: int) -> bool:
	if !is_valid_cell_index(x, y):
		return false

	rows[y].unmark_cell(x)
	columns[x].unmark_cell(y)
	return true

func mark_empty_cells(index: int, fill_direction: Vector2i) -> void:
	if fill_direction == Vector2i.RIGHT: # row
		rows[index].mark_empty_cells()
		for i in range(0, grid_size):
			if !columns[i].is_cell_filled(index):
				columns[i].mark_cell(index)
		columns[index].mark_empty_cells()
	if fill_direction == Vector2i.DOWN: # column
		for i in range(0, grid_size):
			if !rows[i].is_cell_filled(index):
				rows[i].mark_cell(index)

func cell_index_from_location(x: int, y: int) -> int:
	return x + (y * grid_size)

func is_valid_cell_index(x: int, y: int) -> bool:
	return x >= 0 and x < grid_size and y >= 0 and y < grid_size

func is_solved() -> bool:
	for i in range(0, grid_size):
		if !is_row_solved(i):
			return false

	return true

func is_row_solved(i: int) -> bool:
	return solution_rows[i].equals(rows[i])

func is_column_solved(i: int) -> bool:
	return solution_columns[i].equals(columns[i])

#region "Private" Functions

func _initialize_solution(solved_state: String) -> void:
	# setup needed variables
	var count := 0
	var row := 0
	var filled := false

	# iterate over each row state
	var row_states := solved_state.split("/")
	for row_state in row_states:
		var index := 0
		var current_cell := 0
		# the first digit is either 1 or 0, if the first cell is filled or not, respectively
		filled = int(row_state[0])
		index += 2
		while index < row_state.length():
			var next_char := row_state[index]
			if next_char.is_valid_int():
				# this is a number with either one or two digits, so get the full number
				var result := _get_count(index, row_state)
				count = result[0]
				index += result[1]
			else:
				# row_state[index] should be a ','
				if !filled: # empty cell(s)
					current_cell += count
				else:
					# starting from the current cell, fill the next <count> cells
					for cell in range(0, count):
						solution_rows[row].fill_cell(current_cell + cell)
						solution_columns[current_cell + cell].fill_cell(row)
					current_cell += 1
				filled = !filled
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
			if solution_rows[row].is_cell_filled(i):
				current_clue += 1
			elif current_clue > 0:
				var current_count = _add_row_clue(row, current_clue)
				solution_rows[row].record_clue(current_count-1, i-current_clue, current_clue)
				if current_count > current_max:
					current_max = current_count
				current_clue = 0
			i += 1

		# we may have made it to the end of the row with clues, so we need to add those here
		if current_clue > 0:
			var current_count = _add_row_clue(row, current_clue)
			solution_rows[row].record_clue(current_count-1, i-current_clue, current_clue)
			if current_count > current_max:
				current_max	= current_count

		if current_max > max_row_clues:
			max_row_clues = current_max

	# figure out column clues
	for col in range(0, grid_size):
		var i := 0
		var current_clue := 0
		while i < grid_size:
			if solution_rows[i].is_cell_filled(col):
				current_clue += 1
			elif current_clue > 0:
				var current_count = _add_col_clue(col, current_clue)
				solution_columns[col].record_clue(current_count-1, i-current_clue, current_clue)
				if current_count > current_max:
					current_max = current_count
				current_clue = 0
			i += 1

		# we may have made it to the end of the row with clues, so we need to add those here
		if current_clue > 0:
			var current_count = _add_col_clue(col, current_clue)
			solution_columns[col].record_clue(current_count-1, i-current_clue, current_clue)
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

func _fill_cell(x: int, y: int) -> bool:
	if !is_valid_cell_index(x, y):
		return false

	rows[y].fill_cell(x)
	columns[x].fill_cell(y)
	return true

#endregion
