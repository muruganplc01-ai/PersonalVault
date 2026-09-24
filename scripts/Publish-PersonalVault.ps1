<#
.SYNOPSIS
  Builds a portable, self-contained copy of Personal Vault and zips it up, ready to
  hand to another PC (yourself, disaster recovery, family/friend sharing) or to drop
  into Google Drive manually.

.DESCRIPTION
  1. Publishes a self-contained, single-file win-x64 build to a clean output folder.
  2. Optionally copies your own credentials.json into that folder, so the zip is a
     complete, ready-to-run package (see PersonalVault-Disaster-Recovery.html for why
     that file is safe to bundle with your own copy).
  3. Zips the whole publish folder into a dated .zip next to it.

  This script does NOT upload anything anywhere - it stops once the zip exists. Copy
  that zip into your Drive folder (or upload it at drive.google.com) yourself.

.PARAMETER ProjectPath
  Path to the .csproj to publish. Defaults to the PersonalVault project in this repo.

.PARAMETER PublishDir
  Where the published app is placed. Defaults to C:\Personal\publish.

.PARAMETER OutputZipDir
  Where the resulting .zip is written. Defaults to C:\Personal.

.PARAMETER CredentialsJsonPath
  Path to your own credentials.json to bundle into the zip. Defaults to
  C:\Personal\PersonalVault\credentials.json (this project's default data folder,
  since PersonalVault.csproj builds to C:\Personal). Pass -SkipCredentials to leave
  it out entirely.

.PARAMETER SkipCredentials
  Don't bundle credentials.json even if found.

.EXAMPLE
  .\Publish-PersonalVault.ps1

.EXAMPLE
  .\Publish-PersonalVault.ps1 -SkipCredentials
#>

[CmdletBinding()]
param(
    [string]$ProjectPath = (Join-Path $PSScriptRoot "..\PersonalVault\PersonalVault.csproj"),
    [string]$PublishDir = "C:\Personal\publish",
    [string]$OutputZipDir = "C:\Personal",
    [string]$CredentialsJsonPath = "C:\Personal\PersonalVault\credentials.json",
    [switch]$SkipCredentials
)

$ErrorActionPreference = "Stop"

$ProjectPath = (Resolve-Path $ProjectPath).Path
Write-Host "Project:      $ProjectPath"
Write-Host "Publish dir:  $PublishDir"

# Start from a clean publish folder each time, so the zip never carries stale files
# left over from a previous build (e.g. an older single-file .exe).
if (Test-Path $PublishDir) {
    Write-Host "Removing existing publish folder..."
    Remove-Item -Recurse -Force $PublishDir
}

Write-Host "Publishing (self-contained, single-file, win-x64)..."
# --source pins restore to nuget.org only - this machine's global NuGet config also
# lists a private/expired Telerik feed (nuget.telerik.com) that 401s and would
# otherwise fail restore, even though nothing this project depends on lives there.
dotnet publish $ProjectPath `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    --source https://api.nuget.org/v3/index.json `
    -o $PublishDir

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

if (-not $SkipCredentials) {
    if (Test-Path $CredentialsJsonPath) {
        Copy-Item $CredentialsJsonPath -Destination (Join-Path $PublishDir "credentials.json") -Force
        Write-Host "Bundled credentials.json from $CredentialsJsonPath"
    } else {
        Write-Warning "credentials.json not found at $CredentialsJsonPath - zip will not include it. Pass -CredentialsJsonPath to point at the right file, or -SkipCredentials to silence this."
    }
}

$timestamp = Get-Date -Format "yyyyMMdd-HHmm"
$zipPath = Join-Path $OutputZipDir "PersonalVault-Publish-$timestamp.zip"

if (Test-Path $zipPath) { Remove-Item -Force $zipPath }

Write-Host "Zipping to $zipPath ..."
Compress-Archive -Path (Join-Path $PublishDir "*") -DestinationPath $zipPath

Write-Host ""
Write-Host "Done: $zipPath" -ForegroundColor Green
Write-Host "Upload this zip to Google Drive (or wherever you keep it) manually - this script does not do that step."
