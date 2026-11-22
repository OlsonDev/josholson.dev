import { readFileSync } from 'node:fs';
import { join } from 'node:path';

import { expect, test } from '@playwright/test';

import { palette } from '../src/lib/palette/palette.generated.ts';

// The generated palette (culori's sRGB for Tailwind's OKLCH colors) against what Chrome paints for the same colors: a
// canvas fills each one and its pixel is read back. Rounding in Chrome's own math may differ by one step in a channel,
// never more. It needs no API or sign-in, so it is independent of the other files.
const channel = (hex: string, index: number): number => Number.parseInt(hex.slice(1 + index * 2, 3 + index * 2), 16);

test('the generated palette is what Chrome paints for the OKLCH colors', async ({ page, browserName }) => {
  test.skip(browserName !== 'chromium', 'Chromium only');
  const theme = readFileSync(join(import.meta.dirname, '..', 'node_modules', 'tailwindcss', 'theme.css'), 'utf8');
  const oklch = [...theme.matchAll(/--color-([a-z]+-\d+):\s*(oklch\([^)]*\))/g)].map(
    (match) => [match[1] ?? '', match[2] ?? ''] as const,
  );
  expect(oklch.length).toBeGreaterThan(200);

  const painted = await page.evaluate((colors) => {
    const canvas = document.createElement('canvas');
    canvas.width = 1;
    canvas.height = 1;
    const context = canvas.getContext('2d', { colorSpace: 'srgb', willReadFrequently: true });
    if (context === null) throw new Error('no 2d canvas');
    const result: Record<string, number[]> = {};
    for (const [name, css] of colors) {
      context.clearRect(0, 0, 1, 1);
      context.fillStyle = css;
      context.fillRect(0, 0, 1, 1);
      result[name] = [...context.getImageData(0, 0, 1, 1).data].slice(0, 3);
    }
    return result;
  }, oklch);

  const far: string[] = [];
  for (const [name] of oklch) {
    const hex = palette[name];
    const pixel = painted[name];
    if (hex === undefined || pixel === undefined) {
      far.push(`${name}: missing`);
      continue;
    }
    const worst = Math.max(...pixel.map((value, index) => Math.abs(value - channel(hex, index))));
    if (worst > 1) far.push(`${name}: ${hex} against ${pixel.join(',')}`);
  }
  expect(far).toEqual([]);
});
