import { spawnSync } from 'node:child_process';
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

/** The API's settings for a run on its own database: that database, and the web preview as the one origin it allows. */
export function apiEnvironment(database: string): NodeJS.ProcessEnv {
  return {
    ...process.env,
    ConnectionStrings__Default: `Host=localhost;Port=${required('POSTGRES_PORT')};Database=${database};Username=josholson_api;Password=${required('POSTGRES_API_PASSWORD')}`,
    Auth__AllowedOrigin: webOrigin,
    ASPNETCORE_URLS: apiUrl,
    Logging__LogLevel__Default: 'Warning',
  };
}

/** Mints an enrollment token (one use, 15 minutes) for the account, the way `JoshOlson.Api enroll` does on a server. */
export function mintEnrollmentToken(database: string): string {
  const result = spawnSync('dotnet', ['run', '--no-build', '--project', 'JoshOlson.Api', '--', 'enroll', 'E2E Admin'], {
    cwd: root,
    env: apiEnvironment(database),
    encoding: 'utf8',
  });
  if (result.status !== 0) throw new Error(`enroll failed (${result.status}):\n${result.stdout}${result.stderr}`);
  const lines = result.stdout.split('\n');
  const header = lines.findIndex((line) => line.startsWith('Enrollment token'));
  const token = lines[header + 1]?.trim();
  if (header < 0 || token === undefined || token === '') throw new Error(`no enrollment token in:\n${result.stdout}`);
  return token;
}
