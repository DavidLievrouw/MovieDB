<#
.SYNOPSIS
    Imports FilmsBase.mdb into the LOCAL MovieDB database and produces a verified, guarded production data dump.

.DESCRIPTION
    Single entry point for PLAN.md section 6. Runs on the Windows dev machine (64-bit Access ODBC driver) against the
    local MariaDB container. Never prints rows, review text, passwords or connection strings.

    Steps: resolve connection string, build the tool, migrate, import (one transaction) and report, write the guarded
    dump with mariadb-dump inside the container, then verify the dump by restoring it into a scratch database and
    comparing row counts and content hashes.

.PARAMETER Source
    Path of the Access database. Opened read-only.

.PARAMETER ConnectionString
    MariaDB connection string. Default: env MOVIEDB_CONNECTION_STRING, then the MovieDB.Web user-secrets, then a prompt.

.PARAMETER Reset
    Empty movies, genres and media first (typed confirmation). Users and auth state are never touched.

.PARAMETER DryRun
    Read, clean, de-duplicate and correct in memory and print the report. Writes nothing.

.PARAMETER DumpPath
    Where to write the guarded production data dump.

.PARAMETER MariaDbContainer
    Name of the local MariaDB container used for mariadb-dump and the dump verification.

.PARAMETER SkipDump
    Import and report only; skip the dump and its verification.

.PARAMETER AllowRemote
    Allow a database host other than localhost. Never use this for production (PLAN.md section 11.2).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Source,
    [string] $ConnectionString,
    [switch] $Reset,
    [switch] $DryRun,
    [string] $DumpPath = '.\artifacts\import\moviedb-data.sql',
    [string] $MariaDbContainer = 'mariadb',
    [switch] $SkipDump,
    [switch] $AllowRemote
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$toolProject = Join-Path $repoRoot 'src\tools\MovieDB.AccessImport'
$toolExe = Join-Path $toolProject 'bin\Release\net10.0\MovieDB.AccessImport.exe'
$scratchDatabase = 'moviedb_dumpcheck'
$connectionVariable = 'MOVIEDB_CONNECTION_STRING'

function Write-Step([string] $message) { Write-Host "==> $message" -ForegroundColor Cyan }

# Runs a native command, returns its output lines and fails on a non-zero exit code. Output must not contain secrets.
function Invoke-Native([string] $File, [string[]] $Arguments, [switch] $AllowFailure) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = & $File @Arguments 2>&1 | ForEach-Object { "$_" }
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previous
    }

    if ($exitCode -ne 0 -and -not $AllowFailure) {
        $output | ForEach-Object { Write-Host $_ }
        throw "'$File $($Arguments[0])' failed with exit code $exitCode."
    }

    [pscustomobject]@{ ExitCode = $exitCode; Output = @($output) }
}

function Invoke-Tool([string[]] $Arguments, [string] $ToolConnectionString, [switch] $AllowFailure) {
    $previous = [Environment]::GetEnvironmentVariable($connectionVariable, 'Process')
    [Environment]::SetEnvironmentVariable($connectionVariable, $ToolConnectionString, 'Process')
    try {
        $result = Invoke-Native $toolExe $Arguments -AllowFailure:$AllowFailure
    }
    finally {
        [Environment]::SetEnvironmentVariable($connectionVariable, $previous, 'Process')
    }

    $result
}

function Get-ConnectionString {
    if ($ConnectionString) { return $ConnectionString }

    $fromEnvironment = [Environment]::GetEnvironmentVariable($connectionVariable)
    if ($fromEnvironment) { return $fromEnvironment }

    $secrets = Invoke-Native 'dotnet' @('user-secrets', 'list', '--project', (Join-Path $repoRoot 'src\MovieDB.Web')) -AllowFailure
    if ($secrets.ExitCode -eq 0) {
        $prefix = 'ConnectionStrings:MovieDb = '
        $line = $secrets.Output | Where-Object { $_.StartsWith($prefix) } | Select-Object -First 1
        if ($line) { return $line.Substring($prefix.Length) }
    }

    $secure = Read-Host 'MariaDB connection string' -AsSecureString
    [System.Net.NetworkCredential]::new('', $secure).Password
}

