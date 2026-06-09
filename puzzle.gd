class_name Puzzle

var puzzle_file : String

var grid_size: int
var rows: Array[CellArray]
var columns: Array[CellArray]
var solution_rows: Array[SolutionCellArray]
var solution_columns: Array[SolutionCellArray]

var all_marked: PackedByteArray
var max_row_clues := 0
var max_col_clues := 0

func _init(in_puzzle_file: String, init_grid_size: int, initial_state: String) -> void:
	puzzle_file = in_puzzle_file
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

func reset() -> void:
	# reset the puzzle
	for i in range(0, grid_size):
		rows[i].reset()
		columns[i].reset()

		solution_rows[i].reset()
		solution_columns[i].reset()

func is_cell_filled(loc: Vector2i) -> bool:
	assert(is_valid_cell_index(loc),
		"Cell Index outside of grid bounds: [%d, %d]" % [loc.x,loc.y])
	return rows[loc.y].is_cell_filled(loc.x)

func is_cell_marked(loc: Vector2i) -> bool:
	assert(is_valid_cell_index(loc),
		"Cell Index outside of grid bounds: [%d, %d]" % [loc.x,loc.y])
	return rows[loc.y].is_cell_marked(loc.x)

func is_cell_empty(loc: Vector2i) -> bool:
	assert(is_valid_cell_index(loc),
		"Cell Index outside of grid bounds: [%d, %d]" % [loc.x, loc.y])
	return !rows[loc.y].is_cell_marked(loc.x) && !rows[loc.y].is_cell_filled(loc.x)

## Get the first filled cell, starting from start_cell, up to count
## Returns -1 if none are set
func get_first_filled(start_cell: Vector2i, fill_direction: Vector2i, count: int = grid_size - 1) -> int:
	var filled = -1
	if fill_direction == Vector2i.RIGHT: # row
		filled = rows[start_cell.y].filled_cells
		return BitOps.FIRST_SET(filled, start_cell.x, count)
	elif fill_direction == Vector2i.DOWN: # column
		filled = columns[start_cell.x].filled_cells
		return BitOps.FIRST_SET(filled, start_cell.y, count)

	return filled

## Get the last filled cell, starting from end_cell, up to end_cell-count
## Returns -1 if none are set
func get_last_filled(end_cell: Vector2i, fill_direction: Vector2i, count: int = grid_size - 1) -> int:
	var filled := -1
	var offset := 0
	if fill_direction == Vector2i.RIGHT: # row
		filled = rows[end_cell.y].filled_cells
		offset = end_cell.x
	elif fill_direction == Vector2i.DOWN: # column
		filled = columns[end_cell.x].filled_cells
		offset = end_cell.y

	return BitOps.LAST_SET(filled, offset - count + 1, count)

func toggle_cell(x: int, y: int) -> bool:
	if !is_valid_cell_index(Vector2i(x, y)):
		return false

	if rows[y].is_cell_filled(x):
		rows[y].empty_cell(x)
		columns[x].empty_cell(y)
	else:
		rows[y].fill_cell(x)
		columns[x].fill_cell(y)
	return true

func fill_line(index: int, fill_direction: Vector2i, value: int, offset: int = 0) -> void:
	if fill_direction == Vector2i.RIGHT: # row
		fill_row(index, value, offset)
	elif fill_direction == Vector2i.DOWN: # column
		fill_column(index, value, offset)

func fill_row(index: int, value: int, offset: int = 0) -> void:
	rows[index].fill(value, offset)

	# update the rows to match the column
	for i in range(0, grid_size):
		if (value & (1 << i)):
			columns[offset + i].fill_cell(index)

func fill_column(index: int, value: int, offset: int = 0) -> void:
	columns[index].fill(value, offset)

	# update the rows to match the column
	for i in range(0, grid_size):
		if (value & (1 << i)):
			rows[offset + i].fill_cell(index)

