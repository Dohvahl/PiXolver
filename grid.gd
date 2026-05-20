extends Control

signal size_changed

@export_range(2, 30) var grid_size: int :
	set(new_size):
		grid_size = new_size
		size_changed.emit()
var cell_size := 32

var puzzle : Puzzle

func _ready() -> void:
	puzzle = Puzzle.new(grid_size)

func _on_size_changed() -> void:
	size = Vector2i(grid_size * cell_size, grid_size * cell_size)
	puzzle = Puzzle.new(grid_size)
	queue_redraw()

func _draw() -> void:
	get_window().content_scale_size = Vector2i(cell_size * grid_size, cell_size * grid_size)
	
	for x in range(grid_size):
		for y in range(grid_size):
			var rect = Rect2(
				x * cell_size, 
				y * cell_size,
				cell_size,
				cell_size
				)

			var cell_index = get_cell_index(rect.position)
			if puzzle.is_cell_filled(cell_index):
				draw_rect(rect, Color.WHITE, true)
			else:
				draw_rect(rect, Color.WHITE, false, 1.5)


#var dragging := false
#var starting_drag_cell := -1
func _input(event: InputEvent) -> void:
	if event is InputEventMouseButton and event.is_pressed():
		#dragging = true
		#starting_drag_cell = get_cell_index(event.position)
	#elif event is InputEventMouseButton and event.is_released():
		#if dragging:
			#var current_cell = get_cell_index(event.position)
			#for cell in range(starting_drag_cell, current_cell):
				#puzzle.fill_cell(cell)
			#
		#dragging = false
		
		var cell_clicked = get_cell_index(event.position)
		if cell_clicked < 0:
			return
			
		if cell_clicked >= grid_size * grid_size:
			return
		
		# Toggle cell
		if puzzle.toggle_cell(cell_clicked):
			queue_redraw()
		else:
			print("Something went wrong toggling cell %d" % cell_clicked)

func get_cell_index(pos: Vector2) -> int:
	if pos < Vector2.ZERO:
		return -1
		
	var clicked_x = int(pos.x / cell_size)
	var clicked_y = int(pos.y / cell_size)
	
	if clicked_x >= grid_size or clicked_y >= grid_size:
		return -1
		
	return clicked_x + (clicked_y * grid_size)
	
