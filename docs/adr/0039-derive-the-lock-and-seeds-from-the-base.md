# ADR 0039: Derive the lock and the seeds from the base branch

- Status: Accepted
- Date: 2026-11-12

## Context

[ADR 0034 (the seed lock file and branch-safe migration)](0034-seed-lock-file-and-branch-safe-migration.md) makes `seeds:migrate` move the layers forward from the working tree, and refuses a migration whose checksum no longer matches the lock. On a feature branch that is the wrong default. A branch iterates on its own unmerged migration over several commits, and every edit changes a checksum the lock recorded on the last run, so each one needs `seeds:accept` or `--relock`. When `main` moves, the lock and the layers conflict at merge, and a conflict in generated files has no good manual resolution.

The lock and the layers the tool wrote are derived data: they are a function of what `main` has (its lock, its layers, its migrations) and the migrations the branch adds. Only a layer somebody wrote is input.

## Decision

- **The base is `origin/main`.** `seeds:migrate` reads its lock and layers with `git show` and `git ls-tree`: nothing is checked out, and the working tree and the branch you are on are untouched. `--base <ref>` names a commit instead (the one before the first iteration, while a project is still working on `main` itself). By default the command first runs `git fetch origin main`, which moves only `origin/main`; `--no-fetch` skips it, and a fetch that fails (offline, a credential it would have asked for) warns and uses `origin/main` as last fetched. The commit used is printed.
- **When the base is the input.** If the working lock and the migrations disagree (typically: the branch edited its own migration), or `--base` is given, the lock and layers are derived from the base's, moved through every migration after the base's lock (`LayerMover`, the same steps as a forward move). The branch's lock and layers are overwritten with the result, so a merge conflict is resolved by taking the base's and running `seeds:migrate` again.
- **A migration the base has locked is immutable.** If one has changed or is missing on the branch, `seeds:migrate` refuses, naming the base's commit. For a missing one it prints the `git checkout <commit> -- <file>` that brings it in without merging the rest of `main`.
- **A layer file is the tool's or somebody's.** The lock records the SHA-256 of each layer file the tool wrote (`layers:`). The tool owns a file if it still hashes to that value, or if it is byte-identical to the base's copy (it has been merged, so the base's tool owns it). Everything else, a layer you wrote with `seeds:export` or edited since, is yours: when the base is the input it is kept exactly as it is, read against the new schema, and built at the newest version, and a table or column a migration renamed since is a hard error naming the layer, never something to guess at. The base is read for this only when a layer fails the hash test.
- **No test against git.** The logic runs on a snapshot a test makes up (`ISnapshotSource`); `GitSnapshotSource` is a thin wrapper over a few `git` commands, tried against this repository's own history.

## Consequences

- Editing an unmerged migration costs nothing: no checksum to accept, no relock. Immutability applies to what `main` has.
- The lock and generated layers stop being a merge problem. What remains yours is checked, not rewritten.
- A branch needs a base with a lock. Before `main` has one the command says so, and `--relock` is still there.
- A migration must give the same result for the same rows every time (a `DEFAULT now()` on a new column would make a child layer appear to update every row it inherits), because each layer is migrated in a database of its own.
- Reference data in repeatable migrations is written for the newest schema, so at the base's version one may not apply. Rows are put in with foreign keys not enforced and checked at the newest version, where a layer pointing at reference data that is gone fails with the layer and the skipped repeatable named.
- Not decided here: `seeds:accept`, and whether `seeds:check` should compare the lock with a derivation from the base.
