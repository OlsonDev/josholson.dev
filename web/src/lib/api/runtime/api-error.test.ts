import { describe, expect, test } from 'bun:test';
import { ApiError } from './api-error.ts';

const validation = (errors: Record<string, string[]>): ApiError =>
  new ApiError('foo.create', 422, { title: 'One or more validation errors occurred.', status: 422, errors });

describe('fieldErrors', () => {
  test('keys each field by its JSON name, with every message for it', () => {
    const error = validation({
      slug: ['Not kebab-case.', 'Too long.'],
      name: ['Taken.'],
      'children[0].label': ['Required.'],
    });

    expect(error.fieldErrors).toEqual({
      slug: ['Not kebab-case.', 'Too long.'],
      name: ['Taken.'],
      'children[0].label': ['Required.'],
    });
  });

  test('a field the JSON reader refused ($.slug) is the field (slug), and joins what validation said about it', () => {
    const error = validation({
      '$.slug': ['Expected a string but found Number.'],
      slug: ['Required.'],
      '$.a.b': ['Bad.'],
    });

    expect(error.fieldErrors).toEqual({ slug: ['Expected a string but found Number.', 'Required.'], 'a.b': ['Bad.'] });
  });

  test('what is about the whole request is not a field', () => {
    const error = validation({ '': ['The end must be after the start.'], $: ['Invalid JSON.'], slug: ['Required.'] });

    expect(error.fieldErrors).toEqual({ slug: ['Required.'] });
  });

  test('is empty when the API sent no errors, or no problem at all', () => {
    expect(new ApiError('foo.get', 404, { title: 'Not Found', status: 404 }).fieldErrors).toEqual({});
    expect(new ApiError('foo.get', 0, undefined).fieldErrors).toEqual({});
  });
});

describe('formErrors', () => {
  test('are the messages under "" and under $, and no others', () => {
    const error = validation({ '': ['The end must be after the start.'], $: ['Invalid JSON.'], slug: ['Required.'] });

    expect(error.formErrors).toEqual(['The end must be after the start.', 'Invalid JSON.']);
  });

  test('are empty when every error is about a field', () => {
    expect(validation({ slug: ['Required.'] }).formErrors).toEqual([]);
  });
});

describe('messages', () => {
  test('are the form errors and then every field message, for a toast or a list', () => {
    const error = validation({
      slug: ['Required.'],
      '': ['The end must be after the start.'],
      name: ['Taken.', 'Too long.'],
    });

    expect(error.messages).toEqual(['The end must be after the start.', 'Required.', 'Taken.', 'Too long.']);
  });

  test('without validation errors are the one message the API sent: its detail, else its title', () => {
    expect(new ApiError('foo.get', 409, { title: 'Conflict', detail: 'Already there.', status: 409 }).messages).toEqual(
      ['Already there.'],
    );
    expect(new ApiError('foo.get', 404, { title: 'Not Found', status: 404 }).messages).toEqual(['Not Found']);
  });

  test('are empty when the API sent nothing to show', () => {
    expect(new ApiError('foo.get', 0, undefined).messages).toEqual([]);
  });
});
