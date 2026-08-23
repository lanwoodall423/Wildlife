# Wildlife Migration

The `DeferredReality.Wildlife` adapter registers provider `lan.wildlife` and
imports one generic population per stable region/species subject. It reads the
existing `RegionalWildlifeMapComponent.Records` and `RoamingAnimals` projections
without changing Wildlife Scribe keys.

## Moved semantic ownership

- regional anonymous animal abundance and uncertainty;
- analytical growth while a region is latent;
- stable population subjects independent of `Map.uniqueID`;
- tagged/notable roaming animals as framework anchors;
- departure constraints and local loss cooldown constraints;
- exactly-once local death, capture, and birth deltas.

## Retained active ownership

Herd AI, packs, pathfinding, hiding, local danger, memories, traditions, signal
rendering, map Things, current jobs/Lords/reservations, UI, and player policies
remain in Wildlife. The adapter suppresses only the regional ecology and local
roaming update methods once the provider migration is committed. It keeps the
existing component as a projection/reconciliation surface, so old UI and save
records remain available.

The adapter adds one narrow `ActiveMap` projection property to the existing
regional component. It does not serialize arbitrary pawn state or move a live
pawn into aggregate storage. Anchors retain the existing RimWorld load ID and
provider provenance; restoration is a provider responsibility and is not claimed
safe for generic pawns.

Wildlife trail excursions are the explicit exception for a provider-owned live
Pawn transfer. The adapter marks generated adjacent maps with typed metadata and
its `WildlifeDeferredMapParent.regionId` identity, then creates a durable framework
excursion ticket only after the transfer host commits the exact tracker Pawn on the
destination map. The ticket retains the origin map/cell, inverse edge, outbound and
return IDs, heartbeat/lease state, and diagnostics. Trail integrations should use
the world lease API rather than assuming Herds exposes a completion callback; the
framework returns the tracker only when the task is explicitly completed or a
conservative idle lease expires. Missing origins, provider removal, interrupted
transfers, duplicate ownership, and unsafe jobs retain the adjacent map and Pawn
for retry rather than falling back to a different map or reconstructing the Pawn.

## Legacy conversion

Migration consumers are `regional:<RealityRegionId>`, version `1`, with checksum
`wildlife-regional-v1`. The legacy regional collections remain in saves until a
future cleanup phase has validated multiple save/load cycles. Missing animal Defs
are quarantined by the framework instead of silently recreated.

Knowledge subject migration is intentionally additive in this phase. Existing
Wildlife Knowledge records keep their domain and subject IDs; a later bridge can
map legacy map-context claims to the stable region context after explicit
consumer-level aliases are registered.

## InsightCanvas player presentation boundary

Wildlife's player-facing information surfaces use the InsightCanvas v2
document/host contract. The Journal and expedition suite are full retained
documents. Compact inspect paths use the shared `WildlifeInspectorCanvas` host
with a 30-tick snapshot bucket, stable IDs, bounded `VirtualList` rows, scoped
theme settings, keyboard focus, map-focus buttons, and `PostClose` overlay
cleanup. The migrated paths are:

- herd and pen-group inspect tabs;
- hiding-refuge inspect tabs;
- animal memory tabs and the timeline/social-relationship window;
- colonist species-knowledge details; and
- fieldcraft hunt setup, including target, party, risk, resource, and readiness
  choices.

Knowledge and observation gates are evaluated while the retained root is built;
callbacks only change the underlying selection/policy and request a bounded
rebuild. The UI assembly references the installed InsightCanvas package and
does not bundle its DLL. Labels for these paths are keyed in
`1.6/Languages/English/Keyed/Herds.xml`.

The following native/manual surfaces remain deliberate boundaries rather than
InsightCanvas consumers: RimWorld's world-map/tile picker and map overlays,
Knowledge Framework's own knowledge menu, and development-only diagnostic
windows. Their ownership is external or map-native. The old overview entry is
a compatibility redirect into the Journal. Regional ecology, landscape,
progression, notable-story, and trail compatibility entries remain available
to older integrations; new top-level player navigation goes through the
Journal/expedition documents and does not create a second Journal shell.

The Journal and those compatibility entries share `WildlifeInsightDisclosure`, a
presentation-only projection over Wildlife simulation, Knowledge Framework V3, colony
knowledge, and Regional Wildlife/DRF state. The projection carries stable subject IDs,
precision, certainty, provenance, evidence summaries, and allowed actions. It is the only
place where player-facing availability is decided: it never writes Knowledge Framework
state or makes gameplay decisions. Research without Knowledge does not fabricate a
species or population, Knowledge without research can still reveal learned biology, and a
spawned-but-unseen animal cannot become an exact local count or current-location focus.
Measured population text requires an authorized operational telemetry source; otherwise
the UI uses qualitative or approximate wording. Retained Pawn references are historical
context only when the animal is absent.

