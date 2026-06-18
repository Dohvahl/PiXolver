using Godot;
using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;

/// <summary>
/// Headless benchmark harness: loads a random sample of puzzles, then runs a full solve over all of
/// them <see cref="BenchmarkRuns"/> times. The solve is deterministic, so the correctness/line stats
/// are reported once for the batch; the timing is summarized across all runs (min/median/max/avg), and
/// a single results row (timing = median) is appended at the end.
/// </summary>
public partial class FullSolve : Node
{
	private const string DataFilePath = "res://results";
	private const string SamplePuzzlesPath = "res://SamplePuzzles/";

	[Export(PropertyHint.Range, "10,5000")]
	public int MaxPuzzles { get; set; } = 10;

	[Export]
	public bool OnlySolved { get; set; } = false;

	[Export]
	public PackedScene SolverScene { get; set; }

	// How many times to run the full solve over the loaded puzzles (minimum 1). Each run is timed; the
	// stats are reported once with timing summarized (min/median/max/avg) across all runs.
	[Export]
	public int BenchmarkRuns { get; set; } = 5;

	// load a random sample of puzzles, then solve them all BenchmarkRuns times
	public override void _Ready()
	{
		Random rand = new Random();
		string[] samplePuzzleFiles = [];
        if (OnlySolved)
			samplePuzzleFiles = ["rand64", "rand5001", "rand3884", "rand3915", "rand4564", "rand2181", "rand4473", "rand2941", "rand479", "rand2165", "rand363", "rand373", "rand1538", "rand603", "rand2104", "rand3149", "rand2921", "rand2872", "rand2830", "rand450", "rand343"];
		else
			samplePuzzleFiles = DirAccess.GetFilesAt(SamplePuzzlesPath);

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

		// The solve is deterministic, so every pass produces identical correctness/line stats; only the
		// timing varies. Run the batch to collect per-pass times, then report the stats once with the
		// timing summarized across all runs.
		int runs = Math.Max(BenchmarkRuns, 1);
		var times = new double[runs];
		RunStats stats = null;
		for (int run = 0; run < runs; run++)
		{
			stats = RunFullSolve(samplePuzzles);
			times[run] = stats.ElapsedMicroseconds;
		}

		Report(stats, times);

		GetTree().Quit();
	}

	/// <summary>
	/// Resets and solves every loaded puzzle once, gathering aggregate statistics for the pass.
	/// Returns the stats (including this pass's elapsed time); does not print or log.
	/// </summary>
	private RunStats RunFullSolve(Puzzle[] samplePuzzles)
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

		// lines processed (Try calls) for solved puzzles vs unsolved (reached no-change) puzzles
		int solvedMinLines = int.MaxValue;
		int solvedMaxLines = 0;
		long solvedSumLines = 0;
		int unsolvedMinLines = int.MaxValue;
		int unsolvedMaxLines = 0;
		long unsolvedSumLines = 0;

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

			int linesProcessed = results.TryGetValue("lines_processed", out Variant linesValue) ? linesValue.AsInt32() : 0;
			if (results.ContainsKey("is_solved"))
			{
				solvedMinLines = Mathf.Min(solvedMinLines, linesProcessed);
				solvedMaxLines = Mathf.Max(solvedMaxLines, linesProcessed);
				solvedSumLines += linesProcessed;
			}
			else
			{
				unsolvedMinLines = Mathf.Min(unsolvedMinLines, linesProcessed);
				unsolvedMaxLines = Mathf.Max(unsolvedMaxLines, linesProcessed);
				unsolvedSumLines += linesProcessed;
			}

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

		var stats = new RunStats
		{
			ElapsedMicroseconds = elapsedMicroseconds,
			TotalRun = totalRun,
		};

		if (totalRun <= 0)
			return stats;

		avgCorFillPct /= totalRun;
		avgSolPct /= totalRun;
		avgDiff /= totalRun;

