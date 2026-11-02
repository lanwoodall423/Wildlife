# RimWorld 1.7 package staging inventory

**Status:** WILD-17-01B source staging only. This is **not** a RimWorld 1.7-compatible build, an installable 1.7-only release, or an approved removal of third-party dependencies.

## Text assets staged for inspection

The 33 original UTF-8 XML assets under `1.6/` have corresponding **byte-identical initial copies** at matching paths under `1.7/`:

| Group | XML files | Staging behavior |
| --- | ---: | --- |
| `Defs/` | 27 | Original Def declarations, pending 1.7 API/Def inspection |
| `Languages/English/Keyed/` | 2 | Original keyed localization text |
| `Patches/` | 3 | Original compatibility and research patch operations |
| `OptionalDeferredReality/Defs/` | 1 | Isolated optional adjacent-region Def; never moved to unconditional `Defs/` |
| **Total** | **33** | **Not loaded yet** |

`DevTools/Check-WildlifeRepositoryIntegrity.ps1` verifies that each original XML has its 1.7 counterpart and the initial mirrors are byte-for-byte identical. The existing repository integrity runner also parses the added XML. When 1.7-specific Def edits begin, replace the **initial-mirror** equality check with explicit version-appropriate checks; do not discard the file-coverage and optional-placement checks.

## Payload that has NOT migrated

- **13 PNG textures:** five `Textures/UI/` assets, one landscape asset, and seven `Textures/Wildlife/Structures/` assets. Copy their original content without conversion only after the 1.7 package staging process can preserve tracked binary integrity.
- **Four compiled DLLs:** `Herds.dll`, `PacksAndPredators.dll`, `Wildlife.dll`, and the separately loaded `DeferredReality.Wildlife.dll`. **Do not copy 1.6 binaries into 1.7**; regenerate and verify these outputs against actual RimWorld 1.7 and supported companion APIs. The optional DRF DLL belongs only under `1.7/OptionalDeferredReality/Assemblies/`.
- **Current runtime metadata:** `About/About.xml` still advertises only 1.6 and requires Harmony, Knowledge Framework, and Insight Canvas. `LoadFolders.xml` still selects `1.6/`. Both are intentionally unchanged until the 1.7 runtime and standalone-core gates are proven.
- **Old package:** all tracked `1.6/` content remains for source preservation at this stage. Remove it from the eventual 1.7-only release rather than shipping two versioned payloads.

## Native-core boundary staged (WILD-DEP-01A)

The WILD-DEP-01A baseline `Source/Wildlife/Wildlife.csproj` originally compiled `WildlifeMod.cs` only, with vanilla RimWorld/Unity references and no project reference to Herds/Packs. The native `WildlifeMod` settings page displays a bounded read-only count of currently spawned, unfogged, factionless animals. This is a truthful field observation, not native ecology simulation, species knowledge, or a drop-in replacement for the previous Insight Canvas UI.

All Herds, Packs, Insight Canvas, Knowledge Framework, Harmony and optional Deferred Reality **gameplay sources** remain separate and unchanged for subsequent adapter and native fallback work. The shipped 1.6 compiled binaries and `About/About.xml` still have their original mandatory dependencies. No 1.7 build, dependency-free loading, RimTest, or old-save behavior has been established by this source-only step.

## Witnessed wildlife field log staged (WILD-DEP-01B)

At WILD-DEP-01B, the native-core candidate compiled `WildlifeMod.cs` plus `WildlifeNativeFieldLog.cs` only. A bounded vanilla `MapComponent` preserves sampled **colonist-witnessed** wild-animal species sightings, recording first and last game ticks and the number of sampled intervals with direct line-of-sight evidence. It limits scans to 256 spawned Pawns and 24 colonists every 1,800 ticks and stores up to 48 species. The native UI labels its unfogged live-map view separately from the sampled, witnessed saved log; a persisted toggle pauses new field-log collection without rewriting old saves.

These new native Scribe keys do not migrate or reinterpret Knowledge Framework, Herds or Packs save data. The feature is a small source-level native field journal, **not** the full wildlife simulation or verified 1.7 persistence contract. No game assembly build or RimTest run has established its runtime behavior.

## Native witnessed field journal window staged (WILD-DEP-01C)

The native assembly explicitly includes `WildlifeNativeFieldJournalWindow.cs` in addition to `WildlifeMod.cs` and `WildlifeNativeFieldLog.cs`. Wildlife settings opens a vanilla, scrollable journal of **already saved witness records** with bounded rows, species filtering, recent/species sorting, first and last observed game ticks, and sampled-sighting counts. There is no new persistence, gameplay mutation, companion reference, or hidden-animal disclosure. This is a narrow native Journal fallback, not a replacement for the previous extensive wildlife simulation and UI.

It remains uncompiled and unverified in RimWorld 1.7 until the RimTest-managed build, save/load and no-companion tests succeed; the 1.6 package and its mandatory dependencies remain unchanged.

## Original predator strategy schema linked into core (WILD-DEP-01D)

The native `Wildlife.csproj` compiles **the original three, unchanged** `Source/Packs/Packs/` source files defining predator social strategies, hunting styles and their existing `Packs.PackHunterExtension` XML Def extension. The legacy Packs project excludes the same three files and references Wildlife one-way. The existing `PackMapComponent`, saved pack records and Harmony-dependent AI stay where they are and **are not duplicated**. The original `Packs.PackHunterExtension` XML `Class` token remains unchanged. New integrity assertions enforce single source ownership; only runtime builds can establish absence of assembly identity and Def/load/save regressions.

This is groundwork for migrating the existing simulation rather than writing another. No 1.7 binaries, textures, game loading metadata or third-party runtime dependencies have been changed.

## Required gates before switching to 1.7-only

1. **1.7 source/Defs:** audit all staged Def classes, Thing/Job/Thought/Research/Precept references and patches against the installed RimWorld 1.7 environment; resolve any 1.7 C# API changes.
2. **Standalone core:** isolate Harmony patch-dependent code, Knowledge Framework coupling, and Insight Canvas UI into optional, conditionally loaded adapters; offer honest native gameplay, species information, settings and Journal fallbacks. A metadata-only removal of `modDependencies` would crash and is prohibited.
3. **Exact binaries and images:** copy/preserve all 13 PNGs into the 1.7 content tree, build fresh core DLLs into `1.7/Assemblies/`, and build the optional DRF adapter separately. No third-party DLLs bundled. Remove stale 1.6 DLLs and 1.6 package content from the release.
4. **Atomic package switch:** only after preceding gates, set `About/About.xml` to support 1.7 alone, remove mandatory companion `modDependencies`, and change `LoadFolders.xml` to `v1.7` with the conditional DRF subfolder. Keep `Lan.Wildlife` as the package ID.
5. **Verification:** run `rimtest doctor --json`, `rimtest affected --run --json`, and the explicitly defined 1.7 no-companion and supported optional-companion load/save/gameplay matrix. Record results with exact source/candidate identity; static source checks or XML parsing do not establish in-game compatibility.

The staging work must pass ProjectNexus static checks and receive developer review before integration. Neither staging nor successful CI grants install, release, or automatic review authority.
