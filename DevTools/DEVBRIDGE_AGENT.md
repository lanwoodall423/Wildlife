# Wildlife and DevBridge2

Wildlife owns its gameplay assembly and its optional development harness. The harness has no compile-time dependency on DevBridge2; it only accepts an explicit Wildlife request and validates the launch identity supplied by the coordinator environment.

Use `C:\Games\Steam\steamapps\common\RimWorld\Mods\DevBridge2\DevBridge.cmd` for all launch, restart, readiness, lease, and maintenance coordination. Never start or terminate RimWorld from a Wildlife script.

The normal test flow is:

```text
DevBridge.cmd test begin --json
Invoke-WildlifeTest.ps1
DevBridge.cmd test end <exact-lease-id>
```

`Invoke-WildlifeTest.ps1` uses the lease response and the current readiness identity to write one atomic JSON request under RimWorld's save-data directory. The request includes `requestId`, `launchId`, `generation`, `suite`, `mode`, `agent`, and `leaseId`. The optional harness atomically claims the request, rejects stale launch/generation identities, serializes destructive suites, and writes a matching result record. A nonempty launch ID never authorizes a test by itself.

After a build, use `Restart-Wildlife.ps1`, which delegates to `DevBridge.cmd restart` and `wait-ready`. For assembly replacement, `Install-WildlifeBuild.ps1` acquires a lease, calls `stop <exact-lease-id>`, verifies `maintenanceReady=true` and `gameState=STOPPED`, copies and hashes the requested outputs, calls `ensure-ready <exact-lease-id>`, and ends that exact lease only after readiness returns.
