param(
    [int]$TimeoutSeconds = 180
)

$game = 'C:\Games\Steam\steamapps\common\RimWorld\RimWorldWin64.exe'
$data = Join-Path $env:USERPROFILE 'AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios'
$status = Join-Path $data 'Wildlife-AutoTest.status'
$bridgeStatus = Join-Path $data 'Wildlife-Bridge-Status.txt'
$existing = Get-CimInstance Win32_Process -Filter "Name = 'RimWorldWin64.exe'" -ErrorAction SilentlyContinue |
    Where-Object { $_.CommandLine -like '*wildlifetestserver*' } |
    Select-Object -First 1

if ($existing -and (Test-Path -LiteralPath $status) -and
    (Get-Content -LiteralPath $status -Raw).Trim() -match '^(READY|DONE)') {
    Write-Output 'server=READY'
    exit 0
}

Remove-Item -LiteralPath $status -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $bridgeStatus -Force -ErrorAction SilentlyContinue
$null = Start-Process -FilePath $game -ArgumentList '-quicktest', '-wildlifetestserver', '-wildlifebridge' -PassThru
$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
do {
    Start-Sleep -Milliseconds 250
    if (Test-Path -LiteralPath $status) {
        $value = (Get-Content -LiteralPath $status -Raw).Trim()
        if ($value -eq 'READY') {
            Write-Output 'server=READY'
            exit 0
        }
        if ($value -like 'ERROR *') {
            Write-Output $value
            exit 1
        }
    }
} while ((Get-Date) -lt $deadline)

Write-Output 'server=TIMEOUT'
exit 2
