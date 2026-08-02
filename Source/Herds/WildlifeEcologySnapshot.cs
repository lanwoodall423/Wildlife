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

        internal WildlifeEvidenceSnapshot(WildlifeEvent value)
        {
            kind = value.kind;
            tick = value.tick;
            source = value.source;
            summary = value.summary;
            quality = value.quality;
            confidence = value.confidence;
            success = value.success;
            cell = value.cell;
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
        public readonly float radius;

        internal WildlifeSignalSnapshot(WildlifeSignalTrace value)
        {
            species = value.species;
            kind = value.kind;
            cell = value.cell;
            tick = value.tick;
            truthful = value.truthful;
            verified = value.verified;
            behaviorConsistent = value.behaviorConsistent;
            radius = value.radius;
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
                .Where(value => value?.species != null).Take(64).Select(value => new WildlifeSignalSnapshot(value)).ToList();
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
                List<WildlifeEvent> recent = events.Where(value => value?.map == map && value.species == def)
                    .OrderByDescending(value => value.tick).Take(8).ToList();
                IReadOnlyList<string> variations = WildlifeRegionalVariation.Variations(map, def);
                species.Add(new WildlifeSpeciesSnapshot(def, WildlifeKnowledgeAdapter.ColonyStage(def),
                    WildlifeKnowledgeAdapter.ColonyKnowledge(def), confidence, local, nearby, population,
                    WildlifeLearningAPI.HabitatScoreAt(map, map.Center), pressure,
                    record == null ? "No regional estimate" : regional.Forecast(record),
                    record == null ? "Nonintervention" : regional.PolicyLabel(record),
                    recent.Select(value => new WildlifeEvidenceSnapshot(value)),
                    trails.Where(value => value.species == def), migrations.Where(value => value.species == def),
                    signals.Where(value => value.species == def), variations));
            }

            snapshot = new WildlifeEcologySnapshot(map, now, regional?.HabitatQuality ?? 0.5f,
                species, trails, migrations, signals,
                map.GetComponent<WildlifeMysteryMapComponent>()?.Mysteries?.Count(value => value != null && !value.Resolved) ?? 0,
                map.GetComponent<HuntingExpeditionMapComponent>()?.ActiveExpeditions?.Count ?? 0);
            dirty = false;
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
