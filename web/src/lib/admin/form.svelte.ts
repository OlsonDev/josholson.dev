import { tick } from 'svelte';

import {
  countMessages,
  type FieldErrors,
  firstInvalid,
  sameData,
  summarizeErrors,
  withoutFieldErrors,
} from './form-logic.ts';

/** What a form needs beyond its values. */
export type FormOptions<T> = {
  /** The rules the API enforces too, so most mistakes are caught before the round trip. Messages by field. */
  validate?: (values: T) => Record<string, string[]>;
  /** Called before the first invalid field is focused, so a form with tabs can show the tab its fields are on. */
  reveal?: () => void | Promise<void>;
};

/**
 * The state of an edit form: its values, whether they differ from what was loaded, and the errors on each field, from
 * the client's rules or the API's. Every list and edit page of the admin builds one, so a form's behavior (the dirty
 * check, the error count, focusing the first invalid input) is the same everywhere. Used by: tags.
 *
 * A field is shown with `Field` (`field.svelte`), whose `data-field` is the name the errors are keyed by (`name`,
 * `aliases[1]`).
 */
export class FormState<T extends object> {
  values: T;
  errors: FieldErrors;

  #initial: T;
  readonly #options: FormOptions<T>;

  constructor(initial: T, options: FormOptions<T> = {}) {
    this.#initial = $state.raw(copy(initial));
    this.values = $state(copy(initial));
    this.errors = $state({});
    this.#options = options;
  }

  /** Whether the values differ from what the form was loaded or last saved with. */
  get isDirty(): boolean {
    return !sameData(this.#initial, $state.snapshot(this.values));
  }

  /** How many messages the fields have in all. */
  get errorCount(): number {
    return countMessages(this.errors);
  }

  /** The sentence for the action bar, or null when nothing is wrong. */
  get summary(): string | null {
    return this.errorCount > 0 ? summarizeErrors(this.errorCount) : null;
  }

  /** Starts over from the saved values: with `next` (what the server now holds), or with what the fields hold now. */
  reset(next?: T): void {
    if (next === undefined) {
      this.#initial = copy(this.values);
    } else {
      this.#initial = copy(next);
      this.values = copy(next);
    }
    this.errors = {};
  }

  /** Shows the errors the API sent, keyed by field. */
  setErrors(errors: FieldErrors): void {
    this.errors = errors;
  }

  /** Forgets the errors of a field (and its entries) once the person edits it. */
  clear(field: string): void {
    this.errors = withoutFieldErrors(this.errors, field);
  }

  /** Runs the client's rules and shows what they found. True when there was nothing wrong. */
  check(): boolean {
    const found = this.#options.validate?.(copy(this.values)) ?? {};
    this.errors = found;
    return countMessages(found) === 0;
  }

  /** Focuses the topmost input that has an error. `root` is the element that holds the `Field`s. */
  async focusFirstInvalid(root: ParentNode): Promise<void> {
    await this.#options.reveal?.();
    await tick();
    const fields = [...root.querySelectorAll<HTMLElement>('[data-field]')].map(
      (element) => element.dataset['field'] ?? '',
    );
    const target = firstInvalid(fields, this.errors);
    if (target === undefined) return;
    const holder = [...root.querySelectorAll<HTMLElement>('[data-field]')].find(
      (element) => element.dataset['field'] === target,
    );
    const control = holder?.matches('input, textarea, select, button')
      ? holder
      : holder?.querySelector<HTMLElement>('input, textarea, select, button, [tabindex]:not([tabindex="-1"])');
    control?.focus();
  }
}

// A copy that shares nothing with the original: the values the form was loaded with must not move when the fields do.
function copy<T>(value: T): T {
  // eslint-disable-next-line typescript/no-unsafe-type-assertion -- a snapshot of a T cloned is a T
  return structuredClone($state.snapshot(value)) as T;
}
