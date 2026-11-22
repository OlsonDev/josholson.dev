import { describe, expect, test } from 'bun:test';

import { tagColors, tagStyle } from './tag-style.ts';

describe('a tag in both modes', () => {
  test('stores light, mirrors for dark, and picks text for each', () => {
    expect(tagColors('emerald-200')).toEqual({
      light: { background: 'emerald-200', text: 'black' },
      dark: { background: 'emerald-800', text: 'white' },
    });
    expect(tagColors(null)).toBeNull();
    expect(tagColors('nonsense')).toBeNull();
  });

  test('is painted with custom properties that name palette variables, never a built class name', () => {
    expect(tagStyle('emerald-200')).toBe(
      '--tag-bg: var(--palette-emerald-200); --tag-fg: var(--palette-black); --tag-bg-dark: var(--palette-emerald-800); --tag-fg-dark: var(--palette-white)',
    );
    expect(tagStyle(null)).toBe('');
  });
});
