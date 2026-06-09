class_name BitOps
extends RefCounted

## Returns n set bits, starting at offset o
static func FIELD_MASK(n: int, o: int = 0) -> int:
	return ((1 << n) - 1) << o

## Number of set bits ("population count"). The standard SWAR/parallel trick:
## add bits in pairs, then nibbles, then bytes, then sum the bytes.
static func POPCOUNT(x: int) -> int:
	var result := x - ((x >> 1) & 0x55555555)
	result = (result & 0x33333333) + ((result >> 2) & 0x33333333)
	result = (result + (result >> 4)) & 0x0F0F0F0F
	return (result * 0x01010101) >> 24

## Count trailing zeros = index of the lowest set bit. Returns -1 if x == 0.
## (x & -x) isolates the lowest set bit; subtracting 1 turns it into a run of
## exactly that many 1s, so the popcount IS the trailing-zero count.
static func CTZ(x: int) -> int:
	if x == 0: return -1
	return int(POPCOUNT((x & (~x + 1)) - 1))

## Returns the index of the lowest set bit in x, or -1 if none are set
static func INDEXOF_LOWEST_SET(x: int) -> int:
	return CTZ(x) if x else -1

## Returns the index of the highest set bit, i.e. floor(log2 x). Returns -1 if x == 0.
## Smear the top bit down so every bit below it becomes 1, then count the ones.
static func INDEXOF_HIGHEST_SET(x: int) -> int:
	var result := x | (x >> 1)
	result |= result >> 2
	result |= result >> 4
	result |= result >> 8
	result |= result >> 16
	return POPCOUNT(result) - 1

## Returns the index of the first set bit in x, starting at offset o, up to max bit w
static func FIRST_SET(x: int, o: int, w: int) -> int:
	var value_mask := FIELD_MASK(w)
	var window := (x >> o) & value_mask
	var index = INDEXOF_LOWEST_SET(window)
	return -1 if index < 0 else o + index

## Returns the index of the last set bit in x, starting at offset l-o, up to max bit w
static func LAST_SET(x: int, o: int, w: int) -> int:
	var value_mask := FIELD_MASK(w) # we only want l bits
	var window := (x >> o) & value_mask
	var index = INDEXOF_HIGHEST_SET(window)
	return -1 if index < 0 else o + index

static func REVERSE_BITS(x: int) -> int:
	var result = ((x >> 1)  & 0x55555555) | ((x & 0x55555555) << 1);
	result = ((result >> 2)  & 0x33333333) | ((result & 0x33333333) << 2);
	result = ((result >> 4)  & 0x0F0F0F0F) | ((result & 0x0F0F0F0F) << 4);
	result = ((result >> 8)  & 0x00FF00FF) | ((result & 0x00FF00FF) << 8);
	return (result >> 16) | (result << 16);
