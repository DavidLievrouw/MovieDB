# MovieDB – Plan 4: performance, memory and database indexes

Phases 1-3 ([`PLAN.md`](PLAN.md), [`PLAN-2.md`](PLAN-2.md), [`PLAN-3.md`](PLAN-3.md)) delivered the features. This plan is a
**measured** performance and memory pass: fewer database round trips, less allocation, a smaller idle footprint, and the
indexes that actually help. Read it together with [`AGENTS.md`](AGENTS.md) and [`HANDOVER.md`](HANDOVER.md).

**Status:** **Done: M14-M17 (committed) and M18 (awaiting review). All PLAN-4 milestones are complete.** The owner reviewed the findings and answered every decision (§3). The next agent implements it milestone by milestone (M14-M18), stops for owner review after each, and does
not commit unless asked (same working agreement as PLAN-3).

**Precedence:** AGENTS.md holds the conventions, this plan the spec of this work. Where they disagree, stop and ask. One
deliberate, owner-approved exception to AGENTS.md is recorded in §3 (D8): `Initial` may be edited once more. Language rule
unchanged: Dutch UI, English everything else.

**Owner's brief (verbatim intent):** performance is key, every CPU cycle and every byte counts. Find performance gains in
the code, achieve the most effective memory use, check how MariaDB is used, add the indexes that make sense, and keep
`Import-Access.ps1` in step with every database change.

**Ground rule: measure before and after (AGENTS.md "measure before optimizing").** The dataset is tiny (about 1,200 movies,
715 directors, 1,207 director links, 1 user). A change is **kept only if** the benchmark (M14) shows a gain in time,
allocations or round trips, or it is neutral on time and clearly cheaper in memory. Otherwise revert it and note why in
HANDOVER.md. Do not add anything "because it is usually faster".

---

## 1. Findings (from the code review, with file references)

All paths are under `src/`. Line numbers drift; search by symbol.

### 1.1 CPU / round trips

| # | Where | Finding |
|---|---|---|
| C1 | `MovieDB.Movies/MovieQueries.cs` `ListOverviewAsync` + `LoadRowsAsync` | Up to **6 round trips** per overview load (total, matching count, page ids, movies, genres, directors) plus anonymous types, two `GroupBy`/`ToDictionary` passes and id lists. |
| C2 | `MovieQueries.NavigateAsync` | Five window functions, each repeating `OVER (ORDER BY …)`, so the engine may sort the whole list several times. |
| C3 | `MovieQueries.ListGenresAsync`, called from `Pages/Overview.razor` (`OnParametersSetAsync`) and `Pages/MovieEditPage.razor` (~line 491) | A DB query for every circuit/page although the genre list is closed and seeded (35 rows; ids are stable). |
| C4 | `MovieQueries.FindDirectorsKeyAsync` | One query per director name (1-5). |
| C5 | `MovieQueries.FindAsync`, `FindForEditAsync` | Expression tree translated on every call (fixed shape). |
| C6 | `MovieStatistics` path (`GetStatisticsAsync`) | Four sequential queries each time the carousel mounts. **No cache (D3).** The date index (§2) may make the date counts cheaper; do not restructure further unless the benchmark says it matters. |
| C7 | `MovieOverviewRow.DirectorText` / `GenreText` | `string.Join` on every access (called per render per row). |
| C8 | `Pages/Overview.razor` `SelectedGenreNames`, `_genres.Any(...)` | Linear scans and a new list per render. Minor; fold into C3 (the cached catalog can offer an id → name dictionary). |

### 1.2 Memory / runtime

| # | Where | Finding |
|---|---|---|
| M1 | `MovieDB.Infrastructure/DatabaseConfiguration.cs` `AddMovieDbDatabase` | `AddPooledDbContextFactory` without `poolSize` keeps up to **1024** contexts. MySqlConnector's default `MaximumPoolSize` is 100. |
| M2 | `MovieDB.Web/Program.cs` `AddInteractiveServerComponents` | `DisconnectedCircuitMaxRetained = 20`, retention 5 minutes: too generous for one user. |
| M3 | `docker-compose/moviedb-web/docker-compose.yml`, `MovieDB.Web/Dockerfile` | `DOTNET_GCHeapHardLimitPercent=50` is set but the container has **no memory limit**, so the percentage is meaningless. |
| M4 | `MovieDB.Web/MovieDB.Web.csproj` (`TieredPGO` true), Dockerfile (`DOTNET_TieredPGO=1`) | Out of scope (owner decision): the expected gain is too small to be worth the effort. Left as is. |
| M5 | Overview read path | Already no-tracking and projects only needed columns; `review` (mediumtext) is not read by list queries. Nothing to do. |
| M6 | Column types | Not worth shrinking (`int` → `mediumint` saves a few KB in total). **Do not change** column types. |

