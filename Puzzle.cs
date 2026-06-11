using Godot;

/// <summary>
/// Data representation of a nonogram puzzle: the playing field, the solution, and the derived clues.
/// Exposed to GDScript (the grid UI) as a global class. Construct with <c>new()</c> then call
/// <see cref="Initialize"/> — Godot objects cannot take constructor arguments from GDScript.
/// </summary>
[GlobalClass]
public partial class Puzzle : RefCounted
{
	public string PuzzleFile { get; private set; }

	public int GridSize { get; private set; }

	internal CellArray[] Rows;
	internal CellArray[] Columns;
	internal SolutionCellArray[] SolutionRows;
	internal SolutionCellArray[] SolutionColumns;

	public int MaxRowClues { get; private set; }
	public int MaxColumnClues { get; private set; }

	public Puzzle()
	{
	}

	public void Initialize(string puzzleFile, int gridSize, string initialState)
	{
		PuzzleFile = puzzleFile;
		GridSize = gridSize;

		// initialize the puzzle that we'll use in normal play
		Rows = new CellArray[gridSize];
		Columns = new CellArray[gridSize];
		for (int i = 0; i < gridSize; i++)
		{
			Rows[i] = new CellArray(gridSize);
			Columns[i] = new CellArray(gridSize);
		}

		// initialize the "solved" version of the puzzle
		SolutionRows = new SolutionCellArray[gridSize];
		SolutionColumns = new SolutionCellArray[gridSize];
		for (int i = 0; i < gridSize; i++)
		{
			SolutionRows[i] = new SolutionCellArray(gridSize);
			SolutionColumns[i] = new SolutionCellArray(gridSize);
		}

		// set the "solved" state of the puzzle
		InitializeSolution(initialState);

		// add clues to the top and left side of the puzzle
		SetupClues();
	}

	public void Reset()
	{
		for (int i = 0; i < GridSize; i++)
		{
			Rows[i].Reset();
			Columns[i].Reset();

			SolutionRows[i].Reset();
			SolutionColumns[i].Reset();
		}
	}

	public bool IsCellFilled(Vector2I loc)
	{
		System.Diagnostics.Debug.Assert(IsValidCellIndex(loc), $"Cell Index outside of grid bounds: [{loc.X}, {loc.Y}]");
		return Rows[loc.Y].IsCellFilled(loc.X);
	}

	public bool IsCellMarked(Vector2I loc)
	{
		System.Diagnostics.Debug.Assert(IsValidCellIndex(loc), $"Cell Index outside of grid bounds: [{loc.X}, {loc.Y}]");
		return Rows[loc.Y].IsCellMarked(loc.X);
	}

	public bool IsCellEmpty(Vector2I loc)
	{
		System.Diagnostics.Debug.Assert(IsValidCellIndex(loc), $"Cell Index outside of grid bounds: [{loc.X}, {loc.Y}]");
		return !Rows[loc.Y].IsCellMarked(loc.X) && !Rows[loc.Y].IsCellFilled(loc.X);
	}

	public bool AreNCellsFilled(Vector2I startLoc, Vector2I fillDirection, int n)
	{
		Vector2I zerodCell = startLoc * fillDirection;
		int startIndex = Mathf.Max(zerodCell.X, zerodCell.Y);
		long nMask = BitOps.FieldMask(n, startIndex);
		long cells = 0;
		if (fillDirection == Vector2I.Right) // row
			cells = Rows[startLoc.Y].FilledCells;
		else if (fillDirection == Vector2I.Down) // column
			cells = Columns[startLoc.X].FilledCells;

		return (cells & nMask) == nMask;
	}

	public long GetMarkedCells(int index, Vector2I fillDirection, int offset = 0, int window = -1)
	{
		if (window < 0)
			window = GridSize - 1;

		long cells = 0;
		if (fillDirection == Vector2I.Right) // row
			cells = Rows[index].MarkedCells;
		else if (fillDirection == Vector2I.Down) // column
			cells = Columns[index].MarkedCells;

		return BitOps.FieldMask(window, offset) & (cells >> offset);
	}

	/// <summary>Get the first filled cell, starting from startCell, up to count. Returns -1 if none are set.</summary>
	public int GetFirstFilled(Vector2I startCell, Vector2I fillDirection, int count = -1)
	{
		if (count < 0)
			count = GridSize - 1;

		if (fillDirection == Vector2I.Right) // row
			return BitOps.FirstSet(Rows[startCell.Y].FilledCells, startCell.X, count);
		if (fillDirection == Vector2I.Down) // column
			return BitOps.FirstSet(Columns[startCell.X].FilledCells, startCell.Y, count);

		return -1;
	}

