<#
  Builds Reminder Bubble into dist\ReminderBubble-win-x64.zip (a single ReminderBubble.exe plus its wwwroot folder).

    .\build.ps1                 # publish + zip
    .\build.ps1 -Run            # publish, then start it
    .\build.ps1 -SkipTests      # skip the unit tests

  Needs the .NET 8 SDK (https://dotnet.microsoft.com/download/dotnet/8.0).
#>
param(
    [switch]$Run,
    [switch]$SkipTests,
    [string]$Runtime = 'win-x64'
)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$out  = Join-Path $root "publish\$Runtime"
$dist = Join-Path $root 'dist'

if (-not $SkipTests) {
    Write-Host '== Running tests' -ForegroundColor Cyan
    dotnet test (Join-Path $root 'tests\ReminderBubble.Tests\ReminderBubble.Tests.csproj') --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}

Write-Host "== Publishing ($Runtime)" -ForegroundColor Cyan
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
dotnet publish (Join-Path $root 'src\ReminderBubble\ReminderBubble.csproj') -c Release -r $Runtime --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none -o $out --nologo
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

New-Item -ItemType Directory -Force $dist | Out-Null
$zip = Join-Path $dist "ReminderBubble-$Runtime.zip"
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path (Join-Path $out '*') -DestinationPath $zip
Write-Host "== Done: $zip" -ForegroundColor Green

if ($Run) { Start-Process (Join-Path $out 'ReminderBubble.exe') }
