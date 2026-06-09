extends Node

## NOTE: Technique names taken from the Nonogram wiki: https://en.wikipedia.org/wiki/Nonogram

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

	func reset() -> void:
		solved_rows.clear()
		solved_columns.clear()
		largest_row_clues.clear()
		largest_column_clues.clear()

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


class Result_Stats:
	var full : int
	var correct : int
	var incorrectly_filled : int
	var missing : int
	var diff : int
	var solution_bits: int

	func calculate(current: int, solution: int, width: int) -> void:
		full = (1 << width) - 1

		correct = _popcount(current & solution)
		incorrectly_filled = _popcount(current & ~solution & full)
		missing = _popcount(~current & solution & full)
		diff = _popcount(current ^ solution)
		solution_bits = _popcount(solution)

	func _popcount(x: int) -> int:
		var count := 0
		while x != 0:
			x &= x - 1
			count += 1
		return count

@export var max_iterations := 5
var tracker : Solver_Data

# Called when the node enters the scene tree for the first time.
func _ready() -> void:
	tracker = Solver_Data.new()

func reset() -> void:
	if tracker:
		tracker.reset()

func run(puzzle: Puzzle, debug: bool = false) -> Dictionary:
	# run the solver until the puzzle is solved, but to keep it from getting
	# into an infinite loop, we cap the number of iterations
	var iterations := 1
	while !puzzle.is_solved():
		if !run_single(puzzle, iterations, debug):
			break
		iterations += 1
		if iterations - 1 >= max_iterations:
			break

	var results := {"iterations": iterations}

	if puzzle.is_solved():
		results.set("is_solved", true)
	else:
		# get some stats on the state of the puzzle
		var correct := 0
		var diff := 0
		var sol_bits := 0
		var total := puzzle.grid_size * puzzle.grid_size
		for i in range(0, puzzle.grid_size):
			var stats = Result_Stats.new()
			stats.calculate(puzzle.rows[i].filled_cells, puzzle.solution_rows[i].filled_cells, puzzle.grid_size)
			correct += stats.correct
			diff += stats.diff
			sol_bits += stats.solution_bits

		results.set("filled", float(correct) / sol_bits)
		results.set("solved", float(total - diff) / total)
		results.set("incorrect", diff)

	return results

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

func run_rows(puzzle: Puzzle) -> void:
	# check each set of row and column clues
	for row_index in range(0, puzzle.grid_size):
		# try to solve the row
		#print("Attempting to solve row %d" % row_index)
		_try(puzzle, row_index, puzzle.get_row_clues(row_index), Vector2i.DOWN, Vector2i.RIGHT)

func run_columns(puzzle: Puzzle) -> void:
	for column_index in range(0, puzzle.grid_size):
		# try to solve the column
		#print("Attempting to solve column %d" % column_index)
		_try(puzzle, column_index, puzzle.get_col_clues(column_index), Vector2i.RIGHT, Vector2i.DOWN)

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

	# Simple Boxes
	var line := _sb_calculate_intersections(puzzle.grid_size - start_offset - end_offset, clues)
	if fill_direction == Vector2i.RIGHT: # row
		puzzle.fill_row(index, line.filled_cells, start_offset)
	if fill_direction == Vector2i.DOWN: # column
		puzzle.fill_column(index, line.filled_cells, start_offset)

	var start_cell := (iteration_direction * index) + (fill_direction * start_offset)
	var end_cell := (iteration_direction * index) + (fill_direction * (puzzle.grid_size - end_offset - 1))

	# Checks related to the edges of the row/column
	var first_clue = clues[0]
	var last_clue = clues.back()
	_try_glueing(puzzle, start_cell, end_cell, first_clue, last_clue, fill_direction)
	_try_mercury(puzzle, start_cell, end_cell, first_clue, last_clue, fill_direction)

	return _is_solved(puzzle, index, iteration_direction)

## Find the "virtual" start and end of the row/column, taking marked cells into account
func _get_array_bounds(puzzle: Puzzle, index: int, iteration_direction: Vector2i, fill_direction: Vector2i) -> Array[int]:
	# previous iterations may have marked cells at the start or end, these can be skipped
	var start_offset := 0
	var starting_cell := iteration_direction * index
	while puzzle.is_cell_marked(starting_cell):
		start_offset += 1
		starting_cell += fill_direction

	var end_offset := 0
	var end_cell := (iteration_direction * index) + (fill_direction * (puzzle.grid_size - 1))
	while puzzle.is_cell_marked(end_cell):
		end_offset += 1
		end_cell -= fill_direction

	return [start_offset, end_offset]

