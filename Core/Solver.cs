using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace PiXolver.Core;

/// <summary>
/// Nonogram solver. Technique names are taken from the Nonogram wiki:
/// https://en.wikipedia.org/wiki/Nonogram
/// </summary>
public partial class Solver
{
	/// <summary>
	/// Maximum guess depth for the backtracking search. Iterative deepening tries limits 1..this and
	/// stops at the first depth that solves the puzzle. Larger values solve more puzzles, but cost grows
	/// roughly exponentially with depth; 0 disables search entirely (line propagation only).
	/// </summary>
	public int MaxSearchDepth { get; set; } = 10;

	private SolverData _tracker;

	// number of lines processed (Try calls / queue pops) during the current Run; reset at each Run
	private int _linesProcessed;

	// set when a line solve finds the current cells admit no valid clue arrangement (a contradiction);
	// RunWorklist checks it to abort propagation. Reset at the start of each RunWorklist call.
	private bool _contradiction;

	// reusable checkpoints for the backtracking search, indexed by recursion depth
	private readonly List<Checkpoint> _checkpoints = new();

	// reusable worklist ring buffer (+ dedup flags), shared by every propagation run
	private int[] _queue = Array.Empty<int>();
	private bool[] _queued = Array.Empty<bool>();

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
	/// Returns a <see cref="SolveResult"/> describing the outcome.
	/// </summary>
	public SolveResult Run(Puzzle puzzle)
	{
		long runStart = Stopwatch.GetTimestamp();
		_linesProcessed = 0;
		_checkpoints.Clear();

		// Line-propagate to a fixpoint; if that doesn't fully decide the grid, fall back to iterative-
		// deepening search to complete it. `ok` is false only if the puzzle is already contradictory, or
		// the search gave up at MaxSearchDepth without finding a solution.
		bool ok = RunWorklist(puzzle);
		int depthReached = 0;
		if (ok && !puzzle.IsFullyDecided())
			ok = RunSearch(puzzle, out depthReached);

		double elapsedMicroseconds = Stopwatch.GetElapsedTime(runStart).TotalMicroseconds;

		var result = new SolveResult
		{
			LinesProcessed = _linesProcessed,
			TimeMicroseconds = elapsedMicroseconds,
			DepthReached = depthReached,
		};

		if (ok && puzzle.IsFullyDecided())
		{
			result.IsSolved = true;
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

			result.HasStats = true;
			result.FilledFraction = (double)correct / solBits;
			result.SolvedFraction = (double)(total - diff) / total;
			result.IncorrectCells = diff;
		}

		return result;
	}

	/// <summary>Runs one full row+column sweep. Returns true if more iterations are required.</summary>
	public bool RunSingle(Puzzle puzzle)
	{
		// snapshot the grid so we can tell whether this iteration makes any progress
		_tracker.SaveState(puzzle);

		// check each set of row and column clues
		RunRows(puzzle);
		RunColumns(puzzle);

		// if this iteration didn't change the state of the puzzle, we're not improving the
		// solution, so there's no point in continuing
		return !puzzle.IsSolved() && _tracker.StateChanged(puzzle);
	}

	public void RunRows(Puzzle puzzle)
	{
		int gridSize = puzzle.GridSize;
		for (int rowIndex = 0; rowIndex < gridSize; rowIndex++)
		{
			uint filled = puzzle.GetFilledCells(rowIndex, Vec2I.Right, 0, gridSize);
			uint marked = puzzle.GetMarkedCells(rowIndex, Vec2I.Right, 0, gridSize);
			Try(puzzle, rowIndex, puzzle.GetRowClues(rowIndex), Vec2I.Down, Vec2I.Right, filled, marked);
		}
	}

	public void RunColumns(Puzzle puzzle)
	{
		int gridSize = puzzle.GridSize;
		for (int columnIndex = 0; columnIndex < gridSize; columnIndex++)
		{
			uint filled = puzzle.GetFilledCells(columnIndex, Vec2I.Down, 0, gridSize);
			uint marked = puzzle.GetMarkedCells(columnIndex, Vec2I.Down, 0, gridSize);
			Try(puzzle, columnIndex, puzzle.GetColClues(columnIndex), Vec2I.Right, Vec2I.Down, filled, marked);
		}
	}

