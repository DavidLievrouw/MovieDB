# MovieDB – Plan 3: fill in a new film from an online movie database

Phase 1 ([`PLAN.md`](PLAN.md)) reached feature parity with Access, phase 2 ([`PLAN-2.md`](PLAN-2.md)) added multi-genre and
multi-director. This plan adds **"search a film online and fill in the form"**. Read it together with
[`AGENTS.md`](AGENTS.md) and [`HANDOVER.md`](HANDOVER.md).

**Status:** **M11 done (committed); M12 implemented (dialog, attribution, page integration), awaiting owner review; M13 not started.** The service
research (§2) is done and the owner has answered all open questions (§11); the decisions in §3 are final. A gap review
(2026-11-05, §14) tightened the spec and the owner answered its five confirmation questions (§14.2); nothing is open.
Implementation deviation: `401`/`403` map to a separate `ErrorKind.Misconfigured` (same Dutch text as `Unavailable`) so
that callers and logs can tell a configuration problem from an outage.

**Precedence:** AGENTS.md holds the conventions, this plan holds the spec of this feature. Where they disagree, stop and
ask the owner. **Language rule** is unchanged: Dutch UI, English everything else.

---

## 1. Goal and scope

On the **new film page** (`/films/nieuw`) the owner can:

1. open a "Zoek film online" search and type a title (fuzzy; the search is done **in English**);
2. see a list of hits, each with a **link to its source page** (opens in a new tab) so the right film can be verified;
3. pick one hit; the details are fetched in the background and the form is filled in automatically with
   **Title (the original title), Genre(s), Duur (minutes), Jaar and Regisseur(s)**;
4. keep a **link to the selected film's source page** visible on the form after filling it in;
5. get friendly Dutch messages when nothing is found, the service is unavailable, or data is incomplete.

Nothing is saved automatically: the owner reviews the filled form, adds Datum, Gezien op/in, the five scores and the
review, and saves as before.

The whole feature is **optional**: without a configured API token the app runs normally and the search is disabled.

Out of scope: full-size images, pasting links or IMDb ids into the search box, series, cast, plot text, user ratings, bulk
import, a second provider, caching of provider data, the edit page of existing films, any change to the database
schema. (Extension after M12, owner request: a small poster thumbnail next to each search hit, see §7.5.)

---

## 2. Research: which service?

Requirement: free, usable from a personal server application, search by title, and delivers title, genres, runtime,
year and directors, with a stable page to link to.

| Service | Free? | Search | Fields we need | Verdict |
|---|---|---|---|---|
| **TMDB** (The Movie Database, `api.themoviedb.org/3`) | Free for **non-commercial** use with a free API key (account needed) | Relevance search `GET /search/movie`, matches original, translated and alternative titles; optional `year` | Details + credits in **one call** (`append_to_response=credits`): `title`, `original_title`, `genres[{id,name}]`, `runtime`, `release_date`, `imdb_id`, `credits.crew[{job,name,id}]`; localized (`language=en-US`) | **Chosen** |
| OMDb (`omdbapi.com`, IMDb-derived, user-maintained) | Free key, 1,000 calls/day | `s=` returns title matches, little fuzziness | `Runtime` ("136 min"), `Genre` and `Director` are **comma-joined strings** (ambiguous for names like "Name, Jr."); English only | Fallback candidate, not built |
| IMDb | **No free API.** Official API is a paid AWS Data Exchange product; the free datasets are bulk TSV files for personal use, with no search | n/a | n/a | Rejected |
| Wikidata / Wikipedia | Free, no key | Fuzzy-ish `wbsearchentities`, but results are all entity types | Runtime/director/genre are inconsistent and need several calls and SPARQL | Rejected |
| TVmaze, Trakt, Letterboxd | TV only / needs OAuth app / API by request | | | Rejected |

Why TMDB: structured data (genre **ids**, separate crew list with `job`), one call for details plus directors, original titles, stable page URLs, an IMDb id for a second verification link, and generous limits (about 40 requests/second
upper bound, `429` on abuse; we make 1-2 calls per user action).

### 2.1 TMDB terms that bind this implementation

Source: <https://www.themoviedb.org/api-terms-of-use>, <https://developer.themoviedb.org/docs/faq>.

- **Non-commercial only.** This is a personal application with one user: fine. Do not add ads, payment or sharing.
- **Attribution is mandatory.** Show a TMDB logo (an approved one, unmodified, less prominent than the app's own
  branding) and the sentence **"This application uses TMDB and the TMDB APIs but is not endorsed, certified, or otherwise
  approved by TMDB."** (the exact English notice, §7.4). The FAQ asks for it in an "About/Credits" place; the terms say
  "prominently": show it in the search dialog footer **and** on the new-film page (`/films/nieuw`), the only place the
  provider is used (owner decision, §14.2 Q4; no app-wide footer).
- **Do not cache provider data longer than 6 months.** This plan caches nothing (data is copied into the user's own
  record when they press save, which is the intended use).
- **Do not hide the identity of the application**: send a `User-Agent` of `MovieDB/<version>`.
- **No use for ML/AI training**, no resale. Not applicable here, do not add such features.
- **Respect `429`**; no retry loops.

### 2.2 API calls used (all `GET`, base `https://api.themoviedb.org/3`)

Auth: header `Authorization: Bearer <API Read Access Token>` (the "v4" token from the TMDB account's API settings page;
works on v3 endpoints). **Never** use the `api_key` query parameter: query strings end up in logs.

| Purpose | Request | Fields used |
|---|---|---|
| Fuzzy title search | `/search/movie?query={text}&language=en-US&include_adult=false&page=1` (+ `&year={yyyy}` if the user typed a year, §5.1) | `results[].id`, `title` (English), `original_title`, `release_date`, `overview` (shortened for display) |
| Details + directors | `/movie/{id}?language=en-US&append_to_response=credits` | `id`, `title`, `original_title`, `release_date`, `runtime`, `genres[].id`, `imdb_id`, `credits.crew[]` where `job == "Director"` (`id`, `name`) |

