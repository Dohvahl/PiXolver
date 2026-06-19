namespace PiXolver.Core;

/// <summary>
/// Plain result of a <see cref="Solver.Run"/>. The Godot layer converts this into the dictionary the
/// GDScript UI expects; nothing here depends on Godot.
/// </summary>
public struct SolveResult
{
	/// <summary>Queue pops (lines processed) before reaching the fixpoint.</summary>
	public int LinesProcessed;

	/// <summary>Wall-clock solve time in microseconds.</summary>
	public double TimeMicroseconds;

	/// <summary>True if the puzzle was fully solved.</summary>
	public bool IsSolved;

	/// <summary>
	/// True when <see cref="FilledFraction"/>/<see cref="SolvedFraction"/>/<see cref="IncorrectCells"/>
	/// are populated — i.e. the puzzle was not solved but has a known solution to score against.
	/// </summary>
	public bool HasStats;

	public double FilledFraction;
	public double SolvedFraction;
	public int IncorrectCells;
}
