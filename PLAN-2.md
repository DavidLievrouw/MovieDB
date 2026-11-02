# MovieDB – Plan 2: multi-genre, multi-director, no Extra's

Phase 1 ([`PLAN.md`](PLAN.md)) reached feature parity with the Access database. This plan covers the first round of
improvements requested by the owner. Read it together with [`AGENTS.md`](AGENTS.md) and [`HANDOVER.md`](HANDOVER.md).

**Status:** M7 (drop Extra's, mandatory scores), M8 (multi-genre + genre filter) and M9 (multi-director) are done, validated by the owner and committed; M10 (wrap-up: docs, full regression, final dump) is done and awaits the owner's review (uncommitted). Phase 2 is complete; the first deployment is owner-driven (README).
The owner's answers are in §2. A review of this plan against the code added the clarifications in §2a, which the
owner has confirmed. There are no open questions (§10).

**Precedence:** where this plan and PLAN.md disagree, this plan wins for the topics below (PLAN.md §2.3, §3, §5, §6,
§8.3, §8.4, §9 and §12 are affected). PLAN.md is kept as the record of phase 1 and is not rewritten. AGENTS.md
(domain rules, project-specific decisions) is updated in the milestone that changes the rule.

**Language rule** is unchanged: Dutch UI, English everything else.

---

## 1. Scope

1. **Genres:** a film can have several genres, chosen from a **closed** list that is extended with well-known genres.
   The overview can be filtered by genre.
2. **Directors:** a film can have several directors, in the entered order, and **new director names can be typed
   freely**. The duplicate rule is adapted to a set of directors.
3. **Extra's is dropped** completely (database, import, UI, tests, docs). The five remaining scores become mandatory
   when adding or updating a film.
4. The **Access import tool** and the production dump produce the new structure.

Out of scope: user-facing management of genres or directors (rename/merge/delete), searching on genre or director text,
EF data-migrating migrations (see §2 decision 10), any production deployment.

---

## 2. Decisions (answers from the owner)

The clarifications in §2a were reviewed and confirmed by the owner.

| # | Topic | Decision |
|---|---|---|
| 1 | Genre input | **Closed list**, multi-select, no free typing. |
| 2 | Genre list | The 21 existing genres stay unchanged, plus 14 new ones (§4.3). Final list: 35 (including "Andere"). "Gangster" is not added; "Muziek" is the same as "Musical" and is not added separately. |
| 3 | Genre limits and order | 1 to 4 genres per film, unordered, displayed alphabetically (collation order). Sorting on the Genre column uses the displayed text. |
| 3a | Genre filter | The overview gets a genre filter (multi-select). A film must have **ALL** the selected genres. One selected genre shows the films that have that genre among their genres. It combines (AND) with the title search. Search itself stays title-only. |
| 3b | Suggestion order | Lists and suggestions in the edit forms are sorted **alphabetically by display name** (collation order), not by usage. M7 already did this for media (`SuggestMediaAsync`). M8 does it for genres (`ListGenresAsync`), M9 for directors (`SuggestDirectorsAsync`). |
| 4 | Existing multi-director values | The 4 current "A, B" director values are **split into separate directors**. |
| 5 | Duplicate rule | Unique on **Title + Year + the set of directors**. The key is **order-insensitive**: the same directors in another order is the same film and violates the rule. |
| 6 | Director order | The entered order is kept and used for display. Duplicate detection ignores the order. 1 to 5 directors per film. |
| 7 | New directors | Typed names are added as new directors on save (matched to existing ones by collation first, as genres and media are today). |
| 8 | Extra's | Dropped completely. The 16 existing Extra's values are lost; the owner confirmed. No backup needed (the local database is disposable). |
| 9 | Scores | The five remaining scores are **required in the UI and validator** (1-10, no empty choice). The **database columns stay nullable**: 9 existing films miss at least one score and must be completed the next time they are edited. |
| 10 | Migrations | **No production database exists.** No data-preserving EF migration is written. The `Initial` migration is edited in place (**same migration id**) to the new structure, the local development database is **dropped and recreated** (dev only), and the data comes back through the Access import (`Import-Access.ps1`). Details and the AGENTS.md exception: §7. |
| 10a | Genre seeding | The 35 genres are **seeded by the migration** (reference data), not by the import. A fresh database and the tests have them without an import. |
| 10b | Rebuild cycles | M7, M8 and M9 each rebuild the local database and re-import (accepted). |
| 10c | No production between milestones | M7-M10 are executed in succession and **nothing is deployed in between**. Every schema change therefore rewrites the desired end state directly: the `Initial` migration (hand-written, self-guarding), both Designers, the snapshot, `Initial.Down`, the seed data and the import/dump tooling. **Never** write an upgrade path, a data-preserving or data-migrating step, a backward-compatibility shim or a "what if an old database exists" check, and **do not ask the owner about it**. The development database is throwaway (the owner has a backup and the original `FilmsBase.mdb`). **After every schema change, drop and recreate the local `moviedb` database and re-run the Access import** (`Import-Access.ps1 -Reset`, §7), so the data always matches the current schema and the import tool is proven against it. Data lost in the local database is not a concern. |
| 11 | Delivery | New milestones M7-M10, one at a time with the owner's review in between (HANDOVER working agreement). |

### 2a. Clarifications from the plan review (agent decisions; the owner may override)

These close gaps or contradictions found while checking this plan against the code. They are binding for the next
agent unless the owner says otherwise.

1. **Validator versus database.** `MovieInputValidator` stays pure (it only knows `UserClock`). It checks counts,
   lengths, the comma rule, required scores and duplicates within the list as far as .NET can (ordinal after trim,
   case-insensitive). The **authoritative** checks happen in `MovieCommands` after resolving names to ids inside the
   transaction: an unknown genre (name not in `genres`, matched by collation) and two director names that resolve to
   the same director id are returned as `Error.Validation` on the same property with the same Dutch messages. A genre
   typed in another case ("actie") resolves to the stored name.
2. **`MovieInput` shape.** `Genre` and `Director` (strings) become `Genres` and `Directors` (`List<string>`).
   `Clone()` currently uses `MemberwiseClone`, which would **share the lists**; it must copy them (dirty tracking and
   Cancel depend on it). `Normalized()` trims every item and drops empty items. `SameAs` compares directors as an
   **ordered** sequence (order is stored, so a reorder is a change) and genres as an **unordered set**.
3. **Replacing links on update.** `movie_genres` and `movie_directors` are replaced, not diffed blindly: remove the old
   links and `SaveChanges`, then add the new ones. EF may otherwise insert before it deletes and hit the primary key or
   `unique (movie_id, position)`. Directors are resolved/inserted before the movie row changes, as genres and media are
   today.
4. **Orphan cleanup scope.** Only directors that were linked to the movie being saved and are no longer referenced by
   any movie are deleted (not a table-wide sweep). This keeps the cleanup away from rows another transaction has just
   created. A concurrent save that re-links such a director must not surface as a server error: a foreign-key failure
   while deleting an orphan is ignored (the director is evidently in use), and a characteristics test covers it.
5. **Typed spelling, rename in place.** "Typed spelling replaces the stored one" means updating `directors.name` of the
   existing row (same id, so no `directors_key` changes) when only the movie being saved uses that director.
6. **Duplicate hint on blur.** The inline check runs when Filmtitel or Jaar loses focus and whenever a director chip is
   added or removed, once all three are filled. `MovieQueries.FindDuplicateAsync` takes the director **names**,
   resolves them to ids by collation, and reports no duplicate when any name is unknown (a set with a new director
   cannot be a duplicate). `FindSameTitleAndYearAsync` ("Bestaat deze film al?") now excludes movies whose director
   **set** equals the entered one and lists the other directors of the match as joined text.
7. **Genre ids in the URL.** `OverviewState` parses `genre` only syntactically (positive integers, no duplicates,
   sorted ascending, at most 35). The overview page loads the genre list (id + name, 35 rows, no caching) and drops
   unknown ids, rewriting the URL. `MovieQueries.ListGenresAsync` (replaces `SuggestGenresAsync`) serves both the edit
   page and the filter. The genre filter and the edit-page genre field both use the Ui component with `AllowNew=false`
   (filter: no `MaxSelected`); the page maps names to ids.
8. **Mobile filter.** On xs/sm the genre filter is its own full-width row below the search box (no collapsing); on md+ it
   sits next to the search box.
9. **Chip order in the form.** Genre chips show in the order added while editing and alphabetically (collation order,
   from the database) after a load or save. The saved genre set is unordered.
10. **Import limits.** A film whose source yields more than 5 directors, or any director name longer than 150 characters,
    is an import error (the dry run must show none; the owner expects at most the 4 split films with 2 directors).
11. **Messages.** The existing "Score moet tussen 1 en 10 liggen." is replaced by the messages in §6.1, and
    `MovieMessages.Duplicate` becomes "Deze film bestaat al (Jaar en Regisseurs zijn gelijk)." Update the unit,
    characteristics and browser tests that assert the old texts.
12. **Stale handover.** HANDOVER.md said PLAN-2.md was uncommitted; it is committed ("Add plan for improvements.").
    HANDOVER.md is corrected in the same change as this section.
13. **"Nieuw" marker on director chips.** The Ui multi-select stays independent of Movies: it exposes
    `AllowNew` (free entry, Enter/comma/blur commit) and an optional `Func<string, bool>? IsKnown`; a chip is rendered
    as "nieuw" when `IsKnown` returns false. The edit page supplies `IsKnown` from the director names it loads for the
    suggestions (collation-insensitive compare is approximate here; the server stays authoritative, §2a.1). With
    `AllowNew=false` (genres) the component only accepts items from the list.

---

## 3. Domain changes at a glance

| Before (phase 1) | After |
|---|---|
| `movies.genre_id` (one genre, free text + suggestions) | `movie_genres` join table, closed list in `genres` |
| `movies.director` (one text) | `directors` + `movie_directors` (with position) |
| unique (`title`, `release_year`, `director`) | unique (`title`, `release_year`, `directors_key`) |
| six scores, `extras_score` optional, others optional | five scores, required in UI and validator, nullable in DB |
| overview: title search | title search + genre filter (ALL) |

---

## 4. Data model

### 4.1 Tables

**`movies`** changes:

- Drop `genre_id` (and its FK and index), `director` and `extras_score` (and its CHECK constraint).
- Add `directors_key` VARCHAR(64) NOT NULL: the ids of the movie's directors, **sorted numerically ascending**, joined
  with `,` (for example `12,57`). The application computes it on every save, in the same transaction that writes
  `movie_directors`. Five ids of at most 10 digits fit in 64 characters.
- Unique index `ux_movies_title_year_directors` on (`title`, `release_year`, `directors_key`) replaces
  `ux_movies_title_year_director`. Because director names are unique per collation, the same set of names in any
  order and case/accent variant resolves to the same ids and therefore to the same key.
- The five score columns keep `TINYINT NULL` and their 1-10 CHECK constraints.

**`genres`**: unchanged shape (`id`, `name` unique, `created_at`). The table now holds the closed list (§4.3).

**`movie_genres`** (new): `movie_id` INT, `genre_id` INT, PK (`movie_id`, `genre_id`), index on `genre_id`. FKs to
`movies` and `genres`, both RESTRICT.

**`directors`** (new): `id` INT AUTO_INCREMENT PK, `name` VARCHAR(150) NOT NULL **unique** (collation
`utf8mb4_uca1400_ai_ci`), `created_at`. A name is trimmed, 1-150 characters and contains no comma.

**`movie_directors`** (new): `movie_id` INT, `director_id` INT, `position` TINYINT NOT NULL (1-based entry order),
PK (`movie_id`, `director_id`), unique (`movie_id`, `position`), index on `director_id`. FKs RESTRICT.

**`media`**: unchanged (free text with suggestions).

Strongly typed IDs (Vogen): add `DirectorId`. Entities follow the existing pattern in
`MovieDB.Infrastructure/Entities`; mapping is explicit, `snake_case`, plural.

### 4.2 Rules

- **Uniqueness:** on save, resolve the typed director names to ids (existing by collation, otherwise new), compute the
  key and check for another movie with the same Title + Year + key. Error text on Filmtitel stays: "Deze film bestaat
  al (Jaar en Regisseurs zijn gelijk)." with the "Bekijk bestaande film" link. A unique-key violation from a race maps
  to the same error. A set that contains a brand-new director cannot be a duplicate.
- **Same title and year, different directors** keeps the existing `SameTitleDialog` ("Bestaat deze film al?").
- **Genres:** at least 1, at most 4, all from the closed list, no duplicates. An unknown genre is a validation error,
  never inserted.
- **Directors:** at least 1, at most 5, no duplicates within a film (compared by collation), each trimmed and 1-150
  characters.
- **Typed spelling:** a typed name that matches an existing director by collation uses the stored spelling. If that
  director is used only by the movie being saved, the typed spelling replaces the stored one (the M5 behavior, kept).
- **Orphans:** after a save, directors that no movie references any more are deleted in the same transaction. There is
  no director management UI, so this keeps the table and the suggestions clean. Genres are never deleted.
- **Concurrency:** the `version` check and increment cover changes to genres and directors too (saving changed
  genres/directors only still bumps `version` and `updated_at`).
- Everything happens in one ReadCommitted transaction, as in M5 (`MovieCommands.SaveAsync`).

### 4.3 Genre list (closed, 35)

Existing (unchanged): Actie, Andere, Animatie, Avontuur, Bond, Detective, Detective-thriller, Documentaire, Drama,
Horror, Kinder, Komedie, Melodrama, Misdaad, Oorlogs, Rampen, Romantisch, Sci-fi, Thriller, Western, Zwarte komedie.

New (14): Musical, Mystery, Film noir, Sport, Kortfilm, Biografie, Familie, Fantasy, Historisch, Superhelden,
Romantische komedie, Psychologische thriller, Roadmovie, Tragikomedie.

Sources: Dutch Wikipedia "Filmgenre" and IMDb's genre list. The list is a single constant in code, not a configuration
value: one `public static` class in Infrastructure (`MovieGenres`, with `All` as ordered `(int Id, string Name)`
pairs, ids 1-35 fixed and stable because they appear in the overview URL). It is used by the migration seed, the import
(name to id mapping), tests and Movies; add it to `PublicSurfaceTests` deliberately. **Seeding:** `Initial` inserts the
35 rows with these explicit ids, self-guarding (`INSERT IGNORE`, unique on `name`) like the rest of that migration. The
migration may not reference the constant if that would make a later list change rewrite history: copy the values into
the migration, and add a test that the seeded table equals `MovieGenres.All` exactly.

---

## 5. Access import and production dump

The tool is `src/tools/MovieDB.AccessImport`; the script is `scripts/Import-Access.ps1`. Rules below replace the
matching rows in PLAN.md §6.3 and §6.4.

- **Genres are seeded by the migration** (§4.3), not by the import. The import only maps each film's Access genre
  (collation-insensitive) onto an existing `genres` row and writes a single `movie_genres` row. The loader never
  inserts, updates or deletes genres. A source genre that is not in the list is an **import error** (the whole import
  fails, as for every unmappable row). Until M8, the importer keeps its M7 behavior (one genre per film via
  `genre_id`). The attribute correction for FilmID 308
  ("Actie" to "Sci-fi") keeps working: the corrected genre becomes the film's only genre.
- **Directors:** the cleaned director text is split on `,`, each part trimmed, empty parts dropped, parts that are equal
  by collation within one film collapsed. Each distinct name becomes a `directors` row; `movie_directors` positions
  follow the source order. The report lists the FilmIDs whose director text was split (IDs only, never names), so the
  owner can check that nothing like "Name, Jr." was split wrongly. Currently 4 films are expected to be split.
- **Duplicates:** grouped by Title + Year + the **order-insensitive set** of directors, with the same keep-the-oldest rule
  as before. A .NET comparer is still an approximation of the collation: the unique indexes on `directors` and
  `movies` stay the safety net, and the collation parity test is extended to director names. The expected
  numbers (19 dropped, 1,203 movies) must be re-verified by the dry run; any difference is reported to the owner before
  continuing.
- **Extra's:** the Access column is no longer read; the extras rules, counts and statistics are removed from the loader,
  cleaner, `DataStats`, report and tests.
- **Scores:** still `0` becomes NULL; the DB keeps allowing NULL (decision 9).
- **`-Reset`** empties `movie_genres`, `movie_directors`, `movies`, `directors` and `media` (in FK order), after the
  typed confirmation. **`genres` is reference data and is never emptied.** Users and auth state are never touched.
- **Dump:** data only, tables in FK order `media`, `directors`, `movies`, `movie_genres`, `movie_directors`; `genres` is
  not dumped (the migration seeds it). The guard block requires the latest migration id, that those **five tables are
  empty**, and that `genres` holds exactly the 35 seeded rows (count and the id/name pairs), so a stale or edited
  schema is refused. Dump verification (load into the scratch database and compare counts) covers the new tables.
- **Report** (counts only): source rows, dropped duplicates, movies, genres (35, verified, not inserted), media,
  directors, director links, genre links, FilmIDs with split directors, NULL counts for the five scores, max lengths,
  dates, max id.

---

## 6. UI changes

### 6.1 Edit / new (`/films/{id}`, `/films/nieuw`)

- **Genre:** a multi-select with chips (reference: Motorsports `FuzzyMultiSelect` as used in `RoundWinnersFields`:
  `MudAutocomplete` plus removable chips, already selected items hidden from the suggestions, maximum reached disables
  the input). Closed list: only listed values can be added, suggested alphabetically by name (decision 3b). Label "Genre" (hint "Maximaal 4").
- **Regisseur(s):** the same component with free entry: choosing a suggestion adds an existing director; typing a name
  and pressing Enter, Tab or comma (or leaving the field with text) adds it as a **new** chip, marked as new
  ("Nieuw"). A pasted "A, B" becomes two chips. Suggestions: directors used by at least one film, sorted alphabetically by name (decision 3b),
  contains-match (extends `MovieQueries.SuggestDirectorsAsync`). Chips keep the entered order; reordering is done by
  removing and re-adding. Label "Regisseur(s)" (hint "Maximaal 5").
- Both fields go into `Ui` as one reusable generic component (`ChipMultiSelect`; strings in, strings out, `AllowNew`
  flag, `MaxSelected`, Dutch texts), independent of Movies and Infrastructure. bUnit tests cover it.
- **In-box chips (M8 extension, applies to Regisseur(s) in M9):** the chips are rendered **inside** the outlined input
  box, like a tag input, followed by the text input that fills the remaining space (the label notch is a separate absolutely positioned, aria-hidden outline `fieldset` + `legend`, like MudBlazor,
  so the border opens behind the floated label). The label looks like on the other outlined fields: inside the empty, unfocused
  box (same position as "Filmtitel*"), floating into the notch on focus or when chips exist. The overview filter
  has no label (`HideLabel`), only a placeholder, like the title search. A chip is removed **only** by clicking the X on the chip itself; Backspace in an
  empty input never removes a chip. Clicking anywhere in the box focuses the input. Focus, hover, error and
  disabled states follow the MudBlazor outlined look; the `Hint` or error text sits under the box. The Regisseur(s)
  field must use this same control (including the free-entry chips and the "Nieuw" marker). There are no other
  multi-selects in the app: `ScoreInput`, the sort select and the page-size select are single selects and
  `SuggestField` (Gezien op/in) is single-valued.
- **Placeholder and deferred blur (lesson from M8, applies to Regisseur(s) in M9):** the control has a `Placeholder`
  (shown only while nothing is selected; "Kies een genre*" on the edit page, with the "*" because the field is required, like the other required fields; "Alle genres" on the filter). Its `OnBlur`
  is raised 250 ms late and cancelled when a value is added meanwhile: mouse-down on a suggestion blurs the input, and
  an immediate blur error shifts the layout so that the mouse-up misses the item (the first pick on "Nieuwe film" was
  lost). For the same reason the "Controleer de gemarkeerde velden." banner shows only after a Save attempt, never
  for blur validation. Test: a slow mouse click (move, down, wait, up) on the first suggestion selects it at 390/1440.
- **Required chip fields (lesson from M8, applies to Regisseur(s) in M9):** the chip component never passes `Required`
  to its inner `MudAutocomplete`. That input is always empty while chips exist, so MudBlazor would flag "Required" on
  blur even though values are chosen. The component only appends "*" to the label (`Required`) and shows an error
  when the parent says so (`Error`/`ErrorText`), i.e. only when **no** value is selected and the field has been
  left. Tab does not select the highlighted suggestion (`SelectValueOnTab=false`) for the closed-list field;
  for the free-entry director field Tab adds the typed text only. Tests: bUnit (leaving a required field that has
  values shows no error) and Playwright (leaving the field with zero values shows "Geef minstens één regisseur op.",
  with a value shows no error; new and edit page, 390 and 1440 px), mirroring the genre tests.
- **Evaluatie card:** five `ScoreInput`s: "Totaal (geen gemiddelde)", "Film", "Beeld", "Muziek", "Geluid". "Extra's" is
  gone. `ScoreInput` gets an `AllowEmpty` parameter (default true, so the component stays reusable): the edit page
  passes false, so there is no "–" option, and a new film starts with no score selected. The mobile select shows a
  placeholder instead of "–".
- **Validation (Dutch):** each score "Totaal is verplicht." (and so on) when empty; "Kies een score van 1 tot 10." for
  out-of-range values. Genre: "Kies minstens één genre." / "Kies maximaal 4 genres." Regisseur: "Geef minstens één
  regisseur op." / "Maximaal 5 regisseurs." / "Een regisseur komt maar één keer voor." / name length and comma
  messages. The validation summary and focus on the first invalid field keep working for the chip fields.
- **Legacy rows:** a film with a NULL score opens with that score unselected; saving requires choosing it.
- Dirty tracking, Ctrl+S, the unsaved-changes guard, the conflict flow and the duplicate hint are extended to the
  new fields (the dirty comparison includes the lists).

### 6.2 Overview (`/films`)

- **Genre and Regisseur columns** show all values (desktop: joined with ", " in stored order for directors,
  alphabetical for genres; mobile cards: genre chips). Sorting on these columns is by the displayed joined text,
  computed **server-side** (no in-memory sorting).
- **Genre filter:** a multi-select in the toolbar next to the search box (on mobile in the same row or a collapsible
  row), using the same in-box `ChipMultiSelect` as the edit page (dense, `AllowNew=false`), plus a clear action. Semantics: **all** selected genres must be present. URL state: `genre=3,7` (genre ids, ascending),
  omitted when empty, combined with `q`, `sort`, `page`, `size`; invalid or unknown ids are ignored. Changing the
  filter resets the page to 1. The total count and the empty state ("Geen films gevonden") reflect the filter.
- **Record navigation** on the edit page follows the overview's context including the genre filter (same raw-SQL
  mirror of the overview order; the edit page passes `genre` through untouched). If the current film no longer
  matches, it falls back to the default sort without search and filter, as it does for search today.
