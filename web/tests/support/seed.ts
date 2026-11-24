import { join } from 'node:path';

import { Glob } from 'bun';

import { root } from './environment.ts';

/**
 * The seed layers the tests clone, and `blank`, the schema with no rows (`JoshOlson.Database/seeds`). A test picks one
 * with `test.use({ layer })` (`scope.ts`). A layer is listed here for its template's name to be found once, when the run
 * starts (`api.ts`).
 */
export const layers = ['blank', 'account', 'some-tags', 'minimal', 'more-tags'] as const;
export type Layer = (typeof layers)[number];

function failure(command: string[], result: Bun.SyncSubprocess<'pipe', 'pipe'>): Error {
  return new Error(
    `${command.join(' ')} failed (${result.exitCode}):\n${result.stdout.toString()}${result.stderr.toString()}`,
  );
}

/**
 * Builds the seed tool, so that asking it for a name is the built program (about 0.4 s) and not `dotnet run` (about 2 s,
 * for each of the layers above).
 */
export function buildSeedTool(): void {
  const command = ['dotnet', 'build', 'JoshOlson.Seed', '--nologo', '-v', 'q'];
  const result = Bun.spawnSync(command, { cwd: root, stdout: 'pipe', stderr: 'pipe' });
  if (result.exitCode !== 0) throw failure(command, result);
}

function seedToolDll(): string {
  const found = [...new Glob('JoshOlson.Seed/bin/Debug/*/JoshOlson.Seed.dll').scanSync({ cwd: root })];
  const [dll] = found;
  if (found.length !== 1 || dll === undefined) {
    throw new Error(`Expected one built JoshOlson.Seed.dll under JoshOlson.Seed/bin/Debug, found ${found.length}.`);
  }
  return join(root, dll);
}

/**
 * The name of a layer's template database, which the seed tool builds first if the migrations or the layer changed since
 * (`seed template`). The name carries a hash of both, so a rebake is a new name: ask again after one. `blank` is the
 * schema template. The tool runs with the environment of the run (`mise web:test` loads `.env`), and needs
 * `buildSeedTool` to have run.
 */
export function templateName(layer: Layer): string {
  const command = ['dotnet', seedToolDll(), 'template', layer];
  const result = Bun.spawnSync(command, { cwd: root, stdout: 'pipe', stderr: 'pipe' });
  if (result.exitCode !== 0) throw failure(command, result);
  const name = result.stdout
    .toString()
    .split('\n')
    .map((line) => line.trim())
    .findLast((line) => /^seed_(layer|schema)_[a-z0-9_]+$/.test(line));
  if (name === undefined)
    throw new Error(`${command.join(' ')} printed no template name:\n${result.stdout.toString()}`);
  return name;
}
