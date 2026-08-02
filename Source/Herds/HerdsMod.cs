using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Herds
{
    public sealed class HerdsSettings : ModSettings
    {
        public bool enablePreyAndHerds = true;
        public bool enableHuntingChanges = true;
        public bool enableResearchProgression = true;
        public bool gateHuntingByResearch = true;
        public bool gateKnowledgeByResearch = true;
        public bool gateStewardshipByResearch = true;
        public bool gateIndustrialEcologyByResearch = true;
        public int updateIntervalTicks = 300;
        public float unpennedJoinDistance = 24f;
        public bool coordinateWildHerds = true;
        public bool enableDefensiveBehavior = true;
        public int defenseScanIntervalTicks = 60;
        public float protectiveBodySizeThreshold = 1.1f;
        public float flightDistance = 20f;
        public bool enableHiding = true;
        public bool allowNaturalBurrows = true;
        public float maximumInferredHidingBodySize = 0.45f;
        public float defaultHideSuccessChance = 0.7f;
        public int hideEntryTicks = 120;
        public int failedHideRetryTicks = 600;
        public int refugeRefreshIntervalTicks = 1200;
        public int minimumHideTicks = 600;
        public int maximumHideTicks = 5000;
        public bool enablePredatorEscapeChance = true;
        public float basePredatorEscapeChance = 0.12f;
        public int predatorEscapeCheckIntervalTicks = 240;
        public bool enableWildlifeKnowledge = true;
        public bool requireObservationForDetails = true;
        public bool enableObservationPosts = true;
        public bool enableWildlifeBait = true;
        public bool enablePredatorDeterrents = true;
        public bool enableWildlifeReserves = true;
        public bool enableEcologicalConsequences = true;
        public bool enableWildlifeLandscaping = true;
        public bool enableLandscapeEffects = true;
        public bool enableLandscapeCrossroads = true;
        public bool enableWildlifeAlerts = true;
        public bool enableTrackingSigns = true;
        public bool enableTrailReading = true;
        public bool enableWindHud = true;
        public bool enableScentMasking = true;
        public bool enableAnimalCalls = true;
        public bool enableWildlifeSignalCulture = true;
        public bool showIdentifiedSignalText = true;
        public bool enablePlayerSignalImitation = true;
        public bool enablePredatorSignalLearning = true;
        public bool enableMannedBlinds = true;
        public bool enableRanchGuardians = true;
        public bool guardiansAttackPredators = true;
        public bool preyAvoidColonists = true;
        public bool enableHuntingExpeditions = true;
        public bool enableOffMapHuntingExpeditions = true;
        public bool enableExtendedHuntingExpeditions = true;
        public bool enableExpeditionIncidents = true;
        public bool enableExpeditionBiomeEvents = true;
        public bool enableInteractiveExpeditionEncounters = true;
        public bool allowExpeditionDeaths = false;
        public bool enableUncertainPredatorWarnings = true;
        public bool enableWildlifeSteward = true;
        public bool enableGuardianPatrolAreas = true;
        public int minimumFieldcraftSkill = 3;
        public bool enableSpeciesKnowledgeProgression = true;
        public bool enableWeaponAwareTactics = true;
        public bool enableWoundedTrackingAndRetreat = true;
        public bool enableHuntTracking = true;
        public bool enableHuntedAdrenaline = true;
        public bool enableHuntEndurance = true;
        public bool enableAdaptivePreyResponses = true;
        public bool enableFieldcraftEquipment = true;
        public bool enableDomesticPredatorRoles = true;
        public bool enableScavenging = true;
        public bool enableTerritorialSigns = true;
        public bool enableJuvenileLearning = true;
        public bool enableHabitatEcology = true;
        public bool enableRegionalPopulations = true;
        public bool enableRegionalMigration = true;
        public bool enablePersistentRoamingAnimals = true;
        public bool enableRoamingExpeditionEncounters = true;
        public bool enableReturnSigns = true;
        public bool enableVisibleMigrationWaves = true;
        public bool enableTerritoryHistory = true;
        public bool enableWildlifeManagementGoals = true;
        public bool enablePersistentFamilyLines = true;
        public bool enableRegionalMap = true;
        public bool enableConservationActions = true;
        public bool enablePopulationConsequences = true;
        public bool enableWildlifeEvents = true;
        public bool enableSeasonalEcologyEvents = true;
        public bool enableNotableAnimals = true;
        public bool enableHuntingRegulations = true;
        public bool enableAnimalRelationships = true;
        public bool enableAnimalPersonalities = true;
        public bool enablePersonalityInheritance = true;
        public bool enableWildlifeLifeIncidents = true;
        public bool enableAnimalMemory = true;
        public bool enableAnimalSocialMemory = true;
        public bool enableAnimalTraditions = true;
        public bool enableColonyWildlifeLandmark = true;
        public bool enableWildlifeFolklore = true;
        public bool enableWildlifeIdeology = true;
        public bool enableFolkloreRetelling = true;
        public bool enableCulturalAnimals = true;
        public bool enableWildlifeCeremonies = true;
        public bool enableFolkloreDisplays = true;
        public bool enableLegendSpread = true;
        public bool enableLegendQuests = true;
        public bool enablePhysicalWildlifeStories = true;
        public bool enableLegendaryPresentation = true;
        public bool enableWildlifeLearning = true;
        public bool enableWildlifeIdeologyRoles = true;
        public bool enableAdvancedScavenging = true;
        public bool enableDomesticRoleProgression = true;
        public bool enableCameraTraps = true;
        public bool enableTelemetry = true;
        public bool enableDiseaseMonitoring = true;
        public bool enableAppliedEcology = true;
        public bool enablePlayerOnboarding = true;
        public bool enableOutcomeHistory = true;
        public bool enableUnlockLetters = true;
        public bool enableFieldJournal = true;
        public bool enableWildlifeMysteries = true;
        public bool enableDynamicWildlifeOpportunities = true;
        public bool enableStewardProjects = true;
        public bool enableHuntRewards = true;
        public float hiddenPreySafeDistance = 40f;
        public int frightenedMemoryLifetimeTicks = 900000;
        public int packMemberKilledMemoryLifetimeTicks = 3600000;
        public List<SpeciesBehaviorOverride> speciesOverrides = new List<SpeciesBehaviorOverride>();

        public override void ExposeData()
        {
            Scribe_Values.Look(ref enablePreyAndHerds, "enablePreyAndHerds", true);
            Scribe_Values.Look(ref enableHuntingChanges, "enableHuntingChanges", true);
            Scribe_Values.Look(ref enableResearchProgression, "enableResearchProgression", true);
            Scribe_Values.Look(ref gateHuntingByResearch, "gateHuntingByResearch", true);
            Scribe_Values.Look(ref gateKnowledgeByResearch, "gateKnowledgeByResearch", true);
            Scribe_Values.Look(ref gateStewardshipByResearch, "gateStewardshipByResearch", true);
            Scribe_Values.Look(ref gateIndustrialEcologyByResearch, "gateIndustrialEcologyByResearch", true);
            Scribe_Values.Look(ref updateIntervalTicks, "updateIntervalTicks", 300);
            Scribe_Values.Look(ref unpennedJoinDistance, "unpennedJoinDistance", 24f);
            Scribe_Values.Look(ref coordinateWildHerds, "coordinateWildHerds", true);
            Scribe_Values.Look(ref enableDefensiveBehavior, "enableDefensiveBehavior", true);
            Scribe_Values.Look(ref defenseScanIntervalTicks, "defenseScanIntervalTicks", 60);
            Scribe_Values.Look(ref protectiveBodySizeThreshold, "protectiveBodySizeThreshold", 1.1f);
            Scribe_Values.Look(ref flightDistance, "flightDistance", 20f);
            Scribe_Values.Look(ref enableHiding, "enableHiding", true);
            Scribe_Values.Look(ref allowNaturalBurrows, "allowNaturalBurrows", true);
            Scribe_Values.Look(ref maximumInferredHidingBodySize, "maximumInferredHidingBodySize", 0.45f);
            Scribe_Values.Look(ref defaultHideSuccessChance, "defaultHideSuccessChance", 0.7f);
            Scribe_Values.Look(ref hideEntryTicks, "hideEntryTicks", 120);
            Scribe_Values.Look(ref failedHideRetryTicks, "failedHideRetryTicks", 600);
            Scribe_Values.Look(ref refugeRefreshIntervalTicks, "refugeRefreshIntervalTicks", 1200);
            Scribe_Values.Look(ref minimumHideTicks, "minimumHideTicks", 600);
            Scribe_Values.Look(ref maximumHideTicks, "maximumHideTicks", 5000);
            Scribe_Values.Look(ref enablePredatorEscapeChance, "enablePredatorEscapeChance", true);
            Scribe_Values.Look(ref basePredatorEscapeChance, "basePredatorEscapeChance", 0.12f);
            Scribe_Values.Look(ref predatorEscapeCheckIntervalTicks, "predatorEscapeCheckIntervalTicks", 240);
            Scribe_Values.Look(ref enableWildlifeKnowledge, "enableWildlifeKnowledge", true);
            Scribe_Values.Look(ref requireObservationForDetails, "requireObservationForDetails", true);
            Scribe_Values.Look(ref enableObservationPosts, "enableObservationPosts", true);
            Scribe_Values.Look(ref enableWildlifeBait, "enableWildlifeBait", true);
            Scribe_Values.Look(ref enablePredatorDeterrents, "enablePredatorDeterrents", true);
            Scribe_Values.Look(ref enableWildlifeReserves, "enableWildlifeReserves", true);
            Scribe_Values.Look(ref enableEcologicalConsequences, "enableEcologicalConsequences", true);
            Scribe_Values.Look(ref enableWildlifeLandscaping, "enableWildlifeLandscaping", true);
            Scribe_Values.Look(ref enableLandscapeEffects, "enableLandscapeEffects", true);
            Scribe_Values.Look(ref enableLandscapeCrossroads, "enableLandscapeCrossroads", true);
            Scribe_Values.Look(ref enableWildlifeAlerts, "enableWildlifeAlerts", true);
            Scribe_Values.Look(ref enableTrackingSigns, "enableTrackingSigns", true);
            Scribe_Values.Look(ref enableTrailReading, "enableTrailReading", true);
            Scribe_Values.Look(ref enableWindHud, "enableWindHud", true);
            Scribe_Values.Look(ref enableScentMasking, "enableScentMasking", true);
            Scribe_Values.Look(ref enableAnimalCalls, "enableAnimalCalls", true);
            Scribe_Values.Look(ref enableWildlifeSignalCulture, "enableWildlifeSignalCulture", true);
            Scribe_Values.Look(ref showIdentifiedSignalText, "showIdentifiedSignalText", true);
            Scribe_Values.Look(ref enablePlayerSignalImitation, "enablePlayerSignalImitation", true);
            Scribe_Values.Look(ref enablePredatorSignalLearning, "enablePredatorSignalLearning", true);
            Scribe_Values.Look(ref enableMannedBlinds, "enableMannedBlinds", true);
            Scribe_Values.Look(ref enableRanchGuardians, "enableRanchGuardians", true);
            Scribe_Values.Look(ref guardiansAttackPredators, "guardiansAttackPredators", true);
            Scribe_Values.Look(ref preyAvoidColonists, "preyAvoidColonists", true);
            Scribe_Values.Look(ref enableHuntingExpeditions, "enableHuntingExpeditions", true);
            Scribe_Values.Look(ref enableOffMapHuntingExpeditions, "enableOffMapHuntingExpeditions", true);
            Scribe_Values.Look(ref enableExtendedHuntingExpeditions, "enableExtendedHuntingExpeditions", true);
            Scribe_Values.Look(ref enableExpeditionIncidents, "enableExpeditionIncidents", true);
            Scribe_Values.Look(ref enableExpeditionBiomeEvents, "enableExpeditionBiomeEvents", true);
            Scribe_Values.Look(ref enableInteractiveExpeditionEncounters, "enableInteractiveExpeditionEncounters", true);
            Scribe_Values.Look(ref allowExpeditionDeaths, "allowExpeditionDeaths", false);
            Scribe_Values.Look(ref enableUncertainPredatorWarnings, "enableUncertainPredatorWarnings", true);
            Scribe_Values.Look(ref enableWildlifeSteward, "enableWildlifeSteward", true);
            Scribe_Values.Look(ref enableGuardianPatrolAreas, "enableGuardianPatrolAreas", true);
            Scribe_Values.Look(ref minimumFieldcraftSkill, "minimumFieldcraftSkill", 3);
            Scribe_Values.Look(ref enableSpeciesKnowledgeProgression, "enableSpeciesKnowledgeProgression", true);
            Scribe_Values.Look(ref enableWeaponAwareTactics, "enableWeaponAwareTactics", true);
            Scribe_Values.Look(ref enableWoundedTrackingAndRetreat, "enableWoundedTrackingAndRetreat", true);
            Scribe_Values.Look(ref enableHuntTracking, "enableHuntTracking", true);
            Scribe_Values.Look(ref enableHuntedAdrenaline, "enableHuntedAdrenaline", true);
            Scribe_Values.Look(ref enableHuntEndurance, "enableHuntEndurance", true);
            Scribe_Values.Look(ref enableAdaptivePreyResponses, "enableAdaptivePreyResponses", true);
            Scribe_Values.Look(ref enableFieldcraftEquipment, "enableFieldcraftEquipment", true);
            Scribe_Values.Look(ref enableDomesticPredatorRoles, "enableDomesticPredatorRoles", true);
            Scribe_Values.Look(ref enableScavenging, "enableScavenging", true);
            Scribe_Values.Look(ref enableTerritorialSigns, "enableTerritorialSigns", true);
            Scribe_Values.Look(ref enableJuvenileLearning, "enableJuvenileLearning", true);
            Scribe_Values.Look(ref enableHabitatEcology, "enableHabitatEcology", true);
            Scribe_Values.Look(ref enableRegionalPopulations, "enableRegionalPopulations", true);
            Scribe_Values.Look(ref enableRegionalMigration, "enableRegionalMigration", true);
            Scribe_Values.Look(ref enablePersistentRoamingAnimals, "enablePersistentRoamingAnimals", true);
            Scribe_Values.Look(ref enableRoamingExpeditionEncounters, "enableRoamingExpeditionEncounters", true);
            Scribe_Values.Look(ref enableReturnSigns, "enableReturnSigns", true);
            Scribe_Values.Look(ref enableVisibleMigrationWaves, "enableVisibleMigrationWaves", true);
            Scribe_Values.Look(ref enableTerritoryHistory, "enableTerritoryHistory", true);
            Scribe_Values.Look(ref enableWildlifeManagementGoals, "enableWildlifeManagementGoals", true);
            Scribe_Values.Look(ref enablePersistentFamilyLines, "enablePersistentFamilyLines", true);
            Scribe_Values.Look(ref enableRegionalMap, "enableRegionalMap", true);
            Scribe_Values.Look(ref enableConservationActions, "enableConservationActions", true);
            Scribe_Values.Look(ref enablePopulationConsequences, "enablePopulationConsequences", true);
            Scribe_Values.Look(ref enableWildlifeEvents, "enableWildlifeEvents", true);
            Scribe_Values.Look(ref enableSeasonalEcologyEvents, "enableSeasonalEcologyEvents", true);
            Scribe_Values.Look(ref enableNotableAnimals, "enableNotableAnimals", true);
            Scribe_Values.Look(ref enableHuntingRegulations, "enableHuntingRegulations", true);
            Scribe_Values.Look(ref enableAnimalRelationships, "enableAnimalRelationships", true);
            Scribe_Values.Look(ref enableAnimalPersonalities, "enableAnimalPersonalities", true);
            Scribe_Values.Look(ref enablePersonalityInheritance, "enablePersonalityInheritance", true);
            Scribe_Values.Look(ref enableWildlifeLifeIncidents, "enableWildlifeLifeIncidents", true);
            Scribe_Values.Look(ref enableAnimalMemory, "enableAnimalMemory", true);
            Scribe_Values.Look(ref enableAnimalSocialMemory, "enableAnimalSocialMemory", true);
            Scribe_Values.Look(ref enableAnimalTraditions, "enableAnimalTraditions", true);
            Scribe_Values.Look(ref enableColonyWildlifeLandmark, "enableColonyWildlifeLandmark", true);
            Scribe_Values.Look(ref enableWildlifeFolklore, "enableWildlifeFolklore", true);
            Scribe_Values.Look(ref enableWildlifeIdeology, "enableWildlifeIdeology", true);
            Scribe_Values.Look(ref enableFolkloreRetelling, "enableFolkloreRetelling", true);
            Scribe_Values.Look(ref enableCulturalAnimals, "enableCulturalAnimals", true);
            Scribe_Values.Look(ref enableWildlifeCeremonies, "enableWildlifeCeremonies", true);
            Scribe_Values.Look(ref enableFolkloreDisplays, "enableFolkloreDisplays", true);
            Scribe_Values.Look(ref enableLegendSpread, "enableLegendSpread", true);
            Scribe_Values.Look(ref enableLegendQuests, "enableLegendQuests", true);
            Scribe_Values.Look(ref enablePhysicalWildlifeStories, "enablePhysicalWildlifeStories", true);
            Scribe_Values.Look(ref enableLegendaryPresentation, "enableLegendaryPresentation", true);
            Scribe_Values.Look(ref enableWildlifeLearning, "enableWildlifeLearning", true);
            Scribe_Values.Look(ref enableWildlifeIdeologyRoles, "enableWildlifeIdeologyRoles", true);
            Scribe_Values.Look(ref enableAdvancedScavenging, "enableAdvancedScavenging", true);
            Scribe_Values.Look(ref enableDomesticRoleProgression, "enableDomesticRoleProgression", true);
            Scribe_Values.Look(ref enableCameraTraps, "enableCameraTraps", true);
            Scribe_Values.Look(ref enableTelemetry, "enableTelemetry", true);
            Scribe_Values.Look(ref enableDiseaseMonitoring, "enableDiseaseMonitoring", true);
            Scribe_Values.Look(ref enableAppliedEcology, "enableAppliedEcology", true);
            Scribe_Values.Look(ref enablePlayerOnboarding, "enablePlayerOnboarding", true);
            Scribe_Values.Look(ref enableOutcomeHistory, "enableOutcomeHistory", true);
            Scribe_Values.Look(ref enableUnlockLetters, "enableUnlockLetters", true);
            Scribe_Values.Look(ref enableFieldJournal, "enableFieldJournal", true);
            Scribe_Values.Look(ref enableWildlifeMysteries, "enableWildlifeMysteries", true);
            Scribe_Values.Look(ref enableDynamicWildlifeOpportunities, "enableDynamicWildlifeOpportunities", true);
            Scribe_Values.Look(ref enableStewardProjects, "enableStewardProjects", true);
            Scribe_Values.Look(ref enableHuntRewards, "enableHuntRewards", true);
            Scribe_Values.Look(ref hiddenPreySafeDistance, "hiddenPreySafeDistance", 40f);
            Scribe_Values.Look(ref frightenedMemoryLifetimeTicks, "frightenedMemoryLifetimeTicks", 900000);
            Scribe_Values.Look(ref packMemberKilledMemoryLifetimeTicks, "packMemberKilledMemoryLifetimeTicks", 3600000);
            Scribe_Collections.Look(ref speciesOverrides, "speciesOverrides", LookMode.Deep);
            if (speciesOverrides == null) speciesOverrides = new List<SpeciesBehaviorOverride>();
            updateIntervalTicks = Mathf.Clamp(updateIntervalTicks, 120, 2000);
            unpennedJoinDistance = Mathf.Clamp(unpennedJoinDistance, 8f, 60f);
            defenseScanIntervalTicks = Mathf.Clamp(defenseScanIntervalTicks, 30, 300);
            protectiveBodySizeThreshold = Mathf.Clamp(protectiveBodySizeThreshold, 0.3f, 4f);
            flightDistance = Mathf.Clamp(flightDistance, 8f, 36f);
            maximumInferredHidingBodySize = Mathf.Clamp(maximumInferredHidingBodySize, 0.1f, 1f);
            defaultHideSuccessChance = Mathf.Clamp(defaultHideSuccessChance, 0.05f, 1f);
            hideEntryTicks = Mathf.Clamp(hideEntryTicks, 30, 600);
            failedHideRetryTicks = Mathf.Clamp(failedHideRetryTicks, 120, 2500);
            refugeRefreshIntervalTicks = Mathf.Clamp(refugeRefreshIntervalTicks, 600, 5000);
            minimumHideTicks = Mathf.Clamp(minimumHideTicks, 120, 2500);
            maximumHideTicks = Mathf.Clamp(maximumHideTicks, minimumHideTicks + 300, 15000);
            basePredatorEscapeChance = Mathf.Clamp(basePredatorEscapeChance, 0f, 0.5f);
            predatorEscapeCheckIntervalTicks = Mathf.Clamp(predatorEscapeCheckIntervalTicks, 120, 900);
            frightenedMemoryLifetimeTicks = Mathf.Clamp(frightenedMemoryLifetimeTicks, 60000, 3600000);
            packMemberKilledMemoryLifetimeTicks = Mathf.Clamp(packMemberKilledMemoryLifetimeTicks, 600000, 7200000);
        }
    }

    public sealed class HerdsMod : Mod
    {
        public static HerdsMod Instance;
        public static HerdsSettings Settings;
        public static readonly Harmony Harmony = new Harmony("lan.herds");
        private static int settingsPage;
        private static Vector2 playerSettingsScroll;

        public HerdsMod(ModContentPack content) : base(content)
        {
            Instance = this;
            Settings = GetSettings<HerdsSettings>();
            WildlifeProgression.RefreshDefGates();
        }

        public override string SettingsCategory() => null;

        public override void DoSettingsWindowContents(Rect inRect)
        {
            if (Widgets.ButtonText(new Rect(inRect.x, inRect.y, 150f, 34f), "Simulation")) settingsPage = 0;
            if (Widgets.ButtonText(new Rect(inRect.x + 158f, inRect.y, 170f, 34f), "Player Interaction")) settingsPage = 1;
            Rect body = new Rect(inRect.x, inRect.y + 46f, inRect.width, inRect.height - 46f);
            if (settingsPage == 1)
            {
                DrawPlayerSettings(body);
                return;
            }
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(body);
            if (listing.ButtonText("Species Behavior Profiles")) Find.WindowStack.Add(new Window_SpeciesBehaviorProfiles());
            listing.Gap();
            listing.Label("Simulation");
            listing.GapLine();
            listing.Label("Herd refresh interval: " + Settings.updateIntervalTicks + " ticks");
            Settings.updateIntervalTicks = Mathf.RoundToInt(listing.Slider(Settings.updateIntervalTicks, 120f, 2000f));
            TooltipHandler.TipRegion(listing.GetRect(0f), "Membership and movement roots are rebuilt in one map-level batch at this interval. Higher values reduce CPU use but make herds respond more slowly.");
            listing.Label("Unpenned join distance: " + Settings.unpennedJoinDistance.ToString("0") + " cells");
            Settings.unpennedJoinDistance = listing.Slider(Settings.unpennedJoinDistance, 8f, 60f);
            listing.CheckboxLabeled("Coordinate Wild Herds", ref Settings.coordinateWildHerds, "Apply cached herd movement to eligible wild animals as well as colony animals.");
            listing.Gap();
            listing.Label("Defense");
            listing.GapLine();
            listing.CheckboxLabeled("Enable Coordinated Herd Defense", ref Settings.enableDefensiveBehavior, "Herds flee together or protect their young when threatened.");
            listing.Label("Defense scan interval: " + Settings.defenseScanIntervalTicks + " ticks");
            Settings.defenseScanIntervalTicks = Mathf.RoundToInt(listing.Slider(Settings.defenseScanIntervalTicks, 30f, 300f));
            listing.Label("Protective herd body size threshold: " + Settings.protectiveBodySizeThreshold.ToString("0.0"));
            Settings.protectiveBodySizeThreshold = listing.Slider(Settings.protectiveBodySizeThreshold, 0.3f, 4f);
            TooltipHandler.TipRegion(listing.GetRect(0f), "Herds with young, at least three adults, and this average adult body size form a protective ring. Smaller herds flee.");
            listing.Label("Flight distance: " + Settings.flightDistance.ToString("0") + " cells");
            Settings.flightDistance = listing.Slider(Settings.flightDistance, 8f, 36f);
            listing.Gap();
            listing.Label("Hiding");
            listing.GapLine();
            listing.CheckboxLabeled("Enable Prey Hiding", ref Settings.enableHiding, "Compatible small prey travel to real trees or animal hide holes when pursued.");
            listing.CheckboxLabeled("Allow Animals To Dig Burrows", ref Settings.allowNaturalBurrows, "Burrowing prey can establish a small persistent home hole when no reachable refuge exists.");
            listing.Label("Maximum inferred hiding body size: " + Settings.maximumInferredHidingBodySize.ToString("0.00"));
            Settings.maximumInferredHidingBodySize = listing.Slider(Settings.maximumInferredHidingBodySize, 0.1f, 1f);
            listing.Label("Default concealment success: " + Settings.defaultHideSuccessChance.ToStringPercent());
            Settings.defaultHideSuccessChance = listing.Slider(Settings.defaultHideSuccessChance, 0.05f, 1f);
            listing.Label("Time to enter refuge: " + Settings.hideEntryTicks.ToStringTicksToPeriod());
            Settings.hideEntryTicks = Mathf.RoundToInt(listing.Slider(Settings.hideEntryTicks, 30f, 600f));
            listing.Label("Minimum hiding time: " + Settings.minimumHideTicks.ToStringTicksToPeriod());
            Settings.minimumHideTicks = Mathf.RoundToInt(listing.Slider(Settings.minimumHideTicks, 120f, 2500f));
            listing.Label("Maximum hiding time: " + Settings.maximumHideTicks.ToStringTicksToPeriod());
            Settings.maximumHideTicks = Mathf.RoundToInt(listing.Slider(Settings.maximumHideTicks, 900f, 15000f));
            listing.Gap();
            listing.Label("Predator Hunts");
            listing.GapLine();
            listing.CheckboxLabeled("Allow Prey To Escape Hunts", ref Settings.enablePredatorEscapeChance, "Adds bounded pursuit escape checks when Packs and Predators is loaded.");
            listing.Label("Base escape chance per check: " + Settings.basePredatorEscapeChance.ToStringPercent());
            Settings.basePredatorEscapeChance = listing.Slider(Settings.basePredatorEscapeChance, 0f, 0.5f);
            listing.Label("Escape check interval: " + Settings.predatorEscapeCheckIntervalTicks.ToStringTicksToPeriod());
            Settings.predatorEscapeCheckIntervalTicks = Mathf.RoundToInt(listing.Slider(Settings.predatorEscapeCheckIntervalTicks, 120f, 900f));
            listing.Gap();
            listing.Label("Prey eligibility and behavior are inferred from vanilla race data and can be overridden by XML prey profiles.");
            listing.End();
        }

        private static void DrawPlayerSettings(Rect rect)
        {
            Rect view = new Rect(0f, 0f, rect.width - 18f, 1120f);
            Widgets.BeginScrollView(rect, ref playerSettingsScroll, view);
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(view);
            Text.Font = GameFont.Medium;
            listing.Label("Player and Wildlife Interaction");
            Text.Font = GameFont.Small;
            listing.GapLine();
            listing.CheckboxLabeled("Wildlife Knowledge Panel", ref Settings.enableWildlifeKnowledge);
            listing.CheckboxLabeled("Require Observation For Details", ref Settings.requireObservationForDetails);
            listing.CheckboxLabeled("Observation Posts / Hunting Blinds", ref Settings.enableObservationPosts);
            listing.CheckboxLabeled("Wildlife Bait", ref Settings.enableWildlifeBait);
            listing.CheckboxLabeled("Predator Deterrents", ref Settings.enablePredatorDeterrents);
            listing.CheckboxLabeled("Wildlife Reserves", ref Settings.enableWildlifeReserves);
            listing.CheckboxLabeled("Ecological Consequences", ref Settings.enableEcologicalConsequences);
            listing.CheckboxLabeled("Wildlife Shapes The Landscape",
                ref Settings.enableWildlifeLandscaping,
                "Repeated compatible activity forms persistent trails, feeding grounds, nesting sites, wallows, shoreline works, and territorial landmarks. Disabled: no scans, formation, decay, drawing, or interaction processing occurs.");
            if (Settings.enableWildlifeLandscaping)
            {
                listing.CheckboxLabeled("Landscape Gameplay Effects",
                    ref Settings.enableLandscapeEffects,
                    "Allow established features to influence animal movement, habitat quality, migration, and fieldcraft. Disabling this preserves existing features as visual history only.");
                listing.CheckboxLabeled("Wildlife Crossroads",
                    ref Settings.enableLandscapeCrossroads,
                    "Reveal promising animal-shaped places before they form. Colonists can quietly observe, steward the site, or promise to leave it wild. Disabled: no markers, notices, jobs, or choice processing occurs.");
            }
            listing.CheckboxLabeled("Wildlife Alerts", ref Settings.enableWildlifeAlerts);
            listing.Gap();
            listing.Label("Fieldcraft");
            listing.GapLine();
            listing.CheckboxLabeled("Fading Tracks And Wildlife Signs", ref Settings.enableTrackingSigns);
            listing.CheckboxLabeled("Interactive Trail Reading", ref Settings.enableTrailReading,
                "Studied wildlife signs become uncertain, visible trails that colonists can follow. Disabled: no trail records, drawing, or trail jobs are processed.");
            listing.CheckboxLabeled("Wind And Scent HUD", ref Settings.enableWindHud);
            listing.CheckboxLabeled("Scent Masking Station", ref Settings.enableScentMasking);
            listing.CheckboxLabeled("Animal Calls From Observation Posts", ref Settings.enableAnimalCalls);
            listing.CheckboxLabeled("Local Wildlife Signal Cultures", ref Settings.enableWildlifeSignalCulture,
                "Animal populations develop persistent local dialects for danger, safety, resources, and coordination. Disabled: no ticking, drawing, or signal processing occurs.");
            if (Settings.enableWildlifeSignalCulture)
            {
                listing.CheckboxLabeled("Show Identified Signal Meanings", ref Settings.showIdentifiedSignalText,
                    "Briefly shows plain-language text beside an animal when it gives a call whose exact meaning the colony has learned. Disabled: no label is created and there is no ongoing processing.");
                listing.CheckboxLabeled("Colonists May Imitate Wildlife Signals", ref Settings.enablePlayerSignalImitation,
                    "Manned observation posts can reproduce learned contact, alarm, and all-clear calls. Misleading calls reduce animal trust.");
                listing.CheckboxLabeled("Predators Learn Prey Signals", ref Settings.enablePredatorSignalLearning,
                    "Predators gradually learn local prey alarms and become slightly better at anticipating group reactions.");
            }
            listing.CheckboxLabeled("Manned Hunting Blinds", ref Settings.enableMannedBlinds);
            listing.CheckboxLabeled("Ranch Guardian Assignments", ref Settings.enableRanchGuardians);
            listing.CheckboxLabeled("Guardians May Confront Predators", ref Settings.guardiansAttackPredators);
            listing.CheckboxLabeled("Wild Prey Avoid Colonists", ref Settings.preyAvoidColonists, "Wild prey avoid nearby colonists, with a larger detection radius for drafted or openly hunting pawns.");
            listing.CheckboxLabeled("Coordinated Wildlife Hunts", ref Settings.enableHuntingExpeditions);
            listing.CheckboxLabeled("Uncertain Predator Warnings", ref Settings.enableUncertainPredatorWarnings);
            listing.CheckboxLabeled("Wildlife Steward Controls", ref Settings.enableWildlifeSteward);
            listing.CheckboxLabeled("Guardian Patrol Areas", ref Settings.enableGuardianPatrolAreas);
            listing.Label("Minimum combined hunting skill: " + Settings.minimumFieldcraftSkill);
            Settings.minimumFieldcraftSkill = Mathf.RoundToInt(listing.Slider(Settings.minimumFieldcraftSkill, 0f, 12f));
            listing.CheckboxLabeled("Per-Colonist Species Knowledge", ref Settings.enableSpeciesKnowledgeProgression);
            listing.Gap();
            listing.Label("Animal Lives");
            listing.GapLine();
            listing.CheckboxLabeled("Animal Memory", ref Settings.enableAnimalMemory,
                "Animals remember meaningful encounters with colonists and display them in their Memory tab.");
            if (Settings.enableAnimalMemory)
                listing.CheckboxLabeled("Animal-To-Animal Social Memory", ref Settings.enableAnimalSocialMemory,
                    "Animals remember mates, parents, teachers, protectors, companions, reunions, rivals, and fights. Disabled: no social memory records, drawing, or behavioral influence is processed.");
            if (Settings.enableAnimalMemory)
            {
                listing.Label("Frightened memory duration: " + Settings.frightenedMemoryLifetimeTicks.ToStringTicksToPeriod());
                Settings.frightenedMemoryLifetimeTicks = Mathf.RoundToInt(listing.Slider(Settings.frightenedMemoryLifetimeTicks, 60000f, 3600000f));
                listing.Label("Pack member death memory duration: " + Settings.packMemberKilledMemoryLifetimeTicks.ToStringTicksToPeriod());
                Settings.packMemberKilledMemoryLifetimeTicks = Mathf.RoundToInt(listing.Slider(Settings.packMemberKilledMemoryLifetimeTicks, 600000f, 7200000f));
            }
            listing.Gap();
            listing.CheckboxLabeled("Weapon-Aware Tactics", ref Settings.enableWeaponAwareTactics);
            listing.CheckboxLabeled("Wounded Tracking And Hunter Retreat", ref Settings.enableWoundedTrackingAndRetreat);
            listing.CheckboxLabeled("Blood-Trail Hunt Tracking", ref Settings.enableHuntTracking, "Active coordinated hunts follow event-driven blood trail points. Rain and time reduce trail retention.");
            listing.CheckboxLabeled("Hunted Prey Adrenaline", ref Settings.enableHuntedAdrenaline, "Prey wounded during a coordinated hunt temporarily gain movement speed.");
            listing.CheckboxLabeled("Finite Pursuit Endurance", ref Settings.enableHuntEndurance, "Hunters stop prolonged chases, become briefly fatigued, and return to the Hunting Spot or home.");
            listing.CheckboxLabeled("Adaptive Prey Responses", ref Settings.enableAdaptivePreyResponses);
            listing.CheckboxLabeled("Fieldcraft Equipment And Snares", ref Settings.enableFieldcraftEquipment);
            listing.Label("Hidden prey safe emergence distance: " + Settings.hiddenPreySafeDistance.ToString("0") + " cells");
            Settings.hiddenPreySafeDistance = listing.Slider(Settings.hiddenPreySafeDistance, 20f, 80f);
            listing.Gap();
            listing.Label("Disabled structures remain in existing saves but stop influencing wildlife.");
            listing.End();
            Widgets.EndScrollView();
        }

        public override void WriteSettings()
        {
            WildlifeProgression.RefreshDefGates();
            PreyProfileDatabase.Clear();
            WildlifeNicheDatabase.Clear();
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
                if (def.race?.Animal == true) HerdsStartup.RefreshAnimalTabs(def);
            if (Current.Game?.Maps != null)
                for (int i = 0; i < Current.Game.Maps.Count; i++) Current.Game.Maps[i].GetComponent<HerdMapComponent>()?.ForceRefresh();
            base.WriteSettings();
        }
    }
}
