using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Herds
{
    public enum ExpeditionObjective
    {
        Hunt,
        Scout,
        Capture,
        Tag,
        Protect,
        Redirect
    }

    public enum ExpeditionRoutePolicy
    {
        Fastest,
        Safest,
        Balanced
    }

    public enum ExpeditionStage
    {
        Embarking,
        OutboundTravel,
        Tracking,
        Stalking,
        Engagement,
        FieldDressing,
        Returning,
        AwaitingRescue
    }

    public sealed class ExpeditionCellSpeciesRecord : IExposable
    {
        public ThingDef species;
        public float population;

        public void ExposeData()
        {
            Scribe_Defs.Look(ref species, "species");
            Scribe_Values.Look(ref population, "population", 0f);
        }
    }

    public sealed class ExpeditionCargoEntry : IExposable
    {
        public ThingDef def;
        public int count;

        public ExpeditionCargoEntry() { }
        public ExpeditionCargoEntry(ThingDef def, int count) { this.def = def; this.count = count; }

        public void ExposeData()
        {
            Scribe_Defs.Look(ref def, "def");
            Scribe_Values.Look(ref count, "count", 0);
        }
    }

    public sealed class ExpeditionCellRecord : IExposable
    {
        public int tileId = -1;
        public int discoveryLevel;
        public int traversals;
        public float confidence;
        public int visits;
        public int lastVisitTick;
        public string discovery;
        public List<ExpeditionCellSpeciesRecord> species = new List<ExpeditionCellSpeciesRecord>();

        public void ExposeData()
        {
            Scribe_Values.Look(ref tileId, "tileId", -1);
            Scribe_Values.Look(ref discoveryLevel, "discoveryLevel", 0);
            Scribe_Values.Look(ref traversals, "traversals", 0);
            Scribe_Values.Look(ref confidence, "confidence", 0.02f);
            Scribe_Values.Look(ref visits, "visits", 0);
            Scribe_Values.Look(ref lastVisitTick, "lastVisitTick", 0);
            Scribe_Values.Look(ref discovery, "discovery");
            Scribe_Collections.Look(ref species, "species", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                species ??= new List<ExpeditionCellSpeciesRecord>();
                if (visits > 0 && discoveryLevel < 2) discoveryLevel = 2;
            }
        }
    }

    public sealed class ExpeditionSpecialistRecord : IExposable
    {
        public Pawn pawn;
        public BiomeDef biome;
        public float experience;

        public void ExposeData()
        {
            Scribe_References.Look(ref pawn, "pawn");
            Scribe_Defs.Look(ref biome, "biome");
            Scribe_Values.Look(ref experience, "experience", 0f);
        }
    }

    public sealed class HuntingExpeditionRecord : IExposable
    {
        public int id;
        public Building_HuntingSpot spot;
        public List<Pawn> hunters = new List<Pawn>();
        public List<Pawn> packAnimals = new List<Pawn>();
        public List<Pawn> rescuers = new List<Pawn>();
        public ThingDef targetSpecies;
        public ThingDef actualSpecies;
        public bool unknownTarget;
        public ExpeditionObjective objective;
        public ExpeditionRoutePolicy routePolicy;
        public ExpeditionStage stage;
        public int destinationTile = -1;
        public int distance;
        public int stageStartedTick;
        public int nextStageTick;
        public int expectedReturnTick;
        public int embarkDeadline;
        public int departureTick;
        public float riskTolerance = 0.5f;
        public float foodNutrition;
        public float dailyNutrition;
        public bool lowFoodAlerted;
        public bool delayedAlerted;
        public List<Thing> packedProvisions = new List<Thing>();
        public int medicine;
        public List<ExpeditionCargoEntry> medicineManifest = new List<ExpeditionCargoEntry>();
        public int bedrolls;
        public int extraFoodDays;
        public bool allowAlternatives = true;
        public bool incidentResolved;
        public bool needsRescue;
        public int rescueArrivalTick;
        public bool encounterFound;
        public bool success;
        public bool captureReady;
        public int meat;
        public int leather;
        public int bonusLeather;
        public int trophies;
        public string bonusReward;
        public string result;
        public WorldObject_HuntingExpeditionMarker marker;
        public Caravan caravan;
        public List<int> routeTiles = new List<int>();
        public float encounterChance;
        public float encounterRoll = -1f;
        public float successChance;
        public float successRoll = -1f;
        public float incidentChance;
        public float incidentRoll = -1f;
        public string biomeEvent;
        public float biomeEncounterModifier;
        public float biomeDangerModifier;
        public float biomeSuccessModifier;
        public string interactiveEncounter;
        public Pawn roamingEncounterAnimal;
        public Pawn trailTargetAnimal;
        public bool interactiveEncounterPending;
        public bool interactiveEncounterResolved;
        public int interactiveEncounterResumeTick;
        public ExpeditionEventDef pendingEvent;
        public int nextEventCheckTick;
        public int eventCount;
        [Unsaved] public bool interactiveEncounterWindowOpen;
        public List<string> resources = new List<string>();
        public List<string> log = new List<string>();

        public IEnumerable<Pawn> Party => hunters.Concat(packAnimals).Concat(rescuers).Where(pawn => pawn != null).Distinct();

        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id", 0);
            Scribe_References.Look(ref spot, "spot");
            Scribe_Collections.Look(ref hunters, "hunters", LookMode.Reference);
            Scribe_Collections.Look(ref packAnimals, "packAnimals", LookMode.Reference);
            Scribe_Collections.Look(ref rescuers, "rescuers", LookMode.Reference);
            Scribe_Defs.Look(ref targetSpecies, "targetSpecies");
            Scribe_Defs.Look(ref actualSpecies, "actualSpecies");
            Scribe_Values.Look(ref unknownTarget, "unknownTarget", false);
            Scribe_Values.Look(ref objective, "objective", ExpeditionObjective.Hunt);
            Scribe_Values.Look(ref routePolicy, "routePolicy", ExpeditionRoutePolicy.Safest);
            Scribe_Values.Look(ref stage, "stage", ExpeditionStage.Embarking);
            Scribe_Values.Look(ref destinationTile, "destinationTile", -1);
            Scribe_Values.Look(ref distance, "distance", 1);
            Scribe_Values.Look(ref stageStartedTick, "stageStartedTick", 0);
            Scribe_Values.Look(ref nextStageTick, "nextStageTick", 0);
            Scribe_Values.Look(ref expectedReturnTick, "expectedReturnTick", 0);
            Scribe_Values.Look(ref embarkDeadline, "embarkDeadline", 0);
            Scribe_Values.Look(ref departureTick, "departureTick", 0);
            Scribe_Values.Look(ref riskTolerance, "riskTolerance", 0.5f);
            Scribe_Values.Look(ref foodNutrition, "foodNutrition", 0f);
            Scribe_Values.Look(ref dailyNutrition, "dailyNutrition", 0f);
            Scribe_Values.Look(ref lowFoodAlerted, "lowFoodAlerted", false);
            Scribe_Values.Look(ref delayedAlerted, "delayedAlerted", false);
            Scribe_Collections.Look(ref packedProvisions, "packedProvisions", LookMode.Reference);
            Scribe_Values.Look(ref medicine, "medicine", 0);
            Scribe_Collections.Look(ref medicineManifest, "medicineManifest", LookMode.Deep);
            Scribe_Values.Look(ref bedrolls, "bedrolls", 0);
            Scribe_Values.Look(ref extraFoodDays, "extraFoodDays", 0);
            Scribe_Values.Look(ref allowAlternatives, "allowAlternatives", true);
            Scribe_Values.Look(ref incidentResolved, "incidentResolved", false);
            Scribe_Values.Look(ref needsRescue, "needsRescue", false);
            Scribe_Values.Look(ref rescueArrivalTick, "rescueArrivalTick", 0);
            Scribe_Values.Look(ref encounterFound, "encounterFound", false);
            Scribe_Values.Look(ref success, "success", false);
            Scribe_Values.Look(ref captureReady, "captureReady", false);
            Scribe_Values.Look(ref meat, "meat", 0);
            Scribe_Values.Look(ref leather, "leather", 0);
            Scribe_Values.Look(ref bonusLeather, "bonusLeather", 0);
            Scribe_Values.Look(ref trophies, "trophies", 0);
            Scribe_Values.Look(ref bonusReward, "bonusReward");
            Scribe_Values.Look(ref result, "result");
            Scribe_References.Look(ref marker, "worldMarker");
            Scribe_References.Look(ref caravan, "expeditionCaravan");
            Scribe_Collections.Look(ref routeTiles, "routeTiles", LookMode.Value);
            Scribe_Values.Look(ref encounterChance, "encounterChance", 0f);
            Scribe_Values.Look(ref encounterRoll, "encounterRoll", -1f);
            Scribe_Values.Look(ref successChance, "successChance", 0f);
            Scribe_Values.Look(ref successRoll, "successRoll", -1f);
            Scribe_Values.Look(ref incidentChance, "incidentChance", 0f);
            Scribe_Values.Look(ref incidentRoll, "incidentRoll", -1f);
            Scribe_Values.Look(ref biomeEvent, "biomeEvent");
            Scribe_Values.Look(ref biomeEncounterModifier, "biomeEncounterModifier", 0f);
            Scribe_Values.Look(ref biomeDangerModifier, "biomeDangerModifier", 0f);
            Scribe_Values.Look(ref biomeSuccessModifier, "biomeSuccessModifier", 0f);
            Scribe_Values.Look(ref interactiveEncounter, "interactiveEncounter");
            Scribe_References.Look(ref roamingEncounterAnimal, "roamingEncounterAnimal");
            Scribe_References.Look(ref trailTargetAnimal, "trailTargetAnimal");
            Scribe_Values.Look(ref interactiveEncounterPending, "interactiveEncounterPending", false);
            Scribe_Values.Look(ref interactiveEncounterResolved, "interactiveEncounterResolved", false);
            Scribe_Values.Look(ref interactiveEncounterResumeTick, "interactiveEncounterResumeTick", 0);
            Scribe_Defs.Look(ref pendingEvent, "pendingExpeditionEvent");
            Scribe_Values.Look(ref nextEventCheckTick, "nextExpeditionEventCheckTick", 0);
            Scribe_Values.Look(ref eventCount, "expeditionEventCount", 0);
            Scribe_Collections.Look(ref resources, "resources", LookMode.Value);
            Scribe_Collections.Look(ref log, "log", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                hunters ??= new List<Pawn>();
                packAnimals ??= new List<Pawn>();
                rescuers ??= new List<Pawn>();
                packedProvisions ??= new List<Thing>();
                medicineManifest ??= new List<ExpeditionCargoEntry>();
                routeTiles ??= new List<int>();
                resources ??= new List<string>();
                log ??= new List<string>();
                if (stage != ExpeditionStage.Embarking && departureTick <= 0) departureTick = stageStartedTick;
                if (dailyNutrition <= 0f && foodNutrition > 0f)
                    dailyNutrition = Mathf.Max(0.8f, hunters.Count * 1.6f + packAnimals.Count * 0.8f);
            }
        }
    }

    public sealed class ExpeditionDestination
    {
        public int tileId;
        public int distance;
        public BiomeDef biome;
        public ExpeditionCellRecord knowledge;
        public float travelFactor;
        public float danger;
        public bool road;
        public bool river;
    }

    public sealed class ExpeditionPlan
    {
        public List<Pawn> hunters = new List<Pawn>();
        public List<Pawn> packAnimals = new List<Pawn>();
        public ExpeditionDestination destination;
        public ThingDef targetSpecies;
        public bool unknownTarget;
        public ExpeditionObjective objective;
        public ExpeditionRoutePolicy routePolicy = ExpeditionRoutePolicy.Safest;
        public float riskTolerance = 0.5f;
        public int foodDays;
        public int medicine;
        public Dictionary<ThingDef, int> medicines = new Dictionary<ThingDef, int>();
        public Dictionary<ThingDef, int> provisions = new Dictionary<ThingDef, int>();
        public bool useBedrolls = true;
        public bool allowAlternatives = true;
        public Pawn trailTargetAnimal;
        public HashSet<string> resources = new HashSet<string>();
    }

    public sealed class TrailHuntOpportunity : IExposable
    {
        public ThingDef species;
        public BiomeDef biome;
        public Pawn tracker;
        public Pawn targetAnimal;
        public int expiresTick;
        public float quality;

        public void ExposeData()
        {
            Scribe_Defs.Look(ref species, "species");
            Scribe_Defs.Look(ref biome, "biome");
            Scribe_References.Look(ref tracker, "tracker");
            Scribe_References.Look(ref targetAnimal, "targetAnimal");
            Scribe_Values.Look(ref expiresTick, "expiresTick");
            Scribe_Values.Look(ref quality, "quality");
        }
    }

    public sealed class HuntingExpeditionMapComponent : MapComponent
    {
        private List<HuntingExpeditionRecord> expeditions = new List<HuntingExpeditionRecord>();
        private List<ExpeditionCellRecord> cells = new List<ExpeditionCellRecord>();
        private List<ExpeditionSpecialistRecord> specialists = new List<ExpeditionSpecialistRecord>();
        private List<string> history = new List<string>();
        private List<TrailHuntOpportunity> trailHuntOpportunities = new List<TrailHuntOpportunity>();
        private List<ExpeditionTrailPath> trailPaths = new List<ExpeditionTrailPath>();
        private int nextId = 1;
        private int lastEcologyTick;

        public HuntingExpeditionMapComponent(Map map) : base(map) { }
        public IReadOnlyList<string> History => history;
        public IReadOnlyList<HuntingExpeditionRecord> ActiveExpeditions => expeditions;
        public Map HomeMap => map;
        public IReadOnlyList<TrailHuntOpportunity> TrailHuntOpportunities => trailHuntOpportunities;
        public IReadOnlyList<ExpeditionTrailPath> TrailPaths => trailPaths;

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            if (HerdsMod.Settings?.enableOffMapHuntingExpeditions != true) CancelAll("Off-map wildlife expeditions are disabled.");
            else
                for (int i = 0; i < expeditions.Count; i++)
                {
                    EnsureCaravan(expeditions[i]);
                    EnsureMarker(expeditions[i]);
                }
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref expeditions, "huntingExpeditions", LookMode.Deep);
            Scribe_Collections.Look(ref cells, "expeditionCells", LookMode.Deep);
            Scribe_Collections.Look(ref specialists, "expeditionSpecialists", LookMode.Deep);
            Scribe_Collections.Look(ref history, "expeditionHistory", LookMode.Value);
            Scribe_Collections.Look(ref trailHuntOpportunities, "trailHuntOpportunities", LookMode.Deep);
            Scribe_Collections.Look(ref trailPaths, "expeditionTrailPaths", LookMode.Deep);
            Scribe_Values.Look(ref nextId, "nextExpeditionId", 1);
            Scribe_Values.Look(ref lastEcologyTick, "lastExpeditionEcologyTick", 0);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                expeditions ??= new List<HuntingExpeditionRecord>();
                cells ??= new List<ExpeditionCellRecord>();
                specialists ??= new List<ExpeditionSpecialistRecord>();
                history ??= new List<string>();
                trailHuntOpportunities ??= new List<TrailHuntOpportunity>();
                trailPaths ??= new List<ExpeditionTrailPath>();
            }
        }

        public override void MapComponentTick()
        {
            if (HerdsMod.Settings?.enableOffMapHuntingExpeditions != true) return;
            int now = Find.TickManager.TicksGame;
            trailHuntOpportunities.RemoveAll(value => value?.species == null || value.expiresTick <= now);
            if (now % 60 != map.uniqueID % 60) return;
            if (now - lastEcologyTick >= 60000)
            {
                lastEcologyTick = now;
                UpdateDistantEcology();
            }
            if (expeditions.Count == 0) return;
            for (int i = expeditions.Count - 1; i >= 0; i--) TickExpedition(expeditions[i], now);
            for (int i = 0; i < expeditions.Count; i++) UpdateMarker(expeditions[i]);
        }

        public override void MapComponentDraw()
        {
            // GUI labels cannot be drawn from MapComponentDraw. Expedition status is
            // available in the Wildlife Expeditions page and development overview.
        }

        public List<string> DebugOverviewLines()
        {
            List<string> lines = new List<string> { "EXPEDITIONS active=" + expeditions.Count + " cells=" + cells.Count + " specialists=" + specialists.Count };
            for (int i = 0; i < expeditions.Count; i++)
            {
                HuntingExpeditionRecord record = expeditions[i];
                lines.Add("EXP " + record.id + " | " + StageLabel(record.stage) + " | tile=" + record.destinationTile + " distance=" + record.distance + " objective=" + record.objective + " target=" + (record.targetSpecies?.defName ?? "survey") + " party=" + record.hunters.Count + "+" + record.packAnimals.Count + " return=" + record.expectedReturnTick);
            }
            return lines;
        }

        public List<string> DebugValidationLines()
        {
            List<string> lines = new List<string>();
            void Check(bool condition, string name) => lines.Add((condition ? "PASS | " : "FAIL | ") + name);
            Check(HerdsDefOf.Herds_EmbarkHuntingExpedition != null, "Embark job definition");
            Check(HerdsDefOf.Herds_HuntingExpeditionMarker != null, "World marker definition");
            ResearchProjectDef expeditionResearch = DefDatabase<ResearchProjectDef>.GetNamedSilentFail("Wildlife_HuntingExpedition");
            Check(expeditionResearch?.prerequisites?.Any(project => project.defName == "Wildlife_Fieldcraft") == true, "Wildlife Expedition requires Organized Hunting");
            List<ExpeditionDestination> destinations = Destinations();
            Check(destinations.Count > 0, "At least one reachable destination");
            Check(destinations.All(destination => destination.tileId >= 0 && destination.biome != null && destination.distance >= 1 && destination.distance <= MaxRange), "Destination bounds and biomes");
            Check(expeditions.All(record => record.hunters.Any(pawn => pawn != null && !pawn.Dead)), "Active parties retain a living hunter");
            Check(expeditions.All(record =>
                record.Party.Any(pawn => pawn?.Spawned == true) ||
                record.caravan?.Destroyed == false ||
                record.marker?.Destroyed == false), "Active expeditions have a map or world presence");
            Check(expeditions.SelectMany(record => record.Party)
                .Where(pawn => pawn != null).GroupBy(pawn => pawn).All(group => group.Count() == 1),
                "No pawn belongs to multiple active expeditions");
            Check(expeditions.Select(record => record.id).Distinct().Count() == expeditions.Count &&
                expeditions.Where(record => record.caravan != null)
                    .GroupBy(record => record.caravan).All(group => group.Count() == 1),
                "Simultaneous expeditions retain independent records and caravans");
            Check(history.Count <= 20, "Completed expedition history is bounded");
            ExpeditionDestination destination = destinations.FirstOrDefault();
            List<Pawn> hunters = map.mapPawns.FreeColonistsSpawned.Where(pawn => !pawn.Downed && !pawn.WorkTagIsDisabled(WorkTags.Violent)).Take(3).ToList();
            ThingDef species = destination?.biome?.AllWildAnimals.Select(kind => kind?.race).FirstOrDefault(def => def?.race?.Animal == true);
            foreach (ExpeditionObjective objective in Enum.GetValues(typeof(ExpeditionObjective)))
            {
                ExpeditionPlan plan = new ExpeditionPlan
                {
                    destination = destination,
                    objective = objective,
                    targetSpecies = objective == ExpeditionObjective.Scout ? null : species,
                    hunters = hunters,
                    riskTolerance = 0.5f,
                    routePolicy = ExpeditionRoutePolicy.Safest
                };
                string forecast = ForecastDetails(plan);
                Check(destination == null || (!forecast.NullOrEmpty() && !forecast.Contains("NaN") && !forecast.Contains("Infinity")), objective + " forecast calculation");
            }
            Check(ExpeditionSupplyUtility.AvailableNutrition(map) >= 0f && ExpeditionSupplyUtility.AvailableMedicine(map) >= 0 &&
                ExpeditionSupplyUtility.AvailableBedrolls(map) >= 0, "Supply accounting");
            return lines;
        }

        public void DebugAdvance(HuntingExpeditionRecord record)
        {
            if (record == null || !expeditions.Contains(record)) return;
            if (record.stage == ExpeditionStage.Embarking)
            {
                foreach (Pawn pawn in record.Party.Where(pawn => pawn?.Spawned == true).ToList()) NotifyEmbarked(record.id, pawn);
            }
            record.nextStageTick = Find.TickManager.TicksGame;
            TickExpedition(record, Find.TickManager.TicksGame);
        }

        public void DebugRevealRegionalTiles(bool surveyed)
        {
            foreach (ExpeditionDestination destination in Destinations())
            {
                ExpeditionCellRecord cell = destination.knowledge;
                cell.discoveryLevel = surveyed ? 2 : 1;
                cell.traversals = Mathf.Max(1, cell.traversals);
                cell.lastVisitTick = Find.TickManager.TicksGame;
                cell.confidence = Mathf.Max(cell.confidence, surveyed ? 0.72f : 0.18f);
                if (surveyed) RevealWildlifeSigns(destination, 4);
            }
            if (map.Tile.Valid) map.Tile.Layer.SetDirty<WorldDrawLayer_WildlifeKnowledgeFog>();
        }

        public void DebugResetRegionalTiles()
        {
            for (int i = 0; i < cells.Count; i++)
            {
                cells[i].discoveryLevel = 0;
                cells[i].traversals = 0;
                cells[i].confidence = 0.02f;
                cells[i].visits = 0;
                cells[i].lastVisitTick = 0;
                cells[i].discovery = null;
                cells[i].species.Clear();
            }
            if (map.Tile.Valid) map.Tile.Layer.SetDirty<WorldDrawLayer_WildlifeKnowledgeFog>();
        }

        public HuntingExpeditionRecord ForSpot(Building_HuntingSpot spot) =>
            expeditions.FirstOrDefault(record => record.spot == spot);

        public HuntingExpeditionRecord FindRecord(int id) => expeditions.FirstOrDefault(record => record.id == id);

        public TrailHuntOpportunity ActiveTrailHuntOpportunity(ThingDef species, BiomeDef biome = null)
        {
            int now = Find.TickManager?.TicksGame ?? 0;
            return trailHuntOpportunities.Where(value => value?.species == species &&
                value.expiresTick > now)
                .OrderByDescending(value => biome != null && value.biome == biome)
                .ThenByDescending(value => value.quality).FirstOrDefault();
        }

        internal static bool MatchesTrailOpportunity(TrailHuntOpportunity opportunity,
            ThingDef species) => opportunity != null && opportunity.species == species &&
            opportunity.targetAnimal != null && opportunity.targetAnimal.Spawned != true &&
            !opportunity.targetAnimal.Dead;

        public bool TryCreateTrailHuntOpportunity(Pawn tracker, ThingDef species, float confidence,
            Pawn targetAnimal = null)
        {
            if (tracker?.Spawned != true || species?.race?.Animal != true ||
                HerdsMod.Settings?.enableOffMapHuntingExpeditions != true ||
                !WildlifeProgression.Unlocked(WildlifeCapability.HuntingExpedition)) return false;
            int knowledge = map.GetComponent<HuntingKnowledgeMapComponent>()?.Level(tracker, species) ?? 0;
            int now = Find.TickManager.TicksGame;
            TrailHuntOpportunity opportunity = trailHuntOpportunities.FirstOrDefault(value =>
                value?.targetAnimal == targetAnimal && value.expiresTick > now);
            if (opportunity == null)
            {
                opportunity = new TrailHuntOpportunity { species = species, biome = map.Biome };
                trailHuntOpportunities.Add(opportunity);
            }
            opportunity.tracker = tracker;
            opportunity.targetAnimal = targetAnimal;
            opportunity.expiresTick = now + 60000;
            opportunity.quality = Mathf.Clamp01(Mathf.Max(opportunity.quality,
                0.35f + confidence * 0.45f + knowledge * 0.04f));
            string text = tracker.LabelShortCap + " found a time-sensitive " + species.label +
                " hunt lead. A Wildlife Expedition launched within one day will have better encounter, success, and safety conditions.";
            Messages.Message(text, tracker, MessageTypeDefOf.PositiveEvent, false);
            WildlifeExperience.Record("Trail Hunt Opportunity", text, tracker);
            return true;
        }

        public ExpeditionDestination NearbyTrailDestination(Pawn targetAnimal)
        {
            List<PlanetTile> neighbors = new List<PlanetTile>();
            Find.WorldGrid.GetTileNeighbors(map.Tile, neighbors);
            return neighbors.Select(tile => (int)tile).Where(CanExpeditionTo)
                .Select(tileId => DestinationForTile(tileId))
                .OrderBy(destination => Mathf.Abs(Gen.HashCombineInt(destination.tileId,
                    targetAnimal?.thingIDNumber ?? 0)))
                .FirstOrDefault();
        }

        public void ForgetTrailOpportunity(Pawn targetAnimal)
        {
            trailHuntOpportunities.RemoveAll(value => value?.targetAnimal == targetAnimal);
        }

        public int MaxRange => int.MaxValue;
        public IReadOnlyList<ExpeditionCellRecord> KnownCellRecords => cells;

        public List<ExpeditionDestination> Destinations()
        {
            List<ExpeditionDestination> result = new List<ExpeditionDestination>();
            if (Find.WorldGrid == null || !map.Tile.Valid) return result;
            HashSet<int> candidates = new HashSet<int>(cells.Where(cell => cell.discoveryLevel > 0).Select(cell => cell.tileId));
            List<PlanetTile> neighbors = new List<PlanetTile>();
            Find.WorldGrid.GetTileNeighbors(map.Tile, neighbors);
            for (int i = 0; i < neighbors.Count; i++) candidates.Add((int)neighbors[i]);
            foreach (int tileId in candidates)
                if (CanExpeditionTo(tileId))
                    result.Add(DestinationForTile(tileId));
            return result.OrderBy(destination => destination.distance).ThenBy(destination => destination.biome.label).ThenBy(destination => destination.tileId).ToList();
        }

        public List<ThingDef> KnownSpecies(ExpeditionDestination destination)
        {
            if (destination?.knowledge == null || destination.knowledge.discoveryLevel < 2) return new List<ThingDef>();
            return destination.knowledge.species
                .Where(record => record?.species != null && record.population > 0.05f)
                .Select(record => record.species)
                .Where(species => species?.race?.Animal == true && (!HerdsMod.Settings.enableSpeciesKnowledgeProgression || HuntingKnowledgeMapComponent.ColonyExperience(species) > 0f))
                .Distinct()
                .OrderBy(species => species.label)
                .ToList();
        }

        public ExpeditionCellRecord KnowledgeForTile(int tileId) => Cell(tileId);

        public ExpeditionCellRecord ExistingKnowledgeForTile(int tileId) =>
            cells.FirstOrDefault(record => record.tileId == tileId);

        public bool CanExpeditionTo(int tileId)
        {
            if (Find.WorldGrid == null || !map.Tile.Valid || tileId < 0 || tileId == (int)map.Tile) return false;
            PlanetTile planetTile = (PlanetTile)tileId;
            if (!planetTile.Valid || planetTile.Layer != map.Tile.Layer) return false;
            Tile tile = Find.WorldGrid[planetTile];
            if (tile == null || tile.WaterCovered || tile.PrimaryBiome == null || tile.PrimaryBiome.impassable) return false;
            return !Find.WorldObjects.ObjectsAt(planetTile).Any(worldObject =>
                worldObject is Settlement ||
                worldObject?.def?.defName?.IndexOf("Outpost", StringComparison.OrdinalIgnoreCase) >= 0 ||
                worldObject?.Label?.IndexOf("Outpost", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        public ExpeditionDestination DestinationForTile(int tileId, bool persistKnowledge = true)
        {
            if (!CanExpeditionTo(tileId)) return null;
            PlanetTile planetTile = (PlanetTile)tileId;
            Tile tile = Find.WorldGrid[planetTile];
            SurfaceTile surface = tile as SurfaceTile;
            int distance = Mathf.Max(1, Mathf.RoundToInt(Find.WorldGrid.ApproxDistanceInTiles(map.Tile, planetTile)));
            float movement = Mathf.Max(0.65f, tile.PrimaryBiome.movementDifficulty);
            ExpeditionCellRecord knowledge = ExistingKnowledgeForTile(tileId) ??
                (persistKnowledge ? Cell(tileId) : new ExpeditionCellRecord { tileId = tileId, confidence = 0.02f });
            return new ExpeditionDestination
            {
                tileId = tileId,
                distance = distance,
                biome = tile.PrimaryBiome,
                knowledge = knowledge,
                road = surface?.Roads?.Count > 0,
                river = surface?.Rivers?.Count > 0,
                travelFactor = movement * (surface?.Roads?.Count > 0 ? 0.78f : 1f) * (surface?.Rivers?.Count > 0 ? 1.08f : 1f),
                danger = Mathf.Clamp01(0.08f + Mathf.Min(0.45f, distance * 0.006f) +
                    Mathf.InverseLerp(1f, 4f, movement) * 0.18f + Mathf.Abs(tile.temperature - 18f) / 120f)
            };
        }

        public bool IsTileKnown(int tileId)
        {
            ExpeditionCellRecord record = cells.FirstOrDefault(item => item.tileId == tileId);
            return record != null && (record.discoveryLevel > 0 || record.visits > 0);
        }

        public string TileKnowledgeLabel(ExpeditionDestination destination)
        {
            if (destination?.knowledge == null || destination.knowledge.discoveryLevel <= 0) return "Unknown region";
            if (destination.knowledge.discoveryLevel == 1)
                return destination.biome.LabelCap + " • route observed • " + destination.knowledge.confidence.ToStringPercent() + " confidence";
            return destination.biome.LabelCap + " • surveyed " + destination.knowledge.visits + " time" +
                (destination.knowledge.visits == 1 ? "" : "s") + " • " + destination.knowledge.confidence.ToStringPercent() + " confidence";
        }

        public static bool IsHerdSpecies(ThingDef species) =>
            species?.race?.Animal == true && PreyProfileDatabase.For(species)?.socialType == PreySocialType.Herd;

        public float PopulationAt(ExpeditionDestination destination, ThingDef species)
        {
            if (destination == null || species == null) return 0f;
            ExpeditionCellSpeciesRecord record = destination.knowledge.species.FirstOrDefault(item => item.species == species);
            if (record != null) return record.population * SeasonalFactor(species);
            PawnKindDef kind = destination.biome?.AllWildAnimals.FirstOrDefault(candidate => candidate?.race == species);
            float commonality = kind == null ? 0f : destination.biome.CommonalityOfAnimal(kind);
            int variation = Mathf.Abs(Gen.HashCombineInt(destination.tileId, species.shortHash)) % 9;
            float population = commonality <= 0f ? 0f : Mathf.Clamp(2f + commonality * 12f + variation, 1f, 90f);
            destination.knowledge.species.Add(new ExpeditionCellSpeciesRecord { species = species, population = population });
            return population * SeasonalFactor(species);
        }

        public float SpecialistExperience(Pawn pawn, BiomeDef biome) =>
            specialists.FirstOrDefault(record => record.pawn == pawn && record.biome == biome)?.experience ?? 0f;

        public int SpecialistLevel(Pawn pawn, BiomeDef biome)
        {
            float xp = SpecialistExperience(pawn, biome);
            return xp >= 600f ? 4 : xp >= 300f ? 3 : xp >= 140f ? 2 : xp >= 50f ? 1 : 0;
        }

        public bool Begin(ExpeditionPlan plan, out string reason)
        {
            reason = null;
            if (HerdsMod.Settings.enableOffMapHuntingExpeditions != true || !WildlifeProgression.Unlocked(WildlifeCapability.HuntingExpedition))
            {
                reason = WildlifeProgression.LockReason(WildlifeCapability.HuntingExpedition);
                return false;
            }
            if (plan?.destination == null || plan.hunters.Count == 0)
            {
                reason = "The expedition plan is incomplete.";
                return false;
            }
            if (plan.trailTargetAnimal != null)
            {
                ExpeditionDestination trailDestination = NearbyTrailDestination(plan.trailTargetAnimal);
                TrailHuntOpportunity validatedTrailOpportunity = trailHuntOpportunities.FirstOrDefault(value =>
                    value?.targetAnimal == plan.trailTargetAnimal &&
                    MatchesTrailOpportunity(value, plan.trailTargetAnimal.def));
                if (validatedTrailOpportunity == null || trailDestination == null ||
                    plan.objective != ExpeditionObjective.Hunt ||
                    plan.targetSpecies != plan.trailTargetAnimal.def || plan.unknownTarget ||
                    plan.destination.tileId != trailDestination.tileId)
                {
                    reason = "This trail expedition must hunt its exact animal at the nearby trail destination.";
                    return false;
                }
            }
            if (!CanExpeditionTo(plan.destination.tileId))
            {
                reason = "Choose an unoccupied, passable world tile.";
                return false;
            }
            plan.destination = DestinationForTile(plan.destination.tileId, true);
            if (plan.hunters.Concat(plan.packAnimals).Any(PawnOnExpedition))
            {
                reason = "A selected pawn is already assigned to another wildlife expedition.";
                return false;
            }
            if (plan.objective == ExpeditionObjective.Hunt && plan.hunters.Any(pawn => pawn.WorkTagIsDisabled(WorkTags.Violent)))
            {
                reason = "Pacifists cannot join a Hunt.";
                return false;
            }
            if (plan.objective == ExpeditionObjective.Tag && !WildlifeProgression.Unlocked(WildlifeCapability.Telemetry)) { reason = WildlifeProgression.LockReason(WildlifeCapability.Telemetry); return false; }
            if (plan.objective != ExpeditionObjective.Scout && plan.targetSpecies == null && !plan.unknownTarget) { reason = "Choose a target animal or Unknown."; return false; }
            if (plan.objective == ExpeditionObjective.Redirect && plan.targetSpecies != null && !IsHerdSpecies(plan.targetSpecies)) { reason = "Only herd animals can be redirected."; return false; }
            float requiredFood = ExpeditionSupplyUtility.RequiredNutrition(plan, EstimateDays(plan));
            float selectedFood = ExpeditionSupplyUtility.SelectedNutrition(plan.provisions);
            if (selectedFood + 0.001f < requiredFood) { reason = "Select enough provisions for the planned journey."; return false; }
            if (!ExpeditionSupplyUtility.ManifestAvailable(map, plan.provisions) || !ExpeditionSupplyUtility.ManifestAvailable(map, plan.medicines))
            {
                reason = "A selected provision or medicine is no longer available.";
                return false;
            }
            if (plan.useBedrolls && ExpeditionSupplyUtility.AvailableBedrolls(map) < plan.hunters.Count) { reason = "Not enough packed bedrolls are available."; return false; }
            if (!ConsumeFieldcraftResources(plan, out reason)) return false;
            List<Thing> packedProvisions = ExpeditionSupplyUtility.PackManifest(map, plan.provisions, plan.hunters[0]);
            List<ExpeditionCargoEntry> medicineManifest = ExpeditionSupplyUtility.ConsumeManifest(map, plan.medicines);
            int medicineCount = medicineManifest.Sum(entry => entry.count);
            float dailyFood = ExpeditionSupplyUtility.DailyNutrition(plan);
            int extraDays = dailyFood <= 0f ? 0 : Mathf.Clamp(Mathf.FloorToInt((selectedFood - requiredFood) / dailyFood), 0, 3);
            if (plan.useBedrolls) ExpeditionSupplyUtility.PackBedrolls(map, plan.hunters.Count, plan.hunters[0]);
            int now = Find.TickManager.TicksGame;
            HuntingExpeditionRecord record = new HuntingExpeditionRecord
            {
                id = nextId++,
                spot = null,
                hunters = plan.hunters.Distinct().ToList(),
                packAnimals = plan.packAnimals.Distinct().ToList(),
                targetSpecies = plan.targetSpecies,
                unknownTarget = plan.unknownTarget,
                objective = plan.objective,
                routePolicy = plan.routePolicy,
                stage = ExpeditionStage.Embarking,
                destinationTile = plan.destination.tileId,
                distance = plan.destination.distance,
                stageStartedTick = now,
                embarkDeadline = now + 15000,
                expectedReturnTick = now + Mathf.RoundToInt(EstimateDays(plan) * 60000f),
                riskTolerance = plan.riskTolerance,
                foodNutrition = selectedFood,
                dailyNutrition = dailyFood,
                packedProvisions = packedProvisions,
                medicine = medicineCount,
                medicineManifest = medicineManifest,
                bedrolls = plan.useBedrolls ? plan.hunters.Count : 0,
                extraFoodDays = extraDays,
                allowAlternatives = plan.allowAlternatives,
                resources = plan.resources.ToList()
            };
            TrailHuntOpportunity trailOpportunity = plan.objective == ExpeditionObjective.Hunt
                ? trailHuntOpportunities.FirstOrDefault(value =>
                    value?.targetAnimal == plan.trailTargetAnimal &&
                    MatchesTrailOpportunity(value, plan.targetSpecies)) : null;
            if (!MatchesTrailOpportunity(trailOpportunity, plan.targetSpecies))
                trailOpportunity = null;
            if (trailOpportunity != null)
            {
                float bonus = TrailHuntBonus(trailOpportunity);
                record.biomeEncounterModifier += bonus;
                record.biomeSuccessModifier += bonus * 0.75f;
                record.biomeDangerModifier -= bonus * 0.45f;
                record.biomeEvent = "Fresh trail lead";
                record.trailTargetAnimal = trailOpportunity.targetAnimal;
                trailHuntOpportunities.Remove(trailOpportunity);
                record.log.Add("The expedition followed a fresh trail-study lead.");
            }
            record.routeTiles = BuildRoute(record.destinationTile);
            if (trailOpportunity != null) AddTrailPath(record.routeTiles, record.targetSpecies);
            record.log.Add("Assembling at the colony edge.");
            expeditions.Add(record);
            map.Tile.Layer.SetDirty<WorldDrawLayer_WildlifeExpeditionRoutes>();
            EnsureMarker(record);
            Current.Game?.GetComponent<WildlifeExperienceGameComponent>()?.ShowExpeditionTutorial();
            IntVec3 edge;
            if (!RCellFinder.TryFindRandomPawnEntryCell(out edge, map, CellFinder.EdgeRoadChance_Animal)) edge = CellFinder.RandomEdgeCell(map);
            foreach (Pawn pawn in record.hunters.Concat(record.packAnimals)) OrderEmbark(record, pawn, edge);
            string departureDestination = plan.destination.knowledge.discoveryLevel > 0 ? plan.destination.biome.LabelCap.ToString() : "an unknown region";
            Pawn messageTarget = record.hunters.FirstOrDefault(pawn => pawn?.Spawned == true);
            Messages.Message("Wildlife expedition assembling for " + departureDestination + ".", messageTarget, MessageTypeDefOf.NeutralEvent, false);
            WildlifeExperience.Record("Expedition", "A wildlife expedition began toward " + departureDestination + ".", messageTarget);
            if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("ExpeditionBegin", "id=" + record.id + " tile=" + record.destinationTile + " distance=" + record.distance + " objective=" + record.objective + " hunters=" + record.hunters.Count);
            return true;
        }

        internal static float TrailHuntBonus(TrailHuntOpportunity opportunity) =>
            opportunity == null ? 0f : Mathf.Lerp(0.08f, 0.18f,
                Mathf.Clamp01(opportunity.quality));

        public bool HasTrailPath(int fromTile, int toTile) =>
            trailPaths.Any(path => path?.Connects(fromTile, toTile) == true);

        private void AddTrailPath(List<int> route, ThingDef species)
        {
            if (route == null) return;
            for (int i = 0; i + 1 < route.Count; i++)
                if (!HasTrailPath(route[i], route[i + 1]))
                    trailPaths.Add(new ExpeditionTrailPath
                    {
                        fromTile = route[i], toTile = route[i + 1],
                        createdTick = Find.TickManager.TicksGame, targetSpecies = species
                    });
            map.Tile.Layer.SetDirty<WorldDrawLayer_WildlifeExpeditionRoutes>();
        }

        public bool PawnOnExpedition(Pawn pawn) =>
            pawn != null && expeditions.Any(record => record.Party.Contains(pawn));

        public void NotifyEmbarked(int id, Pawn pawn)
        {
            HuntingExpeditionRecord record = expeditions.FirstOrDefault(item => item.id == id);
            if (record == null || pawn?.Spawned != true || !record.Party.Contains(pawn)) return;
            pawn.DeSpawn(DestroyMode.Vanish);
            AddToExpeditionCaravan(record, pawn);
            if (record.needsRescue && record.rescuers.Contains(pawn))
            {
                record.rescueArrivalTick = Find.TickManager.TicksGame + TravelTicks(record) / 2;
                record.nextStageTick = Mathf.Max(record.nextStageTick, record.rescueArrivalTick + 60000);
                record.log.Add(pawn.LabelShortCap + " left the colony and is traveling to the stranded party.");
            }
        }

        public void Cancel(HuntingExpeditionRecord record)
        {
            if (record == null || !expeditions.Contains(record)) return;
            record.result = "Cancelled and recalled by the player.";
            if (record.stage == ExpeditionStage.FieldDressing)
            {
                record.meat /= 2;
                record.leather /= 2;
                record.result += " The hurried return recovered only part of the quarry.";
            }
            ReturnParty(record);
            Finish(record, false);
        }

        public void CancelAll(string reason)
        {
            for (int i = expeditions.Count - 1; i >= 0; i--)
            {
                HuntingExpeditionRecord record = expeditions[i];
                record.result = reason;
                ReturnParty(record);
                Finish(record, false);
            }
        }

        public void BeginRescue(HuntingExpeditionRecord record, Pawn pawn)
        {
            if (record?.needsRescue != true || record.rescuers.Any(candidate => candidate != null && !candidate.Dead) ||
                pawn?.Spawned != true || pawn.Faction != Faction.OfPlayer || pawn.Downed) return;
            record.rescuers.Add(pawn);
            IntVec3 edge;
            if (!RCellFinder.TryFindRandomPawnEntryCell(out edge, map, CellFinder.EdgeRoadChance_Animal)) edge = CellFinder.RandomEdgeCell(map);
            OrderEmbark(record, pawn, edge);
            record.log.Add(pawn.LabelShortCap + " departed as a rescuer.");
        }

        public string Status(HuntingExpeditionRecord record)
        {
            if (record == null) return "No active expedition.";
            int remaining = Mathf.Max(0, record.expectedReturnTick - Find.TickManager.TicksGame);
            return StageLabel(record.stage) + " • " + DestinationLabel(record.destinationTile) + " • return in " + remaining.ToStringTicksToPeriod();
        }

        public string Warning(HuntingExpeditionRecord record)
        {
            if (record == null) return null;
            if (record.needsRescue) return "Stranded — rescue recommended";
            if (record.hunters.Any(pawn => pawn != null && !pawn.Dead &&
                pawn.health?.hediffSet?.hediffs?.Any(hediff => hediff is Hediff_Injury) == true))
                return "Injured party member";
            float remainingFood = EstimatedFoodDaysRemaining(record);
            if (remainingFood >= 0f && remainingFood < 0.75f) return "Low provisions";
            if (Find.TickManager.TicksGame > record.expectedReturnTick + 15000) return "Overdue";
            return null;
        }

        public float EstimatedFoodDaysRemaining(HuntingExpeditionRecord record)
        {
            if (record == null || record.dailyNutrition <= 0f) return -1f;
            float used = record.departureTick <= 0 ? 0f :
                Mathf.Max(0f, Find.TickManager.TicksGame - record.departureTick) / 60000f * record.dailyNutrition;
            return Mathf.Max(0f, (record.foodNutrition - used) / record.dailyNutrition);
        }

        private void UpdateAlerts(HuntingExpeditionRecord record, int now)
        {
            if (HerdsMod.Settings?.enableWildlifeAlerts != true || record.stage == ExpeditionStage.Embarking) return;
            float foodDays = EstimatedFoodDaysRemaining(record);
            if (!record.lowFoodAlerted && foodDays >= 0f && foodDays < 0.75f &&
                record.stage != ExpeditionStage.Returning)
            {
                record.lowFoodAlerted = true;
                NotifyExpedition(record, "Wildlife expedition " + record.id +
                    " is running low on provisions.", MessageTypeDefOf.CautionInput);
            }
            if (!record.delayedAlerted && now > record.expectedReturnTick + 15000)
            {
                record.delayedAlerted = true;
                NotifyExpedition(record, "Wildlife expedition " + record.id +
                    " is overdue. Review its status and route.", MessageTypeDefOf.CautionInput);
            }
        }

        public float Progress(HuntingExpeditionRecord record)
        {
            if (record == null) return 0f;
            float stage = record.stage == ExpeditionStage.Embarking ? 0.03f :
                record.stage == ExpeditionStage.OutboundTravel ? 0.15f :
                record.stage == ExpeditionStage.Tracking ? 0.36f :
                record.stage == ExpeditionStage.Stalking ? 0.50f :
                record.stage == ExpeditionStage.Engagement ? 0.62f :
                record.stage == ExpeditionStage.FieldDressing ? 0.72f :
                record.stage == ExpeditionStage.Returning ? 0.84f :
                record.stage == ExpeditionStage.AwaitingRescue ? 0.70f : 0f;
            if (record.nextStageTick > record.stageStartedTick)
                stage += 0.1f * Mathf.Clamp01(Mathf.InverseLerp(record.stageStartedTick, record.nextStageTick, Find.TickManager.TicksGame));
            return Mathf.Clamp01(stage);
        }

        public float EstimateDays(ExpeditionPlan plan)
        {
            if (plan?.destination == null) return 1f;
            List<Pawn> party = plan.hunters.Concat(plan.packAnimals).Where(pawn => pawn != null).Distinct().ToList();
            int oneWay;
            if (party.Count == 0)
            {
                oneWay = Mathf.Max(3000, Mathf.Max(1, plan.destination.distance) * 4500);
            }
            else
            {
                float massUsage = party.Sum(MassUtility.GearAndInventoryMass) +
                    (plan.provisions?.Sum(pair => pair.Key == null ? 0f :
                        pair.Key.GetStatValueAbstract(StatDefOf.Mass) * pair.Value) ?? 0f);
                float massCapacity = party.Sum(pawn => MassUtility.Capacity(pawn, null));
                int ticksPerMove = CaravanTicksPerMoveUtility.GetTicksPerMove(party, massUsage, massCapacity, false, null);
                WorldPath path = map.Tile.Layer.Pather.FindPath(map.Tile, (PlanetTile)plan.destination.tileId, null, null);
                oneWay = path?.Found == true
                    ? CaravanArrivalTimeEstimator.EstimatedTicksToArrive(map.Tile, (PlanetTile)plan.destination.tileId,
                        path, 0f, ticksPerMove, GenTicks.TicksAbs)
                    : Mathf.Max(3000, Mathf.Max(1, plan.destination.distance) * ticksPerMove);
                path?.ReleaseToPool();
            }
            float route = plan.routePolicy == ExpeditionRoutePolicy.Fastest ? 0.82f : plan.routePolicy == ExpeditionRoutePolicy.Safest ? 1.12f : 1f;
            float bedroll = plan.useBedrolls ? 0.94f : 1f;
            int biomeKnowledge = plan.hunters.Select(pawn => map.GetComponent<HuntingKnowledgeMapComponent>()?.BiomeLevel(pawn, plan.destination.biome) ?? 0).DefaultIfEmpty(0).Max();
            int proficiency = plan.hunters.Select(pawn => map.GetComponent<HuntingKnowledgeMapComponent>()?.WildlifeProficiencyLevel(pawn) ?? 0).DefaultIfEmpty(0).Max();
            float learnedRoute = 1f - biomeKnowledge * 0.02f - proficiency * 0.02f;
            float fieldDays = plan.objective == ExpeditionObjective.Scout ? 0.35f : 0.7f;
            return Mathf.Clamp(((oneWay * 2f * route * bedroll) / 60000f + fieldDays) * learnedRoute, 0.25f, 120f);
        }

        private void TickExpedition(HuntingExpeditionRecord record, int now)
        {
            if (record.hunters.All(pawn => pawn == null || pawn.Dead))
            {
                record.result = "The expedition could no longer continue.";
                ReturnParty(record);
                Finish(record, false);
                return;
            }
            RevealTravelProgress(record, now);
            UpdateAlerts(record, now);
            if (record.interactiveEncounterPending)
            {
                ShowInteractiveEncounter(record);
                return;
            }
            if (now < record.interactiveEncounterResumeTick) return;
            if (TryExpeditionEvent(record, now)) return;
            if (record.stage == ExpeditionStage.Embarking)
            {
                List<Pawn> awayHunters = record.hunters.Where(pawn => pawn != null && !pawn.Spawned && !pawn.Dead).ToList();
                bool allPartyAway = record.hunters.Concat(record.packAnimals).Where(pawn => pawn != null && !pawn.Dead).All(pawn => !pawn.Spawned);
                if (awayHunters.Count == record.hunters.Count && allPartyAway)
                {
                    record.departureTick = now;
                    ConsumePackedProvisions(record);
                    BeginTravel(record, ExpeditionStage.OutboundTravel, now);
                    record.log.Add("The party left the colony map.");
                    NotifyExpedition(record, "The wildlife expedition has departed for " + DestinationLabel(record.destinationTile) + ".", MessageTypeDefOf.NeutralEvent);
                }
                else if (now >= record.embarkDeadline)
                {
                    if (awayHunters.Count == 0)
                    {
                        record.result = "No hunters reached the embarkation point.";
                        ReturnParty(record);
                        Finish(record, false);
                    }
                    else
                    {
                        record.departureTick = now;
                        ConsumePackedProvisions(record);
                        record.hunters = awayHunters;
                        record.packAnimals = record.packAnimals.Where(pawn => pawn != null && !pawn.Spawned).ToList();
                        record.bedrolls = Mathf.Min(record.bedrolls, record.hunters.Concat(record.packAnimals).Sum(ExpeditionSupplyUtility.PackedBedrolls));
                        BeginTravel(record, ExpeditionStage.OutboundTravel, now);
                        record.log.Add("The expedition departed without unavailable members.");
                    }
                }
                return;
            }
            if (record.stage == ExpeditionStage.AwaitingRescue)
            {
                if (record.rescueArrivalTick > 0 && now >= record.rescueArrivalTick)
                {
                    record.needsRescue = false;
                    BeginTravel(record, ExpeditionStage.Returning, now);
                    record.log.Add("The rescuer reached the stranded party; everyone started home.");
                    NotifyExpedition(record, "A rescuer reached the stranded expedition. The party is returning.", MessageTypeDefOf.PositiveEvent);
                    return;
                }
                if (now >= record.nextStageTick)
                {
                    record.needsRescue = false;
                    ApplyPartyInjury(record, true);
                    BeginTravel(record, ExpeditionStage.Returning, now);
                    record.log.Add("The stranded party began a difficult return without assistance.");
                }
                return;
            }
            if (record.stage == ExpeditionStage.OutboundTravel || record.stage == ExpeditionStage.Returning)
            {
                PlanetTile target = record.stage == ExpeditionStage.Returning ? map.Tile : (PlanetTile)record.destinationTile;
                if (record.caravan?.Destroyed != false) return;
                if (record.caravan.Tile != target)
                {
                    if (!record.caravan.pather.Moving) record.caravan.pather.StartPath(target, null, true, true);
                    return;
                }
            }
            else if (now < record.nextStageTick) return;
            switch (record.stage)
            {
                case ExpeditionStage.OutboundTravel:
                    if (TryInteractiveEncounter(record, now)) return;
                    if (TryIncident(record, now)) return;
                    if (ResolveBiomeEvent(record, now)) return;
                    BeginStage(record, ExpeditionStage.Tracking, now, TrackingTicks(record));
                    record.log.Add("Tracking began in " + DestinationLabel(record.destinationTile) + ".");
                    NotifyExpedition(record, "The expedition reached " + DestinationLabel(record.destinationTile) + " and began tracking.", MessageTypeDefOf.NeutralEvent);
                    break;
                case ExpeditionStage.Tracking:
                    ResolveTracking(record, now);
                    break;
                case ExpeditionStage.Stalking:
                    BeginStage(record, ExpeditionStage.Engagement, now, 3000);
                    record.log.Add("The party moved into engagement positions.");
                    NotifyExpedition(record, "The expedition located " + (record.actualSpecies?.LabelCap.ToString() ?? "wildlife") + " and is preparing to engage.", MessageTypeDefOf.NeutralEvent);
                    break;
                case ExpeditionStage.Engagement:
                    ResolveObjective(record, now);
                    break;
                case ExpeditionStage.FieldDressing:
                    BeginTravel(record, ExpeditionStage.Returning, now);
                    record.log.Add("Field work finished; the party started home.");
                    NotifyExpedition(record, "The expedition finished field dressing and started home.", MessageTypeDefOf.PositiveEvent);
                    break;
                case ExpeditionStage.Returning:
                    if (ReturnParty(record))
                        Finish(record, record.success);
                    else
                        record.nextStageTick = now + 60;
                    break;
            }
        }

        private void ResolveTracking(HuntingExpeditionRecord record, int now)
        {
            ExpeditionDestination destination = Destination(record.destinationTile, record.distance);
            ExpeditionCellRecord cell = destination.knowledge;
            cell.visits++;
            cell.traversals++;
            cell.discoveryLevel = Mathf.Max(cell.discoveryLevel, 2);
            cell.lastVisitTick = now;
            float distanceLearning = Mathf.Min(0.12f, Mathf.Sqrt(Mathf.Max(1, record.distance)) * 0.012f);
            float surveyGain = (record.objective == ExpeditionObjective.Scout ? 0.32f : 0.14f) + distanceLearning;
            cell.confidence = Mathf.Clamp01(cell.confidence + surveyGain + BestKnowledge(record) * 0.018f);
            RevealWildlifeSigns(destination, record.objective == ExpeditionObjective.Scout ? 3 : 1);
            Discover(record, destination);
            if (record.objective == ExpeditionObjective.Scout)
            {
                record.success = true;
                record.result = "The party completed a regional wildlife survey.";
                GainExperience(record, null, 34f);
                BeginTravel(record, ExpeditionStage.Returning, now);
                NotifyExpedition(record, "The survey is complete and the expedition is returning.", MessageTypeDefOf.PositiveEvent);
                return;
            }
            ThingDef chosen = ChooseEncounterSpecies(record, destination);
            float population = PopulationAt(destination, chosen);
            record.encounterChance = Mathf.Clamp01(0.18f + Mathf.InverseLerp(0f, 25f, population) * 0.58f +
                BestFieldcraft(record) * 0.018f + cell.confidence * 0.12f +
                BestBiomeKnowledge(record, destination.biome) * 0.02f +
                BestWildlifeProficiency(record) * 0.025f +
                (map.GetComponent<WildlifeFieldJournalMapComponent>()?.OutcomeBonus ?? 0f) +
                (record.riskTolerance - 0.5f) * 0.12f + DiscoveryEncounterModifier(cell.discovery) + record.biomeEncounterModifier);
            record.actualSpecies = chosen;
            record.encounterRoll = Rand.Value;
            record.encounterFound = chosen != null && record.encounterRoll < record.encounterChance;
            if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("ExpeditionEncounter",
                "id=" + record.id + " species=" + (chosen?.defName ?? "none") + " chance=" + record.encounterChance.ToString("0.000") +
                " roll=" + record.encounterRoll.ToString("0.000") + " found=" + record.encounterFound);
            if (!record.encounterFound)
            {
                record.result = chosen == null ? "No suitable animals were identified in the destination biome." : "The party found signs but could not locate the animals.";
                GainExperience(record, chosen, 18f);
                BeginTravel(record, ExpeditionStage.Returning, now);
                NotifyExpedition(record, record.result + " The party is returning.", MessageTypeDefOf.NeutralEvent);
                return;
            }
            BeginStage(record, ExpeditionStage.Stalking, now, 5000 + record.distance * 900);
            record.log.Add("Fresh signs of " + chosen.LabelCap + " were found.");
        }

        private void ResolveObjective(HuntingExpeditionRecord record, int now)
        {
            ExpeditionDestination destination = Destination(record.destinationTile, record.distance);
            float fieldcraft = BestFieldcraft(record);
            float specialist = BestSpecialist(record, destination.biome);
            float supplies = ResourceBonus(record);
            float roleBonus = record.objective == ExpeditionObjective.Hunt &&
                record.hunters.Any(WildlifeRoleUtility.IsMasterHunter) ? 0.08f :
                record.objective != ExpeditionObjective.Hunt &&
                record.hunters.Any(WildlifeRoleUtility.IsMasterConservationist) ? 0.10f : 0f;
            float danger = destination.danger + DiscoveryDangerModifier(destination.knowledge.discovery) + record.biomeDangerModifier +
                (record.routePolicy == ExpeditionRoutePolicy.Fastest ? 0.08f : record.routePolicy == ExpeditionRoutePolicy.Safest ? -0.08f : 0f);
            record.successChance = Mathf.Clamp(0.28f + fieldcraft * 0.035f + specialist * 0.035f + supplies * 0.04f -
                danger - Mathf.Min(0.32f, Mathf.Sqrt(Mathf.Max(1, record.distance)) * 0.035f) +
                BestBiomeKnowledge(record, destination.biome) * 0.02f +
                BestWildlifeProficiency(record) * 0.025f +
                (map.GetComponent<WildlifeFieldJournalMapComponent>()?.OutcomeBonus ?? 0f) +
                (record.riskTolerance - 0.5f) * 0.08f +
                DiscoverySuccessModifier(destination.knowledge.discovery, record.objective) +
                record.biomeSuccessModifier + roleBonus, 0.08f, 0.94f);
            if (record.objective == ExpeditionObjective.Capture) record.successChance -= 0.12f;
            if (record.objective == ExpeditionObjective.Tag) record.successChance -= 0.04f;
            record.successChance = Mathf.Clamp(record.successChance, 0.05f, 0.94f);
            record.successRoll = Rand.Value;
            record.success = record.successRoll < record.successChance;
            if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("ExpeditionEngagement",
                "id=" + record.id + " objective=" + record.objective + " chance=" + record.successChance.ToString("0.000") +
                " roll=" + record.successRoll.ToString("0.000") + " success=" + record.success);
            float impact = 0f;
            if (record.success)
            {
                if (record.objective == ExpeditionObjective.Hunt)
                {
                    float carrying = record.hunters.Count * 18f + record.packAnimals.Sum(pawn => Mathf.Max(12f, pawn.BodySize * 35f));
                    record.meat = Mathf.RoundToInt(Mathf.Min(carrying * 0.72f, 18f + record.actualSpecies.race.baseBodySize * 42f));
                    record.leather = record.actualSpecies.race.leatherDef == null ? 0 : Mathf.RoundToInt(Mathf.Min(carrying * 0.28f, 8f + record.actualSpecies.race.baseBodySize * 20f));
                    ResolveSpecialHuntReward(record, destination);
                    impact = -1f;
                    record.result = "The hunt succeeded and the party recovered its quarry." +
                        (record.bonusReward.NullOrEmpty() ? string.Empty : " " + record.bonusReward);
                }
                else if (record.objective == ExpeditionObjective.Capture)
                {
                    record.captureReady = true;
                    impact = -1f;
                    record.result = "The target was captured alive for transport.";
                }
                else if (record.objective == ExpeditionObjective.Tag)
                {
                    destination.knowledge.confidence = Mathf.Clamp01(destination.knowledge.confidence + 0.28f);
                    record.result = "The party tagged an animal and recorded its route.";
                }
                else if (record.objective == ExpeditionObjective.Protect)
                {
                    impact = 1.5f;
                    record.result = "The party secured breeding habitat and disrupted threats.";
                }
                else if (record.objective == ExpeditionObjective.Redirect)
                {
                    impact = -0.5f;
                    map.GetComponent<RegionalWildlifeMapComponent>()?.ApplyExpeditionImpact(record.actualSpecies, 0.8f, 0.12f);
                    record.result = "The party redirected part of the population toward the colony region.";
                }
            }
            else record.result = "The animals evaded the party after the engagement.";
            NotifyExpedition(record, record.result, record.success ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.NegativeEvent);
            ApplyPopulation(destination, record.actualSpecies, impact);
            GainExperience(record, record.actualSpecies, record.success ? 70f : 30f);
            if (HerdsMod.Settings.enableExpeditionIncidents && Rand.Chance(Mathf.Clamp01(0.06f + danger * 0.24f + record.riskTolerance * 0.08f - record.medicine * 0.025f)))
                ApplyPartyInjury(record, false);
            if (record.objective == ExpeditionObjective.Hunt && record.success)
                BeginStage(record, ExpeditionStage.FieldDressing, now, 5000);
            else
            {
                BeginTravel(record, ExpeditionStage.Returning, now);
                NotifyExpedition(record, "The wildlife expedition has started home.", MessageTypeDefOf.NeutralEvent);
            }
        }

        private bool TryIncident(HuntingExpeditionRecord record, int now)
        {
            if (record.incidentResolved || !HerdsMod.Settings.enableExpeditionIncidents) return false;
            record.incidentResolved = true;
            ExpeditionDestination destination = Destination(record.destinationTile, record.distance);
            float medicineBonus = record.medicine * 0.018f;
            record.incidentChance = Mathf.Clamp01(destination.danger + DiscoveryDangerModifier(destination.knowledge.discovery) +
                record.biomeDangerModifier + Mathf.Min(0.25f, record.distance * 0.003f) - medicineBonus -
                record.extraFoodDays * 0.03f - BestBiomeKnowledge(record, destination.biome) * 0.03f -
                BestWildlifeProficiency(record) * 0.025f + record.riskTolerance * 0.05f);
            if (record.routePolicy == ExpeditionRoutePolicy.Safest) record.incidentChance *= 0.62f;
            record.incidentRoll = Rand.Value;
            if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("ExpeditionIncident",
                "id=" + record.id + " chance=" + record.incidentChance.ToString("0.000") + " roll=" + record.incidentRoll.ToString("0.000"));
            if (record.incidentRoll >= record.incidentChance) return false;
            int kind = Rand.Range(0, 4);
            if (kind == 0)
            {
                record.nextStageTick = now + 12000;
                record.log.Add("Severe weather delayed the route.");
                NotifyExpedition(record, "Severe weather delayed the wildlife expedition.", MessageTypeDefOf.CautionInput);
                return true;
            }
            if (kind == 1)
            {
                ApplyPartyInjury(record, false);
                record.log.Add("A dangerous animal encounter caused injuries.");
                NotifyExpedition(record, "A dangerous animal encounter injured a member of the expedition.", MessageTypeDefOf.ThreatSmall);
                return false;
            }
            if (kind == 2 && record.distance >= 2)
            {
                record.stage = ExpeditionStage.AwaitingRescue;
                record.stageStartedTick = now;
                record.nextStageTick = now + 60000;
                record.expectedReturnTick += 60000;
                record.needsRescue = true;
                record.log.Add("The party became stranded and can be assisted from Wildlife Expeditions.");
                Messages.Message("A wildlife expedition is stranded near " + DestinationLabel(record.destinationTile) + ". Open Wildlife Expeditions to send assistance.", MessageTypeDefOf.ThreatSmall, false);
                WildlifeExperience.Record("Expedition", "A wildlife expedition became stranded.", null, true);
                return true;
            }
            record.log.Add("Spoiled provisions slowed the party.");
            NotifyExpedition(record, "Spoiled provisions delayed the wildlife expedition.", MessageTypeDefOf.CautionInput);
            record.nextStageTick = now + 8000;
            return true;
        }

        private bool TryInteractiveEncounter(HuntingExpeditionRecord record, int now)
        {
            if (record.interactiveEncounterResolved || !HerdsMod.Settings.enableInteractiveExpeditionEncounters)
            {
                record.interactiveEncounterResolved = true;
                return false;
            }
            int seed = Gen.HashCombineInt(record.destinationTile, record.id * 7919);
            ExpeditionDestination encounterDestination = Destination(record.destinationTile, record.distance);
            if (record.trailTargetAnimal != null && !record.trailTargetAnimal.Dead &&
                record.trailTargetAnimal.Spawned != true)
            {
                record.roamingEncounterAnimal = record.trailTargetAnimal;
                record.interactiveEncounter = "Trail Target";
                record.interactiveEncounterPending = true;
                record.interactiveEncounterWindowOpen = false;
                record.log.Add("The party caught up with the exact animal identified by the trail study.");
                ShowInteractiveEncounter(record);
                return true;
            }
            if (HerdsMod.Settings.enableRoamingExpeditionEncounters)
            {
                RegionalWildlifeMapComponent regional = map.GetComponent<RegionalWildlifeMapComponent>();
                RoamingAnimalRecord roamer = regional?.RoamingAnimals.Where(value =>
                    value?.animal != null && !value.animal.Dead &&
                    value.state != RoamingAnimalState.Present &&
                    (record.targetSpecies == null || value.species == record.targetSpecies))
                    .OrderBy(value => Mathf.Abs(value.expectedReturnTick - now)).FirstOrDefault();
                float roamingChance = roamer == null ? 0f : Mathf.Clamp01(0.16f + BestFieldcraft(record) * 0.015f);
                if (roamer != null && Rand.ChanceSeeded(roamingChance, Gen.HashCombineInt(record.id, now / 60000)))
                {
                    record.roamingEncounterAnimal = roamer.animal;
                    record.interactiveEncounter = "Known Roaming Animal";
                    record.interactiveEncounterPending = true;
                    record.interactiveEncounterWindowOpen = false;
                    record.log.Add("The party encountered " + roamer.animal.LabelShortCap + " while it was roaming.");
                    ShowInteractiveEncounter(record);
                    return true;
                }
            }
            record.interactiveEncounterResolved = true;
            return false;
        }

        private bool TryExpeditionEvent(HuntingExpeditionRecord record, int now)
        {
            if (!HerdsMod.Settings.enableInteractiveExpeditionEncounters ||
                record.stage == ExpeditionStage.Embarking ||
                record.stage == ExpeditionStage.AwaitingRescue ||
                record.eventCount >= 2 || now < record.nextEventCheckTick)
                return false;
            record.nextEventCheckTick = now + 12000;
            BiomeDef biome = Destination(record.destinationTile, record.distance).biome;
            List<ExpeditionEventDef> candidates = DefDatabase<ExpeditionEventDef>.AllDefsListForReading
                .Where(def => def.Applies(record, biome)).ToList();
            if (candidates.Count == 0) return false;
            int seed = Gen.HashCombineInt(record.id * 7919, now / 12000 + record.eventCount * 101);
            ExpeditionEventDef eventDef = candidates[Mathf.Abs(seed) % candidates.Count];
            float chance = Mathf.Clamp01(eventDef.chance + record.riskTolerance * 0.04f);
            if (!Rand.ChanceSeeded(chance, seed)) return false;
            record.pendingEvent = eventDef;
            record.interactiveEncounter = eventDef.LabelCap;
            record.interactiveEncounterPending = true;
            record.interactiveEncounterWindowOpen = false;
            record.eventCount++;
            record.log.Add("Expedition event: " + eventDef.LabelCap + ".");
            ShowInteractiveEncounter(record);
            return true;
        }

        private void ShowInteractiveEncounter(HuntingExpeditionRecord record)
        {
            if (record?.interactiveEncounterPending != true || record.interactiveEncounterWindowOpen) return;
            record.interactiveEncounterWindowOpen = true;
            Find.WindowStack.Add(new Window_InteractiveExpeditionEncounter(this, record));
        }

        public void ResolveInteractiveEncounter(HuntingExpeditionRecord record, int choice)
        {
            if (record?.interactiveEncounterPending != true || !expeditions.Contains(record)) return;
            if (record.roamingEncounterAnimal != null)
            {
                ResolveRoamingEncounter(record, choice);
                return;
            }
            if (record.pendingEvent != null)
            {
                ResolveExpeditionEvent(record, choice);
                return;
            }
            string result;
            if (choice == 0)
            {
                record.biomeEncounterModifier += 0.14f;
                record.biomeSuccessModifier += 0.05f;
                record.biomeDangerModifier += 0.07f;
                if (record.objective == ExpeditionObjective.Scout)
                    Destination(record.destinationTile, record.distance).knowledge.confidence =
                        Mathf.Clamp01(Destination(record.destinationTile, record.distance).knowledge.confidence + 0.10f);
                if (record.objective == ExpeditionObjective.Capture || record.objective == ExpeditionObjective.Tag)
                    record.biomeSuccessModifier += 0.05f;
                if (record.objective == ExpeditionObjective.Redirect)
                    record.biomeEncounterModifier += 0.07f;
                record.interactiveEncounterResumeTick = Find.TickManager.TicksGame + 5000;
                result = "The party followed the signs, improving its chances of finding game but accepting greater danger.";
            }

            else if (choice == 1)
            {
                ExpeditionDestination destination = Destination(record.destinationTile, record.distance);
                destination.knowledge.confidence = Mathf.Clamp01(destination.knowledge.confidence + 0.16f);
                record.biomeDangerModifier = Mathf.Max(-0.12f, record.biomeDangerModifier - 0.04f);
                record.interactiveEncounterResumeTick = Find.TickManager.TicksGame + 2800;
                result = "The party documented the signs and chose a measured route.";
                for (int i = 0; i < record.hunters.Count; i++)
                    if (record.hunters[i] != null && record.targetSpecies != null)
                        map.GetComponent<HuntingKnowledgeMapComponent>()?.Learn(record.hunters[i], record.targetSpecies, 12f);
            }
            else
            {
                record.biomeDangerModifier = Mathf.Max(-0.18f, record.biomeDangerModifier - 0.10f);
                record.biomeEncounterModifier -= 0.05f;
                record.interactiveEncounterResumeTick = Find.TickManager.TicksGame + 7000;
                result = "The party made a careful detour, reducing danger at the cost of time and opportunity.";
            }
            record.interactiveEncounterPending = false;
            record.interactiveEncounterResolved = true;
            record.interactiveEncounterWindowOpen = false;
            record.expectedReturnTick += Mathf.Max(0, record.interactiveEncounterResumeTick - Find.TickManager.TicksGame);
            record.log.Add(result);
            WildlifeExperience.Record("Expedition Decision", result, record.spot);
            if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("ExpeditionDecision",
                "id=" + record.id + " event=" + record.interactiveEncounter + " choice=" + choice);
        }

        private void ResolveExpeditionEvent(HuntingExpeditionRecord record, int choiceIndex)
        {
            ExpeditionEventDef eventDef = record.pendingEvent;
            if (eventDef?.choices == null || choiceIndex < 0 || choiceIndex >= eventDef.choices.Count)
                return;
            ExpeditionEventChoiceDef choice = eventDef.choices[choiceIndex];
            int now = Find.TickManager.TicksGame;
            record.biomeEncounterModifier += choice.encounterModifier;
            record.biomeSuccessModifier += choice.successModifier;
            record.biomeDangerModifier += choice.dangerModifier;
            record.interactiveEncounterResumeTick = now + Mathf.Max(0, choice.delayTicks);
            record.expectedReturnTick += Mathf.Max(0, choice.delayTicks);
            if (choice.knowledgeGain > 0f && record.targetSpecies != null)
                foreach (Pawn hunter in record.hunters.Where(pawn => pawn != null))
                    map.GetComponent<HuntingKnowledgeMapComponent>()?
                        .Learn(hunter, record.targetSpecies, choice.knowledgeGain);
            if (choice.injureParty) ApplyPartyInjury(record, false);
            string result = choice.result.NullOrEmpty() ?
                "The party chose " + choice.label + "." : choice.result;
            record.log.Add(result);
            record.pendingEvent = null;
            record.interactiveEncounterPending = false;
            record.interactiveEncounterWindowOpen = false;
            WildlifeExperience.Record("Expedition Decision", result, record.spot);
            if (choice.turnBack)
            {
                record.result = result;
                BeginTravel(record, ExpeditionStage.Returning, now);
            }
        }

        private void ResolveRoamingEncounter(HuntingExpeditionRecord record, int choice)
        {
            RegionalWildlifeMapComponent regional = map.GetComponent<RegionalWildlifeMapComponent>();
            RoamingAnimalRecord roamer = regional?.RoamingAnimals.FirstOrDefault(value =>
                value.animal == record.roamingEncounterAnimal);
            Pawn animal = record.roamingEncounterAnimal;
            string result;
            if (choice == 0)
            {
                regional?.ApplyExpeditionImpact(animal.def, 0f, 0.16f);
                foreach (Pawn hunter in record.hunters)
                    map.GetComponent<HuntingKnowledgeMapComponent>()?.Learn(hunter, animal.def, 18f);
                result = "The party observed " + animal.LabelShortCap + " without disturbing it.";
            }
            else if (choice == 1)
            {
                if (roamer != null)
                {
                    roamer.expectedReturnTick = Mathf.Max(Find.TickManager.TicksGame + 12000,
                        roamer.expectedReturnTick - 60000);
                    regional.EncourageReturn(roamer);
                }
                result = "The party helped " + animal.LabelShortCap + " and improved its trust and chance of returning.";
            }
            else if (choice == 2)
            {
                float skill = BestFieldcraft(record);
                bool success = Rand.ChanceSeeded(Mathf.Clamp01(0.25f + skill * 0.035f),
                    Gen.HashCombineInt(record.id, animal.thingIDNumber));
                if (success)
                {
                    animal.Kill(null);
                    if (roamer != null) roamer.state = RoamingAnimalState.Dead;
                    regional?.ApplyExpeditionImpact(animal.def, -1f, 0.08f);
                    result = "The party successfully hunted " + animal.LabelShortCap + ".";
                }
                else
                {
                    record.biomeDangerModifier += 0.08f;
                    result = animal.LabelShortCap + " escaped the attempted hunt.";
                }
            }
            else if (choice == 3)
            {
                if (roamer != null && HerdsDefOf.Herds_TrackingCollar != null)
                {
                    roamer.tagged = true;
                    if (animal.health.hediffSet.GetFirstHediffOfDef(HerdsDefOf.Herds_TrackingCollar) == null)
                        animal.health.AddHediff(HerdsDefOf.Herds_TrackingCollar);
                }
                result = "The party tagged " + animal.LabelShortCap + " for future telemetry.";
            }
            else if (choice == 4)
            {
                if (roamer != null)
                {
                    roamer.direction = "Toward managed habitat";
                    regional.EncourageReturn(roamer);
                }
                result = "The party redirected " + animal.LabelShortCap + " toward colony-managed habitat.";
            }
            else result = "The party avoided " + animal.LabelShortCap + " and continued without disturbance.";
            record.interactiveEncounterResumeTick = Find.TickManager.TicksGame + (choice == 5 ? 1000 : 3500);
            record.interactiveEncounterPending = false;
            record.interactiveEncounterResolved = true;
            record.interactiveEncounterWindowOpen = false;
            record.log.Add(result);
            WildlifeExperience.Record("Roaming Encounter", result, record.spot, choice == 2);
            if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("RoamingEncounter",
                "id=" + record.id + " animal=" + animal.thingIDNumber + " choice=" + choice);
        }

        private bool ResolveBiomeEvent(HuntingExpeditionRecord record, int now)
        {
            if (record.biomeEvent != null) return false;
            if (!HerdsMod.Settings.enableExpeditionBiomeEvents)
            {
                record.biomeEvent = "Disabled";
                return false;
            }
            ExpeditionDestination destination = Destination(record.destinationTile, record.distance);
            string biome = destination.biome?.label?.ToLowerInvariant() ?? string.Empty;
            if (!Rand.Chance(0.38f))
            {
                record.biomeEvent = "No unusual biome conditions";
                return false;
            }
            int delay = 0;
            if (biome.Contains("desert") || biome.Contains("arid"))
            {
                record.biomeEvent = "Water shortage";
                record.biomeDangerModifier = 0.08f;
                delay = 5000;
            }
            else if (biome.Contains("ice") || biome.Contains("tundra") || biome.Contains("cold"))
            {
                record.biomeEvent = "Cold front";
                record.biomeDangerModifier = 0.10f;
                delay = 6500;
            }
            else if (biome.Contains("swamp") || biome.Contains("marsh"))
            {
                record.biomeEvent = "Bog crossing";
                record.biomeDangerModifier = 0.06f;
                record.biomeEncounterModifier = 0.04f;
                delay = 4500;
            }
            else if (biome.Contains("jungle") || biome.Contains("forest") || biome.Contains("wood"))
            {
                record.biomeEvent = "Dense animal trails";
                record.biomeEncounterModifier = 0.09f;
                record.biomeSuccessModifier = 0.04f;
            }
            else if (biome.Contains("mountain") || biome.Contains("hill"))
            {
                record.biomeEvent = "High game trail";
                record.biomeEncounterModifier = 0.06f;
                record.biomeDangerModifier = 0.05f;
            }
            else
            {
                record.biomeEvent = "Fresh migration signs";
                record.biomeEncounterModifier = 0.06f;
            }
            record.log.Add("Biome event: " + record.biomeEvent + ".");
            if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("ExpeditionBiome",
                "id=" + record.id + " event=" + record.biomeEvent + " encounterMod=" + record.biomeEncounterModifier.ToString("0.000") +
                " dangerMod=" + record.biomeDangerModifier.ToString("0.000") + " successMod=" + record.biomeSuccessModifier.ToString("0.000"));
            NotifyExpedition(record, "Expedition field report: " + record.biomeEvent + " in " + destination.biome.LabelCap + ".", delay > 0 ? MessageTypeDefOf.CautionInput : MessageTypeDefOf.NeutralEvent);
            if (delay > 0)
            {
                record.nextStageTick = now + delay;
                record.expectedReturnTick += delay;
                return true;
            }
            return false;
        }

        private void ApplyPartyInjury(HuntingExpeditionRecord record, bool serious)
        {
            Pawn pawn = record.hunters.Where(candidate => candidate != null && !candidate.Dead).RandomElementWithFallback();
            BodyPartRecord part = pawn?.health?.hediffSet?.GetNotMissingParts().Where(item => item.depth == BodyPartDepth.Outside).RandomElementWithFallback();
            if (pawn == null || part == null) return;
            Hediff injury = HediffMaker.MakeHediff(HediffDefOf.Cut, pawn, part);
            injury.Severity = serious ? Rand.Range(7f, 14f) : Rand.Range(2f, 7f);
            pawn.health.AddHediff(injury, part);
            UseMedicine(record);
            record.log.Add(pawn.LabelShortCap + " was " + (serious ? "seriously " : "") + "injured.");
            if (serious && HerdsMod.Settings.allowExpeditionDeaths && record.riskTolerance > 0.72f && Rand.Chance(0.08f))
            {
                pawn.Kill(null);
                record.log.Add(pawn.LabelShortCap + " died during the expedition.");
                NotifyExpedition(record, pawn.LabelShortCap + " died during the wildlife expedition.", MessageTypeDefOf.ThreatBig);
            }
        }

        private ThingDef ChooseEncounterSpecies(HuntingExpeditionRecord record, ExpeditionDestination destination)
        {
            if (record.targetSpecies != null && PopulationAt(destination, record.targetSpecies) > 0.1f) return record.targetSpecies;
            if (!record.unknownTarget && !record.allowAlternatives) return record.targetSpecies;
            IEnumerable<ThingDef> candidates = record.unknownTarget
                ? ValidWildAnimals(destination.biome).Select(kind => kind.race)
                : KnownSpecies(destination);
            if (record.objective == ExpeditionObjective.Redirect) candidates = candidates.Where(IsHerdSpecies);
            List<ThingDef> available = candidates.Distinct().Where(species => PopulationAt(destination, species) > 0.1f).ToList();
            return available.Count == 0 ? null : available.RandomElementByWeight(species => Mathf.Max(0.05f, PopulationAt(destination, species)));
        }

        private void Discover(HuntingExpeditionRecord record, ExpeditionDestination destination)
        {
            if (record.objective == ExpeditionObjective.Scout && HerdsMod.Settings.enableSpeciesKnowledgeProgression)
            {
                ThingDef unknown = ValidWildAnimals(destination.biome)
                    .Where(kind => HuntingKnowledgeMapComponent.ColonyExperience(kind.race) <= 0f)
                    .OrderByDescending(kind => destination.biome.CommonalityOfAnimal(kind))
                    .ThenBy(kind => Mathf.Abs(Gen.HashCombineInt(destination.tileId, kind.race.shortHash)))
                    .Select(kind => kind.race).FirstOrDefault();
                if (unknown != null)
                {
                    PopulationAt(destination, unknown);
                    HuntingKnowledgeMapComponent knowledge = map.GetComponent<HuntingKnowledgeMapComponent>();
                    for (int i = 0; i < record.hunters.Count; i++) if (record.hunters[i] != null) knowledge?.Learn(record.hunters[i], unknown, 40f);
                    record.log.Add("Identified previously unknown " + unknown.LabelCap + " signs.");
                }
            }
            if (!HerdsMod.Settings.enableExtendedHuntingExpeditions || destination.knowledge.discovery != null || !Rand.Chance(0.34f)) return;
            string biome = destination.biome?.label?.ToLowerInvariant() ?? string.Empty;
            string[] discoveries = biome.Contains("desert") || biome.Contains("arid")
                ? new[] { "Watering site", "Migration route", "Sheltered field camp", "Rare wildlife signs", "Predator territory" }
                : biome.Contains("forest") || biome.Contains("jungle") || biome.Contains("wood")
                    ? new[] { "Breeding ground", "Nesting colony", "Abandoned kill", "Predator territory", "Rare wildlife signs" }
                    : biome.Contains("swamp") || biome.Contains("marsh")
                        ? new[] { "Watering site", "Nesting colony", "Injured wildlife", "Dense predator concentration" }
                        : biome.Contains("ice") || biome.Contains("tundra")
                            ? new[] { "Migration route", "Sheltered field camp", "Abandoned kill", "Rare wildlife signs" }
                            : new[] { "Migration route", "Breeding ground", "Watering site", "Sheltered field camp", "Predator territory",
                                "Rare wildlife signs", "Nesting colony", "Abandoned kill", "Injured wildlife", "Dense predator concentration" };
            destination.knowledge.discovery = discoveries[Mathf.Abs(Gen.HashCombineInt(destination.tileId, destination.knowledge.visits)) % discoveries.Length];
            record.log.Add("Discovered: " + destination.knowledge.discovery + ".");
            Messages.Message("Expedition discovery: " + destination.knowledge.discovery + " in " + destination.biome.LabelCap + ".", record.spot, MessageTypeDefOf.PositiveEvent, false);
        }

        private void ApplyPopulation(ExpeditionDestination destination, ThingDef species, float delta)
        {
            if (destination == null || species == null || Mathf.Approximately(delta, 0f)) return;
            ExpeditionCellSpeciesRecord cellSpecies = destination.knowledge.species.FirstOrDefault(item => item.species == species);
            if (cellSpecies == null)
            {
                PopulationAt(destination, species);
                cellSpecies = destination.knowledge.species.FirstOrDefault(item => item.species == species);
            }
            if (cellSpecies != null) cellSpecies.population = Mathf.Max(0f, cellSpecies.population + delta);
            if (destination.distance == 1) map.GetComponent<RegionalWildlifeMapComponent>()?.ApplyExpeditionImpact(species, delta, 0.08f);
        }

        private void GainExperience(HuntingExpeditionRecord record, ThingDef species, float amount)
        {
            BiomeDef biome = Destination(record.destinationTile, record.distance).biome;
            HuntingKnowledgeMapComponent knowledge = map.GetComponent<HuntingKnowledgeMapComponent>();
            for (int i = 0; i < record.hunters.Count; i++)
            {
                Pawn hunter = record.hunters[i];
                if (hunter == null || hunter.Dead) continue;
                ExpeditionSpecialistRecord specialist = specialists.FirstOrDefault(item => item.pawn == hunter && item.biome == biome);
                if (specialist == null)
                {
                    specialist = new ExpeditionSpecialistRecord { pawn = hunter, biome = biome };
                    specialists.Add(specialist);
                }
                specialist.experience += amount;
                knowledge?.LearnBiome(hunter, biome, amount * (record.objective == ExpeditionObjective.Scout ? 0.9f : 0.55f), record.success);
                if (species != null && HerdsMod.Settings.enableSpeciesKnowledgeProgression) knowledge?.Learn(hunter, species, amount * 0.55f, record.success, !record.success);
            }
        }

        private void ResolveSpecialHuntReward(HuntingExpeditionRecord record, ExpeditionDestination destination)
        {
            if (HerdsMod.Settings.enableHuntRewards != true || record?.actualSpecies == null) return;
            float journal = map.GetComponent<WildlifeFieldJournalMapComponent>()?.OutcomeBonus ?? 0f;
            float chance = Mathf.Clamp(0.10f + BestFieldcraft(record) * 0.012f +
                BestWildlifeProficiency(record) * 0.05f + journal, 0.10f, 0.55f);
            if (!Rand.Chance(chance)) return;
            int reward = Rand.Range(0, 4);
            if (reward == 0 && record.actualSpecies.race.leatherDef != null)
            {
                record.bonusLeather = Mathf.Max(2, Mathf.RoundToInt(record.leather * 0.20f));
                record.bonusReward = "Careful field dressing preserved " + record.bonusLeather + " additional quality hides.";
            }
            else if (reward == 1 && HerdsDefOf.Herds_WildlifeTrophy != null)
            {
                record.trophies = 1;
                record.bonusReward = "The party recovered a display-worthy wildlife trophy.";
            }
            else if (reward == 2)
            {
                HuntingKnowledgeMapComponent knowledge = map.GetComponent<HuntingKnowledgeMapComponent>();
                for (int i = 0; i < record.hunters.Count; i++)
                    knowledge?.Learn(record.hunters[i], record.actualSpecies, 28f, true);
                record.bonusReward = "Useful specimens substantially improved Animal Knowledge.";
            }
            else
            {
                destination.knowledge.confidence = Mathf.Clamp01(destination.knowledge.confidence + 0.12f);
                if (destination.knowledge.discovery.NullOrEmpty()) destination.knowledge.discovery = "Sheltered field camp";
                record.bonusReward = "The party established a useful field camp and documented the surrounding habitat.";
            }
            record.log.Add("Special reward: " + record.bonusReward);
        }

        private bool ReturnParty(HuntingExpeditionRecord record)
        {
            IntVec3 entry;
            if (!RCellFinder.TryFindRandomPawnEntryCell(out entry, map, CellFinder.EdgeRoadChance_Animal)) entry = CellFinder.RandomEdgeCell(map);
            bool allReturned = true;
            foreach (Pawn pawn in record.Party.ToList())
            {
                if (pawn == null || pawn.Dead || pawn.Spawned) continue;
                try
                {
                    if (record.caravan?.ContainsPawn(pawn) == true) record.caravan.RemovePawn(pawn);
                    IntVec3 returnCell = CellFinder.RandomClosewalkCellNear(entry, map, 4);
                    if (!returnCell.IsValid || !returnCell.Standable(map)) returnCell = entry;
                    GenSpawn.Spawn(pawn, returnCell, map, Rot4.Random);
                    if (Find.WorldPawns.Contains(pawn)) Find.WorldPawns.RemovePawn(pawn);
                    if (pawn.RaceProps?.Humanlike == true && HerdsDefOf.Herds_HuntFatigue != null) pawn.health.AddHediff(HerdsDefOf.Herds_HuntFatigue);
                }
                catch (Exception exception)
                {
                    allReturned = false;
                    Log.Error("[Wildlife] Could not return expedition member " + pawn.LabelShortCap + ": " + exception);
                }
            }
            if (!allReturned || record.Party.Any(pawn => pawn != null && !pawn.Dead && !pawn.Spawned)) return false;
            if (record.caravan != null && !record.caravan.Destroyed) record.caravan.Destroy();
            IntVec3 lootCell = record.spot?.Spawned == true ? record.spot.Position : entry;
            SpawnStack(record.actualSpecies?.race?.meatDef, record.meat, lootCell);
            SpawnStack(record.actualSpecies?.race?.leatherDef, record.leather + record.bonusLeather, lootCell);
            SpawnStack(HerdsDefOf.Herds_WildlifeTrophy, record.trophies, lootCell);
            if (record.captureReady && record.actualSpecies != null)
            {
                PawnKindDef kind = DefDatabase<PawnKindDef>.AllDefsListForReading.FirstOrDefault(def => def.race == record.actualSpecies);
                if (kind != null)
                {
                    Pawn captured = PawnGenerator.GeneratePawn(kind, null);
                    GenSpawn.Spawn(captured, CellFinder.RandomClosewalkCellNear(entry, map, 4), map, Rot4.Random);
                    HealthUtility.DamageUntilDowned(captured, false);
                }
            }
            if (record.medicineManifest?.Count > 0)
            {
                for (int i = 0; i < record.medicineManifest.Count; i++)
                    SpawnStack(record.medicineManifest[i].def, record.medicineManifest[i].count, lootCell);
            }
            else
            {
                ThingDef medicineDef = null;
                string medicineEntry = record.resources.FirstOrDefault(value => value.StartsWith("medicine:"));
                if (medicineEntry != null) medicineDef = DefDatabase<ThingDef>.GetNamedSilentFail(medicineEntry.Substring("medicine:".Length));
                SpawnStack(medicineDef, record.medicine, lootCell);
            }
            return true;
        }

        private void Finish(HuntingExpeditionRecord record, bool positive)
        {
            foreach (Pawn pawn in record.Party.Where(pawn => pawn?.Spawned == true))
                if (pawn.CurJobDef == HerdsDefOf.Herds_EmbarkHuntingExpedition)
                    pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
            string target = record.actualSpecies?.LabelCap.ToString() ?? record.targetSpecies?.LabelCap.ToString() ?? "wildlife";
            string summary = record.objective + " expedition for " + target + ": " + (record.result ?? "completed.");
            history.Insert(0, "Day " + (Find.TickManager.TicksGame / 60000 + 1) + " — " + summary);
            if (history.Count > 20) history.RemoveRange(20, history.Count - 20);
            Messages.Message(summary, record.spot ?? (Thing)record.hunters.FirstOrDefault(pawn => pawn?.Spawned == true), positive ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.NeutralEvent, false);
            WildlifeExperience.Record("Expedition", summary, record.spot, !positive);
            WildlifeMysteryUtility.NotifyExpedition(map, record.actualSpecies ?? record.targetSpecies,
                record.objective, positive);
            if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("ExpeditionEnd", "id=" + record.id + " success=" + positive + " result=" + record.result + " meat=" + record.meat + " leather=" + record.leather);
            if (record.marker != null && !record.marker.Destroyed) record.marker.Destroy();
            if (record.caravan != null && !record.caravan.Destroyed) record.caravan.Destroy();
            expeditions.Remove(record);
            if (map.Tile.Valid) map.Tile.Layer.SetDirty<WorldDrawLayer_WildlifeExpeditionRoutes>();
        }

        private static void ConsumePackedProvisions(HuntingExpeditionRecord record)
        {
            if (record?.packedProvisions == null) return;
            // Food remains in the expedition caravan and is consumed normally.
            record.packedProvisions.Clear();
        }

        private static void UseMedicine(HuntingExpeditionRecord record)
        {
            if (record?.medicineManifest != null)
            {
                ExpeditionCargoEntry entry = record.medicineManifest.Where(item => item?.def != null && item.count > 0)
                    .OrderBy(item => item.def.GetStatValueAbstract(StatDefOf.MedicalPotency)).FirstOrDefault();
                if (entry != null) entry.count--;
            }
            if (record != null) record.medicine = Mathf.Max(0, record.medicine - 1);
        }

        private void BeginStage(HuntingExpeditionRecord record, ExpeditionStage stage, int now, int duration)
        {
            record.stage = stage;
            record.stageStartedTick = now;
            record.nextStageTick = now + Mathf.Max(60, duration);
        }

        private void BeginTravel(HuntingExpeditionRecord record, ExpeditionStage stage, int now)
        {
            PlanetTile target = stage == ExpeditionStage.Returning ? map.Tile : (PlanetTile)record.destinationTile;
            int duration = TravelTicks(record, target);
            BeginStage(record, stage, now, duration);
            if (stage == ExpeditionStage.Returning)
                record.expectedReturnTick = record.nextStageTick;
            else
                record.expectedReturnTick = now + duration * 2 +
                    (record.objective == ExpeditionObjective.Scout ? 21000 : 42000);
            if (record.caravan?.Destroyed == false && record.caravan.Tile != target)
                record.caravan.pather.StartPath(target, null, true, true);
        }

        private int TravelTicks(HuntingExpeditionRecord record)
        {
            PlanetTile target = record.stage == ExpeditionStage.Returning ? map.Tile : (PlanetTile)record.destinationTile;
            return TravelTicks(record, target);
        }

        private int TravelTicks(HuntingExpeditionRecord record, PlanetTile target)
        {
            if (record.caravan?.Destroyed == false)
            {
                if (record.caravan.Tile == target) return 60;
                int estimate = CaravanArrivalTimeEstimator.EstimatedTicksToArrive(record.caravan.Tile, target, record.caravan);
                if (estimate > 0) return estimate;
            }
            ExpeditionDestination destination = Destination(record.destinationTile, record.distance);
            float route = record.routePolicy == ExpeditionRoutePolicy.Fastest ? 0.82f :
                record.routePolicy == ExpeditionRoutePolicy.Safest ? 1.12f : 1f;
            return Mathf.RoundToInt(Mathf.Clamp(16500f * record.distance * destination.travelFactor * route, 9000f, 3600000f));
        }

        private int TrackingTicks(HuntingExpeditionRecord record) =>
            Mathf.RoundToInt(Mathf.Clamp(7000f - BestFieldcraft(record) * 180f -
                BestBiomeKnowledge(record, Destination(record.destinationTile, record.distance).biome) * 300f +
                record.distance * 900f, 2400f, 16000f));

        private float BestFieldcraft(HuntingExpeditionRecord record) =>
            record.hunters.Where(pawn => pawn != null && !pawn.Dead).Select(pawn => record.actualSpecies == null ? ColonistHuntingUtility.HuntingSkill(pawn) : ColonistHuntingUtility.HuntingSkill(pawn, record.actualSpecies)).DefaultIfEmpty(0f).Max();

        private int BestKnowledge(HuntingExpeditionRecord record) =>
            record.targetSpecies == null ? 0 : record.hunters.Where(pawn => pawn != null).Select(pawn => map.GetComponent<HuntingKnowledgeMapComponent>()?.Level(pawn, record.targetSpecies) ?? 0).DefaultIfEmpty(0).Max();

        private int BestSpecialist(HuntingExpeditionRecord record, BiomeDef biome) =>
            record.hunters.Where(pawn => pawn != null).Select(pawn => SpecialistLevel(pawn, biome)).DefaultIfEmpty(0).Max();

        private int BestBiomeKnowledge(HuntingExpeditionRecord record, BiomeDef biome) =>
            record.hunters.Where(pawn => pawn != null).Select(pawn =>
                map.GetComponent<HuntingKnowledgeMapComponent>()?.BiomeLevel(pawn, biome) ?? 0).DefaultIfEmpty(0).Max();

        private int BestWildlifeProficiency(HuntingExpeditionRecord record) =>
            record.hunters.Where(pawn => pawn != null).Select(pawn =>
                map.GetComponent<HuntingKnowledgeMapComponent>()?.WildlifeProficiencyLevel(pawn) ?? 0).DefaultIfEmpty(0).Max();

        private float ResourceBonus(HuntingExpeditionRecord record)
        {
            float bonus = 0f;
            for (int i = 0; i < record.resources.Count; i++)
            {
                HuntResourceDef resource = DefDatabase<HuntResourceDef>.GetNamedSilentFail(record.resources[i]);
                if (resource != null) bonus += resource.fieldcraftBonus;
            }
            if (record.medicine > 0) bonus += 0.4f;
            bonus += record.extraFoodDays * 0.15f;
            return bonus;
        }

        public string ForecastDetails(ExpeditionPlan plan)
        {
            if (plan?.destination == null) return "Choose a destination to calculate an expedition forecast.";
            if (plan.destination.knowledge?.discoveryLevel <= 0)
                return "This world tile is unknown.\n\nThe expedition can estimate travel time from distance, but terrain, routes, wildlife, field discoveries, encounter chance, and hazard levels will remain uncertain until the party travels through or surveys the tile.\n\nEstimated duration: " +
                    EstimateDays(plan).ToString("0.0") + " days";
            int medicineCount = plan.medicines?.Values.Sum() ?? plan.medicine;
            float requiredFood = ExpeditionSupplyUtility.RequiredNutrition(plan, EstimateDays(plan));
            float selectedFood = ExpeditionSupplyUtility.SelectedNutrition(plan.provisions);
            float dailyFood = ExpeditionSupplyUtility.DailyNutrition(plan);
            plan.foodDays = dailyFood <= 0f ? 0 : Mathf.Clamp(Mathf.FloorToInt((selectedFood - requiredFood) / dailyFood), 0, 3);
            float population = plan.targetSpecies == null ? 0f : PopulationAt(plan.destination, plan.targetSpecies);
            float skill = plan.hunters.Select(pawn => plan.targetSpecies == null
                ? ColonistHuntingUtility.HuntingSkill(pawn)
                : ColonistHuntingUtility.HuntingSkill(pawn, plan.targetSpecies)).DefaultIfEmpty(0f).Max();
            float specialist = plan.hunters.Select(pawn => SpecialistLevel(pawn, plan.destination.biome)).DefaultIfEmpty(0).Max();
            float proficiency = plan.hunters.Select(pawn =>
                map.GetComponent<HuntingKnowledgeMapComponent>()?.WildlifeProficiencyLevel(pawn) ?? 0).DefaultIfEmpty(0).Max();
            float supplies = PlanResourceBonus(plan);
            float encounterBase = 0.18f;
            float populationBonus = Mathf.InverseLerp(0f, 25f, population) * 0.58f;
            float skillEncounter = skill * 0.018f;
            float confidence = plan.destination.knowledge.confidence * 0.12f;
            float riskEncounter = (plan.riskTolerance - 0.5f) * 0.12f;
            float discoveryEncounter = DiscoveryEncounterModifier(plan.destination.knowledge.discovery);
            float trailLead = plan.objective == ExpeditionObjective.Hunt
                ? TrailHuntBonus(ActiveTrailHuntOpportunity(plan.targetSpecies,
                    plan.destination.biome)) : 0f;
            float routeEncounter = trailLead;
            float proficiencyBonus = proficiency * 0.025f;
            float encounter = Mathf.Clamp01(encounterBase + populationBonus + skillEncounter + confidence + proficiencyBonus + riskEncounter + discoveryEncounter + routeEncounter);
            float danger = plan.destination.danger + DiscoveryDangerModifier(plan.destination.knowledge.discovery) -
                trailLead * 0.45f +
                (plan.routePolicy == ExpeditionRoutePolicy.Fastest ? 0.08f : plan.routePolicy == ExpeditionRoutePolicy.Safest ? -0.08f : 0f);
            float objective = plan.objective == ExpeditionObjective.Capture ? -0.12f : plan.objective == ExpeditionObjective.Tag ? -0.04f : 0f;
            float discoverySuccess = DiscoverySuccessModifier(plan.destination.knowledge.discovery, plan.objective);
            float success = Mathf.Clamp(0.28f + skill * 0.035f + specialist * 0.035f + proficiencyBonus + supplies * 0.04f - danger -
                Mathf.Min(0.35f, plan.destination.distance * 0.006f) + (plan.riskTolerance - 0.5f) * 0.08f + discoverySuccess +
                trailLead * 0.75f + objective, 0.05f, 0.94f);
            float incident = Mathf.Clamp01(plan.destination.danger + DiscoveryDangerModifier(plan.destination.knowledge.discovery) +
                Mathf.Min(0.25f, plan.destination.distance * 0.003f) - medicineCount * 0.018f -
                plan.foodDays * 0.03f - proficiency * 0.025f + plan.riskTolerance * 0.05f -
                trailLead * 0.45f);
            if (plan.routePolicy == ExpeditionRoutePolicy.Safest) incident *= 0.62f;
            return "Encounter chance: " + encounter.ToStringPercent() +
                "\n  Base " + encounterBase.ToStringPercent() + "; population +" + populationBonus.ToStringPercent() +
                "; Skill +" + skillEncounter.ToStringPercent() + "; survey +" + confidence.ToStringPercent() +
                "; proficiency +" + proficiencyBonus.ToStringPercent() +
                "; risk " + SignedPercent(riskEncounter) + "; route " + SignedPercent(routeEncounter) +
                "; discovery " + SignedPercent(discoveryEncounter) +
                "\n\nEngagement success: " + success.ToStringPercent() +
                "\n  Base 28%; Skill +" + (skill * 0.035f).ToStringPercent() + "; biome expertise +" + (specialist * 0.035f).ToStringPercent() +
                "; Wildlife proficiency +" + proficiencyBonus.ToStringPercent() +
                "; equipment +" + (supplies * 0.04f).ToStringPercent() + "; danger -" + danger.ToStringPercent() +
                "; distance -" + Mathf.Min(0.35f, plan.destination.distance * 0.006f).ToStringPercent() + "; risk " + SignedPercent((plan.riskTolerance - 0.5f) * 0.08f) +
                "; objective " + SignedPercent(objective) + "; discovery " + SignedPercent(discoverySuccess) +
                "\n\nIncident risk: " + incident.ToStringPercent() +
                "\nExtra provisions: " + plan.foodDays + " days\nEstimated duration: " + EstimateDays(plan).ToString("0.0") + " days\n" +
                DiscoveryEffect(plan.destination.knowledge.discovery);
        }

        public string RecordDetails(HuntingExpeditionRecord record)
        {
            if (record == null) return "No expedition information is available.";
            string encounter = record.encounterChance <= 0f ? "Not rolled" :
                record.encounterChance.ToStringPercent() + (record.encounterRoll >= 0f ? " (roll " + record.encounterRoll.ToStringPercent() + ")" : "");
            string success = record.successChance <= 0f ? "Not rolled" :
                record.successChance.ToStringPercent() + (record.successRoll >= 0f ? " (roll " + record.successRoll.ToStringPercent() + ")" : "");
            return "Encounter chance: " + encounter + "\nEngagement success: " + success +
                "\nIncident risk: " + record.incidentChance.ToStringPercent() +
                (record.incidentRoll >= 0f ? " (roll " + record.incidentRoll.ToStringPercent() + ")" : "") +
                "\nRisk posture: " + (record.riskTolerance < 0.34f ? "Cautious" : record.riskTolerance < 0.67f ? "Balanced" : "Bold") +
                "\nRoute: " + record.routePolicy + "\nBiome event: " + (record.biomeEvent ?? "None") +
                "\nDiscovery: " + (Destination(record.destinationTile, record.distance).knowledge.discovery ?? "None");
        }

        private static string SignedPercent(float value) => (value >= 0f ? "+" : "-") + Mathf.Abs(value).ToStringPercent();

        private float PlanResourceBonus(ExpeditionPlan plan)
        {
            float bonus = 0f;
            foreach (string defName in plan.resources)
            {
                HuntResourceDef resource = DefDatabase<HuntResourceDef>.GetNamedSilentFail(defName);
                if (resource != null) bonus += resource.fieldcraftBonus;
            }
            if ((plan.medicines?.Values.Sum() ?? plan.medicine) > 0) bonus += 0.4f;
            bonus += plan.foodDays * 0.15f;
            return bonus;
        }

        private static void NotifyExpedition(HuntingExpeditionRecord record, string text, MessageTypeDef type)
        {
            if (HerdsMod.Settings?.enableWildlifeAlerts != true || text.NullOrEmpty()) return;
            Messages.Message(text, record?.caravan ?? (LookTargets)record?.marker ?? record?.spot, type, false);
        }

        private bool ConsumeFieldcraftResources(ExpeditionPlan plan, out string reason)
        {
            reason = null;
            foreach (string defName in plan.resources)
            {
                HuntResourceDef resource = DefDatabase<HuntResourceDef>.GetNamedSilentFail(defName);
                if (resource == null) continue;
                int required = resource.RequiredFor(plan.hunters.Count);
                int available = resource.use == HuntResourceUse.ScentChargePerHunter
                    ? map.listerBuildings.allBuildingsColonist.OfType<Building_WildlifeTool>()
                        .Where(tool => tool.def == resource.sourceBuildingDef && tool.active).Sum(station => station.scentCharges)
                    : resource.thingDef == null ? required : ExpeditionSupplyUtility.AvailableThing(map, resource.thingDef);
                if (available < required) { reason = "Not enough " + resource.label + "."; return false; }
            }
            foreach (string defName in plan.resources)
            {
                HuntResourceDef resource = DefDatabase<HuntResourceDef>.GetNamedSilentFail(defName);
                if (resource == null) continue;
                int required = resource.RequiredFor(plan.hunters.Count);
                if (resource.use == HuntResourceUse.ScentChargePerHunter)
                {
                    List<Building_WildlifeTool> stations = map.listerBuildings.allBuildingsColonist.OfType<Building_WildlifeTool>().Where(tool => tool.def == resource.sourceBuildingDef && tool.active).ToList();
                    int remaining = required;
                    for (int i = 0; i < stations.Count && remaining > 0; i++)
                    {
                        int used = Mathf.Min(remaining, stations[i].scentCharges);
                        stations[i].scentCharges -= used;
                        remaining -= used;
                    }
                }
                else if (resource.thingDef != null)
                {
                    if (resource.use == HuntResourceUse.ConsumablePerHunter) ExpeditionSupplyUtility.ConsumeThing(map, resource.thingDef, required);
                }
            }
            return true;
        }

        private void OrderEmbark(HuntingExpeditionRecord record, Pawn pawn, IntVec3 edge)
        {
            if (pawn?.Spawned != true || pawn.Downed) return;
            Job job = JobMaker.MakeJob(HerdsDefOf.Herds_EmbarkHuntingExpedition, edge);
            job.count = record.id;
            job.playerForced = true;
            pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }

        private ExpeditionCellRecord Cell(int tileId)
        {
            ExpeditionCellRecord record = cells.FirstOrDefault(item => item.tileId == tileId);
            if (record != null) return record;
            record = new ExpeditionCellRecord { tileId = tileId, confidence = 0.02f };
            cells.Add(record);
            return record;
        }

        private void RevealTravelProgress(HuntingExpeditionRecord record, int now)
        {
            if (record?.routeTiles == null || record.routeTiles.Count == 0 || record.stage != ExpeditionStage.OutboundTravel) return;
            int last = record.caravan?.Destroyed == false
                ? Mathf.Max(0, record.routeTiles.IndexOf((int)record.caravan.Tile))
                : Mathf.Clamp(Mathf.FloorToInt((record.nextStageTick <= record.stageStartedTick ? 1f :
                    Mathf.Clamp01(Mathf.InverseLerp(record.stageStartedTick, record.nextStageTick, now))) *
                    (record.routeTiles.Count - 1)), 0, record.routeTiles.Count - 1);
            bool changed = false;
            for (int i = 0; i <= last; i++)
            {
                ExpeditionCellRecord cell = Cell(record.routeTiles[i]);
                if (cell.discoveryLevel > 0) continue;
                cell.discoveryLevel = 1;
                cell.traversals++;
                cell.lastVisitTick = now;
                cell.confidence = Mathf.Max(cell.confidence, 0.12f);
                changed = true;
            }
            if (changed && map.Tile.Valid) map.Tile.Layer.SetDirty<WorldDrawLayer_WildlifeKnowledgeFog>();
        }

        private void RevealWildlifeSigns(ExpeditionDestination destination, int count)
        {
            if (destination?.biome == null || destination.knowledge == null || count <= 0) return;
            List<ThingDef> candidates = ValidWildAnimals(destination.biome)
                .OrderByDescending(kind => destination.biome.CommonalityOfAnimal(kind))
                .ThenBy(kind => Mathf.Abs(Gen.HashCombineInt(destination.tileId, kind.race.shortHash)))
                .Select(kind => kind.race)
                .Distinct()
                .Take(count)
                .ToList();
            for (int i = 0; i < candidates.Count; i++) PopulationAt(destination, candidates[i]);
            if (map.Tile.Valid) map.Tile.Layer.SetDirty<WorldDrawLayer_WildlifeKnowledgeFog>();
        }

        private static IEnumerable<PawnKindDef> ValidWildAnimals(BiomeDef biome)
        {
            if (biome == null) return Enumerable.Empty<PawnKindDef>();
            return biome.AllWildAnimals.Where(kind =>
                kind?.race?.race?.Animal == true &&
                biome.CommonalityOfAnimal(kind) > 0.001f);
        }

        private List<int> BuildRoute(int destinationTile)
        {
            PlanetTile originTile = map.Tile;
            int origin = originTile;
            if (origin == destinationTile) return new List<int> { origin };
            WorldPath path = originTile.Layer.Pather.FindPath(originTile, (PlanetTile)destinationTile, null, null);
            if (path == null || !path.Found)
            {
                path?.ReleaseToPool();
                return new List<int> { origin, destinationTile };
            }
            List<int> route = path.NodesReversed.Select(tile => (int)tile).Reverse().ToList();
            path.ReleaseToPool();
            if (route.Count == 0 || route[0] != origin) route.Insert(0, origin);
            if (route[route.Count - 1] != destinationTile) route.Add(destinationTile);
            return route;
        }

        private void EnsureMarker(HuntingExpeditionRecord record)
        {
            if (record == null || HerdsDefOf.Herds_HuntingExpeditionMarker == null) return;
            if (record.caravan != null && !record.caravan.Destroyed)
            {
                if (record.marker != null && !record.marker.Destroyed) record.marker.Destroy();
                record.marker = null;
                UpdateMarker(record);
                return;
            }
            if (record.marker == null || record.marker.Destroyed)
            {
                record.marker = Find.WorldObjects.AllWorldObjects.OfType<WorldObject_HuntingExpeditionMarker>()
                    .FirstOrDefault(marker => marker.mapId == map.uniqueID && marker.expeditionId == record.id);
                if (record.marker == null)
                {
                    record.marker = (WorldObject_HuntingExpeditionMarker)WorldObjectMaker.MakeWorldObject(HerdsDefOf.Herds_HuntingExpeditionMarker);
                    record.marker.mapId = map.uniqueID;
                    record.marker.expeditionId = record.id;
                    record.marker.Tile = (PlanetTile)(record.routeTiles.Count > 0 ? record.routeTiles[0] : (int)map.Tile);
                    record.marker.SetFaction(Faction.OfPlayer);
                    Find.WorldObjects.Add(record.marker);
                }
            }
            UpdateMarker(record);
        }

        private void UpdateMarker(HuntingExpeditionRecord record)
        {
            if (record == null || ((record.marker == null || record.marker.Destroyed) &&
                (record.caravan == null || record.caravan.Destroyed))) return;
            if (record.routeTiles == null || record.routeTiles.Count == 0) record.routeTiles = BuildRoute(record.destinationTile);
            float travel = record.nextStageTick <= record.stageStartedTick ? 1f :
                Mathf.Clamp01(Mathf.InverseLerp(record.stageStartedTick, record.nextStageTick, Find.TickManager.TicksGame));
            float routeProgress = record.stage == ExpeditionStage.Embarking ? 0f :
                record.stage == ExpeditionStage.OutboundTravel ? travel :
                record.stage == ExpeditionStage.Returning ? 1f - travel : 1f;
            int index = Mathf.Clamp(Mathf.RoundToInt(routeProgress * (record.routeTiles.Count - 1)), 0, record.routeTiles.Count - 1);
            if (record.caravan == null || record.caravan.Destroyed)
                record.marker.Tile = (PlanetTile)record.routeTiles[index];
        }

        private void EnsureCaravan(HuntingExpeditionRecord record)
        {
            if (record == null || (record.caravan != null && !record.caravan.Destroyed)) return;
            List<Pawn> away = record.Party.Where(pawn => pawn != null && !pawn.Dead && !pawn.Spawned).ToList();
            if (away.Count == 0) return;
            record.caravan = CaravanMaker.MakeCaravan(new[] { away[0] }, Faction.OfPlayer,
                record.marker?.Tile ?? map.Tile, true);
            record.caravan.Name = "Wildlife Expedition";
            for (int i = 1; i < away.Count; i++)
                if (!record.caravan.ContainsPawn(away[i])) record.caravan.AddPawn(away[i], true);
            if (record.marker != null && !record.marker.Destroyed) record.marker.Destroy();
            record.marker = null;
            UpdateMarker(record);
        }

        private void AddToExpeditionCaravan(HuntingExpeditionRecord record, Pawn pawn)
        {
            if (record.caravan == null || record.caravan.Destroyed)
            {
                record.caravan = CaravanMaker.MakeCaravan(new[] { pawn }, Faction.OfPlayer, map.Tile, true);
                record.caravan.Name = "Wildlife Expedition";
                if (record.marker != null && !record.marker.Destroyed) record.marker.Destroy();
                record.marker = null;
            }
            else if (!record.caravan.ContainsPawn(pawn))
                record.caravan.AddPawn(pawn, true);
            UpdateMarker(record);
        }

        private void UpdateDistantEcology()
        {
            if (!HerdsMod.Settings.enableExtendedHuntingExpeditions || cells.Count == 0) return;
            Dictionary<int, ExpeditionCellRecord> byTile = cells.ToDictionary(record => record.tileId);
            List<PlanetTile> neighbors = new List<PlanetTile>();
            for (int i = 0; i < cells.Count; i++)
            {
                ExpeditionCellRecord cell = cells[i];
                if (cell.discoveryLevel <= 0) continue;
                Tile tile = Find.WorldGrid[(PlanetTile)cell.tileId];
                for (int s = 0; s < cell.species.Count; s++)
                {
                    ExpeditionCellSpeciesRecord animal = cell.species[s];
                    PawnKindDef kind = tile.PrimaryBiome?.AllWildAnimals.FirstOrDefault(candidate => candidate?.race == animal.species);
                    float carrying = kind == null ? animal.population : Mathf.Clamp(2f + tile.PrimaryBiome.CommonalityOfAnimal(kind) * 12f +
                        Mathf.Abs(Gen.HashCombineInt(cell.tileId, animal.species.shortHash)) % 9, 1f, 90f);
                    float habitat = cell.discovery == "Breeding ground" || cell.discovery == "Nesting colony" ? 0.22f : 0f;
                    animal.population = Mathf.Max(0f, animal.population + (carrying - animal.population) * 0.025f + habitat + Rand.Range(-0.12f, 0.12f));
                }
                cell.confidence = Mathf.Max(0.02f, cell.confidence - 0.008f);
                neighbors.Clear();
                Find.WorldGrid.GetTileNeighbors((PlanetTile)cell.tileId, neighbors);
                for (int n = 0; n < neighbors.Count; n++)
                {
                    if (!byTile.TryGetValue(neighbors[n], out ExpeditionCellRecord other) || other.tileId <= cell.tileId) continue;
                    for (int s = 0; s < cell.species.Count; s++)
                    {
                        ExpeditionCellSpeciesRecord source = cell.species[s];
                        ExpeditionCellSpeciesRecord target = other.species.FirstOrDefault(item => item.species == source.species);
                        if (target == null) continue;
                        float transfer = (source.population - target.population) * 0.012f;
                        source.population -= transfer;
                        target.population += transfer;
                    }
                }
            }
        }

        private ExpeditionDestination Destination(int tileId, int distance)
        {
            Tile tile = Find.WorldGrid[(PlanetTile)tileId];
            SurfaceTile surface = tile as SurfaceTile;
            float movement = Mathf.Max(0.65f, tile.PrimaryBiome.movementDifficulty);
            return new ExpeditionDestination
            {
                tileId = tileId,
                distance = distance,
                biome = tile.PrimaryBiome,
                knowledge = Cell(tileId),
                road = surface?.Roads?.Count > 0,
                river = surface?.Rivers?.Count > 0,
                travelFactor = movement * (surface?.Roads?.Count > 0 ? 0.78f : 1f) * (surface?.Rivers?.Count > 0 ? 1.08f : 1f),
                danger = Mathf.Clamp01(0.08f + Mathf.Min(0.45f, distance * 0.006f) +
                    Mathf.InverseLerp(1f, 4f, movement) * 0.18f + Mathf.Abs(tile.temperature - 18f) / 120f)
            };
        }

        private float SeasonalFactor(ThingDef species)
        {
            Season season = GenLocalDate.Season(map);
            if (species?.race?.predator == true) return season == Season.Winter ? 0.92f : 1f;
            return season == Season.Spring ? 1.16f : season == Season.Fall ? 1.08f : season == Season.Winter ? 0.74f : 1f;
        }

        public static string DiscoveryEffect(string discovery)
        {
            if (discovery.NullOrEmpty()) return "No field discovery has been recorded.";
            if (discovery == "Migration route") return "Improves the chance of locating animals.";
            if (discovery == "Breeding ground" || discovery == "Nesting colony") return "Improves protection work and makes wildlife easier to locate.";
            if (discovery == "Watering site") return "Improves encounters and reduces travel danger.";
            if (discovery == "Sheltered field camp") return "Reduces expedition incident risk.";
            if (discovery == "Predator territory" || discovery == "Dense predator concentration") return "Raises expedition danger.";
            if (discovery == "Rare wildlife signs") return "Improves scouting and tagging work.";
            if (discovery == "Abandoned kill") return "Makes hunting and tracking easier.";
            if (discovery == "Injured wildlife") return "Improves capture and tagging chances.";
            return "Recorded field knowledge may affect expedition conditions.";
        }

        private static float DiscoveryEncounterModifier(string discovery) =>
            discovery == "Migration route" || discovery == "Watering site" ? 0.15f :
            discovery == "Breeding ground" || discovery == "Nesting colony" || discovery == "Abandoned kill" ? 0.10f : 0f;

        private static float DiscoveryDangerModifier(string discovery) =>
            discovery == "Sheltered field camp" ? -0.12f :
            discovery == "Watering site" ? -0.05f :
            discovery == "Predator territory" ? 0.13f :
            discovery == "Dense predator concentration" ? 0.20f : 0f;

        private static float DiscoverySuccessModifier(string discovery, ExpeditionObjective objective)
        {
            if ((discovery == "Breeding ground" || discovery == "Nesting colony") && objective == ExpeditionObjective.Protect) return 0.16f;
            if (discovery == "Rare wildlife signs" && (objective == ExpeditionObjective.Scout || objective == ExpeditionObjective.Tag)) return 0.13f;
            if (discovery == "Injured wildlife" && (objective == ExpeditionObjective.Capture || objective == ExpeditionObjective.Tag)) return 0.18f;
            if (discovery == "Abandoned kill" && objective == ExpeditionObjective.Hunt) return 0.08f;
            return 0f;
        }

        private string DestinationLabel(int tileId)
        {
            if (!IsTileKnown(tileId)) return "unknown region";
            Tile tile = Find.WorldGrid?[(PlanetTile)tileId];
            return tile?.PrimaryBiome?.LabelCap.ToString() ?? "regional cell";
        }

        private void SpawnStack(ThingDef def, int count, IntVec3 cell)
        {
            if (def == null || count <= 0) return;
            while (count > 0)
            {
                Thing thing = ThingMaker.MakeThing(def);
                thing.stackCount = Mathf.Min(count, def.stackLimit);
                count -= thing.stackCount;
                GenPlace.TryPlaceThing(thing, cell, map, ThingPlaceMode.Near);
            }
        }

        public static string StageLabel(ExpeditionStage stage)
        {
            return stage == ExpeditionStage.OutboundTravel ? "Traveling Out" :
                stage == ExpeditionStage.FieldDressing ? "Field Dressing" :
                stage == ExpeditionStage.AwaitingRescue ? "Awaiting Rescue" :
                stage.ToString();
        }
    }

    public static class ExpeditionSupplyUtility
    {
        public static float DailyNutrition(ExpeditionPlan plan) =>
            (plan?.hunters?.Count ?? 0) * 1.6f + (plan?.packAnimals?.Count ?? 0) * 1.1f;

        public static float RequiredNutrition(ExpeditionPlan plan, float days) =>
            Mathf.Ceil(DailyNutrition(plan) * Mathf.Max(1f, days) * 10f) / 10f;

        public static float NutritionPerUnit(ThingDef def) =>
            def?.IsNutritionGivingIngestible == true ? Mathf.Max(0f, def.GetStatValueAbstract(StatDefOf.Nutrition)) : 0f;

        public static float SelectedNutrition(IDictionary<ThingDef, int> manifest) =>
            manifest?.Where(pair => pair.Key != null && pair.Value > 0).Sum(pair => NutritionPerUnit(pair.Key) * pair.Value) ?? 0f;

        public static Dictionary<ThingDef, int> AvailableFoods(Map map) =>
            AvailableManifest(map, thing => thing.def.IsNutritionGivingIngestible && thing.def.ingestible?.HumanEdible == true);

        public static Dictionary<ThingDef, int> AvailableMedicines(Map map) =>
            AvailableManifest(map, thing => thing.def.IsMedicine);

        public static bool ManifestAvailable(Map map, IDictionary<ThingDef, int> manifest)
        {
            if (manifest == null) return true;
            foreach (KeyValuePair<ThingDef, int> pair in manifest)
                if (pair.Key == null || pair.Value < 0 || AvailableThing(map, pair.Key) < pair.Value) return false;
            return true;
        }

        private static Dictionary<ThingDef, int> AvailableManifest(Map map, Func<Thing, bool> predicate)
        {
            Dictionary<ThingDef, int> result = new Dictionary<ThingDef, int>();
            if (map?.listerThings?.AllThings == null) return result;
            List<Thing> things = map.listerThings.AllThings;
            for (int i = 0; i < things.Count; i++)
            {
                Thing thing = things[i];
                if (thing?.def == null || !Available(thing, map) || !predicate(thing)) continue;
                result[thing.def] = result.TryGetValue(thing.def, out int count) ? count + thing.stackCount : thing.stackCount;
            }
            return result;
        }

        public static float AvailableNutrition(Map map)
        {
            float total = 0f;
            List<Thing> things = map?.listerThings?.AllThings;
            if (things == null) return 0f;
            for (int i = 0; i < things.Count; i++)
            {
                Thing thing = things[i];
                if (thing?.def?.IsNutritionGivingIngestible != true || thing.def.ingestible?.HumanEdible != true || !Available(thing, map)) continue;
                total += thing.GetStatValue(StatDefOf.Nutrition) * thing.stackCount;
            }
            return total;
        }

        public static int AvailableMedicine(Map map) =>
            map?.listerThings?.AllThings.Where(thing => thing?.def?.IsMedicine == true && Available(thing, map)).Sum(thing => thing.stackCount) ?? 0;

        public static int AvailableBedrolls(Map map) =>
            map?.listerThings?.AllThings.Count(thing => thing is MinifiedThing && Available(thing, map) &&
                BedrollDef(thing)?.defName.IndexOf("Bedroll", StringComparison.OrdinalIgnoreCase) >= 0) ?? 0;

        public static void PackBedrolls(Map map, int count, Pawn carrier)
        {
            if (carrier?.inventory?.innerContainer == null || count <= 0) return;
            List<Thing> beds = map.listerThings.AllThings.Where(thing => thing is MinifiedThing && Available(thing, map) &&
                BedrollDef(thing)?.defName.IndexOf("Bedroll", StringComparison.OrdinalIgnoreCase) >= 0).Take(count).ToList();
            for (int i = 0; i < beds.Count; i++)
            {
                Thing bed = beds[i];
                bed.DeSpawn();
                if (!carrier.inventory.innerContainer.TryAdd(bed))
                    GenPlace.TryPlaceThing(bed, carrier.Position, map, ThingPlaceMode.Near);
            }
        }

        public static int PackedBedrolls(Pawn pawn) =>
            pawn?.inventory?.innerContainer?.Count(thing => BedrollDef(thing)?.defName.IndexOf("Bedroll", StringComparison.OrdinalIgnoreCase) >= 0) ?? 0;

        public static int AvailableThing(Map map, ThingDef def) =>
            map?.listerThings?.ThingsOfDef(def).Where(thing => Available(thing, map)).Sum(thing => thing.stackCount) ?? 0;

        public static List<Thing> PackNutrition(Map map, float nutrition, Pawn carrier)
        {
            List<Thing> packed = new List<Thing>();
            List<Thing> foods = map.listerThings.AllThings.Where(thing => thing?.def?.IsNutritionGivingIngestible == true &&
                thing.def.ingestible?.HumanEdible == true && Available(thing, map))
                .OrderBy(thing => thing.MarketValue / Mathf.Max(0.01f, thing.GetStatValue(StatDefOf.Nutrition))).ToList();
            float remaining = nutrition;
            for (int i = 0; i < foods.Count && remaining > 0.001f; i++)
            {
                Thing food = foods[i];
                float perUnit = Mathf.Max(0.01f, food.GetStatValue(StatDefOf.Nutrition));
                int units = Mathf.Min(food.stackCount, Mathf.CeilToInt(remaining / perUnit));
                Thing taken = food.SplitOff(units);
                remaining -= units * perUnit;
                if (taken.Spawned) taken.DeSpawn();
                if (carrier?.inventory?.innerContainer?.TryAdd(taken) != true)
                    GenPlace.TryPlaceThing(taken, carrier?.Position ?? map.Center, map, ThingPlaceMode.Near);
                packed.Add(taken);
            }
            return packed;
        }

        public static List<Thing> PackManifest(Map map, IDictionary<ThingDef, int> manifest, Pawn carrier)
        {
            List<Thing> packed = new List<Thing>();
            if (manifest == null) return packed;
            foreach (KeyValuePair<ThingDef, int> pair in manifest.Where(pair => pair.Key != null && pair.Value > 0))
            {
                int remaining = pair.Value;
                List<Thing> things = map.listerThings.ThingsOfDef(pair.Key).Where(thing => Available(thing, map)).ToList();
                for (int i = 0; i < things.Count && remaining > 0; i++)
                {
                    int units = Mathf.Min(remaining, things[i].stackCount);
                    Thing taken = things[i].SplitOff(units);
                    remaining -= units;
                    if (taken.Spawned) taken.DeSpawn();
                    if (carrier?.inventory?.innerContainer?.TryAdd(taken) != true)
                        GenPlace.TryPlaceThing(taken, carrier?.Position ?? map.Center, map, ThingPlaceMode.Near);
                    packed.Add(taken);
                }
            }
            return packed;
        }

        public static List<ExpeditionCargoEntry> ConsumeManifest(Map map, IDictionary<ThingDef, int> manifest)
        {
            List<ExpeditionCargoEntry> result = new List<ExpeditionCargoEntry>();
            if (manifest == null) return result;
            foreach (KeyValuePair<ThingDef, int> pair in manifest.Where(pair => pair.Key != null && pair.Value > 0))
            {
                ConsumeThing(map, pair.Key, pair.Value);
                result.Add(new ExpeditionCargoEntry(pair.Key, pair.Value));
            }
            return result;
        }

        public static ThingDef ConsumeMedicine(Map map, int count)
        {
            ThingDef usedDef = null;
            List<Thing> medicines = map.listerThings.AllThings.Where(thing => thing?.def?.IsMedicine == true && Available(thing, map)).OrderBy(thing => thing.MarketValue).ToList();
            int remaining = count;
            for (int i = 0; i < medicines.Count && remaining > 0; i++)
            {
                Thing medicine = medicines[i];
                usedDef ??= medicine.def;
                int units = Mathf.Min(remaining, medicine.stackCount);
                medicine.SplitOff(units).Destroy(DestroyMode.Vanish);
                remaining -= units;
            }
            return usedDef;
        }

        public static void ConsumeThing(Map map, ThingDef def, int count)
        {
            List<Thing> things = map.listerThings.ThingsOfDef(def).Where(thing => Available(thing, map)).ToList();
            int remaining = count;
            for (int i = 0; i < things.Count && remaining > 0; i++)
            {
                int units = Mathf.Min(remaining, things[i].stackCount);
                things[i].SplitOff(units).Destroy(DestroyMode.Vanish);
                remaining -= units;
            }
        }

        private static bool Available(Thing thing, Map map) =>
            thing?.Spawned == true && !thing.IsForbidden(Faction.OfPlayer) && (thing.IsInAnyStorage() || map.areaManager.Home[thing.Position]);

        private static ThingDef BedrollDef(Thing thing) => thing is MinifiedThing minified ? minified.InnerThing?.def : thing?.def;
    }

    public sealed class JobDriver_EmbarkHuntingExpedition : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            yield return Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.OnCell);
            Toil embark = ToilMaker.MakeToil("EmbarkHuntingExpedition");
            embark.initAction = () => pawn.Map?.GetComponent<HuntingExpeditionMapComponent>()?.NotifyEmbarked(job.count, pawn);
            embark.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return embark;
        }
    }
}
