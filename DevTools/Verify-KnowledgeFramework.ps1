param(
    [string]$WildlifeRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$KnowledgeFrameworkPath = [Environment]::GetEnvironmentVariable('KNOWLEDGE_FRAMEWORK_PATH')
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'WildlifeEnvironment.ps1')

$wildlifeRoot = [IO.Path]::GetFullPath($WildlifeRoot)
$frameworkRoot = if ([string]::IsNullOrWhiteSpace($KnowledgeFrameworkPath)) {
    Get-WildlifeRequiredPath 'KNOWLEDGE_FRAMEWORK_PATH' 'Knowledge-Framework checkout'
} else { [IO.Path]::GetFullPath($KnowledgeFrameworkPath) }
$frameworkAssembly = Join-Path $frameworkRoot '1.6\Assemblies\KnowledgeFramework.dll'
$adapterPath = Join-Path $wildlifeRoot 'Source\Herds\WildlifeKnowledgeAdapter.cs'
$testPath = Join-Path $wildlifeRoot 'Source\Herds\WildlifeInGameTestSuite.cs'
$aboutPath = Join-Path $wildlifeRoot 'About\About.xml'

foreach ($path in @($frameworkAssembly, $adapterPath, $testPath, $aboutPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Required validation input is missing: $path" }
}

$metadata = @(& (Join-Path $PSScriptRoot 'Read-AssemblyMetadata.ps1') -AssemblyPath $frameworkAssembly -Contains @(
    'KnowledgeFramework.KnowledgeEngine::Submit',
    'KnowledgeFramework.KnowledgeClaimService::Snapshot',
    'KnowledgeFramework.KnowledgeClaimService::ForSubject',
    'KnowledgeFramework.KnowledgeDiagnostics::Snapshot',
    'KnowledgeFramework.KnowledgeMigrationService::IsCommitted',
    'KnowledgeFramework.KnowledgeQuery::get_Revision'
))
$adapter = [IO.File]::ReadAllText($adapterPath)
$tests = [IO.File]::ReadAllText($testPath)
[xml]$about = Get-Content -LiteralPath $aboutPath -Raw

$checks = [ordered]@{
    'framework-assembly' = Test-Path -LiteralPath $frameworkAssembly -PathType Leaf
    'public-submit' = @($metadata | Where-Object { $_ -eq 'contains=KnowledgeFramework.KnowledgeEngine::Submit|True' }).Count -eq 1
    'public-claim-snapshot' = @($metadata | Where-Object { $_ -eq 'contains=KnowledgeFramework.KnowledgeClaimService::Snapshot|True' }).Count -eq 1
    'public-claim-query' = @($metadata | Where-Object { $_ -eq 'contains=KnowledgeFramework.KnowledgeClaimService::ForSubject|True' }).Count -eq 1
    'public-diagnostics' = @($metadata | Where-Object { $_ -eq 'contains=KnowledgeFramework.KnowledgeDiagnostics::Snapshot|True' }).Count -eq 1
    'public-migration-status' = @($metadata | Where-Object { $_ -eq 'contains=KnowledgeFramework.KnowledgeMigrationService::IsCommitted|True' }).Count -eq 1
    'public-query-revision' = @($metadata | Where-Object { $_ -eq 'contains=KnowledgeFramework.KnowledgeQuery::get_Revision|True' }).Count -eq 1
    'wildlife-v3-submit' = $adapter -match 'KnowledgeEngine\.Submit'
    'wildlife-public-claims' = $adapter -match 'KnowledgeQuery\.Claims' -and $tests -match 'KnowledgeClaimService\.Snapshot'
    'wildlife-public-migration' = $adapter -match 'KnowledgeMigrationService\.IsCommitted'
    'wildlife-migration-id' = $adapter -match 'wildlife\.v3\.legacy'
    'wildlife-no-private-v3-reflection' = $tests -notmatch 'claimsV3|contextFacetsV3|accrualV3|RebuildV3Indexes'
    'wildlife-no-obsolete-staleness' = $adapter -notmatch 'KnowledgeClaimStalenessPolicy\.Contextual'
    'wildlife-no-obsolete-archetype-fields' = $adapter -notmatch 'categoryId\s*=|discoveryStageIds\s*=|observationIds\s*='
    'wildlife-no-obsolete-comparison-fields' = $adapter -notmatch 'relationTypeIds\s*='
    'package-id' = $about.ModMetaData.packageId -eq 'Lan.Wildlife'
    'knowledge-package-id' = @($about.ModMetaData.modDependencies.li.packageId) -contains 'lan.knowledgeframework'
}

$failed = @($checks.GetEnumerator() | Where-Object { -not $_.Value })
if ($failed.Count -gt 0) { throw "Knowledge Framework V3 verification failed: $(($failed | ForEach-Object Key) -join ', ')" }
Write-Output ("Knowledge Framework V3 verification passed ({0} checks) against {1}." -f $checks.Count, $frameworkAssembly)
