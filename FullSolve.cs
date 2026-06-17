using Godot;
using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;

/// <summary>
/// Headless benchmark harness: loads a random sample of puzzles, then runs a full solve over all of
/// them <see cref="BenchmarkRuns"/> times, printing aggregate statistics and appending a results row
/// per run. A min/median/avg timing summary across the runs is printed at the end.
/// </summary>
public partial class FullSolve : Node
{
	private const string DataFilePath = "res://results";
	private const string SamplePuzzlesPath = "res://SamplePuzzles/";

	[Export(PropertyHint.Range, "10,5000")]
	public int MaxPuzzles { get; set; } = 10;

	[Export]
	public PackedScene SolverScene { get; set; }

	// How many times to run the full solve over the loaded puzzles (minimum 1). Each run is timed and
	// logged; a min/median/avg timing summary across runs is printed at the end.
	[Export]
	public int BenchmarkRuns { get; set; } = 5;

	// load a random sample of puzzles, then solve them all BenchmarkRuns times
	public override void _Ready()
	{
		Random rand = new Random();
		string[] samplePuzzleFiles = DirAccess.GetFilesAt(SamplePuzzlesPath);

		// pick a distinct random subset of the available files via a partial Fisher–Yates shuffle:
		// swap each of the first 'targetCount' slots with a random slot at or after it, drawing
		// uniformly from every file with no repeats.
		int targetCount = Mathf.Min(MaxPuzzles, samplePuzzleFiles.Length);
		for (int j = 0; j < targetCount; j++)
		{
			int k = rand.Next(j, samplePuzzleFiles.Length);
			(samplePuzzleFiles[j], samplePuzzleFiles[k]) = (samplePuzzleFiles[k], samplePuzzleFiles[j]);
		}

		var samplePuzzles = new Puzzle[targetCount];
		int count = 0;
		for (int j = 0; j < targetCount; j++)
		{
			string file = samplePuzzleFiles[j];

			using FileAccess samplePuzzle = FileAccess.Open(SamplePuzzlesPath + file, FileAccess.ModeFlags.Read);
			if (samplePuzzle == null)
			{
				GD.Print($"Failed to open sample puzzle '{file}': {FileAccess.GetOpenError()}");
				continue;
			}

			string text = samplePuzzle.GetAsText();
			if (string.IsNullOrEmpty(text))
				continue;

			// A clues-only file starts with an "x y" header; a solution file starts with a row of 0/1s.
			var puzzle = new Puzzle();
			string firstLine = text.Split('\n')[0].Trim();
			string[] header = firstLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
			if (header.Length == 2 && int.TryParse(header[0], out _) && int.TryParse(header[1], out _))
				puzzle.InitializeFromClues(samplePuzzle.GetPath(), text);
			else
				puzzle.Initialize(samplePuzzle.GetPath(), firstLine.Length, text);

			samplePuzzles[count] = puzzle;
			count += 1;
		}

		int runs = Math.Max(BenchmarkRuns, 1);
		var times = new double[runs];
		for (int run = 0; run < runs; run++)
		{
			GD.Print($"\n=== Full solve run {run + 1}/{runs} ===");
			times[run] = RunFullSolve(samplePuzzles);
		}

		if (runs > 1)
		{
			double[] sorted = (double[])times.Clone();
			Array.Sort(sorted);
			GD.Print($"\n=== Timing across {runs} runs ===  min={sorted[0]:F1}  median={sorted[runs / 2]:F1}  max={sorted[runs - 1]:F1}  avg={times.Average():F1} microsecs");
		}

		GetTree().Quit();
	}

