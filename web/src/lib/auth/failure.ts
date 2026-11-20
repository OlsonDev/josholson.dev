import { ApiError } from '#lib/api/index.ts';

import { wasCanceled } from './webauthn.ts';

/** What to show for something that did not work: a sentence, and the messages that belong to a field of a form. */
export type Failure = { message: string; fields: Readonly<Record<string, readonly string[]>> };

/**
 * Words for an error from a ceremony or a call. A closed passkey dialog is not an error, and an authenticator that already
 * holds a passkey for the site is "already registered". A status the page has words for
 * (`messages`) uses them, and any other is "That did not work.", with the API's own messages for the form's fields.
 */
export function describeFailure(error: unknown, messages: Partial<Record<number, string>> = {}): Failure {
  if (wasCanceled(error)) return { message: 'Canceled.', fields: {} };
  // The browser's own message for it is long and differs between browsers.
  if (error instanceof DOMException && error.name === 'InvalidStateError') {
    return { message: 'That authenticator is already registered.', fields: {} };
  }
  if (error instanceof ApiError) {
    const fallback = error.status === 0 ? 'The API did not answer.' : 'That did not work.';
    return { message: messages[error.status] ?? fallback, fields: error.fieldErrors };
  }
  return { message: error instanceof Error ? error.message : 'That did not work.', fields: {} };
}

/** Runs something the visitor asked for. Null when it worked, and words for what went wrong when it did not. */
export async function attempt(
  action: () => Promise<void>,
  messages: Partial<Record<number, string>> = {},
): Promise<Failure | null> {
  try {
    await action();
    return null;
  } catch (error) {
    return describeFailure(error, messages);
  }
}
