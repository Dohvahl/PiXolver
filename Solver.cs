using Godot;
using System;
using System.Diagnostics;

/// <summary>
/// Nonogram solver. Technique names are taken from the Nonogram wiki:
/// https://en.wikipedia.org/wiki/Nonogram
/// </summary>
[GlobalClass]
public partial class Solver : RefCounted
{
	[Export]
	public int MaxIterations { get; set; } = 50;

	private SolverData _tracker;

	public void Init(int inGridSize)
	{
		_tracker = new SolverData();
		_tracker?.Init(inGridSize);
    }

	public void Reset()
	{
		_tracker?.Reset();
	}

	/// <summary>
	/// Runs the solver until the puzzle is solved or <see cref="MaxIterations"/> is reached.
	/// Returns a dictionary describing the outcome (either <c>is_solved</c>, or fill/solved/incorrect stats).
	/// </summary>
	public Godot.Collections.Dictionary Run(Puzzle puzzle, bool debug = false)
	{
		// run the solver until the puzzle is solved, but to keep it from getting
		// into an infinite loop, we cap the number of iterations
		long runStart = Stopwatch.GetTimestamp();
		int iterations = 1;
		while (iterations - 1 < MaxIterations && RunSingle(puzzle, iterations, debug))
		{
			iterations++;
		}
		double elapsedMicroseconds = Stopwatch.GetElapsedTime(runStart).TotalMicroseconds;

		if (debug)
			GD.Print($"Total Solve Time: {elapsedMicroseconds:F1} microsec");

		var results = new Godot.Collections.Dictionary
		{
			{ "iterations", iterations },
			{ "time_us", elapsedMicroseconds },
		};

		if (puzzle.IsSolved())
		{
			results["is_solved"] = true;
		}
		else if (puzzle.HasSolution)
		{
			// get some stats on the state of the puzzle
			int correct = 0;
			int diff = 0;
			int solBits = 0;
			int gridSize = puzzle.GridSize;
			int total = gridSize * gridSize;
			for (int i = 0; i < gridSize; i++)
			{
				var stats = new ResultStats();
				stats.Calculate(puzzle.RowFilledBits(i), puzzle.SolutionRowFilledBits(i), gridSize);
				correct += stats.Correct;
				diff += stats.Diff;
				solBits += stats.SolutionBits;
			}

			results["filled"] = (double)correct / solBits;
			results["solved"] = (double)(total - diff) / total;
			results["incorrect"] = diff;
		}

		return results;
	}

	/// <summary>Returns true if additional iterations are required.</summary>
	public bool RunSingle(Puzzle puzzle, int iterations, bool debug = false)
	{
		if (debug)
			GD.Print($"\n*** DEBUG *** Iteration {iterations} *** DEBUG ***");

		// measure how long the preprocessing takes
		long preprocessStart = Stopwatch.GetTimestamp();

		int gridSize = puzzle.GridSize;

		// snapshot the grid so we can tell whether this iteration makes any progress
		_tracker.SaveState(puzzle);

		if (debug)
			GD.Print($"PreProcess Time: {Stopwatch.GetElapsedTime(preprocessStart).TotalMicroseconds:F1} microsec");

		// measuring the time to solve the puzzle
		long solutionStart = Stopwatch.GetTimestamp();

		// check each set of row and column clues
		RunRows(puzzle);
		RunColumns(puzzle);

		if (debug)
			GD.Print($"Solution Time: {Stopwatch.GetElapsedTime(solutionStart).TotalMicroseconds:F1} microsec");

		// if this iteration didn't change the state of the puzzle, we're not improving the
		// solution, so there's no point in continuing
		return !puzzle.IsSolved() && _tracker.StateChanged(puzzle);
	}

	public void RunRows(Puzzle puzzle)
	{
		for (int rowIndex = 0; rowIndex < puzzle.GridSize; rowIndex++)
			Try(puzzle, rowIndex, puzzle.GetRowClues(rowIndex), Vector2I.Down, Vector2I.Right);
	}

	public void RunColumns(Puzzle puzzle)
	{
		for (int columnIndex = 0; columnIndex < puzzle.GridSize; columnIndex++)
			Try(puzzle, columnIndex, puzzle.GetColClues(columnIndex), Vector2I.Right, Vector2I.Down);
	}

	#region "Private" solver functions

	/// <summary>Returns true if this solved the row/column.</summary>
	private bool Try(Puzzle puzzle, int index, Godot.Collections.Array<Clue> clues, Vector2I iterationDirection, Vector2I fillDirection)
	{
		// no clues for this row, or we've already solved it; skip to the next one
		if (clues.Count == 0 || _tracker.IsSolved(iterationDirection, index))
			return true;

		// the current row/column may have been solved by previous iterations,
		// so we should check it before we try to do any work to it
		if (!_tracker.IsSolved(iterationDirection, index) && IsLineSolved(puzzle, index, iterationDirection))
		{
			_tracker.MarkSolved(iterationDirection, index);

			// mark the clues as solved
			foreach (var clue in clues)
				clue.MarkSolved();

            // ensure the empty cells are marked
            puzzle.MarkEmptyCells(index, fillDirection);
			return true;
		}

		bool result = TryLineSolve(puzzle, index, clues, iterationDirection, fillDirection);
		if (result)
		{
			// ensure all row clues are marked solved
			foreach (Clue clue in clues)
				clue.MarkSolved();
		}

		return result;
	}

	private static bool IsLineSolved(Puzzle puzzle, int index, Vector2I iterDirection)
	{
		if (iterDirection == Vector2I.Down)
			return puzzle.IsRowSolved(index);
		if (iterDirection == Vector2I.Right)
			return puzzle.IsColumnSolved(index);
		return false;
	}

