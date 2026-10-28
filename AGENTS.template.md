# Web Application – AI Agent Guide

Use this file as a starting point for an AI agent guide. Replace every `[TODO: ...]` with facts and decisions for the
new repository, remove irrelevant guidance, and keep the instructions aligned with the actual codebase.

## Project overview

- **Product:** [TODO: what the application does and who uses it]
- **Stack:** [.NET version and language version], [web framework/UI], EF Core, MariaDB
- **Runtime/deployment:** [TODO: hosting model, operating system, container/runtime ports]
- **Current plan and handover:** [TODO: link to the project plan, if one exists]

### Architecture

Describe the actual projects and dependency directions here. A possible starting point:

- **Host:** application startup, composition, authentication wiring, and top-level routing.
- **Common:** small, dependency-light cross-cutting primitives and utilities.
- **UI:** reusable presentation components; must not depend on feature or persistence projects.
- **Infrastructure:** EF Core context, entities/configuration, migrations, and database integration.
- **Features:** vertical slices that own their pages/endpoints, queries, commands, and validation.
- **Tests:** unit, integration/characteristics, architecture, and browser tests as appropriate.

Prefer feature-oriented ownership and explicit module registration. Keep shared abstractions small; avoid coupling between
feature slices. Document exceptions and enforce important boundaries with architecture tests where practical.

## Agent behavior

- Examine the existing code and working tree before changing files. Preserve unrelated user changes.
- Follow the repository's existing patterns and architecture; make complete, focused changes without unrelated cleanup.
- Keep secrets, credentials, connection strings, personal data, and production database contents out of source, logs,
  test output, and external services.
- Validate submitted data on the server. Report errors through the application's standard result, logging, or UI
  mechanisms; do not hide failures behind broad catches or success-shaped fallbacks.
- Ask before making product or schema decisions that are not established by code or documentation.
- Update directly related documentation when behavior, architecture, or operational requirements change.

## Architecture and code conventions

Adapt these rules to the new repository; remove any that do not fit:

- Keep host composition in the host project, persistence code in Infrastructure, and feature behavior in its owning
  feature.
- Keep reusable UI independent of feature and database projects. Feature projects should not reference one another
  unless the architecture explicitly permits it.
- Make a type `public` only when another production assembly must use it, or the host requires it (for example
  `Program` for `WebApplicationFactory`). Everything else is `internal`, including options, stores, health checks,
  assembly markers and EF migrations/design-time factories. Tests see internals through `InternalsVisibleTo`; do not
  make a type public for a test. Generated Razor components are always public (the Razor compiler cannot emit internal
  components). Re-check visibility when adding a type, and make newly scaffolded migrations `internal`. This does not
  apply to test projects.
- Do not use primary constructors on classes or structs: declare `private readonly` fields (`_camelCase`) and an
  explicit constructor. Primary constructors (positional parameter lists) are allowed, and preferred, on `record` and
  `record struct` types, which are small immutable data carriers. This applies to test projects too.
- XML documentation must be complete: every `<param>` (including positional record parameters), `<typeparam>` and
  `<exception>` thrown by a documented member. Roslynator only reports these (RCS1140/1141/1142) when
  `GenerateDocumentationFile` is true, which the IDE always does; check with
  `dotnet build -p:GenerateDocumentationFile=true -p:TreatWarningsAsErrors=false --no-incremental`.
- Use the repository's established dependency injection and module-registration patterns.
- Use the repository's established result/error and validation conventions. Validate before mutating state.
- Use the configured time abstraction (for .NET, prefer `TimeProvider`); avoid direct local-clock calls in business
  logic.
- Always specify the `DateTimeKind` when creating a `DateTime` (`new DateTime(…, DateTimeKind.Utc)`; use `Unspecified`
  for zone-less values). This applies to test projects too.
- Keep route and external data formats stable unless a change is intentional and documented.
- For any authentication or authorization flow, protect state-changing requests, validate local redirect targets, and
  make authorization explicit. Default to requiring authentication when that matches the product.

## MariaDB and data access

- MariaDB is the application datastore. Use EF Core for ordinary persistence; add another data-access technology only
  when the project has a documented reason.
- Inject `IDbContextFactory<TContext>` where appropriate and use a short-lived context per operation. Do not retain a
  context in a singleton or long-lived UI circuit/component.
- Read paths should be no-tracking and project only the columns needed into display/response models. Avoid loading
  whole entity graphs for display, N+1 queries, and filtering large result sets in memory.
