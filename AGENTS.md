# MovieDB – AI Agent Guide

## Project overview

- **Product:** a personal film database ("Films" / "Database van films"). It is a log of watched films with genre,
  runtime, year, director, viewing date, medium, six personal scores and a written review. It replaces the Access
  database `FilmsBase.mdb` (form `frmFilms`). There is one user (the owner), who must log in. **The UI is Dutch**
  (nl-BE). Everything else is English: code, identifiers, database, docs, logs and commits.
- **Stack:** .NET 10 / C# 14, ASP.NET Core Blazor Web App (static SSR for auth/error pages, Interactive Server for the
  overview and edit pages), MudBlazor 9, EF Core 10 with `Microting.EntityFrameworkCore.MySql`, MariaDB 12.3,
  ASP.NET Core Identity Core with cookie auth, FluentValidation, Serilog, Vogen.
- **Runtime/deployment:** Linux container (`aspnet:10.0-noble-chiseled-extra`, non-root, port 8080) on the same Docker
  host and Docker network as the owner's other web application, behind Nginx Proxy Manager (TLS), using the shared
  MariaDB instance (database `moviedb`). Public URL: `https://mdb.dalion.eu` (HTTPS only, HSTS). Local development uses the local Docker MariaDB container `mariadb` (12.3.3) on `localhost:3306` with its
  own `moviedb` database and user.
- **Current status and next steps:** [`HANDOVER.md`](HANDOVER.md). Read it first and update it at the end of every
  milestone.
- **Current plan and handover:** [`PLAN.md`](PLAN.md) covers the source analysis, decisions, data model, import, UI
  spec and milestones. Phase 1 (MVP) means functional equivalence with the
  Access input form, plus a sortable overview, add/edit and login. PLAN.md §13 records the owner's answers to the
  review questions.
- **Reference implementation:** a sibling repository by the same owner is the pattern source for auth, migrator,
  options, time zone, Docker, compose, backup and tooling. PLAN.md names it and says where to find it. It is read-only.
  Mirror its patterns unless PLAN.md or this file says otherwise. Its package versions are the starting point
  (PLAN.md §4.1).
- **Precedence:** AGENTS.md holds the conventions, PLAN.md the product spec. If the two, or a document and the
  code/environment, disagree, stop and ask the owner before choosing. Record the answer in PLAN.md §3 and §13.
- **Deliberate differences from the reference implementation** (do not copy its behavior blindly here):
  - Database objects are `snake_case` with plural names, not PascalCase.
  - `InvariantGlobalization=false`, culture `nl-BE` and request localization, instead of an invariant culture.
  - `MudBlazor.Translations` is used for Dutch component text.
  - No forced password change on first login.
  - `UserClock` uses only the configured `App:TimeZone` (no browser time-zone detection), so it is also safe for
    validation.
  - Browser tests (Playwright) are new.
  - The container listens on 8080 and compose does not override the Kestrel URL.

### Architecture

Solution `src/MovieDB.slnx` (PLAN.md §4.2):

- **`MovieDB.Web`:** host. Startup, composition, App/Routes/MainLayout, security headers (CSP nonce), health
  endpoints, options validation, the CLI switch `--health-check`, Dockerfile.
- **`MovieDB.Auth`:** login, logout, access-denied and change-password pages, plus auth services.
- **`MovieDB.Common`:** `Result<T>`/`Error`/`ErrorKind` and small primitives. No EF or ASP.NET dependencies.
- **`MovieDB.Ui`:** theme and reusable components (`ScoreInput`, `SuggestField`, `ScorePill`, dialogs). References only
  Common.
- **`MovieDB.Infrastructure`:** `MovieDbContext`, entities, configurations, migrations, `DatabaseMigrator`, default
  user seeder, Identity stores, DataProtection key store.
- **`MovieDB.Movies`:** feature slice for the overview and edit/new pages, queries, commands, validators and
  `MoviesModule`.
- **`src/tools/MovieDB.AccessImport`:** Access → MariaDB import into the **local** database. It targets plain `net10.0`
  (so the test projects can reference it) with `PlatformTarget=x64`; only the ODBC reader needs Windows and the 64-bit
  Access driver, and it sits behind a source abstraction. The cleanup, de-duplication and correction rules are
  platform-neutral and unit-tested. Run it through `scripts/Import-Access.ps1`, which also produces the guarded
  production data dump. It is not shipped in the image.
