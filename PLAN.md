# MovieDB – Project Plan & Handover

Handover for the coding agent that builds the solution. Read it together with [`AGENTS.md`](AGENTS.md). The reference
implementation for conventions, auth, deployment and tooling is the owner's **Motorsports** repository. Read it and
mirror its patterns unless this plan says otherwise.

**Where to find the Motorsports source (read-only, never modify it):**

- Local clone: `C:\_git\Motorsports` (sibling of this repo). Read it directly from disk.
- GitHub: <https://github.com/DavidLievrouw/Motorsports> (use it if the local clone is missing; clone it outside this
  repo, e.g. next to it).
- Paths quoted in this plan, such as `src/Motorsports.Infrastructure/Auth`, `src/Motorsports.Common/UserClock.cs`,
  `src/Motorsports.Web/Program.cs`, `src/Directory.Packages.props`, `Dockerfile`, `docker-compose/` and the backup
  script, are relative to that repository root.

**Status:** M0 (scaffold), M1 (data) and M2 (auth) implemented; M2 awaiting owner review. M3 (import) implemented and run against the local `.mdb`; awaiting owner review. M4 has not started. All §13 questions are answered.

**Language rule:** the **UI is Dutch** (labels, messages, validation, page titles and routes). **Everything else is
English**: code, identifiers, database objects, comments, commits, docs and logs.

---

## 1. Goal and scope

Replace the Access database `FilmsBase.mdb` with a .NET 10 Blazor web application backed by MariaDB. It is a personal
log of watched films with personal scores and a written review. The application has one user (the owner) and
requires a login.

### Phase 1 – MVP (this plan)

1. **Functional equivalence with the Access form `frmFilms`**: view, browse, add and edit films with the same fields,
   defaults, value suggestions and record navigation.
2. **Overview**: a sortable, paged list of all films with a debounced title search.
3. **Add / edit** with an explicit Save button, server-side validation and optimistic concurrency.
4. **Login**: cookie authentication like Motorsports. A default user `admin` is seeded once.
5. **Import**: a repeatable, verifiable import from `FilmsBase.mdb` into the **local** MariaDB. It produces a `.sql`
   data dump that the owner runs on the production database at deployment.
6. **Deployment**: a Docker image plus docker-compose, like Motorsports.

### Explicitly out of scope for the MVP

- Deleting films.
- Logging re-watches. There is one record per film.
- `tblFilmInBezit` and the report `rptFilmInBezit` (legacy; not migrated).
- CI pipelines.
- Ongoing synchronisation with Access. Access is retired after the import.
- Statistics, charts, external metadata (TMDb/IMDb), posters, multi-user features, user management UI, exports.

---

## 2. Source analysis (`FilmsBase.mdb`)

Jet 4.0 database. AppTitle "Films", startup form `frmFilms`. It has no VBA modules other than form handlers, no
relationships and no saved queries.

| Object | Kind | Migrate? |
|---|---|---|
| `tblFilms` | table, 1,222 rows, FilmID 1–1239 (gaps from deletes) | **Yes** |
| `frmFilms` | single-record input form (startup) | **Yes** (spec in §2.2) |
| `tblFilmInBezit` | table, 284 rows (owned media), not linked | No (legacy) |
| `rptFilmInBezit` | report on `tblFilmInBezit` | No |
| `mcrGaNaarFilm` | macro `FindRecord =[txtRecord]`; the control does not exist | No (dead) |
| `Knop35_Click` | form handler without a button | No (dead) |

### 2.1 `tblFilms` schema

| Access field | Access type | Req | Default | Notes |
|---|---|---|---|---|
| `FilmID` | AutoNumber (Long) PK | – | – | Preserved as `movies.id` |
| `FilmTitel` | Text(70) | yes | – | Indexed, **not unique** |
| `Genre` | Text(50) | yes | – | Combo, value list, `LimitToList=False` |
| `Duur` | **Text**(50) | yes | – | Input mask `#00" min"`; stored like `" 88"`/`"123"`/`"099"`; range 83–227 |
| `Jaar` | **Text**(4) | yes | – | Mask `0000`; 1941–2026, all numeric |
| `Regisseur` | Text(40) | yes | – | Sometimes several names, comma-separated |
| `Datum` | Date | yes | `=Date()` | **Viewing date**; 2000-09-21 … 2026-07-27; no time parts |
| `Totaal` | Long | yes | 0 | Combo 1–10, manual (label: "geen gemiddelde") |
| `Film` | Byte | yes | 0 | Combo 1–10 |
| `Beeld` | Byte | yes | 0 | Combo 1–10 |
| `Muziek` | Byte | no | 0 | Combo 1–10 |
| `Geluid` | Byte | yes | 0 | Combo 1–10 |
| `Extras` | **Text**(5) | yes | `"n/a"` | Combo 1–10; 1,206 × `n/a`, 16 × digit 2–9 |
| `Gezien op` | Text(8) | no | – | **Space in name**. Combo `LimitToList=False` |
| `Opmerkingen` | Memo | no | – | Review text in Flemish Dutch, max 569 chars, 1 contains CRLF |

### 2.2 Form `frmFilms` (functional reference)

Single-record form, caption "Films", header "Database van films". The tab order and labels are:

| Tab | Label | Control | Notes |
|---|---|---|---|
| – | `FilmID:` | text box | Display only |
| 1 | `Filmtitel:` | text box | |
| 2 | `Genre:` | combo | List: Actie, Animatie, Avontuur, Bond, Detective, Detective-thriller, Documentaire, Drama, Horror, Kinder, Komedie, Melodrama, Misdaad, Oorlogs, Rampen, Romantisch, Sci-fi, Thriller, Western, Zwarte komedie, Andere. Free text allowed |
| 3 | `Duur:` | text box | Mask `#00" min"` |
| 4 | `Jaar:` | text box | Mask `0000` |
| 5 | `Regisseur:` | text box | |
| 6 | `Datum:` | text box | Short date, default today |
| 7 | `Gezien op/in:` | combo | List: Blu-ray, DVD, Video, TV, Bioscoop, Andere. Free text allowed |
| 8 | `Opmerkingen:` | multiline text, vertical scrollbar | |
| – | `EVALUATIE` | section header (white on maroon) | |
| 9 | `Totaal (geen gemiddelde):` | combo 1–10 | |
| 10–14 | `Film:`, `Beeld:`, `Muziek:`, `Geluid:`, `Extra's:` | combo 1–10 | |
| 15–19 | Buttons `Eerste`, `Vorige`, `Nieuw`, `Volgende`, `Laatste` | `DoCmd.GoToRecord` | Tooltips: "Eerste record", "Vorige record", "Record toevoegen", "Volgende record", "Laatste record" |

- Access saves implicitly when you leave the record. The web app uses an **explicit Save** instead (decided).
- Look and feel: background #004080, yellow labels, orange title on teal, EVALUATIE white on maroon, background
  picture `video.jpg`. The new design (§8) borrows these accents, not the layout.

### 2.3 Data profile and quirks

- **Unicode**: é è ë ï ø å ñ ç ô ö á í ó à, `…` (U+2026), `»`, `—`. Use utf8mb4 end to end.
- **Polluted titles** (IDs 733, 772, 789, 811): tabs and CRLF with IMDb copy-paste junk, for example
  `"\tArthur\r\nTop 5000\r\nArthur"`. The first and last non-empty lines are always the real title.
- **Polluted directors**: IDs 190, 212, 254 and 1090 have a leading `\r\n`. ID 1095 is
  `"\r\nJohn Landis\r\nJohn Landis"`.
- **Whitespace**: 2 titles and 46 directors have leading or trailing spaces.
- **Score 0** means "not scored": Totaal 6×, Film 6×, Beeld 8×, Muziek 6×, Geluid 7×. Some rows are all zero.
- **Extras `n/a`** means "not applicable" or not scored.
- **`Gezien op`**: Andere 461, TV 428, Bioscoop 119, DVD 96, Video 66, Blu-ray 43, empty 9.
- **Genres** in the data: exactly the 21 values listed in §2.2.
- **Directors**: 718 distinct values after cleanup. Max lengths after cleanup: title ≈ 60, director 31.
- **Same title, different film**: 9 titles appear with a different year and/or director, i.e. remakes (for example
  *Casino Royale* 1967/2006). These are different movies and are imported as separate rows.
