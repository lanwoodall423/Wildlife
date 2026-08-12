# Wildlife

- Package ID: `Lan.Wildlife`.
- Gameplay build: `dotnet build Source\Herds\Herds.csproj -c Release`; optional test harness: `dotnet build Source\DevHarness\Wildlife.DevHarness.csproj -c Release`.
- Wildlife runtime validation is explicit and Wildlife-owned; use `DevTools\Invoke-WildlifeTest.ps1`.
- Launch, restart, leases, readiness, and maintenance are coordinated only by DevBridge2. See `DevTools/DEVBRIDGE_AGENT.md`.
- Wildlife has no runtime or compile-time dependency on DevBridge2.
