# Wildlife Release Candidate

Recommended tag: `wildlife-v0.1.0-rc.1`

The normal Wildlife and Herds assemblies remain independent of Deferred Reality.
The optional `DeferredReality.Wildlife` adapter is loaded only from
`1.6/OptionalDeferredReality` when `lan.deferredreality.framework` is active.

## Included

- Provider-owned adjacent materialization, task evidence, exact-Pawn transfer,
  and recovery integration.
- Transactional `Pawn.ExitMap` departure handling with durable operation IDs.
- Standalone Wildlife/Herds assembly-reference checks and optional-package
  verification.

## Release Checklist

Build `Source/Herds/Herds.csproj`, `Source/Wildlife/Wildlife.csproj`, and the
optional adapter separately. Run `DevTools/Verify-DeferredRealityIntegration.ps1`
and the Wildlife bridge checks. Then execute the combined manual acceptance
report from the DRF repository in RimWorld. Do not enable adjacent regions by
default or describe them as production-ready before the live checklist passes.
