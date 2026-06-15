using Godot;
using System;
using System.Linq;

/// <summary>
/// Nonogram solver. Technique names are taken from the Nonogram wiki:
/// https://en.wikipedia.org/wiki/Nonogram
/// </summary>
[GlobalClass]
public partial class Solver : RefCounted
{
	[Export]
	public int MaxIterations { get; set; } = 20;

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
		ulong preprocessStart = Time.Singleton.GetTicksUsec();

		int gridSize = puzzle.GridSize;

		// snapshot the grid so we can tell whether this iteration makes any progress
		_tracker.SaveState(puzzle);

		// preprocess the puzzle to get some basic information
		for (int i = 0; i < gridSize; i++)
		{
			if (puzzle.GetRowClues(i).Count > 0)
				_tracker.LargestRowClues[i] = puzzle.RowMaxClueValue(i);

			if (puzzle.GetColClues(i).Count > 0)
				_tracker.LargestColumnClues[i] = puzzle.ColumnMaxClueValue(i);
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
		(int startOffset, int endOffset) = GetLineBounds(puzzle, index, iterationDirection, fillDirection, clues);

		int lineSize = gridSize - startOffset - endOffset;

		Vector2I startCell = (iterationDirection * index) + (fillDirection * startOffset);
		Vector2I endCell = (iterationDirection * index) + (fillDirection * (gridSize - endOffset - 1));

		// Simple Boxes
		uint filledCells = puzzle.GetFilledCells(index, fillDirection, startOffset, lineSize);
		uint markedCells = puzzle.GetMarkedCells(index, fillDirection, startOffset, lineSize);

		int[] clueValues = clues.Select(c => c.Value).ToArray();
		var lineSolver = new LineSolver(puzzle.GridSize, filledCells, markedCells, clueValues);
		lineSolver.Deduce(out uint forcedFilled, out uint forcedEmpty);
		puzzle.FillLine(index, fillDirection, forcedFilled, startOffset);
		puzzle.SetEmptyCells(index, fillDirection, forcedEmpty, startOffset);

        //uint line = CalculateIntersections(lineSize, clues, markedCells);
        //puzzle.FillLine(index, fillDirection, line, startOffset);
        if (puzzle.IsLineSolved(index, fillDirection))
		{
			puzzle.MarkEmptyCells(index, fillDirection);
			_tracker.MarkSolved(iterationDirection, index);
			return true;
		}

		// Checks related to the edges of the row/column
		(int lo, int hi) = _tracker.GetUnsolvedClueBounds(iterationDirection, index);
		if (lo > hi)
		{
			lo = 0; 
			hi = clues.Count - 1;
		}
        Clue firstClue = clues[Math.Max(lo, 0)];
		Clue lastClue = clues[Math.Min(hi, clues.Count - 1)];
		TryGlueing(puzzle, startCell, endCell, firstClue, lastClue, fillDirection);
		TryMercury(puzzle, startCell, endCell, firstClue, lastClue, fillDirection);
		TryForcingSpaces(puzzle, startCell, endCell, firstClue, lastClue, fillDirection);
		TryJoiningSplitting(puzzle, startCell, endCell, firstClue, lastClue, fillDirection);

        return IsLineSolved(puzzle, index, iterationDirection);
	}

