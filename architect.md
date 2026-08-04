# Shared Wildlife Interface

The Wildlife pawn-table tab owns a small ordered navigation registry. Wildlife registers its
own overview and expedition destinations; optional companion mods register their existing
`MainButtonDef` destinations through reflection after defs load.

## Invariants

- Navigation order is Wildlife Journal (0), Horticulture (10), Aquaculture (20), then
  Expeditions (30).
- The stable `wildlife.overview` entry opens the Journal Field Log; integrations should keep
  using the existing ID and public `Register` signature.
- The Journal top-level navigation is Field Log, Knowledge, Region, and Chronicle. Existing
  detail pages remain available through contextual links and direct constructors.
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

## Expedition and trail extensions

- Studied trails identify animals already beyond the local map and launch exact-animal
  expeditions through the existing expedition setup and record lifecycle.
- Trail expedition routes are persisted as world-tile edges and provide a 6% caravan
  movement-cost reduction, deliberately weaker than a road.
- Expedition events are data-driven `ExpeditionEventDef` records. Their choices carry
  generic delay, encounter, success, danger, knowledge, and turn-back consequences so new
  events can be added in XML without another window or state machine.
- Wildlife focus actions share `WildlifeUI`, which closes Wildlife dialogs and the Wildlife
  main tab before selecting the target and moving the camera.

## Shared knowledge adapter

- `KnowledgeFramework.dll` owns the single collapsible pawn Bio panel and the common Novice,
  Adept, Expert, and Master rank contract.
- Wildlife retains `colonistSpeciesKnowledge` and `colonistBiomeKnowledge` as its authoritative
  save records; the adapter reads those records directly and never mirrors them.
- Observation, trails, hunts, handling, training, and animal tending feed the existing `Learn`
  method. Shared ranks govern hunting, handling, and tending bonuses.
- Clicking Wildlife in the shared panel opens the existing colonist wildlife-knowledge window.
- Both existing Wildlife knowledge window entry points delegate to the shared detailed menu.
  Colonist mode preserves animal and biome records; Colony mode preserves accumulated animal
  knowledge and exposes best colony expertise without adding or renaming save fields.

## Landscape growth performance

- The plant growth Harmony postfix rejects unspawned, non-growing, and non-grass plants before
  accessing map components. Ordinary crops and trees therefore retain the vanilla hot path.
- `WildlifeLandscapeMapComponent` caches only active grazing-ground features and their obstruction
  effectiveness. The cache initializes after save load, refreshes during the existing landscape
  scan, and updates immediately when a grazing ground forms.
- Per-plant growth checks iterate that bounded feature cache; they never enumerate the map's full
  thing list. With no grazing grounds, the lookup is constant time and returns zero.

## Predator pressure evidence

- `HerdMapComponent.UpdateDefense` and `UpdateSentinels` remain the authorities for group threat
  detection, defense response, and truthful or false alarm production. `WildlifeSignalCulture`
  owns the persisted trace and listener gate; no separate pressure simulation or event store is
  introduced.
- Eligible listeners contribute a `PredatorPressure` Knowledge Framework observation to the
  existing population subject and `FacetPopulation` claim. Its stable identity is
  `wildlife:predator-pressure:<map>:<trace>:<observer>` and its recipe uses unique source-instance
  accrual. Presentation markers remain on existing signal trace presentations for save-safe retry
  and reload deduplication.
- The adapter owns V3 claim submission and qualitative state queries. Ecology snapshots, the
  Knowledge and Region Journal hubs, and Field Log rows consume those projections without writing
  Journal state. `WildlifeEventRouter` remains session-local and is not durable evidence.
- The slice reuses existing defense and signal cadences. It adds no map-wide pressure scan or
  persisted aggregate. Missing new fields on old saves default safely; existing warning, legacy
  signal, group, population, and expedition records remain authoritative.

## Representative signal-family boundary

- The representative family is the existing animal `Alarm` call. `HerdSimulation` emits it through
  `WildlifeSignalCultureMapComponent.NotifyAnimalSignal`; `Broadcast` persists the trace;
  `TeachColonists` gates eligible listeners by signal proximity or manned observation-post
  influence; and `VerifyActiveSignals` records the observable response after the existing delay.
- Eligible warning observations use `WildlifeKnowledgeAdapter.ObserveWarningCall` on the map/species
  dialect subject, `FacetSignals`, and the existing `signal-meaning` claim. The first claim remains
  an unfamiliar warning record, repeated trace identities establish family recognition, later
  observations can interpret meaning, and non-contradictory understood evidence can support a
  cautious prediction. Same-trace colony/witness copies are counted as one equivalent observation.
- `HuntingKnowledge` remains a loadable legacy compatibility view. If an old save has signal
  understanding but no V3 claim, the signal component supplies a qualitative legacy projection;
  it does not fabricate a V3 meaning claim or write new evidence.
- `WildlifeSignalTrace` and its existing observer presentations remain the persisted signal and
  processing owners. Stable source identities, claim provenance, presentation markers, and the
  existing history/presentation caps prevent replay after reload. Developer scenarios are marked
  on the trace and cannot become ecological pressure evidence.
- Normal signal detail, Field Log, Knowledge, Region, and Ecology Snapshot surfaces use qualitative
  warning language. Hidden cause, trace identity, listener processing, claim provenance, and exact
  response diagnostics remain in developer-only bridge traces.
