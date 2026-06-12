using Godot;
using System;

/// <summary>
/// Data representation of a nonogram puzzle: the playing field, the solution, and the derived clues.
/// Exposed to GDScript (the grid UI) as a global class. Construct with <c>new()</c> then call
/// <see cref="Initialize"/> — Godot objects cannot take constructor arguments from GDScript.
/// </summary>
/// <remarks>
/// The playing field is a single 2D grid (<see cref="_grid"/>) — the one source of truth. Per-line
/// filled/marked bitmasks are derived from it on demand for the solver's bit-twiddling techniques.
/// </remarks>
[GlobalClass]
public partial class Puzzle : RefCounted
{
	[Flags]
	private enum CellState : byte
	{
		Empty = 0,
		Filled = 1,
		Marked = 2,
	}

	public string PuzzleFile { get; private set; }

	public int GridSize { get; private set; }

	// Single source of truth: the playing field and the (immutable) solution.
	private CellState[,] _grid;   // _grid[x, y]
	private bool[,] _solution;    // _solution[x, y] — filled cells of the answer

	// Derived clue metadata for the top/left of the puzzle.
	private ClueLine[] _rowClues;
	private ClueLine[] _columnClues;

	public int MaxRowClues { get; private set; }
	public int MaxColumnClues { get; private set; }

	public Puzzle()
	{
	}

	public void Initialize(string puzzleFile, int gridSize, string initialState)
	{
		PuzzleFile = puzzleFile;
		GridSize = gridSize;

		_grid = new CellState[gridSize, gridSize];
		_solution = new bool[gridSize, gridSize];

		_rowClues = new ClueLine[gridSize];
		_columnClues = new ClueLine[gridSize];
		for (int i = 0; i < gridSize; i++)
		{
			_rowClues[i] = new ClueLine();
			_columnClues[i] = new ClueLine();
		}

		// set the "solved" state of the puzzle
		InitializeSolution(initialState);

		// add clues to the top and left side of the puzzle
		SetupClues();
	}

	public void Reset()
	{
		// clear the playing field; the solution is left intact
		Array.Clear(_grid, 0, _grid.Length);
		for (int i = 0; i < GridSize; i++)
		{
			_rowClues[i].Reset();
			_columnClues[i].Reset();
		}
	}

	public bool IsCellFilled(Vector2I loc)
	{
		System.Diagnostics.Debug.Assert(IsValidCellIndex(loc), $"Cell Index outside of grid bounds: [{loc.X}, {loc.Y}]");
		return (_grid[loc.X, loc.Y] & CellState.Filled) != 0;
	}

	public bool IsCellMarked(Vector2I loc)
	{
		System.Diagnostics.Debug.Assert(IsValidCellIndex(loc), $"Cell Index outside of grid bounds: [{loc.X}, {loc.Y}]");
		return (_grid[loc.X, loc.Y] & CellState.Marked) != 0;
	}

	public bool IsCellEmpty(Vector2I loc)
	{
		System.Diagnostics.Debug.Assert(IsValidCellIndex(loc), $"Cell Index outside of grid bounds: [{loc.X}, {loc.Y}]");
		return _grid[loc.X, loc.Y] == CellState.Empty;
	}

	public bool AreNCellsFilled(Vector2I startLoc, Vector2I fillDirection, int n)
	{
		Vector2I zerodCell = startLoc * fillDirection;
		int startIndex = Mathf.Max(zerodCell.X, zerodCell.Y);
		uint nMask = BitOps.FieldMask(n, startIndex);
		uint cells = 0;
		if (fillDirection == Vector2I.Right) // row
			cells = RowFilled(startLoc.Y);
		else if (fillDirection == Vector2I.Down) // column
			cells = ColumnFilled(startLoc.X);

		return (cells & nMask) == nMask;
	}

	public uint GetMarkedCells(int index, Vector2I fillDirection, int offset = 0, int window = -1)
	{
		if (window < 0)
			window = GridSize - 1;

		uint cells = 0;
		if (fillDirection == Vector2I.Right) // row
			cells = RowMarked(index);
		else if (fillDirection == Vector2I.Down) // column
			cells = ColumnMarked(index);

		return BitOps.FieldMask(window) & (cells >> offset);
	}

