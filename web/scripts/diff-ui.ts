import { spawnSync } from 'node:child_process';
import { cpSync, existsSync, mkdirSync, readdirSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { join, relative } from 'node:path';

import { filesUnder, rewriteImports } from './ui-support.ts';

// Shows how the shadcn-svelte components we have differ from what the CLI would copy in today (ADR 0045):
//   bun scripts/diff-ui.ts [component]...        (`mise web:ui:diff [component]...` runs it)
// It changes nothing in web/. It copies the CLI's settings into a scratch project under .tmp/, adds the components there
// (all the ones we have, unless some are named), makes them ours the way `add-ui.ts` does, and prints a diff for each file
// that differs, and which files only one side has. What it shows is our edits and upstream's changes together: a file
// that is only ours to edit shows as the difference it is, and an edit of ours is the difference left after the
// upstream change is taken (see the update workflow in ADR 0045).
const web = join(import.meta.dirname, '..');
const ours = join(web, 'src', 'lib', 'components', 'ui');
const scratch = join(web, '..', '.tmp', 'ui-diff');
const theirs = join(scratch, 'src', 'lib', 'components', 'ui');

const isDirectory = (path: string): boolean => statSync(path).isDirectory();
const requested = process.argv.slice(2);
const components = requested.length > 0 ? requested : readdirSync(ours).filter((name) => isDirectory(join(ours, name)));

rmSync(scratch, { recursive: true, force: true });
mkdirSync(join(scratch, 'src', 'routes'), { recursive: true });
mkdirSync(join(scratch, 'src', 'lib'), { recursive: true });
for (const file of ['package.json', 'components.json', 'tsconfig.json', '.prettierrc', 'bunfig.toml'])
  cpSync(join(web, file), join(scratch, file));
cpSync(join(web, 'src', 'routes', 'layout.css'), join(scratch, 'src', 'routes', 'layout.css'));
cpSync(join(web, 'src', 'lib', 'utils.ts'), join(scratch, 'src', 'lib', 'utils.ts'));

// A scratch project has no dependencies installed, and the CLI would run `svelte-kit sync` (through `npx`, which this
// toolchain does not have) unless it finds what Kit 3's `sync` writes, so it gets an empty one of that.
mkdirSync(join(scratch, 'node_modules', '$app'), { recursive: true });
writeFileSync(join(scratch, 'node_modules', '$app', 'tsconfig.json'), '{}');
const added = spawnSync(
  'bun',
  ['x', 'shadcn-svelte@latest', 'add', '--yes', '--no-deps-install', '--skip-preflight', ...components],
  { cwd: scratch, encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] },
);
if (added.status !== 0) {
  console.error(`The CLI could not fetch the components:\n${added.stdout}${added.stderr}`);
  process.exit(added.status ?? 1);
}
rewriteImports(join(scratch, 'src', 'lib'));
spawnSync('bun', ['fmt', '--no-script', join(scratch, 'src', 'lib')], {
  cwd: scratch,
  stdio: ['ignore', 'ignore', 'inherit'],
});

const differing: string[] = [];
const onlyOurs: string[] = [];
const onlyTheirs: string[] = [];
for (const name of components) {
  const here = join(ours, name);
  const there = join(theirs, name);
  const ourFiles = existsSync(here) ? [...filesUnder(here)].map((file) => relative(ours, file)) : [];
  const theirFiles = existsSync(there) ? [...filesUnder(there)].map((file) => relative(theirs, file)) : [];
  for (const file of ourFiles) {
    if (!theirFiles.includes(file)) onlyOurs.push(file);
    else if (spawnSync('git', ['diff', '--no-index', '--quiet', join(ours, file), join(theirs, file)]).status !== 0) {
      differing.push(file);
    }
  }
  for (const file of theirFiles) if (!ourFiles.includes(file)) onlyTheirs.push(file);
}

for (const file of differing) {
  const diff = spawnSync('git', ['diff', '--no-index', '--no-color', '--', join(ours, file), join(theirs, file)], {
    encoding: 'utf8',
  });
  console.log(`\n=== ${file} (- is ours, + is what the CLI copies in today)\n${diff.stdout}`);
}
if (onlyOurs.length > 0) console.log(`\nOnly ours (an addition, or removed upstream):\n  ${onlyOurs.join('\n  ')}`);
if (onlyTheirs.length > 0) console.log(`\nOnly upstream (new there, or one we deleted):\n  ${onlyTheirs.join('\n  ')}`);
console.log(
  differing.length + onlyOurs.length + onlyTheirs.length === 0
    ? `\nNo difference in ${components.length} component(s): what we have is what the CLI copies in today.`
    : `\n${differing.length} file(s) differ, ${onlyOurs.length} only ours, ${onlyTheirs.length} only upstream.`,
);
rmSync(scratch, { recursive: true, force: true });
