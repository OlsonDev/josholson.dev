import { root } from './environment.ts';

/**
 * The seed layers the tests clone, each a layer in `JoshOlson.Database/seeds`. A test picks one with `test.use({ layer })`
 * (`scope.ts`). A layer is listed here for its template's name to be found once, when the run starts (`api.ts`).
 */
export const layers = ['minimal', 'more-tags'] as const;
export type Layer = (typeof layers)[number];

/**
 * The name of a layer's template database, which the seed tool builds first if the migrations or the layer changed since
 * (`mise seed:template`). The name carries a hash of both, so a rebake is a new name: ask again after one.
 */
export function templateName(layer: Layer): string {
  const result = Bun.spawnSync(['mise', 'seed:template', layer], { cwd: root, stdout: 'pipe', stderr: 'pipe' });
  if (result.exitCode !== 0) {
    throw new Error(
      `mise seed:template ${layer} failed (${result.exitCode}):\n${result.stdout.toString()}${result.stderr.toString()}`,
    );
  }
  const name = result.stdout
    .toString()
    .split('\n')
    .map((line) => line.trim())
    .findLast((line) => /^seed_layer_[a-z0-9]+$/.test(line));
  if (name === undefined)
    throw new Error(`mise seed:template ${layer} printed no template name:\n${result.stdout.toString()}`);
  return name;
}
