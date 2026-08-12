param(
    [ValidateSet('full')][string]$Suite = 'full',
    [ValidateSet('destructive')][string]$Mode = 'destructive',
    [int]$TimeoutSeconds = 900,
    [string]$DevBridgeRoot = '',
    [string]$SaveDataPath = ''
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'WildlifeEnvironment.ps1')
. (Join-Path $PSScriptRoot 'WildlifeCoordinator.ps1')

if ($TimeoutSeconds -lt 1) { throw 'TimeoutSeconds must be positive.' }
$bridgeRoot = Get-WildlifeCoordinatorRoot $DevBridgeRoot
$bridge = Join-Path $bridgeRoot 'DevBridge.cmd'
$saveData = Get-WildlifeSaveDataPath $SaveDataPath
$requestDirectory = Join-Path $saveData 'Wildlife-TestRequests'
New-Item -ItemType Directory -Path $requestDirectory -Force | Out-Null

$lease = Invoke-WildlifeCoordinatorJson $bridge @('test', 'begin', '--json')
Assert-WildlifeCoordinatorSuccess $lease 'test begin'
$leaseId = [string]$lease.leaseId
if ([string]::IsNullOrWhiteSpace($leaseId)) { throw 'DevBridge2 test begin returned no lease ID.' }

$runError = $null
$passed = $false
try {
    $status = Invoke-WildlifeCoordinatorJson $bridge @('status', '--json')
    Assert-WildlifeCoordinatorSuccess $status 'status'
    $identity = Get-WildlifeLaunchIdentity $bridgeRoot $status
    $requestId = [Guid]::NewGuid().ToString('N')
    $requestPath = Join-Path $requestDirectory ($requestId + '.request.json')
    $temporaryPath = $requestPath + '.tmp-' + [Guid]::NewGuid().ToString('N')
    $request = [ordered]@{
        requestId = $requestId
        launchId = $identity.launchId
        generation = $identity.generation
        suite = $Suite
        mode = $Mode
        agent = [string]$lease.agent
        leaseId = $leaseId
        requestedUtc = [DateTime]::UtcNow.ToString('o', [Globalization.CultureInfo]::InvariantCulture)
    }
    try {
        [IO.File]::WriteAllText($temporaryPath, ($request | ConvertTo-Json -Compress), [Text.UTF8Encoding]::new($false))
        [IO.File]::Move($temporaryPath, $requestPath)
    } finally {
        if (Test-Path -LiteralPath $temporaryPath -PathType Leaf) { Remove-Item -LiteralPath $temporaryPath -Force }
    }

    $resultPath = Join-Path $requestDirectory ($requestId + '.result.json')
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    $result = $null
    while ([DateTime]::UtcNow -lt $deadline) {
        if (Test-Path -LiteralPath $resultPath -PathType Leaf) {
            $result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
            break
        }
        Start-Sleep -Seconds 1
    }
    if ($null -eq $result) { throw "Wildlife request $requestId timed out after $TimeoutSeconds seconds." }
    if ([string]$result.requestId -ne $requestId -or [string]$result.launchId -ne $identity.launchId -or
        [int]$result.generation -ne $identity.generation) {
        throw "Wildlife result identity mismatch for request $requestId."
    }
    if ([string]$result.status -ne 'PASS' -or $result.success -ne $true) {
        throw "Wildlife suite reported $($result.status): $($result.error)"
    }
    $passed = $true
    [ordered]@{ status = 'PASS'; requestId = $requestId; leaseId = $leaseId; launchId = $identity.launchId; generation = $identity.generation; reportPath = [string]$result.reportPath } | ConvertTo-Json -Compress
} catch {
    $runError = $_.Exception
} finally {
    try {
        $ended = Invoke-WildlifeCoordinatorJson $bridge @('test', 'end', $leaseId, '--json')
        Assert-WildlifeCoordinatorSuccess $ended 'test end'
    } catch {
        if ($null -eq $runError) { $runError = $_.Exception }
    }
}
if ($null -ne $runError) { throw $runError }
if (-not $passed) { throw 'Wildlife suite did not pass.' }