	/// <summary>
	/// AC-3-style constraint-propagation worklist seeded with every row and column. Pops a line, runs the
	/// line solver, and enqueues the perpendicular line through each newly determined cell. Returns false
	/// on a contradiction, true at the propagation fixpoint (or once the puzzle is solved). Propagation is
	/// confluent, so order only affects speed, never the result.
	/// </summary>
	private bool RunWorklist(Puzzle puzzle)
	{
		int gridSize = puzzle.GridSize;
		// line ids: 0..gridSize-1 are rows, gridSize..2*gridSize-1 are columns
		int lineCount = 2 * gridSize;
		PrepareQueue(lineCount);

		// seed every row and column
		for (int id = 0; id < lineCount; id++)
		{
			_queue[id] = id;
			_queued[id] = true;
		}

		// head=0, tail=0, size=lineCount is the "full ring" state (tail == (head + size) mod lineCount)
		return DrainQueue(puzzle, gridSize, lineCount, head: 0, tail: 0, size: lineCount);
	}

	/// <summary>
	/// Worklist seeded with just the row and column through cell (x, y) — used after the search changes a
	/// single cell. The rest of the grid is already at a fixpoint, so only those two lines can yield new
	/// deductions and propagation cascades from them. Returns false on a contradiction.
	/// </summary>
	private bool RunWorklistFrom(Puzzle puzzle, int x, int y)
	{
		int gridSize = puzzle.GridSize;
		int lineCount = 2 * gridSize;
		PrepareQueue(lineCount);

		int rowId = y;            // row through (x, y)
		int colId = gridSize + x; // column through (x, y)
		_queue[0] = rowId;
		_queue[1] = colId;
		_queued[rowId] = true;
		_queued[colId] = true;

		return DrainQueue(puzzle, gridSize, lineCount, head: 0, tail: 2, size: 2);
	}

	/// <summary>
	/// Drains the seeded worklist to a fixpoint: pop a line, run the line solver, and enqueue the
	/// perpendicular line through each newly determined cell. Returns false on a contradiction.
	/// </summary>
	private bool DrainQueue(Puzzle puzzle, int gridSize, int lineCount, int head, int tail, int size)
	{
		_contradiction = false;

		while (size > 0)
		{
			int id = _queue[head];
			head = head + 1 == lineCount ? 0 : head + 1;
			size -= 1;
			_queued[id] = false;

			bool isRow = id < gridSize;
			int index = isRow ? id : id - gridSize;
			Vec2I iterationDirection = isRow ? Vec2I.Down : Vec2I.Right;
			Vec2I fillDirection = isRow ? Vec2I.Right : Vec2I.Down;

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
			if (_contradiction)
				return false;
			if (changed == 0)
				continue;

			// enqueue the perpendicular line through each changed cell: the perpendicular of a row is
			// the column at the changed position, and vice versa
			Vec2I perpIterDir = isRow ? Vec2I.Right : Vec2I.Down;
			uint bits = changed;
			while (bits != 0)
			{
				int p = BitOps.Ctz(bits);
				bits &= bits - 1;

				if (_tracker.IsSolved(perpIterDir, p))
					continue;

				int perpId = isRow ? gridSize + p : p;
				if (!_queued[perpId])
				{
					_queued[perpId] = true;
					_queue[tail] = perpId;
					tail = tail + 1 == lineCount ? 0 : tail + 1;
					size += 1;
				}
			}

			if (_tracker.AllRowsSolved)
				break;
		}

		return true;
	}

	/// <summary>Ensures the worklist buffers fit lineCount slots and clears the dedup flags for this run.</summary>
	private void PrepareQueue(int lineCount)
	{
		if (_queue.Length < lineCount)
		{
			_queue = new int[lineCount];
			_queued = new bool[lineCount];
		}
		else
		{
			Array.Clear(_queued, 0, lineCount);
		}
	}

	/// <summary>
	/// Iterative-deepening driver: tries depth-limited searches at increasing guess limits (1, 2, …,
	/// <see cref="MaxSearchDepth"/>) and stops at the first that solves the puzzle. Reports the limit
	/// that succeeded via <paramref name="depthReached"/>, or <see cref="MaxSearchDepth"/> if it gave up.
	/// </summary>
	private bool RunSearch(Puzzle puzzle, out int depthReached)
	{
		for (int limit = 1; limit <= MaxSearchDepth; limit++)
		{
			if (Search(puzzle, 0, limit))
			{
				depthReached = limit;
				return true;
			}
		}

		depthReached = MaxSearchDepth;
		return false;
	}

