import { readdirSync, readFileSync, statSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';

// What `add-ui.ts` and `diff-ui.ts` share: finding the files of a component tree, and making the CLI's output ours.

/** Every file under `path`, or `path` itself when it is a file. */
export function* filesUnder(path: string): Generator<string> {
  if (statSync(path).isFile()) {
    yield path;
    return;
  }
  for (const entry of readdirSync(path)) yield* filesUnder(join(path, entry));
}

/** The CLI spells an import of a TypeScript file with `.js`. We import `.ts` everywhere (ADR 0045), so this rewrites them. */
export function rewriteImports(root: string): void {
  for (const file of filesUnder(root)) {
    if (!file.endsWith('.svelte') && !file.endsWith('.ts')) continue;
    const text = readFileSync(file, 'utf8');
    const rewritten = text.replaceAll(/(from "[^"]+)\.js"/g, '$1.ts"');
    if (rewritten !== text) writeFileSync(file, rewritten);
  }
}
