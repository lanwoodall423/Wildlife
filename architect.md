# Shared Wildlife Interface

The Wildlife pawn-table tab owns a small ordered navigation registry. Wildlife registers its
own overview and expedition destinations; optional companion mods register their existing
`MainButtonDef` destinations through reflection after defs load.

## Invariants

- Navigation order is Wildlife Journal (0), Horticulture (10), Aquaculture (20), then
  Expeditions (30).
- The stable `wildlife.overview` entry opens the Journal Field Log; integrations should keep
  using the existing ID and public `Register` signature.
- The Journal preserves stable top-level page IDs, but exposes destinations progressively from
  authorized evidence, knowledge, records, settings, and existing capability gates. Existing
  detail pages remain available through contextual links and direct constructors when authorized.
- Reserved menu height and rendered button wrapping use the same live content width.
- Companion assemblies are never referenced by Wildlife, and companions do not reference
  the Wildlife assembly at compile time.
- A companion button exists only when that active companion successfully registers it.
- Activating a companion button delegates to RimWorld's `MainTabsRoot` and the companion's
  existing `MainTabWindow`; no interface state or drawing logic is duplicated.
- Registration IDs replace earlier entries, so initialization remains idempotent.

## Wildlife information authority and disclosure

Player-facing wildlife information has one presentation boundary:
`WildlifeInsightDisclosure` in the Herds presentation layer. It is a pure projection
over existing owner state. It emits stable subject identity, display-safe values,
precision (`qualitative`, `approximate`, or `measured`), certainty, provenance/evidence,
and allowed actions. It never submits Knowledge Framework observations, changes a
regional record, changes a herd or pack, or makes a simulation decision.

| Authority | Owns | Presentation may reveal |
| --- | --- | --- |
| Wildlife simulation | spawned animals, herd state, signs, trails, migration state, ecology calculations | only current observed behavior or physical evidence authorized by the projection |
| Knowledge Framework V3 | colony subject/facet stage, claims, confidence, evidence contribution | identity and biological facets at the recorded stage; it does not imply research capability |
| Colony wildlife knowledge | player progression and sharing gates | whether a learned facet or action is available to the colony |
| Regional Wildlife / DRF | regional records, deferred ownership, routes, aggregate populations | qualitative or approximate regional interpretations when the relevant knowledge and capability exist |
| Insight Canvas / presentation | disclosure, page availability, retained UI state, focus fallbacks | no new wildlife fact and no gameplay truth |

Research alone never creates species visibility, population, forecast, disease, or
individual-location knowledge. Knowledge without the relevant research can still show
learned biological facts, but not research-owned precision. A merely spawned animal does
not create an exact local count. Exact or measured population text requires an authorized
operational measurement such as telemetry; otherwise local presence is qualitative and
regional values are approximate or unknown. Hidden pawn positions and retained-Pawn
references are never used as current player-facing locations.

The disclosure projection is reused by the Journal's Knowledge, Field Guide, Region, and
Living Atlas pages, the native Regional Wildlife compatibility window, Herd/compact
inspectors, and the optional Packs boundary. Packs remains compile-time independent and
reflects the optional `WildlifePackPresentationAPI` only when Herds is loaded. Native
world-map pickers, map overlays, Knowledge Framework's own menu, and developer diagnostics
remain owned by their existing systems.

Journal page IDs are stable (`field-log`, `knowledge`, `region`, `chronicle`,
`field-guide`, `living-atlas`, `signals`, `investigations`, `expeditions`, `stories`),
but page availability is state-derived. A fresh colony starts with the Field Log and
only grows the navigation tree as authorized evidence, knowledge, records, or existing
research-gated actions appear. Direct deep links to an unavailable page fall back to the
Field Log and restore focus to the retained navigation control. The document remains one
Insight Canvas v2 `InsightUiDocument`/`InsightUiHost`/`InsightUiWindow` shell with bounded
virtual lists and no bundled Insight Canvas assembly.

The in-game suite includes deterministic disclosure mismatch tests for research-without-
Knowledge, Knowledge-without-research, unseen spawned animals, hidden positions, and
non-mutating projection behavior. Signal ambiguity/progression remains tested at the
existing signal authority, and save-facing Scribe field names remain unchanged.

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

## Deferred Reality regional graph

Regions describe places. Connections describe topology. Transfers/processes describe changes
across topology.

Wildlife uses DRF's persisted `RealityRegionConnection` graph for abstract migration eligibility
and adjacent-region discovery. A cardinal surface relationship is one enabled bidirectional
`cardinal-surface` connection with a deterministic ID and edge/tile metadata; it is not a
transfer journal and does not create a map. Statistical wildlife processes query outgoing
connections, apply bounded aggregate migration using elapsed time, and include the connection ID
in their deterministic operation identity. A live projection suppresses duplicate analytical
birth/death accounting, while an active player excursion can request escalation to a materialized
projection through the typed DRF process result. The connection graph exposes immediate neighbors
only; it is not a pathfinding engine.

