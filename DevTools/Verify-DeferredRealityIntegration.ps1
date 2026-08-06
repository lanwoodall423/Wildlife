param(
    [string]$ReleaseOutputRoot = '',
    [string]$DeferredRealityFrameworkPath = [Environment]::GetEnvironmentVariable('DEFERRED_REALITY_FRAMEWORK_PATH')
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'WildlifeEnvironment.ps1')

$wildlifeRoot = Split-Path -Parent $PSScriptRoot
$frameworkRoot = if ([string]::IsNullOrWhiteSpace($DeferredRealityFrameworkPath)) {
    Get-WildlifeRequiredPath 'DEFERRED_REALITY_FRAMEWORK_PATH' 'Deferred Reality Framework checkout'
} else { [IO.Path]::GetFullPath($DeferredRealityFrameworkPath) }
$wildlifeAssembly = Join-Path $wildlifeRoot '1.6\Assemblies\Wildlife.dll'
$herdsAssembly = Join-Path $wildlifeRoot '1.6\Assemblies\Herds.dll'
$oldAdapterAssembly = Join-Path $wildlifeRoot '1.6\Assemblies\DeferredReality.Wildlife.dll'
$adapterAssembly = Join-Path $wildlifeRoot '1.6\OptionalDeferredReality\Assemblies\DeferredReality.Wildlife.dll'
$optionalDef = Join-Path $wildlifeRoot '1.6\OptionalDeferredReality\Defs\WorldObjectDefs\DeferredReality_Wildlife.xml'
$unconditionalDef = Join-Path $wildlifeRoot '1.6\Defs\WorldObjectDefs\DeferredReality_Wildlife.xml'
$loadFolders = Join-Path $wildlifeRoot 'LoadFolders.xml'
$frameworkAssembly = Join-Path $frameworkRoot '1.6\Assemblies\DeferredRealityFramework.dll'
$projectFiles = @(
    (Join-Path $wildlifeRoot 'Source\Herds\Herds.csproj'),
    (Join-Path $wildlifeRoot 'Source\Packs\PacksAndPredators.csproj'),
    (Join-Path $wildlifeRoot 'Source\Wildlife\Wildlife.csproj'),
    (Join-Path $wildlifeRoot 'Source\Wildlife\DeferredReality.Wildlife.csproj')
)

foreach ($path in @($wildlifeAssembly, $herdsAssembly, $adapterAssembly, $optionalDef, $frameworkAssembly)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Missing required integration output: $path"
    }
}

foreach ($project in $projectFiles) {
    if (-not (Test-Path -LiteralPath $project -PathType Leaf)) {
        throw "Missing Wildlife project: $project"
    }
    if (([IO.File]::ReadAllText($project) -match '(?i)(?:[A-Z]:[\\/]|\\\\[^\\]+[\\/])')) {
        throw "Machine-specific absolute path remains in project: $project"
    }
}

if (Test-Path -LiteralPath $oldAdapterAssembly -PathType Leaf) {
    throw "Obsolete unconditional adapter remains: $oldAdapterAssembly"
}
if (Test-Path -LiteralPath $unconditionalDef -PathType Leaf) {
    throw "Obsolete unconditional Deferred Reality def remains: $unconditionalDef"
}

function Get-AssemblyName([string] $path) {
    return [Reflection.AssemblyName]::GetAssemblyName($path).Name
}

function Get-Metadata([string]$path) {
    return @(& (Join-Path $PSScriptRoot 'Read-AssemblyMetadata.ps1') -AssemblyPath $path)
}

function Has-Reference([string[]]$metadata, [string]$name) {
    return @($metadata | Where-Object { $_ -eq ('reference=' + $name + '|0.0.0.0') -or $_ -like ('reference=' + $name + '|*') }).Count -gt 0
}

if ((Get-AssemblyName $wildlifeAssembly) -ne 'Wildlife') {
    throw 'The normal Wildlife assembly has an unexpected identity.'
}
if ((Get-AssemblyName $herdsAssembly) -ne 'Herds') {
    throw 'The normal Herds assembly has an unexpected identity.'
}
if ((Get-AssemblyName $adapterAssembly) -ne 'DeferredReality.Wildlife') {
    throw 'The optional adapter assembly has an unexpected identity.'
}
if ((Get-AssemblyName $frameworkAssembly) -ne 'DeferredRealityFramework') {
    throw 'The framework assembly has an unexpected identity.'
}

$wildlifeReferences = Get-Metadata $wildlifeAssembly
$herdsReferences = Get-Metadata $herdsAssembly
$adapterReferences = Get-Metadata $adapterAssembly
foreach ($name in @('DeferredRealityFramework', 'RimWorldDevBridge', 'InsightCanvas')) {
    if (Has-Reference $wildlifeReferences $name -or Has-Reference $herdsReferences $name) {
        throw "A normal Wildlife assembly references unsupported integration assembly $name."
    }
    if (($name -ne 'DeferredRealityFramework') -and (Has-Reference $adapterReferences $name)) {
        throw "The optional adapter references unsupported integration assembly $name."
    }
}
if (-not (Has-Reference $adapterReferences 'DeferredRealityFramework')) {
    throw 'The optional adapter does not reference DeferredRealityFramework.'
}

function Compare-ReleaseOutput([string] $packagedPath, [string] $fileName) {
    $releasePath = if ([string]::IsNullOrWhiteSpace($ReleaseOutputRoot)) {
        $packagedPath
    } else {
        Join-Path $ReleaseOutputRoot $fileName
    }
    if (-not (Test-Path -LiteralPath $releasePath -PathType Leaf)) {
        throw "Missing verified Release output for $fileName`: $releasePath"
    }
    $packagedHash = (Get-FileHash -LiteralPath $packagedPath -Algorithm SHA256).Hash
    $releaseHash = (Get-FileHash -LiteralPath $releasePath -Algorithm SHA256).Hash
    if ($packagedHash -ne $releaseHash) {
        throw "Packaged output does not match verified Release output for $fileName."
    }
    return [pscustomobject]@{
        name = $fileName
        packaged = $packagedHash
        release = $releaseHash
        mode = if ($releasePath -eq $packagedPath) { 'direct-release-output' } else { 'separate-release-output' }
    }
}

$releaseComparisons = @(
    (Compare-ReleaseOutput $wildlifeAssembly 'Wildlife.dll'),
    (Compare-ReleaseOutput $herdsAssembly 'Herds.dll'),
    (Compare-ReleaseOutput $adapterAssembly 'DeferredReality.Wildlife.dll'),
    (Compare-ReleaseOutput $frameworkAssembly 'DeferredRealityFramework.dll')
)

$loadText = [IO.File]::ReadAllText($loadFolders)
if ($loadText -notmatch 'IfModActive="lan\.deferredreality\.framework"') {
    throw 'LoadFolders.xml does not conditionally load the adapter.'
}

Write-Output ('PASS: optional Wildlife integration package; Wildlife bytes={0}, Herds bytes={1}, adapter bytes={2}, framework bytes={3}; release comparisons={4}' -f `
    (Get-Item -LiteralPath $wildlifeAssembly).Length,
    (Get-Item -LiteralPath $herdsAssembly).Length,
    (Get-Item -LiteralPath $adapterAssembly).Length,
    (Get-Item -LiteralPath $frameworkAssembly).Length,
    (($releaseComparisons | ConvertTo-Json -Compress)))