- **Duplicate Title + Year + Director**: after cleanup, **19 groups** of exactly 2 rows share title (case- and
  accent-insensitive), year **and** director. This violates the uniqueness rule (§5). **Decision: keep the oldest row
  of each group** (earliest `Datum`, then lowest `FilmID`) and drop the other one entirely (§6.3).
  - **Skyfall 747 / 749**: same date 2012-11-02, both Bioscoop, different scores and reviews. **Keep 747**, drop 749.
  - **18 groups with dates years apart**: re-watches that were logged in the past (kept / dropped: 32/859, 33/1014,
    60/728, 62/1084, 74/1216, 148/1020, 154/1093, 262/705, 308/885, 323/1117, 328/1095, 347/1007, 403/852, 406/863,
    510/1150, 524/1221, 745/756, 765/1086). In every group the older row also has the lower id. The newer row's
    scores and review are not migrated.
  - So the import drops **19 rows** and creates **1,203 movies** from the current `.mdb`.
  - **7 of those groups also disagree on movie attributes** (the year and director are identical). They were checked
    against **IMDb** (2026-10-28, IMDb GraphQL API; title, year and director match IMDb for all 7). The kept (older)
    row is imported, and the import **corrects it to the IMDb value** where it is wrong (§6.3, "Attribute
    corrections"):

    | IDs (kept / dropped) | Title, year (IMDb id) | Conflict (kept / dropped) | IMDb | Imported value |
    |---|---|---|---|---|
    | 32 / 859 | Unforgiven, 1992 (tt0105695) | runtime 130 / 131 | 130 min; Drama, Western | unchanged (130) |
    | 33 / 1014 | GoldenEye, 1995 (tt0113189) | runtime 145 / 130; title "Goldeneye" / "GoldenEye" | "GoldenEye", 130 min | **title "GoldenEye", runtime 130** |
    | 148 / 1020 | Die Another Day, 2002 (tt0246460) | runtime 132 / 133 | 133 min | **runtime 133** |
    | 262 / 705 | The Rainmaker, 1997 (tt0119978) | genre Thriller / Misdaad | 135 min; Crime, Drama, Thriller | unchanged (Thriller is an IMDb genre; runtime 135 correct) |
    | 308 / 885 | Twelve Monkeys, 1995 (tt0114746) | genre Actie / Sci-fi | 129 min; Mystery, Sci-Fi, Thriller | **genre Sci-fi** (Action is not an IMDb genre); runtime 129 correct |
    | 328 / 1095 | Coming to America, 1988 (tt0094898) | runtime 116 / 117 | 117 min; Comedy, Romance | **runtime 117** |
    | 765 / 1086 | The Dictator, 2012 (tt1645170) | runtime 99 / 83 | 83 min; Comedy | **runtime 83** |

    - IMDb titles the film "12 Monkeys". The owner's title "Twelve Monkeys" is kept; titles are not normalised to
      IMDb.
    - Only these 7 conflict groups were checked. The other rows are imported as entered (after the cleanup rules).

---

## 3. Decisions (answers from the owner)

| # | Topic | Decision |
|---|---|---|
| 1 | Language | Dutch UI only. Code, DB, models and docs are in English. |
| 2 | Datum / duplicates | `Datum` = viewing date. Titles are not unique; same title with a different year or director is a different movie. Uniqueness is on **Title + Year + Director**. |
| 2a | Re-watches | **Not logged.** One table, one row per movie (no separate viewings table). |
| 3 | Score 0 / Extras `n/a` | Become empty (`NULL`). |
| 4 | Dirty data | Clean up during import (§6.3). |
| 5 | `tblFilmInBezit` | Legacy, not migrated. |
| 6 | Delete | Not in scope. |
| 7 | Search | Debounced search on title, no Enter needed. |
| 8 | Default sort | Datum DESC, Title ASC. |
| 9 | Save | Explicit Save button. |
| 10 | Genre / Gezien op | Free text with suggestions. New values are added to suggestion lists stored in the DB. |
| 11 | Field lengths | Allow longer fields, especially Opmerkingen. |
| 12 | Audit columns | Yes. |
| 13 | Auth | Login required, similar to Motorsports. |
| 14 | Culture / time zone | nl-BE. Time zone configurable through `App__TimeZone` (default `Europe/Brussels`). |
| 15 | Design | Modern, hi-tech. MudBlazor plus free assets, following the designer spec in §8. |
| 16 | EF provider | `Microting.EntityFrameworkCore.MySql` (Pomelo has no EF Core 10 release). |
| 17 | CI | Out of scope. |
| 18 | Import | One-time, scripted and repeatable from scratch. |
| 19 | Browser tests | Playwright. |
| b | Duplicate Title + Year + Director (18 re-watch groups) | **Keep the oldest row** (earliest Datum, then lowest FilmID); drop the newer row, including its scores and review. Where the groups disagreed on runtime, genre or title casing, the kept row is corrected to the IMDb-verified value (§2.3). |
| c | Skyfall 747/749 | **Keep 747**, drop 749 (also what the rule in b yields: same date, lower id). |
| d | Terms | `StoryScore` for "Film", `Medium` for "Gezien op/in", `Review` for "Opmerkingen". Dutch word for a viewing: **"Vertoning"**. |
| e | Environments | Production runs on the Motorsports server (shared MariaDB instance, NPM proxy). Development uses the **local Docker MariaDB at `localhost:3306`**, with a dedicated `moviedb` database. The owner configures the production connection string through env vars in the compose stack. |
| f | Production data | Import **locally**, then produce a **`.sql` data dump** that the owner runs on the production database. |
| g | Users | Seed a default user **`admin`** once (when `users` is empty). Its password comes from the `Auth:DefaultPassword` setting (env `Auth__DefaultPassword`, set in the compose stack from `${MOVIEDB_DEFAULT_PASSWORD}`; the `http` launch profile and the characteristics tests set `demodemo`). It is never hard-coded in the app. Startup fails with a clear message when the table is empty and no password is configured. The owner may change the password after logging in. |
| h | Formatting | CSharpier with **LF** line endings (as Motorsports). |
| i | IDs | Vogen strongly typed IDs (as Motorsports). |
| j | M1 implementation choices | `UserId` is a GUID string (`users.id` varchar(36)), as Motorsports. Auth throttle table is `auth_throttle_states`, DataProtection table `data_protection_keys`. The initial migration is hand-written with self-guarding `CREATE TABLE IF NOT EXISTS` / `DROP TABLE IF EXISTS` so it is retry-safe after a partial failure (the generated Designer and snapshot are kept). `UserClock` is a singleton and throws on an invalid `App:TimeZone`; this is validated at startup. `Health:PublicUrl` has no default; the public-host health check is registered only when set. The connection string must use `CharSet=utf8mb4` (validated at startup). Local dev port is 4200. |

---

## 4. Target solution

### 4.1 Stack

Mirror the Motorsports versions. Check the latest patch versions when scaffolding. Starting point (from
`C:\_git\Motorsports`, as of 2026-10-28): SDK 10.0.200 (`rollForward: latestFeature`, test runner
`Microsoft.Testing.Platform` in `global.json`), CSharpier 1.3.0, dotnet-ef 10.0.14, MudBlazor 9.11.0,
`MudBlazor.Translations` 3.x (the line that matches MudBlazor 9; new, Motorsports does not use it),
Microting.EntityFrameworkCore.MySql 10.0.13, FluentValidation 12.2.0, Vogen 8.0.7, Serilog.AspNetCore 10.0.0,
Roslynator 5.0.1, Testcontainers.MariaDb 4.16.0, xunit.v3 4.0.2, Shouldly 4.3.0, bunit 2.12.5, NetArchTest.Rules 1.3.2,
Microsoft.Playwright 1.64.1. Motorsports has **no** browser tests and no request localization; both are new here.

| Concern | Choice |
|---|---|
| Runtime | .NET 10 (SDK pinned in `global.json`, `rollForward: latestFeature`), C# 14 |
| UI | Blazor Web App. Static SSR for login/error pages; **Interactive Server** for overview and edit |
| Components | **MudBlazor 9.x** + `MudBlazor.Translations` (nl-BE) |
| Persistence | EF Core 10 + `Microting.EntityFrameworkCore.MySql` 10.x, `IDbContextFactory` |
| Database | **MariaDB 12.3.3** everywhere: local dev container, Testcontainers and production (§11.1). |
| Auth | ASP.NET Core Identity **Core** + cookie authentication (Motorsports pattern) |
| Validation | FluentValidation (assembly scan, singleton validators) |
| Logging | Serilog console sink, structured |
| Strongly typed IDs | Vogen (`MovieId`, `GenreId`, `MediumId`, `UserId`) |
| Formatting | CSharpier (local tool, `.csharpierrc.yaml`, printWidth 120, LF) |
| Analyzers | Roslynator, `TreatWarningsAsErrors` |
| Tests | xUnit v3 (Microsoft.Testing.Platform), Shouldly, Testcontainers.MariaDb, NetArchTest, bUnit, Playwright .NET |

### 4.2 Repository layout (mirrors Motorsports)

```
/
├─ AGENTS.md, PLAN.md, README.md
├─ global.json, dotnet-tools.json (csharpier, dotnet-ef), .csharpierrc.yaml
├─ version.txt                       # e.g. 1.0.0 → version.props → image tag
├─ backup.ps1                        # mariadb-dump of the moviedb database
├─ scripts/Import-Access.ps1         # local import + production data dump (§6)
├─ artifacts/                        # git-ignored; the import dump lands in artifacts/import/
├─ docker-compose/
│  └─ moviedb-web/docker-compose.yml # app container behind the existing proxy
└─ src/
   ├─ MovieDB.slnx, Directory.Build.props, Directory.Packages.props, version.props
   ├─ MovieDB.Web/            # host: Program, composition, App/Routes/MainLayout, health, Dockerfile, --health-check
   ├─ MovieDB.Auth/           # login/logout/access denied/change password pages, auth services
   ├─ MovieDB.Common/         # Result<T>, Error, ErrorKind, small primitives (no EF/ASP.NET)
   ├─ MovieDB.Ui/             # theme, shared components (ScoreInput, SuggestField, ScorePill, ConfirmDialog, …)
   ├─ MovieDB.Infrastructure/ # MovieDbContext, entities, configurations, migrations, DatabaseMigrator, seeders, auth stores
   ├─ MovieDB.Movies/         # feature: overview + edit pages, queries, commands, validators, MoviesModule
   ├─ tools/MovieDB.AccessImport/  # net10.0 (not -windows), PlatformTarget x64, System.Data.Odbc; not part of the image
   └─ tests/
      ├─ MovieDB.UnitTests/
      ├─ MovieDB.CharacteristicsTests/  # WebApplicationFactory + Testcontainers MariaDB 12.3.3
      ├─ MovieDB.ArchitectureTests/     # NetArchTest
      └─ MovieDB.BrowserTests/          # Playwright .NET
```

- M0 also changes `.editorconfig` to `end_of_line = lf` (matching CSharpier and Motorsports) and normalises existing
  files.
- `artifacts/`, `*.mdb` and `.env` are already git-ignored. Also ignore `*.sql` dumps outside `src/`.

**Dependency rules** (enforced by architecture tests):

- Web → everything. Movies, Auth → Common, Ui, Infrastructure.
- Ui → Common only. Infrastructure → Common. Common → nothing.
- Features never reference each other.
- AccessImport → Common, Infrastructure.
- Test projects may reference anything (the unit and characteristics tests reference AccessImport). Nothing in
  `src/MovieDB.*` references `tools/` or `tests/`.
- AccessImport targets plain `net10.0`, **not** `net10.0-windows`: a `net10.0` test project cannot reference a
  `-windows` project. Only the ODBC reader needs Windows; keep it behind an `IFilmSource`-style abstraction so the
  rules (cleanup, de-duplication, corrections, report) are platform-neutral and unit-testable (§9).
- Razor components in Auth, Movies and Ui are discovered through `AdditionalAssemblies` on the router and
  `MapRazorComponents` in the host (as Motorsports does).
- The host exposes `public partial class Program` for `WebApplicationFactory<Program>`.

Types are `internal` by default; each slice exposes a `*Module.Add<Slice>Services()` entry point. Use
`InternalsVisibleTo` for tests (as Motorsports does in `Directory.Build.props`).

### 4.3 Naming

- C#: English. Entities are `Movie`, `Genre`, `Medium`, `User`. There is no `Viewing` entity (re-watches are not
  logged).
