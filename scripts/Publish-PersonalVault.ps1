<#
.SYNOPSIS
  Builds a portable, self-contained copy of Personal Vault and zips it up as one
  file, ready to hand to someone who'll set everything up from scratch (their own
  credentials.json, their own Drive account) - the friend-sharing model.

.DESCRIPTION
  1. Publishes a self-contained, single-file win-x64 build into PublishDir.
  2. Zips the contents of PublishDir into one dated .zip, placed in that same
     PublishDir - so everything this script produces lives in one place
     (C:\Personal\publish by default) instead of spreading into C:\Personal itself.

  Deliberately does NOT include credentials.json - the recipient gets just the app
  and follows PersonalVault-Drive-Setup-Guide.html themselves if they want Drive
  backup. This script also does not upload anything; send the zip however you like.

.PARAMETER ProjectPath
  Path to the .csproj to publish. Defaults to the PersonalVault project in this repo.

.PARAMETER PublishDir
  Where the published app AND the resulting zip both go. Defaults to
  C:\Personal\publish.

.EXAMPLE
  .\Publish-PersonalVault.ps1
#>

[CmdletBinding()]
param(
    [string]$ProjectPath = (Join-Path $PSScriptRoot "..\PersonalVault\PersonalVault.csproj"),
    [string]$PublishDir = "C:\Personal\publish"
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

$timestamp = Get-Date -Format "yyyyMMdd-HHmm"
$zipPath = Join-Path $PublishDir "PersonalVault-Publish-$timestamp.zip"

if (Test-Path $zipPath) { Remove-Item -Force $zipPath }

# .pdb is debug symbols only (crash stack traces for you, the developer) - a recipient
# never needs it to run the app, so it's left out of the zip (still sits in PublishDir
# alongside the exe if you need it yourself).
$filesToZip = Get-ChildItem $PublishDir -Exclude "*.pdb", "*.zip"

Write-Host "Zipping to $zipPath ..."
Compress-Archive -Path $filesToZip.FullName -DestinationPath $zipPath

Write-Host ""
Write-Host "Done: $zipPath" -ForegroundColor Green
Write-Host "No credentials.json included - the recipient sets up their own via PersonalVault-Drive-Setup-Guide.html if they want Drive backup."
