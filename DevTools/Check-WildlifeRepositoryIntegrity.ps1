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

function GetCombinedText([object[]]$files) {
    return (($files | ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw }) -join "`n")
}

function HasReference([string[]]$metadata, [string]$name) {
    return @($metadata | Where-Object { $_ -like ('reference=' + $name + '|*') }).Count -gt 0
}

foreach ($required in @(
    'About\About.xml', 'LoadFolders.xml', '.gitignore',
    'DevTools\RimWorldReferences.props',
    'DevTools\WildlifeEnvironment.ps1',
    'DevTools\AssemblyMetadataReader\AssemblyMetadataReader.csproj',
    'DevTools\Read-AssemblyMetadata.ps1',
    'DevTools\Verify-DeferredRealityIntegration.ps1',
    'DevTools\Verify-KnowledgeFramework.ps1',
    'Source\Herds\Herds.csproj',
    'Source\Packs\PacksAndPredators.csproj',
    'Source\Wildlife\Wildlife.csproj',
    'Source\Wildlife\DeferredReality.Wildlife.csproj')) {
    Check "required:$required" (Test-Path -LiteralPath (Join-Path $root $required) -PathType Leaf) 'required file'
}

foreach ($xml in Get-ChildItem -LiteralPath $root -Recurse -Filter '*.xml' -File) {
    try { [xml](Get-Content -LiteralPath $xml.FullName -Raw) | Out-Null; Check "xml:$($xml.FullName.Substring($root.Length + 1))" $true 'parsed' }
    catch { Check "xml:$($xml.FullName.Substring($root.Length + 1))" $false $_.Exception.Message }
}

foreach ($script in Get-ChildItem -LiteralPath (Join-Path $root 'DevTools') -Filter '*.ps1' -File) {
    $tokens = $null
    $errors = $null
    [System.Management.Automation.Language.Parser]::ParseFile($script.FullName, [ref]$tokens, [ref]$errors) | Out-Null
    Check "powershell:$($script.Name)" ($errors.Count -eq 0) 'parsed'
}

$about = [xml](ReadText 'About\About.xml')
$loadText = ReadText 'LoadFolders.xml'
$propsText = ReadText 'DevTools\RimWorldReferences.props'
Check 'package:wildlife' ($about.ModMetaData.packageId -eq 'Lan.Wildlife') 'exact Wildlife package ID'
Check 'package:knowledge-framework' (@($about.ModMetaData.modDependencies.li.packageId) -contains 'lan.knowledgeframework') 'exact Knowledge Framework package ID'
Check 'package:deferred-reality' (@($about.ModMetaData.loadAfter.li) -contains 'lan.deferredreality.framework') 'exact Deferred Reality package ID'
Check 'package:devbridge' (@($about.ModMetaData.loadAfter.li) -contains 'Lan.RimWorldDevBridge') 'exact DevBridge package ID'
Check 'conditional-optional-load' ($loadText -match 'IfModActive="lan\.deferredreality\.framework"') 'optional adapter is conditional'
Check 'optional-load-path' ($loadText -match 'OptionalDeferredReality') 'optional load folder is scoped'
Check 'optional-def-placement' ((Test-Path -LiteralPath (Join-Path $root '1.6\OptionalDeferredReality\Defs\WorldObjectDefs\DeferredReality_Wildlife.xml') -PathType Leaf) -and
    -not (Test-Path -LiteralPath (Join-Path $root '1.6\Defs\WorldObjectDefs\DeferredReality_Wildlife.xml') -PathType Leaf)) 'DRF definitions are optional'
Check 'shared-environment-properties' ($propsText -match 'RIMWORLD_MANAGED_PATH' -and $propsText -match 'WILDLIFE_HARMONY_PATH' -and
    $propsText -match 'KNOWLEDGE_FRAMEWORK_PATH' -and $propsText -match 'DEFERRED_REALITY_FRAMEWORK_PATH') 'shared properties expose documented overrides'

$normalProjects = @('Source\Herds\Herds.csproj', 'Source\Packs\PacksAndPredators.csproj', 'Source\Wildlife\Wildlife.csproj')
foreach ($project in $normalProjects) {
    $text = ReadText $project
    Check "normal-no-drf:$project" ($text -notmatch '<Reference Include="DeferredRealityFramework"') 'normal project has no DRF reference'
    Check "normal-no-devbridge:$project" ($text -notmatch 'DevBridge') 'normal project has no DevBridge reference'
    Check "normal-relative:$project" ($text -notmatch '(?i)(?:[A-Z]:[\\/]|\\\\[^\\]+[\\/]|steamapps|Workshop|USERPROFILE)') 'no machine-specific path'
}
$adapterText = ReadText 'Source\Wildlife\DeferredReality.Wildlife.csproj'
$validatorFiles = @(Get-ChildItem -LiteralPath (Join-Path $root 'DevTools') -Recurse -File -Include '*.ps1','*.cs' |
    Where-Object { $_.Name -ne 'Check-WildlifeRepositoryIntegrity.ps1' })
