using System.Numerics;

namespace PiXolver.Core;

/// <summary>
/// Bit-twiddling helpers for the puzzle's row/column bitmasks. Values are unsigned 32-bit
/// (<see cref="uint"/>): a grid line is at most 32 cells, and unsigned types avoid the
/// sign-extension surprises that signed shifts/masks would introduce.
/// </summary>
internal static class BitOps
{
	/// <summary>Returns n set bits, starting at offset o.</summary>
	public static uint FieldMask(int n, int o = 0)
	{
		return ((1u << n) - 1) << o;
	}

	/// <summary>Number of set bits ("population count").</summary>
	public static int PopCount(uint x)
	{
		return BitOperations.PopCount(x);
	}

	/// <summary>Count trailing zeros = index of the lowest set bit. Returns -1 if x == 0.</summary>
	public static int Ctz(uint x)
	{
		return x == 0 ? -1 : BitOperations.TrailingZeroCount(x);
	}

	/// <summary>Returns the index of the lowest set bit in x, or -1 if none are set.</summary>
	public static int IndexOfLowestSet(uint x)
	{
		return x != 0 ? Ctz(x) : -1;
	}

	/// <summary>Returns the index of the highest set bit, i.e. floor(log2 x). Returns -1 if x == 0.</summary>
	public static int IndexOfHighestSet(uint x)
	{
		return x == 0 ? -1 : 31 - BitOperations.LeadingZeroCount(x);
	}

	/// <summary>Returns the index of the first set bit in x, starting at offset o, up to max bit w.</summary>
	public static int FirstSet(uint x, int o, int w)
	{
		uint valueMask = FieldMask(w);
		uint window = (x >> o) & valueMask;
		int index = IndexOfLowestSet(window);
		return index < 0 ? -1 : o + index;
	}

	/// <summary>Returns the index of the last set bit in x, starting at offset o, up to max bit w.</summary>
	public static int LastSet(uint x, int o, int w)
	{
		uint valueMask = FieldMask(w);
		uint window = (x >> o) & valueMask;
		int index = IndexOfHighestSet(window);
		return index < 0 ? -1 : o + index;
	}

	/// <summary>Reverses all 32 bits of x.</summary>
	public static uint ReverseBits(uint x)
	{
		uint result = ((x >> 1) & 0x55555555) | ((x & 0x55555555) << 1);
		result = ((result >> 2) & 0x33333333) | ((result & 0x33333333) << 2);
		result = ((result >> 4) & 0x0F0F0F0F) | ((result & 0x0F0F0F0F) << 4);
		result = ((result >> 8) & 0x00FF00FF) | ((result & 0x00FF00FF) << 8);
		return (result >> 16) | (result << 16);
	}
}
