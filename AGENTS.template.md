# Web Application – AI Agent Guide

Use this file as a starting point for an AI agent guide. Replace every `[TODO: ...]` with facts and decisions for the
new repository, remove irrelevant guidance, and keep the instructions aligned with the actual codebase.

## Project overview

- **Product:** [TODO: what the application does and who uses it]
- **Stack:** [.NET version and language version], [web framework/UI], EF Core, MariaDB
- **Runtime/deployment:** [TODO: hosting model, operating system, container/runtime ports]
- **Current plan and handover:** [TODO: link to the project plan and handover/status file, if they exist. Tell the agent
  to read the handover first and to update it at the end of every milestone.]
- **Language rule:** [TODO: the language of the UI (and its locale) versus the language of code, identifiers, database
  objects, comments, docs, logs and commits. State a domain term mapping if the two differ.]
- **Reference implementation:** [TODO: optional, a read-only sibling repository to mirror for auth, options, deployment
  and tooling. List the deliberate differences so the agent does not copy them blindly.]
- **Precedence:** this file holds conventions, the plan holds the product spec. If the two, or a document and the
  code/environment, disagree, stop and ask the owner before choosing, then record the answer in the plan.

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

- [TODO: list private or irrelevant files the agent must never read, search, quote, edit or stage (for example a personal
  prompt history), and exclude them from searches, diffs and commits.]
- Work one milestone at a time, then stop and wait for the owner's review. Do not start the next milestone unprompted.
  Do not commit unless asked.
- After every milestone update the handover file: status table, what was delivered (files, tests, check results), the
  plan for the next milestone, environment facts and new learnings. Promote learnings that become conventions into this
  file or the plan, and keep secrets, connection strings and production data out of it.
- Ask questions first when scope, product behavior or schema is not established by code or documentation, and record
  the answers in the plan. Do not re-ask what the plan already decided.
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
- Use strongly typed IDs (for example Vogen) for entity keys instead of bare integers or GUIDs.
- Compute derived or denormalized data (composite keys, joined sort text) in exactly one place and reuse that helper from
  every writer, including import tools. Add a test that checks it.
- Use the configured time abstraction (for .NET, prefer `TimeProvider`); avoid direct local-clock calls in business
  logic.