## Simple Boxes
## Create two overlapping bitsets using the clues: one starting from the right, one from the left.
## Compare the two bitsets for overlapping bits. If the overlapping bits come from the same clue,
## then that bit can be filled in
func _sb_calculate_intersections(size: int, clues: Array[Clue]) -> CellArray:
	var n := clues.size()
	var lclue := 0
	var rclue := n - 1
	var lpos := 0
	var rpos := size
	var lstarts : Array[int] = []
	lstarts.resize(n)
	var rstarts : Array[int] = []
	rstarts.resize(n)

	while lclue < n and rclue >= 0:
		lstarts[lclue] = lpos
		lpos += clues[lclue]._value + 1
		lclue += 1

		rpos -= clues[rclue]._value
		rstarts[rclue] = rpos
		rpos -= 1
		rclue -= 1

	var intersect := 0
	for i in range(0, n):
		var clue_val = clues[i]._value
		var left_mask = BitOps.FIELD_MASK(lstarts[i] + clue_val)
		var right_mask = ~((1 << rstarts[i]) - 1)
		intersect |= left_mask & right_mask

	var result := CellArray.new(size)
	result.filled_cells = intersect
	return result

#region DEBUG Show Overlap Regions
	#var left := CellArray.new(size)
	#var right := CellArray.new(size)
#
	#var left_cell := 0
	#var right_cell := size - 1
	#var num = clues.size()
	#for i in range(0, num):
		#var left_clue = clues[i]
		#var right_clue = clues[num - 1 - i]
		#left.fill_n_cells(left_clue._value, left_cell)
		#right.fill_n_cells(right_clue._value, right_cell - right_clue._value + 1)
#
		## keep checking
		#left_cell += left_clue._value + 1
		#right_cell -= right_clue._value + 1
#
	#if !get_tree().current_scene.intersect:
		#if get_tree().current_scene.show_left:
			#return left
		#else:
			#return right
	#else:
		#return result
#endregion DEBUG Show Overlap Regions

## Glue
## Check for filled squares that are on or near the edges of the line, but not farther away
## than the length the first/last clue.
func _try_glueing(puzzle: Puzzle, start_cell: Vector2i, end_cell: Vector2i, first_clue: Clue, last_clue: Clue, fill_direction: Vector2i) -> void:
	# Check if the first/last clue isn't yet completed, and there are filled cells within reach.
	# If so, we can fill in the clues
	if !first_clue.is_solved():
		# if any cells < first clue are filled,
		# then we can fill from that filled cell up to the clue
		var lowest_set := puzzle.get_first_filled(start_cell, fill_direction, first_clue._value)
		if lowest_set > -1:
			var first_filled = start_cell + (fill_direction * lowest_set)
			var mark := lowest_set == 0
			_fill_n_cells(puzzle, first_filled, first_clue._value - lowest_set, fill_direction, mark)
			if mark: first_clue.toggle_solved()

	if !last_clue.is_solved():
		# if any cells >= length - last clue,
		# then we can fill from that filled cell up to the clue
		var highest_set := puzzle.get_last_filled(end_cell, fill_direction, last_clue._value)
		if highest_set > -1:
			var last_filled = end_cell - (fill_direction * (puzzle.grid_size - (highest_set + 1)))
			var fill_amount = last_clue._value - (puzzle.grid_size - highest_set) + 1
			var mark := highest_set == puzzle.grid_size - 1
			_fill_n_cells(puzzle, last_filled, fill_amount, -fill_direction, mark)
			if mark: last_clue.toggle_solved()

func _try_mercury(puzzle: Puzzle, start_cell: Vector2i, end_cell: Vector2i, first_clue: Clue, last_clue: Clue, fill_direction: Vector2i) -> void:
	# Check if there are filled cells 1 away from the first/last clues amount
	# For example, if the first clue is 3, and there are 3 empty cells, then a filled cell,
	# we can safely mark the first cell. Similar logic holds for the last cell.
	if !first_clue.is_solved():
		if puzzle.is_cell_filled(start_cell + (fill_direction * (first_clue._value + 1))) and puzzle.get_num_empty_cells(start_cell, fill_direction) == first_clue._value:
			puzzle.mark_cell(start_cell)
	if !last_clue.is_solved():
		if puzzle.is_cell_filled(end_cell + (-fill_direction * (last_clue._value))) and puzzle.get_num_empty_cells(end_cell, -fill_direction) == last_clue._value:
			puzzle.mark_cell(end_cell)

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


## Returns the cell the next cell after the fill.
## This might be outside the bounds of the grid if this fills to the end of the row/column
func _fill_n_cells(puzzle: Puzzle, starting_cell: Vector2i, n: int, fill_dir: Vector2i, mark_next_cell: bool = false) -> Vector2i:
	puzzle.fill_n_cells(starting_cell, n, fill_dir)

	var next_cell = Vector2i(starting_cell + (fill_dir * n))
	if mark_next_cell:
		puzzle.mark_cell(next_cell)
	return next_cell

#endregion
