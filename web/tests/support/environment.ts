import { readFileSync } from 'node:fs';
import { join } from 'node:path';

// What the Playwright tests and the process that runs their API both need to know (see api.ts).

export const root = join(import.meta.dirname, '..', '..', '..');
export const apiUrl = 'http://localhost:5100';
export const webOrigin = 'http://localhost:4173';
export const e2eFile = join(root, '.tmp', 'e2e.json');
// The API's `Auth:CookieName` at its default, which the tests do not change (`AuthOptions`).
export const sessionCookieName = '__Secure-Session';

function required(name: string): string {
  const value = process.env[name];
  if (value === undefined || value === '') {
    throw new Error(`${name} is not set. Run the tests with \`mise web:test\`, which loads .env.`);
  }
  return value;
}

/** The API's settings for a run on its own database: that database, the setup secret, and the web preview as the one origin it allows. */
export function apiEnvironment(database: string, setupSecret: string): NodeJS.ProcessEnv {
  return {
    ...process.env,
    ConnectionStrings__Default: `Host=localhost;Port=${required('POSTGRES_PORT')};Database=${database};Username=josholson_api;Password=${required('POSTGRES_API_PASSWORD')}`,
    Auth__AllowedOrigin: webOrigin,
    Auth__SetupSecret: setupSecret,
    // The API honors a test's `X-Test-Scope` header (ADR 0033, a scoped database per test); a request without one runs on
    // `database`.
    Testing__ScopedDatabases: 'true',
    // Not `Production`, which refuses to start with the setting above on.
    ASPNETCORE_ENVIRONMENT: 'Testing',
    ASPNETCORE_URLS: apiUrl,
    Logging__LogLevel__Default: 'Warning',
  };
}

/** The server's owner, who makes and drops the databases the tests run on (the seed tool's login, from `.env`). */
export function ownerLogin(): { hostname: string; port: number; username: string; password: string; database: string } {
  return {
    hostname: 'localhost',
    port: Number(required('POSTGRES_PORT')),
    username: process.env['POSTGRES_USER'] ?? 'josholson',
    password: required('POSTGRES_PASSWORD'),
    database: 'postgres',
  };
}

/**
 * What `api.ts` made for this run, for a test: the database the API runs on for a request with no scope, the setup secret
 * it was started with, and the name of each seed layer's template (`seed.ts`), which a test's scope is cloned from.
 */
export function e2e(): { database: string; setupSecret: string; templates: Record<string, string> } {
  return JSON.parse(readFileSync(e2eFile, 'utf8'));
}
