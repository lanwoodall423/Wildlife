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
$removedLegacyHarness = $false
$quarantinedLegacyCompanion = $false
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

    $files = @('Herds.dll', 'PacksAndPredators.dll', 'Wildlife.dll')
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

    $legacyHarnessName = 'Wildlife.' + 'DevHarness.dll'
    $legacyHarnessPath = Join-Path $target (Join-Path '1.6\Assemblies' $legacyHarnessName)
    if (Test-Path -LiteralPath $legacyHarnessPath -PathType Leaf) {
        Remove-Item -LiteralPath $legacyHarnessPath -Force
        $removedLegacyHarness = $true
    }

    $sourceModsRoot = Split-Path -Parent $source
    $targetModsRoot = Split-Path -Parent $target
    $sourceGameRoot = Split-Path -Parent $sourceModsRoot
    $targetGameRoot = Split-Path -Parent $targetModsRoot
    $companionSourcePath = Join-Path $sourceGameRoot 'BridgeTools\Wildlife\Wildlife.BridgeTools.dll'
    $companionTargetDirectory = Join-Path $targetGameRoot 'BridgeTools\Wildlife'
    $companionTargetPath = Join-Path $companionTargetDirectory 'Wildlife.BridgeTools.dll'
    $legacyCompanionDirectory = Join-Path $targetModsRoot 'BridgeTools\Wildlife'
    if (([IO.Path]::GetFullPath($legacyCompanionDirectory) -ne [IO.Path]::GetFullPath($companionTargetDirectory)) -and
        (Test-Path -LiteralPath $legacyCompanionDirectory -PathType Container)) {
        $quarantineRoot = Join-Path $targetModsRoot '_quarantine'
        New-Item -ItemType Directory -Path $quarantineRoot -Force | Out-Null
        $quarantinePath = Join-Path $quarantineRoot ('Wildlife-BridgeTools-legacy-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
        Move-Item -LiteralPath $legacyCompanionDirectory -Destination $quarantinePath
        $quarantinedLegacyCompanion = $true
    }
    if (-not (Test-Path -LiteralPath $companionSourcePath -PathType Leaf)) {
        throw "Missing Release output: $companionSourcePath"
    }
    $sdkTargetPath = Join-Path $companionTargetDirectory 'RimBridgeServer.Sdk.dll'
    if (Test-Path -LiteralPath $sdkTargetPath -PathType Leaf) {
        throw "RimBridgeServer.Sdk.dll must not be deployed beside the Wildlife companion: $sdkTargetPath"
    }
    New-Item -ItemType Directory -Path $companionTargetDirectory -Force | Out-Null
    $companionHash = (Get-FileHash -LiteralPath $companionSourcePath -Algorithm SHA256).Hash
    if ([IO.Path]::GetFullPath($companionSourcePath) -ne [IO.Path]::GetFullPath($companionTargetPath) -and
        (-not (Test-Path -LiteralPath $companionTargetPath -PathType Leaf) -or
         (Get-FileHash -LiteralPath $companionTargetPath -Algorithm SHA256).Hash -ne $companionHash)) {
        Copy-Item -LiteralPath $companionSourcePath -Destination $companionTargetPath -Force
    }
    $installedCompanionHash = (Get-FileHash -LiteralPath $companionTargetPath -Algorithm SHA256).Hash
    if ($companionHash -ne $installedCompanionHash) { throw 'Hash mismatch after installing Wildlife.BridgeTools.dll.' }
    $hashes.Add([pscustomobject]@{ name = 'Wildlife.BridgeTools.dll'; sha256 = $installedCompanionHash })

    $readyResponse = Invoke-WildlifeCoordinatorJson $bridge @('ensure-ready', $LeaseId, '--json')
    Assert-WildlifeCoordinatorSuccess $readyResponse 'ensure-ready'
    $ready = $true
    if ($ownedLease) {
        $ended = Invoke-WildlifeCoordinatorJson $bridge @('test', 'end', $LeaseId, '--json')
        Assert-WildlifeCoordinatorSuccess $ended 'test end'
    }
    [ordered]@{ status = 'READY'; leaseId = $LeaseId; removedLegacyHarness = $removedLegacyHarness; quarantinedLegacyCompanion = $quarantinedLegacyCompanion; files = $hashes.ToArray() } | ConvertTo-Json -Depth 4 -Compress
} catch {
    $failure = $_.Exception
}

if ($null -ne $failure) {
    if ($maintenanceReady -and -not $ready) {
        Write-Error "Installation stopped with the maintenance lease still held. Run DevBridge.cmd ensure-ready $LeaseId after correcting the outputs. $failure"
    } else { Write-Error $failure }
    exit 1
}
