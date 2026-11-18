import { expect, test } from 'bun:test';
import { transport } from './hooks.ts';

const { Instant } = transport;

test('an instant is sent as its ISO text, and arrives as the same instant', () => {
  const sent = Temporal.Instant.from('2026-11-16T12:30:00.123456789Z');

  const text = Instant?.encode(sent);
  expect(text).toBe('2026-11-16T12:30:00.123456789Z');
  expect(typeof text === 'string' ? Instant?.decode(text).equals(sent) : undefined).toBe(true);
});

test('anything else is left for SvelteKit to send itself', () => {
  expect(Instant?.encode('2026-11-16T12:30:00Z')).toBe(false);
  expect(Instant?.encode({})).toBe(false);
  expect(Instant?.encode(undefined)).toBe(false);
});