- **Database: lowercase `snake_case`, plural table names** (`movies`, `genres`, `media`). This differs from
  Motorsports (PascalCase); it is deliberate.
- Routes and UI strings: Dutch (`/films`, `/films/nieuw`, `/films/{id}`, `/login`, `/account/wachtwoord`; logout is
  POST `/logout`).
- Glossary (UI): a single viewing of a film is a **"Vertoning"**. Use this word wherever the UI refers to the viewing
  itself (Datum + Gezien op/in), for example as the sub-heading that groups those two fields.

**Term mapping** (decided):

| Access | Dutch UI label | C# property | DB column |
|---|---|---|---|
| FilmID | – (shown small, and in the URL) | `Movie.Id` | `movies.id` |
| FilmTitel | Filmtitel | `Title` | `title` |
| Genre | Genre | `Genre` | `genre_id` → `genres.name` |
| Duur | Duur (min) | `RuntimeMinutes` | `runtime_minutes` |
| Jaar | Jaar | `ReleaseYear` | `release_year` |
| Regisseur | Regisseur | `Director` | `director` |
| Datum | Datum | `WatchedOn` | `watched_on` |
| Gezien op | Gezien op/in | `Medium` | `medium_id` → `media.name` |
| Opmerkingen | Opmerkingen | `Review` | `review` |
| Totaal | Totaal (geen gemiddelde) | `OverallScore` | `overall_score` |
| Film | Film | `StoryScore` | `story_score` |
| Beeld | Beeld | `PictureScore` | `picture_score` |
| Muziek | Muziek | `MusicScore` | `music_score` |
| Geluid | Geluid | `SoundScore` | `sound_score` |
| Extras | Extra's | `ExtrasScore` | `extras_score` |

---

## 5. Data model (MariaDB)

Database `moviedb`, charset `utf8mb4`, collation **`utf8mb4_uca1400_ai_ci`** (case- and accent-insensitive; available
since MariaDB 10.10). This makes uniqueness and search behave as the user expects without
`ToLower()`.

### 5.1 Tables

**`movies`** (one row per Access record)

| Column | Type | Rules |
|---|---|---|
| `id` | INT AUTO_INCREMENT PK | Access FilmID preserved; new rows continue after the max imported id |
| `title` | VARCHAR(200) NOT NULL | trimmed, 1–200 |
| `release_year` | SMALLINT NOT NULL | 1888 … current year + 5 (validator only; "current year" from `UserClock`) |
| `director` | VARCHAR(150) NOT NULL | trimmed, 1–150 |
| `genre_id` | INT NOT NULL FK → `genres` | RESTRICT |
| `runtime_minutes` | SMALLINT NOT NULL | 1–999 (CHECK constraint + validator) |
| `watched_on` | DATE NOT NULL | default today in `App:TimeZone`; 1900-01-01 … today + 1 day (validator only; "today" from `UserClock`) |
| `medium_id` | INT NULL FK → `media` | RESTRICT |
| `overall_score`, `story_score`, `picture_score`, `music_score`, `sound_score`, `extras_score` | TINYINT NULL | 1–10 or NULL (CHECK constraint + validator) |
| `review` | MEDIUMTEXT NULL | validated max 20,000 chars; empty → NULL |
| `created_at`, `updated_at` | DATETIME(6) NOT NULL | UTC, set from `TimeProvider` |
| `version` | INT NOT NULL | concurrency token: 1 on insert (including imported rows), incremented by 1 by the app on every update (set explicitly in the update command; EF `IsConcurrencyToken`) |
| **UQ** | (`title`, `release_year`, `director`) | `ux_movies_title_year_director` |
| Index (optional) | (`watched_on` DESC, `title`, `id` DESC) | default sort `watched_on DESC, title ASC, id DESC`. A plain ascending index cannot serve this mixed direction, so declare the descending key parts explicitly (MariaDB 10.8+ supports them) or skip the index: at ~1.2k rows a filesort is fine. Do not add indexes for the other sort columns. |