- **Statistics carousel:** "Top genres" counts a film once per genre it has; "Top regisseurs" once per director. The
  Extra's figures, if any, are removed.
- **Score pills** keep showing "–" for legacy NULL scores.

---

## 7. Implementation notes and risks

- **Server-side joined text for sorting:** the preferred approach is a correlated `GROUP_CONCAT(... ORDER BY ...)`
  subquery (single source of truth, no denormalized column). First check whether the EF provider translates it in
  LINQ; otherwise use raw SQL as the navigation query already does. No extra columns on `movies` for this. If it proves
  impractical, stop and ask the owner before denormalizing.
- **Genre filter query:** one `Any` per selected genre (AND), or `COUNT(DISTINCT)` = number selected; no in-memory
  filtering. Navigation SQL needs the same predicate with parameters.
- **N+1:** list queries project genres and directors without per-row queries (two additional keyed queries for the
  current page, or aggregated subqueries). The edit load reads the movie plus its two small lists.
- **`directors_key`** is derived data. It is computed in exactly one place (`MovieCommands`) and the import loader
  calls the same helper. A characteristics test verifies key = sorted ids for random sets.
- **Collation parity:** director-name matching in the planner (.NET) must agree with the DB; extend the 28-pair
  parity test with director variants (case, accents, `ø`, `ß`, trailing spaces).
