# TODO
- [x] combine row/col solver code
- on full fill:
	- [x] mark cells where we can
- [x] check if the row/column has been solved before trying to solve it
- [x] fix "solved" checking to include columns
- [x] if solved, mark the other cells
- [x] change solution representation to just have numbers, no symbols
- [x] check for marked cells at the start and end of the row/column,
		adjust the fill/partial-fill checks and solving to account for them
- [x] change sol'n rep to match data set (just 1/0 in a grid)
- [x] add representation for clues so we can track if individual clues have been solved
- [x] change rendering of individual clue if solved
- [x] change fill/partial fill to use [Simple Boxes](https://en.wikipedia.org/wiki/Nonogram#Simple_boxes) check
- [x] detect and mark start/end cells that are just passed the closest clue
- [x] show percentage solved (full puzzle)
- [x] check for "completed clue" while glueing

# CLEANUP
- [ ] refactor `get_array_bounds` to share code

# MAYBE
- [ ] make `is_row_solved` more sophisticated

# OPTIMIZE
- [ ] solver.partial_fill may be able to be simplified
- [ ] stack of "lines to solve" instead of just looping through all
- [ ] refactor `get_array_bounds` to use single loop
