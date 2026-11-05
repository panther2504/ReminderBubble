<#
  Builds Reminder Bubble:
    dist\ReminderBubble-win-x64.zip            a single ReminderBubble.exe plus its wwwroot folder (portable)
    dist\ReminderBubble-Setup-<version>.exe    the installer (needs Inno Setup 6, see below)

    .\build.ps1                 # tests + publish + zip + installer
    .\build.ps1 -Run            # ... then start the published app
    .\build.ps1 -SkipTests      # skip the unit tests
    .\build.ps1 -SkipInstaller  # zip only

  Needs the .NET 8 SDK (https://dotnet.microsoft.com/download/dotnet/8.0).
  The installer step needs Inno Setup 6 (https://jrsoftware.org/isdl.php  or  `winget install JRSoftware.InnoSetup`);
  if it is not installed that step is skipped with a warning.
#>
param(
    [switch]$Run,
    [switch]$SkipTests,
    [switch]$SkipInstaller,
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
Write-Host "== Zip: $zip" -ForegroundColor Green

if (-not $SkipInstaller) {
    $version = ([xml](Get-Content (Join-Path $root 'src\ReminderBubble\ReminderBubble.csproj'))).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
    $candidates = @(
        (Get-Command iscc -ErrorAction SilentlyContinue).Source,
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    ) | Where-Object { $_ -and (Test-Path $_) }
    if ($candidates) {
        Write-Host "== Building installer ($version)" -ForegroundColor Cyan
        & ($candidates | Select-Object -First 1) "/DAppVersion=$version" "/DSourceDir=$out" (Join-Path $root 'installer\ReminderBubble.iss')
        if ($LASTEXITCODE -ne 0) { throw 'Installer build failed.' }
        Write-Host "== Installer: $(Join-Path $dist "ReminderBubble-Setup-$version.exe")" -ForegroundColor Green
    } else {
        Write-Warning 'Inno Setup 6 was not found - skipping the installer. Install it with: winget install JRSoftware.InnoSetup'
    }
}

if ($Run) { Start-Process (Join-Path $out 'ReminderBubble.exe') }
