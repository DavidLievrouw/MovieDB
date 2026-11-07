# MovieDB – Milestone Handover

Status note for the next agent. Read [`AGENTS.md`](AGENTS.md) (conventions), [`PLAN.md`](PLAN.md) (phase 1 product
spec, milestones M0-M6 in §10) and [`PLAN-2.md`](PLAN-2.md) (phase 2: multi-genre, multi-director, no Extra's,
milestones M7-M10) first. Update this file at the end of every milestone.

## Working agreement with the owner

- Do **one milestone at a time**, then stop and wait for the owner's review. Do not start the next milestone
  unprompted.
- Do not commit unless asked. M0-M10 are committed, and so is PLAN-2.md. PLAN-2.md §2a holds review clarifications
  confirmed by the owner (binding); there are no open questions: read it first.
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
| M2 Auth | Done (committed) |
| M3 Import | Done (committed) |
| M4 UI shell + overview | Done (committed) |
| M5 Edit/new | Done (committed) |
| M6 Browser tests, Docker, compose, backup, docs | Done (committed) |
| M7 Drop Extra's, mandatory scores | Done (committed) |
| M8 Multi-genre + genre filter | Done (committed) |
| M9 Multi-director | Done (committed) |
| M10 Phase 2 wrap-up | Done (committed) |
| M11 TMDB provider client (no UI), [`PLAN-3.md`](PLAN-3.md) | Done (committed) |
| M12 Lookup dialog, attribution, page integration | Done, awaiting owner review (uncommitted) |
| M13 Docs, AGENTS.md convention, full regression | Not started |

Phase 2 is complete; phase 3 (optional online film search, PLAN-3.md) is in progress, one milestone at a time. The
owner-driven first deployment (README) is independent of it.

### What M12 delivered (uncommitted, awaiting review)

- `MovieLookupDialog.razor` (public component): own 400 ms debounced search (at least 2 characters, at most 10 hits),
  hit rows with a TMDB link and a "Kies" button, status area, Dutch messages from `LookupMessages`, retry for a failed
  details call (not for "not found"), stale searches ignored.
- `TmdbAttribution.razor` + the unmodified TMDB "Alt short (blue)" logo in `MovieDB.Movies/wwwroot/images/` (with
  `ATTRIBUTION.txt`). Shown in the dialog and on `/films/nieuw` only; served locally, so the CSP is unchanged.
- `MovieEditPage`: on `/films/nieuw` an outlined "Zoek film online" button (disabled with a tooltip when no token is
  configured), overwrite confirmation when the form holds more than a title, `LookupFill.Apply`, stored director
  spellings (no "nieuw" marker for known directors), duplicate check, dismissible success alert with the notices, a source
  row (TMDB and IMDb links) and focus on the first empty required field. Nothing is saved until the owner saves.
- Tests: bUnit (dialog, attribution), browser (`MovieLookupTests` at 390 and 1440 px with a `FakeMovieLookup` that replaces
  `IMovieLookup`, so the real token is never used), characteristics (logo asset and CSP, no attribution on `/login`,
  anonymous redirect). Full run: 737 tests pass; build `-warnaserror`, XML-doc build and `csharpier check` are clean
  (except the pre-existing `Directory.Packages.props` newline).
- **Owner to verify:** try the dialog against the real TMDB (needs the user-secrets token).

### What M11 delivered (committed)

- `MovieDB.Movies/Lookup/`: `IMovieLookup`, `TmdbMovieLookup` (typed `HttpClient`, bearer token per request, no
  redirects), `LookupInputParser`, `LookupFill`, `TmdbGenreMap`, `TmdbOptions` + validator, `LookupMessages`, models.
- `MovieQueries.FindKnownDirectorsAsync` (one no-tracking query, collation matching, returns stored spellings).
- `ErrorKind`: `NotConfigured`, `Unavailable`, `RateLimited`, `InvalidResponse`, plus `Misconfigured` (deviation from
  PLAN-3 §8: 401/403, same Dutch text as `Unavailable`, logged at Error).
- `MovieInput.MinRuntimeMinutes`/`MaxRuntimeMinutes` shared with the lookup fill.
- Config: `Tmdb` section in appsettings, `Tmdb__ApiReadAccessToken` (compose: `MOVIEDB_TMDB_TOKEN`), README section,
  Serilog override for `System.Net.Http.HttpClient`. The CSP is unchanged (all calls are server side).
