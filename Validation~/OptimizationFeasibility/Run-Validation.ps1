param(
    [Parameter(Mandatory = $true)][string]$ProjectPath,
    [Parameter(Mandatory = $true)][string]$ResultsDirectory,
    [ValidateSet('Udon', 'Gamma', 'Linear', 'Raster', 'GpuGamma', 'GpuLinear')][string]$Mode = 'Udon',
    [string]$UnityPath = 'C:/Program Files/Unity/Hub/Editor/2022.3.22f1/Editor/Unity.exe'
)
$ErrorActionPreference = 'Stop'
$project = (Resolve-Path -LiteralPath $ProjectPath).Path
if (-not (Test-Path -LiteralPath (Join-Path $project 'ProjectSettings/ProjectVersion.txt'))) { throw 'Use an isolated validation project.' }
$scripts = Join-Path $project 'Assets/OptimizationFeasibility'
[IO.Directory]::CreateDirectory($scripts) | Out-Null
[IO.Directory]::CreateDirectory($ResultsDirectory) | Out-Null
$results = (Resolve-Path -LiteralPath $ResultsDirectory).Path
$env:TSMP_FEASIBILITY_RESULTS = $results
if ($Mode -eq 'Udon') {
    [IO.Directory]::CreateDirectory((Join-Path $scripts 'Editor')) | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'BulkCopyProbe.cs') -Destination $scripts
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Editor/BulkCopyValidation.cs') -Destination (Join-Path $scripts 'Editor')
    $method = 'BulkCopyValidation.Run'
} elseif ($Mode -eq 'Raster') {
    [IO.Directory]::CreateDirectory((Join-Path $scripts 'Editor')) | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Editor/NativeRasterValidation.cs') -Destination (Join-Path $scripts 'Editor')
    $method = 'NativeRasterValidation.Run'
} elseif ($Mode -like 'Gpu*') {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'GpuLuma4Probe.cs') -Destination $scripts
    $method = 'GpuLuma4Probe.' + $Mode.Substring(3)
} else {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'SnapshotFormatProbe.cs') -Destination $scripts
    $method = "SnapshotFormatProbe.$Mode"
}
$log = Join-Path $results 'Editor.log'
$process = Start-Process -FilePath $UnityPath -ArgumentList "-batchmode -force-d3d11 -projectPath `"$project`" -executeMethod $method -logFile `"$log`"" -WindowStyle Hidden -PassThru
if (-not $process.WaitForExit(900000)) { throw "Unity timed out. Inspect PID $($process.Id): $log" }
if ($process.ExitCode -ne 0) { throw "Unity exit $($process.ExitCode): $log" }
Get-Content -LiteralPath (Join-Path $results $(if ($Mode -eq 'Udon') { 'bulk-result.txt' } elseif ($Mode -eq 'Raster') { 'raster-result.txt' } elseif ($Mode -like 'Gpu*') { 'gpu-result.txt' } else { 'snapshot-result.txt' }))
