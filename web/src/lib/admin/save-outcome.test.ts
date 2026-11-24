import { describe, expect, test } from 'bun:test';

import { ApiError } from '#lib/api/index.ts';
import type { Problem } from '#lib/api/runtime/api-error.ts';

import { outcomeOfSaveError } from './save-outcome.ts';

// What the client holds as an error's body is JSON the API sent, whatever its shape.
function body(value: unknown): Problem {
  return JSON.parse(JSON.stringify(value));
}

describe('outcomeOfSaveError', () => {
  test('a 409 is a conflict', () => {
    expect(outcomeOfSaveError(new ApiError('foo.update', 409, body({ title: 'Conflict' })))).toEqual({
      kind: 'conflict',
    });
  });

  test('a 422 with errors on fields gives them to the form and says nothing more', () => {
    const error = new ApiError('foo.update', 422, body({ errors: { slug: ['Slug is taken.'] } }));
    expect(outcomeOfSaveError(error)).toEqual({
      kind: 'invalid',
      fieldErrors: { slug: ['Slug is taken.'] },
      message: null,
    });
  });

  test('a 422 with no field errors says what is wrong with the request as a whole, else the API title', () => {
    const whole = new ApiError('foo.update', 422, body({ errors: { '': ['Foo and bar conflict.'] } }));
    expect(outcomeOfSaveError(whole)).toEqual({ kind: 'invalid', fieldErrors: {}, message: 'Foo and bar conflict.' });
    const titled = new ApiError('foo.update', 422, body({ title: 'Unprocessable' }));
    expect(outcomeOfSaveError(titled)).toEqual({ kind: 'invalid', fieldErrors: {}, message: 'Unprocessable' });
    expect(outcomeOfSaveError(new ApiError('foo.update', 422, undefined))).toEqual({
      kind: 'invalid',
      fieldErrors: {},
      message: null,
    });
  });

  test('anything else is words for what went wrong', () => {
    expect(outcomeOfSaveError(new ApiError('foo.update', 500, undefined))).toEqual({
      kind: 'failed',
      message: 'That did not work.',
    });
    expect(outcomeOfSaveError(new ApiError('foo.update', 0, undefined))).toEqual({
      kind: 'failed',
      message: 'The API did not answer.',
    });
    expect(outcomeOfSaveError(new Error('foo'))).toEqual({ kind: 'failed', message: 'foo' });
  });
});
