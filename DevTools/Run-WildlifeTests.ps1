param(
    [int]$TimeoutSeconds = 180
)

$game = 'C:\Games\Steam\steamapps\common\RimWorld\RimWorldWin64.exe'
$report = Join-Path $env:USERPROFILE 'AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Wildlife-InGame-Test.txt'
$started = Get-Date
$process = Start-Process -FilePath $game -ArgumentList '-quicktest', '-wildlifetest' -PassThru

if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
    Stop-Process -Id $process.Id -Force
    Write-Output 'summary=TIMEOUT'
    exit 2
}

if (-not (Test-Path -LiteralPath $report)) {
    Write-Output 'summary=NO_REPORT'
    exit 3
}

$item = Get-Item -LiteralPath $report
if ($item.LastWriteTime -lt $started) {
    Write-Output 'summary=STALE_REPORT'
    exit 4
}

$lines = Get-Content -LiteralPath $report
$lines | Where-Object {
    $_ -like 'summary=*' -or $_ -like 'WARN|*' -or $_ -like 'FAIL|*'
}

$summary = $lines | Where-Object { $_ -like 'summary=*' } | Select-Object -First 1
if ($summary -like 'summary=PASS*') { exit 0 }
exit 1
