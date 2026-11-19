import { readFileSync } from 'node:fs';
import { join } from 'node:path';

// What the Playwright tests and the process that runs their API both need to know (see api.ts).

export const root = join(import.meta.dirname, '..', '..', '..');
export const apiUrl = 'http://localhost:5100';
export const webOrigin = 'http://localhost:4173';
export const e2eFile = join(root, '.tmp', 'e2e.json');

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
    ASPNETCORE_URLS: apiUrl,
    Logging__LogLevel__Default: 'Warning',
  };
}

/** What `api.ts` made for this run, for a test: the setup secret its API was started with. */
export function e2e(): { database: string; setupSecret: string } {
  return JSON.parse(readFileSync(e2eFile, 'utf8'));
}