	/// <summary>Get the first filled cell, starting from startCell, up to count. Returns -1 if none are set.</summary>
	public int GetFirstFilled(Vector2I startCell, Vector2I fillDirection, int count = -1)
	{
		if (count < 0)
			count = GridSize - 1;

		if (fillDirection == Vector2I.Right) // row
			return BitOps.FirstSet(RowFilled(startCell.Y), startCell.X, count);
		if (fillDirection == Vector2I.Down) // column
			return BitOps.FirstSet(ColumnFilled(startCell.X), startCell.Y, count);

		return -1;
	}

	/// <summary>Get the last filled cell, starting from endCell, down to endCell-count. Returns -1 if none are set.</summary>
	public int GetLastFilled(Vector2I endCell, Vector2I fillDirection, int count = -1)
	{
		if (count < 0)
			count = GridSize - 1;

		uint filled = 0;
		int offset = 0;
		if (fillDirection == Vector2I.Right) // row
		{
			filled = RowFilled(endCell.Y);
			offset = endCell.X;
		}
		else if (fillDirection == Vector2I.Down) // column
		{
			filled = ColumnFilled(endCell.X);
			offset = endCell.Y;
		}

		return BitOps.LastSet(filled, offset - count + 1, count);
	}

	/// <summary>Get the first marked cell, starting from startCell, up to count. Returns -1 if none are marked.</summary>
	public int GetFirstMarked(Vector2I startCell, Vector2I fillDirection, int count = -1)
	{
		if (count < 0)
			count = GridSize - 1;

		if (fillDirection == Vector2I.Right) // row
			return BitOps.FirstSet(RowMarked(startCell.Y), startCell.X, count);
		if (fillDirection == Vector2I.Down) // column
			return BitOps.FirstSet(ColumnMarked(startCell.X), startCell.Y, count);

		return -1;
	}

	/// <summary>Get the last marked cell, starting from endCell, down to endCell-count. Returns -1 if none are marked.</summary>
	public int GetLastMarked(Vector2I endCell, Vector2I fillDirection, int count = -1)
	{
		if (count < 0)
			count = GridSize - 1;

		uint marked = 0;
		int offset = 0;
		if (fillDirection == Vector2I.Right) // row
		{
			marked = RowMarked(endCell.Y);
			offset = endCell.X;
		}
		else if (fillDirection == Vector2I.Down) // column
		{
			marked = ColumnMarked(endCell.X);
			offset = endCell.Y;
		}

		return BitOps.LastSet(marked, offset - count + 1, count);
	}

	public bool ToggleCell(int x, int y)
	{
		if (!IsValidCellIndex(new Vector2I(x, y)))
			return false;

		if ((_grid[x, y] & CellState.Filled) != 0)
			EmptyCell(x, y);
		else
			SetFilled(x, y);
		return true;
	}

	public void FillLine(int index, Vector2I fillDirection, uint value, int offset = 0)
	{
		if (fillDirection == Vector2I.Right) // row
			FillRow(index, value, offset);
		else if (fillDirection == Vector2I.Down) // column
			FillColumn(index, value, offset);
	}

	public void FillRow(int index, uint value, int offset = 0)
	{
		int i = 0;
		while (offset + i < GridSize)
		{
			if ((value & (1u << i)) != 0)
				SetFilled(offset + i, index);
			i += 1;
		}
	}

	public void FillColumn(int index, uint value, int offset = 0)
	{
		int i = 0;
		while (offset + i < GridSize)
		{
			if ((value & (1u << i)) != 0)
				SetFilled(index, offset + i);
			i += 1;
		}
	}

	public void FillNCells(Vector2I start, int n, Vector2I fillDir)
	{
		if (!IsValidCellIndex(start) || !IsValidCellIndex(start + (n * fillDir)))
			return;

		for (int i = 0; i < n; i++)
			FillCell(start + (i * fillDir));
	}

	public bool MarkCell(Vector2I loc)
	{
		if (!IsValidCellIndex(loc))
			return false;

		_grid[loc.X, loc.Y] = CellState.Marked;
		return true;
	}

