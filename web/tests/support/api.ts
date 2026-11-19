import { randomBytes } from 'node:crypto';
import { mkdirSync, writeFileSync } from 'node:fs';
import { dirname } from 'node:path';
import { apiEnvironment, e2eFile, root } from './environment.ts';

// Starts the API for the Playwright tests on a database of its own, and stops it and drops the database afterwards. It is
// the `webServer` command of playwright.config.ts, so Playwright waits for the API to answer before the first test:
//   1. `mise seed:test` makes a database from the `minimal` layer (ADR 0033: a scoped database for each test run),
//   2. the API is built, and
//   3. it runs against that database, with a setup secret of its own and the web preview's origin as the one it allows.
// The database starts with no account and no passkey, so setup is open: the browser tests set up with that secret, as
// the owner does. The secret and the database are in `.tmp/e2e.json` (`e2e()`). Needs the dev containers and a
// provisioned API role (`mise db:provision`), and the environment `mise web:test` loads from `.env`.

const id = `e2e${Math.random().toString(36).slice(2, 10)}`;
const database = `seed_test_${id}`;
const setupSecret = randomBytes(24).toString('base64url');

function run(command: string[]): void {
  const result = Bun.spawnSync(command, { cwd: root, stdout: 'pipe', stderr: 'pipe' });
  if (result.exitCode !== 0) {
    throw new Error(
      `${command.join(' ')} failed (${result.exitCode}):\n${result.stdout.toString()}${result.stderr.toString()}`,
    );
  }
}

run(['mise', 'seed:test', id, 'minimal']);
run(['dotnet', 'build', 'JoshOlson.Api', '--nologo', '-v', 'q']);

mkdirSync(dirname(e2eFile), { recursive: true });
writeFileSync(e2eFile, JSON.stringify({ database, setupSecret }));

const api = Bun.spawn(['dotnet', 'run', '--no-build', '--project', 'JoshOlson.Api'], {
  cwd: root,
  env: apiEnvironment(database, setupSecret),
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
