param(
    [string]$ModulePath = (Join-Path $PSScriptRoot '..\1.6\Assemblies\Herds.dll'),
    [string]$AdapterDirectory = (Join-Path $PSScriptRoot 'BridgeAdapters')
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'WildlifeEnvironment.ps1')
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$descriptorPath = Join-Path $repositoryRoot 'DevTools\DevBridge\agent.json'
if (-not (Test-Path -LiteralPath $descriptorPath -PathType Leaf)) { throw "Wildlife owner descriptor is missing: $descriptorPath" }
$descriptor = Get-Content -LiteralPath $descriptorPath -Raw | ConvertFrom-Json
if ($descriptor.packageId -ne 'Lan.Wildlife') { throw "Unexpected Wildlife owner package ID: $($descriptor.packageId)" }
if ($descriptor.adapterDirectory -ne 'DevTools/BridgeAdapters') { throw "Unexpected adapter directory: $($descriptor.adapterDirectory)" }
if ($descriptor.adapterSource -ne 'Source/Herds/WildlifeDevBridge.cs') { throw "Unexpected adapter source: $($descriptor.adapterSource)" }
$modulePath = [IO.Path]::GetFullPath($ModulePath)
$adapterDirectory = [IO.Path]::GetFullPath($AdapterDirectory)
if (-not (Test-Path -LiteralPath $modulePath -PathType Leaf)) { throw "Wildlife module is missing: $modulePath" }
if (-not (Test-Path -LiteralPath $adapterDirectory -PathType Container)) {
    New-Item -ItemType Directory -Force -Path $adapterDirectory | Out-Null
}

$manifestFiles = @(Get-ChildItem -LiteralPath $adapterDirectory -Filter '*.manifest.json' -File)
if ($manifestFiles.Count -ne 1) { throw "Expected exactly one Wildlife manifest template." }
$manifest = Get-Content -LiteralPath $manifestFiles[0].FullName -Raw | ConvertFrom-Json
if ($manifest.protocolMin -ne 10 -or $manifest.protocolMax -ne 10) {
    throw 'Wildlife owner manifest must target DevBridge protocol 10.'
}
$bytes = [IO.File]::ReadAllBytes($modulePath)
$hash = (Get-FileHash -LiteralPath $modulePath -Algorithm SHA256).Hash.ToUpperInvariant()
$assembly = [Reflection.AssemblyName]::GetAssemblyName($modulePath)
$metadata = @(& (Join-Path $PSScriptRoot 'Read-AssemblyMetadata.ps1') -AssemblyPath $modulePath)
$mvidLine = @($metadata | Where-Object { $_ -like 'mvid=*' }) | Select-Object -First 1
if (-not $mvidLine) { throw 'The metadata reader did not return an MVID.' }
$mvid = $mvidLine.Substring(5)
$sameBinding = [int64]$manifest.assemblyBytes -eq $bytes.Length -and
    [string]::Equals([string]$manifest.contentHash, $hash, [StringComparison]::OrdinalIgnoreCase) -and
    [string]::Equals([string]$manifest.assemblyIdentity, $assembly.FullName, [StringComparison]::Ordinal)
$generation = if ($sameBinding) { [string]$manifest.generation } else { (Get-Date).ToUniversalTime().ToString('yyyyMMddHHmmssfff') }
if ([string]::IsNullOrWhiteSpace($generation)) { throw 'Wildlife manifest generation is missing.' }

$manifest.manifestVersion = 2
$manifest.adapterId = 'Wildlife'
$manifest.assemblySource = 'loaded'
$manifest.assemblyFile = $null
$manifest.modulePackageId = 'Lan.Wildlife'
$manifest.moduleRelativePath = '1.6/Assemblies/Herds.dll'
$manifest.assemblyIdentity = $assembly.FullName
$manifest.assemblyBytes = $bytes.Length
$manifest.contentHash = $hash
$manifest.moduleMvid = $mvid
$manifest.generation = $generation
$manifest.buildUtc = (Get-Date).ToUniversalTime().ToString('o')
if ($null -eq $manifest.requiredPackageIds -or -not @($manifest.requiredPackageIds).Contains('Lan.Wildlife')) {
    $manifest.requiredPackageIds = @('Lan.Wildlife')
}

$temporary = Join-Path $adapterDirectory ('.Wildlife.' + $generation + '.manifest.json.tmp')
$target = Join-Path $adapterDirectory ('Wildlife.' + $generation + '.manifest.json')
try {
    $json = $manifest | ConvertTo-Json -Depth 20
    [IO.File]::WriteAllText($temporary, $json, (New-Object Text.UTF8Encoding($false)))
    foreach ($file in Get-ChildItem -LiteralPath $adapterDirectory -Filter '*.manifest.json' -File) {
        Remove-Item -LiteralPath $file.FullName -Force
    }
    Move-Item -LiteralPath $temporary -Destination $target -Force
} finally {
    if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force }
}

Write-Output ('wildlifeManifest=PASS generation={0} bytes={1} sha256={2} mvid={3}' -f
    $generation, $bytes.Length, $hash, $mvid)