Source page links: `https://www.themoviedb.org/movie/{id}` for every hit; after the details call also
`https://www.imdb.com/title/{imdb_id}/` when `imdb_id` is present **and matches `^tt\d{7,10}$`**. Both URLs are built by
the application from the numeric id / validated imdb id; a URL string from the API response is never used in an `href`.

Things to **verify with a real token during M11** (they cannot be verified without a key, and the answers decide small
details; record the outcome in HANDOVER.md):

- How tolerant the search is of typos and partial titles ("matirx", "godfath"). TMDB does not document fuzzy rules.
  The owner will judge the fuzzy search himself once it is built. If it is poor, the acceptable improvements are: retry
  once with the trailing year removed, and re-rank so that exact/prefix title matches come first. Do not add another
  provider without asking.
- The real `404` shape and `429` headers (`Retry-After`) for the friendly messages.
- That `original_title` is always present (expected; fall back to `title` when empty).

---

## 3. Decisions (final, answered by the owner, see §11)

| # | Topic | Decision |
|---|---|---|
| 1 | Provider | TMDB (§2). One interface (`IMovieLookup`) so a provider can be swapped later; no second implementation now. |
| 2 | Where | A dialog "Film online zoeken" opened from a button on `/films/nieuw` only. |
| 3 | Fields filled | Title, Genres, RuntimeMinutes, ReleaseYear, Directors. Never WatchedOn, Medium, scores or Review. |
| 4 | Language | Search and details are requested in **English** (`en-US`). The Filmtitel is filled with the **original title** (`original_title`; falls back to `title` when empty). Hits show the original title, plus the English title muted when it differs. |
| 5 | Genres | Map TMDB genre ids to the closed list of 35 (§6). Unmappable ids are dropped. At most 4 (first 4 in TMDB order). |
| 6 | Directors | Crew entries with `job == "Director"`, de-duplicated by TMDB person id, TMDB order, at most 5. |
| 7 | Overwrite | The five fields are replaced. If any of them already contains user input, ask for confirmation first (§7.3). |
| 8 | Input | Title text only (optionally with a year). Pasted links and IMDb ids are **not** supported. No user-supplied URL is ever fetched. |
| 9 | Config | `Tmdb:ApiReadAccessToken` is **optional**, set through user-secrets (development) or the environment variable `Tmdb__ApiReadAccessToken` (container); never in a tracked file (§9). Without it the button is shown disabled with an explanation; the app still starts. |
| 9a | Source link | After filling, a link to the selected film's TMDB page (and IMDb when known) stays visible on the form (§7.3). It is not stored in the database. |
| 10 | Caching/retries | None (AGENTS: no blind retries, no caches without a freshness policy). |
| 11 | No schema change | No migration, no new tables. |
| 12 | Details call | The **dialog** performs the details call after a pick and only closes on success (§5.3), so failures can offer "Opnieuw proberen"/"Kies een andere" without the form being touched. (Added by the 2026-11-05 review to resolve the §5.3/§8 contradiction; confirm in §14.2 Q1.) |
| 13 | Attribution scope | The TMDB logo and notice are shown in the dialog footer **and** at the bottom of the `/films/nieuw` page (§7.4). Nowhere else: no `MainLayout` change. |
| 14 | Overwrite prompt | The prompt appears only when something **other than the Filmtitel** holds input (the Filmtitel seeds the search and is always replaced silently). Owner decision, §14.2 Q2. |

---

## 4. Architecture

Stay inside the existing slice rules (AGENTS.md "Architecture"): `MovieDB.Movies` references Common, Ui and
Infrastructure only; the host composes. No new project. All new types are **`internal`** (tests see them through
`InternalsVisibleTo`); generated Razor components are public as always. Keep the architecture tests green.

New code in `src/MovieDB.Movies/Lookup/`:

