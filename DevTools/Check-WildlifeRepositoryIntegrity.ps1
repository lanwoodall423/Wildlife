param(
    [switch]$RequireAssemblies
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$checks = New-Object System.Collections.Generic.List[object]
$failures = New-Object System.Collections.Generic.List[string]

function Check([string]$name, [bool]$passed, [string]$detail) {
    $checks.Add([pscustomobject]@{ name = $name; passed = $passed; detail = $detail })
    if (-not $passed) { $failures.Add($name) }
}

function ReadText([string]$relativePath) {
    return [IO.File]::ReadAllText((Join-Path $root $relativePath))
}

function GetMetadata([string]$path) {
    return @(& (Join-Path $PSScriptRoot 'Read-AssemblyMetadata.ps1') -AssemblyPath $path)
}

function HasReference([string[]]$metadata, [string]$name) {
    return @($metadata | Where-Object { $_ -like ('reference=' + $name + '|*') }).Count -gt 0
}

$required = @(
    'About\About.xml', 'LoadFolders.xml', '.gitignore',
    'DevTools\RimWorldReferences.props', 'DevTools\WildlifeEnvironment.ps1',
    'DevTools\WildlifeCoordinator.ps1',
    'DevTools\Install-WildlifeBuild.ps1', 'DevTools\Restart-Wildlife.ps1',
    'DevTools\AssemblyMetadataReader\AssemblyMetadataReader.csproj',
    'DevTools\Read-AssemblyMetadata.ps1', 'DevTools\Verify-DeferredRealityIntegration.ps1',
    'DevTools\Verify-KnowledgeFramework.ps1', 'DevTools\Check-WildlifeTests.ps1', 'Source\Herds\Herds.csproj',
    'DevTools\PORTABLE_VALIDATION.md', 'DevTools\WILDLIFE_17_STAGING.md', 'Source\Herds\WildlifeInsightUi.cs',
    'Source\Herds\WildlifeInspectorUi.cs', 'WILDLIFE_MIGRATION.md',
    'Source\Packs\PacksAndPredators.csproj', 'Source\Wildlife\Wildlife.csproj',
    'Source\Wildlife\DeferredReality.Wildlife.csproj',
    'Source\BridgeTools\Wildlife.BridgeTools.csproj',
    'Source\BridgeTools\WildlifeBridgeTools.cs',
    'Source\BridgeTools\WildlifeInGameTestSuite.cs',
    '.rimdev\stack.json', 'TestCatalog\rimtest.catalog.json')
foreach ($relative in $required) {
    Check "required:$relative" (Test-Path -LiteralPath (Join-Path $root $relative) -PathType Leaf) 'required file'
}

foreach ($xml in Get-ChildItem -LiteralPath $root -Recurse -Filter '*.xml' -File |
    Where-Object { $_.FullName -notmatch '[\\/]bin[\\/]|[\\/]obj[\\/]' }) {
    try { [xml](Get-Content -LiteralPath $xml.FullName -Raw) | Out-Null; Check "xml:$($xml.FullName.Substring($root.Length + 1))" $true 'parsed' }
    catch { Check "xml:$($xml.FullName.Substring($root.Length + 1))" $false $_.Exception.Message }
}

foreach ($script in Get-ChildItem -LiteralPath $root -Recurse -Filter '*.ps1' -File |
    Where-Object { $_.FullName -notmatch '[\\/]bin[\\/]|[\\/]obj[\\/]' }) {
    $tokens = $null
    $errors = $null
    [System.Management.Automation.Language.Parser]::ParseFile($script.FullName, [ref]$tokens, [ref]$errors) | Out-Null
    Check "powershell:$($script.FullName.Substring($root.Length + 1))" ($errors.Count -eq 0) 'parsed'
}

$about = [xml](ReadText 'About\About.xml')
$loadText = ReadText 'LoadFolders.xml'
$propsText = ReadText 'DevTools\RimWorldReferences.props'
$portableText = ReadText 'DevTools\PORTABLE_VALIDATION.md'
$stackText = ReadText '.rimdev\stack.json'
$catalogText = ReadText 'TestCatalog\rimtest.catalog.json'
$uiText = ReadText 'Source\Herds\WildlifeInsightUi.cs'
$inspectorText = ReadText 'Source\Herds\WildlifeInspectorUi.cs'
$huntPlanningText = ReadText 'Source\Herds\HuntPlanning.cs'
$herdIntegrationText = ReadText 'Source\Herds\HerdIntegration.cs'
$hidingText = ReadText 'Source\Herds\Hiding.cs'
$memoryText = ReadText 'Source\Herds\WildlifeCulturePresentation.cs'
$knowledgeText = ReadText 'Source\Herds\HuntingKnowledge.cs'
$migrationText = ReadText 'WILDLIFE_MIGRATION.md'
$journalText = ReadText 'Source\Herds\WildlifeJournal.cs'
$herdsProjectText = ReadText 'Source\Herds\Herds.csproj'
# Versioned build outputs have migrated first. Defs, textures and About/LoadFolders
# intentionally remain 1.6 until the complete 1.7 package has been relocated.
$versionedProjects = @(
    'Source\Herds\Herds.csproj',
    'Source\Packs\PacksAndPredators.csproj',
    'Source\Wildlife\Wildlife.csproj',
    'Source\Wildlife\DeferredReality.Wildlife.csproj'
)
foreach ($project in $versionedProjects) {
    $projectText = ReadText $project
    $expected = if ($project -like '*DeferredReality.Wildlife.csproj') {
        '1\.7[\\/]OptionalDeferredReality[\\/]Assemblies'
    } else {
        '1\.7[\\/]Assemblies'
    }
    Check "1.7-output:$project" ($projectText -match $expected) 'Release output is scoped to RimWorld 1.7'
}
Check '1.7-companion-defaults' ($propsText -match 'KnowledgeFrameworkPath.*1\.7' -and
    $propsText -match 'InsightCanvasPath.*1\.7' -and
    $propsText -match 'DeferredRealityFrameworkRoot.*1\.7') 'companion build references target 1.7'

# WILD-17-01B is a staging gate, not a claim that these original XML
# definitions have been vetted against RimWorld 1.7's Def/API changes.
$legacyXmlRoot = Join-Path $root '1.6'
$stagedXmlRoot = Join-Path $root '1.7'
$legacyXmlFiles = @(Get-ChildItem -LiteralPath $legacyXmlRoot -Recurse -Filter '*.xml' -File)
Check '1.7-text-staging:baseline-count' ($legacyXmlFiles.Count -eq 33) 'expected original 33 XML text assets'
foreach ($legacyXmlFile in $legacyXmlFiles) {
    $relative = $legacyXmlFile.FullName.Substring($legacyXmlRoot.Length + 1)
    $stagedXmlPath = Join-Path $stagedXmlRoot $relative
    $exists = Test-Path -LiteralPath $stagedXmlPath -PathType Leaf
    Check "1.7-text-staging:$relative" $exists '1.7 counterpart exists'
    if ($exists) {
        $legacyHash = (Get-FileHash -LiteralPath $legacyXmlFile.FullName -Algorithm SHA256).Hash
        $stagedHash = (Get-FileHash -LiteralPath $stagedXmlPath -Algorithm SHA256).Hash
        Check "1.7-initial-mirror:$relative" ($legacyHash -eq $stagedHash) 'initial 1.7 text copy is byte-identical'
    }
}
Check '1.7-stage:not-advertised' (([string]$about.ModMetaData.supportedVersions.li -eq '1.6') -and
    $loadText -match '<v1\.6>' -and $loadText -notmatch '<v1\.7>') 'unverified 1.7 payload is not loaded or advertised'
Check '1.7-stage:drf-only-conditional' ((Test-Path -LiteralPath (Join-Path $root '1.7\OptionalDeferredReality\Defs\WorldObjectDefs\DeferredReality_Wildlife.xml') -PathType Leaf) -and
    -not (Test-Path -LiteralPath (Join-Path $root '1.7\Defs\WorldObjectDefs\DeferredReality_Wildlife.xml') -PathType Leaf)) 'deferred region Def remains optional'

Check 'package:wildlife' ($about.ModMetaData.packageId -eq 'Lan.Wildlife') 'exact Wildlife package ID'
Check 'package:knowledge-framework' (@($about.ModMetaData.modDependencies.li.packageId) -contains 'lan.knowledgeframework') 'exact Knowledge Framework package ID'
Check 'package:insightcanvas' (@($about.ModMetaData.modDependencies.li.packageId) -contains 'lan.insightcanvas') 'exact Insight Canvas package ID'
Check 'load-after:insightcanvas' (@($about.ModMetaData.loadAfter.li) -contains 'lan.insightcanvas') 'Insight Canvas load order'
Check 'package:deferred-reality' (@($about.ModMetaData.loadAfter.li) -contains 'lan.deferredreality.framework') 'exact Deferred Reality package ID'
$retiredPackage = 'Lan.' + 'RimWorld' + 'DevBridge'
Check 'package:retired-bridge-absent' (-not (@($about.ModMetaData.loadAfter.li) -contains $retiredPackage)) 'retired bridge package is absent'
Check 'conditional-optional-load' ($loadText -match 'IfModActive="lan\.deferredreality\.framework"') 'optional adapter is conditional'
Check 'optional-load-path' ($loadText -match 'OptionalDeferredReality') 'optional load folder is scoped'
Check 'optional-def-placement' ((Test-Path -LiteralPath (Join-Path $root '1.6\OptionalDeferredReality\Defs\WorldObjectDefs\DeferredReality_Wildlife.xml') -PathType Leaf) -and
    -not (Test-Path -LiteralPath (Join-Path $root '1.6\Defs\WorldObjectDefs\DeferredReality_Wildlife.xml') -PathType Leaf)) 'DRF definitions are optional'
Check 'shared-environment-properties' ($propsText -match 'RIMWORLD_MANAGED_PATH' -and $propsText -match 'WILDLIFE_HARMONY_PATH' -and
    $propsText -match 'KNOWLEDGE_FRAMEWORK_PATH' -and $propsText -match 'INSIGHT_CANVAS_PATH' -and
    $propsText -match 'DEFERRED_REALITY_FRAMEWORK_PATH') 'shared properties expose documented overrides'
Check 'portable-insightcanvas-documentation' ($portableText -match 'INSIGHT_CANVAS_PATH') 'Insight Canvas override is documented'

$legacyInvokerPath = Join-Path $root ('DevTools\Invoke-' + 'WildlifeTest.ps1')
Check 'rimtest-legacy-wrapper-absent' (-not (Test-Path -LiteralPath $legacyInvokerPath)) 'legacy repository test wrapper is absent'
try {
    $stack = $stackText | ConvertFrom-Json
    $catalog = $catalogText | ConvertFrom-Json
    $wildlifeTest = @($catalog.tests | Where-Object { $_.id -eq 'wildlife-in-game-suite' })[0]
    $smokeSuite = @($catalog.suites | Where-Object { $_.id -eq 'smoke' })[0]
    Check 'rimtest-stack-configured' ($stack.catalog -eq 'TestCatalog/rimtest.catalog.json' -and
        $stack.devBridgeProject -eq 'wildlife' -and $stack.fallbackSuite -eq 'smoke') 'RimTest stack routes Wildlife to its catalog and fallback suite'
    Check 'rimtest-wildlife-recipe-mapped' ($null -ne $wildlifeTest -and
        $wildlifeTest.recipe -eq 'wildlife-in-game-suite' -and $null -ne $smokeSuite -and
        @($smokeSuite.tests) -contains 'wildlife-in-game-suite') 'RimTest catalog maps the Wildlife suite into the fallback workflow'
} catch {
    Check 'rimtest-metadata-json' $false $_.Exception.Message
}
Check 'insightcanvas-project-required' ($herdsProjectText -match '<WildlifeRequireInsightCanvas>true</WildlifeRequireInsightCanvas>' -and
    $herdsProjectText -match '<Reference Include="InsightCanvas"' -and $herdsProjectText -match '<Private>false</Private>')
    'Herds references the installed Insight Canvas assembly without copying it'
Check 'insightcanvas-ui-api' ($uiText -match 'InsightUiDocument' -and $uiText -match 'InsightUiHost' -and $uiText -match 'InsightUiWindow')
    'Wildlife UI uses the Insight Canvas v2 document/host/window API'
Check 'wildlife-ui-surfaces' ($uiText -match 'settings-categories' -and $uiText -match 'profiles-responsive-master-detail' -and
    $uiText -match 'InsightUi\.VirtualList' -and $uiText -match 'profile-reset')
    'settings and Species Profiles use the responsive migrated surfaces'
Check 'wildlife-inspector-v2-shell' ($inspectorText -match 'InsightUiDocument' -and
    $inspectorText -match 'InsightUiHost' -and $inspectorText -match 'WildlifeUiTheme\.Configure' -and
    $inspectorText -match 'InsightUi\.VirtualList' -and $inspectorText -match 'PostClose' -and
    $inspectorText -match '/ 30')
    'compact inspectors use bounded retained InsightCanvas hosts'
Check 'wildlife-inspector-wiring' ($herdIntegrationText -match 'WildlifeInspectorCanvas' -and
    $herdIntegrationText -match 'WildlifeInspectorUi\.Herd' -and $herdIntegrationText -match 'WildlifeInspectorUi\.Pen' -and
    $hidingText -match 'WildlifeInspectorUi\.Hiding' -and $memoryText -match 'WildlifeInspectorUi\.Memory' -and
    $memoryText -match 'WildlifeInspectorUi\.MemoryTimeline' -and $knowledgeText -match 'WildlifeInspectorUi\.SpeciesKnowledge' -and
    $huntPlanningText -match 'BuildInsightRoot')
    'high-priority animal, memory, knowledge, and hunt paths route through shared inspectors'
Check 'wildlife-inspector-localized' ($migrationText -match 'Labels for these paths are keyed' -and
    (ReadText '1.6\Languages\English\Keyed\Herds.xml') -match 'Wildlife\.UI\.Inspector\.Title' -and
    (ReadText '1.6\Languages\English\Keyed\Herds.xml') -match 'Wildlife\.UI\.Memory\.Title' -and
    (ReadText '1.6\Languages\English\Keyed\Herds.xml') -match 'Wildlife\.UI\.Hunt\.Title')
    'migrated player-facing surfaces have keyed English localization and documented scope'
Check 'wildlife-journal-v2-shell' ($journalText -match 'InsightUiDocument\("wildlife-journal"' -and
    $journalText -match 'InsightUiHost' -and $journalText -match 'InsightUiWindow' -and
    $journalText -match 'InsightUi\.Navigation' -and $journalText -match 'InsightUi\.Split|WildlifeResponsiveJournalSplit' -and
    $journalText -match 'InsightUi\.Scroll' -and $journalText -match 'InsightUi\.VirtualList')
    'Journal uses the shared InsightCanvas v2 document shell and composable responsive surfaces'
Check 'wildlife-journal-bounded-snapshot' ($journalText -match 'snapshotBucket' -and
    $journalText -match 'snapshotKnowledgeRevision' -and $journalText -match 'Take\(128\)' -and
    $journalText -match 'Take\(96\)' -and $journalText -match 'Take\(64\)')
    'Journal presentation data is cached by tick/Knowledge revision and bounded'
Check 'wildlife-journal-localized' ($journalText -match 'JournalText\.Get' -and
    $journalText -notmatch 'Widgets\.' -and $journalText -notmatch 'GUI\.color' -and
    $journalText -notmatch 'BeginScrollView' -and $journalText -notmatch 'Vector2\s+(leftScroll|bodyScroll|scroll)')
    'Journal rendering uses localized v2 components without manual scroll/color state'
Check 'wildlife-journal-no-obsolete-windows' ($journalText -notmatch 'class\s+Window_WildlifeJournal' -and
    (ReadText 'Source\Herds\WildlifeSignalCulture.cs') -notmatch 'class\s+Window_WildlifeSignals' -and
    (ReadText 'Source\Herds\WildlifeFieldJournal.cs') -notmatch 'class\s+Window_WildlifeFieldJournal')
    'obsolete manual Journal, Signals, and Field Journal windows are removed'
Check 'insightcanvas-no-v1-ui' ($uiText -notmatch '\bInsightModel\b' -and $uiText -notmatch '\bInsightWindow\b' -and
    $uiText -notmatch '\bInsightCanvasHost\b') 'ordinary Wildlife screens do not use the legacy semantic API'
Check 'insightcanvas-no-bundle' (@(Get-ChildItem -LiteralPath $root -Recurse -Filter 'InsightCanvas.dll' -File -ErrorAction SilentlyContinue).Count -eq 0)
    'Insight Canvas is never bundled in Wildlife output'

$normalProjects = @('Source\Herds\Herds.csproj', 'Source\Packs\PacksAndPredators.csproj', 'Source\Wildlife\Wildlife.csproj')
foreach ($project in $normalProjects) {
    $text = ReadText $project
    Check "normal-no-drf:$project" ($text -notmatch '<Reference Include="DeferredRealityFramework"') 'normal project has no DRF reference'
    Check "normal-no-bridge:$project" ($text -notmatch 'DevBridge') 'normal project has no coordinator reference'
    Check "normal-relative:$project" ($text -notmatch '(?i)(?:[A-Z]:[\\/]|\\\\[^\\]+[\\/]|steamapps|Workshop|USERPROFILE)') 'no machine-specific path'
    if ($project -ne 'Source\Herds\Herds.csproj') {
        Check "normal-no-unneeded-insightcanvas:$project" ($text -notmatch '<Reference Include="InsightCanvas"')
            'non-UI assembly has no unnecessary Insight Canvas coupling'
    }
}
# WILD-DEP-01A: the base Wildlife DLL has an explicit, vanilla-only compile
# boundary. Legacy Herds/Packs remain separately migrated and are not described
# as optional or independently loadable by this check.
$coreProjectText = ReadText 'Source\Wildlife\Wildlife.csproj'
$coreSourceText = ReadText 'Source\Wildlife\WildlifeMod.cs'
$coreCompiles = @([regex]::Matches($coreProjectText, '<Compile\s+Include="([^"]+)"') |
    ForEach-Object { $_.Groups[1].Value })
$coreReferences = @([regex]::Matches($coreProjectText, '<Reference\s+Include="([^"]+)"') |
    ForEach-Object { $_.Groups[1].Value })
$coreAllowedReferences = @('Assembly-CSharp', 'UnityEngine.CoreModule',
    'UnityEngine.IMGUIModule', 'UnityEngine.TextRenderingModule')
Check 'native-core-explicit-compile' ($coreProjectText -match '<EnableDefaultCompileItems>false</EnableDefaultCompileItems>' -and
    $coreCompiles.Count -eq 1 -and $coreCompiles[0] -eq 'WildlifeMod.cs') 'native core compiles only its independent entry point'
Check 'native-core-no-external-build-references' ($coreProjectText -notmatch '<ProjectReference\b' -and
    $coreProjectText -notmatch '<WildlifeRequire(?:Harmony|KnowledgeFramework|InsightCanvas)>true' -and
    $coreReferences.Count -eq 4 -and
    @($coreReferences | Where-Object { $coreAllowedReferences -notcontains $_ }).Count -eq 0) 'native Wildlife DLL references only supported game/Unity assemblies'
Check 'native-core-no-third-party-types' ($coreSourceText -notmatch '\b(HarmonyLib|KnowledgeFramework|InsightCanvas|DeferredReality)\b' -and
    $coreSourceText -notmatch '(?m)^\s*using\s+(Herds|Packs)\s*;') 'native entry point does not use companion mod types'
Check 'native-core-safe-observation' ($coreSourceText -match 'Find\.CurrentMap' -and
    $coreSourceText -match 'AllPawnsSpawned' -and
    $coreSourceText -match 'pawn\.Position\.Fogged\(map\)' -and
    $coreSourceText -match 'pawn\.Faction != null' -and
    $coreSourceText -match 'pawn\.Dead' -and
    $coreSourceText -notmatch '\bScribe_') 'field view only counts actual unfogged unowned live animals'

$bridgeProject = ReadText 'Source\BridgeTools\Wildlife.BridgeTools.csproj'
$bridgeToolText = ReadText 'Source\BridgeTools\WildlifeBridgeTools.cs'
$suiteText = ReadText 'Source\BridgeTools\WildlifeInGameTestSuite.cs'
Check 'bridge-sdk-package' ($bridgeProject -match 'RimBridgeServer\.Sdk' -and $bridgeProject -match 'RimBridgeServerSdkPath' -and
    $bridgeProject -match 'RimBridgeServerSdkPackageVersion' -and $bridgeProject -match '>2\.0\.0<') 'companion uses the released SDK fallback or an explicit host SDK path'
Check 'bridge-no-coordinator-reference' ($bridgeProject -notmatch 'DevBridge' -and $bridgeToolText -notmatch 'DevBridge') 'companion has no coordinator reference'
Check 'bridge-output' ($bridgeProject -match '<AssemblyName>Wildlife\.BridgeTools</AssemblyName>' -and $bridgeProject -match 'BridgeTools[\\/]Wildlife') 'companion output is the sibling BridgeTools bundle'
Check 'bridge-main-thread-seam' ($bridgeToolText -match 'IRimBridgeContext' -and $bridgeToolText -match 'MainThread' -and $bridgeToolText -match 'InvokeAsync' -and $bridgeToolText -match 'WildlifeInGameTestSuite\.Run') 'companion invokes gameplay assertions on the RimBridge main-thread seam'
Check 'bridge-authenticated-tool' ($bridgeToolText -match '\[Tool\("wildlife/run-test-suite"' -and $bridgeToolText -match 'RequiresAuth\s*=\s*true') 'suite tool requires authenticated routed calls'
Check 'bridge-stable-result' ($bridgeToolText -match 'public bool success' -and $bridgeToolText -match 'reportPath' -and $bridgeToolText -match 'WildlifeInGameTestSuite') 'companion returns a stable suite/report result'
Check 'production-harness-boundary' (-not (Test-Path -LiteralPath (Join-Path $root 'Source\Herds\WildlifeInGameTestSuite.cs')) -and
    -not (Test-Path -LiteralPath (Join-Path $root 'Source\Herds\WildlifeAutomatedTestRunner.cs')) -and
    -not (Test-Path -LiteralPath (Join-Path $root 'Source\Herds\WildlifeDevDashboard.cs')) -and
    -not (Test-Path -LiteralPath (Join-Path $root 'Source\DevHarness\Wildlife.DevHarness.csproj'))) 'test and dashboard code is outside production and old harness paths are absent'

$retiredPaths = @(
    'Source\Herds\WildlifeDevBridge.cs', 'Source\Herds\WildlifeBridgeHotReload.cs',
    'DevTools\BridgeAdapters', 'DevTools\DevBridge', 'DevTools\Build-WildlifeBridgeAdapter.ps1',
    'DevTools\Test-WildlifeBridgeAdapter.ps1', 'DevTools\Run-WildlifeTests.ps1',
    'DevTools\Start-WildlifeTestServer.ps1', 'DevTools\Stop-WildlifeTestServer.ps1',
    'DevTools\Request-WildlifeTests.ps1', 'DevTools\Install-StagedWildlife.ps1')
foreach ($relative in $retiredPaths) {
    Check "retired-absent:$relative" (-not (Test-Path -LiteralPath (Join-Path $root $relative))) 'retired artifact absent'
}

$textFiles = Get-ChildItem -LiteralPath $root -Recurse -File |
    Where-Object {
        $_.FullName -notmatch '[\\/]\.git[\\/]|[\\/]bin[\\/]|[\\/]obj[\\/]|[\\/]1\.6[\\/]Assemblies[\\/]' -and
        $_.Extension -in @('.cs', '.csproj', '.props', '.ps1', '.md', '.xml', '.txt') -and
        $_.FullName -ne (Join-Path $PSScriptRoot 'Check-WildlifeRepositoryIntegrity.ps1')
    }
$forbiddenText = @(
    ('Wildlife' + 'DevBridge'), ('Wildlife' + 'BridgeHotReload'),
    ('RimWorld' + 'DevBridge'), 'wildlifetestserver', 'Application.Quit',
    'Start-Process', 'Get-Process', 'Stop-Process', 'taskkill', 'Process.Kill', 'Process.Start',
    'RIMWORLD_DEVBRIDGE', 'WILDLIFE_ENABLE_LEGACY_BRIDGE',
    'WildlifeAutomatedTestRunner', 'Wildlife.DevHarness', 'Wildlife-TestRequests',
    'ProtocolSelfTest')
$foundForbidden = New-Object System.Collections.Generic.List[string]
foreach ($file in $textFiles) {
    $content = Get-Content -LiteralPath $file.FullName -Raw
    foreach ($token in $forbiddenText) {
        if ($content.IndexOf($token, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
            $foundForbidden.Add("$($file.FullName.Substring($root.Length + 1)):$token")
        }
    }
}
Check 'no-retired-text-or-process-control' ($foundForbidden.Count -eq 0) ('found=' + ($foundForbidden -join ', '))

Check 'suite-no-old-runner-coupling' ($suiteText -notmatch 'WildlifeAutomatedTestRunner' -and $suiteText -notmatch 'ProtocolSelfTest' -and
    $suiteText -match 'execution=rimbridge-server') 'suite has no file-runner coupling'
Check 'suite-no-debug-action-entry' ($suiteText -notmatch '\[DebugAction') 'suite has no duplicate in-game DebugAction entry'
Check 'suite-cleanup-regression' ($suiteText -match 'OriginalGameRestored' -and $suiteText -match 'OriginalMapStateRestored' -and
    $suiteText -match 'WorldInterfaceSafe' -and $suiteText -match 'TilePickerSafe' -and $suiteText -match 'RootUiUpdateSafe' -and
    $suiteText -match 'NoIsolatedState' -and $suiteText -match 'DeferredCallbacksDrained') 'suite owns cleanup regression checks'
Check 'suite-insightcanvas-ui-coverage' ($suiteText -match 'CheckInsightCanvasPresentations' -and
    $suiteText -match 'settings-global-no-results' -and $suiteText -match 'profiles-no-results' -and
    $suiteText -match 'wildlife-journal' -and $suiteText -match 'journal-field-log-items' -and
    $suiteText -match 'CloseTransientOverlays') 'suite covers migrated Insight Canvas settings/profile/Journal lifecycle states'

$adapterText = ReadText 'Source\Wildlife\DeferredReality.Wildlife.csproj'
Check 'adapter-drf-reference' ($adapterText -match '<Reference Include="DeferredRealityFramework"') 'optional adapter references DRF'
Check 'adapter-optional-output' ($adapterText -match 'OptionalDeferredReality[\\/]Assemblies') 'optional adapter output is scoped'
Check 'adapter-no-bridge' ($adapterText -notmatch 'DevBridge') 'optional adapter has no coordinator reference'

$trackedIntermediate = @(git -C $root ls-files -- '*/bin/*' '*/obj/*' 'bin/*' 'obj/*')
Check 'tracked-intermediates' ($trackedIntermediate.Count -eq 0) ('count=' + $trackedIntermediate.Count)
$gitignore = ReadText '.gitignore'
Check 'ignore-bin-obj' ($gitignore -match '(?m)^\*\*/bin/$' -and $gitignore -match '(?m)^\*\*/obj/$') 'bin/obj ignored'

if ($RequireAssemblies) {
    $assemblies = @(
        (Join-Path $root '1.7\Assemblies\Herds.dll'),
        (Join-Path $root '1.7\Assemblies\PacksAndPredators.dll'),
        (Join-Path $root '1.7\Assemblies\Wildlife.dll'))
    $gameRoot = Split-Path -Parent (Split-Path -Parent $root)
    $bridgeAssembly = Join-Path $gameRoot 'BridgeTools\Wildlife\Wildlife.BridgeTools.dll'
    $legacyHarnessAssembly = Join-Path $root '1.7\Assemblies\Wildlife.DevHarness.dll'
    $optionalAssembly = Join-Path $root '1.7\OptionalDeferredReality\Assemblies\DeferredReality.Wildlife.dll'
    foreach ($path in $assemblies) { Check "assembly:$([IO.Path]::GetFileName($path))" (Test-Path -LiteralPath $path -PathType Leaf) 'built output' }
    Check 'assembly:Wildlife.BridgeTools.dll' (Test-Path -LiteralPath $bridgeAssembly -PathType Leaf) 'bridge companion output'
    Check 'assembly:Wildlife.DevHarness.dll' (-not (Test-Path -LiteralPath $legacyHarnessAssembly -PathType Leaf)) 'legacy harness output absent'
    if (Test-Path -LiteralPath $bridgeAssembly -PathType Leaf) {
        Check 'assembly:Wildlife.BridgeTools.no-sdk-deploy' (-not (Test-Path -LiteralPath (Join-Path (Split-Path $bridgeAssembly) 'RimBridgeServer.Sdk.dll') -PathType Leaf)) 'companion does not deploy the host SDK'
    }
    Check 'assembly:DeferredReality.Wildlife.dll' (Test-Path -LiteralPath $optionalAssembly -PathType Leaf) 'optional built output'
    if ($failures.Count -eq 0) {
        $metadata = @{}
        foreach ($path in $assemblies + $optionalAssembly + $bridgeAssembly) { $metadata[[IO.Path]::GetFileName($path)] = GetMetadata $path }
        foreach ($path in $assemblies) {
            $name = [IO.Path]::GetFileName($path)
            Check "assembly:$name-no-drf" (-not (HasReference $metadata[$name] 'DeferredRealityFramework')) 'assembly has no DRF reference'
            Check "assembly:$name-no-bridge" (-not (HasReference $metadata[$name] ('RimWorld' + 'DevBridge'))) 'assembly has no coordinator reference'
        }
        Check 'assembly:Herds-insightcanvas' (HasReference $metadata['Herds.dll'] 'InsightCanvas') 'Herds contains the Insight Canvas UI dependency'
        Check 'assembly:Packs-no-insightcanvas' (-not (HasReference $metadata['PacksAndPredators.dll'] 'InsightCanvas')) 'Packs has no UI dependency'
        Check 'assembly:Wildlife-no-insightcanvas' (-not (HasReference $metadata['Wildlife.dll'] 'InsightCanvas')) 'Wildlife has no unnecessary UI dependency'
        $optionalName = [IO.Path]::GetFileName($optionalAssembly)
        Check 'assembly:optional-drf' (HasReference $metadata[$optionalName] 'DeferredRealityFramework') 'optional adapter references DRF'
        Check 'assembly:optional-no-bridge' (-not (HasReference $metadata[$optionalName] ('RimWorld' + 'DevBridge'))) 'optional adapter has no coordinator reference'
        $bridgeName = [IO.Path]::GetFileName($bridgeAssembly)
        Check 'assembly:Wildlife.BridgeTools-sdk' (HasReference $metadata[$bridgeName] 'RimBridgeServer.Sdk') 'companion references only the RimBridgeServer SDK for bridge access'
        Check 'assembly:Wildlife.BridgeTools-no-coordinator' (-not (HasReference $metadata[$bridgeName] ('RimWorld' + 'DevBridge'))) 'companion has no coordinator reference'
    }
}

$status = if ($failures.Count -eq 0) { 'PASS' } else { 'FAIL' }
[ordered]@{ status = $status; checks = $checks.ToArray(); failures = $failures.ToArray() } | ConvertTo-Json -Depth 5
if ($failures.Count -ne 0) { exit 1 }
