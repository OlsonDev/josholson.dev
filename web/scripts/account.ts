// What the generator of the test account (`generate-test-account.ts`) and its test share: a passkey's public key as a
// COSE key, the layer that holds the account, and the public key a private one belongs to. All of it is plain data in and
// out, so a test can check it.

const hex = (bytes: Uint8Array): string => Buffer.from(bytes).toString('hex');
const unbase64url = (text: string): Uint8Array => new Uint8Array(Buffer.from(text, 'base64url'));

/** What the account is made of, as the layer and the browser tests each need it. */
export type TestAccount = {
  displayName: string;
  nickname: string;
  userHandle: Uint8Array;
  credentialId: Uint8Array;
  /** The private key, PKCS#8, which the virtual authenticator is given. */
  privateKey: Uint8Array;
};

/** The ES256 public key of a P-256 point as a COSE key (RFC 9052), which is what a passkey row's `public_key` holds. */
export function coseKey(x: Uint8Array, y: Uint8Array): Uint8Array {
  if (x.length !== 32 || y.length !== 32) throw new Error('A P-256 point is two 32-byte coordinates.');
  // {1: 2 (EC2), 3: -7 (ES256), -1: 1 (P-256), -2: x, -3: y}, each coordinate a byte string of length 32.
  const beforeX = Uint8Array.of(0xa5, 0x01, 0x02, 0x03, 0x26, 0x20, 0x01, 0x21, 0x58, 0x20);
  const beforeY = Uint8Array.of(0x22, 0x58, 0x20);
  return new Uint8Array([...beforeX, ...x, ...beforeY, ...y]);
}

/** The COSE public key that goes with a private key (PKCS#8, P-256). */
export async function publicKeyOf(privateKey: Uint8Array): Promise<Uint8Array> {
  const key = await crypto.subtle.importKey('pkcs8', privateKey.slice(), { name: 'ECDSA', namedCurve: 'P-256' }, true, [
    'sign',
  ]);
  const { x, y } = await crypto.subtle.exportKey('jwk', key);
  if (x === undefined || y === undefined) throw new Error('A P-256 key has an x and a y.');
  return coseKey(unbase64url(x), unbase64url(y));
}

/**
 * The text of `account.yaml`, the layer with the user and the passkey, written the way `LayerWriter` writes a layer (the
 * tables by name, the columns in the table's order, `~` for NULL), because `mise seeds:check` wants a layer to be its own
 * export, byte for byte. It is a root, so its IDs are in the first block.
 */
export function accountLayer(account: TestAccount, publicKey: Uint8Array): string {
  const passkey = [
    'passkey_id: 1',
    'user_id: 1',
    `credential_id: \\x${hex(account.credentialId)}`,
    `public_key: \\x${hex(publicKey)}`,
    'sign_count: 0',
    // `PasskeyTransport.Usb`, so the page says "Security key", as it does for the key the owner enrolls.
    'transports: [1]',
    'is_backup_eligible: false',
    'is_backed_up: false',
    `nickname: ${account.nickname}`,
    'created_utc: 0s',
    'last_used_utc: ~',
  ];
  const user = [
    'user_id: 1',
    `display_name: ${account.displayName}`,
    `user_handle: \\x${hex(account.userHandle)}`,
    'is_active: true',
    'created_utc: 0s',
  ];
  return [
    'description: The owner with one passkey, whose private key is in web/tests/support/account.generated.ts',
    'tables:',
    '  auth.passkey:',
    '    insert:',
    `      - { ${passkey.join(', ')} }`,
    '  auth.user:',
    '    insert:',
    `      - { ${user.join(', ')} }`,
    '',
  ].join('\n');
}
