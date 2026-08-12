# Portable Wildlife validation

Set the build and coordinator locations explicitly:

```powershell
$env:RIMWORLD_MANAGED_PATH = '.../RimWorldWin64_Data/Managed'
$env:WILDLIFE_HARMONY_PATH = '.../0Harmony.dll'
$env:KNOWLEDGE_FRAMEWORK_PATH = '.../KnowledgeFramework'
$env:INSIGHT_CANVAS_PATH = '.../InsightCanvas'
$env:DEFERRED_REALITY_FRAMEWORK_PATH = '.../DeferredRealityFramework'
$env:RIMWORLD_DATA_PATH = '.../RimWorld user data directory'
$env:WILDLIFE_DEVBRIDGE2_ROOT = '.../DevBridge2'
```

Normal assemblies are built into `1.6/Assemblies` and do not reference Deferred Reality Framework or DevBridge2. The optional Deferred Reality adapter is built into `1.6/OptionalDeferredReality/Assemblies` and is conditionally loaded. `Wildlife.DevHarness.dll` is a development-only optional assembly; install it when running the automatic in-game suite.

Build and validate with PowerShell 7:

```powershell
dotnet build Source/Herds/Herds.csproj -c Release
dotnet build Source/Packs/PacksAndPredators.csproj -c Release
dotnet build Source/Wildlife/Wildlife.csproj -c Release
dotnet build Source/Wildlife/DeferredReality.Wildlife.csproj -c Release
dotnet build Source/DevHarness/Wildlife.DevHarness.csproj -c Release
pwsh -File DevTools/Check-WildlifeRepositoryIntegrity.ps1 -RequireAssemblies
pwsh -File DevTools/Verify-KnowledgeFramework.ps1
pwsh -File DevTools/Verify-DeferredRealityIntegration.ps1
```

Runtime validation is coordinator-owned:

```powershell
pwsh -File DevTools/Restart-Wildlife.ps1
pwsh -File DevTools/Invoke-WildlifeTest.ps1
```

The scripts use shared test leases and explicit Wildlife request/result files. They do not launch or kill RimWorld.
