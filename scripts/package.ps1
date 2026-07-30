[CmdletBinding()]
param(
    [string]$Version = '1.4.2',
    [string]$JellyfinVersion = '10.11.11'
)

$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw 'Version must use stable semantic versioning, for example 1.3.0.'
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$localDotnet = Join-Path $repoRoot '.dotnet\dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $localDotnet) {
    $localDotnet
} else {
    (Get-Command dotnet -ErrorAction Stop).Source
}

$archiveName =
    "ChooseYourMeta_${Version}_jellyfin-$JellyfinVersion.zip"
$artifacts = Join-Path $repoRoot 'artifacts'
$publish = Join-Path $repoRoot 'publish'
$stage = Join-Path $artifacts 'package'

foreach ($path in @($publish, $stage)) {
    $resolved = [System.IO.Path]::GetFullPath($path)
    $resolvedRoot = [System.IO.Path]::GetFullPath($repoRoot)
    if (-not $resolved.StartsWith(
        $resolvedRoot,
        [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Refusing to clean a path outside the repository.'
    }

    if (Test-Path -LiteralPath $resolved) {
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}

New-Item -ItemType Directory -Path $stage -Force | Out-Null

& $dotnet restore (Join-Path $repoRoot 'RussianMetadata.Tests\RussianMetadata.Tests.csproj')
if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed.' }
& $dotnet test `
    (Join-Path $repoRoot 'RussianMetadata.Tests\RussianMetadata.Tests.csproj') `
    -c Release `
    --no-restore `
    -p:Version=$Version
if ($LASTEXITCODE -ne 0) { throw 'dotnet test failed.' }
& $dotnet publish `
    (Join-Path $repoRoot 'RussianMetadata.csproj') `
    -c Release `
    --no-restore `
    -p:Version=$Version `
    -o $publish
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

$dll = Join-Path $publish 'RussianMetadata.dll'
if (-not (Test-Path -LiteralPath $dll)) {
    throw 'Published plugin DLL was not found.'
}
Copy-Item -LiteralPath $dll -Destination $stage

$meta = @{
    category = 'General'
    changelog = 'Prioritize matching movie titles over exact release years and reject unrelated TMDB results.'
    description = 'Choose Russian or English metadata, posters, and logos for movies and collections.'
    guid = 'a8f3c2e1-4b5d-6e7f-8a9b-0c1d2e3f4a5b'
    name = 'Choose your Meta!'
    overview = 'Controls RU/EN metadata and artwork without requiring a separate TMDB key.'
    owner = 'Lootfullin'
    targetAbi = "$JellyfinVersion.0"
    timestamp = [DateTime]::UtcNow.ToString('o')
    version = "$Version.0"
    status = 'Active'
    autoUpdate = $false
}
$metaPath = Join-Path $stage 'meta.json'
$metaJson = ConvertTo-Json -InputObject $meta
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($metaPath, "$metaJson`n", $utf8NoBom)

New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
$archive = Join-Path $artifacts $archiveName
if (Test-Path -LiteralPath $archive) {
    Remove-Item -LiteralPath $archive -Force
}
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $archive

$checksum = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash
$checksumPath = "$archive.sha256"
"$($checksum.ToLowerInvariant())  $archiveName" |
    Set-Content -LiteralPath $checksumPath -Encoding ascii

Write-Host "Package: $archive"
Write-Host "SHA256:  $checksumPath"
