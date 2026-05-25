extends Node

@export var max_iterations := 5

# Called when the node enters the scene tree for the first time.
func _ready() -> void:
	print("I'm ready to solve!")

func run(puzzle: Puzzle) -> void:
	
	# run the solver until the puzzle is solved, but to keep it from getting
	# into an infinite loop, we cap the number of iterations
	var iterations := 0
	while !puzzle.is_solved():
		# check each set of row and column clues
		for row_clues in puzzle.row_clues:
			pass
			
		for column_clues in puzzle.col_clues:
			pass
			
		iterations += 1
		if iterations >= max_iterations:
			break
			
	if puzzle.is_solved():
		print("We did it!")
	else:
		print("FAILURE!")
