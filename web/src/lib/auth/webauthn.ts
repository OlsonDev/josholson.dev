// The browser's WebAuthn API, for the two ceremonies the API starts: creating a passkey (enrolling, adding one) and
// signing in with one. The API sends the options in WebAuthn's JSON form and takes the answer in its JSON form, and the
// web relays both without reading them, so each is `unknown` in the generated types and is handled here, once.

/** Whether this browser can do WebAuthn's JSON forms (Chrome 129, Firefox 119, Safari 18.4, and later). */
export const supportsPasskeys = (): boolean =>
  typeof PublicKeyCredential !== 'undefined' && 'parseCreationOptionsFromJSON' in PublicKeyCredential;

/** The user closed the browser's passkey dialog, or it timed out. Nothing is wrong, so nothing is reported as an error. */
export const wasCanceled = (error: unknown): boolean =>
  error instanceof DOMException && (error.name === 'NotAllowedError' || error.name === 'AbortError');

/** Where the browser should offer to make a passkey: on a phone (it shows a QR code), or on a security key. */
export type CreateHint = 'hybrid' | 'security-key';

/** Makes a passkey from the options the API returned, with a hint about which kind (ADR 0031: Red, Purple, or a phone). */
export async function createPasskey(options: unknown, hint?: CreateHint): Promise<unknown> {
  // eslint-disable-next-line @typescript-eslint/no-unsafe-type-assertion -- the API sends WebAuthn's own JSON, and the browser checks it
  const publicKey = PublicKeyCredential.parseCreationOptionsFromJSON(options as PublicKeyCredentialCreationOptionsJSON);
  // `hints` is in the specification and the JSON types, and not yet in the DOM types of the options.
  const hinted: PublicKeyCredentialCreationOptions & { hints?: string[] } =
    hint === undefined ? publicKey : { ...publicKey, hints: [hint] };
  const credential = await navigator.credentials.create({ publicKey: hinted });
  if (!(credential instanceof PublicKeyCredential)) throw new Error('The browser made no passkey.');
  return credential.toJSON();
}

/** Signs with a passkey, for the options the API returned. */
export async function signWithPasskey(options: unknown): Promise<unknown> {
  // eslint-disable-next-line @typescript-eslint/no-unsafe-type-assertion -- the API sends WebAuthn's own JSON, and the browser checks it
  const publicKey = PublicKeyCredential.parseRequestOptionsFromJSON(options as PublicKeyCredentialRequestOptionsJSON);
  const credential = await navigator.credentials.get({ publicKey });
  if (!(credential instanceof PublicKeyCredential)) throw new Error('The browser signed with no passkey.');
  return credential.toJSON();
}

/**
 * Whether this browser can make a passkey on a phone (WebAuthn's `hybrid` transport, which is the phone's QR code and
 * Bluetooth). False only when the browser says it cannot, as Firefox on Linux does, so that a browser that cannot say
 * (older ones lack `getClientCapabilities`) still gets the button.
 */
export async function canUsePhone(): Promise<boolean> {
  if (typeof PublicKeyCredential === 'undefined' || !('getClientCapabilities' in PublicKeyCredential)) return true;
  try {
    const capabilities = await PublicKeyCredential.getClientCapabilities();
    // Read as unknown: a browser that does not report the capability has no such member, whatever the types say.
    const reported: unknown = Reflect.get(capabilities, 'hybridTransport');
    return reported !== false;
  } catch {
    // The browser could not say, which is not the same as saying no.
    return true;
  }
}
