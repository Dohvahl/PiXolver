extends Control

@export var show_left := false :
	set(new):
		show_left = new
		if solver:
			solver.RunSingle(puzzle, 0, debug)
		queue_redraw()
@export var intersect := false :
	set(new):
		intersect = new
		if solver:
			solver.RunSingle(puzzle, 0, debug)
		queue_redraw()
var puzzle : Puzzle							# data representation of the puzzle

@export_range(1, 5000) var puzzle_number : int = 1
const SAMPLE_PUZZLES_PATH := "res://SamplePuzzles/rand"

var _total_width							# grid width + row clues area width
var _total_height							# grid height + column clues area height

var solved_color = Color.CHARTREUSE
var unsolved_color = Color.NAVY_BLUE

const CELL_SIZE := 32						# pixels per cell
var FONT_OFFSET := int(int(CELL_SIZE + ThemeDB.fallback_font_size) / 2)
@export var unsolved_clue_color : Color
@export var solved_clue_color : Color

@export var solver_scene : PackedScene
var solver : Solver

var solved := false

#region DEBUG VARIABLES

@export var debug: bool :
	set(new_val):
		debug = new_val
		queue_redraw()

#endregion

# initial state is a string representing the solved puzzle
var initial_state: String

var grid_size := 30
func _ready() -> void:
	# get the puzzle from the available samples
	var file_path = SAMPLE_PUZZLES_PATH + str(puzzle_number)
	var puzzle_file := FileAccess.open(file_path,FileAccess.READ)
	assert(puzzle_file, "Failed to open sample puzzle %d: '%s'" % [puzzle_number, FileAccess.get_open_error()])
	initial_state = puzzle_file.get_as_text()

	puzzle = Puzzle.new()
	puzzle.Initialize(puzzle_file.get_path(), grid_size, initial_state)

	_total_width = CELL_SIZE * (puzzle.MaxRowClues + grid_size)
	_total_height = CELL_SIZE * (puzzle.MaxColumnClues + grid_size)

	# size the window to contain the whole puzzle
	get_window().content_scale_size = Vector2i(_total_width, _total_height)

	$Message.hide()

	solver = Solver.new()
	solver.Init(grid_size)
	solved = false

func _draw() -> void:
	# draw the row and column clues first, since the puzzle grid is affected by their position
	_draw_row_clues()
	_draw_column_clues()

	# now draw the puzzle grid, offset appropriately
	_draw_puzzle_grid()

	# check if we've solved it
	if puzzle.IsSolved():
		solved = true
		$Message.text = "Solved!"
		$Message.show()

var iterations = 1
func _input(event: InputEvent) -> void:
	if solved:
		return

	if event is InputEventMouseButton and event.is_pressed():
		var cell_clicked = _get_cell_index_from_position(event.position)
		if !puzzle.IsValidCellIndex(cell_clicked):
			return

		if event.button_index == MOUSE_BUTTON_LEFT:	# left click
			# Toggle cell
			if puzzle.ToggleCell(cell_clicked.x, cell_clicked.y):
				queue_redraw()
		elif event.button_index == MOUSE_BUTTON_RIGHT: # right click
			# Mark cell
			if puzzle.IsCellMarked(cell_clicked) && puzzle.UnmarkCell(cell_clicked.x, cell_clicked.y):
				queue_redraw()
			elif puzzle.MarkCell(cell_clicked):
				queue_redraw()
	elif event.is_action_pressed("run_solver"):
		var results = solver.Run(puzzle, debug)
		if results.get("is_solved"):
			print("Solved!")
		else:
			print("\n")
			print("Percent Correctly Filled: %.2f%%" % (float(results.get("filled")) * 100))
			print("Percent Correct: %.2f%%" % (float(results.get("solved")) * 100))
			print("Incorrect Cells: %d/%d" % [int(results.get("incorrect")), puzzle.GridSize * puzzle.GridSize])
		queue_redraw()
	elif event.is_action_pressed("run_solver_single"):
		if (!solver.RunSingle(puzzle, iterations, debug)):
			print("No Change")
		iterations += 1
		queue_redraw()
	elif event.is_action_pressed("solve_rows"):
		solver.RunRows(puzzle)
		queue_redraw()
	elif event.is_action_pressed("solve_columns"):
		solver.RunColumns(puzzle)
		queue_redraw()
	elif event.is_action_pressed("reset"):
		puzzle.Reset()
		solver.Reset()
		iterations = 0
		queue_redraw()

#region "Private" functions

#region Drawing Functions

func _draw_row_clues() -> void:
	for row_index in range(0, grid_size):
		var clue_color = solved_color if puzzle.IsRowSolved(row_index) else unsolved_color
		var clues := puzzle.GetRowClues(row_index)
		if clues.is_empty():
			# draw a '0'
			var clue_position := _get_row_clue_position(row_index, 1)
			draw_string(ThemeDB.fallback_font, _get_row_clue_position(row_index, 1), "0", HORIZONTAL_ALIGNMENT_CENTER, CELL_SIZE, 16, solved_clue_color)
#region DEBUG
			if debug:
				var clue_rect = Rect2(
					clue_position - Vector2(0, FONT_OFFSET),
					Vector2(CELL_SIZE, CELL_SIZE)
				)
				draw_rect(clue_rect, clue_color, false, 1.0)