**`genres`** and **`media`** (suggestion lists): `id` INT PK, `name` VARCHAR(50) NOT NULL **unique**, `created_at`.
Typed values are trimmed and validated to 1–50 characters ("Genre mag maximaal 50 tekens bevatten.").
Suggestions are listed alphabetically (collation order) and there is no UI to rename or remove them in the MVP.

- The import fills them: the Access form lists (21 genres including "Documentaire"; Blu-ray, DVD, Video, TV, Bioscoop,
  Andere) plus any other value found in the data. They are not seeded by a migration, so the production dump can
  insert them into empty tables.
- Their values are Dutch domain data.

**Auth tables** (copy the Motorsports shape, renamed to snake_case): `users` (id, user_name, normalized_user_name
unique, password_hash, security_stamp, concurrency_stamp, created_at, updated_at), `data_protection_keys`, and the
auth throttle table(s).

### 5.2 Rules

- **Uniqueness**: Title + Year + Director is unique. On save, check first and return a validation error on Filmtitel:
  "Deze film bestaat al (Jaar en Regisseur zijn gelijk)." with a link "Bekijk bestaande film" to `/films/{id}`. Also map
  a unique-key violation from a race to the same error.
- **Suggestion values**: on save, the typed Genre/Medium value is trimmed and resolved by case- and
  accent-insensitive match (collation). If no match exists, a new row is inserted **in the same transaction**. Handle
  the unique-key race by re-reading. An empty Medium means NULL; Genre is required.
- **Concurrency**: `version` is checked on update. A conflict becomes an application error, not a 500 (§7.4).
- **Restrictive FKs**; no cascades. No delete in the MVP.
- **Audit**: `created_at` and `updated_at` are stored in UTC and shown in the user's time zone. Imported rows get the
  import time for both (Access has no audit data). Saving without changes still updates `updated_at` and `version`
  (no dirty-field comparison on the server).

### 5.3 Migrations and seeding

- Explicit EF Core migrations with working `Down` methods. Generate the reviewed SQL with
  `dotnet ef migrations script --idempotent`.
- `DatabaseMigrator` (an `IHostedLifecycleService.StartingAsync`, copy from Motorsports) applies migrations on startup
  under `GET_LOCK('moviedb-migrate', 120)` on a dedicated connection. Split it into a migrate-only method (public,
  used by the import tool) and the startup path (migrate + seed). EF keeps its default history table
  `__EFMigrationsHistory` (§6.5 relies on it).
- **Default user seeding** runs after the migrations, inside the same lock. If the `users` table is **empty**, create
  user `admin` with the password from `Auth:DefaultPassword` (env `Auth__DefaultPassword`; if it is missing, fail
  startup) through `UserManager`/`PasswordHasher` (never store a literal hash). If any user
  exists, do nothing. So it happens exactly once per database.
  - While `admin` still has the default password, log a Warning at startup: "Default admin password is still in use".
    Detect it after seeding by verifying the configured password against the stored hash with `PasswordHasher` (result
    not `Failed`). Do not log the password itself. There is no UI banner.
- MariaDB DDL is not transactional, so migrations must be safe to re-run after a partial failure (self-guarding SQL
  where needed). Test repeated and concurrent startup.
- No `EnableRetryOnFailure` (it conflicts with user transactions; Motorsports gotcha).

---

## 6. Access import and production dump

### 6.1 Tool

`src/tools/MovieDB.AccessImport`: a console app targeting plain `net10.0` (see §4.2) with `PlatformTarget=x64` (the Access ODBC
driver "Microsoft Access Driver (*.mdb, *.accdb)" is 64-bit only on this machine). It reads `FilmsBase.mdb`
**read-only** through `System.Data.Odbc` and writes through `MovieDbContext` (or batched SQL), reusing the
Infrastructure mapping.

### 6.2 Script

`scripts/Import-Access.ps1` is the single entry point. It always runs on the Windows dev machine against the **local**
database:

```
./scripts/Import-Access.ps1 -Source .\FilmsBase.mdb [-ConnectionString <cs>] [-Reset] [-DryRun]
                            [-DumpPath .\artifacts\import\moviedb-data.sql] [-MariaDbContainer mariadb]
                            [-SkipDump] [-AllowRemote]
```

- `-DryRun`: read, clean, de-duplicate and correct in memory, print the report (§6.4), write nothing (no migration,
  no import, no dump, no connection to the target DB unless the temporary-table fallback of §6.3 is needed). Report
  lines that need the DB (max id and next AUTO_INCREMENT, DB-side checks) print "n/a (dry run)".
- `-SkipDump`: import and report, but skip steps 7–8 (useful while iterating).
- `-AllowRemote`: allow a non-`localhost`/`127.0.0.1` host. Never used for production (§11.2).
- Exit code 0 only when every step succeeded; any failure stops the script with a non-zero exit code.

1. Resolve the connection string from `-ConnectionString`, else env `MOVIEDB_CONNECTION_STRING`, else the
   `MovieDB.Web` user-secrets, else a secure prompt. Never echo it. Refuse non-local hosts unless `-AllowRemote` is
   given.