- **Rebuilding the local database** (decision 10, **dev only**; the owner's local `moviedb` is disposable): drop and
  recreate the `moviedb` database (the `moviedb` user has ALL on `moviedb.*`), or drop **every** table including
  `__EFMigrationsHistory`, `users`, the login throttle table and the data-protection keys, never a partial drop. Then
  start the app or run the migrator so the edited `Initial` plus `SeedAuthThrottleGlobalRow` apply, and run
  `Import-Access.ps1 -Reset`. The `admin` user is re-seeded from `Auth__DefaultPassword`. Because the migration id
  stays the same, a stale local database is not detected: always rebuild after pulling a schema milestone.
- **Editing `Initial` in place:** update **all three** model copies: `Initial.Designer.cs`,
  `SeedAuthThrottleGlobalRow.Designer.cs` (it embeds the full model too) and `MovieDbContextModelSnapshot.cs`, and keep
  `Initial.Down` in sync (drop the new tables in FK order). `Initial` stays hand-written and self-guarding; the
  `HasPendingModelChanges` test must stay green. In M7 AGENTS.md gets this exception next to its migration rule:
  "During phase 2 (M7-M10) the schema is rewritten in place (`Initial` edited, no upgrade path) because nothing is
  deployed (owner-approved); the local database is rebuilt and re-imported from Access after every schema change. Once
  a database is deployed, schema changes are new data-preserving migrations again." M10 reviews that wording.
- **Test fixtures:** `genres` is seeded by the migration, so any fixture or test helper that clears tables must not
  clear `genres` (or must reseed it from `MovieGenres.All`).
- **Visibility:** new types are `internal` unless another production assembly needs them; update `PublicSurfaceTests`
  deliberately. No primary constructors on classes; complete XML docs; LF endings; CSharpier.
- **Privacy:** never log review text or rows; test output contains no database content; tests use synthetic data
  (never the `.mdb`).

---

## 8. Testing

Update and extend, using real MariaDB (Testcontainers `mariadb:12.3.3`), never InMemory/SQLite.

- **Unit:** `MovieInput` and validator (required scores, genre and director counts, duplicates, name rules, comma),
  `MovieCleaner` (director split, genre closed list, no extras), planner/duplicates (set-based key, order-insensitive),
  genre constant (35 unique names, length 50), `OverviewState` (genre parameter parsing, defaults omitted, invalid
  ids), `StatisticSlides`, bUnit for the new multi-select component and `ScoreInput` with `AllowEmpty=false`.
- **Characteristics:** `MovieConstraintTests` (new tables, FKs RESTRICT, unique keys, CHECKs, no `extras_score`),
  `MovieCommandTests` (create/update with several genres and directors, new directors inserted, collation reuse, spelling
  rule, orphan cleanup, version bump on list-only changes, **A,B equals B,A duplicate**, duplicate race, validation
  errors, closed genre list), `MovieNavigationQueryTests` and overview query tests (genre filter ALL semantics with
  one, two and zero matches, filter + search, sort on the joined columns both directions, navigation consistent with
  the overview including the filter), migration and import loader tests (new structure, split directors, `-Reset`
  order), dump/guard tests if present.
- **Browser (Playwright, 390 and 1440 px):** existing flows updated for mandatory scores and the new fields; new
  flows: add a film with two genres and two directors (one new), duplicate in reversed director order is rejected,
  genre filter with two genres, legacy film with missing score must be completed.
- **End-of-task routine** per milestone as in AGENTS.md: build `-warnaserror`, XML-doc build, `csharpier check`,
  targeted then full test run, final diff review.

---

## 9. Milestones

One at a time; stop for the owner's review after each (see decision 10c: nothing is deployed in between, and every
schema change rewrites the end-state schema and is followed by a database rebuild and a full Access re-import). Each
milestone updates README, AGENTS.md, PLAN.md pointers and HANDOVER.md where it changes them.

