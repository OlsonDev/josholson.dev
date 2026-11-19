// The browser's WebAuthn API, for the two ceremonies the API starts: creating a passkey (enrolling, adding one) and
// signing in with one. The API sends the options in WebAuthn's JSON form and takes the answer in its JSON form, and the
// web relays both without reading them, so each is `unknown` in the generated types and is handled here, once.

/** Whether this browser can do WebAuthn's JSON forms (Chrome 129, Firefox 119, Safari 18.4, and later). */
export const supportsPasskeys = (): boolean =>
  typeof PublicKeyCredential !== 'undefined' && 'parseCreationOptionsFromJSON' in PublicKeyCredential;

/** The user closed the browser's passkey dialog, or it timed out. Nothing is wrong, so nothing is reported as an error. */
export const wasCanceled = (error: unknown): boolean =>
  error instanceof DOMException && (error.name === 'NotAllowedError' || error.name === 'AbortError');

/** Makes a passkey from the options `POST /auth/enrollment/options` (or the registration options) returned. */
export async function createPasskey(options: unknown): Promise<unknown> {
  // eslint-disable-next-line @typescript-eslint/no-unsafe-type-assertion -- the API sends WebAuthn's own JSON, and the browser checks it
  const publicKey = PublicKeyCredential.parseCreationOptionsFromJSON(options as PublicKeyCredentialCreationOptionsJSON);
  const credential = await navigator.credentials.create({ publicKey });
  if (!(credential instanceof PublicKeyCredential)) throw new Error('The browser made no passkey.');
  return credential.toJSON();
}

/** Signs with a passkey, for the options `POST /auth/sign-in/options` returned. */
export async function signWithPasskey(options: unknown): Promise<unknown> {
  // eslint-disable-next-line @typescript-eslint/no-unsafe-type-assertion -- the API sends WebAuthn's own JSON, and the browser checks it
  const publicKey = PublicKeyCredential.parseRequestOptionsFromJSON(options as PublicKeyCredentialRequestOptionsJSON);
  const credential = await navigator.credentials.get({ publicKey });
  if (!(credential instanceof PublicKeyCredential)) throw new Error('The browser signed with no passkey.');
  return credential.toJSON();
}
