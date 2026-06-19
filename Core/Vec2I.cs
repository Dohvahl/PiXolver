namespace PiXolver.Core;

/// <summary>
/// A plain 2D integer vector used for cell coordinates and line directions. Mirrors the small slice
/// of Godot's <c>Vector2I</c> the core needs, but carries no Godot dependency so the core stays
/// portable. The Godot layer converts to/from <c>Vector2I</c> at the boundary.
/// </summary>
public readonly struct Vec2I : System.IEquatable<Vec2I>
{
	public readonly int X;
	public readonly int Y;

	public Vec2I(int x, int y)
	{
		X = x;
		Y = y;
	}

	// Direction constants: Right runs along a row (+x), Down runs along a column (+y).
	public static readonly Vec2I Right = new(1, 0);
	public static readonly Vec2I Down = new(0, 1);

	public bool Equals(Vec2I other) => X == other.X && Y == other.Y;
	public override bool Equals(object obj) => obj is Vec2I other && Equals(other);
	public override int GetHashCode() => (X * 397) ^ Y;

	public static bool operator ==(Vec2I a, Vec2I b) => a.Equals(b);
	public static bool operator !=(Vec2I a, Vec2I b) => !a.Equals(b);

	public override string ToString() => $"({X}, {Y})";
}
