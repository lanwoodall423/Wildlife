param(
    [int]$TimeoutSeconds = 180
)

. (Join-Path $PSScriptRoot 'WildlifeEnvironment.ps1')
$game = Get-WildlifeGamePath
$data = Get-WildlifeDataPath
$status = Join-Path $data 'Wildlife-AutoTest.status'
$existing = Get-CimInstance Win32_Process -Filter "Name = 'RimWorldWin64.exe'" -ErrorAction SilentlyContinue |
    Where-Object { $_.CommandLine -like '*-quicktest*' -and $_.CommandLine -like '*-wildlifetestserver*' } |
    Select-Object -First 1

if ($existing -and (Test-Path -LiteralPath $status) -and
    (Get-Content -LiteralPath $status -Raw).Trim() -match '^(READY|DONE)') {
    Write-Output 'server=READY'
    exit 0
}

Remove-Item -LiteralPath $status -Force -ErrorAction SilentlyContinue
$null = Start-Process -FilePath $game -ArgumentList '-quicktest', '-wildlifetestserver' -PassThru
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
