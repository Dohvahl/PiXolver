using Godot;
using Core = PiXolver.Core;

/// <summary>
/// Godot-facing wrapper over <see cref="Core.Solver"/>. Operates on the <see cref="Puzzle"/> wrapper and
/// converts the core's <see cref="Core.SolveResult"/> into the dictionary the GDScript UI expects.
/// </summary>
[GlobalClass]
public partial class Solver : RefCounted
{
	private readonly Core.Solver _core = new();

	public void Init(int gridSize) => _core.Init(gridSize);
	public void Reset() => _core.Reset();

	public Godot.Collections.Dictionary Run(Puzzle puzzle, bool debug = false)
	{
		Core.SolveResult r = _core.Run(puzzle.Inner);

		if (debug)
			GD.Print($"Total Solve Time: {r.TimeMicroseconds:F1} microsec");

		var results = new Godot.Collections.Dictionary
		{
			{ "lines_processed", r.LinesProcessed },
			{ "time_us", r.TimeMicroseconds },
		};

		if (r.IsSolved)
		{
			results["is_solved"] = true;
		}
		else if (r.HasStats)
		{
			results["filled"] = r.FilledFraction;
			results["solved"] = r.SolvedFraction;
			results["incorrect"] = r.IncorrectCells;
		}

		return results;
	}

	// iterations/debug are kept for GDScript call-site compatibility; the core sweep needs neither.
	public bool RunSingle(Puzzle puzzle, int iterations = 0, bool debug = false) => _core.RunSingle(puzzle.Inner);

	public void RunRows(Puzzle puzzle) => _core.RunRows(puzzle.Inner);
	public void RunColumns(Puzzle puzzle) => _core.RunColumns(puzzle.Inner);
}
