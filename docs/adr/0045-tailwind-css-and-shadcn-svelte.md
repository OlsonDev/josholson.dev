# ADR 0045: Tailwind CSS and shadcn-svelte for the interface

- Status: Accepted
- Date: 2026-11-19

## Context

`web/` started with plain CSS (a few variables and a page's own `<style>`), which was fine for one page. The admin area is about to be most of the site's interface: a sidebar, forms, tables, dialogs, and editors for a resume's thirteen kinds of section. Writing those from scratch is a large amount of accessible, keyboard-friendly UI for one person to own. [shadcn-svelte](https://shadcn-svelte.com) is a set of components (on bits-ui and Tailwind CSS) that are copied into the project, so they are ours to change, not a dependency to upgrade around.

Tailwind was close to decided already: `tag.tag.color` is a Tailwind palette name (the `color.tailwind` domain), so the front end needs Tailwind's colors in any case.

## Decision

- **Tailwind CSS v4, site-wide**, through `@tailwindcss/vite`. Its reset applies to every page, and the public site is one page, ported to utility classes. `src/routes/layout.css` is the one stylesheet (Tailwind, the theme variables, and the base layer) and `src/routes/+layout.svelte` imports it. The old `app.css` is gone.
- **shadcn-svelte components live in `src/lib/components/ui`**, with the CLI's settings in `web/components.json`: aliases on `#lib` (Kit 3 has no `$lib`), the `vega` preset (Lucide icons, Inter), and the `neutral` base color. They are TypeScript (`.svelte` with `lang="ts"`, and `.ts` for the modules and types), and the CLI writes every import of one of them with `.js`. We import `.ts` everywhere, and a spike showed both compile and build, so `mise web:ui:add <component>` runs the CLI and rewrites its imports to `.ts` and formats the TypeScript with `bun fmt`. Types come from the same modules (`import type { ButtonProps } from '#lib/components/ui/button/index.ts'`).
- **The components are ours.** Their TypeScript is formatted and linted like the rest of `web/` and passes the lint rules unchanged (`bun fmt` does not reach `.svelte` files, so those keep the CLI's tabs and quotes), and an edit of ours survives, since adding a component that is already there asks before overwriting it. The CLI itself runs on Bun with a warning that only Node is supported, and works. `init` needs a terminal to confirm its stylesheet changes, and was run once.
- **Ours and theirs are in different folders.** `src/lib/components/ui/` holds shadcn-svelte's components and nothing else, each either what the CLI copies in or marked as customized. Our own components are in `src/lib/components/` beside it (`app-sidebar.svelte`, `page-header.svelte`), in a folder of their own once a feature has several (`components/resume/`). A component of ours that wraps or extends a `ui` one goes there, not into `ui/`. The CLI's *blocks* (`sidebar-01` and the like) also install into `src/lib/components/`, and nothing here uses them, since we add `ui` components only; adding one would put its files beside ours, so check for a name that is already taken.
- **A customized component says so.** An edit in place (a bug fix, which is the only reason to make one) is its own commit, named for the component and the reason, and the file gets a first comment, `// Customized from shadcn-svelte: <why>` (`<!-- ... -->` in a `.svelte` file). `grep -r "Customized from shadcn-svelte" src/lib/components/ui` lists them, and `mise web:ui:diff` shows the difference itself.
- **Updating is by hand, in git, with the diff first.** The CLI has no update, diff, or per-component version, and the registry serves only the latest. The changelog (`shadcn-svelte.com/docs/changelog`, with an RSS feed) says what changed. Most behavior, such as focus and keyboard handling, is in `bits-ui`, a normal dependency that `bun update` moves, and what is copied is mostly markup and classes, so a fix upstream rarely needs the copy to change.
  1. `mise web:ui:diff [component]` changes nothing: it adds the components to a scratch project (`.tmp/ui-diff`), makes them ours the way `mise web:ui:add` does, and prints how they differ from ours, with the files only one side has. With no edits of ours, the difference is purely upstream's.
  2. **A component with no edit of ours:** `mise web:ui:add -o <name>` takes upstream's, and `git diff` is its change. Commit it alone.
  3. **A component with an edit of ours:** a three-way merge. The base is the version we last took from upstream, which git has, since an edit is never in the same commit as a sync (`git show <sync commit>:<path>`). Ours is the working file, and theirs is the scratch copy from step 1. `git merge-file <ours> <base> <theirs>` leaves conflict markers only where our edit and upstream's change touch the same lines. Commit the merge alone, and check the customized comment still says why.
  4. **When to look:** when something breaks visually, when the changelog names a component we use, and when `bits-ui` or `shadcn-svelte` is bumped. `bun x shadcn-svelte@latest apply <preset>` changes the look, if we ever want another.
- **Dark mode follows the system.** shadcn's dark variant is a class (`.dark`), so a few lines in `app.html` set it before the first paint and keep it in step with `prefers-color-scheme`. A toggle is a later addition.
- **The admin is built from these**: a sidebar (the `sidebar-01` block's shape, flat, with no sections) around every page behind a session, and the sign-in in a card in the middle of the page.

## Alternatives considered

- **Plain CSS and our own components.** Nothing to learn and nothing to update, and every dialog, menu, and focus trap written and kept accessible by us.
- **A component library as a dependency** (Skeleton, Flowbite, Melt UI's styled kits). Less code in the repository, and an upgrade or a design that does not fit is not ours to change. shadcn-svelte's components are the same bits-ui underneath, with the code in our tree.
- **Tailwind for the admin only.** Tailwind's reset and the theme would be loaded for admin pages, and the public pages would keep plain CSS. A visit to the admin in the same tab would leave the reset applied to the public pages. One page of public CSS is cheap to port, so there is no reason to keep two systems.

## Consequences

- **Staying current is our job, and cheap only while edits are few.** Prefer the theme variables in `layout.css`, `class` props, and wrappers in our own folder to an edit in `ui/`.
- **A re-added component brings `.js` imports back**, which is why `mise web:ui:add` rewrites them. Adding components with the CLI directly leaves them, and they work, but `svelte-check` and the repository's imports then disagree.
- **A tag's color is a palette name stored in the database**, and Tailwind finds classes by scanning source for whole strings, so a color must be mapped to class names that appear in the code (a static map), never built as `bg-${color}-500`. The pinned notes on tag colors have the rest.
- **The theme is shadcn's variables** (`--primary`, `--muted`, ...) in `layout.css`, so a change of look is a change there, and `bun x shadcn-svelte@latest init` can regenerate it for another preset.
- **More dependencies** (Tailwind, bits-ui, Lucide, tailwind-variants, the animation plugin, a font), all build-time or small on the page, and all moving.
