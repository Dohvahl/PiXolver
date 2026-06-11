using System.Numerics;

/// <summary>
/// Bit-twiddling helpers for the puzzle's row/column bitmasks. All values are treated as
/// 64-bit signed integers to mirror GDScript's native <c>int</c> width.
/// </summary>
internal static class BitOps
{
	/// <summary>Returns n set bits, starting at offset o.</summary>
	public static long FieldMask(int n, int o = 0)
	{
		return ((1L << n) - 1) << o;
	}

	/// <summary>Number of set bits ("population count").</summary>
	public static int PopCount(long x)
	{
		return BitOperations.PopCount((ulong)x);
	}

	/// <summary>Count trailing zeros = index of the lowest set bit. Returns -1 if x == 0.</summary>
	public static int Ctz(long x)
	{
		return x == 0 ? -1 : BitOperations.TrailingZeroCount((ulong)x);
	}

	/// <summary>Returns the index of the lowest set bit in x, or -1 if none are set.</summary>
	public static int IndexOfLowestSet(long x)
	{
		return x != 0 ? Ctz(x) : -1;
	}

	/// <summary>Returns the index of the highest set bit, i.e. floor(log2 x). Returns -1 if x == 0.</summary>
	public static int IndexOfHighestSet(long x)
	{
		return x == 0 ? -1 : 63 - BitOperations.LeadingZeroCount((ulong)x);
	}

	/// <summary>Returns the index of the first set bit in x, starting at offset o, up to max bit w.</summary>
	public static int FirstSet(long x, int o, int w)
	{
		long valueMask = FieldMask(w);
		long window = (x >> o) & valueMask;
		int index = IndexOfLowestSet(window);
		return index < 0 ? -1 : o + index;
	}

	/// <summary>Returns the index of the last set bit in x, starting at offset o, up to max bit w.</summary>
	public static int LastSet(long x, int o, int w)
	{
		long valueMask = FieldMask(w);
		long window = (x >> o) & valueMask;
		int index = IndexOfHighestSet(window);
		return index < 0 ? -1 : o + index;
	}

	/// <summary>Reverses the low 32 bits of x.</summary>
	public static long ReverseBits(long value)
	{
		uint x = (uint)value;
		uint result = ((x >> 1) & 0x55555555) | ((x & 0x55555555) << 1);
		result = ((result >> 2) & 0x33333333) | ((result & 0x33333333) << 2);
		result = ((result >> 4) & 0x0F0F0F0F) | ((result & 0x0F0F0F0F) << 4);
		result = ((result >> 8) & 0x00FF00FF) | ((result & 0x00FF00FF) << 8);
		return (result >> 16) | (result << 16);
	}
}
