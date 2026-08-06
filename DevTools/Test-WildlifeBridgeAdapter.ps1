param(
    [string]$ModRoot = (Join-Path $PSScriptRoot '..')
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'WildlifeEnvironment.ps1')
$modRoot = [IO.Path]::GetFullPath($ModRoot)
$descriptorPath = Join-Path $modRoot 'DevTools\DevBridge\agent.json'
if (-not (Test-Path -LiteralPath $descriptorPath -PathType Leaf)) { throw 'Wildlife owner descriptor is missing.' }
$descriptor = Get-Content -LiteralPath $descriptorPath -Raw | ConvertFrom-Json
if ($descriptor.packageId -ne 'Lan.Wildlife') { throw "Unexpected owner package ID: $($descriptor.packageId)" }
if ($descriptor.adapterDirectory -ne 'DevTools/BridgeAdapters') { throw "Unexpected adapter directory: $($descriptor.adapterDirectory)" }
if ($descriptor.adapterSource -ne 'Source/Herds/WildlifeDevBridge.cs') { throw "Unexpected adapter source: $($descriptor.adapterSource)" }
$dllPath = Join-Path $modRoot '1.6\Assemblies\Herds.dll'
$manifestFiles = @(Get-ChildItem -LiteralPath (Join-Path $modRoot 'DevTools\BridgeAdapters') -Filter '*.manifest.json' -File |
    Where-Object { $_.Name -like 'Wildlife.*' })
$manifestPath = if ($manifestFiles.Count -eq 1) { $manifestFiles[0].FullName } else { $null }

if (-not (Test-Path -LiteralPath $dllPath -PathType Leaf)) { throw "Wildlife module is missing: $dllPath" }
if ([string]::IsNullOrWhiteSpace($manifestPath)) { throw 'Wildlife owner manifest is missing.' }

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$info = Get-Item -LiteralPath $dllPath
$metadata = @(& (Join-Path $PSScriptRoot 'Read-AssemblyMetadata.ps1') -AssemblyPath $dllPath)
$mvidLine = @($metadata | Where-Object { $_ -like 'mvid=*' }) | Select-Object -First 1
if (-not $mvidLine) { throw 'The metadata reader did not return an MVID.' }
$identity = [Reflection.AssemblyName]::GetAssemblyName($dllPath).FullName
$hash = (Get-FileHash -LiteralPath $dllPath -Algorithm SHA256).Hash

if ($manifest.adapterId -ne 'Wildlife') { throw "Unexpected adapterId: $($manifest.adapterId)" }
if ($manifest.protocolMin -ne 10 -or $manifest.protocolMax -ne 10) { throw 'Wildlife manifest must target protocol 10.' }
if ($manifest.assemblySource -ne 'loaded') { throw 'Wildlife manifest is not loaded-assembly backed.' }
if ($manifest.assemblyFile) { throw 'Wildlife loaded manifest must not declare assemblyFile.' }
if ($manifest.modulePackageId -ne 'Lan.Wildlife') { throw "Unexpected modulePackageId: $($manifest.modulePackageId)" }
if ($manifest.moduleRelativePath -ne '1.6/Assemblies/Herds.dll') { throw "Unexpected moduleRelativePath: $($manifest.moduleRelativePath)" }
if (@($manifest.requiredPackageIds) -notcontains 'Lan.Wildlife') { throw 'Wildlife owner package is not required.' }
if ($manifest.assemblyIdentity -ne $identity) { throw "assemblyIdentity mismatch: expected $identity actual $($manifest.assemblyIdentity)" }
if ([long]$manifest.assemblyBytes -ne $info.Length) { throw "assemblyBytes mismatch: expected $($info.Length) actual $($manifest.assemblyBytes)" }
if ($manifest.contentHash -ne $hash) { throw "contentHash mismatch: expected $hash actual $($manifest.contentHash)" }
if ($manifest.moduleMvid -ne $mvidLine.Substring(5)) { throw "moduleMvid mismatch: expected $($mvidLine.Substring(5)) actual $($manifest.moduleMvid)" }
$allowedModes = @('PureRead', 'UiOnly', 'Reversible', 'TemporaryTestMutation', 'PersistentMutation', 'PotentiallyDestructive')
$allowedCosts = @('Normal', 'Expensive', 'Simulation')
$commandNames = @()
foreach ($command in @($manifest.commands)) {
    if ([string]::IsNullOrWhiteSpace([string]$command.name)) { throw 'Manifest command has no name.' }
    if ($commandNames -contains $command.name) { throw "Duplicate manifest command: $($command.name)" }
    $commandNames += [string]$command.name
    if ($allowedModes -notcontains [string]$command.mode) { throw "Unsupported command mode: $($command.name)=$($command.mode)" }
    if ($allowedCosts -notcontains [string]$command.cost) { throw "Unsupported command cost: $($command.name)=$($command.cost)" }
    if ([int]$command.minimumExecutionBudgetMs -le 0) { throw "Invalid execution budget: $($command.name)" }
    if ([string]::IsNullOrWhiteSpace([string]$command.providerCommand)) { throw "Missing provider command: $($command.name)" }
}
if (@($manifest.requiredPackageIds) -notcontains 'Lan.Wildlife') { throw 'Manifest does not require Lan.Wildlife.' }

Write-Output ('wildlifeAdapterVerification=PASS generation={0} commands={1} bytes={2} sha256={3}' -f
    $manifest.generation, @($manifest.commands).Count, $info.Length, $hash)
