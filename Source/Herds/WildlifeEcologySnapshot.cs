using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using KnowledgeFramework;
using RimWorld;
using UnityEngine;
using Verse;

namespace Herds
{
    public sealed class WildlifeEvidenceSnapshot
    {
        public readonly WildlifeEventKind kind;
        public readonly int tick;
        public readonly string source;
        public readonly string summary;
        public readonly float quality;
        public readonly float confidence;
        public readonly bool success;
        public readonly IntVec3 cell;
        public readonly Pawn observer;
        public readonly string observerName;
        public readonly string facetId;
        public readonly string discoveryKind;
        public readonly string contextLabel;
        public readonly string previousStage;
        public readonly string newStage;
        public readonly float previousAmount;
        public readonly float newAmount;
        public readonly float previousConfidence;
        public readonly float newConfidence;
        public readonly float amountDelta;
        public readonly float confidenceDelta;
        public readonly int observerCount;
        public readonly float observationHours;
        public readonly float elapsedHours;
        internal readonly string signalTraceId;

        internal WildlifeEvidenceSnapshot(WildlifeEvent value, int observerCount = 1, float observationHours = 0f, float elapsedHours = 0f, float amountDelta = -1f)
        {
            kind = value.kind;
            tick = value.tick;
            source = value.source;
            summary = value.summary;
            quality = value.quality;
            confidence = value.confidence;
            success = value.success;
            cell = value.cell;
            observer = value.observer;
            this.observerCount = Mathf.Max(1, observerCount);
            this.observationHours = Mathf.Max(0f, observationHours);
            this.elapsedHours = Mathf.Max(0f, elapsedHours);
            this.amountDelta = amountDelta >= 0f ? amountDelta : MetadataFloat(value, "amountDelta", value.amount);
            confidenceDelta = MetadataFloat(value, "confidenceDelta", 0f);
            signalTraceId = Metadata(value, "signalTraceId", string.Empty);
            observerName = Metadata(value, "observerName", value.observer?.LabelShortCap.ToString());
            facetId = Metadata(value, "facetId", string.Empty);
            discoveryKind = Metadata(value, "discoveryKind", string.Empty);
            contextLabel = Metadata(value, "contextLabel", string.Empty);
            previousStage = Metadata(value, "previousStage", string.Empty);
            newStage = Metadata(value, "newStage", string.Empty);
            previousAmount = MetadataFloat(value, "previousAmount", 0f);
            newAmount = MetadataFloat(value, "newAmount", previousAmount + this.amountDelta);
            previousConfidence = MetadataFloat(value, "previousConfidence", 0f);
            newConfidence = MetadataFloat(value, "newConfidence", previousConfidence + confidenceDelta);
        }

        private static string Metadata(WildlifeEvent value, string key, string fallback)
        {
            return value?.metadata != null && value.metadata.TryGetValue(key, out string result) && !result.NullOrEmpty() ? result : fallback;
        }

        internal static float MetadataFloat(WildlifeEvent value, string key, float fallback)
        {
            return float.TryParse(Metadata(value, key, string.Empty), out float result) ? result : fallback;
        }
    }

    public sealed class WildlifeTrailSnapshot
    {
        public readonly ThingDef species;
        public readonly IntVec3 predictedCell;
        public readonly IntVec3 departureCell;
        public readonly float confidence;
        public readonly float uncertaintyRadius;
        public readonly WildlifeTrailState state;
        public readonly WildlifeSignKind dominantKind;
        public readonly int groupSize;
        public readonly int expiresTick;
        public readonly bool viable;

        internal WildlifeTrailSnapshot(WildlifeTrailLead value)
        {
            species = value.species;
            predictedCell = value.predictedCell;
            departureCell = value.departureCell;
            confidence = value.confidence;
            uncertaintyRadius = value.UncertaintyRadius;
            state = value.state;
            dominantKind = value.dominantKind;
            groupSize = value.groupSize;
            expiresTick = value.expiresTick;
            viable = value.viableLead;
        }
    }

