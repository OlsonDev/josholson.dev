import { spawnSync } from 'node:child_process';
import { rewriteImports } from './ui-support.ts';

// Adds shadcn-svelte components to src/lib/components/ui and makes them ours (ADR 0045: Tailwind CSS and shadcn-svelte):
//   bun scripts/add-ui.ts <component>...        (`mise web:ui:add <component>...` runs it)
// The CLI copies the components in, spelling every import of a TypeScript file with `.js`. We import `.ts` everywhere,
// so this rewrites them, and formats the TypeScript like the rest of web/ (`bun fmt` does not reach `.svelte` files).
// A component that is already here is left as it is, so an edit of ours survives, and `-o` (the CLI's `--overwrite`)
// takes the registry's again: `mise web:ui:add -o button`. To see first what would change, `mise web:ui:diff button`.
const components = process.argv.slice(2);
if (components.length === 0) {
  throw new Error('Usage: bun scripts/add-ui.ts <component>... (see shadcn-svelte.com/docs/components)');
}

// No stdin: with one, the CLI waits on a prompt that `--yes` should have answered.
const added = spawnSync('bun', ['x', 'shadcn-svelte@latest', 'add', '--yes', ...components], {
  stdio: ['ignore', 'inherit', 'inherit'],
});
if (added.status !== 0) process.exit(added.status ?? 1);

const roots = ['src/lib/components', 'src/lib/hooks', 'src/lib/utils.ts'];
for (const root of roots) rewriteImports(root);

spawnSync('bun', ['fmt', '--no-script', ...roots, 'components.json', 'src/routes/layout.css'], { stdio: 'inherit' });
