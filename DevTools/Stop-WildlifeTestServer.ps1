. (Join-Path $PSScriptRoot 'WildlifeEnvironment.ps1')
$data = Get-WildlifeDataPath
$stop = Join-Path $data 'Wildlife-AutoTest.stop'
$status = Join-Path $data 'Wildlife-AutoTest.status'
Set-Content -LiteralPath $stop -Value 'STOP' -NoNewline

$deadline = (Get-Date).AddSeconds(15)
do {
    Start-Sleep -Milliseconds 100
    $existing = Get-CimInstance Win32_Process -Filter "Name = 'RimWorldWin64.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.CommandLine -like '*wildlifetestserver*' } |
        Select-Object -First 1
    if (-not $existing) {
        Remove-Item -LiteralPath $status -Force -ErrorAction SilentlyContinue
        Write-Output 'server=STOPPED'
        exit 0
    }
} while ((Get-Date) -lt $deadline)

Write-Output 'server=STOP_TIMEOUT'
exit 2
