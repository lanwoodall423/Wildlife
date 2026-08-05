param(
    [string]$ModulePath = (Join-Path $PSScriptRoot '..\1.6\Assemblies\Herds.dll'),
    [string]$AdapterDirectory = (Join-Path $PSScriptRoot 'BridgeAdapters')
)

$ErrorActionPreference = 'Stop'
$modulePath = [IO.Path]::GetFullPath($ModulePath)
$adapterDirectory = [IO.Path]::GetFullPath($AdapterDirectory)
if (-not (Test-Path -LiteralPath $modulePath -PathType Leaf)) { throw "Wildlife module is missing: $modulePath" }
if (-not (Test-Path -LiteralPath $adapterDirectory -PathType Container)) {
    New-Item -ItemType Directory -Force -Path $adapterDirectory | Out-Null
}

$manifestFiles = @(Get-ChildItem -LiteralPath $adapterDirectory -Filter '*.manifest.json' -File)
if ($manifestFiles.Count -ne 1) { throw "Expected exactly one Wildlife manifest template." }
$manifest = Get-Content -LiteralPath $manifestFiles[0].FullName -Raw | ConvertFrom-Json
$bytes = [IO.File]::ReadAllBytes($modulePath)
$hash = (Get-FileHash -LiteralPath $modulePath -Algorithm SHA256).Hash.ToUpperInvariant()
$assembly = [Reflection.AssemblyName]::GetAssemblyName($modulePath)
$reflectionAssembly = [Reflection.Assembly]::ReflectionOnlyLoadFrom($modulePath)
$mvid = $reflectionAssembly.ManifestModule.ModuleVersionId.ToString('D')
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
