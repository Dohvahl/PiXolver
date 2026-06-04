extends Node

const INT_MIN := -2 ^ 63

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

	func get_largest_clue(iter_direction: Vector2i, index: int) -> int:
		if iter_direction == Vector2i.DOWN:
			return largest_row_clues[index]
		elif iter_direction == Vector2i.RIGHT:
			return largest_column_clues[index]
		else:
			return INT_MIN

	func is_solved(iter_direction: Vector2i, index: int) -> int:
		if iter_direction == Vector2i.DOWN:
			return solved_rows.has(index)
		elif iter_direction == Vector2i.RIGHT:
			return solved_columns.has(index)
		else:
			return false

	func mark_solved(iter_direction: Vector2i, index: int) -> void:
		if iter_direction == Vector2i.DOWN:
			solved_rows.set(index, null)
		elif iter_direction == Vector2i.RIGHT:
			solved_columns.set(index, null)

@export var max_iterations := 5
var tracker : Solver_Data

# Called when the node enters the scene tree for the first time.
func _ready() -> void:
	tracker = Solver_Data.new()

func run(puzzle: Puzzle, debug: bool = false) -> bool:
	# run the solver until the puzzle is solved, but to keep it from getting
	# into an infinite loop, we cap the number of iterations
	var iterations := 1
	while !puzzle.is_solved():
		if !run_single(puzzle, iterations, debug):
			break
		iterations += 1
		if iterations - 1 >= max_iterations:
			break

	if debug:
		if puzzle.is_solved():
			print("We did it!")
		else:
			print("FAILURE!")
		print("Iterations: %d" % (iterations - 1))
	return puzzle.is_solved()

## returns true if additional iterations are required
func run_single(puzzle: Puzzle, iterations: int, debug: bool = false) -> bool:
	if debug: print("\n*** DEBUG *** Iteration %d *** DEBUG ***" % iterations)
#region PreProcess
#region DEBUG PreProcess Timer Start
	# measure how long the preprocessing takes
	var preprocess_start = Time.get_ticks_usec()
#endregion DEBUG PreProcess Timer Start

	tracker.largest_row_clues.resize(puzzle.grid_size)
	tracker.largest_column_clues.resize(puzzle.grid_size)

	# preprocess the puzzle to get some basic information
	for i in range(0, puzzle.grid_size):
		var row_clues = puzzle.get_row_clues(i)
		var column_clues = puzzle.get_col_clues(i)

		if row_clues:
			var max_clue = puzzle.solution_rows[i].max_clue_value
			assert(max_clue != null, "Invalid row clues")
			tracker.largest_row_clues[i] = max_clue

		if column_clues:
			var max_clue = puzzle.solution_columns[i].max_clue_value
			assert(max_clue != null, "Invalid column clues")
			tracker.largest_column_clues[i] = max_clue

#region DEBUG PreProcess Timer End
	var preprocess_end = Time.get_ticks_usec()
	if debug: print("PreProcess Time: %d microsec" % (preprocess_end-preprocess_start))
#endregion DEBUG PreProcess Timer End
#endregion PreProcess

#region Solution
#region DEBUG Solve Timer Start
	# measuring the time to solve the puzzle
	var solution_start := Time.get_ticks_usec()
#endregion DEBUG Solve Timer Start

	var should_continue := true

	# check each set of row and column clues
	for row_index in range(0, puzzle.grid_size):
		# try to solve the row
		#print("Attempting to solve row %d" % row_index)
		_try(puzzle, row_index, puzzle.get_row_clues(row_index), Vector2i.DOWN, Vector2i.RIGHT)

	for column_index in range(0, puzzle.grid_size):
		# try to solve the column
		#print("Attempting to solve column %d" % column_index)
		_try(puzzle, column_index, puzzle.get_col_clues(column_index), Vector2i.RIGHT, Vector2i.DOWN)

#region DEBUG Solve Timer End
	var solution_end = Time.get_ticks_usec()
	if debug: print("Solution Time: %d microsec" % (solution_end-solution_start))
#endregion Solve Timer End
#endregion Solution

	# if this iteration didn't change the state of the puzzle,
	# we're not improving the solution, so there's no point in continuing
	return true

#region "Private" solver functions

## Returns true if this solved the row/column
func _try(puzzle: Puzzle, index: int, clues: Array, iteration_direction: Vector2i, fill_direction: Vector2i) -> bool:
	# no clues for this row, or we've already solved it; skip to the next one
	if !clues or tracker.is_solved(iteration_direction, index):
		return true

	# the current row/column may have been solved by previous iterations,
	# so we should check it before we try to do any work to it
	if _is_solved(puzzle, index, iteration_direction):
		tracker.mark_solved(iteration_direction, index)

		# ensure the empty cells are marked
		puzzle.mark_empty_cells(index, fill_direction)
		return true

	return _try_line_solve(puzzle, index, clues, iteration_direction, fill_direction)

