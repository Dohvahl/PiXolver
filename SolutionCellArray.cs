using Godot;

/// <summary>
/// A <see cref="CellArray"/> for the solved ("answer") side of the puzzle. In addition to the cell
/// bitmasks it owns the clues derived from the solution and tracks the largest/smallest clue value.
/// </summary>
internal class SolutionCellArray : CellArray
{
	// Clues are surfaced to the GDScript UI via Puzzle.GetRowClues/GetColClues, so they live in a
	// Godot array of the (Godot-visible) Clue type.
	public Godot.Collections.Array<Clue> Clues { get; } = new();

	public int MaxClueValue { get; private set; } = int.MinValue;
	public int MinClueValue { get; private set; } = int.MaxValue;

	public SolutionCellArray(int numCells) : base(numCells)
	{
	}

	// Note: intentionally does NOT reset the solution's cell bitmasks (mirrors the original
	// GDScript); only the clues' solved state is cleared between runs.
	public override void Reset()
	{
		foreach (Clue clue in Clues)
			clue.Reset();
	}

	/// <summary>Records a clue and returns the new clue count.</summary>
	public int RecordClue(int index, int start, int value)
	{
		MaxClueValue = Mathf.Max(MaxClueValue, value);
		MinClueValue = Mathf.Min(MinClueValue, value);

		Clues.Add(new Clue(index, start, value));
		return Clues.Count;
	}
}
