$report = Join-Path $env:USERPROFILE 'AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Wildlife-InGame-Test.txt'
if (-not (Test-Path -LiteralPath $report)) {
    Write-Output 'summary=NO_REPORT'
    exit 3
}

Get-Content -LiteralPath $report | Where-Object {
    $_ -like 'summary=*' -or $_ -like 'WARN|*' -or $_ -like 'FAIL|*'
}
