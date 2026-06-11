using Godot;

/// <summary>
/// A single clue (a run of filled cells) within a row or column. Instances are handed to the
/// GDScript UI layer, so this is a <see cref="RefCounted"/> with public, GDScript-callable members.
/// </summary>
public partial class Clue : RefCounted
{
	/// <summary>Index of the clue in the clues array.</summary>
	public int Index { get; private set; }

	/// <summary>First index of the clue in the row/column.</summary>
	public int StartingCell { get; private set; }

	/// <summary>The clue value (length of the run).</summary>
	public int Value { get; private set; }

	/// <summary>Whether this clue has been filled in.</summary>
	public bool Solved { get; private set; }

	// Required parameterless constructor for Godot object instantiation.
	public Clue()
	{
	}

	public Clue(int index, int start, int value)
	{
		Index = index;
		StartingCell = start;
		Value = value;
		Solved = false;
	}

	public void Reset()
	{
		Solved = false;
	}

	public void MarkSolved()
	{
		Solved = true;
	}

	public void ToggleSolved()
	{
		Solved = !Solved;
	}

	public bool IsSolved()
	{
		return Solved;
	}
}
