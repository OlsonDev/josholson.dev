import { mkdirSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';

// Starts the API for the Playwright tests on a database of its own, and stops it and drops the database afterwards. It is
// the `webServer` command of playwright.config.ts, so Playwright waits for the API to answer before the first test:
//   1. `mise seed:test` makes a database from the `minimal` layer (ADR 0033: a scoped database for each test run),
//   2. `JoshOlson.Api enroll` mints the one enrollment token the tests use to register a passkey, and
//   3. the API runs against that database, allowing the web preview's origin.
// Needs the dev containers and a provisioned API role (`mise db:provision`), and the environment `mise web:test` loads
// from `.env`. What the tests need to know is in `.tmp/e2e.json`.

const root = join(import.meta.dirname, '..', '..', '..');
export const apiUrl = 'http://localhost:5100';
export const webOrigin = 'http://localhost:4173';

function required(name: string): string {
  const value = process.env[name];
  if (value === undefined || value === '') {
    throw new Error(`${name} is not set. Run the tests with \`mise web:test\`, which loads .env.`);
  }
  return value;
}

const id = `e2e${Math.random().toString(36).slice(2, 10)}`;
const database = `seed_test_${id}`;
const env = {
  ...process.env,
  ConnectionStrings__Default: `Host=localhost;Port=${required('POSTGRES_PORT')};Database=${database};Username=josholson_api;Password=${required('POSTGRES_API_PASSWORD')}`,
  Auth__AllowedOrigin: webOrigin,
  ASPNETCORE_URLS: apiUrl,
  Logging__LogLevel__Default: 'Warning',
};

function run(command: string[], with_: Record<string, string | undefined> = process.env): string {
  const result = Bun.spawnSync(command, { cwd: root, env: with_, stdout: 'pipe', stderr: 'pipe' });
  if (result.exitCode !== 0) {
    throw new Error(
      `${command.join(' ')} failed (${result.exitCode}):\n${result.stdout.toString()}${result.stderr.toString()}`,
    );
  }
  return result.stdout.toString();
}

run(['mise', 'seed:test', id, 'minimal']);
run(['dotnet', 'build', 'JoshOlson.Api', '--nologo', '-v', 'q']);

const minted = run(
  ['dotnet', 'run', '--no-build', '--project', 'JoshOlson.Api', '--', 'enroll', 'E2E Admin'],
  env,
).split('\n');
const header = minted.findIndex((line) => line.startsWith('Enrollment token'));
const token = minted[header + 1]?.trim();
if (header < 0 || token === undefined || token === '') throw new Error(`no enrollment token in:\n${minted.join('\n')}`);

mkdirSync(join(root, '.tmp'), { recursive: true });
writeFileSync(join(root, '.tmp', 'e2e.json'), JSON.stringify({ apiUrl, webOrigin, database, enrollmentToken: token }));

const api = Bun.spawn(['dotnet', 'run', '--no-build', '--project', 'JoshOlson.Api'], {
  cwd: root,
  env,
  stdout: 'inherit',
  stderr: 'inherit',
});

let stopping = false;
function stop(): void {
  if (stopping) return;
  stopping = true;
  api.kill('SIGTERM');
  // Drops this run's database, and any other the seed tool no longer names.
  Bun.spawnSync(['mise', 'seed:clean'], { cwd: root, stdout: 'ignore', stderr: 'ignore' });
  process.exit(0);
}
process.on('SIGTERM', stop);
process.on('SIGINT', stop);
void api.exited.then((code) => {
  if (!stopping) process.exit(code === 0 ? 1 : code);
});
