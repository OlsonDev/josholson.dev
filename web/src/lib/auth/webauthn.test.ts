import { afterEach, describe, expect, test } from 'bun:test';
import { canUsePhone } from './webauthn.ts';

// `PublicKeyCredential` is the browser's, so a test puts in the one it wants and takes it out again.
const browserHas = (credential: unknown): void => {
  Reflect.set(globalThis, 'PublicKeyCredential', credential);
};

afterEach(() => {
  Reflect.deleteProperty(globalThis, 'PublicKeyCredential');
});

describe('canUsePhone', () => {
  test('is true when the browser has no WebAuthn to ask, or cannot say', async () => {
    expect(await canUsePhone()).toBe(true);

    browserHas({});
    expect(await canUsePhone()).toBe(true);
  });

  test('is false only when the browser says the hybrid transport is not supported', async () => {
    browserHas({ getClientCapabilities: () => Promise.resolve({ hybridTransport: false }) });
    expect(await canUsePhone()).toBe(false);

    browserHas({ getClientCapabilities: () => Promise.resolve({ hybridTransport: true }) });
    expect(await canUsePhone()).toBe(true);
  });

  test('is true when the browser reports nothing about it, or fails to report', async () => {
    browserHas({ getClientCapabilities: () => Promise.resolve({ conditionalGet: true }) });
    expect(await canUsePhone()).toBe(true);

    browserHas({ getClientCapabilities: () => Promise.reject(new Error('no')) });
    expect(await canUsePhone()).toBe(true);
  });
});
