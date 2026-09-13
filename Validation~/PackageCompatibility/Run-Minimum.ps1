param(
    [Parameter(Mandatory = $true)][string]$UnityPath,
    [Parameter(Mandatory = $true)][string]$StagingRoot,
    [ValidateSet('Luma4', 'RGB16', 'RGB20', 'Color256')][string[]]$Codecs = @('Luma4', 'RGB16', 'RGB20', 'Color256'),
    [ValidatePattern('^[A-Za-z0-9-]+$')][string]$ProjectPrefix = 'Minimum'
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $StagingRoot).Path
foreach ($codec in $Codecs) {
    $project = Join-Path $root "$ProjectPrefix-$codec"
    if (Test-Path -LiteralPath $project) { throw "Do not overwrite $project" }
    [IO.Directory]::CreateDirectory((Join-Path $project 'Assets/Editor')) | Out-Null
    [IO.Directory]::CreateDirectory((Join-Path $project 'Packages')) | Out-Null
    Copy-Item -LiteralPath (Join-Path $root 'UPM-NoSDK/ProjectSettings') -Destination $project -Recurse
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'MinimumCodecValidation.cs') -Destination (Join-Path $project 'Assets/Editor')
    $manifest = Get-Content -LiteralPath (Join-Path $root 'UPM-NoSDK/Packages/manifest.json') -Raw | ConvertFrom-Json
    foreach ($name in @($manifest.dependencies.PSObject.Properties.Name)) {
        if ($name.StartsWith('com.kibalab.tsmp.codec.') -and $name -ne 'com.kibalab.tsmp.codec.luma4' -and
            $name -ne ('com.kibalab.tsmp.codec.' + $codec.ToLowerInvariant())) {
            $manifest.dependencies.PSObject.Properties.Remove($name)
        }
    }
    $manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $project 'Packages/manifest.json') -Encoding utf8
    $env:TSMP_MINIMUM_CODEC = $codec
    $env:TSMP_MINIMUM_RESULT = Join-Path $project 'result.txt'
    $log = Join-Path $project 'Editor.log'
    $arguments = "-batchmode -force-d3d11 -projectPath `"$project`" -executeMethod MinimumCodecValidation.Run -logFile `"$log`""
    $process = Start-Process -FilePath $UnityPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(900000)) { throw "Unity validation timed out; inspect PID $($process.Id): $log" }
    if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $env:TSMP_MINIMUM_RESULT)) { throw "Validation failed: $log" }
    if ((Get-Content -LiteralPath $env:TSMP_MINIMUM_RESULT -TotalCount 1) -ne 'PASS') { throw "Validation failed: $log" }
    Get-Content -LiteralPath $env:TSMP_MINIMUM_RESULT
}