function Get-Setting($Builder, [string[]] $Keys) {
    foreach ($key in $Keys) {
        $value = $null
        if ($Builder.TryGetValue($key, [ref] $value)) { return [string] $value }
    }

    $null
}

function Set-Database([string] $Value, [string] $Database) {
    $builder = [System.Data.Common.DbConnectionStringBuilder]::new()
    $builder.set_ConnectionString($Value)
    foreach ($key in @('Database', 'Initial Catalog')) { [void] $builder.Remove($key) }
    $builder['Database'] = $Database
    $builder.get_ConnectionString()
}

function Get-FullPath([string] $Path) {
    if ([System.IO.Path]::IsPathRooted($Path)) { return [System.IO.Path]::GetFullPath($Path) }
    [System.IO.Path]::GetFullPath((Join-Path (Get-Location).Path $Path))
}

function Remove-ContainerFile([string] $Path) {
    [void] (Invoke-Native 'docker' @('exec', $MariaDbContainer, 'rm', '-f', $Path) -AllowFailure)
}

# --- 1. Inputs ---------------------------------------------------------------------------------------------------
$sourcePath = Get-FullPath $Source
if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) { throw "Source file not found: $sourcePath" }
$sourceName = Split-Path -Leaf $sourcePath

Write-Step 'Building the import tool (Release, x64)'
[void] (Invoke-Native 'dotnet' @('build', $toolProject, '-c', 'Release', '--nologo', '-v', 'quiet', '-warnaserror'))
if (-not (Test-Path -LiteralPath $toolExe)) { throw "Import tool not found after build: $toolExe" }

if ($DryRun) {
    Write-Step 'Dry run: reading the source and printing the report (nothing is written)'
    $result = Invoke-Native $toolExe @('import', '--source', $sourcePath, '--dry-run')
    $result.Output | ForEach-Object { Write-Host $_ }
    return
}

$targetConnection = Get-ConnectionString
$builder = [System.Data.Common.DbConnectionStringBuilder]::new()
$builder.set_ConnectionString($targetConnection)
$dbHost = Get-Setting $builder @('Server', 'Host', 'Data Source')
$dbUser = Get-Setting $builder @('User', 'User Id', 'Uid', 'User Name', 'Username')
$dbPassword = Get-Setting $builder @('Password', 'Pwd')
$dbName = Get-Setting $builder @('Database', 'Initial Catalog')
if (-not $dbHost -or -not $dbUser -or -not $dbName) { throw 'The connection string needs a server, user and database.' }
if (-not $AllowRemote -and $dbHost -notin @('localhost', '127.0.0.1', '::1')) {
    throw "Refusing to import into non-local host '$dbHost'. Use -AllowRemote only if you are sure."
}

if ($dbUser -notmatch '^[A-Za-z0-9_.-]+$' -or $dbName -notmatch '^[A-Za-z0-9_]+$') {
    throw 'The database user or name contains unsupported characters.'
}

if ($dbName -eq $scratchDatabase) { throw "The target database must not be '$scratchDatabase'." }

if ($Reset) {
    Write-Host "-Reset empties movies, genres and media in database '$dbName' (users and auth state are kept)." -ForegroundColor Yellow
    if ((Read-Host 'Type RESET to continue') -cne 'RESET') { throw 'Reset not confirmed.' }
}

# --- 2. Migrate and import ---------------------------------------------------------------------------------------
Write-Step "Applying migrations to '$dbName'"
$migrate = Invoke-Tool @('migrate') $targetConnection
$migrate.Output | ForEach-Object { Write-Host $_ }

Write-Step 'Importing (one transaction) and verifying'
$importArguments = @('import', '--source', $sourcePath)
if ($Reset) { $importArguments += '--reset' }
$import = Invoke-Tool $importArguments $targetConnection
$import.Output | ForEach-Object { Write-Host $_ }

if ($SkipDump) {
    Write-Step 'Done (dump skipped)'
    return
}

# --- 3. Production data dump -------------------------------------------------------------------------------------
$dumpFullPath = Get-FullPath $DumpPath
$dumpDirectory = Split-Path -Parent $dumpFullPath
if (-not (Test-Path -LiteralPath $dumpDirectory)) { [void] (New-Item -ItemType Directory -Path $dumpDirectory) }
$bodyPath = "$dumpFullPath.body.tmp"
$containerBody = '/tmp/moviedb-body.sql'
$containerDump = '/tmp/moviedb-dumpcheck.sql'

