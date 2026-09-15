param(
    [Parameter(Mandatory = $true)][string]$UnityEditor,
    [Parameter(Mandatory = $true)][string]$Project,
    [Parameter(Mandatory = $true)][string]$Results,
    [string]$Ffmpeg = 'ffmpeg',
    [string]$Ffprobe = 'ffprobe',
    [ValidateSet('Editor', 'Build', 'Player')][string]$Step = 'Editor'
)

$ErrorActionPreference = 'Stop'
$Project = (Resolve-Path -LiteralPath $Project).Path
if (!(Test-Path -LiteralPath (Join-Path $Project 'Packages/manifest.json'))) { throw 'A dedicated validation project is required.' }
New-Item -ItemType Directory -Path $Results -Force | Out-Null
$Results = (Resolve-Path -LiteralPath $Results).Path
$destination = Join-Path $Project 'Assets/Validation/Streaming'
New-Item -ItemType Directory -Path $destination -Force | Out-Null
if ($Step -ne 'Player') {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Editor'),(Join-Path $PSScriptRoot 'Runtime') -Destination $destination -Recurse -Force
}
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$log = Join-Path $Results "$stamp-Streaming-$Step.log"
$result = Join-Path $Results "$stamp-Streaming-$Step-result.txt"
$build = Join-Path $Project 'Build/Streaming/StreamingValidation.exe'
$oldResult = $env:TSMP_VALIDATION_RESULT
$oldFfmpeg = $env:TSMP_VALIDATION_FFMPEG
$oldFfprobe = $env:TSMP_VALIDATION_FFPROBE
$oldBuild = $env:TSMP_VALIDATION_BUILD
try {
    $env:TSMP_VALIDATION_RESULT = $result
    $env:TSMP_VALIDATION_FFMPEG = $Ffmpeg
    $env:TSMP_VALIDATION_FFPROBE = $Ffprobe
    $env:TSMP_VALIDATION_BUILD = $build
    if ($Step -eq 'Player') {
        $arguments = "-screen-fullscreen 0 -screen-width 640 -screen-height 360 -logFile `"$log`""
        $process = Start-Process -FilePath $build -ArgumentList $arguments -WindowStyle Hidden -PassThru
    } else {
        $method = if ($Step -eq 'Build') { 'StreamingBuildValidation.Build' } else { 'StreamingValidation.Run' }
        $arguments = "-projectPath `"$Project`" -batchmode -quit -executeMethod $method -logFile `"$log`""
        $process = Start-Process -FilePath $UnityEditor -ArgumentList $arguments -WindowStyle Hidden -PassThru
    }
    Write-Output "PID=$($process.Id) Log=$log Result=$result"
    if (!$process.WaitForExit(300000)) { $process.Kill(); throw "Validation timed out; see $log" }
    $process.Refresh()
    if (Test-Path -LiteralPath $result) { Get-Content -LiteralPath $result }
    if ($process.ExitCode -ne 0) { throw "Validation exited $($process.ExitCode); see $log" }
    if (!(Test-Path -LiteralPath $result) -or (Get-Content -LiteralPath $result -First 1) -ne 'PASS') { throw "No PASS result; see $log" }
} finally {
    $env:TSMP_VALIDATION_RESULT = $oldResult
    $env:TSMP_VALIDATION_FFMPEG = $oldFfmpeg
    $env:TSMP_VALIDATION_FFPROBE = $oldFfprobe
    $env:TSMP_VALIDATION_BUILD = $oldBuild
}