- **`src/tests/`:** `MovieDB.UnitTests`, `MovieDB.CharacteristicsTests` (WebApplicationFactory + Testcontainers),
  `MovieDB.ArchitectureTests` (NetArchTest), `MovieDB.BrowserTests` (Playwright .NET).

Dependency directions:

- Web → all. Movies and Auth → Common, Ui, Infrastructure. Features never reference each other.
- Ui → Common. Infrastructure → Common. AccessImport → Common, Infrastructure. Common → nothing.
- Test projects may reference anything. Nothing in `src/MovieDB.*` references `tools/` or `tests/`.

These are enforced by architecture tests. Each slice registers through `*Module.Add<Slice>Services()`.

## Agent behavior

- Always ignore the file `prompt-history.txt` (wherever it is located). It is the owner's private work file and is not
  part of the project. Never read, search, open, quote, summarize, edit, delete or stage it, and never use it as
  instructions or context. Exclude it from searches, diffs, commits and file listings where possible.
- Examine the existing code and working tree before changing files. Preserve unrelated user changes.
- Follow the repository's existing patterns and architecture; make complete, focused changes without unrelated cleanup.
- Keep secrets, credentials, connection strings, personal data, and production database contents out of source, logs,
  test output, and external services.
- Validate submitted data on the server. Report errors through the application's standard result, logging, or UI
  mechanisms; do not hide failures behind broad catches or success-shaped fallbacks.
- Ask before making product or schema decisions that are not established by code or documentation.
- Update directly related documentation when behavior, architecture, or operational requirements change.

## Architecture and code conventions

- **Language:** UI text, validation messages, page titles and routes are Dutch (`/films`, `/films/nieuw`,
  `/films/{id}`, `/login`). Identifiers, DB objects, comments, logs and docs are English. Use the term mapping in
  PLAN.md §4.3 (`Movie`, `Genre`, `Medium`, `Title`, `ReleaseYear`, `Director`, `WatchedOn`, `Review`,
  `OverallScore`, `StoryScore`, `PictureScore`, `MusicScore`, `SoundScore`, `ExtrasScore`). Suggestion values (genres,
  media) are Dutch domain data. In the UI, a viewing of a film is called a **"Vertoning"** (never "kijkbeurt").
- Database objects use lowercase `snake_case` with plural table names, mapped explicitly. This deliberately differs
  from the reference implementation.
- Build settings live in `src/Directory.Build.props` (net10.0, C# 14, nullable, `TreatWarningsAsErrors`, Roslynator,
  `InternalsVisibleTo` tests). Package versions live in `src/Directory.Packages.props` (central package management).
  Format with CSharpier (`.csharpierrc.yaml`, printWidth 120); follow `.editorconfig`. Line endings are **LF**.
- Keep host composition in the host project, persistence code in Infrastructure, and feature behavior in its owning
  feature.
- Keep reusable UI independent of feature and database projects. Feature projects should not reference one another
  unless the architecture explicitly permits it.
- Prefer internal visibility by default; expose only module entry points and types required across project boundaries.
- Use `Result<T>`/`Error`/`ErrorKind` for expected failures and FluentValidation (Dutch messages) for input. Validate
  before mutating state.
- Use strongly typed IDs (Vogen) for entity keys (`MovieId`, `GenreId`, `MediumId`, `UserId`).
- Use `TimeProvider` and `UserClock` (time zone from `App:TimeZone` only; it lives in Common). Never call
  `DateTime.Now`/`UtcNow` in business logic. Store UTC and display in the configured time zone. `watched_on` is a date
  (`DateOnly`). Validators and commands get "today"/"current year" from `UserClock`, never from the browser.
- Keep route and external data formats stable unless a change is intentional and documented.
- **Authentication:** the fallback policy requires an authenticated user. Only login, access-denied, health and static
  assets allow anonymous access. Auth pages are static SSR with antiforgery. Logout is POST only. `returnUrl` must be
  local. When the `users` table is empty, startup seeds the default user `admin` / `demodemo` exactly once; the owner can
  change the password at `/account/wachtwoord`. There is no registration UI and no user management.

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
- **Domain rules** (PLAN.md §5):
  - A movie is unique on Title + ReleaseYear + Director, enforced by the collation, so the match is case- and
    accent-insensitive.
  - There is a single `movies` table: one row per movie, holding its viewing date, medium, scores and review.
    Re-watches are not logged. The same title with a different year or director is a different movie.
  - Genre and medium values are suggestion rows. New typed values are inserted in the same transaction as the save.
  - `version` columns are concurrency tokens: 1 on insert, incremented by the app on every update.
