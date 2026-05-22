extends Control


var puzzle : Puzzle							# data representation of the puzzle

@export_range(2, 30) var grid_size: int 	# dimenion for the grid. Total grid size is grid_size * grid_size
var _total_width							# grid width + row clues area width
var _total_height							# grid height + column clues area height

const CELL_SIZE := 32						# pixels per cell
var FONT_OFFSET := (CELL_SIZE + ThemeDB.fallback_font_size) / 2

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
	solved = false

func _draw() -> void:
	# draw the row and column clues first, since the puzzle grid is affected by their position
	_draw_clues_area(puzzle.row_clues, _get_row_clue_position)
	_draw_clues_area(puzzle.col_clues, _get_col_clue_position)
	
	# now draw the puzzle grid, offset appropriately
	_draw_puzzle_grid()
					
	# check if we've solved it
	if puzzle.is_solved():
		solved = true
		$Message.text = "Solved!"
		$Message.show()

#var dragging := false
#var starting_drag_cell := -1
func _input(event: InputEvent) -> void:
	if solved:
		return
		
	if event is InputEventMouseButton and event.is_pressed():
		#dragging = true
		#starting_drag_cell = get_cell_index(event.position)
	#elif event is InputEventMouseButton and event.is_released():
		#if dragging:
			#var current_cell = get_cell_index(event.position)
			#for cell in range(starting_drag_cell, current_cell):
				#puzzle.fill_cell(cell)

		#dragging = false
		
		var cell_clicked = _get_cell_index_from_position(event.position)
		if !puzzle.is_valid_cell_index(cell_clicked):
			return
		
		# Toggle cell
		if puzzle.toggle_cell(cell_clicked):
			queue_redraw()
		else:
			print("Something went wrong toggling cell %d" % cell_clicked)

#region "Private" functions

#region Drawing Functions
			
func _draw_clues_area(clues: Dictionary[int, Array], get_clue_position: Callable) -> void:
	for index in range(0, grid_size):
		var clues_var = clues.get(index)
		if !clues_var:
			# draw a '0'
			var clue_position = get_clue_position.call(index, 1)
			
			if draw_clue_cells:
				var clue_rect = Rect2(
					clue_position - Vector2(0, FONT_OFFSET),
					Vector2i(CELL_SIZE, CELL_SIZE)
				)
				draw_rect(clue_rect, Color.GREEN, false, 1.0)
				
			draw_string(ThemeDB.fallback_font, clue_position, "0", HORIZONTAL_ALIGNMENT_CENTER, CELL_SIZE)
			continue
			
		# draw the clues bottom-up
		var clues_array = Array(clues_var) 
		var i = clues_array.size()
		while i > 0:
			var clue_position = get_clue_position.call(index, i)
			
			if draw_clue_cells:
				var clue_rect = Rect2(
					clue_position - Vector2(0, FONT_OFFSET),
					Vector2i(CELL_SIZE, CELL_SIZE)
				)
				draw_rect(clue_rect, Color.GREEN, false, 1.0)

			draw_string(ThemeDB.fallback_font, clue_position, str(clues_array[i-1]), HORIZONTAL_ALIGNMENT_CENTER, CELL_SIZE)
			i -= 1

func _draw_puzzle_grid() -> void:
	for x in range(grid_size):
		for y in range(grid_size):
			var rect = Rect2(
				CELL_SIZE * (x + puzzle.max_row_clues), 
				CELL_SIZE * (y + puzzle.max_col_clues),
				CELL_SIZE,
				CELL_SIZE
				)

			var cell_index = _get_cell_index_from_position(rect.position)
			if puzzle.is_cell_filled(cell_index):
				draw_rect(rect, Color.BLACK, true)
			else:
				draw_rect(rect, Color.WHITE, false, 1.5)
				
#endregion

func _get_cell_index_from_position(pos: Vector2) -> int:
	if pos < Vector2.ZERO:
		return -1
		
	var clicked_x = int(pos.x / CELL_SIZE) - puzzle.max_row_clues
	var clicked_y = int(pos.y / CELL_SIZE) - puzzle.max_col_clues
	
	# we have to account for the clues areas when checking the grid locations
	if clicked_x < 0 or clicked_y < 0:
		return -1
	
	if clicked_x >= grid_size or clicked_y >= grid_size:
		return -1
		
	return puzzle.cell_index_from_location(clicked_x, clicked_y)

func _get_row_clue_position(row_index: int, offset: int) -> Vector2:
	return Vector2i(
		(puzzle.max_row_clues - offset) * CELL_SIZE,
		(puzzle.max_col_clues * CELL_SIZE) + (row_index * CELL_SIZE) + FONT_OFFSET
	)

func _get_col_clue_position(col_index: int, offset: int) -> Vector2:
	return Vector2i(
		(puzzle.max_row_clues * CELL_SIZE) + (col_index * CELL_SIZE),
		(puzzle.max_col_clues - offset) * CELL_SIZE + FONT_OFFSET
	)
	
#endregion
