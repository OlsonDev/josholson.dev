import { randomBytes } from 'node:crypto';

import { SQL } from 'bun';

import { coseKey } from '../../scripts/account.ts';
import { ownerLogin } from './environment.ts';

/**
 * Adds a passkey to the first account of a scoped database, as the API would have after the account enrolled another key:
 * a row with a key of its own, and a name. No authenticator holds it, so a test can list it, rename it, and remove it (the
 * account's own passkey confirms the removal), but not sign in with it.
 */
export async function addPasskeyRow(database: string, nickname: string): Promise<void> {
  const pair = await crypto.subtle.generateKey({ name: 'ECDSA', namedCurve: 'P-256' }, true, ['sign', 'verify']);
  const raw = new Uint8Array(await crypto.subtle.exportKey('raw', pair.publicKey)); // 0x04, x, y
  const publicKey = coseKey(raw.slice(1, 33), raw.slice(33, 65));
  const db = new SQL({ ...ownerLogin(), database, max: 1 });
  try {
    const [user] = await db`SELECT u.user_id FROM auth."user" AS u ORDER BY u.user_id LIMIT 1`;
    if (user === undefined) throw new Error('A passkey row needs a layer with an account, such as `account`.');
    await db`INSERT INTO auth.passkey (user_id, credential_id, public_key, sign_count, is_backup_eligible, is_backed_up, nickname)
             VALUES (${user.user_id}, ${randomBytes(32)}, ${publicKey}, 0, false, false, ${nickname})`;
  } finally {
    await db.close();
  }
}