    /// <summary>
    /// Find the "virtual" start and end of the row/column, taking marked cells into account.
    /// </summary>
    private (int StartOffset, int EndOffset) GetLineBounds(Puzzle puzzle, int index, Vector2I iterationDirection, Vector2I fillDirection, Godot.Collections.Array<Clue> clues)
	{
		int gridSize = puzzle.GridSize;
		int lowClueIndex = 0;
		int highClueIndex = clues.Count - 1;

        // previous iterations may have marked cells at the start or end, these can be skipped
        int startOffset = 0;
		Vector2I startingCell = iterationDirection * index;
		while (puzzle.IsValidCellIndex(startingCell) && !puzzle.IsCellEmpty(startingCell))
		{
			if (puzzle.IsCellMarked(startingCell))
			{
				startOffset += 1;
			}
			else if (lowClueIndex < clues.Count && puzzle.IsCellFilled(startingCell))
			{
				int clueVal = clues[lowClueIndex].Value;
				if (clues[lowClueIndex].IsSolved())
				{
					startOffset += clueVal + 1;
					startingCell += fillDirection * clueVal;
                    lowClueIndex += 1;
				}
				// if the filled cells satisfy the clue, we can punctuate the clue and move on
				else if (puzzle.AreNCellsFilled(startingCell, fillDirection, clueVal))
				{
					clues[lowClueIndex].MarkSolved();
					startingCell += fillDirection * clueVal;
					// Punctuate the clue then move on
					if (puzzle.IsCellEmpty(startingCell))
						puzzle.MarkCell(startingCell);
					startOffset += clueVal + 1;
                    lowClueIndex += 1;
				}
			}

			startingCell += fillDirection;
		}

		highClueIndex = clues.Count - 1;
		int endOffset = 0;
		Vector2I endCell = (iterationDirection * index) + (fillDirection * (gridSize - 1));
		while (puzzle.IsValidCellIndex(endCell) && !puzzle.IsCellEmpty(endCell))
		{
			if (puzzle.IsCellMarked(endCell))
			{
				endOffset += 1;
			}
			else if (highClueIndex >= 0 && puzzle.IsCellFilled(endCell))
			{
				int clueVal = clues[highClueIndex].Value;
				if (clues[highClueIndex].IsSolved())
				{
					endOffset += clueVal + 1;
					endCell -= fillDirection * clueVal;
                    highClueIndex -= 1;
				}
				// if the filled cells satisfy the clue, we can punctuate the clue and move on
				else if (puzzle.AreNCellsFilled(endCell - (fillDirection * (clueVal - 1)), fillDirection, clueVal))
				{
					clues[highClueIndex].MarkSolved();
					endCell -= fillDirection * clueVal;
					// Punctuate the clue then move on
					if (puzzle.IsCellEmpty(endCell))
						puzzle.MarkCell(endCell);
					endOffset += clueVal + 1;
                    highClueIndex -= 1;
				}
			}

			endCell -= fillDirection;
		}

		_tracker.UpdateUnsolvedClues(index, iterationDirection, lowClueIndex, highClueIndex);

		return (startOffset, endOffset);
	}

	/// <summary>
	/// Simple Boxes.
	/// Create two overlapping bitsets using the clues: one starting from the right, one from the left.
	/// Compare the two bitsets for overlapping bits. If the overlapping bits come from the same clue,
	/// then that bit can be filled in.
	/// </summary>
	private static uint CalculateIntersections(int size, Godot.Collections.Array<Clue> clues, uint markedCells)
	{
		// Solved clues at the very start and very end should be ignored
		int n = clues.Count;

		bool shrinkStart = true;
		bool shrinkEnd = true;
		int lclue = 0;
		int rclue = n - 1;
		while (lclue < clues.Count && rclue >= 0 && (shrinkStart || shrinkEnd))
		{
			if (shrinkStart)
			{
				if (clues[lclue].IsSolved())
					lclue += 1;
				else
					shrinkStart = false;
			}
			if (shrinkEnd)
			{
				if (clues[rclue].IsSolved())
					rclue -= 1;
				else
					shrinkEnd = false;
			}
		}

		int lpos = 0;
		int[] lstarts = new int[n];
		Array.Fill(lstarts, -1);

		int rpos = size;
		int[] rstarts = new int[n];
		Array.Fill(rstarts, -1);

		// Calculate the minimum starting cell of each clue, from each side
		int lstartsIndex = lclue;
		int rstartsIndex = rclue;
		while (lstartsIndex < n || rstartsIndex >= 0)
		{
			// if the current cell is marked skip to the next cell
			if (((1u << lpos) & markedCells) == (1u << lpos))
			{
				lpos += 1;
			}
			else if (lstartsIndex < n)
			{
				int clueValue = clues[lstartsIndex].Value;
				int firstMarked = BitOps.FirstSet(markedCells, lpos, clueValue + 1);
                // if there is not enough space to fit the clue, skip to the cell beyond 
                // where the clue would end if it started at lpos
                if (firstMarked > -1 && firstMarked < lpos + clueValue)
				{
					lpos = firstMarked + 1;
				}
				else
				{
					lstarts[lstartsIndex] = lpos;
					lpos += clueValue + 1;
					lstartsIndex += 1;
				}
			}

            // if the current cell is marked skip to the next cell
            if (((1u << (rpos - 1)) & markedCells) == (1u << (rpos - 1)))
			{
				rpos -= 1;
			}
			else if (rstartsIndex >= 0)
			{
                int clueValue = clues[rstartsIndex].Value;
				int lastMarked = BitOps.LastSet(markedCells, rpos - clueValue, clueValue + 1);
                // if there is not enough space to fit the clue, skip to the cell beyond 
                // where the clue would end if it started at lpos
                if (lastMarked > -1 && lastMarked + clueValue < rpos)
				{
					rpos = lastMarked - 1;
				}
				else
				{
					rpos -= clueValue;
					rstarts[rstartsIndex] = rpos;
					rpos -= 1;
					rstartsIndex -= 1;
				}
			}
		}

		uint intersect = 0;
		for (int i = lclue; i <= rclue; i++)
		{
			if (clues[i].IsSolved() || lstarts[i] == -1 || rstarts[i] == -1)
                continue;	// just a sanity check, this should never happen

            int clueVal = clues[i].Value;
			uint leftMask = BitOps.FieldMask(lstarts[i] + clueVal);
			uint rightMask = ~((1u << rstarts[i]) - 1u);
			intersect |= leftMask & rightMask;
		}

		return intersect;
	}