$previousPassword = $env:MYSQL_PWD
$env:MYSQL_PWD = $dbPassword
try {
    Write-Step 'Writing the production data dump'
    [void] (Invoke-Native 'docker' @(
            'exec', '-e', 'MYSQL_PWD', $MariaDbContainer, 'mariadb-dump',
            '-h', '127.0.0.1', '--protocol=tcp', '-u', $dbUser,
            '--no-create-info', '--no-create-db', '--skip-triggers', '--complete-insert',
            '--default-character-set=utf8mb4', '--single-transaction', '--skip-add-locks',
            '--skip-disable-keys', '--no-tablespaces', "--result-file=$containerBody",
            $dbName, 'genres', 'media', 'movies'))
    [void] (Invoke-Native 'docker' @('cp', "${MariaDbContainer}:$containerBody", $bodyPath))
    Remove-ContainerFile $containerBody

    $wrap = Invoke-Tool @('wrap-dump', '--body', $bodyPath, '--output', $dumpFullPath, '--source', $sourceName) $targetConnection
    $wrap.Output | ForEach-Object { Write-Host $_ }
    Remove-Item -LiteralPath $bodyPath -Force

    # --- 4. Verify the dump in a scratch database -----------------------------------------------------------------
    Write-Step "Verifying the dump in scratch database '$scratchDatabase'"
    $scratchConnection = Set-Database $targetConnection $scratchDatabase
    $mariadbArguments = @('exec', '-e', 'MYSQL_PWD', $MariaDbContainer, 'mariadb', '-h', '127.0.0.1', '--protocol=tcp', '-u', $dbUser)
    $loadCommand = "mariadb --default-character-set=utf8mb4 -h 127.0.0.1 --protocol=tcp -u '$dbUser' $scratchDatabase < $containerDump"
    try {
        [void] (Invoke-Native 'docker' ($mariadbArguments + @('--execute', "DROP DATABASE IF EXISTS $scratchDatabase; CREATE DATABASE $scratchDatabase CHARACTER SET utf8mb4 COLLATE utf8mb4_uca1400_ai_ci;")))
        [void] (Invoke-Tool @('migrate') $scratchConnection)

        [void] (Invoke-Native 'docker' @('cp', $dumpFullPath, "${MariaDbContainer}:$containerDump"))
        [void] (Invoke-Native 'docker' @('exec', '-e', 'MYSQL_PWD', $MariaDbContainer, 'sh', '-c', $loadCommand))

        $expected = (Invoke-Tool @('fingerprint') $targetConnection).Output
        $actual = (Invoke-Tool @('fingerprint') $scratchConnection).Output
        $expected | ForEach-Object { Write-Host "  source : $_" }
        $actual | ForEach-Object { Write-Host "  restore: $_" }
        if (($expected -join "`n") -ne ($actual -join "`n")) { throw 'Dump verification failed: the restored data differs from the imported data.' }

        $second = Invoke-Native 'docker' @('exec', '-e', 'MYSQL_PWD', $MariaDbContainer, 'sh', '-c', $loadCommand) -AllowFailure
        if ($second.ExitCode -eq 0) { throw 'Dump verification failed: the guard did not refuse a second load.' }
        if (-not ($second.Output -match 'Refusing to load')) { throw 'Dump verification failed: the second load failed for another reason than the guard.' }
        $afterSecond = (Invoke-Tool @('fingerprint') $scratchConnection).Output
        if (($expected -join "`n") -ne ($afterSecond -join "`n")) { throw 'Dump verification failed: the refused second load changed data.' }
        Write-Host '  guard refused a second load, as expected.'
    }
    finally {
        [void] (Invoke-Native 'docker' ($mariadbArguments + @('--execute', "DROP DATABASE IF EXISTS $scratchDatabase;")) -AllowFailure)
        Remove-ContainerFile $containerDump
    }
}
finally {
    $env:MYSQL_PWD = $previousPassword
    if (Test-Path -LiteralPath $bodyPath) { Remove-Item -LiteralPath $bodyPath -Force }
}

Write-Step 'Done'
Write-Host "Dump: $dumpFullPath (personal reviews: never commit, delete after deployment)"