- Tests: unit (parser, fill, genre map, messages, client with a stub handler, options validator) and characteristics
  (`FindKnownDirectorsAsync` on MariaDB; starts without a token; invalid `Tmdb:BaseUrl` stops startup). No live calls
  in automated tests.
- Live check (throwaway, token never printed, nothing kept): search and details work with the local token.
  `original_title` is always present; `title` is English (Amélie vs. Le Fabuleux Destin d'Amélie Poulain). Search tolerates
  prefixes ("godfath" finds The Godfather) but **not typos** ("matirx" returns 0 hits). A search page has up to 20
  hits (the client keeps 10). Unknown id: `404` with a JSON status body. A bad token: `401`. Not exercised live: `429`.
  Directors for The Matrix: two (Lana and Lilly Wachowski), so the multi-director fill is real.
- `dotnet csharpier check .` reports only `src/Directory.Packages.props` (trailing newline), which predates this work and
  is untouched.

## Phase 2: M7-M10 done

The owner has reached feature parity with Access and asked for improvements. [`PLAN-2.md`](PLAN-2.md) holds the
decisions, data model, UI and import changes and the milestones. All questions are answered. Summary:

- **Genres:** closed list of 35 (the 21 existing + 14 new, PLAN-2 §4.3), 1-4 per film (`movie_genres`), multi-select with
  chips. The overview gets a genre filter where a film must have **all** selected genres (URL `genre=3,7`).
- **Directors:** 1-5 per film (`directors` + `movie_directors` with position), free entry of new names, entered order
  kept for display. Uniqueness is Title + Year + the **order-insensitive** set of directors, enforced by
  `movies.directors_key` (sorted director ids) with a unique index. The 4 current "A, B" values are split on import.
- **Extra's** is dropped everywhere (done in M7; the 16 existing values are lost, confirmed). The five other scores are required
  in the UI and validator; DB columns stay nullable (9 existing films miss a score and must be completed on edit).
- **Migrations:** no production exists, so no data-preserving EF migration. The `Initial` migration is edited in place
  (same id; Designers and snapshot too), the local `moviedb` is **dropped and recreated** (dev only), then migrated
  and re-seeded with `Import-Access.ps1 -Reset`. The 35 genres are **seeded by the migration** (PLAN-2 §4.3); the import
  only maps onto them, and the dump guard requires the five data tables to be empty and `genres` to equal the seed.
  M7, M8 and M9 each rebuild and re-import (accepted). **M7-M10 run in succession with no production deployment in
  between: rewrite the end-state schema each time, no upgrade paths, and re-run the Access import after every schema
  change (PLAN-2 decision 10c). Do not ask the owner about old databases or data loss.**
- **Reference component:** Motorsports `RoundWinnersFields.razor` and `src/Motorsports.Ui/Components/Shared/FuzzyMultiSelect.razor`
  (read-only repo); MovieDB needs a Ui component with an `AllowNew` flag (genres closed, directors free).
- The first deployment (README) should wait for the new structure: the dump for production must come from the phase 2
  import. PLAN.md stays as the phase 1 record; PLAN-2.md wins where they differ.

Local data facts used for the plan (counts only): 1,203 movies; 21 genres used; 4 director values contain a comma;
9 movies miss at least one score; 16 movies have an Extra's value.

### What M10 delivered (validated by the owner, committed)

- **Docs only; no code or schema change.** README (links PLAN-2, states the final rules, `-Reset` and dump contents),
  AGENTS.md (the in-place schema rewrite exception is now past tense: `Initial` is the phase 2 end state, never edit it
  again once a database is deployed, so the first deployment switches to data-preserving migrations), PLAN.md (status
  pointer, §12 no longer lists the genre filter and the directors table), PLAN-2.md status and this file.
- **Regression:** build `-warnaserror` and XML-doc build (0 warnings), `csharpier check` (113 files) and the full test run
  pass: 561 tests (unit 251, architecture 17, characteristics 213, browser 80).
- **Final import and dump** (`Import-Access.ps1 -Reset`, local `moviedb`): 1,222 source rows, 19 duplicates dropped, **1,203
  movies, 715 directors, 1,207 director links, 1,203 genre links, 35 genres (verified, not inserted), 8 media**; plan
  figures equal database figures (also in the dry run); the 4 split FilmIDs are 996, 1077, 1082, 1088. The guarded dump
  `artifacts\import\moviedb-data.sql` was written and restored into the scratch database: fingerprints of all five
  tables match and a second load is refused. The dump is git-ignored and holds personal reviews: delete every copy after
  the production load.
- Not done: no manual look at 390/1440 px beyond the automated browser tests; the PLAN.md §8.6 item "every imported
  record shows the same values as in Access" is still the owner's to tick.

### What M9 delivered (committed)

- **Data model:** `movies.director` is replaced by `directors` (unique name, collation `utf8mb4_uca1400_ai_ci`),
  `movie_directors` (PK `movie_id` + `director_id`, `position`, unique `movie_id` + `position`, restrict FKs) and
  `movies.directors_key` (max 64; sorted numeric director ids joined with `,`, built by the public
  `DirectorsKey.Of`) with the unique index `ux_movies_title_year_directors`. `Initial`, both Designers and the snapshot
  were edited in place; `MigrationTests` (`HasPendingModelChanges`) pass. New Vogen id `DirectorId`.
- **Movies:** `MovieInput.Directors` (`List<string>`, 1-5 names, max 150 characters, no comma, no duplicates; Dutch
  messages "Geef minstens één regisseur op.", "Maximaal 5 regisseurs.", "Regisseur mag maximaal 150 tekens bevatten.",
  "Een regisseur mag geen komma bevatten.", "Een regisseur komt maar één keer voor."). The duplicate message is "Deze film
  bestaat al (Jaar en Regisseurs zijn gelijk).". `MovieCommands` finds or creates directors by collation, renames a
  director in place when no other movie links it, replaces the links (entered order kept as `position`), bumps `version`
  for a list-only change, and deletes orphan directors afterwards (an FK race with a concurrent link is swallowed on
  purpose; covered by a characteristics test). The same director set in another order is a duplicate. The same title with
  a different set is a different film (the same-title dialog shows the directors). `SuggestDirectorsAsync` is
  alphabetical. Statistics count a film once per director. The overview shows all directors (mobile cards and desktop
  column) and sorts by the `GROUP_CONCAT` of the names; record navigation uses the same SQL.
- **UI:** the Regisseur field is a `ChipMultiSelect` with `AllowNew`, `data-testid="director-field"`, and the new `Class`
  parameter (`films-field-wide`). A comma, Enter, Tab or leaving the field commits typed text; pasted comma lists are
  split and de-duplicated; candidates longer than `MaxLength` are skipped; adding stops at 5.
- **Import tool:** `MovieCleaner` splits the director text on commas (parts equal by collation collapse, source order
  kept, 1-5 names), `ImportPlan`/`ImportLoader` write `directors`, `movie_directors` and `directors_key`, `-Reset` also
  empties `movie_directors` and `directors`, dump order is `media`, `directors`, `movies`, `movie_genres`,
  `movie_directors`, the guard needs those tables empty, and report and fingerprints cover the new tables.
- **Tests:** unit 251, architecture 17, characteristics 213, browser 80 (new: reversed-order duplicate, two genres + two
  directors saved in order, commit by comma/Tab/leaving the field, `DirectorsKey`, concurrent director creation and the
  orphan-delete race). All pass.
- **Local DB rebuilt** (dropped/recreated as root in the container, app start migrated and seeded `admin`, then
  `Import-Access.ps1` without `-Reset`): 1,222 source rows, **19 duplicates dropped, 1,203 movies, 715 directors, 1,207
  director links (4 split FilmIDs: 996, 1077, 1082, 1088)**, 35 genres, 1,203 genre links, 8 media; plan figures equal
  database figures; the dump was written and verified in the scratch database (same as the plan, no difference).
- Checks: build `-warnaserror`, XML-doc build (0 warnings), `csharpier check` and all four test projects pass.
- Not done: no manual look at 390/1440 px beyond the automated browser tests.

### What M8 delivered (committed)

- **Closed genre list:** `MovieGenres` (public static in Infrastructure: `All` = 35 `(Id, Name)` tuples, ids 1-35 stable
  because URLs carry them, `MaxNameLength`, `MaxPerMovie` = 4). The hand-written `Initial` migration (edited in place,
  both Designers and the snapshot) seeds them with `INSERT IGNORE` and creates `movie_genres` (composite PK, index on
  `genre_id`, both FKs RESTRICT); `movies.genre_id` is gone. `Down` drops the join table. A unit test pins the list
  (35 unique names, length) and a characteristics test the seeded table = `MovieGenres.All`.
- **Movies:** `MovieInput.Genres` (`List<string>`, validator: 1-4, no duplicates; "Kies minstens één genre.",
  "Kies maximaal 4 genres.", "Een genre komt maar één keer voor."). `MovieCommands` resolves names against the closed list
  by collation (unknown -> `Error.Validation("Genres", "Onbekend genre: X.")`), replaces the links on update and bumps
  `version` also for a genre-only change. `SuggestGenresAsync` is replaced by `ListGenresAsync` (id + name, alphabetical).
  `OverviewState.Genres` with URL `genre=3,7` (distinct positive ints, ascending, at most 35); the page drops unknown ids
  and rewrites the URL. `MovieQueries` pages, filters and sorts with raw SQL (`OrderSql` is the single place for the
  order): filter = `COUNT(DISTINCT genre_id) = k` (a film must have ALL selected genres), genre sort = correlated
  `GROUP_CONCAT(name ORDER BY name)`. Record navigation uses the same SQL; when the current film no longer matches the
  filter, search and filter are dropped and the sort is kept. Statistics "Top genres" count a film once per genre.
- **UI:** new Ui component `ChipMultiSelect` (chips + `MudAutocomplete`, `AllowNew`, `IsKnown`, `MaxSelected`;
  `AllowNew`/`IsKnown` are for M9 directors). Edit page: "Genre" field with hint "Maximaal 4", the input is disabled at 4
  chips, chips show in the order added and alphabetically after load/save. Overview: a genre filter next to the search
  box (own full-width row on mobile) with "Genrefilter wissen", genre chips in the mobile cards, all genres joined in the
  desktop column.
- **Import tool:** `ImportPlan` maps each film's Access genre onto the seeded list (an unknown genre is an import error;
  the FilmID 308 correction still applies) and writes one `movie_genres` row per film. `ImportLoader` verifies the
  seeded genres (refuses a database whose `genres` differ) and never writes `genres`; `-Reset` empties `movie_genres`,
  `movies` and `media` only. Dump order `media`, `movies`, `movie_genres`; the guard needs those tables empty and `genres`
  equal to the seed. Report and fingerprints cover `movie_genres` (genres are fingerprinted by id + name only: `created_at`
  is set at migration time).