### 1.3 Database (MariaDB 12.3, collation `utf8mb4_uca1400_ai_ci`)

Existing indexes (all required by FK/unique rules; none is redundant): `pk_movies`, `ux_movies_title_year_directors`,
`ix_movies_medium_id`, `ux_genres_name`, `pk_movie_genres` + `ix_movie_genres_genre_id`, `ux_directors_name`,
`pk_movie_directors` + `ux_movie_directors_movie_position` + `ix_movie_directors_director_id`, `ux_media_name`, auth tables.

Access patterns (see `MovieQueries`): default sort `watched_on DESC, title, id DESC`; navigation uses the same order;
title search is `LIKE '%text%'` (kept, D1: a B-tree cannot serve it and FULLTEXT would change the semantics); statistics
count by `watched_on` ranges and `overall_score >= 9`.

---

## 2. Index proposal (D2)

One new index, **only if `EXPLAIN ANALYZE` proves it is used and helps** on the representative dataset (M14):

```
ix_movies_watched_on_title  ON movies (watched_on DESC, title [, id DESC])
```

- Serves the default overview order and the navigation order, and the `watched_on` range counts of the statistics.
- **Check the tail of the sort.** `OrderSql` appends `m.id DESC` when the primary sort is descending. An InnoDB secondary
  index stores the primary key ascending after the declared columns, so `(watched_on DESC, title)` alone may still filesort
  on the `id DESC` tie-breaker. Try three variants and keep the one `EXPLAIN` shows without `Using filesort`:
  (a) `(watched_on DESC, title)`, (b) `(watched_on DESC, title, id DESC)`, (c) change the tie-breaker in `OrderSql` (only
  if navigation and paging stay consistent and tests prove the same order).
- The `LEFT JOIN media md` and the `ORDER BY` with `md.name` sorts cannot use this index; that is fine.
- MariaDB honours `DESC` index parts since 10.8, so 12.3 is fine. EF: `HasIndex(...).IsDescending(true, false)` (verify the
  Microting provider accepts it; the DDL itself is raw SQL in `Initial`, so only the model/snapshot must agree).