- Evolve the schema through explicit EF Core migrations with usable `Down` methods. Preserve existing data and test
  migrations against representative schemas.
- MariaDB DDL may not be transactional. Make migrations safe to retry after partial failure; use self-guarding SQL
  where required by the provider and migration design.
- `DatabaseMigrator` (`IHostedLifecycleService.StartingAsync`) migrates on startup under
  `GET_LOCK('moviedb-migrate', 120)` on a dedicated connection, then seeds the default user under the same lock. Verify
  repeated and concurrent startup behavior.
- Local development database: container `mariadb` (`mariadb:12.3.3`) on `localhost:3306`, database and user `moviedb`.
  The owner created the database and user; the connection string lives in user-secrets (`UserSecretsId`
  `9b46a74e-075e-4f32-b29a-dc9bd38c1cec`). Never write passwords into files, scripts, logs or commits. Development, tests and production all use MariaDB 12.3.3; keep them aligned.
- Collation `utf8mb4_uca1400_ai_ci`. Connection strings include `CharSet=utf8mb4`. Never use `ToLower()`/`ToUpper()`
  for case-insensitive comparisons; rely on the collation.
- Search uses `LIKE` with `%`, `_` and `\` escaped. Do not filter in memory.
- The Access source `FilmsBase.mdb` is a git-ignored local copy in the repository root. Code opens it read-only. Never
  commit it, and never use it or its contents in automated tests or test output; tests use synthetic data.
- Use `utf8mb4` end to end. Preserve Unicode in source, database connections, pages, and tests; never repair or replace
  text through lossy transcoding.
- Keep database command logging free of credentials, personal data, and large or sensitive values. Never print database
  dumps or production rows in test output.

## Web, UI, and configuration

- **MudBlazor** is the only component framework. Use `MudBlazor.Translations` for nl-BE and Material icons. Fonts are
  self-hosted (Space Grotesk, Inter); load nothing from Google Fonts or other CDNs. Follow the design spec in PLAN.md
  §8: a dark-first theme with a light toggle, the given palette, score pills and the sticky save bar. Do not add a
  second framework or ad-hoc controls without a clear need.
- Culture is `nl-BE`: dates `dd/MM/yyyy`, Monday is the first day of the week, `.` is the thousands separator.
  `InvariantGlobalization` must be **false**. `MudBlazor.Translations` only works when Blazor
  localization is configured: call `AddMudTranslations()`, use `UseRequestLocalization` with `nl-BE` as the only
  supported and default culture, and use `<html lang="nl-BE">`. Verify that the Dutch strings appear (the package
  may only ship `nl`, which `nl-BE` falls back to); subclass `MudLocalizer` for gaps.
- Configuration uses Options with `ValidateOnStart`:
  - `ConnectionStrings:MovieDb`: user-secrets in development; `ConnectionStrings__MovieDb` in deployment (compose maps
    it from `MOVIEDB_CONNECTION_STRING`).
  - `App:TimeZone` (`App__TimeZone`, IANA id, default `Europe/Brussels`).
  - `Auth:*`, `DataProtection:*`, `ForwardedHeaders:KnownProxies`, `Health:PublicUrl`.
- Design mobile-first and verify key flows at narrow and desktop widths (390px and 1440px).
- Prefer static/server-rendered content when it does not need client interactivity. Add interactivity only where the
  user experience requires it. Use global `InteractiveServer` routing, with auth and error pages opted
  out through `[ExcludeFromInteractiveRouting]` (static SSR). Avoid double data loads during prerendering.
- Send the security headers of the reference implementation (CSP with a per-request nonce on every `<script>`, `X-Frame-Options`, etc.).
  Load no external scripts, styles or fonts.
- Validate configuration at startup. Use local secret storage for development and environment/secret-manager
  configuration in deployment. Never commit secrets.
- Use Serilog structured console logging through `ILogger`. Never log review text, passwords, connection strings or
  rows.
- Health: `/health/live` has no dependencies. `/health` checks the DB and the public URL. The `--health-check` CLI
  switch is used by the Docker healthcheck.
- Keep state-changing operations on appropriate non-GET methods and enable the framework's antiforgery protections.

## Testing and quality

Prefer tests that exercise the real behavior and boundaries:

- Unit tests for isolated logic and fast feedback (including import cleanup rules and bUnit component tests).
- Characteristics tests for database, HTTP, authentication, and business-rule behavior.
- Browser tests (Playwright .NET) for critical user flows at mobile and desktop widths.
- Architecture tests (NetArchTest) for project boundaries and prohibited dependencies.

Use a real MariaDB instance for database behavior tests (Testcontainers.MariaDb, image `mariadb:12.3.3`). Do not
substitute EF InMemory or SQLite. Seed tests with synthetic data, not production dumps or the `.mdb`. Cover invalid
input, boundary cases, concurrency/conflicts, and important business rules—not only happy paths.

- Test stack: xUnit v3 (Microsoft.Testing.Platform), Shouldly, bUnit, Testcontainers, NetArchTest, Playwright.
- Characteristics and browser tests require Docker (Rancher Desktop) to be running.
- Browser tests need a real HTTP listener (not the in-memory `TestServer`), because Playwright and the Blazor circuit
  connect over HTTP/WebSocket. Browsers are installed once with
  `pwsh src/tests/MovieDB.BrowserTests/bin/Debug/net10.0/playwright.ps1 install chromium` after a build.
- Characteristics tests that need `Program` use `WebApplicationFactory<Program>`; the host project exposes it
  (`public partial class Program`).

### End-of-task routine

Run the smallest relevant checks, then expand when results warrant it:

1. Build the affected solution/project with warnings treated as errors.
2. Run the repository formatter and verify formatting.
3. Run targeted tests; run the full suite when the change affects shared behavior or project-wide contracts.
4. Inspect the final diff and ensure no generated artifacts, secrets, or unrelated changes are included.
5. Update `PLAN.md` (milestone status, answered questions) when the task changes it, and update `HANDOVER.md` at the
   end of every milestone.

**Repository commands** (verify and correct once the solution exists, in milestone M0):

- Build: `dotnet build src/MovieDB.slnx -warnaserror`
- Format: `dotnet csharpier format .`. Check: `dotnet csharpier check .`
- Test: `dotnet test --solution src/MovieDB.slnx`. Fast subset: the UnitTests and ArchitectureTests projects.
- Add migration: `dotnet ef migrations add <Name> --project src/MovieDB.Infrastructure --startup-project src/MovieDB.Web`
- Migration review: `dotnet ef migrations script --idempotent --project src/MovieDB.Infrastructure --startup-project src/MovieDB.Web`
- Restore tools: `dotnet tool restore`
- Access import + production dump (local DB only):
  `./scripts/Import-Access.ps1 -Source .\FilmsBase.mdb [-Reset] [-DryRun] [-SkipDump] [-DumpPath .\artifacts\import\moviedb-data.sql]`
- Default login after a fresh database: `admin` / `demodemo` (seeded once on startup).

## Performance and reliability

- Measure before optimizing. Prefer efficient SQL projections, server-side filtering/pagination, and bounded result sets.
- Avoid per-request or per-user copies of large, stable lookup data. Any cache of mutable data needs a freshness policy,
  invalidation path, and regression test.
- Do not add retries blindly: retry only transient operations that are safe to replay, and account for transactions and
  duplicate writes. Do not use `EnableRetryOnFailure`.
- Keep long-lived UI state small. Do not retain database contexts or large entity graphs in circuits or components.
- Add benchmarks only for demonstrated hot paths or when a performance requirement calls for them.

## Deployment and operations

- Runtime image and non-root execution: `src/MovieDB.Web/Dockerfile` uses the `sdk:10.0-noble` build stage and the
  `aspnet:10.0-noble-chiseled-extra` runtime (ICU + tzdata), running as `$APP_UID`. The image tag `moviedb-web:<ver>`
  comes from `version.txt`.
- Listening address/port and health probe:
  - Port 8080 in the container. `docker-compose/moviedb-web`   joins the existing external Docker network of the shared
      MariaDB and proxy (name in PLAN.md §11.2) with no host port; Nginx Proxy Manager terminates TLS for
      `https://mdb.dalion.eu`.
  - Forwarded headers come from known proxies only.
  - The healthcheck runs `dotnet MovieDB.Web.dll --health-check`.
