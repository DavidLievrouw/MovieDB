# MovieDB – Milestone Handover

Status note for the next agent. Read [`AGENTS.md`](AGENTS.md) (conventions) and [`PLAN.md`](PLAN.md) (product spec,
milestones in §10) first. Update this file at the end of every milestone.

## Working agreement with the owner

- Do **one milestone at a time**, then stop and wait for the owner's review. Do not start the next milestone
  unprompted.
- Do not commit unless asked. All work so far is uncommitted in the working tree.
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
| M0 Scaffold | Done, awaiting owner review (uncommitted) |
| M1 Data | **Next. Not started** |
| M2–M6 | Not started |

### What M0 delivered

- Root: `global.json` (SDK 10.0.200, MTP test runner), `dotnet-tools.json` (csharpier 1.3.0, dotnet-ef 10.0.14),
  `.csharpierrc.yaml`, `.csharpierignore`, `NuGet.config`, `version.txt` (1.0.0), `README.md`. `.editorconfig` and
  `.gitattributes` are LF; all existing text files were converted to LF.
- `src/MovieDB.slnx`, `Directory.Build.props`, `Directory.Packages.props`, `version.props`.
- Projects: Common (`Result<T>`, `Error`, `ErrorKind`), Ui, Infrastructure, Auth, Movies, Web, `tools/MovieDB.AccessImport`.
  Ui and Infrastructure only hold an assembly-anchor type. Auth and Movies hold an empty `Add<Slice>Services()`.
- `MovieDB.Web`: minimal host (Serilog console, `GET /health/live`, `public partial class Program`). No Blazor, auth,
  EF or options yet. `UserSecretsId` is `9b46a74e-075e-4f32-b29a-dc9bd38c1cec`.
- `MovieDB.AccessImport`: stub that prints a message and exits with 1 (real work in M3).
- Tests (17 passing): `ResultTests` (unit), `LayerTests` (architecture), `MariaDbCollationTests` (Testcontainers),
  `ProjectWiringTests` (browser project placeholder).
- Checks at the end of M0: `dotnet build src/MovieDB.slnx -warnaserror`, `dotnet csharpier check .` and
  `dotnet test --solution src/MovieDB.slnx` all pass.

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

## Next: M1 – Data (PLAN.md §10, with §5, §4.3 and §11)

Scope: entities, `MovieDbContext`, configurations, initial migration (domain + auth tables), `DatabaseMigrator`,
health endpoints (`/health/live`, `/health`, `--health-check` switch), options validation (`ValidateOnStart`), Serilog
setup.

Done when: characteristics tests for migrations and constraints pass, and the app migrates the local `moviedb`.

Suggested order:

1. Read PLAN.md §5 (tables, columns, constraints, indexes, seed data) and §4.3 (naming). Mirror Motorsports'
   `Infrastructure` (context, migrator, DataProtection store, options).
2. Vogen IDs (`MovieId`, `GenreId`, `MediumId`, `UserId`) with EF value converters.
3. Entities and `IEntityTypeConfiguration`s with explicit `snake_case`/plural names, collation
   `utf8mb4_uca1400_ai_ci`, restrictive FKs, `version` concurrency tokens, unique index on
   Title + ReleaseYear + Director.
4. Add the first migration with the documented command (AGENTS.md); review its idempotent script. `Down` must work.
   Migrations are excluded from CSharpier.
5. `DatabaseMigrator` (`IHostedLifecycleService.StartingAsync`, `GET_LOCK('moviedb-migrate', 120)` on a dedicated
   connection). Test repeated and concurrent startup.
6. Real health endpoints and options classes (`ConnectionStrings:MovieDb`, `App:TimeZone`, `Auth:*`,
   `DataProtection:*`, `ForwardedHeaders:KnownProxies`, `Health:PublicUrl`). `UserClock` goes in Common (time zone
   from `App:TimeZone` only).
7. Characteristics tests against a real `mariadb:12.3.3` (Testcontainers): migration applies, constraints hold
   (duplicate Title+Year+Director, FK restrict, collation matching, concurrency token), concurrent migrator runs.
8. Run the app once against the local `moviedb` and confirm the migration applies. Do not print rows.
9. End-of-task routine (AGENTS.md), then update PLAN.md §10/status and **this file** as described in "Keeping this
   handover current", then stop and wait for the owner's review.

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
- **Placeholders to replace:** `MovieDB.Web/Program.cs` is deliberately minimal (the `/health/live` mapping, Serilog
  bootstrap) and will be restructured in M1–M2 (options, migrator, auth). `UiAssembly`/`InfrastructureAssembly`
  anchors can be dropped once those projects hold real types.
- **Deliberate PLAN deviations so far:** none. `Result.cs` enum values were made explicit only to satisfy RCS1161.
