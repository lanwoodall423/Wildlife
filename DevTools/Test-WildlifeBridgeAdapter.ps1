param(
    [string]$ModRoot = (Join-Path $PSScriptRoot '..')
)

$ErrorActionPreference = 'Stop'
$modRoot = [IO.Path]::GetFullPath($ModRoot)
$dllPath = Join-Path $modRoot '1.6\Assemblies\Herds.dll'
$manifestPath = Get-ChildItem -LiteralPath (Join-Path $modRoot 'DevTools\BridgeAdapters') -Filter '*.manifest.json' -File |
    Where-Object { $_.Name -like 'Wildlife.*' } |
    Sort-Object Name -Descending |
    Select-Object -First 1 -ExpandProperty FullName

if (-not (Test-Path -LiteralPath $dllPath -PathType Leaf)) { throw "Wildlife module is missing: $dllPath" }
if ([string]::IsNullOrWhiteSpace($manifestPath)) { throw 'Wildlife owner manifest is missing.' }

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$info = Get-Item -LiteralPath $dllPath
$assembly = [Reflection.Assembly]::ReflectionOnlyLoadFrom($dllPath)
$identity = [Reflection.AssemblyName]::GetAssemblyName($dllPath).FullName
$hash = (Get-FileHash -LiteralPath $dllPath -Algorithm SHA256).Hash

if ($manifest.adapterId -ne 'Wildlife') { throw "Unexpected adapterId: $($manifest.adapterId)" }
if ($manifest.assemblySource -ne 'loaded') { throw 'Wildlife manifest is not loaded-assembly backed.' }
if ($manifest.assemblyFile) { throw 'Wildlife loaded manifest must not declare assemblyFile.' }
if ($manifest.modulePackageId -ne 'Lan.Wildlife') { throw "Unexpected modulePackageId: $($manifest.modulePackageId)" }
if ($manifest.moduleRelativePath -ne '1.6/Assemblies/Herds.dll') { throw "Unexpected moduleRelativePath: $($manifest.moduleRelativePath)" }
if (@($manifest.requiredPackageIds) -notcontains 'Lan.Wildlife') { throw 'Wildlife owner package is not required.' }
if ($manifest.assemblyIdentity -ne $identity) { throw "assemblyIdentity mismatch: expected $identity actual $($manifest.assemblyIdentity)" }
if ([long]$manifest.assemblyBytes -ne $info.Length) { throw "assemblyBytes mismatch: expected $($info.Length) actual $($manifest.assemblyBytes)" }
if ($manifest.contentHash -ne $hash) { throw "contentHash mismatch: expected $hash actual $($manifest.contentHash)" }
if ($manifest.moduleMvid -ne $assembly.ManifestModule.ModuleVersionId.ToString('D')) { throw "moduleMvid mismatch: expected $($assembly.ManifestModule.ModuleVersionId) actual $($manifest.moduleMvid)" }

Write-Output ('wildlifeAdapterVerification=PASS generation={0} commands={1} bytes={2} sha256={3}' -f
    $manifest.generation, @($manifest.commands).Count, $info.Length, $hash)
