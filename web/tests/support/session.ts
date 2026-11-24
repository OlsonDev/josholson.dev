import { createHash, randomBytes } from 'node:crypto';

import type { Cookie } from '@playwright/test';
import { SQL } from 'bun';

import { ownerLogin, sessionCookieName } from './environment.ts';

/**
 * Signs the first account in a scoped database in: a session row, as the API makes one when a passkey signs in (the token
 * is random, and the row holds its SHA-256, as `OpaqueToken.Hash` does), and the cookie the browser sends it with. A
 * session is not seed data (`auth.session` is `@seed ignore`), so it is made here, for the test, with the database's own
 * clock for its times. About 7 ms. It needs a layer with an account (`account`).
 */
export async function startSession(database: string): Promise<Cookie> {
  const db = new SQL({ ...ownerLogin(), database, max: 1 });
  try {
    const [user] = await db`SELECT u.user_id FROM auth."user" AS u ORDER BY u.user_id LIMIT 1`;
    if (user === undefined) throw new Error('`signedIn` needs a layer with an account, such as `account`.');
    const token = randomBytes(32).toString('base64url');
    const hash = createHash('sha256').update(token, 'utf8').digest();
    await db`INSERT INTO auth.session (user_id, token_hash, expires_utc)
             VALUES (${user.user_id}, ${hash}, now() + interval '1 day')`;
    return {
      name: sessionCookieName,
      value: token,
      domain: 'localhost',
      path: '/',
      expires: -1,
      secure: true,
      httpOnly: true,
      sameSite: 'Lax',
    };
  } finally {
    await db.close();
  }
}
