extends CellArray
class_name SolutionCellArray

class Clue:
	var index : int				# index of the clue in the clues array
	var value : int				# the clue value
	var starting_cell : int		# first index of the clue in thee row/column

	var solved : bool			# whether or not this clue has been filled in
