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
$env:DEVBRIDGE_RIMBRIDGE_MODE = 'required'
# Optional: compile against the SDK shipped by the installed RimBridgeServer 2.1.0 payload.
# Omit this to use the published RimBridgeServer.Sdk 2.0.0 compile-time fallback.
$env:RIMBRIDGE_SERVER_SDK_PATH = '.../RimBridgeServer/1.6/Assemblies/RimBridgeServer.Sdk.dll'
```

Normal assemblies are built into `1.6/Assemblies` and do not reference Deferred Reality Framework, DevBridge2, or RimBridgeServer. The optional Deferred Reality adapter is built into `1.6/OptionalDeferredReality/Assemblies` and is conditionally loaded. The RimBridgeServer companion project writes to the game-root `BridgeTools/Wildlife/Wildlife.BridgeTools.dll` bundle beside the `Mods` folder; do not copy `RimBridgeServer.Sdk.dll` beside it. Required RimBridge mode makes DevBridge2 include and validate RimBridgeServer in the managed profile.

Build and validate with PowerShell 7:

```powershell
dotnet build Source/Herds/Herds.csproj -c Release
dotnet build Source/Packs/PacksAndPredators.csproj -c Release
dotnet build Source/Wildlife/Wildlife.csproj -c Release
dotnet build Source/Wildlife/DeferredReality.Wildlife.csproj -c Release
dotnet build Source/BridgeTools/Wildlife.BridgeTools.csproj -c Release
pwsh -File DevTools/Install-WildlifeBuild.ps1
pwsh -File DevTools/Check-WildlifeRepositoryIntegrity.ps1 -RequireAssemblies
pwsh -File DevTools/Verify-KnowledgeFramework.ps1
pwsh -File DevTools/Verify-DeferredRealityIntegration.ps1
```

Runtime validation is coordinator-owned:

```powershell
rimtest doctor --json
rimtest validate --json
rimtest run wildlife-in-game-suite --json
rimtest affected --run --json
```

RimTest delegates the catalog recipe to DevBridge2. DevBridge2 owns registration, launch, restart, readiness, leases, and authenticated RimBridgeServer routing; the Wildlife repository contains only the companion tool and assertion suite.
