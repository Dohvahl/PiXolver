using System;
using System.Collections.Generic;
using System.Linq;

namespace PiXolver.Core;

/// <summary>
/// Data representation of a nonogram puzzle: the playing field, the solution, and the derived clues.
/// Plain data with no Godot dependency; the Godot layer wraps it for the grid UI. Construct with
/// <c>new()</c> then call <see cref="Initialize"/> (or <see cref="InitializeFromClues"/>).
/// </summary>
/// <remarks>
/// The playing field is a single 2D grid (<see cref="_grid"/>) — the one source of truth. Per-line
/// filled/marked bitmasks are derived from it on demand for the solver's bit-twiddling techniques.
/// </remarks>
public partial class Puzzle
{
	[Flags]
	internal enum CellState : byte
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
	// Per-line bitmasks of the (immutable) solution, precomputed once so "is this line solved?" checks
	// don't re-scan _solution. Left as zeros for clues-only puzzles, where there is no solution.
	private uint[] _solutionRowFilled = Array.Empty<uint>();
	private uint[] _solutionColumnFilled = Array.Empty<uint>();

	// Derived clue metadata for the top/left of the puzzle.
	private ClueLine[] _rowClues;
	private ClueLine[] _columnClues;

	public int MaxRowClues { get; private set; }
	public int MaxColumnClues { get; private set; }

	// True when built from a known solution (IsRowSolved/IsColumnSolved compare against it). False for
	// clues-only puzzles, where "solved" means the line's filled runs match its clues.
	private bool _hasSolution;

	public bool HasSolution => _hasSolution;

	public Puzzle()
	{
	}

	public void Initialize(string puzzleFile, int gridSize, string initialState)
	{
		PuzzleFile = puzzleFile;
		GridSize = gridSize;
		_hasSolution = true;

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

		// precompute the solution's per-line bitmasks (used by the solved-state checks)
		PrecomputeSolutionMasks();

		// add clues to the top and left side of the puzzle
		SetupClues();
	}

	/// <summary>
	/// Build a clues-only puzzle (no known solution) from text in the format:
	/// <c>x y</c> on the first line, then x lines of row clues, then y lines of column clues. Each
	/// clue line is whitespace-separated positive integers; a blank line (or a lone 0) means no clues.
	/// Only square puzzles (x == y) are supported.
	/// </summary>
	public void InitializeFromClues(string puzzleFile, string cluesText)
	{
		PuzzleFile = puzzleFile;
		_hasSolution = false;
		MaxRowClues = 0;
		MaxColumnClues = 0;

		string[] lines = cluesText.Split('\n').Select(s => s.Trim()).ToArray();

		// the first non-empty line is the "x y" header
		int cursor = 0;
		while (cursor < lines.Length && lines[cursor].Length == 0)
			cursor++;

		string[] dims = lines[cursor].Split(' ', StringSplitOptions.RemoveEmptyEntries);
		cursor++;
		int x = int.Parse(dims[0]);
		int y = dims.Length > 1 ? int.Parse(dims[1]) : x;
		// only square puzzles are supported; a non-square header (x != y) just uses x as the grid size

		int gridSize = x;
		GridSize = gridSize;
		_grid = new CellState[gridSize, gridSize];
		_solution = new bool[gridSize, gridSize]; // unused for clues-only; kept non-null for safety
		_solutionRowFilled = new uint[gridSize];     // unused for clues-only
		_solutionColumnFilled = new uint[gridSize];  // unused for clues-only
		_rowClues = new ClueLine[gridSize];
		_columnClues = new ClueLine[gridSize];
		for (int i = 0; i < gridSize; i++)
		{
			_rowClues[i] = new ClueLine();
			_columnClues[i] = new ClueLine();
		}

		// x lines of row clues, then y (== x) lines of column clues
		for (int row = 0; row < gridSize; row++)
			MaxRowClues = Math.Max(MaxRowClues, ParseClueLine(lines, ref cursor, _rowClues[row]));

		for (int col = 0; col < gridSize; col++)
			MaxColumnClues = Math.Max(MaxColumnClues, ParseClueLine(lines, ref cursor, _columnClues[col]));
	}

