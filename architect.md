# Shared Wildlife Interface

The Wildlife pawn-table tab owns a small ordered navigation registry. Wildlife registers its
own overview and expedition destinations; optional companion mods register their existing
`MainButtonDef` destinations through reflection after defs load.

## Invariants

- Navigation order is Wildlife Overview (0), Horticulture (10), Aquaculture (20), then
  Expeditions (30).
- Reserved menu height and rendered button wrapping use the same live content width.
- Companion assemblies are never referenced by Wildlife, and companions do not reference
  the Wildlife assembly at compile time.
- A companion button exists only when that active companion successfully registers it.
- Activating a companion button delegates to RimWorld's `MainTabsRoot` and the companion's
  existing `MainTabWindow`; no interface state or drawing logic is duplicated.
- Registration IDs replace earlier entries, so initialization remains idempotent.

## Backward compatibility

The existing public `WildlifeMenuRegistry.Register` signature remains unchanged for other
integrations. The in-game suite verifies unique IDs, stable built-in ordering, optional
companion visibility, and the expected existing `MainButtonDef` window types.
