import { beforeNavigate, goto } from '$app/navigation';

/**
 * Stops a page with unsaved changes from being left by accident. Every way out inside the app (a link, `goto`, the
 * browser's back and forward) is held while `isDirty()` is true, and `isOpen` says the person is being asked: show
 * `UnsavedChangesDialog` with this, and it calls `leave()` or `stay()`. Leaving the site (closing the tab, a reload) is
 * the browser's own prompt, which has no words of ours. `staysHere` says which addresses are still the same page (a
 * section of the same tag), so moving to one is not leaving. Create it while the component initializes. Used by: tags.
 */
export class UnsavedGuard {
  #held = $state.raw<(() => void) | null>(null);
  #allowed = false;

  constructor(isDirty: () => boolean, staysHere: (url: URL) => boolean = () => false) {
    beforeNavigate((navigation) => {
      if (this.#allowed || !isDirty()) return;
      if (navigation.to !== null && staysHere(navigation.to.url)) return;
      // Canceling a navigation that unloads the page is how the browser's own prompt is shown.
      navigation.cancel();
      if (navigation.willUnload || navigation.to === null) return;

      const { url } = navigation.to;
      // Back and forward move through history, so confirming moves the same way and leaves no extra entry.
      const delta = navigation.type === 'popstate' ? navigation.delta : undefined;
      this.#held = () => {
        if (delta === undefined) {
          void goto(url);
        } else {
          history.go(delta);
        }
      };
    });
  }

  /** Whether the person is being asked. */
  get isOpen(): boolean {
    return this.#held !== null;
  }

  /** Goes where the person was headed, discarding the changes. */
  leave(): void {
    const go = this.#held;
    this.#held = null;
    if (go === null) return;
    this.#allowed = true;
    go();
  }

  /** Stays on the page. */
  stay(): void {
    this.#held = null;
  }
}
