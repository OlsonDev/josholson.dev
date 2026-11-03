# Security

Rules for code and operations. Where the schema already enforces something, the schema is authoritative; see [database.md](database.md).

## Secrets

- Never commit connection strings, passwords, API keys, or private keys. `.env` is gitignored; `.env.example` holds names only.
- A secret that reached git history is leaked, even after a revert. Rotate it.
- The API's database password is supplied at provisioning time (`-v api_password=...`), never in a migration ([ADR 0018](adr/0018-dml-only-api-role.md)).

## Transport

- Caddy terminates TLS for both domains. Browsers never reach `web` or `api` over plain HTTP.
- The hop from Caddy to the guest is plain HTTP, so firewall the mapped ports to Caddy's IP only ([architecture.md](architecture.md)).
- ASP.NET Core trusts forwarded headers from Caddy's IP and no one else; otherwise a client can forge its address and scheme.

## Hashing and encryption

- Hash what you only need to compare: session tokens (`auth.session.token_hash`, a `hash.sha256`). Generate the token from a cryptographic random source and store only its hash.
- Encrypt what you must read back. Nothing is encrypted at the application level yet; add a note here when something is.
- Never use a fast hash such as SHA-256 for passwords. If passwords are ever added, use a password hashing function (Argon2id or the framework's PBKDF2 implementation). Passkeys avoid the problem.
- Don't write cryptography. Use the platform's primitives.

## Authorization

- Every API endpoint states who may call it: public, or authenticated. Default to authenticated; public is the exception and is explicit.
- The admin area is secure by authentication, not by an obscure URL.
- Tests prove it: for each protected endpoint, a request with no session and a request with an expired session are both rejected.

## Injection and scripting

- SQL: parameters only, never string-built values ([csharp.md](../.claude/rules/csharp.md)). The API role cannot change structure, which limits the damage of a mistake.
- XSS: Svelte escapes interpolated text. Treat `{@html ...}` as unsafe, and sanitize any rendered Markdown (blog posts) or user-supplied HTML (contact messages) before it reaches it.
- CSRF: SvelteKit rejects cross-origin form POSTs by checking `Origin`, which depends on `ORIGIN` being right behind the proxy ([architecture.md](architecture.md)). That covers only SvelteKit's own forms, so the API protects itself: `OriginCheckMiddleware` refuses any `POST`, `PUT`, or `DELETE` whose `Origin` is not exactly the `www` origin (403), and a request body must be `application/json` (415), which forces a CORS preflight. Cookies are `HttpOnly`, `Secure`, and `SameSite=Lax` (ADR 0027, passkey auth and direct browser-to-API calls).
- CORS: allow only the `www` origin on the API, as an exact match with credentials, never `*`. Responses carry `Vary: Origin`, and a preflight is cached for an hour.
- Sessions: an opaque 256-bit token in a cookie, only its SHA-256 hash in `auth.session`. 7 days idle, 30 days absolute. Authenticated is the default: a global `SessionFilter` returns 401 for any action without a valid session unless it is marked `[AllowAnonymous]`. `SessionFilterTests` lists every public action by name, so a new endpoint is protected until someone adds it there on purpose.
- Passkeys: the owner signs in with a passkey and there is no password. Adding or removing a passkey takes a second ceremony with an already registered passkey, so a stolen session cookie cannot change the set (ADR 0031, Red, Purple, or a phone). Enrollment tokens are single-use, expire after 15 minutes, and are stored hashed; only someone with a shell on the server can mint one (`JoshOlson.Api enroll`).
- Uploads: identify files by content (SHA-256) and check type against `blob.mime_type`, never by the client's file name or declared type ([ADR 0008](adr/0008-content-addressed-blobs.md)).

## Public forms

- The contact form is the one unauthenticated write. Bound the size of every field, rate-limit it, and keep the spam flag (`form.contact.is_spam`) in play.
- The email address shown on the Contact page is obfuscated, but assume it gets scraped.

## Dependencies

- Patch quickly. Turn on Dependabot alerts and updates for NuGet and npm.
- Review what `bun audit` and `dotnet list package --vulnerable` report before each deploy.
