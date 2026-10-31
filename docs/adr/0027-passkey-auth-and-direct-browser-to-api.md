# ADR 0027: Passkey auth and direct browser-to-API calls

- Status: Proposed
- Date: 2026-10-28

## Context

The admin area (contact form submissions, résumé editors) needs "secure auth". There is one account, the site owner's. Caddy fronts `www.josholson.dev` (SvelteKit, server-side rendered) and `api.josholson.dev` (the C# API). Schema today: `auth.user`, `auth.user_email`, and `auth.session` (SHA-256 token hash, expiry, IP, user agent).

## Decision

### Browsers call the API directly (decided earlier)

SvelteKit serves pages and may call the API server to server to render a page. Once a page has loaded, the browser calls the API directly for data. A backend-for-frontend was rejected: data requests would pay an extra hop, and every browser action would need a SvelteKit endpoint.

- **Cookie.** `__Secure-` prefix (the `__Host-` prefix cannot carry a `Domain`), `HttpOnly`, `Secure`, `SameSite=Lax`, `Domain=.josholson.dev`, so the browser sends it to both `www` and `api`, which are one site with two origins. The API owns it: it sets and clears it.
- **Session.** An opaque random token with at least 256 bits. Only its SHA-256 hash is stored in `auth.session`. Lifetimes: 7 days idle, 30 days absolute.
- **SSR.** SvelteKit forwards the visitor's session cookie on its server-to-server calls, so a page renders as that user.
- **CSRF.** The API protects itself, because SvelteKit's `Origin` check only covers SvelteKit's own form posts. State-changing requests must carry an `Origin` that is on an allowlist, and the body must be `application/json`, which forces a preflight.
- **CORS.** Only the `www` origin, as an exact match and never `*`, with credentials. Send `Vary: Origin` and `Access-Control-Max-Age`.

### Passkeys only

`docs/security.md` says passwords are not planned, so the owner signs in with a passkey (WebAuthn). There is no password login.

- **Server library: `Fido2`** (passwordless-lib/fido2-net-lib). Version 4.3.1 was released on 2026-10-16, with 5.0 previews in progress and 55 releases in total, and it targets `net10.0`. It is general-purpose, so the API owns the endpoints and the storage, which fits Dapper and our `auth.*` tables. ASP.NET Core Identity's passkey support (new in .NET 10) is built around `UserManager<TUser>` and Identity's own store model, so using it would mean implementing Identity's store interfaces on top of Dapper. I did not test how well that works.
- **Browser side:** the native `navigator.credentials` API with its JSON helpers (`PublicKeyCredential.parseCreationOptionsFromJSON` and `toJSON`). The owner signs in from Chrome and Firefox on Windows, Linux, and Android, so check support in those when the sign-in page is built; `@simplewebauthn/browser` is the fallback. Verification stays in the API, with the data.
- **Storage: a typed `auth.passkey` table** keyed to `auth.user`, with a credential id (bytes, unique), the COSE public key, a signature counter, transports, the backup-eligible and backed-up flags, a nickname, and created and last-used times (no AAGUID, see [ADR 0031](0031-three-passkeys-and-phone-registration.md)). A user has several passkeys, so they cannot be one JSON blob: the counter updates need real columns and constraints, and the table shows up in `docs/schema`.
- **User handle.** WebAuthn needs an opaque per-user handle of at most 64 random bytes with no personal data. It is a unique column on `auth.user`.
- **`auth.identity`** has no role if passkeys are the only login, so it is dropped. Nothing has been deployed, so it is removed from `V008__auth.sql` rather than dropped by a new migration. A table for an external login provider can be added if one is ever wanted.

### Bootstrap and recovery

There is one account and the server owner has shell access, so both are operational rather than self-service:

- **First credential.** A CLI command on the server mints a one-time enrollment token (stored as a hash, with a short expiry) and prints it. Presenting it to the API starts passkey registration.
- **Recovery.** Register at least two passkeys on different devices. If both are lost, run the same CLI command again.

## Alternatives considered

- A backend-for-frontend, where only SvelteKit talks to the API: rejected above.
- Passwords, or passwords plus a second factor: more to store and defend, for one user.
- Hosted identity (Auth0 or similar): a dependency and a third party holding the login for one user.

## Consequences

- When this is accepted, update `docs/security.md` (CSRF and CORS lines), `docs/architecture.md`, and "Not decided yet" in `docs/database.md`. A migration adds `auth.passkey` and the user handle column, and `docs/schema` is regenerated.
- The CSRF and CORS rules apply to every state-changing endpoint, including ones already written, before the API is exposed.
- Losing every passkey and shell access means no recovery. That is acceptable for a personal site.