    public sealed class WildlifeMigrationSnapshot
    {
        public readonly Pawn animal;
        public readonly ThingDef species;
        public readonly RoamingAnimalState state;
        public readonly string direction;
        public readonly int expectedReturnTick;
        public readonly bool notable;
        public readonly bool tagged;

        internal WildlifeMigrationSnapshot(RoamingAnimalRecord value)
        {
            animal = value.animal;
            species = value.species;
            state = value.state;
            direction = value.direction;
            expectedReturnTick = value.expectedReturnTick;
            notable = value.notable;
            tagged = value.tagged;
        }
    }

    public sealed class WildlifeSignalSnapshot
    {
        public readonly ThingDef species;
        public readonly WildlifeSignalKind kind;
        public readonly IntVec3 cell;
        public readonly int tick;
        public readonly bool truthful;
        public readonly bool verified;
        public readonly bool behaviorConsistent;
        public readonly bool predatorPressureEligible;
        public readonly float radius;
        public readonly string historicalDescription;
        public readonly int historicalTier;

        internal WildlifeSignalSnapshot(WildlifeSignalTrace value, Map map)
        {
            species = value.species;
            kind = value.kind;
            cell = value.cell;
            tick = value.tick;
            truthful = value.truthful;
            verified = value.verified;
            behaviorConsistent = value.behaviorConsistent;
            predatorPressureEligible = WildlifeKnowledgeAdapter.IsPredatorPressureTrace(value);
            radius = value.radius;
            if (WildlifeSignalCultureMapComponent.IsWarningCall(value.kind))
            {
                WildlifeSignalCultureMapComponent signalCulture = map?.GetComponent<WildlifeSignalCultureMapComponent>();
                WildlifeWarningKnowledgeState warning = signalCulture?.ColonyWarningKnowledge(value.species);
                historicalDescription = warning?.PlayerDescription ?? "A warning call was recorded.";
                if (WildlifeKnowledgeAdapter.IsPredatorPressureTrace(value))
                {
                    WildlifePredatorPressureKnowledgeState pressure = signalCulture?.ColonyPredatorPressure(value.species);
                    if (pressure?.hasEvidence == true)
                        historicalDescription += " " + pressure.PlayerDescription;
                }
                historicalTier = warning == null || !warning.hasEvidence ? (int)WildlifeSignalDisplayTier.Unknown :
                    warning.claimSupported ? (int)WildlifeSignalDisplayTier.Reliability :
                    warning.meaningInterpreted ? (int)WildlifeSignalDisplayTier.Exact :
                    (int)WildlifeSignalDisplayTier.Family;
            }
            else
            {
                historicalDescription = value.playerFacingDescription;
                historicalTier = value.playerFacingTier;
            }
        }
    }

    public sealed class WildlifeSpeciesSnapshot
    {
        public readonly ThingDef species;
        public readonly string subjectId;
        public readonly string stageId;
        public readonly float knowledge;
        public readonly float confidence;
        public readonly int localCount;
        public readonly float nearbyPopulation;
        public readonly float regionalPopulation;
        public readonly float habitatQuality;
        public readonly float pressure;
        public readonly string forecast;
        public readonly string policy;
        public readonly IReadOnlyList<WildlifeEvidenceSnapshot> evidence;
        public readonly IReadOnlyList<WildlifeTrailSnapshot> trails;
        public readonly IReadOnlyList<WildlifeMigrationSnapshot> migrations;
        public readonly IReadOnlyList<WildlifeSignalSnapshot> signals;
        public readonly IReadOnlyList<string> variations;