## Simple DRF provider path

`WildlifeRealityProvider.BuildFrameworkProvider` is the reference composition
for ordinary latent simulation. The builder supplies fidelity, the bounded
analytical process, aggregate population validation/reconciliation, identity
anchor validation, constraint resolution, and diagnostics. Wildlife's
transactional map, compression, transfer, excursion, and observation-consistency
logic is attached with `UseAdvanced(this)` and remains behind the facade.

This keeps the gameplay loop readable: seed a region's aggregate species
records, schedule stable daily processes, apply elapsed-time growth and
`RealityPopulationService.Transfer` across enabled graph edges, and let DRF
pause those processes while a live projection is authoritative. The same
facade owns the registration, so no second provider registration or manual
rollback wiring is needed.

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

## Local predator-encounter evidence

- `HerdMapComponent.UpdateDefense` and `UpdateSentinels` remain the authorities for group threat
  detection, defense response, and truthful or false alarm production. Production sets the
  persisted `subjectWasPredator` fact only when the established predator classification predicate
  recognizes the Alarm subject. `WildlifeSignalCulture` owns the persisted trace and listener gate;
  no separate pressure simulation or event store is introduced.
- One shared trace produces one colony-level `PredatorPressure` Knowledge Framework observation on
  the existing population subject and `FacetPopulation` claim. Its stable event identity is
  `wildlife:predator-encounter:<map>:<trace>`. Observer-specific warning identities remain
  `wildlife:warning-signal:<map>:<trace>:<observer>`; presentation markers remain on existing
  signal presentations for save-safe retry and reload deduplication.
- The compatibility claim identifier remains `predator-pressure`, but its player-facing concept is
  repeated local predator encounters and observed herd-defense behavior, not regional predator
  abundance or sustained population pressure.
- The Region Journal projection names the existing `Predator Deterrent` as the response and opens
  the retained Regional Wildlife management surface. Its established return and migration
  calculations remain the only deterrent simulation effects.
- The adapter owns V3 claim submission and qualitative state queries. Ecology snapshots, the
  Knowledge and Region Journal hubs, and Field Log rows consume those projections without writing
  Journal state. `WildlifeEventRouter` remains session-local and is not durable evidence.
- The slice reuses existing defense and signal cadences. It adds no map-wide pressure scan or
  persisted aggregate. Missing new fields on old saves default safely; existing warning, legacy
  signal, group, population, and expedition records remain authoritative.
- `WildlifeSignalTrace.subjectWasPredator` is serialized by the existing trace owner under
  `subjectWasPredator` and defaults to false for old saves. It is required because response
  processing occurs after the production classification point and therefore cannot safely infer
  the fact later. `developerScenario` remains persisted for the same delayed-processing reason.
- Predator-pressure presentation markers remain on the existing presentation owner. The event
  source ID is written to its existing marker fields for all eligible observer presentations, while
  the Knowledge claim itself is submitted once at colony scope. No separate evidence ledger is
  introduced.

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

## Population, anchor, and observation rulebook

Wildlife keeps ordinary ecology fungible: wild muffalo, generic insects,
biomass, and other anonymous animals are `RealityPopulationRecord` quantities
with `compressionSignificance = Aggregate`. If gameplay cares which individual
an animal is—because it is named, bonded, tagged, quest-related, player-bound,
or otherwise important—it becomes a `RealityAnchorRecord` with
`compressionSignificance = Identity`; its ID is retained in the aggregate's
`anchoredMemberIds`. The aggregate amount is never the sole record of that
individual.

The Wildlife provider owns restoration of anchor payloads and RimWorld load IDs.
An anchor with a load ID is compression-safe only after Wildlife has resolved it
and marks `externalReferenceState = ProviderResolved`. Unknown, unsafe,
player-controlled, or quest-critical references veto compression. Wildlife does
not copy live Pawn graphs into latent state.

Deaths on a materialized wildlife map are reconciled into the latent aggregate
and/or retained anchor state during compression. Statistical births are disabled
for the live projection, so they cannot be counted twice. Established player or
provider facts use `RealityObservationRecord`/`RealityConstraint` with
`EstablishedFact`; disposable transient sensor detail may use `Disposable`.

## Observation-constrained rematerialization

Wildlife's consistency provider turns an established habitat observation into a
coarse habitat/population obligation: the next map must support the observed herd
or suitability, but anonymous muffalo may be regenerated at different valid cells.
A named or bonded animal is instead an identity anchor; its retained cell/edge
location is a stronger obligation and missing restoration vetoes the transaction.
The map factory receives these obligations through DRF and Wildlife validates the
reconciled projection before commit. If a later gameplay event makes a fact false,
Wildlife explicitly invalidates or supersedes the observation; it never silently
contradicts it during map generation.
