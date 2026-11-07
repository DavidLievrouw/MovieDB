# MovieDB

Persoonlijke filmdatabase ("Films"): een logboek van bekeken films met genre, duur, jaar, regisseur, datum, medium,
vijf scores en een recensie. Vervangt de Access-database `FilmsBase.mdb`.

Stack: .NET 10, Blazor Web App, MudBlazor, EF Core, MariaDB 12.3.

See [`PLAN.md`](PLAN.md) (phase 1 product spec), [`PLAN-2.md`](PLAN-2.md) (phase 2: several genres and directors per
film, genre filter, no Extra's; it wins where the two differ) and [`AGENTS.md`](AGENTS.md) for the conventions.

A film has 1-4 genres from a closed list of 35 and 1-5 directors (new names can be typed). It is unique on title, year
and the set of directors, in any order. The five scores are required.

## Local development

Prerequisites: .NET SDK 10 (see `global.json`), PowerShell 7, Docker with the MariaDB 12.3.3 container on
`localhost:3306` (database and user `moviedb`).

The connection string is stored in user-secrets (`ConnectionStrings:MovieDb`, `UserSecretsId` in
`src/MovieDB.Web/MovieDB.Web.csproj`). Never commit it.

```powershell
dotnet tool restore
dotnet build src/MovieDB.slnx -warnaserror
dotnet csharpier format .
dotnet test --solution src/MovieDB.slnx
```

Characteristics and browser tests require Docker to be running. The browser tests also need Chromium, installed once
after a build:

```powershell
dotnet build src/tests/MovieDB.BrowserTests
pwsh src/tests/MovieDB.BrowserTests/bin/Debug/net10.0/playwright.ps1 install chromium
dotnet test --project src/tests/MovieDB.BrowserTests
```

### Benchmarks

`src/tests/MovieDB.Benchmarks` measures the read/write paths against a throwaway MariaDB container (Docker must be running) with
synthetic data of about today's size and ten times that. It is an executable, not part of `dotnet test`. Results go to the
git-ignored `artifacts/benchmarks/<name>`.

```powershell
# timings and allocations (add --job short for a quick run)
dotnet run -c Release --project src/tests/MovieDB.Benchmarks -- --filter "*" --artifacts artifacts/benchmarks/<name>
# database round trips per operation
dotnet run -c Release --project src/tests/MovieDB.Benchmarks -- --roundtrips artifacts/benchmarks/<name>
```

## Online film search (TMDB)

The new-film form can look a film up online and fill in title, genres, runtime, year and directors. It is optional: without
a token the feature is switched off and the rest of the app is unaffected.

1. Create a free account at <https://www.themoviedb.org>, then go to Settings → API and request an API key (personal use).
2. Copy the **API Read Access Token** (not the short "API Key").
3. Provide it as `Tmdb:ApiReadAccessToken`: in development with `dotnet user-secrets set "Tmdb:ApiReadAccessToken" "<token>"
   --project src/MovieDB.Web`; in deployment as `MOVIEDB_TMDB_TOKEN` (compose maps it to `Tmdb__ApiReadAccessToken`).
   Never commit it.

Privacy: only the text you search for is sent to TMDB, from the server. Your reviews, scores and other data are never
sent, and the browser never talks to TMDB. No TMDB text or images are stored beyond the values you save in the form.

This product uses the TMDB API but is not endorsed or certified by TMDB.

## Importing the Access data

The one-time import loads the git-ignored `FilmsBase.mdb` (repository root) into the **local** database and writes a
guarded data dump for production (see PLAN.md §6). It needs Windows, the 64-bit Access ODBC driver and the local
MariaDB container.

```powershell
.\scripts\Import-Access.ps1 -Source .\FilmsBase.mdb -DryRun   # counts only, no database changes
.\scripts\Import-Access.ps1 -Source .\FilmsBase.mdb           # migrate, import, report, dump, verify the dump
.\scripts\Import-Access.ps1 -Source .\FilmsBase.mdb -Reset    # clear the five data tables first (asks for RESET)
```

The dump lands in `artifacts\import\moviedb-data.sql`. It holds the data of `media`, `directors`, `movies`,
`movie_genres` and `movie_directors` (the 35 genres come from the migration) and its guard refuses a database that is not
at the expected schema version or whose data tables are not empty. It contains personal reviews: never commit it and
delete all copies after deploying it. The script never prints rows or review text.

## Deployment

The app runs as the container `moviedb` (image `moviedb-web:<version>`, port 8080) on the Docker host that also runs the
shared MariaDB and Nginx Proxy Manager. The public URL is `https://mdb.dalion.eu` (HTTPS only). Deployment is manual.

### Build the image

The version comes from `version.txt`. The build context is the repository root.

```powershell
$version = (Get-Content version.txt).Trim()
docker build -f src/MovieDB.Web/Dockerfile -t moviedb-web:$version .
```

Copy the image to the server (`docker save` / `docker load`, or a registry).

### One-time server setup

1. **Database:** create the database `moviedb` (`utf8mb4`, `utf8mb4_uca1400_ai_ci`) and a user with privileges on
   `moviedb` only (the startup migrations need DDL) in the shared MariaDB 12.3.3 container.
2. **DataProtection certificate:** the key ring is encrypted with a PFX that lives on the server only.

   ```sh
   sudo mkdir -p /etc/dalion/moviedb/secrets
   sudo openssl req -x509 -newkey rsa:3072 -nodes -days 3650 -subj "/CN=moviedb-dataprotection" \
        -keyout /tmp/dp.key -out /tmp/dp.crt
   sudo openssl pkcs12 -export -inkey /tmp/dp.key -in /tmp/dp.crt \
        -out /etc/dalion/moviedb/secrets/dataprotection.pfx -passout pass:<certificate password>
   sudo shred -u /tmp/dp.key /tmp/dp.crt
   sudo chown 1654:1654 /etc/dalion/moviedb/secrets/dataprotection.pfx   # the container runs as $APP_UID
   sudo chmod 0400 /etc/dalion/moviedb/secrets/dataprotection.pfx
   ```

   Rotating the PFX invalidates auth cookies; the owner logs in again.
3. **Nginx Proxy Manager:** add a proxy host for `mdb.dalion.eu` that forwards to `moviedb:8080` with TLS, "Force SSL",
   HSTS and WebSocket support (Blazor Server needs WebSockets).

### Start the stack

`docker-compose/moviedb-web/docker-compose.yml` joins the external network `motorsports_net` and publishes no host
port. Set these values in the environment or the stack (never in the repository):

| Variable | Purpose |
|----------|---------|
| `MOVIEDB_CONNECTION_STRING` | e.g. `Server=mariadb;Port=3306;Database=moviedb;User=moviedb;Password=...;CharSet=utf8mb4` |
| `MOVIEDB_DEFAULT_PASSWORD` | initial password of the user `admin`, only used when the `users` table is empty |
| `MOVIEDB_TMDB_TOKEN` | optional TMDB "API Read Access Token"; without it the online film search is disabled (see [Online film search (TMDB)](#online-film-search-tmdb)) |
| `DATAPROTECTION_CERTIFICATE_PASSWORD` | password of the PFX |
| `MOVIEDB_VERSION` | image tag (default `1.0.0`) |
| `FORWARDED_HEADERS_KNOWN_PROXY_0` | Nginx Proxy Manager container IP (default `172.18.0.3`) |

```sh
docker compose -f docker-compose/moviedb-web/docker-compose.yml up -d
docker inspect moviedb --format '{{.State.Health.Status}}'
```

On first start the container applies the migrations and seeds the user `admin` with `MOVIEDB_DEFAULT_PASSWORD`. Log in
and change the password at `/account/wachtwoord`.

### First deployment: load the data

1. Locally, run `Import-Access.ps1` on a fresh local database (see above). It writes and verifies
   `artifacts\import\moviedb-data.sql`.
2. Deploy the container (above), so the schema exists.
3. Copy the dump to the server and into the MariaDB container, then load it. The guard block refuses to run on a wrong
   schema version or on non-empty tables.

   ```sh
   docker cp moviedb-data.sql mariadb:/tmp/moviedb-data.sql
   docker exec -it mariadb sh -c "mariadb -u root -p moviedb < /tmp/moviedb-data.sql"
   ```

4. Smoke-test: log in, check the film count, open a film with accented text.
5. Run `backup.ps1`. Delete every copy of the dump (local, server, container `/tmp`).

### Updating

Take a backup first (`backup.ps1`) when the new version contains migrations. Load the new image, set `MOVIEDB_VERSION`
and run `docker compose ... up -d`. Migrations run on startup under a database lock.

## Backup and restore

```powershell
.\backup.ps1            # asks for the MariaDB root password, or reads MARIADB_ROOT_PASSWORD
```

This writes `backup\moviedb-backup-YYYYMMDD-mariadb.sql` (git-ignored; it contains personal reviews, keep it private). The data of
`data_protection_keys` is left out on purpose; only its structure is dumped, so users log in again after a restore.

Restore:

1. Drop and recreate the database: `DROP DATABASE IF EXISTS moviedb; CREATE DATABASE moviedb CHARACTER SET utf8mb4
   COLLATE utf8mb4_uca1400_ai_ci;` (and make sure the `moviedb` user has privileges on it).
2. Load the dump:

   ```sh
   docker cp backup/moviedb-backup-YYYYMMDD-mariadb.sql mariadb:/tmp/restore.sql
   docker exec -it mariadb sh -c "mariadb -u root -p moviedb < /tmp/restore.sql"
   docker exec mariadb rm /tmp/restore.sql
   ```

3. Start or restart the `moviedb` container and log in.