	public void MarkNCells(Vector2I start, int n, Vector2I fillDir, Vector2I endCell)
	{
		if (n < 1 || !IsValidCellIndex(start))
			return;

		Vector2I cell = start;
        Vector2I end = endCell.Min(start + (n * fillDir));
        for (int i = 0; cell <= end; i++)
		{
			MarkCell(cell);
			cell += i * fillDir;
		}		
	}

	public bool UnmarkCell(int x, int y)
	{
		if (!IsValidCellIndex(new Vector2I(x, y)))
			return false;

		_grid[x, y] &= ~CellState.Marked;
		return true;
	}

	public int GetNumEmptyCells(Vector2I startCell, Vector2I fillDirection)
	{
		int count = 0;
		Vector2I currentCell = startCell;
		while (IsCellEmpty(currentCell))
		{
			count += 1;
			currentCell += fillDirection;
		}
		return count;
	}

	public void MarkEmptyCells(int index, Vector2I fillDirection)
	{
		if (fillDirection == Vector2I.Right) // row
		{
			for (int x = 0; x < GridSize; x++)
			{
				if ((_grid[x, index] & CellState.Filled) == 0)
					_grid[x, index] |= CellState.Marked;
			}
		}
		else if (fillDirection == Vector2I.Down) // column
		{
			for (int y = 0; y < GridSize; y++)
			{
				if ((_grid[index, y] & CellState.Filled) == 0)
					_grid[index, y] |= CellState.Marked;
			}
		}
	}

	public int CellIndexFromLocation(int x, int y)
	{
		return x + (y * GridSize);
	}

	public Godot.Collections.Array<Clue> GetRowClues(int index)
	{
		if (index < 0 || index > GridSize)
			return new Godot.Collections.Array<Clue>();
		return _rowClues[index].Clues;
	}

	public Godot.Collections.Array<Clue> GetColClues(int index)
	{
		if (index < 0 || index > GridSize)
			return new Godot.Collections.Array<Clue>();
		return _columnClues[index].Clues;
	}

	public bool IsValidCellIndex(Vector2I cell)
	{
		return cell.X >= 0 && cell.X < GridSize && cell.Y >= 0 && cell.Y < GridSize;
	}

	public bool IsSolved()
	{
		for (int i = 0; i < GridSize; i++)
		{
			if (!IsRowSolved(i))
				return false;
		}

		return true;
	}

	public bool IsLineSolved(int index, Vector2I fillDirection)
	{
		if (fillDirection == Vector2I.Right) // row
			return IsRowSolved(index);
		if (fillDirection == Vector2I.Down) // column
			return IsColumnSolved(index);
		return false;
	}

	public bool IsRowSolved(int i)
	{
		// the solution carries no marks, so a solved line is correctly filled and unmarked
		return RowFilled(i) == SolutionRowFilled(i) && RowMarked(i) == 0;
	}

	public bool IsColumnSolved(int i)
	{
		return ColumnFilled(i) == SolutionColumnFilled(i) && ColumnMarked(i) == 0;
	}

	// Accessors for the solver (same assembly), so it doesn't reach into the grid directly.
	internal uint RowFilledBits(int row) => RowFilled(row);
	internal uint RowMarkedBits(int row) => RowMarked(row);
	internal uint SolutionRowFilledBits(int row) => SolutionRowFilled(row);
	internal int RowMaxClueValue(int row) => _rowClues[row].MaxClueValue;
	internal int ColumnMaxClueValue(int col) => _columnClues[col].MaxClueValue;

	#region "Private" Functions

	// --- Per-line bitmasks derived from the 2D grid ---

	private uint RowFilled(int row)
	{
		uint bits = 0;
		for (int x = 0; x < GridSize; x++)
		{
			if ((_grid[x, row] & CellState.Filled) != 0)
				bits |= 1u << x;
		}
		return bits;
	}

	private uint ColumnFilled(int col)
	{
		uint bits = 0;
		for (int y = 0; y < GridSize; y++)
		{
			if ((_grid[col, y] & CellState.Filled) != 0)
				bits |= 1u << y;
		}
		return bits;
	}

	private uint RowMarked(int row)
	{
		uint bits = 0;
		for (int x = 0; x < GridSize; x++)
		{
			if ((_grid[x, row] & CellState.Marked) != 0)
				bits |= 1u << x;
		}
		return bits;
	}

