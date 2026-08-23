# Wildlife and DevBridge2

Wildlife owns its gameplay assemblies and its optional RimBridgeServer companion. The companion references only `RimBridgeServer.Sdk`; DevBridge2 owns lifecycle, readiness, generation identity, and leases.

Use `C:\Games\Steam\steamapps\common\RimWorld\Mods\DevBridge2\DevBridge.cmd` for all launch, restart, readiness, lease, and maintenance coordination. Never start or terminate RimWorld from a Wildlife script.

Use aggregate-first coordination. Register `wildlife` immediately, even when unrelated project intents or active test leases exist. DevBridge combines active registrations into one deterministic profile; a test lease delays restart or test execution, not registration. Do not wait for an exclusive Wildlife profile unless reproducing a baseline, isolating a failure observed under the combined profile, or honoring a known incompatibility.

The normal in-game test flow is RimTest-owned and must run from the Wildlife repository root:

```text
rimtest doctor --json
rimtest validate --json
rimtest run wildlife-in-game-suite --json
rimtest affected --run --json
```

The `wildlife-in-game-suite` catalog entry maps to a DevBridge2 v2 recipe. RimTest delegates the recipe, while DevBridge2 owns the exact lease, launch, generation, process identity, endpoint, companion verification, and authenticated RimBridge call. The companion invokes the retained suite through `IRimBridgeMainThread`, writes `Wildlife-InGame-Test.txt`, and returns a stable `{ success, status, suite, reportPath }` result. There is no Wildlife request-directory transport or repository test orchestrator.

After a build, use `Restart-Wildlife.ps1`, which delegates to `DevBridge.cmd restart` and `wait-ready`. For assembly replacement, `Install-WildlifeBuild.ps1` acquires a lease, calls `stop <exact-lease-id>`, verifies `maintenanceReady=true` and `gameState=STOPPED`, copies and hashes the requested outputs, calls `ensure-ready <exact-lease-id>`, and ends that exact lease only after readiness returns.
