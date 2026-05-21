extends Control


var puzzle : Puzzle							# data representation of the puzzle

@export_range(2, 30) var grid_size: int 	# dimenion for the grid. Total grid size is grid_size * grid_size
var cell_size := 32							# pixels per cell
var _total_width							# grid width + row clues area width
var _total_height							# grid height + column clues area height

var solved := false

# initial state is a string representing the puzzle at the start. 
# 'x' -> a filled cell
# '-' -> an empty cell
# '/' -> new row
# eg. A 2x2 puzzle where cells (1, 1) and (0, 1) are filled would be represented as
# 	1-1x/1x1-
@export_multiline("Starting Puzzle State") var initial_state: String

func _ready() -> void:
	puzzle = Puzzle.new(grid_size, initial_state)
	
	_total_width = cell_size * (puzzle.max_row_clues + grid_size)
	#_total_height = column_clues_height + (cell_size * grid_size)
	
	$Message.hide()
	solved = false


func _draw() -> void:
	get_window().content_scale_size = Vector2i(_total_width, cell_size * grid_size)
	
	# TODO - draw row clues
	for row_index in range(0, grid_size):
		var clues_var = puzzle.row_clues.get(row_index)
		if !clues_var:
			# draw a '0'
			draw_string(ThemeDB.fallback_font, _get_row_clue_position(1, row_index), "0", HORIZONTAL_ALIGNMENT_CENTER, cell_size)
			continue
			
		var clues = Array(clues_var)
		# draw the clues right-justified
		var i = clues.size()
		while i > 0:
			draw_string(ThemeDB.fallback_font, _get_row_clue_position(i, row_index), str(clues[i-1]), HORIZONTAL_ALIGNMENT_CENTER, cell_size)
			i -= 1
	
	# TODO - draw column clues
	
	# draw the puzzle grid, offset by the clues areas
	for x in range(grid_size):
		for y in range(grid_size):
			var rect = Rect2(
				cell_size * (x + puzzle.max_row_clues), 
				y * cell_size,
				cell_size,
				cell_size
				)

			var cell_index = _get_cell_index_from_position(rect.position)
			if puzzle.is_cell_filled(cell_index):
				draw_rect(rect, Color.BLACK, true)
			else:
				draw_rect(rect, Color.WHITE, false, 1.5)
				
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

func _get_cell_index_from_position(pos: Vector2) -> int:
	if pos < Vector2.ZERO:
		return -1
		
	var clicked_x = int(pos.x / cell_size) - puzzle.max_row_clues
	var clicked_y = int(pos.y / cell_size) - puzzle.max_col_clues
	
	# we have to account for the clues areas when checking the grid locations
	if clicked_x < 0 or clicked_y < 0:
		return -1
	
	if clicked_x >= grid_size or clicked_y >= grid_size:
		return -1
		
	return puzzle.cell_index_from_location(clicked_x, clicked_y)

func _get_row_clue_position(x: int, y: int) -> Vector2i:
	return Vector2i(
		(puzzle.max_row_clues - x) * cell_size,
		(y * cell_size) + (cell_size + ThemeDB.fallback_font_size) / 2
	)
	
#endregion
