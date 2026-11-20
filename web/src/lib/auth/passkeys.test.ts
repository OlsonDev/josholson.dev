import { describe, expect, test } from 'bun:test';

import { PasskeyTransport } from '#lib/api/schema.ts';

import { describePasskey, nicknameProblem } from './passkeys.ts';

describe('describePasskey', () => {
  test('a key that can be reached over a wire or by touch is a security key', () => {
    expect(describePasskey({ transports: [PasskeyTransport.Usb, PasskeyTransport.Nfc], isBackedUp: false })).toBe(
      'Security key',
    );
  });

  test('a hybrid one is a phone, and one that is synced says so', () => {
    expect(
      describePasskey({ transports: [PasskeyTransport.Hybrid, PasskeyTransport.Internal], isBackedUp: true }),
    ).toBe('Phone, synced');
  });

  test('one that is only internal is this device, and one that said nothing is a passkey', () => {
    expect(describePasskey({ transports: [PasskeyTransport.Internal], isBackedUp: false })).toBe('This device');
    expect(describePasskey({ transports: [], isBackedUp: false })).toBe('Passkey');
  });
});

describe('nicknameProblem', () => {
  test('is null for a good name', () => {
    expect(nicknameProblem(' Red ', ['Purple'])).toBeNull();
  });

  test('refuses an empty name, a long one, and one that is taken', () => {
    expect(nicknameProblem('   ', [])).toBe('Give it a name.');
    expect(nicknameProblem('x'.repeat(65), [])).toBe('The name can be at most 64 characters.');
    expect(nicknameProblem('Red', ['Red'])).toBe('Another passkey already has that name.');
  });
});
