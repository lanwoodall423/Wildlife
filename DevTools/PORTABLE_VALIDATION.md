# Portable Wildlife validation

**WILD-17-01A/B (migration in progress):** The paths and commands below describe the **future 1.7 build target**, not a validated 1.7 package. The 33 original 1.6 XML Defs/localization/patch files have initial 1.7 mirrors for inspection, but 13 textures and four 1.6 compiled DLLs have not migrated. `About/About.xml` and `LoadFolders.xml` still load the 1.6 package until the content migration and native/optional integration work has been validated. See `DevTools/WILDLIFE_17_STAGING.md` for exact gates. The install script deliberately refuses to deploy 1.7 binaries to an unmigrated target. Do not release or install this intermediate configuration. RimTest remains the required runtime owner; no in-game compatibility result is implied by the source changes.

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
$env:RIMBRIDGE_SERVER_SDK_PATH = '.../RimBridgeServer/1.7/Assemblies/RimBridgeServer.Sdk.dll'
```

Once rebuilt against RimWorld 1.7, normal assemblies are expected in `1.7/Assemblies` and must not reference Deferred Reality Framework, DevBridge2, or RimBridgeServer. The optional Deferred Reality adapter must be rebuilt separately into `1.7/OptionalDeferredReality/Assemblies` and will only be loaded after the 1.7 content package and conditional LoadFolders entry are completed. The RimBridgeServer companion project writes to the game-root `BridgeTools/Wildlife/Wildlife.BridgeTools.dll` bundle beside the `Mods` folder; do not copy `RimBridgeServer.Sdk.dll` beside it. Required RimBridge mode makes DevBridge2 include and validate RimBridgeServer in the managed profile.

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
