/** What a `Field` tells the input inside it: the `id` its label points at, what to put in `aria-describedby`, and whether it has an error. */
export type FieldControl = { id: string; describedBy: string | undefined; invalid: boolean };

/**
 * Attributes that tell password managers and autofill extensions that an input is not a login or an address, so they leave it
 * alone: LastPass (`data-lpignore`), 1Password (`data-1p-ignore`), Bitwarden (`data-bwignore`), and the rest (`data-form-type`).
 * An admin form's inputs are content (a tag's name), and a name field in particular looks like a contact form to them. Spread
 * onto an `Input` or `Textarea`.
 */
export const noAutofill = {
  'data-lpignore': 'true',
  'data-1p-ignore': 'true',
  'data-bwignore': 'true',
  'data-form-type': 'other',
} as const;
