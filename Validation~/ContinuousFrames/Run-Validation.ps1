param(
    [Parameter(Mandatory = $true)][string]$ProjectPath,
    [Parameter(Mandatory = $true)][string]$ResultsDirectory,
    [ValidateSet('Play', 'Player', 'Udon')][string]$Mode = 'Player',
    [string]$Filter = '',
    [string]$UnityPath = 'C:/Program Files/Unity/Hub/Editor/2022.3.22f1/Editor/Unity.exe'
)
$ErrorActionPreference = 'Stop'
$project = (Resolve-Path -LiteralPath $ProjectPath).Path
if (-not (Test-Path -LiteralPath (Join-Path $project 'ProjectSettings/ProjectVersion.txt'))) { throw 'Use an isolated validation project.' }
$scripts = Join-Path $project 'Assets/ContinuousFrames'
[IO.Directory]::CreateDirectory($scripts) | Out-Null
[IO.Directory]::CreateDirectory($ResultsDirectory) | Out-Null
$results = (Resolve-Path -LiteralPath $ResultsDirectory).Path
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ContinuousFrameProbe.cs') -Destination $scripts
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ContinuousFrameValidation.cs') -Destination $scripts
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ResourceProfile.cs') -Destination $scripts
$env:TSMP_CONTINUOUS_RESULTS = $results
$env:TSMP_CONTINUOUS_FILTER = $Filter
$env:TSMP_CONTINUOUS_REVISION = git -C (Join-Path $PSScriptRoot '../..') rev-parse HEAD
$env:TSMP_CONTINUOUS_BUILD = Join-Path $results 'Player/ContinuousFrames.exe'
[IO.Directory]::CreateDirectory((Split-Path $env:TSMP_CONTINUOUS_BUILD)) | Out-Null
$method = if ($Mode -eq 'Play') { 'ContinuousFrameValidation.Play' } else { 'ContinuousFrameValidation.Build' }
if ($Mode -eq 'Udon') {
    $editor = Join-Path $scripts 'Editor'
    [IO.Directory]::CreateDirectory($editor) | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Editor/ContinuousFrameUdonValidation.cs') -Destination $editor
    $method = 'ContinuousFrameUdonValidation.Run'
}
$log = Join-Path $results 'Editor.log'
$process = Start-Process -FilePath $UnityPath -ArgumentList "-batchmode -force-d3d11 -projectPath `"$project`" -executeMethod $method -logFile `"$log`"" -WindowStyle Hidden -PassThru
if (-not $process.WaitForExit(1800000)) { throw "Unity validation timed out. Inspect PID $($process.Id): $log" }
if ($process.ExitCode -ne 0) { throw "Unity exit $($process.ExitCode): $log" }
if ($Mode -eq 'Player') {
    $log = Join-Path $results 'Player.log'
    $process = Start-Process -FilePath $env:TSMP_CONTINUOUS_BUILD -ArgumentList "-batchmode -force-d3d11 -screen-fullscreen 0 -screen-width 640 -screen-height 360 -logFile `"$log`"" -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(1800000)) { throw "Player validation timed out. Inspect PID $($process.Id): $log" }
    if ($process.ExitCode -ne 0) { throw "Player exit $($process.ExitCode): $log" }
}
Get-Content -LiteralPath (Join-Path $results 'status.txt')
foreach ($name in @('summary.csv', 'regression.txt', 'overlap-regression.txt')) {
    $path = Join-Path $results $name
    if (Test-Path -LiteralPath $path) { Get-Content -LiteralPath $path }
}