	/// <summary>
	/// Depth-limited backtracking search: assume a value for an undecided cell, propagate, and recurse;
	/// on contradiction or dead end, restore and try the other value. Stops descending once
	/// <paramref name="depth"/> reaches <paramref name="limit"/>. Returns true once the grid is completed
	/// into a valid solution. Uses the line worklist as the per-node propagator.
	/// </summary>
	private bool Search(Puzzle puzzle, int depth, int limit)
	{
		if (!puzzle.TryFindBranchCell(out int x, out int y))
			return true; // fully decided after a contradiction-free propagation => a valid solution
		if (depth >= limit)
			return false; // hit the depth cap for this iteration; can't guess any deeper

		Checkpoint cp = GetCheckpoint(puzzle, depth);
		puzzle.CaptureInto(cp.Puzzle);
		_tracker.CaptureInto(cp.Tracker);

		// branch 1: assume the cell is filled. Only the row/column through (x, y) can yield new
		// deductions (the rest of the grid is already at a fixpoint), so seed propagation from there.
		puzzle.SetCellFilled(x, y);
		if (RunWorklistFrom(puzzle, x, y) && Search(puzzle, depth + 1, limit))
			return true;
		puzzle.RestoreFrom(cp.Puzzle);
		_tracker.RestoreFrom(cp.Tracker);

		// branch 2: assume the cell is empty
		puzzle.MarkCellEmpty(x, y);
		if (RunWorklistFrom(puzzle, x, y) && Search(puzzle, depth + 1, limit))
			return true;
		puzzle.RestoreFrom(cp.Puzzle);
		_tracker.RestoreFrom(cp.Tracker);

		return false; // neither value works => this branch is a dead end
	}

	/// <summary>A reusable checkpoint (one per search depth) of the grid and solved-line state.</summary>
	private Checkpoint GetCheckpoint(Puzzle puzzle, int depth)
	{
		while (_checkpoints.Count <= depth)
			_checkpoints.Add(new Checkpoint
			{
				Puzzle = puzzle.CreateSnapshot(),
				Tracker = _tracker.CreateSnapshot(),
			});
		return _checkpoints[depth];
	}

	private sealed class Checkpoint
	{
		public Puzzle.Snapshot Puzzle;
		public SolverData.TrackerSnapshot Tracker;
	}

	#region "Private" solver functions

	/// <summary>Returns the cells this newly determined (filled or marked), in line-local coordinates.</summary>
	private uint Try(Puzzle puzzle, int index, IReadOnlyList<Clue> clues, Vec2I iterationDirection, Vec2I fillDirection, uint filled, uint marked)
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
	private uint TryLineSolve(Puzzle puzzle, int index, IReadOnlyList<Clue> clues, Vec2I iterationDirection, Vec2I fillDirection, uint filledCells, uint markedCells)
	{
		uint occupied = filledCells | markedCells;

		DPLineSolver lineSolver = _tracker.GetLineSolver(iterationDirection, index);
		lineSolver.Configure(filledCells, markedCells, clues);
		if (!lineSolver.DeduceComplete(out uint forcedFilled, out uint forcedEmpty, out uint solvedClues))
		{
			// the line admits no valid arrangement given the current cells — a contradiction
			_contradiction = true;
			return 0;
		}
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
		public DPLineSolver GetLineSolver(Vec2I iterDirection, int index)
		{
			return iterDirection == Vec2I.Down ? _rowSolvers[index] : _columnSolvers[index];
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

		public bool IsSolved(Vec2I iterDirection, int index)
		{
			if (iterDirection == Vec2I.Down)
				return _solvedRows[index];
			if (iterDirection == Vec2I.Right)
				return _solvedColumns[index];
			return false;
		}

		public void MarkSolved(Vec2I iterDirection, int index)
		{
			if (iterDirection == Vec2I.Down)
			{
				if (!_solvedRows[index])
				{
					_solvedRows[index] = true;
					_solvedRowCount++;
				}
			}
			else if (iterDirection == Vec2I.Right)
			{
				_solvedColumns[index] = true;
			}
		}

		// --- Checkpoint/restore of the solved-line state for the backtracking search ---

		public sealed class TrackerSnapshot
		{
			public bool[] Rows;
			public bool[] Columns;
			public int RowCount;
		}

		public TrackerSnapshot CreateSnapshot() => new()
		{
			Rows = new bool[gridSize],
			Columns = new bool[gridSize],
		};

		public void CaptureInto(TrackerSnapshot s)
		{
			Array.Copy(_solvedRows, s.Rows, gridSize);
			Array.Copy(_solvedColumns, s.Columns, gridSize);
			s.RowCount = _solvedRowCount;
		}

		public void RestoreFrom(TrackerSnapshot s)
		{
			Array.Copy(s.Rows, _solvedRows, gridSize);
			Array.Copy(s.Columns, _solvedColumns, gridSize);
			_solvedRowCount = s.RowCount;
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