- If `EXPLAIN` shows the optimizer prefers a scan/filesort anyway for 1,200 rows (likely at this size), **drop the index**
  and record the evidence. At ~12,000 rows (the benchmark's second size, M14) it must be clearly beneficial to be kept.

**M15 result (kept).** Variant (b) `(watched_on DESC, title, id DESC)` removes the filesort of the default overview order:
12,030 movies 3.64 ms to 0.39 ms (about 9x, `type=index`, 25 rows), a middle page 5.77 to 2.53 ms; 1,203 movies 0.75 to
0.45 ms. Variant (a) only helps an `id ASC` tie-breaker, so (c) was not needed. Navigation and the statistics totals did not
benefit (left to M16 C2).

Explicitly **not** added: indexes on `release_year`, `runtime_minutes`, `overall_score`, a covering index for statistics,
FULLTEXT. A filesort of 1.2k rows is microseconds and every index costs writes and RAM. Revisit only on benchmark evidence.

---

## 3. Decisions (all final, owner answers)

| # | Decision |
|---|---|
| D1 | Title search stays `LIKE '%text%'` (escaped via `ContainsPattern`). No FULLTEXT, no prefix search. |
| D2 | Add `ix_movies_watched_on_title` only if proven (§2). No covering index. |
| D3 | **No cache** for the statistics. |
| D4 | The genre list is a **process-lifetime singleton cache** (closed seeded list; no invalidation). |
| D5 | Apply the tighter runtime limits of §6, **including `mem_limit: 256m`** in compose. `TieredPGO` is left unchanged (owner decision: not worth benchmarking). |
| D6 | Use `EF.CompileAsyncQuery` for the hot fixed-shape queries (`FindAsync`, `FindForEditAsync`), kept per the ground rule. |
| D7 | Add a **benchmark** on a temporary MariaDB database with dummy data of about today's size (§5). |
| D8 | Nothing is deployed, so **`Initial` may be edited in place** (and its snapshot/Designer). The owner rebuilds the local database from the `.mdb` (§4.3). This is a one-time exception to AGENTS.md ("never edit `Initial` again"). **Only if the index is kept** (§2): add a note to AGENTS.md that, after this plan's edit, `Initial` is final again and from the first deployment on only new migrations are allowed. If the index is dropped, `Initial` is not touched and AGENTS.md stays as it is (owner decision). |
| D9 | M12 and the poster work are committed; this plan starts from a clean tree. |

---

## 4. Database changes

### 4.1 Edit `Initial` (D8)

- `src/MovieDB.Infrastructure/Migrations/20261028115529_Initial.cs`: add the index to the `movies` `CREATE TABLE IF NOT EXISTS`
  (inline `INDEX ix_movies_watched_on_title (...)`), keeping every statement self-guarding and retry-safe. Keep `Down`
  usable. Do not add a second migration.
- `MovieConfiguration.cs`: `builder.HasIndex(...).HasDatabaseName("ix_movies_watched_on_title")` with the same column list and
  descending flags.
- Update `MovieDbContextModelSnapshot.cs` and the `Initial.Designer.cs` so the model matches (generate with
  `dotnet ef migrations add Tmp --project src/MovieDB.Infrastructure --startup-project src/MovieDB.Web`, copy the snapshot
  change, then delete `Tmp`; **make no other change** to `Initial`). Migrations stay `internal`.
- Confirm there is a test that fails on a pending model change (model vs snapshot); if not, add one.
- Skip the whole §4 index work if `EXPLAIN` rejects the index (§2); then there is **no schema change at all**, and §4.3 and
  §4.2 reduce to the `ANALYZE TABLE` addition.

### 4.2 Keep `scripts/Import-Access.ps1` and the AccessImport tool in step (owner requirement)

- `Import-Access.ps1` and `src/tools/MovieDB.AccessImport/*` do not create schema: the script calls the tool's `migrate`
  (which runs the app migrations, so the index arrives with `Initial`), imports data, and writes a **data-only** dump
  (`--no-create-info`) of `media directors movies movie_genres movie_directors`. The dump therefore needs no change for an
  index. **Verify** this by running it end to end (§4.3), including the scratch-database verification step.
- Add: after the import (and after the scratch restore), run `ANALYZE TABLE movies, movie_genres, movie_directors,
  directors, genres, media` inside the container (`docker exec … mariadb -e …`, password via `MYSQL_PWD` like the other
  calls) so the optimizer has fresh statistics on the freshly loaded database. Never print rows or secrets.
- Update the `.SYNOPSIS/.DESCRIPTION` comment, `PLAN.md` §6.5/§11.2 wording and the README deployment section: after loading
  the production dump the owner runs `ANALYZE TABLE …` once (give the exact command; InnoDB would recalculate in the
  background eventually, this makes it immediate).
- If any column or table is changed after all (none is planned), also update `ImportLoader.cs` (it reads
  `information_schema.TABLES` for `AUTO_INCREMENT`), the `fingerprint` command, the dump table list and the
  `ImportLoader`/dump tests in the same change.

### 4.3 Rebuild the local database (owner will review the result)

Because `Initial` changed, the local `moviedb` database must be rebuilt from scratch:

1. Drop all tables of the local `moviedb` database (container `mariadb`, user `moviedb`), including
   `__EFMigrationsHistory`. The connection string is in the user-secrets: never print or write it.
2. `./scripts/Import-Access.ps1 -Source .\FilmsBase.mdb -Reset` (expect 1,203 movies, 715 directors, 1,207 director links).
   The default user `admin` is seeded on the next app start.
3. Report counts only; no rows.

If a destructive step lacks permission (drop database), fall back to dropping tables one by one, or ask the owner.

---

## 5. Benchmark (D7): milestone M14 comes first, and records the baseline

### 5.1 Project

New project `src/tests/MovieDB.Benchmarks` (console app, BenchmarkDotNet, Testcontainers.MariaDb `mariadb:12.3.3`), added to
`src/MovieDB.slnx` and to central package management (`Directory.Packages.props`). It references Movies and Infrastructure
(tests may reference anything). It is **not** run by `dotnet test` (it is an executable, not a test project); document the
command in AGENTS.md and README (§5.5). The benchmarks run **in process** (`--inProcess`, added by `Program.cs`) so they share the one
container and the seeded databases; this also keeps the solution's analyzers and central package management. Docker must be running.
`Directory.Build.props` grants the project `InternalsVisibleTo` (owner-approved).
Add an architecture-test-safe layout (nothing in `src/MovieDB.*` may reference it; the existing rule already covers this).

### 5.2 Data: synthetic, deterministic, representative

- A **temporary database** in a Testcontainers MariaDB (a throwaway database, created through the real migrations so the
  schema and indexes are exactly production's), dropped at the end. Never touch the local `moviedb` database, never use the
  `.mdb` or its contents.
- Size of today's data: **1,203 movies, 715 directors, ~1,207 director links (a few movies have 2+), 1-3 genres per movie
  (the seeded 35 genres), ~8 media, reviews of 200-2,000 characters, five scores (a few NULL), `watched_on` spread over about
  25 years, skewed so some days have several movies.** Fixed random seed; Dutch-looking and accented names and some titles
  that share Title + Year so the unique index and collation are exercised.
- A second size parameter at **×10 (about 12,000 movies)** as a sensitivity check for index decisions, not for the baseline.
- After loading: `ANALYZE TABLE` on all tables (same as the script).

### 5.3 What is measured

Benchmarks (use `[MemoryDiagnoser]`; report time, allocations and, via a counter/`CommandInterceptor`, **number of database
round trips** per operation):

- `ListOverviewAsync`: default sort page 1; sort by title; sort by director; sort by genre; search (title `LIKE`); 1 genre
  filter; 2 genre filter; the last page.
- `GetNavigationAsync`: middle of the list, with search, with genre filter.
- `GetStatisticsAsync`.
- `FindAsync`, `FindForEditAsync`, `FindDuplicateAsync`, `FindSameTitleAndYearAsync`, `SuggestDirectorsAsync`,
  `SuggestMediaAsync`, `ListGenresAsync` (and the cached catalog).
- Save command for a new movie (`MovieCommands`), in a rolled-back or disposable way so the data stays the same.

**Baseline first:** run the benchmark on the **unmodified** code and store the summary under git-ignored
`artifacts/benchmarks/baseline` (and a short table in HANDOVER.md). Every later milestone compares against it.

### 5.4 TieredPGO (out of scope)

The owner decided that a TieredPGO on/off comparison is not worth the effort. Neither the benchmarks nor the settings change it.

### 5.5 Commands

```powershell
# timings and allocations (add --job short for a quick run)
dotnet run -c Release --project src/tests/MovieDB.Benchmarks -- --filter "*" --artifacts artifacts/benchmarks/<name>
# database round trips per operation, written to roundtrips.md
dotnet run -c Release --project src/tests/MovieDB.Benchmarks -- --roundtrips artifacts/benchmarks/<name>
```

---

## 6. Runtime and memory settings (D5)

Apply, then re-measure idle and after-use memory with `docker stats` (and `GC` counters in the benchmark where relevant):

| Setting | Where | New value |
|---|---|---|
| `AddPooledDbContextFactory(..., poolSize: 8)` | `DatabaseConfiguration.AddMovieDbDatabase` | 8 (was 1024) |
| MySqlConnector `MaximumPoolSize` | `DatabaseConfiguration.Configure` via `MySqlConnectionStringBuilder` | 8 **only when the connection string does not set it**; keep `RequireUtf8mb4`. Check the characteristics tests (parallel contexts) still pass; `DatabaseMigrator`'s dedicated lock connection must not be starved. |
| `DisconnectedCircuitRetentionPeriod` / `DisconnectedCircuitMaxRetained` | `Program.cs` | 2 minutes / 3 |
| `mem_limit: 256m` | `docker-compose/moviedb-web/docker-compose.yml` | owner-approved. With `DOTNET_GCHeapHardLimitPercent=50` the managed heap is capped at 128 MB. Check idle and after-use `docker stats`; if the app is killed or near the limit, report to the owner before raising it. |
| `DOTNET_GCConserveMemory=5` | Dockerfile `ENV` | on; verify memory goes down and the CPU does not visibly rise |
| `GCgen0MaxBudget` | Dockerfile `ENV` | **optional**: only if the measurement shows gen0 churn dominating memory; otherwise leave it |

Existing and unchanged: `DOTNET_gcServer=0`, `ConcurrentGarbageCollection=false`.

---

## 7. Code changes (each is subject to the ground rule)

1. **C1 – overview in two queries.** One counts query returning `total` and `matching` together
   (`SELECT (SELECT COUNT(*) FROM movies), (SELECT COUNT(*) FROM movies m WHERE 1=1{where})`; skip the second subquery
   when there is no filter) and one page query that also returns the director and genre texts via correlated `GROUP_CONCAT`
   (directors `ORDER BY position`, genres `ORDER BY name`, same fragments `OrderSql` already uses for sorting). Build rows
   straight from the reader/`SqlQueryRaw` result type. Keep parameterised SQL and the fixed-fragment composition rules of the
   existing code. Keep the page number clamping (needs the count first; if the page is clamped, run the page query after it).
   Directors/genres names are free text: if a separator character could occur in a name, use `CHAR(31)` as separator and
   split in C#, or keep `SEPARATOR ', '` and carry only the joined **text** (see C7). Prefer carrying text only.
2. **C7 – row shape.** If the grid only needs the joined texts, change `MovieOverviewRow` to carry
   `DirectorText`/`GenreText` strings instead of lists (check `Pages/Overview.razor`, tests and the `MovieEditPage`
   navigation for users of the lists first; keep lists where something else needs them).
3. **C2 – navigation window.** One named window (`WINDOW w AS (ORDER BY …)`, and a second for the unbounded frame used by
   `LAST_VALUE`) instead of five inline `OVER` clauses. Verify MariaDB 12.3 accepts the syntax and that results are identical
   (equivalence test, §8). If it is not faster, keep the old text.
4. **C3/C8 – genre catalog singleton (D4).** A small `internal sealed` `GenreCatalog` (singleton, `MoviesModule`): lazily loads
   once through `IDbContextFactory`, sorted by the database collation like `ListGenresAsync` today; exposes the list and an
   id → name lookup. **A failed load must not be cached** (the next call retries). No invalidation: the list changes only
   with a migration (restart). Use it from `Overview.razor`, `MovieEditPage.razor` and the validator if it queries genres.
   Keep `ListGenresAsync` only if something still needs it. Document in AGENTS.md ("lookup-data cache": genres only).
5. **C4 – directors key.** One `IN (...)` query only if a test with case/accent variants proves it resolves names exactly like
   the current per-name `Name == name` (collation) comparison and still returns "unknown → null". Matching in C# is **not**
   allowed (AGENTS.md: .NET comparison is only an approximation of the collation). If it cannot be done safely, leave it:
   it is at most 5 tiny indexed lookups.
6. **C5 – compiled queries (D6).** `EF.CompileAsyncQuery` static delegates for `FindAsync` and `FindForEditAsync`, using a
   context from the factory per call. Verify translation still works with the Vogen ids and nested `ToList()` projections.
7. **C6 – statistics:** unchanged (no cache, D3); just re-measure after the index.
8. Do not add retries, caches of mutable data or `EnableRetryOnFailure` (AGENTS.md).

---

## 8. Tests

- **Equivalence (real MariaDB, Testcontainers, synthetic data):** the new overview and navigation queries return exactly the
  same rows, order, counts and neighbours as the old ones for every sort column and direction, multi-sort, search with
  `%`, `_`, `\`, 1-2 genre filters, page clamping and an empty result. Capture the old results before replacing the code
  (keep the old implementation in the test project as a reference, or generate expected values by plain LINQ over the seeded
  data), then remove the reference if it is not needed any more.
- **Index:** a characteristics test that the index exists with the intended columns/directions, and (if kept) that
  `EXPLAIN` for the default overview query uses it. Skip nothing silently: if it cannot be asserted, document why.
- **Migration:** `Initial` runs twice without error (retry safe) and `Down`+`Up` works; model/snapshot match (§4.1).
- **Genre catalog:** loads once (count the queries), retries after a failed load, equals the seeded `MovieGenres.All`.
- **Collation test for C4** if C4 is implemented (case and accent variants; unknown name).
- **Config:** unit tests for the pool-size default (applied only when the connection string does not set it) and for
  `RequireUtf8mb4` still being enforced.
- Existing unit, characteristics, architecture and browser tests stay green. Characteristics tests that count queries
  should not rely on timing.

---

## 9. Milestones

Every milestone ends with the end-of-task routine of AGENTS.md (build `-warnaserror`, XML-doc build, `dotnet csharpier
format` + `check` (revert the pre-existing rewrite of `src/Directory.Packages.props` with `git checkout`), targeted tests,
then the full suite when shared behaviour changed), a HANDOVER.md update, and a stop for owner review.

- **M14 – Benchmark harness and baseline.** §5. Project, data generator, benchmarks, round-trip counter, baseline run stored
  in `artifacts/benchmarks/baseline`, and the commands in AGENTS.md/README. No production code change except
  `InternalsVisibleTo` for the benchmarks project.
- **M15 – Schema and import tooling (done, see HANDOVER.md; index `(watched_on DESC, title, id DESC)` kept).** §2 (`EXPLAIN` experiments on both data sizes first), §4: the index in `Initial` +
  configuration + snapshot (only if proven), the `Import-Access.ps1` `ANALYZE TABLE` step and docs, AGENTS.md exception note (only if the index is kept),
  rebuild of the local database from the `.mdb` (§4.3) and a verified full `Import-Access.ps1` run (with dump
  verification). Report counts only.
- **M16 – Query and code optimizations. Done (awaiting review).** §7 items in the order C3, C1+C7, C2, C5, C4 with the equivalence tests (§8). After
  each item re-run the relevant benchmarks against the baseline; revert items that do not pass the ground rule.
  Result: C3, C1+C7 and C5 kept; C2 reverted (no time gain, ~3% memory); C4 skipped (cannot be proven safe without
  in-memory name matching). Evidence in HANDOVER.md.
- **M17 – Runtime and memory settings.** §6: pool sizes, circuit retention, `mem_limit: 256m`, `GCConserveMemory`, optional
  gen0 budget; measure `docker stats` idle and after a short use session (overview, edit, statistics, TMDB dialog) before
  and after (the limit was raised from 256m to 384m after review, for headroom); update the compose file, Dockerfile, README deployment text and the health check timing if needed.
  Result: all items kept except the optional gen0 budget (skipped, no evidence); settled memory 244 → 200 MiB, peak
  260 → 224 MiB, no OOM. Evidence in HANDOVER.md.
- **M18 – Wrap-up. Done (awaiting review).** Final benchmark run vs baseline (table of before/after in HANDOVER.md: time, allocations, round trips,
  memory), update AGENTS.md (index/`Initial` rule, compiled queries, genre cache, benchmark commands, runtime limits),
  PLAN.md status, this plan's status, full regression, the final diff review (no generated artifacts, no secrets, no rows,
  `prompt-history.txt` untouched).

---

## 10. Risks and notes for the implementer

- **Never read, edit, stage or quote `prompt-history.txt`** (AGENTS.md). It is the owner's private file.
- The `Initial` edit invalidates any database that already ran it: only the local one exists; rebuild it (§4.3). The
  Testcontainers databases are always fresh.
- `mem_limit` too low kills the container: measure, and ask before raising it. The shared MariaDB keeps its own limit.
- `GROUP_CONCAT` is limited by `group_concat_max_len` (default 1 MB in MariaDB): irrelevant for 5 directors/4 genres, but do
  not use it for reviews.
- A DESC index and the `id DESC` tie-breaker interaction (§2) is the most likely reason the index will not be used;
  measure before concluding either way.
- Keep Sonar cognitive complexity ≤ 15, complete XML docs (including `<param>` and `<exception>`), Roslynator-clean code,
  LF line endings, no primary constructors on classes, `internal` visibility by default.
- Check `git log` before starting: PLAN-3's M13 (README/AGENTS wrap-up) may still be open; do not mix it into this work.
