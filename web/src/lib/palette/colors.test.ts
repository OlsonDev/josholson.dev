import { describe, expect, test } from 'bun:test';

import {
  aaaVerdict,
  aaVerdict,
  bestText,
  colorName,
  contrastRatio,
  displayWeight,
  families,
  formatRatio,
  hexOf,
  isPaletteColor,
  mirror,
  offeredWeights,
  parseColor,
  storedWeight,
  textCandidates,
  weights,
} from './colors.ts';

describe('the database domain', () => {
  const migration = new URL('../../../../JoshOlson.Database/migrations/V003__color.sql', import.meta.url);

  test('lists the same families and weights as this module', async () => {
    const sql = await Bun.file(migration).text();
    const match = /color\.tailwind[\s\S]*?\(black\|white\|\(([a-z|]+)\)-\(([\d|]+)\)\)/.exec(sql);
    expect(match).not.toBeNull();
    expect(match?.[1]?.split('|')).toEqual([...families]);
    expect(match?.[2]?.split('|').map(Number)).toEqual([...weights]);
  });

  test('every family and weight has a color, and so do black and white', () => {
    for (const family of families)
      for (const weight of weights) expect(isPaletteColor(colorName(family, weight))).toBe(true);
    expect(isPaletteColor('black')).toBe(true);
    expect(isPaletteColor('white')).toBe(true);
    expect(isPaletteColor('emerald-150')).toBe(false);
    expect(isPaletteColor('foo-200')).toBe(false);
  });
});

describe('parseColor and mirror', () => {
  test('reads a family and weight, and null for black and white', () => {
    expect(parseColor('emerald-200')).toEqual({ family: 'emerald', weight: 200 });
    expect(parseColor('black')).toBeNull();
    expect(parseColor('nonsense')).toBeNull();
  });

  test('mirrors the weight around 500 and swaps black and white', () => {
    expect(mirror('emerald-200')).toBe('emerald-800');
    expect(mirror('slate-950')).toBe('slate-50');
    expect(mirror('rose-500')).toBe('rose-500');
    expect(mirror('black')).toBe('white');
    expect(mirror('white')).toBe('black');
  });
});

describe('what the picker shows', () => {
  test('light mode offers 50 to 500 and dark mode 950 to 500', () => {
    expect(offeredWeights.light).toEqual([50, 100, 200, 300, 400, 500]);
    expect(offeredWeights.dark).toEqual([950, 900, 800, 700, 600, 500]);
  });

  test('a swatch of dark mode stores its light-mode weight', () => {
    expect(storedWeight(800, 'dark')).toBe(200);
    expect(storedWeight(200, 'light')).toBe(200);
    expect(displayWeight(200, 'dark')).toBe(800);
    for (const shown of offeredWeights.dark) expect(displayWeight(storedWeight(shown, 'dark'), 'dark')).toBe(shown);
  });

  test('text to try on a background: the dark end in light mode, the light end in dark mode', () => {
    expect(textCandidates('emerald-100', 'light')).toEqual([
      'black',
      'emerald-950',
      'emerald-900',
      'emerald-800',
      'emerald-700',
    ]);
    expect(textCandidates('emerald-900', 'dark')).toEqual([
      'white',
      'emerald-50',
      'emerald-100',
      'emerald-200',
      'emerald-300',
    ]);
    expect(textCandidates('black', 'light')).toEqual(['white']);
    expect(textCandidates('white', 'dark')).toEqual(['black']);
  });
});

describe('contrast', () => {
  test('is 21 for black on white, 1 for a color on itself, and the same either way round', () => {
    expect(contrastRatio('black', 'white')).toBeCloseTo(21, 5);
    expect(contrastRatio('emerald-200', 'emerald-200')).toBeCloseTo(1, 5);
    expect(contrastRatio('emerald-200', 'black')).toBeCloseTo(contrastRatio('black', 'emerald-200'), 10);
  });

  test('matches a hand-computed value (#a4f4cf under black)', () => {
    expect(hexOf('emerald-200')).toBe('#a4f4cf');
    expect(contrastRatio('emerald-200', 'black')).toBeCloseTo(16.42, 1);
  });

  test('AA is 4.5 with "barely" for a pass within 10%, and AAA is 7 with no in between', () => {
    expect(aaVerdict(4.49)).toBe('fail');
    expect(aaVerdict(4.5)).toBe('barely');
    expect(aaVerdict(4.94)).toBe('barely');
    expect(aaVerdict(4.95)).toBe('pass');
    expect(aaaVerdict(6.99)).toBe('fail');
    expect(aaaVerdict(7)).toBe('pass');
  });

  test('a ratio is cut, never rounded up to a pass', () => {
    expect(formatRatio(4.499)).toBe('4.49');
    expect(formatRatio(21)).toBe('21.00');
    expect(formatRatio(7.004)).toBe('7.00');
  });

  test('safe weights keep their promise: black reads on 50 to 400 and white on 700 to 950 (AA)', () => {
    for (const family of families) {
      for (const weight of [50, 100, 200, 300, 400]) {
        expect(aaVerdict(contrastRatio(colorName(family, weight), 'black'))).not.toBe('fail');
      }
      for (const weight of [700, 800, 900, 950]) {
        expect(aaVerdict(contrastRatio(colorName(family, weight), 'white'))).not.toBe('fail');
      }
    }
  });

  test('the best text is the one with more contrast', () => {
    expect(bestText('emerald-200')).toBe('black');
    expect(bestText('emerald-800')).toBe('white');
    expect(bestText('white')).toBe('black');
    expect(bestText('black')).toBe('white');
  });
});