- **Tests:** unit 226, architecture 17 (`PublicSurfaceTests` knows `MovieGenre`/`MovieGenres`), characteristics 193,
  browser 65 (new: genre filter with ALL semantics and the `genre=` URL, change genres + the max-4 limit, no genre rejected, genre error only without genres, search/genre filter alignment).
- **Local DB rebuilt** (dropped/recreated as root in the container, app start migrated it, then `Import-Access.ps1`
  without `-Reset` because the database was empty): 1,222 source rows, 19 duplicates dropped, **1,203 movies, 35 genres,
  1,203 genre links**, 8 media, plan figures equal database figures, dump written and verified in the scratch database.
  `users` was recreated empty: `admin` is seeded on the next app start.
- Checks: build `-warnaserror`, XML-doc build (0 warnings), `csharpier check` and the full test run pass.
- **Follow-ups after owner review (done):** the genre filter is aligned with the title search (dense autocomplete, toolbar lign-items: flex-start, 4px top margin on the filter input; checked by a browser test). The Genre field no longer turns red when genres are chosen (see learnings); ChipMultiSelect also no longer selects the highlighted suggestion on Tab.
- **First-pick and placeholder fix (done):** the first genre pick on "Nieuwe film" was lost (layout shift from the blur error banner, see learnings); the banner now shows only after Save and the chip blur is deferred. Genre controls have placeholders ("Kies een genre", "Alle genres"). Tests: slow-click browser theory (390/1440), bUnit placeholder + deferred blur. Counts now: unit 231, browser 74. The edit-form Genre box had no top margin while MudBlazor inputs have 8px, so it sat 8px higher than Duur: `.films-fields > .chip-multi-select` now has `margin-top: 8px` (browser test compares the Genre box and the Duur field). Placeholder and chips sat ~8px above the middle of the box (and the dense input text 2px above the middle of its own box): a fieldset does not stretch its content to its `min-height`, and MudBlazor's dense input has 3px/7px vertical padding. CSS-only fix in `ui.css` (no DOM change): the autocomplete root is the flex item that carries the row height (46px, 34px dense), chips are `align-items: center` against it, and the input padding is 5px/5px. Browser tests measure the content-box centre of placeholder/chips against the box on the edit page (390/1440) and the overview filter. The placeholder uses the browser default gray (a darker gray seen in one browser was the DarkReader extension, not the app). Label: the edit-form Genre box shows its label inside the empty box like the other fields (`.chip-multi-select-label`, aligned with "Filmtitel*", floating (200ms animation) into the outline notch on focus or with chips, the border opens a gap behind it; the placeholder "Kies een genre*" appears on focus); the overview filter passes `HideLabel` and shows only "Alle genres".
- **In-box chips (M8 extension, done):** `ChipMultiSelect` now draws the chips inside the outlined input box (a box `fieldset` plus an aria-hidden outline `fieldset` with a `legend` for the notched label; styles in `Ui/wwwroot/ui.css`). Chips are removed only via the chip's X, never with Backspace. The edit page and the overview genre filter use it; there are no other multi-selects (the rest are single selects). PLAN-2 §6.1/§6.2 require the same control for Regisseur(s) in M9. Tests: bUnit (chips inside the box, Backspace keeps chips, legend) and Playwright (chips inside the box at 390/1440, Backspace keeps chips, X removes, alignment with the search box).
- Not done: no manual look at 390/1440 px beyond the automated browser tests.