| Type | Responsibility |
|---|---|
| `IMovieLookup` | `bool IsConfigured`; `SearchAsync(LookupQuery query, CancellationToken)` -> `Result<IReadOnlyList<LookupHit>>` (no hits = success with an empty list); `GetDetailsAsync(int id, CancellationToken)` -> `Result<LookupDetails>` (`id` is the provider's numeric id; `<= 0` is a programming error -> `ArgumentOutOfRangeException`). Provider-neutral: no TMDB type leaks through it. |
| `LookupQuery` (record) | `Title` (non-empty), `Year?` (§5.1). |
| `LookupHit` (record) | `Id` (`int`, TMDB id), `Title` (English), `OriginalTitle?`, `Year?`, `Overview?` (cut to about 200 chars by the provider), `SourceUrl` (`Uri`, TMDB page). |
| `LookupDetails` (record) | The provider's data, **already cleaned of provider specifics but not yet of app rules**: `Title` (`original_title`, trimmed; `title` when empty), `Year?` (parsed from `release_date`; `null` when missing/invalid), `RuntimeMinutes?` (raw `runtime`; `null` when missing), `Genres` (`IReadOnlyList<string>`: app genre names mapped through `TmdbGenreMap`, TMDB order, de-duplicated, **not** capped), `Directors` (`IReadOnlyList<string>`: names of crew with `job == "Director"`, TMDB order, de-duplicated by TMDB person id, trimmed, non-empty, **not** capped), `SourceUrl` (`Uri`), `ImdbUrl?` (`Uri`). |
| `LookupInputParser` | Turns the typed text into a `LookupQuery`; splits off a trailing year (§5.1); reports too short / too long. Pure, no I/O; takes the current year as a parameter (from `UserClock`). |
| `TmdbMovieLookup` | The `IMovieLookup` implementation over a typed `HttpClient`; private DTOs for the two responses; does all TMDB-specific work (JSON, genre-id mapping, `job == "Director"` filtering, link building, `release_date` parsing); maps failures to `Error`/`ErrorKind` (§8). |
| `TmdbGenreMap` | Static TMDB genre id -> app genre name mapping (§6). |
| `TmdbOptions` | Options: `ApiReadAccessToken` (optional), `Language` (default `en-US`), `BaseUrl` (default `https://api.themoviedb.org/3/`, https only, absolute, ends with `/`), `TimeoutSeconds` (default 10, range 1-30). `ValidateOnStart` (validation applies only to what is set; an empty/whitespace token is valid and means "not configured"). Follow the `AppOptions` pattern in `Program.cs` (`AddOptions().Bind().Validate...ValidateOnStart()`), registered inside `AddMoviesServices`. |
| `LookupFill` | Pure, provider-neutral function `IReadOnlyList<string> Apply(MovieInput target, LookupDetails details, UserClock clock)`: applies the **app rules** of §5.3 (length cuts, year/runtime ranges, caps of 4 genres / 5 directors, comma replacement, name de-duplication) to the five fields of `target` and returns the Dutch notices (§7.3). Unit-testable without Blazor. |
| `LookupMessages` | One static class with every Dutch UI string of this feature (error text per `ErrorKind`, notices, empty states), so the strings are tested and reused by dialog and page. |
| `MovieLookupDialog.razor` | The dialog (§7). It injects `IMovieLookup`, does the search **and** the details fetch (§5.3) and returns the `LookupDetails`. |
| `TmdbAttribution.razor` | Small public Razor component (logo + the notice of §7.4), used by the dialog footer and by `MovieEditPage` (new-film mode only). |

**Supporting changes outside `Lookup/`:**

- `MovieDB.Common/Result.cs`: add the `ErrorKind` values `NotConfigured`, `Unavailable`, `RateLimited` and
  `InvalidResponse` (existing `NotFound` is reused for details `404`) and matching `Error` factory methods, with a test
  in `ResultTests`. The only `switch` over `ErrorKind` (`ShowFailureAsync` in `MovieEditPage`, for save failures) has a
  `default` branch and never sees lookup errors, so it is unaffected.
- `MovieInput`: add `MinRuntimeMinutes = 1` and `MaxRuntimeMinutes = 999` constants and use them in the validator
  (`InclusiveBetween`, same Dutch message), so `LookupFill` and the validator cannot drift apart.
- `MovieQueries.FindKnownDirectorsAsync(IReadOnlyList<string> names, CancellationToken)` -> the stored spelling of
  every name that already exists in `directors` (matched by collation, never `ToLower`), used after filling (§5.3).
  Read-only, no tracking, one query. Characteristics test against MariaDB (case and accent variants).
- `Program.cs`: the Serilog override of §13 (`System.Net.Http.HttpClient` -> Warning).

Registration: `AddMoviesServices(this IServiceCollection services, IConfiguration configuration)` (add the parameter, as
`AddAuthServices(builder.Configuration)` already does; the host's single call site in `Program.cs` passes
`builder.Configuration`) binds `Tmdb`, validates it, registers the typed `HttpClient`
(`AddHttpClient<IMovieLookup, TmdbMovieLookup>`: `BaseAddress`, `Accept: application/json`,
`User-Agent: MovieDB/<version>` where `<version>` is the `AssemblyInformationalVersion` of the Movies assembly without
the `+revision` suffix, timeout from `TimeoutSeconds`, **no** retry/resilience handler). The typed client is transient;
inject `IMovieLookup` into the dialog (short-lived) and into `MovieEditPage` (only for `IsConfigured`, never for calls).
The `Authorization: Bearer` header is set on each request **only when a token is configured**. When no token is
configured, register the same type; `IsConfigured` is `false` (`!string.IsNullOrWhiteSpace(token)`) and both methods
return `ErrorKind.NotConfigured` without any network access. The health checks do **not** include TMDB.

HTTP happens **server-side** (Blazor Server): the browser never talks to the TMDB API, so `connect-src 'self' ws: wss:`
stays as it is today. The only browser-side TMDB contact is the poster thumbnails (§7.5, `img-src`). The token never
reaches the browser, logs, test output or the repository.

`MovieEditPage` gets: the button, the dialog call, the overwrite confirmation, the persistent source link row, and
`ApplyLookupAsync` which uses `LookupFill`, `FindKnownDirectorsAsync`, then reuses the existing machinery (dirty tracking
via `SameAs`, `CheckDuplicateAsync`, `_errors` clearing). Keep the page's code-behind growth small; put logic in
`LookupFill`.

---

## 5. Behavior

### 5.1 Search input

The dialog's search field is pre-filled with the Filmtitel already typed (if any; a non-empty pre-fill of at least 2
characters is searched immediately on open) and searches automatically after a debounce of 400 ms (min. 2 characters)
and on Enter or the search button. A new search **cancels** the previous request (`CancellationToken`). Only the first
results page is used; show at most 10 hits in TMDB's relevance order. The field has `MaxLength` 200.

`LookupInputParser.Parse(string? text, int currentYear)` -> `Result<LookupQuery>`:

1. Trim; collapse nothing else. Fewer than 2 characters -> `Validation` error (UI: hint under the field, no request).
   More than 200 characters -> `Validation` error.
2. A trailing `(yyyy)` or ` yyyy` (4 digits, a space or parenthesis before it) with `MovieInput.MinYear <= yyyy <=
   currentYear + MovieInput.FutureYears` is split off as `Year`, **but only when a non-empty title remains** (input
   that is only a year stays a title). `currentYear` comes from `UserClock`, never from the browser.
3. Everything else is the title. Links and IMDb ids are not interpreted: such text is just searched as a title.

Known edge: a real title that ends in a plausible year ("Mission Impossible 2000") would be searched as title +
`year` filter. If the first search with a split year returns **no hits**, do not retry in the first build; the owner
judges this together with the fuzzy-search quality (§2.2, §13).

Security: the typed text is only ever a URL-encoded `query` parameter of the fixed TMDB base URL (use
`Uri.EscapeDataString`, never string-concatenate unescaped input); the application never requests a user-supplied URL.
Never echo unescaped input (Blazor encodes by default; keep it that way).

### 5.2 Hits

Each row shows: **original title (year)**, the English title in a muted style when it differs, a one-line shortened
overview (optional), a **link "Bekijk op TMDB"** (`target="_blank" rel="noopener noreferrer"`, `aria-label` with the
title) and a "Kies" button. The whole row is also selectable with the keyboard. Hits without a release date show "jaar
onbekend". Clicking the TMDB link must **not** also choose the row.

