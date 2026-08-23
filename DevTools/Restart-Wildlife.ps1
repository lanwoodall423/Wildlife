param(
    [string]$DevBridgeRoot = '',
    [string]$LeaseId = ''
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'WildlifeEnvironment.ps1')
. (Join-Path $PSScriptRoot 'WildlifeCoordinator.ps1')
$bridgeRoot = Get-WildlifeCoordinatorRoot $DevBridgeRoot
$bridge = Join-Path $bridgeRoot 'DevBridge.cmd'
$registration = Invoke-WildlifeCoordinatorJson $bridge @('project', 'register', 'wildlife', '--json')
Assert-WildlifeCoordinatorSuccess $registration 'project register wildlife'
$arguments = if ([string]::IsNullOrWhiteSpace($LeaseId)) {
    @('restart', '--json')
} else {
    @('restart', $LeaseId, '--json')
}
$restart = Invoke-WildlifeCoordinatorJson $bridge $arguments
Assert-WildlifeCoordinatorSuccess $restart 'restart'
$ready = Invoke-WildlifeCoordinatorJson $bridge @('wait-ready', '--json')
Assert-WildlifeCoordinatorSuccess $ready 'wait-ready'
$status = Invoke-WildlifeCoordinatorJson $bridge @('status', '--json')
Assert-WildlifeCoordinatorSuccess $status 'status after wait-ready'
$identity = Get-WildlifeLaunchIdentity $bridgeRoot $status
[ordered]@{ status = 'READY'; generation = $identity.generation; launchId = $identity.launchId; pid = $status.rimworldPid } | ConvertTo-Json -Compress
