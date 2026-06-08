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

	# stats on correctly filled cells. This doesn't account for "over filled" states
	var min_cor_fill_pct := INF
	var max_cor_fill_pct := -INF
	var avg_cor_fill_pct := 0.0

	# stats on correct cells. This includes correctly empty cells
	var min_sol_pct := INF
	var max_sol_pct := -INF
	var avg_sol_pct := 0.0

	# stats on incorrect cells
	var min_diff := INF
	var max_diff := -INF
	var avg_diff := 0.0

	var total_cells := 900
	var start_time := Time.get_ticks_usec()
	for puzzle in sample_puzzles:
		if !puzzle:
			continue

		# set up the solver
		var solver = solver_scene.instantiate()
		add_child(solver)

		var results = solver.run(puzzle, false)
		if results.get("is_solved"): total_solved += 1
		else:
			var pct_filled = results.get("filled", 0.0)
			var pct_solved = results.get("solved", 0.0)
			var incorrect = results.get("incorrect")

			min_cor_fill_pct = min(pct_filled, min_cor_fill_pct)
			max_cor_fill_pct = max(pct_filled, max_cor_fill_pct)
			avg_cor_fill_pct += pct_filled

			min_sol_pct = min(pct_solved, min_sol_pct)
			max_sol_pct = max(pct_solved, max_sol_pct)
			avg_sol_pct += pct_solved

			if incorrect:
				min_diff = min(incorrect, min_diff)
				max_diff = max(incorrect, max_diff)
				avg_diff += incorrect

		total_run += 1
	var end_time := Time.get_ticks_usec()

	if total_run <= 0:
		print("Didn't run any solvers")
		get_tree().quit()
		return

	avg_cor_fill_pct = avg_cor_fill_pct / total_run
	avg_sol_pct = avg_sol_pct / total_run
	avg_diff = avg_diff / total_run

	print("Solved %d of %d puzzles" % [total_solved, total_run])
	print("Total Solve Time: %d microsecs" % (end_time - start_time))

	print("Average solve time: %0.2f microsecs" % (float(end_time - start_time) / total_run))

	print("\nMin Correctly Filled Cells: %.2f%%" % (min_cor_fill_pct * 100))
	print("Max Correctly Filled Cells: %.2f%%" % (max_cor_fill_pct * 100))
	print("Average Correctly Filled Cells: %.2f%%" % (avg_cor_fill_pct * 100))

	print("\nMin Correct Cells: %.2f%%" % (min_sol_pct * 100))
	print("Max Correct Cells: %.2f%%" % (max_sol_pct * 100))
	print("Average Correct Cells: %.2f%%" % (avg_sol_pct * 100))

	print("\nMin Incorrect Cells: %d/%d" % [min_diff, total_cells])
	print("Max Incorrect Cells: %d/%d" % [max_diff, total_cells])
	print("Average Incorrect Cells: %d/%d" % [avg_diff, total_cells])

	var data_file = FileAccess.open(DATA_FILE_PATH, FileAccess.READ_WRITE)
	if data_file:
		data_file.seek_end()
		# Format:
		# Date, #runs, #solved, total_time (microseconds), average_solve_time (microseconds),
		# min/max/avg correctly filled, min/max/avg correct, min/max/avg diff
		data_file.store_line(str("%s,%d,%d,%d,%0.2f,%.5f,%.5f,%.5f,%.5f,%.5f,%.5f,%d,%d,%d" %
			[Time.get_datetime_string_from_system(),
			total_run,
			total_solved,
			(end_time - start_time),
			(float(end_time - start_time) / total_run),
			min_cor_fill_pct,
			max_cor_fill_pct,
			avg_cor_fill_pct,
			min_sol_pct,
			max_sol_pct,
			avg_sol_pct,
			min_diff,
			max_diff,
			avg_diff
			]))
		data_file.close()
	else:
		print("Can't record data")
	get_tree().quit()