### M7 in one line

Extra's dropped everywhere (entity, `Initial`, Designers, snapshot, import) and the five scores made mandatory in the UI
and the validator (DB columns stay nullable; legacy films with a missing score must be completed on edit).
### What M6 delivered

- **Browser tests** (`src/tests/MovieDB.BrowserTests`, 50 tests = 25 theories x 390x844 and 1440x900, Docker required):
  `BrowserFixture` (Testcontainers MariaDB 12.3.3, `WebApplicationFactory<Program>` on a real Kestrel listener via
  `UseKestrel(0)` + `StartServer()`, Playwright Chromium, a logged-in `storageState`, synthetic seeding through the
  app's own commands) and `BrowserSession` helpers. Classes: LoginTests (redirect, login, wrong password, logout),
  OverviewTests (debounced search, empty state, open + back link, new-film button), MovieEditTests (save, cancel,
  conflict, not found, record
  navigation, unsaved-changes dialog, discard, Ctrl+S), NewMovieTests (validation, ranges, create, duplicate with link,
  same-title dialog, "Nieuwe film" from the edit page gives a pristine form), ChangePasswordTests. They never touch the developer database (env vars override user-secrets,
  cleaned up on dispose). The fixture raises the login throttle limits and `LoginAsync` retries (at most 5 attempts) while the 1 s cooldown after a failed
  login ("tijdelijk beperkt") is active.
- **Deployment:** `src/MovieDB.Web/Dockerfile` (build context = repo root; sdk:10.0-noble -> aspnet:10.0-noble-chiseled-extra,
  port 8080, exec-form HEALTHCHECK), `docker-compose/moviedb-web/docker-compose.yml` (container `moviedb`, external
  network `motorsports_net`, no host port), `backup.ps1` (see learnings), README deployment, restore and
  browser-test sections.
- **Verified:** image built (`moviedb-web:1.0.0`); run on `mariadb_default` against the local `moviedb` database with a
  throwaway PFX it became `healthy` (migrations ran, `/health` 200); container and PFX removed. `backup.ps1` run against the
  local database: key table has structure only, movies data present; temp files removed.
- **Not verified here:** the compose stack itself (network `motorsports_net` does not exist locally) and the owner
  check of PLAN.md §8.6 ("every imported record shows the same values as in Access"), which is still open.

### What M5 delivered

- **Ui:** ScoreInput (toggle group "–", 1–10 on desktop, a select below 600px), SuggestField (debounced
  MudAutocomplete that accepts any text), UnsavedChangesDialog + UnsavedChangesChoice (Stay/Save/Discard).
- **Movies:** MovieInput + MovieInputValidator (Dutch messages; year 1888..current+5, date 1900..tomorrow, from
  UserClock), MovieCommands.SaveAsync (validate, duplicate check, resolve-or-insert genre/medium, version check,
  all in one ReadCommitted transaction; returns Result<SavedMovie>: Validation per field, NotFound, Conflict),
  MovieQueries additions (FindForEditAsync, FindDuplicateAsync, SuggestGenresAsync/SuggestMediaAsync,
  GetNavigationAsync using raw-SQL window functions that mirror the overview ORDER BY), OverviewState.WithoutSearch.
  Pages/MovieEditPage.razor serves /films/nieuw and /films/{id} (record navigation Eerste/Vorige/Volgende/Laatste
  with "n van N" that keeps the overview's search/sort, Gegevens + Vertoning panels, sticky Evaluatie panel, sticky save
  bar, inline duplicate hint with a link, validation summary and focus on the first invalid field, Ctrl/Cmd+S,
  unsaved-changes guard on in-app navigation, conflict alert with Herladen / Toch opslaan, not-found card).
  The M4 placeholders MovieDetailPage/NewMoviePage are gone.
- **Infrastructure:** DbOperations/DbFailure are now public; PublicSurfaceTests updated.
- **Follow-up:** Regisseur is a `SuggestField` too (`MovieQueries.SuggestDirectorsAsync`: distinct by collation, most
  used first, contains match); new names can still be typed freely.
  Saving snaps the director to the spelling other movies already use (case/accent per collation, most-used spelling;
  `MovieCommands.ResolveDirectorAsync`); a director only used by the movie itself keeps the typed spelling.
- **Host:** snackbars at top-center; pp.js Ctrl+S helper.
- **Follow-up:** saving a new film (or one whose title/year changed) whose Title + Year already exists under another
  director shows `SameTitleDialog` ("Bestaat deze film al?"): links to the possible conflicts (new tab) and three
  choices: "Terug naar formulier", "Invoer annuleren" (discard, back to the overview), "Toch opslaan"
  (`MovieQueries.FindSameTitleAndYearAsync`; a failed lookup never blocks saving). Exact duplicates keep the existing
  inline/server error.
- **Tests (381 total):** unit MovieInputTests, bUnit ScoreInputTests; characteristics MovieCommandTests
  (create/update, versions, conflict + retry, not found, duplicates by case/accent, races, lookup reuse, Unicode,
  suggestions with %/_ escaping) and MovieNavigationQueryTests (boundaries, every sort column both directions,
  multi-sort, search, search fallback, consistency with ListOverviewAsync).
- **Browser check (throwaway Playwright against the local DB, 1440px and 390px):** validation, create + redirect,
  Ctrl+S, dirty-navigation dialog, duplicate hint, concurrency alert + "Toch opslaan", not found, no console errors,
  no horizontal overflow. Synthetic rows removed afterwards (1,203 movies). Not yet in the automated browser suite (M6).
- Checks: build -warnaserror, XML-doc build (0 warnings), csharpier check, full test run pass.
- **Open for the owner:** the §8.6 item "every imported record shows the same values as in Access" is not ticked; the
  form shows the database values, but the comparison against the .mdb was done only by M3's counts and rules.

Earlier: M4 delivered the shell + overview (summary below).
### What M4 delivered (summary)

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
  owner created them; the agent does not need the root password for normal work (a database drop/recreate is done as root
  inside the container, see "Learnings from M7"). Check that the key exists without printing its value:
  `dotnet user-secrets list --id 9b46a74e-075e-4f32-b29a-dc9bd38c1cec | ForEach-Object { ($_ -split '=')[0] }`.
  The owner granted the user rights on `moviedb.*` and `moviedb_dumpcheck.*`.
- Production database and user also exist (owner-managed). Nothing for the agent to do there.
- `scripts/Import-Access.ps1` exists (M3). The local `moviedb` already contains the imported 1,203 movies (35 genres, 1,203 genre links, 715 directors, 1,207 director links); re-run with
  `-Reset` to redo it. The dump is at `artifacts\import\moviedb-data.sql` (git-ignored, personal reviews).
- Docker (Rancher Desktop) is running with `mariadb:12.3.3` locally. Testcontainers pulled from the same image.
- PowerShell 7 is installed at `C:\Users\David\AppData\Local\Microsoft\WindowsApps\pwsh.exe`. The default tool shell
  is Windows PowerShell 5.1.
- Git needs `git -c safe.directory=C:/_git/MovieDB …`.
- Playwright chromium is installed (via `playwright.ps1 install chromium` after a build).
- The reference repo `C:\_git\Motorsports` is read-only. Mirror its patterns.

## Next: first deployment (owner-driven)

There is no further planned milestone. After the owner has reviewed and committed M10, the first deployment follows
README "First deployment: load the data" with the verified dump from the phase 2 import (`artifacts\import\moviedb-data.sql`):
production gets the schema from the container's startup migration, then the dump. From the first deployment on, schema
changes are new data-preserving migrations (AGENTS.md). Anything new needs a new plan and the owner's decisions first.

## Learnings from M10

- `Import-Access.ps1 -Reset` runs non-interactively with `'RESET' | powershell -File .\scripts\Import-Access.ps1 ... -Reset`.
- Run build/test commands from the repo root: from `src` the `src/MovieDB.slnx` path does not exist.

## Learnings from M9

- **Enter right after typing can pick a stale suggestion.** `ChipMultiSelect` debounces the autocomplete by 150 ms. If
  the typed name matches an existing director and Enter is pressed before the debounce ran, the first item of the
  not-yet-filtered list is added instead of the typed text. `BrowserSession.TypeChipAsync` waits 450 ms before Enter. A
  fast human typist could hit this too; a component tweak (commit the typed text on Enter unless a list item was
  explicitly highlighted) may be worth considering.
- bUnit cannot drive Enter, Tab or blur-commit of `MudAutocomplete` (it needs real JS/DOM behaviour): the browser tests
  cover those.
- A movie holds at most 5 directors, so test seeds must respect it (a seed of 8 failed the validator-level limit).
  Fixtures that clear tables must clear `movie_directors` before `directors` and `movies`, and must not clear `genres`.
  Movies written straight through EF need `DirectorLinks` and `DirectorsKey` (`DirectorTestData.AssignAsync`).
- Roslynator RCS1246: use `[0]` instead of `First()` on lists.
- A cancelled `dotnet test ... | Select-Object -First N` leaves the test host running and locks `MovieDB.BrowserTests.exe`
  (build error MSB3026). Write test output to a file instead of piping into `Select -First`.

## Learnings from M8

- **A click on a suggestion can be lost to a layout shift.** Mouse-down on a MudAutocomplete item blurs the input; the blur validation error used to show the summary banner, pushing the page (and the list) down 62px, so the mouse-up hit another element. Playwright's instant `click()` hides this: reproduce with move, `Mouse.DownAsync`, delay, `Mouse.UpAsync`. Fixed by showing the banner only after Save and by deferring `ChipMultiSelect.OnBlur` by 250 ms (cancelled when a value is added).
- **Never pass `Required` to the inner `MudAutocomplete` of a chip control.** Its text box is always empty while chips exist, so MudBlazor flags "Required" on blur although values are chosen (reproduced only in a real browser: click the input, then click another field, no Escape). `ChipMultiSelect` marks the label with "*" and shows an error only via the parent (Error/`ErrorText`, i.e. no values). PLAN-2 §6.1 requires the same for the Regisseur(s) field in M9, with matching tests.
- **In-box chip layout:** the box is a `fieldset` without border; the border and notch are drawn by an absolutely positioned aria-hidden outline fieldset (`top:-5px` with an 11px hidden legend, as MudBlazor does). A rendered legend inside the content fieldset shifts the border in Chrome, and a 0-height legend leaves no gap, so do not put the legend there. The inner `MudAutocomplete` keeps `.mud-input-control-margin-dense` margins (4px top/bottom): override with a higher-specificity `margin: 0` inside the box, otherwise the box is 8px taller than the search field. The overview wrapper adds the 4px top margin that the dense search field has. Chip hosts stop click propagation (the fieldset click focuses the input), so closing a chip does not reopen the list.
- `ChipMultiSelect` refocuses its input after each add, and `OpenOnFocus` reopens the suggestion list over the rest of the
  form. In browser tests dismiss it (Escape while the input still has focus) before clicking anything else
  (`BrowserSession.AddChipAsync`/`FillAsync` do); after a blur the list cannot be closed with Escape any more.
- `ParameterState` ignores an unchanged `Value`, so the component regenerates the inner autocomplete with a new `@key` to
  clear the typed text. Set `Immediate="false"` on the inner `MudAutocomplete`, or bUnit sees the text before the debounce.
- A test helper `State(params SortKey[])` next to `State(string search = "", ..., params int[] genres)` binds a no-arg
  call to the params overload and produced an empty sort list: require a first `SortKey`.
- Migrated test databases already hold the 35 seeded genres; tests look them up by name and never insert them (the unique
  name fails with 1062). Fixtures that clear tables must not clear `genres`. Never fingerprint `genres` with `SELECT *`.
- `SuggestMediaAsync` is alphabetical, not "most used first" (decision 3b).
- `Import-Access.ps1 -Reset` needs an interactive prompt (`Read-Host`), which tool shells do not have: either run it without
  `-Reset` on an empty database or pipe `RESET` from a real PowerShell.
- EF expression trees cannot contain collection expressions or `Order()` chains in assertions: materialise first.
## Learnings from M7

- Editing `Initial` in place is safe as long as the snapshot and both Designers agree (`HasPendingModelChanges` test).
  The entity said `int?` while the snapshot said `sbyte?`; removing a score from all three kept them aligned.
- `@bind-Value:after` on the `ScoreInput` `Value` parameter works for per-field validation.
- Seeding through `MovieCommands` now needs five scores. To get a legacy row with a NULL score, save normally and
  `ExecuteUpdate` the column to NULL (see `BrowserFixture.ClearStoryScoreAsync`).
- The `moviedb` DB user cannot `DROP DATABASE`; do it as root inside the container (no password is typed or stored).
## Learnings from M6

- `WebApplicationFactory` in .NET 10 can host Kestrel (`UseKestrel(0)`, `StartServer()`), so Playwright can drive it.
  The default factory environment is Development, so no DataProtection PFX is needed in browser tests.
- Login throttling is real in the browser tests: a failed login blocks the source for a short cooldown, and the
  following login in another test is refused (page stays on /login with "tijdelijk beperkt"), not delayed.
- `backup.ps1` reads the password from `MARIADB_ROOT_PASSWORD` or prompts (the Motorsports script hardcodes one; not
  copied). It dumps inside the container with `--result-file` and `docker cp`, because PowerShell 5.1 redirection
  writes UTF-16. Output goes to the git-ignored `backup/moviedb-backup-YYYYMMDD-mariadb.sql`.
- Navigating within `MovieEditPage` (existing film to `/films/nieuw`) reuses the component, so MudBlazor fields kept
  their required-validation state (red Genre/Regisseur). The field grid is keyed on the loaded record to prevent that.
- Roslynator in the build rejects needless verbatim strings (RCS1192) and the static `Regex.Match` (RCS1186).

## Learnings from M5

- **MudBlazor v9:** AutoGrow is gone (use Sizing="InputSizing.Auto"); ShowAsync<T>(null, options) is ambiguous
  (use (string?)null). Do not capture a or loop variable in a Razor child content (copy it first).
- **.films-card** belongs to the overview's mobile cards; the edit page uses ilms-panel.
- **Save of a new film:** after the redirect the page reloads by id (the baseline is set first, so it is not dirty);
  keeping _id null would make a second save insert a duplicate.
- **Navigation SQL** is raw (SqlQueryRaw<NavigationRow>) with fixed ORDER BY fragments and parameters for values;
  a test checks it against the overview order for every sort column.
- **MudBlazor + bUnit:** JSInterop.Mode = Loose, AddMudServices() and a rendered MudPopoverProvider.
- **Evaluatie header** uses the PLAN maroon gradient (#7A1F2B -> #B0263F); the label "Film" replaces M4's "Verhaal".
- The create tool wrote CRLF again; normalise new files to LF.
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
