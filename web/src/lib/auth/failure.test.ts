import { expect, test } from 'bun:test';
import { ApiError } from '#lib/api/index.ts';
import { attempt, describeFailure } from './failure.ts';

test('a closed passkey dialog is "Canceled." and not an error', () => {
  expect(describeFailure(new DOMException('', 'NotAllowedError')).message).toBe('Canceled.');
  expect(describeFailure(new DOMException('', 'AbortError')).message).toBe('Canceled.');
});

test('a status the page has words for uses them', () => {
  const failure = describeFailure(new ApiError('foo.bar', 404, undefined), { 404: 'Gone.' });

  expect(failure.message).toBe('Gone.');
});

test('any other status is a plain sentence, with the fields the API named', () => {
  const problem = { title: 'Invalid', status: 422, errors: { nickname: ['Nickname is already in use.'] } };
  const failure = describeFailure(new ApiError('foo.bar', 422, problem));

  expect(failure.message).toBe('That did not work.');
  expect(failure.fields).toEqual({ nickname: ['Nickname is already in use.'] });
});

test('a network failure says the API did not answer', () => {
  expect(describeFailure(new ApiError('foo.bar', 0, undefined)).message).toBe('The API did not answer.');
});

test('another error keeps its own message', () => {
  expect(describeFailure(new Error('The browser made no passkey.')).message).toBe('The browser made no passkey.');
  expect(describeFailure('odd').message).toBe('That did not work.');
});

test('attempt is null when it worked, and the words for what went wrong when it did not', async () => {
  expect(await attempt(() => Promise.resolve())).toBeNull();
  const failure = await attempt(() => Promise.reject(new ApiError('foo.bar', 409, undefined)), { 409: 'In use.' });

  expect(failure?.message).toBe('In use.');
});
