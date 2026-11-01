<#
.SYNOPSIS
    Backs up the moviedb database from the MariaDB container to backup\moviedb-backup-YYYYMMDD-mariadb.sql.

.DESCRIPTION
    The root password is read from the MARIADB_ROOT_PASSWORD environment variable, or prompted for. It is passed to the
    container through MYSQL_PWD only, never on a command line or in a file.

    The data of data_protection_keys is excluded: a dump that contains the key ring next to the Identity data could be
    used to forge auth cookies. Only the table structure is dumped; the application generates a new key ring after a
    restore and users log in again.

    The dump contains personal reviews. The backup folder is git-ignored; keep the files private.

.PARAMETER Container
    Name of the MariaDB container. Default: mariadb.

.PARAMETER Database
    Name of the database. Default: moviedb.

.PARAMETER User
    Database user with enough privileges to dump (default: root).

.PARAMETER OutputDirectory
    Folder for the dump. Default: .\backup next to this script.
#>
[CmdletBinding()]
param(
    [string]$Container = 'mariadb',
    [string]$Database = 'moviedb',
    [string]$User = 'root',
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'backup')
)

$ErrorActionPreference = 'Stop'

$password = $env:MARIADB_ROOT_PASSWORD
if ([string]::IsNullOrEmpty($password)) {
    $secure = Read-Host -Prompt "Password of '$User' on container '$Container'" -AsSecureString
    $password = [System.Net.NetworkCredential]::new('', $secure).Password
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$date = Get-Date -Format 'yyyyMMdd'
$target = Join-Path $OutputDirectory "moviedb-backup-${date}-mariadb.sql"
$containerFile = "/tmp/moviedb-backup-$date.sql"

$keyTable = 'data_protection_keys'
$options = "--user $User --single-transaction --default-character-set=utf8mb4"
$dataDump = "mariadb-dump $options --routines --triggers --hex-blob --ignore-table=$Database.$keyTable --result-file=$containerFile $Database"
$keyTableSchemaDump = "mariadb-dump $options --no-data $Database $keyTable >> $containerFile"

try {
    # The dump is written inside the container and copied out: PowerShell 5.1 redirection would produce UTF-16.
    docker exec -e "MYSQL_PWD=$password" $Container sh -c "$dataDump && $keyTableSchemaDump"
    if ($LASTEXITCODE -ne 0) { throw "mariadb-dump failed (exit code $LASTEXITCODE)." }

    docker cp "${Container}:$containerFile" $target
    if ($LASTEXITCODE -ne 0) { throw "docker cp failed (exit code $LASTEXITCODE)." }
}
finally {
    docker exec $Container rm -f $containerFile | Out-Null
    $password = $null
}

if (-not (Test-Path $target) -or (Get-Item $target).Length -eq 0) {
    throw "The backup file '$target' is missing or empty."
}

Write-Host "Backup written to $target ($([math]::Round((Get-Item $target).Length / 1KB)) KB)."

# Restore:
# - Make sure the database exists with the right charset and collation:
#     DROP DATABASE IF EXISTS moviedb;
#     CREATE DATABASE moviedb CHARACTER SET utf8mb4 COLLATE utf8mb4_uca1400_ai_ci;
#   (create it only, if it does not exist yet; the moviedb user needs privileges on it)
# - Copy the file into the container and load it:
#     docker cp .\backup\moviedb-backup-YYYYMMDD-mariadb.sql mariadb:/tmp/restore.sql
#     docker exec -it mariadb sh -c "mariadb -u root -p moviedb < /tmp/restore.sql"
#     docker exec mariadb rm /tmp/restore.sql
# - Start (or restart) the moviedb container and log in again.