	private uint ColumnMarked(int col)
	{
		uint bits = 0;
		for (int y = 0; y < GridSize; y++)
		{
			if ((_grid[col, y] & CellState.Marked) != 0)
				bits |= 1u << y;
		}
		return bits;
	}

	private uint SolutionRowFilled(int row)
	{
		uint bits = 0;
		for (int x = 0; x < GridSize; x++)
		{
			if (_solution[x, row])
				bits |= 1u << x;
		}
		return bits;
	}

	private uint SolutionColumnFilled(int col)
	{
		uint bits = 0;
		for (int y = 0; y < GridSize; y++)
		{
			if (_solution[col, y])
				bits |= 1u << y;
		}
		return bits;
	}

	// --- Single-cell writes (the one place cell state changes) ---

	private void SetFilled(int x, int y)
	{
		// filling a cell clears any mark (a filled cell is not "marked empty")
		_grid[x, y] = CellState.Filled;
	}

	private void EmptyCell(int x, int y)
	{
		_grid[x, y] = 0;
	}

	private bool FillCell(Vector2I loc)
	{
		if (!IsValidCellIndex(loc))
			return false;

		SetFilled(loc.X, loc.Y);
		return true;
	}

	// --- Solution / clue setup ---

	private void InitializeSolution(string solvedState)
	{
		int row = 0;

		// iterate over each row state
		string[] rowStates = solvedState.Split('\n');
		foreach (string rowState in rowStates)
		{
			for (int index = 0; index < rowState.Length; index++)
			{
				if (rowState[index] == '1')
					_solution[index, row] = true;
			}
			row += 1;
		}
	}

	private void SetupClues()
	{
		int currentMax = 0;

		// figure out row clues
		for (int row = 0; row < GridSize; row++)
		{
			int i = 0;
			int currentClue = 0;
			while (i < GridSize)
			{
				if (_solution[i, row])
				{
					currentClue += 1;
				}
				else if (currentClue > 0)
				{
					int currentCount = AddRowClue(row, i - currentClue, currentClue);
					if (currentCount > currentMax)
						currentMax = currentCount;
					currentClue = 0;
				}
				i += 1;
			}

			// we may have made it to the end of the row with clues, so we need to add those here
			if (currentClue > 0)
			{
				int currentCount = AddRowClue(row, i - currentClue, currentClue);
				if (currentCount > currentMax)
					currentMax = currentCount;
			}

			if (currentMax > MaxRowClues)
				MaxRowClues = currentMax;
		}

		// figure out column clues
		for (int col = 0; col < GridSize; col++)
		{
			int i = 0;
			int currentClue = 0;
			while (i < GridSize)
			{
				if (_solution[col, i])
				{
					currentClue += 1;
				}
				else if (currentClue > 0)
				{
					int currentCount = AddColClue(col, i - currentClue, currentClue);
					if (currentCount > currentMax)
						currentMax = currentCount;
					currentClue = 0;
				}
				i += 1;
			}

			// we may have made it to the end of the column with clues, so we need to add those here
			if (currentClue > 0)
			{
				int currentCount = AddColClue(col, i - currentClue, currentClue);
				if (currentCount > currentMax)
					currentMax = currentCount;
			}

			if (currentMax > MaxColumnClues)
				MaxColumnClues = currentMax;
		}
	}

	// returns the number of clues currently in the array
	private int AddRowClue(int row, int startCol, int clue)
	{
		return _rowClues[row].RecordClue(GridSize, startCol, clue);
	}

	// returns the number of clues currently in the array
	private int AddColClue(int col, int startRow, int clue)
	{
		return _columnClues[col].RecordClue(GridSize, startRow, clue);
	}

	/// <summary>Clue metadata for a single row or column.</summary>
	private sealed class ClueLine
	{
		public Godot.Collections.Array<Clue> Clues { get; } = new();
		public int MaxClueValue { get; private set; } = int.MinValue;

		public int RecordClue(int index, int start, int value)
		{
			MaxClueValue = Mathf.Max(MaxClueValue, value);
			Clues.Add(new Clue(index, start, value));
			return Clues.Count;
		}

		public void Reset()
		{
			foreach (Clue clue in Clues)
				clue.Reset();
		}
	}

	#endregion
}