func _is_solved(puzzle: Puzzle, index: int, iter_direction: Vector2i) -> bool:
	if iter_direction == Vector2i.DOWN:
		return puzzle.is_row_solved(index)
	elif iter_direction == Vector2i.RIGHT:
		return puzzle.is_column_solved(index)
	else:
		return false

## Returns true if the row/column is solved by this
func _try_line_solve(puzzle: Puzzle, index: int, clues: Array[Clue], iteration_direction: Vector2i, fill_direction: Vector2i) -> bool:
	var bounds := _get_array_bounds(puzzle, index, iteration_direction, fill_direction)
	var start_offset := bounds[0]
	var end_offset := bounds[1]

	# Start by adding the clues and the spaces in between.
	var leftover_cells = _distance_to_end(clues, puzzle.grid_size - start_offset - end_offset)
	var start_cell := (iteration_direction * index) + (fill_direction * start_offset)
	var end_cell := start_cell + (fill_direction * (puzzle.grid_size - end_offset - 1))

	# If the sum is the same as the grid size, then the entire row is filled
	# by the clues
	if leftover_cells == 0:
		_fill(puzzle, start_cell, clues, fill_direction)
		tracker.mark_solved(iteration_direction, index)
		return true
	# If the sum is less than the largest clue in the row,
	# then the row can be partially filled
	elif leftover_cells <= tracker.get_largest_clue(iteration_direction, index):
		_partial_fill(puzzle, start_cell, clues, leftover_cells, fill_direction)

	# Check if the first/last cell is filled, but the first/last clue isn't yet completed.
	# If so, we can fill in the clues
	if puzzle.is_cell_filled(start_cell.x, start_cell.y) and !clues[0].is_solved():
		_fill_n_cells(puzzle, start_cell, clues[0]._value, fill_direction, true)
		clues[0].toggle_solved()
	var last_clue = clues.back()
	if puzzle.is_cell_filled(end_cell.x, end_cell.y) and !last_clue.is_solved():
		_fill_n_cells(puzzle, end_cell, last_clue._value, -fill_direction, true)
		clues.back().toggle_solved()

	return _is_solved(puzzle, index, iteration_direction)

## Find the "virtual" start and end of the row/column, taking marked cells into account
func _get_array_bounds(puzzle: Puzzle, index: int, iteration_direction: Vector2i, fill_direction: Vector2i) -> Array[int]:
	# previous iterations may have marked cells at the start or end, these can be skipped
	var start_offset := 0
	var starting_cell := iteration_direction * index
	while puzzle.is_cell_marked(starting_cell.x, starting_cell.y):
		start_offset += 1
		starting_cell += fill_direction

	var end_offset := 0
	var end_cell := (iteration_direction * index) + (fill_direction * (puzzle.grid_size - 1))
	while puzzle.is_cell_marked(end_cell.x, end_cell.y):
		end_offset += 1
		end_cell -= fill_direction

	return [start_offset, end_offset]


func _distance_to_end(clues: Array[Clue], grid_size: int) -> int:
	# sum the clues
	var accumulate = func(accum: int, clue: Clue): return accum + clue._value
	var sum = clues.reduce(accumulate, 0)

	# the number of spaces is the number of n-1, where n is the number of clues
	var spaces = clues.size() - 1
	return grid_size - (sum + spaces)

func _fill(puzzle: Puzzle, starting_location: Vector2i, clues: Array[Clue], fill_direction: Vector2i) -> void:
	# fill in the row/column
	var i := 0
	for clue in clues:
		var next_cell = _fill_n_cells(puzzle, starting_location + (i * fill_direction), clue._value, fill_direction, true)
		if fill_direction == Vector2i.RIGHT:
			i = next_cell.x
		elif fill_direction == Vector2i.DOWN:
			i = next_cell.y
		clue.toggle_solved()
		# because the column is filled by the clues, the next row
		# is a space, so skip to the next row down
		i += 1

func _partial_fill(puzzle: Puzzle, starting_location: Vector2i, clues: Array[Clue], leftover_cells: int, fill_direction: Vector2i) -> void:
	var i := 0
	for clue in clues:
		if clue._value <= leftover_cells:
			# we can't fill any cells for this clue
			# skip passed it and the following space
			i += clue._value + 1
		else:
			# we can partially fill in this clue's cells
			var offset = (i + leftover_cells) * fill_direction
			var next_cell = _fill_n_cells(puzzle, starting_location + offset, clue._value-leftover_cells, fill_direction)
			if fill_direction == Vector2i.RIGHT:
				i = next_cell.x
			elif fill_direction == Vector2i.DOWN:
				i = next_cell.y


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
