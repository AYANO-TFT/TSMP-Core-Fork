param(
    [Parameter(Mandatory = $true)][string]$NoSdkTemplate,
    [Parameter(Mandatory = $true)][string]$VrcTemplate,
    [Parameter(Mandatory = $true)][string]$OutputRoot
)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($OutputRoot)
if (Test-Path -LiteralPath $root) { throw 'Use a new output directory to preserve earlier evidence.' }
$specs = @(
    @('TSMP-Core', 'com.kibalab.tsmp.core', '0.3.0-beta.2'),
    @('TSMPCodec-Luma4', 'com.kibalab.tsmp.codec.luma4', '0.0.4-beta.1'),
    @('TSMPCodec-RGB16', 'com.kibalab.tsmp.codec.rgb16', '0.0.3-beta.3'),
    @('TSMPCodec-RGB20', 'com.kibalab.tsmp.codec.rgb20', '0.0.3-beta.4'),
    @('TSMPCodec-Color256', 'com.kibalab.tsmp.codec.color256', '0.0.3-beta.3')
)
$index = Invoke-RestMethod 'https://vpm.kiba.red/index.json'
$packages = Join-Path $root 'packages'
$archives = Join-Path $root 'archives'
[IO.Directory]::CreateDirectory($archives) | Out-Null
$inventory = @()
foreach ($spec in $specs) {
    $repo = $spec[0]
    $id = $spec[1]
    $version = $spec[2]
    $entry = $index.packages.$id.versions.$version
    $url = "https://github.com/kibalab/$repo/releases/download/v$version/$id-$version.zip"
    if ($null -eq $entry -or $entry.url -ne $url) { throw "Missing or unexpected VPM release: $id@$version" }
    $zip = Join-Path $archives "$id-$version.zip"
    Invoke-WebRequest -Uri $url -OutFile $zip
    $hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
    if ($hash -ne $entry.zipSHA256) { throw "VPM archive hash mismatch: $id" }
    $destination = Join-Path $packages $id
    Expand-Archive -LiteralPath $zip -DestinationPath $destination
    $manifest = Get-Content -LiteralPath (Join-Path $destination 'package.json') -Raw | ConvertFrom-Json
    if ($manifest.name -ne $id -or $manifest.version -ne $version) { throw "Archive version mismatch: $id" }
    if ($id -ne 'com.kibalab.tsmp.core' -and
        ($manifest.dependencies.'com.kibalab.tsmp.core' -ne '0.3.0-beta.2' -or
         $manifest.vpmDependencies.'com.kibalab.tsmp.core' -ne '>=0.3.0-beta.2')) {
        throw "Core dependency mismatch: $id"
    }
    if ($manifest.dependencies.PSObject.Properties.Name -contains 'com.vrchat.worlds') { throw "SDK dependency in UPM: $id" }
    $inventory += [ordered]@{ name = $id; version = $version; repository = "kibalab/$repo"; tag = "v$version"; url = $url; archive = $zip; sha256 = $hash }
}
$inventory | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $root 'inventory.json') -Encoding utf8

foreach ($kind in @('UPM-NoSDK', 'VPM-VRC')) {
    $template = if ($kind -eq 'UPM-NoSDK') { $NoSdkTemplate } else { $VrcTemplate }
    $project = Join-Path $root $kind
    [IO.Directory]::CreateDirectory((Join-Path $project 'Assets')) | Out-Null
    [IO.Directory]::CreateDirectory((Join-Path $project 'Packages')) | Out-Null
    Copy-Item -LiteralPath (Join-Path $template 'ProjectSettings') -Destination $project -Recurse
    $sourceManifest = Get-Content -LiteralPath (Join-Path $template 'Packages/manifest.json') -Raw | ConvertFrom-Json
    $manifest = Get-Content -LiteralPath (Join-Path $template 'Packages/manifest.json') -Raw | ConvertFrom-Json
    foreach ($name in @($manifest.dependencies.PSObject.Properties.Name)) {
        if ($name.StartsWith('com.kibalab.tsmp.') -or $name.StartsWith('com.vrchat.')) { $manifest.dependencies.PSObject.Properties.Remove($name) }
    }
    if ($kind -eq 'UPM-NoSDK') {
        foreach ($spec in $specs) {
            $path = (Join-Path $packages $spec[1]).Replace('\', '/')
            $manifest.dependencies | Add-Member -NotePropertyName $spec[1] -NotePropertyValue "file:$path"
        }
        $settings = Get-Content -LiteralPath (Join-Path $project 'ProjectSettings/ProjectSettings.asset') -Raw
        if ($settings -match '(?m)^\s+[^\r\n]*(?:UDONSHARP|COMPILER_UDONSHARP)') { throw 'SDK-free template contains Udon symbols.' }
    } else {
        $dependencies = [ordered]@{}
        $locked = [ordered]@{}
        foreach ($id in @('com.vrchat.base', 'com.vrchat.worlds')) {
            $source = Join-Path $template "Packages/$id"
            if (-not (Test-Path -LiteralPath $source)) {
                $reference = $sourceManifest.dependencies.$id
                if (-not $reference.StartsWith('file:')) { throw "Cannot locate SDK package: $id" }
                $source = $reference.Substring(5)
            }
            Copy-Item -LiteralPath $source -Destination (Join-Path $project "Packages/$id") -Recurse
            $sdk = Get-Content -LiteralPath (Join-Path $source 'package.json') -Raw | ConvertFrom-Json
            $dependencies[$id] = @{ version = $sdk.version }
            $locked[$id] = @{ version = $sdk.version; dependencies = $sdk.vpmDependencies }
        }
        foreach ($spec in $specs) { $dependencies[$spec[1]] = @{ version = $spec[2] } }
        @{ dependencies = $dependencies; locked = $locked } | ConvertTo-Json -Depth 10 |
            Set-Content -LiteralPath (Join-Path $project 'Packages/vpm-manifest.json') -Encoding utf8
    }
    $manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $project 'Packages/manifest.json') -Encoding utf8
}
Write-Output "Downloaded public packages and verified VPM hashes in $root. VPM-VRC contains only SDK packages. Install the exact versions before opening Unity:"
foreach ($package in $inventory) {
    Write-Output "vpm add package $($package.name)@$($package.version) -p `"$(Join-Path $root 'VPM-VRC')`""
}
