extends Node

class Solver_Data:
	# the largest clues in each row/col
	var largest_row_clues : Array[int]
	var largest_column_clues : Array[int]

	# "Sets" to track what we've already solved
	var solved_rows := {
		-1: null, # initial dummy entry to set the Dictionary type
	}
	var solved_columns := {
		-1: null, # initial dummy entry to set the Dictionary type
	}

	func _mark_row_solved(row_index: int) -> void:
			solved_rows.set(row_index, null)

	func _mark_column_solved(column_index: int) -> void:
			solved_columns.set(column_index, null)


@export var max_iterations := 5
var tracker : Solver_Data

# Called when the node enters the scene tree for the first time.
func _ready() -> void:
	tracker = Solver_Data.new()

func run(puzzle: Puzzle) -> void:
#region PreProcess
#region DEBUG PreProcess Timer Start
	# measure how long the preprocessing takes
	var preprocess_start = Time.get_ticks_usec()
#endregion DEBUG PreProcess Timer Start

	tracker.largest_row_clues.resize(puzzle.grid_size)
	tracker.largest_column_clues.resize(puzzle.grid_size)

	# preprocess the puzzle to get some basic information
	for i in range(0, puzzle.grid_size):
		var row_clues = Array(puzzle.row_clues.get(i))
		var column_clues = Array(puzzle.col_clues.get(i))

		if row_clues:
			var max_clue = row_clues.max()
			assert(max_clue != null, "Invalid row clues")
			tracker.largest_row_clues[i] = max_clue

		if column_clues:
			var max_clue = column_clues.max()
			assert(max_clue != null, "Invalid column clues")
			tracker.largest_column_clues[i] = max_clue

#region DEBUG PreProcess Timer End
	var preprocess_end = Time.get_ticks_usec()
	print("PreProcess Time: %d microsec" % (preprocess_end-preprocess_start))
#endregion DEBUG PreProcess Timer End
#endregion PreProcess

#region Solution
#region DEBUG Solve Timer Start
	# measuring the time to solve the puzzle
	var solution_start := Time.get_ticks_usec()
#endregion DEBUG Solve Timer Start

	# run the solver until the puzzle is solved, but to keep it from getting
	# into an infinite loop, we cap the number of iterations
	var iterations := 0
	while !puzzle.is_solved():
		# check each set of row and column clues
		for row_index in range(0, puzzle.grid_size):
			_try_solve_row(puzzle, row_index)

		for column_index in range(0, puzzle.grid_size):
			_try_solve_column(puzzle, column_index)

		get_parent().queue_redraw()
		iterations += 1
		if iterations >= max_iterations:
			break

	if puzzle.is_solved():
		print("We did it!")
	else:
		print("FAILURE!")

#region DEBUG Solve Timer End
	var solution_end = Time.get_ticks_usec()
	print("Solution Time: %d microsec" % (solution_end-solution_start))
#endregion Solve Timer End
#endregion Solution


#region "Private" solver functions

func _try_solve_row(puzzle: Puzzle, row_index: int) -> void:
	var row_clues = puzzle.row_clues.get(row_index)
	# no clues for this row, or we've already solved it; skip to the next one
	if !row_clues or tracker.solved_rows.has(row_index):
		row_index += 1
		return

	# Start by adding the clues and the spaces in between.
	var leftover_cells = _distance_to_end(row_clues, puzzle.grid_size)

	# If the sum is the same as the grid size, then the entire row is filled
	# by the clues
	if leftover_cells == 0:
		_fill(puzzle, Vector2i(0, row_index), row_clues, Vector2i.RIGHT)
		tracker._mark_row_solved(row_index)

	# If the sum is less than the largest clue in the row,
	# then the row can be partially filled
	elif leftover_cells <= tracker.largest_row_clues[row_index]:
		var column := 0
		for clue in row_clues:
			if clue <= leftover_cells:
				# we can't fill any cells for this clue
				# skip passed it and the following space
				column += clue + 1
			else:
				# we can partially fill in this clue's cells
				column = _fill_n_cells(puzzle, Vector2i(column + leftover_cells, row_index), clue-leftover_cells, Vector2i.RIGHT).x

func _try_solve_column(puzzle: Puzzle, column_index: int) -> void:
	var column_clues = puzzle.col_clues.get(column_index)

	# no clues for this column, or we've already solved it; skip to the next one
	if !column_clues or tracker.solved_columns.has(column_index):
		column_index += 1
		return

	# Start by adding the clues and the spaces in between.
	var leftover_cells = _distance_to_end(column_clues, puzzle.grid_size)

	# If the sum is the same as the grid size,
	# the entire column is filled the clues
	if leftover_cells == 0:
		_fill(puzzle, Vector2i(column_index, 0), column_clues, Vector2i.DOWN)
		tracker._mark_column_solved(column_index)

	# If the sum is less than the largest clue in the column,
	# the column can be partially filled
	elif leftover_cells <= tracker.largest_column_clues[column_index]:
		var row := 0
		for clue in column_clues:
			if clue <= leftover_cells:
				# we can't fill any cells for this clue
				# skip passed it and the following space
				row += clue + 1
			else:
				# we can partially fill in this clue's cells
				row = _fill_n_cells(puzzle, Vector2i(column_index, row + leftover_cells), clue-leftover_cells, Vector2i.DOWN).y


func _distance_to_end(clues: Array, grid_size: int) -> int:
	# sum the clues
	var accumulate = func(accum, number): return accum + number
	var sum = clues.reduce(accumulate, 0)

	# the number of spaces is the number of n-1, where n is the number of clues
	var spaces = clues.size() - 1
	return grid_size - (sum + spaces)

func _fill(puzzle: Puzzle, starting_location: Vector2i, clues: Array, fill_direction: Vector2i) -> void:
	# fill in the row/column
	var i := 0
	for clue in clues:
		var next_cell = _fill_n_cells(puzzle, starting_location + (i * fill_direction), clue, fill_direction, true)
		if fill_direction == Vector2i.RIGHT:
			i = next_cell.x
		elif fill_direction == Vector2i.DOWN:
			i = next_cell.y
		# because the column is filled by the clues, the next row
		# is a space, so skip to the next row down
		i += 1


# returns the cell the next cell after the fill.
# this might be outside the bounds of the grid if this fills to the end of the row/column
func _fill_n_cells(puzzle: Puzzle, starting_cell: Vector2i, n: int, fill_dir: Vector2i, mark_next_cell: bool = false) -> Vector2i:
	var count = 0
	while count < n:
		var current := Vector2i(starting_cell + (fill_dir * count))
		puzzle._fill_cell(current.x, current.y)
		count += 1

	var next_cell = Vector2i(starting_cell + (fill_dir * count))
	if mark_next_cell:
		puzzle.mark_cell(next_cell.x, next_cell.y)
	return next_cell

#endregion
