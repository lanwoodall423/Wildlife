$ErrorActionPreference = 'Stop'

$wildlifeRoot = Split-Path -Parent $PSScriptRoot
$frameworkRoot = Join-Path (Split-Path -Parent $wildlifeRoot) 'DeferredRealityFramework'
$wildlifeAssembly = Join-Path $wildlifeRoot '1.6\Assemblies\Wildlife.dll'
$oldAdapterAssembly = Join-Path $wildlifeRoot '1.6\Assemblies\DeferredReality.Wildlife.dll'
$adapterAssembly = Join-Path $wildlifeRoot '1.6\OptionalDeferredReality\Assemblies\DeferredReality.Wildlife.dll'
$optionalDef = Join-Path $wildlifeRoot '1.6\OptionalDeferredReality\Defs\WorldObjectDefs\DeferredReality_Wildlife.xml'
$unconditionalDef = Join-Path $wildlifeRoot '1.6\Defs\WorldObjectDefs\DeferredReality_Wildlife.xml'
$loadFolders = Join-Path $wildlifeRoot 'LoadFolders.xml'
$frameworkAssembly = Join-Path $frameworkRoot '1.6\Assemblies\DeferredRealityFramework.dll'

foreach ($path in @($wildlifeAssembly, $adapterAssembly, $optionalDef, $frameworkAssembly)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Missing required integration output: $path"
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

if ((Get-AssemblyName $wildlifeAssembly) -ne 'Wildlife') {
    throw 'The normal Wildlife assembly has an unexpected identity.'
}
if ((Get-AssemblyName $adapterAssembly) -ne 'DeferredReality.Wildlife') {
    throw 'The optional adapter assembly has an unexpected identity.'
}
if ((Get-AssemblyName $frameworkAssembly) -ne 'DeferredRealityFramework') {
    throw 'The framework assembly has an unexpected identity.'
}

$wildlifeReferences = ([Reflection.Assembly]::ReflectionOnlyLoadFrom($wildlifeAssembly)).GetReferencedAssemblies()
if ($wildlifeReferences.Name -contains 'DeferredRealityFramework') {
    throw 'The normal Wildlife assembly references DeferredRealityFramework.'
}
$adapterReferences = ([Reflection.Assembly]::ReflectionOnlyLoadFrom($adapterAssembly)).GetReferencedAssemblies()
if (-not ($adapterReferences.Name -contains 'DeferredRealityFramework')) {
    throw 'The optional adapter does not reference DeferredRealityFramework.'
}

$loadText = [IO.File]::ReadAllText($loadFolders)
if ($loadText -notmatch 'IfModActive="lan\.deferredreality\.framework"') {
    throw 'LoadFolders.xml does not conditionally load the adapter.'
}

Write-Output ('PASS: optional Wildlife integration package; Wildlife bytes={0}, adapter bytes={1}, framework bytes={2}' -f `
    (Get-Item -LiteralPath $wildlifeAssembly).Length,
    (Get-Item -LiteralPath $adapterAssembly).Length,
    (Get-Item -LiteralPath $frameworkAssembly).Length)
