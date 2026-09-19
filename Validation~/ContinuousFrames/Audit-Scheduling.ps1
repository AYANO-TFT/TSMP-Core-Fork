param(
    [Parameter(Mandatory = $true)][string]$ResultsDirectory
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $ResultsDirectory).Path
$audit = @()
foreach ($run in Get-ChildItem -LiteralPath $root -Directory) {
    $summary = Join-Path $run.FullName 'summary.csv'
    if (-not (Test-Path -LiteralPath $summary)) { continue }
    foreach ($row in Import-Csv -LiteralPath $summary) {
        $sent = @(Import-Csv -LiteralPath (Join-Path $run.FullName ($row.case + '-publications.csv')) | Where-Object inMeasuredWindow -eq 'True')
        $received = @(Import-Csv -LiteralPath (Join-Path $run.FullName ($row.case + '-applications.csv')) | Where-Object inMeasuredWindow -eq 'True')
        $sentIds = @{}
        foreach ($entry in $sent) { $sentIds[$entry.id] = $true }
        $receivedIds = @{}
        foreach ($entry in $received) {
            if (-not $sentIds.ContainsKey($entry.id) -or $receivedIds.ContainsKey($entry.id)) { throw "Invalid application: $($run.Name)/$($row.case)" }
            $receivedIds[$entry.id] = $true
        }
        if ($sent.Count -ne [int]$row.published -or $received.Count -ne [int]$row.receivedAfterDrain) { throw "Count mismatch: $($run.Name)/$($row.case)" }
        $missing = @($sent | Where-Object { -not $receivedIds.ContainsKey($_.id) })
        $path = Join-Path $run.FullName ($row.case + '-timing.csv')
        $beforeUpdate = $null
        $beforeLateUpdate = $null
        $afterPreviousLateUpdate = $null
        $retries = $null
        $missingSeenBusy = $null
        if (Test-Path -LiteralPath $path) {
            $timing = @(Import-Csv -LiteralPath $path)
            $seen = @{}
            $seen[$timing[0].completionTime] = $true
            $phases = @{}
            $busyIds = @{}
            $captureFrames = @{}
            $lastCapture = -1
            foreach ($sample in $timing) {
                if ($sample.phase -eq 'Update' -and $sample.busy -eq 'True') { $busyIds[$sample.publishedId] = $true }
                if ([int]$sample.captures -ne $lastCapture) {
                    if ($captureFrames.ContainsKey($sample.captureFrame)) { throw "Multiple captures in one frame: $($run.Name)/$($row.case)" }
                    $captureFrames[$sample.captureFrame] = $true
                    $lastCapture = [int]$sample.captures
                }
                if ([double]$sample.completionTime -le 0 -or $seen.ContainsKey($sample.completionTime)) { continue }
                $seen[$sample.completionTime] = $true
                $key = if ([int]$sample.completionFrame -lt [int]$sample.frame) { 'PreviousFrame' } else { $sample.phase }
                if (-not $phases.ContainsKey($key)) { $phases[$key] = 0 }
                $phases[$key]++
            }
            $beforeUpdate = [int]$phases['Update']
            $beforeLateUpdate = [int]$phases['LateUpdate']
            $afterPreviousLateUpdate = [int]$phases['PreviousFrame']
            $retries = [int]$timing[-1].retries
            $missingSeenBusy = @($missing | Where-Object { $busyIds.ContainsKey($_.id) }).Count
        }
        $audit += [pscustomobject]@{
            run = $run.Name; case = $row.case; published = $sent.Count; received = $received.Count; missing = $missing.Count
            missingSeenBusy = $missingSeenBusy; beforeUpdate = $beforeUpdate; beforeLateUpdate = $beforeLateUpdate
            afterPreviousLateUpdate = $afterPreviousLateUpdate; retries = $retries
        }
    }
}
$audit | Export-Csv -NoTypeInformation -LiteralPath (Join-Path $root 'scheduling-audit.csv')
$audit | Format-Table -AutoSize
