# Wildlife feature ownership and optional integration contract

**Stage:** WILD-DEP-02A. Architecture/managed source candidate, not a validated RimWorld 1.7 build or a standalone release.

## One gameplay model, optional extensions

The goal is to isolate **existing** Wildlife behavior from each framework's specialized mechanisms. Do **not** clone Herds/Packs simulation, build a second biological learning engine, or reimplement Insight Canvas's widget stack in the vanilla assembly. An absent integration may reduce optional features; it must not crash the base Wildlife mod.

| Capability / state | Owner | Standalone presentation | Installed-framework enhancement |
| --- | --- | --- | --- |
| Confirmed local species receipt | `WildlifeNativeFieldLog` (`nativeWildlifeWitnessedSightingsV1`, max 48 species) | Vanilla `WildlifeNativeFieldJournalWindow` shows first/last witnessed tick and bounded sampled checks | Rich journal may present additional *independently sourced* knowledge; do not replay a receipt to generate extra evidence |
| Rich species knowledge, evidence, claims, ranks | Existing `Herds/WildlifeKnowledgeAdapter` and `HuntingKnowledgeMapComponent` while Knowledge Framework is available | Only confirmed native receipts: no inferred confidence, XP/ranks, population, invented knowledge or progression replacement | Knowledge Framework processes observations, biological evidence, claim provenance, confidence and ranks through its existing APIs |
| Journal navigation and species inspectors | Basic native view in `Wildlife.dll`; existing rich `Herds/WildlifeJournal.cs` for enhanced mode | Basic read-only field journal, no duplicated rich widget engine | Insight Canvas renders the existing richer Wildlife Journal, pages, inspector and navigation when the dependent assembly successfully loads |
| Animal behavior, herds, packs and predator encounters | Existing Wildlife-owned Herds/Packs simulation source, pending separation from framework-specific hooks | To be extracted into supported vanilla game extension points and maintained as **one** simulation, not rewritten a second time | Harmony-isolated patch assemblies may add job/AI/render hooks that cannot operate without Harmony |
| Off-map ecology, migration and adjacent-region activity | Existing conditional Deferred Reality adapter; no substitute off-map simulator in core | Active-map wildlife gameplay; no fabricated off-map population | Deferred Reality Framework supplies deferred-region integration through its already separated conditional adapter |

### Presenter registration seam

`WildlifeJournalRouting` in `Source/Wildlife/WildlifeMod.cs` contains only vanilla `Action<Map>` registration and presentation routing. The native settings page **always** offers the basic witnessed field journal. An enhanced journal button appears **only after a provider registers**. The framework-dependent `HerdsStartup` registers a delegate to the *existing* `WildlifeJournal.OpenFieldLog(map)`. The core does not load any framework assembly, use its types, infer companion presence or need it to construct the basic window.

The dependency arrow is **Herds → Wildlife**, never Wildlife → Herds. The Herds project references `Wildlife.csproj` with `Private=false`; it retains existing Harmony, Knowledge Framework and Insight Canvas compile dependencies pending actual adapter extraction. If the enhanced presenter throws, the router disables that delegate and falls back to the native journal. No new Scribe keys, knowledge events, or history mirrors are created by routing.

### Prevent double counting and competing truths

1. The native receipt is a low-precision historical fact: an awake colonist had line of sight of an unfogged, unowned wild animal of a species during a bounded scan. It is not a population estimate, a probabilistic Knowledge Framework claim, or species expertise.
2. The current Herds/KF implementation already gathers passive observation contexts and publishes knowledge. **Do not** automatically submit native receipt samples to Knowledge Framework or duplicate its progression on registration; doing so could credit the same sighting twice. If future code shares events, design a stable provenance identity and migrate/disable overlapping producers first.
3. Presenters read their respective existing records. They never own or update simulation/knowledge records merely because a window was opened. No new mirror save file or second Journal model is needed.
4. When separating Herds/Packs, **move** their actual gameplay systems into vanilla-compatible source where possible; leave Harmony patches and framework-backed evidence/UI in conditional adapters. Do not replace them with a parallel simplified animal model.

### First physical native predator schema extraction (WILD-DEP-01D)

The existing `Source/Packs/Packs/PredatorSocialStrategy.cs`, `PredatorHuntingStyle.cs`, and `PackHunterExtension.cs` are now **linked into the vanilla Wildlife build from their original paths**; the Packs project explicitly removes those items from its own compile and takes a one-way `PacksAndPredators → Wildlife` project reference. The file bodies, public `Packs.*` names and enum values, and `1.7/Patches/Packs_VanillaPackHunters.xml` class tokens are unchanged. This gives the standalone core the existing species-level predator strategy vocabulary **without inventing a second model or copying source**.

The predator `PacksSettings`, `AnimalPackSettings`, saved `PackRecord`, `PackMapComponent`, AI jobs and Harmony patches are **not** moved and continue to use the same schema through that one-way reference. These saved/gameplay types must be disentangled later using their existing implementation and specific compatibility gates, not rewritten. Moving the DefModExtension and enums changes their future defining **assembly**, so old saves and 1.7 Def type resolution require real build/mod-load/save testing; the current staged source alone is not a runtime or save-compatibility guarantee.

### Still blocking independent 1.7 operation

- `About/About.xml` still advertises RimWorld 1.6 and mandatory Harmony, Knowledge Framework and Insight Canvas; `LoadFolders.xml` still selects `1.6/`. Neither is changed by this presenter seam.
- Herds and Packs still include framework-dependent startup and Harmony patches; Herds still directly compiles against Knowledge Framework and Insight Canvas. The enhanced route is presently **registration inside Herds**, not yet an independently packaged optional adapter.
- Finish one-time code extraction/adapter packaging, 1.7 Def review, 13 texture migration, and compile new RimWorld 1.7 DLLs. Use RimTest doctor/affected and live standalone plus optional-mod load/save/gameplay checks before changing metadata or calling the mod compatible.

**Review boundary:** ProjectNexus static validation and CI checks are source-integrity evidence only. Developer acceptance and integration remain separate from installation, release and runtime validation.
