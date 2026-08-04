using System;
using System.Collections.Generic;
using System.Linq;
using LudeonTK;
using RimWorld;
using UnityEngine;
using Verse;

namespace Herds
{
    public enum WildlifeMysteryCause
    {
        DistortedTradition,
        ColonyReputation,
        OverdueTaggedAnimal,
        HiddenBreedingGround,
        PredatorLearning,
        PopulationCollapse,
        AncestralReturn
    }

    public enum WildlifeMysteryResolution
    {
        Unresolved,
        ProtectDiscovery,
        CorrectTradition,
        ExploitForHunting,
        EstablishSanctuary,
        LeaveUndisturbed
    }

    public sealed class WildlifeMysteryEvidence : IExposable
    {
        public string clue;
        public string source;
        public int discoveredTick;
        public float value;
        public void ExposeData()
        {
            Scribe_Values.Look(ref clue, "clue");
            Scribe_Values.Look(ref source, "source");
            Scribe_Values.Look(ref discoveredTick, "discoveredTick");
            Scribe_Values.Look(ref value, "value");
        }
    }

    public sealed class WildlifeMysteryRecord : IExposable
    {
        public int id;
        public WildlifeMysteryCause cause;
        public WildlifeMysteryResolution resolution;
        public ThingDef species;
        public Pawn animal;
        public string title;
        public string anomaly;
        public string explanation;
        public float progress;
        public int startedTick;
        public int solvedTick;
        public int lastReviewTick;
        public int baselineSignStudies;
        public bool announced;
        public List<WildlifeMysteryEvidence> evidence = new List<WildlifeMysteryEvidence>();

