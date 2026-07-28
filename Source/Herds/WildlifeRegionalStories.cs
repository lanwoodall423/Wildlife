using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using LudeonTK;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Herds
{
    public enum MigrationWaveResponse { Undecided, Observe, Protect, Hunt, Redirect, Encourage }

    public sealed class MigrationWaveRecord : IExposable
    {
        public ThingDef species;
        public List<Pawn> animals = new List<Pawn>();
        public IntVec3 entry;
        public IntVec3 exit;
        public int startedTick;
        public int expectedExitTick;
        public MigrationWaveResponse response;
        public string outcome;

        public void ExposeData()
        {
            Scribe_Defs.Look(ref species, "species");
            Scribe_Collections.Look(ref animals, "animals", LookMode.Reference);
            Scribe_Values.Look(ref entry, "entry");
            Scribe_Values.Look(ref exit, "exit");
            Scribe_Values.Look(ref startedTick, "startedTick");
            Scribe_Values.Look(ref expectedExitTick, "expectedExitTick");
            Scribe_Values.Look(ref response, "response");
            Scribe_Values.Look(ref outcome, "outcome");
            if (Scribe.mode == LoadSaveMode.PostLoadInit) animals ??= new List<Pawn>();
        }
    }

    public sealed class WildlifeTerritoryEntry : IExposable
    {
        public Pawn animal;
        public IntVec3 from;
        public IntVec3 to;
        public int tick;
        public string reason;
        public void ExposeData()
        {
            Scribe_References.Look(ref animal, "animal");
            Scribe_Values.Look(ref from, "from");
            Scribe_Values.Look(ref to, "to");
            Scribe_Values.Look(ref tick, "tick");
            Scribe_Values.Look(ref reason, "reason");
        }
    }

    public sealed class WildlifeFamilyLine : IExposable
    {
        public Pawn animal;
        public Pawn parent;
        public ThingDef species;
        public string lineName;
        public int generation;
        public int recordedTick;
        public void ExposeData()
        {
            Scribe_References.Look(ref animal, "animal");
            Scribe_References.Look(ref parent, "parent");
            Scribe_Defs.Look(ref species, "species");
            Scribe_Values.Look(ref lineName, "lineName");
            Scribe_Values.Look(ref generation, "generation");
            Scribe_Values.Look(ref recordedTick, "recordedTick");
        }
    }

    public sealed class WildlifeRegionalStoriesMapComponent : MapComponent
    {
        private MigrationWaveRecord wave;
        private List<WildlifeTerritoryEntry> territory = new List<WildlifeTerritoryEntry>();
        private List<WildlifeFamilyLine> familyLines = new List<WildlifeFamilyLine>();
        private List<int> signedRoamers = new List<int>();
        private int nextTick;
        private int lastWaveSeason = -1;
        private int lastWaveYear = -1;

        public WildlifeRegionalStoriesMapComponent(Map map) : base(map) { }
        public MigrationWaveRecord Wave => wave;
        public IReadOnlyList<WildlifeTerritoryEntry> TerritoryHistory => territory;
        public IReadOnlyList<WildlifeFamilyLine> FamilyLines => familyLines;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.Look(ref wave, "visibleMigrationWave");
            Scribe_Collections.Look(ref territory, "wildlifeTerritoryHistory", LookMode.Deep);
            Scribe_Collections.Look(ref familyLines, "wildlifeFamilyLines", LookMode.Deep);
            Scribe_Collections.Look(ref signedRoamers, "returnSignsCreated", LookMode.Value);
            Scribe_Values.Look(ref nextTick, "nextRegionalStoriesTick");
            Scribe_Values.Look(ref lastWaveSeason, "lastMigrationWaveSeason", -1);
            Scribe_Values.Look(ref lastWaveYear, "lastMigrationWaveYear", -1);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                territory ??= new List<WildlifeTerritoryEntry>();
                familyLines ??= new List<WildlifeFamilyLine>();
                signedRoamers ??= new List<int>();
            }
        }

        public override void MapComponentTick()
        {
            HerdsSettings settings = HerdsMod.Settings;
            if (settings == null || (!settings.enableReturnSigns && !settings.enableVisibleMigrationWaves &&
                !settings.enableTerritoryHistory && !settings.enablePersistentFamilyLines)) return;
            int now = Find.TickManager.TicksGame;
            if (now < nextTick) return;
            nextTick = now + 2500;
            if (settings.enableReturnSigns) UpdateReturnSigns(now);
            if (settings.enableVisibleMigrationWaves) UpdateWave(now);
            if (settings.enableTerritoryHistory) UpdateTerritories(now);
            if (settings.enablePersistentFamilyLines) UpdateFamilies(now);
        }

        public override void MapComponentDraw()
        {
            if (!Prefs.DevMode || !WildlifeDevMaster.CompleteOverlayEnabled || Find.CurrentMap != map) return;
            if (wave != null)
            {
                GenDraw.DrawLineBetween(wave.entry.ToVector3Shifted(), wave.exit.ToVector3Shifted(), SimpleColor.Green);
                GenDraw.DrawRadiusRing(wave.entry, 2f, Color.green);
                GenDraw.DrawRadiusRing(wave.exit, 2f, Color.yellow);
            }
            if (!HerdsMod.Settings.enableTerritoryHistory) return;
            foreach (WildlifeTerritoryEntry entry in territory.Where(value => value.tick > Find.TickManager.TicksGame - 600000)
                .Skip(Mathf.Max(0, territory.Count - 12)))
                GenDraw.DrawLineBetween(entry.from.ToVector3Shifted(), entry.to.ToVector3Shifted(), SimpleColor.Magenta);
        }

        private void UpdateReturnSigns(int now)
        {
            RegionalWildlifeMapComponent regional = map.GetComponent<RegionalWildlifeMapComponent>();
            if (regional == null || HerdsDefOf.Herds_WildlifeSign == null) return;
            foreach (RoamingAnimalRecord roamer in regional.RoamingAnimals)
            {
                if (roamer?.animal == null || roamer.state == RoamingAnimalState.Present ||
                    roamer.state == RoamingAnimalState.Dead || signedRoamers.Contains(roamer.animal.thingIDNumber)) continue;
                int lead = roamer.expectedReturnTick - now;
                if (lead < 0 || lead > 60000) continue;
                if (!CellFinder.TryFindRandomEdgeCellWith(c => c.Standable(map), map, CellFinder.EdgeRoadChance_Animal, out IntVec3 cell)) continue;
                WildlifeSign sign = (WildlifeSign)ThingMaker.MakeThing(HerdsDefOf.Herds_WildlifeSign);
                sign.species = roamer.species;
                sign.sourceAnimal = roamer.animal;
                sign.createdTick = now;
                sign.predator = roamer.species.race.predator;
                sign.groupSize = 1;
                sign.signKind = roamer.tagged ? WildlifeSignKind.Tracks :
                    sign.predator ? WildlifeSignKind.TerritoryMark :
                    PreyProfileDatabase.IsBird(roamer.species) ? WildlifeSignKind.Browse : WildlifeSignKind.Tracks;
                sign.travelFrom = cell;
                sign.travelTo = map.Center;
                GenSpawn.Spawn(sign, cell, map);
                signedRoamers.Add(roamer.animal.thingIDNumber);
                if (roamer.tagged && WildlifeProgression.Unlocked(WildlifeCapability.Telemetry))
                    Messages.Message("Telemetry and fresh signs suggest " + roamer.animal.LabelShortCap +
                        " may return soon.", sign, MessageTypeDefOf.NeutralEvent, false);
                WildlifeExperience.Record("Return Sign", "Signs of " + roamer.animal.LabelShortCap + " appeared near the colony.", sign);
                if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("ReturnSign",
                    "animal=" + roamer.animal.thingIDNumber + " lead=" + lead);
            }
        }

        private void UpdateWave(int now)
        {
            if (wave != null)
            {
                wave.animals.RemoveAll(pawn => pawn == null || pawn.Dead || !pawn.Spawned);
                if (wave.animals.Count == 0 || now > wave.expectedExitTick)
                {
                    FinishWave();
                    wave = null;
                }
                return;
            }
            Season season = GenLocalDate.Season(map);
            int year = GenLocalDate.Year(map);
            if (season != Season.Spring && season != Season.Fall ||
                lastWaveSeason == (int)season && lastWaveYear == year) return;
            RegionalWildlifeMapComponent regional = map.GetComponent<RegionalWildlifeMapComponent>();
            RegionalSpeciesRecord species = regional?.Records.Where(record =>
                !record.species.race.predator && HuntingExpeditionMapComponent.IsHerdSpecies(record.species) &&
                record.nearbyPopulation >= 5f).OrderByDescending(record => record.nearbyPopulation).FirstOrDefault();
            if (species == null || !Rand.Chance(0.32f)) return;
            StartWave(species.species, Mathf.Clamp(Mathf.RoundToInt(species.nearbyPopulation * 0.35f), 3, 10), now);
            lastWaveSeason = (int)season;
            lastWaveYear = year;
        }

        public bool StartWave(ThingDef species, int count, int now)
        {
            PawnKindDef kind = DefDatabase<PawnKindDef>.AllDefsListForReading.FirstOrDefault(value => value.race == species);
            if (kind == null || !RCellFinder.TryFindRandomPawnEntryCell(out IntVec3 entry, map, CellFinder.EdgeRoadChance_Animal)) return false;
            IntVec3 exit = OppositeEdge(entry);
            wave = new MigrationWaveRecord { species = species, entry = entry, exit = exit,
                startedTick = now, expectedExitTick = now + 45000 };
            for (int i = 0; i < count; i++)
            {
                IntVec3 spawn = CellFinder.RandomClosewalkCellNear(entry, map, 5);
                Pawn pawn = PawnGenerator.GeneratePawn(kind, null);
                GenSpawn.Spawn(pawn, spawn, map, Rot4.Random);
                wave.animals.Add(pawn);
                OrderAcrossMap(pawn);
            }
            Find.LetterStack.ReceiveLetter("Wildlife Migration", count + " " + species.label +
                " are crossing the map. Select one to observe, protect, hunt, redirect, or encourage the migration.",
                LetterDefOf.NeutralEvent, wave.animals.FirstOrDefault());
            WildlifeExperience.Record("Migration Wave", count + " " + species.label + " entered the map.");
            if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("MigrationWave",
                "species=" + species.defName + " count=" + count);
            return true;
        }

        private IntVec3 OppositeEdge(IntVec3 entry)
        {
            bool Opposite(IntVec3 cell) =>
                cell.Standable(map) && (entry.x < map.Size.x / 3 ? cell.x == map.Size.x - 1 :
                entry.x > map.Size.x * 2 / 3 ? cell.x == 0 :
                entry.z < map.Size.z / 2 ? cell.z == map.Size.z - 1 : cell.z == 0);
            if (CellFinder.TryFindRandomEdgeCellWith(Opposite, map, CellFinder.EdgeRoadChance_Animal, out IntVec3 target))
                return target;
            return RCellFinder.TryFindRandomPawnEntryCell(out target, map, CellFinder.EdgeRoadChance_Animal)
                ? target : entry;
        }

        private void OrderAcrossMap(Pawn pawn)
        {
            if (pawn?.Spawned != true || wave == null) return;
            Job job = JobMaker.MakeJob(JobDefOf.Goto, wave.exit);
            job.exitMapOnArrival = true;
            job.expiryInterval = 60000;
            pawn.jobs.StartJob(job, JobCondition.InterruptForced);
        }

        public void Respond(MigrationWaveResponse response)
        {
            if (wave == null) return;
            wave.response = response;
            RegionalWildlifeMapComponent regional = map.GetComponent<RegionalWildlifeMapComponent>();
            if (response == MigrationWaveResponse.Hunt)
                foreach (Pawn pawn in wave.animals.Where(value => value?.Spawned == true))
                    map.designationManager.AddDesignation(new Designation(pawn, DesignationDefOf.Hunt));
            else if (response == MigrationWaveResponse.Protect)
                foreach (Pawn pawn in wave.animals.Where(value => value?.Spawned == true))
                {
                    Designation hunt = map.designationManager.DesignationOn(pawn, DesignationDefOf.Hunt);
                    if (hunt != null) map.designationManager.RemoveDesignation(hunt);
                }
            else if (response == MigrationWaveResponse.Redirect)
            {
                Building_WildlifeTool corridor = map.listerBuildings.allBuildingsColonist.OfType<Building_WildlifeTool>()
                    .FirstOrDefault(tool => tool.active && tool.Kind == WildlifeToolKind.MigrationCorridor);
                if (corridor != null)
                {
                    bool horizontal = Mathf.Abs(corridor.Position.x - map.Center.x) >
                        Mathf.Abs(corridor.Position.z - map.Center.z);
                    bool NearCorridorEdge(IntVec3 cell) => cell.Standable(map) &&
                        (horizontal
                            ? corridor.Position.x < map.Center.x ? cell.x == 0 : cell.x == map.Size.x - 1
                            : corridor.Position.z < map.Center.z ? cell.z == 0 : cell.z == map.Size.z - 1);
                    if (CellFinder.TryFindRandomEdgeCellWith(NearCorridorEdge, map,
                        CellFinder.EdgeRoadChance_Animal, out IntVec3 redirected))
                        wave.exit = redirected;
                }
                foreach (Pawn pawn in wave.animals) OrderAcrossMap(pawn);
            }
            else if (response == MigrationWaveResponse.Encourage)
            {
                regional?.ApplyExpeditionImpact(wave.species, 1.5f, 0.08f);
                foreach (Pawn pawn in wave.animals.Where(value => value?.Spawned == true).Take(Mathf.Max(1, wave.animals.Count / 3)))
                    pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
            }
            else if (response == MigrationWaveResponse.Observe)
                regional?.ApplyExpeditionImpact(wave.species, 0f, 0.12f);
            Messages.Message("Migration response: " + response + ".", MessageTypeDefOf.NeutralEvent, false);
        }

        private void FinishWave()
        {
            if (wave == null) return;
            int survived = wave.animals.Count(pawn => pawn != null && !pawn.Dead);
            wave.outcome = wave.response + ": " + survived + " animals completed the passage.";
            float impact = wave.response == MigrationWaveResponse.Protect ? 1.2f :
                wave.response == MigrationWaveResponse.Encourage ? 1.5f :
                wave.response == MigrationWaveResponse.Hunt ? -0.8f : 0.25f;
            map.GetComponent<RegionalWildlifeMapComponent>()?.ApplyExpeditionImpact(wave.species, impact, 0.08f);
            WildlifeExperience.Record("Migration Outcome", wave.outcome, null,
                wave.response == MigrationWaveResponse.Hunt);
            if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("MigrationOutcome",
                "species=" + wave.species.defName + " response=" + wave.response + " survived=" + survived);
        }

        private void UpdateTerritories(int now)
        {
            NotableWildlifeMapComponent notables = map.GetComponent<NotableWildlifeMapComponent>();
            if (notables == null) return;
            foreach (NotableAnimalRecord notable in notables.Records.Where(value => value?.animal?.Spawned == true))
            {
                WildlifeTerritoryEntry last = territory.LastOrDefault(value => value.animal == notable.animal);
                if (last == null)
                {
                    territory.Add(new WildlifeTerritoryEntry { animal = notable.animal, from = notable.animal.Position,
                        to = notable.animal.Position, tick = now, reason = "First recorded range" });
                    continue;
                }
                if (now - last.tick < 60000 || last.to.DistanceToSquared(notable.animal.Position) < 2500) continue;
                string reason = map.listerThings.ThingsInGroup(ThingRequestGroup.Fire)
                    .Any(fire => fire.Position.DistanceToSquared(last.to) < 900) ? "Displaced by fire" :
                    map.listerBuildings.allBuildingsColonist.Any(building => building.Position.DistanceToSquared(last.to) < 625) ?
                    "Shifted around colony construction" : "Seasonal range shift";
                territory.Add(new WildlifeTerritoryEntry { animal = notable.animal, from = last.to,
                    to = notable.animal.Position, tick = now, reason = reason });
                if (territory.Count > 80) territory.RemoveAt(0);
                notable.history.Add(reason + ".");
            }
        }

        private void UpdateFamilies(int now)
        {
            RegionalWildlifeMapComponent regional = map.GetComponent<RegionalWildlifeMapComponent>();
            NotableWildlifeMapComponent notables = map.GetComponent<NotableWildlifeMapComponent>();
            if (regional == null || notables == null) return;
            foreach (Pawn animal in map.mapPawns.AllPawnsSpawned.Where(pawn => pawn.RaceProps?.Animal == true))
            {
                AnimalRelationshipRecord relation = regional.RelationshipFor(animal);
                if (relation?.parent == null || familyLines.Any(line => line.animal == animal)) continue;
                WildlifeFamilyLine parentLine = familyLines.FirstOrDefault(line => line.animal == relation.parent);
                NotableAnimalRecord parentNotable = notables.For(relation.parent);
                if (parentLine == null && parentNotable == null) continue;
                string name = parentLine?.lineName ?? parentNotable.title + " Line";
                WildlifeFamilyLine line = new WildlifeFamilyLine { animal = animal, parent = relation.parent,
                    species = animal.def, lineName = name, generation = (parentLine?.generation ?? 0) + 1,
                    recordedTick = now };
                familyLines.Add(line);
                notables.MakeNotable(animal, true);
                NotableAnimalRecord child = notables.For(animal);
                if (child != null) child.history.Add("Recognized as generation " + line.generation + " of the " + name + ".");
                WildlifeMemoryUtility.Folklore(map, name, animal.LabelShortCap + " continued the " + name + ".");
                if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("FamilyLine",
                    "animal=" + animal.thingIDNumber + " generation=" + line.generation);
            }
        }

        public string FamilySummary(Pawn animal)
        {
            WildlifeFamilyLine line = familyLines.LastOrDefault(value => value.animal == animal);
            return line == null ? null : line.lineName + ", generation " + line.generation +
                (line.parent != null ? "\nParent: " + line.parent.LabelShortCap : "");
        }

        public string TerritorySummary(Pawn animal)
        {
            List<WildlifeTerritoryEntry> entries = territory.Where(value => value.animal == animal).ToList();
            if (entries.Count == 0) return null;
            WildlifeTerritoryEntry latest = entries[entries.Count - 1];
            return entries.Count + " recorded range observation" + (entries.Count == 1 ? "" : "s") +
                "\nLatest: " + latest.reason + " (" + (Find.TickManager.TicksGame - latest.tick).ToStringTicksToPeriod() + " ago)";
        }

        [DebugAction("Wildlife", "Force visible migration wave", actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void DebugWave()
        {
            Map map = Find.CurrentMap;
            RegionalSpeciesRecord species = map?.GetComponent<RegionalWildlifeMapComponent>()?.Records
                .FirstOrDefault(record => !record.species.race.predator);
            if (species != null) map.GetComponent<WildlifeRegionalStoriesMapComponent>()?
                .StartWave(species.species, 5, Find.TickManager.TicksGame);
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class MigrationWaveGizmosPatch
    {
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> values, Pawn __instance)
        {
            foreach (Gizmo gizmo in values) yield return gizmo;
            if (__instance?.Spawned != true || __instance.Faction != null) yield break;
            WildlifeRegionalStoriesMapComponent stories = __instance.Map.GetComponent<WildlifeRegionalStoriesMapComponent>();
            if (stories?.Wave?.animals?.Contains(__instance) != true) yield break;
            yield return new Command_Action
            {
                defaultLabel = "Migration Response",
                defaultDesc = "Choose how the colony responds to this visible migration wave.",
                icon = TexCommand.GatherSpotActive,
                action = () => Find.WindowStack.Add(new FloatMenu(Enum.GetValues(typeof(MigrationWaveResponse))
                    .Cast<MigrationWaveResponse>().Where(value => value != MigrationWaveResponse.Undecided)
                    .Select(value => new FloatMenuOption(value.ToString(), () => stories.Respond(value))).ToList()))
            };
        }
    }
}
