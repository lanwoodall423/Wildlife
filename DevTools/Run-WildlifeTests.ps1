param(
    [int]$TimeoutSeconds = 180
)

$game = 'C:\Games\Steam\steamapps\common\RimWorld\RimWorldWin64.exe'
$data = Join-Path $env:USERPROFILE 'AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios'
$report = Join-Path $data 'Wildlife-InGame-Test.txt'
$request = Join-Path $data 'Wildlife-AutoTest.request'
$status = Join-Path $data 'Wildlife-AutoTest.status'
$process = Get-Process -Name RimWorldWin64 -ErrorAction SilentlyContinue | Select-Object -First 1
if ($null -eq $process) {
    Remove-Item -LiteralPath $request, $status -Force -ErrorAction SilentlyContinue
    $process = Start-Process -FilePath $game -ArgumentList '-quicktest', '-wildlifetestserver' -PassThru
}

$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
while ((Get-Date) -lt $deadline) {
    $process.Refresh()
    if ($process.HasExited) {
        Write-Output "summary=GAME_EXITED code=$($process.ExitCode)"
        exit 2
    }
    if (Test-Path -LiteralPath $status) {
        $serverStatus = (Get-Content -LiteralPath $status -Raw).Trim()
        if ($serverStatus -eq 'READY' -or $serverStatus -like 'DONE *') { break }
    }
    Start-Sleep -Milliseconds 250
}
$serverStatus = if (Test-Path -LiteralPath $status) {
    (Get-Content -LiteralPath $status -Raw).Trim()
} else { '' }
if ($serverStatus -ne 'READY' -and $serverStatus -notlike 'DONE *') {
    Write-Output 'summary=SERVER_TIMEOUT'
    exit 3
}

$requestId = [Guid]::NewGuid().ToString('N')
Set-Content -LiteralPath $request -Value $requestId -NoNewline
$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
while ((Get-Date) -lt $deadline) {
    $process.Refresh()
    if ($process.HasExited) {
        Write-Output "summary=GAME_EXITED code=$($process.ExitCode)"
        exit 4
    }
    if (Test-Path -LiteralPath $status) {
        $serverStatus = (Get-Content -LiteralPath $status -Raw).Trim()
        if ($serverStatus -like "DONE $requestId *") { break }
        if ($serverStatus -like 'ERROR *') {
            Write-Output "summary=SERVER_ERROR $serverStatus"
            exit 5
        }
    }
    Start-Sleep -Milliseconds 100
}
if (-not (Test-Path -LiteralPath $status) -or
    (Get-Content -LiteralPath $status -Raw).Trim() -notlike "DONE $requestId *") {
    Write-Output 'summary=TEST_TIMEOUT'
    exit 6
}
if (-not (Test-Path -LiteralPath $report)) {
    Write-Output 'summary=NO_REPORT'
    exit 4
}

$lines = Get-Content -LiteralPath $report
$lines | Where-Object {
    $_ -like 'summary=*' -or $_ -like 'WARN|*' -or $_ -like 'FAIL|*'
}

$summary = $lines | Where-Object { $_ -like 'summary=*' } | Select-Object -First 1
if ($summary -like 'summary=PASS*') { exit 0 }
exit 1
