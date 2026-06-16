public partial class Solver
{
	/// <summary>
	/// A complete, reusable single-line solver. Construct it once per line with a fixed grid size,
	/// then call <see cref="Configure"/> before each <see cref="Deduce"/>: it re-points the solver at
	/// a new line and clears its memo in place, so steady-state solving allocates nothing.
	///
	/// Given a line (filled/marked bitmasks) and its clue lengths it computes, for every clue, the
	/// earliest (leftmost) and latest (rightmost) start across all arrangements that (a) avoid marked
	/// cells, (b) keep the required gaps, and (c) cover every filled cell. Every forced cell follows
	/// from those two placements — see <see cref="Deduce"/>. O(clues × size) thanks to memoisation.
	/// </summary>
	internal sealed class DPLineSolver
	{
		private readonly int _size;

		// Reusable scratch, sized to the worst case at construction and never reallocated.
		private readonly int[] _clues;        // configured clue lengths (_count valid)
		private readonly int[] _reversed;      // reversed clue lengths (rightmost pass)
		private readonly int[] _leftStarts;
		private readonly int[] _rightStarts;
		private readonly int[] _mirrorStarts;
		private readonly sbyte[,] _feasible;   // memo: -1 unknown, 0 no, 1 yes; indexed [clue, cell]

		// The configured line.
		private uint _filled;
		private uint _marked;
		private int _count;

		// Inputs the DP is currently running against (the forward line, then the mirrored line).
		private uint _activeFilled;
		private uint _activeMarked;
		private int[] _activeClues;

		public DPLineSolver(int size)
		{
			_size = size;
			int maxClues = (size + 1) / 2; // a line of N cells holds at most ceil(N/2) clues
			_clues = new int[maxClues];
			_reversed = new int[maxClues];
			_leftStarts = new int[maxClues];
			_rightStarts = new int[maxClues];
			_mirrorStarts = new int[maxClues];
			_feasible = new sbyte[maxClues + 1, size + 1];
		}

		/// <summary>Point the solver at a new line. Allocation-free.</summary>
		public void Configure(uint filled, uint marked, Godot.Collections.Array<Clue> clues)
		{
			_filled = filled;
			_marked = marked;
			_count = clues.Count;
			for (int i = 0; i < _count; i++)
				_clues[i] = clues[i].Value;
		}

		/// <summary>
		/// forcedFilled = cells covered by a clue in BOTH extreme placements (the per-clue overlap);
		/// forcedEmpty = cells covered by NO clue in either extreme. Mid-line completion and the
		/// join/split boundary marks fall out of forcedEmpty automatically.
		/// solvedClues = bit i set when clue i is pinned to a single position (leftmost == rightmost),
		/// i.e. it can only sit in one place and so is fully determined.
		/// </summary>
		public void Deduce(out uint forcedFilled, out uint forcedEmpty, out uint solvedClues)
		{
			ComputeLeftmost();
			ComputeRightmost();

			forcedFilled = 0;
			solvedClues = 0;
			uint covered = 0; // every cell a clue could possibly occupy
			for (int i = 0; i < _count; i++)
			{
				if (_leftStarts[i] < 0 || _rightStarts[i] < 0)
				{
					// unsatisfiable line (shouldn't happen for a valid puzzle); deduce nothing
					forcedFilled = 0;
					forcedEmpty = 0;
					solvedClues = 0;
					return;
				}

				int len = _clues[i];
				if (_rightStarts[i] < _leftStarts[i] + len)
					forcedFilled |= RangeMask(_rightStarts[i], _leftStarts[i] + len);
				covered |= RangeMask(_leftStarts[i], _rightStarts[i] + len);

				// a clue with a single possible position is fully determined
				if (_leftStarts[i] == _rightStarts[i])
					solvedClues |= 1u << i;
			}

			forcedEmpty = RangeMask(0, _size) & ~covered;
		}

		private void ComputeLeftmost()
		{
			SetActive(_filled, _marked, _clues);
			Solve(_leftStarts);
		}

		private void ComputeRightmost()
		{
			for (int i = 0; i < _count; i++)
				_reversed[i] = _clues[_count - 1 - i];

			SetActive(Reverse(_filled), Reverse(_marked), _reversed);
			Solve(_mirrorStarts);

			for (int i = 0; i < _count; i++)
			{
				int mirrored = _mirrorStarts[_count - 1 - i];
				_rightStarts[i] = mirrored < 0 ? -1 : _size - mirrored - _clues[i];
			}
		}

		private void SetActive(uint filled, uint marked, int[] clues)
		{
			_activeFilled = filled;
			_activeMarked = marked;
			_activeClues = clues;
			for (int i = 0; i <= _count; i++)
				for (int p = 0; p <= _size; p++)
					_feasible[i, p] = -1;
		}

		/// <summary>Leftmost reconstruction over the active line; writes a start per clue (-1 if unsatisfiable).</summary>
		private void Solve(int[] starts)
		{
			int cursor = 0;
			for (int i = 0; i < _count; i++)
			{
				int len = _activeClues[i];
				int p = cursor;
				while (!(Fits(p, len) && Feasible(i + 1, p + len + 1)))
				{
					if (FilledAt(p) || p >= _size) // can't skip a filled cell, or ran off the end
					{
						for (int j = i; j < _count; j++)
							starts[j] = -1;
						return;
					}
					p += 1;
				}
				starts[i] = p;
				cursor = p + len + 1;
			}
		}

		/// <summary>Are clues[i..] placeable within cells [p, size), covering all filled cells there?</summary>
		private bool Feasible(int i, int p)
		{
			if (i == _count)
				return (_activeFilled & RangeMask(p, _size)) == 0; // no clues left → nothing may be filled
			if (p > _size)
				return false;
			if (_feasible[i, p] != -1)
				return _feasible[i, p] == 1;

			bool result = false;
			if (p < _size && !FilledAt(p))                 // option A: leave cell p empty
				result = Feasible(i, p + 1);
			if (!result && Fits(p, _activeClues[i]))       // option B: start clue i at p, gap, then the rest
				result = Feasible(i + 1, p + _activeClues[i] + 1);

			_feasible[i, p] = (sbyte)(result ? 1 : 0);
			return result;
		}

		/// <summary>Can a clue of <paramref name="len"/> sit exactly at [start, start+len)?</summary>
		private bool Fits(int start, int len)
		{
			if (start < 0 || start + len > _size)
				return false;
			if ((_activeMarked & BitOps.FieldMask(len, start)) != 0)
				return false;                  // no X inside the block
			if (FilledAt(start - 1))
				return false;                  // would merge with a filled cell on the left
			if (FilledAt(start + len))
				return false;                  // ...or on the right (block would be too long)
			return true;
		}

		private bool FilledAt(int cell) => cell >= 0 && cell < _size && (_activeFilled & (1u << cell)) != 0;

		/// <summary>Bits set for the half-open cell range [start, end).</summary>
		private static uint RangeMask(int start, int end) => end <= start ? 0u : BitOps.FieldMask(end - start, start);

		/// <summary>Reverse the low <see cref="_size"/> bits of a line bitmask.</summary>
		private uint Reverse(uint mask) => BitOps.ReverseBits(mask) >> (32 - _size);
	}
}