### 5.3 Fetching and filling

The dialog is modal and does the details call itself (decision 12, §14.2 Q1). Choosing a hit: the list is disabled and
the dialog shows "Gegevens ophalen…" (`role="status"`); `GetDetailsAsync` runs with a token that is cancelled when the
dialog closes. On **success** the dialog closes and returns the `LookupDetails`. On **failure** the dialog stays open,
shows the Dutch message of §8 and an "Opnieuw proberen" action; the hit list stays visible so the owner can pick another
film; the form behind it is untouched. Because the dialog is modal, the owner cannot edit the form while the call runs.

Then `MovieEditPage.ApplyLookupAsync(details)`:

1. If the form already holds input besides the Filmtitel (§7.3 defines this), show the overwrite confirmation; "Annuleren"
   stops here and changes nothing (the source row is not updated either).
2. `notices = LookupFill.Apply(_model, details, Clock)`.
3. Resolve the directors against the database with `Queries.FindKnownDirectorsAsync`; replace each filled name by its
   stored spelling when one exists (§14.2 Q3) and add the stored names to `_directorNames` so the "nieuw" marker
   (`IsKnownDirector`) is correct. A failure of this query is logged (Warning) and ignored: saving matches by collation
   anyway.
4. Clear `_errors` for Title, Genres, RuntimeMinutes, ReleaseYear, Directors (and the form-level `_showSummary`);
   do **not** show "... is verplicht" for fields that stayed empty (those appear on blur/save as usual). Then call
   `CheckDuplicateAsync(Commands.Validate(_model.Normalized()))`, which sets `_duplicateId` and the duplicate message as
   today (it skips itself while Title/Year/Directors are invalid or empty).
5. Store the source (`SourceUrl`, `ImdbUrl`, title) for the persistent source row, show the success alert with the
   notices (§7.3), assign a **new list instance** to `_model.Genres`/`_model.Directors` (the chip components bind by
   reference), `StateHasChanged`, then focus the first still-empty field in form order (Filmtitel, Genre, Duur, Jaar,
   Regisseur), or the Datum field when all five are filled.

`LookupFill.Apply` rules (provider-neutral; input is `LookupDetails`, which `TmdbMovieLookup` already cleaned):

- **Title:** `details.Title` trimmed, cut to `MovieInput.TitleMaxLength` (200); empty -> leave empty (cannot normally
  happen, the provider falls back to `title`).
- **Year:** `details.Year` when `MovieInput.MinYear <= year <= clock.CurrentYear + MovieInput.FutureYears`; null or out
  of range -> leave empty and add the notice "Releasedatum ontbreekt; vul het jaar zelf in."
- **Runtime:** `details.RuntimeMinutes` when `MinRuntimeMinutes..MaxRuntimeMinutes` (1-999); null, `0` or out of range ->
  leave empty and add the "Duur ontbreekt..." notice.
- **Genres:** `details.Genres` de-duplicated (ordinal ignore case), first `MovieInput.MaxGenres` (4). Empty -> leave
  empty and add the "Geen passend genre..." notice. (More than 4 mapped genres: silently keep the first 4, as decided in §11.)
- **Directors:** `details.Directors` with `,` replaced by a space and whitespace collapsed, trimmed, non-empty,
  de-duplicated by name (ordinal ignore case, **after** the comma replacement), each cut at
  `MovieInput.DirectorMaxLength` (150), first `MovieInput.MaxDirectors` (5). If more than 5 distinct directors were
  provided, add the "Meer dan 5 regisseurs..." notice. None -> leave empty and add the "Geen regisseur..." notice
  (Dutch text: "Regisseur ontbreekt bij TMDB; vul die zelf in.").
- A value that cannot be filled leaves the field **empty** (not the previous value), so the owner sees what is missing.
- The method touches only these five properties of `target` and never WatchedOn, Medium, scores or Review.

Directors are finally matched to stored spelling at save time by collation (PLAN-2 §4.2); step 3 above only makes the
form show the same spelling and the right "nieuw" marker beforehand.

---

## 6. Genre mapping (TMDB -> the 35 closed genres)

TMDB movie genres are a fixed list of 19 (ids below). The map lives in `TmdbGenreMap` as a single constant dictionary
keyed by **TMDB id** (never by the localized name). A unit test asserts that every mapped name exists in
`MovieGenres.All`. Mapping (confirmed by the owner, §11):

| TMDB id | TMDB name | App genre |
|---|---|---|
| 28 | Action | Actie |
| 12 | Adventure | Avontuur |
| 16 | Animation | Animatie |
| 35 | Comedy | Komedie |
| 80 | Crime | Misdaad |
| 99 | Documentary | Documentaire |
| 18 | Drama | Drama |
| 10751 | Family | Familie |
| 14 | Fantasy | Fantasy |
| 36 | History | Historisch |
| 27 | Horror | Horror |
| 10402 | Music | Musical |
| 9648 | Mystery | Mystery |
| 10749 | Romance | Romantisch |
| 878 | Science Fiction | Sci-fi |
| 10770 | TV Movie | (not mapped) |
| 53 | Thriller | Thriller |
| 10752 | War | Oorlogs |
| 37 | Western | Western |

Rules: keep TMDB's order (the first genre is the primary one), drop duplicates and unmapped ids, take the first 4. If
nothing maps, leave Genre empty and add a notice (owner decision, §11). The genres the app has but TMDB lacks (Bond, Film noir,
Kortfilm, Superhelden, Roadmovie, ...) are never suggested automatically; the owner adds them by hand.

---

## 7. UI (Dutch)

### 7.1 Button
On `/films/nieuw`, in the "Gegevens" card above Filmtitel: an outlined button "Zoek film online"
(`Icons.Material.Filled.Search`, `data-testid="lookup-open"`). Disabled with a tooltip "Online zoeken is niet ingesteld."
when `IMovieLookup.IsConfigured` is false. Not shown on existing films.

### 7.2 Dialog "Film online zoeken"
Full-screen on mobile (390 px), a medium dialog on desktop (1440 px), pattern like `SameTitleDialog`:

