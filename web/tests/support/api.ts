import { mkdirSync, writeFileSync } from 'node:fs';
import { dirname } from 'node:path';
import { apiEnvironment, e2eFile, mintEnrollmentToken, root } from './environment.ts';

// Starts the API for the Playwright tests on a database of its own, and stops it and drops the database afterwards. It is
// the `webServer` command of playwright.config.ts, so Playwright waits for the API to answer before the first test:
//   1. `mise seed:test` makes a database from the `minimal` layer (ADR 0033: a scoped database for each test run),
//   2. the API is built, and
//   3. it runs against that database, allowing the web preview's origin.
// A test mints the enrollment tokens it needs (`mintEnrollmentToken`), with the database named in `.tmp/e2e.json`. Needs
// the dev containers and a provisioned API role (`mise db:provision`), and the environment `mise web:test` loads from
// `.env`.

const id = `e2e${Math.random().toString(36).slice(2, 10)}`;
const database = `seed_test_${id}`;

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
writeFileSync(e2eFile, JSON.stringify({ database }));

// Minting one now proves the database and the API's settings work, before a test needs to.
mintEnrollmentToken(database);

const api = Bun.spawn(['dotnet', 'run', '--no-build', '--project', 'JoshOlson.Api'], {
  cwd: root,
  env: apiEnvironment(database),
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
