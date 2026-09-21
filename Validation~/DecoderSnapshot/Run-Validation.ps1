param(
    [Parameter(Mandatory = $true)][string]$ProjectPath,
    [Parameter(Mandatory = $true)][string]$ResultsDirectory,
    [string]$UnityPath = 'C:/Program Files/Unity/Hub/Editor/2022.3.22f1/Editor/Unity.exe',
    [ValidateSet('Play', 'PlayLinear', 'Player', 'Udon')][string]$Mode = 'Play'
)
$ErrorActionPreference = 'Stop'
$project = (Resolve-Path -LiteralPath $ProjectPath).Path
if (-not (Test-Path -LiteralPath (Join-Path $project 'ProjectSettings/ProjectVersion.txt'))) { throw 'Use an isolated validation project.' }
$scripts = Join-Path $project 'Assets/DecoderSnapshot'
[IO.Directory]::CreateDirectory($scripts) | Out-Null
[IO.Directory]::CreateDirectory($ResultsDirectory) | Out-Null
$results = (Resolve-Path -LiteralPath $ResultsDirectory).Path
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'DecoderSnapshotValidation.cs') -Destination $scripts
$env:TSMP_SNAPSHOT_RESULTS = Join-Path $results "$Mode.txt"
$method = if ($Mode -eq 'Play') { 'DecoderSnapshotValidation.Play' } else { 'DecoderSnapshotValidation.Build' }
if ($Mode -eq 'PlayLinear') { $method = 'DecoderSnapshotValidation.PlayLinear' }
if ($Mode -eq 'Udon') {
    $editor = Join-Path $scripts 'Editor'
    [IO.Directory]::CreateDirectory($editor) | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Editor/UdonSnapshotValidation.cs') -Destination $editor
    $method = 'UdonSnapshotValidation.Run'
}
$env:TSMP_SNAPSHOT_BUILD = Join-Path $results 'Player/Snapshot.exe'
[IO.Directory]::CreateDirectory((Split-Path $env:TSMP_SNAPSHOT_BUILD)) | Out-Null
$log = Join-Path $results "$Mode-editor.log"
$arguments = "-batchmode -force-d3d11 -projectPath `"$project`" -executeMethod $method -logFile `"$log`""
$process = Start-Process -FilePath $UnityPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
$process.WaitForExit()
if ($process.ExitCode -ne 0) { throw "Unity exit $($process.ExitCode): $log" }
if ($Mode -eq 'Player') {
    $log = Join-Path $results 'Player.log'
    $process = Start-Process -FilePath $env:TSMP_SNAPSHOT_BUILD -ArgumentList "-batchmode -force-d3d11 -screen-fullscreen 0 -screen-width 640 -screen-height 360 -logFile `"$log`"" -WindowStyle Hidden -PassThru
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) { throw "Player exit $($process.ExitCode): $log" }
}
if ((Get-Content -LiteralPath $env:TSMP_SNAPSHOT_RESULTS -TotalCount 1) -ne 'PASS') { throw 'Snapshot validation failed.' }
Get-Content -LiteralPath $env:TSMP_SNAPSHOT_RESULTS
