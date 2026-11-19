# ADR 0044: The first passkey is registered from the browser with a setup secret, and a passkey's nickname goes onto the key

- Status: Accepted
- Date: 2026-11-19

Replaces the bootstrap and recovery section of [ADR 0027 (passkey auth and direct browser-to-API calls)](0027-passkey-auth-and-direct-browser-to-api.md), and adds to [ADR 0031 (Red, Purple, or a phone)](0031-three-passkeys-and-phone-registration.md).

## Context

ADR 0027 had the owner run `JoshOlson.Api enroll "Name"` on the server, which printed a one-time token (15 minutes, stored as a hash) to open `/admin#token=...` with. That put a step in a terminal in front of every first run and every recovery, and the owner would rather finish setup in the browser. The enrollment ceremony was already in the browser. What the terminal gave was proof of owning the server.

Separately, the key stores the user's name with each passkey, and a browser's account chooser shows it. The name was the account's display name, so two passkeys on one key were both "Josh Olson", and Firefox told them apart with "(0)" and "(1)". The nickname the owner types was only sent after the browser had made the credential, so it could not be on the key.

## Decision

- **A setup secret in the API's environment** (`Auth__SetupSecret`, at least 32 characters, empty by default, which turns setup off). The admin page asks the API whether setup is open (`GET /auth/enrollment`), and if it is, shows a form for the secret, a name for the account (only if there is no account yet), and a nickname.
- **Setup is open only while no passkey exists.** It closes with the first passkey, so the secret does nothing afterward: a leaked secret is useless once the owner has set up, and nothing long-lived stands in for a passkey. The check is the API's, on every call, not the page's. A wrong secret, setup being off, and setup being closed all answer the same `404`, so a caller learns nothing about which. The secret is compared by hash in fixed time.
- **The secret is traded for the existing one-time token** (`POST /auth/enrollment/setup`), and the token registers the passkey as before (`/auth/enrollment/options`, then `/auth/enrollment`). So the token table, its 15-minute lifetime, and the tests of the enrollment endpoints stay, and only the way a token is first obtained changes. The account is created at setup, when there is none, from the name the form asks for.
- **The CLI command is removed**, and so is the `#token=` fragment on `/admin`. `mise passkey enroll <setup secret>` does the same for a real key without a browser.
- **Recovery** when every key is lost is a person with access to the database, who deletes the passkey rows (`DELETE FROM auth.passkey;`, as the owner). Setup is then open again with the same secret. ADR 0027's recovery by minting a token while passkeys exist is gone, on purpose: that was the only way in that did not need a passkey, and a secret that works while one exists is a password.
- **The nickname goes onto the key.** Both registration options requests (`/auth/enrollment/options` and `/auth/passkeys/registration/options`) carry the nickname, and the API names the user "Josh Olson (Red)" in the options, so the key stores it and a chooser shows it. The API checks the nickname at that step (not empty, at most 64 characters, not another passkey's), before the key makes anything, which also stops a refused nickname from leaving a credential on the key. The finishing requests no longer carry it: the API kept it with the options.

## Alternatives considered

- **Keep the CLI and add a field to paste its token.** The smallest change, and it keeps the stronger credential (15 minutes, one use). It does not remove the step in a terminal, which was the point.
- **A setup secret that works while passkeys exist, for recovery.** One command fewer when every key is lost, but a long-lived secret that signs in beside the passkeys, which ADR 0027 chose not to have.
- **Whoever reaches `/admin` first while there is no account becomes the owner.** Needs no secret, and the public site would be open to a stranger between the deploy and the first visit.
- **A rename that updates the key.** The label is written when the key makes the passkey. Chrome has an API for changing it (`PublicKeyCredential.signalCurrentUserDetails`), and other browsers do not, so a rename on the passkey page changes the nickname and not what the key shows.

## Consequences

- **Deploying needs the secret** in the API's environment until the first passkey is registered. After that it can be removed, and a missing secret is not an error.
- **The API's tests share a database**, and setup opens only while it holds no passkey, so they cannot test that it opens. They test every way it stays shut, and the browser test (`web/tests/journey.test.ts`, on a database of its own) tests that it opens, closes with the first passkey, and refuses the secret afterward.
- **Setup has no rate limit**, like the other anonymous endpoints, and a 32-character secret is the protection. A rate limit (Cloudflare in front of the public site, or the API's own) is planned and not built.
- **The label is frozen when the key makes the passkey.** A passkey renamed later is still labeled with its first nickname on the key.
- **A nickname refused at the options step** is a `422` there, so the passkey page reports a taken name before any ceremony, and the finishing requests have no `422`.
