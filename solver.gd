extends Node

class Solver_Data:
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
	pass

func run(puzzle: Puzzle) -> void:

#region DEBUG Timer Start
	# measuring the time to solve the puzzle
	var start = Time.get_ticks_usec()
#endregion

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

#region DEBUG Timer End
	var end = Time.get_ticks_usec()
	var solve_time = (end - start) / 1000000.0
	print("Solution Time: %.6f microsec" % solve_time)

#endregion

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
		# fill in the row
		var column := 0
		for clue in row_clues:
			column = _fill_n_cells(puzzle, Vector2i(column, row_index), clue, Vector2i.RIGHT).x
			# because the row is filled by the clues, the next column
			# is a space, so skip to the next column over
			column += 1

		tracker._mark_row_solved(row_index)

func _try_solve_column(puzzle: Puzzle, column_index: int) -> void:
	var column_clues = puzzle.col_clues.get(column_index)

	# no clues for this column, or we've already solved it; skip to the next one
	if !column_clues or tracker.solved_columns.has(column_index):
		column_index += 1
		return

	# Start by adding the clues and the spaces in between.
	var leftover_cells = _distance_to_end(column_clues, puzzle.grid_size)

	# If the sum is the same as the grid size, then the entire column is filled
	# by the clues
	if leftover_cells == 0:
		# fill in the column
		var row := 0
		for clue in column_clues:
			row = _fill_n_cells(puzzle, Vector2i(column_index, row), clue, Vector2i.DOWN).y
			# because the column is filled by the clues, the next row
			# is a space, so skip to the next row down
			row += 1

		tracker._mark_column_solved(column_index)

func _distance_to_end(clues: Array, grid_size: int) -> int:
	# sum the clues
	var accumulate = func(accum, number): return accum + number
	var sum = clues.reduce(accumulate, 0)

	# the number of spaces is the number of n-1, where n is the number of clues
	var spaces = clues.size() - 1
	return grid_size - (sum + spaces)

# returns the cell the next cell after the fill.
# this might be outside the bounds of the grid if this fills to the end of the row/column
func _fill_n_cells(puzzle: Puzzle, starting_cell: Vector2i, n: int, increment_dir: Vector2i) -> Vector2i:
	var count = 0
	while count < n:
		var current := Vector2i(starting_cell + (increment_dir * count))
		puzzle._fill_cell(current.x, current.y)
		count += 1
	return Vector2i(starting_cell + (increment_dir * count))

#endregion
