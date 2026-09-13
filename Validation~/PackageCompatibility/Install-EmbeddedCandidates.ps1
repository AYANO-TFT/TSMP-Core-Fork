param([Parameter(Mandatory = $true)][string]$StagingRoot)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $StagingRoot).Path
$project = Join-Path $root 'VPM-VRC'
if (-not (Test-Path -LiteralPath (Join-Path $project 'ProjectSettings/ProjectVersion.txt'))) {
    throw 'Run Stage-Candidates.ps1 first.'
}
$inventory = Get-Content -LiteralPath (Join-Path $root 'inventory.json') -Raw | ConvertFrom-Json
foreach ($package in $inventory) {
    if ($package.name -notmatch '^com\.kibalab\.tsmp\.[a-z0-9.]+$') { throw 'Unexpected package name.' }
    $destination = Join-Path $project "Packages/$($package.name)"
    if (Test-Path -LiteralPath $destination) { throw "Do not overwrite an installed package: $destination" }
    if ((Get-FileHash -LiteralPath $package.archive -Algorithm SHA256).Hash -ne $package.sha256) {
        throw "Archive changed: $($package.archive)"
    }
    Expand-Archive -LiteralPath $package.archive -DestinationPath $destination
}
$dependencies = [ordered]@{}
$locked = [ordered]@{}
foreach ($directory in Get-ChildItem -LiteralPath (Join-Path $project 'Packages') -Directory) {
    $path = Join-Path $directory.FullName 'package.json'
    if (-not (Test-Path -LiteralPath $path)) { continue }
    $manifest = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    if (-not ($manifest.name.StartsWith('com.kibalab.tsmp.') -or $manifest.name.StartsWith('com.vrchat.'))) { continue }
    $dependencies[$manifest.name] = @{ version = $manifest.version }
    $ranges = if ($null -ne $manifest.vpmDependencies) { $manifest.vpmDependencies } else { @{} }
    $locked[$manifest.name] = @{ version = $manifest.version; dependencies = $ranges }
}
@{ dependencies = $dependencies; locked = $locked } | ConvertTo-Json -Depth 10 |
    Set-Content -LiteralPath (Join-Path $project 'Packages/vpm-manifest.json') -Encoding utf8
Write-Output 'Embedded candidates installed from recorded ZIPs. Run the VPM resolver and Unity validation separately; this is not a remote repository install.'
