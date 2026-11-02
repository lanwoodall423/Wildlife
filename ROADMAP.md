# Wildlife Roadmap

This roadmap tracks planned Wildlife features and their intended implementation sequence. Entries describe design goals, not release commitments. Features remain unimplemented until explicitly marked otherwise.

## Priority: RimWorld 1.7-only standalone Wildlife

**Direction:** Approved for planning; migration not implemented or validated.  
**Release support target:** RimWorld **1.7 only** (do not continue packaging or claiming 1.6 support).  
**Dependency policy:** Wildlife's core must load and provide useful functionality on a clean RimWorld 1.7 installation, without requiring third-party mods. External integrations are opt-in enhancements and must not prevent base loading.

### Migration workstreams (in priority order)

1. **WILD-17-01 — Version/package migration.** Port the current 1.6 source and Defs to RimWorld 1.7, validate relevant 1.7 API differences, move version-specific assets and assembly outputs to a 1.7-only layout, update `About/About.xml` and `LoadFolders.xml`, update development scripts/release notes/test paths, and remove the advertised 1.6 supported-version entry. Do not change the package ID or silently claim compatibility until runtime evidence exists.
2. **WILD-DEP-01 — Independent native core.** Inventory all third-party runtime and compile references, Harmony patches, conditional Defs, UI hooks and shared-knowledge calls. Remove the current mandatory `brrainz.harmony`, `lan.knowledgeframework`, and `lan.insightcanvas` dependencies from the final core distribution. Build a useful native 1.7 baseline with no third-party mods: wildlife behaviors/ecology, safe save/load, player-facing core controls, and no missing-assembly, missing-type or missing-Def errors. Preserve public functionality where practical; make features that cannot work without a companion explicitly unavailable rather than crashing.
3. **WILD-DEP-02 — Optional feature bridges.** Move integrations requiring third-party types or assemblies behind isolated, conditionally loaded adapter assemblies/Defs or narrowly guarded reflection interfaces, avoiding compile-time coupling in the base core. With **Harmony** present, enable patch-dependent enhanced AI/behavior hooks; without it, the native core still loads and uses supported vanilla extension points. With **Knowledge Framework** present, enable richer shared species learning/evidence; without it, retain an honest native knowledge/observation presentation. With **Insight Canvas** present, enable enhanced retained Journal/inspector UI; without it, provide a usable native RimWorld UI for core controls. Keep **Deferred Reality Framework** and its adjacent-region/trail integration optional, retaining its separate adapter and conditional package loading. No third-party DLLs should be bundled implicitly into Wildlife.
4. **WILD-TEST-01 — Standalone and combination matrix.** Verify fresh RimWorld 1.7 startup and gameplay with no optional dependencies, then individually with Harmony, Knowledge Framework, Insight Canvas, Deferred Reality, and representative supported combinations. Confirm conditional load paths, safe no-mod behavior, 1.7 build resolution, save/load, old-save migration behavior where supported, and no unexpected loss of core gameplay. Run repository-prescribed `rimtest doctor --json` followed by `rimtest affected --run --json` for affected changes and record actual in-game evidence. Treat compatibility, missing-runtime behavior and integration smoke tests as release gates.

### Roadmap milestones and release gates

| ID | Milestone | Status | Exit evidence |
| --- | --- | --- | --- |
| `WILD-17-01` | RimWorld 1.7-only source, metadata, and package layout | Planned | 1.7 assembly references and Defs are validated; `supportedVersions` advertises only 1.7; packaged output contains no stale 1.6 runtime payload |
| `WILD-DEP-01` | Usable standalone core without third-party mods | Planned | Core mod loads on RimWorld 1.7 with **no** Harmony, Knowledge Framework, Insight Canvas, or Deferred Reality; supported baseline features and native UI work with no missing-assembly/type/Def errors |
| `WILD-DEP-02` | Optional integration adapters | Planned | Each integration can be absent with no startup failure; when installed at a supported version, it unlocks only its declared extra features, without duplicating or overriding core ownership |
| `WILD-TEST-01` | Runtime compatibility and release evidence | Planned | RimTest-owned 1.7 mod-load and focused gameplay/save/load checks pass for the standalone configuration plus supported individual and combined optional-mod configurations |

