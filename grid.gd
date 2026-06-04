extends Control


var puzzle : Puzzle							# data representation of the puzzle

@export_range(1, 5000) var puzzle_number : int = 1
const SAMPLE_PUZZLES_PATH := "res://SamplePuzzles/rand"

var _total_width							# grid width + row clues area width
var _total_height							# grid height + column clues area height

var solved_color = Color.CHARTREUSE
var unsolved_color = Color.NAVY_BLUE

const CELL_SIZE := 32						# pixels per cell
var FONT_OFFSET := int((CELL_SIZE + ThemeDB.fallback_font_size) / 2)
@export var unsolved_clue_color : Color
@export var solved_clue_color : Color

@export var solver_scene : PackedScene
var solver

var solved := false

#region DEBUG VARIABLES

@export var draw_clue_cells: bool :
	set(new_val):
		draw_clue_cells = new_val
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

	puzzle = Puzzle.new(grid_size, initial_state)

	_total_width = CELL_SIZE * (puzzle.max_row_clues + grid_size)
	_total_height = CELL_SIZE * (puzzle.max_col_clues + grid_size)

	# size the window to contain the whole puzzle
	get_window().content_scale_size = Vector2i(_total_width, _total_height)

	$Message.hide()

	if solver_scene != null and solver_scene.can_instantiate():
		solver = solver_scene.instantiate()
		add_child(solver)
	solved = false

func _draw() -> void:
	# draw the row and column clues first, since the puzzle grid is affected by their position
	_draw_row_clues()
	_draw_column_clues()

	# now draw the puzzle grid, offset appropriately
	_draw_puzzle_grid()

	# check if we've solved it
	if puzzle.is_solved():
		solved = true
		$Message.text = "Solved!"
		$Message.show()

var iterations = 1
func _input(event: InputEvent) -> void:
	if solved:
		return

	if event is InputEventMouseButton and event.is_pressed():
		var cell_clicked = _get_cell_index_from_position(event.position)
		if !puzzle.is_valid_cell_index(cell_clicked.x, cell_clicked.y):
			return

		if event.button_index == MOUSE_BUTTON_LEFT:	# left click
			# Toggle cell
			if puzzle.toggle_cell(cell_clicked.x, cell_clicked.y):
				queue_redraw()
		elif event.button_index == MOUSE_BUTTON_RIGHT: # right click
			# Mark cell
			if puzzle.is_cell_marked(cell_clicked.x, cell_clicked.y) && puzzle.unmark_cell(cell_clicked.x, cell_clicked.y):
				queue_redraw()
			elif puzzle.mark_cell(cell_clicked.x, cell_clicked.y):
				queue_redraw()
	elif event.is_action_pressed("run_solver"):
		solver.run(puzzle, true)
		queue_redraw()
	elif event.is_action_pressed("run_solver_single"):
		solver.run_single(puzzle, iterations, true)
		iterations += 1
		queue_redraw()

#region "Private" functions

#region Drawing Functions

func _draw_row_clues() -> void:
	for row_index in range(0, grid_size):
		var clue_color = solved_color if puzzle.is_row_solved(row_index) else unsolved_color
		var clues := puzzle.get_row_clues(row_index)
		if clues.is_empty():
			# draw a '0'
			var clue_position := _get_row_clue_position(row_index, 1)
			draw_string(ThemeDB.fallback_font, _get_row_clue_position(row_index, 1), "0", HORIZONTAL_ALIGNMENT_CENTER, CELL_SIZE, 16, solved_clue_color)
#region DEBUG
			if draw_clue_cells:
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
			var clue := clues[clues.size() - i - 1]
			var clue_position := _get_row_clue_position(row_index, i)
			var color = solved_clue_color if clue.is_solved() else unsolved_clue_color
			draw_string(ThemeDB.fallback_font, clue_position, str(clue._value), HORIZONTAL_ALIGNMENT_CENTER, CELL_SIZE, 16, color)
#region DEBUG
			if draw_clue_cells:
				var clue_rect = Rect2(
					clue_position - Vector2(0, FONT_OFFSET),
					Vector2(CELL_SIZE, CELL_SIZE)
				)
				draw_rect(clue_rect, clue_color, false, 1.0)
#endregion DEBUG
			i += 1

func _draw_column_clues() -> void:
	for col_index in range(0, grid_size):
		var clue_color = solved_color if puzzle.is_column_solved(col_index) else unsolved_color
		var clues := puzzle.get_col_clues(col_index)
		if clues.is_empty():
			# draw a '0'
			var clue_position = _get_col_clue_position(col_index, 1)
			draw_string(ThemeDB.fallback_font, clue_position, "0", HORIZONTAL_ALIGNMENT_CENTER, CELL_SIZE, 16, solved_clue_color)
#region DEBUG
			if draw_clue_cells:
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
			var clue := clues[clues.size() - 1 - i]
			var clue_position = _get_col_clue_position(col_index, i)
			var color = solved_clue_color if clue.is_solved() else unsolved_clue_color
			draw_string(ThemeDB.fallback_font, clue_position, str(clue._value), HORIZONTAL_ALIGNMENT_CENTER, CELL_SIZE, 16, color)
#region DEBUG
			if draw_clue_cells:
				var clue_rect = Rect2(
					clue_position - Vector2(0, FONT_OFFSET),
					Vector2i(CELL_SIZE, CELL_SIZE)
				)
				draw_rect(clue_rect, clue_color, false, 1.0)
#endregion DEBUG
			i += 1

func _draw_puzzle_grid() -> void:
	for x in range(grid_size):
		for y in range(grid_size):
			var rect = Rect2(
				CELL_SIZE * (x + puzzle.max_row_clues),
				CELL_SIZE * (y + puzzle.max_col_clues),
				CELL_SIZE,
				CELL_SIZE
				)

			var cell_location = _get_cell_index_from_position(rect.position)
			if puzzle.is_cell_filled(cell_location.x, cell_location.y):
				draw_rect(rect, Color.BLACK, true)
			elif puzzle.is_cell_marked(cell_location.x, cell_location.y):
				draw_rect(rect, Color.RED, true)
			else:
				draw_rect(rect, Color.WHITE, false, 1.5)

#endregion

func _get_cell_index_from_position(pos: Vector2) -> Vector2i:
	if pos < Vector2.ZERO:
		return Vector2i.MIN

	var clicked_x = int(pos.x / CELL_SIZE) - puzzle.max_row_clues
	var clicked_y = int(pos.y / CELL_SIZE) - puzzle.max_col_clues

	# we have to account for the clues areas when checking the grid locations
	if clicked_x < 0 or clicked_y < 0:
		return Vector2i.MIN

	if clicked_x >= grid_size or clicked_y >= grid_size:
		return Vector2i.MIN

	return Vector2i(clicked_x, clicked_y)

func _get_row_clue_position(row_index: int, offset: int) -> Vector2:
	return Vector2i(
		(puzzle.max_row_clues - offset - 1) * CELL_SIZE,
		(puzzle.max_col_clues * CELL_SIZE) + (row_index * CELL_SIZE) + FONT_OFFSET
	)

func _get_col_clue_position(col_index: int, offset: int) -> Vector2:
	return Vector2i(
		(puzzle.max_row_clues * CELL_SIZE) + (col_index * CELL_SIZE),
		(puzzle.max_col_clues - offset - 1) * CELL_SIZE + FONT_OFFSET
	)

#endregion
