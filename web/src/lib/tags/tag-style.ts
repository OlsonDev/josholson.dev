import { bestText, isPaletteColor, mirror, type PaletteColor, paletteVar } from '#lib/palette/colors.ts';

/** A tag's colors in both modes: what is stored for light, its mirror for dark, and the text for each. */
export type TagColors = {
  light: { background: PaletteColor; text: PaletteColor };
  dark: { background: PaletteColor; text: PaletteColor };
};

/** The colors for a stored one, or null for a tag with no color. */
export function tagColors(stored: PaletteColor | null): TagColors | null {
  if (stored === null || !isPaletteColor(stored)) return null;
  const darkBackground = mirror(stored);
  return {
    light: { background: stored, text: bestText(stored) },
    dark: { background: darkBackground, text: bestText(darkBackground) },
  };
}

/**
 * The inline style that paints a chip: four custom properties, which the chip's classes (`bg-(--tag-bg)`,
 * `dark:bg-(--tag-bg-dark)`) read. They name the palette's custom properties (`palette.generated.css`, imported by
 * `layout.css`), which hold every color, so no class is ever built from a color name. Empty for a tag with no color.
 */
export function tagStyle(stored: PaletteColor | null): string {
  const colors = tagColors(stored);
  if (colors === null) return '';
  return [
    `--tag-bg: ${paletteVar(colors.light.background)}`,
    `--tag-fg: ${paletteVar(colors.light.text)}`,
    `--tag-bg-dark: ${paletteVar(colors.dark.background)}`,
    `--tag-fg-dark: ${paletteVar(colors.dark.text)}`,
  ].join('; ');
}
