# MovieDB – Milestone Handover

Status note for the next agent. Read [`AGENTS.md`](AGENTS.md) (conventions) and [`PLAN.md`](PLAN.md) (product spec,
milestones in §10) first. Update this file at the end of every milestone.

## Working agreement with the owner

- Do **one milestone at a time**, then stop and wait for the owner's review. Do not start the next milestone
  unprompted.
- Do not commit unless asked. M1 is committed; M2, M3 and M4 are uncommitted in the working tree.
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
| M3 Import | Done, awaiting owner review (uncommitted) |
| M4 UI shell + overview | Done, awaiting owner review (uncommitted) |
| M5 Edit/new | **Next. Not started** |
| M6 | Not started |

### What M4 delivered

Scope follows PLAN.md §10 (M4 = shell + overview; M5 = edit/new). The older HANDOVER text that put edit in M4 was
wrong; the owner confirmed the PLAN split.

- **Ui:** `FilmsTheme` (dark-first palette, light toggle), `ScorePill` (bands error/warning/primary/success, "–" when
  empty, aria-label), `wwwroot/ui.css`.
- **Movies:** `OverviewState` (URL-backed multi-column sort `sort=genre,title:desc` / search / page / size with
  validation and defaults omitted from the
  query string), `MovieQueries` (no-tracking projection; NULLs last in both directions; stable paging via id
  tie-break; `LIKE` search with escaped `%`, `_`, `\`; page clamping; `FindAsync`), `Formats` (dates as
  "Sa, June 15th, 2022" by owner request), pages `/films`
  (`Overview.razor`: debounced search, sortable desktop table with fixed column widths and CTRL+click multi-sort,
  mobile cards with a single-column sort dropdown, an always-visible total plus a **statistics carousel**
  (`Components/StatisticsCarousel.razor`, `MovieQueries.GetStatisticsAsync`, `StatisticSlides`; whole DB, loaded after
  first render, auto-advance with pause/prev/next/dots),
  floating "Nieuwe film" button on mobile), `/films/{id}` (**read-only placeholder**, M5 replaces it) and
  `/films/nieuw` (**stub**), `wwwroot/movies.css`.
- **Host:** new `MainLayout` (app bar, theme toggle, user menu with change password and logout POST form), `/` redirects
  to `/films`, self-hosted fonts in `wwwroot/fonts` (Inter, Space Grotesk, latin + latin-ext woff2, OFL licences; CSP
  `font-src 'self'`), `app.css`/`app.js`, restyled `auth.css`.
- **Tests (307 total, all passing):** unit `OverviewStateTests`, `MovieFormatTests`, `StatisticSlidesTests`, bUnit
  `ScorePillTests`;
  characteristics `MovieOverviewQueryTests` (13) and `MovieStatisticsQueryTests` (7), real MariaDB, plus a host test
  for the redirect, local fonts and CSP.
  Existing auth tests now use `/films` as the authenticated page (`/` redirects).
- **Browser check (throwaway Playwright script against the local DB, 390px and 1440px):** fonts load, no horizontal
  overflow, search/sort/paging update the URL, back from detail restores state, theme toggle persists across reload,
  user menu shows both entries, Dutch "Per pagina" appears. Checks that remain for M6's suite: row-click versus title
  link, the static (non-interactive) theme-toggle fallback.
- Checks: build `-warnaserror`, XML-doc build (0 warnings), `csharpier check`, full test run pass.

Earlier: M3 delivered the import (summary below).

### What M3 delivered

- **`src/tools/MovieDB.AccessImport`** (internal, plain `net10.0`, x64): `OdbcFilmSource` (read-only ODBC, behind
  `IFilmSource`-style abstraction), `MovieCleaner` (whitespace, polluted titles/directors, `n/a`, `0` scores, text
  `Duur`/`Jaar`), `CollationKey` (approximation of `utf8mb4_uca1400_ai_ci`), `Duplicates` (keep oldest Datum, then
  lowest FilmID), attribute corrections, `ImportPlanner`, `ImportLoader` (one transaction, preserves `FilmID` as
  `movie_id`, seeds genres/media, sets AUTO_INCREMENT past max, refuses a non-empty DB unless `--reset`, which keeps
  `users`), `ImportReport` (counts only), `Fingerprint` + `DumpComposer` (guarded dump). Subcommands: `import`,
  `migrate`, `fingerprint`, `wrap-dump`. The connection string comes from env `MOVIEDB_CONNECTION_STRING`.
- **`scripts/Import-Access.ps1`**: `-Source`, `-DryRun`, `-Reset` (typed `RESET`), `-SkipDump`, `-DumpPath` (default
  `.\artifacts\import\moviedb-data.sql`), `-ConnectionString` (else env, else user-secrets, else prompt),
  `-MariaDbContainer`, `-AllowRemote`. Builds, migrates, imports, writes the report, dumps via `mariadb-dump` in the
  container, wraps the dump with a guard (single transaction, refuses unless the target is empty and at the expected
  migration), restores it into scratch DB `moviedb_dumpcheck`, compares fingerprints, checks that a second load is
  refused, and drops the scratch DB.
- **Real run against the local `FilmsBase.mdb`:** 1,222 source rows, 19 duplicates dropped, **1,203 movies**, 21 genres,
  6 media, 6 corrections applied; every plan figure equals the database figure; dump verification passed. The local
  `moviedb` now holds the imported data (users untouched).
- **Tests:** unit (95: cleaner, duplicates, corrections, planner, dump composer) and characteristics (35:
  28 collation parity pairs against the real DB, loader, dump round trip, guard refusals, rollback). All synthetic data.
- Checks: build, csharpier and the full test run pass (see "Learnings from M3").

Earlier: M2 delivered auth (summary below).

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
- `scripts/Import-Access.ps1` exists (M3). The local `moviedb` already contains the imported 1,203 movies; re-run with
  `-Reset` to redo it. The dump is at `artifacts\import\moviedb-data.sql` (git-ignored, personal reviews).
- Docker (Rancher Desktop) is running with `mariadb:12.3.3` locally. Testcontainers pulled from the same image.
- PowerShell 7 is installed at `C:\Users\David\AppData\Local\Microsoft\WindowsApps\pwsh.exe`. The default tool shell
  is Windows PowerShell 5.1.
- Git needs `git -c safe.directory=C:/_git/MovieDB …`.
- Playwright chromium is installed (via `playwright.ps1 install chromium` after a build).
- The reference repo `C:\_git\Motorsports` is read-only. Mirror its patterns.

## Next: M5 – Edit/new (PLAN.md §10, with §5, §7 and §8.4–8.6)

Replace the placeholder `/films/{id}` and the `/films/nieuw` stub with the real form: `ScoreInput`, `SuggestField`,
dialogs in `MovieDB.Ui`; commands and FluentValidation validators (Dutch messages; "today"/current year from
`UserClock`); duplicate check (Title + Year + Director, collation-based), new genre/medium rows inserted in the save
transaction, `version` concurrency token, sticky save bar, record navigation, unsaved-changes guard. Map unique-key
and concurrency conflicts to `Result`/`Error`. Make `DbOperations`/`DbFailure` public if the slice needs them and
extend `PublicSurfaceTests`. Done when the §8.6 checklist is ticked and tests pass. Develop against the imported local
data (never print rows or reviews). Stop for owner review afterwards.

## Learnings from M4

- **MudBlazor deviations from PLAN §8.3:** `MudDataGrid` is used with `Items` only (the page is one bounded query,
  with the built-in pager and sort) instead of `ServerData`; sorting and paging are driven by `OverviewState` and the
  URL. Mobile uses cards, not the grid.
- **Name clashes:** a component parameter called `Size` collides with the MudBlazor `Size` enum; `HideLabel` is not
  valid on `MudTextField`.
- **`/` redirects** (`NavigateTo(..., replace: true)` in static prerender becomes a 302). Tests that need an
  authenticated page must request `/films`, not `/`.
- **Font preloads removed:** `@Assets[...]` preload hrefs are fingerprinted while the CSS `url()` is not, so the
  browser downloaded twice and warned. The `@font-face` rules load the fonts; the CSP allows `font-src 'self'`.
- **Internal types in tests:** test members exposing internal types must be non-public; use primitives in
  `[InlineData]`.
- **Running the host for a browser check:** a `Start-Job` does not survive between tool calls. Start the host in an
  async shell (`dotnet run --no-build --no-launch-profile --urls http://localhost:5099` with
  `ASPNETCORE_ENVIRONMENT=Development` and `Auth__DefaultPassword` set), run Playwright, then stop the process by id.
  Playwright chromium is now installed.
