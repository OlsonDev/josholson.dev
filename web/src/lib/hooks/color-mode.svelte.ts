import { createSubscriber } from 'svelte/reactivity';

import type { Mode } from '#lib/palette/colors.ts';

/**
 * Whether the page is in light or dark mode, which `app.html` decides from the system and sets as the `dark` class on
 * `<html>`. Reading `current` inside an effect or a template makes it follow the class when it changes.
 */
class ColorMode {
  readonly #subscribe = createSubscriber((update) => {
    const observer = new MutationObserver(update);
    observer.observe(document.documentElement, { attributes: true, attributeFilter: ['class'] });
    return () => observer.disconnect();
  });

  get current(): Mode {
    if (typeof document === 'undefined') return 'light';
    this.#subscribe();
    return document.documentElement.classList.contains('dark') ? 'dark' : 'light';
  }
}

export const colorMode = new ColorMode();
