/** The shape of the palette the arrow keys move through: the families' rows of swatches, then the tall ones beside them. */
export type PaletteShape = {
  /** How many swatches are in a row (the families). */
  columns: number;
  /** How many rows there are (the weights shown). */
  rows: number;
  /** How many tall swatches there are in the column at the right (black and white). */
  extremes: number;
};

/** The place a key press goes when it goes to "No color", which is not a swatch. */
export const NO_COLOR = -1;

/** The row a move right into a tall swatch started from, which a move straight back left returns to. */
export type EnteredFrom = { extreme: number; row: number } | null;

/**
 * Where an arrow key (or Home or End) goes from the swatch at `at`, or null for nowhere. The swatches are numbered in
 * reading order: the families' rows left to right, top to bottom, and then each tall swatch, which sits beside
 * `rows / extremes` rows. A move that goes nowhere is null, and one that goes to "No color" is `NO_COLOR`, which only a
 * palette that has it (`allowNone`) can do: up from the top row, or from the top tall swatch.
 *
 * Going right into a tall swatch and straight back left returns to the row the move started from (`enteredFrom`), so a
 * sideways move undoes itself. A tall swatch has no row of its own otherwise, so left from it goes to the middle of the
 * rows beside it.
 */
export function neighbor(
  at: number,
  key: string,
  shape: PaletteShape,
  enteredFrom: EnteredFrom,
  allowNone: boolean,
): number | null {
  const { columns, rows, extremes } = shape;
  const cells = columns * rows;
  const perExtreme = rows / extremes;
  const up = allowNone ? NO_COLOR : null;
  if (key === 'Home') return 0;
  if (key === 'End') return cells - 1;

  if (at >= cells) {
    const extreme = at - cells;
    if (key === 'ArrowLeft') {
      const row =
        enteredFrom?.extreme === extreme ? enteredFrom.row : extreme * perExtreme + Math.floor(perExtreme / 2);
      return row * columns + columns - 1;
    }
    if (key === 'ArrowUp') return extreme > 0 ? at - 1 : up;
    if (key === 'ArrowDown') return extreme < extremes - 1 ? at + 1 : null;
    return null;
  }

  const row = Math.floor(at / columns);
  const column = at % columns;
  if (key === 'ArrowLeft') return column > 0 ? at - 1 : null;
  if (key === 'ArrowRight') return column < columns - 1 ? at + 1 : cells + Math.floor(row / perExtreme);
  if (key === 'ArrowUp') return row > 0 ? at - columns : up;
  if (key === 'ArrowDown') return row < rows - 1 ? at + columns : null;
  return null;
}
