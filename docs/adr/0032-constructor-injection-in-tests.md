# ADR 0032: Constructor injection in tests

- Status: Accepted
- Date: 2026-11-02

## Context

Every integration test opened its own connection, built the repositories it used by hand, and read the cancellation token from `TestContext`. A test that needed a service built the whole container as well. That setup was most of each test, it repeated in every file, and it grew with every new dependency (a clock, options, a second repository).

## Decision

- **`Xunit.DependencyInjection`** builds one container for the test run, from a `Startup` class in `Common/Testing`. A test class takes what it needs in its constructor, and each test gets its own scope, so scoped services (the open connection, every `*Service` and `*Repository`) are fresh per test and disposed when it ends.
- `Startup` uses the API's own registrations (`AddDatabase`, `AddServicesAndRepositories`, and so on) against the dev database, so a test resolves the same graph a request does.
- A class named `*TestData` (a helper that creates and removes a feature's rows) is registered as scoped by the same kind of scan, so tests take it in their constructor too.
- The in-memory API (`WebApplicationFactory<Program>`) and an `HttpClient` for it are scoped services, built on first use. A test that never asks for them does not start the app.
- The cancellation token stays out of the scope: a test class keeps it in a field initialised from `TestContext.Current.CancellationToken`.
- Constructor parameters only. xUnit offers no hook to inject a service into a test method's parameters, so a `[Theory]` takes its data as arguments and its services from the constructor.

## Alternatives considered

- **A base class** that builds a provider in `InitializeAsync` and exposes services as properties. It needs no package, but each service needs a line in the base class.
- **`Xunit.Microsoft.DependencyInjection`** is built around a fixture class per test class, so every class carries boilerplate that this package removes.

## Consequences

- The package adds a test framework attribute to the assembly, and `XunitStartupFullName` in the project file points it at `Startup` because the class is not in the root namespace.
- The container is built once, so a test must not leave state in a singleton. There are none today.
- Cleanup of inserted rows is unchanged: tests still delete what they create in a `finally`.
