#Requires -Version 5.1

param(
    [Parameter(Mandatory)]
    [ValidatePattern('^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$')]
    [string] $Version
)

$ErrorActionPreference = 'Stop'
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    throw 'The release build requires Windows.'
}

$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo 'src/SnipNation/SnipNation.csproj'
$icon = Join-Path $repo 'src/SnipNation/Assets/SnipNation.ico'
$splash = Join-Path $repo 'assets/branding/snipnation-dark-background.png'
$palette = Get-Content -LiteralPath (Join-Path $repo 'assets/branding/palette.json') -Raw | ConvertFrom-Json
$staging = Join-Path $repo "artifacts/staging/$([Guid]::NewGuid().ToString('N'))"
$publish = Join-Path $staging 'publish'
$packages = Join-Path $staging 'packages'
$output = Join-Path $repo "artifacts/releases/$Version"

Push-Location $repo
try {
    & dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw 'Could not restore Velopack.' }

    & dotnet restore $project --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Could not restore release dependencies.' }

    $publishArgs = @(
        'publish', $project, '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '--no-restore',
        '-o', $publish, "-p:Version=$Version", '-p:ContinuousIntegrationBuild=true'
    )
    & dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) { throw 'The Windows build failed.' }

    $packArgs = @(
        'vpk', 'pack', '--packId', 'SnipNation', '--packTitle', 'SnipNation', '--packVersion', $Version,
        '--packDir', $publish, '--mainExe', 'SnipNation.exe', '--runtime', 'win11-x64', '--channel', 'win',
        '--icon', $icon, '--splashImage', $splash, '--splashProgressColor', $palette.accent,
        '--shortcuts', 'StartMenuRoot', '--delta', 'None', '--outputDir', $packages
    )
    & dotnet @packArgs
    if ($LASTEXITCODE -ne 0) { throw 'Could not create the installer.' }

    $releaseFiles = @('SnipNation-win-Setup.exe', "SnipNation-$Version-full.nupkg", 'releases.win.json')
    foreach ($file in $releaseFiles) {
        if (-not (Test-Path -LiteralPath (Join-Path $packages $file) -PathType Leaf)) {
            throw "Missing release file $file."
        }
    }
    New-Item -ItemType Directory -Path $output -Force | Out-Null
    foreach ($file in $releaseFiles) {
        Copy-Item -LiteralPath (Join-Path $packages $file) -Destination $output -Force
    }
    Write-Output $output
}
finally {
    Pop-Location
    if (Test-Path -LiteralPath $staging) {
        Remove-Item -LiteralPath $staging -Recurse -Force
    }
}
