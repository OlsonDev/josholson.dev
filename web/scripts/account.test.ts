import { describe, expect, test } from 'bun:test';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';

import { accountLayer, coseKey, publicKeyOf, type TestAccount } from './account.ts';

const repository = join(import.meta.dirname, '..', '..');
const hex = (bytes: Uint8Array): string => Buffer.from(bytes).toString('hex');

describe('coseKey', () => {
  test('is the map of an ES256 key, with x and y as 32-byte strings', () => {
    const x = new Uint8Array(32).fill(1);
    const y = new Uint8Array(32).fill(2);

    const key = coseKey(x, y);

    expect(hex(key.slice(0, 10))).toBe('a5010203262001215820');
    expect(key.slice(10, 42)).toEqual(x);
    expect(hex(key.slice(42, 45))).toBe('225820');
    expect(key.slice(45)).toEqual(y);
    expect(key.length).toBe(77);
  });

  test('refuses a coordinate that is not 32 bytes', () => {
    expect(() => coseKey(new Uint8Array(31), new Uint8Array(32))).toThrow('32-byte');
  });
});

describe('accountLayer', () => {
  test('is a root with the passkey and the user, columns in the table order', () => {
    const account: TestAccount = {
      displayName: 'Foo',
      nickname: 'Bar',
      userHandle: new Uint8Array([1, 2]),
      credentialId: new Uint8Array([3, 4]),
      privateKey: new Uint8Array(),
    };

    const yaml = accountLayer(account, new Uint8Array([5, 6]));

    expect(yaml).toContain(
      '  auth.passkey:\n    insert:\n      - { passkey_id: 1, user_id: 1, credential_id: \\x0304, public_key: \\x0506,',
    );
    expect(yaml).toContain('nickname: Bar, created_utc: 0s, last_used_utc: ~ }');
    expect(yaml).toContain(
      '  auth.user:\n    insert:\n      - { user_id: 1, display_name: Foo, user_handle: \\x0102, is_active: true, created_utc: 0s }',
    );
    expect(yaml).not.toContain('extends');
    expect(yaml.endsWith('\n')).toBe(true);
  });
});

// The two files the generator writes belong together: the public key in the layer is the one of the private key the
// browser tests hold, so the passkey in the database can verify what the authenticator signs.
describe('the committed account', () => {
  test('has a layer whose public key belongs to the private key in the tests', async () => {
    const yaml = readFileSync(join(repository, 'JoshOlson.Database', 'seeds', 'account.yaml'), 'utf8');
    const generated = readFileSync(join(repository, 'web', 'tests', 'support', 'account.generated.ts'), 'utf8');
    const privateKey = /privateKey:\s*'([^']+)'/.exec(generated)?.[1];
    const credentialId = /credentialId:\s*'([^']+)'/.exec(generated)?.[1];
    const userHandle = /userHandle:\s*'([^']+)'/.exec(generated)?.[1];
    if (privateKey === undefined || credentialId === undefined || userHandle === undefined) {
      throw new Error('account.generated.ts does not hold the account.');
    }

    const publicKey = await publicKeyOf(new Uint8Array(Buffer.from(privateKey, 'base64')));

    expect(yaml).toContain(`public_key: \\x${hex(publicKey)},`);
    expect(yaml).toContain(`credential_id: \\x${Buffer.from(credentialId, 'base64').toString('hex')},`);
    expect(yaml).toContain(`user_handle: \\x${Buffer.from(userHandle, 'base64').toString('hex')},`);
  });
});