	/// <summary>Returns true if the row/column is solved by this.</summary>
	private bool TryLineSolve(Puzzle puzzle, int index, Godot.Collections.Array<Clue> clues, Vector2I iterationDirection, Vector2I fillDirection)
	{
		int gridSize = puzzle.GridSize;
		uint filledCells = puzzle.GetFilledCells(index, fillDirection, 0, gridSize);
		uint markedCells = puzzle.GetMarkedCells(index, fillDirection, 0, gridSize);

		DPLineSolver lineSolver = _tracker.GetLineSolver(iterationDirection, index);
		lineSolver.Configure(filledCells, markedCells, clues);
		lineSolver.DeduceComplete(out uint forcedFilled, out uint forcedEmpty, out uint solvedClues);
		puzzle.FillLine(index, fillDirection, forcedFilled);
		puzzle.SetEmptyCells(index, fillDirection, forcedEmpty);

		// mark any clue that's now pinned to a single position as solved (UI + future passes)
		for (int i = 0; i < clues.Count; i++)
		{
			if ((solvedClues & (1u << i)) != 0)
				clues[i].MarkSolved();
		}

        if (puzzle.IsLineSolved(index, fillDirection))
		{
			puzzle.MarkEmptyCells(index, fillDirection);
			_tracker.MarkSolved(iterationDirection, index);
			return true;
		}

        return IsLineSolved(puzzle, index, iterationDirection);
	}

	#endregion "Private" solver functions

	/// <summary>Tracks per-line solve state and the largest clue in each row/column.</summary>
	private sealed class SolverData
	{
        // Size of the grid
		private int gridSize;

		// "Sets" to track what we've already solved
		private readonly System.Collections.Generic.HashSet<int> _solvedRows = new();
		private readonly System.Collections.Generic.HashSet<int> _solvedColumns = new();

		// snapshot of the grid (per-row filled/marked bitmasks) taken at the start of RunSingle,
		// used to detect when an iteration made no progress
		private uint[] _previousFilled = Array.Empty<uint>();
		private uint[] _previousMarked = Array.Empty<uint>();

		// one reusable line solver per row and per column, cached so we don't allocate one per call
		private DPLineSolver[] _rowSolvers = Array.Empty<DPLineSolver>();
		private DPLineSolver[] _columnSolvers = Array.Empty<DPLineSolver>();

		public void Init(int size)
		{
			gridSize = size;
			ResizeState(size);
			ResizeLineSolvers(size);
		}

		public void Reset()
		{
			_solvedRows.Clear();
			_solvedColumns.Clear();
			ResizeState(gridSize);
			ResizeLineSolvers(gridSize);
		}

		public void ResizeState(int size)
		{
			if (_previousFilled.Length != size)
				_previousFilled = new uint[size];
			if (_previousMarked.Length != size)
				_previousMarked = new uint[size];
		}

		public void ResizeLineSolvers(int size)
		{
			if (_rowSolvers.Length == size)
				return; // already sized; keep the cached instances

			_rowSolvers = new DPLineSolver[size];
			_columnSolvers = new DPLineSolver[size];
			for (int i = 0; i < size; i++)
			{
				_rowSolvers[i] = new DPLineSolver(size);
				_columnSolvers[i] = new DPLineSolver(size);
			}
		}

		/// <summary>The cached line solver for a given row (Down) or column (Right).</summary>
		public DPLineSolver GetLineSolver(Vector2I iterDirection, int index)
		{
			return iterDirection == Vector2I.Down ? _rowSolvers[index] : _columnSolvers[index];
		}

		/// <summary>Snapshot the puzzle's current grid (per-row filled/marked bitmasks).</summary>
		public void SaveState(Puzzle puzzle)
		{
			for (int i = 0; i < _previousFilled.Length; i++)
			{
				_previousFilled[i] = puzzle.RowFilledBits(i);
				_previousMarked[i] = puzzle.RowMarkedBits(i);
			}
		}

		/// <summary>Returns true if the puzzle's grid differs from the last <see cref="SaveState"/>.</summary>
		public bool StateChanged(Puzzle puzzle)
		{
			for (int i = 0; i < _previousFilled.Length; i++)
			{
				if (_previousFilled[i] != puzzle.RowFilledBits(i)
					|| _previousMarked[i] != puzzle.RowMarkedBits(i))
					return true;
			}
			return false;
		}

		public bool IsSolved(Vector2I iterDirection, int index)
		{
			if (iterDirection == Vector2I.Down)
				return _solvedRows.Contains(index);
			if (iterDirection == Vector2I.Right)
				return _solvedColumns.Contains(index);
			return false;
		}

		public void MarkSolved(Vector2I iterDirection, int index)
		{
			if (iterDirection == Vector2I.Down)
				_solvedRows.Add(index);
			else if (iterDirection == Vector2I.Right)
				_solvedColumns.Add(index);
		}
	}

	/// <summary>Per-line correctness statistics, derived from the current vs. solution bitmasks.</summary>
	private sealed class ResultStats
	{
		public int Full { get; private set; }
		public int Correct { get; private set; }
		public int IncorrectlyFilled { get; private set; }
		public int Missing { get; private set; }
		public int Diff { get; private set; }
		public int SolutionBits { get; private set; }

		public void Calculate(uint current, uint solution, int width)
		{
			uint full = (1u << width) - 1;

			Full = (int)full;
			Correct = BitOps.PopCount(current & solution);
			IncorrectlyFilled = BitOps.PopCount(current & ~solution & full);
			Missing = BitOps.PopCount(~current & solution & full);
			Diff = BitOps.PopCount(current ^ solution);
			SolutionBits = BitOps.PopCount(solution);
		}
	}
}
