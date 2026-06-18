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
	private SolverData _tracker;

	// number of lines processed (Try calls / queue pops) during the current Run; reset at each Run
	private int _linesProcessed;

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
	/// Runs the solver until the puzzle is solved or the worklist reaches its propagation fixpoint.
	/// Returns a dictionary describing the outcome (either <c>is_solved</c>, or fill/solved/incorrect stats).
	/// </summary>
	public Godot.Collections.Dictionary Run(Puzzle puzzle, bool debug = false)
	{
		long runStart = Stopwatch.GetTimestamp();
		_linesProcessed = 0;

		// propagate constraints via the worklist until it drains (fixpoint) or the puzzle is solved
		RunWorklist(puzzle);

		double elapsedMicroseconds = Stopwatch.GetElapsedTime(runStart).TotalMicroseconds;

		if (debug)
			GD.Print($"Total Solve Time: {elapsedMicroseconds:F1} microsec");

		var results = new Godot.Collections.Dictionary
		{
			{ "lines_processed", _linesProcessed },
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
				stats.Calculate(puzzle.RowFilledBits(i), puzzle.SolutionRowFilledBits(i));
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
		int gridSize = puzzle.GridSize;
		for (int rowIndex = 0; rowIndex < gridSize; rowIndex++)
		{
			uint filled = puzzle.GetFilledCells(rowIndex, Vector2I.Right, 0, gridSize);
			uint marked = puzzle.GetMarkedCells(rowIndex, Vector2I.Right, 0, gridSize);
			Try(puzzle, rowIndex, puzzle.GetRowClues(rowIndex), Vector2I.Down, Vector2I.Right, filled, marked);
		}
	}

	public void RunColumns(Puzzle puzzle)
	{
		int gridSize = puzzle.GridSize;
		for (int columnIndex = 0; columnIndex < gridSize; columnIndex++)
		{
			uint filled = puzzle.GetFilledCells(columnIndex, Vector2I.Down, 0, gridSize);
			uint marked = puzzle.GetMarkedCells(columnIndex, Vector2I.Down, 0, gridSize);
			Try(puzzle, columnIndex, puzzle.GetColClues(columnIndex), Vector2I.Right, Vector2I.Down, filled, marked);
		}
	}

	/// <summary>
	/// AC-3-style constraint-propagation worklist. Seeds a FIFO queue with every row and column, then
	/// pops a line, runs the line solver on it, and enqueues the perpendicular line through each cell
	/// it newly fills or marks. Solved lines are never re-enqueued. Cells only ever get added, so the
	/// queue is guaranteed to drain: the run ends at the propagation fixpoint (empty queue) or when the
	/// puzzle is solved. Propagation is confluent, so the order only affects speed, never the result.
	/// </summary>
	private void RunWorklist(Puzzle puzzle)
	{
		int gridSize = puzzle.GridSize;
		// line ids: 0..gridSize-1 are rows, gridSize..2*gridSize-1 are columns
		int lineCount = 2 * gridSize;

		// With per-line dedup a line is queued at most once, so a ring buffer of lineCount slots holds
		// the whole queue; `queued` is the membership flag that enforces the dedup (and bounds size).
		int[] queue = new int[lineCount];
		bool[] queued = new bool[lineCount];
		int head = 0;
		int tail = 0;
		int size = lineCount;

		// seed every row and column
		for (int id = 0; id < lineCount; id++)
		{
			queue[id] = id;
			queued[id] = true;
		}

		while (size > 0)
		{
			int id = queue[head];
			head = head + 1 == lineCount ? 0 : head + 1;
			size -= 1;
			queued[id] = false;

			bool isRow = id < gridSize;
			int index = isRow ? id : id - gridSize;
			Vector2I iterationDirection = isRow ? Vector2I.Down : Vector2I.Right;
			Vector2I fillDirection = isRow ? Vector2I.Right : Vector2I.Down;

			// already solved by an earlier pop; nothing left to propagate from this line
			if (_tracker.IsSolved(iterationDirection, index))
				continue;

			var clues = isRow ? puzzle.GetRowClues(index) : puzzle.GetColClues(index);

			// Hand the pre-solve masks to Try so the line solver doesn't re-derive them, and let Try
			// report exactly the cells it newly determined — so we never re-scan the grid for an "after"
			// snapshot. Those changed cells are the ones whose perpendicular lines now have new info.
			uint beforeFilled = puzzle.GetFilledCells(index, fillDirection, 0, gridSize);
			uint beforeMarked = puzzle.GetMarkedCells(index, fillDirection, 0, gridSize);

			uint changed = Try(puzzle, index, clues, iterationDirection, fillDirection, beforeFilled, beforeMarked);
			if (changed == 0)
				continue;

			// enqueue the perpendicular line through each changed cell: the perpendicular of a row is
			// the column at the changed position, and vice versa
			Vector2I perpIterDir = isRow ? Vector2I.Right : Vector2I.Down;
			uint bits = changed;
			while (bits != 0)
			{
				int p = BitOps.Ctz(bits);
				bits &= bits - 1;

				if (_tracker.IsSolved(perpIterDir, p))
					continue;

				int perpId = isRow ? gridSize + p : p;
				if (!queued[perpId])
				{
					queued[perpId] = true;
					queue[tail] = perpId;
					tail = tail + 1 == lineCount ? 0 : tail + 1;
					size += 1;
				}
			}

			if (_tracker.AllRowsSolved)
				break;
		}
	}

	#region "Private" solver functions

	/// <summary>Returns the cells this newly determined (filled or marked), in line-local coordinates.</summary>
	private uint Try(Puzzle puzzle, int index, Godot.Collections.Array<Clue> clues, Vector2I iterationDirection, Vector2I fillDirection, uint filled, uint marked)
	{
		_linesProcessed++;

		// no clues for this row, or we've already solved it; nothing changes
		if (clues.Count == 0 || _tracker.IsSolved(iterationDirection, index))
			return 0;

		uint occupied = filled | marked;

		// the line may already be complete (e.g. finished off by a perpendicular solve); if so just
		// record it solved instead of running the line solver
		if (puzzle.IsLineSolvedWith(index, fillDirection, filled))
		{
			_tracker.MarkSolved(iterationDirection, index);

			// mark the clues as solved
			foreach (var clue in clues)
				clue.MarkSolved();

			// every previously-empty cell just became marked
			puzzle.MarkEmptyCells(index, fillDirection);
			return BitOps.FieldMask(puzzle.GridSize) & ~occupied;
		}

		return TryLineSolve(puzzle, index, clues, iterationDirection, fillDirection, filled, marked);
	}

	/// <summary>Returns the cells this newly determined (filled or marked), in line-local coordinates.</summary>
	private uint TryLineSolve(Puzzle puzzle, int index, Godot.Collections.Array<Clue> clues, Vector2I iterationDirection, Vector2I fillDirection, uint filledCells, uint markedCells)
	{
		uint occupied = filledCells | markedCells;

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

		// post-fill filled mask, derived without re-scanning the grid
		uint newFilled = filledCells | forcedFilled;
		if (puzzle.IsLineSolvedWith(index, fillDirection, newFilled))
		{
			// fully solved: mark every remaining empty and pin all clues
			puzzle.MarkEmptyCells(index, fillDirection);
			_tracker.MarkSolved(iterationDirection, index);
			foreach (Clue clue in clues)
				clue.MarkSolved();
			return BitOps.FieldMask(puzzle.GridSize) & ~occupied;
		}

		// not solved: the changed cells are the forced ones that weren't already occupied
		return (forcedFilled | forcedEmpty) & ~occupied;
	}

	#endregion "Private" solver functions

	/// <summary>Tracks per-line solve state and the largest clue in each row/column.</summary>
	private sealed class SolverData
	{
        // Size of the grid
		private int gridSize;

		// per-line solved flags, plus a count of solved rows for the O(1) "is the puzzle solved?" check
		// (all rows solved ⇒ every cell is determined ⇒ the puzzle is solved)
		private bool[] _solvedRows = Array.Empty<bool>();
		private bool[] _solvedColumns = Array.Empty<bool>();
		private int _solvedRowCount;

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
			ResizeState(gridSize);
			ResizeLineSolvers(gridSize);
			Array.Clear(_solvedRows, 0, _solvedRows.Length);
			Array.Clear(_solvedColumns, 0, _solvedColumns.Length);
			_solvedRowCount = 0;
		}

		public void ResizeState(int size)
		{
			if (_previousFilled.Length != size)
				_previousFilled = new uint[size];
			if (_previousMarked.Length != size)
				_previousMarked = new uint[size];
			if (_solvedRows.Length != size)
				_solvedRows = new bool[size];
			if (_solvedColumns.Length != size)
				_solvedColumns = new bool[size];
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

		/// <summary>True once every row is solved, which means the whole puzzle is solved.</summary>
		public bool AllRowsSolved => _solvedRowCount == gridSize;

		public bool IsSolved(Vector2I iterDirection, int index)
		{
			if (iterDirection == Vector2I.Down)
				return _solvedRows[index];
			if (iterDirection == Vector2I.Right)
				return _solvedColumns[index];
			return false;
		}

		public void MarkSolved(Vector2I iterDirection, int index)
		{
			if (iterDirection == Vector2I.Down)
			{
				if (!_solvedRows[index])
				{
					_solvedRows[index] = true;
					_solvedRowCount++;
				}
			}
			else if (iterDirection == Vector2I.Right)
			{
				_solvedColumns[index] = true;
			}
		}
	}

	/// <summary>Per-line correctness statistics, derived from the current vs. solution bitmasks.</summary>
	private sealed class ResultStats
	{
		public int Correct { get; private set; }
		public int Diff { get; private set; }
		public int SolutionBits { get; private set; }

		public void Calculate(uint current, uint solution)
		{
			Correct = BitOps.PopCount(current & solution);
			Diff = BitOps.PopCount(current ^ solution);
			SolutionBits = BitOps.PopCount(solution);
		}
	}
}
