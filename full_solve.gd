extends Node

var sample_puzzles : Array[Puzzle]
@export_range(10, 5000) var max_puzzles : int = 10

const DATA_FILE_PATH := "res://results"

@export var solver_scene : PackedScene

# run through each sample puzzle and attempt to solve it
func _ready() -> void:
	var i = 0
	var sample_puzzle_files = DirAccess.get_files_at("res://SamplePuzzles/")
	sample_puzzles.resize(mini(max_puzzles, sample_puzzle_files.size()))
	for file in sample_puzzle_files:
		if i >= max_puzzles:
			break
		var sample_puzzle = FileAccess.open("res://SamplePuzzles/" + file, FileAccess.READ)
		if !sample_puzzle:
			print("Failed to open sample puzzle '%s': %s" % [file, FileAccess.get_open_error()])
			continue

		sample_puzzles[i] = Puzzle.new(30, sample_puzzle.get_as_text())
		sample_puzzle.close()
		i += 1

	var total_run := 0
	var total_solved := 0

	print("Begin Solving")

	var start_time := Time.get_ticks_usec()
	for puzzle in sample_puzzles:
		if !puzzle:
			continue

		# set up the solver
		var solver = solver_scene.instantiate()
		add_child(solver)

		var was_solved = solver.run(puzzle, false)
		if was_solved: total_solved += 1
		total_run += 1
	var end_time := Time.get_ticks_usec()

	print("Solved %d of %d puzzles" % [total_solved, total_run])
	print("Total Solve Time: %d microsecs" % (end_time - start_time))
	if total_run > 0:
		print("Average solve time: %0.2f microsecs" % float((end_time - start_time) / total_run))

	var data_file = FileAccess.open(DATA_FILE_PATH, FileAccess.READ_WRITE)
	if data_file:
		data_file.seek_end()
		# Format:
		# Date, #runs, #solved, total_time (microseconds), average_solve_time (microseconds)
		data_file.store_line(str("%s,%d,%d,%d,%0.2f" %
			[Time.get_datetime_string_from_system(),
			total_run,
			total_solved,
			(end_time - start_time),
			float((end_time - start_time) / total_run)
			]))
		data_file.close()
	else:
		print("Can't record data")
	get_tree().quit()
