using Godot;
using System.Collections.Generic;
using Core = PiXolver.Core;

/// <summary>
/// Godot-facing wrapper over <see cref="Core.Puzzle"/>. Exposes the surface the GDScript grid UI uses,
/// converting between Godot's <c>Vector2I</c> and the core's <c>Vec2I</c>, and wrapping core clues for
/// display. <see cref="Inner"/> hands the underlying core puzzle to the <c>Solver</c> wrapper.
/// </summary>
[GlobalClass]
public partial class Puzzle : RefCounted
{
	private readonly Core.Puzzle _core = new();

	/// <summary>The wrapped core puzzle (used by the Solver wrapper).</summary>
	internal Core.Puzzle Inner => _core;

	public int GridSize => _core.GridSize;
	public int MaxRowClues => _core.MaxRowClues;
	public int MaxColumnClues => _core.MaxColumnClues;
	public bool HasSolution => _core.HasSolution;

	public void Initialize(string puzzleFile, int gridSize, string initialState)
		=> _core.Initialize(puzzleFile, gridSize, initialState);

	public void InitializeFromClues(string puzzleFile, string cluesText)
		=> _core.InitializeFromClues(puzzleFile, cluesText);

	public void Reset() => _core.Reset();

	public bool IsSolved() => _core.IsSolved();
	public bool IsRowSolved(int i) => _core.IsRowSolved(i);
	public bool IsColumnSolved(int i) => _core.IsColumnSolved(i);

	public bool IsValidCellIndex(Vector2I cell) => _core.IsValidCellIndex(new Core.Vec2I(cell.X, cell.Y));
	public bool IsCellFilled(Vector2I loc) => _core.IsCellFilled(new Core.Vec2I(loc.X, loc.Y));
	public bool IsCellMarked(Vector2I loc) => _core.IsCellMarked(new Core.Vec2I(loc.X, loc.Y));
	public bool MarkCell(Vector2I loc) => _core.MarkCell(new Core.Vec2I(loc.X, loc.Y));
	public bool UnmarkCell(int x, int y) => _core.UnmarkCell(x, y);
	public bool ToggleCell(int x, int y) => _core.ToggleCell(x, y);

	public Godot.Collections.Array<Clue> GetRowClues(int index) => Wrap(_core.GetRowClues(index));
	public Godot.Collections.Array<Clue> GetColClues(int index) => Wrap(_core.GetColClues(index));

	private static Godot.Collections.Array<Clue> Wrap(IReadOnlyList<Core.Clue> clues)
	{
		var result = new Godot.Collections.Array<Clue>();
		for (int i = 0; i < clues.Count; i++)
			result.Add(new Clue(clues[i]));
		return result;
	}
}
