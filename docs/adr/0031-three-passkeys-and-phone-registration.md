# ADR 0031: Sign in with Red, Purple, or a phone; register the phone from a signed-in page

- Status: Proposed
- Date: 2026-10-30

Builds on [ADR 0027](0027-passkey-auth-and-direct-browser-to-api.md), which is also Proposed. Items marked **Confirm** need an answer before this is accepted.

## Context

ADR 0027 chose passkeys only, with the `Fido2` library and a typed `auth.passkey` table, and left open which authenticators the owner uses and how they are enrolled. The owner wants to sign in with any of three: the Token2 keys Red and Purple, or a phone.

A WebAuthn credential is a key pair held by one authenticator, and the biometric or PIN only unlocks it locally. No credential works on a second device unless that device registers its own, or the credential is a passkey that the phone's platform syncs across its own ecosystem. So "all my devices work" means registering each authenticator on the one account. The model is one user with many `auth.passkey` rows.

## Decision

### Three authenticators, one account

- **Red and Purple** register as roaming FIDO2 authenticators over USB or NFC, once each, from any computer they are plugged into. Their OpenPGP applet (used for commit signing) is separate from the FIDO2 applet and is not affected.
- **The phone** registers as a passkey. It is a platform authenticator, so the credential is created on the phone and unlocked with its biometric or screen lock. If the phone's platform syncs passkeys (iCloud Keychain or Google Password Manager), the passkey also reaches the owner's other devices on that platform. This is a bonus, not something the site relies on.
- All three are ordinary rows in `auth.passkey`. Nothing in the schema or the sign-in path treats one kind differently. The stored backup-eligible and backed-up flags and the transports record which kind each is.

### Relying party

- `rpId` is `josholson.dev`, the registrable domain, so a credential works from `www.josholson.dev` (the page that calls `navigator.credentials`) and the API at `api.josholson.dev` can verify it. A narrower `rpId` of `www.josholson.dev` would also work today but would block moving the sign-in page to another subdomain later.
- The API's allowed origin for verification is exactly `https://www.josholson.dev` (the same allowlist as the CSRF and CORS rules in ADR 0027). In development `rpId` is `localhost` and the origin is the dev server.

### Signing in

- The sign-in page calls `navigator.credentials.get` with an empty `allowCredentials` and `userVerification: "required"`. The browser offers whichever passkeys it can use: a plugged-in key (PIN and touch) or the phone. No username step is needed, because there is one account, and the API never reveals credential ids to an anonymous visitor.
- This needs discoverable credentials, so registration asks for `residentKey: "required"`. A security key keeps a limited number of these slots; one slot per key for this site is fine. Checked with `fido2-token -I` on 2026-10-30: the key has a FIDO2 PIN set (`clientPin`) and 298 free slots (`remaining rk(s)`).
- A phone user on a computer picks "use a phone" in the browser's sign-in dialog and approves on the phone (the same QR flow described below).

### The passkey page and registering the phone

A page behind the session, `/admin/passkeys`, lists the account's passkeys (nickname, created, last used, whether synced, transports) and lets the owner:

- **Add a phone.** The button runs `navigator.credentials.create` with `hints: ["hybrid"]`, so the browser goes straight to its cross-device option. The **browser shows the QR code**, not the site: the owner scans it with the phone, approves with the phone's biometric, and the phone creates the passkey. The phone and the computer talk over Bluetooth proximity and a relay run by the browser vendor. The site neither generates nor receives anything QR-related.
- **Add a security key.** The same button with `hints: ["security-key"]`, for Red, Purple, or a replacement.
- **Rename** a passkey. The owner types the nickname. The page may pre-fill a plain label from the transports ("USB key" or "phone"), but never from the AAGUID.
- **Remove** a passkey, but never the last one.

Registration endpoints:

- All of them require a session. Because a hijacked session could otherwise plant an attacker's passkey, a change to the passkey set only takes effect once the owner **confirms it with a passkey that is already registered**. The confirmation is part of the flow, not a freshness window on the session:
  1. **Start.** `POST /auth/passkeys/registration/options` returns the creation options, with `excludeCredentials` set to the existing credential ids so an authenticator cannot be registered twice. The owner creates the new credential (scans the QR and approves on the phone, or touches the key).
  2. **Hold.** `POST /auth/passkeys/registration/verify` checks the attestation but does **not** insert anything. It keeps the result as a *pending change* bound to the session.
  3. **Confirm.** The page then asks the owner to sign in with a known passkey. `POST /auth/passkeys/confirm/options` returns assertion options for the active passkeys only, with a challenge bound to the pending change. `POST /auth/passkeys/confirm/verify` checks the assertion and, only then, inserts the row. The new, still-pending credential can never confirm itself.