- Time zones:
  - Store instants (things that happened or are recorded) in the database as UTC, and always as UTC on the wire (APIs,
    logs, serialization).
  - Resolve the **user's time zone** in this order, in one shared service (the "user clock"), and use it everywhere:
    1. The time zone stored on the **user profile** (IANA id), when the app has user accounts. Give the profile a time
       zone setting, validated against the known IANA ids, and preselect it from the browser zone on first login or
       registration.
    2. The **browser's** time zone, detected on the client (for example
       `Intl.DateTimeFormat().resolvedOptions().timeZone`) and made available to server-rendered/interactive
       components. This is the source for anonymous visitors and for users whose profile has no zone yet.
    3. A configured default zone (IANA id, for example `App:TimeZone`) when neither is known, such as during
       prerendering or in background jobs.
  - Convert UTC to the user's zone in one shared helper or component for display; never format a `DateTime` ad hoc.
    Background jobs, emails and exports acting for a user use that user's profile zone, not the server's.
  - Things planned in the future (appointments, schedules, recurring jobs, "at 09:00 on Monday") are stored as a local
    date/time **plus the IANA time zone id of the target scheduler or location**, not as UTC. A UTC instant computed today
    can be wrong after a DST or tz-rule change. Resolve to UTC only when the occurrence is due or must be compared.
  - Resolving "today", "the current year" or "this week" (defaults, validation, filters, reports) must not trust the
    browser clock. Take the instant from the server-side clock abstraction (`TimeProvider`) and evaluate it in the
    relevant zone: the user clock's zone (profile, then browser, then default) for user-facing rules, the target zone
    for scheduled items. Two users can be on different dates at the same instant; never compute "today" in UTC or the
    server's zone for them.
  - Use `DateOnly` for pure dates that have no time-of-day meaning.
  - Test the DST boundaries, a zone far from the server's (for example UTC+13 and UTC-10), a profile zone that
    differs from the browser zone, and a "today" that differs between two users at the same instant.
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
- Name database objects consistently (for example lowercase `snake_case`, plural table names) and map them explicitly.
- Use a case- and accent-insensitive collation where the product needs it (for example `utf8mb4_uca1400_ai_ci`) and put
  the charset in connection strings. Never use `ToLower()`/`ToUpper()` for case-insensitive comparisons; rely on the
  collation. Search with `LIKE` and escape `%`, `_` and `\`. Uniqueness that must ignore case or accents belongs in a
  unique index, not only in application code.
- Use concurrency tokens (for example a `version` column incremented by the application on every update) for records
  that can be edited concurrently, and surface conflicts to the user.
- When replacing the rows of a join table on update, delete the old links and save before adding the new ones: EF may
  otherwise insert before it deletes and hit a key or unique-index violation. Clean up orphans only for rows the
  operation touched, and treat a foreign-key failure while deleting an orphan as "still in use", not as an error.
- Prefer LINQ projections. Use raw SQL only where EF cannot translate the query (window functions, correlated
  `GROUP_CONCAT`), with fixed SQL fragments for identifiers and ordering and parameters for values, and test the raw SQL
  against the LINQ path for consistency.
- Do not use `EnableRetryOnFailure` or other blanket retry strategies.
- Evolve the schema through explicit EF Core migrations with usable `Down` methods. Preserve existing data and test
  migrations against representative schemas. If the owner approves rewriting the initial migration while nothing is
  deployed, record that exception explicitly (scope, end condition, how local databases are rebuilt) and keep the
  migration, every Designer file and the model snapshot in sync (a `HasPendingModelChanges` test guards this). After the
  first deployment, never edit a deployed migration again. Seed closed reference data in the migration and have a test
  that the seeded table equals the code constant; test fixtures that clear tables must not clear it.
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
- Data import tools and dumps: run them against the local database only; report counts, never rows or free text; wrap
  production dumps in a guard (expected schema version, target tables empty, reference data identical) and verify them by
  restoring into a scratch database and comparing fingerprints. Create dumps with the server's dump tool writing to a
  file inside the container and copy it out: Windows PowerShell `>` redirection writes UTF-16. Keep dumps with personal
  data git-ignored and delete every copy after use. Source files of legacy data are git-ignored, opened read-only, and
  never used in automated tests.

## Web, UI, and configuration

- Follow the project's chosen UI framework and design system; do not introduce a second framework or ad-hoc controls
  without a clear need.
- Design mobile-first and verify key flows at narrow and desktop widths (for example 390 px and 1440 px).
- Prefer static/server-rendered content when it does not need client interactivity (auth and error pages), and add
  interactivity only where the user experience requires it. Avoid double data loads during prerendering.
- Self-host fonts, scripts and styles; load nothing from CDNs. Send security headers (CSP with a per-request nonce on
  every `<script>`, frame and content-type protections, HSTS outside development) and treat the site as HTTPS-only.
- Culture and localization: when the UI is localized, configure request localization with the supported culture, set
  `<html lang>`, and make sure globalization is not invariant (`InvariantGlobalization=false`). Verify in a browser that
  the localized component-library strings actually appear.
- Avoid layout shifts caused by validation on blur: a banner or message that appears on blur can move the page between
  mouse-down and mouse-up and make a click on a suggestion or button miss. Show summary banners only after an explicit
  submit, and defer blur handlers that would change the layout.
- Custom composite controls must not pass `Required` to an inner input that is empty by design (for example a chip
  input); show required state and errors from the parent instead.
- Component tests (bUnit) cannot reproduce real focus, blur, Enter/Tab and layout behavior. Cover those with browser
  tests, including slow mouse interaction and geometry checks where alignment matters.
- Behind a reverse proxy, accept forwarded headers from known proxies only, enable WebSocket support for server-side
  interactive Blazor, and encrypt the data-protection key ring with a certificate kept outside the repository.
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

Use a real MariaDB instance for database behavior tests (for example, through Testcontainers, pinned to the production
image tag); do not substitute EF InMemory or SQLite when validating MariaDB-specific behavior. Seed tests with synthetic
data, not production dumps, preferably through the application's own commands. Tests must never touch the developer
database: environment variables override user-secrets in test hosts. Cover invalid input, boundary cases,
concurrency/conflicts, and important business rules—not only happy paths.

- Browser tests need a real HTTP listener (not the in-memory `TestServer`) when they drive Blazor circuits; install the
  browsers once after a build. Log in once and reuse the stored state, and account for login throttling in fixtures.
- Collation-dependent logic implemented in .NET (for example import de-duplication) is only an approximation of the
  database: add parity tests against the real database and keep the unique indexes as the safety net.
- Never pipe a long test run into `Select-Object -First N` (a cancelled run leaves the test host running and locks
  binaries). Write the output to a file and read the summary from it.

### End-of-task routine

Run the smallest relevant checks, then expand when results warrant it:

1. Build the affected solution/project with warnings treated as errors.
2. Run the repository formatter and verify formatting.
3. Run targeted tests; run the full suite when the change affects shared behavior or project-wide contracts.
4. Inspect the final diff and ensure no generated artifacts, secrets, or unrelated changes are included.
5. Update the project plan (milestone status, answered questions) and, at the end of every milestone, the handover file
   (when the repository has them and the task changes them).

**Repository commands — fill these in for the new project** (run them from the repository root; give paths relative to it):

- Build: `[TODO: build command]`
- XML-doc build: `[TODO: build with GenerateDocumentationFile=true and TreatWarningsAsErrors=false, no incremental]`
- Format/check: `[TODO: formatter commands]`
- Test: `[TODO: test command, plus the fast subset]`
- Add migration: `[TODO: EF migration command]`
- Migration review: `[TODO: EF migration/script command]`
- Import/dump scripts: `[TODO: if any, including how to answer confirmation prompts non-interactively]`
- Benchmarks: `[TODO: if any, including the baseline/compare workflow and the artifacts folder]`

Also note platform gotchas here: line endings (enforce one style with `.editorconfig`; agent file tools may write CRLF, so
normalize new files), `git -c safe.directory=…` when Git reports "dubious ownership", and Roslynator rules that the
build enforces (for example prefer `[0]` over `First()` on lists).

## Performance and reliability

- Measure before optimizing. Prefer efficient SQL projections, server-side filtering/pagination, and bounded result sets.
- **Keep rule:** keep an optimization only if a benchmark shows a gain in time, allocations or database round trips (or it
  is neutral on time and clearly cheaper in memory). Otherwise revert it and record in the handover what was tried, the
  numbers and why it was dropped. Do not add anything "because it is usually faster".
- Benchmarks (only for demonstrated hot paths): a console project (for example BenchmarkDotNet, in process) against a
  throwaway real database container (same image tag as production) with deterministic synthetic data at the current size
  and about ten times larger, plus a round-trip counter (a `DbCommandInterceptor`). Store results in git-ignored
  `artifacts/`, take a baseline first, and re-run the relevant benchmarks after each change. Keep it out of the normal test
  run and give it `InternalsVisibleTo`.
- Add a database index only with evidence: compare `EXPLAIN` and timings before and after at both data sizes (an index
  must match the sort order exactly, including the tie-breaker column, to avoid a filesort). Keep import/dump tooling and
  docs in step with every schema change, and run `ANALYZE TABLE` after a bulk load, which does not refresh optimizer
  statistics.
- Avoid per-request or per-user copies of large, stable lookup data. Any cache of mutable data needs a freshness policy,
  invalidation path, and regression test. A closed, seeded reference list (for example genres, whose ids are stable) may be
  a singleton that loads once without invalidation (guard the lazy load, and do not cache a failed load). Do not cache
  anything else without the same justification.
- Paged lists with aggregated child rows (for example names joined with `GROUP_CONCAT`): select the page ids first in a
  derived table (filter, order, `LIMIT`) and aggregate only in the outer query, otherwise correlated subqueries run before
  the `LIMIT`. Prefer a few set-based queries per page over one query per concern, and count round trips.
- When rewriting a query for speed, prove equivalence: run a temporary test against a copy of the old implementation over
  many combinations (sorts, filters, pages, edge cases), keep permanent tests for the result shape, then delete the old
  copy. Use compiled queries (`EF.CompileAsyncQuery`) for hot fixed-shape reads, if measured.
- Size pools for the real number of users: a pooled `DbContextFactory` with an explicit pool size and a database
  connection pool limit (apply the default only when the connection string does not set it, and make sure a migration lock
  connection cannot be starved). Keep disconnected interactive-server circuits few and short-lived.
- Bound container memory: set a container memory limit and a matching managed heap cap (for .NET
  `DOTNET_GCHeapHardLimitPercent`), consider `DOTNET_GCConserveMemory`, and measure `docker stats` (idle, peak and settled
  after a scripted use session) before and after. Check `OOMKilled` and restarts, and ask the owner before raising a
  limit.
- Do not add retries blindly: retry only transient operations that are safe to replay, and account for transactions and
  duplicate writes.
- Keep long-lived UI state small. Do not retain database contexts or large entity graphs in circuits or components.

## Deployment and operations

Document the repository's actual deployment setup here:

- Runtime image and non-root execution: [TODO] (a chiseled/distroless image has no shell: use the exec form for the
  container healthcheck, for example `["CMD", "dotnet", "App.dll", "--health-check"]`)
- Listening address/port and health probe: [TODO]
- Database backup/restore and migration procedure: [TODO]
- Required secrets/certificates and rotation procedure: [TODO]
- Logging, monitoring, and resource limits: [TODO] (give the container a memory limit and measure it, see Performance)

Use supported .NET runtime and SDK versions. Keep local development, tests, and production configuration consistent
without copying production secrets or data into development.

## Project-specific decisions

Record only decisions that are binding for this repository:

- **Business rules:** [TODO: link or summarize]
- **Security/privacy requirements:** [TODO]
- **Data retention and deletion:** [TODO]
- **Versioning/release process:** [TODO]
- **Known constraints and gotchas:** [TODO]