- search field (label "Titel", hint "Typ de (Engelse of originele) titel, eventueel met jaartal"), search button, clear;
- a status area (`role="status"`, `aria-live="polite"`) with a skeleton while searching;
- the hit list (§5.2) and, below, a short line "Controleer via de link of dit de juiste film is.";
- the footer with the TMDB logo and the attribution notice (§7.4);
- buttons: "Annuleren".

Keyboard: arrow keys/Tab through hits, Enter chooses, Esc closes. All text Dutch; dates `dd/MM/yyyy` where relevant.

### 7.3 After choosing
- A **persistent source row** in the "Gegevens" card, below the lookup button, once a film has been filled in:
  "Ingevuld vanuit: <film title> · TMDB ↗ · IMDb ↗" (the IMDb link only when `ImdbUrl` is known; links open in a new tab
  with `rel="noopener noreferrer"`, `data-testid="lookup-source"`). It stays until the page is left; it is not stored
  in the database. A second successful lookup replaces it; a cancelled overwrite does not. `<film title>` is the
  looked-up (original) title, not whatever the owner edits afterwards.
- A success `MudAlert` (dismissible, `role="status"`) above the form: "Ingevuld vanuit TMDB: titel, genre(s), duur, jaar,
  regisseur(s). Controleer de gegevens." plus one bullet per notice. The notice texts are fixed constants in
  `LookupMessages`:
  - "Duur ontbreekt bij TMDB; vul die zelf in."
  - "Meer dan 5 regisseurs; enkel de eerste 5 zijn overgenomen."
  - "Regisseur ontbreekt bij TMDB; vul die zelf in."
  - "Geen passend genre gevonden; kies zelf een genre."
  - "Releasedatum ontbreekt; vul het jaar zelf in."
  The alert is replaced by a new lookup and removed when the owner dismisses it.
- **"The form holds input"** (the trigger of the overwrite confirmation) is exactly: `Genres.Count > 0`, **or**
  `RuntimeMinutes != null`, **or** `ReleaseYear != null`, **or** `Directors.Count > 0`. The **Filmtitel alone never
  triggers the prompt** (owner decision, §14.2 Q2): it seeds the search and is replaced by the looked-up title without
  asking. (Datum, Medium, scores and Review are not considered.) Put this check in a small pure, unit-tested helper
  (e.g. `LookupFill.HasInputBesidesTitle(MovieInput)`).
- Overwrite confirmation (`IDialogService.ShowMessageBoxAsync`, like the existing unsaved-changes prompt): "Bestaande
  gegevens overschrijven? De velden Filmtitel, Genre, Duur, Jaar en Regisseur(s) worden vervangen." (the text still
  names all five fields) with "Overschrijven" /
  "Annuleren". Cancel leaves the form untouched.
- The unsaved-changes guard, Ctrl+S and conflict logic keep working unchanged.

### 7.4 Attribution (required by the TMDB terms)
- Self-host the approved TMDB logo so it is served from this origin (the logo needs no CSP origin; see §7.5 for the one
  origin added later for posters). The owner chose **"Alt short (blue)"**; the implementer (M12) downloads it, **unmodified**:
  - Source page: <https://www.themoviedb.org/about/logos-attribution> (it sits behind a browser check, so open the
    page via a browser-like client; the file itself downloads fine with
    `curl.exe -sL -A "Mozilla/5.0" <url> -o <target>`).
  - File URL (verified 2026-11-05, `image/svg+xml`, about 2 KB, viewBox `0 0 273.42 35.52`):
    `https://www.themoviedb.org/assets/v4/logos/v2/blue_short-8e7b30f73a4020692ccca9c88bafe5dcb6f8a62a4c6bc55cd9ba82bb2cd95f6c.svg`
    (the hash in the file name can change; if it 404s, take the current "Alt short (blue) - SVG" link from the source
    page).
  - Target: `src/MovieDB.Movies/wwwroot/images/tmdb-logo.svg` (next to the existing `wwwroot/movies.css`; served as
    `_content/MovieDB.Movies/images/tmdb-logo.svg`, same mechanism as `movies.css`; the Web project has no images
    folder). Reference it with that same-origin path; never the themoviedb.org URL.
  - Next to it, `ATTRIBUTION.txt` with the source page, the file URL, the download date and "unmodified".
  - Verify after the build: the file is present, starts with `<svg`, and `GET /_content/MovieDB.Movies/images/tmdb-logo.svg`
    returns `200 image/svg+xml` (add this to the characteristics tests); the CSP header is unchanged.
  - Not downloaded yet; this is part of M12. If the agent cannot download it, stop and ask the owner to place the
    file; do not draw a substitute logo.
- Notice, exact English text: "This application uses TMDB and the TMDB APIs but is not endorsed, certified, or otherwise
  approved by TMDB." Implemented once in the public Razor component `TmdbAttribution.razor` (Movies; logo `<img>` with
  `alt="TMDB"`, the sentence in an element with `lang="en"`, small and muted, the logo less prominent than the app's own
  title), used in two places only (owner decision, §14.2 Q4): the dialog footer, and the bottom of the page content of
  `MovieEditPage` **when `IsNew`** (below the form, clear of the sticky save bar; shown whether or not the feature is
  configured, since the button is visible there). `MainLayout.razor` and the other pages (overview, edit, login) are
  **not** changed. Keep it a plain, non-interactive component. Check 390 px and 1440 px. Add
  `data-testid="tmdb-attribution"` (the page and the dialog each have one instance).
- Do not store provider data beyond what the owner saves in the film record.

### 7.5 Poster thumbnails (extension after M12, owner request)
- Each search hit in the dialog shows a 46 × 69 px poster thumbnail on the left; hits without a poster show a neutral
  placeholder icon so the rows keep their height. The poster is decorative (`alt=""`; the title is next to it).
- `LookupHit.PosterUrl` is built **server-side** from TMDB's `poster_path`, only when that is a plain file name
  (`^/[A-Za-z0-9_-]{1,64}\.(jpg|png)$`); anything else (a full URL, a path, a query) means "no poster". The host and size
  are fixed: `https://image.tmdb.org/t/p/w92` (`TmdbMovieLookup.ImageOrigin`), never taken from the response.