- Database backup/restore and migration procedure:
  - Migrations run on startup (named lock).
  - `backup.ps1` runs `mariadb-dump` of `moviedb` without the data-protection key data. Take a backup before
    deploying a version with migrations.
  - The first deployment does not run the import tool against production. The app container migrates the schema and
    seeds `admin`; then the owner runs the guarded data dump produced locally by `scripts/Import-Access.ps1` (PLAN.md
    §6.5, §11.2). The dump contains personal reviews: keep it in git-ignored `artifacts/`, never commit it, and delete
    all copies after the deployment.
- Required secrets/certificates and rotation procedure:
  - Secrets: the DB credentials (`MOVIEDB_CONNECTION_STRING`) and the DataProtection PFX, bind-mounted read-only from
    `/etc/dalion/moviedb/secrets/`, with its password in `DATAPROTECTION_CERTIFICATE_PASSWORD`. They live in the
    environment only, never in the repo.
  - Rotating the PFX invalidates auth cookies; users log in again.
- Logging, monitoring, and resource limits: Serilog to the console, read with `docker logs`. Compose healthcheck.
  Resource limits follow the reference implementation's compose files; the shared MariaDB keeps its existing limit.

Use supported .NET runtime and SDK versions. Keep local development, tests, and production configuration consistent
without copying production secrets or data into development.

