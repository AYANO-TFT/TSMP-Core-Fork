param(
    [Parameter(Mandatory = $true)][string]$UnityPath,
    [Parameter(Mandatory = $true)][string]$ProjectPath,
    [Parameter(Mandatory = $true)][string]$ResultsDirectory,
    [ValidateSet('Gpu', 'Udon', 'Play', 'Player')][string]$Mode = 'Gpu',
    [switch]$SkipTiming
)

$ErrorActionPreference = 'Stop'
$project = (Resolve-Path -LiteralPath $ProjectPath).Path
$unity = (Resolve-Path -LiteralPath $UnityPath).Path
if (-not (Test-Path -LiteralPath (Join-Path $project 'ProjectSettings/ProjectVersion.txt'))) {
    throw 'Use a dedicated Unity validation project.'
}
$scripts = Join-Path $project 'Assets/GpuCalibration'
[IO.Directory]::CreateDirectory($scripts) | Out-Null
[IO.Directory]::CreateDirectory($ResultsDirectory) | Out-Null
$results = (Resolve-Path -LiteralPath $ResultsDirectory).Path
$name = $Mode.ToLowerInvariant()
$env:TSMP_GPU_RESULTS = Join-Path $results "$name.txt"
$env:TSMP_GPU_SKIP_TIMING = if ($SkipTiming) { '1' } else { '' }
$env:TSMP_GPU_DISABLE_LUT = ''

if ($Mode -eq 'Udon') {
    $editor = Join-Path $scripts 'Editor'
    [IO.Directory]::CreateDirectory($editor) | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Editor/UdonCalibrationValidation.cs') -Destination $editor
    $method = 'UdonCalibrationValidation.Run'
} else {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'CalibrationLutValidation.cs') -Destination $scripts
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'CalibrationDecoderValidation.cs') -Destination $scripts
    $method = if ($Mode -eq 'Gpu') { 'CalibrationLutValidation.Run' }
        elseif ($Mode -eq 'Play') { 'CalibrationDecoderValidation.Play' }
        else { 'CalibrationDecoderValidation.Build' }
}

if ($Mode -eq 'Player') {
    $build = Join-Path $results 'Player/Calibration.exe'
    [IO.Directory]::CreateDirectory((Split-Path $build)) | Out-Null
    $env:TSMP_VALIDATION_BUILD = $build
}
$log = Join-Path $results "$name-editor.log"
$arguments = "-batchmode -force-d3d11 -projectPath `"$project`" -executeMethod $method -logFile `"$log`""
$process = Start-Process -FilePath $unity -ArgumentList $arguments -WindowStyle Hidden -PassThru
$process.WaitForExit()
if ($process.ExitCode -ne 0) { throw "Unity exited with $($process.ExitCode). See $log" }

if ($Mode -eq 'Player') {
    $log = Join-Path $results 'Player.log'
    $arguments = "-force-d3d11 -screen-fullscreen 0 -screen-width 640 -screen-height 360 -logFile `"$log`""
    $process = Start-Process -FilePath $build -ArgumentList $arguments -WindowStyle Hidden -PassThru
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) { throw "Player exited with $($process.ExitCode). See $log" }
}
if ((Get-Content -LiteralPath $env:TSMP_GPU_RESULTS -TotalCount 1) -ne 'PASS') {
    throw "Validation did not pass. See $env:TSMP_GPU_RESULTS"
}
Get-Content -LiteralPath $env:TSMP_GPU_RESULTS
