using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using HarmonyLib;
using KnowledgeFramework;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Herds
{
    public enum WildlifeKnowledgeObservation
    {
        Sighting,
        Study,
        Tracks,
        TrailCompletion,
        Call,
        Hunt,
        Tending,
        Taming,
        Survey,
        Expedition,
        MysteryEvidence,
        Storytelling,
        Report,
        Documentation
    }

    public sealed class WildlifePassiveObservationResult
    {
        public bool applied;
        public bool meaningful;
        public string discoveryKind;
        public string summary;
        public KnowledgeChange change;
    }

    /// <summary>
    /// The sole Wildlife integration boundary with Knowledge Framework V3.
    /// Wildlife systems provide facts; this adapter translates them into recipes and queries.
    /// </summary>
    public static class WildlifeKnowledgeAdapter
    {
        public const string DomainId = "wildlife";
        public const string SpeciesArchetype = "wildlife.species";
        public const string PopulationArchetype = "wildlife.regional-population";
        public const string IndividualArchetype = "wildlife.notable-individual";
        public const string BiomeArchetype = "wildlife.biome";
        public const string DialectArchetype = "wildlife.signal-dialect";

        public const string FacetIdentity = "identity";
        public const string FacetHabitat = "habitat";
        public const string FacetDiet = "diet";
        public const string FacetMovement = "movement";
        public const string FacetSocial = "social-behavior";
        public const string FacetSignals = "signals";
        public const string FacetDanger = "danger";
        public const string FacetAnatomy = "anatomy";
        public const string FacetHunting = "hunting";
        public const string FacetHandling = "handling";
        public const string FacetPopulation = "population-ecology";

        public const string StageUnknown = "unknown";
        public const string StageSighted = "sighted";
        public const string StageIdentified = "identified";
        public const string StageStudied = "studied";
        public const string StageUnderstood = "understood";
        public const string StageDocumented = "documented";

        public const string ContextSector = "wildlife.map-sector";
        public const string ContextMap = "wildlife.map";
        public const string ContextWorldRegion = "wildlife.world-region";
        public const string ContextBiome = "wildlife.biome";
        public const string ContextGlobal = "wildlife.global";

        private const string MigrationId = "wildlife.v3.legacy";
        private const int MigrationVersion = 1;
        private const string ExpertiseTrack = "fieldcraft";
        private const string ExpertiseNamespace = "wildlife.fieldcraft";
        private static bool registered;

        private static readonly string[] SpeciesFacets =
        {
            FacetIdentity, FacetHabitat, FacetDiet, FacetMovement, FacetSocial,
            FacetSignals, FacetDanger, FacetAnatomy, FacetHunting, FacetHandling, FacetPopulation
        };

        public static void Register()
        {
            if (registered) return;
            try
            {
                KnowledgeRegistry.BuildDefSchemas();
                RegisterContexts();
                KnowledgeDomainRegistration registration = BuildRegistration();
                KnowledgeRegistry.RegisterDomain(registration, new KnowledgeRegistrationOptions
                {
                    source = "wildlife.v3",
                    priority = int.MaxValue,
                    conflict = KnowledgeRegistrationConflict.Replace
                });
                RegisterRelationsAndComparisons();
                KnowledgeV3Ui.Register(new WildlifeV3UiProvider(), true);
                KnowledgeDomainRegistry.RegisterUi(new WildlifeKnowledgeUiProvider());
                KnowledgeRegistry.InvalidateSubjects(DomainId);
                registered = true;
            }
            catch (Exception exception)
            {
                Log.ErrorOnce("Wildlife V3 registration failed: " + exception.Message, 0x51A701);
            }
        }

        public static string SpeciesSubjectId(ThingDef species) => species == null ? null : "species:" + species.defName;
        public static string PopulationSubjectId(Map map, ThingDef species) => map == null || species == null ? null : "population:" + map.uniqueID + ":" + species.defName;
        public static string IndividualSubjectId(Pawn animal) => animal == null ? null : "individual:" + animal.thingIDNumber;
        public static string RegionSubjectId(Map map) => map == null ? null : "region:" + map.uniqueID;
        public static string BiomeSubjectId(BiomeDef biome) => biome == null ? null : "biome:" + biome.defName;
        public static string DialectSubjectId(Map map, ThingDef species) => map == null || species == null ? null : "dialect:" + map.uniqueID + ":" + species.defName;

        public static KnowledgeContextKey ContextFor(Map map, IntVec3 cell = default(IntVec3))
        {
            if (map == null) return new KnowledgeContextKey(ContextGlobal, "global");
            IntVec3 actual = cell.IsValid ? cell : map.Center;
            string sector = map.uniqueID + ":" + Mathf.Max(0, actual.x / 12) + ":" + Mathf.Max(0, actual.z / 12);
            return new KnowledgeContextKey(ContextSector, sector);
        }

        public static KnowledgeContextKey MapContext(Map map) => map == null
            ? new KnowledgeContextKey(ContextGlobal, "global")
            : new KnowledgeContextKey(ContextMap, map.uniqueID.ToString());

        public static KnowledgeContextKey BiomeContext(BiomeDef biome) => biome == null
            ? new KnowledgeContextKey(ContextGlobal, "global")
            : new KnowledgeContextKey(ContextBiome, biome.defName);

        public static KnowledgeFacetSnapshotV2 Facet(Pawn pawn, ThingDef species, string facetId = FacetIdentity,
            Map map = null, bool colony = false)
        {
            Register();
            KnowledgeContextKey context = map == null ? KnowledgeContextKey.Empty : ContextFor(map);
            return KnowledgeQuery.Facet(DomainId, SpeciesSubjectId(species), facetId, pawn,
                colony ? KnowledgeScope.Colony : KnowledgeScope.Personal, true, true, context,
                KnowledgeContextFallbackMode.ParentThenGlobal);
        }

        public static float PersonalKnowledge(Pawn pawn, ThingDef species, string facetId = FacetIdentity) =>
            pawn == null || species == null ? 0f : Facet(pawn, species, facetId).amount;

        public static float ColonyKnowledge(ThingDef species, string facetId = FacetIdentity)
        {
            if (species == null) return 0f;
            KnowledgeFacetSnapshotV2 value = Facet(null, species, facetId, null, true);
            if (value.amount > 0f || GameComponent_KnowledgeFramework.Current != null) return value.amount;
            return LegacyColonyExperience(species);
        }

        public static float ColonyConfidence(ThingDef species, string facetId = FacetIdentity) =>
            species == null ? 0f : Facet(null, species, facetId, null, true).confidence;

        public static string PersonalStage(Pawn pawn, ThingDef species) => StageFor(species, pawn, KnowledgeScope.Personal);
        public static string ColonyStage(ThingDef species) => StageFor(species, null, KnowledgeScope.Colony);

        public static string StageFor(ThingDef species, Pawn pawn, KnowledgeScope scope)
        {
            if (species == null) return StageUnknown;
            Register();
            KnowledgeSubjectSnapshotV2 state = KnowledgeQuery.Subject(DomainId, SpeciesSubjectId(species), pawn, scope);
            return state.stageId.NullOrEmpty() ? StageUnknown : state.stageId;
        }

        public static int StageOrder(string stageId)
        {
            if (stageId == StageDocumented) return 5;
            if (stageId == StageUnderstood) return 4;
            if (stageId == StageStudied) return 3;
            if (stageId == StageIdentified) return 2;
            if (stageId == StageSighted) return 1;
            return 0;
        }

        public static int TierFor(Pawn pawn, ThingDef species)
        {
            int order = StageOrder(PersonalStage(pawn, species));
            return order >= 5 ? 3 : order >= 4 ? 2 : order >= 2 ? 1 : 0;
        }

        public static int ColonyTierFor(ThingDef species)
        {
            int order = StageOrder(ColonyStage(species));
            return order >= 5 ? 3 : order >= 4 ? 2 : order >= 2 ? 1 : 0;
        }

        public static KnowledgeRank ExpertiseFor(Pawn pawn) =>
            KnowledgeQuery.Expertise(DomainId, pawn, ExpertiseTrack).rank;

        public static float ExpertiseProgressFor(Pawn pawn) =>
            KnowledgeQuery.Expertise(DomainId, pawn, ExpertiseTrack).progress;

        public static bool Observe(Pawn observer, ThingDef species, WildlifeKnowledgeObservation observation,
            Map map = null, bool success = true, float quality = 1f, string summary = null,
            string sourceInstanceId = null, bool documented = false, IReadOnlyList<Pawn> witnesses = null,
            IReadOnlyList<KnowledgeMeasurement> measurements = null, KnowledgeEvidenceDisposition disposition = KnowledgeEvidenceDisposition.Supporting,
            float directKnowledge = 0f, IReadOnlyDictionary<string, string> metadata = null)
        {
            if (species?.race?.Animal != true) return false;
            Register();
            string recipe = RecipeId(observation);
            KnowledgeObservation value = new KnowledgeObservation
            {
                observer = observer,
                domainId = DomainId,
                subjectId = SpeciesSubjectId(species),
                facetId = FacetIdentity,
                observationId = recipe,
                methodId = recipe,
                quality = Mathf.Clamp(quality, 0.05f, 8f),
                success = success,
                disposition = disposition,
                witnesses = witnesses,
                shareable = true,
                documented = documented,
                source = "Wildlife",
                sourceInstanceId = sourceInstanceId ?? InstanceId(observer, species, recipe),
                reasonId = recipe,
                context = ContextFor(map, observer?.Position ?? map?.Center ?? IntVec3.Invalid),
                summary = summary,
                metadata = metadata,
                claimMeasurements = measurements,
                directKnowledge = Mathf.Max(0f, directKnowledge),
                notify = false
            };
            KnowledgeTransactionResult result = KnowledgeEngine.Submit(value);
            if (result.success)
            {
                WildlifeEventUtility.Publish(EventKindFor(observation), map ?? observer?.MapHeld, observer, null, species,
                    "Knowledge observation", summary ?? recipe, recipe, success, quality, quality >= 1f ? 0.7f : quality * 0.55f,
                    directKnowledge, documented, value.sourceInstanceId, recipe, observer?.Position ?? IntVec3.Invalid, witnesses, metadata);
                return true;
            }
            return false;
        }

        public static bool Learn(Pawn observer, ThingDef species, float amount, bool success = false, bool failure = false)
        {
            WildlifeKnowledgeObservation observation = success ? WildlifeKnowledgeObservation.Study : WildlifeKnowledgeObservation.Sighting;
            if (failure) observation = WildlifeKnowledgeObservation.Hunt;
            float quality = Mathf.Clamp(amount / 18f, 0.15f, 3.5f);
            return Observe(observer, species, observation, observer?.MapHeld, success || !failure, quality,
                success ? "A successful field outcome improved the record." : failure ? "A failed attempt revealed a limit or risk." : "A field observation added a small piece of evidence.",
                null, false, null, null, failure ? KnowledgeEvidenceDisposition.Contradictory : KnowledgeEvidenceDisposition.Supporting,
                0f, new Dictionary<string, string> { ["observationLayer"] = "deliberate" });
        }

        public static WildlifePassiveObservationResult ApplyPassiveObservation(Pawn observer, ThingDef species, float amount,
            Map map, int day, string contextKey, string contextLabel, bool firstColony, bool reacquired)
        {
            WildlifePassiveObservationResult output = new WildlifePassiveObservationResult();
            if (observer == null || species?.race?.Animal != true || amount <= 0f) return output;
            Register();
            KnowledgeFacetSnapshotV2 before = Facet(observer, species, FacetIdentity);
            string sourceInstanceId = PassiveInstanceId(observer, species, day);
            KnowledgeObservation value = new KnowledgeObservation
            {
                observer = observer,
                domainId = DomainId,
                subjectId = SpeciesSubjectId(species),
                facetId = FacetIdentity,
                observationId = RecipeId(WildlifeKnowledgeObservation.Sighting),
                methodId = "passive-sighting",
                quality = Mathf.Clamp(amount / 18f, 0.15f, 3.5f),
                novelty = contextKey.NullOrEmpty() ? 0.2f : 1f,
                repetition = contextKey.NullOrEmpty() ? 0.2f : 1f,
                success = true,
                shareable = true,
                source = "Wildlife passive familiarity",
                sourceInstanceId = sourceInstanceId,
                reasonId = "passive-sighting",
                context = ContextFor(map, observer.Position),
                summary = "Routine field familiarity with " + species.LabelCap + ".",
                metadata = new Dictionary<string, string>
                {
                    ["observationLayer"] = "passive-familiarity",
                    ["contextKey"] = contextKey ?? string.Empty
                },
                notify = false
            };
            KnowledgeTransactionResult result = KnowledgeEngine.Submit(value);
            if (!result.success) return output;
            output.applied = true;
            output.change = BestChange(result.changes);
            bool firstObserver = before == null || before.evidenceCount <= 0;
            bool stageAdvanced = output.change != null && StageOrder(output.change.newStageId) > StageOrder(output.change.oldStageId);
            bool newFacet = output.change != null && output.change.facetId != FacetIdentity &&
                (output.change.oldAmount <= 0.001f || output.change.oldConfidence <= 0.001f) &&
                (output.change.newAmount > output.change.oldAmount + 0.001f || output.change.newConfidence > output.change.oldConfidence + 0.01f);
            bool confidenceAdvanced = output.change != null && output.change.newConfidence - output.change.oldConfidence >= 0.05f;
            bool contextDiscovery = !contextKey.NullOrEmpty();
            output.discoveryKind = WildlifePassiveObservationPolicy.DiscoveryKind(firstColony, firstObserver, stageAdvanced,
                newFacet, confidenceAdvanced, contextDiscovery, reacquired);
            if (output.discoveryKind.NullOrEmpty()) return output;
            if (output.discoveryKind == "first-colony") output.summary = "First colony sighting of " + species.LabelCap + ".";
            else if (output.discoveryKind == "first-observer") output.summary = observer.LabelShortCap + " recorded " + species.LabelCap + " for the first time.";
            else if (output.discoveryKind == "stage-milestone") output.summary = species.LabelCap + " knowledge advanced from " + StageLabel(output.change.oldStageId) + " to " + StageLabel(output.change.newStageId) + ".";
            else if (output.discoveryKind == "facet-confirmation") output.summary = observer.LabelShortCap + " strengthened " + FacetLabel(output.change.facetId) + " evidence for " + species.LabelCap + ".";
            else if (output.discoveryKind == "new-context") output.summary = species.LabelCap + " was observed in a new context" + (contextLabel.NullOrEmpty() ? "." : ": " + contextLabel + ".");
            else output.summary = species.LabelCap + " was reacquired after a substantial absence.";
            output.meaningful = true;
            Dictionary<string, string> metadata = new Dictionary<string, string>
            {
                ["observationLayer"] = "passive-meaningful",
                ["discoveryKind"] = output.discoveryKind,
                ["observerId"] = (observer.thingIDNumber).ToString(),
                ["observerName"] = observer.LabelShortCap.ToString(),
                ["species"] = species.defName,
                ["previousAmount"] = (output.change?.oldAmount ?? before?.amount ?? 0f).ToString("0.###"),
                ["newAmount"] = (output.change?.newAmount ?? before?.amount ?? 0f).ToString("0.###"),
                ["previousConfidence"] = (output.change?.oldConfidence ?? before?.confidence ?? 0f).ToString("0.###"),
                ["newConfidence"] = (output.change?.newConfidence ?? before?.confidence ?? 0f).ToString("0.###"),
                ["previousStage"] = output.change?.oldStageId ?? StageUnknown,
                ["newStage"] = output.change?.newStageId ?? StageUnknown,
                ["facetId"] = output.change?.facetId ?? FacetIdentity,
                ["contextKey"] = contextKey ?? string.Empty,
                ["contextLabel"] = contextLabel ?? string.Empty,
                ["firstColony"] = firstColony.ToString(),
                ["firstObserver"] = firstObserver.ToString(),
                ["reacquired"] = reacquired.ToString(),
                ["amountDelta"] = (output.change == null ? 0f : output.change.newAmount - output.change.oldAmount).ToString("0.###"),
                ["confidenceDelta"] = (output.change == null ? 0f : output.change.newConfidence - output.change.oldConfidence).ToString("0.###"),
                ["observedHours"] = (amount * 0.6f).ToString("0.###"),
                ["observerCount"] = "1"
            };
            WildlifeEventUtility.Publish(WildlifeEventKind.Sighting, map, observer, null, species,
                "Meaningful field discovery", output.summary, "passive-sighting", true, value.quality,
                output.change?.newConfidence ?? before?.confidence ?? 0f,
                output.change == null ? 0f : output.change.newAmount - output.change.oldAmount, false,
                sourceInstanceId, output.discoveryKind, observer.Position, null, metadata);
            return output;
        }

        public static bool HasPassiveColonySighting(ThingDef species)
        {
            if (species == null) return false;
            for (int i = 0; i < (Find.Maps?.Count ?? 0); i++)
            {
                HuntingKnowledgeMapComponent component = Find.Maps[i]?.GetComponent<HuntingKnowledgeMapComponent>();
                if (component != null && component.HasPassiveColonySighting(species)) return true;
            }
            IReadOnlyList<WildlifeEvent> history = WildlifeEventRouter.Shared.History;
            for (int i = 0; i < history.Count; i++)
            {
                WildlifeEvent value = history[i];
                if (value?.species == species && value.metadata != null && value.metadata.TryGetValue("observationLayer", out string layer) &&
                    layer == "passive-meaningful" && value.metadata.TryGetValue("firstColony", out string first) && first == "True") return true;
                if (value?.species == species && value.success && value.observer?.Faction == Faction.OfPlayer) return true;
            }
            return false;
        }

        public static bool LearnBiome(Pawn observer, BiomeDef biome, float amount, bool completed = false)
        {
            if (observer == null || biome == null) return false;
            Register();
            KnowledgeObservation value = new KnowledgeObservation
            {
                observer = observer,
                domainId = DomainId,
                subjectId = BiomeSubjectId(biome),
                facetId = FacetHabitat,
                observationId = RecipeId(completed ? WildlifeKnowledgeObservation.Expedition : WildlifeKnowledgeObservation.Survey),
                methodId = completed ? "expedition" : "survey",
                quality = Mathf.Clamp(amount / 12f, 0.15f, 3f),
                success = true,
                source = "Wildlife",
                sourceInstanceId = InstanceId(observer, biome, completed ? "expedition" : "survey"),
                reasonId = completed ? "expedition" : "survey",
                context = BiomeContext(biome),
                summary = completed ? "An expedition documented terrain and wildlife conditions." : "A survey improved the regional habitat estimate.",
                notify = false
            };
            KnowledgeTransactionResult result = KnowledgeEngine.Submit(value);
            if (result.success)
            {
                WildlifeEventUtility.Publish(completed ? WildlifeEventKind.Expedition : WildlifeEventKind.Survey, observer.MapHeld, observer, null, null,
                    "Biome knowledge", value.summary, value.methodId, true, value.quality, 0.7f, amount, false,
                    value.sourceInstanceId, value.reasonId, observer.Position);
                return true;
            }
            return false;
        }

        public static bool Report(Pawn reporter, ThingDef species, Map map, string summary,
            WildlifeKnowledgeObservation observation = WildlifeKnowledgeObservation.Report,
            KnowledgeContextKey context = default(KnowledgeContextKey),
            IReadOnlyList<KnowledgeMeasurement> measurements = null, bool documented = false)
        {
            if (species?.race?.Animal != true) return false;
            Register();
            KnowledgeObservation value = new KnowledgeObservation
            {
                observer = reporter,
                domainId = DomainId,
                subjectId = SpeciesSubjectId(species),
                facetId = FacetIdentity,
                observationId = RecipeId(observation),
                methodId = "reported:" + RecipeId(observation),
                targetColony = true,
                documented = documented,
                source = "Wildlife report",
                sourceInstanceId = "report:" + (reporter?.thingIDNumber ?? 0) + ":" + species.defName + ":" + (Find.TickManager?.TicksGame ?? 0),
                reasonId = "report",
                context = context.IsEmpty ? MapContext(map) : context,
                summary = summary,
                claimMeasurements = measurements,
                notify = false
            };
            KnowledgeTransactionResult result = KnowledgeEngine.Submit(value);
            if (result.success)
            {
                WildlifeEventUtility.Publish(documented ? WildlifeEventKind.Documentation : WildlifeEventKind.Report,
                    map, reporter, null, species, "Wildlife report", summary, value.methodId, true, 1f, 0.75f,
                    0f, documented, value.sourceInstanceId, "report", reporter?.Position ?? IntVec3.Invalid);
                return true;
            }
            return false;
        }

        public static bool Document(Pawn author, ThingDef species, Map map, string summary,
            IReadOnlyList<KnowledgeMeasurement> measurements = null)
        {
            return Report(author, species, map, summary, WildlifeKnowledgeObservation.Documentation,
                MapContext(map), measurements, true);
        }

        public static bool DebugSet(Pawn pawn, ThingDef species, float legacyExperience)
        {
            if (pawn == null || species?.race?.Animal != true) return false;
            Register();
            float amount = Mathf.Clamp(legacyExperience * 0.08f, 0f, 100f);
            KnowledgeTransactionResult result = KnowledgeEngine.Submit(new KnowledgeObservation
            {
                observer = pawn,
                domainId = DomainId,
                subjectId = SpeciesSubjectId(species),
                facetId = FacetIdentity,
                observationId = "legacy-import",
                methodId = "debug-set",
                directKnowledge = amount,
                suppressConfiguredKnowledge = true,
                source = "Wildlife debug",
                sourceInstanceId = "debug:" + pawn.thingIDNumber + ":" + species.defName + ":" + (Find.TickManager?.TicksGame ?? 0),
                reasonId = "debug-set",
                documented = legacyExperience >= 1200f,
                summary = "Developer-set evidence for the Wildlife V3 test harness.",
                notify = false
            });
            return result.success;
        }

        public static void TryMigrateLegacy()
        {
            Register();
            if (GameComponent_KnowledgeFramework.Current == null || KnowledgeMigrationService.IsCommitted(MigrationId, MigrationVersion)) return;
            List<ColonistSpeciesKnowledgeRecord> speciesRecords = Find.Maps.SelectMany(map =>
                map.GetComponent<HuntingKnowledgeMapComponent>()?.LegacySpeciesRecords ?? Array.Empty<ColonistSpeciesKnowledgeRecord>()).Where(value =>
                    value?.colonist != null && value.species?.race?.Animal == true && value.experience > 0f).ToList();
            List<ColonistBiomeKnowledgeRecord> biomeRecords = Find.Maps.SelectMany(map =>
                map.GetComponent<HuntingKnowledgeMapComponent>()?.LegacyBiomeRecords ?? Array.Empty<ColonistBiomeKnowledgeRecord>()).Where(value =>
                    value?.colonist != null && value.biome != null && value.experience > 0f).ToList();

            foreach (ColonistSpeciesKnowledgeRecord record in speciesRecords)
                SubmitLegacy(record.colonist, SpeciesSubjectId(record.species), FacetIdentity,
                    Mathf.Clamp(record.experience * 0.08f, 0.5f, 60f),
                    "legacy-species:" + record.colonist.thingIDNumber + ":" + record.species.defName,
                    "Imported conservatively from legacy Wildlife knowledge; no unrelated facets were inferred.");
            foreach (ColonistBiomeKnowledgeRecord record in biomeRecords)
                SubmitLegacy(record.colonist, BiomeSubjectId(record.biome), FacetHabitat,
                    Mathf.Clamp(record.experience * 0.08f, 0.5f, 60f),
                    "legacy-biome:" + record.colonist.thingIDNumber + ":" + record.biome.defName,
                    "Imported conservatively from legacy biome knowledge; no population fact was inferred.");

            ThingDef marker = DefDatabase<ThingDef>.AllDefsListForReading.FirstOrDefault(value => value?.race?.Animal == true);
            if (marker == null) return;
            KnowledgeMigrationService.Import(new KnowledgeConsumerMigration
            {
                consumerId = MigrationId,
                version = MigrationVersion,
                domainId = DomainId,
                subjectId = SpeciesSubjectId(marker),
                personalKnowledge = 0f,
                colonyKnowledge = 0f,
                expertise = 0f
            });
        }

        public static bool CanMakePolicyDecision(ThingDef species, WildlifeManagementPolicy policy, Pawn pawn, out string reason)
        {
            reason = null;
            if (ColonyTierFor(species) < 2 || ColonyConfidence(species) < 0.42f)
            {
                reason = "Document a studied population and reach a reliable confidence band first.";
                return false;
            }
            if (policy == WildlifeManagementPolicy.ControlledCull && ColonyTierFor(species) < 3)
            {
                reason = "A controlled cull requires the population to be understood, not merely identified.";
                return false;
            }
            return true;
        }

        public static string StageLabel(string stageId) =>
            stageId == StageSighted ? "Sighted" : stageId == StageIdentified ? "Identified" :
            stageId == StageStudied ? "Studied" : stageId == StageUnderstood ? "Understood" :
            stageId == StageDocumented ? "Documented" : "Unknown";

        public static IReadOnlyList<string> FacetIds() => SpeciesFacets;

        public static KnowledgeMenuModel MenuModelFor(Pawn pawn, bool colony) => BuildMenu(pawn, colony);

        private static void SubmitLegacy(Pawn pawn, string subjectId, string facetId, float amount, string instance, string summary)
        {
            if (pawn == null || subjectId.NullOrEmpty()) return;
            KnowledgeEngine.Submit(new KnowledgeObservation
            {
                observer = pawn,
                domainId = DomainId,
                subjectId = subjectId,
                facetId = facetId,
                observationId = "legacy-import",
                methodId = "legacy-import",
                directKnowledge = amount,
                suppressConfiguredKnowledge = true,
                quality = 1f,
                source = "Wildlife legacy migration",
                sourceInstanceId = instance,
                reasonId = "legacy-import",
                summary = summary,
                notify = false
            });
        }

        private static KnowledgeDomainRegistration BuildRegistration()
        {
            List<KnowledgeFacetDef> facets = SpeciesFacets.Select((id, index) => new KnowledgeFacetDef
            {
                defName = "Wildlife_Facet_" + index,
                stableId = id,
                label = FacetLabel(id),
                description = "Evidence about " + FacetLabel(id).ToLowerInvariant() + ".",
                completenessAmount = id == FacetIdentity ? 70f : 100f,
                personallyKnowable = true,
                documentable = true,
                shareable = true,
                approximateWhenUncertain = true
            }).ToList();
            List<KnowledgeStageDef> stages = new List<KnowledgeStageDef>
            {
                Stage(StageUnknown, "Unknown", 0, 0f, 0f, false),
                Stage(StageSighted, "Sighted", 1, 1f, 0.15f, false),
                Stage(StageIdentified, "Identified", 2, 12f, 0.35f, false),
                Stage(StageStudied, "Studied", 3, 35f, 0.50f, false),
                Stage(StageUnderstood, "Understood", 4, 72f, 0.64f, false),
                Stage(StageDocumented, "Documented", 5, 92f, 0.72f, true)
            };
            List<KnowledgeObservationDef> observations = BuildObservations();
            List<KnowledgeClaimDef> claims = BuildClaims();
            List<KnowledgeSubjectArchetypeDef> archetypes = new List<KnowledgeSubjectArchetypeDef>
            {
                Archetype(SpeciesArchetype, "Species", SpeciesFacets, claims.Select(value => value.StableId)),
                Archetype(PopulationArchetype, "Regional population", new[] { FacetPopulation, FacetHabitat, FacetMovement, FacetDanger }, claims.Select(value => value.StableId)),
                Archetype(IndividualArchetype, "Notable individual", SpeciesFacets, claims.Select(value => value.StableId)),
                Archetype(BiomeArchetype, "Biome or region", new[] { FacetHabitat, FacetPopulation, FacetMovement, FacetDanger }, claims.Select(value => value.StableId)),
                Archetype(DialectArchetype, "Signal dialect", new[] { FacetSignals, FacetSocial, FacetDanger }, claims.Select(value => value.StableId))
            };
            return new KnowledgeDomainRegistration
            {
                id = DomainId,
                label = "Wildlife",
                description = "Interpret a living ecosystem through evidence, hypotheses, decisions, and preserved stories.",
                enableUncertainty = true,
                enableFamiliarity = true,
                sharingModel = KnowledgeSharingModel.Reportable,
                sortOrder = 10,
                provenanceLimit = 24,
                evidenceAggregateLimit = 160,
                facets = facets,
                stages = stages,
                expertiseTracks = new[]
                {
                    new KnowledgeExpertiseTrackDef { defName = "Wildlife_Fieldcraft", stableId = ExpertiseTrack, label = "Fieldcraft", adept = 80f, expert = 240f, master = 520f }
                },
                observations = observations,
                claims = claims,
                archetypes = archetypes,
                expertiseNamespaces = new[]
                {
                    new KnowledgeExpertiseNamespaceDef { defName = "Wildlife_FieldcraftNamespace", stableId = ExpertiseNamespace, label = "Wildlife fieldcraft", adept = 80f, expert = 240f, master = 520f }
                },
                subjectResolver = ResolveSubject,
                subjectSource = SubjectSource,
                source = "wildlife.v3"
            };
        }

        private static List<KnowledgeObservationDef> BuildObservations()
        {
            List<KnowledgeObservationDef> values = new List<KnowledgeObservationDef>
            {
                Observation("sighting", "Sighting", 0.5f, new[] { Outcome(FacetIdentity, 6f, 2f), Outcome(FacetMovement, 2f, 1f), Outcome(FacetDanger, 1f, 0f) }),
                Observation("study", "Field study", 1f, new[] { Outcome(FacetIdentity, 8f, 3f), Outcome(FacetHabitat, 7f, 2f), Outcome(FacetDiet, 6f, 2f), Outcome(FacetMovement, 6f, 2f), Outcome(FacetSocial, 5f, 2f), Outcome(FacetAnatomy, 5f, 1f), Outcome(FacetDanger, 4f, 1f) }),
                Observation("tracks", "Tracks and signs", 0.8f, new[] { Outcome(FacetMovement, 8f, 2f), Outcome(FacetHabitat, 5f, 1f), Outcome(FacetPopulation, 4f, 1f) }),
                Observation("trail-completion", "Trail completion", 1f, new[] { Outcome(FacetMovement, 12f, 3f), Outcome(FacetPopulation, 8f, 2f), Outcome(FacetHabitat, 5f, 1f) }),
                Observation("call", "Call or signal", 0.9f, new[] { Outcome(FacetSignals, 12f, 3f), Outcome(FacetSocial, 4f, 1f), Outcome(FacetDanger, 3f, 1f) }),
                Observation("hunt", "Hunt outcome", 0.8f, new[] { Outcome(FacetHunting, 10f, 1f), Outcome(FacetDanger, 5f, 1f), Outcome(FacetAnatomy, 4f, 0f) }),
                Observation("tending", "Tending", 0.8f, new[] { Outcome(FacetHandling, 8f, 2f), Outcome(FacetAnatomy, 4f, 1f), Outcome(FacetIdentity, 2f, 1f) }),
                Observation("taming", "Taming or training", 0.9f, new[] { Outcome(FacetHandling, 10f, 2f), Outcome(FacetSocial, 5f, 1f), Outcome(FacetIdentity, 3f, 1f) }),
                Observation("survey", "Regional survey", 1f, new[] { Outcome(FacetPopulation, 12f, 2f), Outcome(FacetHabitat, 9f, 2f), Outcome(FacetMovement, 5f, 1f) }),
                Observation("expedition", "Expedition report", 1f, new[] { Outcome(FacetPopulation, 15f, 3f), Outcome(FacetHabitat, 10f, 2f), Outcome(FacetMovement, 8f, 2f), Outcome(FacetSignals, 4f, 1f) }),
                Observation("mystery-evidence", "Mystery evidence", 1f, new[] { Outcome(FacetPopulation, 8f, 2f), Outcome(FacetMovement, 7f, 2f), Outcome(FacetSignals, 6f, 2f), Outcome(FacetHabitat, 6f, 2f) }),
                Observation("storytelling", "Storytelling", 0.6f, new[] { Outcome(FacetIdentity, 4f, 2f), Outcome(FacetSocial, 3f, 2f) }),
                Observation("report", "Witness report", 0.7f, new[] { Outcome(FacetIdentity, 5f, 1f, true), Outcome(FacetMovement, 3f, 1f, true) }),
                Observation("documentation", "Documentation", 1f, new[] { Outcome(FacetIdentity, 6f, 2f, true, true), Outcome(FacetHabitat, 5f, 2f, true, true), Outcome(FacetMovement, 5f, 2f, true, true), Outcome(FacetPopulation, 5f, 2f, true, true) }),
                Observation("legacy-import", "Legacy import", 0f, new[] { Outcome(FacetIdentity, 0f, 0f) }, true)
            };
            foreach (KnowledgeObservationDef value in values)
            {
                value.retainProvenance = value.StableId != "sighting";
                value.witnessDistribution = new KnowledgeWitnessDistribution
                {
                    policy = value.StableId == "report" || value.StableId == "documentation"
                        ? KnowledgeWitnessDistributionPolicy.ColonyDirect
                        : KnowledgeWitnessDistributionPolicy.WitnessesReduced,
                    efficiency = 0.55f,
                    expertiseEfficiency = 0.45f,
                    confidenceEfficiency = 0.75f,
                    maximumRecipients = 32
                };
                value.expertiseOutcomes = new List<KnowledgeExpertiseOutcome>
                {
                    new KnowledgeExpertiseOutcome { trackId = ExpertiseTrack, expertise = value.StableId == "study" || value.StableId == "expedition" ? 3f : 1f, namespaceId = ExpertiseNamespace, namespaceWeight = 1f }
                };
                if (value.StableId == "legacy-import")
                    value.accrualPolicy = new KnowledgeAccrualPolicy { uniquePerSourceInstance = true, stateLimit = 2048 };
            }
            return values;
        }

        private static List<KnowledgeClaimDef> BuildClaims() => new List<KnowledgeClaimDef>
        {
            Claim("population-estimate", "Population estimate", FacetPopulation, KnowledgeClaimValueType.NumericRange, KnowledgeClaimAggregation.ObservedRange, KnowledgeClaimStalenessPolicy.Seasonal),
            Claim("movement-direction", "Movement direction", FacetMovement, KnowledgeClaimValueType.Direction, KnowledgeClaimAggregation.Latest, KnowledgeClaimStalenessPolicy.Seasonal),
            Claim("habitat-quality", "Habitat quality", FacetHabitat, KnowledgeClaimValueType.Percentage, KnowledgeClaimAggregation.WeightedMean, KnowledgeClaimStalenessPolicy.Seasonal),
            Claim("signal-meaning", "Signal meaning", FacetSignals, KnowledgeClaimValueType.EnumId, KnowledgeClaimAggregation.MostSupported, KnowledgeClaimStalenessPolicy.Contextual),
            Claim("danger-level", "Danger level", FacetDanger, KnowledgeClaimValueType.Percentage, KnowledgeClaimAggregation.WeightedMean, KnowledgeClaimStalenessPolicy.SlowlyStale),
            Claim("diet-preference", "Diet preference", FacetDiet, KnowledgeClaimValueType.DefReference, KnowledgeClaimAggregation.MostSupported, KnowledgeClaimStalenessPolicy.SlowlyStale),
            Claim("den-location", "Den or refuge location", FacetHabitat, KnowledgeClaimValueType.Vector, KnowledgeClaimAggregation.Latest, KnowledgeClaimStalenessPolicy.Contextual),
            Claim("social-pattern", "Social pattern", FacetSocial, KnowledgeClaimValueType.EnumId, KnowledgeClaimAggregation.MostSupported, KnowledgeClaimStalenessPolicy.SlowlyStale),
            Claim("policy-outcome", "Policy outcome", FacetPopulation, KnowledgeClaimValueType.EnumId, KnowledgeClaimAggregation.Latest, KnowledgeClaimStalenessPolicy.ConsumerManaged)
        };

        private static KnowledgeClaimDef Claim(string id, string label, string facet, KnowledgeClaimValueType type,
            KnowledgeClaimAggregation aggregation, KnowledgeClaimStalenessPolicy staleness) => new KnowledgeClaimDef
        {
            defName = "Wildlife_Claim_" + id.Replace("-", "_"),
            stableId = id,
            label = label,
            facetId = facet,
            valueType = type,
            aggregation = aggregation,
            stalenessPolicy = staleness,
            halfLifeTicks = staleness == KnowledgeClaimStalenessPolicy.Seasonal ? 900000f : 1200000f,
            provisionalConfidence = 0.5f,
            documentable = true,
            provenanceLimit = 16,
            measurementHistoryLimit = 64
        };

        private static KnowledgeObservationDef Observation(string id, string label, float baseExpertise,
            IEnumerable<KnowledgeObservationOutcome> outcomes, bool legacy = false)
        {
            return new KnowledgeObservationDef
            {
                defName = "Wildlife_Observation_" + id.Replace("-", "_"),
                stableId = id,
                label = label,
                baseKnowledge = 0f,
                baseExpertise = 0f,
                baseFamiliarity = 0f,
                facetIds = outcomes.Select(value => value.facetId).Distinct().ToList(),
                successOutcomes = outcomes.ToList(),
                failureOutcomes = legacy ? outcomes.ToList() : outcomes.Select(value => new KnowledgeObservationOutcome
                {
                    facetId = value.facetId,
                    knowledge = value.knowledge * 0.42f,
                    familiarity = value.familiarity * 0.5f,
                    evidenceWeight = value.evidenceWeight,
                    confidenceFactor = value.confidenceFactor,
                    disposition = value.facetId == FacetDanger ? KnowledgeEvidenceDisposition.Supporting : KnowledgeEvidenceDisposition.Contradictory
                }).ToList()
            };
        }

        private static KnowledgeObservationOutcome Outcome(string facet, float knowledge, float familiarity,
            bool targetColony = false, bool document = false) => new KnowledgeObservationOutcome
            {
                facetId = facet,
                knowledge = knowledge,
                familiarity = familiarity,
                evidenceWeight = 1f,
                confidenceFactor = 1f,
                targetColony = targetColony,
                document = document
            };

        private static KnowledgeStageDef Stage(string id, string label, int order, float minimumKnowledge,
            float minimumConfidence, bool documented) => new KnowledgeStageDef
        {
            defName = id,
            label = label,
            order = order,
            minimumKnowledge = minimumKnowledge,
            minimumConfidence = minimumConfidence,
            documented = documented,
            allowRegression = false
        };

        private static KnowledgeSubjectArchetypeDef Archetype(string id, string label, IEnumerable<string> facets,
            IEnumerable<string> claims) => new KnowledgeSubjectArchetypeDef
        {
            defName = "Wildlife_Archetype_" + id.Replace(".", "_"),
            stableId = id,
            categoryId = id,
            applicableFacetIds = facets.ToList(),
            applicableClaimIds = claims.ToList(),
            discoveryStageIds = new[] { StageUnknown, StageSighted, StageIdentified, StageStudied, StageUnderstood, StageDocumented }.ToList(),
            observationIds = new[] { "sighting", "study", "tracks", "trail-completion", "survey", "expedition", "documentation" }.ToList(),
            comparisonSchemaId = "wildlife.species-comparison"
        };

        private static void RegisterContexts()
        {
            KnowledgeContextRegistry.RegisterType(new KnowledgeContextTypeDef { defName = "Wildlife_MapSector", stableId = ContextSector }, true);
            KnowledgeContextRegistry.RegisterType(new KnowledgeContextTypeDef { defName = "Wildlife_Map", stableId = ContextMap }, true);
            KnowledgeContextRegistry.RegisterType(new KnowledgeContextTypeDef { defName = "Wildlife_WorldRegion", stableId = ContextWorldRegion }, true);
            KnowledgeContextRegistry.RegisterType(new KnowledgeContextTypeDef { defName = "Wildlife_Biome", stableId = ContextBiome }, true);
            KnowledgeContextRegistry.RegisterType(new KnowledgeContextTypeDef { defName = "Wildlife_Global", stableId = ContextGlobal }, true);
            KnowledgeContextRegistry.RegisterResolver(ContextSector, new WildlifeContextResolver(), true);
            KnowledgeContextRegistry.RegisterResolver(ContextMap, new WildlifeContextResolver(), true);
            KnowledgeContextRegistry.RegisterResolver(ContextWorldRegion, new WildlifeContextResolver(), true);
            KnowledgeContextRegistry.RegisterResolver(ContextBiome, new WildlifeContextResolver(), true);
            KnowledgeContextRegistry.RegisterResolver(ContextGlobal, new WildlifeContextResolver(), true);
        }

        private static void RegisterRelationsAndComparisons()
        {
            KnowledgeRelationService.RegisterType(new KnowledgeSubjectRelationTypeDef
            {
                defName = "Wildlife_PopulationOf",
                stableId = "wildlife.population-of",
                symmetric = false,
                parentage = false,
                metadataLimit = 8
            }, true);
            KnowledgeRelationService.RegisterType(new KnowledgeSubjectRelationTypeDef
            {
                defName = "Wildlife_DialectOf",
                stableId = "wildlife.dialect-of",
                symmetric = false,
                parentage = false,
                metadataLimit = 8
            }, true);
            KnowledgeComparisonService.RegisterSchema(new KnowledgeComparisonSchema
            {
                id = "wildlife.species-comparison",
                label = "Species comparison",
                claimIds = BuildClaims().Select(value => value.StableId).ToList(),
                facetIds = SpeciesFacets.ToList(),
                relationTypeIds = new List<string> { "wildlife.population-of", "wildlife.dialect-of" }
            }, true);
        }

        private static IEnumerable<KnowledgeSubjectRegistration> SubjectSource()
        {
            List<KnowledgeSubjectRegistration> values = new List<KnowledgeSubjectRegistration>();
            foreach (ThingDef species in DefDatabase<ThingDef>.AllDefsListForReading.Where(value => value?.race?.Animal == true))
                values.Add(Subject(SpeciesSubjectId(species), species.LabelCap, species.label, species, SpeciesArchetype, species));
            foreach (BiomeDef biome in DefDatabase<BiomeDef>.AllDefsListForReading)
                values.Add(Subject(BiomeSubjectId(biome), biome.LabelCap, biome.description, biome, BiomeArchetype, biome));
            foreach (Map map in Find.Maps ?? Enumerable.Empty<Map>())
            {
                if (map?.Biome != null) values.Add(Subject(RegionSubjectId(map), "Region " + map.Biome.LabelCap, "The local wildlife region.", map.Biome, BiomeArchetype, map.Biome));
                RegionalWildlifeMapComponent regional = map?.GetComponent<RegionalWildlifeMapComponent>();
                foreach (RegionalSpeciesRecord record in regional?.Records ?? Array.Empty<RegionalSpeciesRecord>())
                    if (record?.species != null)
                        values.Add(Subject(PopulationSubjectId(map, record.species), record.species.LabelCap + " regional population",
                            "A population estimate beyond the colony map.", record.species, PopulationArchetype, record.species));
                foreach (NotableAnimalRecord record in map?.GetComponent<NotableWildlifeMapComponent>()?.Records ?? Array.Empty<NotableAnimalRecord>())
                    if (record?.animal != null && record.species != null)
                        values.Add(Subject(IndividualSubjectId(record.animal), record.title ?? record.animal.LabelCap,
                            record.distinction, record.species, IndividualArchetype, record.species));
                foreach (ThingDef species in DefDatabase<ThingDef>.AllDefsListForReading.Where(value => value?.race?.Animal == true))
                    values.Add(Subject(DialectSubjectId(map, species), species.LabelCap + " local dialect",
                        "A regional signal vocabulary, which may differ from the species norm.", species, DialectArchetype, species));
            }
            return values.GroupBy(value => value.id).Select(group => group.First());
        }

        private static KnowledgeSubjectRegistration ResolveSubject(string id)
        {
            if (id.NullOrEmpty()) return null;
            if (id.StartsWith("species:", StringComparison.Ordinal))
            {
                ThingDef species = DefDatabase<ThingDef>.GetNamedSilentFail(id.Substring("species:".Length));
                return species?.race?.Animal == true ? Subject(id, species.LabelCap, species.description, species, SpeciesArchetype, species) : null;
            }
            if (id.StartsWith("biome:", StringComparison.Ordinal))
            {
                BiomeDef biome = DefDatabase<BiomeDef>.GetNamedSilentFail(id.Substring("biome:".Length));
                return biome == null ? null : Subject(id, biome.LabelCap, biome.description, biome, BiomeArchetype, biome);
            }
            if (id.StartsWith("region:", StringComparison.Ordinal) && int.TryParse(id.Substring("region:".Length), out int regionMapId))
            {
                Map map = Find.Maps?.FirstOrDefault(value => value?.uniqueID == regionMapId);
                return map?.Biome == null ? null : Subject(id, "Region " + map.Biome.LabelCap, "The local wildlife region.", map.Biome, BiomeArchetype, map.Biome);
            }
            if (id.StartsWith("population:", StringComparison.Ordinal))
            {
                string[] parts = id.Split(':');
                if (parts.Length == 3 && int.TryParse(parts[1], out int populationMapId))
                {
                    Map map = Find.Maps?.FirstOrDefault(value => value?.uniqueID == populationMapId);
                    ThingDef species = DefDatabase<ThingDef>.GetNamedSilentFail(parts[2]);
                    if (map != null && species?.race?.Animal == true)
                        return Subject(id, species.LabelCap + " regional population", "A population estimate beyond the colony map.", species, PopulationArchetype, species);
                }
            }
            if (id.StartsWith("dialect:", StringComparison.Ordinal))
            {
                string[] parts = id.Split(':');
                if (parts.Length == 3 && int.TryParse(parts[1], out int dialectMapId))
                {
                    Map map = Find.Maps?.FirstOrDefault(value => value?.uniqueID == dialectMapId);
                    ThingDef species = DefDatabase<ThingDef>.GetNamedSilentFail(parts[2]);
                    if (map != null && species?.race?.Animal == true)
                        return Subject(id, species.LabelCap + " local dialect", "A regional signal vocabulary.", species, DialectArchetype, species);
                }
            }
            if (id.StartsWith("individual:", StringComparison.Ordinal) && int.TryParse(id.Substring("individual:".Length), out int animalId))
            {
                Pawn animal = Find.Maps?.SelectMany(value => value.mapPawns.AllPawns).FirstOrDefault(value => value?.thingIDNumber == animalId);
                NotableAnimalRecord notable = Find.Maps?.SelectMany(value => value.GetComponent<NotableWildlifeMapComponent>()?.Records ?? Array.Empty<NotableAnimalRecord>())
                    .FirstOrDefault(value => value?.animal?.thingIDNumber == animalId);
                ThingDef species = animal?.def ?? notable?.species;
                if (species?.race?.Animal == true)
                    return Subject(id, notable?.title ?? animal?.LabelCap ?? species.LabelCap, notable?.distinction ?? "A remembered individual animal.", species, IndividualArchetype, species);
            }
            return null;
        }

        private static KnowledgeSubjectRegistration Subject(string id, string label, string description, Def sourceDef,
            string archetype, Def iconSource)
        {
            return new KnowledgeSubjectRegistration
            {
                id = id,
                label = label,
                description = description ?? string.Empty,
                unidentifiedLabel = "Unidentified wildlife",
                unidentifiedDescription = "The evidence is not yet sufficient to identify this subject.",
                sourceDef = sourceDef,
                archetypeId = archetype,
                applicableFacetIds = null,
                applicableClaimIds = null,
                sortOrder = archetype == SpeciesArchetype ? 0 : archetype == PopulationArchetype ? 10 : 20,
                source = "wildlife.v3"
            };
        }

        private static string RecipeId(WildlifeKnowledgeObservation observation) =>
            observation == WildlifeKnowledgeObservation.TrailCompletion ? "trail-completion" :
            observation == WildlifeKnowledgeObservation.MysteryEvidence ? "mystery-evidence" :
            observation == WildlifeKnowledgeObservation.Storytelling ? "storytelling" :
            observation == WildlifeKnowledgeObservation.Documentation ? "documentation" :
            observation == WildlifeKnowledgeObservation.Report ? "report" : observation.ToString().ToLowerInvariant();

        private static WildlifeEventKind EventKindFor(WildlifeKnowledgeObservation observation) =>
            observation == WildlifeKnowledgeObservation.Sighting ? WildlifeEventKind.Sighting :
            observation == WildlifeKnowledgeObservation.Study ? WildlifeEventKind.Study :
            observation == WildlifeKnowledgeObservation.Tracks ? WildlifeEventKind.Tracks :
            observation == WildlifeKnowledgeObservation.TrailCompletion ? WildlifeEventKind.TrailCompletion :
            observation == WildlifeKnowledgeObservation.Call ? WildlifeEventKind.Signal :
            observation == WildlifeKnowledgeObservation.Hunt ? WildlifeEventKind.Hunt :
            observation == WildlifeKnowledgeObservation.Tending ? WildlifeEventKind.Tending :
            observation == WildlifeKnowledgeObservation.Taming ? WildlifeEventKind.Taming :
            observation == WildlifeKnowledgeObservation.Survey ? WildlifeEventKind.Survey :
            observation == WildlifeKnowledgeObservation.Expedition ? WildlifeEventKind.Expedition :
            observation == WildlifeKnowledgeObservation.MysteryEvidence ? WildlifeEventKind.MysteryEvidence :
            observation == WildlifeKnowledgeObservation.Report ? WildlifeEventKind.Report : WildlifeEventKind.Documentation;

        private static string InstanceId(Pawn pawn, object subject, string method) =>
            "wildlife:" + (pawn?.thingIDNumber ?? 0) + ":" + (subject is ThingDef def ? def.defName : subject is BiomeDef biome ? biome.defName : "subject") + ":" + method + ":" + (Find.TickManager?.TicksGame ?? 0);

        public static string PassiveInstanceId(Pawn pawn, ThingDef species, int day) =>
            "wildlife:passive:" + (pawn?.thingIDNumber ?? 0) + ":" + (species?.defName ?? "species") + ":day:" + day;

        private static KnowledgeChange BestChange(IReadOnlyList<KnowledgeChange> changes)
        {
            KnowledgeChange best = null;
            float bestScore = 0f;
            if (changes == null) return null;
            for (int i = 0; i < changes.Count; i++)
            {
                KnowledgeChange change = changes[i];
                if (change == null) continue;
                float score = Mathf.Abs(change.newAmount - change.oldAmount) + Mathf.Abs(change.newConfidence - change.oldConfidence) * 2f;
                if (StageOrder(change.newStageId) > StageOrder(change.oldStageId)) score += 5f;
                if (best == null || score > bestScore) { best = change; bestScore = score; }
            }
            return best;
        }

        private static float LegacyColonyExperience(ThingDef species) =>
            Find.Maps?.Sum(map => map.GetComponent<HuntingKnowledgeMapComponent>()?.LegacyColonyExperienceFor(species) ?? 0f) ?? 0f;

        private static string FacetLabel(string id) =>
            id == FacetSocial ? "Social behavior" : id == FacetPopulation ? "Population ecology" : id.CapitalizeFirst();

        private sealed class WildlifeContextResolver : IKnowledgeContextResolver
        {
            public KnowledgeContextKey Parent(KnowledgeContextKey context)
            {
                if (context.IsEmpty) return KnowledgeContextKey.Empty;
                if (context.typeId == ContextSector)
                {
                    string mapId = context.stableId.Split(':').FirstOrDefault();
                    return new KnowledgeContextKey(ContextMap, mapId);
                }
                if (context.typeId == ContextMap && int.TryParse(context.stableId, out int mapIdValue))
                {
                    Map map = Find.Maps?.FirstOrDefault(value => value?.uniqueID == mapIdValue);
                    return map == null ? new KnowledgeContextKey(ContextGlobal, "global") :
                        new KnowledgeContextKey(ContextWorldRegion, map.Tile.ToString());
                }
                if (context.typeId == ContextWorldRegion && int.TryParse(context.stableId, out int tile))
                {
                    BiomeDef biome = Find.WorldGrid?[(PlanetTile)tile]?.PrimaryBiome;
                    return biome == null ? new KnowledgeContextKey(ContextGlobal, "global") :
                        new KnowledgeContextKey(ContextBiome, biome.defName);
                }
                if (context.typeId == ContextBiome) return new KnowledgeContextKey(ContextGlobal, "global");
                return KnowledgeContextKey.Empty;
            }
        }

        private sealed class WildlifeKnowledgeUiProvider : IKnowledgeUiProvider
        {
            public string DomainId => WildlifeKnowledgeAdapter.DomainId;
            public KnowledgeEntry BioEntry(Pawn pawn)
            {
                if (pawn == null) return null;
                int known = KnowledgeQuery.PersonalFacets(DomainId, pawn).Count(value => value.amount > 0f);
                KnowledgeExpertiseSnapshotV2 expertise = KnowledgeQuery.Expertise(DomainId, pawn, ExpertiseTrack);
                if (known == 0 && expertise.amount <= 0f) return null;
                return new KnowledgeEntry
                {
                    label = "Wildlife",
                    rank = expertise.rank,
                    progress = expertise.progress,
                    summary = known + " field records",
                    tooltip = "Wildlife knowledge grows through sightings, evidence, reports, teaching, expeditions, and documentation.",
                    openDetails = () => Find.WindowStack.Add(new Window_WildlifeJournal(pawn?.MapHeld, WildlifeJournalPage.FieldGuide))
                };
            }

            public KnowledgeMenuModel Menu(Pawn pawn, bool colony) => BuildMenu(pawn, colony);
        }

        private sealed class WildlifeV3UiProvider : IKnowledgeDomainUiV3
        {
            public string DomainId => WildlifeKnowledgeAdapter.DomainId;

            public IEnumerable<string> ListBadges(KnowledgeBrowserRow row, Pawn pawn, KnowledgeScope scope)
            {
                if (row == null) return Array.Empty<string>();
                List<string> values = new List<string>();
                if (!row.lastStage.NullOrEmpty()) values.Add(StageLabel(row.lastStage));
                if (row.confidence >= 0.7f) values.Add("verified");
                else if (row.confidence > 0.05f) values.Add("uncertain");
                if (row.usedContextFallback) values.Add("fallback context");
                else if (!row.resolvedContext.IsEmpty) values.Add("exact context");
                if (row.relations.Count > 0) values.Add("related");
                return values;
            }

            public IEnumerable<string> ListColumns(KnowledgeBrowserRow row, Pawn pawn, KnowledgeScope scope) =>
                new[] { StageLabel(row?.lastStage), "Confidence " + (row?.confidence ?? 0f).ToStringPercent(),
                    "Evidence " + (row?.state?.familiarity ?? 0f).ToString("0"),
                    row == null || row.recencyTick <= 0 ? "No recent evidence" :
                        "Last evidence " + (Find.TickManager.TicksGame - row.recencyTick).ToStringTicksToPeriod() + " ago" };

            public void DrawDetailPanels(Rect rect, KnowledgeBrowserRow row, Pawn pawn, KnowledgeScope scope)
            {
                if (row == null || rect.width <= 20f) return;
                Rect panel = new Rect(rect.x, rect.y, rect.width, Mathf.Min(80f, rect.height));
                Widgets.DrawMenuSection(panel);
                string context = row.usedContextFallback
                    ? "Fallback from " + row.resolvedContext
                    : row.resolvedContext.IsEmpty ? "No contextual evidence" : "Exact " + row.resolvedContext;
                Widgets.Label(panel.ContractedBy(8f), "Wildlife interpretation\n" + StageLabel(row.lastStage) + " - " +
                    row.confidence.ToStringPercent() + " confidence. " + context + ".");
            }
        }

        private static KnowledgeMenuModel BuildMenu(Pawn pawn, bool colony)
        {
            Register();
            List<KnowledgeMenuSection> sections = new List<KnowledgeMenuSection>();
            KnowledgeMenuSection species = new KnowledgeMenuSection
            {
                id = "species",
                label = "Field Guide",
                emptyText = "No wildlife evidence yet. Notice an animal, gather signs, or hear a call."
            };
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading.Where(value => value?.race?.Animal == true))
            {
                float amount = colony ? ColonyKnowledge(def) : PersonalKnowledge(pawn, def);
                KnowledgeFacetSnapshotV2 facet = colony ? Facet(null, def, FacetIdentity, null, true) : Facet(pawn, def);
                string stage = colony ? ColonyStage(def) : PersonalStage(pawn, def);
                if (amount <= 0f && stage == StageUnknown) continue;
                species.rows.Add(new KnowledgeMenuRow
                {
                    label = def.LabelCap,
                    iconDef = def,
                    rank = (KnowledgeRank)ColonyTierFor(def),
                    progress = Mathf.Clamp01(facet.completeness),
                    confidence = facet.confidence,
                    stageId = stage,
                    status = StageLabel(stage) + " - " + facet.confidence.ToStringPercent() + " confidence",
                    tooltip = def.LabelCap + "\n" + StageLabel(stage) + "\nEvidence: " + facet.evidenceCount + "\nThe Field Guide shows facts, uncertainty, and the next useful observation.",
                    select = () => Find.WindowStack.Add(new Window_WildlifeJournal(pawn?.MapHeld ?? Find.CurrentMap, WildlifeJournalPage.FieldGuide, def))
                });
            }
            sections.Add(species);
            KnowledgeMenuSection biomes = new KnowledgeMenuSection
            {
                id = "biomes",
                label = "Habitats",
                emptyText = "No habitat evidence yet. Survey a biome or complete an expedition."
            };
            foreach (BiomeDef biome in DefDatabase<BiomeDef>.AllDefsListForReading)
            {
                string id = BiomeSubjectId(biome);
                KnowledgeFacetSnapshotV2 facet = KnowledgeQuery.Facet(DomainId, id, FacetHabitat, pawn,
                    colony ? KnowledgeScope.Colony : KnowledgeScope.Personal, true, true,
                    BiomeContext(biome), KnowledgeContextFallbackMode.ParentThenGlobal);
                if (facet.amount <= 0f) continue;
                biomes.rows.Add(new KnowledgeMenuRow
                {
                    label = biome.LabelCap,
                    rank = KnowledgeRank.Novice,
                    progress = facet.completeness,
                    confidence = facet.confidence,
                    status = "Habitat evidence - " + facet.confidence.ToStringPercent(),
                    tooltip = "Habitat evidence is contextual and may fall back from a local sector to the biome or global record."
                });
            }
            sections.Add(biomes);
            KnowledgeExpertiseSnapshotV2 expertise = KnowledgeQuery.Expertise(DomainId, pawn, ExpertiseTrack);
            return new KnowledgeMenuModel
            {
                title = colony ? "Colony Wildlife Journal" : (pawn?.LabelShortCap ?? "Colonist") + " - Wildlife",
                expertiseLabel = "Fieldcraft",
                expertiseRank = expertise.rank,
                expertiseProgress = expertise.progress,
                sections = sections
            };
        }
    }

    public static class WildlifeKnowledgeMigrationHooks
    {
        [HarmonyPatch(typeof(Game), nameof(Game.FinalizeInit))]
        private static class GameFinalizePatch
        {
            public static void Postfix()
            {
                try { WildlifeKnowledgeAdapter.TryMigrateLegacy(); }
                catch (Exception exception) { Log.ErrorOnce("Wildlife legacy knowledge migration failed: " + exception.Message, Gen.HashCombineInt(0x51A7, 3)); }
            }
        }
    }
}
