# MovieDB

Persoonlijke filmdatabase ("Films"): een logboek van bekeken films met genre, duur, jaar, regisseur, datum, medium,
zes scores en een recensie. Vervangt de Access-database `FilmsBase.mdb`.

Stack: .NET 10, Blazor Web App, MudBlazor, EF Core, MariaDB 12.3.

See [`PLAN.md`](PLAN.md) for the product spec and milestones and [`AGENTS.md`](AGENTS.md) for the conventions.

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

Characteristics and browser tests require Docker to be running.

## Importing the Access data

The one-time import loads the git-ignored `FilmsBase.mdb` (repository root) into the **local** database and writes a
guarded data dump for production (see PLAN.md §6). It needs Windows, the 64-bit Access ODBC driver and the local
MariaDB container.

```powershell
.\scripts\Import-Access.ps1 -Source .\FilmsBase.mdb -DryRun   # counts only, no database changes
.\scripts\Import-Access.ps1 -Source .\FilmsBase.mdb           # migrate, import, report, dump, verify the dump
.\scripts\Import-Access.ps1 -Source .\FilmsBase.mdb -Reset    # clear movies/genres/media first (asks for RESET)
```

The dump lands in `artifacts\import\moviedb-data.sql`. It contains personal reviews: never commit it and delete all
copies after deploying it. The script never prints rows or review text.