- **Removing** a passkey uses the same confirm step: the pending change is "remove this one", and any registered passkey confirms it.
- A pending change and its challenges are random, single-use, bound to the session that created them, and expire after 5 minutes. They live in memory, which suits a single API instance; an unconfirmed attempt leaves nothing in the database, and a restart mid-flow just fails and is retried. The 5 minutes and the in-memory storage are decided.
- Attestation is `none` (the other values are `indirect`, `direct`, and `enterprise`). The site has no need to prove which hardware the owner used, and `none` avoids the permission prompt that `direct` shows and the certificate-chain verification code.

### No AAGUID column

An AAGUID names the authenticator model (for example "Token2 PIN Plus Security Key Series"), not the individual device. With attestation `none`, browsers replace it with all zeros, so `auth.passkey` has no `aaguid` column.

- On registration the API checks the AAGUID in the attested credential data. If it is not all zeros, it throws instead of ignoring it. The point is to learn that an authenticator reports one, and so decide whether to add the column.
- The exception message names the AAGUID and, when it can, the model. It looks the AAGUID up in the [FIDO Metadata Service](https://fidoalliance.org/metadata/) (MDS3, a signed list of certified authenticators) and, failing that, in the community `passkey-authenticator-aaguids` list, then says what it found. The lookup is best effort and never decides anything: a lookup failure or an unknown AAGUID still throws.
- Seeing the exception means attestation or a browser changed. Decide then whether to add the column, and for what (labels, or restricting which models may register).
- Checked on 2026-10-30: Red and Purple are the same model and report AAGUID `eabb46cc-e241-80bf-ae9e-96fa6d2975cf`, which MDS3 lists as "TOKEN2 PIN Plus Security Key Series" (FIDO certified). With `none` the site still sees zeros.

### Bootstrap

The first passkey comes from the server-side enrollment token in ADR 0027 (decided), and the owner uses Red for it. Purple and the phone are then added from the page above. Recovery is unchanged: any one of the three signs in, and if all are lost, the CLI mints a new enrollment token.

## Alternatives considered

- **A site-generated QR code.** The page would show a QR holding a one-time enrollment link that the phone opens and registers from directly, with no Bluetooth needed. It is a more reliable fallback where cross-device is unsupported, but it adds a bearer token in a URL, an endpoint to issue and redeem it, and a QR library. The browser's built-in flow gives the same outcome with none of that. Revisit only if the built-in flow proves unreliable on the owner's browsers.
- **A synced passkey only, no hardware keys.** Simplest, but ties the account to one platform vendor's account and leaves a computer without a phone nearby unable to sign in.
- **Non-discoverable credentials with a username and `allowCredentials`.** Saves key slots, but needs a username step or exposes credential ids to anonymous requests.

## Consequences

- The phone flow needs a browser that starts the cross-device flow, plus Bluetooth on the computer (the Linux laptop has it). Chrome and Edge do. The owner also uses Firefox on Windows and Linux: on Windows it hands off to the OS dialog, which offers the phone; on Linux it may not offer the phone at all. **Confirm** by trying it on Firefox for Linux. If it fails, the owner registers and signs in with the phone from Chrome or uses Red or Purple in Firefox, or the site-generated QR above is built.
- A synced phone passkey is only as safe as the owner's Apple or Google account. That is accepted because Red and Purple remain independent of it, and any passkey can be removed from the page.
- `auth.passkey` stores `transports`, `is_backup_eligible`, and `is_backed_up`, and no AAGUID (see above). `auth.user` gains `user_handle`.
- Adding or removing a passkey takes two ceremonies, the change and the confirmation, which is the point. A stolen session cookie alone cannot change the passkey set, and no time window is needed.
- The first passkey is the exception: it comes from the enrollment token and has no known passkey to confirm it. With only one passkey, removal is already refused.
- When accepted, add the registration and passkey-management endpoints to the API contract, and describe the passkey page in `docs/product.md`.
