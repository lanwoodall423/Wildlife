param(
    [int]$TimeoutSeconds = 15
)

$tools = Split-Path -Parent $MyInvocation.MyCommand.Path
$serverOutput = & (Join-Path $tools 'Start-WildlifeTestServer.ps1')
if ($LASTEXITCODE -ne 0) {
    $serverOutput
    exit $LASTEXITCODE
}

. (Join-Path $tools 'WildlifeEnvironment.ps1')
$data = Get-WildlifeDataPath
$requestPath = Join-Path $data 'Wildlife-AutoTest.request'
$statusPath = Join-Path $data 'Wildlife-AutoTest.status'
$reportPath = Join-Path $data 'Wildlife-InGame-Test.txt'
$request = [Guid]::NewGuid().ToString('N')
Set-Content -LiteralPath $requestPath -Value $request -NoNewline

$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
do {
    Start-Sleep -Milliseconds 50
    if (Test-Path -LiteralPath $statusPath) {
        $status = (Get-Content -LiteralPath $statusPath -Raw).Trim()
        if ($status -like "DONE $request *") {
            Get-Content -LiteralPath $reportPath | Where-Object {
                $_ -like 'summary=*' -or $_ -like 'WARN|*' -or $_ -like 'FAIL|*'
            }
            if ($status -like '* PASS') { exit 0 }
            exit 1
        }
        if ($status -like 'ERROR *') {
            Write-Output $status
            exit 1
        }
    }
} while ((Get-Date) -lt $deadline)

Write-Output 'summary=REQUEST_TIMEOUT'
exit 2
