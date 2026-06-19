namespace PiXolver.Core;

/// <summary>
/// A single clue (a run of filled cells) within a row or column. Plain data; the Godot UI layer wraps
/// these for display.
/// </summary>
public class Clue
{
	/// <summary>Index of the clue in the clues array.</summary>
	public int Index { get; private set; }

	/// <summary>First index of the clue in the row/column.</summary>
	public int StartingCell { get; private set; }

	/// <summary>The clue value (length of the run).</summary>
	public int Value { get; private set; }

	/// <summary>Whether this clue has been filled in.</summary>
	public bool Solved { get; private set; }

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
