import { ApiError } from '#lib/api/index.ts';
import { describeFailure } from '#lib/auth/failure.ts';

import { countMessages, type FieldErrors } from './form-logic.ts';

/** What a save that threw means for the page, so each page does not read the error itself. */
export type SaveOutcome =
  /** Someone saved the thing after the form was loaded (a 409 for a stale version). */
  | { kind: 'conflict' }
  /** The API refused the values: `fieldErrors` for the form, and `message` for what is about no field (null when a field has one). */
  | { kind: 'invalid'; fieldErrors: FieldErrors; message: string | null }
  /** Anything else: the API did not answer, or answered with a status the page has no words for. */
  | { kind: 'failed'; message: string };

/**
 * What an error from saving means: a 409 is a conflict, a 422 is invalid values, and the rest is words for what went
 * wrong (`describeFailure`). Only a save that sends `expectedVersion` can have a conflict, so a page without versions
 * never calls this for a 409 it can get another way. Used by: tags.
 */
export function outcomeOfSaveError(error: unknown): SaveOutcome {
  if (error instanceof ApiError && error.is(409)) return { kind: 'conflict' };
  if (error instanceof ApiError && error.is(422)) {
    const fieldErrors = error.fieldErrors;
    const message = countMessages(fieldErrors) === 0 ? (error.formErrors[0] ?? error.messages[0] ?? null) : null;
    return { kind: 'invalid', fieldErrors, message };
  }
  return { kind: 'failed', message: describeFailure(error).message };
}