### Optional integration feature contract

| Installed companion | No companion: required baseline | Companion present: optional enhancement |
| --- | --- | --- |
| **Harmony** | Standalone core loads and exposes its supported vanilla-extension behavior and UI without requiring `0Harmony.dll` | Harmony-isolated patch layer adds hook-dependent AI coordination and advanced behavior not possible through native extension points alone |
| **Knowledge Framework** | Native species knowledge and observation remain available at honest, bounded precision | Shared biological evidence, progression and species facets integrate through an isolated adapter |
| **Insight Canvas** | Core Journal, animal details, settings and other essential controls have usable native RimWorld presentation | Enhanced retained Journal, inspectors and navigation activate through an isolated UI adapter |
| **Deferred Reality Framework** | Local ecology and animal gameplay operate without regional deferred simulation | Existing optional adapter supplies supported deferred-region, migration and adjacent-trail features |

**Acceptance boundary:** The supported game and its shipped assemblies are inherent requirements, not optional companion mods. Internal Wildlife components must also be tested together. No optional adapter may expose another mod's types through public core signatures, require its DLL during base-load type resolution, or activate when an incompatible version is detected. Every absent-integration fallback must be honest about which features are unavailable; do not silently emulate advanced behavior or fabricate research/observation evidence.

The 1.7 migration and third-party dependency decoupling are independent release gates: a working 1.7 build with mandatory integrations is **not** `WILD-DEP-01` complete, and an independently loading core still must prove the 1.7-only package and runtime contract. Do not mark any row complete from source review or compilation alone; record exact RimTest and in-game evidence.

### WILD-17-01A — build-target preparation (not 1.7 support)

The first migration slice changes versioned **Release build outputs** and installed-companion **assembly reference defaults** to `1.7` and aligns build/install validation paths. Deployment is deliberately blocked until the **target** mod package advertises only 1.7, has a `v1.7` LoadFolders entry, and carries relocated 1.7 Defs. This is a staging change, **not** a compatible release; existing `About/About.xml`, `LoadFolders.xml`, 1.6 content/graphics, and tracked 1.6 DLLs remain unchanged.

Remaining WILD-17-01 gates: relocate all text/binary assets and remove obsolete packaged 1.6 outputs; build fresh 1.7 DLLs against actual RimWorld 1.7 references and supported companions; audit 1.7 Def/API changes; then switch metadata and LoadFolders atomically. WILD-DEP-01/02 must eliminate mandatory companion dependencies and establish independent loading before declaring a standalone release. Run RimTest's doctor/affected workflow and real 1.7 runtime/save-load matrix before checking off either milestone. Static source validation is not a substitute.

### WILD-17-01B — text asset mirrors (staged, not loaded)

The next migration slice stages **33 byte-identical XML assets** from `1.6/` into matching `1.7/` locations: 27 Def files, 2 localization files, 3 patch files, and 1 optional Deferred Reality Def. Repository integrity now verifies the initial mirrors and their optional integration placement. This prepares a concrete 1.7 Def/API review surface; copying old XML is **not** 1.7 compatibility proof.

The remaining **13 PNG graphics and four compiled DLLs** are not copied. The original `1.6/` package, `About/About.xml`, and `LoadFolders.xml` stay unchanged; the new `1.7/` text payload is not loaded or advertised. See `DevTools/WILDLIFE_17_STAGING.md` for the binary inventory, 1.7 rebuild gates, eventual 1.7-only package switch, standalone companion fallback work, and RimTest evidence required before release.

### WILD-DEP-01A — vanilla-only core shell (not standalone gameplay support)

