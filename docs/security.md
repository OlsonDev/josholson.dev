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
- CSRF: SvelteKit rejects cross-origin form POSTs by checking `Origin`, which depends on `ORIGIN` being right behind the proxy ([architecture.md](architecture.md)). Cookies are `HttpOnly`, `Secure`, and `SameSite`.
- CORS: allow only the `www` origin on the API.
- Uploads: identify files by content (SHA-256) and check type against `blob.mime_type`, never by the client's file name or declared type ([ADR 0008](adr/0008-content-addressed-blobs.md)).

## Public forms

- The contact form is the one unauthenticated write. Bound the size of every field, rate-limit it, and keep the spam flag (`form.contact.is_spam`) in play.
- The email address shown on the Contact page is obfuscated, but assume it gets scraped.

## Dependencies

- Patch quickly. Turn on Dependabot alerts and updates for NuGet and npm.
- Review what `bun audit` and `dotnet list package --vulnerable` report before each deploy.