$projectFiles = @(Get-ChildItem -LiteralPath $root -Recurse -File -Include '*.csproj','*.props')
$scriptFiles = @(Get-ChildItem -LiteralPath $root -Recurse -File -Include '*.ps1' |
    Where-Object { $_.Name -notin @('Check-WildlifeRepositoryIntegrity.ps1', 'Verify-DeferredRealityIntegration.ps1', 'Verify-KnowledgeFramework.ps1') })
$projectAndScriptFiles = @($projectFiles + $scriptFiles)
Check 'adapter-drf-reference' ($adapterText -match '<Reference Include="DeferredRealityFramework"') 'optional adapter references DRF'
Check 'adapter-optional-output' ($adapterText -match 'OptionalDeferredReality[\\/]Assemblies') 'optional adapter output is scoped'
Check 'adapter-no-devbridge' ($adapterText -notmatch 'DevBridge') 'optional adapter has no DevBridge reference'
Check 'no-insight-canvas' ((GetCombinedText $projectAndScriptFiles) -notmatch '(?i)InsightCanvas') 'InsightCanvas is absent'
Check 'no-reflection-only-load' ((GetCombinedText $validatorFiles) -notmatch 'ReflectionOnlyLoadFrom') 'validators use metadata reader'
$combinedProjectScriptText = GetCombinedText $projectAndScriptFiles
$forbiddenPathTokens = @('KnowledgeFramework' + '\\', 'DeferredRealityFramework' + '\\', 'RimWorldDevBridge' + '\\', 'C:' + '\\', 'USERPROFILE', 'steamapps', 'Workshop')
$forbiddenPath = @($forbiddenPathTokens | Where-Object { $combinedProjectScriptText.IndexOf($_, [StringComparison]::OrdinalIgnoreCase) -ge 0 })
Check 'no-sibling-paths' ($forbiddenPath.Count -eq 0) ('forbidden=' + ($forbiddenPath -join ', '))

$trackedIntermediate = @(git -C $root ls-files -- '*/bin/*' '*/obj/*' 'bin/*' 'obj/*')
Check 'tracked-intermediates' ($trackedIntermediate.Count -eq 0) ('count=' + $trackedIntermediate.Count)
$gitignore = ReadText '.gitignore'
Check 'ignore-bin-obj' ($gitignore -match '(?m)^\*\*/bin/$' -and $gitignore -match '(?m)^\*\*/obj/$') 'bin/obj ignored'

if ($RequireAssemblies) {
    $normalAssemblies = @(
        (Join-Path $root '1.6\Assemblies\Herds.dll'),
        (Join-Path $root '1.6\Assemblies\PacksAndPredators.dll'),
        (Join-Path $root '1.6\Assemblies\Wildlife.dll'))
    $optionalAssembly = Join-Path $root '1.6\OptionalDeferredReality\Assemblies\DeferredReality.Wildlife.dll'
    foreach ($path in $normalAssemblies) { Check "assembly:$([IO.Path]::GetFileName($path))" (Test-Path -LiteralPath $path -PathType Leaf) 'built output' }
    Check 'assembly:DeferredReality.Wildlife.dll' (Test-Path -LiteralPath $optionalAssembly -PathType Leaf) 'optional built output'
    if ($failures.Count -eq 0) {
        $herdsMetadata = GetMetadata $normalAssemblies[0]
        $packsMetadata = GetMetadata $normalAssemblies[1]
        $wildlifeMetadata = GetMetadata $normalAssemblies[2]
        $optionalMetadata = GetMetadata $optionalAssembly
        Check 'assembly:herds-knowledge' (HasReference $herdsMetadata 'KnowledgeFramework') 'Herds references Knowledge Framework'
        foreach ($entry in @([pscustomobject]@{ Name = 'Wildlife'; Data = $wildlifeMetadata }, [pscustomobject]@{ Name = 'Packs'; Data = $packsMetadata }, [pscustomobject]@{ Name = 'Herds'; Data = $herdsMetadata })) {
            Check "assembly:$($entry.Name)-no-drf" (-not (HasReference $entry.Data 'DeferredRealityFramework')) 'normal assembly has no DRF reference'
            Check "assembly:$($entry.Name)-no-devbridge" (-not (HasReference $entry.Data 'RimWorldDevBridge')) 'normal assembly has no DevBridge reference'
            Check "assembly:$($entry.Name)-no-insight" (-not (HasReference $entry.Data 'InsightCanvas')) 'normal assembly has no InsightCanvas reference'
        }
        Check 'assembly:optional-drf' (HasReference $optionalMetadata 'DeferredRealityFramework') 'optional adapter references DRF'
        Check 'assembly:optional-no-devbridge' (-not (HasReference $optionalMetadata 'RimWorldDevBridge')) 'optional adapter has no DevBridge reference'
        Check 'assembly:optional-no-insight' (-not (HasReference $optionalMetadata 'InsightCanvas')) 'optional adapter has no InsightCanvas reference'
    }
}

$status = if ($failures.Count -eq 0) { 'PASS' } else { 'FAIL' }
[ordered]@{ status = $status; checks = $checks.ToArray(); failures = $failures.ToArray() } | ConvertTo-Json -Depth 5
if ($failures.Count -ne 0) { exit 1 }
