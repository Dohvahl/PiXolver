extends Control


var puzzle : Puzzle							# data representation of the puzzle

@export_range(2, 30) var grid_size: int 	# dimenion for the grid. Total grid size is grid_size * grid_size
var _total_width							# grid width + row clues area width
var _total_height							# grid height + column clues area height

const CELL_SIZE := 32						# pixels per cell
var FONT_OFFSET := int((CELL_SIZE + ThemeDB.fallback_font_size) / 2)

@export var solver_scene : PackedScene
var solver

var solved := false

#region DEBUG VARIABLES

@export var draw_clue_cells: bool :
	set(new_val):
		draw_clue_cells = new_val
		queue_redraw()

#endregion

# initial state is a string representing the puzzle at the start. 
# 'x' -> a filled cell
# '-' -> an empty cell
# '/' -> new row
# eg. A 2x2 puzzle where cells (1, 1) and (0, 1) are filled would be represented as
# 	1-1x/1x1-
@export_multiline("Starting Puzzle State") var initial_state: String

func _ready() -> void:
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
		solver.run(puzzle)

#region "Private" functions

#region Drawing Functions

func _draw_row_clues() -> void:
	for row_index in range(0, grid_size):
		var clues_var = puzzle.row_clues.get(row_index)
		if !clues_var:
			# draw a '0'
			var clue_position := _get_row_clue_position(row_index, 1)
			
			if draw_clue_cells:
				var clue_rect = Rect2(
					clue_position - Vector2(0, FONT_OFFSET),
					Vector2(CELL_SIZE, CELL_SIZE)
				)
				draw_rect(clue_rect, Color.GREEN, false, 1.0)
				
			draw_string(ThemeDB.fallback_font, _get_row_clue_position(row_index, 1), "0", HORIZONTAL_ALIGNMENT_CENTER, CELL_SIZE)
			continue
			
		var clues = Array(clues_var)
		# draw the clues right-justified
		var i = 0
		while i < clues.size():
			var clue_position := _get_row_clue_position(row_index, i)
			
			if draw_clue_cells:
				var clue_rect = Rect2(
					clue_position - Vector2(0, FONT_OFFSET),
					Vector2(CELL_SIZE, CELL_SIZE)
				)
				draw_rect(clue_rect, Color.GREEN, false, 1.0)
				
			draw_string(ThemeDB.fallback_font, clue_position, str(clues[clues.size() - i - 1]), HORIZONTAL_ALIGNMENT_CENTER, CELL_SIZE)
			i += 1

func _draw_column_clues() -> void:
	for col_index in range(0, grid_size):
		var clues_var = puzzle.col_clues.get(col_index)
		if !clues_var:
			# draw a '0'
			var clue_position = _get_col_clue_position(col_index, 1)
			
			if draw_clue_cells:
				var clue_rect = Rect2(
					clue_position,
					Vector2i(CELL_SIZE, CELL_SIZE)
				)
				draw_rect(clue_rect, Color.GREEN, false, 1.0)
				
			draw_string(ThemeDB.fallback_font, clue_position, "0", HORIZONTAL_ALIGNMENT_CENTER, CELL_SIZE)
			continue
			
		var clues = Array(clues_var)
		# draw the clues bottom-up
		var i = 0
		while i < clues.size():
			var clue_position = _get_col_clue_position(col_index, i)
			
			if draw_clue_cells:
				var clue_rect = Rect2(
					clue_position - Vector2(0, FONT_OFFSET),
					Vector2i(CELL_SIZE, CELL_SIZE)
				)
				draw_rect(clue_rect, Color.GREEN, false, 1.0)
				
			draw_string(ThemeDB.fallback_font, clue_position, str(clues[clues.size() - 1 - i]), HORIZONTAL_ALIGNMENT_CENTER, CELL_SIZE)
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
