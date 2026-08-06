. (Join-Path $PSScriptRoot 'WildlifeEnvironment.ps1')
$report = Join-Path (Get-WildlifeDataPath) 'Wildlife-InGame-Test.txt'
if (-not (Test-Path -LiteralPath $report)) {
    Write-Output 'summary=NO_REPORT'
    exit 3
}

Get-Content -LiteralPath $report | Where-Object {
    $_ -like 'summary=*' -or $_ -like 'WARN|*' -or $_ -like 'FAIL|*'
}
