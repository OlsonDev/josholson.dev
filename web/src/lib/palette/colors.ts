import { palette } from './palette.generated.ts';

/**
 * The Tailwind families a color can come from, in the order of the database domain `color.tailwind`
 * (JoshOlson.Database/migrations/V003__color.sql), which `colors.test.ts` compares this list with. A Tailwind release that
 * adds or renames one needs a migration, this list, and `mise web:palette`.
 */
export const families = [
  'red',
  'orange',
  'amber',
  'yellow',
  'lime',
  'green',
  'emerald',
  'teal',
  'cyan',
  'sky',
  'blue',
  'indigo',
  'violet',
  'purple',
  'fuchsia',
  'pink',
  'rose',
  'slate',
  'gray',
  'zinc',
  'neutral',
  'stone',
  'taupe',
  'mauve',
  'mist',
  'olive',
] as const;

/** Every weight of a family, light to dark. */
export const weights = [50, 100, 200, 300, 400, 500, 600, 700, 800, 900, 950] as const;

/** The light or the dark appearance of the site, which the picker and the chips are drawn for. */
export type Mode = 'light' | 'dark';

/** A stored color is the light-mode one: a palette name such as `emerald-200`, or `black` or `white`. */
export type PaletteColor = string;

/** Which weights the picker offers, nearest the page's own end of the scale first: 50 to 500 in light mode, 950 to 500 in dark. */
export const offeredWeights: Readonly<Record<Mode, readonly number[]>> = {
  light: [50, 100, 200, 300, 400, 500],
  dark: [950, 900, 800, 700, 600, 500],
};

/** The weight a swatch shows in a mode, for a stored (light-mode) weight: dark mode shows the mirror, 1000 minus it. */
export function displayWeight(stored: number, mode: Mode): number {
  return mode === 'light' ? stored : 1000 - stored;
}

/** What is stored when a swatch of the mode is picked: its weight as light mode would show it. */
export function storedWeight(shown: number, mode: Mode): number {
  return mode === 'light' ? shown : 1000 - shown;
}

/** The name a family and a weight make. */
export function colorName(family: string, weight: number): PaletteColor {
  return `${family}-${weight.toString()}`;
}

/** Whether a name is a color the database accepts. */
export function isPaletteColor(name: string): boolean {
  return name in palette;
}

/** The family and weight of a name such as `emerald-200`, or null for `black`, `white`, and anything else. */
export function parseColor(name: string): { family: string; weight: number } | null {
  const dash = name.lastIndexOf('-');
  if (dash < 0 || !isPaletteColor(name)) return null;
  return { family: name.slice(0, dash), weight: Number(name.slice(dash + 1)) };
}

/**
 * The color that stands in for a stored one in dark mode: the mirrored weight (200 becomes 800, 950 becomes 50), and
 * `black` and `white` swap. A light weight reads as a dark one on a dark page, so a color keeps its feel.
 */
export function mirror(name: PaletteColor): PaletteColor {
  if (name === 'black') return 'white';
  if (name === 'white') return 'black';
  const parsed = parseColor(name);
  return parsed === null ? name : colorName(parsed.family, 1000 - parsed.weight);
}

/** The sRGB hex of a palette color. */
export function hexOf(name: PaletteColor): string {
  const hex = palette[name];
  if (hex === undefined) throw new Error(`${name} is not a Tailwind palette color.`);
  return hex;
}

function luminance(hex: string): number {
  const channels = [1, 3, 5].map((start) => {
    const value = Number.parseInt(hex.slice(start, start + 2), 16) / 255;
    return value <= 0.04045 ? value / 12.92 : ((value + 0.055) / 1.055) ** 2.4;
  });
  return 0.2126 * (channels[0] ?? 0) + 0.7152 * (channels[1] ?? 0) + 0.0722 * (channels[2] ?? 0);
}

/** The WCAG 2 contrast ratio of two colors, from 1 (identical) to 21 (black on white). Not rounded. */
export function contrastRatio(a: PaletteColor, b: PaletteColor): number {
  const [light, dark] = [luminance(hexOf(a)), luminance(hexOf(b))].toSorted((x, y) => y - x);
  return ((light ?? 0) + 0.05) / ((dark ?? 0) + 0.05);
}

/** Whether a ratio meets a level, "barely" when it passes by less than 10%, and fails otherwise. */
export type Verdict = 'pass' | 'barely' | 'fail';

/** The WCAG 2 contrast ratio AA asks of normal text. */
export const AA = 4.5;
/** The ratio AAA asks of normal text. */
export const AAA = 7;
// A pass by less than this factor over AA is "barely".
const BARELY = 1.1;

/** WCAG AA for normal text (4.5): a pass within 10% of the line is "barely". */
export function aaVerdict(ratio: number): Verdict {
  if (ratio < AA) return 'fail';
  return ratio < AA * BARELY ? 'barely' : 'pass';
}

/** WCAG AAA for normal text (7): there is no "barely", it is met or it is not. */
export function aaaVerdict(ratio: number): Exclude<Verdict, 'barely'> {
  return ratio < AAA ? 'fail' : 'pass';
}

/** The ratio as a person reads it: cut, not rounded, so a 4.499 is never shown as the 4.50 that would pass. */
export function formatRatio(ratio: number): string {
  return (Math.floor(ratio * 100) / 100).toFixed(2);
}

/**
 * The text colors worth trying on a background, as the picker previews them, from the pole outward so the ratios fall
 * steadily down the list: in light mode black, then the family's darkest weights (950 down to 700, within 300 of the
 * dark end), in dark mode white, then its lightest (50 up to 300). `black` and `white` backgrounds have only the opposite.
 */
export function textCandidates(background: PaletteColor, mode: Mode): PaletteColor[] {
  const parsed = parseColor(background);
  if (parsed === null) return [background === 'black' ? 'white' : 'black'];
  return mode === 'light'
    ? ['black', ...[950, 900, 800, 700].map((weight) => colorName(parsed.family, weight))]
    : ['white', ...[50, 100, 200, 300].map((weight) => colorName(parsed.family, weight))];
}

/** Black or white, whichever reads better on the background. */
export function bestText(background: PaletteColor): 'black' | 'white' {
  return contrastRatio(background, 'black') >= contrastRatio(background, 'white') ? 'black' : 'white';
}