#endregion DEBUG
			continue

		# draw the clues right-justified
		var i = 0
		while i < clues.size():
			var clue = clues[clues.size() - i - 1]
			var clue_position := _get_row_clue_position(row_index, i)
			var color = solved_clue_color if clue.IsSolved() else unsolved_clue_color
			draw_string(ThemeDB.fallback_font, clue_position, str(clue.Value), HORIZONTAL_ALIGNMENT_CENTER, CELL_SIZE, 16, color)
#region DEBUG
			if debug:
				var clue_rect = Rect2(
					clue_position - Vector2(0, FONT_OFFSET),
					Vector2(CELL_SIZE, CELL_SIZE)
				)
				draw_rect(clue_rect, clue_color, false, 1.0)

				var horz_line_size = 3 if row_index % 5 == 0 else 1
				draw_line(clue_rect.position, clue_rect.position + (Vector2.RIGHT * CELL_SIZE * grid_size), clue_color, horz_line_size)

#endregion DEBUG
			i += 1

func _draw_column_clues() -> void:
	for col_index in range(0, grid_size):
		var clue_color = solved_color if puzzle.IsColumnSolved(col_index) else unsolved_color
		var clues := puzzle.GetColClues(col_index)
		if clues.is_empty():
			# draw a '0'
			var clue_position = _get_col_clue_position(col_index, 1)
			draw_string(ThemeDB.fallback_font, clue_position, "0", HORIZONTAL_ALIGNMENT_CENTER, CELL_SIZE, 16, solved_clue_color)
#region DEBUG
			if debug:
				var clue_rect = Rect2(
					clue_position,
					Vector2i(CELL_SIZE, CELL_SIZE)
				)
				draw_rect(clue_rect, clue_color, false, 1.0)
#endregion DEBUG
			continue

		# draw the clues bottom-up
		var i = 0
		while i < clues.size():
			var clue = clues[clues.size() - 1 - i]
			var clue_position = _get_col_clue_position(col_index, i)
			var color = solved_clue_color if clue.IsSolved() else unsolved_clue_color
			draw_string(ThemeDB.fallback_font, clue_position, str(clue.Value), HORIZONTAL_ALIGNMENT_CENTER, CELL_SIZE, 16, color)
#region DEBUG
			if debug:
				var clue_rect = Rect2(
					clue_position - Vector2(0, FONT_OFFSET),
					Vector2i(CELL_SIZE, CELL_SIZE)
				)
				draw_rect(clue_rect, clue_color, false, 1.0)
				var vert_line_size = 3 if col_index % 5 == 0 else 1
				draw_line(clue_rect.position, clue_rect.position + (Vector2.DOWN * CELL_SIZE * grid_size), clue_color, vert_line_size)
#endregion DEBUG
			i += 1

func _draw_puzzle_grid() -> void:
	for x in range(grid_size):
		var vert_line_size = 3 if x % 5 == 0 else 1
		for y in range(grid_size):
			var rect = Rect2(
				CELL_SIZE * (x + puzzle.MaxRowClues),
				CELL_SIZE * (y + puzzle.MaxColumnClues),
				CELL_SIZE,
				CELL_SIZE
				)

			var cell_location = _get_cell_index_from_position(rect.position)
			if puzzle.IsCellFilled(cell_location):
				draw_rect(rect, Color.BLACK, true)
			elif puzzle.IsCellMarked(cell_location):
				draw_rect(rect, Color.RED, true)
			else:
				draw_rect(rect, Color.WHITE, false, 1)

			var horz_line_size = 3 if y % 5 == 0 else 1
			draw_line(rect.position, rect.position + (Vector2.DOWN * CELL_SIZE * grid_size), Color.WHITE, vert_line_size)
			draw_line(rect.position, rect.position + (Vector2.RIGHT * CELL_SIZE * grid_size), Color.WHITE, horz_line_size)

#endregion

func _get_cell_index_from_position(pos: Vector2) -> Vector2i:
	if pos < Vector2.ZERO:
		return Vector2i.MIN

	var clicked_x = int(pos.x / CELL_SIZE) - puzzle.MaxRowClues
	var clicked_y = int(pos.y / CELL_SIZE) - puzzle.MaxColumnClues

	# we have to account for the clues areas when checking the grid locations
	if clicked_x < 0 or clicked_y < 0:
		return Vector2i.MIN

	if clicked_x >= grid_size or clicked_y >= grid_size:
		return Vector2i.MIN

	return Vector2i(clicked_x, clicked_y)

func _get_row_clue_position(row_index: int, offset: int) -> Vector2:
	return Vector2i(
		(puzzle.MaxRowClues - offset - 1) * CELL_SIZE,
		(puzzle.MaxColumnClues * CELL_SIZE) + (row_index * CELL_SIZE) + FONT_OFFSET
	)

func _get_col_clue_position(col_index: int, offset: int) -> Vector2:
	return Vector2i(
		(puzzle.MaxRowClues * CELL_SIZE) + (col_index * CELL_SIZE),
		(puzzle.MaxColumnClues - offset - 1) * CELL_SIZE + FONT_OFFSET
	)

#endregion
