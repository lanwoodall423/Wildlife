param(
    [Parameter(Mandatory = $true)][string]$AssemblyPath,
    [string]$ReaderProject = (Join-Path $PSScriptRoot 'AssemblyMetadataReader\AssemblyMetadataReader.csproj'),
    [string[]]$Contains = @()
)

$ErrorActionPreference = 'Stop'
$resolvedAssembly = [IO.Path]::GetFullPath($AssemblyPath)
$resolvedProject = [IO.Path]::GetFullPath($ReaderProject)
if (-not (Test-Path -LiteralPath $resolvedAssembly -PathType Leaf)) { throw "Assembly is missing: $resolvedAssembly" }
if (-not (Test-Path -LiteralPath $resolvedProject -PathType Leaf)) { throw "Metadata reader project is missing: $resolvedProject" }

$arguments = @('run', '--project', $resolvedProject, '--configuration', 'Release', '--verbosity', 'quiet', '--', $resolvedAssembly)
foreach ($symbol in $Contains) { $arguments += '--contains=' + $symbol }
$output = @(& dotnet @arguments 2>&1)
if ($LASTEXITCODE -ne 0) { throw ($output -join [Environment]::NewLine) }
$output