	// Parses one whitespace-separated clue line into the target; returns the number of clues recorded.
	private static int ParseClueLine(string[] lines, ref int cursor, ClueLine target)
	{
		if (cursor >= lines.Length)
		{
			cursor++;
			return 0;
		}

		string line = lines[cursor];
		cursor++;
		if (line.Length == 0)
			return 0;

		int count = 0;
		foreach (string token in line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
		{
			if (int.TryParse(token, out int value) && value > 0)
			{
				target.RecordClue(0, 0, value); // start position is unknown for clues-only puzzles
				count++;
			}
		}
		return count;
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

	public bool IsCellFilled(Vec2I loc)
	{
		System.Diagnostics.Debug.Assert(IsValidCellIndex(loc), $"Cell Index outside of grid bounds: [{loc.X}, {loc.Y}]");
		return (_grid[loc.X, loc.Y] & CellState.Filled) != 0;
	}

	public bool IsCellMarked(Vec2I loc)
	{
		System.Diagnostics.Debug.Assert(IsValidCellIndex(loc), $"Cell Index outside of grid bounds: [{loc.X}, {loc.Y}]");
		return (_grid[loc.X, loc.Y] & CellState.Marked) != 0;
	}

	public bool IsCellEmpty(Vec2I loc)
	{
		System.Diagnostics.Debug.Assert(IsValidCellIndex(loc), $"Cell Index outside of grid bounds: [{loc.X}, {loc.Y}]");
		return _grid[loc.X, loc.Y] == CellState.Empty;
	}

    internal uint GetFilledCells(int index, Vec2I fillDirection, int offset, int window = -1)
    {
        if (window < 0)
            window = GridSize - 1;

        uint cells = 0;
        if (fillDirection == Vec2I.Right) // row
            cells = RowFilled(index);
        else if (fillDirection == Vec2I.Down) // column
            cells = ColumnFilled(index);

        return BitOps.FieldMask(window) & (cells >> offset);
    }

    public uint GetMarkedCells(int index, Vec2I fillDirection, int offset = 0, int window = -1)
	{
		if (window < 0)
			window = GridSize - 1;

		uint cells = 0;
		if (fillDirection == Vec2I.Right) // row
			cells = RowMarked(index);
		else if (fillDirection == Vec2I.Down) // column
			cells = ColumnMarked(index);

		return BitOps.FieldMask(window) & (cells >> offset);
	}

	public bool ToggleCell(int x, int y)
	{
		if (!IsValidCellIndex(new Vec2I(x, y)))
			return false;

		if ((_grid[x, y] & CellState.Filled) != 0)
			EmptyCell(x, y);
		else
			SetFilled(x, y);
		return true;
	}

    public void SetEmptyCells(int index, Vec2I fillDirection, uint value, int offset = 0)
    {
        if (fillDirection == Vec2I.Right) // row
            SetEmptyRowCells(index, value, offset);
        else if (fillDirection == Vec2I.Down) // column
            SetEmptyColumnCells(index, value, offset);

    }

    private void SetEmptyColumnCells(int index, uint value, int offset)
    {
        while (value != 0)
        {
            int i = BitOps.Ctz(value);
            value &= value - 1;
            if (offset + i < GridSize)
                MarkCell(new Vec2I(index, offset + i));
        }
    }

    private void SetEmptyRowCells(int index, uint value, int offset)
    {
		while (value != 0)
		{
			int i = BitOps.Ctz(value);
			value &= value - 1;
			if (offset + i < GridSize)
				MarkCell(new Vec2I(offset + i, index));
		}
    }

    public void FillLine(int index, Vec2I fillDirection, uint value, int offset = 0)
	{
		if (fillDirection == Vec2I.Right) // row
			FillRow(index, value, offset);
		else if (fillDirection == Vec2I.Down) // column
			FillColumn(index, value, offset);
	}

	public void FillRow(int index, uint value, int offset = 0)
	{
		while (value != 0)
		{
			int i = BitOps.Ctz(value);
			value &= value - 1;
			if (offset + i < GridSize)
				SetFilled(offset + i, index);
		}
	}

	public void FillColumn(int index, uint value, int offset = 0)
	{
		while (value != 0)
		{
			int i = BitOps.Ctz(value);
			value &= value - 1;
			if (offset + i < GridSize)
				SetFilled(index, offset + i);
		}
	}

	public bool MarkCell(Vec2I loc)
	{
		if (!IsValidCellIndex(loc))
			return false;

		_grid[loc.X, loc.Y] = CellState.Marked;
		return true;
	}

	public bool UnmarkCell(int x, int y)
	{
		if (!IsValidCellIndex(new Vec2I(x, y)))
			return false;

		_grid[x, y] &= ~CellState.Marked;
		return true;
	}

	public void MarkEmptyCells(int index, Vec2I fillDirection)
	{
		if (fillDirection == Vec2I.Right) // row
		{
			for (int x = 0; x < GridSize; x++)
			{
				if ((_grid[x, index] & CellState.Filled) == 0)
					_grid[x, index] |= CellState.Marked;
			}
		}
		else if (fillDirection == Vec2I.Down) // column
		{
			for (int y = 0; y < GridSize; y++)
			{
				if ((_grid[index, y] & CellState.Filled) == 0)
					_grid[index, y] |= CellState.Marked;
			}
		}
	}

	public IReadOnlyList<Clue> GetRowClues(int index)
	{
		if (index < 0 || index > GridSize)
			return Array.Empty<Clue>();
		return _rowClues[index].Clues;
	}

	public IReadOnlyList<Clue> GetColClues(int index)
	{
		if (index < 0 || index > GridSize)
			return Array.Empty<Clue>();
		return _columnClues[index].Clues;
	}

	public bool IsValidCellIndex(Vec2I cell)
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

	public bool IsLineSolved(int index, Vec2I fillDirection)
	{
		if (fillDirection == Vec2I.Right) // row
			return IsRowSolved(index);
		if (fillDirection == Vec2I.Down) // column
			return IsColumnSolved(index);
		return false;
	}

	public bool IsRowSolved(int i)
	{
		if (_hasSolution)
			return RowFilled(i) == SolutionRowFilled(i);
		return LineMatchesClues(RowFilled(i), _rowClues[i]);
	}

	public bool IsColumnSolved(int i)
	{
		if (_hasSolution)
			return ColumnFilled(i) == SolutionColumnFilled(i);
		return LineMatchesClues(ColumnFilled(i), _columnClues[i]);
	}

	/// <summary>
	/// Like <see cref="IsLineSolved"/>, but tests a caller-supplied filled mask instead of re-deriving
	/// the line's filled cells from the grid. Lets the solver reuse masks it already has on hand.
	/// </summary>
	public bool IsLineSolvedWith(int index, Vec2I fillDirection, uint filled)
	{
		if (fillDirection == Vec2I.Right) // row
			return _hasSolution ? filled == _solutionRowFilled[index] : LineMatchesClues(filled, _rowClues[index]);
		if (fillDirection == Vec2I.Down) // column
			return _hasSolution ? filled == _solutionColumnFilled[index] : LineMatchesClues(filled, _columnClues[index]);
		return false;
	}

	// Accessors for the solver (same assembly), so it doesn't reach into the grid directly.
	internal uint RowFilledBits(int row) => RowFilled(row);
	internal uint RowMarkedBits(int row) => RowMarked(row);
	internal uint SolutionRowFilledBits(int row) => SolutionRowFilled(row);
	internal int RowMaxClueValue(int row) => _rowClues[row].MaxClueValue;
	internal int ColumnMaxClueValue(int col) => _columnClues[col].MaxClueValue;

	// --- Search support: single-cell writes, completion checks, and checkpoint/restore ---

	/// <summary>Sets a single cell filled (used when the search assumes a value).</summary>
	internal void SetCellFilled(int x, int y) => _grid[x, y] = CellState.Filled;

	/// <summary>Marks a single cell empty (used when the search assumes a value).</summary>
	internal void MarkCellEmpty(int x, int y) => _grid[x, y] = CellState.Marked;

	/// <summary>True if every cell is decided (filled or marked) — none left undecided.</summary>
	internal bool IsFullyDecided()
	{
		for (int x = 0; x < GridSize; x++)
			for (int y = 0; y < GridSize; y++)
				if (_grid[x, y] == CellState.Empty)
					return false;
		return true;
	}

	/// <summary>Finds the first undecided (empty) cell. Returns false if the grid is fully decided.</summary>
	internal bool TryFindUndecidedCell(out int x, out int y)
	{
		for (int yy = 0; yy < GridSize; yy++)
			for (int xx = 0; xx < GridSize; xx++)
				if (_grid[xx, yy] == CellState.Empty)
				{
					x = xx;
					y = yy;
					return true;
				}
		x = -1;
		y = -1;
		return false;
	}

	/// <summary>Opaque grid + clue-solved snapshot for the search's checkpoint/restore (CellState is private).</summary>
	internal sealed class Snapshot
	{
		public CellState[,] Grid;
		public bool[] ClueSolved;
	}

	internal Snapshot CreateSnapshot() => new()
	{
		Grid = new CellState[GridSize, GridSize],
		ClueSolved = new bool[TotalClueCount()],
	};

	internal void CaptureInto(Snapshot s)
	{
		Array.Copy(_grid, s.Grid, _grid.Length);
		int k = 0;
		for (int i = 0; i < GridSize; i++)
			foreach (Clue c in _rowClues[i].Clues) s.ClueSolved[k++] = c.IsSolved();
		for (int i = 0; i < GridSize; i++)
			foreach (Clue c in _columnClues[i].Clues) s.ClueSolved[k++] = c.IsSolved();
	}

	internal void RestoreFrom(Snapshot s)
	{
		Array.Copy(s.Grid, _grid, _grid.Length);
		int k = 0;
		for (int i = 0; i < GridSize; i++)
			foreach (Clue c in _rowClues[i].Clues) c.SetSolved(s.ClueSolved[k++]);
		for (int i = 0; i < GridSize; i++)
			foreach (Clue c in _columnClues[i].Clues) c.SetSolved(s.ClueSolved[k++]);
	}

	private int TotalClueCount()
	{
		int n = 0;
		for (int i = 0; i < GridSize; i++)
			n += _rowClues[i].Clues.Count + _columnClues[i].Clues.Count;
		return n;
	}

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

	private uint SolutionRowFilled(int row) => _solutionRowFilled[row];
	private uint SolutionColumnFilled(int col) => _solutionColumnFilled[col];

	// Build the per-line solution bitmasks from _solution in a single pass (called once at init).
	private void PrecomputeSolutionMasks()
	{
		_solutionRowFilled = new uint[GridSize];
		_solutionColumnFilled = new uint[GridSize];
		for (int y = 0; y < GridSize; y++)
		{
			for (int x = 0; x < GridSize; x++)
			{
				if (_solution[x, y])
				{
					_solutionRowFilled[y] |= 1u << x;
					_solutionColumnFilled[x] |= 1u << y;
				}
			}
		}
	}

	/// <summary>True if the line's maximal filled runs exactly match the clue values, in order.</summary>
	private bool LineMatchesClues(uint filled, ClueLine clueLine)
	{
		List<Clue> clues = clueLine.Clues;
		int clueIndex = 0;
		int runLength = 0;
		for (int i = 0; i < GridSize; i++)
		{
			if ((filled & (1u << i)) != 0)
			{
				runLength++;
			}
			else if (runLength > 0)
			{
				if (clueIndex >= clues.Count || clues[clueIndex].Value != runLength)
					return false;
				clueIndex++;
				runLength = 0;
			}
		}
		if (runLength > 0)
		{
			if (clueIndex >= clues.Count || clues[clueIndex].Value != runLength)
				return false;
			clueIndex++;
		}
		return clueIndex == clues.Count;
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

	// --- Solution / clue setup ---

	private void InitializeSolution(string solvedState)
	{
		int row = 0;

		// iterate over each row state
		string[] rowStates = solvedState.Split('\n').Select(s => s.Trim()).ToArray();
		foreach (string rowState in rowStates)
		{
			if (string.IsNullOrEmpty(rowState))
				continue;

			for (int index = 0; index < rowState.Length; index++)
			{
				System.Diagnostics.Debug.Assert(index < _solution.GetLength(0) && row < _solution.GetLength(1), $"Either {index} or {row} are outside the bounds of {GridSize} in puzzle {PuzzleFile}");
                _solution[index, row] = rowState[index] == '1';
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

		currentMax = 0;
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
		public List<Clue> Clues { get; } = new();
		public int MaxClueValue { get; private set; } = int.MinValue;

		public int RecordClue(int index, int start, int value)
		{
			MaxClueValue = Math.Max(MaxClueValue, value);
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
