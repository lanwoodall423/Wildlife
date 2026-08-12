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
    'DevTools\WildlifeCoordinator.ps1', 'DevTools\Invoke-WildlifeTest.ps1',
    'DevTools\Install-WildlifeBuild.ps1', 'DevTools\Restart-Wildlife.ps1',
    'DevTools\AssemblyMetadataReader\AssemblyMetadataReader.csproj',
    'DevTools\Read-AssemblyMetadata.ps1', 'DevTools\Verify-DeferredRealityIntegration.ps1',
    'DevTools\Verify-KnowledgeFramework.ps1', 'DevTools\Check-WildlifeTests.ps1', 'Source\Herds\Herds.csproj',
    'DevTools\PORTABLE_VALIDATION.md', 'Source\Herds\WildlifeInsightUi.cs',
    'Source\Packs\PacksAndPredators.csproj', 'Source\Wildlife\Wildlife.csproj',
    'Source\Wildlife\DeferredReality.Wildlife.csproj',
    'Source\DevHarness\Wildlife.DevHarness.csproj',
    'Source\DevHarness\WildlifeAutomatedTestRunner.cs',
    'Source\DevHarness\WildlifeInGameTestSuite.cs')
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
$uiText = ReadText 'Source\Herds\WildlifeInsightUi.cs'
$herdsProjectText = ReadText 'Source\Herds\Herds.csproj'
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
Check 'insightcanvas-project-required' ($herdsProjectText -match '<WildlifeRequireInsightCanvas>true</WildlifeRequireInsightCanvas>' -and
    $herdsProjectText -match '<Reference Include="InsightCanvas"' -and $herdsProjectText -match '<Private>false</Private>')
    'Herds references the installed Insight Canvas assembly without copying it'
Check 'insightcanvas-ui-api' ($uiText -match 'InsightUiDocument' -and $uiText -match 'InsightUiHost' -and $uiText -match 'InsightUiWindow')
    'Wildlife UI uses the Insight Canvas v2 document/host/window API'
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
$harnessProject = ReadText 'Source\DevHarness\Wildlife.DevHarness.csproj'
Check 'harness-no-coordinator-reference' ($harnessProject -notmatch 'DevBridge') 'harness has no coordinator reference'
Check 'harness-output' ($harnessProject -match '<AssemblyName>Wildlife\.DevHarness</AssemblyName>' -and $harnessProject -match '1\.6[\\/]Assemblies') 'harness output is optional and isolated'
Check 'production-harness-boundary' (-not (Test-Path -LiteralPath (Join-Path $root 'Source\Herds\WildlifeInGameTestSuite.cs')) -and
    -not (Test-Path -LiteralPath (Join-Path $root 'Source\Herds\WildlifeAutomatedTestRunner.cs')) -and
    -not (Test-Path -LiteralPath (Join-Path $root 'Source\Herds\WildlifeDevDashboard.cs'))) 'test and dashboard code is outside Herds source'

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
    'RIMWORLD_DEVBRIDGE', 'WILDLIFE_ENABLE_LEGACY_BRIDGE')
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

$runnerText = ReadText 'Source\DevHarness\WildlifeAutomatedTestRunner.cs'
$suiteText = ReadText 'Source\DevHarness\WildlifeInGameTestSuite.cs'
Check 'runner-no-implicit-auto-run' ($runnerText -match 'AutomaticMode\s*=>\s*false' -and $runnerText -match 'Postfix\(\)') 'launch identity does not authorize execution'
Check 'runner-explicit-protocol' ($runnerText -match 'requestId' -and $runnerText -match 'launchId' -and $runnerText -match 'generation' -and
    $runnerText -match 'suite' -and $runnerText -match 'mode' -and $runnerText -match 'leaseId' -and
    $runnerText -match 'result\.json' -and $runnerText -match 'TryClaim') 'runner uses atomic identity-bound request/result protocol'
Check 'suite-cleanup-regression' ($suiteText -match 'OriginalGameRestored' -and $suiteText -match 'OriginalMapStateRestored' -and
    $suiteText -match 'WorldInterfaceSafe' -and $suiteText -match 'TilePickerSafe' -and $suiteText -match 'RootUiUpdateSafe' -and
    $suiteText -match 'NoIsolatedState' -and $suiteText -match 'DeferredCallbacksDrained') 'suite owns cleanup regression checks'

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
        (Join-Path $root '1.6\Assemblies\Herds.dll'),
        (Join-Path $root '1.6\Assemblies\PacksAndPredators.dll'),
        (Join-Path $root '1.6\Assemblies\Wildlife.dll'),
        (Join-Path $root '1.6\Assemblies\Wildlife.DevHarness.dll'))
    $optionalAssembly = Join-Path $root '1.6\OptionalDeferredReality\Assemblies\DeferredReality.Wildlife.dll'
    foreach ($path in $assemblies) { Check "assembly:$([IO.Path]::GetFileName($path))" (Test-Path -LiteralPath $path -PathType Leaf) 'built output' }
    Check 'assembly:DeferredReality.Wildlife.dll' (Test-Path -LiteralPath $optionalAssembly -PathType Leaf) 'optional built output'
    if ($failures.Count -eq 0) {
        $metadata = @{}
        foreach ($path in $assemblies + $optionalAssembly) { $metadata[[IO.Path]::GetFileName($path)] = GetMetadata $path }
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
    }
}

$status = if ($failures.Count -eq 0) { 'PASS' } else { 'FAIL' }
[ordered]@{ status = $status; checks = $checks.ToArray(); failures = $failures.ToArray() } | ConvertTo-Json -Depth 5
if ($failures.Count -ne 0) { exit 1 }