func fill_n_cells(start: Vector2i, n: int, fill_dir: Vector2i) -> void:
	if !is_valid_cell_index(start) or !is_valid_cell_index(start + (n * fill_dir)):
		return

	# we also need to fill in the corresponding row/column
	for i in range(0, n):
		_fill_cell(start + (i * fill_dir))

func mark_cell(loc: Vector2i) -> bool:
	if !is_valid_cell_index(loc):
		return false

	rows[loc.y].mark_cell(loc.x)
	columns[loc.x].mark_cell(loc.y)
	return true

func unmark_cell(x: int, y: int) -> bool:
	if !is_valid_cell_index(Vector2i(x, y)):
		return false

	rows[y].unmark_cell(x)
	columns[x].unmark_cell(y)
	return true

func get_num_empty_cells(start_cell: Vector2i, fill_direction: Vector2i) -> int:
	var count := 0
	var current_cell := start_cell
	while is_cell_empty(current_cell):
		count += 1
		current_cell += (fill_direction)
	return count

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

func get_row_clues(index: int) -> Array[Clue]:
	if index < 0 or index > grid_size:
		return []
	return solution_rows[index].clues

func get_col_clues(index: int) -> Array[Clue]:
	if index < 0 or index > grid_size:
		return []
	return solution_columns[index].clues

func is_valid_cell_index(cell: Vector2i) -> bool:
	return cell.x >= 0 and cell.x < grid_size and cell.y >= 0 and cell.y < grid_size

func is_solved() -> bool:
	for i in range(0, grid_size):
		if !is_row_solved(i):
			return false

	return true

func is_line_solved(index: int, fill_direction: Vector2i) -> bool:
	if fill_direction == Vector2i.RIGHT: # row
		return is_row_solved(index)
	elif fill_direction == Vector2i.DOWN: # column
		return is_column_solved(index)
	return false

func is_row_solved(i: int) -> bool:
	return solution_rows[i].equals(rows[i])

func is_column_solved(i: int) -> bool:
	return solution_columns[i].equals(columns[i])

#region "Private" Functions

func _initialize_solution(solved_state: String) -> void:
	# setup needed variables
	var row := 0

	# iterate over each row state
	var row_states := solved_state.split("\n")
	for row_state in row_states:
		var index := 0
		while index < row_state.length():
			if row_state[index] == '1':
				solution_rows[row].fill_cell(index)
				solution_columns[index].fill_cell(row)
			index += 1
		row += 1

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
				var current_count = _add_row_clue(row, i-current_clue, current_clue)
				if current_count > current_max:
					current_max = current_count
				current_clue = 0
			i += 1

		# we may have made it to the end of the row with clues, so we need to add those here
		if current_clue > 0:
			var current_count = _add_row_clue(row, i-current_clue, current_clue)
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
				var current_count = _add_col_clue(col, i-current_clue, current_clue)
				if current_count > current_max:
					current_max = current_count
				current_clue = 0
			i += 1

		# we may have made it to the end of the row with clues, so we need to add those here
		if current_clue > 0:
			var current_count = _add_col_clue(col, i-current_clue, current_clue)
			if current_count > current_max:
				current_max	= current_count

		if current_max > max_col_clues:
			max_col_clues = current_max

# returns the number of clues currently in the array
func _add_row_clue(row: int, start_col: int, clue: int) -> int:
	var current_num = solution_rows[row].length
	return solution_rows[row].record_clue(current_num, start_col, clue)

# returns the number of clues currently in the array
func _add_col_clue(col: int, start_row: int, clue: int) -> int:
	var current_num = solution_columns[col].length
	return solution_columns[col].record_clue(current_num, start_row, clue)

func _fill_cell(loc: Vector2i) -> bool:
	if !is_valid_cell_index(loc):
		return false

	rows[loc.y].fill_cell(loc.x)
	columns[loc.x].fill_cell(loc.y)
	return true

#endregion
