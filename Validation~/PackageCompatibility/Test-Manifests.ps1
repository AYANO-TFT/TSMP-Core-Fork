param(
    [Parameter(Mandatory = $true)][string]$StagingRoot,
    [Parameter(Mandatory = $true)][string]$SemanticVersioningAssembly
)

$ErrorActionPreference = 'Stop'
Add-Type -Path (Resolve-Path -LiteralPath $SemanticVersioningAssembly).Path
$root = (Resolve-Path -LiteralPath $StagingRoot).Path
$inventory = Get-Content -LiteralPath (Join-Path $root 'inventory.json') -Raw | ConvertFrom-Json
$manifests = @{}
foreach ($directory in Get-ChildItem -LiteralPath (Join-Path $root 'VPM-VRC/Packages') -Directory) {
    $path = Join-Path $directory.FullName 'package.json'
    if (Test-Path -LiteralPath $path) {
        $manifest = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
        $manifests[$manifest.name] = $manifest
    }
}
$results = @('PASS', ('VPM SemanticVersioning assembly: ' + $SemanticVersioningAssembly))
foreach ($package in $inventory) {
    $manifest = $manifests[$package.name]
    if ($manifest.version -ne $package.version) { throw "Installed version mismatch: $($package.name)" }
    foreach ($dependency in $manifest.vpmDependencies.PSObject.Properties) {
        $installed = $manifests[$dependency.Name]
        $range = [SemanticVersioning.Range]::Parse($dependency.Value)
        if ($null -eq $installed -or -not $range.IsSatisfied($installed.version, $false, $true)) {
            throw "Unsatisfied VPM dependency: $($package.name) -> $($dependency.Name) $($dependency.Value)"
        }
        $results += "$($package.name) -> $($dependency.Name) $($dependency.Value): $($installed.version) accepted with prereleases enabled"
    }
    if ($package.name -ne 'com.kibalab.tsmp.core') {
        $range = [SemanticVersioning.Range]::Parse($manifest.vpmDependencies.'com.kibalab.tsmp.core')
        foreach ($old in @('0.2.0', '0.3.0-beta.1')) {
            if ($range.IsSatisfied($old, $false, $true)) { throw "Old Core accepted: $old" }
            $results += "$($package.name): incompatible Core $old rejected"
        }
        foreach ($supported in @('0.3.0-beta.2', '0.3.0-beta.3', '0.3.0')) {
            if (-not $range.IsSatisfied($supported, $false, $true)) { throw "Compatible range rejected: $supported" }
        }
    }
}
$lock = Get-Content -LiteralPath (Join-Path $root 'UPM-NoSDK/Packages/packages-lock.json') -Raw | ConvertFrom-Json
if ($lock.dependencies.PSObject.Properties.Name | Where-Object { $_.StartsWith('com.vrchat.') }) { throw 'UPM installed a VRChat package.' }
foreach ($package in $inventory) {
    $entry = $lock.dependencies.($package.name)
    $expected = 'file:' + (Join-Path $root "packages/$($package.name)").Replace('\', '/')
    if ($entry.source -ne 'local' -or $entry.version -ne $expected) { throw "Unexpected UPM source: $($package.name)" }
    if ($package.name -ne 'com.kibalab.tsmp.core' -and $entry.dependencies.'com.kibalab.tsmp.core' -ne '0.3.0-beta.2') {
        throw "Unexpected UPM Core dependency: $($package.name)"
    }
}
$results += 'UPM lock uses the extracted candidate packages, matching minimum Core, and no VRChat SDK.'
$results | Set-Content -LiteralPath (Join-Path $root 'manifest-results.txt') -Encoding utf8
$results