The initial WILD-DEP-01A build candidate for `Wildlife.dll` compiled only the `WildlifeMod` entry point and referenced only RimWorld/Unity assemblies. It no longer depends at **build time** on `Herds.csproj`, `PacksAndPredators.csproj`, or `0Harmony.dll`. This creates a clear native-core assembly boundary, **not** a complete gameplay replacement: the existing Herds/Packs simulation, patch-dependent behavior, Knowledge Framework integration, Insight Canvas UI, and optional Deferred Reality adapter remain in their unchanged source projects.

The native mod settings page adds one conservative field view: counts of spawned, live, factionless animal Pawns in unfogged cells of the selected map. This is a momentary observation only; it cannot identify hidden animals, estimate latent wildlife, infer species knowledge or change saved state. Future standalone work must restore useful vanilla-path ecology/gameplay, native settings/Journal controls, and safe persistent behavior while moving optional features behind conditionally loaded adapters.

This candidate does not change `About/About.xml`, `LoadFolders.xml`, packaged 1.6 binaries, or game startup. Until 1.7 compilation, RimTest-owned real mod-load checks, no-companion checks, and save/load verification exist, do **not** claim standalone RimWorld 1.7 compatibility or remove mandatory dependencies.

### WILD-DEP-01B — native witnessed field log (not 1.7 runtime validation)

The standalone `Wildlife.dll` candidate now explicitly compiles a second vanilla-only source, `WildlifeNativeFieldLog.cs`, alongside its native mod entry point. A standard `MapComponent` samples at most 256 spawned Pawns and 24 free colonist observers every 1,800 game ticks; only spawned, living, factionless animals in unfogged cells that an awake, non-downed colonist can see directly are recorded. The collection retains at most 48 species and a capped count of successful sampled intervals, with first/last witnessed ticks. This is **not** an exact population count or a substitute for Knowledge Framework evidence.

A native mod setting can pause new sightings without destroying the existing saved field log. New Scribe keys are independent from all legacy Herds/Packs saves. The settings UI displays a small recent history and labels live unfogged counts separately from colonist-witnessed sightings. Source checks verify this boundary, but **only a real RimWorld 1.7 build plus RimTest save/load and no-companion checks** can establish compatibility. No packaged binaries, `About` dependencies, LoadFolders, or legacy gameplay have changed in this slice.

### Existing baseline and design constraints

- `About/About.xml` currently advertises 1.6 and lists Harmony, Knowledge Framework, and Insight Canvas as mandatory; `LoadFolders.xml` currently loads from `1.6`.
- `Source/Herds/Herds.csproj` currently directly requires all three assemblies. Other patch-dependent code must be audited for safe absence rather than assuming removing XML metadata is sufficient.
- The Deferred Reality adapter is already packaged conditionally; preserve that boundary and bring it forward to 1.7, rather than converting it into a hard requirement.
- Preserve existing persisted gameplay identities/keys and the established knowledge-disclosure boundary. Optional integration absence must not create fabricated observations or misleading exact biological claims.
- **Do not simply delete mandatory `modDependencies` and call this complete.** Standalone loading, behavioral fallback, build independence and actual 1.7 testing must be demonstrated first.

**Sequencing:** Establish and validate the 1.7-only standalone core and optional-integrations baseline before the Animal Exertion & Endurance feature enters implementation. The stamina feature must use native core APIs first and offer richer optional Knowledge Framework / Insight Canvas presentation only when those integrations are available.

## Planned: Animal Exertion & Endurance

**Status:** Proposed / not implemented  
**Release target:** Unscheduled

### Goal

Make pursuit and escape depend on both an animal's immediate speed and its ability to sustain exertion. Short-burst sprinters should excel at closing a short distance; endurance runners should be able to maintain pressure; prey should be able to sprint, pace itself, break contact, or seek cover. A faster animal should not automatically win every extended chase.

### Initial scope

