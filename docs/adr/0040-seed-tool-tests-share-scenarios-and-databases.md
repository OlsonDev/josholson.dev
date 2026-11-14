# ADR 0040: The seed tool's tests share scenarios and databases

- Status: Accepted
- Date: 2026-11-13

## Context

The seed tool's tests (`JoshOlson.Database` and `JoshOlson.Seed`) work against a real Postgres server and the `tools` container: they build templates, clone databases, and run `seeds:migrate` ([ADR 0033 (a scoped database per test)](0033-scoped-databases-per-test.md), [ADR 0034 (the seed lock file and branch-safe migration)](0034-seed-lock-file-and-branch-safe-migration.md)). Measured on 2026-11-12, `mise run test` took about 8.7 seconds, set by `JoshOlson.Database` at about 7 seconds of wall time for 63 seconds of summed test time.

The single steps were not what cost: cloning the real schema template took 24ms and dropping a database 12ms. The tests repeated their setup. The 21 tests of moving layers ran `SeedMigrator.MigrateAsync` about 70 times, about 50 of them only to reach a starting point. The 21 export tests built 19 databases, eight of them the same one. Sixteen other tests each built a database for one assertion. A class is an xUnit collection, classes run side by side, and they wait on each other at the server, so less total work makes every class faster.

The tests also asserted facts about the real seed layers (3 tags, 22 tags, a color, an ID) in nine files, so adding a tag to `minimal.yaml` would have broken them.

## Decision

- **A test may cover several behaviors.** Tests with the same arrange and act are one test with every assertion, named for the scenario. A test may also walk one database or one workspace through several states and assert after each. This trades the usual one behavior per test for speed, for the seed tool's tests only.
- **A starting point is built once per test run and kept as files.** A state that takes runs of `seeds:migrate` to reach is built by real runs the first time a test asks (`FooScenarios`), and its files are written into each test's own directory (`FakeSeedRepository.StartAtAsync`), so tests stay independent. Nothing is checked in as a golden file: a test builds one starting point twice and compares the bytes, which is what the cache rests on.
- **Templates for fixed test text stay on the server.** A template's name is a hash of its files, so one built from migrations or layers whose text never changes is found again on the next run. `seed:clean` drops the ones no file names any more. Only a test that asserts a template was built makes a new one and drops it.
- **Tests use fixture layers, not the real ones.** `FixtureSeeds` has a root and a child over the real schema that change only when a test needs them to. Tests whose subject is the real layers (`seeds:check`, `bake`) still read them, and assert nothing about their rows.
- **One of each shared object per run** (`SeedTestServer`): the database operations, the container tools, the real lock, the real schema template, and the tool. None keeps state a test changes.
- **Classes are split where that lets them run side by side**, since the tests inside one class run one after another.

## Alternatives considered

- **Checked-in golden files for the starting points.** They go stale when the lock's format or the layer writer changes, and somebody has to regenerate them. Building them once per run costs a few seconds, shared by every class that waits for them.
- **Keep one behavior per test and share only the fixtures.** Most of the cost was the act, not the arrange: each test would still run its own migrate or export.
- **Mock the server.** The tool is SQL, `CREATE DATABASE ... TEMPLATE`, and Waypoint; a test that does not run them checks little.

## Consequences

- `mise run test` takes about 6 seconds, from about 8.7. `JoshOlson.Database` alone is about 4.7 seconds of wall time (from 7.6) and `JoshOlson.Seed` 1.7 (from 3.2), with 697 and 45 tests (from 703 and 53) and every assertion kept.
- The summed test time of `JoshOlson.Database` is 54 to 60 seconds, close to the 63 it was, and the targets set beforehand (3.5 seconds of wall time, 25 summed, no class over 2.5) were not reached. The six classes that move layers are still the slowest, at 2.7 to 4.6 seconds each.
- A failing scenario test names a scenario, not one behavior: the assertion that failed says which.
- A broken shared starting point fails every test that starts from it, and the first failure is the one to read.
- The first run on a clean server builds every template and is much slower than the ones after it.
- Templates from test text accumulate on the dev server until `mise run seed:clean`.
- A test that changes a fixture layer's text changes its template's name for every test that uses it.
