# MovieDB – Milestone Handover

Status note for the next agent. Read [`AGENTS.md`](AGENTS.md) (conventions) and [`PLAN.md`](PLAN.md) (product spec,
milestones in §10) first. Update this file at the end of every milestone.

## Working agreement with the owner

- Do **one milestone at a time**, then stop and wait for the owner's review. Do not start the next milestone
  unprompted.
- Do not commit unless asked. M1 is committed; M2 is uncommitted in the working tree.
- Ask before taking product or schema decisions that the docs do not settle.

## Keeping this handover current (required)

After finishing a milestone, and before reporting to the owner, update this file as part of the end-of-task routine:

1. Set the status table: mark the finished milestone "Done, awaiting owner review (uncommitted)" and name the next one.
2. Replace "What Mx delivered" with a short section for the finished milestone (files, projects, tests, check
   results). Keep a one-line summary of earlier milestones.
3. Replace "Next: Mx" with the plan for the next milestone, taken from PLAN.md §10 and the sections it
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
| M1 Data | Done (committed) |
| M2 Auth | Done, awaiting owner review (uncommitted) |
| M3 Import | **Next. Not started** |
| M4–M6 | Not started |

### What M2 delivered

- **Common:** `LocalUrl` (`IsLocal`, `OrDefault`: single leading `/`, no `//`, `/\`, backslash or control characters).
- **Infrastructure:** `AuthExtensions.AddMovieDbIdentity()` (Identity Core, min password length 8), `UserStore`,
  `AuthThrottleStore` (DB-backed, fails closed), `DefaultUserSeeder` (creates `admin` when `users` is empty, with the
  password from `Auth:DefaultPassword` / env `Auth__DefaultPassword`; fails startup if it is missing; warns while that
  password is in use), `DbOperations`. `DatabaseMigrator.MigrateAndSeedAsync` seeds under the
  migration lock; `MigrateAsync` (used by the import tool and tests) does not seed. Migration
  `20261028123142_SeedAuthThrottleGlobalRow` inserts the `global` throttle row (hand-written, retry-safe).
- **Auth** (`MovieDB.Auth`): `AuthEndpoints` (POST `/login`, POST `/logout`, POST `/account/wachtwoord`, all with
  antiforgery), `AuthProtection` (concurrency guard, 429 page with `Retry-After`, `moviedb.device` cookie),
  `AuthProtectionCleanup`, validators, `AuthOptions` (`Auth:SecureCookies`, `Auth:Protection:*`), static SSR pages
  `/login`, `/account/wachtwoord`, `/accessdenied` on `AuthLayout` with `wwwroot/auth.css`.
- **Web:** `App`/`Routes`/`MainLayout` (minimal; M4 restyles), `ReconnectModal`, `Error`, `NotFound`, placeholder `Home`
  at `/` (M4 replaces it with a redirect to `/films`), `RevalidatingAuthStateProvider`, `Program.cs` wiring: identity
  cookie `moviedb.auth` (Lax, HttpOnly, 7-day sliding, Secure unless `Auth:SecureCookies=false`, which only
  Development sets), fallback authorization policy, nl-BE request localization, MudBlazor + `AddMudTranslations`,
  Interactive Server with global routing, CSP with per-request nonce plus the other security headers.
- **Tests (126 passing in total):** `LocalUrlTests`, `ValidatorTests` (unit); `AuthCharacteristicsTests` (anonymous
  redirect, public paths, headers + nonce, login/remember-me/cookie flags, generic failures, case-insensitive user
  name, local-only return URL, antiforgery, IP/device/account throttling, concurrency guard, logout, change password,
  Production key ring encryption and fail-closed without a certificate, cookie shared across instances),
  `DefaultUserSeedingTests` (once, untouched when users exist, concurrent startups, migrate-only), plus
  `MigrationTests` now count the migrations dynamically and check the `global` throttle row.
- Checks: `dotnet build src/MovieDB.slnx -warnaserror`, `dotnet csharpier check .` and
  `dotnet test --solution src/MovieDB.slnx` pass. Smoke test against the local `moviedb` (curl): login as `admin`
  works, `/` redirects anonymous users to `/login?returnUrl=%2F`, `GET /logout` is 404.
- **Not verified in a browser:** interactive circuits and the Dutch `MudBlazor.Translations` strings. No MudBlazor
  component is rendered yet in M2, so check both in M4 (global `InteractiveServer` routing is configured).

Earlier: M0 delivered the scaffold (repo files, solution and projects, architecture tests, stub import tool). M1
delivered data: entities, `MovieDbContext`, `snake_case` mapping, the hand-written initial migration, `DatabaseMigrator`,
health endpoints, options validation and Serilog.

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

## Next: M3 – Import (PLAN.md §10, with §6 and §2.3, and the stub in `src/tools/MovieDB.AccessImport`)

Scope: the Access → MariaDB import into the **local** database. Platform-neutral cleanup, de-duplication and
correction rules (unit-tested, synthetic data only), an ODBC reader behind a source abstraction (x64 Access driver,
Windows only, read-only), `scripts/Import-Access.ps1` (`-Reset`, `-DryRun`, `-SkipDump`, `-DumpPath`, dump + dump
verification) and the counts-only verification report (§6.4). The tool uses `DatabaseMigrator.MigrateAsync` (no seed).
Recreate the `scripts/` folder. Never print rows or review text. The dump (§6.5) stays in git-ignored `artifacts/`.

Done when: the report matches §6.4 (1,203 movies from the current `.mdb`), dump verification passes, and the unit
tests for the rules pass.

Suggested order: read PLAN.md §2.3, §6.1–6.5; model the source abstraction and the rules with unit tests first;
then the ODBC reader, the loader (genres/media suggestion rows, preserved `FilmID` as `movie_id`), the report, the
script and the dump check; run against the local `FilmsBase.mdb`; update docs; stop for review.

## Learnings from M2

- **Code rules added after M2:** no primary constructors (explicit `_field` + constructor; positional records stay;
  IDE0290 is off in `.editorconfig`), and only types used by another production assembly (plus `Program`) are public.
  `PublicSurfaceTests` pins the public surface per assembly. Scaffolded EF migrations come out `public`: make them
  `internal`. Razor components are always public. `DbOperations`/`DbFailure` are internal until the Movies slice needs
  them (M4): make them public then and extend the allowed list.
- **Default password is an env var.** `MariaDbFixture` sets `Auth__DefaultPassword=demodemo` for the whole
  characteristics run (and clears it on dispose); the `http` launch profile sets the same. The compose stack (M6) must
  map `Auth__DefaultPassword=${MOVIEDB_DEFAULT_PASSWORD:?…}`. Tests that need it absent unset the variable temporarily.
- **Form models need `init` properties.** `[FromForm]` binds through the Blazor form data mapper, which returns 400
  when a constructor parameter is missing. An unchecked checkbox is absent from the post, so a positional record
  with `bool RememberMe` made every login fail. Use `init`-property types (`LoginForm`).
- **Host tests:** `AuthApp` in `AuthCharacteristicsTests` passes the connection string through `UseSetting`; after the
  run the local `moviedb` held only the 4 throttle rows of the manual smoke test, so the tests do use their per-test
  databases. Production-environment tests need the
  self-signed key-ring certificate from `TestDataProtectionCertificate`.
- **Throttle behavior:** failures back off progressively per account/source before the hard limit, so tests that
  assert a block loop until the first 429 instead of assuming an exact attempt count.
- **Static SSR layout:** `MainLayout` also renders in static SSR (auth pages share the host), so the nav uses plain
  HTML (a POST form for logout) instead of `MudMenu`. Revisit in M4.
- Blazor adds a second `Content-Security-Policy: frame-ancestors 'self'` header next to ours. Harmless (the stricter
  `'none'` still applies).
- This machine has `core.autocrlf=true`, so working-tree files show CRLF (pre-existing files too); the index is LF
  (`.gitattributes eol=lf`) and `csharpier check` passes, so this is harmless.
- `MigrationTests` count migrations from the assembly, so adding a migration needs no test change.

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