		stats.TotalSolved = totalSolved;
		stats.SolvedPuzzles = solvedPuzzles;

		stats.MinCorFillPct = minCorFillPct;
		stats.MaxCorFillPct = maxCorFillPct;
		stats.AvgCorFillPct = avgCorFillPct;
		stats.MinCorFillPuzzle = minCorFillPuzzle;
		stats.MaxCorFillPuzzle = maxCorFillPuzzle;

		stats.MinSolPct = minSolPct;
		stats.MaxSolPct = maxSolPct;
		stats.AvgSolPct = avgSolPct;
		stats.MinSolPuzzle = minSolPuzzle;
		stats.MaxSolPuzzle = maxSolPuzzle;

		stats.MinDiff = minDiff;
		stats.MaxDiff = maxDiff;
		stats.AvgDiff = avgDiff;

		stats.SolvedMinLines = solvedMinLines;
		stats.SolvedMaxLines = solvedMaxLines;
		stats.SolvedSumLines = solvedSumLines;
		stats.UnsolvedMinLines = unsolvedMinLines;
		stats.UnsolvedMaxLines = unsolvedMaxLines;
		stats.UnsolvedSumLines = unsolvedSumLines;

		return stats;
	}

	/// <summary>
	/// Prints the aggregate stats once for the whole benchmark batch and appends a single results row.
	/// Correctness/line stats come from <paramref name="stats"/> (identical across passes); the timing
	/// is summarized across all <paramref name="times"/> (min/median/max/avg), since only timing varies.
	/// </summary>
	private void Report(RunStats stats, double[] times)
	{
		if (stats.TotalRun <= 0)
		{
			GD.Print("Didn't run any solvers");
			return;
		}

		const int totalCells = 900;
		int totalRun = stats.TotalRun;
		int totalSolved = stats.TotalSolved;
		int unsolvedRun = totalRun - totalSolved;

		// only timing varies between passes, so summarize it across the batch
		double[] sorted = (double[])times.Clone();
		Array.Sort(sorted);
		double minTime = sorted[0];
		double medianTime = sorted[sorted.Length / 2];
		double maxTime = sorted[sorted.Length - 1];
		double avgTime = times.Average();

		if (!OnlySolved)
		{
			GD.Print($"Solved {totalSolved} of {totalRun} puzzles");
			GD.Print($"Solved Puzzles:[\n{string.Join("\n", stats.SolvedPuzzles.Where(p => p > 0))}]");
		}

		if (times.Length > 1)
			GD.Print($"\nSolve time over {times.Length} runs (microsecs): min={minTime:F1}  median={medianTime:F1}  max={maxTime:F1}  avg={avgTime:F1}");
		else
			GD.Print($"\nTotal Solve Time: {medianTime:F1} microsecs");
		GD.Print($"Average solve time (median run): {medianTime / totalRun:F2} microsecs");

		if (!OnlySolved)
		{
			GD.Print($"\nMin Correctly Filled Cells: {stats.MinCorFillPct * 100:F2}%, Puzzle - {stats.MinCorFillPuzzle}");
			GD.Print($"Max Correctly Filled Cells: {stats.MaxCorFillPct * 100:F2}%, Puzzle - {stats.MaxCorFillPuzzle}");
			GD.Print($"Average Correctly Filled Cells: {stats.AvgCorFillPct * 100:F2}%");

			GD.Print($"\nMin Correct Cells: {stats.MinSolPct * 100:F2}%, Puzzle - {stats.MinSolPuzzle}");
			GD.Print($"Max Correct Cells: {stats.MaxSolPct * 100:F2}%, Puzzle - {stats.MaxSolPuzzle}");
			GD.Print($"Average Correct Cells: {stats.AvgSolPct * 100:F2}%");

			GD.Print($"\nMin Incorrect Cells: {(long)stats.MinDiff}/{totalCells}");
			GD.Print($"Max Incorrect Cells: {(long)stats.MaxDiff}/{totalCells}");
			GD.Print($"Average Incorrect Cells: {(long)stats.AvgDiff}/{totalCells}");
		}

		if (totalSolved > 0)
			GD.Print($"\nLines processed (solved): min={stats.SolvedMinLines}  max={stats.SolvedMaxLines}  avg={(double)stats.SolvedSumLines / totalSolved:F1}");
		else
			GD.Print("\nLines processed (solved): n/a (none solved)");
		if (unsolvedRun > 0)
			GD.Print($"Lines processed (unsolved): min={stats.UnsolvedMinLines}  max={stats.UnsolvedMaxLines}  avg={(double)stats.UnsolvedSumLines / unsolvedRun:F1}");
		else
			GD.Print("Lines processed (unsolved): n/a (all solved)");

		using FileAccess dataFile = FileAccess.Open(DataFilePath, FileAccess.ModeFlags.ReadWrite);
		if (dataFile != null)
		{
			dataFile.SeekEnd();
			// One row per benchmark batch; timing is the median across runs. Format:
			// Date, #puzzles, #solved, median_time (microseconds), average_solve_time (microseconds),
			// min/max/avg correctly filled, min/max/avg correct, min/max/avg diff
			// min/max/avg lines processed (solved), min/max/avg lines processed (unsolved)
			string line = string.Format(
				CultureInfo.InvariantCulture,
				"{0},{1},{2},{3},{4:F2},{5:F5},{6:F5},{7:F5},{8:F5},{9:F5},{10:F5},{11},{12},{13},{14},{15},{16:F1},{17},{18},{19:F1}",
				DateTime.Now.ToString("s"),
				totalRun,
				totalSolved,
				(long)medianTime,
				medianTime / totalRun,
				!OnlySolved ? stats.MinCorFillPct : -1,
				!OnlySolved ? stats.MaxCorFillPct : -1,
				!OnlySolved ? stats.AvgCorFillPct : -1,
				!OnlySolved ? stats.MinSolPct : -1,
				!OnlySolved ? stats.MaxSolPct : -1,
				!OnlySolved ? stats.AvgSolPct : -1,
				!OnlySolved ? (long)stats.MinDiff : -1,
				!OnlySolved ? (long)stats.MaxDiff : -1,
				!OnlySolved ? (long)stats.AvgDiff : -1,
				stats.SolvedMinLines,
				stats.SolvedMaxLines,
				(double)stats.SolvedSumLines / totalSolved,
				!OnlySolved ? stats.UnsolvedMinLines : -1,
				!OnlySolved ? stats.UnsolvedMaxLines : -1,
				!OnlySolved ? (double)stats.UnsolvedSumLines / unsolvedRun : -1);
			dataFile.StoreLine(line);
		}
		else
		{
			GD.Print("Can't record data");
		}
	}

	/// <summary>
	/// Aggregate stats from one full-solve pass. The correctness/line stats are deterministic (identical
	/// across benchmark passes); only <see cref="ElapsedMicroseconds"/> varies from pass to pass.
	/// </summary>
	private sealed class RunStats
	{
		public double ElapsedMicroseconds;

		public int TotalRun;
		public int TotalSolved;
		public int[] SolvedPuzzles = Array.Empty<int>();

		public double MinCorFillPct, MaxCorFillPct, AvgCorFillPct;
		public string MinCorFillPuzzle = "", MaxCorFillPuzzle = "";

		public double MinSolPct, MaxSolPct, AvgSolPct;
		public string MinSolPuzzle = "", MaxSolPuzzle = "";

		public double MinDiff, MaxDiff, AvgDiff;

		public int SolvedMinLines, SolvedMaxLines;
		public long SolvedSumLines;
		public int UnsolvedMinLines, UnsolvedMaxLines;
		public long UnsolvedSumLines;
	}
}
