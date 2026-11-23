/** Each field's messages, keyed by the field's JSON name (`name`, `aliases[1]`). */
export type FieldErrors = Readonly<Record<string, readonly string[]>>;

/** Whether two values hold the same data, however deeply (plain objects, arrays, and primitives). */
export function sameData(a: unknown, b: unknown): boolean {
  if (Object.is(a, b)) return true;
  if (Array.isArray(a) || Array.isArray(b)) {
    return Array.isArray(a) && Array.isArray(b) && a.length === b.length && a.every((item, i) => sameData(item, b[i]));
  }
  if (typeof a === 'object' && typeof b === 'object' && a !== null && b !== null) {
    const keys = Object.keys(a);
    return (
      keys.length === Object.keys(b).length &&
      keys.every((key) => key in b && sameData(Reflect.get(a, key), Reflect.get(b, key)))
    );
  }
  return false;
}

/** How many messages there are in all, which is what the form's summary counts. */
export function countMessages(errors: FieldErrors): number {
  return Object.values(errors).reduce((total, messages) => total + messages.length, 0);
}

/** "Please address the validation error." for one, "Please address the 3 validation errors." for several. */
export function summarizeErrors(count: number): string {
  return count === 1
    ? 'Please address the validation error.'
    : `Please address the ${count.toString()} validation errors.`;
}

/**
 * The first of the form's fields, in the order they are on the page, that has an error. `fields` is the page's own order
 * (the `data-field` of each `Field`), so a failure the API reports in any order still lands on the topmost input.
 */
export function firstInvalid(fields: readonly string[], errors: FieldErrors): string | undefined {
  return fields.find((field) => (errors[field]?.length ?? 0) > 0);
}

/**
 * The errors without a field and the fields under it: editing `aliases` clears `aliases[0]` and `aliases[1]` too, so a
 * message about an entry does not stay once the list changed. Returns the same object when nothing was removed.
 */
export function withoutFieldErrors(errors: FieldErrors, field: string): FieldErrors {
  const remaining = Object.entries(errors).filter(
    ([key]) => key !== field && !key.startsWith(`${field}[`) && !key.startsWith(`${field}.`),
  );
  return remaining.length === Object.keys(errors).length ? errors : Object.fromEntries(remaining);
}