        internal WildlifeSpeciesSnapshot(ThingDef species, string stageId, float knowledge, float confidence,
            int localCount, float nearbyPopulation, float regionalPopulation, float habitatQuality,
            float pressure, string forecast, string policy, IEnumerable<WildlifeEvidenceSnapshot> evidence,
            IEnumerable<WildlifeTrailSnapshot> trails, IEnumerable<WildlifeMigrationSnapshot> migrations,
            IEnumerable<WildlifeSignalSnapshot> signals, IEnumerable<string> variations)
        {
            this.species = species;
            subjectId = WildlifeKnowledgeAdapter.SpeciesSubjectId(species);
            this.stageId = stageId;
            this.knowledge = knowledge;
            this.confidence = confidence;
            this.localCount = localCount;
            this.nearbyPopulation = nearbyPopulation;
            this.regionalPopulation = regionalPopulation;
            this.habitatQuality = habitatQuality;
            this.pressure = pressure;
            this.forecast = forecast;
            this.policy = policy;
            this.evidence = ReadOnly(evidence);
            this.trails = ReadOnly(trails);
            this.migrations = ReadOnly(migrations);
            this.signals = ReadOnly(signals);
            this.variations = new ReadOnlyCollection<string>((variations ?? Enumerable.Empty<string>()).Where(value => !value.NullOrEmpty()).Distinct().ToList());
        }

        private static IReadOnlyList<T> ReadOnly<T>(IEnumerable<T> values) =>
            new ReadOnlyCollection<T>((values ?? Enumerable.Empty<T>()).Where(value => value != null).ToList());
    }

    public sealed class WildlifeEcologySnapshot
    {
        public readonly Map map;
        public readonly int tick;
        public readonly float habitatQuality;
        public readonly IReadOnlyList<WildlifeSpeciesSnapshot> species;
        public readonly IReadOnlyList<WildlifeTrailSnapshot> trails;
        public readonly IReadOnlyList<WildlifeMigrationSnapshot> migrations;
        public readonly IReadOnlyList<WildlifeSignalSnapshot> signals;
        public readonly int activeMysteries;
        public readonly int activeExpeditions;

        internal WildlifeEcologySnapshot(Map map, int tick, float habitatQuality,
            IEnumerable<WildlifeSpeciesSnapshot> species, IEnumerable<WildlifeTrailSnapshot> trails,
            IEnumerable<WildlifeMigrationSnapshot> migrations, IEnumerable<WildlifeSignalSnapshot> signals,
            int activeMysteries, int activeExpeditions)
        {
            this.map = map;
            this.tick = tick;
            this.habitatQuality = habitatQuality;
            this.species = new ReadOnlyCollection<WildlifeSpeciesSnapshot>((species ?? Enumerable.Empty<WildlifeSpeciesSnapshot>()).Where(value => value != null).ToList());
            this.trails = new ReadOnlyCollection<WildlifeTrailSnapshot>((trails ?? Enumerable.Empty<WildlifeTrailSnapshot>()).Where(value => value != null).ToList());
            this.migrations = new ReadOnlyCollection<WildlifeMigrationSnapshot>((migrations ?? Enumerable.Empty<WildlifeMigrationSnapshot>()).Where(value => value != null).ToList());
            this.signals = new ReadOnlyCollection<WildlifeSignalSnapshot>((signals ?? Enumerable.Empty<WildlifeSignalSnapshot>()).Where(value => value != null).ToList());
            this.activeMysteries = activeMysteries;
            this.activeExpeditions = activeExpeditions;
        }

        public WildlifeSpeciesSnapshot For(ThingDef value) => species.FirstOrDefault(item => item.species == value);
    }

    /// <summary>Event-invalidated, read-only view of existing ecological owners.</summary>
    public sealed class WildlifeEcologySnapshotMapComponent : MapComponent
    {
        private WildlifeEcologySnapshot snapshot;
        private bool dirty = true;
        private int knowledgeRevision = -1;
        private IDisposable subscription;

