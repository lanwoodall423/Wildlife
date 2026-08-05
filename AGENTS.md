# Wildlife

- Package ID: `Lan.Wildlife`.
- Adapter source: `Source/Herds/WildlifeDevBridge.cs`; loaded-module manifest output: `DevTools/BridgeAdapters`.
- Build: `dotnet build Source\Herds\Herds.csproj -c Release`; validate/build binding: `DevTools\Build-WildlifeBridgeAdapter.ps1`, `DevTools\Test-WildlifeBridgeAdapter.ps1`.
- Query fresh live Dev Bridge context before runtime tests with the Dev Bridge checkout's `DevTools\devbridge.ps1`.
- Wildlife module changes require a full restart. An adapter reload alone cannot replace `Herds.dll`.
- Wildlife owns the provider and controls its optional integration; Dev Bridge is optional.
- Full workflow: `DevTools/DEVBRIDGE_AGENT.md`.
