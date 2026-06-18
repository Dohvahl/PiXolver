public partial class Solver
{
	/// <summary>
	/// A complete, reusable single-line solver. Construct it once per line with a fixed grid size,
	/// then call <see cref="Configure"/> before each <see cref="Deduce"/>: it re-points the solver at
	/// a new line and invalidates its memo via a generation stamp, so steady-state solving allocates nothing.
	///
	/// Given a line (filled/marked bitmasks) and its clue lengths it computes, for every clue, the
	/// earliest (leftmost) and latest (rightmost) start across all arrangements that (a) avoid marked
	/// cells, (b) keep the required gaps, and (c) cover every filled cell. Every forced cell follows
	/// from those two placements — see <see cref="Deduce"/>. O(clues × size) thanks to memoisation.
	/// </summary>
	internal sealed class DPLineSolver
	{
		// A/B toggle used by FullSolve's benchmark to compare with / without solved-clue trimming.
		internal static bool TrimSolvedClues = true;

		private readonly int _capacity; // max line length; fixes the buffer sizes

		// The active window the DP runs over: the line minus any trimmed solved end-clues.
		private int _size;              // active window length
		private int _winStart;          // window start, in full-line coordinates
		private int _clueOffset;        // index of the first active clue in the configured list

		// Reusable scratch, sized to the worst case at construction and never reallocated.
		private readonly int[] _clues;        // configured clue lengths (_count valid)
		// Clue-list cache: the Clue references and their (immutable) values are resolved across the
		// Godot↔C# boundary once per line; only the mutable solved-flags are re-read each Configure.
		// _cachedClues guards the cache (the clue list for a line is the same object every call).
		private Godot.Collections.Array<Clue> _cachedClues;
		private int _cachedCount;
		private readonly Clue[] _clueRefs;    // cached Clue references for the current line
		private readonly int[] _cfgValues;    // cached clue values (immutable; refreshed on cache miss)
		private readonly bool[] _cfgSolved;   // clue solved-flags, refreshed each Configure call
		private readonly int[] _reversedClues;      // reversed clue lengths (rightmost pass)
		private readonly int[] _leftStarts;
		private readonly int[] _rightStarts;
		private readonly int[] _mirrorStarts;
		
		// Feasibility memos, indexed [clue, cell]. Each cell packs the generation it was written in with
		// the boolean result: (generation << 1) | (result ? 1 : 0). A cell is valid for the current pass
		// iff (cell >> 1) == _generation, so bumping _generation in SetActive invalidates the whole memo
		// in O(1) without clearing it. 0 (the default) is never a valid stamp since generations start at 1.
		private readonly int[,] _suffixFeasible;
		private readonly int[,] _prefixFeasible;
		private int _generation;

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
			_capacity = size;
			_size = size;
			int maxClues = (size + 1) / 2; // a line of N cells holds at most ceil(N/2) clues
			_clues = new int[maxClues];
			_clueRefs = new Clue[maxClues];
			_cfgValues = new int[maxClues];
			_cfgSolved = new bool[maxClues];
			_reversedClues = new int[maxClues];
			_leftStarts = new int[maxClues];
			_rightStarts = new int[maxClues];
			_mirrorStarts = new int[maxClues];
			_suffixFeasible = new int[maxClues + 1, size + 1];
			_prefixFeasible = new int[maxClues + 1, size + 1];
		}

		/// <summary>Point the solver at a new line. Allocation-free.</summary>
		public void Configure(uint filled, uint marked, Godot.Collections.Array<Clue> clues)
		{
			// Resolve the Clue references and their (immutable) values once per line and cache them — the
			// clue list for a line is the same object every call. Only the mutable solved-flags are
			// re-read each call. The reference check refreshes the cache if the line ever changes.
			if (!ReferenceEquals(clues, _cachedClues))
			{
				_cachedClues = clues;
				_cachedCount = clues.Count;
				for (int i = 0; i < _cachedCount; i++)
				{
					Clue clue = clues[i];
					_clueRefs[i] = clue;
					_cfgValues[i] = clue.Value;
				}
			}

			int count = _cachedCount;
			for (int i = 0; i < count; i++)
				_cfgSolved[i] = _clueRefs[i].IsSolved();

			int lo = 0;
			int windowStart = 0;
			int hi = count - 1;
			int windowEnd = _capacity; // exclusive

			// Trim contiguous solved clues off each end. A solved clue is pinned and filled, so the
			// leading/trailing solved clues occupy the extremes; drop them (and their cells) and run
			// the DP only over the ambiguous middle. Mid-line solved clues are left in — removing
			// those would split the line, which this windowed form can't represent.
			if (TrimSolvedClues)
			{
				while (lo < count && _cfgSolved[lo])
				{
					int runStart = BitOps.FirstSet(filled, windowStart, _capacity - windowStart);
					if (runStart < 0)
						break; // a solved clue must have a filled run; bail rather than mis-trim
					windowStart = runStart + _cfgValues[lo];
					lo++;
				}

				while (hi >= lo && _cfgSolved[hi])
				{
					int runEnd = BitOps.LastSet(filled, windowStart, windowEnd - windowStart);
					if (runEnd < 0)
						break;
					windowEnd = runEnd - _cfgValues[hi] + 1;
					hi--;
				}
			}

			_winStart = windowStart;
			_clueOffset = lo;
			_size = windowEnd > windowStart ? windowEnd - windowStart : 0;
			_count = hi >= lo ? hi - lo + 1 : 0;

			for (int i = 0; i < _count; i++)
				_clues[i] = _cfgValues[lo + i];

			uint windowMask = _size > 0 ? BitOps.FieldMask(_size) : 0u;
			_filled = (filled >> windowStart) & windowMask;
			_marked = (marked >> windowStart) & windowMask;
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

			uint filledBits = 0;
			uint solvedBits = 0;
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
					filledBits |= RangeMask(_rightStarts[i], _leftStarts[i] + len);
				covered |= RangeMask(_leftStarts[i], _rightStarts[i] + len);

				// a clue with a single possible position is fully determined
				if (_leftStarts[i] == _rightStarts[i])
					solvedBits |= 1u << i;
			}

			uint emptyBits = RangeMask(0, _size) & ~covered;

			// shift the window-local results back into full-line / full-clue-list coordinates
			forcedFilled = filledBits << _winStart;
			forcedEmpty = emptyBits << _winStart;
			solvedClues = solvedBits << _clueOffset;
		}

        public void DeduceComplete(out uint forcedFilled, out uint forcedEmpty, out uint solvedClues)
        {
            SetActive(_filled, _marked, _clues);   // forward line; bumps the generation to reset both memos

            // everFilled: cells some feasible placement covers. Also count starts per clue.
            uint everFilled = 0;
            solvedClues = 0;
            for (int i = 0; i < _count; i++)
            {
                int len = _clues[i];
                int feasibleStarts = 0;
                for (int s = 0; s + len <= _size; s++)
                {
                    if (Fits(s, len) && PrefixFeasible(i, s - 1) && SuffixFeasible(i + 1, s + len + 1))
                    {
                        everFilled |= RangeMask(s, s + len);
                        feasibleStarts++;
                    }
                }
                if (feasibleStarts == 1)
                    solvedClues |= 1u << i;   // exactly one place to go → pinned
            }

            // everEmpty: cells that can be a gap in some valid arrangement (split before clue i, cell p empty)
            uint everEmpty = 0;
            for (int p = 0; p < _size; p++)
            {
                if (FilledAt(p))
                    continue; // a filled cell is never empty
                for (int i = 0; i <= _count; i++)
                {
                    if (PrefixFeasible(i, p) && SuffixFeasible(i, p + 1))
                    {
                        everEmpty |= 1u << p;
                        break;
                    }
                }
            }

            // shift the window-local results back into full-line / full-clue-list coordinates
            forcedFilled = (everFilled & ~everEmpty) << _winStart;            // filled in some arrangement, empty in none → in all
            forcedEmpty = (RangeMask(0, _size) & ~everFilled) << _winStart;   // no clue ever covers it → empty in all
            solvedClues <<= _clueOffset;
        }
        
		private void ComputeLeftmost()
		{
			SetActive(_filled, _marked, _clues);
			Solve(_leftStarts);
		}

		private void ComputeRightmost()
		{
			for (int i = 0; i < _count; i++)
				_reversedClues[i] = _clues[_count - 1 - i];

			SetActive(Reverse(_filled), Reverse(_marked), _reversedClues);
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
			// invalidate both memos in O(1) by moving to a new generation (no clearing)
			_generation++;
		}

		/// <summary>Leftmost reconstruction over the active line; writes a start per clue (-1 if unsatisfiable).</summary>
		private void Solve(int[] starts)
		{
			int cursor = 0;
			for (int i = 0; i < _count; i++)
			{
				int len = _activeClues[i];
				int p = cursor;
				while (!(Fits(p, len) && SuffixFeasible(i + 1, p + len + 1)))
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

        /// <summary>Are clue[..i] placeable within cells [0,p), covering all filled cells there?</summary>
        private bool PrefixFeasible(int i, int p)
        {
            if (i == 0)
                return (_activeFilled & RangeMask(0, p)) == 0;
            if (p < 0)
                return false;
            int memo = _prefixFeasible[i, p];
            if ((memo >> 1) == _generation)
                return (memo & 1) != 0;

            int clue = _activeClues[i - 1];   // the last of the first i clues
            bool result = false;

            // option A: leave the last cell of [0,p), which is p-1, empty
            if (!FilledAt(p - 1))
                result = PrefixFeasible(i, p - 1);

            // option B: clue i-1 occupies [p-clue, p) (ends at p-1); gap sits at p-clue-1
            if (!result && Fits(p - clue, clue))
                result = PrefixFeasible(i - 1, p - clue - 1);

            _prefixFeasible[i, p] = (_generation << 1) | (result ? 1 : 0);
            return result;
        }		
		
		/// <summary>Are clues[i..] placeable within cells [p, size), covering all filled cells there?</summary>
		private bool SuffixFeasible(int i, int p)
		{
			if (i == _count)
                // no clues left to check, so just verify no filled cells remain at the end of the line
                return (_activeFilled & RangeMask(p, _size)) == 0;
			if (p > _size)
				return false;
			int memo = _suffixFeasible[i, p];
			if ((memo >> 1) == _generation)
				return (memo & 1) != 0;

			bool result = false;
			if (p < _size && !FilledAt(p))                 // option A: leave cell p empty
				result = SuffixFeasible(i, p + 1);
			if (!result && Fits(p, _activeClues[i]))       // option B: start clue i at p, gap, then the rest
				result = SuffixFeasible(i + 1, p + _activeClues[i] + 1);

			_suffixFeasible[i, p] = (_generation << 1) | (result ? 1 : 0);
			return result;
		}

		/// <summary>Can a clue of <paramref name="len"/> sit exactly at [start, start+clue)?</summary>
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