	/// <summary>Get the last filled cell, starting from endCell, down to endCell-count. Returns -1 if none are set.</summary>
	public int GetLastFilled(Vector2I endCell, Vector2I fillDirection, int count = -1)
	{
		if (count < 0)
			count = GridSize - 1;

		long filled = -1;
		int offset = 0;
		if (fillDirection == Vector2I.Right) // row
		{
			filled = Rows[endCell.Y].FilledCells;
			offset = endCell.X;
		}
		else if (fillDirection == Vector2I.Down) // column
		{
			filled = Columns[endCell.X].FilledCells;
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
			return BitOps.FirstSet(Rows[startCell.Y].MarkedCells, startCell.X, count);
		if (fillDirection == Vector2I.Down) // column
			return BitOps.FirstSet(Columns[startCell.X].MarkedCells, startCell.Y, count);

		return -1;
	}

	/// <summary>Get the last marked cell, starting from endCell, down to endCell-count. Returns -1 if none are marked.</summary>
	public int GetLastMarked(Vector2I endCell, Vector2I fillDirection, int count = -1)
	{
		if (count < 0)
			count = GridSize - 1;

		long marked = -1;
		int offset = 0;
		if (fillDirection == Vector2I.Right) // row
		{
			marked = Rows[endCell.Y].MarkedCells;
			offset = endCell.X;
		}
		else if (fillDirection == Vector2I.Down) // column
		{
			marked = Columns[endCell.X].MarkedCells;
			offset = endCell.Y;
		}

		return BitOps.LastSet(marked, offset - count + 1, count);
	}

	public bool ToggleCell(int x, int y)
	{
		if (!IsValidCellIndex(new Vector2I(x, y)))
			return false;

		if (Rows[y].IsCellFilled(x))
		{
			Rows[y].EmptyCell(x);
			Columns[x].EmptyCell(y);
		}
		else
		{
			Rows[y].FillCell(x);
			Columns[x].FillCell(y);
		}
		return true;
	}

	public void FillLine(int index, Vector2I fillDirection, long value, int offset = 0)
	{
		if (fillDirection == Vector2I.Right) // row
			FillRow(index, value, offset);
		else if (fillDirection == Vector2I.Down) // column
			FillColumn(index, value, offset);
	}

	public void FillRow(int index, long value, int offset = 0)
	{
		Rows[index].Fill(value, offset);

		// update the columns to match the row
		int i = 0;
		while (offset + i < GridSize)
		{
			if ((value & (1L << i)) != 0)
				Columns[offset + i].FillCell(index);
			i += 1;
		}
	}

	public void FillColumn(int index, long value, int offset = 0)
	{
		Columns[index].Fill(value, offset);

		// update the rows to match the column
		int i = 0;
		while (offset + i < GridSize)
		{
			if ((value & (1L << i)) != 0)
				Rows[offset + i].FillCell(index);
			i += 1;
		}
	}

	public void FillNCells(Vector2I start, int n, Vector2I fillDir)
	{
		if (!IsValidCellIndex(start) || !IsValidCellIndex(start + (n * fillDir)))
			return;

		// we also need to fill in the corresponding row/column
		for (int i = 0; i < n; i++)
			FillCell(start + (i * fillDir));
	}

	public bool MarkCell(Vector2I loc)
	{
		if (!IsValidCellIndex(loc))
			return false;

		Rows[loc.Y].MarkCell(loc.X);
		Columns[loc.X].MarkCell(loc.Y);
		return true;
	}

	public void MarkNCells(Vector2I start, int n, Vector2I fillDir)
	{
		if (!IsValidCellIndex(start) || !IsValidCellIndex(start + (n * fillDir)))
			return;

		// we also need to mark the corresponding row/column
		for (int i = 0; i < n; i++)
			MarkCell(start + (i * fillDir));
	}

	public bool UnmarkCell(int x, int y)
	{
		if (!IsValidCellIndex(new Vector2I(x, y)))
			return false;

		Rows[y].UnmarkCell(x);
		Columns[x].UnmarkCell(y);
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
			Rows[index].MarkEmptyCells();
			for (int i = 0; i < GridSize; i++)
			{
				if (!Columns[i].IsCellFilled(index))
					Columns[i].MarkCell(index);
			}
			Columns[index].MarkEmptyCells();
		}
		if (fillDirection == Vector2I.Down) // column
		{
			for (int i = 0; i < GridSize; i++)
			{
				if (!Rows[i].IsCellFilled(index))
					Rows[i].MarkCell(index);
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
		return SolutionRows[index].Clues;
	}

	public Godot.Collections.Array<Clue> GetColClues(int index)
	{
		if (index < 0 || index > GridSize)
			return new Godot.Collections.Array<Clue>();
		return SolutionColumns[index].Clues;
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
		return SolutionRows[i].Matches(Rows[i]);
	}

	public bool IsColumnSolved(int i)
	{
		return SolutionColumns[i].Matches(Columns[i]);
	}

	#region "Private" Functions

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
				{
					SolutionRows[row].FillCell(index);
					SolutionColumns[index].FillCell(row);
				}
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
				if (SolutionRows[row].IsCellFilled(i))
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
				if (SolutionRows[i].IsCellFilled(col))
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
		int currentNum = SolutionRows[row].Length;
		return SolutionRows[row].RecordClue(currentNum, startCol, clue);
	}

	// returns the number of clues currently in the array
	private int AddColClue(int col, int startRow, int clue)
	{
		int currentNum = SolutionColumns[col].Length;
		return SolutionColumns[col].RecordClue(currentNum, startRow, clue);
	}

	private bool FillCell(Vector2I loc)
	{
		if (!IsValidCellIndex(loc))
			return false;

		Rows[loc.Y].FillCell(loc.X);
		Columns[loc.X].FillCell(loc.Y);
		return true;
	}

	#endregion
}
