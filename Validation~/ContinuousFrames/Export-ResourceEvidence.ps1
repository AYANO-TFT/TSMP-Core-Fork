param(
    [Parameter(Mandatory = $true)][string]$ResultsDirectory,
    [Parameter(Mandatory = $true)][string]$Destination
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $ResultsDirectory).Path
[IO.Directory]::CreateDirectory($Destination) | Out-Null
$tables = @{}
foreach ($file in @('cpu.csv', 'helpers.csv', 'counters.csv', 'memory-warm.csv', 'memory-end.csv', 'gpu.csv', 'gpu-trace.csv')) {
    $tables[$file] = [Collections.Generic.List[object]]::new()
}
$delivery = [Collections.Generic.List[object]]::new()
$metadata = [Collections.Generic.List[string]]::new()
foreach ($run in @('native-v2', 'udon-v2', 'native-one-slot', 'gpu-editor-v2')) {
    $directory = Join-Path $root $run
    $status = Get-Content -LiteralPath (Join-Path $directory 'status.txt') -Raw
    if (-not $status.StartsWith('PASS')) { throw "Incomplete run: $run" }
    $metadata.Add("[$run]")
    $metadata.Add((Get-Content -LiteralPath (Join-Path $directory 'environment.txt') -Raw))
    foreach ($case in Get-ChildItem -LiteralPath $directory -Directory -Filter 'profile-*' | Sort-Object Name) {
        foreach ($file in $tables.Keys) {
            if ($file.StartsWith('gpu') -and $run -ne 'gpu-editor-v2') { continue }
            if (-not $file.StartsWith('gpu') -and $run -eq 'gpu-editor-v2') { continue }
            $path = Join-Path $case.FullName $file
            if (-not (Test-Path -LiteralPath $path)) { continue }
            foreach ($row in Import-Csv -LiteralPath $path) {
                $record = [ordered]@{ run = $run; case = $case.Name }
                foreach ($property in $row.PSObject.Properties) { $record[$property.Name] = $property.Value }
                $tables[$file].Add([PSCustomObject]$record)
            }
        }
        $record = [ordered]@{ run = $run; case = $case.Name }
        foreach ($line in Get-Content -LiteralPath (Join-Path $case.FullName 'delivery.txt')) {
            $pair = $line.Split('=', 2)
            $record[$pair[0]] = $pair[1]
        }
        $delivery.Add([PSCustomObject]$record)
        if ($run -eq 'gpu-editor-v2') {
            $check = Get-Content -LiteralPath (Join-Path $case.FullName 'gpu-replay.txt') -Raw
            if (-not $check.StartsWith('PASS')) { throw "GPU replay failed: $($case.Name)" }
            $metadata.Add($case.Name + ': ' + $check)
        }
    }
}
foreach ($file in $tables.Keys) {
    $tables[$file] | Export-Csv -LiteralPath (Join-Path $Destination $file) -NoTypeInformation -Encoding utf8
}
$delivery | Export-Csv -LiteralPath (Join-Path $Destination 'delivery.csv') -NoTypeInformation -Encoding utf8
$metadata.Add((Get-Content -LiteralPath (Join-Path $root 'gpu-editor-v2/profile-small30/hardware.txt') -Raw))
$metadata.Add((Get-Content -LiteralPath (Join-Path $root 'native-v2/profile-small30/allocation-counter.txt') -Raw))
$metadata.Add((Get-Content -LiteralPath (Join-Path $root 'native-v2/Player/ContinuousFrames.build-report.txt') -Raw))
$metadata.Add('Udon compilation evidence:')
foreach ($line in Select-String -LiteralPath (Join-Path $root 'udon-v2/Editor.log') -Pattern 'Compile of .*scripts finished') {
    $metadata.Add($line.Line)
}
$metadata | Set-Content -LiteralPath (Join-Path $Destination 'environment.txt') -Encoding utf8
