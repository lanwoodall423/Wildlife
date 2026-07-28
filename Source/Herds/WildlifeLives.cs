using System;
using System.Collections.Generic;
using System.Linq;
using LudeonTK;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Herds
{
    public enum AnimalPersonality
    {
        Bold,
        Curious,
        Cautious,
        Loyal,
        Territorial,
        Clever
    }

    public sealed class AnimalPersonalityRecord : IExposable
    {
        public Pawn animal;
        public Pawn inheritedFrom;
        public AnimalPersonality personality;
        public bool inherited;
        public int createdTick;

        public void ExposeData()
        {
            Scribe_References.Look(ref animal, "animal");
            Scribe_References.Look(ref inheritedFrom, "inheritedFrom");
            Scribe_Values.Look(ref personality, "personality");
            Scribe_Values.Look(ref inherited, "inherited", false);
            Scribe_Values.Look(ref createdTick, "createdTick", 0);
        }
    }

    public sealed class AnimalEscapeHistory : IExposable
    {
        public Pawn animal;
        public int escapes;

        public void ExposeData()
        {
            Scribe_References.Look(ref animal, "animal");
            Scribe_Values.Look(ref escapes, "escapes", 0);
        }
    }

    public sealed class WildlifeLivesMapComponent : MapComponent
    {
        private List<AnimalPersonalityRecord> personalities = new List<AnimalPersonalityRecord>();
        private List<AnimalEscapeHistory> escapeHistories = new List<AnimalEscapeHistory>();
        private Dictionary<Pawn, AnimalPersonalityRecord> index =
            new Dictionary<Pawn, AnimalPersonalityRecord>();
        private int nextPersonalityTick;
        private int nextIncidentTick;
        private string lastIncident;
        private int lastIncidentTick;

        public WildlifeLivesMapComponent(Map map) : base(map) { }
        public IReadOnlyList<AnimalPersonalityRecord> Personalities => personalities;
        public string LastIncident => lastIncident;

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref personalities, "animalPersonalities", LookMode.Deep);
            Scribe_Collections.Look(ref escapeHistories, "animalEscapeHistories", LookMode.Deep);
            Scribe_Values.Look(ref nextPersonalityTick, "nextAnimalPersonalityTick", 0);
            Scribe_Values.Look(ref nextIncidentTick, "nextWildlifeLifeIncidentTick", 0);
            Scribe_Values.Look(ref lastIncident, "lastWildlifeLifeIncident");
            Scribe_Values.Look(ref lastIncidentTick, "lastWildlifeLifeIncidentTick", 0);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                personalities = personalities?.Where(record => record?.animal != null &&
                    !record.animal.Destroyed).ToList() ?? new List<AnimalPersonalityRecord>();
                escapeHistories = escapeHistories?.Where(record => record?.animal != null &&
                    !record.animal.Destroyed).ToList() ?? new List<AnimalEscapeHistory>();
                RebuildIndex();
            }
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            RebuildIndex();
        }

        public override void MapComponentTick()
        {
            int now = Find.TickManager.TicksGame;
            if (HerdsMod.Settings?.enableAnimalPersonalities == true && now >= nextPersonalityTick)
            {
                nextPersonalityTick = now + 60000;
                EnsureSpawnedAnimals();
            }
            if (HerdsMod.Settings?.enableWildlifeEvents == true &&
                HerdsMod.Settings.enableWildlifeLifeIncidents && now >= nextIncidentTick)
            {
                nextIncidentTick = now + 180000;
                if (now > 180000 && Rand.Chance(0.28f)) TryIncident(now);
            }
        }

        public AnimalPersonalityRecord For(Pawn animal, bool create = true)
        {
            if (HerdsMod.Settings?.enableAnimalPersonalities != true ||
                animal?.RaceProps?.Animal != true) return null;
            if (index.TryGetValue(animal, out AnimalPersonalityRecord record)) return record;
            if (!create) return null;
            record = CreateRecord(animal);
            personalities.Add(record);
            index[animal] = record;
            return record;
        }

        public int RegisterHuntEscape(Pawn animal)
        {
            if (animal?.RaceProps?.Animal != true) return 0;
            AnimalEscapeHistory history = escapeHistories.FirstOrDefault(value => value.animal == animal);
            if (history == null)
            {
                history = new AnimalEscapeHistory { animal = animal };
                escapeHistories.Add(history);
            }
            history.escapes++;
            if (history.escapes == 2 && HerdsMod.Settings.enableNotableAnimals)
            {
                NotableAnimalRecord notable = map.GetComponent<NotableWildlifeMapComponent>()
                    ?.MakeNotable(animal, true);
                if (notable != null)
                {
                    notable.escapes = Mathf.Max(notable.escapes, history.escapes);
                    notable.distinction = "A clever survivor of repeated colony hunts";
                    HediffDef cunning = DefDatabase<HediffDef>.GetNamedSilentFail("Herds_NotableCunning");
                    if (cunning != null)
                    {
                        notable.ability = cunning;
                        if (animal.health?.hediffSet?.GetFirstHediffOfDef(cunning) == null)
                            animal.health?.AddHediff(cunning);
                    }
                    notable.history.Add("Became famous after escaping two colony hunts.");
                }
            }
            return history.escapes;
        }

        public int EscapeCount(Pawn animal) =>
            escapeHistories.FirstOrDefault(value => value?.animal == animal)?.escapes ?? 0;

        public string RelationshipSummary(Pawn animal)
        {
            if (animal == null) return null;
            WildlifeMemoryMapComponent memory = map.GetComponent<WildlifeMemoryMapComponent>();
            List<AnimalColonistMemory> known = memory?.Memories.Where(value =>
                value?.animal == animal && value.colonist != null).ToList();
            if (known == null || known.Count == 0) return "No recognized colonists.";
            AnimalColonistMemory favorite = known.OrderByDescending(value => value.trust).First();
            AnimalColonistMemory feared = known.OrderByDescending(value =>
                value.fear + value.hostility).First();
            string result = favorite.trust >= 0.2f
                ? "Trusted: " + favorite.colonist.LabelShortCap
                : "No trusted colonist.";
            if (feared.fear + feared.hostility >= 0.3f)
                result += "  •  Feared: " + feared.colonist.LabelShortCap;
            return result;
        }

        public string Lifecycle(Pawn animal)
        {
            if (animal?.Spawned != true) return "Unknown";
            Season season = GenLocalDate.Season(animal.Map);
            bool bird = PreyProfileDatabase.IsBird(animal.def);
            if (season == Season.Spring) return bird ? "Nesting" : "Breeding season";
            if (season == Season.Summer) return animal.ageTracker.Adult ? "Foraging" : "Growing";
            if (season == Season.Fall) return bird ? "Preparing to migrate" : "Building reserves";
            if (season == Season.Winter) return animal.RaceProps.predator ? "Winter ranging" : "Winter sheltering";
            return "Seasonally active";
        }

        public List<string> DebugLines() => new List<string>
        {
            "LIVES personalities=" + personalities.Count + " escapeHistories=" + escapeHistories.Count,
            "LIVES incident=" + (lastIncident ?? "none") + " tick=" + lastIncidentTick
        };

        private AnimalPersonalityRecord CreateRecord(Pawn animal)
        {
            Pawn parent = null;
            if (HerdsMod.Settings.enablePersonalityInheritance && !animal.ageTracker.Adult)
            {
                parent = map.GetComponent<RegionalWildlifeMapComponent>()?.RelationshipFor(animal)?.parent;
                if (parent == null)
                    parent = map.mapPawns.AllPawnsSpawned.Where(candidate => candidate != animal &&
                        candidate.def == animal.def && candidate.Faction == animal.Faction &&
                        candidate.ageTracker.Adult &&
                        candidate.Position.DistanceToSquared(animal.Position) <= 900)
                        .OrderBy(candidate => candidate.Position.DistanceToSquared(animal.Position))
                        .FirstOrDefault();
            }
            int hash = PositiveHash(animal.thingIDNumber * 397 ^ map.uniqueID * 31);
            AnimalPersonality personality = (AnimalPersonality)(hash % 6);
            bool inherited = false;
            if (parent != null && hash % 100 < 68)
            {
                AnimalPersonalityRecord source = For(parent);
                if (source != null)
                {
                    personality = source.personality;
                    inherited = true;
                    if (hash % 10 == 0)
                        personality = (AnimalPersonality)(((int)personality + 1 + hash % 5) % 6);
                }
            }
            return new AnimalPersonalityRecord
            {
                animal = animal,
                inheritedFrom = inherited ? parent : null,
                personality = personality,
                inherited = inherited,
                createdTick = Find.TickManager.TicksGame
            };
        }

        private void EnsureSpawnedAnimals()
        {
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
                if (pawns[i]?.RaceProps?.Animal == true) For(pawns[i]);
            personalities.RemoveAll(record => record?.animal == null || record.animal.Destroyed);
            escapeHistories.RemoveAll(record => record?.animal == null || record.animal.Destroyed);
            RebuildIndex();
        }

        private void RebuildIndex()
        {
            index.Clear();
            for (int i = 0; i < personalities.Count; i++)
                if (personalities[i]?.animal != null) index[personalities[i].animal] = personalities[i];
        }

        private bool TryIncident(int now)
        {
            int first = PositiveHash(map.uniqueID * 31 + now / 180000) % 5;
            for (int offset = 0; offset < 5; offset++)
            {
                int kind = (first + offset) % 5;
                bool success = kind == 0 ? InjuredAnimalSeeksHelp() :
                    kind == 1 ? DisplacedFlock() :
                    kind == 2 ? TerritorialDispute() :
                    kind == 3 ? OrphanedYoung() : CropRaid();
                if (!success) continue;
                lastIncidentTick = now;
                return true;
            }
            return false;
        }

        public bool DebugTriggerIncident() => TryIncident(Find.TickManager?.TicksGame ?? 0);

        public void DebugCyclePersonality(Pawn animal)
        {
            AnimalPersonalityRecord record = For(animal);
            if (record == null) return;
            record.personality = (AnimalPersonality)(((int)record.personality + 1) % 6);
            record.inherited = false;
            record.inheritedFrom = null;
        }

        private bool InjuredAnimalSeeksHelp()
        {
            Pawn animal = WildAnimals().Where(pawn => !pawn.RaceProps.predator &&
                pawn.health.summaryHealth.SummaryHealthPercent < 0.72f && !pawn.Downed)
                .OrderBy(pawn => pawn.health.summaryHealth.SummaryHealthPercent).FirstOrDefault();
            if (animal == null) return false;
            IntVec3 destination = CellFinder.RandomClosewalkCellNear(map.Center, map, 10);
            StartVisibleMove(animal, destination, 12000);
            return Announce("Injured Animal",
                "An injured " + animal.def.label + " has approached the colony. It may accept aid—or flee if frightened.",
                animal, false);
        }

        private bool DisplacedFlock()
        {
            HerdMapComponent herds = map.GetComponent<HerdMapComponent>();
            HerdSnapshot flock = map.mapPawns.AllPawnsSpawned
                .Where(pawn => pawn?.Faction == null && PreyProfileDatabase.IsBird(pawn.def))
                .Select(pawn => herds?.HerdFor(pawn))
                .FirstOrDefault(group => group?.profile?.socialType == PreySocialType.Flock &&
                    group.members.Count >= 2);
            if (flock == null) return false;
            for (int i = 0; i < flock.members.Count; i++)
                StartVisibleMove(flock.members[i],
                    CellFinder.RandomClosewalkCellNear(map.Center, map, 18), 9000);
            return Announce("Displaced Flock",
                "A displaced flock of " + flock.species.label + " is crossing the colony's territory while seeking safer habitat.",
                flock.leader, false);
        }

        private bool TerritorialDispute()
        {
            List<Pawn> predators = WildAnimals().Where(pawn => pawn.RaceProps.predator &&
                !pawn.Downed).Take(12).ToList();
            for (int i = 0; i < predators.Count; i++)
                for (int j = i + 1; j < predators.Count; j++)
                {
                    if (predators[i].Position.DistanceToSquared(predators[j].Position) > 1600) continue;
                    Vector2 away = new Vector2(predators[i].Position.x - predators[j].Position.x,
                        predators[i].Position.z - predators[j].Position.z).normalized;
                    StartVisibleMove(predators[i], SafeCell(predators[i].Position, away, 20f), 5000);
                    StartVisibleMove(predators[j], SafeCell(predators[j].Position, -away, 20f), 5000);
                    return Announce("Predator Territory Dispute",
                        predators[i].def.LabelCap + " and " + predators[j].def.label +
                        " are contesting nearby territory. Both may range unpredictably until the boundary settles.",
                        predators[i], true);
                }
            return false;
        }

        private bool OrphanedYoung()
        {
            Pawn young = WildAnimals().Where(pawn => !pawn.ageTracker.Adult &&
                !map.mapPawns.AllPawnsSpawned.Any(adult => adult != pawn && adult.def == pawn.def &&
                    adult.Faction == pawn.Faction && adult.ageTracker.Adult &&
                    adult.Position.DistanceToSquared(pawn.Position) <= 2500)).FirstOrDefault();
            if (young == null) return false;
            map.GetComponent<NotableWildlifeMapComponent>()?.MakeNotable(young, true);
            return Announce("Orphaned Young",
                "A young " + young.def.label + " appears to have been separated from every adult of its species. " +
                "The colony may protect, observe, or attempt to tame it.", young, false);
        }

        private bool CropRaid()
        {
            Plant crop = map.listerThings.AllThings.OfType<Plant>()
                .FirstOrDefault(plant => plant?.Spawned == true && plant.sown);
            Pawn raider = WildAnimals().Where(pawn => !pawn.RaceProps.predator &&
                pawn.RaceProps.foodType != FoodTypeFlags.CarnivoreAnimal &&
                pawn.CanReach(crop?.Position ?? IntVec3.Invalid, PathEndMode.OnCell, Danger.Deadly))
                .OrderBy(pawn => pawn.Position.DistanceToSquared(crop?.Position ?? pawn.Position)).FirstOrDefault();
            if (crop == null || raider == null) return false;
            HerdSnapshot group = map.GetComponent<HerdMapComponent>()?.HerdFor(raider);
            List<Pawn> members = group?.members ?? new List<Pawn> { raider };
            for (int i = 0; i < members.Count && i < 8; i++)
                StartVisibleMove(members[i], CellFinder.RandomClosewalkCellNear(crop.Position, map, 4), 7000);
            return Announce("Wildlife Crop Raid",
                "A " + raider.def.label + (members.Count > 1 ? " group is" : " is") +
                " moving toward cultivated food. Scaring them away may also teach them to fear this area.",
                raider, true);
        }

        private IEnumerable<Pawn> WildAnimals() => map.mapPawns.AllPawnsSpawned.Where(pawn =>
            pawn?.Spawned == true && !pawn.Dead && pawn.RaceProps?.Animal == true &&
            pawn.Faction == null && !pawn.InMentalState);

        private void StartVisibleMove(Pawn pawn, IntVec3 cell, int expiry)
        {
            if (pawn?.Spawned != true || !cell.IsValid || !cell.InBounds(map) ||
                !cell.Walkable(map) || !pawn.CanReach(cell, PathEndMode.OnCell, Danger.Deadly)) return;
            Job job = JobMaker.MakeJob(JobDefOf.Goto, cell);
            job.expiryInterval = expiry;
            job.locomotionUrgency = LocomotionUrgency.Jog;
            pawn.jobs.StartJob(job, JobCondition.InterruptForced);
        }

        private IntVec3 SafeCell(IntVec3 origin, Vector2 direction, float distance)
        {
            IntVec3 wanted = origin + new IntVec3(Mathf.RoundToInt(direction.x * distance), 0,
                Mathf.RoundToInt(direction.y * distance));
            wanted.x = Mathf.Clamp(wanted.x, 1, map.Size.x - 2);
            wanted.z = Mathf.Clamp(wanted.z, 1, map.Size.z - 2);
            return CellFinder.RandomClosewalkCellNear(wanted, map, 6);
        }

        private bool Announce(string title, string text, Pawn target, bool negative)
        {
            lastIncident = title + ": " + text;
            Find.LetterStack.ReceiveLetter(title, text,
                negative ? LetterDefOf.NegativeEvent : LetterDefOf.NeutralEvent, target);
            WildlifeExperience.Record("Wildlife Incident", text, target, negative);
            if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("WildlifeIncident",
                "kind=" + title.Replace(" ", string.Empty), target);
            return true;
        }

        private static int PositiveHash(int value) => value == int.MinValue ? 0 : Math.Abs(value);
    }

    public static class WildlifeLifeUtility
    {
        public static AnimalPersonalityRecord Record(Pawn animal) =>
            animal == null ? null :
            animal.MapHeld?.GetComponent<WildlifeLivesMapComponent>()?.For(animal) ??
            Find.Maps.Select(map => map.GetComponent<WildlifeLivesMapComponent>()?.For(animal, false))
                .FirstOrDefault(record => record != null);

        public static string PersonalityLabel(Pawn animal) =>
            Record(animal)?.personality.ToString() ?? "Unrecorded";

        public static string PersonalityDescription(Pawn animal)
        {
            AnimalPersonalityRecord record = Record(animal);
            if (record == null) return "No individual temperament is being simulated.";
            string effect = record.personality == AnimalPersonality.Bold
                ? "Slow to flee and less strongly affected by frightening encounters."
                : record.personality == AnimalPersonality.Curious
                    ? "Approaches novelty more readily and forms positive memories quickly."
                : record.personality == AnimalPersonality.Cautious
                    ? "More vigilant, keeps greater distance, and remembers danger strongly."
                : record.personality == AnimalPersonality.Loyal
                    ? "Forms strong, lasting trust toward familiar colonists and companions."
                : record.personality == AnimalPersonality.Territorial
                    ? "Strongly resists intrusion and remembers hostile encounters."
                    : "Learns threats and repeated hunting tactics unusually quickly.";
            if (record.inherited && record.inheritedFrom != null)
                effect += "\nInherited from " + record.inheritedFrom.LabelShortCap + ".";
            return effect;
        }

        public static float VigilanceFactor(Pawn animal)
        {
            AnimalPersonalityRecord record = Record(animal);
            if (record == null) return 1f;
            return record.personality == AnimalPersonality.Cautious ? 1.16f :
                record.personality == AnimalPersonality.Clever ? 1.10f :
                record.personality == AnimalPersonality.Bold ? 0.86f :
                record.personality == AnimalPersonality.Curious ? 0.92f : 1f;
        }

        public static float AvoidanceFactor(Pawn animal)
        {
            AnimalPersonalityRecord record = Record(animal);
            if (record == null) return 1f;
            return record.personality == AnimalPersonality.Cautious ? 1.20f :
                record.personality == AnimalPersonality.Clever ? 1.10f :
                record.personality == AnimalPersonality.Territorial ? 1.12f :
                record.personality == AnimalPersonality.Bold ? 0.76f :
                record.personality == AnimalPersonality.Curious ? 0.84f : 0.96f;
        }

        public static float MemoryFactor(Pawn animal, bool positive)
        {
            AnimalPersonalityRecord record = Record(animal);
            if (record == null) return 1f;
            if (record.personality == AnimalPersonality.Loyal && positive) return 1.35f;
            if (record.personality == AnimalPersonality.Curious && positive) return 1.18f;
            if (record.personality == AnimalPersonality.Cautious && !positive) return 1.28f;
            if (record.personality == AnimalPersonality.Territorial && !positive) return 1.18f;
            if (record.personality == AnimalPersonality.Clever) return 1.14f;
            if (record.personality == AnimalPersonality.Bold && !positive) return 0.82f;
            return 1f;
        }
    }

    public static class WildlifeLivesDebug
    {
        [DebugAction("Wildlife", "Trigger wildlife life incident",
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void TriggerIncident()
        {
            bool result = Find.CurrentMap?.GetComponent<WildlifeLivesMapComponent>()
                ?.DebugTriggerIncident() == true;
            Messages.Message(result ? "Wildlife incident triggered." :
                "No valid wildlife incident was available.", result
                    ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.RejectInput, false);
        }

        [DebugAction("Wildlife", "Cycle animal personality",
            actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void CyclePersonality()
        {
            Pawn animal = UI.MouseCell().GetFirstPawn(Find.CurrentMap);
            if (animal?.RaceProps?.Animal != true)
            {
                Messages.Message("Choose an animal.", MessageTypeDefOf.RejectInput, false);
                return;
            }
            WildlifeLivesMapComponent component = Find.CurrentMap
                .GetComponent<WildlifeLivesMapComponent>();
            component.DebugCyclePersonality(animal);
            Messages.Message(animal.LabelShortCap + ": " +
                WildlifeLifeUtility.PersonalityLabel(animal), animal,
                MessageTypeDefOf.PositiveEvent, false);
        }
    }
}
