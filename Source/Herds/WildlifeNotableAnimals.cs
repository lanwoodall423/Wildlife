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
    internal static class NotableAnimalActionPolicy
    {
        internal static readonly string[] Order = { "Study", "Hunt", "Protect", "Capture" };
    }

    public enum NotableAnimalIntent
    {
        Observe,
        Hunt,
        Capture,
        Protect,
        Track
    }

    public enum WildlifeCulturalStatus
    {
        Unremarked,
        Feared,
        Beloved,
        Sacred,
        Legendary
    }

    public sealed class NotableAnimalRecord : IExposable
    {
        public Pawn animal;
        public ThingDef species;
        public string title;
        public string distinction;
        public HediffDef ability;
        public int discoveredTick;
        public bool deathRecorded;
        public NotableAnimalIntent intent;
        public int sightings = 1;
        public int escapes;
        public int studies;
        public bool wasPresent;
        public bool captureRecorded;
        public IntVec3 lastKnownPosition = IntVec3.Invalid;
        public int lastTerritoryStoryTick;
        public int territoryShifts;
        public bool injuryObserved;
        public WildlifeCulturalStatus culturalStatus;
        public bool culturalStatusDeclared;
        public int lastLegendPresentationTick;
        public int lastProtectionResponseTick;
        public List<string> history = new List<string>();

        public void ExposeData()
        {
            Scribe_References.Look(ref animal, "animal");
            Scribe_Defs.Look(ref species, "species");
            Scribe_Values.Look(ref title, "title");
            Scribe_Values.Look(ref distinction, "distinction");
            Scribe_Defs.Look(ref ability, "ability");
            Scribe_Values.Look(ref discoveredTick, "discoveredTick", 0);
            Scribe_Values.Look(ref deathRecorded, "deathRecorded", false);
            Scribe_Values.Look(ref intent, "intent", NotableAnimalIntent.Observe);
            Scribe_Values.Look(ref sightings, "sightings", 1);
            Scribe_Values.Look(ref escapes, "escapes", 0);
            Scribe_Values.Look(ref studies, "studies", 0);
            Scribe_Values.Look(ref wasPresent, "wasPresent", false);
            Scribe_Values.Look(ref captureRecorded, "captureRecorded", false);
            Scribe_Values.Look(ref lastKnownPosition, "lastKnownPosition", IntVec3.Invalid);
            Scribe_Values.Look(ref lastTerritoryStoryTick, "lastTerritoryStoryTick", 0);
            Scribe_Values.Look(ref territoryShifts, "territoryShifts", 0);
            Scribe_Values.Look(ref injuryObserved, "injuryObserved", false);
            Scribe_Values.Look(ref culturalStatus, "culturalStatus", WildlifeCulturalStatus.Unremarked);
            Scribe_Values.Look(ref culturalStatusDeclared, "culturalStatusDeclared", false);
            Scribe_Values.Look(ref lastLegendPresentationTick, "lastLegendPresentationTick");
            Scribe_Values.Look(ref lastProtectionResponseTick, "lastProtectionResponseTick");
            Scribe_Collections.Look(ref history, "history", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) history ??= new List<string>();
        }
    }

    public sealed class NotableWildlifeMapComponent : MapComponent
    {
        private List<NotableAnimalRecord> records = new List<NotableAnimalRecord>();
        private int nextCheckTick;
        private int lastDiscoveryTick;

        private static readonly string[] Names =
        {
            "Ashback", "Longstep", "White Ear", "Old Thorn", "Red Foot", "Ghost", "Stonehide", "Dusk Runner"
        };

        public NotableWildlifeMapComponent(Map map) : base(map) { }
        public IReadOnlyList<NotableAnimalRecord> Records => records;
        public int ActiveCount => records.Count(record => record?.animal?.Spawned == true && !record.animal.Dead);
        public int ProtectedCount(ThingDef species) => records.Count(record =>
            record?.species == species && record.intent == NotableAnimalIntent.Protect &&
            record.animal != null && !record.animal.Dead);

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref records, "notableWildlife", LookMode.Deep);
            Scribe_Values.Look(ref nextCheckTick, "nextNotableWildlifeCheck", 0);
            Scribe_Values.Look(ref lastDiscoveryTick, "lastNotableWildlifeDiscovery", 0);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                records = records?.Where(record => record?.species?.race?.Animal == true).ToList() ??
                    new List<NotableAnimalRecord>();
        }

        public override void MapComponentTick()
        {
            int now = Find.TickManager.TicksGame;
            if (now < nextCheckTick) return;
            nextCheckTick = now + 60000;
            if (HerdsMod.Settings?.enableNotableAnimals != true)
            {
                RemoveActiveEffects();
                return;
            }
            MaintainRecords();
            if (ActiveCount >= 4 || now - lastDiscoveryTick < 60000 || !Rand.Chance(0.22f)) return;
            Pawn candidate = map.mapPawns.AllPawnsSpawned
                .Where(IsEligible)
                .Where(pawn => For(pawn) == null)
                .OrderByDescending(NotabilityScore)
                .FirstOrDefault();
            if (candidate != null && NotabilityScore(candidate) >= 0.42f) MakeNotable(candidate, false);
        }

        public override void MapComponentDraw()
        {
            if (!Prefs.DevMode || !WildlifeDevMaster.CompleteOverlayEnabled ||
                HerdsMod.Settings?.enableNotableAnimals != true || Find.CurrentMap != map) return;
            for (int i = 0; i < records.Count; i++)
            {
                NotableAnimalRecord record = records[i];
                if (record?.animal?.Spawned != true || record.animal.Dead) continue;
                Color color = record.intent == NotableAnimalIntent.Protect ? new Color(0.25f, 1f, 0.42f) :
                    record.culturalStatus == WildlifeCulturalStatus.Sacred ? new Color(0.85f, 0.72f, 1f) :
                    record.culturalStatus == WildlifeCulturalStatus.Beloved ? new Color(0.45f, 1f, 0.58f) :
                    record.culturalStatus == WildlifeCulturalStatus.Feared ? new Color(1f, 0.25f, 0.2f) :
                    record.culturalStatus == WildlifeCulturalStatus.Legendary ? new Color(1f, 0.78f, 0.12f) :
                    new Color(0.94f, 0.72f, 0.18f);
                GenDraw.DrawRadiusRing(record.animal.Position, 1.15f, color);
                if (record.intent == NotableAnimalIntent.Protect)
                    GenDraw.DrawRadiusRing(record.animal.Position, 1.55f,
                        new Color(color.r, color.g, color.b, 0.72f));
            }
        }

        public List<string> DebugOverviewLines() => new List<string>
        {
            "NOTABLE active=" + ActiveCount + " records=" + records.Count,
            "NOTABLE titles=" + string.Join(",", records.Where(record => record?.animal?.Spawned == true)
                .Select(record => record.title + ":" + record.culturalStatus).Take(6))
        };

        public NotableAnimalRecord For(Pawn pawn) =>
            pawn == null ? null : records.FirstOrDefault(record => record?.animal == pawn);

        public NotableAnimalRecord MakeNotable(Pawn pawn, bool forced)
        {
            if (pawn?.RaceProps?.Animal != true || pawn.Dead) return null;
            NotableAnimalRecord existing = For(pawn);
            if (existing != null)
            {
                ApplyAbility(existing);
                return existing;
            }
            int hash = Math.Abs(Gen.HashCombineInt(pawn.thingIDNumber, map.uniqueID));
            int kind = hash % 3;
            HediffDef ability = DefDatabase<HediffDef>.GetNamedSilentFail(kind == 0
                ? "Herds_NotableSwift" : kind == 1 ? "Herds_NotableCunning" : "Herds_NotableScarred");
            string distinction = kind == 0 ? "Exceptionally swift" :
                kind == 1 ? "Unusually perceptive and difficult to surprise" :
                "A hardened survivor of old wounds";
            string name = Names[(hash / 3) % Names.Length];
            if (pawn.Name == null || pawn.Name.Numerical) pawn.Name = new NameSingle(name);
            NotableAnimalRecord record = new NotableAnimalRecord
            {
                animal = pawn,
                species = pawn.def,
                title = name,
                distinction = distinction,
                ability = ability,
                discoveredTick = Find.TickManager.TicksGame,
                wasPresent = pawn.Spawned,
                lastKnownPosition = pawn.Position
            };
            record.history.Add("Recognized near the colony as " + name + ".");
            records.Add(record);
            lastDiscoveryTick = record.discoveredTick;
            ApplyAbility(record);
            string text = pawn.def.LabelCap + " known as " + name + " has been recognized as a notable animal. " + distinction + ".";
            if (!forced && HerdsMod.Settings.enableWildlifeAlerts)
                Find.LetterStack.ReceiveLetter("Notable Animal", text, LetterDefOf.NeutralEvent, pawn);
            else
                Messages.Message(text, pawn, MessageTypeDefOf.PositiveEvent, false);
            WildlifeExperience.Record("Notable Animal", text, pawn);
            WildlifeMemoryUtility.Folklore(map, "The First Sighting of " + name,
                "The colony recognized " + name + ", " + pawn.def.label + ": " + distinction + ".", pawn);
            if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("NotableAnimal",
                "species=" + pawn.def.defName + " title=" + name + " ability=" + (ability?.defName ?? "none"), pawn);
            return record;
        }

        private void MaintainRecords()
        {
            for (int i = 0; i < records.Count; i++)
            {
                NotableAnimalRecord record = records[i];
                if (record?.animal == null) continue;
                if (record.animal.Dead)
                {
                    if (record.deathRecorded) continue;
                    record.deathRecorded = true;
                    WildlifeExperience.Record("Notable Animal",
                        record.title + ", the notable " + record.species.label + ", died.", record.animal.Corpse, true);
                    WildlifeMemoryUtility.Folklore(map, "The Death of " + record.title,
                        record.title + ", the notable " + record.species.label + ", died after " +
                        record.sightings + " sightings and " + record.escapes + " escapes.", record.animal, false);
                    continue;
                }
                if (record.animal.Spawned && !record.wasPresent)
                {
                    record.wasPresent = true;
                    record.sightings++;
                    AddHistory(record, "Sighted again near the colony.");
                    AnimalColonistMemory remembered = map.GetComponent<WildlifeMemoryMapComponent>()?.Memories
                        .Where(value => value?.animal == record.animal).OrderByDescending(value =>
                            Mathf.Max(value.trust, Mathf.Max(value.fear, value.hostility))).FirstOrDefault();
                    if (remembered != null)
                        AddHistory(record, "Returned still " +
                            (remembered.trust >= remembered.fear && remembered.trust >= remembered.hostility
                                ? "trusting of " : remembered.hostility >= remembered.fear ? "hostile toward " : "fearful of ") +
                            remembered.colonist.LabelShortCap + ".");
                    if (HerdsMod.Settings.enableWildlifeAlerts)
                        Messages.Message(record.title + " has been sighted again.", record.animal,
                            MessageTypeDefOf.NeutralEvent, false);
                }
                else if (!record.animal.Spawned) record.wasPresent = false;
                if (!record.captureRecorded && record.animal.Faction == Faction.OfPlayer)
                {
                    record.captureRecorded = true;
                    AddHistory(record, "Joined the colony after being captured or tamed.");
                    Messages.Message(record.title + " has joined the colony.", record.animal,
                        MessageTypeDefOf.PositiveEvent, false);
                }
                if (record.animal.Spawned)
                {
                    float health = record.animal.health.summaryHealth.SummaryHealthPercent;
                    if (!record.injuryObserved && health < 0.72f)
                    {
                        record.injuryObserved = true;
                        AddHistory(record, "Survived a serious injury.");
                    }
                    else if (record.injuryObserved && health > 0.95f)
                    {
                        record.injuryObserved = false;
                        AddHistory(record, "Recovered from its wounds.");
                    }
                    int now = Find.TickManager.TicksGame;
                    if (record.lastKnownPosition.IsValid && now - record.lastTerritoryStoryTick > 120000 &&
                        record.animal.Position.DistanceToSquared(record.lastKnownPosition) > 3600)
                    {
                        record.territoryShifts++;
                        record.lastTerritoryStoryTick = now;
                        record.lastKnownPosition = record.animal.Position;
                        AddHistory(record, "Shifted its range to a new part of the colony territory.");
                    }
                    else if (!record.lastKnownPosition.IsValid) record.lastKnownPosition = record.animal.Position;
                }
                EvaluateCulturalStatus(record);
                PresentLegend(record);
                ApplyAbility(record);
            }
        }

        private void PresentLegend(NotableAnimalRecord record)
        {
            if (HerdsMod.Settings?.enableLegendaryPresentation != true ||
                record?.culturalStatus != WildlifeCulturalStatus.Legendary ||
                record.animal?.Spawned != true) return;
            int now = Find.TickManager.TicksGame;
            if (now - record.lastLegendPresentationTick < 60000) return;
            record.lastLegendPresentationTick = now;
            if (record.animal.Position.GetFirstThing(map, HerdsDefOf.Herds_WildlifeSign) == null)
            {
                WildlifeSign sign = (WildlifeSign)ThingMaker.MakeThing(HerdsDefOf.Herds_WildlifeSign);
                sign.species = record.species;
                sign.sourceAnimal = record.animal;
                sign.createdTick = now;
                sign.travelFrom = record.lastKnownPosition.IsValid ? record.lastKnownPosition : record.animal.Position;
                sign.travelTo = record.animal.Position;
                sign.predator = record.animal.RaceProps.predator;
                sign.signKind = sign.predator ? WildlifeSignKind.TerritoryMark : WildlifeSignKind.Tracks;
                sign.legendary = true;
                sign.legendTitle = record.title;
                GenSpawn.Spawn(sign, record.animal.Position, map);
            }
            if (Rand.Chance(0.45f))
                MoteMaker.ThrowText(record.animal.DrawPos, map,
                    record.animal.RaceProps.predator ? "A legendary call echoes." : "A legendary animal passes.");
        }

        private void EvaluateCulturalStatus(NotableAnimalRecord record)
        {
            if (HerdsMod.Settings?.enableCulturalAnimals != true || record?.culturalStatusDeclared == true) return;
            WildlifeCulturalStatus next = WildlifeCulturalStatus.Unremarked;
            bool revered = ModsConfig.IdeologyActive && map.mapPawns.FreeColonists.Any(pawn =>
                pawn.Ideo?.HasPrecept(HerdsDefOf.Herds_WildlifeEthic_Reverence) == true);
            WildlifeMemoryMapComponent memory = map.GetComponent<WildlifeMemoryMapComponent>();
            float trust = memory?.Memories.Where(value => value?.animal == record.animal)
                .Select(value => value.trust).DefaultIfEmpty().Max() ?? 0f;
            if (record.sightings + record.escapes + record.studies + record.territoryShifts >= 7)
                next = WildlifeCulturalStatus.Legendary;
            else if (revered && record.intent == NotableAnimalIntent.Protect)
                next = WildlifeCulturalStatus.Sacred;
            else if (record.captureRecorded || record.studies >= 3 || trust >= 0.65f)
                next = WildlifeCulturalStatus.Beloved;
            else if (record.species?.race?.predator == true && record.escapes >= 2)
                next = WildlifeCulturalStatus.Feared;
            if (next == record.culturalStatus) return;
            record.culturalStatus = next;
            if (next != WildlifeCulturalStatus.Unremarked)
            {
                AddHistory(record, "Became known as " + next.ToString().ToLowerInvariant() + " in colony culture.");
                WildlifeMemoryUtility.Folklore(map, record.title + ", the " + next,
                    record.title + " became " + next.ToString().ToLowerInvariant() +
                    " in the stories and customs of the colony.", record.animal);
                Messages.Message(record.title + " is now regarded as " + next.ToString().ToLowerInvariant() + ".",
                    record.animal, MessageTypeDefOf.PositiveEvent, false);
            }
        }

        public void SetCulturalStatus(NotableAnimalRecord record, WildlifeCulturalStatus status)
        {
            if (record == null || HerdsMod.Settings?.enableCulturalAnimals != true) return;
            record.culturalStatus = status;
            record.culturalStatusDeclared = status != WildlifeCulturalStatus.Unremarked;
            AddHistory(record, status == WildlifeCulturalStatus.Unremarked
                ? "No longer held a formal place in colony culture."
                : "Was formally recognized as " + status.ToString().ToLowerInvariant() + ".");
        }

        public void SetIntent(NotableAnimalRecord record, NotableAnimalIntent intent)
        {
            if (record?.animal == null || record.animal.Dead) return;
            record.intent = intent;
            DesignationManager designations = record.animal.Map?.designationManager;
            Designation hunt = designations?.DesignationOn(record.animal, DesignationDefOf.Hunt);
            Designation tame = designations?.DesignationOn(record.animal, DesignationDefOf.Tame);
            if (hunt != null) designations.RemoveDesignation(hunt);
            if (tame != null) designations.RemoveDesignation(tame);
            if (intent == NotableAnimalIntent.Hunt && record.animal.Spawned)
                designations?.AddDesignation(new Designation(record.animal, DesignationDefOf.Hunt));
            else if (intent == NotableAnimalIntent.Capture && record.animal.Spawned)
                designations?.AddDesignation(new Designation(record.animal, DesignationDefOf.Tame));
            else if (intent == NotableAnimalIntent.Protect)
            {
                map.GetComponent<RegionalWildlifeMapComponent>()?.ApplyExpeditionImpact(record.species, 0.5f, 0.10f);
                Pawn steward = map.mapPawns.FreeColonistsSpawned.OrderByDescending(pawn =>
                    pawn.skills?.GetSkill(SkillDefOf.Animals)?.Level ?? 0).FirstOrDefault();
                WildlifeMemoryUtility.Remember(record.animal, steward, AnimalMemoryKind.Protected);
                WildlifeIdeologyUtility.Notify(map, WildlifeIdeologyEvent.Protect, record.animal, steward);
            }
            AddHistory(record, intent == NotableAnimalIntent.Protect
                ? "The colony declared this animal protected."
                : "The colony chose to " + intent.ToString().ToLowerInvariant() + " this animal.");
        }

        public void CompleteStudy(Pawn colonist, Pawn animal, bool fitCollar)
        {
            NotableAnimalRecord record = For(animal);
            if (record == null || colonist == null) return;
            if (fitCollar)
            {
                Thing collar = map.listerThings.ThingsOfDef(HerdsDefOf.Herds_TrackingCollarItem)
                    .FirstOrDefault(thing => !thing.Destroyed);
                if (collar == null || HerdsDefOf.Herds_TrackingCollar == null) return;
                collar.SplitOff(1).Destroy(DestroyMode.Vanish);
                if (animal.health.hediffSet.GetFirstHediffOfDef(HerdsDefOf.Herds_TrackingCollar) == null)
                    animal.health.AddHediff(HerdsDefOf.Herds_TrackingCollar);
                record.intent = NotableAnimalIntent.Track;
                AddHistory(record, colonist.LabelShortCap + " fitted a tracking collar.");
            }
            else
            {
                record.studies++;
                float trust = map.GetComponent<WildlifeMemoryMapComponent>()?.TrustFor(animal, colonist) ?? 0f;
                float fear = map.GetComponent<WildlifeMemoryMapComponent>()?.FearFor(animal, colonist) ?? 0f;
                map.GetComponent<HuntingKnowledgeMapComponent>()?.Learn(colonist, animal.def,
                    70f * Mathf.Clamp(1f + trust * 0.35f - fear * 0.15f, 0.8f, 1.35f), true);
                AddHistory(record, colonist.LabelShortCap + " completed a safe-distance field study.");
                WildlifeMemoryUtility.Remember(animal, colonist, AnimalMemoryKind.Studied);
                WildlifeIdeologyUtility.Notify(map, WildlifeIdeologyEvent.Study, animal, colonist);
            }
            Messages.Message(record.history.Last(), animal, MessageTypeDefOf.PositiveEvent, false);
        }

        public void NotifyHuntOutcome(Pawn animal, string result)
        {
            NotableAnimalRecord record = For(animal);
            bool escaped = result == "escaped" || result == "hidden" || result == "trail-lost" ||
                result == "pursuit-exhausted" || result == "escaped-during-staging" ||
                result == "hidden-during-staging";
            if (record == null)
            {
                if (escaped) map.GetComponent<WildlifeLivesMapComponent>()?.RegisterHuntEscape(animal);
                return;
            }
            if (result == "kill" || result == "killed-during-staging")
            {
                AddHistory(record, "Killed during a colony hunt after " + record.escapes + " recorded escapes.");
                WildlifeMemoryUtility.Folklore(map, "The Last Hunt of " + record.title,
                    record.title + " was finally brought down after " + record.escapes + " earlier escapes.", animal,
                    false);
            }
            else if (escaped)
            {
                record.escapes++;
                map.GetComponent<WildlifeLivesMapComponent>()?.RegisterHuntEscape(animal);
                AddHistory(record, "Escaped a colony hunt (" + record.escapes + " total).");
                if (HerdsMod.Settings.enableWildlifeAlerts)
                    Messages.Message(record.title + " escaped the hunt.", animal, MessageTypeDefOf.NeutralEvent, false);
                if (record.escapes == 3)
                    WildlifeMemoryUtility.Folklore(map, record.title + ", the Elusive",
                        record.title + " escaped a colony hunt for the third time and became a story told among the hunters.", animal);
            }
        }

        public void NotifyTracked(Pawn animal, string source)
        {
            NotableAnimalRecord record = For(animal);
            if (record == null) return;
            record.intent = NotableAnimalIntent.Track;
            AddHistory(record, "Fitted with a tracking collar" +
                (source.NullOrEmpty() ? "." : " at " + source + "."));
        }

        public void NotifyProtectedAttack(NotableAnimalRecord record, Thing attacker, int responders)
        {
            if (record?.animal == null) return;
            string threat = attacker?.LabelShortCap ?? "an unknown threat";
            AddHistory(record, "Was attacked by " + threat + "; " + responders +
                (responders == 1 ? " colonist responded." : " colonists responded."));
            if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("ProtectedAnimal",
                "event=attacked attacker=" + (attacker?.thingIDNumber.ToString() ?? "none") +
                " responders=" + responders, record.animal, attacker);
        }

        public void NotifyProtectedDeath(NotableAnimalRecord record, Thing cause)
        {
            if (record?.animal == null) return;
            AddHistory(record, "Died while under colony protection" +
                (cause == null ? "." : " after an attack by " + cause.LabelShortCap + "."));
            foreach (Pawn colonist in map.mapPawns.FreeColonists)
                colonist.needs?.mood?.thoughts?.memories?.TryGainMemory(HerdsDefOf.Herds_ProtectedAnimalDied);
            WildlifeMemoryUtility.Folklore(map, "A Promise Broken: " + record.title,
                record.title + " died while under the colony's protection. The failure became part of its lasting story.",
                record.animal, false);
            WildlifeIdeologyUtility.Notify(map, WildlifeIdeologyEvent.ProtectedDeath, record.animal);
            if (HerdsMod.Settings.enableWildlifeAlerts)
                Find.LetterStack.ReceiveLetter("Protected Animal Died",
                    record.title + " died while under colony protection. Colonists are upset that the commitment was not kept.",
                    LetterDefOf.NegativeEvent, record.animal);
        }

        private void AddHistory(NotableAnimalRecord record, string text)
        {
            if (record == null || text.NullOrEmpty()) return;
            record.history.Insert(0, GenDate.DateFullStringAt(Find.TickManager.TicksAbs,
                Find.WorldGrid.LongLatOf(map.Tile)) + " — " + text);
            if (record.history.Count > 12) record.history.RemoveRange(12, record.history.Count - 12);
            WildlifeExperience.Record("Notable Animal", record.title + ": " + text, record.animal);
            if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("NotableStory",
                "title=" + record.title + " intent=" + record.intent + " event=" + text, record.animal);
        }

        private void ApplyAbility(NotableAnimalRecord record)
        {
            if (record?.animal?.health == null || record.ability == null || record.animal.Dead) return;
            if (record.animal.health.hediffSet.GetFirstHediffOfDef(record.ability) == null)
                record.animal.health.AddHediff(record.ability);
        }

        private void RemoveActiveEffects()
        {
            for (int i = 0; i < records.Count; i++)
            {
                NotableAnimalRecord record = records[i];
                Hediff hediff = record?.animal?.health?.hediffSet?.GetFirstHediffOfDef(record.ability);
                if (hediff != null) record.animal.health.RemoveHediff(hediff);
            }
        }

        private static bool IsEligible(Pawn pawn) =>
            pawn?.Spawned == true && !pawn.Dead && pawn.RaceProps?.Animal == true &&
            pawn.Faction != Faction.OfPlayer && pawn.ageTracker?.CurLifeStage?.reproductive == true;

        private static float NotabilityScore(Pawn pawn)
        {
            float age = pawn.RaceProps.lifeExpectancy <= 0f ? 0f :
                pawn.ageTracker.AgeBiologicalYearsFloat / pawn.RaceProps.lifeExpectancy;
            return Mathf.Clamp01(age * 0.55f + Mathf.InverseLerp(0.2f, 3f, pawn.BodySize) * 0.25f +
                (pawn.RaceProps.predator ? 0.18f : 0f) + pawn.health.hediffSet.hediffs.Count * 0.025f);
        }

        [DebugAction("Wildlife", "Make animal notable", actionType = DebugActionType.ToolMap,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void DebugMakeNotable()
        {
            Pawn pawn = UI.MouseCell().GetFirstPawn(Find.CurrentMap);
            if (pawn?.RaceProps?.Animal != true)
            {
                Messages.Message("Choose an animal.", MessageTypeDefOf.RejectInput, false);
                return;
            }
            Find.CurrentMap.GetComponent<NotableWildlifeMapComponent>()?.MakeNotable(pawn, true);
        }
    }

    public sealed class JobDriver_StudyNotableAnimal : JobDriver
    {
        internal const float MinimumStudyDistance = 18f;
        internal const float MaximumStudyDistance = 28f;

        public override bool TryMakePreToilReservations(bool errorOnFailed) => job.count == 1
            ? pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed)
            : pawn.Reserve(job.targetB.Cell, job, 1, -1, null, errorOnFailed);

        public static bool TryFindStudyCell(Pawn observer, Pawn animal, out IntVec3 result)
        {
            result = IntVec3.Invalid;
            if (observer?.Spawned != true || animal?.Spawned != true || observer.Map != animal.Map)
                return false;
            Map map = animal.Map;
            float minimumSquared = MinimumStudyDistance * MinimumStudyDistance;
            float maximumSquared = MaximumStudyDistance * MaximumStudyDistance;
            int count = GenRadial.NumCellsInRadius(MaximumStudyDistance);
            int start = PositiveMod(observer.thingIDNumber + animal.thingIDNumber, count);
            float best = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                IntVec3 cell = animal.Position + GenRadial.RadialPattern[(start + i) % count];
                float distance = cell.DistanceToSquared(animal.Position);
                if (distance < minimumSquared || distance > maximumSquared || !cell.InBounds(map) ||
                    !cell.Standable(map) || cell.IsForbidden(observer) ||
                    !GenSight.LineOfSight(cell, animal.Position, map) ||
                    !observer.CanReach(cell, PathEndMode.OnCell, Danger.Some)) continue;
                float score = observer.Position.DistanceToSquared(cell) +
                    Mathf.Abs(distance - 484f) * 0.2f;
                if (score >= best) continue;
                best = score;
                result = cell;
            }
            return result.IsValid;
        }

        private static int PositiveMod(int value, int modulus)
        {
            int result = value % modulus;
            return result < 0 ? result + modulus : result;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            yield return job.count == 1
                ? Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch)
                : Toils_Goto.GotoCell(TargetIndex.B, PathEndMode.OnCell);
            int proficiency = pawn.Map?.GetComponent<HuntingKnowledgeMapComponent>()?.WildlifeProficiencyLevel(pawn) ?? 0;
            int baseTicks = job.count == 1 ? 900 : 1200;
            Toil study = Toils_General.Wait(Mathf.RoundToInt(baseTicks * (1f - proficiency * 0.08f)), TargetIndex.A);
            study.socialMode = RandomSocialMode.Off;
            study.WithProgressBarToilDelay(TargetIndex.A);
            if (job.count != 1)
                study.AddFailCondition(() => job.targetA.Pawn?.Spawned != true ||
                    !pawn.Position.InHorDistOf(job.targetA.Pawn.Position, MaximumStudyDistance) ||
                    pawn.Position.DistanceToSquared(job.targetA.Pawn.Position) <
                        MinimumStudyDistance * MinimumStudyDistance ||
                    !GenSight.LineOfSight(pawn.Position, job.targetA.Pawn.Position, pawn.Map));
            yield return study;
            Toil finish = ToilMaker.MakeToil("CompleteNotableAnimalStudy");
            finish.initAction = () =>
            {
                Pawn animal = job.targetA.Pawn;
                animal?.Map?.GetComponent<NotableWildlifeMapComponent>()?.CompleteStudy(pawn, animal, job.count == 1);
            };
            finish.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return finish;
        }
    }

    public sealed class Window_NotableAnimalStory : Window
    {
        private readonly NotableWildlifeMapComponent component;
        private readonly NotableAnimalRecord record;
        private Vector2 scroll;
        public override Vector2 InitialSize => new Vector2(720f, 620f);

        public Window_NotableAnimalStory(NotableWildlifeMapComponent component, NotableAnimalRecord record)
        {
            this.component = component;
            this.record = record;
            doCloseX = true;
            absorbInputAroundWindow = true;
        }

        public override void DoWindowContents(Rect rect)
        {
            Pawn animal = record?.animal;
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, rect.width, 34f), record?.title ?? "Notable Animal");
            Text.Font = GameFont.Small;
            GUI.color = new Color(0.67f, 0.76f, 0.68f);
            Widgets.Label(new Rect(0f, 35f, rect.width, 24f),
                (record?.species?.LabelCap.ToString() ?? "Unknown animal") + " • " + record?.distinction);
            GUI.color = Color.white;

            Rect summary = new Rect(0f, 68f, rect.width, 92f);
            Widgets.DrawMenuSection(summary);
            Widgets.Label(new Rect(12f, 78f, rect.width - 24f, 24f),
                "Intent: " + record.intent + "    Sightings: " + record.sightings +
                "    Escapes: " + record.escapes + "    Studies: " + record.studies);
            Widgets.Label(new Rect(12f, 107f, rect.width - 190f, 40f),
                animal?.Dead == true ? "Status: Dead" :
                animal?.Spawned == true ? "Status: Present near the colony" : "Status: Away from the colony");
            Rect cultureButton = new Rect(rect.width - 166f, 108f, 154f, 32f);
            if (HerdsMod.Settings.enableCulturalAnimals &&
                Widgets.ButtonText(cultureButton, "Culture: " + record.culturalStatus))
                ShowCulturalStatusMenu();
            TooltipHandler.TipRegion(cultureButton,
                "How this animal figures in colony culture. Status may emerge from its history and ideology, or be formally declared.");

            Widgets.Label(new Rect(0f, 172f, rect.width, 24f), "Colony Response");
            bool showTrack = WildlifeProgression.Unlocked(WildlifeCapability.Telemetry);
            int responseCount = showTrack ? 5 : 4;
            float gap = 4f;
            float width = (rect.width - gap * (responseCount - 1)) / responseCount;
            Rect studyRect = new Rect(0f, 201f, width, 38f);
            if (Widgets.ButtonText(studyRect, NotableAnimalActionPolicy.Order[0], active: animal?.Spawned == true))
                ChooseColonist(false);
            TooltipHandler.TipRegion(studyRect,
                "Choose a colonist to observe from a safe distance with a clear line of sight. This increases Animal Knowledge without approaching closely enough to scare the animal.");
            DrawIntent(new Rect(width + gap, 201f, width, 38f), NotableAnimalActionPolicy.Order[1], NotableAnimalIntent.Hunt);
            DrawIntent(new Rect((width + gap) * 2f, 201f, width, 38f), NotableAnimalActionPolicy.Order[2], NotableAnimalIntent.Protect);
            DrawIntent(new Rect((width + gap) * 3f, 201f, width, 38f), NotableAnimalActionPolicy.Order[3], NotableAnimalIntent.Capture);
            if (showTrack)
            {
                bool canTrack = animal?.Spawned == true && HerdsDefOf.Herds_TrackingCollarItem != null &&
                    animal.Map.listerThings.ThingsOfDef(HerdsDefOf.Herds_TrackingCollarItem).Any();
                Rect trackRect = new Rect((width + gap) * 4f, 201f, width, 38f);
                if (Widgets.ButtonText(trackRect, "Track", active: canTrack))
                    ChooseColonist(true);
                TooltipHandler.TipRegion(trackRect,
                    "Fit a tracking collar so the colony can follow this animal's movements and resolve tracking-related legend challenges. Requires an available tracking collar.");
            }

            Widgets.Label(new Rect(0f, 254f, rect.width, 24f), "Known Story");
            Rect outer = new Rect(0f, 282f, rect.width, rect.height - 282f);
            Rect view = new Rect(0f, 0f, outer.width - 18f,
                Mathf.Max(outer.height, Mathf.Max(1, record.history.Count) * 48f));
            Widgets.BeginScrollView(outer, ref scroll, view);
            if (record.history.Count == 0) Widgets.Label(new Rect(8f, 8f, view.width - 16f, 30f), "No additional history recorded.");
            for (int i = 0; i < record.history.Count; i++)
            {
                Rect row = new Rect(0f, i * 48f, view.width, 42f);
                Widgets.DrawMenuSection(row);
                Widgets.Label(row.ContractedBy(8f), record.history[i]);
            }
            Widgets.EndScrollView();
        }

        private void DrawIntent(Rect rect, string label, NotableAnimalIntent intent)
        {
            bool active = record?.animal?.Spawned == true && !record.animal.Dead;
            if (Widgets.ButtonText(rect, label, active: active))
            {
                component.SetIntent(record, intent);
                if (intent == NotableAnimalIntent.Protect)
                    Messages.Message(record.title + " is now protected by colony policy.", record.animal,
                        MessageTypeDefOf.PositiveEvent, false);
            }
            string tooltip = intent == NotableAnimalIntent.Hunt
                ? "Designate this animal for a colony hunt. Killing a culturally important animal may affect folklore and ideology."
                : intent == NotableAnimalIntent.Capture
                    ? "Designate this animal for taming. Its existing trust or fear of the handler influences the attempt, and it may become beloved after joining the colony."
                    : "Commit the colony to this animal's safety. Nearby capable colonists will respond when it is attacked, its regional population benefits, and colonists will be upset if it dies under their protection.";
            TooltipHandler.TipRegion(rect, tooltip);
        }

        private void ChooseColonist(bool fitCollar)
        {
            Pawn animal = record?.animal;
            if (animal?.Spawned != true) return;
            List<FloatMenuOption> options = animal.Map.mapPawns.FreeColonistsSpawned
                .Where(colonist => !colonist.Downed && (fitCollar
                    ? colonist.CanReach(animal, PathEndMode.Touch, Danger.Some)
                    : JobDriver_StudyNotableAnimal.TryFindStudyCell(colonist, animal, out _)))
                .OrderByDescending(colonist => colonist.skills?.GetSkill(SkillDefOf.Animals)?.Level ?? 0)
                .Select(colonist => new FloatMenuOption(colonist.LabelShortCap, () =>
                {
                    Job job = JobMaker.MakeJob(HerdsDefOf.Herds_StudyNotableAnimal, animal);
                    job.count = fitCollar ? 1 : 0;
                    if (!fitCollar && JobDriver_StudyNotableAnimal.TryFindStudyCell(colonist,
                        animal, out IntVec3 studyCell)) job.targetB = studyCell;
                    job.playerForced = true;
                    colonist.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                    if (fitCollar) component.SetIntent(record, NotableAnimalIntent.Track);
                })).ToList();
            if (options.Count == 0)
                options.Add(new FloatMenuOption("No colonist can safely reach this animal", null));
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void ShowCulturalStatusMenu()
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            foreach (WildlifeCulturalStatus status in Enum.GetValues(typeof(WildlifeCulturalStatus)))
            {
                WildlifeCulturalStatus selected = status;
                options.Add(new FloatMenuOption(status == WildlifeCulturalStatus.Unremarked
                    ? "Allow status to emerge naturally" : "Recognize as " + status,
                    () => component.SetCulturalStatus(record, selected)));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class NotableAnimalGizmoPatch
    {
        public static void Postfix(Pawn __instance, ref IEnumerable<Gizmo> __result)
        {
            if (HerdsMod.Settings?.enableNotableAnimals != true || __instance?.Spawned != true) return;
            NotableWildlifeMapComponent component = __instance.Map.GetComponent<NotableWildlifeMapComponent>();
            NotableAnimalRecord record = component?.For(__instance);
            if (record == null) return;
            __result = __result.Concat(new[]
            {
                new Command_Action
                {
                    defaultLabel = "Notable Animal",
                    defaultDesc = "Review this animal's known history and choose how the colony will respond.",
                    icon = TexCommand.OpenLinkedQuestTex,
                    action = () => Find.WindowStack.Add(new Window_NotableAnimalStory(component, record))
                }
            });
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetInspectString))]
    public static class NotableAnimalInspectPatch
    {
        public static void Postfix(Pawn __instance, ref string __result)
        {
            if (HerdsMod.Settings?.enableNotableAnimals != true || __instance?.Spawned != true) return;
            NotableAnimalRecord record = __instance.Map.GetComponent<NotableWildlifeMapComponent>()?.For(__instance);
            if (record == null) return;
            string line = "Notable: " + record.title + " — " + record.distinction +
                (record.culturalStatus == WildlifeCulturalStatus.Unremarked ? string.Empty :
                    "\nCultural status: " + record.culturalStatus);
            __result = __result.NullOrEmpty() ? line : __result.TrimEnd() + "\n" + line;
        }
    }
}