| # | Milestone | Done when |
|---|---|---|
| M7 | **Drop Extra's, mandatory scores:** remove `extras_score` (model, configuration, `Initial` migration, snapshot), `MovieInput`/validator/commands/queries/statistics, `ScoreInput` `AllowEmpty`, edit page, import tool, tests, docs. Rebuild the local DB and re-import. | Build, formatter and the full test run pass; no `Extras` left in `src` or docs (except history); saving without a score is rejected in UI and server; import report matches the previous numbers |
| M8 | **Multi-genre + filter:** closed 35-genre list (`MovieGenres`, seeded by `Initial`), `movie_genres`, edit-page multi-select (Ui component, genres suggested alphabetically, decision 3b), validation, overview column, genre filter (ALL) with URL state, navigation and statistics, import mapping (no genre inserts), dump order and guard. | Tests pass; seeded table equals `MovieGenres.All`; browser check at 390/1440 px; import report shows 35 genres and 1,203 movies; filter with two genres returns only films that have both |
| M9 | **Multi-director:** `directors`, `movie_directors`, `directors_key`, free-entry chips (director suggestions alphabetical, decision 3b), order-insensitive duplicate rule, orphan cleanup, overview column and sort, split import, dump order and guard. | Tests pass incl. reversed-order duplicate; import report lists the 4 split FilmIDs and 19 dropped duplicates (or a reported difference); browser flows pass |
| M10 | **Wrap-up:** documentation (README, AGENTS.md domain rules, PLAN.md §12 and status pointer), full regression incl. browser tests, final dump + verification, HANDOVER.md. | Full suite green; dump verification passes; HANDOVER reflects the final state |

---

## 10. Open questions

None. Anything new that the code or this plan does not establish goes to the owner before it is decided (record the
answer in §2).
