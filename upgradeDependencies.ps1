# Requires: dotnet tool install --global dotnet-outdated-tool
# Run from the repository root.

$ErrorActionPreference = 'Stop'

Write-Host "Minor updates for .NET projects:`n`n"

dotnet outdated -u -vl Major dotnetthanks-loader.csproj
if ($LASTEXITCODE -ne 0) { throw "Loader minor updates failed with exit code $LASTEXITCODE." }
dotnet outdated -u -vl Major Tests\dotnetthanks-loader.Tests.csproj
if ($LASTEXITCODE -ne 0) { throw "Test project minor updates failed with exit code $LASTEXITCODE." }

Write-Host "`n`nMinor updates complete. Press Enter to continue to prompt for MAJOR version updates or exit the script to keep only minor updates...`n`n"
Read-Host | Out-Null

Write-Host "Major updates for .NET projects:`n`n"

dotnet outdated -u:Prompt dotnetthanks-loader.csproj
if ($LASTEXITCODE -ne 0) { throw "Loader major updates failed with exit code $LASTEXITCODE." }
dotnet outdated -u:Prompt Tests\dotnetthanks-loader.Tests.csproj
if ($LASTEXITCODE -ne 0) { throw "Test project major updates failed with exit code $LASTEXITCODE." }
