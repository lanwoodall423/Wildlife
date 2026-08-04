using System;
using System.Collections.Generic;
using System.Linq;
using LudeonTK;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Herds
{
    public static class WildlifeLearningAPI
    {
        public static float FactorFor(Pawn pawn) => pawn?.Map?.GetComponent<RegionalWildlifeMapComponent>()?.LearningFactor(pawn) ?? 0f;

        public static float HabitatScoreAt(Map map, IntVec3 cell)
        {
            if (HerdsMod.Settings?.enableHabitatEcology != true || map == null || !cell.InBounds(map)) return 0.5f;
            float fertility = map.fertilityGrid.FertilityAt(cell);
            float shelter = cell.Roofed(map) ? 0.18f : 0f;
            float vegetation = cell.GetPlant(map) != null ? 0.18f : 0f;
            return Mathf.Clamp01(fertility * 0.55f + shelter + vegetation);
        }
    }

    public sealed class RegionalSpeciesRecord : IExposable
    {
        public ThingDef species;
        public string legacySpeciesDefName;
        public float population;
        public float previousPopulation;
        public float nearbyPopulation;
        public float previousNearbyPopulation;
        public float confidence;
        public int policy;
        public int lastLocalCount;
        public int lastUpdateTick;
        public List<float> neighboringPopulations = new List<float>();
        public int consequenceState;

        public void ExposeData()
        {
            Scribe_Defs.Look(ref species, "species");
            if (Scribe.mode == LoadSaveMode.Saving && species != null) legacySpeciesDefName = species.defName;
            Scribe_Values.Look(ref legacySpeciesDefName, "legacySpeciesDefName");
            Scribe_Values.Look(ref population, "population", 0f);
            Scribe_Values.Look(ref previousPopulation, "previousPopulation", 0f);
            Scribe_Values.Look(ref nearbyPopulation, "nearbyPopulation", 0f);
            Scribe_Values.Look(ref previousNearbyPopulation, "previousNearbyPopulation", 0f);
            Scribe_Values.Look(ref confidence, "confidence", 0f);
            Scribe_Values.Look(ref policy, "policy", 0);
            Scribe_Values.Look(ref lastLocalCount, "lastLocalCount", 0);
            Scribe_Values.Look(ref lastUpdateTick, "lastUpdateTick", 0);
            Scribe_Collections.Look(ref neighboringPopulations, "neighboringPopulations", LookMode.Value);
            Scribe_Values.Look(ref consequenceState, "consequenceState", 0);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) neighboringPopulations = neighboringPopulations ?? new List<float>();
        }
    }

    public enum RoamingAnimalState
    {
        Present,
        RoamingNearby,
        SeasonalMigration,
        Displaced,
        Missing,
        Dead
    }

    public sealed class RoamingAnimalRecord : IExposable
    {
        public Pawn animal;
        public ThingDef species;
        public string legacySpeciesDefName;
        public RoamingAnimalState state;
        public string reason;
        public string direction;
        public int leftTick;
        public int earliestReturnTick;
        public int expectedReturnTick;
        public int lastSeenTick;
        public int returnCount;
        public int encouragedUntilTick;
        public int discouragedUntilTick;
        public bool tagged;
        public bool notable;
        public int herdId;

        public void ExposeData()
        {
            Scribe_References.Look(ref animal, "animal");
            Scribe_Defs.Look(ref species, "species");
            if (Scribe.mode == LoadSaveMode.Saving && species != null) legacySpeciesDefName = species.defName;
            Scribe_Values.Look(ref legacySpeciesDefName, "legacySpeciesDefName");
            Scribe_Values.Look(ref state, "state", RoamingAnimalState.RoamingNearby);
            Scribe_Values.Look(ref reason, "reason");
            Scribe_Values.Look(ref direction, "direction");
            Scribe_Values.Look(ref leftTick, "leftTick", 0);
            Scribe_Values.Look(ref earliestReturnTick, "earliestReturnTick", 0);
            Scribe_Values.Look(ref expectedReturnTick, "expectedReturnTick", 0);
            Scribe_Values.Look(ref lastSeenTick, "lastSeenTick", 0);
            Scribe_Values.Look(ref returnCount, "returnCount", 0);
            Scribe_Values.Look(ref encouragedUntilTick, "encouragedUntilTick", 0);
            Scribe_Values.Look(ref discouragedUntilTick, "discouragedUntilTick", 0);
            Scribe_Values.Look(ref tagged, "tagged", false);
            Scribe_Values.Look(ref notable, "notable", false);
            Scribe_Values.Look(ref herdId, "herdId", 0);
        }
    }

    internal static class WildlifePopulationPolicy
    {
        internal const int ReplacementCooldownTicks = 120000;

        internal static bool CanAddLocalAnimal(int now, int lastLossTick, int localCount,
            float nearbyPopulation, float regionalPopulation, bool mapInitializing)
        {
            if (mapInitializing) return true;
            if (regionalPopulation < 1f) return false;
            if (lastLossTick > 0 && now - lastLossTick < ReplacementCooldownTicks) return false;
            int desired = Mathf.CeilToInt(Mathf.Clamp(nearbyPopulation * 0.28f, 1f, 14f));
            return localCount < desired;
        }
    }

    public sealed class AnimalRelationshipRecord : IExposable
    {
        public Pawn animal;
        public Pawn mate;
        public Pawn parent;
        public Pawn teacher;
        public Pawn rival;
        public void ExposeData()
        {
            Scribe_References.Look(ref animal, "animal"); Scribe_References.Look(ref mate, "mate");
            Scribe_References.Look(ref parent, "parent"); Scribe_References.Look(ref teacher, "teacher"); Scribe_References.Look(ref rival, "rival");
        }
    }

    public sealed class RegionalWildlifeMapComponent : MapComponent
    {
        private List<RegionalSpeciesRecord> records = new List<RegionalSpeciesRecord>();
        private List<RegionalSpeciesRecord> orphanedRecords = new List<RegionalSpeciesRecord>();
        private List<RoamingAnimalRecord> roamingAnimals = new List<RoamingAnimalRecord>();
        private List<RoamingAnimalRecord> orphanedRoamingAnimals = new List<RoamingAnimalRecord>();
        private Dictionary<Pawn, float> juvenileLearning = new Dictionary<Pawn, float>();
        private readonly Dictionary<Pawn, string> pendingDepartureReasons = new Dictionary<Pawn, string>();
        private readonly Dictionary<Pawn, int> pendingDepartureHerds = new Dictionary<Pawn, int>();
        private Dictionary<string, int> populationLossTicks = new Dictionary<string, int>();
        private int nextRegionalTick;
        private int nextLearningTick;
        private int nextScavengeTick;
        private float habitatQuality = 0.5f;
        private int lastImmigrationTick;
        private int lastEmigrationTick;
        private int lastCameraOutcomeTick;
        private int lastTelemetryOutcomeTick;
        private int scavengingOrders;
        private Pawn lastMigrant;
        private IntVec3 lastMigrationEdge;
        private int migrationVisualUntil;
        private bool regionalSeeded;
        private int nextWildlifeEventTick;
        private int lastSeason = -1;
        private string activeSeasonalEvent;
        private int activeSeasonalEventUntil;
        private int nextRelationshipTick;
        private List<AnimalRelationshipRecord> relationships = new List<AnimalRelationshipRecord>();
        private Dictionary<Corpse, Pawn> carcassOwners = new Dictionary<Corpse, Pawn>();
        private Dictionary<Corpse, IntVec3> carcassCaches = new Dictionary<Corpse, IntVec3>();
        private int cachedReserves, cachedBait, cachedRestoration, cachedWater, cachedBurns, cachedCorridors, cachedDeterrents;
        private int nextLocalRoamingTick;

        public RegionalWildlifeMapComponent(Map map) : base(map) { }

        // Public projection hook for optional Deferred Reality integration; regional truth remains provider-owned.
        public Map ActiveMap => map;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref records, "regionalWildlife", LookMode.Deep);
            Scribe_Collections.Look(ref orphanedRecords, "deferredRealityRegionalOrphans", LookMode.Deep);
            Scribe_Collections.Look(ref roamingAnimals, "persistentRoamingAnimals", LookMode.Deep);
            Scribe_Collections.Look(ref orphanedRoamingAnimals, "deferredRealityRoamingOrphans", LookMode.Deep);
            Scribe_Collections.Look(ref juvenileLearning, "juvenileWildlifeLearning", LookMode.Reference, LookMode.Value);
            Scribe_Values.Look(ref habitatQuality, "wildlifeHabitatQuality", 0.5f);
            Scribe_Values.Look(ref lastImmigrationTick, "lastWildlifeImmigrationTick", 0);
            Scribe_Values.Look(ref lastEmigrationTick, "lastWildlifeEmigrationTick", 0);
            Scribe_Values.Look(ref lastCameraOutcomeTick, "lastCameraOutcomeTick", 0);
            Scribe_Values.Look(ref lastTelemetryOutcomeTick, "lastTelemetryOutcomeTick", 0);
            Scribe_Values.Look(ref regionalSeeded, "regionalWildlifeSeeded", false);
            Scribe_Values.Look(ref nextWildlifeEventTick, "nextWildlifeEventTick", 0);
            Scribe_Values.Look(ref lastSeason, "lastWildlifeSeason", -1);
            Scribe_Values.Look(ref activeSeasonalEvent, "activeSeasonalEvent");
            Scribe_Values.Look(ref activeSeasonalEventUntil, "activeSeasonalEventUntil", 0);
            Scribe_Values.Look(ref nextLocalRoamingTick, "nextLocalRoamingTick", 0);
            Scribe_Collections.Look(ref relationships, "animalRelationships", LookMode.Deep);
            Scribe_Collections.Look(ref carcassOwners, "carcassOwners", LookMode.Reference, LookMode.Reference);
            Scribe_Collections.Look(ref carcassCaches, "carcassCaches", LookMode.Reference, LookMode.Value);
            Scribe_Collections.Look(ref populationLossTicks, "wildlifePopulationLossTicks", LookMode.Value, LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                orphanedRecords = orphanedRecords ?? new List<RegionalSpeciesRecord>();
                orphanedRecords.AddRange((records ?? new List<RegionalSpeciesRecord>()).Where(record => record?.species == null));
                records = records?.Where(record => record?.species?.race?.Animal == true).ToList() ?? new List<RegionalSpeciesRecord>();
                orphanedRoamingAnimals = orphanedRoamingAnimals ?? new List<RoamingAnimalRecord>();
                orphanedRoamingAnimals.AddRange((roamingAnimals ?? new List<RoamingAnimalRecord>()).Where(record => record?.animal == null ||
                    record.species?.race?.Animal != true));
                roamingAnimals = roamingAnimals?.Where(record => record?.animal != null &&
                    record.species?.race?.Animal == true).ToList() ?? new List<RoamingAnimalRecord>();
                juvenileLearning = juvenileLearning ?? new Dictionary<Pawn, float>();
                populationLossTicks = populationLossTicks ?? new Dictionary<string, int>();
                relationships = relationships?.Where(record => record?.animal != null && !record.animal.Dead).ToList() ?? new List<AnimalRelationshipRecord>();
                carcassOwners = carcassOwners ?? new Dictionary<Corpse, Pawn>();
                carcassCaches = carcassCaches ?? new Dictionary<Corpse, IntVec3>();
            }
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            if (HerdsMod.Settings.enableRegionalPopulations) UpdateRegional(Find.TickManager?.TicksGame ?? 0, false);
        }

        public override void MapComponentTick()
        {
            HerdsSettings settings = HerdsMod.Settings;
            if (settings == null || (!settings.enableRegionalPopulations && !settings.enableJuvenileLearning && !settings.enableScavenging && !settings.enableAnimalRelationships)) return;
            int now = Find.TickManager.TicksGame;
            if (settings.enableRegionalPopulations && now >= nextRegionalTick) UpdateRegional(now, true);
            if (settings.enableRegionalPopulations && settings.enableRegionalMigration &&
                now >= nextLocalRoamingTick) UpdateLocalRoaming(now);
            if (settings.enableJuvenileLearning && now >= nextLearningTick) UpdateLearning(now);
            if (settings.enableScavenging && now >= nextScavengeTick) UpdateScavengers(now);
            if (settings.enableAnimalRelationships && now >= nextRelationshipTick) UpdateRelationships(now);
        }

        public string ActiveSeasonalEvent =>
            Find.TickManager.TicksGame < activeSeasonalEventUntil ? activeSeasonalEvent : null;

        public override void MapComponentDraw()
        {
            base.MapComponentDraw();
            if (!Prefs.DevMode || !WildlifeDevMaster.CompleteOverlayEnabled || Find.CurrentMap != map) return;
            if (lastMigrant?.Spawned == true && Find.TickManager.TicksGame < migrationVisualUntil)
            {
                GenDraw.DrawLineBetween(lastMigrant.Position.ToVector3Shifted(), lastMigrationEdge.ToVector3Shifted(), SimpleColor.Green);
                GenDraw.DrawRadiusRing(lastMigrationEdge, 1.2f, Color.green);
            }
            if (HerdsMod.Settings.enableAnimalRelationships)
                for (int i = 0; i < relationships.Count; i++)
                {
                    AnimalRelationshipRecord relation = relationships[i];
                    if (relation?.animal?.Spawned != true) continue;
                    if (relation.mate?.Spawned == true && relation.animal.thingIDNumber < relation.mate.thingIDNumber) GenDraw.DrawLineBetween(relation.animal.Position.ToVector3Shifted(), relation.mate.Position.ToVector3Shifted(), SimpleColor.Magenta);
                    if (relation.teacher?.Spawned == true) GenDraw.DrawLineBetween(relation.animal.Position.ToVector3Shifted(), relation.teacher.Position.ToVector3Shifted(), SimpleColor.Cyan);
                    if (relation.rival?.Spawned == true && relation.animal.thingIDNumber < relation.rival.thingIDNumber) GenDraw.DrawLineBetween(relation.animal.Position.ToVector3Shifted(), relation.rival.Position.ToVector3Shifted(), SimpleColor.Red);
                }
            if (HerdsMod.Settings.enableAdvancedScavenging)
                foreach (KeyValuePair<Corpse, Pawn> claim in carcassOwners)
                    if (claim.Key?.Spawned == true && claim.Value?.Spawned == true)
                    {
                        GenDraw.DrawLineBetween(claim.Value.Position.ToVector3Shifted(), claim.Key.Position.ToVector3Shifted(), SimpleColor.Yellow);
                        GenDraw.DrawRadiusRing(claim.Key.Position, 0.65f, new Color(1f, 0.55f, 0.1f));
                    }
            if (HerdsMod.Settings.enableTelemetry && WildlifeProgression.Unlocked(WildlifeCapability.Telemetry) && HerdsDefOf.Herds_TrackingCollar != null)
            {
                IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
                for (int i = 0; i < pawns.Count; i++)
                    if (pawns[i]?.health?.hediffSet?.GetFirstHediffOfDef(HerdsDefOf.Herds_TrackingCollar) != null)
                        GenDraw.DrawRadiusRing(pawns[i].Position, 0.8f, Color.cyan);
            }
            if (HerdsMod.Settings.enableConservationActions)
                for (int i = 0; i < map.listerBuildings.allBuildingsColonist.Count; i++)
                    if (map.listerBuildings.allBuildingsColonist[i] is Building_WildlifeTool tool && tool.active && (tool.Kind == WildlifeToolKind.HabitatRestoration || tool.Kind == WildlifeToolKind.WaterSource || tool.Kind == WildlifeToolKind.MigrationCorridor || tool.Kind == WildlifeToolKind.ManagedBurn))
                    {
                        GenDraw.DrawRadiusRing(tool.Position, tool.InfluenceRadius, tool.Kind == WildlifeToolKind.WaterSource ? Color.cyan : tool.Kind == WildlifeToolKind.HabitatRestoration ? Color.green : Color.yellow);
                    }
            for (int i = 0; i < map.listerBuildings.allBuildingsColonist.Count; i++)
                if (map.listerBuildings.allBuildingsColonist[i] is Building_WildlifeTool monitor && monitor.active &&
                    (monitor.Kind == WildlifeToolKind.CameraTrap || monitor.Kind == WildlifeToolKind.TelemetryStation))
                {
                    GenDraw.DrawRadiusRing(monitor.Position, monitor.InfluenceRadius, monitor.Kind == WildlifeToolKind.TelemetryStation ? Color.cyan : new Color(0.45f, 0.8f, 0.55f));
                }
        }

        public override void MapComponentOnGUI()
        {
            base.MapComponentOnGUI();
            if (!Prefs.DevMode || !WildlifeDevMaster.CompleteOverlayEnabled || Find.CurrentMap != map) return;
            foreach (KeyValuePair<Pawn, float> pair in juvenileLearning)
                if (pair.Key?.Spawned == true && !pair.Key.ageTracker.Adult)
                    GenMapUI.DrawThingLabel(pair.Key, "learning " + pair.Value.ToStringPercent());
            if (lastMigrant?.Spawned == true && Find.TickManager.TicksGame < migrationVisualUntil)
                GenMapUI.DrawThingLabel(lastMigrant, "regional migrant");
            if (HerdsMod.Settings.enableTelemetry && WildlifeProgression.Unlocked(WildlifeCapability.Telemetry) &&
                HerdsDefOf.Herds_TrackingCollar != null)
            {
                IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
                for (int i = 0; i < pawns.Count; i++)
                    if (pawns[i]?.health?.hediffSet?.GetFirstHediffOfDef(HerdsDefOf.Herds_TrackingCollar) != null)
                        GenMapUI.DrawThingLabel(pawns[i], "telemetry: transmitting");
            }
            if (HerdsMod.Settings.enableConservationActions)
                for (int i = 0; i < map.listerBuildings.allBuildingsColonist.Count; i++)
                    if (map.listerBuildings.allBuildingsColonist[i] is Building_WildlifeTool tool && tool.active &&
                        (tool.Kind == WildlifeToolKind.HabitatRestoration || tool.Kind == WildlifeToolKind.WaterSource ||
                         tool.Kind == WildlifeToolKind.MigrationCorridor || tool.Kind == WildlifeToolKind.ManagedBurn))
                        GenMapUI.DrawThingLabel(tool, tool.Kind + " | habitat influence");
            for (int i = 0; i < map.listerBuildings.allBuildingsColonist.Count; i++)
                if (map.listerBuildings.allBuildingsColonist[i] is Building_WildlifeTool monitor && monitor.active &&
                    (monitor.Kind == WildlifeToolKind.CameraTrap || monitor.Kind == WildlifeToolKind.TelemetryStation))
                    GenMapUI.DrawThingLabel(monitor, monitor.Kind + " | " +
                        (monitor.Kind == WildlifeToolKind.TelemetryStation ? "regional tracking" : "automated census"));
        }

        public IReadOnlyList<RegionalSpeciesRecord> Records
        {
            get { EnsureCurrent(); return records.OrderByDescending(record => record.population).ThenBy(record => record.species.label).ToList(); }
        }

        public IReadOnlyList<RegionalSpeciesRecord> OrphanedRecords => orphanedRecords;

        public IReadOnlyList<RoamingAnimalRecord> RoamingAnimals => roamingAnimals;
        public IReadOnlyList<RoamingAnimalRecord> OrphanedRoamingAnimals => orphanedRoamingAnimals;
        public int RoamingCount => roamingAnimals.Count(record =>
            record != null && record.state != RoamingAnimalState.Present &&
            record.state != RoamingAnimalState.Dead);
        public int KnownRoamingCount => roamingAnimals.Count(record =>
            record != null && record.state != RoamingAnimalState.Present && record.state != RoamingAnimalState.Dead &&
            (!HerdsMod.Settings.enableSpeciesKnowledgeProgression ||
                HuntingKnowledgeMapComponent.ColonyExperience(record.species) > 0f));

        public IEnumerable<RoamingAnimalRecord> RoamersFor(ThingDef species) =>
            roamingAnimals.Where(record => record?.species == species &&
                record.state != RoamingAnimalState.Dead);

        public void QueueDeparture(Pawn animal, string reason)
        {
            QueueDeparture(animal, reason, IntVec3.Invalid);
        }

        public void QueueDeparture(Pawn animal, string reason, IntVec3 edge)
        {
            if (animal?.RaceProps?.Animal != true || animal.Faction != null) return;
            string departureReason = reason.NullOrEmpty() ? "Roaming beyond the map" : reason;
            HerdSnapshot herd = map.GetComponent<HerdMapComponent>()?.HerdFor(animal);
            int herdId = herd?.members.Count > 1 ? herd.id : 0;
            IEnumerable<Pawn> followers = herdId == 0 ? new[] { animal } : herd.members;
            foreach (Pawn follower in followers)
            {
                if (!CanFollowMigration(follower)) continue;
                pendingDepartureReasons[follower] = departureReason;
                pendingDepartureHerds[follower] = herdId;
                if (follower == animal || !edge.IsValid ||
                    !follower.CanReach(edge, PathEndMode.OnCell, Danger.Deadly)) continue;
                Job leave = JobMaker.MakeJob(JobDefOf.Goto, edge);
                leave.exitMapOnArrival = true;
                leave.expiryInterval = 12000;
                follower.jobs.StartJob(leave, JobCondition.InterruptForced);
            }
        }

        internal static bool CanFollowMigration(Pawn animal) =>
            animal?.Spawned == true && !animal.Dead && !animal.Downed &&
            !animal.InMentalState && animal.Faction == null && animal.RaceProps?.Animal == true;

        public bool ShouldPreserveExit(Pawn animal)
        {
            if (HerdsMod.Settings?.enablePersistentRoamingAnimals != true ||
                animal?.Spawned != true || animal.Faction != null) return false;
            string reason = pendingDepartureReasons.TryGetValue(animal, out string pendingReason)
                ? pendingReason : "Roaming beyond the colony map";
            QueueDeparture(animal, reason, animal.Position);
            if ((!pendingDepartureHerds.TryGetValue(animal, out int herdId) || herdId == 0) &&
                !ShouldPersist(animal))
                return false;
            RegisterRoamingDeparture(animal, pendingDepartureReasons.TryGetValue(animal,
                out reason) ? reason : "Roaming beyond the colony map");
            pendingDepartureReasons.Remove(animal);
            pendingDepartureHerds.Remove(animal);
            return true;
        }

        public void NotifyOrdinaryDeparture(Pawn animal)
        {
            if (animal?.def == null) return;
            RegionalSpeciesRecord record = RecordFor(animal.def, true);
            record.lastLocalCount = CountLocalWildlife(animal.def, animal);
            lastEmigrationTick = Find.TickManager?.TicksGame ?? 0;
            pendingDepartureReasons.Remove(animal);
            pendingDepartureHerds.Remove(animal);
        }

        public void NotifyLocalDeath(Pawn animal)
        {
            if (animal?.def == null || animal.Faction != null) return;
            RegionalSpeciesRecord record = RecordFor(animal.def, true);
            record.lastLocalCount = CountLocalWildlife(animal.def, animal);
            record.nearbyPopulation = Mathf.Max(record.lastLocalCount,
                record.nearbyPopulation - 1f);
            record.population = Mathf.Max(record.nearbyPopulation,
                record.population - 1f);
            RoamingAnimalRecord roaming = roamingAnimals.FirstOrDefault(value => value.animal == animal);
            if (roaming != null) roaming.state = RoamingAnimalState.Dead;
            populationLossTicks[animal.def.defName] = Find.TickManager?.TicksGame ?? 0;
            if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("NearbyPopulationDeath",
                "species=" + animal.def.defName + " nearby=" +
                record.nearbyPopulation.ToString("0.0"), animal);
        }

        public void NotifyLocalCapture(Pawn animal)
        {
            if (animal?.def?.race?.Animal != true) return;
            RegionalSpeciesRecord record = RecordFor(animal.def, true);
            record.lastLocalCount = CountLocalWildlife(animal.def, animal);
            record.nearbyPopulation = Mathf.Max(record.lastLocalCount, record.nearbyPopulation - 1f);
            record.population = Mathf.Max(record.nearbyPopulation, record.population - 1f);
            populationLossTicks[animal.def.defName] = Find.TickManager?.TicksGame ?? 0;
            pendingDepartureReasons.Remove(animal);
            pendingDepartureHerds.Remove(animal);
        }

        public void NotifyLocalSpawn(Pawn animal, bool respawningAfterLoad)
        {
            if (animal?.def?.race?.Animal != true || animal.Faction != null) return;
            RegionalSpeciesRecord record = RecordFor(animal.def, true);
            record.lastLocalCount = CountLocalWildlife(animal.def);
            record.nearbyPopulation = Mathf.Max(record.nearbyPopulation, record.lastLocalCount);
            record.population = Mathf.Max(record.population, record.nearbyPopulation);
        }

        public bool CanSpawnWildAnimal(PawnKindDef kind, bool mapInitializing)
        {
            if (kind?.race?.race?.Animal != true || HerdsMod.Settings?.enableRegionalPopulations != true) return true;
            RegionalSpeciesRecord record = RecordFor(kind.race, false);
            if (record == null) return true;
            int now = Find.TickManager?.TicksGame ?? 0;
            int lossTick = populationLossTicks.TryGetValue(kind.race.defName, out int value) ? value : 0;
            return WildlifePopulationPolicy.CanAddLocalAnimal(now, lossTick,
                CountLocalWildlife(kind.race), record.nearbyPopulation, record.population, mapInitializing);
        }

        public bool CanEncourageReturns => cachedBait + cachedWater + cachedReserves + cachedCorridors > 0;
        public bool CanDiscourageReturns => cachedDeterrents > 0;

        public float PredatorDeterrentReturnChanceModifier(ThingDef species) =>
            PredatorDeterrentReturnChanceModifier(species, cachedDeterrents);

        public float PredatorDeterrentMigrationAttractionModifier(ThingDef species) =>
            PredatorDeterrentMigrationAttractionModifier(species, cachedDeterrents);

        public static float PredatorDeterrentReturnChanceModifier(ThingDef species, int deterrentCount) =>
            WildlifeSpeciesClassification.IsPredator(species)
                ? -Mathf.Min(0.20f, Mathf.Max(0, deterrentCount) * 0.05f) : 0f;

        public static float PredatorDeterrentMigrationAttractionModifier(ThingDef species, int deterrentCount) =>
            WildlifeSpeciesClassification.IsPredator(species) && deterrentCount > 0 ? -0.75f : 0f;

        public static bool PredatorDeterrentEffectSelfTest()
        {
            ThingDef predator = DefDatabase<ThingDef>.AllDefsListForReading.FirstOrDefault(value =>
                value?.race?.Animal == true && WildlifeSpeciesClassification.IsPredator(value));
            ThingDef ordinary = DefDatabase<ThingDef>.AllDefsListForReading.FirstOrDefault(value =>
                value?.race?.Animal == true && !WildlifeSpeciesClassification.IsPredator(value));
            if (predator == null || ordinary == null) return false;
            return PredatorDeterrentReturnChanceModifier(predator, 0) == 0f &&
                PredatorDeterrentReturnChanceModifier(predator, 1) < 0f &&
                PredatorDeterrentReturnChanceModifier(ordinary, 1) == 0f &&
                PredatorDeterrentMigrationAttractionModifier(predator, 0) == 0f &&
                PredatorDeterrentMigrationAttractionModifier(predator, 1) == -0.75f &&
                PredatorDeterrentMigrationAttractionModifier(ordinary, 1) == 0f;
        }

        public bool PredatorDeterrentIntegrationSelfTest(out string detail)
        {
            detail = "";
            ThingDef deterrentDef = HerdsDefOf.Herds_PredatorDeterrent;
            ThingDef predator = DefDatabase<ThingDef>.AllDefsListForReading.FirstOrDefault(value =>
                value?.race?.Animal == true && WildlifeSpeciesClassification.IsPredator(value));
            if (map == null || deterrentDef == null || predator == null)
            {
                detail = "missing map, Predator Deterrent Def, or predator species";
                return false;
            }

            List<Thing> existing = map.listerThings?.ThingsOfDef(deterrentDef)
                ?.Where(thing => thing?.Spawned == true).ToList() ?? new List<Thing>();
            List<Tuple<Thing, IntVec3, Rot4>> parked = new List<Tuple<Thing, IntVec3, Rot4>>();
            Thing temporary = null;
            try
            {
                for (int i = 0; i < existing.Count; i++)
                {
                    Thing thing = existing[i];
                    parked.Add(Tuple.Create(thing, thing.Position, thing.Rotation));
                    thing.DeSpawn(DestroyMode.Vanish);
                }
                RefreshToolCounts();
                int withoutCount = cachedDeterrents;
                RegionalSpeciesRecord record = new RegionalSpeciesRecord
                {
                    species = predator,
                    population = 24f,
                    previousPopulation = 24f,
                    lastLocalCount = 2,
                    policy = 0
                };
                float without = MigrationPressure(record);

                temporary = ThingMaker.MakeThing(deterrentDef);
                IntVec3 cell = CellFinder.RandomClosewalkCellNear(map.Center, map, 8);
                GenSpawn.Spawn(temporary, cell, map, Rot4.North);
                RefreshToolCounts();
                int withCount = cachedDeterrents;
                float with = MigrationPressure(record);

                temporary.Destroy(DestroyMode.Vanish);
                temporary = null;
                RefreshToolCounts();
                float restored = MigrationPressure(record);
                bool changed = withoutCount == 0 && withCount == 1 && with < without;
                bool restoredState = cachedDeterrents == 0 && Mathf.Abs(restored - without) < 0.0001f;
                detail = "without=" + without.ToString("0.000") + " with=" + with.ToString("0.000") +
                    " restored=" + restored.ToString("0.000") + " counts=" + withoutCount + "/" +
                    withCount + "/" + cachedDeterrents + " cadence=300";
                return changed && restoredState;
            }
            catch (Exception exception)
            {
                detail = exception.GetType().Name + ": " + exception.Message;
                return false;
            }
            finally
            {
                if (temporary?.Spawned == true) temporary.Destroy(DestroyMode.Vanish);
                for (int i = 0; i < parked.Count; i++)
                {
                    Thing thing = parked[i].Item1;
                    if (thing == null || thing.Destroyed || thing.Spawned) continue;
                    try { GenSpawn.Spawn(thing, parked[i].Item2, map, parked[i].Item3); }
                    catch { }
                }
                RefreshToolCounts();
            }
        }

        public void EncourageReturn(RoamingAnimalRecord record)
        {
            if (record == null || !CanEncourageReturns) return;
            record.encouragedUntilTick = Find.TickManager.TicksGame + 300000;
            record.discouragedUntilTick = 0;
            Messages.Message("Bait, water, reserves, and migration corridors will encourage " +
                record.animal.LabelShortCap + " to return.", MessageTypeDefOf.PositiveEvent, false);
            WildlifeExperience.Record("Wildlife Management", "The colony prepared habitat to encourage " +
                record.animal.LabelShortCap + " to return.");
        }

        public void DiscourageReturn(RoamingAnimalRecord record)
        {
            if (record == null || !CanDiscourageReturns) return;
            record.discouragedUntilTick = Find.TickManager.TicksGame + 300000;
            record.encouragedUntilTick = 0;
            Messages.Message("Active deterrents will discourage " + record.animal.LabelShortCap +
                " from returning.", MessageTypeDefOf.NeutralEvent, false);
        }

        public float HabitatQuality { get { EnsureCurrent(); return habitatQuality; } }

        public float LearningFactor(Pawn pawn)
        {
            return pawn != null && juvenileLearning.TryGetValue(pawn, out float value) ? Mathf.Clamp01(value) : 0f;
        }

        public string LearningLabel(Pawn pawn)
        {
            float value = LearningFactor(pawn);
            return value < 0.2f ? "Untrained" : value < 0.5f ? "Learning" : value < 0.8f ? "Practiced" : "Experienced";
        }

        public void Survey(Pawn observer)
        {
            if (!HerdsMod.Settings.enableRegionalPopulations || observer?.Spawned != true) return;
            EnsureCurrent();
            int skill = observer.skills?.GetSkill(SkillDefOf.Animals)?.Level ?? 0;
            int proficiency = map.GetComponent<HuntingKnowledgeMapComponent>()?.WildlifeProficiencyLevel(observer) ?? 0;
            float journal = map.GetComponent<WildlifeFieldJournalMapComponent>()?.OutcomeBonus ?? 0f;
            float warningSystemBonus = WildlifeProgression.Unlocked(WildlifeCapability.WarningSystems) ? 0.05f : 0f;
            for (int i = 0; i < records.Count; i++)
            {
                int knowledge = HuntingKnowledgeMapComponent.ColonyLevel(records[i].species);
                records[i].confidence = Mathf.Clamp01(records[i].confidence + 0.05f + warningSystemBonus + skill * 0.008f + knowledge * 0.015f + proficiency * 0.02f + journal);
            }
            WildlifeKnowledgeAdapter.LearnBiome(observer, map.Biome, 6f + skill * 0.25f + proficiency, false);
            Messages.Message(observer.LabelShortCap + " completed a regional wildlife survey.", observer, MessageTypeDefOf.PositiveEvent, false);
            WildlifeExperience.Record("Observation", observer.LabelShortCap + " completed a regional wildlife survey.", observer);
            if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("RegionalSurvey", "skill=" + skill + " records=" + records.Count, observer);
        }

        public void AutomatedSurvey(IntVec3 origin, float radius)
        {
            if (!HerdsMod.Settings.enableRegionalPopulations || !HerdsMod.Settings.enableCameraTraps || !WildlifeProgression.Unlocked(WildlifeCapability.CameraMonitoring)) return;
            EnsureCurrent();
            float radiusSquared = radius * radius;
            HashSet<ThingDef> observed = new HashSet<ThingDef>();
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn?.Spawned != true || pawn.RaceProps?.Animal != true || pawn.Position.DistanceToSquared(origin) > radiusSquared) continue;
                observed.Add(pawn.def);
            }
            for (int i = 0; i < records.Count; i++)
                if (observed.Contains(records[i].species)) records[i].confidence = Mathf.Clamp01(records[i].confidence + 0.012f);
            int now = Find.TickManager.TicksGame;
            if (observed.Count > 0 && now >= lastCameraOutcomeTick + 60000)
            {
                lastCameraOutcomeTick = now;
                WildlifeExperience.Record("Camera Trap", "Camera traps recorded " + observed.Count + " wildlife species.");
            }
            if (observed.Count > 0 && WildlifeTestLog.Enabled) WildlifeTestLog.Write("CameraSurvey", "species=" + observed.Count + " origin=" + origin);
        }

        public void TelemetrySurvey()
        {
            if (!HerdsMod.Settings.enableRegionalPopulations || !HerdsMod.Settings.enableTelemetry || !WildlifeProgression.Unlocked(WildlifeCapability.Telemetry)) return;
            EnsureCurrent();
            int tagged = 0;
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn?.Spawned != true || pawn.RaceProps?.Animal != true || pawn.health?.hediffSet?.GetFirstHediffOfDef(HerdsDefOf.Herds_TrackingCollar) == null) continue;
                RegionalSpeciesRecord record = records.FirstOrDefault(candidate => candidate.species == pawn.def);
                if (record != null) record.confidence = Mathf.Clamp01(record.confidence + 0.025f);
                tagged++;
            }
            for (int i = 0; i < roamingAnimals.Count; i++)
            {
                RoamingAnimalRecord roaming = roamingAnimals[i];
                if (roaming?.tagged != true || roaming.state == RoamingAnimalState.Present ||
                    roaming.state == RoamingAnimalState.Dead) continue;
                RegionalSpeciesRecord record = RecordFor(roaming.species, true);
                record.confidence = Mathf.Clamp01(record.confidence + 0.025f);
                roaming.lastSeenTick = Find.TickManager.TicksGame;
                tagged++;
            }
            int now = Find.TickManager.TicksGame;
            if (tagged > 0 && now >= lastTelemetryOutcomeTick + 60000)
            {
                lastTelemetryOutcomeTick = now;
                WildlifeExperience.Record("Telemetry", "Telemetry updated positions for " + tagged + " tagged animal" + (tagged == 1 ? "." : "s."));
            }
            if (tagged > 0 && WildlifeTestLog.Enabled) WildlifeTestLog.Write("TelemetrySurvey", "tagged=" + tagged);
        }

        public int TaggedCount(ThingDef species)
        {
            if (species == null || HerdsDefOf.Herds_TrackingCollar == null) return 0;
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            int count = 0;
            for (int i = 0; i < pawns.Count; i++)
                if (pawns[i]?.def == species && pawns[i].health?.hediffSet?.GetFirstHediffOfDef(HerdsDefOf.Herds_TrackingCollar) != null) count++;
            count += roamingAnimals.Count(record => record?.species == species && record.tagged &&
                record.state != RoamingAnimalState.Present && record.state != RoamingAnimalState.Dead);
            return count;
        }

        public bool HasOperationalMonitor(WildlifeToolKind kind)
        {
            List<Building> buildings = map.listerBuildings.allBuildingsColonist;
            for (int i = 0; i < buildings.Count; i++)
                if (buildings[i] is Building_WildlifeTool tool && tool.Kind == kind && tool.Operational) return true;
            return false;
        }

        public string QualitativePopulation(RegionalSpeciesRecord record)
        {
            if (record.population < 2f) return "Rare";
            if (record.population < 7f) return "Scarce";
            if (record.population < 18f) return "Common";
            return "Abundant";
        }

        public string QualitativeNearbyPopulation(RegionalSpeciesRecord record)
        {
            if (record.nearbyPopulation < 1.5f) return "Rare";
            if (record.nearbyPopulation < 5f) return "Occasional";
            if (record.nearbyPopulation < 12f) return "Common";
            return "Abundant";
        }

        public string ApproximatePopulation(RegionalSpeciesRecord record)
        {
            float uncertainty = Mathf.Max(2f, record.population * Mathf.Lerp(0.45f, 0.12f, record.confidence));
            int low = Mathf.Max(0, Mathf.RoundToInt(record.population - uncertainty));
            int high = Mathf.Max(low + 1, Mathf.RoundToInt(record.population + uncertainty));
            return low + "–" + high;
        }

        public string ApproximateNearbyPopulation(RegionalSpeciesRecord record)
        {
            float uncertainty = Mathf.Max(1f, record.nearbyPopulation *
                Mathf.Lerp(0.40f, 0.10f, record.confidence));
            int low = Mathf.Max(record.lastLocalCount,
                Mathf.RoundToInt(record.nearbyPopulation - uncertainty));
            int high = Mathf.Max(low + 1,
                Mathf.RoundToInt(record.nearbyPopulation + uncertainty));
            return low + "-" + high;
        }

        public string NextExpectedReturn(ThingDef species)
        {
            RoamingAnimalRecord next = RoamersFor(species).Where(record =>
                record.state != RoamingAnimalState.Present && record.state != RoamingAnimalState.Dead)
                .OrderBy(record => record.expectedReturnTick).FirstOrDefault();
            if (next == null) return null;
            int remaining = next.expectedReturnTick - Find.TickManager.TicksGame;
            return remaining <= 0 ? "return possible now" :
                "possible return in " + remaining.ToStringTicksToPeriod();
        }

        public string DiseaseRisk(RegionalSpeciesRecord record)
        {
            float risk = (1f - habitatQuality) * 0.45f + (record.consequenceState == 2 ? 0.35f : 0f) + (record.population > 35f ? 0.15f : 0f);
            return risk < 0.25f ? "Low" : risk < 0.55f ? "Moderate" : "High";
        }

        public string InterventionSummary(RegionalSpeciesRecord record)
        {
            if (record.policy > 0) return "Encouragement raises inward migration and local pressure.";
            if (record.policy < 0) return "Discouragement reduces arrivals and may push animals outward.";
            if (record.consequenceState == 2) return "More habitat or lower hunting restrictions should reduce crowding risk.";
            if (record.consequenceState == 1 || record.consequenceState == 3) return "Protection and habitat support should improve recovery.";
            return "Current habitat and population pressure are broadly sustainable.";
        }

        public void CyclePolicy(RegionalSpeciesRecord record)
        {
            if (record == null || !WildlifeProgression.Unlocked(WildlifeCapability.Stewardship)) return;
            record.policy = record.policy >= 1 ? -1 : record.policy + 1;
        }

        public string PolicyLabel(RegionalSpeciesRecord record) => record.policy > 0 ? "Encourage" : record.policy < 0 ? "Discourage" : "Neutral";

        public AnimalRelationshipRecord RelationshipFor(Pawn pawn) => pawn == null ? null : relationships.FirstOrDefault(record => record.animal == pawn);

        public int OffspringCount(Pawn pawn) => pawn == null ? 0 : relationships.Count(record => record.parent == pawn);

        public string CarcassInfo(Corpse corpse)
        {
            if (corpse == null || !carcassOwners.TryGetValue(corpse, out Pawn owner) || owner == null || owner.Dead) return null;
            string result = "Claimed by: " + owner.LabelShortCap;
            if (carcassCaches.TryGetValue(corpse, out IntVec3 cache)) result += "\nCache destination: " + cache;
            return result;
        }

        public string PopulationStatus(RegionalSpeciesRecord record) => record.consequenceState == 1 ? "Regionally scarce" : record.consequenceState == 2 ? "Overpopulated" : record.consequenceState == 3 ? "Locally depleted" : "Balanced";

        public string Forecast(RegionalSpeciesRecord record)
        {
            float pressure = MigrationPressure(record);
            if (pressure > 1.5f) return "Likely arrival";
            if (pressure > 0.35f) return "Possible arrival";
            if (pressure < -1.2f) return "Likely departure";
            if (pressure < -0.25f) return "Possible departure";
            return "Stable";
        }

        public void ApplyExpeditionImpact(ThingDef species, float populationDelta, float confidenceGain)
        {
            if (species?.race?.Animal != true) return;
            RegionalSpeciesRecord record = records.FirstOrDefault(item => item.species == species);
            if (record == null)
            {
                record = new RegionalSpeciesRecord { species = species, population = Mathf.Max(0f, populationDelta), previousPopulation = 0f, confidence = 0f };
                record.nearbyPopulation = Mathf.Max(0f, populationDelta * 0.3f);
                record.previousNearbyPopulation = record.nearbyPopulation;
                EnsureNeighbors(record);
                records.Add(record);
            }
            record.population = Mathf.Clamp(record.population + populationDelta, 0f, 250f);
            record.confidence = Mathf.Clamp01(record.confidence + confidenceGain);
            record.lastUpdateTick = Find.TickManager?.TicksGame ?? record.lastUpdateTick;
        }

        public void NotifyFearEmigration(Pawn animal, IntVec3 edge)
        {
            if (HerdsMod.Settings?.enableRegionalPopulations != true ||
                animal?.def?.race?.Animal != true || animal.Faction != null) return;
            EnsureCurrent();
            QueueDeparture(animal, "Fled the map after being scared", edge);
            lastEmigrationTick = Find.TickManager?.TicksGame ?? 0;
            lastMigrant = animal;
            lastMigrationEdge = edge;
            migrationVisualUntil = lastEmigrationTick + 5000;
            WildlifeTestLog.Count("regional.fearEmigration");
            if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("FearEmigration",
                "species=" + animal.def.defName + " queued=true", animal);
        }

        public List<string> DebugOverviewLines()
        {
            EnsureCurrent();
            int learners = juvenileLearning.Count(pair => pair.Key?.Spawned == true && !pair.Key.ageTracker.Adult);
            List<string> lines = new List<string> { "REGIONAL habitat=" + habitatQuality.ToString("0.00") + " species=" + records.Count + " roaming=" + RoamingCount + " immigrationTick=" + lastImmigrationTick + " emigrationTick=" + lastEmigrationTick + " learners=" + learners + " relationships=" + relationships.Count + " carcassClaims=" + carcassOwners.Count + " scavengeOrders=" + scavengingOrders,
                "SEASONAL " + (ActiveSeasonalEvent ?? "none") };
            for (int i = 0; i < records.Count; i++) lines.Add("REGION " + records[i].species.LabelCap + " | nearby=" + records[i].nearbyPopulation.ToString("0.0") + " present=" + records[i].lastLocalCount + " regional=" + records[i].population.ToString("0.0") + " | " + Forecast(records[i]) + " | policy=" + PolicyLabel(records[i]) + " confidence=" + records[i].confidence.ToStringPercent());
            return lines;
        }

        public void DebugRunRegionalDay()
        {
            int now = Find.TickManager?.TicksGame ?? 0;
            if (HerdsMod.Settings.enableRegionalPopulations) UpdateRegional(now, true);
            if (HerdsMod.Settings.enableJuvenileLearning) UpdateLearning(now);
            if (HerdsMod.Settings.enableScavenging) UpdateScavengers(now);
            if (HerdsMod.Settings.enableAnimalRelationships) UpdateRelationships(now);
            Messages.Message("Regional ecology, learning, and scavenging tests advanced and logged.", MessageTypeDefOf.NeutralEvent, false);
        }

        public void DebugForceEvent()
        {
            TryWildlifeEvent(Find.TickManager?.TicksGame ?? 0, true);
        }

        public bool DebugSendRoaming(Pawn animal)
        {
            if (animal?.Spawned != true || animal.Faction != null ||
                animal.RaceProps?.Animal != true) return false;
            map.GetComponent<NotableWildlifeMapComponent>()?.MakeNotable(animal, true);
            if (!RCellFinder.TryFindRandomPawnEntryCell(out IntVec3 edge, map,
                CellFinder.EdgeRoadChance_Animal) ||
                !animal.CanReach(edge, PathEndMode.OnCell, Danger.Deadly)) return false;
            QueueDeparture(animal, "DEV roaming test", edge);
            Job leave = JobMaker.MakeJob(JobDefOf.Goto, edge);
            leave.exitMapOnArrival = true;
            leave.expiryInterval = 12000;
            animal.jobs.StartJob(leave, JobCondition.InterruptForced);
            return true;
        }

        public bool DebugReturnRoamer()
        {
            RoamingAnimalRecord record = roamingAnimals.FirstOrDefault(value =>
                value?.animal != null && !value.animal.Spawned &&
                value.state != RoamingAnimalState.Dead);
            if (record == null) return false;
            record.earliestReturnTick = 0;
            record.expectedReturnTick = 0;
            record.encouragedUntilTick = Find.TickManager.TicksGame + 60000;
            for (int i = 0; i < 12; i++) if (TryReturnRoamer(Find.TickManager.TicksGame)) return true;
            return false;
        }

        private void EnsureCurrent()
        {
            if (records.Count == 0 && HerdsMod.Settings.enableRegionalPopulations) UpdateRegional(Find.TickManager?.TicksGame ?? 0, false);
        }

        private RegionalSpeciesRecord RecordFor(ThingDef species, bool create)
        {
            RegionalSpeciesRecord record = records.FirstOrDefault(item => item.species == species);
            if (record != null || !create) return record;
            float seed = Mathf.Max(3f, PositiveMod(species.shortHash + map.uniqueID, 9));
            record = new RegionalSpeciesRecord
            {
                species = species,
                population = seed * 2f,
                previousPopulation = seed * 2f,
                nearbyPopulation = seed,
                previousNearbyPopulation = seed,
                confidence = 0.03f
            };
            EnsureNeighbors(record);
            records.Add(record);
            return record;
        }

        private bool ShouldPersist(Pawn animal)
        {
            if (animal == null) return false;
            if (map.GetComponent<WildlifeTrailMapComponent>()?.Retains(animal) == true ||
                map.GetComponent<WildlifeFieldJournalMapComponent>()?.ReferencesAnimal(animal) == true)
                return true;
            if (map.GetComponent<NotableWildlifeMapComponent>()?.For(animal) != null) return true;
            if (HerdsDefOf.Herds_TrackingCollar != null &&
                animal.health?.hediffSet?.GetFirstHediffOfDef(HerdsDefOf.Herds_TrackingCollar) != null) return true;
            if (animal.Name != null && !animal.Name.Numerical) return true;
            WildlifeMemoryMapComponent memory = map.GetComponent<WildlifeMemoryMapComponent>();
            if (memory?.Memories.Any(value => value?.animal == animal &&
                (value.events?.Count ?? 0) > 0) == true) return true;
            return map.GetComponent<WildlifeLivesMapComponent>()?.EscapeCount(animal) > 1;
        }

        private void RegisterRoamingDeparture(Pawn animal, string reason)
        {
            int now = Find.TickManager.TicksGame;
            RoamingAnimalRecord roaming = roamingAnimals.FirstOrDefault(record => record.animal == animal);
            if (roaming == null)
            {
                roaming = new RoamingAnimalRecord { animal = animal, species = animal.def };
                roamingAnimals.Add(roaming);
            }
            bool tagged = HerdsDefOf.Herds_TrackingCollar != null &&
                animal.health?.hediffSet?.GetFirstHediffOfDef(HerdsDefOf.Herds_TrackingCollar) != null;
            bool notable = map.GetComponent<NotableWildlifeMapComponent>()?.For(animal) != null;
            float trust = map.GetComponent<WildlifeMemoryMapComponent>()?.Memories
                .Where(value => value?.animal == animal).Select(value => value.trust)
                .DefaultIfEmpty().Max() ?? 0f;
            float fear = map.GetComponent<WildlifeMemoryMapComponent>()?.Memories
                .Where(value => value?.animal == animal).Select(value => value.fear + value.hostility)
                .DefaultIfEmpty().Max() ?? 0f;
            int variation = PositiveMod(animal.thingIDNumber * 31 + now / 60000, 180000);
            int duration = 90000 + variation + Mathf.RoundToInt(fear * 120000f - trust * 60000f);
            AnimalPersonalityRecord personality = map.GetComponent<WildlifeLivesMapComponent>()?.For(animal);
            if (personality?.personality == AnimalPersonality.Curious) duration -= 30000;
            if (personality?.personality == AnimalPersonality.Cautious) duration += 45000;
            duration = Mathf.Clamp(duration, 60000, 420000);
            roaming.reason = reason;
            roaming.direction = DirectionFor(animal.Position);
            roaming.leftTick = now;
            roaming.lastSeenTick = now;
            roaming.earliestReturnTick = now + Mathf.Min(60000, duration / 2);
            roaming.expectedReturnTick = now + duration;
            roaming.tagged = tagged;
            roaming.notable = notable;
            roaming.herdId = pendingDepartureHerds.TryGetValue(animal, out int herdId) ? herdId : 0;
            roaming.state = reason.IndexOf("scared", StringComparison.OrdinalIgnoreCase) >= 0 ||
                reason.IndexOf("flee", StringComparison.OrdinalIgnoreCase) >= 0
                    ? RoamingAnimalState.Displaced
                    : GenLocalDate.Season(map) == Season.Fall
                        ? RoamingAnimalState.SeasonalMigration : RoamingAnimalState.RoamingNearby;
            RegionalSpeciesRecord species = RecordFor(animal.def, true);
            species.lastLocalCount = CountLocalWildlife(animal.def, animal);
            species.nearbyPopulation = Mathf.Max(species.nearbyPopulation, species.lastLocalCount + 1);
            lastEmigrationTick = now;
            string text = animal.LabelShortCap + " left the colony map and is now " +
                StatePhrase(roaming.state) + " to the " + roaming.direction.ToLowerInvariant() + ".";
            if (HerdsMod.Settings.enableWildlifeAlerts && (notable || tagged))
                Messages.Message(text, animal, MessageTypeDefOf.NeutralEvent, false);
            WildlifeExperience.Record("Roaming Wildlife", text, animal);
            if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("RoamingDeparture",
                "state=" + roaming.state + " direction=" + roaming.direction +
                " expected=" + roaming.expectedReturnTick, animal);
        }

        private void UpdateLocalRoaming(int now)
        {
            nextLocalRoamingTick = now + 15000;
            RefreshToolCounts();
            if (HerdsMod.Settings.enablePersistentRoamingAnimals && TryReturnRoamer(now)) return;
            WildlifeTrailMapComponent trails = map.GetComponent<WildlifeTrailMapComponent>();
            WildlifeFieldJournalMapComponent journal =
                map.GetComponent<WildlifeFieldJournalMapComponent>();
            Dictionary<ThingDef, List<Pawn>> local = map.mapPawns.AllPawnsSpawned
                .Where(pawn => pawn?.Faction == null && pawn.RaceProps?.Animal == true &&
                    !pawn.Dead && !pawn.Downed && !pawn.InMentalState &&
                    trails?.Retains(pawn) != true &&
                    journal?.ReferencesAnimal(pawn) != true)
                .GroupBy(pawn => pawn.def).ToDictionary(group => group.Key, group => group.ToList());
            RegionalSpeciesRecord departure = records.Where(record =>
                local.TryGetValue(record.species, out List<Pawn> present) && present.Count > 1)
                .OrderByDescending(record =>
                {
                    int present = local[record.species].Count;
                    float desired = Mathf.Clamp(record.nearbyPopulation * 0.28f, 1f, 14f);
                    return present - desired;
                }).FirstOrDefault();
            if (departure != null)
            {
                int present = local[departure.species].Count;
                float desired = Mathf.Clamp(departure.nearbyPopulation * 0.28f, 1f, 14f);
                float chance = present > desired ? 0.58f : 0.14f;
                float landmarkAttraction = map.GetComponent<WildlifeLandmarkMapComponent>()?
                    .MigrationAttraction(departure.species) ?? 0f;
                chance = Mathf.Clamp01(chance - landmarkAttraction * 0.12f);
                if (Rand.Chance(chance))
                {
                    Pawn animal = local[departure.species]
                        .OrderBy(pawn => pawn.Position.DistanceToSquared(map.Center)).Last();
                    if (RCellFinder.TryFindRandomPawnEntryCell(out IntVec3 edge, map,
                        CellFinder.EdgeRoadChance_Animal) &&
                        animal.CanReach(edge, PathEndMode.OnCell, Danger.Deadly))
                    {
                        QueueDeparture(animal, "Roaming through its wider home range", edge);
                        Job leave = JobMaker.MakeJob(JobDefOf.Goto, edge);
                        leave.exitMapOnArrival = true;
                        leave.expiryInterval = 12000;
                        animal.jobs.StartJob(leave, JobCondition.InterruptForced);
                        return;
                    }
                }
            }
            RegionalSpeciesRecord arrival = records.Where(record => record.nearbyPopulation >= 1f)
                .OrderByDescending(record =>
                {
                    int present = local.TryGetValue(record.species, out List<Pawn> pawns) ? pawns.Count : 0;
                    return Mathf.Clamp(record.nearbyPopulation * 0.28f, 1f, 14f) - present;
                }).FirstOrDefault();
            if (arrival != null)
            {
                int present = local.TryGetValue(arrival.species, out List<Pawn> pawns) ? pawns.Count : 0;
                float desired = Mathf.Clamp(arrival.nearbyPopulation * 0.28f, 1f, 14f);
                float landmarkAttraction = map.GetComponent<WildlifeLandmarkMapComponent>()?
                    .MigrationAttraction(arrival.species) ?? 0f;
                if (present + 0.5f < desired && CanAddRegionalArrival(arrival, now) &&
                    Rand.Chance(Mathf.Clamp01(0.38f + landmarkAttraction * 0.12f)))
                    SpawnOrdinaryArrival(arrival, now);
            }
        }

        private bool TryReturnRoamer(int now)
        {
            List<RoamingAnimalRecord> candidates = roamingAnimals.Where(record =>
                record?.animal != null && !record.animal.Dead && !record.animal.Spawned &&
                record.state != RoamingAnimalState.Present && record.state != RoamingAnimalState.Dead &&
                now >= record.earliestReturnTick).OrderBy(record => record.expectedReturnTick).ToList();
            for (int i = 0; i < candidates.Count; i++)
            {
                RoamingAnimalRecord record = candidates[i];
                if (record.state == RoamingAnimalState.SeasonalMigration &&
                    GenLocalDate.Season(map) == Season.Winter) continue;
                RegionalSpeciesRecord species = RecordFor(record.species, true);
                float chance = now >= record.expectedReturnTick ? 0.48f : 0.12f;
                chance += species.policy * 0.10f + habitatQuality * 0.08f;
                chance += map.GetComponent<WildlifeLandmarkMapComponent>()?
                    .ReturnChanceModifier(record.species) ?? 0f;
                chance += Mathf.Min(0.08f, cachedBait * 0.02f) +
                    Mathf.Min(0.08f, cachedWater * 0.025f) +
                    Mathf.Min(0.08f, cachedReserves * 0.02f) +
                    Mathf.Min(0.10f, cachedCorridors * 0.025f);
                chance += PredatorDeterrentReturnChanceModifier(record.species);
                if (now < record.encouragedUntilTick) chance += 0.30f;
                if (now < record.discouragedUntilTick) chance -= 0.42f;
                if (!Rand.Chance(Mathf.Clamp01(chance))) continue;
                if (!RCellFinder.TryFindRandomPawnEntryCell(out IntVec3 entry, map,
                    CellFinder.EdgeRoadChance_Animal)) continue;
                try
                {
                    List<RoamingAnimalRecord> returning = record.herdId == 0
                        ? new List<RoamingAnimalRecord> { record }
                        : roamingAnimals.Where(value => value?.herdId == record.herdId &&
                            IsValidReturnCandidate(value)).ToList();
                    for (int memberIndex = 0; memberIndex < returning.Count; memberIndex++)
                    {
                        RoamingAnimalRecord member = returning[memberIndex];
                        IntVec3 memberEntry = memberIndex == 0 ? entry :
                            CellFinder.RandomClosewalkCellNear(entry, map, 5);
                        if (Find.WorldPawns.Contains(member.animal)) Find.WorldPawns.RemovePawn(member.animal);
                        GenSpawn.Spawn(member.animal, memberEntry, map, Rot4.Random);
                        member.state = RoamingAnimalState.Present;
                        member.lastSeenTick = now;
                        member.returnCount++;
                    }
                    species.lastLocalCount = CountLocalWildlife(record.species);
                    lastImmigrationTick = now;
                    lastMigrant = record.animal;
                    lastMigrationEdge = entry;
                    migrationVisualUntil = now + 5000;
                    string text = (returning.Count > 1 ? returning.Count + " " + record.species.label +
                        " herd members" : record.animal.LabelShortCap.ToString()) + " returned from " +
                        record.direction.ToLowerInvariant() + " after " +
                        (now - record.leftTick).ToStringTicksToPeriod() + " away.";
                    Messages.Message(text, record.animal, MessageTypeDefOf.PositiveEvent, false);
                    WildlifeExperience.Record("Roaming Wildlife", text, record.animal);
                    WildlifeMemoryMapComponent memory =
                        map.GetComponent<WildlifeMemoryMapComponent>();
                    Pawn partner = memory?.RememberedPartners(record.animal)
                        .Where(value => value?.Spawned == true && value.Map == map)
                        .OrderBy(value => value.Position.DistanceToSquared(record.animal.Position))
                        .FirstOrDefault();
                    if (partner != null)
                        memory.RememberAnimal(record.animal, partner,
                            AnimalSocialMemoryKind.Reunited, 1.1f);
                    if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("RoamingReturn",
                        "returns=" + record.returnCount + " awayTicks=" + (now - record.leftTick),
                        record.animal);
                    return true;
                }

                catch (Exception exception)
                {
                    Log.Error("[Wildlife] Could not return roaming animal " +
                        record.animal.LabelShortCap + ": " + exception);
                }
            }
            return false;
        }

        private static bool IsValidReturnCandidate(RoamingAnimalRecord record) =>
            record?.animal != null && !record.animal.Dead && !record.animal.Spawned &&
            !record.animal.Downed && !record.animal.InMentalState && record.animal.Faction == null &&
            record.state != RoamingAnimalState.Present && record.state != RoamingAnimalState.Dead;

        private void SpawnOrdinaryArrival(RegionalSpeciesRecord record, int now)
        {
            PawnKindDef kind = DefDatabase<PawnKindDef>.AllDefsListForReading
                .FirstOrDefault(def => def.race == record.species);
            if (kind == null || !RCellFinder.TryFindRandomPawnEntryCell(out IntVec3 entry, map,
                CellFinder.EdgeRoadChance_Animal)) return;
            Pawn pawn = PawnGenerator.GeneratePawn(kind, null);
            GenSpawn.Spawn(pawn, entry, map, Rot4.Random);
            record.lastLocalCount = CountLocalWildlife(record.species);
            lastImmigrationTick = now;
            lastMigrant = pawn;
            lastMigrationEdge = entry;
            migrationVisualUntil = now + 5000;
            if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("LocalRangeArrival",
                "species=" + record.species.defName + " nearby=" +
                record.nearbyPopulation.ToString("0.0"), pawn);
        }

        private string DirectionFor(IntVec3 cell)
        {
            int west = cell.x;
            int east = map.Size.x - 1 - cell.x;
            int south = cell.z;
            int north = map.Size.z - 1 - cell.z;
            int minimum = Mathf.Min(Mathf.Min(west, east), Mathf.Min(south, north));
            return minimum == west ? "West" : minimum == east ? "East" :
                minimum == south ? "South" : "North";
        }

        public static string StatePhrase(RoamingAnimalState state) =>
            state == RoamingAnimalState.RoamingNearby ? "roaming nearby" :
            state == RoamingAnimalState.SeasonalMigration ? "following a seasonal migration" :
            state == RoamingAnimalState.Displaced ? "keeping away after being frightened" :
            state == RoamingAnimalState.Missing ? "of uncertain location" :
            state == RoamingAnimalState.Dead ? "dead" : "present";

        private void UpdateRegional(int now, bool allowMigration)
        {
            nextRegionalTick = now + 60000;
            Dictionary<ThingDef, int> local = new Dictionary<ThingDef, int>();
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn?.Spawned != true || pawn.Dead || pawn.Faction != null || pawn.RaceProps?.Animal != true) continue;
                local[pawn.def] = local.TryGetValue(pawn.def, out int count) ? count + 1 : 1;
            }
            if (!regionalSeeded)
            {
                foreach (PawnKindDef kind in map.Biome.AllWildAnimals)
                {
                    ThingDef species = kind?.race;
                    if (species?.race?.Animal != true || records.Any(record => record.species == species)) continue;
                    float commonality = map.Biome.CommonalityOfAnimal(kind);
                    float seed = Mathf.Clamp(3f + commonality * 12f + PositiveMod(species.shortHash + map.uniqueID, 5), 2f, 80f);
                    RegionalSpeciesRecord seeded = new RegionalSpeciesRecord
                    {
                        species = species,
                        population = seed,
                        previousPopulation = seed,
                        nearbyPopulation = Mathf.Max(2f, seed * 0.34f),
                        previousNearbyPopulation = Mathf.Max(2f, seed * 0.34f),
                        confidence = 0.02f
                    };
                    EnsureNeighbors(seeded); records.Add(seeded);
                }
                regionalSeeded = true;
            }
            foreach (KeyValuePair<ThingDef, int> pair in local)
            {
                RegionalSpeciesRecord record = records.FirstOrDefault(item => item.species == pair.Key);
                if (record == null)
                {
                    float seed = Mathf.Max(3f, pair.Value * 3f + PositiveMod(pair.Key.shortHash + map.uniqueID, 7));
                    records.Add(record = new RegionalSpeciesRecord
                    {
                        species = pair.Key,
                        population = seed,
                        previousPopulation = seed,
                        nearbyPopulation = Mathf.Max(pair.Value, seed * 0.34f),
                        previousNearbyPopulation = Mathf.Max(pair.Value, seed * 0.34f),
                        confidence = 0.05f
                    }); EnsureNeighbors(record);
                }
                record.lastLocalCount = pair.Value;
            }
            RefreshToolCounts();
            habitatQuality = CalculateHabitatQuality();
            TrySeasonalEvent(now);
            for (int i = 0; i < records.Count; i++)
            {
                RegionalSpeciesRecord record = records[i];
                EnsureNeighbors(record);
                record.previousPopulation = record.population;
                record.previousNearbyPopulation = record.nearbyPopulation;
                int localCount = local.TryGetValue(record.species, out int count) ? count : 0;
                record.lastLocalCount = localCount;
                bool predator = WildlifeSpeciesClassification.IsPredator(record.species);
                float capacity = predator ? Mathf.Max(2f, TotalRegionalPrey() * 0.09f) : 5f + habitatQuality * 30f;
                capacity *= 1f + record.policy * 0.18f;
                float growth = (capacity - record.population) * (predator ? 0.018f : 0.035f);
                record.population = Mathf.Clamp(record.population + growth + Rand.Range(-0.35f, 0.36f), 0f, 250f);
                float nearbyTarget = Mathf.Max(localCount, record.population *
                    Mathf.Lerp(0.14f, 0.32f, habitatQuality) * (1f + record.policy * 0.18f));
                if (record.nearbyPopulation <= 0f) record.nearbyPopulation = nearbyTarget;
                record.nearbyPopulation = Mathf.Clamp(Mathf.Lerp(record.nearbyPopulation,
                    nearbyTarget, 0.12f) + Rand.Range(-0.12f, 0.13f), localCount, 80f);
                for (int cell = 0; cell < 8; cell++)
                {
                    float desired = record.population / 8f * (0.72f + PositiveMod(record.species.shortHash + cell * 31 + now / 60000, 57) / 100f);
                    record.neighboringPopulations[cell] = Mathf.Max(0f, Mathf.Lerp(record.neighboringPopulations[cell], desired, 0.18f));
                }
                if (HerdsMod.Settings.enablePopulationConsequences)
                {
                    int oldState = record.consequenceState;
                    record.consequenceState = record.population < 1f ? 1 : record.lastLocalCount == 0 && record.population < 4f ? 3 : record.population > capacity * 1.35f ? 2 : 0;
                    float alertConfidence = HasOperationalMonitor(WildlifeToolKind.TelemetryStation) ? 0.15f : 0.35f;
                    if (oldState != record.consequenceState && HerdsMod.Settings.enableWildlifeAlerts && record.confidence >= alertConfidence)
                        Messages.Message(record.species.LabelCap + " population status: " + PopulationStatus(record) + ".", MessageTypeDefOf.NeutralEvent, false);
                    if (record.consequenceState == 2) record.population = Mathf.Max(capacity, record.population - Mathf.Max(0.4f, record.population * 0.025f));
                }
                int knowledge = HuntingKnowledgeMapComponent.ColonyLevel(record.species);
                record.confidence = Mathf.Clamp01(record.confidence + knowledge * 0.004f);
                record.lastUpdateTick = now;
            }
            if (HerdsMod.Settings.enableWildlifeEvents && now >= nextWildlifeEventTick) TryWildlifeEvent(now);
            if (allowMigration && HerdsMod.Settings.enableRegionalMigration) TryMigration(now);
            if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("RegionalUpdate", "species=" + records.Count + " habitat=" + habitatQuality.ToString("0.00"));
        }

        private float CalculateHabitatQuality()
        {
            if (!HerdsMod.Settings.enableHabitatEcology) return 0.5f;
            int plants = map.listerThings.ThingsInGroup(ThingRequestGroup.Plant).Count;
            float vegetation = Mathf.Clamp01(plants / Mathf.Max(1f, map.Size.x * map.Size.z * 0.12f));
            int reserves = cachedReserves; int bait = cachedBait;
            int restoration = HerdsMod.Settings.enableConservationActions ? cachedRestoration : 0;
            int water = HerdsMod.Settings.enableConservationActions ? cachedWater : 0;
            int burns = HerdsMod.Settings.enableConservationActions ? cachedBurns : 0;
            Season season = GenLocalDate.Season(map);
            float seasonal = season == Season.Winter ? -0.18f : season == Season.Spring ? 0.08f : season == Season.Fall ? -0.04f : 0.03f;
            float livingLandscape = HerdsMod.Settings.enableWildlifeLandscaping &&
                HerdsMod.Settings.enableLandscapeEffects
                    ? map.GetComponent<WildlifeLandscapeMapComponent>()?.HabitatBonus() ?? 0f
                    : 0f;
            return Mathf.Clamp01(0.15f + vegetation * 0.62f + seasonal +
                Mathf.Min(0.18f, reserves * 0.06f) +
                Mathf.Min(0.08f, bait * 0.025f) +
                Mathf.Min(0.16f, restoration * 0.04f) +
                Mathf.Min(0.12f, water * 0.04f) +
                Mathf.Min(0.08f, burns * 0.025f) + livingLandscape);
        }

        private void RefreshToolCounts()
        {
            cachedReserves = cachedBait = cachedRestoration = cachedWater = cachedBurns = cachedCorridors = cachedDeterrents = 0;
            List<Building> buildings = map.listerBuildings.allBuildingsColonist;
            for (int i = 0; i < buildings.Count; i++)
            {
                Building building = buildings[i];
                if (building?.def == null || building is Building_WildlifeTool tool && !tool.active) continue;
                string name = building.def.defName;
                if (name == "Herds_WildlifeReserve" && WildlifeProgression.Unlocked(WildlifeCapability.Stewardship)) cachedReserves += RadiusWeight(building, 55f);
                else if (name == "Herds_WildlifeBait" && WildlifeProgression.Unlocked(WildlifeCapability.FeedingGrounds)) cachedBait++;
                else if (name == "Herds_HabitatRestoration" && WildlifeProgression.Unlocked(WildlifeCapability.TreeHabitat)) cachedRestoration += RadiusWeight(building, 35f);
                else if (name == "Herds_WildlifeWaterSource" && WildlifeProgression.Unlocked(WildlifeCapability.HabitatSupport)) cachedWater++;
                else if (name == "Herds_ManagedBurnMarker" && WildlifeProgression.Unlocked(WildlifeCapability.ManagedBurns)) cachedBurns += RadiusWeight(building, 12f);
                else if (name == "Herds_MigrationCorridor" && WildlifeProgression.Unlocked(WildlifeCapability.Stewardship)) cachedCorridors += RadiusWeight(building, 45f);
                else if (name == "Herds_PredatorDeterrent") cachedDeterrents++;
            }
        }

        private static int RadiusWeight(Building building, float defaultRadius)
        {
            if (building is not Building_WildlifeTool tool) return 1;
            float ratio = tool.InfluenceRadius / defaultRadius;
            return Mathf.Clamp(Mathf.RoundToInt(ratio * ratio), 1, 4);
        }

        private float TotalRegionalPrey()
        {
            float total = 0f;
            for (int i = 0; i < records.Count; i++) if (records[i].species?.race?.predator != true) total += records[i].population;
            return total;
        }

        private float MigrationPressure(RegionalSpeciesRecord record)
        {
            float attraction = habitatQuality * 2f + (WildlifeProgression.Unlocked(WildlifeCapability.Stewardship) ? record.policy * 0.8f : 0f);
            attraction += map.GetComponent<WildlifeLandmarkMapComponent>()?
                .MigrationAttraction(record.species) ?? 0f;
            if (HerdsMod.Settings.enableWildlifeLandscaping &&
                HerdsMod.Settings.enableLandscapeEffects)
                attraction += map.GetComponent<WildlifeLandscapeMapComponent>()?
                    .MigrationAttraction(record.species) ?? 0f;
            if (HerdsMod.Settings.enableConservationActions && WildlifeProgression.Unlocked(WildlifeCapability.Stewardship)) attraction += Mathf.Min(0.8f, cachedCorridors * 0.2f);
            attraction += PredatorDeterrentMigrationAttractionModifier(record.species);
            return attraction + (record.population - record.lastLocalCount * 3f) * 0.06f - record.lastLocalCount * 0.08f;
        }

        private void EnsureNeighbors(RegionalSpeciesRecord record)
        {
            if (record.neighboringPopulations == null) record.neighboringPopulations = new List<float>();
            while (record.neighboringPopulations.Count < 8)
            {
                int cell = record.neighboringPopulations.Count;
                float variation = 0.7f + PositiveMod((record.species?.shortHash ?? 0) + cell * 43 + map.uniqueID, 61) / 100f;
                record.neighboringPopulations.Add(Mathf.Max(0f, record.population / 8f * variation));
            }
            if (record.neighboringPopulations.Count > 8) record.neighboringPopulations.RemoveRange(8, record.neighboringPopulations.Count - 8);
        }

        private void TryWildlifeEvent(int now, bool force = false)
        {
            nextWildlifeEventTick = now + 120000 + PositiveMod(map.uniqueID * 977 + now / 60000 * 613, 120000);
            if (records.Count == 0 || (!force && !Rand.Chance(0.48f))) return;
            RegionalSpeciesRecord record = records[PositiveMod(map.uniqueID + now / 60000 * 17, records.Count)];
            int kind = PositiveMod(record.species.shortHash + now / 60000, 5);
            string text;
            if (kind == 0) { record.population *= 1.12f; text = "A strong nesting season is increasing " + record.species.LabelCap + " numbers."; }
            else if (kind == 1) { record.population *= 0.84f; text = "Disease is reducing the regional " + record.species.LabelCap + " population."; }
            else if (kind == 2) { record.population += WildlifeSpeciesClassification.IsPredator(record.species) ? 3f : 6f; text = "A territorial movement is pushing " + record.species.LabelCap + " toward this area."; }
            else if (kind == 3) { record.confidence = Mathf.Clamp01(record.confidence + 0.3f); text = "Surveyors report a rare sighting of " + record.species.LabelCap + "."; }
            else { record.population += 4f; record.policy = Mathf.Max(0, record.policy); text = "A seasonal migration wave of " + record.species.LabelCap + " is moving through neighboring cells."; }
            float eventConfidence = HasOperationalMonitor(WildlifeToolKind.TelemetryStation) ? 0.08f : 0.2f;
            if (HerdsMod.Settings.enableWildlifeAlerts && (record.confidence >= eventConfidence || kind == 3)) Messages.Message(text, MessageTypeDefOf.NeutralEvent, false);
            WildlifeExperience.Record("Regional Ecology", text, null, kind == 1);
            if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("RegionalEvent", "kind=" + kind + " species=" + record.species.defName + " population=" + record.population.ToString("0.0"));
        }

        private void TrySeasonalEvent(int now)
        {
            int season = (int)GenLocalDate.Season(map);
            if (lastSeason < 0)
            {
                lastSeason = season;
                return;
            }
            if (season == lastSeason || HerdsMod.Settings.enableWildlifeEvents != true ||
                HerdsMod.Settings.enableSeasonalEcologyEvents != true || records.Count == 0) return;
            lastSeason = season;
            Season current = (Season)season;
            RegionalSpeciesRecord prey = records
                .Where(record => record?.species?.race?.predator != true)
                .OrderByDescending(record => record.population)
                .FirstOrDefault();
            RegionalSpeciesRecord predator = records
                .Where(record => record?.species?.race?.predator == true)
                .OrderByDescending(record => record.population)
                .FirstOrDefault();
            RegionalSpeciesRecord focus = prey ?? predator ?? records[0];
            bool negative = false;
            if (current == Season.Spring)
            {
                focus.population = Mathf.Min(250f, focus.population * 1.16f + 2f);
                activeSeasonalEvent = "Breeding season: " + focus.species.LabelCap + " numbers are rising.";
            }
            else if (current == Season.Summer)
            {
                if (habitatQuality < 0.42f)
                {
                    focus.population *= 0.88f;
                    for (int i = 0; i < focus.neighboringPopulations.Count; i++)
                        focus.neighboringPopulations[i] += focus.population * 0.025f;
                    activeSeasonalEvent = "Summer scarcity: " + focus.species.LabelCap + " are dispersing toward neighboring regions.";
                    negative = true;
                }
                else
                {
                    focus.population = Mathf.Min(250f, focus.population + 3f);
                    activeSeasonalEvent = "Summer abundance: healthy habitat is supporting " + focus.species.LabelCap + ".";
                }
            }
            else if (current == Season.Fall)
            {
                focus.population = Mathf.Min(250f, focus.population + 6f);
                for (int i = 0; i < focus.neighboringPopulations.Count; i++)
                    focus.neighboringPopulations[i] += 1.5f;
                activeSeasonalEvent = "Autumn migration: " + focus.species.LabelCap + " are moving through the region.";
            }
            else
            {
                focus.population *= 0.90f;
                if (predator != null) predator.population = Mathf.Min(250f, predator.population + 1.5f);
                activeSeasonalEvent = "Winter pressure: scarce forage is reducing " + focus.species.LabelCap +
                    " numbers" + (predator == null ? "." : " while predators range more widely.");
                negative = true;
            }
            int protectedNotables = map.GetComponent<NotableWildlifeMapComponent>()?.ProtectedCount(focus.species) ?? 0;
            if (protectedNotables > 0)
            {
                focus.population = Mathf.Min(250f, focus.population + protectedNotables * 1.5f);
                activeSeasonalEvent += " Colony protection of a notable animal is helping preserve this population.";
                negative = false;
            }
            focus.confidence = Mathf.Clamp01(focus.confidence + 0.08f);
            activeSeasonalEventUntil = now + 90000;
            if (HerdsMod.Settings.enableWildlifeAlerts)
                Find.LetterStack.ReceiveLetter("Seasonal Wildlife", activeSeasonalEvent,
                    negative ? LetterDefOf.NegativeEvent : LetterDefOf.NeutralEvent);
            WildlifeExperience.Record("Seasonal Ecology", activeSeasonalEvent, null, negative);
            if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("SeasonalEcology",
                "season=" + current + " species=" + focus.species.defName + " population=" + focus.population.ToString("0.0"));
        }

        private void TryMigration(int now)
        {
            if (records.Count == 0) return;
            RegionalSpeciesRecord arrival = records.Where(record => record.population >= 1f && record.lastLocalCount < 40).OrderByDescending(MigrationPressure).FirstOrDefault();
            if (arrival != null && CanAddRegionalArrival(arrival, now) && MigrationPressure(arrival) > 0.3f && Rand.Chance(Mathf.Clamp01(MigrationPressure(arrival) * 0.12f)))
            {
                PawnKindDef kind = DefDatabase<PawnKindDef>.AllDefsListForReading.FirstOrDefault(def => def.race == arrival.species);
                if (kind != null && RCellFinder.TryFindRandomPawnEntryCell(out IntVec3 cell, map, CellFinder.EdgeRoadChance_Animal))
                {
                    Pawn pawn = PawnGenerator.GeneratePawn(kind, null);
                    GenSpawn.Spawn(pawn, cell, map, Rot4.Random);
                    if (HerdsMod.Settings.enableConservationActions)
                    {
                        Building_WildlifeTool corridor = ClosestActiveTool("Herds_MigrationCorridor", cell);
                        if (corridor != null && pawn.CanReach(corridor.Position, PathEndMode.OnCell, Danger.Deadly))
                        {
                            Job route = JobMaker.MakeJob(JobDefOf.Goto, CellFinder.RandomClosewalkCellNear(corridor.Position, map, 4)); route.expiryInterval = 4000;
                            pawn.jobs.StartJob(route, JobCondition.InterruptForced);
                        }
                    }
                    arrival.lastLocalCount = CountLocalWildlife(arrival.species);
                    lastImmigrationTick = now;
                    lastMigrant = pawn; lastMigrationEdge = cell; migrationVisualUntil = now + 5000;
                    float migrationConfidence = HasOperationalMonitor(WildlifeToolKind.TelemetryStation) ? 0.12f : 0.35f;
                    string migrationText = "A " + arrival.species.LabelCap + " migrated into the area.";
                    if (HerdsMod.Settings.enableWildlifeAlerts && arrival.confidence >= migrationConfidence) Messages.Message(migrationText, pawn, MessageTypeDefOf.NeutralEvent, false);
                    WildlifeExperience.Record("Migration", migrationText, pawn);
                    if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("RegionalImmigration", "species=" + arrival.species.defName + " pressure=" + MigrationPressure(arrival).ToString("0.00"), pawn);
                    return;
                }
            }
            RegionalSpeciesRecord departure = records.Where(record => record.lastLocalCount > 1).OrderBy(MigrationPressure).FirstOrDefault();
            if (departure == null || MigrationPressure(departure) > -0.2f || !Rand.Chance(Mathf.Clamp01(-MigrationPressure(departure) * 0.1f))) return;
            Pawn emigrant = map.mapPawns.AllPawnsSpawned.FirstOrDefault(pawn => pawn?.Spawned == true && !pawn.Dead && pawn.Faction == null && pawn.def == departure.species && !pawn.Downed && !pawn.InMentalState);
            if (emigrant == null || !RCellFinder.TryFindRandomPawnEntryCell(out IntVec3 edge, map, CellFinder.EdgeRoadChance_Animal) || !emigrant.CanReach(edge, PathEndMode.OnCell, Danger.Deadly)) return;
            Job leave = JobMaker.MakeJob(JobDefOf.Goto, edge); leave.exitMapOnArrival = true; leave.expiryInterval = 10000;
            QueueDeparture(emigrant, "Seasonal movement through the nearby region", edge);
            emigrant.jobs.StartJob(leave, JobCondition.InterruptForced);
            lastEmigrationTick = now;
            lastMigrant = emigrant; lastMigrationEdge = edge; migrationVisualUntil = now + 5000;
            WildlifeExperience.Record("Migration", emigrant.LabelShortCap + " emigrated from the area.", emigrant);
            if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("RegionalEmigration", "species=" + departure.species.defName + " pressure=" + MigrationPressure(departure).ToString("0.00"), emigrant);
        }

        private bool CanAddRegionalArrival(RegionalSpeciesRecord record, int now)
        {
            int lossTick = populationLossTicks.TryGetValue(record.species.defName, out int value) ? value : 0;
            return WildlifePopulationPolicy.CanAddLocalAnimal(now, lossTick,
                CountLocalWildlife(record.species), record.nearbyPopulation, record.population, false);
        }

        private int CountLocalWildlife(ThingDef species, Pawn excluded = null)
        {
            return map.mapPawns.AllPawnsSpawned.Count(pawn => pawn != excluded &&
                pawn?.Spawned == true && !pawn.Dead && pawn.Faction == null && pawn.def == species);
        }

        private void UpdateLearning(int now)
        {
            nextLearningTick = now + 2500;
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn young = pawns[i];
                if (young?.Spawned != true || young.Dead || young.RaceProps?.Animal != true || young.ageTracker.Adult) continue;
                bool teacher = false;
                for (int j = 0; j < pawns.Count; j++)
                {
                    Pawn adult = pawns[j];
                    if (adult?.Spawned == true && !adult.Dead && adult != young && adult.def == young.def && adult.ageTracker.Adult && adult.Faction == young.Faction && adult.Position.DistanceToSquared(young.Position) <= 144) { teacher = true; break; }
                }
                if (!teacher) continue;
                juvenileLearning[young] = Mathf.Clamp01((juvenileLearning.TryGetValue(young, out float learned) ? learned : 0f) + 0.035f);
            }
            foreach (Pawn stale in juvenileLearning.Keys.Where(pawn => pawn == null || pawn.Dead || pawn.Destroyed).ToList()) juvenileLearning.Remove(stale);
        }

        private void UpdateRelationships(int now)
        {
            nextRelationshipTick = now + 5000;
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            relationships.RemoveAll(record => record?.animal == null || record.animal.Dead || record.animal.Destroyed);
            Dictionary<Pawn, AnimalRelationshipRecord> index = new Dictionary<Pawn, AnimalRelationshipRecord>(relationships.Count);
            for (int i = 0; i < relationships.Count; i++) index[relationships[i].animal] = relationships[i];
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn animal = pawns[i];
                if (animal?.Spawned != true || animal.Dead || animal.RaceProps?.Animal != true) continue;
                if (!index.TryGetValue(animal, out AnimalRelationshipRecord record)) { relationships.Add(record = new AnimalRelationshipRecord { animal = animal }); index[animal] = record; }
                Pawn previousTeacher = record.teacher;
                Pawn previousParent = record.parent;
                Pawn previousMate = record.mate;
                Pawn previousRival = record.rival;
                Pawn nearestAdult = null, nearestMate = null, nearestRival = null; float adultDistance = 401f, mateDistance = 901f, rivalDistance = 626f;
                for (int j = 0; j < pawns.Count; j++)
                {
                    Pawn other = pawns[j];
                    if (other?.Spawned != true || other.Dead || other == animal || other.def != animal.def || other.Faction != animal.Faction || !other.ageTracker.Adult) continue;
                    float distance = other.Position.DistanceToSquared(animal.Position);
                    if (distance < adultDistance) { nearestAdult = other; adultDistance = distance; }
                    if (animal.ageTracker.Adult && other.gender != Gender.None && animal.gender != Gender.None && other.gender != animal.gender && distance < mateDistance) { nearestMate = other; mateDistance = distance; }
                    if (animal.ageTracker.Adult && WildlifeSpeciesClassification.IsPredator(animal.def) && other.gender == animal.gender && distance < rivalDistance) { nearestRival = other; rivalDistance = distance; }
                }
                if (!animal.ageTracker.Adult)
                {
                    if (nearestAdult != null)
                    {
                        record.teacher = nearestAdult;
                        if (record.parent == null) record.parent = nearestAdult;
                        if (record.teacher != previousTeacher)
                            WildlifeMemoryUtility.RememberAnimal(animal, record.teacher,
                                AnimalSocialMemoryKind.Taught, 0.75f);
                        if (record.parent != previousParent)
                            WildlifeMemoryUtility.RememberAnimal(animal, record.parent,
                                AnimalSocialMemoryKind.ParentCare, 1f);
                    }
                }
                else
                {
                    if ((record.mate == null || record.mate.Dead) && nearestMate != null)
                    {
                        record.mate = nearestMate;
                        if (!index.TryGetValue(nearestMate, out AnimalRelationshipRecord reciprocal)) { relationships.Add(reciprocal = new AnimalRelationshipRecord { animal = nearestMate }); index[nearestMate] = reciprocal; }
                        if (reciprocal.mate == null) reciprocal.mate = animal;
                        if (record.mate != previousMate)
                            WildlifeMemoryUtility.RememberAnimal(animal, record.mate,
                                AnimalSocialMemoryKind.MateBond, 1f);
                    }
                    if (WildlifeSpeciesClassification.IsPredator(animal.def))
                    {
                        record.rival = nearestRival;
                        if (record.rival != null && record.rival != previousRival)
                            WildlifeMemoryUtility.RememberAnimal(animal, record.rival,
                                AnimalSocialMemoryKind.Rivalry, 0.8f);
                    }
                }
                if (nearestAdult != null &&
                    PositiveMod(animal.thingIDNumber + now / 5000, 12) == 0)
                    WildlifeMemoryUtility.RememberAnimal(animal, nearestAdult,
                        AnimalSocialMemoryKind.TravelledTogether, 0.45f);
            }
        }

        private void UpdateScavengers(int now)
        {
            nextScavengeTick = now + 1200;
            foreach (Corpse stale in carcassOwners.Keys.Where(corpse => corpse == null || corpse.Destroyed || !corpse.Spawned || !carcassOwners.TryGetValue(corpse, out Pawn owner) || owner?.Spawned != true || owner.Dead).ToList()) { carcassOwners.Remove(stale); carcassCaches.Remove(stale); }
            if (HerdsMod.Settings.enableAdvancedScavenging)
            {
                foreach (KeyValuePair<Corpse, Pawn> claim in carcassOwners.ToList())
                {
                    Pawn owner = claim.Value;
                    if (owner?.Spawned != true || owner.Dead || owner.Downed || owner.InMentalState) continue;
                    Pawn intruder = map.mapPawns.AllPawnsSpawned.FirstOrDefault(pawn => pawn?.Spawned == true && pawn != owner && !pawn.Dead && pawn.Faction == null && pawn.RaceProps?.Animal == true && pawn.Position.DistanceToSquared(claim.Key.Position) <= 36 && WildlifeSpeciesClassification.IsPredator(pawn.def));
                    if (intruder != null && WildlifeSpeciesClassification.IsPredator(owner.def) && owner.kindDef.combatPower >= intruder.kindDef.combatPower * 0.75f && (owner.CurJobDef == null || owner.CurJobDef == JobDefOf.Wait_Wander))
                    {
                        Job defend = JobMaker.MakeJob(JobDefOf.AttackMelee, intruder); defend.maxNumMeleeAttacks = 1; defend.expiryInterval = 150;
                        owner.jobs.StartJob(defend, JobCondition.InterruptForced);
                        WildlifeMemoryUtility.RememberAnimal(owner, intruder,
                            AnimalSocialMemoryKind.Rivalry, 0.9f);
                        if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("CarcassDispute", "corpse=" + claim.Key.thingIDNumber, owner, intruder);
                    }
                }
            }
            Corpse corpse = null;
            List<Thing> things = map.listerThings.AllThings;
            for (int i = PositiveMod(now / 1200, 13); i < things.Count; i += 13) if (things[i] is Corpse found && found.Spawned && !found.IsForbidden(Faction.OfPlayer)) { corpse = found; break; }
            if (corpse == null) return;
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn?.Spawned != true || pawn.Dead || pawn.Downed || pawn.Faction != null || pawn.RaceProps?.Animal != true || (!WildlifeSpeciesClassification.IsPredator(pawn.def) && (pawn.RaceProps.foodType & FoodTypeFlags.Corpse) == 0) || pawn.needs?.food?.CurLevelPercentage > 0.58f) continue;
                if (!pawn.CanReserve(corpse) || !pawn.CanReach(corpse, PathEndMode.Touch, Danger.Deadly)) continue;
                if (HerdsMod.Settings.enableAdvancedScavenging && carcassOwners.TryGetValue(corpse, out Pawn owner) && owner != pawn && owner?.Dead == false) continue;
                if (pawn.CurJobDef != null && pawn.CurJobDef != JobDefOf.Wait_Wander && pawn.CurJobDef != JobDefOf.GotoWander) continue;
                if (HerdsMod.Settings.enableAdvancedScavenging)
                {
                    carcassOwners[corpse] = pawn;
                    if (WildlifeSpeciesClassification.IsPredator(pawn.def) && pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation) && TryFindCacheCell(pawn, corpse, out IntVec3 cache))
                    {
                        carcassCaches[corpse] = cache;
                        Job haul = JobMaker.MakeJob(JobDefOf.HaulToCell, corpse, cache); haul.expiryInterval = 5000;
                        pawn.jobs.StartJob(haul, JobCondition.InterruptForced);
                        scavengingOrders++;
                        if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("CarcassCache", "corpse=" + corpse.thingIDNumber + " cache=" + cache, pawn, corpse);
                        break;
                    }
                }
                Job job = JobMaker.MakeJob(JobDefOf.Ingest, corpse); job.expiryInterval = 5000;
                pawn.jobs.StartJob(job, JobCondition.InterruptForced);
                scavengingOrders++;
                if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("Scavenge", "corpse=" + corpse.LabelShortCap, pawn, corpse);
                break;
            }
        }

        private bool TryFindCacheCell(Pawn pawn, Corpse corpse, out IntVec3 cell)
        {
            cell = IntVec3.Invalid;
            if (corpse.Position.Roofed(map)) return false;
            int count = GenRadial.NumCellsInRadius(10f);
            for (int i = 0; i < count; i += 2)
            {
                IntVec3 candidate = pawn.Position + GenRadial.RadialPattern[i];
                if (!candidate.InBounds(map) || !candidate.Standable(map) || !candidate.Roofed(map) || candidate.IsForbidden(pawn) || !pawn.CanReach(candidate, PathEndMode.OnCell, Danger.Deadly)) continue;
                cell = candidate; return true;
            }
            return false;
        }

        private Building_WildlifeTool ClosestActiveTool(string defName, IntVec3 origin)
        {
            Building_WildlifeTool best = null; float bestDistance = float.MaxValue;
            List<Building> buildings = map.listerBuildings.allBuildingsColonist;
            for (int i = 0; i < buildings.Count; i++)
            {
                if (buildings[i] is not Building_WildlifeTool tool || !tool.active || tool.def.defName != defName) continue;
                float distance = tool.Position.DistanceToSquared(origin);
                if (distance < bestDistance) { best = tool; bestDistance = distance; }
            }
            return best;
        }

        private static int PositiveMod(int value, int modulus) { int result = value % modulus; return result < 0 ? result + modulus : result; }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.ExitMap))]
    public static class PersistentRoamingAnimalExitPatch
    {
        public static bool Prefix(Pawn __instance)
        {
            if (__instance?.Spawned != true || __instance.Faction != null ||
                __instance.RaceProps?.Animal != true) return true;
            Map map = __instance.Map;
            RegionalWildlifeMapComponent component = map?.GetComponent<RegionalWildlifeMapComponent>();
            if (component == null) return true;
            bool preserve = component.ShouldPreserveExit(__instance);
            map.GetComponent<WildlifeTrailMapComponent>()?
                .NotifyAnimalDeparture(__instance, __instance.Position);
            map.GetComponent<WildlifeFieldJournalMapComponent>()?
                .NotifyAnimalDeparture(__instance, __instance.Position);
            if (!preserve)
            {
                component.NotifyOrdinaryDeparture(__instance);
                return true;
            }
            try
            {
                __instance.DeSpawn(DestroyMode.Vanish);
                if (!Find.WorldPawns.Contains(__instance))
                    Find.WorldPawns.PassToWorld(__instance, PawnDiscardDecideMode.KeepForever);
                return false;
            }
            catch (Exception exception)
            {
                Log.Error("[Wildlife] Could not preserve roaming animal " +
                    __instance.LabelShortCap + ": " + exception);
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    public static class NearbyWildlifeDeathPatch
    {
        public static void Prefix(Pawn __instance)
        {
            if (__instance?.Spawned != true || __instance.Faction != null ||
                __instance.RaceProps?.Animal != true || __instance.Dead) return;
            __instance.Map?.GetComponent<RegionalWildlifeMapComponent>()
                ?.NotifyLocalDeath(__instance);
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.SpawnSetup))]
    public static class NearbyWildlifeSpawnPatch
    {
        public static void Postfix(Pawn __instance, Map map, bool respawningAfterLoad)
        {
            map?.GetComponent<RegionalWildlifeMapComponent>()
                ?.NotifyLocalSpawn(__instance, respawningAfterLoad);
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.SetFaction))]
    public static class NearbyWildlifeCapturePatch
    {
        public static void Prefix(Pawn __instance, Faction newFaction, ref Map __state)
        {
            __state = __instance?.Spawned == true && __instance.Faction == null &&
                newFaction == Faction.OfPlayer && __instance.RaceProps?.Animal == true ? __instance.Map : null;
        }

        public static void Postfix(Pawn __instance, Map __state)
        {
            __state?.GetComponent<RegionalWildlifeMapComponent>()?.NotifyLocalCapture(__instance);
        }
    }

    [HarmonyPatch(typeof(WildAnimalSpawner), "SpawnRandomWildAnimalAt")]
    public static class RegionalWildAnimalSpawnPatch
    {
        public static bool Prefix(Map ___map, PawnKindDef animalKind)
        {
            return ___map?.GetComponent<RegionalWildlifeMapComponent>()
                ?.CanSpawnWildAnimal(animalKind, false) ?? true;
        }
    }
    }

    public static class PersistentRoamingDebug
    {
        [DebugAction("Wildlife", "Send unique animal roaming",
            actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void SendRoaming()
        {
            Pawn animal = UI.MouseCell().GetFirstPawn(Find.CurrentMap);
            bool result = Find.CurrentMap?.GetComponent<RegionalWildlifeMapComponent>()
                ?.DebugSendRoaming(animal) == true;
            Messages.Message(result ? "Animal is leaving to roam nearby." :
                "Choose a reachable wild animal.", result
                    ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.RejectInput, false);
        }

        [DebugAction("Wildlife", "Return first roaming animal",
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void ReturnRoamer()
        {
            bool result = Find.CurrentMap?.GetComponent<RegionalWildlifeMapComponent>()
                ?.DebugReturnRoamer() == true;
            Messages.Message(result ? "Roaming animal returned." :
                "No persistent roaming animal could return.", result
                    ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.RejectInput, false);
        }
    }

    public sealed class Window_RegionalWildlife : Window
    {
        private readonly Map map;
        private Vector2 scroll;
        private Vector2 spatialScroll;
        private ThingDef selectedSpecies;
        public override Vector2 InitialSize => new Vector2(1080f, 680f);
        public Window_RegionalWildlife(Map map) { this.map = map; doCloseX = true; resizeable = true; absorbInputAroundWindow = true; }

        public override void DoWindowContents(Rect rect)
        {
            RegionalWildlifeMapComponent component = map?.GetComponent<RegionalWildlifeMapComponent>();
            if (component == null) { Widgets.Label(rect, "Local wildlife information is unavailable."); return; }
            bool hasStewardship = WildlifeProgression.Unlocked(WildlifeCapability.Stewardship);
            bool hasCameras = HerdsMod.Settings.enableCameraTraps && WildlifeProgression.Unlocked(WildlifeCapability.CameraMonitoring) && component.HasOperationalMonitor(WildlifeToolKind.CameraTrap);
            bool hasTelemetry = HerdsMod.Settings.enableTelemetry && WildlifeProgression.Unlocked(WildlifeCapability.Telemetry) && component.HasOperationalMonitor(WildlifeToolKind.TelemetryStation);
            bool hasEcology = HerdsMod.Settings.enableAppliedEcology && WildlifeProgression.Unlocked(WildlifeCapability.AppliedEcology);
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, rect.width, 32f), "Local Wildlife");
            Text.Font = GameFont.Small;
            GUI.color = new Color(0.72f, 0.78f, 0.72f);
            Widgets.Label(new Rect(0f, 29f, rect.width, 22f), "Daily estimates combine known animals, local sightings, habitat, and migration. Surveys improve confidence.");
            GUI.color = Color.white;

            string habitat = hasEcology
                ? component.HabitatQuality.ToStringPercent()
                : component.HabitatQuality < 0.35f ? "Poor" : component.HabitatQuality < 0.7f ? "Adequate" : "Healthy";
            float cardWidth = (rect.width - 16f) / 3f;
            string seasonal = component.ActiveSeasonalEvent;
            DrawHeaderCard(new Rect(0f, 56f, cardWidth, 66f), "Season",
                GenLocalDate.Season(map) + (seasonal.NullOrEmpty() ? "" : " • Active Event"),
                new Color(0.55f, 0.48f, 0.25f),
                "Season changes vegetation, habitat quality, reproduction, and migration." +
                (seasonal.NullOrEmpty() ? "" : "\n\n" + seasonal));
            DrawHeaderCard(new Rect(cardWidth + 8f, 56f, cardWidth, 66f), "Habitat", habitat,
                new Color(0.34f, 0.57f, 0.31f),
                "Habitat reflects vegetation, season, reserves, water, restoration, managed burns, and conserved wildlife-shaped Landscape features. Better habitat raises carrying capacity, reproduction, and return migration.\n\nImprove habitat by preserving vegetation and Landscape features, establishing reserves and migration corridors, and operating water or habitat-restoration structures. Degrade it by clearing vegetation, building through Landscape features, removing water access, or concentrating disruptive colony activity in wildlife areas.");
            Rect roamerCard = new Rect((cardWidth + 8f) * 2f, 56f, cardWidth, 66f);
            DrawHeaderCard(roamerCard,
                "Known Roamers", component.KnownRoamingCount.ToString(), new Color(0.30f, 0.48f, 0.58f),
                "Persistent notable, tagged, named, or remembered animals currently beyond the map. They retain health, personality, relationships, and memories and may return.\n\nClick to review known individuals.");
            if (component.KnownRoamingCount > 0 && Widgets.ButtonInvisible(roamerCard))
                Find.WindowStack.Add(new Window_RoamingWildlife(map));
            IReadOnlyList<RegionalSpeciesRecord> rows = VisibleRows(component);
            Rect speciesHeader = new Rect(8f, 132f, 160f, 24f);
            Rect populationHeader = new Rect(180f, 132f, 220f, 24f);
            Rect trendHeader = new Rect(410f, 132f, 105f, 24f);
            Rect outlookHeader = new Rect(525f, 132f, rect.width - 825f, 24f);
            Widgets.Label(speciesHeader, "Species");
            Widgets.Label(populationHeader, "Population");
            Widgets.Label(trendHeader, "Trend");
            Widgets.Label(outlookHeader, "Outlook");
            Rect confidenceHeader = new Rect(rect.width - 282f, 132f, 130f, 24f);
            Text.Anchor = TextAnchor.UpperCenter;
            Widgets.Label(confidenceHeader, "Confidence");
            Text.Anchor = TextAnchor.UpperLeft;
            if (hasStewardship) Widgets.Label(new Rect(rect.width - 142f, 132f, 134f, 24f), "Management");
            TooltipHandler.TipRegion(populationHeader, "The nearby population includes animals whose ranges overlap this map. Present animals are the currently visible subset.");
            TooltipHandler.TipRegion(trendHeader, "Change in the nearby population since the previous daily estimate.");
            TooltipHandler.TipRegion(outlookHeader, "Current ecological status and, when researched, the predicted direction of migration.");
            Rect outer = new Rect(0f, 158f, rect.width, rect.height - 158f); Rect view = new Rect(0f, 0f, outer.width - 18f, Mathf.Max(outer.height, rows.Count * 76f));
            Widgets.BeginScrollView(outer, ref scroll, view);
            for (int i = 0; i < rows.Count; i++)
            {
                RegionalSpeciesRecord record = rows[i]; int knowledge = HerdsMod.Settings.enableSpeciesKnowledgeProgression ? HuntingKnowledgeMapComponent.ColonyLevel(record.species) : 5;
                Rect row = new Rect(0f, i * 76f, view.width, 70f); Widgets.DrawMenuSection(row);
                Widgets.DrawHighlightIfMouseover(row);
                Widgets.Label(new Rect(row.x + 10f, row.y + 8f, 166f, 24f), record.species.LabelCap);
                GUI.color = new Color(0.72f, 0.78f, 0.72f);
                Widgets.Label(new Rect(row.x + 10f, row.y + 33f, 166f, 22f), HuntingKnowledgeMapComponent.LevelLabel(knowledge));
                GUI.color = Color.white;
                string estimate = knowledge < 1 ? "Nearby: Unknown" :
                    !hasStewardship ? "Nearby signs: " + component.QualitativeNearbyPopulation(record) :
                    "Nearby: " + (hasTelemetry ? "About " + Mathf.RoundToInt(record.nearbyPopulation) :
                        component.ApproximateNearbyPopulation(record)) + "\nPresent: " + record.lastLocalCount;
                string trend = knowledge < 2 || !hasStewardship ? "Uncertain" :
                    record.nearbyPopulation > record.previousNearbyPopulation + 0.2f ? "Increasing" :
                    record.nearbyPopulation < record.previousNearbyPopulation - 0.2f ? "Declining" : "Stable";
                string forecast = !hasEcology || knowledge < 3 ? null : "Forecast: " + component.Forecast(record);
                int tagged = hasTelemetry ? component.TaggedCount(record.species) : 0;
                if (tagged > 0) forecast += " | Tagged " + tagged;
                if (HerdsMod.Settings.enableDiseaseMonitoring && WildlifeProgression.Unlocked(WildlifeCapability.DiseaseMonitoring)) forecast = (forecast.NullOrEmpty() ? "" : forecast + "    •    ") + "Disease Risk: " + component.DiseaseRisk(record);
                int roamers = component.RoamersFor(record.species).Count(value =>
                    value.state != RoamingAnimalState.Present && value.state != RoamingAnimalState.Dead);
                string returnText = component.NextExpectedReturn(record.species);
                string outlook = (hasStewardship ? component.PopulationStatus(record) : "Basic field signs") +
                    (roamers > 0 ? "\n" + roamers + " known roaming" +
                        (returnText.NullOrEmpty() ? "" : " • " + returnText) :
                        forecast.NullOrEmpty() ? "" : "\n" + forecast);
                string traditionSummary = HerdsMod.Settings.enableAnimalTraditions
                    ? map.GetComponent<AnimalTraditionMapComponent>()?.RegionalSummary(record.species, knowledge) : null;
                string landmarkSummary = HerdsMod.Settings.enableColonyWildlifeLandmark
                    ? map.GetComponent<WildlifeLandmarkMapComponent>()?.Summary(record.species, knowledge) : null;
                Rect populationRect = new Rect(row.x + 180f, row.y + 10f, 220f, 48f);
                Rect trendRect = new Rect(row.x + 410f, row.y + 10f, 105f, 44f);
                Rect outlookRect = new Rect(row.x + 525f, row.y + 8f, row.width - 815f, 55f);
                Rect confidenceRect = new Rect(row.xMax - 282f, row.y + 10f, 130f, 44f);
                Widgets.Label(populationRect, estimate);
                Widgets.Label(trendRect, trend);
                Widgets.Label(outlookRect, outlook);
                Text.Anchor = TextAnchor.UpperCenter;
                Widgets.Label(confidenceRect, record.confidence.ToStringPercent());
                Text.Anchor = TextAnchor.UpperLeft;
                TooltipHandler.TipRegion(populationRect, "Nearby is the population whose home ranges overlap this map and its immediate surroundings. Present is the subset currently visible. Animals regularly move between these states without changing the nearby population.");
                TooltipHandler.TipRegion(trendRect, "Trend compares the current nearby population with the previous daily estimate. Habitat, hunting pressure, season, migration management, and the wider regional population affect it.");
                TooltipHandler.TipRegion(outlookRect, "Outlook summarizes population balance and, after Applied Ecology is researched, predicts arrivals or departures from habitat, population pressure, and management policy." +
                    (traditionSummary.NullOrEmpty() ? "" : "\n\nAnimal traditions: " + traditionSummary) +
                    (landmarkSummary.NullOrEmpty() ? "" : "\n\nColony reputation: " + landmarkSummary));
                TooltipHandler.TipRegion(confidenceRect,
                    "Confidence measures how reliable the colony's estimate is. Surveys" +
                    (hasCameras ? ", camera traps" : "") +
                    (hasTelemetry ? ", telemetry, and tracking collars" : "") +
                    " increase it. Confidence narrows population ranges and determines whether forecasts and population alerts are reported; it does not change the underlying animal population.");
                Rect policy = new Rect(row.xMax - 137f, row.y + 12f, 129f, 32f);
                bool managementRelevant = hasStewardship && (HerdsMod.Settings.enableRegionalMigration || HerdsMod.Settings.enableHuntingRegulations || HerdsMod.Settings.enableWildlifeSteward);
                if (managementRelevant && knowledge >= 3)
                {
                    string manageLabel = record.policy > 0 ? "Encourage" : record.policy < 0 ? "Discourage" : "Manage";
                    if (Widgets.ButtonText(policy, manageLabel)) ShowManagement(component, record);
                    TooltipHandler.TipRegion(policy, "Manage migration, hunting regulations, quotas, and conservation goals for this species.");
                }
                else if (managementRelevant)
                {
                    GUI.color = new Color(0.68f, 0.68f, 0.68f);
                    Widgets.Label(policy, "Requires Studied");
                    GUI.color = Color.white;
                    TooltipHandler.TipRegion(policy, "Reach Studied Animal Knowledge for this species to unlock management.");
                }
                Rect speciesActions = new Rect(row.x, row.y, row.width -
                    (managementRelevant ? 148f : 0f), row.height);
                if (Widgets.ButtonInvisible(speciesActions))
                    ShowSpeciesActions(component, record, knowledge, managementRelevant);
                TooltipHandler.TipRegion(speciesActions,
                    "Click for local animals, Animal Knowledge, roaming individuals, and available management actions.");
            }
            Widgets.EndScrollView();
            if (rows.Count == 0) Widgets.Label(new Rect(8f, 168f, rect.width - 16f, 50f), "No known species are currently present in the region. Observe local wildlife to begin identifying regional populations.");
        }

        private IReadOnlyList<RegionalSpeciesRecord> VisibleRows(RegionalWildlifeMapComponent component) =>
            component.Records
                .Where(record => record?.species != null && record.population > 0.05f)
                .Where(record => !HerdsMod.Settings.enableSpeciesKnowledgeProgression || HuntingKnowledgeMapComponent.ColonyExperience(record.species) > 0f)
                .OrderByDescending(record => record.population)
                .ThenBy(record => record.species.label)
                .ToList();

        private static void DrawHeaderCard(Rect rect, string title, string value, Color accent, string tooltip)
        {
            Widgets.DrawMenuSection(rect);
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, 5f, rect.height), accent);
            GUI.color = new Color(0.72f, 0.78f, 0.72f);
            Widgets.Label(new Rect(rect.x + 13f, rect.y + 8f, rect.width - 20f, 20f), title);
            GUI.color = Color.white;
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(rect.x + 13f, rect.y + 29f, rect.width - 20f, 28f), value);
            Text.Font = GameFont.Small;
            TooltipHandler.TipRegion(rect, tooltip);
        }

        private void ShowManagement(RegionalWildlifeMapComponent component, RegionalSpeciesRecord record)
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            if (HerdsMod.Settings.enableRegionalMigration)
            {
                options.Add(new FloatMenuOption("Migration: Neutral", () => record.policy = 0));
                options.Add(new FloatMenuOption("Migration: Encourage", () => record.policy = 1));
                options.Add(new FloatMenuOption("Migration: Discourage", () => record.policy = -1));
            }
            List<RoamingAnimalRecord> roamers = component.RoamersFor(record.species)
                .Where(value => value.state != RoamingAnimalState.Dead).ToList();
            if (roamers.Count > 0)
                options.Add(new FloatMenuOption("Known Roaming Animals…",
                    () => Find.WindowStack.Add(new Window_RoamingWildlife(map, record.species))));
            RoamingAnimalRecord predatorRoamer = roamers.FirstOrDefault(value =>
                WildlifeSpeciesClassification.IsPredator(value.species));
            if (predatorRoamer != null && component.CanDiscourageReturns)
                options.Add(new FloatMenuOption("Predator Deterrent: Discourage Return",
                    () => component.DiscourageReturn(predatorRoamer)));
            if (HerdsMod.Settings.enableHuntingRegulations || HerdsMod.Settings.enableWildlifeSteward)
                options.Add(new FloatMenuOption("Species Management…", () => PlayerWildlifeCommandPatch.ShowRegulationMenu(map, record.species)));
            if (HerdsMod.Settings.enableAppliedEcology && WildlifeProgression.Unlocked(WildlifeCapability.AppliedEcology))
                options.Add(new FloatMenuOption("Predicted Effects: " + component.InterventionSummary(record), null));
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void ShowSpeciesActions(RegionalWildlifeMapComponent component,
            RegionalSpeciesRecord record, int knowledge, bool managementRelevant)
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            Pawn local = map.mapPawns.AllPawnsSpawned.Where(pawn =>
                pawn?.Spawned == true && !pawn.Dead && pawn.Faction == null &&
                pawn.def == record.species).OrderBy(pawn =>
                pawn.Position.DistanceToSquared(map.Center)).FirstOrDefault();
            if (local != null)
                options.Add(new FloatMenuOption("Focus Local " + record.species.LabelCap,
                    () => WildlifeUI.Focus(local)));
            options.Add(new FloatMenuOption("Review Animal Knowledge",
                () => Find.WindowStack.Add(new Window_ColonyWildlifeKnowledge())));
            bool hasRoamers = component.RoamersFor(record.species).Any(value =>
                value.state != RoamingAnimalState.Present &&
                value.state != RoamingAnimalState.Dead);
            if (hasRoamers)
                options.Add(new FloatMenuOption("Review Known Roaming Animals",
                    () => Find.WindowStack.Add(
                        new Window_RoamingWildlife(map, record.species))));
            if (managementRelevant && knowledge >= 3)
                options.Add(new FloatMenuOption("Manage " + record.species.LabelCap,
                    () => ShowManagement(component, record)));
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void ShowRegionalWorldMap(RegionalWildlifeMapComponent regional)
        {
            HuntingExpeditionMapComponent expeditions = map?.GetComponent<HuntingExpeditionMapComponent>();
            if (expeditions == null)
            {
                Messages.Message("Regional world-map knowledge is unavailable.", MessageTypeDefOf.RejectInput, false);
                return;
            }
            List<FloatMenuOption> options = new List<FloatMenuOption>
            {
                new FloatMenuOption("General Regional Knowledge", () => OpenRegionalWorldMap(expeditions, null))
            };
            foreach (RegionalSpeciesRecord record in VisibleRows(regional))
            {
                ThingDef species = record.species;
                options.Add(new FloatMenuOption(species.LabelCap + " Distribution", () => OpenRegionalWorldMap(expeditions, species)));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void OpenRegionalWorldMap(HuntingExpeditionMapComponent expeditions, ThingDef species)
        {
            Close(false);
            WildlifeWorldMapController.BeginRegionalMap(expeditions, this, species);
        }

        private void DrawSpatial(Rect rect, RegionalWildlifeMapComponent component, IReadOnlyList<RegionalSpeciesRecord> rows)
        {
            if (selectedSpecies == null || rows.All(record => record.species != selectedSpecies)) selectedSpecies = rows.FirstOrDefault()?.species;
            Rect speciesRect = new Rect(0f, 132f, 240f, rect.height - 132f);
            Rect speciesView = new Rect(0f, 0f, speciesRect.width - 18f, Mathf.Max(speciesRect.height, rows.Count * 60f));
            Widgets.BeginScrollView(speciesRect, ref spatialScroll, speciesView);
            for (int i = 0; i < rows.Count; i++)
            {
                RegionalSpeciesRecord record = rows[i];
                bool isSelected = record.species == selectedSpecies;
                Rect row = new Rect(0f, i * 60f, speciesView.width, 54f);
                Widgets.DrawMenuSection(row);
                if (isSelected)
                {
                    Widgets.DrawHighlightSelected(row);
                    Widgets.DrawBoxSolid(new Rect(row.x, row.y, 5f, row.height), new Color(0.38f, 0.62f, 0.35f));
                }
                else Widgets.DrawHighlightIfMouseover(row);
                Rect icon = new Rect(row.x + 11f, row.y + 9f, 36f, 36f);
                if (record.species.uiIcon != null) GUI.DrawTexture(icon, record.species.uiIcon, ScaleMode.ScaleToFit);
                Widgets.Label(new Rect(row.x + 54f, row.y + 6f, row.width - 62f, 23f), record.species.LabelCap);
                int rowKnowledge = HerdsMod.Settings.enableSpeciesKnowledgeProgression ? HuntingKnowledgeMapComponent.ColonyLevel(record.species) : 5;
                GUI.color = new Color(0.72f, 0.78f, 0.72f);
                string amount = rowKnowledge < 1 ? "Population unknown" : component.QualitativePopulation(record);
                Widgets.Label(new Rect(row.x + 54f, row.y + 29f, row.width - 62f, 20f), HuntingKnowledgeMapComponent.LevelLabel(rowKnowledge) + "  •  " + amount);
                GUI.color = Color.white;
                if (Widgets.ButtonInvisible(row)) selectedSpecies = record.species;
                TooltipHandler.TipRegion(row, "Show the regional distribution for " + record.species.LabelCap + ".");
            }
            Widgets.EndScrollView();
            RegionalSpeciesRecord selected = rows.FirstOrDefault(record => record.species == selectedSpecies);
            if (selected == null) { Widgets.Label(new Rect(260f, 150f, rect.width - 260f, 40f), "No known species are currently present in the region."); return; }
            int knowledge = HerdsMod.Settings.enableSpeciesKnowledgeProgression ? HuntingKnowledgeMapComponent.ColonyLevel(selected.species) : 5;
            Rect panel = new Rect(252f, 132f, rect.width - 252f, rect.height - 132f); Widgets.DrawMenuSection(panel);
            Widgets.Label(new Rect(panel.x + 12f, panel.y + 10f, panel.width - 24f, 28f), selected.species.LabelCap + " Regional Distribution — " + component.Forecast(selected));
            GUI.color = new Color(0.72f, 0.78f, 0.72f);
            Widgets.Label(new Rect(panel.x + 12f, panel.y + 35f, panel.width - 24f, 24f), "Each cell estimates a neighboring world region. Select another known animal from the list.");
            GUI.color = Color.white;
            float cellSize = Mathf.Min(145f, (panel.width - 60f) / 3f, (panel.height - 80f) / 3f);
            float startX = panel.x + (panel.width - cellSize * 3f) * 0.5f; float startY = panel.y + 66f;
            int[] mapping = { 0, 1, 2, 3, -1, 4, 5, 6, 7 };
            string[] directions = { "Northwest", "North", "Northeast", "West", "This Map", "East", "Southwest", "South", "Southeast" };
            for (int grid = 0; grid < 9; grid++)
            {
                int index = mapping[grid]; Rect cell = new Rect(startX + grid % 3 * cellSize, startY + grid / 3 * cellSize, cellSize - 6f, cellSize - 6f);
                Color color = index < 0 ? new Color(0.22f, 0.34f, 0.22f) : selected.neighboringPopulations[index] > selected.population / 8f ? new Color(0.18f, 0.38f, 0.22f) : new Color(0.30f, 0.24f, 0.18f);
                Widgets.DrawBoxSolid(cell, color); Widgets.DrawBox(cell);
                Text.Anchor = TextAnchor.MiddleCenter;
                bool telemetry = HerdsMod.Settings.enableTelemetry && WildlifeProgression.Unlocked(WildlifeCapability.Telemetry) && component.HasOperationalMonitor(WildlifeToolKind.TelemetryStation);
                string amount = knowledge < 2 ? "Unknown" : index < 0 ? selected.lastLocalCount + " Local" :
                    telemetry ? "About " + Mathf.RoundToInt(selected.neighboringPopulations[index]) :
                    selected.neighboringPopulations[index] < 1f ? "Sparse" : selected.neighboringPopulations[index] < 4f ? "Moderate" : "Dense";
                string route = index >= 0 && knowledge >= 3 && telemetry ? (selected.neighboringPopulations[index] > selected.lastLocalCount ? "\n→ inward" : "\n← outward") : "";
                Widgets.Label(cell.ContractedBy(5f), directions[grid] + "\n" + amount + route);
                Text.Anchor = TextAnchor.UpperLeft;
                TooltipHandler.TipRegion(cell, index < 0
                    ? "Animals of this species currently sighted on the colony map."
                    : "Estimated population in the " + directions[grid].ToLowerInvariant() +
                      " neighboring world cell." +
                      (WildlifeProgression.Unlocked(WildlifeCapability.Telemetry)
                          ? " Telemetry improves the estimate; arrows show predicted movement relative to this map."
                          : " Continued surveys improve the estimate."));
            }
        }
    }

    public sealed class Window_RoamingWildlife : Window
    {
        private readonly Map map;
        private readonly ThingDef species;
        private Vector2 scroll;

        public override Vector2 InitialSize => new Vector2(790f, 620f);

        public Window_RoamingWildlife(Map map, ThingDef species = null)
        {
            this.map = map;
            this.species = species;
            doCloseX = true;
            resizeable = true;
            absorbInputAroundWindow = true;
        }

        public override void DoWindowContents(Rect rect)
        {
            RegionalWildlifeMapComponent component = map?.GetComponent<RegionalWildlifeMapComponent>();
            if (component == null) return;
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, rect.width, 32f),
                species == null ? "Known Roaming Animals" : species.LabelCap.ToString() + " Roamers");
            Text.Font = GameFont.Small;
            GUI.color = new Color(0.70f, 0.78f, 0.74f);
            Widgets.Label(new Rect(0f, 31f, rect.width, 42f),
                "These are persistent individuals moving through the colony's wider home range. " +
                "Habitat, memories, season, management, and tracking affect what is known and when they return.");
            GUI.color = Color.white;

            List<RoamingAnimalRecord> rows = component.RoamingAnimals.Where(record =>
                record?.animal != null && record.state != RoamingAnimalState.Dead &&
                (species == null || record.species == species) &&
                (!HerdsMod.Settings.enableSpeciesKnowledgeProgression ||
                    HuntingKnowledgeMapComponent.ColonyExperience(record.species) > 0f))
                .OrderBy(record => record.state == RoamingAnimalState.Present ? 1 : 0)
                .ThenBy(record => record.expectedReturnTick).ToList();
            Rect outer = new Rect(0f, 78f, rect.width, rect.height - 78f);
            Rect view = new Rect(0f, 0f, outer.width - 18f,
                Mathf.Max(outer.height, rows.Count * 104f));
            Widgets.BeginScrollView(outer, ref scroll, view);
            for (int i = 0; i < rows.Count; i++)
            {
                RoamingAnimalRecord record = rows[i];
                Rect card = new Rect(0f, i * 104f, view.width, 96f);
                Widgets.DrawMenuSection(card);
                Widgets.DrawBoxSolid(new Rect(card.x, card.y, 5f, card.height),
                    record.state == RoamingAnimalState.Present
                        ? new Color(0.34f, 0.62f, 0.36f)
                        : record.state == RoamingAnimalState.Displaced
                            ? new Color(0.72f, 0.45f, 0.24f)
                            : new Color(0.30f, 0.48f, 0.62f));
                Widgets.Label(new Rect(card.x + 13f, card.y + 8f, 240f, 24f),
                    record.animal.LabelShortCap + " • " + record.species.LabelCap);
                string status = record.state == RoamingAnimalState.Present ? "Present on map" :
                    RegionalWildlifeMapComponent.StatePhrase(record.state).CapitalizeFirst() +
                    " • " + (record.tagged && WildlifeProgression.Unlocked(WildlifeCapability.Telemetry)
                        ? "Telemetry: " : "Last seen: ") + record.direction;
                Widgets.Label(new Rect(card.x + 13f, card.y + 34f, card.width - 285f, 24f), status);
                GUI.color = new Color(0.72f, 0.78f, 0.74f);
                string timing = record.state == RoamingAnimalState.Present
                    ? "Returned " + record.returnCount + (record.returnCount == 1 ? " time" : " times")
                    : record.tagged
                        ? "Expected return: " + Mathf.Max(0,
                            record.expectedReturnTick - Find.TickManager.TicksGame).ToStringTicksToPeriod()
                        : "Return estimate: " + component.NextExpectedReturn(record.species);
                Widgets.Label(new Rect(card.x + 13f, card.y + 59f, card.width - 285f, 24f),
                    timing + " • " + WildlifeLifeUtility.PersonalityLabel(record.animal));
                GUI.color = Color.white;
                TooltipHandler.TipRegion(new Rect(card.x + 8f, card.y + 5f,
                    card.width - 276f, card.height - 10f),
                    record.reason + "\n\n" + WildlifeLifeUtility.PersonalityDescription(record.animal) +
                    "\n\nIt retains its memories, health, relationships, and history while away.");

                if (record.state == RoamingAnimalState.Present && record.animal.Spawned)
                {
                    Rect primary = new Rect(card.xMax - 132f, card.y + 12f, 120f, 31f);
                    if (Widgets.ButtonText(primary, "Select"))
                        WildlifeUI.Show(record.animal);
                }
                if (record.tagged)
                    Widgets.Label(new Rect(card.xMax - 260f, card.y + 53f, 248f, 24f),
                        "Tracking collar • location confidence high");
                else if (record.notable)
                    Widgets.Label(new Rect(card.xMax - 260f, card.y + 53f, 248f, 24f),
                        "Notable animal • sightings are approximate");
            }
            Widgets.EndScrollView();
            if (rows.Count == 0)
                Widgets.Label(new Rect(8f, 90f, rect.width - 16f, 48f),
                    "No known persistent animals are currently roaming nearby.");
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetInspectString))]
    public static class JuvenileLearningInspectPatch
    {
        public static void Postfix(Pawn __instance, ref string __result)
        {
            if ((!HerdsMod.Settings.enableJuvenileLearning && !HerdsMod.Settings.enableAnimalRelationships && !HerdsMod.Settings.enableDomesticRoleProgression) || __instance?.Spawned != true || __instance.RaceProps?.Animal != true) return;
            RegionalWildlifeMapComponent component = __instance.Map.GetComponent<RegionalWildlifeMapComponent>();
            float learned = component?.LearningFactor(__instance) ?? 0f;
            if (HerdsMod.Settings.enableJuvenileLearning && learned > 0f) __result += "\nWildlife learning: " + component.LearningLabel(__instance) + " (" + learned.ToStringPercent() + ")";
            if (HerdsMod.Settings.enableAnimalRelationships)
            {
                AnimalRelationshipRecord relation = component?.RelationshipFor(__instance);
                if (relation?.mate != null && !relation.mate.Dead) __result += "\nMate: " + relation.mate.LabelShortCap;
                if (relation?.parent != null && !relation.parent.Dead) __result += "\nFamily adult: " + relation.parent.LabelShortCap;
                if (relation?.teacher != null && !relation.teacher.Dead) __result += "\nTeacher: " + relation.teacher.LabelShortCap;
                if (relation?.rival != null && !relation.rival.Dead) __result += "\nRival: " + relation.rival.LabelShortCap;
                int offspring = component?.OffspringCount(__instance) ?? 0;
                if (offspring > 0) __result += "\nKnown offspring: " + offspring;
            }
            if (HerdsMod.Settings.enableDomesticRoleProgression && __instance.Faction == Faction.OfPlayer && WildlifeSpeciesClassification.IsPredator(__instance.def))
            {
                WildlifeFieldcraftMapComponent fieldcraft = __instance.Map.GetComponent<WildlifeFieldcraftMapComponent>();
                DomesticPredatorRole role = fieldcraft?.DomesticRole(__instance) ?? DomesticPredatorRole.None;
                if (role != DomesticPredatorRole.None) __result += "\nRole experience: " + fieldcraft.DomesticLevel(__instance) + " (" + fieldcraft.DomesticExperience(__instance).ToStringPercent() + ")";
            }
        }
    }

    [HarmonyPatch(typeof(Corpse), nameof(Corpse.GetInspectString))]
    public static class CarcassClaimInspectPatch
    {
        public static void Postfix(Corpse __instance, ref string __result)
        {
            if (!HerdsMod.Settings.enableAdvancedScavenging || __instance?.Spawned != true) return;
            string info = __instance.Map.GetComponent<RegionalWildlifeMapComponent>()?.CarcassInfo(__instance);
            if (!string.IsNullOrEmpty(info)) __result += "\n" + info;
        }
    }

    [HarmonyPatch(typeof(JobGiver_Mate), "TryGiveJob")]
    [HarmonyPriority(Priority.Last)]
    public static class HabitatReproductionPatch
    {
        public static void Postfix(Pawn pawn, ref Job __result)
        {
            if (__result == null || HerdsMod.Settings?.enableHabitatEcology != true || pawn?.Spawned != true || pawn.Faction != null || pawn.RaceProps?.Animal != true) return;
            RegionalWildlifeMapComponent ecology = pawn.Map.GetComponent<RegionalWildlifeMapComponent>();
            float quality = ecology?.HabitatQuality ?? 0.5f;
            float allowed = 0.35f + quality * 0.65f;
            int day = (Find.TickManager?.TicksGame ?? 0) / 60000;
            float roll = Mathf.Abs((pawn.thingIDNumber * 37 + day * 17) % 100) / 100f;
            if (roll > allowed) __result = null;
        }
    }
}