- **CRLF:** the `create`/`edit` tools wrote CRLF here; new files were normalised to LF before finishing.
- Full test run: 274 pass (95+ unit, 116 characteristics, architecture, browser placeholder).

## Learnings from M3

- **`dotnet user-secrets` connection strings** use keys `Server;Port;Database;User;Password;CharSet`. In PowerShell
  use `$b.set_ConnectionString(...)` / `get_ConnectionString()` on a `DbConnectionStringBuilder`: the
  `ConnectionString` property is shadowed by the indexer in PS.
- **Piping to the script:** `'RESET' | powershell -File .\scripts\Import-Access.ps1 -Reset …` satisfies the typed
  confirmation non-interactively. Do not `Select-Object -First N` the script output: it stops the pipeline and
  can interrupt the run midway (the first real run did exactly that; the data was imported but no dump written).
- **Windows PowerShell 5.1:** the script is ASCII-only for that reason; native stderr is handled in `Invoke-Native`.
- **Collation parity:** the 28 synthetic pairs agree with `utf8mb4_uca1400_ai_ci`; the real data yields exactly the 19
  expected duplicate groups. The DB unique index is the final arbiter inside the loader transaction.
- **Duplicate FilmIDs in the source** (should not occur) are de-duplicated by the planner (first row wins) and
  reported as an error rather than crashing.
- **Rules hit by Roslynator:** RCS1205 (named arguments must follow parameter order).
- PLAN §6.1 said `net10.0-windows`; corrected to plain `net10.0` (§4.2 wins).
- **Test container connections:** each test's own database has its own connection pool, so the full run exceeded
  MariaDB's default `max_connections` (151) ("Too many connections"). `MariaDbFixture` now starts the container with
  `--max-connections=1000`.
- Final checks: build `-warnaserror`, `csharpier check` and `dotnet test --solution` pass (214 tests).

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