2. Build the import tool (x64, Release).
3. Apply migrations (same migrator as the app, same named lock). The tool calls the migrate-only entry point of
   `DatabaseMigrator` (§5.3); it does **not** seed the default user (that is the app's job on startup).
4. Refuse to run if the domain tables (`movies`, `genres`, `media`) contain data, unless `-Reset` is given. `-Reset`
   asks for typed confirmation, then empties **only** those tables. It never touches users, data protection keys or
   auth state.
5. Import everything in **one transaction**: suggestion lists first, then movies with explicit IDs. InnoDB moves
   AUTO_INCREMENT past the max explicit id by itself. Verify that, and do not use `ALTER TABLE`.
6. Print the verification report (§6.4), including the DB-side numbers read back after the commit. (`-DryRun` skips
   steps 3–8 and prints the in-memory report only.)
7. Write the **production data dump** (§6.5) to `-DumpPath` (default `artifacts/import/moviedb-data.sql`).
8. **Verify the dump**: drop and create a scratch database `moviedb_dumpcheck` (same charset/collation), migrate it
   with the tool (connection string with `Database=moviedb_dumpcheck`), run the dump in it exactly as in production
   (§11.2 step 4: `docker cp` into the container, then `mariadb … < /tmp/file` inside `sh -c`), compare row counts and
   an ordered SHA-256 hash of all columns per table (`genres`, `media`, `movies`) with `moviedb`, run the dump a second
   time and verify that the guard refuses it (non-empty tables), then drop the scratch database and the `/tmp` copy.

- `mariadb-dump`/`mariadb` inside the container use the app user's credentials from the resolved connection string.
  Pass the password through `docker exec -e MYSQL_PWD=…` (never as a `-p…` argument, never echoed or logged).
- The `moviedb` user has privileges on `moviedb.*` and `moviedb_dumpcheck.*` (§11.1); the script never needs root.

### 6.3 Mapping and cleanup rules

| Source | Rule |
|---|---|
| Text fields | Split on CR/LF/tab, trim each part, drop empty parts. Use the **first** non-empty part (first and last always agree in this data). Report every row where parts were dropped. |
| Titles / directors | Then collapse internal runs of spaces into one. Keep case and Unicode as-is. |
| `Duur` | Trim, parse int (`"099"` → 99). Must be 1–999. |
| `Jaar` | Parse int. |
| `Datum` | Date part only. |
| Scores | `0` → NULL; 1–10 kept; anything else is an import error. |
| `Extras` | `n/a` → NULL; a digit 1–10 → int. |
| `Gezien op` | Trim; empty → NULL; resolve to `media`. |
| `Genre` | Trim; resolve to `genres`. |
| `Opmerkingen` | Normalise line endings to `\n`, trim; empty → NULL. |
| Duplicates | After cleanup, group rows by Title + Year + Director **with the DB collation semantics** (case- and accent-insensitive). A .NET comparer (for example ICU `IgnoreCase | IgnoreNonSpace`) is only an approximation of `utf8mb4_uca1400_ai_ci` (for example `ø`, `ß`, `æ`, `œ`, punctuation). Therefore: (1) plan the groups in .NET; (2) a characteristics test compares the comparer with the real DB collation for a synthetic set of variants (case, `é`/`e`, `ø`/`o`, `å`/`a`, `ç`/`c`, `ß`/`ss`, ligatures, trailing spaces); (3) the unique key on `movies` is the safety net, so a missed duplicate fails the single import transaction loudly instead of importing it. If (2) shows that the comparer disagrees, detect the groups with a query on a session-scoped temporary table in the target DB (same collation) instead, which also works for `-DryRun` (§6.2). In each group **keep the row with the earliest `Datum`, then the lowest `FilmID`**; drop the others completely (no merging of scores, reviews or attributes). Report every group as kept id / dropped ids. The rule is generic, so a re-import from a newer `.mdb` (at deployment) handles new duplicates the same way. For the current `.mdb` it drops exactly 859, 1014, 728, 1084, 1216, 1020, 1093, 705, 885, 1117, 1095, 1007, 852, 863, 1150, 1221, 756, 1086 and 749 (Skyfall: 747 kept). |
| Attribute corrections | After duplicate resolution, apply a fixed, code-reviewed list of IMDb-verified corrections to kept rows (§2.3). Each entry is (FilmID, field, expected source value after cleanup, corrected value): 33 Title "Goldeneye" → "GoldenEye"; 33 RuntimeMinutes 145 → 130; 148 RuntimeMinutes 132 → 133; 308 Genre "Actie" → "Sci-fi"; 328 RuntimeMinutes 116 → 117; 765 RuntimeMinutes 99 → 83. Apply an entry only when the source still holds the expected value. If the source already holds the corrected value, or something else (for example, because the owner edited it in Access before the deployment import), skip it and report it as skipped. The list lives in the import tool (not in config), with unit tests. |

Any row that cannot be mapped fails the whole import. No partial imports.

### 6.4 Verification report (counts only, never row contents)

The report prints:

- Source rows, dropped duplicate rows (kept id → dropped ids per group), movies created, genres, media.
- Rows cleaned per rule (IDs).
- Attribute corrections applied / skipped (FilmID, field; never review text). Expected for the current `.mdb`: 6
  applied, 0 skipped.
- NULL counts per score column.
- Max lengths.
- Min and max dates.
- Max id and the next AUTO_INCREMENT value.

Expected for the current `.mdb`: 1,222 source rows, 19 dropped duplicates, **1,203 movies**, 21 genres, 6 media;
every non-dropped source id is present with the same id.

### 6.5 Production data dump

- Created with `mariadb-dump` **inside the MariaDB container** (`docker exec <container> mariadb-dump … --result-file=/tmp/…`
  followed by `docker cp`). Do not use PowerShell `>` redirection: Windows PowerShell writes UTF-16 and would corrupt
  the file.
- Options: data only (`--no-create-info`, `--no-create-db`, `--skip-triggers`), `--complete-insert`,
  `--default-character-set=utf8mb4`, `--single-transaction`, `--skip-add-locks`, `--skip-disable-keys` (its
  `ALTER TABLE … DISABLE KEYS` would implicitly commit), `--no-tablespaces`. Dump a single database without
  `--databases`, so the file has no `CREATE DATABASE`/`USE` and runs against the database named on the command line.
  Tables in FK order: `genres`, `media`, `movies`. All columns are included (`created_at`, `updated_at`, `version`).
  No users, keys, throttle state or migration history.
- The script wraps the dump:
  - A header comment with the source file name, the import time, the row counts and the **latest migration id**.
  - `SET NAMES utf8mb4;`.
  - A **guard block** (`DELIMITER //` … `BEGIN NOT ATOMIC … END //` … `DELIMITER ;`, with `SIGNAL SQLSTATE '45000'`
    and a clear English message). It aborts unless `MAX(MigrationId)` in `__EFMigrationsHistory` (the EF default
    history table; do not rename it) **equals** that latest migration id, and unless `movies`, `genres` and `media`
    are empty.
  - `START TRANSACTION;` … inserts … `COMMIT;`. The `mariadb` client (batch mode, no `--force`) stops at the first
    error and the open transaction rolls back when the connection closes.
- The dump contains personal reviews. It stays under the git-ignored `artifacts/`, is never committed and is deleted
  after the deployment.

---

## 7. Cross-cutting

### 7.1 Authentication (copy from Motorsports `src/Motorsports.Infrastructure/Auth`, `src/Motorsports.Auth`)

- `AddIdentityCore<User>` with a custom `UserStore` on EF, `AddSignInManager`, and `PasswordHasher`. Minimum
  password length 8.
- Cookie `moviedb.auth`: HttpOnly, SameSite=Lax, 7-day sliding expiration. `Secure` is driven by
  `Auth:SecureCookies`. Revalidate the security stamp every 5 minutes.
- **Fallback authorization policy requires an authenticated user.** Only `/login`, `/accessdenied`, health endpoints
  and static assets allow anonymous access. Use `AuthorizeRouteView`.
- Pages are static SSR with `<AntiforgeryToken/>`:
  - `/login` (Dutch: "Aanmelden", "Gebruikersnaam", "Wachtwoord", "Onthoud mij"), local-only `returnUrl`.
  - POST `/logout`.
  - `/accessdenied`.
  - `/account/wachtwoord` ("Wachtwoord wijzigen": current, new, confirm). It updates the security stamp and keeps the
    current session signed in. Changing the password is optional; there is no forced change (do not port Motorsports'
    `UseForcePasswordChange`).
- Unknown users get a dummy password-hash verification (timing equalisation). Use a generic error message:
  "Aanmelden mislukt."
- MariaDB-backed login throttling per account, IP, device and globally (`Auth:Protection:*`). It fails closed.
- DataProtection keys are persisted in the DB. Production requires a PFX certificate
  (`DataProtection:CertificatePath` / `CertificatePassword`); startup fails without it.
- Users: the default `admin` user is seeded once (§5.3). There is no registration UI and no user management.
- **Security headers** (copy the Motorsports middleware in `Program.cs`): a per-request CSP nonce
  (`script-src 'self' 'nonce-…'`, `style-src 'self' 'unsafe-inline'`, `font-src 'self' data:`,
  `connect-src 'self' ws: wss:`, `frame-ancestors 'none'`, `form-action 'self'`), `X-Content-Type-Options`,
  `X-Frame-Options: DENY`, `Referrer-Policy`, `Permissions-Policy`, HSTS outside Development. All `<script>` tags carry
  the nonce. Add a characteristics test for the header, as Motorsports does.
- **Render modes** (as Motorsports `App.razor`): global interactive routing with `InteractiveServer`; the auth and
  error pages opt out with `[ExcludeFromInteractiveRouting]` and stay static SSR. Prerendering stays on; pages must
  load their data once (load in `OnInitializedAsync` only when interactive, or use `PersistentComponentState`) and
  show the loading state meanwhile.

### 7.2 Configuration (Options + `ValidateOnStart`)

| Key | Env var | Default / notes |
|---|---|---|
| `ConnectionStrings:MovieDb` | `ConnectionStrings__MovieDb` (compose maps it from `MOVIEDB_CONNECTION_STRING`) | user-secrets in dev; must contain `CharSet=utf8mb4` |
| `App:TimeZone` | `App__TimeZone` | `Europe/Brussels`; must be a valid IANA id |
| `App:Culture` | – | fixed `nl-BE` (not configurable in the MVP) |
| `Auth:SecureCookies` | `Auth__SecureCookies` | `true` in `appsettings.json`; `false` in `appsettings.Development.json` |
| `Auth:Protection:*` | | as Motorsports |
| `Auth:DefaultPassword` | `Auth__DefaultPassword` (compose: `${MOVIEDB_DEFAULT_PASSWORD:?…}`) | password of the seeded `admin`; only read when `users` is empty, then required (startup fails without it). Dev: set by the launch profile |
| `DataProtection:CertificatePath` / `CertificatePassword` | compose: `DataProtection__CertificatePassword=${DATAPROTECTION_CERTIFICATE_PASSWORD}` | required outside Development |
| `ForwardedHeaders:KnownProxies:0` | compose: `ForwardedHeaders__KnownProxies__0=${FORWARDED_HEADERS_KNOWN_PROXY_0}` | proxy container IP |
| `Health:PublicUrl` | `Health__PublicUrl` | optional; when empty (dev, tests) the public-URL check is not registered |

- Time: inject `TimeProvider`, plus a `UserClock` (as Motorsports) to convert to and from `App:TimeZone`. "Today" for
  the default `Datum` comes from `UserClock`.
- **Globalization**: unlike Motorsports, set `InvariantGlobalization=false`. nl-BE formatting needs ICU, so use the
  `aspnet:10.0-noble-chiseled-extra` runtime image (ICU + tzdata). Motorsports has no request localization, so set it
  up here: `UseRequestLocalization` with `nl-BE` as the default and only supported culture (also for the Blazor
  circuit), `AddMudTranslations()` (the package needs this to work), `<html lang="nl-BE">`. Dev machines run on
  Windows with ICU, where IANA time zone ids resolve; Motorsports' `UserClock.ResolveZone` fallback to
  `TimeZoneInfo.Local` is then only a last resort and must not hide an invalid `App:TimeZone` (that fails validation
  on start).
- **`UserClock`**: copy it to Common, but resolve "today" from the configured `App:TimeZone` only. Motorsports applies
  the browser time zone after first render and forbids using it for validation; MovieDB has no browser-zone
  detection, so the same clock is used for the default Datum, the validators (max `watched_on`, max `release_year`)
  and display. Register it as a singleton.
- Formatting: dates `dd/MM/yyyy`, thousands separator `.` (for example "1.203 films").

### 7.3 Logging and health

- Serilog console sink, structured. Never log review text, passwords, connection strings or full rows.
- `/health/live`: no dependencies. `/health`: DB reachable plus the public URL. A `--health-check` CLI switch is used
  by the Docker healthcheck (as Motorsports).

### 7.4 Errors and validation

- `Result<T>` / `Error` / `ErrorKind` in Common (copy from Motorsports). Commands return results; pages map them to
  field errors, alerts or snackbars.
- Unique-key violations become a validation error. `DbUpdateConcurrencyException` becomes a `Conflict`, which drives
  the concurrency UI (§8.4). Not-found becomes a 404 page.
- FluentValidation messages are in Dutch, for example "Filmtitel is verplicht.", "Jaar moet tussen 1888 en {max}
  liggen.", "Score moet tussen 1 en 10 liggen."
- **Required**: Filmtitel, Genre, Duur, Jaar, Regisseur, Datum (as in Access). **Optional**: Gezien op/in,
  Opmerkingen, all six scores. Ranges and lengths are in §5.1; text is trimmed before validation.

---

## 8. Functional and UI specification

The designer spec is for a modern, hi-tech look with a dark-first theme. MudBlazor is the only component framework.

### 8.1 Theme and assets

- **MudBlazor 9.x** with `MudBlazor.Translations` (nl-BE). Subclass `MudLocalizer` for any missing keys. Use Material
  icons (inline SVG).
- **Fonts, self-hosted** (no Google Fonts link): *Space Grotesk* for headings and the wordmark, *Inter* for body text
  with `font-variant-numeric: tabular-nums` for numbers. Both are under the OFL licence.
- **Dark by default** with a light/dark toggle. The cookie `films-theme` (`dark`/`light`, 1 year, SameSite=Lax) stores
  the choice. As in Motorsports: `App.razor` reads the cookie from `HttpContext` to set `data-theme` and the initial
  `IsDarkMode` (no flash); the toggle writes the cookie through a small nonce'd `app.js` helper via JS interop. Without
  a cookie the theme is dark (no system-preference detection, so SSR and the client agree).
- Dark palette:

  | Role | Value |
  |---|---|
  | background | #070B14 |
  | surface | #0E1626 |
  | surface-2 | #142036 |
  | primary (cyan) | #22D3EE (#0E7490 in light) |
  | secondary ("Access yellow") | #FFC83D |
  | tertiary (orange) | #FF7A1A |
  | evaluation accent | maroon gradient #7A1F2B → #B0263F |
  | text | #E6EDF7 / #9AA8BF |
  | success / warning / error / info | #34D399 / #FBBF24 / #F87171 / #60A5FA |
- Glass app bar (backdrop blur). Radius 10px, cards 14px. Subtle borders instead of heavy shadows.
- **Score pills** use the colour range 1–4 error, 5–6 warning, 7–8 primary, 9–10 success. They always show the number,
  never colour alone. An empty score shows "–".
- Respect `prefers-reduced-motion`. Focus ring is 2px #22D3EE. Text contrast is at least 4.5:1. Touch targets are at
  least 44px.

### 8.2 Shell and login

- `MudLayout` with a 56px app bar containing:
  - The wordmark "Films" (orange → yellow gradient), which links to `/films`.
  - The theme toggle.
  - A user menu with "Wachtwoord wijzigen" and "Afmelden".
- No drawer. Add a skip link "Naar inhoud".
- Max content widths: overview 1600px, edit 1280px, login 400px.
- Dutch reconnect UI ("Verbinding verbroken – opnieuw verbinden…").
- **Login** (`/login`, static SSR `EditForm`): a centred card on a radial #004080 glow with a subtle film-strip
  pattern, the wordmark and the subtitle "Database van films".
- `/` redirects to `/films`.
- Page titles: "Films", "Nieuwe film – Films", "{Titel} – Films", "Aanmelden – Films", "Wachtwoord wijzigen – Films".

### 8.3 Overview `/films`

- **One row per film** (equivalent to an Access record).
- **Sticky toolbar**:
  - `MudTextField` with `Immediate`, `DebounceInterval=300` and placeholder "Zoek op titel…". The search starts
    without Enter and has a clear button.
  - A live count: "1.203 films", or "12 van 1.203 films" when filtered.
  - The primary button "Nieuwe film" (a FAB on xs/sm).
- **md and up**: `MudDataGrid` with `ServerData`, `FixedHeader`, `Dense`, row hover and `RowClick` → `/films/{id}`.
  The title is also a real link (keyboard and middle-click). The pager offers 25/50/100, default 50.

  | Column | Header | Sortable | Notes |
  |---|---|---|---|
  | WatchedOn | Datum | ✓ | `dd/MM/yyyy` |
  | Title | Filmtitel | ✓ | link |
  | ReleaseYear | Jaar | ✓ | |
  | Director | Regisseur | ✓ | hidden < lg |
  | Genre | Genre | ✓ | chip, hidden < lg |
  | RuntimeMinutes | Duur | ✓ | "123 min", hidden < lg |
  | Medium | Gezien op/in | ✓ | hidden < lg |
  | OverallScore | Totaal | ✓ | score pill, NULLs last |

- **xs/sm**: a card list (title, year, date, genre, total pill) with `MudPagination`, a "Sorteren op" select and a
  direction toggle.
- **Paging, not virtualization.** All filtering, sorting and paging run server-side with no-tracking projections.
- **Default sort**: `watched_on DESC, title ASC, id DESC` (id makes the order stable). Clicking a column sorts by it,
  then by `id` in the same direction. Genre and Medium sort by name. **NULLs sort last in both directions**
  (Totaal, Gezien op/in), i.e. order by `(column IS NULL), column, id`.
- **Search**: case- and accent-insensitive `title LIKE %q%` through the collation; escape `%`, `_` and `\`. Trim the
  query and cap it at 100 chars.
- **URL state**: `?q=&sort=&dir=&page=&size=` (`NavigationManager`, replace history). Reload, back and share must
  restore the view. `sort` uses the C# property names in camelCase (`watchedOn`, `title`, …), `dir` is `asc`/`desc`,
  `size` is 25/50/100. Unknown values fall back to the defaults; a `page` beyond the last page clamps to the last page.
  Default values are omitted from the URL.
- Empty states: "Nog geen films." and "Geen films gevonden voor ‘{q}’." with a "Zoekopdracht wissen" button.
- Loading: a skeleton or progress bar. Errors: an alert with "Opnieuw proberen".

### 8.4 Edit / new `/films/{id:int}`, `/films/nieuw`

The layout is equivalent to `frmFilms`, modernised:

- Header: the "← Overzicht" link (back to the overview with its URL state) and the page title (the film title or
  "Nieuwe film").
- **Record navigation** button group: `Eerste`, `Vorige`, `Volgende`, `Laatste` and `Nieuw`, with the tooltips from
  §2.2 and Dutch aria-labels ("Eerste film", …). The position indicator reads "12 van 1.203".
  - Navigation follows the **overview's current sort and search**, passed through the query string as
    `?q=&sort=&dir=` (same names and defaults as §8.3). The edit page also passes `page` and `size` through
    untouched, so "← Overzicht" restores the exact overview view. Without context it uses the default sort.
    Eerste/Vorige are disabled on the first record; Volgende/Laatste on the last.
  - Each move runs a server query by sort-key seek, not by loading all ids (same ordering, including NULLs-last and
    the id tie-breaker, as the overview). The position comes from a `COUNT` of rows before the current one. If the
    current film no longer matches the search (for example, after its title was edited), navigation falls back to
    the default sort without search.
  - `Nieuw` opens `/films/nieuw` with the same context; after saving, the new film's position is computed in that
    context.
- `MudGrid` 8/4 layout. On xs/sm the cards stack.
  - **"Gegevens" card** (in tab order):

    | Field | Component |
    |---|---|
    | Filmtitel | text, required |
    | Genre | `SuggestField`: `MudAutocomplete` with `CoerceValue=true`, debounce 150, case-insensitive suggestions from `genres`, free text allowed |
    | Duur | `MudNumericField`, `HideSpinButtons`, adornment "min" |
    | Jaar | `MudNumericField`, 4 digits |
    | Regisseur | text |
    | *sub-heading "Vertoning"* | |
    | Datum | `MudDatePicker`, nl-BE, `dd/MM/yyyy`, Monday first, editable, default today |
    | Gezien op/in | `SuggestField` on `media`, optional |
    | Opmerkingen | `MudTextField`, `Lines=8`, `AutoGrow`, `MaxLines=30`, counter at 20.000 |

  - **"Evaluatie" card**: sticky on md+, with a maroon gradient header "EVALUATIE". It holds six `ScoreInput`s:
    "Totaal (geen gemiddelde)", "Film", "Beeld", "Muziek", "Geluid" and "Extra's".
    - `ScoreInput` = `MudToggleGroup<int?>` with "–" plus 1–10, or a `MudSelect` on xs.
    - For Extra's, the empty option reads "n.v.t.".
    - `MudRating` is rejected because it cannot show an empty state or a 10-point scale clearly.
- The record ID and audit info are shown in small muted text: "Film #1234 · aangemaakt …, gewijzigd …" in the user's
  time zone.
- **Duplicate check**: when Filmtitel, Jaar and Regisseur are all filled in, check on blur of any of these three
  fields whether **another** film (excluding the current id) has the same combination. If so, show the uniqueness
  error from §5.2 inline before saving. The server check on save is authoritative.
- **Sticky glass save bar**: shows "Niet-opgeslagen wijzigingen" when dirty, plus `Annuleren` (reverts) and `Opslaan`
  (primary, spinner while saving). **Ctrl+S** saves (a small nonce'd JS key handler that calls `preventDefault` and
  invokes the save through a `DotNetObjectReference`). `Annuleren` reverts to the last loaded state (defaults for a
  new film).
  - After a successful save: the snackbar "Film opgeslagen." (TopCenter). New records navigate to `/films/{id}`.
- **Validation**: `MudForm` + FluentValidation on the client, re-run on the server in the command. Show the summary
  "Controleer de gemarkeerde velden." and focus the first invalid field.
- **Unsaved changes**: `NavigationLock` (record navigation, links, back) opens a dialog "Niet-opgeslagen wijzigingen"
  with `Opslaan` / `Niet opslaan` / `Blijven`. The browser `beforeunload` prompt is on while dirty.
- **Concurrency conflict**: an alert "Deze film is intussen gewijzigd." with `Herladen` (discard and reload) and
  `Toch opslaan` (re-read the current `version` and save the user's values over it; still validated, including the
  uniqueness rule).
- Unknown id → 404 card "Film niet gevonden." with a "Naar overzicht" button.
- Defaults for a new record: Datum = today, all scores empty, Medium empty.

### 8.5 Error pages

- 404 and error pages use a card with a "Naar overzicht" button. The error page shows only the request ID, never
  exception details outside Development.

### 8.6 Equivalence checklist (acceptance for "functionally equivalent")

- [ ] All 14 Access fields are viewable and editable with the same labels, order and defaults (Datum = today).
- [ ] Genre and Gezien op/in offer the Access lists and accept new values, which then become suggestions.
- [ ] Scores 1–10 plus empty; Extra's empty = "n.v.t."; Totaal is manual (no averaging).
- [ ] Eerste / Vorige / Nieuw / Volgende / Laatste work, including at the boundaries.
- [ ] Every imported record shows the same values as in Access (after the documented cleanup and the 6 IMDb
  corrections; dropped duplicates are the newer rows listed in §2.3).
- [ ] Unicode text round-trips unchanged.

---

## 9. Testing

| Layer | Project | Scope |
|---|---|---|
| Unit | `MovieDB.UnitTests` | Import mapping/cleanup rules (synthetic strings mimicking the quirks), duplicate detection with case/accent variants, oldest-row-wins resolution (earliest date, then lowest id; same-date ties), attribute corrections (applied when the source matches, skipped otherwise), validators, sort/search parameter parsing, LIKE escaping, score mapping, `UserClock`. bUnit for `ScoreInput`, `SuggestField`, `ScorePill`. |
| Characteristics | `MovieDB.CharacteristicsTests` | Testcontainers `mariadb:12.3.3` + `WebApplicationFactory`. Migrations (fresh, re-run, concurrent startup); default user seeding (empty → `admin` created once; existing users → untouched; concurrent startup → one user); unique key + case/accent-insensitivity; suggestion insertion and race; concurrency conflicts; overview query (sort, search with `%`/`_`, paging, stable order); record-navigation queries at boundaries; auth (anonymous redirect, login success/failure, throttling, logout POST + antiforgery, local returnUrl only, change password); the import into a **synthetic** source; the dump guard (refuses non-empty tables and a wrong migration) and dump round-trip. |
| Architecture | `MovieDB.ArchitectureTests` | The dependency rules from §4.2; no feature-to-feature references; Common free of EF/ASP.NET. |
| Browser | `MovieDB.BrowserTests` | Playwright .NET against the app + Testcontainers DB with seed data. Flows: login; search (debounced) → open → edit → save; new film; duplicate film error; record navigation; unsaved-changes dialog; validation messages; change password; mobile (390px) and desktop (1440px) layouts. |

- Never use the real `FilmsBase.mdb` or its contents in tests. Never print rows in test output. Docker (Rancher
  Desktop) must run for characteristics and browser tests.
- Make the import tool's source reader an abstraction so tests can feed synthetic rows. The ODBC reader itself is
  verified manually in M3.
- **Browser tests** need a real HTTP listener: Playwright and the Blazor circuit (WebSocket) cannot use the in-memory
  `TestServer` of `WebApplicationFactory`. Start the host on a random local port (a Kestrel-backed fixture, or launch
  the built `MovieDB.Web` as a process with `ConnectionStrings__MovieDb` pointing at the Testcontainers DB), seed
  synthetic data, and install the Playwright browsers once (`playwright.ps1 install chromium`). This is new
  relative to Motorsports.

---

## 10. Milestones

Each milestone ends with the end-of-task routine in AGENTS.md: build with `-warnaserror`, CSharpier check and tests.

| # | Milestone | Done when |
|---|---|---|
| M0 | **Scaffold**: repo files (`global.json`, tools, `.csharpierrc.yaml`, `version.txt`, `.editorconfig` → LF), `src/` solution and projects, Directory props, empty test projects, architecture tests, README skeleton. The local `moviedb` database, user and user-secrets connection string already exist (§11.1, created by the owner); the `MovieDB.Web` project uses that `UserSecretsId` | Build and the (empty) test run pass |
| M1 | **Data** (done): entities, `MovieDbContext`, configurations, initial migration (domain + auth tables), `DatabaseMigrator`, health endpoints, options validation, Serilog | Characteristics tests for migrations and constraints pass; the app migrates the local `moviedb` |
| M2 | **Auth** (done, awaiting review): Identity Core stores, cookie auth, login/logout/access-denied/change-password pages, throttling, DataProtection, default `admin` seeding, fallback policy | Auth characteristics tests pass; local login as `admin` works |
| M3 | **Import**: tool, `Import-Access.ps1` (`-Reset`, `-DryRun`, dump + dump verification), verification report. Run it against the local `FilmsBase.mdb` | Report matches §6.4; dump verification passes; unit tests for the rules pass |
| M4 | **UI shell + overview**: theme, fonts, layout, login styling, overview with search/sort/paging/URL state, mobile cards | Characteristics + bUnit tests pass; manual check at 390px and 1440px |
| M5 | **Edit/new**: form, ScoreInput, SuggestField, validation, duplicate check, save, concurrency, record navigation, unsaved-changes guard | §8.6 checklist ticked; tests pass |
| M6 | **Browser tests + deployment**: Playwright suite, Dockerfile, compose file, `backup.ps1`, README (deploy, dump import, default user, backup/restore) | Playwright passes; image builds; container is healthy against the local MariaDB |

---

## 11. Local development and deployment

### 11.1 Local development

- Use the **existing local Docker MariaDB** (container `mariadb`, image `mariadb:12.3.3`, `localhost:3306`).
  - The owner has created the database `moviedb` (`CHARACTER SET utf8mb4 COLLATE utf8mb4_uca1400_ai_ci`) and the user
    `moviedb`@`%` with `ALL PRIVILEGES` on `moviedb.*` **and** `moviedb_dumpcheck.*` (the import's dump verification
    creates and drops that scratch database, §6.2). The agent never needs the root password.
  - The app user's password is chosen by the owner and stored only in user-secrets.
- Connection string in user-secrets (`dotnet user-secrets` on `MovieDB.Web`, `UserSecretsId`
  `9b46a74e-075e-4f32-b29a-dc9bd38c1cec`):
  `Server=localhost;Port=3306;Database=moviedb;User=moviedb;Password=…;CharSet=utf8mb4`.
- On first start, the app migrates and seeds `admin` with the password from `Auth__DefaultPassword` (`demodemo`,
  set by the `http` launch profile).
- Import with `./scripts/Import-Access.ps1 -Source .\FilmsBase.mdb`. This also produces the production dump.
- Development, tests and production all run MariaDB 12.3.3. Keep them aligned when upgrading.

### 11.2 Deployment (mirror Motorsports)

- `src/MovieDB.Web/Dockerfile`: build stage `sdk:10.0-noble`, runtime **`aspnet:10.0-noble-chiseled-extra`**, user
  `$APP_UID`, port 8080. The image tag comes from `version.txt` (`moviedb-web:<version>`).
- `docker-compose/moviedb-web/docker-compose.yml`: container `moviedb` on the external network `motorsports_net`, no
  host port, behind the existing Nginx Proxy Manager (TLS). The healthcheck runs
  `dotnet MovieDB.Web.dll --health-check`.
  - Environment variables, as in Motorsports (`KEY=${SOURCE_VAR:?…}` for required secrets):
    `ConnectionStrings__MovieDb=${MOVIEDB_CONNECTION_STRING}`, image tag `${MOVIEDB_VERSION}`,
    `App__TimeZone=Europe/Brussels`, `Health__PublicUrl=https://mdb.dalion.eu`, `Auth__SecureCookies=true`,
    `ForwardedHeaders__KnownProxies__0=${FORWARDED_HEADERS_KNOWN_PROXY_0:-172.18.0.3}` (the Nginx Proxy Manager
    container IP, confirmed by the owner),
    `DataProtection__CertificatePath=/run/secrets/dataprotection.pfx` and
    `DataProtection__CertificatePassword=${DATAPROTECTION_CERTIFICATE_PASSWORD}`, and
    `Auth__DefaultPassword=${MOVIEDB_DEFAULT_PASSWORD:?…}` (initial `admin` password, only used when the database has
    no users).
    The owner sets the values in the stack at deployment.
  - The PFX is bind-mounted read-only from `/etc/dalion/moviedb/secrets/`.
- **Database**: the existing production MariaDB 12.3.3 instance (container `mariadb`, hostname `mariadb`, on
  `motorsports_net`; see `docker-compose/mariadb` in the Motorsports repo), with a separate database `moviedb` and a
  separate user that has privileges on `moviedb` only (it needs DDL for the startup migrations). The production
  connection string therefore reads
  `Server=mariadb;Port=3306;Database=moviedb;User=moviedb;Password=<secret>;CharSet=utf8mb4`.
- **Ingress**: the container listens on 8080 (`ASPNETCORE_HTTP_PORTS=8080` in the image; do not set
  `Kestrel__Endpoints__Http__Url` as Motorsports' compose does). In Nginx Proxy Manager add a proxy host for the
  public hostname `mdb.dalion.eu` that forwards to `moviedb:8080` with TLS, "Force SSL" and HSTS, and WebSocket support
  enabled (Blazor Server needs WebSockets). `Health__PublicUrl` is `https://mdb.dalion.eu`, and
  `ForwardedHeaders__KnownProxies__0` is the NPM container IP `172.18.0.3` (confirmed by the owner). The app itself
  must also send an HSTS header (`UseHsts` outside Development) and treat the site as HTTPS-only (`Secure` cookies).
- **Compose service name**: `moviedb` (container and image `moviedb-web:${MOVIEDB_VERSION}`), which must differ from
  Motorsports' `motorsports` container.
- `backup.ps1` runs `mariadb-dump` of `moviedb`, excluding `data_protection_keys` data (as Motorsports). README also
  documents the restore (drop/create `moviedb` with the right charset and collation, load the dump, start the app).
- **First deployment** (documented step by step in README):
  1. Locally: run `Import-Access.ps1` on a fresh local database to produce and verify
     `artifacts/import/moviedb-data.sql`.
  2. Production: the owner has already created the database `moviedb` (utf8mb4, `utf8mb4_uca1400_ai_ci`) and its
     user, with a password different from the local one. Nothing to do for the agent.
  3. Deploy the container. It migrates the schema and seeds `admin` with the password from
     `MOVIEDB_DEFAULT_PASSWORD`.
  4. Copy the dump to the server, `docker cp` it into the MariaDB container, and run it there with
     `docker exec -it mariadb sh -c "mariadb -u root -p moviedb < /tmp/moviedb-data.sql"`. The guard block refuses to
     run on a wrong schema version or non-empty tables.
  5. Smoke-test: log in as `admin`, check the film count, open a film with accented text, and optionally change the
     password.
  6. Run `backup.ps1`. Delete every copy of the dump (local, server, container `/tmp`).
- The deploy procedure is manual and documented in README (as Motorsports).

---

## 12. Later phases (not now; shape decisions so they stay possible)

- Statistics (per year, genre, director, medium), charts.
- Filters (genre, medium, score range, year), full-text search in reviews.
- TMDb/IMDb metadata and posters; directors as a separate table (many-to-many).
- Delete with confirmation; export (CSV).
- Re-watch logging ("Vertoningen" per film), if ever wanted. This needs a `viewings` table split from `movies`.
- Owned-media inventory (a successor to `tblFilmInBezit`), if ever needed.

---

## 13. Open questions and decisions from review

The owner answered the questions raised in the review of AGENTS.md and PLAN.md:

| # | Question | Answer |
|---|---|---|
| Q-k | Public hostname? | `https://mdb.dalion.eu`, HTTPS only (HSTS). Used for `Health__PublicUrl`, the NPM proxy host and `Secure` cookies. |
| Q-l | Is the NPM container IP still `172.18.0.3`? | Yes. Default `ForwardedHeaders__KnownProxies__0` to it. |
| Q-m | Share the existing production MariaDB and its resource limit? | Yes. |
| Q-n | Production DB host and user? | Host `mariadb`, with its own `moviedb` user (privileges on `moviedb` only). |

All questions are answered; nothing is assumed any more.
Review findings already resolved in the text above (no action needed, listed for traceability):

- AccessImport targets `net10.0`, not `net10.0-windows` (§4.2), so that the unit tests can reference it.
- `-DryRun` semantics are defined once (§6.2).
- Duplicate detection must agree with the DB collation; a parity test and the unique key are the safeguards (§6.3).
- The default-sort index claim was wrong for a mixed `DESC, ASC` order and is corrected (§5.1).
- `UserClock` uses only the configured time zone, so validators may use it (§7.2); culture and `MudBlazor.Translations`
  setup is specified (§7.2).
- Browser tests need a real HTTP listener (§9); ingress port 8080 and NPM WebSocket support are specified (§11.2).

Ask the owner before deciding anything new that the code or this plan does not establish. Earlier decisions: Q-b (keep
the oldest row), Q-c (keep Skyfall 747) and Q-j (local MariaDB upgraded to 12.3.3) are recorded in §3, §2.3, §6.3 and
§11.1.