## Project-specific decisions

Record only decisions that are binding for this repository:

- **Business rules:** see PLAN.md §3 (decisions), §5 (data model) and §8 (UI). Highlights:
  - One row per movie; uniqueness is on Title + Year + Director. Re-watches are not logged.
  - Scores are 1–10 or empty ("–"); Extra's empty means "n.v.t.". "Totaal" is manual, not an average.
  - Genre and "Gezien op/in" are free text with DB-backed suggestions.
  - Datum defaults to today in `App:TimeZone`.
  - Default sort is Datum DESC, Title ASC. Search is a debounced title search. Save is explicit.
- **Security/privacy requirements:**
  - A login is required everywhere.
  - Reviews are personal content: never log them or print rows in tests or output.
  - Login throttling is DB-backed and fails closed.
- **Data retention and deletion:** no delete in the MVP. Access `FilmID` values are preserved as movie IDs.
  `tblFilmInBezit` is not migrated.
- **Versioning/release process:** SemVer in `version.txt`, which drives the image tag. Manual deployment as documented
  in README. CI is out of scope.
- **Known constraints and gotchas:**
  - Pomelo has no EF Core 10 release; use `Microting.EntityFrameworkCore.MySql` (same API).
  - The Access ODBC driver is 64-bit only ("Microsoft Access Driver (*.mdb, *.accdb)"; the 32-bit drivers on this
    machine are not usable), so the import tool must run as x64 on Windows.
  - The Access source is read through ODBC only; no Python or `pyodbc` is installed here.
  - Duplicate detection in the import must agree with the DB collation `utf8mb4_uca1400_ai_ci`. A .NET comparer is
    only an approximation; see PLAN.md §6.3.
  - The Docker healthcheck image is chiseled (no shell), so it uses the exec form `["CMD", "dotnet", …]`.
  - The column `[Gezien op]` contains a space. `Duur`, `Jaar` and `Extras` are text in Access. `Extras` uses `"n/a"`
    and scores use `0` for "empty".
  - Source data quirks (polluted titles and directors, whitespace, duplicate Title + Year + Director groups) are
    documented in PLAN.md §2.3 and handled by the import rules in §6.3. Duplicate Title + Year + Director rows are
    resolved by keeping the oldest row (earliest Datum, then lowest FilmID); the current `.mdb` yields 1,203 movies.
    Six IMDb-verified attribute corrections are applied to kept rows (PLAN.md §2.3, §6.3).
  - Windows PowerShell `>` redirection writes UTF-16. Create SQL dumps with `mariadb-dump --result-file` inside the
    container and `docker cp` them out.
  - The Access macro `mcrGaNaarFilm` and handler `Knop35_Click` are dead code; do not port them.
  - Git may report "dubious ownership" for this repo. Use `git -c safe.directory=C:/_git/MovieDB …` rather than
    changing global config.
  - `AGENTS.template.md` is the generic template this file was derived from; do not edit it.