- **CSP:** the only change is `img-src 'self' data: https://image.tmdb.org` (the image host only, no wildcard).
  `connect-src`, `script-src` and the other directives are unchanged. This is the only place where the browser contacts
  a TMDB host directly (the poster requests do not carry the token).
- The `<img>` uses `loading="lazy"`, `decoding="async"` and `referrerpolicy="no-referrer"` (no origin sent to TMDB).
- Posters are not stored, not copied to the server and not shown on the form; only the search dialog shows them. No
  caching beyond the browser's own.
- Tests: unit (URL construction and rejected paths; dialog renders the poster and the placeholder), characteristics (the
  exact CSP directive). Browser tests keep fake hits without posters so they never need the network.

---

## 8. Errors (all friendly, Dutch, no stack traces, no technical detail in the UI)

Use `Result<T>`/`Error`/`ErrorKind` (Common). The existing kinds do not cover the lookup failures, so §4 adds
`NotConfigured`, `Unavailable`, `RateLimited` and `InvalidResponse`. `Error.Message` stays a short **English** technical
string (never shown); the UI maps `ErrorKind` to Dutch through `LookupMessages`. Details go to `ILogger` (Warning/Error,
structured, **never** the token, the typed text beyond its length, or the film data).

Mapping used by `TmdbMovieLookup`:

| Situation | `ErrorKind` |
|---|---|
| No token configured | `NotConfigured` |
| Search with no hits | success, empty list |
| Details `404` | `NotFound` |
| `401` / `403` | `Misconfigured` (logged at Error: configuration problem; shown like `Unavailable`) |
| `429` | `RateLimited` |
| Timeout, `HttpRequestException`, `5xx` | `Unavailable` |
| Malformed/unexpected JSON, missing mandatory fields (`id` on a hit; `id` on details) | `InvalidResponse` |
| Other unexpected `4xx` | `Unavailable` |
| Caller cancelled | not an `Error`: let `OperationCanceledException` propagate to the caller, which ignores it |

| Situation | User message (Dutch) | Log |
|---|---|---|
| Not configured | "Online zoeken is niet ingesteld." | none |
| Text shorter than 2 characters | (no search; hint under the field) | none |
| No results | "Geen films gevonden voor "{tekst}". Controleer de spelling of probeer de originele titel." | Debug |
| Details `404` | "Deze film bestaat niet (meer) bij TMDB. Kies een andere." | Warning |
| `401`/`403` (bad or revoked token) | "Online zoeken is momenteel niet beschikbaar." | **Error** (configuration problem) |
| `429` | "Te veel aanvragen. Probeer het over een minuut opnieuw." | Warning |
| Timeout / network / `5xx` | "De filmdatabase is momenteel niet bereikbaar. Probeer het later opnieuw." | Warning |
| Invalid/unexpected JSON | "De gegevens konden niet worden gelezen. Probeer het later opnieuw." | Error |

The same message table applies to a failed **search** and a failed **details** call, except that a failed details call
(any kind other than `NotFound`/`NotConfigured`) additionally shows "De gegevens van deze film konden niet worden
opgehaald." and the "Opnieuw proberen" action (§5.3). `NotFound` on details shows its own message and the owner picks
another hit. Messages are rendered with an `aria-live` region, never as raw exception text.