	/// <summary>
	/// Glue.
	/// Check for filled squares that are on or near the edges of the line, but not farther away
	/// than the length the first/last clue.
	/// </summary>
	private void TryGlueing(Puzzle puzzle, Vector2I startCell, Vector2I endCell, Clue firstClue, Clue lastClue, Vector2I fillDirection)
	{
		int gridSize = puzzle.GridSize;

		// Check if the first/last clue isn't yet completed, and there are filled cells within reach.
		// If so, we can fill in the clues
		if (!firstClue.IsSolved())
		{
			// if any cells < first clue are filled,
			// then we can fill from that filled cell up to the clue
			int lowestSetIndex = puzzle.GetFirstFilled(startCell, fillDirection, firstClue.Value);
			if (lowestSetIndex > -1 && lowestSetIndex < gridSize)
			{
		        // the cell that is the lowest set
		        Vector2I firstFilled = (startCell * new Vector2I(fillDirection.Y, fillDirection.X)) + (fillDirection * lowestSetIndex);
		
				int fillAmount = 0;
				Vector2I startingPoint;
				bool mark;
				if (puzzle.IsValidCellIndex(firstFilled + fillDirection) && puzzle.IsCellMarked(firstFilled + fillDirection))
				{
					// if the next cell is marked, we're boxed in and should be able to fill
					// from the start up to the marked cell
					fillAmount = firstClue.Value;
					startingPoint = startCell;
					mark = true;
				}
				else
				{
					Vector2I zerodStart = startCell * fillDirection;
					fillAmount = firstClue.Value - lowestSetIndex;
					startingPoint = firstFilled;
		                  mark = lowestSetIndex == Math.Min(zerodStart.X, zerodStart.Y);
				}
				FillNCells(puzzle, startingPoint, fillAmount, fillDirection, mark);
				if (mark)
					firstClue.ToggleSolved();
		
			}
		}

		if (!lastClue.IsSolved())
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
				if (puzzle.IsValidCellIndex(highestSetCell - fillDirection) && puzzle.IsCellMarked(highestSetCell - fillDirection))
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

	private void TryMercury(Puzzle puzzle, Vector2I startCell, Vector2I endCell, Clue firstClue, Clue lastClue, Vector2I fillDirection)
	{
		// Check if there are filled cells 1 away from the first/last clues amount.
		// For example, if the first clue is 3, and there are 3 empty cells, then a filled cell,
		// we can safely mark the first cell. Similar logic holds for the last cell.
		if (!firstClue.IsSolved())
		{
			// get the start index for the line
			Vector2I zerodCell = startCell * fillDirection;
			int firstIndex = Math.Max(zerodCell.X, zerodCell.Y);

			int highestFilledIndex = puzzle.GetLastFilled(startCell + (fillDirection * firstClue.Value), fillDirection, firstClue.Value + 1);
			if (highestFilledIndex > -1)
			{
				if (highestFilledIndex == firstClue.Value + firstIndex)
					puzzle.MarkCell(startCell);
			}		
		}
		if (!lastClue.IsSolved())
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

	private void TryForcingSpaces(Puzzle puzzle, Vector2I startCell, Vector2I endCell, Clue firstClue, Clue lastClue, Vector2I fillDirection)
	{
		int gridSize = puzzle.GridSize;

		// if the space between the start/end cells and the next marked cell is too small for the
		// first/last clue then we can mark the in-between cells
		if (!firstClue.IsSolved())
		{
			int lowestMarked = puzzle.GetFirstMarked(startCell, fillDirection, firstClue.Value);
			if (lowestMarked > -1 && lowestMarked < gridSize)
			{
				Vector2I zerodStart = startCell * fillDirection;
				int start = Math.Max(zerodStart.X, zerodStart.Y);
				int numSpaces = lowestMarked - start;
				if (numSpaces > 0 && numSpaces < firstClue.Value)
					MarkNCells(puzzle, startCell, numSpaces, fillDirection, endCell);
			}
		}

		if (!lastClue.IsSolved())
		{
			int highestMarked = puzzle.GetLastMarked(endCell, fillDirection, lastClue.Value);
			if (highestMarked > -1 && highestMarked < gridSize)
			{
				Vector2I zerodEnd = endCell * fillDirection;
				int end = Math.Max(zerodEnd.X, zerodEnd.Y);
				int numSpaces = end - highestMarked;
				if (numSpaces > 0 && numSpaces < lastClue.Value)
				{
					Vector2I lastMarked = endCell - (fillDirection * (end - highestMarked));
					MarkNCells(puzzle, lastMarked + fillDirection, numSpaces, fillDirection, endCell);
				}
			}
		}
	}

    private void TryJoiningSplitting(Puzzle puzzle, Vector2I startCell, Vector2I endCell, Clue firstClue, Clue lastClue, Vector2I fillDirection)
    {
        
    }

    private static void CheckClues(Puzzle puzzle, Vector2I startCell, Vector2I endCell, Vector2I fillDirection, Godot.Collections.Array<Clue> clues)
	{
		// Intentionally empty: placeholder for a future "did we miss any clues" pass.
	}

	#region Puzzle Modifiers

	/// <summary>
	/// Returns the next cell after the fill. This might be outside the bounds of the grid if this
	/// fills to the end of the row/column.
	/// </summary>
	private static Vector2I FillNCells(Puzzle puzzle, Vector2I startingCell, int n, Vector2I fillDir, bool markNextCell = false)
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
	private static Vector2I MarkNCells(Puzzle puzzle, Vector2I startingCell, int n, Vector2I fillDir, Vector2I endCell)
	{
		puzzle.MarkNCells(startingCell, n, fillDir, endCell);
		return startingCell + (fillDir * n);
	}

	#endregion Puzzle Modifiers

	#endregion "Private" solver functions

	/// <summary>Tracks per-line solve state and the largest clue in each row/column.</summary>
	private sealed class SolverData
	{
        // Size of the grid
		private int gridSize;

        // the largest clues in each row/col
        public int[] LargestRowClues { get; private set; } = Array.Empty<int>();
		public int[] LargestColumnClues { get; private set; } = Array.Empty<int>();

		// "Sets" to track what we've already solved
		private readonly System.Collections.Generic.HashSet<int> _solvedRows = new();
		private readonly System.Collections.Generic.HashSet<int> _solvedColumns = new();

		// indices of the first/last unsolved clues for each row and column
		public (int, int)[] UnsolvedRowClues { get; private set; } = Array.Empty<(int, int)>();
		public (int, int)[] UnsolvedColumnClues { get; private set; } = Array.Empty<(int, int)>();

		// snapshot of the grid (per-row filled/marked bitmasks) taken at the start of RunSingle,
		// used to detect when an iteration made no progress
		private uint[] _previousFilled = Array.Empty<uint>();
		private uint[] _previousMarked = Array.Empty<uint>();

		public void Init(int size)
		{
			gridSize = size;
			ResizeLargestClues(size);
			ResizeUnsolvedClues(size);
			ResizeState(size);
		}

		public void Reset()
		{
			_solvedRows.Clear();
			_solvedColumns.Clear();
			LargestRowClues = Array.Empty<int>();
			LargestColumnClues = Array.Empty<int>();
			UnsolvedRowClues = Array.Empty<(int, int)>();
			UnsolvedColumnClues = Array.Empty<(int, int)>();

			ResizeUnsolvedClues(gridSize);
			ResizeLargestClues(gridSize);
			ResizeState(gridSize);
		}

		public void ResizeLargestClues(int size)
		{
			if (LargestRowClues.Length != size)
				LargestRowClues = new int[size];
			if (LargestColumnClues.Length != size)
				LargestColumnClues = new int[size];
		}

		public void ResizeUnsolvedClues(int size)
		{
			if (UnsolvedRowClues.Length != size)
				UnsolvedRowClues = new (int, int)[size];
			if (UnsolvedColumnClues.Length != size)
				UnsolvedColumnClues = new (int, int)[size];
			for (int i = 0; i < size; i++)
			{
				UnsolvedRowClues[i] = (0, 0);
				UnsolvedColumnClues[i] = (0, 0);
			}
		}

		public void ResizeState(int size)
		{
			if (_previousFilled.Length != size)
				_previousFilled = new uint[size];
			if (_previousMarked.Length != size)
				_previousMarked = new uint[size];
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

		public int GetLargestClue(Vector2I iterDirection, int index)
		{
			if (iterDirection == Vector2I.Down)
				return LargestRowClues[index];
			if (iterDirection == Vector2I.Right)
				return LargestColumnClues[index];
			return int.MinValue;
		}

		public (int, int) GetUnsolvedClueBounds(Vector2I iterDirection, int index)
		{
			if (iterDirection == Vector2I.Down)
				return UnsolvedRowClues[index];
			if (iterDirection == Vector2I.Right)
				return UnsolvedColumnClues[index];
			return (int.MinValue, int.MaxValue);
		}

		public int GetHighestUnsolvedClue(Vector2I iterDirection, int index)
		{
			if (iterDirection == Vector2I.Down)
				return UnsolvedRowClues[index].Item2;
			if (iterDirection == Vector2I.Right)
				return UnsolvedColumnClues[index].Item2;
			return int.MaxValue;
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

		public void UpdateUnsolvedClues(int lineIndex, Vector2I iterDirection, int low, int high)
		{
			if (iterDirection == Vector2I.Down) // row clues
				UnsolvedRowClues[lineIndex] = (low, high);
			if (iterDirection == Vector2I.Right) // column clues
				UnsolvedColumnClues[lineIndex] = (low, high);
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

	/// <summary>
	/// EXPERIMENTAL — not yet wired into the solver.
	///
	/// A complete single-line solver. Given a line (as filled/marked bitmasks) and its clue
	/// lengths, it computes, for every clue, the earliest (leftmost) and latest (rightmost) start
	/// position it can occupy across all arrangements that (a) avoid marked cells, (b) keep the
	/// required gaps, and (c) cover every filled cell. From those two arrangements every forced
	/// cell follows — including Simple Boxes, Glue, Mercury, Forcing Spaces, Joining/Splitting,
	/// and mid-line clue completion — see <see cref="Deduce"/>.
	///
	/// The DP <see cref="Feasible"/>(i, p) answers "can clues[i..] be placed within cells [p, size)
	/// while covering all filled cells there?" Leftmost reconstruction then walks each clue to the
	/// earliest spot that keeps the remainder feasible; rightmost reuses leftmost on the mirrored line.
	/// O(clues × size) thanks to memoisation — trivial for a 30-cell line.
	/// </summary>
	internal sealed class LineSolver
	{
		private readonly int _size;
		private readonly uint _filled;
		private readonly uint _marked;
		private readonly int[] _lens;
		private readonly sbyte[,] _feasible; // memo: -1 unknown, 0 no, 1 yes; indexed [clue, cell]

		public LineSolver(int size, uint filled, uint marked, int[] clueLengths)
		{
			_size = size;
			_filled = filled;
			_marked = marked;
			_lens = clueLengths;
			_feasible = new sbyte[clueLengths.Length + 1, size + 1];
			for (int i = 0; i <= clueLengths.Length; i++)
				for (int p = 0; p <= size; p++)
					_feasible[i, p] = -1;
		}

		private bool FilledAt(int cell) => cell >= 0 && cell < _size && (_filled & (1u << cell)) != 0;

		/// <summary>Can a clue of <paramref name="len"/> sit exactly at [start, start+len)?</summary>
		private bool Fits(int start, int len)
		{
			if (start < 0 || start + len > _size)
				return false;
			if ((_marked & BitOps.FieldMask(len, start)) != 0)
				return false;                       // no X inside the block
			if (FilledAt(start - 1))
				return false;                       // would merge with a filled cell on the left
			if (FilledAt(start + len))
				return false;                       // ...or on the right (block would be too long)
			return true;
		}

		/// <summary>Are clues[i..] placeable within cells [p, size), covering all filled cells there?</summary>
		private bool Feasible(int i, int p)
		{
			if (i == _lens.Length)
				return (_filled & RangeMask(p, _size)) == 0; // no clues left → nothing may be filled
			if (p > _size)
				return false;
			if (_feasible[i, p] != -1)
				return _feasible[i, p] == 1;

			bool result = false;
			if (p < _size && !FilledAt(p))           // option A: leave cell p empty
				result = Feasible(i, p + 1);
			if (!result && Fits(p, _lens[i]))        // option B: start clue i at p, gap, then the rest
				result = Feasible(i + 1, p + _lens[i] + 1);

			_feasible[i, p] = (sbyte)(result ? 1 : 0);
			return result;
		}

		/// <summary>Earliest start of each clue (entry is -1 if the line is unsatisfiable).</summary>
		public int[] Leftmost()
		{
			var starts = new int[_lens.Length];
			int cursor = 0;
			for (int i = 0; i < _lens.Length; i++)
			{
				int p = cursor;
				while (!(Fits(p, _lens[i]) && Feasible(i + 1, p + _lens[i] + 1)))
				{
					if (FilledAt(p) || p >= _size)   // can't skip a filled cell, or ran off the end
					{
						starts[i] = -1;
						return starts;
					}
					p += 1;
				}
				starts[i] = p;
				cursor = p + _lens[i] + 1;
			}
			return starts;
		}

		/// <summary>Latest start of each clue — leftmost computed on the mirrored line.</summary>
		public int[] Rightmost()
		{
			int k = _lens.Length;
			var reversedLens = new int[k];
			for (int i = 0; i < k; i++)
				reversedLens[i] = _lens[k - 1 - i];

			int[] mirroredLeft = new LineSolver(_size, Reverse(_filled), Reverse(_marked), reversedLens).Leftmost();

			var starts = new int[k];
			for (int i = 0; i < k; i++)
			{
				int mirrored = mirroredLeft[k - 1 - i];
				starts[i] = mirrored < 0 ? -1 : _size - mirrored - _lens[i];
			}
			return starts;
		}

		/// <summary>
		/// Turn the leftmost/rightmost placements into forced cells:
		/// <paramref name="forcedFilled"/> = cells covered by a clue in BOTH extremes (the per-clue
		/// overlap), <paramref name="forcedEmpty"/> = cells covered by NO clue in either extreme.
		/// The clue-completion / split marks fall out of <paramref name="forcedEmpty"/> automatically.
		/// </summary>
		public void Deduce(out uint forcedFilled, out uint forcedEmpty)
		{
			int[] left = Leftmost();
			int[] right = Rightmost();

			forcedFilled = 0;
			uint covered = 0; // any cell a clue could possibly occupy
			for (int i = 0; i < _lens.Length; i++)
			{
				if (left[i] < 0 || right[i] < 0)
				{
					forcedFilled = 0;
					forcedEmpty = 0;
					return; // unsatisfiable line; nothing to deduce
				}

				int len = _lens[i];
				// overlap of the two extreme placements of clue i
				if (right[i] < left[i] + len)
					forcedFilled |= RangeMask(right[i], left[i] + len);
				// everywhere this clue could sit
				covered |= RangeMask(left[i], right[i] + len);
			}

			forcedEmpty = RangeMask(0, _size) & ~covered;
		}

		/// <summary>Bits set for the half-open cell range [start, end).</summary>
		private static uint RangeMask(int start, int end) => end <= start ? 0u : BitOps.FieldMask(end - start, start);

		/// <summary>Reverse the low <see cref="_size"/> bits of a line bitmask.</summary>
		private uint Reverse(uint mask) => BitOps.ReverseBits(mask) >> (32 - _size);
	}
}