Journal navigation keeps the legacy enum values and stable page IDs, but the visible page
set is derived from the current snapshot. Field Log is always the safe root; unavailable
deep links fall back there and retained navigation focus is restored after rebuild. The
same projection feeds Journal Knowledge/Field Guide/Region/Living Atlas, Regional
Wildlife, Herd inspectors, and the optional reflection-based Packs boundary. Deterministic
in-game checks cover research/Knowledge mismatch, KF-only knowledge, unseen spawned
animals, hidden positions, and projection purity.

## Exactly-once event domains

Wildlife currently uses durable operation-ID markers for `consume`, `release`,
`transfer`, and `active-map-reconcile` events. Their domains are respectively
`population:<populationId>`, `transfer:<sourcePopulationId>:<destinationPopulationId>`,
and `region:<regionId>` where applicable. Herds does not currently persist a
monotonic source sequence or a replay-proof cursor for these event streams, so
the adapter deliberately leaves them durable: it does not declare a DRF sequence
domain or request marker compaction. Replaying an old event must continue to be
rejected by its stable operation ID.

If Wildlife later adds a durable source cursor, the adapter must assign sequences
at the source boundary, validate them before changing population state, advance
the DRF cursor only after the marker and state commit, and document whether gaps
are permitted. A cursor must prove that every sequence at or below it can never
be submitted again; an aggregate population value or elapsed time is not proof.

## Adapter checks and load order

The optional `DeferredReality.Wildlife` assembly loads after the framework and
Wildlife/Herds assemblies. It owns `WildlifeDeferredMapParent`, the scoped map
factory, transfer host, materialization/compression provider, and task evidence.
Run `DevTools/Verify-DeferredRealityIntegration.ps1` after building to verify
the normal Wildlife assembly has no DRF reference, the optional adapter and
framework identities are correct, the old unconditional adapter/def are absent,
and `LoadFolders.xml` gates the optional package on DRF.
The `Deferred Reality -> Verify Wildlife adjacent integration` dev action checks
the real loaded markers, core-region/owner separation, parent identity, active
projection, construction policy, compression ownership, one-ticket-per-Pawn
invariant, and durable operation policy. It is diagnostic only and does not
fabricate maps or Pawns.

Compression accepts a Wildlife-owned site representing a `core` surface region
only when the request owner, marker owner, represented region, active projection,
`WildlifeDeferredMapParent`, and all provider identity claims agree. A different
provider, parent, projection, or claim fails closed. A second provider assembly
with the same stable ID is rejected with a duplicate-installation diagnostic.

## In-game departure and reload checks

Run `rimtest run wildlife-in-game-suite --json` with the optional adapter loaded;
the RimTest recipe covers the adapter boundary. For a trail Pawn, save at each
of these boundaries: before outbound `ExitMap`, after the Pawn is resident on the
adjacent map, after a task heartbeat, and during return. Reloading must rebuild
the task association from the durable ticket, serialized `WildlifeTrailLead`,
and exact Pawn load ID without producing a progress heartbeat from the baseline.
A subsequent trail evidence/state change may renew the same task ID once.
The rebuild is deterministic and may run again after provider registration or map
post-load; it rejects duplicate active tickets, duplicate matching trails, missing
destination placement, terminal or quarantined tickets, and task-ID mismatches
rather than selecting an alternative. Runtime dictionaries are never serialized
and are reconstructed lazily when the first observation arrives.

Force a thrown or aborted `ExitMap` and an unexpected world-pawn disposition;
the source population and anchor membership must remain unchanged and the
failure must be quarantined. A successful departure transfers exactly one unit,
updates the traveling anchor after the transfer, and a duplicate completion
does not transfer again. Complete or abandon the task, save during return, and
verify the same Pawn reaches the recorded origin exactly once. These are manual
RimWorld checks; compilation and the package script do not claim them passed.
The `Pawn.ExitMap` prefix only captures immutable intent. Population, anchor, and
anchored-membership mutations occur in the exception-safe finalizer after the
original call proves the exact Pawn is an unspawned `WorldPawn`; any post-departure
membership failure is retained as a framework diagnostic.

The combined release-candidate checklist is maintained in the DRF repository's
`MANUAL_ACCEPTANCE_REPORT.md`. Adjacent regions remain disabled until that
checklist has live evidence.

## Build and CI boundary

The normal Herds, Packs, and Wildlife projects use the Wildlife-owned
`DevTools/RimWorldReferences.props` file and can build without a DRF checkout or
assembly. Set `RIMWORLD_ROOT` and `DEFERRED_REALITY_HARMONY_PATH` (or the
equivalent MSBuild properties) for local RimWorld builds. The typed
`DeferredReality.Wildlife` project is the only project that requires DRF and
reports a clear missing-framework error when its configured root is absent.
`DevTools/Check-WildlifeRepositoryIntegrity.ps1` and the GitHub integrity
workflow validate source separation, conditional loading, package layout,
PowerShell/XML syntax, and tracked-output hygiene without proprietary runtime
files. They do not replace the live RimWorld/Scribe acceptance cases above.
