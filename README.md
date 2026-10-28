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
