using Godot;
using System.Globalization;
using System.Linq.Expressions;

/// <summary>
/// Headless benchmark harness: loads the sample puzzles, runs the solver against each, prints
/// aggregate statistics, and appends a row to the results data file.
/// </summary>
public partial class FullSolve : Node
{
	private const string DataFilePath = "res://results";
	private const string SamplePuzzlesPath = "res://SamplePuzzles/";

	[Export(PropertyHint.Range, "10,5000")]
	public int MaxPuzzles { get; set; } = 10;

	[Export]
	public PackedScene SolverScene { get; set; }

	// run through each sample puzzle and attempt to solve it
	public override void _Ready()
	{
		int i = 0;
		string[] samplePuzzleFiles = DirAccess.GetFilesAt(SamplePuzzlesPath);
		var samplePuzzles = new Puzzle[Mathf.Min(MaxPuzzles, samplePuzzleFiles.Length)];
		foreach (string file in samplePuzzleFiles)
		{
			if (i >= MaxPuzzles)
				break;

			using FileAccess samplePuzzle = FileAccess.Open(SamplePuzzlesPath + file, FileAccess.ModeFlags.Read);
			if (samplePuzzle == null)
			{
				GD.Print($"Failed to open sample puzzle '{file}': {FileAccess.GetOpenError()}");
				continue;
			}

			var puzzle = new Puzzle();
			puzzle.Initialize(samplePuzzle.GetPath(), 30, samplePuzzle.GetAsText());
			samplePuzzles[i] = puzzle;
			i += 1;
		}

		int totalRun = 0;
		int totalSolved = 0;

		GD.Print("Begin Solving");

		// stats on correctly filled cells. This doesn't account for "over filled" states
		double minCorFillPct = double.PositiveInfinity;
		double maxCorFillPct = double.NegativeInfinity;
		double avgCorFillPct = 0.0;
		string maxCorFillPuzzle = "";

		// stats on correct cells. This includes correctly empty cells
		double minSolPct = double.PositiveInfinity;
		double maxSolPct = double.NegativeInfinity;
		double avgSolPct = 0.0;
		string maxSolPuzzle = "";

		// stats on incorrect cells
		double minDiff = double.PositiveInfinity;
		double maxDiff = double.NegativeInfinity;
		double avgDiff = 0.0;

		int totalCells = 900;
		ulong startTime = Time.Singleton.GetTicksUsec();
		for (int puzzleIndex = 0; puzzleIndex < samplePuzzles.Length; puzzleIndex++)
		{
			Puzzle puzzle = samplePuzzles[puzzleIndex];
			if (puzzle == null)
				continue;

			// set up the solver
			var solver = new Solver();
			solver.Init(puzzle.GridSize);

			Godot.Collections.Dictionary results = solver.Run(puzzle, false);
			if (results.ContainsKey("is_solved"))
			{
				totalSolved += 1;
			}
			else
			{
				double pctFilled = results.TryGetValue("filled", out Variant filledValue) ? filledValue.AsDouble() : 0.0;
				double pctSolved = results.TryGetValue("solved", out Variant solvedValue) ? solvedValue.AsDouble() : 0.0;
				int incorrect = results.TryGetValue("incorrect", out Variant incorrectValue) ? incorrectValue.AsInt32() : 0;

				minCorFillPct = Mathf.Min(pctFilled, minCorFillPct);
				if (pctFilled > maxCorFillPct)
				{
					maxCorFillPct = pctFilled;
					maxCorFillPuzzle = puzzle.PuzzleFile;
				}
				avgCorFillPct += pctFilled;

				minSolPct = Mathf.Min(pctSolved, minSolPct);
				if (pctSolved > maxSolPct)
				{
					maxSolPct = pctSolved;
					maxSolPuzzle = puzzle.PuzzleFile;
				}
				avgSolPct += pctSolved;

				if (incorrect != 0)
				{
					minDiff = Mathf.Min(incorrect, minDiff);
					maxDiff = Mathf.Max(incorrect, maxDiff);
					avgDiff += incorrect;
				}
			}

			totalRun += 1;
		}
		ulong endTime = Time.Singleton.GetTicksUsec();

		if (totalRun <= 0)
		{
			GD.Print("Didn't run any solvers");
			GetTree().Quit();
			return;
		}

		avgCorFillPct /= totalRun;
		avgSolPct /= totalRun;
		avgDiff /= totalRun;

		GD.Print($"Solved {totalSolved} of {totalRun} puzzles");
		GD.Print($"Total Solve Time: {endTime - startTime} microsecs");

		GD.Print($"Average solve time: {(double)(endTime - startTime) / totalRun:F2} microsecs");

		GD.Print($"\nMin Correctly Filled Cells: {minCorFillPct * 100:F2}%");
		GD.Print($"Max Correctly Filled Cells: {maxCorFillPct * 100:F2}%, Puzzle - {maxCorFillPuzzle}");
		GD.Print($"Average Correctly Filled Cells: {avgCorFillPct * 100:F2}%");

		GD.Print($"\nMin Correct Cells: {minSolPct * 100:F2}%");
		GD.Print($"Max Correct Cells: {maxSolPct * 100:F2}%, Puzzle - {maxSolPuzzle}");
		GD.Print($"Average Correct Cells: {avgSolPct * 100:F2}%");

		GD.Print($"\nMin Incorrect Cells: {(long)minDiff}/{totalCells}");
		GD.Print($"Max Incorrect Cells: {(long)maxDiff}/{totalCells}");
		GD.Print($"Average Incorrect Cells: {(long)avgDiff}/{totalCells}");

		using FileAccess dataFile = FileAccess.Open(DataFilePath, FileAccess.ModeFlags.ReadWrite);
		if (dataFile != null)
		{
			dataFile.SeekEnd();
			// Format:
			// Date, #runs, #solved, total_time (microseconds), average_solve_time (microseconds),
			// min/max/avg correctly filled, min/max/avg correct, min/max/avg diff
			string line = string.Format(
				CultureInfo.InvariantCulture,
				"{0},{1},{2},{3},{4:F2},{5:F5},{6:F5},{7:F5},{8:F5},{9:F5},{10:F5},{11},{12},{13}",
				Time.Singleton.GetDatetimeStringFromSystem(),
				totalRun,
				totalSolved,
				endTime - startTime,
				(double)(endTime - startTime) / totalRun,
				minCorFillPct,
				maxCorFillPct,
				avgCorFillPct,
				minSolPct,
				maxSolPct,
				avgSolPct,
				(long)minDiff,
				(long)maxDiff,
				(long)avgDiff);
			dataFile.StoreLine(line);
		}
		else
		{
			GD.Print("Can't record data");
		}
		GetTree().Quit();
	}
}