- Write paths must validate and check conflicts before changing data. Use transactions and suitable isolation when
  correctness depends on checks and writes being atomic. Map expected unique-key, foreign-key, and concurrency
  conflicts to application-level errors rather than server errors.
- Use restrictive foreign keys unless the product explicitly requires database cascades. Guard deletes in application
  code when related records must be preserved.
- Evolve the schema through explicit EF Core migrations with usable `Down` methods. Preserve existing data and test
  migrations against representative schemas.
- MariaDB DDL may not be transactional. Make migrations safe to retry after partial failure; use self-guarding SQL
  where required by the provider and migration design.
- If application instances migrate on startup, serialize migration execution using a MariaDB named lock on a dedicated
  connection. Verify repeated and concurrent startup behavior.
- Use parameterized queries (EF Core LINQ/`FormattableString`, or `DbParameter`s) instead of string formatting,
  interpolation or concatenation to build SQL. Identifiers cannot be parameters: use fixed literal statements for them.
- Use `utf8mb4` end to end. Preserve Unicode in source, database connections, pages, and tests; never repair or replace
  text through lossy transcoding.
- Keep database command logging free of credentials, personal data, and large or sensitive values. Never print database
  dumps or production rows in test output.

## Web, UI, and configuration

- Follow the project's chosen UI framework and design system; do not introduce a second framework or ad-hoc controls
  without a clear need.
- Design mobile-first and verify key flows at narrow and desktop widths.
- Prefer static/server-rendered content when it does not need client interactivity. Add interactivity only where the
  user experience requires it.
- Validate configuration at startup. Use local secret storage for development and environment/secret-manager
  configuration in deployment. Never commit secrets.
- Use structured logging and the built-in logging abstractions. Avoid logging sensitive values or response bodies.
- Make health and readiness endpoints reflect the intended dependencies; keep liveness checks independent of remote
  dependencies to avoid recursive or misleading health failures.
- Keep state-changing operations on appropriate non-GET methods and enable the framework's antiforgery protections.

## Testing and quality

Prefer tests that exercise the real behavior and boundaries:

- Unit tests for isolated logic and fast feedback.
- Integration/characteristics tests for database, HTTP, authentication, and business-rule behavior.
- Browser/UI tests for critical user flows and responsive behavior.
- Architecture tests for project boundaries and prohibited dependencies.

Use a real MariaDB instance for database behavior tests (for example, through Testcontainers); do not substitute
EF InMemory or SQLite when validating MariaDB-specific behavior. Seed tests with synthetic data, not production dumps.
Cover invalid input, boundary cases, concurrency/conflicts, and important business rules—not only happy paths.

### End-of-task routine

Run the smallest relevant checks, then expand when results warrant it:

1. Build the affected solution/project with warnings treated as errors.
2. Run the repository formatter and verify formatting.
3. Run targeted tests; run the full suite when the change affects shared behavior or project-wide contracts.
4. Inspect the final diff and ensure no generated artifacts, secrets, or unrelated changes are included.
5. Update the project plan or handover only if the repository has one and the task changes it.

**Repository commands — fill these in for the new project:**

- Build: `[TODO: build command]`
- Format/check: `[TODO: formatter commands]`
- Test: `[TODO: test command]`
- Migration review: `[TODO: EF migration/script command]`

## Performance and reliability

- Measure before optimizing. Prefer efficient SQL projections, server-side filtering/pagination, and bounded result sets.
- Avoid per-request or per-user copies of large, stable lookup data. Any cache of mutable data needs a freshness policy,
  invalidation path, and regression test.
- Do not add retries blindly: retry only transient operations that are safe to replay, and account for transactions and
  duplicate writes.
- Keep long-lived UI state small. Do not retain database contexts or large entity graphs in circuits or components.
- Add benchmarks only for demonstrated hot paths or when a performance requirement calls for them.

## Deployment and operations

Document the repository's actual deployment setup here:

- Runtime image and non-root execution: [TODO]
- Listening address/port and health probe: [TODO]
- Database backup/restore and migration procedure: [TODO]
- Required secrets/certificates and rotation procedure: [TODO]
- Logging, monitoring, and resource limits: [TODO]

Use supported .NET runtime and SDK versions. Keep local development, tests, and production configuration consistent
without copying production secrets or data into development.

## Project-specific decisions

Record only decisions that are binding for this repository:

- **Business rules:** [TODO: link or summarize]
- **Security/privacy requirements:** [TODO]
- **Data retention and deletion:** [TODO]
- **Versioning/release process:** [TODO]
- **Known constraints and gotchas:** [TODO]
