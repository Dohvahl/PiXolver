using Godot;
using System;

/// <summary>
/// Nonogram solver. Technique names are taken from the Nonogram wiki:
/// https://en.wikipedia.org/wiki/Nonogram
/// </summary>
/// <remarks>
/// The puzzle model (<c>Puzzle</c>, <c>Clue</c>, <c>CellArray</c>) still lives in GDScript,
/// so this node reaches into those objects through the thin <see cref="PuzzleProxy"/> and
/// <see cref="ClueRef"/> wrappers below. They keep the algorithm itself strongly typed and
/// readable while hiding the dynamic <c>Call</c>/<c>Get</c> interop in one place.
/// </remarks>
public partial class Solver : Node
{
	[Export]
	public int MaxIterations { get; set; } = 10;

	private SolverData _tracker;

	public override void _Ready()
	{
		_tracker = new SolverData();
	}

	public void Reset()
	{
		_tracker?.Reset();
	}

	/// <summary>
	/// Runs the solver until the puzzle is solved or <see cref="MaxIterations"/> is reached.
	/// Returns a dictionary describing the outcome (either <c>is_solved</c>, or fill/solved/incorrect stats).
	/// </summary>
	public Godot.Collections.Dictionary Run(GodotObject puzzleObject, bool debug = false)
	{
		var puzzle = new PuzzleProxy(puzzleObject);

		// run the solver until the puzzle is solved, but to keep it from getting
		// into an infinite loop, we cap the number of iterations
		int iterations = 1;
		while (!puzzle.IsSolved())
		{
			if (!RunSingle(puzzle, iterations, debug))
				break;
			iterations += 1;
			if (iterations - 1 >= MaxIterations)
				break;
		}

		var results = new Godot.Collections.Dictionary { { "iterations", iterations } };

		if (puzzle.IsSolved())
		{
			results["is_solved"] = true;
		}
		else
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
				stats.Calculate(puzzle.RowFilledCells(i), puzzle.SolutionRowFilledCells(i), gridSize);
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
	public bool RunSingle(GodotObject puzzleObject, int iterations, bool debug = false)
	{
		return RunSingle(new PuzzleProxy(puzzleObject), iterations, debug);
	}

	public void RunRows(GodotObject puzzleObject)
	{
		var puzzle = new PuzzleProxy(puzzleObject);
		for (int rowIndex = 0; rowIndex < puzzle.GridSize; rowIndex++)
			Try(puzzle, rowIndex, puzzle.GetRowClues(rowIndex), Vector2I.Down, Vector2I.Right);
	}

	public void RunColumns(GodotObject puzzleObject)
	{
		var puzzle = new PuzzleProxy(puzzleObject);
		for (int columnIndex = 0; columnIndex < puzzle.GridSize; columnIndex++)
			Try(puzzle, columnIndex, puzzle.GetColClues(columnIndex), Vector2I.Right, Vector2I.Down);
	}

	private bool RunSingle(PuzzleProxy puzzle, int iterations, bool debug)
	{
		if (debug)
			GD.Print($"\n*** DEBUG *** Iteration {iterations} *** DEBUG ***");

		// measure how long the preprocessing takes
		ulong preprocessStart = Time.Singleton.GetTicksUsec();

		int gridSize = puzzle.GridSize;
		_tracker.ResizeLargestClues(gridSize);

		// preprocess the puzzle to get some basic information
		for (int i = 0; i < gridSize; i++)
		{
			if (puzzle.GetRowClues(i).Length > 0)
				_tracker.LargestRowClues[i] = puzzle.MaxRowClueValue(i);

			if (puzzle.GetColClues(i).Length > 0)
				_tracker.LargestColumnClues[i] = puzzle.MaxColumnClueValue(i);
		}

		ulong preprocessEnd = Time.Singleton.GetTicksUsec();
		if (debug)
			GD.Print($"PreProcess Time: {preprocessEnd - preprocessStart} microsec");

		// measuring the time to solve the puzzle
		ulong solutionStart = Time.Singleton.GetTicksUsec();

		// check each set of row and column clues
		for (int rowIndex = 0; rowIndex < gridSize; rowIndex++)
			Try(puzzle, rowIndex, puzzle.GetRowClues(rowIndex), Vector2I.Down, Vector2I.Right);

		for (int columnIndex = 0; columnIndex < gridSize; columnIndex++)
			Try(puzzle, columnIndex, puzzle.GetColClues(columnIndex), Vector2I.Right, Vector2I.Down);

		ulong solutionEnd = Time.Singleton.GetTicksUsec();
		if (debug)
			GD.Print($"Solution Time: {solutionEnd - solutionStart} microsec");

		// if this iteration didn't change the state of the puzzle, we're not improving the
		// solution, so there's no point in continuing
		return true;
	}

	#region "Private" solver functions

	/// <summary>Returns true if this solved the row/column.</summary>
	private bool Try(PuzzleProxy puzzle, int index, ClueRef[] clues, Vector2I iterationDirection, Vector2I fillDirection)
	{
		// no clues for this row, or we've already solved it; skip to the next one
		if (clues.Length == 0 || _tracker.IsSolved(iterationDirection, index))
			return true;

		// the current row/column may have been solved by previous iterations,
		// so we should check it before we try to do any work to it
		if (IsLineSolved(puzzle, index, iterationDirection))
		{
			_tracker.MarkSolved(iterationDirection, index);

			// ensure the empty cells are marked
			puzzle.MarkEmptyCells(index, fillDirection);
			return true;
		}

		bool result = TryLineSolve(puzzle, index, clues, iterationDirection, fillDirection);
		if (result)
		{
			// ensure all row clues are marked solved
			foreach (ClueRef clue in clues)
				clue.MarkSolved();
		}

		return result;
	}

	private static bool IsLineSolved(PuzzleProxy puzzle, int index, Vector2I iterDirection)
	{
		if (iterDirection == Vector2I.Down)
			return puzzle.IsRowSolved(index);
		if (iterDirection == Vector2I.Right)
			return puzzle.IsColumnSolved(index);
		return false;
	}

	/// <summary>Returns true if the row/column is solved by this.</summary>
	private bool TryLineSolve(PuzzleProxy puzzle, int index, ClueRef[] clues, Vector2I iterationDirection, Vector2I fillDirection)
	{
		int gridSize = puzzle.GridSize;
		(int startOffset, int endOffset) = GetArrayBounds(puzzle, index, iterationDirection, fillDirection, clues);

		int lineSize = gridSize - startOffset - endOffset;

		Vector2I startCell = (iterationDirection * index) + (fillDirection * startOffset);
		Vector2I endCell = (iterationDirection * index) + (fillDirection * (gridSize - endOffset - 1));

		// Simple Boxes
		long markedCells = puzzle.GetMarkedCells(index, fillDirection, startOffset, lineSize);
		long line = CalculateIntersections(lineSize, clues, markedCells);
		puzzle.FillLine(index, fillDirection, line, startOffset);
		if (puzzle.IsLineSolved(index, fillDirection))
		{
			puzzle.MarkEmptyCells(index, fillDirection);
			_tracker.MarkSolved(iterationDirection, index);
			return true;
		}

		// Checks related to the edges of the row/column
		ClueRef firstClue = clues[0];
		ClueRef lastClue = clues[clues.Length - 1];
		TryGlueing(puzzle, startCell, endCell, firstClue, lastClue, fillDirection);
		TryMercury(puzzle, startCell, endCell, firstClue, lastClue, fillDirection);
		TryForcingSpaces(puzzle, startCell, endCell, firstClue, lastClue, fillDirection);

		// Let's make sure we didn't miss solving any clues
		CheckClues(puzzle, startCell, endCell, fillDirection, clues);

		return IsLineSolved(puzzle, index, iterationDirection);
	}

	/// <summary>
	/// Find the "virtual" start and end of the row/column, taking marked cells into account.
	/// </summary>
	private static (int StartOffset, int EndOffset) GetArrayBounds(PuzzleProxy puzzle, int index, Vector2I iterationDirection, Vector2I fillDirection, ClueRef[] clues)
	{
		int gridSize = puzzle.GridSize;
		int clueIndex = 0;

		// previous iterations may have marked cells at the start or end, these can be skipped
		int startOffset = 0;
		Vector2I startingCell = iterationDirection * index;
		while (puzzle.IsValidCellIndex(startingCell) && !puzzle.IsCellEmpty(startingCell))
		{
			if (puzzle.IsCellMarked(startingCell))
			{
				startOffset += 1;
			}
			else if (puzzle.IsCellFilled(startingCell))
			{
				int clueVal = clues[clueIndex].Value;
				if (clues[clueIndex].IsSolved)
				{
					startOffset += clueVal + 1;
					startingCell += fillDirection * clueVal;
					clueIndex += 1;
				}
				// if the filled cells satisfy the clue, we can punctuate the clue and move on
				else if (puzzle.AreNCellsFilled(startingCell, fillDirection, clueVal))
				{
					clues[clueIndex].MarkSolved();
					startingCell += fillDirection * clueVal;
					// Punctuate the clue then move on
					if (puzzle.IsCellEmpty(startingCell))
						puzzle.MarkCell(startingCell);
					startOffset += clueVal + 1;
					clueIndex += 1;
				}
			}

			startingCell += fillDirection;
		}

		clueIndex = clues.Length - 1;
		int endOffset = 0;
		Vector2I endCell = (iterationDirection * index) + (fillDirection * (gridSize - 1));
		while (puzzle.IsValidCellIndex(endCell) && !puzzle.IsCellEmpty(endCell))
		{
			if (puzzle.IsCellMarked(endCell))
			{
				endOffset += 1;
			}
			else if (puzzle.IsCellFilled(endCell))
			{
				int clueVal = clues[clueIndex].Value;
				if (clues[clueIndex].IsSolved)
				{
					endOffset += clueVal + 1;
					endCell -= fillDirection * clueVal;
					clueIndex -= 1;
				}
				// if the filled cells satisfy the clue, we can punctuate the clue and move on
				else if (puzzle.AreNCellsFilled(endCell - (fillDirection * (clueVal - 1)), fillDirection, clueVal))
				{
					clues[clueIndex].MarkSolved();
					endCell -= fillDirection * clueVal;
					// Punctuate the clue then move on
					if (puzzle.IsCellEmpty(endCell))
						puzzle.MarkCell(endCell);
					endOffset += clueVal + 1;
					clueIndex -= 1;
				}
			}

			endCell -= fillDirection;
		}

		return (startOffset, endOffset);
	}

	/// <summary>
	/// Simple Boxes.
	/// Create two overlapping bitsets using the clues: one starting from the right, one from the left.
	/// Compare the two bitsets for overlapping bits. If the overlapping bits come from the same clue,
	/// then that bit can be filled in.
	/// </summary>
	private static long CalculateIntersections(int size, ClueRef[] clues, long markedCells)
	{
		// Solved clues at the very start and very end should be ignored
		int n = clues.Length;

		bool shrinkStart = true;
		bool shrinkEnd = true;
		int lclue = 0;
		int rclue = n - 1;
		while (lclue < clues.Length && rclue >= 0 && (shrinkStart || shrinkEnd))
		{
			if (shrinkStart)
			{
				if (clues[lclue].IsSolved)
					lclue += 1;
				else
					shrinkStart = false;
			}
			if (shrinkEnd)
			{
				if (clues[rclue].IsSolved)
					rclue -= 1;
				else
					shrinkEnd = false;
			}
		}

		int lpos = 0;
		int[] lstarts = new int[n];

		int rpos = size;
		int[] rstarts = new int[n];

		int lstartsIndex = lclue;
		int rstartsIndex = rclue;
		while (lstartsIndex < n || rstartsIndex >= 0)
		{
			if (((1L << lpos) & markedCells) == (1L << lpos))
			{
				lpos += 1;
			}
			else if (lstartsIndex < n)
			{
				lstarts[lstartsIndex] = lpos;
				lpos += clues[lstartsIndex].Value + 1;
				lstartsIndex += 1;
			}

			if (((1L << (rpos - 1)) & markedCells) == (1L << (rpos - 1)))
			{
				rpos -= 1;
			}
			else if (rstartsIndex >= 0)
			{
				rpos -= clues[rstartsIndex].Value;
				rstarts[rstartsIndex] = rpos;
				rpos -= 1;
				rstartsIndex -= 1;
			}
		}

		long intersect = 0;
		for (int i = lclue; i <= rclue; i++)
		{
			int clueVal = clues[i].Value;
			long leftMask = FieldMask(lstarts[i] + clueVal);
			long rightMask = ~((1L << rstarts[i]) - 1);
			intersect |= leftMask & rightMask;
		}

		return intersect;
	}

	/// <summary>
	/// Glue.
	/// Check for filled squares that are on or near the edges of the line, but not farther away
	/// than the length the first/last clue.
	/// </summary>
	private void TryGlueing(PuzzleProxy puzzle, Vector2I startCell, Vector2I endCell, ClueRef firstClue, ClueRef lastClue, Vector2I fillDirection)
	{
		int gridSize = puzzle.GridSize;

		// Check if the first/last clue isn't yet completed, and there are filled cells within reach.
		// If so, we can fill in the clues
		if (!firstClue.IsSolved)
		{
			// if any cells < first clue are filled,
			// then we can fill from that filled cell up to the clue
			int lowestSetIndex = puzzle.GetFirstFilled(startCell, fillDirection, firstClue.Value);
			if (lowestSetIndex > -1 && lowestSetIndex < gridSize)
			{
				Vector2I firstFilled = startCell + (fillDirection * lowestSetIndex);
				Vector2I zerodStart = startCell * fillDirection;
				bool mark = lowestSetIndex == Math.Min(zerodStart.X, zerodStart.Y);
				FillNCells(puzzle, firstFilled, firstClue.Value - lowestSetIndex, fillDirection, mark);
				if (mark)
					firstClue.ToggleSolved();
			}
		}

		if (!lastClue.IsSolved)
		{
			// if any cells >= length - last clue,
			// then we can fill from that filled cell up to the clue
			int highestSetIndex = puzzle.GetLastFilled(endCell, fillDirection, lastClue.Value);
			if (highestSetIndex > -1 && highestSetIndex < gridSize)
			{
				// the cell that is the highest set
				Vector2I highestSetCell = (startCell * new Vector2I(fillDirection.Y, fillDirection.X)) + (highestSetIndex * fillDirection);

				int fillAmount;
				Vector2I startingPoint;
				bool mark;
				if (puzzle.IsCellMarked(highestSetCell - fillDirection))
				{
					// if the previous cell is marked, we're boxed in and should be able to fill
					// up to the end
					fillAmount = lastClue.Value;
					startingPoint = endCell;
					mark = true;
				}
				else
				{
					// get the cell that would be the end of the clue
					Vector2I clueEnd = endCell - (fillDirection * lastClue.Value) + fillDirection;
					// the number of cells we need to fill
					Vector2I diff = highestSetCell - clueEnd + fillDirection;
					fillAmount = Math.Max(diff.X, diff.Y);
					mark = highestSetCell == endCell;
					startingPoint = highestSetCell;
				}

				FillNCells(puzzle, startingPoint, fillAmount, -fillDirection, mark);
				if (mark)
					lastClue.ToggleSolved();
			}
		}
	}

	private void TryMercury(PuzzleProxy puzzle, Vector2I startCell, Vector2I endCell, ClueRef firstClue, ClueRef lastClue, Vector2I fillDirection)
	{
		// Check if there are filled cells 1 away from the first/last clues amount.
		// For example, if the first clue is 3, and there are 3 empty cells, then a filled cell,
		// we can safely mark the first cell. Similar logic holds for the last cell.
		if (!firstClue.IsSolved)
		{
			int emptyCellsCount = puzzle.GetFirstFilled(startCell, fillDirection, firstClue.Value);
			int lowestSetIndex = puzzle.GetFirstFilled(startCell, fillDirection, firstClue.Value + 1);
			if (lowestSetIndex == firstClue.Value && emptyCellsCount == -1)
				puzzle.MarkCell(startCell);
		}
		if (!lastClue.IsSolved)
		{
			// get the end index for this line
			Vector2I zerodCell = endCell * fillDirection;
			int lastIndex = Math.Max(zerodCell.X, zerodCell.Y);

			// get the first filled cell end_cell-last_clue away
			int lowestFilledIndex = puzzle.GetFirstFilled(endCell - (fillDirection * lastClue.Value), fillDirection, lastClue.Value + 1);
			if (lowestFilledIndex > -1)
			{
				if (lowestFilledIndex == lastIndex - lastClue.Value)
					puzzle.MarkCell(endCell);
			}
		}
	}

	private void TryForcingSpaces(PuzzleProxy puzzle, Vector2I startCell, Vector2I endCell, ClueRef firstClue, ClueRef lastClue, Vector2I fillDirection)
	{
		int gridSize = puzzle.GridSize;

		// if the space between the start/end cells and the next marked cell is too small for the
		// first/last clue then we can mark the in-between cells
		if (!firstClue.IsSolved)
		{
			int lowestMarked = puzzle.GetFirstMarked(startCell, fillDirection, firstClue.Value);
			if (lowestMarked > -1 && lowestMarked < gridSize)
			{
				if (lowestMarked < firstClue.Value)
					MarkNCells(puzzle, startCell, lowestMarked, fillDirection);
			}
		}

		if (!lastClue.IsSolved)
		{
			int highestMarked = puzzle.GetLastMarked(endCell, fillDirection, lastClue.Value);
			if (highestMarked > -1 && highestMarked < gridSize)
			{
				Vector2I zerodEnd = endCell * fillDirection;
				int end = Math.Max(zerodEnd.X, zerodEnd.Y);
				int numSpaces = end - highestMarked;
				if (numSpaces < lastClue.Value)
				{
					Vector2I lastMarked = endCell - (fillDirection * (end - highestMarked));
					MarkNCells(puzzle, lastMarked + fillDirection, numSpaces, fillDirection);
				}
			}
		}
	}

	private static void CheckClues(PuzzleProxy puzzle, Vector2I startCell, Vector2I endCell, Vector2I fillDirection, ClueRef[] clues)
	{
		// Intentionally empty: placeholder for a future "did we miss any clues" pass.
	}

	#region Puzzle Modifiers

	/// <summary>
	/// Returns the next cell after the fill. This might be outside the bounds of the grid if this
	/// fills to the end of the row/column.
	/// </summary>
	private static Vector2I FillNCells(PuzzleProxy puzzle, Vector2I startingCell, int n, Vector2I fillDir, bool markNextCell = false)
	{
		puzzle.FillNCells(startingCell, n, fillDir);

		Vector2I nextCell = startingCell + (fillDir * n);
		if (markNextCell)
			puzzle.MarkCell(nextCell);
		return nextCell;
	}

	/// <summary>
	/// Returns the next cell after the fill. This might be outside the bounds of the grid if this
	/// fills to the end of the row/column.
	/// </summary>
	private static Vector2I MarkNCells(PuzzleProxy puzzle, Vector2I startingCell, int n, Vector2I fillDir)
	{
		puzzle.MarkNCells(startingCell, n, fillDir);
		return startingCell + (fillDir * n);
	}

	#endregion Puzzle Modifiers

	#endregion "Private" solver functions

	/// <summary>Returns n set bits, starting at offset o.</summary>
	private static long FieldMask(int n, int o = 0)
	{
		return ((1L << n) - 1) << o;
	}

	/// <summary>Tracks per-line solve state and the largest clue in each row/column.</summary>
	private sealed class SolverData
	{
		// the largest clues in each row/col
		public int[] LargestRowClues { get; private set; } = Array.Empty<int>();
		public int[] LargestColumnClues { get; private set; } = Array.Empty<int>();

		// "Sets" to track what we've already solved
		private readonly System.Collections.Generic.HashSet<int> _solvedRows = new();
		private readonly System.Collections.Generic.HashSet<int> _solvedColumns = new();

		public void Reset()
		{
			_solvedRows.Clear();
			_solvedColumns.Clear();
			LargestRowClues = Array.Empty<int>();
			LargestColumnClues = Array.Empty<int>();
		}

		public void ResizeLargestClues(int size)
		{
			if (LargestRowClues.Length != size)
				LargestRowClues = new int[size];
			if (LargestColumnClues.Length != size)
				LargestColumnClues = new int[size];
		}

		public int GetLargestClue(Vector2I iterDirection, int index)
		{
			if (iterDirection == Vector2I.Down)
				return LargestRowClues[index];
			if (iterDirection == Vector2I.Right)
				return LargestColumnClues[index];
			return int.MinValue;
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

		public void Calculate(long current, long solution, int width)
		{
			long full = (1L << width) - 1;

			Full = (int)full;
			Correct = PopCount(current & solution);
			IncorrectlyFilled = PopCount(current & ~solution & full);
			Missing = PopCount(~current & solution & full);
			Diff = PopCount(current ^ solution);
			SolutionBits = PopCount(solution);
		}

		private static int PopCount(long x)
		{
			return System.Numerics.BitOperations.PopCount((ulong)x);
		}
	}

	/// <summary>
	/// Strongly-typed view over a single GDScript <c>Clue</c> object.
	/// </summary>
	private readonly struct ClueRef
	{
		private readonly GodotObject _clue;

		public ClueRef(GodotObject clue)
		{
			_clue = clue;
		}

		public int Value => _clue.Get("_value").AsInt32();
		public bool IsSolved => _clue.Call("is_solved").AsBool();
		public void MarkSolved() => _clue.Call("mark_solved");
		public void ToggleSolved() => _clue.Call("toggle_solved");
	}

	/// <summary>
	/// Strongly-typed view over the GDScript <c>Puzzle</c> object. Every member mirrors a method or
	/// property on <c>puzzle.gd</c>; the dynamic interop is confined here.
	/// </summary>
	private sealed class PuzzleProxy
	{
		private readonly GodotObject _puzzle;

		public PuzzleProxy(GodotObject puzzle)
		{
			_puzzle = puzzle;
		}

		public int GridSize => _puzzle.Get("grid_size").AsInt32();

		public bool IsSolved() => _puzzle.Call("is_solved").AsBool();

		public ClueRef[] GetRowClues(int index) => ToClues(_puzzle.Call("get_row_clues", index));
		public ClueRef[] GetColClues(int index) => ToClues(_puzzle.Call("get_col_clues", index));

		public bool IsRowSolved(int index) => _puzzle.Call("is_row_solved", index).AsBool();
		public bool IsColumnSolved(int index) => _puzzle.Call("is_column_solved", index).AsBool();
		public bool IsLineSolved(int index, Vector2I fillDirection) => _puzzle.Call("is_line_solved", index, fillDirection).AsBool();

		public void MarkEmptyCells(int index, Vector2I fillDirection) => _puzzle.Call("mark_empty_cells", index, fillDirection);
		public long GetMarkedCells(int index, Vector2I fillDirection, int offset, int window) => _puzzle.Call("get_marked_cells", index, fillDirection, offset, window).AsInt64();
		public void FillLine(int index, Vector2I fillDirection, long value, int offset) => _puzzle.Call("fill_line", index, fillDirection, value, offset);

		public bool IsValidCellIndex(Vector2I cell) => _puzzle.Call("is_valid_cell_index", cell).AsBool();
		public bool IsCellEmpty(Vector2I cell) => _puzzle.Call("is_cell_empty", cell).AsBool();
		public bool IsCellMarked(Vector2I cell) => _puzzle.Call("is_cell_marked", cell).AsBool();
		public bool IsCellFilled(Vector2I cell) => _puzzle.Call("is_cell_filled", cell).AsBool();
		public bool AreNCellsFilled(Vector2I start, Vector2I fillDirection, int n) => _puzzle.Call("are_n_cells_filled", start, fillDirection, n).AsBool();

		public void MarkCell(Vector2I cell) => _puzzle.Call("mark_cell", cell);
		public void FillNCells(Vector2I start, int n, Vector2I fillDir) => _puzzle.Call("fill_n_cells", start, n, fillDir);
		public void MarkNCells(Vector2I start, int n, Vector2I fillDir) => _puzzle.Call("mark_n_cells", start, n, fillDir);

		public int GetFirstFilled(Vector2I start, Vector2I fillDirection, int count) => _puzzle.Call("get_first_filled", start, fillDirection, count).AsInt32();
		public int GetLastFilled(Vector2I end, Vector2I fillDirection, int count) => _puzzle.Call("get_last_filled", end, fillDirection, count).AsInt32();
		public int GetFirstMarked(Vector2I start, Vector2I fillDirection, int count) => _puzzle.Call("get_first_marked", start, fillDirection, count).AsInt32();
		public int GetLastMarked(Vector2I end, Vector2I fillDirection, int count) => _puzzle.Call("get_last_marked", end, fillDirection, count).AsInt32();

		public int MaxRowClueValue(int index) => SolutionRows[index].Get("max_clue_value").AsInt32();
		public int MaxColumnClueValue(int index) => SolutionColumns[index].Get("max_clue_value").AsInt32();

		public long RowFilledCells(int index) => Rows[index].Get("filled_cells").AsInt64();
		public long SolutionRowFilledCells(int index) => SolutionRows[index].Get("filled_cells").AsInt64();

		private Godot.Collections.Array<GodotObject> Rows => _puzzle.Get("rows").AsGodotArray<GodotObject>();
		private Godot.Collections.Array<GodotObject> SolutionRows => _puzzle.Get("solution_rows").AsGodotArray<GodotObject>();
		private Godot.Collections.Array<GodotObject> SolutionColumns => _puzzle.Get("solution_columns").AsGodotArray<GodotObject>();

		private static ClueRef[] ToClues(Variant value)
		{
			Godot.Collections.Array<GodotObject> array = value.AsGodotArray<GodotObject>();
			var clues = new ClueRef[array.Count];
			for (int i = 0; i < array.Count; i++)
				clues[i] = new ClueRef(array[i]);
			return clues;
		}
	}
}
