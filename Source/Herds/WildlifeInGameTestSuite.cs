using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using LudeonTK;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Herds
{
    public static class WildlifeInGameTestSuite
    {
        private const string FileName = "Wildlife-InGame-Test.txt";

        private sealed class Result
        {
            public string section;
            public string severity;
            public string text;
        }

        public static string ReportPath => Path.Combine(GenFilePaths.SaveDataFolderPath, FileName);

        [DebugAction("Wildlife", "Run full in-game test suite", actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void Run()
        {
            Run(false);
        }

        public static bool Run(bool quiet)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            List<Result> results = new List<Result>();
            Map map = Find.CurrentMap;
            void Check(string section, bool condition, string text) =>
                results.Add(new Result { section = section, severity = condition ? "PASS" : "FAIL", text = text });
            void Warn(string section, bool condition, string text)
            {
                if (!condition) results.Add(new Result { section = section, severity = "WARN", text = text });
                else results.Add(new Result { section = section, severity = "PASS", text = text });
            }
            void Section(string name, Action test)
            {
                try { test(); }
                catch (Exception exception)
                {
                    results.Add(new Result
                    {
                        section = name,
                        severity = "FAIL",
                        text = "Unhandled " + exception.GetType().Name + ": " + exception.GetBaseException().Message
                    });
                }
            }

            Section("Core", () =>
            {
                Check("Core", map != null, "Current map exists");
                Check("Core", Current.Game != null, "Current game exists");
                Check("Core", HerdsMod.Settings != null, "Wildlife settings loaded");
                Check("Core", AccessTools.TypeByName("Packs.PackMapComponent") != null, "Predator assembly loaded");
                Check("Core", AccessTools.TypeByName("Packs.ITab_Pack") != null, "Predator Wildlife tab loaded");
                Check("Core", typeof(WorldDrawLayer_WildlifeKnowledgeFog) != null, "World knowledge layer loaded");
                Check("Core", WildlifeDevBridge.ProtocolSelfTest(), "Activate Bridge protocol");
                Check("Core", typeof(WildlifeUnifiedOverlayMapComponent) != null &&
                    typeof(WildlifeDevMaster).GetMethod("DebugToggleUnifiedOverlay") != null,
                    "Unified dev overlay and global toggle are registered");
            });

            Section("Defs", () =>
            {
                Check("Defs", HerdsDefOf.Herds_WildlifeSign != null, "Wildlife sign def");
                Check("Defs", HerdsDefOf.Herds_StudyWildlifeSign != null, "Study Wildlife job");
                Check("Defs", HerdsDefOf.Herds_StudyLandscapeFeature?.driverClass ==
                    typeof(JobDriver_StudyLandscapeFeature), "Study landscape feature job");
                Check("Defs", HerdsDefOf.Herds_LandscapeCrossroad?.thingClass ==
                    typeof(WildlifeLandscapeCrossroad) &&
                    HerdsDefOf.Herds_ObserveLandscapeCrossroad?.driverClass ==
                    typeof(JobDriver_ObserveLandscapeCrossroad) &&
                    HerdsDefOf.Herds_StewardLandscapeCrossroad?.driverClass ==
                    typeof(JobDriver_StewardLandscapeCrossroad),
                    "Wildlife Crossroad marker and interaction jobs");
                Check("Defs", HerdsDefOf.Herds_LogSignalAlarm != null &&
                    HerdsDefOf.Herds_LogSignalHumanDanger != null &&
                    HerdsDefOf.Herds_LogSignalAllClear != null &&
                    HerdsDefOf.Herds_LogSignalContact != null &&
                    HerdsDefOf.Herds_LogSignalFood != null &&
                    HerdsDefOf.Herds_LogSignalWater != null &&
                    HerdsDefOf.Herds_LogSignalCoordination != null,
                    "Animal-call Log rule packs");
                Check("Defs", HerdsDefOf.Herds_GameTrail?.thingClass ==
                    typeof(WildlifeLandscapeFeature) &&
                    HerdsDefOf.Herds_GrazingGround != null &&
                    HerdsDefOf.Herds_ScentPost != null &&
                    HerdsDefOf.Herds_FeedingRemains != null,
                    "Landscape feature definitions");
                Check("Defs", HerdsDefOf.Herds_StudyNotableAnimal != null, "Study Notable Animal job");
                Check("Defs", HerdsDefOf.Herds_ObserveWildlifeMoment?.driverClass ==
                    typeof(JobDriver_ObserveWildlifeMoment), "Observe Wildlife Moment job");
                Check("Defs", HerdsDefOf.Herds_PerformStewardshipProject?.driverClass ==
                    typeof(JobDriver_PerformStewardshipProject),
                    "Stewardship projects use colonist fieldwork");
                Check("Defs", HerdsDefOf.Herds_WildlifeStory?.letterClass ==
                    typeof(ChoiceLetter_WildlifeStory),
                    "Colony Story notification uses Folklore routing letter");
                Check("Defs", HerdsDefOf.Herds_EmbarkHuntingExpedition != null, "Wildlife embark job");
                List<ExpeditionEventDef> expeditionEvents =
                    DefDatabase<ExpeditionEventDef>.AllDefsListForReading;
                Check("Defs", expeditionEvents.Count >= 3 && expeditionEvents.All(eventDef =>
                        eventDef.chance > 0f && !eventDef.choices.NullOrEmpty() &&
                        eventDef.choices.Any(choice => choice.turnBack) &&
                        eventDef.choices.Any(choice => choice.label == "Press On") &&
                        eventDef.choices.Any(choice => !choice.turnBack && choice.label != "Press On")),
                    "Expandable expedition events provide Turn Back, Press On, and event-specific choices");
                Check("Defs", HerdsDefOf.Herds_HuntingExpeditionMarker != null, "Wildlife expedition marker");
                Check("Defs", HerdsDefOf.Herds_HuntingSpot != null, "Hunting Spot");
                Check("Defs", HerdsDefOf.Herds_ObservationPost != null, "Observation Post");
                Check("Defs", HerdsDefOf.Herds_AnimalBurrow != null, "Animal burrow");
                Check("Defs", HerdsDefOf.Herds_CameraTrap != null, "Camera trap");
                Check("Defs", HerdsDefOf.Herds_TelemetryStation != null, "Telemetry station");
                Check("Defs", HerdsDefOf.Herds_FlightBurst != null, "Bird flight burst");
                Check("Defs", HerdsDefOf.Herds_WildlifeTrophy != null, "Wildlife trophy reward");
                Check("Defs", HerdsDefOf.Herds_FolkloreCairn != null &&
                    HerdsDefOf.Herds_FolkloreCairn.thingClass == typeof(Building_FolkloreCairn),
                    "Wildlife folklore cairn");
                Check("Defs", HerdsDefOf.Herds_RetellWildlifeStory?.driverClass == typeof(JobDriver_RetellWildlifeStory) &&
                    HerdsDefOf.Herds_WildlifeCeremonyGather?.driverClass == typeof(JobDriver_WildlifeCeremonyGather),
                    "Physical storytelling and ceremony jobs");
                Check("Defs", HerdsDefOf.Herds_WildlifeInsight != null &&
                    HerdsDefOf.Herds_WildlifeAttuned != null, "Wildlife inspiration and trait");
                Check("Defs", HerdsDefOf.Herds_ProtectedAnimalDied != null,
                    "Protected-animal death thought");
                Check("Defs", DefDatabase<HediffDef>.GetNamedSilentFail("Herds_NotableSwift") != null &&
                    DefDatabase<HediffDef>.GetNamedSilentFail("Herds_NotableCunning") != null &&
                    DefDatabase<HediffDef>.GetNamedSilentFail("Herds_NotableScarred") != null,
                    "Notable animal abilities");
                ResearchProjectDef expedition = DefDatabase<ResearchProjectDef>.GetNamedSilentFail("Wildlife_HuntingExpedition");
                Check("Defs", expedition != null && expedition.label == "wildlife expedition", "Wildlife Expedition research label");
                Check("Defs", expedition?.prerequisites?.Any(def => def.defName == "Wildlife_Fieldcraft") == true,
                    "Wildlife Expedition requires Organized Hunting");
            });

            if (map != null)
            {
                Section("Components", () =>
                {
                    Check("Components", map.GetComponent<HerdMapComponent>() != null, "Prey simulation component");
                    Check("Components", map.GetComponent<WildlifeFieldcraftMapComponent>() != null, "Fieldcraft component");
                    Check("Components", map.GetComponent<WildlifeSignalCultureMapComponent>() != null,
                        "Wildlife signal culture component");
                    Check("Components", map.GetComponent<HuntingKnowledgeMapComponent>() != null, "Animal Knowledge component");
                    Check("Components", map.GetComponent<WildlifeHuntCoordinator>() != null, "Hunt coordinator");
                    Check("Components", map.GetComponent<RegionalWildlifeMapComponent>() != null, "Regional wildlife component");
                    Check("Components", map.GetComponent<WildlifeLandscapeMapComponent>() != null,
                        "Landscape component");
                    Check("Components", map.GetComponent<HuntingExpeditionMapComponent>() != null, "Wildlife expedition component");
                    Check("Components", map.GetComponent<NotableWildlifeMapComponent>() != null, "Notable wildlife component");
                    Check("Components", map.GetComponent<WildlifeFieldJournalMapComponent>() != null, "Wildlife Field Journal component");
                    Check("Components", map.GetComponent<WildlifeUnifiedOverlayMapComponent>() != null,
                        "Unified Wildlife overlay component");
                    Check("Components", map.components.Any(component => component.GetType().FullName == "Packs.PackMapComponent"),
                        "Predator simulation component");
                });

                Section("Deferred Reality", () =>
                {
                    bool adapterLoaded = AccessTools.TypeByName("DeferredReality.Wildlife.WildlifeRealityProvider") != null;
                    Check("Deferred Reality", !adapterLoaded || WildlifeDeferredRealityBridge.MaterializeBeyondMap != null,
                        "Adjacent trail bridge is installed when the Deferred Reality Wildlife adapter is loaded");
                    Check("Deferred Reality", !adapterLoaded ||
                        typeof(WildlifeTrailMapComponent).GetMethod("NotifyAnimalDeparture") != null,
                        "Trail records expose the adjacent-departure handoff");
                });

                Section("Landscape", () =>
                {
                    Check("Landscape", WildlifeNicheDatabase.ConservativeRulesSelfTest(),
                        "Ecological roles reject humans and contain no duplicates");
                    Check("Landscape", map.GetComponent<WildlifeLandscapeMapComponent>()
                        .Features.Count() <= 14,
                        "Persistent ecological feature cap");
                    Check("Landscape", map.mapPawns.AllPawnsSpawned
                        .Where(pawn => pawn.Faction == null &&
                            pawn.RaceProps?.Animal == true)
                        .All(pawn => WildlifeNicheDatabase.RolesFor(pawn.def)
                            .All(role => Enum.IsDefined(typeof(WildlifeEcologicalRole), role))),
                        "Local species resolve only valid ecological roles");
                    WildlifeLandscapeMapComponent landscape =
                        map.GetComponent<WildlifeLandscapeMapComponent>();
                    Check("Landscape", landscape.Features
                            .Where(feature => feature.kind == WildlifeLandscapeKind.FeedingRemains)
                            .All(feature => feature.strength > 0f &&
                                WildlifeLandscapeUtility.Effect(feature.kind).Contains("feeding site")) &&
                        typeof(WildlifeLandscapeFeature).GetMethod("TickRare") != null &&
                        typeof(WildlifeLandscapeMapComponent).GetMethod("MigrationAttraction") != null &&
                        typeof(WildlifeLandscapeMapComponent).GetMethod("PreferredFeatureTarget") != null,
                        "Feeding remains attract scavengers and expose gradual consumption lifecycle behavior");
                    Check("Landscape", landscape.Activities.All(activity => activity.id > 0) &&
                        landscape.Activities.Select(activity => activity.id).Distinct().Count() ==
                        landscape.Activities.Count,
                        "Wildlife Crossroad activity IDs are valid and unique");
                    Check("Landscape", landscape.Crossroads.All(marker =>
                        landscape.ActivityById(marker.activityId) != null),
                        "Wildlife Crossroad markers reference live activities");
                    Check("Landscape",
                        WildlifeLandscapeMapComponent.ObstructionEffectiveness(0) == 1f &&
                        WildlifeLandscapeMapComponent.ObstructionEffectiveness(1) <= 0.6f &&
                        WildlifeLandscapeMapComponent.ObstructionEffectiveness(3) <= 0.15f,
                        "Colony construction sharply reduces Landscape effectiveness");
                    Check("Landscape",
                        WildlifeLandscapeMapComponent.GrazingGrowthBonus(0f) == 0f &&
                        WildlifeLandscapeMapComponent.GrazingGrowthBonus(0.5f) > 0f &&
                        WildlifeLandscapeMapComponent.GrazingGrowthBonus(1f) >
                            WildlifeLandscapeMapComponent.GrazingGrowthBonus(0.5f) &&
                        GrazingGroundGrowthPatch.ApplyGrowthBonus(1f, true, 0.5f) > 1f &&
                        GrazingGroundGrowthPatch.ApplyGrowthBonus(1f, true, 0f) == 1f &&
                        GrazingGroundGrowthPatch.ApplyGrowthBonus(1f, false, 0.5f) == 1f &&
                        GrazingGroundGrowthPatch.IsGrass(ThingDefOf.Plant_Grass) &&
                        GrazingGroundGrowthPatch.ShouldQueryGrowthBonus(true, 1f, ThingDefOf.Plant_Grass) &&
                        !GrazingGroundGrowthPatch.ShouldQueryGrowthBonus(true, 1f,
                            DefDatabase<ThingDef>.GetNamed("Plant_Rice")) &&
                        !GrazingGroundGrowthPatch.ShouldQueryGrowthBonus(false, 1f, ThingDefOf.Plant_Grass) &&
                        !GrazingGroundGrowthPatch.ShouldQueryGrowthBonus(true, 0f, ThingDefOf.Plant_Grass),
                        "Grazing Grounds scale grass growth with effectiveness and remove inactive bonuses");
                    Check("Landscape",
                        WildlifeFieldJournalMapComponent.ProjectLabel(
                            WildlifeStewardProjectKind.RanchDefense) ==
                            "Protect Wildlife Habitat" &&
                        WildlifeFieldJournalMapComponent.ProjectDescription(
                            WildlifeStewardProjectKind.RanchDefense).Contains("habitat") &&
                        WildlifeFieldJournalMapComponent.RestoreSpeciesEligible(
                            new RegionalSpeciesRecord
                            {
                                population = 74f,
                                previousPopulation = 100f
                            }) &&
                        !WildlifeFieldJournalMapComponent.RestoreSpeciesEligible(
                            new RegionalSpeciesRecord
                            {
                                population = 80f,
                                previousPopulation = 100f
                            }),
                        "Stewardship labels describe habitat protection and restoration requires significant decline");
                });

                Section("Prey", () =>
                {
                    HerdMapComponent herds = map.GetComponent<HerdMapComponent>();
                    List<Pawn> prey = map.mapPawns.AllPawnsSpawned
                        .Where(pawn => pawn?.Dead == false && PreyProfileDatabase.IsEligible(pawn.def)).ToList();
                    int missingGroups = 0;
                    int badProfiles = 0;
                    int badSolitary = 0;
                    int badBirdDefaults = 0;
                    for (int i = 0; i < prey.Count; i++)
                    {
                        PreyProfile profile = PreyProfileDatabase.For(prey[i].def);
                        HerdSnapshot group = herds?.HerdFor(prey[i]);
                        if (profile == null) badProfiles++;
                        if (group == null) missingGroups++;
                        if (profile?.socialType == PreySocialType.Solitary && group?.members.Count > 1) badSolitary++;
                        if (PreyProfileDatabase.IsBird(prey[i].def) &&
                            PreyProfileDatabase.DefaultFor(prey[i].def)?.socialType != PreySocialType.Flock) badBirdDefaults++;
                    }
                    Check("Prey", badProfiles == 0, "All prey have behavior profiles");
                    Warn("Prey", missingGroups == 0, missingGroups + " prey currently lack a simulation group");
                    Check("Prey", badSolitary == 0, "Solitary prey are not grouped");
                    Check("Prey", badBirdDefaults == 0, "Bird species default to flock behavior");
                });

                Section("Predators", () =>
                {
                    MapComponent packs = map.components.FirstOrDefault(component => component.GetType().FullName == "Packs.PackMapComponent");
                    MethodInfo overview = AccessTools.Method(packs?.GetType(), "DebugOverviewLines");
                    List<string> lines = overview?.Invoke(packs, null) as List<string>;
                    Check("Predators", packs != null, "Pack component present");
                    Check("Predators", lines != null && lines.Count > 0, "Predator state API responds");
                    List<Pawn> wildPredators = map.mapPawns.AllPawnsSpawned
                        .Where(pawn => pawn?.Dead == false &&
                            WildlifeSpeciesClassification.IsPredator(pawn.def) &&
                            pawn.Faction != Faction.OfPlayer).ToList();
                    Warn("Predators", wildPredators.Count > 0, "No wild predators available for live behavior checks");
                });

                Section("Fieldcraft", () =>
                {
                    List<WildlifeSign> signs = map.listerThings.ThingsOfDef(HerdsDefOf.Herds_WildlifeSign)
                        .OfType<WildlifeSign>().ToList();
                    Check("Fieldcraft", signs.All(sign => sign.species != null), "All wildlife signs identify a species internally");
                    Check("Fieldcraft", signs.All(sign => sign.sourceAnimal == null ||
                        sign.sourceAnimal.def == sign.species),
                        "Wildlife signs retain a valid originating animal when available");
                    Check("Fieldcraft", signs.All(sign => sign.studiedBy != null), "All wildlife signs have study records");
                    WildlifeTrailMapComponent trails = map.GetComponent<WildlifeTrailMapComponent>();
                    Check("Fieldcraft", trails != null, "Interactive trail-reading component");
                    Check("Fieldcraft", HerdsDefOf.Herds_FollowWildlifeTrail != null,
                        "Follow Wildlife Trail job");
                    Check("Fieldcraft",
                        typeof(WildlifeSign).GetMethod("ShowStudyMenu") != null,
                        "Selected wildlife signs expose an explicit colonist study menu");
                    Check("Fieldcraft", trails?.TrailLeads != null,
                        "Trail records are available for bridge and UI assessment");
                    List<WildlifeSign> availableLeadSigns = trails?.AvailableLeadSigns() ??
                        new List<WildlifeSign>();
                    int urgentLeadCount = WildlifeTrailMapComponent.CountUrgentLeads(
                        availableLeadSigns);
                    Check("Fieldcraft", trails?.UrgentLeadCount == urgentLeadCount &&
                        WildlifeTrailMapComponent.CountUrgentLeads(
                            availableLeadSigns.Concat(availableLeadSigns)) == urgentLeadCount &&
                        urgentLeadCount <= availableLeadSigns.Select(sign =>
                            sign.sourceAnimal).Distinct().Count(),
                        "Wildlife tracking counts grouped Trail Leads rather than individual clues");
                    Check("Fieldcraft", JobDriver_StudyNotableAnimal.MinimumStudyDistance >= 18f &&
                        JobDriver_StudyNotableAnimal.MaximumStudyDistance >
                            JobDriver_StudyNotableAnimal.MinimumStudyDistance &&
                        typeof(JobDriver_StudyNotableAnimal).GetMethod("TryFindStudyCell") != null,
                        "Notable animal study uses a safe line-of-sight observation range");
                    Check("Fieldcraft", NotableAnimalActionPolicy.Order.SequenceEqual(new[]
                        { "Study", "Hunt", "Protect", "Capture" }),
                        "Notable animal actions use the requested study, hunt, protect, capture order");
                    Check("Fieldcraft", HuntingExpeditionMapComponent.TrailHuntBonus(
                        new TrailHuntOpportunity { quality = 0f }) > 0f &&
                        HuntingExpeditionMapComponent.TrailHuntBonus(
                            new TrailHuntOpportunity { quality = 1f }) >
                        HuntingExpeditionMapComponent.TrailHuntBonus(
                            new TrailHuntOpportunity { quality = 0f }),
                        "Trail hunt opportunities provide progressive expedition advantages");
                    Check("Fieldcraft", trails.TrailLeads.All(lead => lead?.species != null &&
                        lead.targetAnimal != null && !lead.targetAnimal.Spawned &&
                        Enum.IsDefined(typeof(WildlifeTrailState), lead.state) &&
                        lead.targetAnimal.def == lead.species &&
                        lead.state == WildlifeTrailState.BeyondMap && lead.predictedCell.IsValid),
                        "Every trail represents one exact animal that has already left the map");
                    Check("Fieldcraft", trails.TrailLeads.Where(lead => lead?.targetAnimal != null)
                        .All(lead => trails.LeadFor(lead.targetAnimal) == lead),
                        "Trail lookup remains bound to the exact departed animal");
                    HuntingExpeditionMapComponent trailExpeditions =
                        map.GetComponent<HuntingExpeditionMapComponent>();
                    Check("Fieldcraft", trailExpeditions.TrailHuntOpportunities.All(opportunity =>
                            opportunity?.species != null && opportunity.targetAnimal != null &&
                            opportunity.targetAnimal.def == opportunity.species) &&
                        trailExpeditions.ActiveExpeditions.All(record =>
                            record.trailTargetAnimal == null ||
                            record.trailTargetAnimal.def == record.targetSpecies),
                        "Temporary trail opportunities and expeditions preserve exact quarry identity");
                    Check("Fieldcraft",
                        typeof(WildlifeTrailMapComponent).GetMethod("Retains") != null &&
                        typeof(WildlifeTrailMapComponent).GetMethod("NotifyAnimalDeparture") != null &&
                        typeof(WildlifeTrailMapComponent).GetMethod("SafeFollowDestination") != null,
                        "Active trails can retain quarry and survive map departure");
                    Check("Fieldcraft",
                        typeof(WildlifeFieldcraftMapComponent).GetMethod(
                            "CanSafelyTrack") != null &&
                        typeof(WildlifeFieldcraftMapComponent).GetMethod(
                            "CreateSafeTrackingSign") != null,
                        "Wildlife Moment tracking uses safely separated physical evidence");
                    Check("Fieldcraft", typeof(Window_WildlifeTrailBoard) != null,
                        "Player-facing Trail Leads board");
                    Check("Fieldcraft", WildlifeTrailMapComponent.NaturalPaletteSelfTest(),
                        "Trail overlays use distinct, restrained natural colors");
                    Check("Fieldcraft", map.GetComponent<WildlifeHuntCoordinator>().DebugOverviewLines() != null,
                        "Coordinated hunt state API responds");
                });

                Section("Signals", () =>
                {
                    WildlifeSignalCultureMapComponent signals =
                        map.GetComponent<WildlifeSignalCultureMapComponent>();
                    List<ThingDef> species = map.mapPawns.AllPawnsSpawned
                        .Where(pawn => pawn?.RaceProps?.Animal == true)
                        .Select(pawn => pawn.def).Distinct().ToList();
                    Check("Signals", signals != null, "Local signal culture state API responds");
                    Check("Signals", signals != null && species.All(def =>
                    {
                        WildlifeDialectRecord dialect = signals.DialectFor(def);
                        return dialect != null && dialect.credibility >= 0f &&
                            dialect.credibility <= 1f && dialect.humanTrust >= 0f &&
                            dialect.humanTrust <= 1f && !signals.DialectName(def).NullOrEmpty();
                    }), "Animal dialect identities and trust values are valid");
                    Check("Signals", signals != null && map.mapPawns.FreeColonists.All(pawn =>
                        species.All(def =>
                        {
                            float value = signals.Understanding(pawn, def);
                            return value >= 0f && value <= 1f;
                        })), "Per-colonist signal understanding is bounded");
                    Check("Signals", signals != null && species.All(def =>
                    {
                        Pawn contributor = signals.ColonyContributor(def);
                        float displayed = signals.ColonyUnderstanding(def);
                        float expected = map.mapPawns.FreeColonistsSpawned
                            .Select(pawn => signals.Understanding(pawn, def))
                            .DefaultIfEmpty(0f).Max();
                        return Math.Abs(displayed - expected) < 0.0001f &&
                            (contributor == null
                                ? Math.Abs(displayed) < 0.0001f
                                : Math.Abs(signals.Understanding(contributor, def) - displayed) < 0.0001f);
                    }), "Colony signal knowledge names the currently contributing colonist");
                    Check("Signals", signals != null && signals.ActiveSignals.All(signal =>
                        signal.species?.race?.Animal == true && signal.radius >= 0f &&
                        signal.expiresTick >= signal.startedTick),
                        "Active signal visuals have valid state");
                    Check("Signals", signals != null && signals.RecentSignals.All(trace =>
                        trace.species?.race?.Animal == true && trace.traceId > 0 &&
                        trace.radius >= 0f && !trace.cause.NullOrEmpty() &&
                        !trace.expectedBehavior.NullOrEmpty() &&
                        (!trace.verified || !trace.observedBehavior.NullOrEmpty())),
                        "Signal history records cause, intent, and verified behavior");
                    Check("Signals", WildlifeSignalCultureMapComponent.VisualGrammarSelfTest(),
                        "Every signal kind has a distinct visual identity and player label");
                    Check("Signals", WildlifeSignalCultureMapComponent.ResponseSafetySelfTest(),
                        "Solitary or ungrouped signalers are safe during response verification");
                    Check("Signals", WildlifeSignalCultureMapComponent.IdentifiedSignalTextSelfTest(),
                        "Signal meaning labels appear only after exact identification");
                    Check("Signals", WildlifeKnowledgeAdapter.WarningKnowledgeSelfTest(),
                        "Warning calls progress from first evidence through family, meaning, support, and contradiction states");
                    Check("Signals", WildlifeKnowledgeAdapter.LegacyWarningState(0f, 1).hasEvidence &&
                        !WildlifeKnowledgeAdapter.LegacyWarningState(0f, 1).familyRecognized &&
                        WildlifeKnowledgeAdapter.LegacyWarningState(0.3f, 1).familyRecognized &&
                        !WildlifeKnowledgeAdapter.LegacyWarningState(0.3f, 1).meaningInterpreted,
                        "Legacy warning knowledge remains qualitative without inventing a V3 meaning claim");
                    Check("Signals", WildlifeKnowledgeAdapter.PredatorPressureKnowledgeSelfTest(),
                        "Predator pressure progresses from a herd consequence through pattern, meaning, support, and contradiction states");
                    Check("Signals", typeof(WildlifeKnowledgeAdapter).GetMethod("ObserveWarningCall") != null &&
                        typeof(WildlifeKnowledgeAdapter).GetMethod("WarningObservationAlreadyApplied") != null &&
                        typeof(WildlifeKnowledgeAdapter).GetMethod("WarningSourceInstanceId") != null,
                        "Warning knowledge uses a stable V3 observation identity and explicit duplicate guard");
                    Check("Signals", typeof(WildlifeKnowledgeAdapter).GetMethod("ObservePredatorPressure") != null &&
                        typeof(WildlifeKnowledgeAdapter).GetMethod("PredatorPressureObservationAlreadyApplied") != null &&
                        typeof(WildlifeKnowledgeAdapter).GetMethod("PredatorPressureSourceInstanceId") != null &&
                        typeof(WildlifeKnowledgeAdapter).GetMethod("IsPredatorPressureTrace") != null,
                        "Predator pressure uses a separate stable V3 observation identity and duplicate guard");
                    Check("Signals", typeof(WildlifeSignalObservationPresentation).GetField("warningKnowledgeSubmitted") != null &&
                        typeof(WildlifeSignalCultureMapComponent).GetProperty("WarningKnowledgeSources") != null,
                        "Warning processing markers are retained on existing signal presentation owners");
                    Check("Signals", typeof(WildlifeSignalObservationPresentation).GetField("predatorPressureSubmitted") != null &&
                        typeof(WildlifeSignalObservationPresentation).GetField("predatorPressureSourceInstanceId") != null &&
                        typeof(WildlifeSignalCultureMapComponent).GetMethod("ColonyPredatorPressure") != null,
                        "Predator pressure markers and qualitative colony projections remain on existing signal owners");
                    Check("Signals", typeof(WildlifeSignalTrace).GetField("developerScenario") != null &&
                        !WildlifeKnowledgeAdapter.IsPredatorPressureTrace(new WildlifeSignalTrace
                        {
                            kind = WildlifeSignalKind.Alarm,
                            hasSubject = true,
                            developerScenario = true
                        }),
                        "Developer scenarios cannot become ecological pressure evidence");
                    Check("Signals", signals != null && signals.WarningKnowledgeSources.Distinct().Count() ==
                        signals.WarningKnowledgeSources.Count,
                        "Warning source identity ledger remains duplicate-free after load normalization");
                    Check("Signals", signals != null && signals.RecentSignals.Where(trace =>
                        WildlifeSignalCultureMapComponent.IsWarningCall(trace.kind)).All(trace =>
                        trace.playerFacingDescription.NullOrEmpty() ||
                        !trace.playerFacingDescription.Contains("human-danger")),
                        "Warning projections do not expose hidden call identity in normal descriptions");
                    Check("Signals", signals != null && signals.RecentSignals.Where(trace =>
                        WildlifeKnowledgeAdapter.IsPredatorPressureTrace(trace)).All(trace =>
                        trace.playerFacingDescription.NullOrEmpty() ||
                        !trace.playerFacingDescription.Contains("predator")),
                        "Predator pressure remains ambiguous before its claim is supported");
                    Check("Signals", WildlifeSignalPresentation.SelfTest(),
                        "Signal descriptions use threshold-safe grammar and animal references");
                    Check("Signals", signals != null && signals.RecentSignals.All(trace => trace.playerFacingTier >= 0 &&
                        trace.playerFacingTier <= (int)WildlifeSignalDisplayTier.Truthfulness &&
                        !trace.playerFacingDescription.NullOrEmpty()),
                        "Signal history preserves a bounded historical player-facing description");
                    Check("Signals", typeof(WildlifeSignalJournalPanel) != null &&
                        typeof(WildlifeSignalCultureMapComponent).GetMethod("Replay") != null &&
                        typeof(WildlifeSignalCultureMapComponent).GetMethod("TraceLines") != null,
                        "Journal Signals supports replay and compact bridge traces");
                    Check("Signals", typeof(WildlifeSignalAudio).GetMethod("Replay") != null,
                        "Recorded signal replay is audio-only");
                    Check("Signals", WildlifeSignalAudio.SelfTest(),
                        "Signal vocalization pitch is deterministic, subtle, and bounded");
                    Check("Signals", signals == null || signals.RecentSignals.All(trace =>
                        trace.soundPitch >= 0.96f && trace.soundPitch <= 1.04f &&
                        (trace.soundDef == null || !trace.soundStatus.NullOrEmpty())),
                        "Signal audio identity and bounded pitch are persisted safely");
                    Check("Signals", typeof(HerdsSettings).GetField("enableWildlifeSignalCulture") != null &&
                        typeof(HerdsSettings).GetField("showIdentifiedSignalText") != null &&
                        typeof(HerdsSettings).GetField("enablePlayerSignalImitation") != null &&
                        typeof(HerdsSettings).GetField("enablePredatorSignalLearning") != null,
                        "Signal culture features have individual configuration switches");
                });

                Section("Expeditions", () =>
                {
                    HuntingExpeditionMapComponent expeditions = map.GetComponent<HuntingExpeditionMapComponent>();
                    List<string> validations = expeditions.DebugValidationLines();
                    Check("Expeditions", validations.All(line => !line.StartsWith("FAIL")), "Built-in expedition validation");
                    ExpeditionDestination near = expeditions.Destinations().FirstOrDefault();
                    Check("Expeditions", near != null, "At least one valid nearby destination");
                    ExpeditionDestination far = null;
                    PlanetLayer layer = map.Tile.Layer;
                    for (int i = 0; i < layer.TilesCount && far == null; i++)
                    {
                        PlanetTile tile = layer.PlanetTileForID(i);
                        if (Find.WorldGrid.ApproxDistanceInTiles(map.Tile, tile) > 20f && expeditions.CanExpeditionTo((int)tile))
                            far = expeditions.DestinationForTile((int)tile, false);
                    }
                    Check("Expeditions", far != null && far.distance > 20, "Distant valid world tiles are selectable");
                    PlanetTile settlementTile = Find.WorldObjects.Settlements.FirstOrDefault()?.Tile ?? PlanetTile.Invalid;
                    Check("Expeditions", !settlementTile.Valid || !expeditions.CanExpeditionTo((int)settlementTile),
                        "Settlements cannot be expedition destinations");
                    ResearchProjectDef expeditionResearch = DefDatabase<ResearchProjectDef>.GetNamedSilentFail("Wildlife_HuntingExpedition");
                    Check("Expeditions", !NatureWorldUI.Enabled ||
                        expeditionResearch?.IsFinished == true,
                        "Nature world tab requires Wildlife Expedition research");
                    Check("Expeditions", expeditions.ActiveExpeditions.SelectMany(record => record.Party)
                        .Where(pawn => pawn != null).GroupBy(pawn => pawn).All(group => group.Count() == 1),
                        "A pawn belongs to at most one active expedition");
                    Check("Expeditions", expeditions.ActiveExpeditions.SelectMany(record => record.Party)
                        .Where(pawn => pawn != null && !pawn.Dead)
                        .All(pawn => pawn.Spawned || Find.WorldPawns.Contains(pawn)),
                        "Every active expedition member is on-map or retained by WorldPawns");
                    Check("Expeditions", expeditions.ActiveExpeditions
                        .Where(record => record.stage != ExpeditionStage.Embarking)
                        .All(record => record.caravan?.Destroyed == false &&
                            record.Party.Where(pawn => pawn != null && !pawn.Dead && !pawn.Spawned)
                                .All(record.caravan.ContainsPawn)),
                        "Departed expedition members are held by a visible caravan");
                    Check("Expeditions", expeditions.ActiveExpeditions
                        .Where(record => record.caravan?.Destroyed == false)
                        .All(record => !record.caravan.GetInspectString().Contains("\n\n")),
                        "Expedition caravan inspect strings contain no empty lines");
                    Check("Expeditions", expeditions.ActiveExpeditions
                        .Where(record => record.caravan?.Destroyed == false &&
                            (record.stage == ExpeditionStage.OutboundTravel || record.stage == ExpeditionStage.Returning))
                        .All(record =>
                        {
                            PlanetTile target = record.stage == ExpeditionStage.Returning ? map.Tile : (PlanetTile)record.destinationTile;
                            return record.caravan.Tile == target ||
                                (record.caravan.pather.Moving && record.caravan.pather.Destination == target);
                        }), "Traveling expeditions use the caravan pather");
                    Check("Expeditions", expeditions.ActiveExpeditions.All(record =>
                        !record.interactiveEncounterPending || !record.interactiveEncounter.NullOrEmpty()),
                        "Pending interactive encounters have valid field reports");
                    Check("Expeditions", expeditions.ActiveExpeditions.All(record =>
                        record.foodNutrition >= 0f && record.dailyNutrition >= 0f &&
                        record.expectedReturnTick >= record.stageStartedTick),
                        "Active expedition timing and supply state survives save data");
                    Check("Expeditions", expeditions.TrailPaths.All(path => path != null &&
                            path.fromTile >= 0 && path.toTile >= 0 && path.fromTile != path.toTile &&
                            path.targetSpecies != null) &&
                        expeditions.TrailPaths.Select(path =>
                            Math.Min(path.fromTile, path.toTile) + ":" +
                            Math.Max(path.fromTile, path.toTile)).Distinct().Count() ==
                            expeditions.TrailPaths.Count,
                        "Permanent trail paths contain unique valid world-tile edges");
                    Check("Expeditions", expeditions.History.Count <= 20,
                        "Completed expedition history remains bounded");
                    if (near != null)
                    {
                        ExpeditionPlan without = new ExpeditionPlan { destination = near, objective = ExpeditionObjective.Scout, useBedrolls = false };
                        ExpeditionPlan with = new ExpeditionPlan { destination = near, objective = ExpeditionObjective.Scout, useBedrolls = true };
                        Check("Expeditions", expeditions.EstimateDays(with) < expeditions.EstimateDays(without),
                            "Bedrolls modestly reduce expedition time");
                        if (far != null)
                            Check("Expeditions", expeditions.EstimateDays(new ExpeditionPlan
                            {
                                destination = far,
                                objective = ExpeditionObjective.Scout,
                                useBedrolls = false
                            }) > expeditions.EstimateDays(without), "Distance increases expedition duration");
                    }
                });

                Section("Knowledge", () =>
                {
                    HuntingKnowledgeMapComponent knowledge = map.GetComponent<HuntingKnowledgeMapComponent>();
                    List<string> lines = knowledge.DebugOverviewLines();
                    Check("Knowledge", lines != null, "Animal Knowledge state API responds");
                    Check("Knowledge", !WildlifeKnowledgeStatPatch.IsPlayerPawn(null) &&
                        map.mapPawns.FreeColonists.All(WildlifeKnowledgeStatPatch.IsPlayerPawn) &&
                        map.mapPawns.AllPawnsSpawned.Where(pawn => pawn.Faction?.def?.isPlayer != true)
                            .All(pawn => !WildlifeKnowledgeStatPatch.IsPlayerPawn(pawn)),
                        "Knowledge stat evaluation identifies player pawns without the player-faction singleton");
                    Check("Knowledge",
                        WildlifeSpeciesClassification.Resolve(false, false, true) == false &&
                        WildlifeSpeciesClassification.Resolve(false, true, true) &&
                        !WildlifeSpeciesClassification.Resolve(true, true, false) &&
                        DefDatabase<ThingDef>.AllDefsListForReading.Where(def =>
                            def.race?.Animal == true).All(def =>
                            WildlifeSpeciesClassification.IsPredator(def) ||
                            !WildlifeSpeciesClassification.IsPredator(def)) &&
                        typeof(SpeciesBehaviorOverride).GetField("hasPredatorOverride") != null &&
                        typeof(SpeciesBehaviorOverride).GetField("hasPreyOverride") != null,
                        "Per-species Predator and Prey overrides preserve defaults and support loaded mod species");
                    Check("Knowledge", HuntingKnowledgeMapComponent.LevelForExperience(0f) == 0 &&
                        HuntingKnowledgeMapComponent.LevelForExperience(119.99f) == 0 &&
                        HuntingKnowledgeMapComponent.LevelForExperience(120f) == 1 &&
                        HuntingKnowledgeMapComponent.LevelForExperience(299.99f) == 1 &&
                        HuntingKnowledgeMapComponent.LevelForExperience(300f) == 2 &&
                        HuntingKnowledgeMapComponent.LevelForExperience(649.99f) == 2 &&
                        HuntingKnowledgeMapComponent.LevelForExperience(650f) == 3 &&
                        HuntingKnowledgeMapComponent.LevelForExperience(1200f) == 3,
                        "Biome Knowledge tiers use valid progression thresholds");
                    Check("Knowledge",
                        HuntingKnowledgeMapComponent.WildlifeProficiencyLabel(0) == "Novice" &&
                        HuntingKnowledgeMapComponent.WildlifeProficiencyLabel(1) == "Adept" &&
                        HuntingKnowledgeMapComponent.WildlifeProficiencyLabel(2) == "Expert" &&
                        HuntingKnowledgeMapComponent.WildlifeProficiencyLabel(3) == "Master",
                        "Wildlife proficiency tiers are ordered correctly");
                    Check("Knowledge", map.mapPawns.FreeColonists.All(pawn =>
                    {
                        float animalCoverage = knowledge.AnimalCoverage(pawn);
                        float biomeCoverage = knowledge.BiomeCoverage(pawn);
                        float combinedCoverage = knowledge.WildlifeProficiencyCoverage(pawn);
                        int proficiency = knowledge.WildlifeProficiencyLevel(pawn);
                        return animalCoverage >= 0f && animalCoverage <= 1f &&
                            biomeCoverage >= 0f && biomeCoverage <= 1f &&
                            Math.Abs(combinedCoverage - (animalCoverage + biomeCoverage) * 0.5f) < 0.001f &&
                            proficiency >= 0 && proficiency <= 3;
                    }), "Wildlife proficiency coverage and tiers are valid");
                    Check("Knowledge", map.mapPawns.FreeColonists.All(pawn =>
                        knowledge.BiomesForColonist(pawn).All(record =>
                            record.biome != null && record.experience >= 0f && record.completedExpeditions >= 0)),
                        "Biome Knowledge records are valid");
                    Check("Knowledge", ProgressionEducationKnowledgeCompatibility.Active ==
                        ModsConfig.IsActive("ferny.ProgressionEducation"),
                        "Optional Progression: Education integration state matches the active mod");
                    Check("Knowledge", DefDatabase<ThingDef>.AllDefsListForReading
                        .Where(def => def.race?.Animal == true)
                        .All(def => HuntingKnowledgeMapComponent.ColonyExperience(def) >= 0f),
                        "Species knowledge values are nonnegative");
                    Check("Knowledge", WildlifeTabKnowledgePolicy.RevealsIdentity(0) &&
                        !WildlifeTabKnowledgePolicy.RevealsBehavior(0) &&
                        WildlifeTabKnowledgePolicy.RevealsBehavior(1) &&
                        !WildlifeTabKnowledgePolicy.RevealsSignals(1) &&
                        WildlifeTabKnowledgePolicy.RevealsSignals(2) &&
                        !WildlifeTabKnowledgePolicy.RevealsIndividualMemory(2) &&
                        WildlifeTabKnowledgePolicy.RevealsIndividualMemory(3),
                        "Animal Wildlife tab reveals information progressively by colony knowledge");
                    Check("Knowledge", WildlifePassiveObservationPolicy.SelfTest(),
                        "Passive familiarity caps, diminishes repetition, and classifies meaningful discoveries");
                    List<PassiveObservationRecord> passiveRecords = knowledge.PassiveRecords.ToList();
                    Check("Knowledge", passiveRecords.Select(record => (record?.observer?.thingIDNumber ?? 0) + ":" +
                        (record.species?.defName ?? string.Empty)).Distinct().Count() == passiveRecords.Count,
                        "Passive exposure aggregates to one record per observer and species");
                    Check("Knowledge", passiveRecords.All(record => record != null && record.dailyExposure >= 0f &&
                        record.pendingExposure >= 0f && record.pendingExposure <= record.dailyExposure + 0.001f &&
                        record.dailyExposure <= (record.usedObservationPost ? WildlifePassiveObservationPolicy.ObservationPostDailyCap : WildlifePassiveObservationPolicy.DailyCap) + 0.001f),
                        "Passive exposure remains within its daily cap and save-safe pending balance");
                    List<WildlifeEvent> passiveEvents = WildlifeEventRouter.Shared.History
                        .Where(value => value?.metadata != null && value.metadata.TryGetValue("observationLayer", out string layer) &&
                            layer == "passive-meaningful").ToList();
                    Check("Knowledge", passiveEvents.GroupBy(value => value.sourceInstanceId ?? string.Empty)
                        .All(group => group.Key.NullOrEmpty() || group.Count() == 1),
                        "Stable passive day source IDs prevent duplicate rewards");
                    Check("Knowledge", passiveEvents.All(value => !value.summary.NullOrEmpty() &&
                        value.metadata.ContainsKey("previousAmount") && value.metadata.ContainsKey("newAmount") &&
                        value.metadata.ContainsKey("discoveryKind") && value.metadata.ContainsKey("observerId")),
                        "Meaningful passive events carry descriptive change metadata");
                    Check("Knowledge", typeof(PassiveObservationRecord).GetInterface(nameof(IExposable)) != null,
                        "Passive familiarity records are save-compatible");
                });

                Section("Regional", () =>
                {
                    RegionalWildlifeMapComponent regional = map.GetComponent<RegionalWildlifeMapComponent>();
                    Check("Regional", regional.DebugOverviewLines() != null, "Regional wildlife state API responds");
                    Check("Regional", regional.Records.All(record =>
                        record.nearbyPopulation >= 0f && record.previousNearbyPopulation >= 0f),
                        "Nearby population estimates are nonnegative");
                    Check("Regional",
                        !WildlifePopulationPolicy.CanAddLocalAnimal(100000, 90000, 0, 10f, 20f, false) &&
                        !WildlifePopulationPolicy.CanAddLocalAnimal(300000, 0, 3, 10f, 20f, false) &&
                        WildlifePopulationPolicy.CanAddLocalAnimal(300000, 90000, 0, 10f, 20f, false),
                        "Population policy prevents rapid replacement and excessive local spawning");
                    Check("Regional", typeof(RoamingAnimalRecord).GetField("herdId") != null &&
                        typeof(RegionalWildlifeMapComponent).GetMethod("NotifyLocalSpawn") != null &&
                        typeof(RegionalWildlifeMapComponent).GetMethod("NotifyLocalCapture") != null &&
                        typeof(RegionalWildlifeMapComponent).GetMethod("QueueDeparture", new[]
                            { typeof(Pawn), typeof(string), typeof(IntVec3) }) != null &&
                        typeof(RegionalWildlifeMapComponent).GetMethod("ShouldPreserveExit") != null,
                        "Population lifecycle and roaming herd state are save-compatible");
                    Check("Regional", regional.RoamingAnimals.All(record =>
                        record?.animal?.RaceProps?.Animal == true && record.species == record.animal.def &&
                        System.Enum.IsDefined(typeof(RoamingAnimalState), record.state) &&
                        (record.state == RoamingAnimalState.Present || record.state == RoamingAnimalState.Dead ||
                            record.expectedReturnTick > record.leftTick) &&
                        (record.animal.Spawned || Find.WorldPawns.Contains(record.animal))),
                        "Persistent roaming animals remain present or retained by WorldPawns");
                    HuntingExpeditionMapComponent expeditions = map.GetComponent<HuntingExpeditionMapComponent>();
                    Check("Regional", expeditions.KnownCellRecords.All(cell =>
                        cell.tileId >= 0 && cell.discoveryLevel >= 0 && cell.discoveryLevel <= 2 &&
                        cell.confidence >= 0f && cell.confidence <= 1f), "World-tile knowledge records are valid");
                    Check("Regional", expeditions.KnownCellRecords.All(cell =>
                    {
                        BiomeDef biome = Find.WorldGrid?[(PlanetTile)cell.tileId]?.PrimaryBiome;
                        return biome == null || cell.species.Where(entry => entry?.species != null && entry.population > 0f)
                            .All(entry => biome.AllWildAnimals.Any(kind => kind?.race == entry.species &&
                                biome.CommonalityOfAnimal(kind) > 0.001f));
                    }), "Recorded expedition animals are valid for their tile biome");
                    WildlifeRegionalStoriesMapComponent stories = map.GetComponent<WildlifeRegionalStoriesMapComponent>();
                    Check("Regional", stories != null &&
                        (stories.Wave == null || stories.Wave.species?.race?.Animal == true &&
                            stories.Wave.animals != null && stories.Wave.expectedExitTick > stories.Wave.startedTick),
                        "Visible migration wave state is valid");
                    Check("Regional", stories.TerritoryHistory.All(entry => entry?.animal?.RaceProps?.Animal == true &&
                        entry.from.IsValid && entry.to.IsValid && !entry.reason.NullOrEmpty()),
                        "Territory history entries are valid");
                    Check("Regional", stories.FamilyLines.All(line => line?.animal?.RaceProps?.Animal == true &&
                        line.parent?.RaceProps?.Animal == true && line.species == line.animal.def &&
                        line.generation > 0 && !line.lineName.NullOrEmpty()),
                        "Persistent wildlife family lines are valid");
                    WildlifeLandmarkMapComponent landmark = map.GetComponent<WildlifeLandmarkMapComponent>();
                    Check("Regional", landmark != null && landmark.Reputations.All(value =>
                        value?.species?.race?.Animal == true &&
                        value.sanctuary >= 0f && value.sanctuary <= 1f &&
                        value.water >= 0f && value.water <= 1f &&
                        value.feeding >= 0f && value.feeding <= 1f &&
                        value.forbidden >= 0f && value.forbidden <= 1f &&
                        value.killingGround >= 0f && value.killingGround <= 1f &&
                        value.predatorNest >= 0f && value.predatorNest <= 1f &&
                        value.sacred >= 0f && value.sacred <= 1f &&
                        value.unstable >= 0f && value.unstable <= 1f),
                        "Species-specific colony landmark reputations are valid");
                    Check("Regional", landmark.Reputations.All(value =>
                        landmark.MigrationAttraction(value.species) >= -1.5f &&
                        landmark.MigrationAttraction(value.species) <= 1.5f),
                        "Landmark migration effects remain within bounds");
                });

                Section("Notable", () =>
                {
                    NotableWildlifeMapComponent notable = map.GetComponent<NotableWildlifeMapComponent>();
                    Check("Notable", notable.Records.All(record => record?.species?.race?.Animal == true &&
                        !record.title.NullOrEmpty() && !record.distinction.NullOrEmpty() && record.history != null),
                        "Notable animal records are valid");
                    Check("Notable", notable.Records.All(record => record.lastProtectionResponseTick >= 0),
                        "Protected-animal response state is valid");
                    Check("Notable", notable.Records.Where(record => record?.animal?.Spawned == true && !record.animal.Dead)
                        .All(record => record.ability == null ||
                            record.animal.health.hediffSet.GetFirstHediffOfDef(record.ability) != null ||
                            !HerdsMod.Settings.enableNotableAnimals),
                        "Active notable animals have their distinction ability");
                    Check("Notable", typeof(Window_NotableAnimalStory) != null &&
                        typeof(JobDriver_StudyNotableAnimal) != null,
                        "Notable animal story UI and study job loaded");
                });

                Section("Journal", () =>
                {
                    WildlifeFieldJournalMapComponent journal = map.GetComponent<WildlifeFieldJournalMapComponent>();
                    Check("Journal", journal.DebugOverviewLines().Count >= 4, "Journal state API responds");
                    Check("Journal", journal.Entries.All(entry => entry?.species?.race?.Animal == true),
                        "Journal entries reference valid animal species");
                    Check("Journal", journal.OutcomeBonus >= 0f && journal.OutcomeBonus <= 0.10f &&
                        journal.HuntingSkillBonus >= 0f && journal.HuntingSkillBonus <= 2f,
                        "Permanent journal rewards remain within balance caps");
                    Check("Journal", journal.Opportunity == null ||
                        journal.Opportunity.expiresTick > journal.Opportunity.startedTick &&
                        journal.Opportunity.availableUntilTick > journal.Opportunity.startedTick &&
                        journal.Opportunity.species?.race?.Animal == true &&
                        !journal.Opportunity.eventKey.NullOrEmpty() &&
                        journal.Opportunity.wildlifeWitnesses >= 0,
                        "Active Wildlife Moment has valid real-event state");
                    Check("Journal", journal.MomentHistory.All(value =>
                        value?.species?.race?.Animal == true && !value.text.NullOrEmpty() &&
                        value.tick >= 0), "Wildlife Moment history is valid");
                    Check("Journal", Enum.GetValues(typeof(WildlifeMomentResponse)).Length == 6 &&
                        typeof(WildlifeFieldJournalMapComponent).GetMethod("CompleteMomentObservation") != null &&
                        typeof(WildlifeFieldJournalMapComponent).GetMethod("NotifyAnimalDeparture") != null &&
                        typeof(WildlifeFieldJournalMapComponent).GetMethod("ReferencesAnimal") != null &&
                        typeof(WildlifeFieldJournalMapComponent).GetMethod("MomentBridgeLines") != null,
                        "Wildlife Moments expose player responses, safe continuation, and bridge state");
                    Check("Journal",
                        WildlifeFieldJournalMapComponent.ResearchAllowsResponse(
                            WildlifeMomentResponse.Hunt) ==
                        WildlifeProgression.Unlocked(WildlifeCapability.Fieldcraft) &&
                        WildlifeFieldJournalMapComponent.ResearchAllowsResponse(
                            WildlifeMomentResponse.Track) ==
                        WildlifeProgression.Unlocked(WildlifeCapability.Telemetry),
                        "Moment Hunt and Track actions follow Organized Hunting and Telemetry research");
                    Check("Journal", WildlifeFieldJournalMapComponent.ProtectionAllowsFollowupSelfTest(),
                        "Protect is additive and leaves Observe available");
                    Check("Journal", WildlifeFieldJournalMapComponent.MomentAvailabilitySelfTest(),
                        "Unclaimed Wildlife Moments last a deterministic 1-3 in-game hours");
                    ResearchProjectDef expeditionResearch = DefDatabase<ResearchProjectDef>.GetNamedSilentFail("Wildlife_HuntingExpedition");
                    Check("Journal", Window_WildlifeJournal.ExpeditionsVisible() ==
                        (HerdsMod.Settings.enableOffMapHuntingExpeditions && expeditionResearch?.IsFinished == true),
                        "Field Guide Expeditions tab follows the expedition research gate");
                    Check("Journal", map.GetComponent<WildlifeEcologySnapshotMapComponent>()?.Current?.species
                        .SelectMany(value => value.evidence ?? Array.Empty<WildlifeEvidenceSnapshot>())
                        .All(value => value.summary != "A field observation added a small piece of evidence.") ?? true,
                        "Field Guide evidence excludes routine proximity summaries");
                    Check("Journal", typeof(WildlifeEvidenceSnapshot).GetField("amountDelta") != null &&
                        typeof(WildlifeEvidenceSnapshot).GetField("observerCount") != null &&
                        typeof(WildlifeEvidenceSnapshot).GetField("observationHours") != null,
                        "Field Guide evidence exposes concrete contribution and familiarity metadata");
                    WildlifeEcologySnapshot atlas = map.GetComponent<WildlifeEcologySnapshotMapComponent>()?.Current;
                    Check("Journal", atlas != null && atlas.species.All(value => value != null &&
                        value.species?.race?.Animal == true && value.confidence >= 0f && value.confidence <= 1f),
                        "Living Atlas derives bounded species activity state from the ecology snapshot");
                    Check("Journal", typeof(WildlifeEcologySnapshotMapComponent).GetMethod("DebugOverviewLines") != null,
                        "Living Atlas exposes bounded bridge diagnostics");
                    Check("Journal", typeof(WildlifeKnowledgeAdapter).GetMethod("PredatorPressureStateFor") != null &&
                        typeof(WildlifeSignalCultureMapComponent).GetMethod("ColonyPredatorPressure") != null,
                        "Region and Knowledge hubs can query qualitative predator-pressure state without owning it");
                    Window_WildlifeJournal defaultJournal = new Window_WildlifeJournal(map);
                    object defaultPage = AccessTools.Field(typeof(Window_WildlifeJournal), "page")?.GetValue(defaultJournal);
                    Check("Journal", defaultPage is WildlifeJournalPage &&
                        (WildlifeJournalPage)defaultPage == WildlifeJournalPage.FieldLog,
                        "Journal opens to the Field Log by default");
                    Check("Journal", (int)WildlifeJournalPage.FieldGuide == 0 &&
                        (int)WildlifeJournalPage.LivingAtlas == 1 &&
                        (int)WildlifeJournalPage.Signals == 2 &&
                        (int)WildlifeJournalPage.Investigations == 3 &&
                        (int)WildlifeJournalPage.Expeditions == 4 &&
                        (int)WildlifeJournalPage.Stories == 5 &&
                        (int)WildlifeJournalPage.FieldLog == 6 &&
                        Window_WildlifeJournal.TopLevelPagesForTesting().SequenceEqual(new[]
                        {
                            WildlifeJournalPage.FieldLog,
                            WildlifeJournalPage.Knowledge,
                            WildlifeJournalPage.Region,
                            WildlifeJournalPage.Chronicle
                        }),
                        "Journal preserves legacy page values and exposes four top-level hubs");
                    WildlifeJournalPage[] journalPages =
                    {
                        WildlifeJournalPage.FieldGuide, WildlifeJournalPage.LivingAtlas,
                        WildlifeJournalPage.Signals, WildlifeJournalPage.Investigations,
                        WildlifeJournalPage.Expeditions, WildlifeJournalPage.Stories,
                        WildlifeJournalPage.FieldLog, WildlifeJournalPage.Knowledge,
                        WildlifeJournalPage.Region, WildlifeJournalPage.Chronicle
                    };
                    Check("Journal", journalPages.All(value => new Window_WildlifeJournal(map, value) != null),
                        "Journal constructors retain direct page deep links");
                    WildlifeMenuEntry signalEntry = WildlifeMenuRegistry.VisibleEntriesForTesting()
                        .FirstOrDefault(entry => entry.id == "wildlife.signals");
                    Check("Journal", signalEntry == null &&
                        typeof(Window_WildlifeJournal).GetMethod("OpenSignals") != null &&
                        Window_WildlifeJournal.SignalsVisible() == (HerdsMod.Settings?.enableWildlifeSignalCulture == true),
                        "Signals is a setting-gated Journal page rather than a standalone menu entry");
                    Check("Journal", journal.Opportunity?.continuedAsTrail != true ||
                        journal.Opportunity.evidence is WildlifeSign,
                        "A departed Wildlife Moment retains physical trail evidence");
                    MapComponent packComponent = map.components.FirstOrDefault(component =>
                        component.GetType().FullName == "Packs.PackMapComponent");
                    Check("Journal", packComponent?.GetType().GetMethod("WildlifeMomentHuntPair") != null,
                        "Predator hunts can become Wildlife Moments without a hard dependency");
                    Check("Journal", journal.Project == null ||
                        journal.Project.species?.race?.Animal == true && journal.Project.progress >= 0f,
                        "Active stewardship project state is valid");
                    Check("Journal", !WildlifeFieldJournalMapComponent.ValidProject(null) &&
                        !WildlifeFieldJournalMapComponent.ProjectReady(null) &&
                        !WildlifeFieldJournalMapComponent.ValidProject(
                            new WildlifeStewardProjectRecord { progress = 1f }) &&
                        !WildlifeFieldJournalMapComponent.ProjectReady(
                            new WildlifeStewardProjectRecord { progress = 1f }),
                        "Invalid legacy stewardship projects are rejected before completion");
                    Check("Journal", Enum.GetValues(typeof(WildlifeStewardProjectKind)).Length >= 7,
                        "Expanded wildlife management goals are registered");
                    WildlifeMysteryMapComponent mysteries = map.GetComponent<WildlifeMysteryMapComponent>();
                    Check("Journal", mysteries != null && mysteries.Mysteries.All(value =>
                        value?.species?.race?.Animal == true && !value.title.NullOrEmpty() &&
                        !value.anomaly.NullOrEmpty() && !value.explanation.NullOrEmpty() &&
                        value.progress >= 0f && value.progress <= 1f && value.evidence != null &&
                        value.evidence.All(entry => entry != null && !entry.clue.NullOrEmpty() &&
                            !entry.source.NullOrEmpty() && entry.value > 0f) &&
                        (!value.Solved || value.solvedTick >= value.startedTick) &&
                        (!value.Resolved || value.Solved)),
                        "Living wildlife mysteries have valid causes, evidence, and resolutions");
                });

                Section("Memory", () =>
                {
                    Check("Memory",
                        WildlifeMemoryMapComponent.EventLabel(
                            AnimalMemoryKind.QuietObservation) ==
                        "quietly watching them observe wildlife",
                        "Wildlife can remember witnessing a colonist's quiet observation");
                    WildlifeMemoryMapComponent memory = map.GetComponent<WildlifeMemoryMapComponent>();
                    Check("Memory", memory != null && memory.DebugOverviewLines().Count == 2,
                        "Animal memory and folklore state API responds");
                    string detailedStory = WildlifeMemoryMapComponent.ContextSentence("Muffalo",
                        new[] { "Kim", "Lee" }, "the north pasture");
                    string fallbackStory = WildlifeMemoryMapComponent.ContextSentence(null,
                        null, null);
                    Check("Memory", detailedStory.Contains("Muffalo") &&
                        detailedStory.Contains("Kim") && detailedStory.Contains("Lee") &&
                        detailedStory.Contains("north pasture") &&
                        fallbackStory.Contains("identity was not preserved") &&
                        fallbackStory.Contains("names were not preserved") &&
                        fallbackStory.Contains("unrecorded place"),
                        "Colony Stories include animal, pawn, and location narrative with fallbacks");
                    Check("Memory", memory.Memories.All(value => value?.animal?.RaceProps?.Animal == true &&
                        value.colonist?.Faction == Faction.OfPlayer && value.trust >= 0f && value.trust <= 1f &&
                        value.fear >= 0f && value.fear <= 1f && value.hostility >= 0f && value.hostility <= 1f &&
                        value.huntingEncounters >= 0 && value.rangedEncounters >= 0 && value.trapEncounters >= 0 &&
                        value.events != null && value.events.All(entry => entry != null && entry.tick >= 0)),
                        "Individual animal memories are valid");
                    Check("Memory", memory.SocialMemories.All(value =>
                        value?.animal?.RaceProps?.Animal == true &&
                        value.otherAnimal?.RaceProps?.Animal == true &&
                        value.animal != value.otherAnimal &&
                        value.bond >= 0f && value.bond <= 1f &&
                        value.fear >= 0f && value.fear <= 1f &&
                        value.rivalry >= 0f && value.rivalry <= 1f &&
                        value.positiveEvents >= 0 && value.negativeEvents >= 0 &&
                        value.events != null && value.events.All(entry =>
                            entry != null && entry.tick >= 0 && entry.strength > 0f)),
                        "Animal-to-animal memories and encounters are valid");
                    Check("Memory", memory.SocialMemories.All(value =>
                        memory.SocialAffinity(value.animal, value.otherAnimal) >= -1f &&
                        memory.SocialAffinity(value.animal, value.otherAnimal) <= 1f),
                        "Remembered social affinity remains within behavior bounds");
                    Check("Memory", System.Enum.IsDefined(typeof(AnimalMemoryKind), AnimalMemoryKind.WarningLearned) &&
                        System.Enum.IsDefined(typeof(AnimalMemoryKind), AnimalMemoryKind.Gunfire),
                        "Learned tactics and socially shared warnings are registered");
                    Check("Memory", memory.Memories.All(value =>
                        memory.AvoidanceFactor(value.animal, value.colonist) >= 0.6f &&
                        memory.AvoidanceFactor(value.animal, value.colonist) <= 1.8f),
                        "Trust, fear, and learned hunting responses remain within behavior bounds");
                    Check("Memory", memory.Folklore.All(value => value != null && !value.title.NullOrEmpty() &&
                        !value.story.NullOrEmpty() && value.retellings >= 0 && value.reach >= 0 && value.reach <= 2),
                        "Folklore records and legend reach are valid");
                    AnimalTraditionMapComponent traditions = map.GetComponent<AnimalTraditionMapComponent>();
                    Check("Memory", traditions != null && traditions.Traditions.All(value =>
                        value?.species?.race?.Animal == true && value.holders != null &&
                        value.holders.All(holder => holder?.RaceProps?.Animal == true) &&
                        value.strength >= 0f && value.strength <= 1f &&
                        value.accuracy >= 0f && value.accuracy <= 1f &&
                        !value.title.NullOrEmpty() && !value.belief.NullOrEmpty()),
                        "Animal traditions, mutations, and holders are valid");
                    Check("Memory", map.mapPawns.AllPawnsSpawned.Where(pawn => pawn.RaceProps?.Animal == true)
                        .Take(20).All(pawn => map.mapPawns.FreeColonistsSpawned.Take(3).All(colonist =>
                        {
                            float factor = traditions.AvoidanceFactor(pawn, colonist);
                            return factor >= 0.55f && factor <= 1.9f;
                        })), "Animal tradition behavior factors remain within bounds");
                    Check("Memory", memory.LegendQuest == null ||
                        memory.LegendQuest.species?.race?.Animal == true &&
                        memory.LegendQuest.expiresTick > memory.LegendQuest.startedTick,
                        "Legend challenge state is valid");
                    Check("Memory", map.GetComponent<NotableWildlifeMapComponent>().Records.All(value =>
                        System.Enum.IsDefined(typeof(WildlifeCulturalStatus), value.culturalStatus)),
                        "Notable animal cultural status is valid");
                    WildlifeLivesMapComponent lives = map.GetComponent<WildlifeLivesMapComponent>();
                    Check("Memory", lives != null && lives.DebugLines().Count == 2,
                        "Wildlife lives state API responds");
                    Check("Memory", lives.Personalities.All(value => value?.animal?.RaceProps?.Animal == true &&
                        System.Enum.IsDefined(typeof(AnimalPersonality), value.personality) &&
                        (!value.inherited || value.inheritedFrom != null)),
                        "Animal personalities and inheritance records are valid");
                    if (ModsConfig.IdeologyActive)
                    {
                        Check("Memory", HerdsDefOf.Herds_WildlifeEthic_Reverence != null &&
                            HerdsDefOf.Herds_WildlifeEthic_Stewardship != null &&
                            HerdsDefOf.Herds_WildlifeEthic_Tradition != null,
                            "Wildlife Ideology precepts loaded");
                        Check("Memory", HerdsDefOf.Herds_IdeoRole_MasterHunter != null &&
                            HerdsDefOf.Herds_IdeoRole_MasterConservationist != null,
                            "Wildlife Ideology roles loaded");
                    }
                });

                Section("UI", () =>
                {
                    Check("UI", SpeciesKnowledgeStatsPatch.AnimalKnowledgeInsertIndex(
                            new[] { "Description", "Market Value" }) == 1 &&
                        SpeciesKnowledgeStatsPatch.AnimalKnowledgeInsertIndex(
                            new[] { "Market Value" }) == 0,
                        "Unlocked Description appears before Animal Knowledge in Stats");
                    ThingDef preyDef = DefDatabase<ThingDef>.AllDefsListForReading.FirstOrDefault(PreyProfileDatabase.IsEligible);
                    Check("UI", preyDef?.inspectorTabs?.Contains(typeof(ITab_Herd)) == true, "Prey Wildlife tab registered");
                    Check("UI", DefDatabase<ThingDef>.AllDefsListForReading.Where(def => def.race?.Animal == true)
                        .All(def => def.inspectorTabs?.Contains(typeof(ITab_AnimalMemory)) == true),
                        "Universal animal Memory tab registered");
                    Check("UI", AccessTools.Method(typeof(AnimalMemoryPresentation),
                        nameof(AnimalMemoryPresentation.DrawSocialWeb)) != null &&
                        typeof(Window_AnimalMemoryTimeline).GetConstructor(new[]
                        { typeof(Pawn), typeof(bool) }) != null,
                        "Interactive animal social web is available from Memory");
                    Check("UI", DefDatabase<ThingDef>.AllDefsListForReading.Where(def => def.race?.Animal == true).All(def =>
                        def.inspectorTabsResolved?.Any(tab => tab.GetType() == typeof(ITab_AnimalMemory)) == true &&
                        (!PreyProfileDatabase.IsEligible(def) ||
                            def.inspectorTabsResolved.Any(tab => tab.GetType() == typeof(ITab_Herd)))),
                        "Resolved selected-animal tabs retain Memory and applicable Wildlife entries");
                    Check("UI", DefDatabase<ThingDef>.AllDefsListForReading.Where(def => def.race?.Animal == true)
                        .All(def =>
                        {
                            int health = def.inspectorTabs.FindIndex(type => type.FullName == "RimWorld.ITab_Pawn_Health");
                            int needs = def.inspectorTabs.FindIndex(type => type.FullName == "RimWorld.ITab_Pawn_Needs");
                            int training = def.inspectorTabs.FindIndex(type => type.FullName == "RimWorld.ITab_Pawn_Training");
                            int social = def.inspectorTabs.FindIndex(type => type.FullName == "RimWorld.ITab_Pawn_Social");
                            int memory = def.inspectorTabs.IndexOf(typeof(ITab_AnimalMemory));
                            int log = def.inspectorTabs.FindIndex(type => type.FullName == "RimWorld.ITab_Pawn_Log");
                            int wildlife = def.inspectorTabs.FindIndex(type =>
                                type.FullName == "Herds.ITab_Herd" || type.FullName == "Packs.ITab_Pack");
                            bool Ordered(int left, int right) =>
                                left < 0 || right < 0 || left < right;
                            return memory >= 0 && Ordered(needs, memory) &&
                                Ordered(memory, health) && Ordered(health, social) &&
                                Ordered(social, training) && Ordered(training, wildlife) &&
                                Ordered(wildlife, log);
                        }), "Available animal tabs follow the safe right-to-left ordering");
                    Check("UI", AccessTools.Method(typeof(AnimalNeedsTabStaleSelectionGuard), "Prefix") != null,
                        "Needs tab safely handles despawned or cleared animal selection");
                    IReadOnlyList<WildlifeMenuEntry> wildlifeMenu =
                        WildlifeMenuRegistry.VisibleEntriesForTesting();
                    Check("UI", wildlifeMenu.Select(entry => entry.id).Distinct().Count() == wildlifeMenu.Count &&
                        wildlifeMenu.SequenceEqual(wildlifeMenu.OrderBy(entry => entry.order)
                            .ThenBy(entry => entry.label, StringComparer.OrdinalIgnoreCase)
                            .ThenBy(entry => entry.id, StringComparer.Ordinal)),
                        "Shared Wildlife menu entries are unique and use stable ordering");
                    Check("UI", WildlifeMenuRegistry.RequiredHeight(4, 560f) == 80f,
                        "Shared Wildlife menu reserves two rows when four buttons wrap at narrow width");
                    Check("UI", Window_WildlifeOverview.OutcomeRowHeight(
                            "A long wildlife outcome wraps across several lines without clipping its text.",
                            120f) > 48f,
                        "Recent Outcome rows grow for wrapped text");
                    Check("UI", typeof(ChoiceLetter_WildlifeStory).GetMethod("OpenLetter") != null &&
                        typeof(Window_WildlifeFieldJournal).GetConstructor(new[]
                        { typeof(Map), typeof(int), typeof(int) }) != null,
                        "Colony Story letters can reopen Folklore at a saved story tick");
                    Check("UI", typeof(Window_WildlifeTrail).GetConstructor(new[]
                            { typeof(Map), typeof(WildlifeTrailLead) }) != null &&
                        typeof(Window_WildlifeTrailBoard).GetConstructor(new[] { typeof(Map) }) != null &&
                        typeof(Window_WildlifeLandscape).GetConstructor(new[] { typeof(Map) }) != null &&
                        typeof(Window_RegionalWildlife).GetConstructor(new[] { typeof(Map) }) != null &&
                        typeof(Window_WildlifeExpeditions).GetConstructor(new[] { typeof(Map) }) != null,
                        "Journal detail destinations retain trail, region, landscape, and expedition constructors");
                    Check("UI", typeof(Window_WildlifeSignals).GetConstructor(new[]
                        {
                            typeof(Map), typeof(Pawn), typeof(ThingDef),
                            typeof(UnityEngine.Vector2?), typeof(UnityEngine.Vector2?)
                        }) != null,
                        "Legacy Signal Guide callers redirect with viewer, species, and scroll state");
                    Check("UI", AccessTools.Method(typeof(WildlifeUI), "Focus",
                            new[] { typeof(Thing) }) != null &&
                        AccessTools.Method(typeof(WildlifeUI), "Focus",
                            new[] { typeof(IntVec3), typeof(Map) }) != null,
                        "Focus actions share menu-closing target navigation");
                    Check("UI", wildlifeMenu.FirstOrDefault()?.id == "wildlife.overview" &&
                        wildlifeMenu.First().order == WildlifeMenuRegistry.OverviewOrder,
                         "Wildlife Journal is the first shared Wildlife menu button");
                    bool horticultureActive = ModsConfig.IsActive("lan.horticulture.novelseeds");
                    MainButtonDef cultivarRegistry =
                        DefDatabase<MainButtonDef>.GetNamedSilentFail("HNS_CultivarRegistry");
                    Check("UI", wildlifeMenu.Any(entry => entry.id == "horticulture.novel-seeds") ==
                            horticultureActive &&
                        (!horticultureActive || cultivarRegistry?.tabWindowClass?.FullName ==
                            "HorticultureNovelSeeds.MainTabWindow_CultivarRegistry"),
                        "Optional Horticulture button reuses the Novel Seeds Cultivar Registry");
                    bool aquacultureActive = ModsConfig.IsActive("lan.aquaculture.fishing");
                    MainButtonDef aquacultureJournal =
                        DefDatabase<MainButtonDef>.GetNamedSilentFail("AF_AquacultureJournal");
                    Check("UI", wildlifeMenu.Any(entry => entry.id == "aquaculture.fish-journal") ==
                            aquacultureActive &&
                        (!aquacultureActive || aquacultureJournal?.tabWindowClass?.FullName ==
                            "AquacultureFishing.MainTabWindow_AquacultureJournal"),
                        "Optional Aquaculture button reuses the existing Fish Journal");
                    List<string> expectedSharedButtons = new List<string> { "Wildlife Journal" };
                    if (horticultureActive) expectedSharedButtons.Add("Horticulture");
                    if (aquacultureActive) expectedSharedButtons.Add("Aquaculture");
                    Check("UI", wildlifeMenu.Take(expectedSharedButtons.Count)
                            .Select(entry => entry.label).SequenceEqual(expectedSharedButtons),
                        "Shared Wildlife menu begins with the requested available buttons in order");
                    WildlifeMenuEntry expeditionsEntry =
                        wildlifeMenu.FirstOrDefault(entry => entry.id == "wildlife.expeditions");
                    Check("UI", expeditionsEntry == null || expeditionsEntry.label == "Expeditions",
                        "Wildlife expedition navigation uses the concise Expeditions label");
                    Type predatorTab = AccessTools.TypeByName("Packs.ITab_Pack");
                    List<ThingDef> predatorDefs = DefDatabase<ThingDef>.AllDefsListForReading.Where(def =>
                        def.race?.Animal == true &&
                        WildlifeSpeciesClassification.IsPredator(def)).ToList();
                    Warn("UI", predatorDefs.Count == 0 || predatorDefs.Any(def => def.inspectorTabs?.Contains(predatorTab) == true),
                        "Predator Wildlife tab is not registered on any predator");
                    Check("UI", typeof(WITab_Nature).IsSubclassOf(typeof(WITab)), "World-map Nature tab loaded");
                    Check("UI", AccessTools.Method(typeof(WorldInspectPane), "get_CurTabs") != null,
                        "World inspector tab integration target exists");
                    Check("UI", AccessTools.Method(typeof(GizmoGridDrawer), nameof(GizmoGridDrawer.DrawGizmoGrid)) != null,
                        "Send Expedition world gizmo integration target exists");
                });
            }

            stopwatch.Stop();
            return WriteReport(map, results, stopwatch.ElapsedMilliseconds, quiet);
        }

        private static bool WriteReport(Map map, List<Result> results, long milliseconds, bool quiet)
        {
            int passed = results.Count(result => result.severity == "PASS");
            int warnings = results.Count(result => result.severity == "WARN");
            int failed = results.Count(result => result.severity == "FAIL");
            List<string> lines = new List<string>
            {
                "WILDLIFE_TEST_REPORT v1",
                "utc=" + DateTime.UtcNow.ToString("O") + " tick=" + (Find.TickManager?.TicksGame ?? -1),
                "summary=" + (failed == 0 ? "PASS" : "FAIL") + " pass=" + passed + " warn=" + warnings +
                    " fail=" + failed + " ms=" + milliseconds,
                "context=map:" + (map?.uniqueID.ToString() ?? "none") + " pawns:" +
                    (map?.mapPawns?.AllPawnsSpawnedCount.ToString() ?? "0") + " mods:" + LoadedModManager.RunningModsListForReading.Count()
            };
            if (map != null)
            {
                lines.Add("features=prey:" + Bool(HerdsMod.Settings.enablePreyAndHerds) +
                    " hunts:" + Bool(HerdsMod.Settings.enableHuntingChanges) +
                    " expeditions:" + Bool(HerdsMod.Settings.enableOffMapHuntingExpeditions) +
                    " regional:" + Bool(HerdsMod.Settings.enableRegionalPopulations) +
                    " knowledge:" + Bool(HerdsMod.Settings.enableWildlifeKnowledge));
                lines.Add("metrics=prey:" + map.mapPawns.AllPawnsSpawned.Count(pawn => PreyProfileDatabase.IsEligible(pawn.def)) +
                    " predators:" + map.mapPawns.AllPawnsSpawned.Count(pawn =>
                        WildlifeSpeciesClassification.IsPredator(pawn.def)) +
                    " signs:" + map.listerThings.ThingsOfDef(HerdsDefOf.Herds_WildlifeSign).Count +
                    " knownTiles:" + map.GetComponent<HuntingExpeditionMapComponent>().KnownCellRecords.Count +
                    " activeExpeditions:" + map.GetComponent<HuntingExpeditionMapComponent>().ActiveExpeditions.Count);
            }
            foreach (IGrouping<string, Result> section in results.GroupBy(result => result.section))
                lines.Add("section=" + section.Key + " pass=" + section.Count(result => result.severity == "PASS") +
                    " warn=" + section.Count(result => result.severity == "WARN") +
                    " fail=" + section.Count(result => result.severity == "FAIL"));
            foreach (Result result in results.Where(result => result.severity != "PASS"))
                lines.Add(result.severity + "|" + result.section + "|" + result.text.Replace('\n', ' '));
            try
            {
                File.WriteAllLines(ReportPath, lines);
                if (!quiet)
                {
                    Log.Message("[WildlifeTest][FullSuite] " + lines[2] + " report=" + ReportPath);
                    Messages.Message("Wildlife test " + (failed == 0 ? "passed" : "failed") + ": " + passed + " passed, " +
                        warnings + " warnings, " + failed + " failed. Report saved.", failed == 0 ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.NegativeEvent, false);
                }
                return failed == 0;
            }
            catch (Exception exception)
            {
                Log.Error("[WildlifeTest][FullSuite] Could not write report: " + exception);
                if (!quiet)
                    Messages.Message("Wildlife test ran but the report could not be saved: " + exception.GetBaseException().Message,
                        MessageTypeDefOf.NegativeEvent, false);
                return false;
            }
        }

        private static string Bool(bool value) => value ? "on" : "off";
    }
}
