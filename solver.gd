extends Node

@export var max_iterations := 5

# Called when the node enters the scene tree for the first time.
func _ready() -> void:
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
	if !row_clues: # no clues for this row, skip to the next one
		row_index += 1
		return

	# start by checking if any of the clue sets, including the spaces,
	# add up to the number of cell in the row. If so, just fill them in
	var leftover_cells = _distance_to_end(row_clues, puzzle.grid_size)
	if leftover_cells == 0:
		# fill in the row
		for i in range(0, puzzle.grid_size):
			if !puzzle.is_cell_filled(i, row_index):
				puzzle.toggle_cell(i, row_index)
	
func _try_solve_column(puzzle: Puzzle, column_index: int) -> void:
	var column_clues = puzzle.col_clues.get(column_index)
	if !column_clues: # no clues for this column, skip to the next one
		column_index += 1
		return

	# start by checking if any of the clue sets, including the spaces,
	# add up to the number of cell in the row. If so, just fill them in
	var leftover_cells = _distance_to_end(column_clues, puzzle.grid_size)
	if leftover_cells == 0:
		# fill in the column
		var row = 0
		for clue in column_clues:
			var count = 0
			while count < clue:
				if !puzzle.is_cell_filled(column_index, row):
					puzzle._fill_cell(column_index, row)
				count += 1
				row += 1
			# because the column is filled by the clues, the next row
			# is a space, so skip to the next row down
			row += 1

func _distance_to_end(clues: Array, grid_size: int) -> int:
	# sum the clues
	var accumulate = func(accum, number): return accum + number
	var sum = clues.reduce(accumulate, 0)

	# the number of spaces is the number of n-1, where n is the number of clues
	var spaces = clues.size() - 1
	return grid_size - (sum + spaces)

#endregion