	/// <summary>
	/// Resets and solves every loaded puzzle once, prints aggregate statistics, and appends a row to
	/// the results file. Returns the total solve time for this pass, in microseconds.
	/// </summary>
	private double RunFullSolve(Puzzle[] samplePuzzles)
	{
		int totalRun = 0;
		int totalSolved = 0;
		int[] solvedPuzzles = new int[samplePuzzles.Length];


		// stats on correctly filled cells. This doesn't account for "over filled" states
		double minCorFillPct = double.PositiveInfinity;
		double maxCorFillPct = double.NegativeInfinity;
		double avgCorFillPct = 0.0;
		string minCorFillPuzzle = "";
		string maxCorFillPuzzle = "";

		// stats on correct cells. This includes correctly empty cells
		double minSolPct = double.PositiveInfinity;
		double maxSolPct = double.NegativeInfinity;
		double avgSolPct = 0.0;
		string minSolPuzzle = "";
		string maxSolPuzzle = "";

		// stats on incorrect cells
		double minDiff = double.PositiveInfinity;
		double maxDiff = double.NegativeInfinity;
		double avgDiff = 0.0;


        int totalCells = 900;
		long startTime = Stopwatch.GetTimestamp();
		for (int puzzleIndex = 0; puzzleIndex < samplePuzzles.Length; puzzleIndex++)
		{
			Puzzle puzzle = samplePuzzles[puzzleIndex];
			if (puzzle == null)
				continue;

			// start from a clean grid so repeated runs each do the full work
			puzzle.Reset();

			// set up the solver
			var solver = new Solver();
			solver.Init(puzzle.GridSize);

			Godot.Collections.Dictionary results = solver.Run(puzzle, false);
			if (results.ContainsKey("is_solved"))
			{
                // each puzzle is named "rand####", so we can extract the number by getting the substring after "rand" and parsing it as an int
				int indexOfRand = puzzle.PuzzleFile.LastIndexOf("rand");
				if (indexOfRand >= 0)
				{
                    string puzzleNumberStr = puzzle.PuzzleFile.Substring(indexOfRand + 4);
                    if (int.TryParse(puzzleNumberStr, out int puzzleNumber))
                    {
                        solvedPuzzles[totalSolved] = puzzleNumber;
                    }
                }

				totalSolved++;
			}
			else if (puzzle.HasSolution)
			{
				double pctFilled = results.TryGetValue("filled", out Variant filledValue) ? filledValue.AsDouble() : 0.0;
				double pctSolved = results.TryGetValue("solved", out Variant solvedValue) ? solvedValue.AsDouble() : 0.0;
				int incorrect = results.TryGetValue("incorrect", out Variant incorrectValue) ? incorrectValue.AsInt32() : 0;

				if (pctFilled < minCorFillPct)
				{
                    minCorFillPct = pctFilled;
                    minCorFillPuzzle = puzzle.PuzzleFile;
                }
				if (pctFilled > maxCorFillPct)
				{
					maxCorFillPct = pctFilled;
					maxCorFillPuzzle = puzzle.PuzzleFile;
				}
				avgCorFillPct += pctFilled;

				if (pctSolved < minSolPct)
				{
					minSolPct = pctSolved;
					minSolPuzzle = puzzle.PuzzleFile;
                }
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
		double elapsedMicroseconds = Stopwatch.GetElapsedTime(startTime).TotalMicroseconds;

		if (totalRun <= 0)
		{
			GD.Print("Didn't run any solvers");
			return elapsedMicroseconds;
		}

		avgCorFillPct /= totalRun;
		avgSolPct /= totalRun;
		avgDiff /= totalRun;

		GD.Print($"Solved {totalSolved} of {totalRun} puzzles");
		GD.Print($"Solved Puzzles:[\n{string.Join("\n", solvedPuzzles.Where(p => p > 0))}]");
		GD.Print($"Total Solve Time: {elapsedMicroseconds:F1} microsecs");

		GD.Print($"Average solve time: {elapsedMicroseconds / totalRun:F2} microsecs");

		GD.Print($"\nMin Correctly Filled Cells: {minCorFillPct * 100:F2}%, Puzzle - {minCorFillPuzzle}");
		GD.Print($"Max Correctly Filled Cells: {maxCorFillPct * 100:F2}%, Puzzle - {maxCorFillPuzzle}");
		GD.Print($"Average Correctly Filled Cells: {avgCorFillPct * 100:F2}%");

		GD.Print($"\nMin Correct Cells: {minSolPct * 100:F2}%, Puzzle - {minSolPuzzle}");
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
				DateTime.Now.ToString("s"),
				totalRun,
				totalSolved,
				(long)elapsedMicroseconds,
				elapsedMicroseconds / totalRun,
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

		return elapsedMicroseconds;
	}
}
