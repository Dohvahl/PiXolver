extends Node

@export var max_iterations := 5

# Called when the node enters the scene tree for the first time.
func _ready() -> void:
	pass

func run(puzzle: Puzzle) -> void:
	# run the solver until the puzzle is solved, but to keep it from getting
	# into an infinite loop, we cap the number of iterations
	var iterations := 0
	while !puzzle.is_solved():
		# check each set of row and column clues
		for row_index in range(0, puzzle.grid_size):
			var row_clues = puzzle.row_clues.get(row_index)
			if !row_clues: # no clues for this row, skip to the next one
				row_index += 1
				continue

			# start by checking if any of the clue sets, including the spaces,
			# add up to the number of cell in the row. If so, just fill them in
			if _clue_totals_plus_spaces_match_cell_count(row_clues, puzzle.grid_size):
				# fill in the row
				for i in range(0, puzzle.grid_size):
					if !puzzle.is_cell_filled(puzzle.cell_index_from_location(i, row_index)):
						puzzle.toggle_cell(puzzle.cell_index_from_location(i, row_index))

		for column_index in range(0, puzzle.grid_size):
			var column_clues = puzzle.col_clues.get(column_index)
			if !column_clues: # no clues for this column, skip to the next one
				column_index += 1
				continue

			if _clue_totals_plus_spaces_match_cell_count(column_clues, puzzle.grid_size):
				# fill in the column
				var cell = puzzle.cell_index_from_location(column_index, 0)
				for clue in column_clues:
					var count = 0
					while count < clue:
						if !puzzle.is_cell_filled(cell):
							puzzle.toggle_cell(cell)
						count += 1
						cell += puzzle.grid_size
					# because the column is filled by the clues, the next row
					# is a space, so skip to the next row down
					cell += puzzle.grid_size

		get_parent().queue_redraw()
		iterations += 1
		if iterations >= max_iterations:
			break

	if puzzle.is_solved():
		print("We did it!")
	else:
		print("FAILURE!")

#region "Private" solver functions

func _clue_totals_plus_spaces_match_cell_count(row_clues: Array, grid_size: int) -> bool:
	# sum the clues
	var sum = func(accum, number): return accum + number
	var row_total = row_clues.reduce(sum, 0)

	# the number of spaces is the number of n-1, where n is the number of clues
	var spaces = row_clues.size() - 1
	return row_total + spaces == grid_size

#endregion
