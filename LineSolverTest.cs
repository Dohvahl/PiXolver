using Godot;
using System.Text;

/// <summary>
/// THROWAWAY harness for the experimental <see cref="Solver.DPLineSolver"/>. Run this scene (select
/// line_solver_test.tscn and press F6) to print each hand-built line and its deductions.
/// Bit 0 is the leftmost cell, matching the solver's <c>1u &lt;&lt; pos</c> convention, so the binary
/// literals below read right-to-left relative to the rendered lines.
/// Delete this file + line_solver_test.tscn (+ .uid) when you're done poking at it.
/// </summary>
public partial class LineSolverTest : Node
{
	public override void _Ready()
	{
		int passed = 0;
		int total = 0;

		//							name,               size, clues,       filled,      marked,    expectedFill, expectedEmpty
		total++; passed += RunCase("simple boxes",      5, new[] { 3 },    0b00000,     0,         0b00100,      0b00000) ? 1 : 0;
		total++; passed += RunCase("01011 join/split",  7, new[] { 2, 3 }, 0b0011010,   0,         0b0111011,    0b1000100) ? 1 : 0;
		total++; passed += RunCase("mid-line complete", 7, new[] { 2 },    0b0001100,   0,         0b0001100,    0b1110011) ? 1 : 0;
		total++; passed += RunCase("blocked by X",      5, new[] { 2 },    0b00000,     0b00100,   0b00000,      0b00000) ? 1 : 0;
		total++; passed += RunCase("pinned at edge",    6, new[] { 3 },    0b000001,    0,         0b000111,     0b111000) ? 1 : 0;

		GD.Print($"\nLineSolver tests: {passed}/{total} passed");
		GetTree().Quit();
	}

	private static bool RunCase(string name, int size, int[] clues, uint filled, uint marked, uint expFill, uint expEmpty)
	{
		var line = new Solver.DPLineSolver(size, filled, marked, clues);
		int[] left = line.Leftmost();
		int[] right = line.Rightmost();
		line.Deduce(out uint fill, out uint empty);

		bool pass = fill == expFill && empty == expEmpty;

		GD.Print($"[{(pass ? "PASS" : "FAIL")}] {name}  clues=[{string.Join(",", clues)}]");
		GD.Print($"        line   {Render(size, filled, marked)}");
		GD.Print($"        left={Fmt(left)}  right={Fmt(right)}");
		GD.Print($"        fill   {Bits(size, fill)}   (want {Bits(size, expFill)})");
		GD.Print($"        empty  {Bits(size, empty)}   (want {Bits(size, expEmpty)})");
		return pass;
	}

	// '#' filled, 'x' marked-empty, '.' unknown — drawn left (cell 0) to right.
	private static string Render(int size, uint filled, uint marked)
	{
		var sb = new StringBuilder(size);
		for (int i = 0; i < size; i++)
		{
			if ((filled & (1u << i)) != 0) sb.Append('#');
			else if ((marked & (1u << i)) != 0) sb.Append('x');
			else sb.Append('.');
		}
		return sb.ToString();
	}

	private static string Bits(int size, uint mask)
	{
		var sb = new StringBuilder(size);
		for (int i = 0; i < size; i++)
			sb.Append((mask & (1u << i)) != 0 ? '#' : '.');
		return sb.ToString();
	}

	private static string Fmt(int[] arr) => "[" + string.Join(",", arr) + "]";
}