1. **Species physiology profiles.** Define independently configurable burst-speed multiplier, burst capacity, sustained-running efficiency/endurance, recovery rate, and exhaustion resistance. Provide sensible default archetypes (sprinter, endurance runner, balanced runner, short-dash prey) and safe fallbacks for animals added by other mods. Treat numeric species profiles as balance data, not claims of biological precision.
2. **Per-animal exertion state.** Track a bounded immediate stamina reserve and slower accumulated fatigue for participating animals. Drain exertion during actual high-urgency fleeing and chasing, allow recovery when resting or moving calmly, and avoid drain from normal wandering. Persist state where individual animals remain authoritative, with backward-compatible defaults on existing saves. Respect Wildlife's active/latent population and identity-anchor boundaries; do not persist or simulate full transient Pawn state in regional aggregates.
3. **Speed and movement.** Let burst movement temporarily exceed each animal's ordinary movement speed, then taper toward a sustainable pace and, if necessary, a tired pace. Preserve vanilla speed effects from injuries, health, terrain, and other mods. Cap modifiers and reconcile existing `Herds_HuntedAdrenaline`, `Herds_FlightBurst`, and `Herds_HuntFatigue` effects so bonuses and penalties do not stack without control.
4. **Predator pursuit decisions.** Feed available stamina, fatigue, and speed sustainability into the existing Packs `HuntPhase.Chase`, `HuntJobFor`, `ScanHunts`, and `ShouldAbandonHunt` flows. Permit sprinters to commit to short attempts and disengage after failed bursts; allow endurance predators to maintain paced pursuits. Preserve hunger, pack roles, deterrents, health, distance, and current hunt-cooldown decisions.
5. **Prey escape decisions.** Integrate with Herds `HerdDefenseJobs`, flight/scatter/hide orders, and the existing predator-escape calculation. Prey can spend a burst to open distance, then slow, seek refuge, or change escape tactics when winded. Avoid repeated panic/restart loops and preserve the existing threat-end notification behavior.

### Implementation stages

- **Stage 1 — Model and instrumentation:** Implement configurable physiology and per-animal exertion, safe serialization/recovery, a bounded update cadence, and developer-only telemetry. Verify stable speed curves and stamina state before changing hunt outcomes.
- **Stage 2 — Encounter integration:** Wire exertion into existing predator and prey job/decision paths, tune chase abandonment and escape behavior, and reconcile the current movement-related Hediffs. Keep Herds and Packs optional; expose state via compatibility-safe interfaces without adding hard compile-time dependencies between companion assemblies.
- **Stage 3 — Wildlife knowledge and polish:** Reveal learned species traits (for example, *explosive sprinter*, *exceptional endurance*, *tires quickly*) through the existing Knowledge Framework / `WildlifeInsightDisclosure` presentation boundary. Prefer qualitative, evidence-gated information to exact hidden stamina values; reserve precise values for developer diagnostics. Consider individual variation, age, weather, terrain, pack-relay tactics, and training only after the core system is balanced.

### Acceptance criteria

- A short-burst sprinter can briefly outrun a faster-at-distance opponent, but loses its advantage if the encounter is prolonged.
- An endurance runner can maintain a sustainable chase longer without an indefinite speed buff.
- A prey animal can recover between threats, but repeated escapes can accumulate meaningful fatigue.
- Hunters abandon impossible or exhausting pursuits rather than continuously re-entering them; no job/threat oscillation is introduced.
- Existing hunting, defensive behavior, refuge use, and Hediff effects still work when the feature is disabled or Packs is absent.
- Existing saves load safely; state remains correct across save/reload, despawn, hiding, and active/latent wildlife transitions.
- No per-tick whole-map animal scans or unbounded per-Pawn allocations are introduced; updates focus on active encounters and bounded recovery.
- Add deterministic chase, recovery, abandonment, serialization, compatibility, and performance checks to the existing in-game test suite. Follow `AGENTS.md`: run `rimtest doctor --json` and `rimtest affected --run --json` for implementation validation.

**Out of scope for the first release:** A global stamina bar for all pawn activity, detailed cardiovascular simulation, complex relay hunting, long-term athletic training, and regional aggregate stamina.