        public WildlifeEcologySnapshotMapComponent(Map map) : base(map) { }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            subscription?.Dispose();
            subscription = WildlifeEventRouter.Shared.Subscribe(OnWildlifeEvent);
            dirty = true;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                snapshot = null;
                dirty = true;
                knowledgeRevision = -1;
            }
        }

        public WildlifeEcologySnapshot Current
        {
            get
            {
                if (snapshot == null || dirty || knowledgeRevision != KnowledgeQuery.Revision) Rebuild();
                return snapshot;
            }
        }

        public void Invalidate()
        {
            dirty = true;
        }

        public List<string> DebugOverviewLines()
        {
            WildlifeEcologySnapshot value = Current;
            List<string> lines = new List<string>
            {
                "atlas=tick:" + value.tick + " species:" + value.species.Count +
                " trails:" + value.trails.Count + " migrations:" + value.migrations.Count +
                " signals:" + value.signals.Count + " habitat:" + value.habitatQuality.ToString("0.00") +
                " season:" + GenLocalDate.Season(map) + " route:journal-signals=" +
                (HerdsMod.Settings?.enableWildlifeSignalCulture == true)
            };
            foreach (WildlifeSpeciesSnapshot speciesValue in value.species
                .OrderByDescending(item => item.evidence.Count + item.trails.Count + item.migrations.Count + item.signals.Count)
                .Take(8))
            {
                int latestTick = 0;
                for (int i = 0; i < speciesValue.evidence.Count; i++)
                    latestTick = Mathf.Max(latestTick, speciesValue.evidence[i].tick);
                for (int i = 0; i < speciesValue.signals.Count; i++)
                    latestTick = Mathf.Max(latestTick, speciesValue.signals[i].tick);
                string action = speciesValue.trails.Count > 0 ? "inspect-trail" :
                    speciesValue.signals.Count > 0 && HerdsMod.Settings?.enableWildlifeSignalCulture == true ? "open-signals" :
                    speciesValue.localCount > 0 ? "focus-area" : "survey";
                IntVec3 target = speciesValue.trails.Count > 0 && speciesValue.trails[0].predictedCell.IsValid
                    ? speciesValue.trails[0].predictedCell :
                    speciesValue.signals.Count > 0 ? speciesValue.signals[0].cell : map.Center;
                string freshness = latestTick <= 0 ? "stale" :
                    value.tick - latestTick <= 5000 ? "fresh" :
                    value.tick - latestTick <= 15000 ? "aging" : "stale";
                lines.Add("atlas.species=" + speciesValue.species.defName +
                    " local:" + speciesValue.localCount + " regional:" + speciesValue.regionalPopulation.ToString("0.0") +
                    " confidence:" + speciesValue.confidence.ToString("0.00") +
                    " evidence:" + speciesValue.evidence.Count + " trails:" + speciesValue.trails.Count +
                    " migrations:" + speciesValue.migrations.Count + " signals:" + speciesValue.signals.Count +
                    " freshness:" + freshness + " action:" + action + " target:" + target);
            }
            return lines;
        }

        private void OnWildlifeEvent(WildlifeEvent value)
        {
            if (value?.map == map) dirty = true;
        }

        private void Rebuild()
        {
            int now = Find.TickManager?.TicksGame ?? 0;
            knowledgeRevision = KnowledgeQuery.Revision;
            RegionalWildlifeMapComponent regional = map.GetComponent<RegionalWildlifeMapComponent>();
            WildlifeTrailMapComponent trailMap = map.GetComponent<WildlifeTrailMapComponent>();
            WildlifeSignalCultureMapComponent signalMap = map.GetComponent<WildlifeSignalCultureMapComponent>();
            IReadOnlyList<WildlifeEvent> events = WildlifeEventRouter.Shared.History;
            List<ThingDef> defs = new List<ThingDef>();
            defs.AddRange(map.mapPawns.AllPawnsSpawned.Where(pawn => pawn?.RaceProps?.Animal == true).Select(pawn => pawn.def));
            defs.AddRange(regional?.Records?.Where(record => record?.species != null).Select(record => record.species) ?? Enumerable.Empty<ThingDef>());
            defs.AddRange(events.Where(value => value?.map == map && value.species != null).Select(value => value.species));
            defs = defs.Where(value => value != null).Distinct().OrderBy(value => value.label).ToList();

            List<WildlifeTrailSnapshot> trails = (trailMap?.TrailLeads ?? new List<WildlifeTrailLead>())
                .Where(value => value?.species != null).Select(value => new WildlifeTrailSnapshot(value)).ToList();
            List<WildlifeMigrationSnapshot> migrations = (regional?.RoamingAnimals ?? Array.Empty<RoamingAnimalRecord>())
                .Where(value => value?.species != null && value.state != RoamingAnimalState.Dead)
                .Select(value => new WildlifeMigrationSnapshot(value)).ToList();
            List<WildlifeSignalSnapshot> signals = (signalMap?.RecentSignals ?? Array.Empty<WildlifeSignalTrace>())
                .Where(value => value?.species != null).Take(64).Select(value => new WildlifeSignalSnapshot(value, map)).ToList();
            List<WildlifeSpeciesSnapshot> species = new List<WildlifeSpeciesSnapshot>(defs.Count);
            for (int i = 0; i < defs.Count; i++)
            {
                ThingDef def = defs[i];
                RegionalSpeciesRecord record = regional?.Records?.FirstOrDefault(value => value.species == def);
                int local = map.mapPawns.AllPawnsSpawned.Count(pawn => pawn?.def == def && pawn.Faction == null && !pawn.Dead);
                float nearby = record?.nearbyPopulation ?? local;
                float population = record?.population ?? nearby;
                float confidence = record?.confidence ?? 0f;
                float pressure = population <= 0f ? 0f : Mathf.Clamp01((local + nearby * 0.28f) / Mathf.Max(1f, population));
                List<WildlifeEvent> recent = events.Where(value => value?.map == map && value.species == def && IsPlayerFacingEvidence(value))
                    .OrderByDescending(value => value.tick).Take(24).ToList();
                List<WildlifeEvidenceSnapshot> evidence = MergeEvidence(recent);
                IReadOnlyList<string> variations = WildlifeRegionalVariation.Variations(map, def);
                species.Add(new WildlifeSpeciesSnapshot(def, WildlifeKnowledgeAdapter.ColonyStage(def),
                    WildlifeKnowledgeAdapter.ColonyKnowledge(def), confidence, local, nearby, population,
                    WildlifeLearningAPI.HabitatScoreAt(map, map.Center), pressure,
                    record == null ? "No regional estimate" : regional.Forecast(record),
                    record == null ? "Nonintervention" : regional.PolicyLabel(record),
                    evidence,
                    trails.Where(value => value.species == def), migrations.Where(value => value.species == def),
                    signals.Where(value => value.species == def), variations));
            }

            snapshot = new WildlifeEcologySnapshot(map, now, regional?.HabitatQuality ?? 0.5f,
                species, trails, migrations, signals,
                map.GetComponent<WildlifeMysteryMapComponent>()?.Mysteries?.Count(value => value != null && !value.Resolved) ?? 0,
                map.GetComponent<HuntingExpeditionMapComponent>()?.ActiveExpeditions?.Count ?? 0);
            dirty = false;
        }

        private static bool IsPlayerFacingEvidence(WildlifeEvent value)
        {
            if (value == null) return false;
            if (value.metadata != null && value.metadata.TryGetValue("observationLayer", out string layer) &&
                (layer == "passive-familiarity" || layer == "passive-routine")) return false;
            if (value.kind == WildlifeEventKind.Signal)
                return value.metadata != null && value.metadata.TryGetValue("observationLayer", out string signalLayer) &&
                    signalLayer == "signal";
            if (value.kind != WildlifeEventKind.Sighting || value.summary != "A field observation added a small piece of evidence.") return true;
            return value.metadata != null && value.metadata.TryGetValue("observationLayer", out string observationLayer) &&
                observationLayer == "deliberate";
        }

        private static List<WildlifeEvidenceSnapshot> MergeEvidence(List<WildlifeEvent> recent)
        {
            List<WildlifeEvidenceSnapshot> merged = new List<WildlifeEvidenceSnapshot>();
            for (int i = 0; i < recent.Count; i++)
            {
                WildlifeEvent value = recent[i];
                WildlifeEvidenceSnapshot candidate = new WildlifeEvidenceSnapshot(value, MetadataInt(value, "observerCount", 1),
                    WildlifeEvidenceSnapshot.MetadataFloat(value, "observedHours", 0f));
                bool passive = value.metadata != null && value.metadata.TryGetValue("observationLayer", out string layer) && layer == "passive-meaningful";
                string signalTraceId = string.Empty;
                bool signal = value.kind == WildlifeEventKind.Signal && value.metadata != null &&
                    value.metadata.TryGetValue("signalTraceId", out signalTraceId) && !signalTraceId.NullOrEmpty();
                int mergeIndex = -1;
                if (passive || signal)
                {
                    for (int j = 0; j < merged.Count; j++)
                    {
                        WildlifeEvidenceSnapshot existing = merged[j];
                        bool sameSignal = signal && existing.kind == WildlifeEventKind.Signal &&
                            signalTraceId == existing.signalTraceId;
                        if ((sameSignal || (passive && existing.kind == candidate.kind &&
                            existing.discoveryKind == candidate.discoveryKind && existing.facetId == candidate.facetId &&
                            Mathf.Abs(existing.tick - candidate.tick) <= 12000f && existing.contextLabel == candidate.contextLabel)))
                        {
                            mergeIndex = j;
                            break;
                        }
                    }
                }
                if (mergeIndex < 0)
                {
                    merged.Add(candidate);
                    continue;
                }
                WildlifeEvidenceSnapshot existingValue = merged[mergeIndex];
                int count = existingValue.observerCount + candidate.observerCount;
                float contribution = existingValue.amountDelta + candidate.amountDelta;
                float observationHours = existingValue.observationHours + candidate.observationHours;
                float elapsedHours = Mathf.Abs(existingValue.tick - candidate.tick) / 2500f + Mathf.Max(existingValue.elapsedHours, candidate.elapsedHours);
                merged[mergeIndex] = new WildlifeEvidenceSnapshot(value, count, observationHours, elapsedHours, contribution);
            }
            return merged.Take(8).ToList();
        }

        private static int MetadataInt(WildlifeEvent value, string key, int fallback)
        {
            return int.TryParse(value?.metadata != null && value.metadata.TryGetValue(key, out string result) ? result : null, out int parsed) ? parsed : fallback;
        }
    }

    public static class WildlifeEcologySnapshots
    {
        public static WildlifeEcologySnapshot For(Map map) => map?.GetComponent<WildlifeEcologySnapshotMapComponent>()?.Current;

        public static void Invalidate(Map map) => map?.GetComponent<WildlifeEcologySnapshotMapComponent>()?.Invalidate();
    }

    internal static class WildlifeRegionalVariation
    {
        public static IReadOnlyList<string> Variations(Map map, ThingDef species)
        {
            if (map == null || species == null) return Array.Empty<string>();
            List<string> values = new List<string>();
            foreach (AnimalTraditionRecord record in map.GetComponent<AnimalTraditionMapComponent>()?.Traditions ?? Array.Empty<AnimalTraditionRecord>())
            {
                if (record?.species != species || record.strength < 0.3f) continue;
                string label = record.kind == AnimalTraditionKind.TrapWise ? "learned trap avoidance" :
                    record.kind == AnimalTraditionKind.FearedHunter ? "unusual defensiveness" :
                    record.kind == AnimalTraditionKind.SafeValley ? "learned tolerance near safe ground" :
                    record.kind == AnimalTraditionKind.EasyRanch ? "learned access to ranch animals" :
                    record.kind == AnimalTraditionKind.ThunderSticks ? "weapon-aware caution" : "learned human trust";
                values.Add(label);
            }
            return values.Distinct().Take(6).ToList();
        }
    }
}
