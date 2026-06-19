using Godot;
using Core = PiXolver.Core;

/// <summary>
/// Godot-facing wrapper over <see cref="Core.Clue"/>, so the GDScript UI can read clue data. Reads
/// delegate to the wrapped core clue, so solved-state stays live.
/// </summary>
[GlobalClass]
public partial class Clue : RefCounted
{
	private readonly Core.Clue _core;

	// Parameterless ctor required for Godot instantiation; not used by the UI (clues come from Puzzle).
	public Clue()
	{
	}

	public Clue(Core.Clue core)
	{
		_core = core;
	}

	public int Index => _core?.Index ?? 0;
	public int StartingCell => _core?.StartingCell ?? 0;
	public int Value => _core?.Value ?? 0;

	public bool IsSolved() => _core?.IsSolved() ?? false;
}
