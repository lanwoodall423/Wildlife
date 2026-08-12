param(
    [string]$DevBridgeRoot = '',
    [string]$SourceRoot = '',
    [string]$TargetModRoot = '',
    [string]$LeaseId = ''
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'WildlifeEnvironment.ps1')
. (Join-Path $PSScriptRoot 'WildlifeCoordinator.ps1')
$wildlifeRoot = Split-Path -Parent $PSScriptRoot
$source = if ([string]::IsNullOrWhiteSpace($SourceRoot)) { $wildlifeRoot } else { [IO.Path]::GetFullPath($SourceRoot) }
$target = if ([string]::IsNullOrWhiteSpace($TargetModRoot)) { $wildlifeRoot } else { [IO.Path]::GetFullPath($TargetModRoot) }
$bridgeRoot = Get-WildlifeCoordinatorRoot $DevBridgeRoot
$bridge = Join-Path $bridgeRoot 'DevBridge.cmd'
$ownedLease = $false
$lease = $null
$maintenanceReady = $false
$ready = $false
$failure = $null

try {
    if ([string]::IsNullOrWhiteSpace($LeaseId)) {
        $lease = Invoke-WildlifeCoordinatorJson $bridge @('test', 'begin', '--json')
        Assert-WildlifeCoordinatorSuccess $lease 'test begin'
        $LeaseId = [string]$lease.leaseId
        $ownedLease = $true
    }
    if ([string]::IsNullOrWhiteSpace($LeaseId)) { throw 'A DevBridge2 lease ID is required.' }

    $stopped = Invoke-WildlifeCoordinatorJson $bridge @('stop', $LeaseId, '--json')
    Assert-WildlifeCoordinatorSuccess $stopped 'stop'
    if ($stopped.maintenanceReady -ne $true -or [string]$stopped.gameState -ne 'STOPPED') {
        throw 'DevBridge2 stop did not establish the verified maintenance state.'
    }
    $maintenanceReady = $true

    $files = @('Herds.dll', 'PacksAndPredators.dll', 'Wildlife.dll', 'Wildlife.DevHarness.dll')
    $hashes = New-Object System.Collections.Generic.List[object]
    foreach ($file in $files) {
        $sourcePath = Join-Path $source ('1.6\Assemblies\' + $file)
        $targetPath = Join-Path $target ('1.6\Assemblies\' + $file)
        if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) { throw "Missing Release output: $sourcePath" }
        $targetDirectory = Split-Path -Parent $targetPath
        New-Item -ItemType Directory -Path $targetDirectory -Force | Out-Null
        $sourceHash = (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash
        if ([IO.Path]::GetFullPath($sourcePath) -ne [IO.Path]::GetFullPath($targetPath) -and
            (-not (Test-Path -LiteralPath $targetPath -PathType Leaf) -or
             (Get-FileHash -LiteralPath $targetPath -Algorithm SHA256).Hash -ne $sourceHash)) {
            Copy-Item -LiteralPath $sourcePath -Destination $targetPath -Force
        }
        $targetHash = (Get-FileHash -LiteralPath $targetPath -Algorithm SHA256).Hash
        if ($sourceHash -ne $targetHash) { throw "Hash mismatch after installing $file." }
        $hashes.Add([pscustomobject]@{ name = $file; sha256 = $targetHash })
    }

    $readyResponse = Invoke-WildlifeCoordinatorJson $bridge @('ensure-ready', $LeaseId, '--json')
    Assert-WildlifeCoordinatorSuccess $readyResponse 'ensure-ready'
    $ready = $true
    if ($ownedLease) {
        $ended = Invoke-WildlifeCoordinatorJson $bridge @('test', 'end', $LeaseId, '--json')
        Assert-WildlifeCoordinatorSuccess $ended 'test end'
    }
    [ordered]@{ status = 'READY'; leaseId = $LeaseId; files = $hashes.ToArray() } | ConvertTo-Json -Depth 4 -Compress
} catch {
    $failure = $_.Exception
}

if ($null -ne $failure) {
    if ($maintenanceReady -and -not $ready) {
        Write-Error "Installation stopped with the maintenance lease still held. Run DevBridge.cmd ensure-ready $LeaseId after correcting the outputs. $failure"
    } else { Write-Error $failure }
    exit 1
}
