import { describe, expect, test } from 'bun:test';

import { neighbor, NO_COLOR, type PaletteShape } from './palette-keys.ts';

// Four columns and six rows, with two tall swatches (each beside three rows), numbered 0 to 23 for the cells and 24 and 25
// for the tall ones: row 0 is 0 to 3, row 1 is 4 to 7, and so on.
const shape: PaletteShape = { columns: 4, rows: 6, extremes: 2 };
const cells = 24;
const move = (at: number, key: string, options: { entered?: { extreme: number; row: number }; none?: boolean } = {}) =>
  neighbor(at, key, shape, options.entered ?? null, options.none ?? false);

describe('moving through the swatches', () => {
  test('goes one step in each direction, and nowhere past an edge', () => {
    expect(move(5, 'ArrowRight')).toBe(6);
    expect(move(5, 'ArrowLeft')).toBe(4);
    expect(move(5, 'ArrowDown')).toBe(9);
    expect(move(5, 'ArrowUp')).toBe(1);
    expect(move(4, 'ArrowLeft')).toBeNull();
    expect(move(20, 'ArrowDown')).toBeNull();
    expect(move(5, 'Tab')).toBeNull();
  });

  test('Home and End go to the first and the last swatch of the families', () => {
    expect(move(9, 'Home')).toBe(0);
    expect(move(9, 'End')).toBe(cells - 1);
  });
});

describe('the tall swatches', () => {
  test('right from the end of a row goes to the tall swatch beside it', () => {
    expect(move(3, 'ArrowRight')).toBe(cells);
    expect(move(11, 'ArrowRight')).toBe(cells);
    expect(move(15, 'ArrowRight')).toBe(cells + 1);
    expect(move(23, 'ArrowRight')).toBe(cells + 1);
  });

  test('left goes to the middle of the rows beside it, and up and down move between the two', () => {
    expect(move(cells, 'ArrowLeft')).toBe(1 * 4 + 3);
    expect(move(cells + 1, 'ArrowLeft')).toBe(4 * 4 + 3);
    expect(move(cells, 'ArrowDown')).toBe(cells + 1);
    expect(move(cells + 1, 'ArrowUp')).toBe(cells);
    expect(move(cells + 1, 'ArrowDown')).toBeNull();
  });

  test('a move straight back left returns to the row it came from, at the top, middle, and bottom of the three', () => {
    for (const [row, extreme] of [
      [0, 0],
      [1, 0],
      [2, 0],
      [3, 1],
      [4, 1],
      [5, 1],
    ] as const) {
      const entered = { extreme, row };
      expect(move(cells + extreme, 'ArrowLeft', { entered })).toBe(row * 4 + 3);
    }
  });

  test('a row remembered for one tall swatch is not used for the other', () => {
    expect(move(cells + 1, 'ArrowLeft', { entered: { extreme: 0, row: 0 } })).toBe(4 * 4 + 3);
  });
});

describe('"No color"', () => {
  test('is above the top row and the top tall swatch only when the palette has it', () => {
    expect(move(2, 'ArrowUp', { none: true })).toBe(NO_COLOR);
    expect(move(cells, 'ArrowUp', { none: true })).toBe(NO_COLOR);
    expect(move(2, 'ArrowUp')).toBeNull();
    expect(move(cells, 'ArrowUp')).toBeNull();
  });

  test('is not above anything else', () => {
    expect(move(6, 'ArrowUp', { none: true })).toBe(2);
    expect(move(cells + 1, 'ArrowUp', { none: true })).toBe(cells);
  });
});
