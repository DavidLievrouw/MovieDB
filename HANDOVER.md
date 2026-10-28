# MovieDB – Milestone Handover

Status note for the next agent. Read [`AGENTS.md`](AGENTS.md) (conventions) and [`PLAN.md`](PLAN.md) (product spec,
milestones in §10) first. Update this file at the end of every milestone.

## Working agreement with the owner

- Do **one milestone at a time**, then stop and wait for the owner's review. Do not start the next milestone
  unprompted.
- Do not commit unless asked. M1 is uncommitted in the working tree.
- Ask before taking product or schema decisions that the docs do not settle.

## Keeping this handover current (required)

After finishing a milestone, and before reporting to the owner, update this file as part of the end-of-task routine:

1. Set the status table: mark the finished milestone "Done, awaiting owner review (uncommitted)" and name the next one.
2. Replace "What M0 delivered" with a short "What Mx delivered" section (files, projects, tests, check results). Keep
   a one-line summary of earlier milestones.
3. Replace "Next: M1 – Data" with the plan for the next milestone, taken from PLAN.md §10 and the sections it
   references, with a suggested order and its done-when criteria.
4. Update "Environment facts" if anything changed (databases, secrets, tools, scripts).
5. Append new learnings, gotchas and deviations from PLAN.md/AGENTS.md. Move anything that has become a binding
   convention into AGENTS.md or PLAN.md, and note decisions in PLAN.md §3/§13.
6. Update the status line at the top of PLAN.md.
7. Check that this file does not contain secrets, passwords, connection strings or production data.

## Current status

| Milestone | State |
|-----------|-------|
| M0 Scaffold | Done (committed) |
| M1 Data | Done, awaiting owner review (uncommitted) |
| M2 Auth | **Next. Not started** |
| M3–M6 | Not started |

### What M1 delivered

- **Common:** `Vogen.cs` (`MovieId`, `GenreId`, `MediumId` as int; `UserId` as GUID string), `UserClock` (time zone from
  `App:TimeZone` only; `Today`, `CurrentYear`; throws on an invalid zone id).
- **Infrastructure:** entities (`Movie`, `Genre`, `Medium`, `User`), `IEntityTypeConfiguration`s in `snake_case` with
  plural tables, `MovieDbContext`, `DatabaseConfiguration.AddMovieDbDatabase` (+ `RequireUtf8mb4`),
  `DatabaseMigrator` (hosted lifecycle `StartingAsync`, `GET_LOCK('moviedb-migrate', 120)` on a dedicated connection),
  design-time factory, auth throttle types, DataProtection key store. Migration `20261028115529_Initial` creates
  `genres`, `media`, `movies`, `users`, `auth_throttle_states`, `data_protection_keys`.
  - The migration is **hand-written** with `CREATE TABLE IF NOT EXISTS` / `DROP TABLE IF EXISTS` (retry-safe after a
    partial failure). The Designer and model snapshot are generated. If you add a migration with `dotnet ef`, expect
    Roslynator `RCS1021` on the generated file: rewrite it by hand in the same style.
- **Web:** `Program.cs` (Serilog, `--health-check` switch, validated `AppOptions`/`AuthProtectionOptions`, DB, migrator,
  DataProtection with optional certificate via `KeyRingProtection`, forwarded headers from known proxies, HSTS outside
  Development, `/health` and `/health/live`), `AppOptions`, `HealthChecks` (DB check; public-host check registered only
  when `Health:PublicUrl` is set), appsettings files, launch profile on port **4200**.
- **Tests (59 passing in total):** `UserClockTests`, `DatabaseConfigurationTests` (unit); `MigrationTests`
  (fresh migrate, model = schema, re-run no-op, 5 concurrent migrators, partial-failure retry, `Down` + up again,
  collation), `MovieConstraintTests` (unique Title+Year+Director ignoring case/accents, FK restrict, check constraints,
  concurrency token, Unicode round trip, unique user name), `HealthEndpointTests` (live, healthy after migrating an
  empty DB, invalid time zone stops startup).
- Checks: `dotnet build src/MovieDB.slnx -warnaserror`, `dotnet csharpier check .` and
  `dotnet test --solution src/MovieDB.slnx` pass. The app was run once against the local `moviedb`: the migration
  applied and `/health` reported Healthy.

Earlier: M0 delivered the scaffold (repo files, solution and projects, architecture tests, stub import tool).

### Environment facts

- The local MariaDB database `moviedb`, its user, and the user-secret `ConnectionStrings:MovieDb` already exist. The
  owner created them; the agent never needs the root password. Check that the key exists without printing its value:
  `dotnet user-secrets list --id 9b46a74e-075e-4f32-b29a-dc9bd38c1cec | ForEach-Object { ($_ -split '=')[0] }`.
  The owner granted the user rights on `moviedb.*` and `moviedb_dumpcheck.*`.
- Production database and user also exist (owner-managed). Nothing for the agent to do there.
- The `scripts/` folder was deliberately removed. Recreate it in M3 for `Import-Access.ps1`.
- Docker (Rancher Desktop) is running with `mariadb:12.3.3` locally. Testcontainers pulled from the same image.
- PowerShell 7 is installed at `C:\Users\David\AppData\Local\Microsoft\WindowsApps\pwsh.exe`. The default tool shell
  is Windows PowerShell 5.1.
