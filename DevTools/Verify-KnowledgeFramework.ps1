param(
    [string]$WildlifeRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$AquacultureRoot = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'AquacultureFishing'),
    [string]$HorticultureRoot = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'Horticulture - Novel Seeds'),
    [string]$FrameworkRoot = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'KnowledgeFramework')
)

$ErrorActionPreference = 'Stop'

$paths = [ordered]@{
    'shared framework project' = Join-Path $FrameworkRoot 'Source\KnowledgeFramework.csproj'
    'shared API and Bio panel' = Join-Path $FrameworkRoot 'Source\KnowledgeFramework.cs'
    'Wildlife adapter' = Join-Path $WildlifeRoot 'Source\Herds\SharedKnowledgeIntegration.cs'
    'Aquaculture adapter' = Join-Path $AquacultureRoot 'Source\SharedKnowledgeIntegration.cs'
    'Horticulture ledger and adapter' = Join-Path $HorticultureRoot 'Source\PlantKnowledge.cs'
}

foreach ($entry in $paths.GetEnumerator()) {
    if (-not (Test-Path -LiteralPath $entry.Value)) { throw "Missing $($entry.Key): $($entry.Value)" }
}

$shared = Get-Content -Raw $paths['shared API and Bio panel']
$wildlife = Get-Content -Raw $paths['Wildlife adapter']
$aquaculture = Get-Content -Raw $paths['Aquaculture adapter']
$horticulture = Get-Content -Raw $paths['Horticulture ledger and adapter']
$fishing = Get-Content -Raw (Join-Path $AquacultureRoot 'Source\FishingExpertise.cs')
$plants = Get-Content -Raw (Join-Path $HorticultureRoot 'Source\ModCore.cs')
$plantPatches = Get-Content -Raw (Join-Path $HorticultureRoot 'Source\Patches.cs')
$plantResource = Get-Content -Raw (Join-Path $HorticultureRoot 'Source\ResourceNeed.cs')
$cultivars = Get-Content -Raw (Join-Path $HorticultureRoot 'Source\CultivarRegistry.cs')
$wildlifeKnowledge = Get-Content -Raw (Join-Path $WildlifeRoot 'Source\Herds\HuntingKnowledge.cs')
$wildlifeStats = Get-Content -Raw (Join-Path $WildlifeRoot 'Source\Herds\HerdIntegration.cs')
$wildlifeJournal = Get-Content -Raw (Join-Path $WildlifeRoot 'Source\Herds\WildlifeFieldJournal.cs')
$wildlifeRankSurfaces = $wildlifeKnowledge + $wildlifeStats + $wildlifeJournal + $wildlife

$requirements = [ordered]@{
    'four unified ranks' = $shared -match 'Novice[\s\S]*Adept[\s\S]*Expert[\s\S]*Master'
    'shared record serialization' = $shared -match 'class KnowledgeRecord' -and $shared -match 'Scribe_References\.Look' -and $shared -match 'Scribe_Values\.Look'
    'cached provider API' = $shared -match 'KnowledgeProviderRegistry' -and $shared -match 'cacheTick'
    'single Bio panel' = $shared -match 'Knowledge & Expertise' -and $shared -match 'CharacterCardUtility'
    'collapsible panel' = $shared -match 'expandedPawns'
    'Wildlife adapter registration' = $wildlife -match 'KnowledgeProviderRegistry\.Register'
    'Wildlife existing window navigation' = $wildlife -match 'Window_ColonistWildlifeKnowledge'
    'Wildlife legacy species key retained' = $wildlifeKnowledge -match 'colonistSpeciesKnowledge'
    'Wildlife reveal gates use four ranks' = $wildlifeRankSurfaces -notmatch 'level\s*>=\s*[45]' -and $wildlifeRankSurfaces -notmatch 'level\s*==\s*[45]'
    'Aquaculture adapter registration' = $aquaculture -match 'KnowledgeProviderRegistry\.Register'
    'Aquaculture existing journal navigation' = $aquaculture -match 'OpenExpertise'
    'Aquaculture legacy progression key retained' = $fishing -match 'aquacultureFishingProgression'
    'Horticulture additive ledger key' = $plants -match 'horticultureKnowledge'
    'plant knowledge is keyed by pawn and crop' = $horticulture -match 'KnowledgeRecord' -and $horticulture -match 'cropDefName'
    'sowing gain hook' = $plantPatches -match 'PlantKnowledgeUtility\.RecordSowing'
    'harvest and cutting gain hook' = $plantPatches -match 'PlantKnowledgeUtility\.RecordPlantWork'
    'fertilizing gain hook' = $plantResource -match 'PlantKnowledgeUtility\.RecordFertilizing'
    'seed discovery gain hook' = $horticulture -match 'RecordSeedDiscovery'
    'plant work speed hook' = $horticulture -match 'PlantWorkSpeedFactor'
    'plant mutation is knowledge-independent' = $horticulture -notmatch 'MutationChanceFactor'
    'plant crafting speed hook' = $horticulture -match 'HorticultureRecipeSpeedPatch' -and $horticulture -match 'CraftingSpeedFactor'
    'Aquaculture legacy rank mapping' = $fishing -match 'RankFromLegacySetting' -and $fishing -match 'LegacySettingForRank'
    'existing Cultivar Registry knowledge page' = $cultivars -match 'RegistryPage\.Knowledge' -and $cultivars -match 'KnowledgeMenuUI\.Draw'
    'no new Horticulture main window' = (Get-ChildItem (Join-Path $HorticultureRoot '1.6\Defs') -Filter '*.xml' | Get-Content -Raw) -notmatch '<defName>HNS_.*Knowledge'
}

$failed = @($requirements.GetEnumerator() | Where-Object { -not $_.Value })
if ($failed.Count -gt 0) {
    throw "Shared knowledge verification failed: $(($failed | ForEach-Object Key) -join ', ')"
}

Write-Output ("Shared knowledge verification passed ({0} checks)." -f $requirements.Count)
