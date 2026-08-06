# Portable Wildlife Builds

The projects do not infer an installed RimWorld or sibling mod location. Set these environment variables, or pass the equivalent MSBuild properties, before building:

```powershell
$env:RIMWORLD_MANAGED_PATH = '.../RimWorldWin64_Data/Managed'
$env:WILDLIFE_HARMONY_PATH = '.../0Harmony.dll'
$env:KNOWLEDGE_FRAMEWORK_PATH = '.../Knowledge-Framework'
$env:DEFERRED_REALITY_FRAMEWORK_PATH = '.../DeferredRealityFramework'
$env:RIMWORLD_EXE = '.../RimWorldWin64.exe'
$env:RIMWORLD_DATA_PATH = '.../RimWorld user data directory'
$env:RIMWORLD_DEVBRIDGE_ROOT = '.../RimWorldDevBridge'
```

`KnowledgeFrameworkPath` and `DeferredRealityFrameworkRoot` are explicit MSBuild overrides. The corresponding environment variables are `KNOWLEDGE_FRAMEWORK_PATH` and `DEFERRED_REALITY_FRAMEWORK_PATH`. `RimWorldManagedPath` and `WildlifeHarmonyPath` are also accepted as MSBuild properties.

Normal assemblies are built into `1.6/Assemblies` and do not reference Deferred Reality Framework or RimWorld DevBridge. The optional `DeferredReality.Wildlife` project is built into `1.6/OptionalDeferredReality/Assemblies` and is loaded only when `lan.deferredreality.framework` is active.

Build and validate with PowerShell 7:

```powershell
dotnet build Source/Herds/Herds.csproj -c Release --no-restore
dotnet build Source/Packs/PacksAndPredators.csproj -c Release --no-restore
dotnet build Source/Wildlife/Wildlife.csproj -c Release --no-restore
dotnet build Source/Wildlife/DeferredReality.Wildlife.csproj -c Release --no-restore
pwsh -File DevTools/Check-WildlifeRepositoryIntegrity.ps1 -RequireAssemblies
pwsh -File DevTools/Verify-KnowledgeFramework.ps1
pwsh -File DevTools/Verify-DeferredRealityIntegration.ps1
pwsh -File DevTools/Build-WildlifeBridgeAdapter.ps1
pwsh -File DevTools/Test-WildlifeBridgeAdapter.ps1
```

The assembly metadata validator uses `System.Reflection.Metadata` and `PEReader`; it does not load production assemblies through `ReflectionOnlyLoadFrom`.