- Git needs `git -c safe.directory=C:/_git/MovieDB …`.
- Playwright browsers are **not** installed yet (M6: build, then run `playwright.ps1 install chromium`).
- The reference repo `C:\_git\Motorsports` is read-only. Mirror its patterns.

## Next: M2 – Auth (PLAN.md §10, with §7.1, §5.3 and the Motorsports `Infrastructure/Auth` and `Auth` projects)

Scope: Identity Core stores, cookie auth, login/logout/access-denied/change-password pages, login throttling,
DataProtection, default `admin` seeding (once, under the migrator lock), fallback authorization policy, security
headers (CSP nonce). Blazor setup (`App`, `Routes`, `MainLayout`, global `InteractiveServer` routing) is needed for
the static SSR pages; keep the layout minimal until M4.

Done when: auth characteristics tests pass and a local login as `admin` works.

Already in place from M1: `User` entity and `users` table, `auth_throttle_states` table and throttle types,
`data_protection_keys` store, `AuthProtectionOptions` binding, `UserId`, `AddAuthServices()` (empty) in `MovieDB.Auth`.

Suggested order: read PLAN.md §7.1 and the Motorsports auth code, port the stores/sign-in/throttle services, add the
seeder to the migrator, add the pages and middleware, then characteristics tests (login, lockout/throttle, return URL,
anonymous redirect, security headers, seeding runs once), run once locally, update docs, stop for review.

## Learnings from M1

- **Testcontainers MariaDB user:** the default test user `mariadb` only owns one database. The fixture grants it
  `ALL ON *.*` via `ExecAsync` as root (`ExecScriptAsync` runs as the test user and does not work), then each test
  creates its own `moviedb_<guid>` database.
- **Host tests and user-secrets:** in `WebApplicationFactory` (Development) user-secrets override `UseSetting`, so host
  tests would hit the developer's local DB. `HealthEndpointTests` sets environment variables instead (they win). Do the
  same for any new test that boots the host; characteristics tests run serially in one collection.
- **Migrations:** see the hand-written `Initial` above. `HasPendingModelChanges` is tested, so keep the snapshot in
  sync with the model.
- **Dev port** is 4200 (`launchSettings.json`). `AllowedHosts` is `mdb.dalion.eu` in appsettings.json and
  `localhost;127.0.0.1` in Development.
- `Health:PublicUrl` is empty by default (check not registered). `KeyRingProtection` fails closed outside Development
  unless a DataProtection certificate is configured.

## Learnings from M0 not yet in PLAN.md/AGENTS.md

- **Build is strict.** `TreatWarningsAsErrors` plus `EnforceCodeStyleInBuild` plus Roslynator turn analyzer hits into
  build errors. Seen so far: `RCS1161` (enums need explicit values) and `RCS1181` (use `///` doc comments, not `//`,
  for comments that precede members/types). Run `dotnet csharpier format .` before the build, then check.
- **Package versions are central** in `src/Directory.Packages.props` (checked on nuget.org on 2026-10-28). Notable:
  `MudBlazor.Translations` is on the 3.x line (3.6.0), `Microting.EntityFrameworkCore.MySql` 10.0.13, EF Core and
  related 10.0.14, xunit.v3 4.0.2. `bunit` (2.12.5) is listed but no project references it yet (needed in M4).
  `FluentValidation`, `Vogen`, `Serilog.*` are referenced by projects that do not use them yet.
- **Test runner:** xUnit v3 on Microsoft.Testing.Platform. Use `dotnet test --solution src/MovieDB.slnx` (not the old
  VSTest form). A test project with zero tests exits with code 8, which is why `BrowserTests` has a placeholder test.
  Test projects are `Exe`.
- **Testcontainers API (4.16):** `new MariaDbBuilder("mariadb:12.3.3").Build()` (image in the constructor). The
  container's `GetConnectionString()` has no charset: append `;CharSet=utf8mb4`. `MySqlConnector` comes in
  transitively through `Microting.EntityFrameworkCore.MySql` (the Characteristics project references it directly).
  Use `TestContext.Current.CancellationToken` in xUnit v3 (analyzer xUnit1051).
- **Architecture tests read the csproj files** (reference matrix) because the compiler drops unused references, which
  would make NetArchTest checks pass vacuously. NetArchTest is used only for type-level rules. New projects must be
  added to `AllowedProjectReferences` in `LayerTests.cs`.
- **InternalsVisibleTo** for the four test assemblies is set once in `src/Directory.Build.props`.
- **Line endings:** LF everywhere. `.gitattributes` has `* text=auto eol=lf`. `create`/`edit` tools preserve LF; do
  not write files via PowerShell `>` (UTF-16, CRLF).
- **Running the host in the tool shell:** `Stop-Process -Name …` is blocked and `dotnet run` in async mode did not
  bind reliably. A `Start-Job` that runs `dotnet run --no-build --urls http://localhost:5099`, a `curl.exe` probe, then
  `Stop-Job` worked. Always check afterwards that no `MovieDB.Web` process is left (`Get-Process MovieDB.Web`).
- **Placeholders to replace:** `UiAssembly`/`InfrastructureAssembly` anchors can be dropped once those projects hold
  real types (Infrastructure now does; check whether the anchor is still used).
- **Deliberate PLAN deviations so far:** none. `Result.cs` enum values were made explicit only to satisfy RCS1161.