Rules: never throw into the UI; catch `HttpRequestException`, `TaskCanceledException` (distinguish timeout — the
`HttpClient` timeout, token not cancelled — from the caller's cancellation, which is silent), `JsonException`
specifically, not a broad catch. A failed search keeps the dialog open and the previous text. Partial data is a
**success with notices**, not an error. `LookupMessages` is unit-tested: every `ErrorKind` that the provider can return
has a non-empty Dutch message, none contains the technical `Error.Message`.

---

## 9. Configuration, secrets, deployment

- Options section `Tmdb` (`TmdbOptions`, §4), bound from normal .NET configuration. `appsettings.json` (tracked) gets a
  `Tmdb` section with an **empty** `ApiReadAccessToken` (placeholder; empty = search disabled) and the defaults for
  `Language` / `BaseUrl` / `TimeoutSeconds`. The token is a secret: **never commit a real token**, and never put it in
  `appsettings.Development.json` either (that file is **tracked** in git and `appsettings.Local.json` is not loaded).
  Locally use user-secrets only
  (`dotnet user-secrets set "Tmdb:ApiReadAccessToken" "<token>" --project src/MovieDB.Web`, UserSecretsId
  `9b46a74e-075e-4f32-b29a-dc9bd38c1cec`) or the environment variable `Tmdb__ApiReadAccessToken`; in the container use
  the environment variable. Never in logs, commits, tests, or the HANDOVER.
- `docker-compose/moviedb-web/docker-compose.yml`: add `- Tmdb__ApiReadAccessToken=${MOVIEDB_TMDB_TOKEN:-}` to the
  environment list (optional, so the stack starts without it). README: new row in the environment table (near the other
  `MOVIEDB_*` variables), the account/token steps below, the privacy note (§13) and the attribution requirement. Outbound
  HTTPS from the container to `api.themoviedb.org:443` must be allowed (the container
  already reaches the Docker network; no inbound change; Nginx Proxy Manager is unaffected).
- **Owner action to get a token** (the agent cannot do this): create a free account at themoviedb.org, open
  Settings > API, request an API key as type **Developer** (personal, non-commercial; the form asks for an application
  name, a URL and a short description: "personal film log"), and copy the **API Read Access Token**.
- Attribution (§7.4) ships with the feature. No version bump decision here; bump `version.txt` per the owner's release
  habits when the milestone is accepted.

---

## 10. Testing

Real behavior at real boundaries; **no live calls to TMDB in any automated test** (flaky, needs a secret, and TMDB
content must not become test data). Use synthetic JSON fixtures written for the tests.

- **Unit** (`MovieDB.UnitTests`):
  - `LookupInputParser`: plain text, trimming, trailing `(yyyy)` / ` yyyy` split (boundaries 1887/1888, current year + 5/+6,
    year only input stays a title), too short, over-length text, pasted URLs treated as plain text (never fetched).
  - `TmdbMovieLookup` with a fake `HttpMessageHandler`: success; empty results; missing/empty `release_date` (-> `Year` null);
    `runtime` 0/null (-> raw value kept); no directors; duplicate directors by person id; non-`Director` crew ignored;
    unmapped-only genres; `401`, `403`, `404`, `429`, `500`, timeout (fake delay + short timeout), caller cancellation
    (propagates, not an `Error`), malformed JSON, missing mandatory fields; request shape (bearer header present, `api_key`
    absent from the URL, `User-Agent` without `+revision`, `Uri.EscapeDataString`-encoded `query`, `year` only when
    given, `language=en-US`); not configured -> `NotConfigured` and **no request sent**; the token never appears in
    logs (capture a fake `ILogger`); `original_title` used, falling back to `title`; IMDb link only for a valid `tt`
    id; hits limited to 10.
  - `TmdbGenreMap`: all 19 TMDB ids covered or deliberately unmapped; every mapped name is in `MovieGenres.All`; order
    kept; no duplicates.
  - `LookupFill`: each rule and notice of §5.3 (pure function tests): caps (4 genres, 5 directors, lengths 200/150),
    comma replacement and name de-duplication, year/runtime range boundaries (1887/1888, `CurrentYear + 5/+6`, 0/1/999/1000),
    empty leaves the field empty, WatchedOn/Medium/scores/Review untouched, `HasInputBesidesTitle` (title only -> false;
    each other field alone -> true), and the filled input passes `MovieInputValidator`.
  - `Error` factories and the new `ErrorKind`s (`ResultTests`); `MovieInput` runtime constants still produce the same
    validator message and boundaries; `LookupMessages` (every provider `ErrorKind` has a Dutch message).
  - `TmdbOptions` validation (bad `BaseUrl` such as http or a relative URL, `TimeoutSeconds` out of range, empty token ok).
  - bUnit `MovieLookupDialog` (fake `IMovieLookup`): typing -> debounce -> hits with `href`, `target="_blank"`,
    `rel="noopener noreferrer"`; no results message; error messages; pick -> details call -> dialog closes with the
    details; details failure keeps the dialog open with "Opnieuw proberen" and the list; Esc/cancel; stale search
    cancelled; footer attribution. `TmdbAttribution` renders the exact notice with `lang="en"` and the logo.
- **Characteristics** (`MovieDB.CharacteristicsTests`): host with a **stub `IMovieLookup`** or a stubbed
  `HttpMessageHandler` replacing the TMDB client: the app **starts without any `Tmdb` token** and the new-film page
  renders the button enabled/disabled depending on configuration; the token set through configuration/environment variable
  enables it; `Tmdb` options validation at startup with an invalid `BaseUrl` fails; unauthenticated users cannot reach
  anything new (the fallback policy already covers it, assert it); the CSP header only differs by the poster image host (§7.5); the attribution notice is
  present on `/films/nieuw` and absent on `/films/{id}`, the overview and `/login`; `MovieQueries.FindKnownDirectorsAsync` against real MariaDB (case and accent
  variants return the stored spelling, unknown names are absent, empty input returns empty).
- **Browser** (`MovieDB.BrowserTests`, Playwright, 390 and 1440 px) with a **fake lookup** registered in the test host
  (`BrowserFixture` creates `WebApplicationFactory<Program>`: use `WithWebHostBuilder(b => b.ConfigureTestServices(...))`,
  `RemoveAll<IMovieLookup>()` and register the fake; the fake must report `IsConfigured == true`):
  open the dialog from "Nieuwe film", search, see hits with links (assert `href` to the TMDB page), pick one, the five
  fields are filled, the notice alert and the persistent source link row show (assert `href`), a looked-up director that
  already exists in the database has **no** "nieuw" marker (and uses the stored spelling), the form is dirty, the
  duplicate hint appears when the film exists, save
  works after entering the remaining required fields; the overwrite confirmation (appears when e.g. a Duur is already filled; does **not** appear when only the Filmtitel
  was typed; cancel keeps data, confirm replaces);
  failure cases (no results, service down, details fail then "Opnieuw proberen" succeeds) show the Dutch messages and
  keep the form usable;
  the button is absent on `/films/{id}`; the dialog is usable on a 390 px screen (no horizontal scroll) and with the
  keyboard.
- **Architecture:** existing rules stay green (no new project, Movies still references only Common/Ui/Infrastructure).
  Public-surface test unchanged (all new types are `internal`; the two new Razor components are public, which the test
  allows because it skips `IComponent`).
- Formatting: CSharpier, LF endings, XML docs complete on every new member (check with the
  `-p:GenerateDocumentationFile=true` build from AGENTS.md).

---

## 11. Owner answers (all questions closed)

1. **Provider:** TMDB. The owner creates a free "Developer" API key (§9). The key must be configurable through
   appsettings configuration, and through environment variables for the Docker container.
2. **Title language:** the **original title** is filled in. The search is done **in English** (`en-US`).
3. **Genres:** the mapping in §6 is right (Family -> Familie, Music -> Musical, TV Movie ignored). When no genre maps,
   leave Genre empty with a notice; more than 4: keep the first 4.
4. **Scope:** only on the new-film page. If the five fields already hold data: overwrite after a confirmation.
5. **Posters:** no at first (the link to the source page is enough). **Changed after M12 (owner request):** a small
   poster thumbnail is shown next to each search hit; see §7.5.
6. **Missing token:** the app **starts normally**; the search is optional and the button is disabled.
7. **Link input:** **not** supported (no pasting of TMDB/IMDb links or IMDb ids). Instead, keep a **link to the selected
   movie** on the form after filling it in (§7.3).

The owner will test the fuzzy search himself once it is implemented (§2.2, §13).

---

## 12. Milestones

One milestone at a time, with the owner's review in between (HANDOVER working agreement). Do not commit unless asked.

**M11: provider client (no UI).**
`ErrorKind`/`Error` additions (Common), `MovieInput` runtime constants, `MovieQueries.FindKnownDirectorsAsync`,
`TmdbOptions`, `IMovieLookup` + models (`LookupQuery`, `LookupHit`, `LookupDetails`), `LookupInputParser`,
`TmdbMovieLookup`, `TmdbGenreMap`, `LookupFill`, `LookupMessages`, `AddMoviesServices(IConfiguration)` registration +
the `Program.cs` call site, the Serilog `System.Net.Http.HttpClient` override, `appsettings.json` section,
compose + README config, all unit and characteristics tests of §10 that do not involve the dialog.
Real-token verification of §2.2: ask the owner for the token (environment variable `Tmdb__ApiReadAccessToken` or
user-secrets, never in chat or files). Use a **temporary, uncommitted** live harness (a throw-away test or console
snippet) that reads the token from configuration, prints neither the token nor request headers, and is **deleted
afterwards**. Record the findings (not the token) in HANDOVER.md.
*Done when:* the unit and characteristics tests pass, the live check returned hits and details for a known film (or the
owner explicitly defers it), the build is clean with `-warnaserror`, formatting is clean, XML docs complete.

**M12: UI and attribution.**
`MovieLookupDialog`, `TmdbAttribution.razor` + the self-hosted logo download (§7.4) + new-film page placement, the button, overwrite confirmation,
`ApplyLookupAsync`, source row, notices, error messages, CSS in `movies.css`, bUnit + characteristics + browser tests at
390 and 1440 px.
*Done when:* all §10 UI tests pass and the owner has tried it against the real service.

**M13: docs and wrap-up.**
README (token steps, env var, privacy note, attribution), AGENTS.md (stack, config list, the "external service"
convention: server-side only, token via secrets, no caching, TMDB terms), PLAN.md status line, HANDOVER.md, full
regression (`dotnet test --solution src/MovieDB.slnx`), final diff check. (Attribution is **not** deferred to this
milestone: it ships with M12, so the feature is never usable without it.)
*Done when:* the full suite is green and the owner accepts.

---

## 13. Risks and notes for the implementer

- **Search quality** is the main unknown (§2.2). Decide on re-ranking only after trying real queries.
- **TMDB `release_date`** is the earliest/primary release (it can be a festival date), so the year can differ by one from
  the year a Belgian cinema release would suggest. The owner verifies through the link; do not try to be clever.
- **Localized titles** are not used: the original title is filled in (`original_title`). It can be non-Latin (Japanese,
  Korean, ...); that is fine, the database is `utf8mb4`. Handle empty strings.
- **Blazor Server:** guard against updating the UI after the page/circuit is gone (cancel tokens tied to
  `IAsyncDisposable`), and do not `await` HTTP while holding UI locks. Do not retain `HttpClient` results in the circuit
  beyond the dialog's lifetime.
- **Privacy:** the typed search text and the chosen TMDB id go to TMDB; reviews, scores and the film database never do. State this in
  the README.
- **Do not log** the token, request headers or response bodies. HTTP client logging at `Information` includes the full URL
  (including the title the owner typed). `Program.cs` has no override for it yet: in M11 add
  `.MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)` next to the existing overrides in the
  Serilog configuration (development minimum level is Debug, so the override matters there too).
- **Titles ending in a year-like number** (e.g. "Mission Impossible 2000") are parsed as title + year filter (§5.1); a
  title ending in an out-of-range number (e.g. "Blade Runner 2049") stays whole. Accepted for the first build.
- **Dialog-owned details call** (§5.3): the page never calls `GetDetailsAsync`; it only reads `IsConfigured`.
- **TMDB may change or end the API**; the interface boundary keeps that contained.
- Keep names stable: route `/films/nieuw`, term "Vertoning", existing `MovieInput` validation messages. Do not change the
  validator to make imported data pass; fix the mapping instead.


---

## 14. Gap review (2026-11-05)

### 14.1 What the review changed

- **Contradiction fixed:** §5.3 closed the dialog on a pick while §8 promised "Opnieuw proberen"/"Kies een andere". The dialog now owns the details call (decision 12).
- **Types made concrete (§4):** the undefined `LookupId` became a plain `int`; added `LookupQuery`, `LookupMessages`, `TmdbAttribution`; split TMDB-specific cleaning (`TmdbMovieLookup`) from app rules (`LookupFill`); dropped `UnmappedGenreCount`/`DirectorsTruncated` (notices come from `LookupFill`).
- **`ErrorKind` (§8):** the existing four kinds could not express the failures; four are added with an explicit mapping table.
- **Config (§9):** `appsettings.Development.json` is tracked in git, so user-secrets / environment variable only.
- **Attribution:** the logo location (Movies `wwwroot`), the placement (dialog + `/films/nieuw` only, no layout change) and the milestone (M12, not M13) are explicit.
- **Director "nieuw" marker (§5.3):** after a fill, existing directors would wrongly show "nieuw"; new `FindKnownDirectorsAsync`.
- **Parser, "holds input", after-fill steps, runtime constants, DI lifetime, version for `User-Agent`, Serilog override timing, test-fake injection** are specified.

### 14.2 Owner answers to the review questions (2026-11-05, all closed)

| # | Question | Answer |
|---|---|---|
| Q1 | The dialog fetches the details itself and only closes on success (§5.3), instead of closing first and fetching on the page. OK? | **Yes** |
| Q2 | Skip the overwrite prompt when **only** the Filmtitel is filled? | **Yes** (§3 #14, §7.3) |
| Q3 | Show the **stored** spelling when a looked-up director matches an existing one by collation? | **Yes** |
| Q4 | Attribution footer on all pages? | **No**: dialog + `/films/nieuw` only (§7.4) |
| Q5 | M11 live verification with a temporary, uncommitted, deleted-afterwards check; the owner supplies the token via user-secrets. | **Yes.** The key `Tmdb:ApiReadAccessToken` already exists in the owner's user-secrets with an empty value (added 2026-11-05); the owner only fills in the value. The agent never reads it into output. |

Q1: **Yes** (dialog owns the details call). All five questions are answered; no open questions remain.

(§3 #7 "Overwrite" is refined by #14: the Filmtitel itself does not count as existing input.)