        public bool Solved => progress >= 1f;
        public bool Resolved => resolution != WildlifeMysteryResolution.Unresolved;

        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id");
            Scribe_Values.Look(ref cause, "cause");
            Scribe_Values.Look(ref resolution, "resolution");
            Scribe_Defs.Look(ref species, "species");
            Scribe_References.Look(ref animal, "animal");
            Scribe_Values.Look(ref title, "title");
            Scribe_Values.Look(ref anomaly, "anomaly");
            Scribe_Values.Look(ref explanation, "explanation");
            Scribe_Values.Look(ref progress, "progress");
            Scribe_Values.Look(ref startedTick, "startedTick");
            Scribe_Values.Look(ref solvedTick, "solvedTick");
            Scribe_Values.Look(ref lastReviewTick, "lastReviewTick");
            Scribe_Values.Look(ref baselineSignStudies, "baselineSignStudies");
            Scribe_Values.Look(ref announced, "announced");
            Scribe_Collections.Look(ref evidence, "evidence", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) evidence ??= new List<WildlifeMysteryEvidence>();
        }
    }

    public sealed class WildlifeMysteryMapComponent : MapComponent
    {
        private List<WildlifeMysteryRecord> mysteries = new List<WildlifeMysteryRecord>();
        private int nextTick;
        private int nextDetectionTick;
        private int nextId = 1;

        public WildlifeMysteryMapComponent(Map map) : base(map) { }
        public IReadOnlyList<WildlifeMysteryRecord> Mysteries => mysteries;
        public WildlifeMysteryRecord Active => mysteries.FirstOrDefault(value => !value.Resolved);

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref mysteries, "livingWildlifeMysteries", LookMode.Deep);
            Scribe_Values.Look(ref nextTick, "nextWildlifeMysteryTick");
            Scribe_Values.Look(ref nextDetectionTick, "nextWildlifeMysteryDetection");
            Scribe_Values.Look(ref nextId, "nextWildlifeMysteryId", 1);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                mysteries = mysteries?.Where(value => value?.species?.race?.Animal == true).ToList() ??
                    new List<WildlifeMysteryRecord>();
                nextId = Mathf.Max(nextId, mysteries.Count == 0 ? 1 : mysteries.Max(value => value.id) + 1);
            }
        }

        public override void MapComponentTick()
        {
            if (HerdsMod.Settings?.enableWildlifeMysteries != true) return;
            int now = Find.TickManager.TicksGame;
            if (now < nextTick) return;
            nextTick = now + 2500;
            WildlifeMysteryRecord active = Active;
            if (active != null && !active.Solved) UpdateEvidence(active, now);
            if (active == null && now >= nextDetectionTick)
            {
                nextDetectionTick = now + 300000;
                TryDetect(now, false);
            }
        }

        public override void MapComponentDraw()
        {
            WildlifeMysteryRecord active = Active;
            if (!Prefs.DevMode || !WildlifeDevMaster.CompleteOverlayEnabled ||
                HerdsMod.Settings?.enableWildlifeMysteries != true || active == null ||
                Find.CurrentMap != map) return;
            Pawn focus = active.animal?.Spawned == true ? active.animal :
                map.mapPawns.AllPawnsSpawned.FirstOrDefault(pawn => pawn.def == active.species);
            if (focus != null)
                GenDraw.DrawRadiusRing(focus.Position, 2.2f, active.Solved ? Color.green : Color.magenta);
            foreach (WildlifeSign sign in RelevantSigns(active).Take(8))
                GenDraw.DrawLineBetween(sign.Position.ToVector3Shifted(),
                    (focus?.Position ?? map.Center).ToVector3Shifted(), SimpleColor.Magenta);
        }

        public override void MapComponentOnGUI()
        {
            base.MapComponentOnGUI();
            WildlifeMysteryRecord active = Active;
            if (!Prefs.DevMode || !WildlifeDevMaster.CompleteOverlayEnabled ||
                HerdsMod.Settings?.enableWildlifeMysteries != true || active == null ||
                Find.CurrentMap != map) return;
            Pawn focus = active.animal?.Spawned == true ? active.animal :
                map.mapPawns.AllPawnsSpawned.FirstOrDefault(pawn => pawn.def == active.species);
            if (focus != null)
                GenMapUI.DrawThingLabel(focus, "mystery " + active.progress.ToStringPercent());
        }

        private void TryDetect(int now, bool force)
        {
            if (!force && mysteries.Any(value => now - value.startedTick < 600000)) return;
            List<Func<WildlifeMysteryRecord>> detectors = new List<Func<WildlifeMysteryRecord>>
            {
                DetectDistortedTradition, DetectOverdueTag, DetectPredatorLearning,
                DetectPopulationPattern, DetectLandmarkPattern, DetectAncestralReturn
            };
            int start = Mathf.Abs(Gen.HashCombineInt(map.uniqueID, now / 60000)) % detectors.Count;
            WildlifeMysteryRecord mystery = null;
            for (int i = 0; i < detectors.Count && mystery == null; i++)
                mystery = detectors[(start + i) % detectors.Count]();
            if (mystery == null && force) mystery = FallbackMystery();
            if (mystery == null) return;
            mystery.id = nextId++;
            mystery.startedTick = now;
            mystery.baselineSignStudies = SignStudyCount(mystery.species);
            mysteries.Insert(0, mystery);
            if (mysteries.Count > 12) mysteries.RemoveRange(12, mysteries.Count - 12);
            CreateInitialSign(mystery);
            Find.LetterStack.ReceiveLetter("A Wildlife Mystery", mystery.anomaly +
                "\n\nEvidence has been added to the Wildlife Journal Field Log.",
                LetterDefOf.NeutralEvent, mystery.animal?.Spawned == true ? (LookTargets)mystery.animal : null);
            WildlifeExperience.Record("Wildlife Mystery", mystery.anomaly, mystery.animal);
            if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("MysteryStarted",
                "id=" + mystery.id + " cause=" + mystery.cause + " species=" + mystery.species.defName);
        }

        private WildlifeMysteryRecord DetectDistortedTradition()
        {
            AnimalTraditionRecord tradition = map.GetComponent<AnimalTraditionMapComponent>()?.Traditions
                .Where(value => value.accuracy < 0.48f && value.strength >= 0.3f &&
                    value.holders.Count >= 2).OrderBy(value => value.accuracy).FirstOrDefault();
            if (tradition == null) return null;
            return Make(WildlifeMysteryCause.DistortedTradition, tradition.species,
                tradition.holders.FirstOrDefault(pawn => pawn?.Spawned == true),
                "The Shared False Warning",
                tradition.species.LabelCap + " are reacting to the colony in a coordinated way that does not match recent events.",
                "A distorted animal tradition spread through the group: \"" + tradition.belief + "\"");
        }

        private WildlifeMysteryRecord DetectOverdueTag()
        {
            int now = Find.TickManager.TicksGame;
            RoamingAnimalRecord roamer = map.GetComponent<RegionalWildlifeMapComponent>()?.RoamingAnimals
                .Where(value => value.tagged && value.state != RoamingAnimalState.Present &&
                    value.state != RoamingAnimalState.Dead && now > value.expectedReturnTick + 120000)
                .OrderBy(value => value.expectedReturnTick).FirstOrDefault();
            if (roamer == null) return null;
            return Make(WildlifeMysteryCause.OverdueTaggedAnimal, roamer.species, roamer.animal,
                "The Vanishing Signal",
                "A tagged " + roamer.species.label + " repeatedly disappears beyond the same part of its expected range.",
                roamer.animal.LabelShortCap + " remained away because its wider route, inherited range, and current regional conditions pulled it beyond the expected return window.");
        }

        private WildlifeMysteryRecord DetectPredatorLearning()
        {
            AnimalTraditionRecord tradition = map.GetComponent<AnimalTraditionMapComponent>()?.Traditions
                .Where(value => value.kind == AnimalTraditionKind.EasyRanch && value.strength >= 0.3f)
                .OrderByDescending(value => value.strength).FirstOrDefault();
            if (tradition == null) return null;
            return Make(WildlifeMysteryCause.PredatorLearning, tradition.species,
                tradition.holders.FirstOrDefault(pawn => pawn?.Spawned == true),
                "The Patient Predator",
                tradition.species.LabelCap + " appear to test colony defenses instead of following ordinary hunting routes.",
                "Predators learned that colony animals were accessible prey and passed the successful approach through their group.");
        }

        private WildlifeMysteryRecord DetectPopulationPattern()
        {
            RegionalSpeciesRecord record = map.GetComponent<RegionalWildlifeMapComponent>()?.Records
                .Where(value => HuntingKnowledgeMapComponent.ColonyExperience(value.species) > 0f)
                .OrderByDescending(value => Mathf.Abs(value.nearbyPopulation - value.previousNearbyPopulation))
                .FirstOrDefault();
            if (record == null) return null;
            if (record.lastLocalCount == 0 && record.nearbyPopulation > record.previousNearbyPopulation + 1f)
                return Make(WildlifeMysteryCause.HiddenBreedingGround, record.species, null,
                    "The Animals That Are Not Here",
                    "Signs of " + record.species.label + " are increasing, yet almost none are appearing on the colony map.",
                    "The nearby population is growing around a concealed breeding or feeding range outside the colony's visible area.");
            if (record.population < record.previousPopulation * 0.82f)
                return Make(WildlifeMysteryCause.PopulationCollapse, record.species, null,
                    "The Quiet Range",
                    "Familiar signs of " + record.species.label + " have abruptly faded across the region.",
                    "The wider population declined after habitat pressure, hunting, disease, season, or displacement exceeded its recovery.");
            return null;
        }

        private WildlifeMysteryRecord DetectLandmarkPattern()
        {
            WildlifeLandmarkReputation reputation = map.GetComponent<WildlifeLandmarkMapComponent>()?.Reputations
                .Where(value => WildlifeLandmarkMapComponent.Strength(value) >= 0.42f &&
                    HuntingKnowledgeMapComponent.ColonyExperience(value.species) > 0f)
                .OrderByDescending(WildlifeLandmarkMapComponent.Strength).FirstOrDefault();
            if (reputation == null) return null;
            WildlifeLandmarkIdentity identity = WildlifeLandmarkMapComponent.Identity(reputation);
            return Make(WildlifeMysteryCause.ColonyReputation, reputation.species, null,
                "The Bent Migration",
                reputation.species.LabelCap + " repeatedly alter their routes near the colony for no immediately visible reason.",
                "Generations now recognize the colony as " +
                WildlifeLandmarkMapComponent.IdentityLabel(identity).ToLowerInvariant() +
                ", changing migration and return decisions.");
        }

        private WildlifeMysteryRecord DetectAncestralReturn()
        {
            WildlifeFamilyLine line = map.GetComponent<WildlifeRegionalStoriesMapComponent>()?.FamilyLines
                .Where(value => value.generation >= 2 && value.animal?.Spawned == true)
                .OrderByDescending(value => value.generation).FirstOrDefault();
            if (line == null) return null;
            return Make(WildlifeMysteryCause.AncestralReturn, line.species, line.animal,
                "The Returning Bloodline",
                "Related " + line.species.label + " repeatedly revisit ground associated with animals long gone.",
                line.lineName + " retained an inherited range and social tradition across " +
                line.generation + " generations.");
        }

        private WildlifeMysteryRecord FallbackMystery()
        {
            ThingDef species = map.mapPawns.AllPawnsSpawned.Where(pawn =>
                pawn.Faction == null && pawn.RaceProps?.Animal == true).Select(pawn => pawn.def).FirstOrDefault();
            if (species == null) return null;
            return Make(WildlifeMysteryCause.HiddenBreedingGround, species, null,
                "Unfamiliar Signs",
                "The distribution of " + species.label + " signs does not match where the animals have recently been seen.",
                "The species is using a wider feeding and resting range beyond the immediately visible map.");
        }

        private static WildlifeMysteryRecord Make(WildlifeMysteryCause cause, ThingDef species,
            Pawn animal, string title, string anomaly, string explanation) =>
            new WildlifeMysteryRecord { cause = cause, species = species, animal = animal,
                title = title, anomaly = anomaly, explanation = explanation };

        private void UpdateEvidence(WildlifeMysteryRecord mystery, int now)
        {
            int studies = SignStudyCount(mystery.species);
            if (studies > mystery.baselineSignStudies)
            {
                AddEvidence(mystery, "Studied signs establish where and when the pattern changed.",
                    "Field signs", Mathf.Min(0.24f, (studies - mystery.baselineSignStudies) * 0.08f));
                mystery.baselineSignStudies = studies;
            }
            List<Building_WildlifeTool> tools = map.listerBuildings.allBuildingsColonist
                .OfType<Building_WildlifeTool>().Where(tool => tool.Operational).ToList();
            Building_WildlifeTool post = tools.FirstOrDefault(tool =>
                tool.Kind == WildlifeToolKind.ObservationPost && tool.ManningColonist() != null);
            if (post != null && !HasSourceToday(mystery, "Manned observation", now))
                AddEvidence(mystery, "Sustained observation confirms that the behavior is coordinated rather than random.",
                    "Manned observation", 0.10f);
            if (tools.Any(tool => tool.Kind == WildlifeToolKind.CameraTrap) &&
                !HasSourceToday(mystery, "Camera traps", now))
                AddEvidence(mystery, "Repeated images reveal timing that was invisible during direct observation.",
                    "Camera traps", 0.055f);
            if ((mystery.cause == WildlifeMysteryCause.OverdueTaggedAnimal ||
                mystery.animal?.health?.hediffSet?.GetFirstHediffOfDef(HerdsDefOf.Herds_TrackingCollar) != null) &&
                tools.Any(tool => tool.Kind == WildlifeToolKind.TelemetryStation) &&
                !HasSourceToday(mystery, "Telemetry", now))
                AddEvidence(mystery, "Telemetry narrows the missing movement to a consistent part of the range.",
                    "Telemetry", 0.10f);
        }

        private static bool HasSourceToday(WildlifeMysteryRecord mystery, string source, int now) =>
            mystery.evidence.Any(value => value.source == source && now - value.discoveredTick < 60000);

        private void AddEvidence(WildlifeMysteryRecord mystery, string clue, string source, float amount)
        {
            if (mystery.Solved || amount <= 0f) return;
            mystery.evidence.Insert(0, new WildlifeMysteryEvidence { clue = clue, source = source,
                discoveredTick = Find.TickManager.TicksGame, value = amount });
            if (mystery.evidence.Count > 16) mystery.evidence.RemoveRange(16, mystery.evidence.Count - 16);
            mystery.progress = Mathf.Clamp01(mystery.progress + amount);
            if (mystery.progress >= 1f)
            {
                mystery.solvedTick = Find.TickManager.TicksGame;
                Find.LetterStack.ReceiveLetter("Wildlife Mystery Solved", mystery.title + "\n\n" +
                    mystery.explanation + "\n\nChoose a response in the Wildlife Journal Field Log.",
                    LetterDefOf.PositiveEvent, mystery.animal?.Spawned == true ? (LookTargets)mystery.animal : null);
                WildlifeExperience.Record("Wildlife Discovery", mystery.explanation, mystery.animal);
            }
            if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("MysteryEvidence",
                "id=" + mystery.id + " source=" + source + " progress=" + mystery.progress.ToString("0.00"));
        }

        public void ReviewEvidence(WildlifeMysteryRecord mystery)
        {
            if (mystery == null || mystery.Resolved || Find.TickManager.TicksGame - mystery.lastReviewTick < 60000)
            {
                Messages.Message("The current evidence has already been reviewed today.",
                    MessageTypeDefOf.RejectInput, false);
                return;
            }
            int knowledge = HuntingKnowledgeMapComponent.ColonyLevel(mystery.species);
            if (knowledge < 1)
            {
                Messages.Message("Recognize this animal through observation or field signs before comparing evidence.",
                    MessageTypeDefOf.RejectInput, false);
                return;
            }
            mystery.lastReviewTick = Find.TickManager.TicksGame;
            string clue = mystery.cause == WildlifeMysteryCause.DistortedTradition ?
                "Comparison with colony folklore reveals that the animals' response preserves a different version of events." :
                mystery.cause == WildlifeMysteryCause.ColonyReputation ?
                "Migration dates align with the colony's changing wildlife reputation." :
                "Population, movement, and encounter records reveal a repeatable ecological pattern.";
            AddEvidence(mystery, clue, "Field journal analysis", 0.07f + knowledge * 0.035f);
        }

        public void FocusEvidence(WildlifeMysteryRecord mystery)
        {
            WildlifeSign sign = RelevantSigns(mystery).FirstOrDefault();
            Thing target = sign ?? (Thing)(mystery.animal?.Spawned == true ? mystery.animal : null) ??
                map.mapPawns.AllPawnsSpawned.FirstOrDefault(pawn => pawn.def == mystery.species);
            if (target == null)
            {
                Messages.Message("No physical evidence is currently visible. Use observation, cameras, telemetry, or an expedition.",
                    MessageTypeDefOf.NeutralEvent, false);
                return;
            }
            WildlifeUI.Focus(target);
        }

        public void NotifyExpedition(ThingDef species, ExpeditionObjective objective, bool success)
        {
            WildlifeMysteryRecord mystery = Active;
            if (mystery == null || mystery.Solved || species != mystery.species) return;
            AddEvidence(mystery, success
                ? "The expedition verified the pattern beyond the colony map."
                : "Even an unsuccessful expedition constrained where the cause could be operating.",
                "Wildlife expedition", success ? 0.28f : 0.12f);
        }

        public void Resolve(WildlifeMysteryRecord mystery, WildlifeMysteryResolution resolution)
        {
            if (mystery?.Solved != true || mystery.Resolved ||
                resolution == WildlifeMysteryResolution.Unresolved) return;
            mystery.resolution = resolution;
            RegionalWildlifeMapComponent regional = map.GetComponent<RegionalWildlifeMapComponent>();
            if (resolution == WildlifeMysteryResolution.ProtectDiscovery)
                regional?.ApplyExpeditionImpact(mystery.species, 1.2f, 0.12f);
            else if (resolution == WildlifeMysteryResolution.CorrectTradition)
                map.GetComponent<AnimalTraditionMapComponent>()?.CorrectSpeciesTraditions(mystery.species);
            else if (resolution == WildlifeMysteryResolution.ExploitForHunting)
            {
                regional?.ApplyExpeditionImpact(mystery.species, -0.8f, 0.15f);
                Pawn hunter = map.mapPawns.FreeColonistsSpawned.OrderByDescending(pawn =>
                    pawn.skills?.GetSkill(SkillDefOf.Animals)?.Level ?? 0).FirstOrDefault();
                map.GetComponent<HuntingKnowledgeMapComponent>()?.Learn(hunter, mystery.species, 45f, true);
            }
            else if (resolution == WildlifeMysteryResolution.EstablishSanctuary)
            {
                regional?.ApplyExpeditionImpact(mystery.species, 1.8f, 0.15f);
                WildlifeLandmarkReputation reputation = map.GetComponent<WildlifeLandmarkMapComponent>()?
                    .For(mystery.species, true);
                if (reputation != null) reputation.sanctuary = Mathf.Clamp01(reputation.sanctuary + 0.22f);
            }
            string result = ResolutionLabel(resolution) + ": " + mystery.title + ".";
            WildlifeExperience.Record("Mystery Resolution", result, mystery.animal,
                resolution == WildlifeMysteryResolution.ExploitForHunting);
            WildlifeMemoryUtility.Folklore(map, mystery.title, mystery.explanation + " " + result,
                mystery.animal, resolution != WildlifeMysteryResolution.ExploitForHunting);
            Messages.Message(result, MessageTypeDefOf.PositiveEvent, false);
            if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("MysteryResolved",
                "id=" + mystery.id + " resolution=" + resolution);
        }

        private IEnumerable<WildlifeSign> RelevantSigns(WildlifeMysteryRecord mystery) =>
            map.listerThings.ThingsOfDef(HerdsDefOf.Herds_WildlifeSign).OfType<WildlifeSign>()
                .Where(sign => sign.species == mystery.species).OrderByDescending(sign => sign.createdTick);

        private int SignStudyCount(ThingDef species) =>
            map.listerThings.ThingsOfDef(HerdsDefOf.Herds_WildlifeSign).OfType<WildlifeSign>()
                .Where(sign => sign.species == species).Sum(sign => sign.studiedBy?.Count ?? 0);

        private void CreateInitialSign(WildlifeMysteryRecord mystery)
        {
            if (HerdsDefOf.Herds_WildlifeSign == null || RelevantSigns(mystery).Any()) return;
            IntVec3 cell = mystery.animal?.Spawned == true ? mystery.animal.Position :
                CellFinder.TryFindRandomEdgeCellWith(value => value.Standable(map), map,
                    CellFinder.EdgeRoadChance_Animal, out IntVec3 edge) ? edge : map.Center;
            WildlifeSign sign = (WildlifeSign)ThingMaker.MakeThing(HerdsDefOf.Herds_WildlifeSign);
            sign.species = mystery.species;
            sign.sourceAnimal = mystery.animal;
            sign.createdTick = Find.TickManager.TicksGame;
            sign.predator = WildlifeSpeciesClassification.IsPredator(mystery.species);
            sign.signKind = sign.predator ? WildlifeSignKind.TerritoryMark : WildlifeSignKind.Tracks;
            sign.travelFrom = cell;
            sign.travelTo = map.Center;
            GenSpawn.Spawn(sign, cell, map);
        }

        public static string ResolutionLabel(WildlifeMysteryResolution resolution) =>
            resolution == WildlifeMysteryResolution.ProtectDiscovery ? "Protect the Discovery" :
            resolution == WildlifeMysteryResolution.CorrectTradition ? "Correct the False Tradition" :
            resolution == WildlifeMysteryResolution.ExploitForHunting ? "Exploit for Hunting" :
            resolution == WildlifeMysteryResolution.EstablishSanctuary ? "Establish a Sanctuary" :
            "Leave Undisturbed";

        [DebugAction("Wildlife", "Force living wildlife mystery",
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void DebugMystery()
        {
            Find.CurrentMap?.GetComponent<WildlifeMysteryMapComponent>()?
                .TryDetect(Find.TickManager.TicksGame, true);
        }

        [DebugAction("Wildlife", "Solve active wildlife mystery",
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void DebugSolve()
        {
            WildlifeMysteryMapComponent component = Find.CurrentMap?.GetComponent<WildlifeMysteryMapComponent>();
            WildlifeMysteryRecord mystery = component?.Active;
            if (mystery == null) return;
            component.AddEvidence(mystery, "DEV complete evidence set.", "DEV", 1f);
        }
    }

    public static class WildlifeMysteryUtility
    {
        public static void NotifyExpedition(Map map, ThingDef species, ExpeditionObjective objective, bool success) =>
            map?.GetComponent<WildlifeMysteryMapComponent>()?.NotifyExpedition(species, objective, success);
    }
}
