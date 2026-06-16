public partial class Solver
{
    internal sealed class LineSolver
    {
        private readonly int _size;
        private readonly uint _filled;
        private readonly uint _marked;
        private readonly Clue[] _clues;
        public LineSolver(int inSize, uint inFilledCells, uint inMarkedCells, Clue[] inClues)
        {
            _size = inSize;
            _filled = inFilledCells;
            _marked = inMarkedCells;
            _clues = inClues;
        }

        public int[] LeftFill()
        {
            uint nClues = (uint)_clues.Length;
            int[] starts = new int[nClues];

            // skip over solved clues for the start of the line, as we don't
            // want to double-fill them.
            uint i = 0;
            while (i < nClues && _clues[i++].IsSolved()) { }

            for (; i < nClues; i++)
            {
                int clue = _clues[i].Value;
                int pos = 0;
                while(!ClueFits(pos, clue))
                {
                    // if we've reached the end of the line, no further clues can be filled in
                    if (pos >= _size)
                    {
                        starts[i] = -1;
                        return starts;
                    }
                    pos++;
                }

                starts[i] = pos;
                pos += clue + 1;
            }

            return starts;
        }

        public int[] RightFill()
        {
            uint nClues = (uint)_clues.Length;
            int[] starts = new int[nClues];

            return starts;
        }

        private bool ClueFits(int start, int clue)
        {
            if (start < 0 || start + clue >= _size)
                return false;
            // if there are any marked cells in the clue's range, it can't fit there.
            if ((_marked & BitOps.FieldMask(clue, start)) != 0)
                return false;
            // if either of the cells that would bookend the clue are filled,
            // then the clue would collide with them and wouldn't fit
            if (IsFilled(start - 1) || IsFilled(start + clue))
                return false;
            return true;
        }

        private bool IsFilled(int cell) => cell >= 0 && cell < _size && (_filled & (1u << cell)) != 0;
        private bool IsMarked(int cell) => cell >= 0 && cell < _size && (_marked & (1u << cell)) != 0;
    }
}