public partial class Solver
{
    /// <summary>
    /// EXPERIMENTAL — not yet wired into the solver.
    ///
    /// A complete single-line solver. Given a line (as filled/marked bitmasks) and its clue
    /// lengths, it computes, for every clue, the earliest (leftmost) and latest (rightmost) start
    /// position it can occupy across all arrangements that (a) avoid marked cells, (b) keep the
    /// required gaps, and (c) cover every filled cell. From those two arrangements every forced
    /// cell follows — including Simple Boxes, Glue, Mercury, Forcing Spaces, Joining/Splitting,
    /// and mid-line clue completion — see <see cref="Deduce"/>.
    ///
    /// The DP <see cref="Feasible"/>(i, p) answers "can clues[i..] be placed within cells [p, size)
    /// while covering all filled cells there?" Leftmost reconstruction then walks each clue to the
    /// earliest spot that keeps the remainder feasible; rightmost reuses leftmost on the mirrored line.
    /// O(clues × size) thanks to memoisation — trivial for a 30-cell line.
    /// </summary>
    internal sealed class DPLineSolver
	{
		private readonly int _size;
		private readonly uint _filled;
		private readonly uint _marked;
		private readonly int[] _lens;
		private readonly int[,] _feasible; // memo: -1 unknown, 0 no, 1 yes; indexed [clue, cell]

		public DPLineSolver(int size, uint filled, uint marked, int[] clueLengths)
		{
			_size = size;
			_filled = filled;
			_marked = marked;
			_lens = clueLengths;
			if (size+1 < 0 || clueLengths.Length + 1 < 0)
			{
				size = 30;
			}
            _feasible = new int[clueLengths.Length + 1, size + 1];
			for (int i = 0; i <= clueLengths.Length; i++)
				for (int p = 0; p <= size; p++)
					_feasible[i, p] = -1;
		}

		private bool FilledAt(int cell) => cell >= 0 && cell < _size && (_filled & (1u << cell)) != 0;

		/// <summary>Can a clue of <paramref name="len"/> sit exactly at [start, start+len)?</summary>
		private bool Fits(int start, int len)
		{
			if (start < 0 || start + len > _size)
				return false;
			if ((_marked & BitOps.FieldMask(len, start)) != 0)
				return false;                       // no X inside the block
			if (FilledAt(start - 1))
				return false;                       // would merge with a filled cell on the left
			if (FilledAt(start + len))
				return false;                       // ...or on the right (block would be too long)
			return true;
		}

		/// <summary>Are clues[i..] placeable within cells [p, size), covering all filled cells there?</summary>
		private bool Feasible(int i, int p)
		{
			if (i == _lens.Length)
				return (_filled & RangeMask(p, _size)) == 0; // no clues left → nothing may be filled
			if (p > _size)
				return false;
			if (_feasible[i, p] != -1)
				return _feasible[i, p] == 1;

			bool result = false;
			if (p < _size && !FilledAt(p))           // option A: leave cell p empty
				result = Feasible(i, p + 1);
			if (!result && Fits(p, _lens[i]))        // option B: start clue i at p, gap, then the rest
				result = Feasible(i + 1, p + _lens[i] + 1);

			_feasible[i, p] = (sbyte)(result ? 1 : 0);
			return result;
		}

		/// <summary>Earliest start of each clue (entry is -1 if the line is unsatisfiable).</summary>
		public int[] Leftmost()
		{
			var starts = new int[_lens.Length];
			int cursor = 0;
			for (int i = 0; i < _lens.Length; i++)
			{
				int p = cursor;
				while (!(Fits(p, _lens[i]) && Feasible(i + 1, p + _lens[i] + 1)))
				{
					if (FilledAt(p) || p >= _size)   // can't skip a filled cell, or ran off the end
					{
						starts[i] = -1;
						return starts;
					}
					p += 1;
				}
				starts[i] = p;
				cursor = p + _lens[i] + 1;
			}
			return starts;
		}

		/// <summary>Latest start of each clue — leftmost computed on the mirrored line.</summary>
		public int[] Rightmost()
		{
			int k = _lens.Length;
			var reversedLens = new int[k];
			for (int i = 0; i < k; i++)
				reversedLens[i] = _lens[k - 1 - i];

			int[] mirroredLeft = new DPLineSolver(_size, Reverse(_filled), Reverse(_marked), reversedLens).Leftmost();

			var starts = new int[k];
			for (int i = 0; i < k; i++)
			{
				int mirrored = mirroredLeft[k - 1 - i];
				starts[i] = mirrored < 0 ? -1 : _size - mirrored - _lens[i];
			}
			return starts;
		}

		/// <summary>
		/// Turn the leftmost/rightmost placements into forced cells:
		/// <paramref name="forcedFilled"/> = cells covered by a clue in BOTH extremes (the per-clue
		/// overlap), <paramref name="forcedEmpty"/> = cells covered by NO clue in either extreme.
		/// The clue-completion / split marks fall out of <paramref name="forcedEmpty"/> automatically.
		/// </summary>
		public void Deduce(out uint forcedFilled, out uint forcedEmpty)
		{
			int[] left = Leftmost();
			int[] right = Rightmost();

			forcedFilled = 0;
			uint covered = 0; // any cell a clue could possibly occupy
			for (int i = 0; i < _lens.Length; i++)
			{
				if (left[i] < 0 || right[i] < 0)
				{
					forcedFilled = 0;
					forcedEmpty = 0;
					return; // unsatisfiable line; nothing to deduce
				}

				int len = _lens[i];
				// overlap of the two extreme placements of clue i
				if (right[i] < left[i] + len)
					forcedFilled |= RangeMask(right[i], left[i] + len);
				// everywhere this clue could sit
				covered |= RangeMask(left[i], right[i] + len);
			}

			forcedEmpty = RangeMask(0, _size) & ~covered;
		}

		/// <summary>Bits set for the half-open cell range [start, end).</summary>
		private static uint RangeMask(int start, int end) => end <= start ? 0u : BitOps.FieldMask(end - start, start);

		/// <summary>Reverse the low <see cref="_size"/> bits of a line bitmask.</summary>
		private uint Reverse(uint mask) => BitOps.ReverseBits(mask) >> (32 - _size);
	}
}
