/// <summary>
/// Represents a single row or column in the puzzle as a pair of bitmasks (filled and marked cells).
/// Internal to the C# assembly; the GDScript layer only ever sees a <see cref="Puzzle"/>.
/// </summary>
internal class CellArray
{
	/// <summary>Number of cells in the row/column.</summary>
	public int Length;

	/// <summary>Bit-mask representing the marked (crossed-out) cells.</summary>
	public long MarkedCells;

	/// <summary>Bit-mask representing the filled-in cells.</summary>
	public long FilledCells;

	public CellArray(int numCells)
	{
		Length = numCells;
		MarkedCells = 0;
		FilledCells = 0;
	}

	public virtual void Reset()
	{
		MarkedCells = 0;
		FilledCells = 0;
	}

	public void Fill(long value, int offset = 0)
	{
		FilledCells |= value << offset;
	}

	public bool IsCellMarked(int cell)
	{
		return (MarkedCells & (1L << cell)) != 0;
	}

	public bool IsCellFilled(int cell)
	{
		return (FilledCells & (1L << cell)) != 0;
	}

	public void MarkCell(int cell)
	{
		MarkedCells |= 1L << cell;
	}

	public void UnmarkCell(int cell)
	{
		MarkedCells &= ~(1L << cell);
	}

	public void FillCell(int cell)
	{
		UnmarkCell(cell);
		FilledCells |= 1L << cell;
	}

	public void EmptyCell(int cell)
	{
		FilledCells &= ~(1L << cell);
	}

	public void MarkEmptyCells()
	{
		MarkedCells = ~FilledCells;
	}

	public bool Matches(CellArray other)
	{
		return FilledCells == other.FilledCells && MarkedCells == other.MarkedCells;
	}

	public void FillNCells(int n, int offset = 0)
	{
		FilledCells |= ((1L << n) - 1) << offset;
	}
}
