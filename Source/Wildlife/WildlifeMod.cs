using Herds;
using Packs;
using RimWorld;
using UnityEngine;
using Verse;

namespace Wildlife
{
    public sealed class WildlifeMod : Mod
    {
        private static int page;
        private static Vector2 scroll;
        private static readonly string[] Pages = { "Systems", "Progression", "Prey", "Predators", "Hunting", "Knowledge & Tools", "Species" };

        public WildlifeMod(ModContentPack content) : base(content) { }
        public override string SettingsCategory() => "Wildlife";

        public override void DoSettingsWindowContents(Rect inRect)
        {
            float buttonWidth = (inRect.width - 40f) / Pages.Length;
            for (int i = 0; i < Pages.Length; i++)
                if (Widgets.ButtonText(new Rect(inRect.x + i * (buttonWidth + 8f), inRect.y, buttonWidth, 34f), Pages[i])) page = i;
            Rect body = new Rect(inRect.x, inRect.y + 46f, inRect.width, inRect.height - 46f);
            if (page == 6) { PacksMod.DrawAnimals(body); return; }
            Rect view = new Rect(0f, 0f, body.width - 18f, page == 5 ? 1900f : 900f);
            Widgets.BeginScrollView(body, ref scroll, view);
            Listing_Standard listing = new Listing_Standard(); listing.Begin(view);
            if (HerdsMod.Settings == null || PacksMod.Settings == null) listing.Label("Wildlife settings are initializing. Reopen this window.");
            else if (page == 0) DrawSystems(listing);
            else if (page == 1) DrawProgression(listing);
            else if (page == 2) DrawPrey(listing);
            else if (page == 3) DrawPredators(listing);
            else if (page == 4) DrawHunting(listing);
            else DrawKnowledgeAndTools(listing);
            listing.End(); Widgets.EndScrollView();
        }

        private static void Header(Listing_Standard listing, string title, string description)
        {
            Text.Font = GameFont.Medium; listing.Label(title); Text.Font = GameFont.Small; listing.Label(description); listing.GapLine();
        }

        private static void Subheader(Listing_Standard listing, string title)
        {
            listing.Gap();
            Text.Font = GameFont.Medium; listing.Label(title); Text.Font = GameFont.Small;
            listing.GapLine();
        }

        private static void LinkedCheckbox(Listing_Standard listing, string label, bool first, bool second, System.Action<bool> set)
        {
            bool value = first && second;
            listing.CheckboxLabeled(label, ref value);
            set(value);
        }

        private static void DrawSystems(Listing_Standard listing)
        {
            Header(listing, "Wildlife Systems", "Master switches immediately gate their simulation paths. Individual preferences remain saved while a system is disabled.");
            listing.Label("Presets");
            Rect presets = listing.GetRect(34f);
            float presetWidth = (presets.width - 16f) / 3f;
            if (Widgets.ButtonText(new Rect(presets.x, presets.y, presetWidth, 32f), "Balanced")) ApplyPreset(0);
            if (Widgets.ButtonText(new Rect(presets.x + presetWidth + 8f, presets.y, presetWidth, 32f), "Performance")) ApplyPreset(1);
            if (Widgets.ButtonText(new Rect(presets.x + (presetWidth + 8f) * 2f, presets.y, presetWidth, 32f), "Full Ecology")) ApplyPreset(2);
            TooltipHandler.TipRegion(presets, "Presets change feature switches and refresh intervals. Individual settings can still be adjusted afterward.");
            listing.Gap();
            listing.CheckboxLabeled("Prey, Herds, Homes, and Hiding", ref HerdsMod.Settings.enablePreyAndHerds, "Master switch for prey simulation and its AI changes.");
            listing.CheckboxLabeled("Predators", ref PacksMod.Settings.enablePredators, "Turns off predator simulation, hunting styles, territories, and dens.");
            bool packsEnabled = GUI.enabled; GUI.enabled = packsEnabled && PacksMod.Settings.enablePredators;
            listing.CheckboxLabeled("Packs and Cooperative Predator Behavior", ref PacksMod.Settings.enablePacks, "When disabled, enabled predators use solitary behavior without cooperative packs.");
            GUI.enabled = packsEnabled;
            listing.CheckboxLabeled("Player Hunting Changes", ref HerdsMod.Settings.enableHuntingChanges, "Controls Form Hunt, coordinated assaults, tracking, adrenaline, and pursuit endurance.");
            listing.Gap(); listing.Label("Performance"); listing.GapLine();
            listing.Label("Prey refresh interval: " + HerdsMod.Settings.updateIntervalTicks + " ticks");
            HerdsMod.Settings.updateIntervalTicks = Mathf.RoundToInt(listing.Slider(HerdsMod.Settings.updateIntervalTicks, 120f, 2000f));
            listing.Label("Predator refresh interval: " + PacksMod.Settings.updateIntervalTicks + " ticks");
            PacksMod.Settings.updateIntervalTicks = Mathf.RoundToInt(listing.Slider(PacksMod.Settings.updateIntervalTicks, 120f, 2000f));
            listing.Label("Disabled master systems return before their map simulation work.");
        }

        private static void DrawProgression(Listing_Standard listing)
        {
            Header(listing, "Long-Term Wildlife Progression", "Natural animal behavior always remains active. Research unlocks player knowledge, organization, management, precision, and automation.");
            bool oldMaster = HerdsMod.Settings.enableResearchProgression;
            bool oldHunting = HerdsMod.Settings.gateHuntingByResearch;
            bool oldKnowledge = HerdsMod.Settings.gateKnowledgeByResearch;
            bool oldStewardship = HerdsMod.Settings.gateStewardshipByResearch;
            bool oldIndustrial = HerdsMod.Settings.gateIndustrialEcologyByResearch;
            listing.CheckboxLabeled("Research-Gated Wildlife Progression", ref HerdsMod.Settings.enableResearchProgression, "Disable to make all enabled Wildlife features immediately available.");
            bool old = GUI.enabled;
            GUI.enabled = old && HerdsMod.Settings.enableResearchProgression;
            listing.CheckboxLabeled("Gate Hunting and Organized Hunting", ref HerdsMod.Settings.gateHuntingByResearch);
            listing.CheckboxLabeled("Gate Knowledge, Calls, and Observation Improvements", ref HerdsMod.Settings.gateKnowledgeByResearch);
            listing.CheckboxLabeled("Gate Stewardship and Conservation Management", ref HerdsMod.Settings.gateStewardshipByResearch);
            listing.CheckboxLabeled("Gate Industrial Monitoring and Applied Ecology", ref HerdsMod.Settings.gateIndustrialEcologyByResearch);
            GUI.enabled = old;
            listing.Gap();
            listing.Label("Animal: basic hunting, calls, ranch roles, oral knowledge, managed fire, and habitat support.");
            listing.Label("Neolithic: Organized Hunting, Wildlife Stewardship, coordinated hunts, habitat restoration, reserves, and regulations.");
            listing.Label("Industrial: camera traps, telemetry, disease-risk assessment, migration alerts, and ecological forecasting.");
            listing.Gap();
            if (listing.ButtonText("View Wildlife Progression Status")) Find.WindowStack.Add(new Window_WildlifeProgression());
            if (oldMaster != HerdsMod.Settings.enableResearchProgression || oldHunting != HerdsMod.Settings.gateHuntingByResearch ||
                oldKnowledge != HerdsMod.Settings.gateKnowledgeByResearch || oldStewardship != HerdsMod.Settings.gateStewardshipByResearch ||
                oldIndustrial != HerdsMod.Settings.gateIndustrialEcologyByResearch) WildlifeProgression.RefreshDefGates();
        }

        private static void DrawPrey(Listing_Standard listing)
        {
            Header(listing, "Prey, Herds, and Survival", "Configure social movement, threat responses, homes, and concealment.");
            bool old = GUI.enabled; GUI.enabled = old && HerdsMod.Settings.enablePreyAndHerds;
            Subheader(listing, "Social Life");
            listing.CheckboxLabeled("Coordinate Wild Herds", ref HerdsMod.Settings.coordinateWildHerds);
            listing.CheckboxLabeled("Coordinated Herd Defense", ref HerdsMod.Settings.enableDefensiveBehavior);
            Subheader(listing, "Homes and Survival");
            listing.CheckboxLabeled("Prey Hiding", ref HerdsMod.Settings.enableHiding);
            listing.CheckboxLabeled("Natural Burrows", ref HerdsMod.Settings.allowNaturalBurrows);
            listing.CheckboxLabeled("Prey May Escape Predator Hunts", ref HerdsMod.Settings.enablePredatorEscapeChance);
            listing.CheckboxLabeled("Wild Prey Avoid Colonists", ref HerdsMod.Settings.preyAvoidColonists);
            listing.CheckboxLabeled("Adaptive Prey Responses", ref HerdsMod.Settings.enableAdaptivePreyResponses);
            listing.Label("Flight distance: " + HerdsMod.Settings.flightDistance.ToString("0") + " cells"); HerdsMod.Settings.flightDistance = listing.Slider(HerdsMod.Settings.flightDistance, 8f, 36f);
            listing.Label("Minimum hiding time: " + HerdsMod.Settings.minimumHideTicks.ToStringTicksToPeriod()); HerdsMod.Settings.minimumHideTicks = Mathf.RoundToInt(listing.Slider(HerdsMod.Settings.minimumHideTicks, 120f, 2500f));
            listing.Label("Hidden prey safe distance: " + HerdsMod.Settings.hiddenPreySafeDistance.ToString("0") + " cells"); HerdsMod.Settings.hiddenPreySafeDistance = listing.Slider(HerdsMod.Settings.hiddenPreySafeDistance, 20f, 80f);
            listing.Gap(); if (listing.ButtonText("Species Behavior Profiles")) Find.WindowStack.Add(new Window_SpeciesBehaviorProfiles());
            GUI.enabled = old;
        }

        private static void DrawPredators(Listing_Standard listing)
        {
            Header(listing, "Predators, Packs, and Territory", "Configure predator ecology and interactions. Detailed per-species behavior is on the Species page.");
            bool old = GUI.enabled; GUI.enabled = old && PacksMod.Settings.enablePredators;
            listing.CheckboxLabeled("Cooperative Packs", ref PacksMod.Settings.enablePacks);
            listing.CheckboxLabeled("Predators May Hunt Colonists", ref PacksMod.Settings.predatorsAttackColonists, "Makes the map substantially more dangerous.");
            listing.CheckboxLabeled("Predator Boldness Learning", ref PacksMod.Settings.enablePredatorBoldness);
            Subheader(listing, "Player Influences");
            listing.CheckboxLabeled("Bait Influences Hunts", ref PacksMod.Settings.enableBaitInfluence);
            listing.CheckboxLabeled("Deterrents Influence Predators", ref PacksMod.Settings.enableDeterrentInfluence);
            listing.CheckboxLabeled("Wildlife Reserves Influence Predators", ref PacksMod.Settings.enableReserveInfluence);
            listing.CheckboxLabeled("Ranch Guardians Influence Predator Risk", ref PacksMod.Settings.enableGuardianInfluence);
            listing.CheckboxLabeled("Uncertain Predator Warnings", ref PacksMod.Settings.enableUncertainWarnings);
            GUI.enabled = old;
        }

        private static void DrawHunting(Listing_Standard listing)
        {
            Header(listing, "Player Hunting", "Configure coordinated field hunts, tracking, equipment, and limits.");
            bool old = GUI.enabled; GUI.enabled = old && HerdsMod.Settings.enableHuntingChanges;
            Subheader(listing, "Planning and Tactics");
            listing.CheckboxLabeled("Coordinated Wildlife Hunts", ref HerdsMod.Settings.enableHuntingExpeditions);
            bool expeditionsWereEnabled = HerdsMod.Settings.enableOffMapHuntingExpeditions;
            listing.CheckboxLabeled("Off-Map Wildlife Expeditions", ref HerdsMod.Settings.enableOffMapHuntingExpeditions, "Allow Hunting Spots to send supplied parties to valid world tiles.");
            if (expeditionsWereEnabled && !HerdsMod.Settings.enableOffMapHuntingExpeditions)
                foreach (Map map in Find.Maps)
                    map.GetComponent<HuntingExpeditionMapComponent>()?.CancelAll("Recalled because off-map expeditions were disabled.");
            bool expeditionOptions = GUI.enabled;
            GUI.enabled = expeditionOptions && HerdsMod.Settings.enableOffMapHuntingExpeditions;
            listing.CheckboxLabeled("Extended Expedition Ecology", ref HerdsMod.Settings.enableExtendedHuntingExpeditions, "Enable distant field discoveries and long-term wildlife population changes in surveyed tiles.");
            listing.CheckboxLabeled("Expedition Incidents and Injuries", ref HerdsMod.Settings.enableExpeditionIncidents);
            listing.CheckboxLabeled("Biome-Specific Expedition Events", ref HerdsMod.Settings.enableExpeditionBiomeEvents, "Adds biome-driven opportunities, hazards, delays, and field reports.");
            listing.CheckboxLabeled("Interactive Expedition Encounters", ref HerdsMod.Settings.enableInteractiveExpeditionEncounters, "Pauses at notable field situations and lets the player choose the response.");
            listing.CheckboxLabeled("Expedition Deaths", ref HerdsMod.Settings.allowExpeditionDeaths, "Allows rare lethal outcomes on bold, poorly supplied expeditions.");
            GUI.enabled = expeditionOptions;
            listing.CheckboxLabeled("Weapon-Aware Tactics", ref HerdsMod.Settings.enableWeaponAwareTactics);
            listing.CheckboxLabeled("Wounded Tracking and Retreat", ref HerdsMod.Settings.enableWoundedTrackingAndRetreat);
            Subheader(listing, "Tracking and Pursuit");
            listing.CheckboxLabeled("Blood-Trail Tracking", ref HerdsMod.Settings.enableHuntTracking);
            listing.CheckboxLabeled("Hunted Prey Adrenaline", ref HerdsMod.Settings.enableHuntedAdrenaline);
            listing.CheckboxLabeled("Finite Pursuit Endurance", ref HerdsMod.Settings.enableHuntEndurance);
            listing.CheckboxLabeled("Fieldcraft Equipment and Snares", ref HerdsMod.Settings.enableFieldcraftEquipment);
            listing.Label("Minimum combined hunting Skill: " + HerdsMod.Settings.minimumFieldcraftSkill); HerdsMod.Settings.minimumFieldcraftSkill = Mathf.RoundToInt(listing.Slider(HerdsMod.Settings.minimumFieldcraftSkill, 0f, 12f));
            GUI.enabled = old;
        }

        private static void DrawKnowledgeAndTools(Listing_Standard listing)
        {
            Header(listing, "Animal Knowledge and Player Tools", "Configure observation, fieldcraft structures, ranch protection, alerts, and ecological effects.");
            Subheader(listing, "Player Experience");
            listing.CheckboxLabeled("Contextual Wildlife Onboarding", ref HerdsMod.Settings.enablePlayerOnboarding, "Shows one introductory letter and contextual guidance in the Wildlife Overview.");
            listing.CheckboxLabeled("Wildlife Unlock Letters", ref HerdsMod.Settings.enableUnlockLetters, "Explains newly available Wildlife capabilities when their research is completed.");
            listing.CheckboxLabeled("Recent Wildlife Outcomes", ref HerdsMod.Settings.enableOutcomeHistory, "Keeps a small event-driven history of hunts, calls, observations, and migration. No polling is performed.");
            listing.CheckboxLabeled("Wildlife Field Journal", ref HerdsMod.Settings.enableFieldJournal, "Tracks species discoveries and grants small permanent colony fieldcraft bonuses for completed entries.");
            listing.CheckboxLabeled("Living Wildlife Mysteries", ref HerdsMod.Settings.enableWildlifeMysteries,
                "Turns unusual simulated wildlife patterns into evidence-based investigations with consequential resolutions.");
            listing.CheckboxLabeled("Wildlife Moments", ref HerdsMod.Settings.enableDynamicWildlifeOpportunities,
                "Turns real hunts, signals, relationships, lookout changes, homes, injuries, and young into optional player-facing moments with Observe, Track, Protect, Hunt, or Ignore responses.");
            listing.CheckboxLabeled("Wildlife Steward Projects", ref HerdsMod.Settings.enableStewardProjects, "Allows long-term restoration, corridor, population-control, and ranch-defense objectives.");
            listing.CheckboxLabeled("Special Hunt Rewards", ref HerdsMod.Settings.enableHuntRewards, "Allows skilled hunts to recover quality hides, trophies, specimens, and useful field discoveries.");
            Subheader(listing, "Knowledge and Observation");
            LinkedCheckbox(listing, "Animal Knowledge", HerdsMod.Settings.enableWildlifeKnowledge, PacksMod.Settings.enableWildlifeKnowledge,
                value => { HerdsMod.Settings.enableWildlifeKnowledge = value; PacksMod.Settings.enableWildlifeKnowledge = value; });
            LinkedCheckbox(listing, "Require Observation for Wildlife Details", HerdsMod.Settings.requireObservationForDetails, PacksMod.Settings.requireObservationForDetails,
                value => { HerdsMod.Settings.requireObservationForDetails = value; PacksMod.Settings.requireObservationForDetails = value; });
            listing.CheckboxLabeled("Per-Colonist Animal Knowledge Progression", ref HerdsMod.Settings.enableSpeciesKnowledgeProgression);
            listing.CheckboxLabeled("Observation Posts", ref HerdsMod.Settings.enableObservationPosts);
            listing.CheckboxLabeled("Manned Observation Posts", ref HerdsMod.Settings.enableMannedBlinds);
            listing.CheckboxLabeled("Animal Calls", ref HerdsMod.Settings.enableAnimalCalls);
            listing.CheckboxLabeled("Fading Tracks and Wildlife Signs", ref HerdsMod.Settings.enableTrackingSigns);
            listing.CheckboxLabeled("Territorial Signs", ref HerdsMod.Settings.enableTerritorialSigns);
            listing.CheckboxLabeled("Wind and Scent HUD", ref HerdsMod.Settings.enableWindHud);
            listing.CheckboxLabeled("Scent Masking", ref HerdsMod.Settings.enableScentMasking);
            Subheader(listing, "Field Tools");
            listing.CheckboxLabeled("Wildlife Bait", ref HerdsMod.Settings.enableWildlifeBait);
            listing.CheckboxLabeled("Predator Deterrents", ref HerdsMod.Settings.enablePredatorDeterrents);
            listing.CheckboxLabeled("Wildlife Reserves", ref HerdsMod.Settings.enableWildlifeReserves);
            listing.CheckboxLabeled("Ranch Guardians", ref HerdsMod.Settings.enableRanchGuardians);
            listing.CheckboxLabeled("Guardians May Confront Predators", ref HerdsMod.Settings.guardiansAttackPredators);
            listing.CheckboxLabeled("Guardian Patrol Areas", ref HerdsMod.Settings.enableGuardianPatrolAreas);
            listing.CheckboxLabeled("Domestic Predator Roles", ref HerdsMod.Settings.enableDomesticPredatorRoles);
            Subheader(listing, "Living Ecology");
            listing.CheckboxLabeled("Scavenger Behavior", ref HerdsMod.Settings.enableScavenging);
            LinkedCheckbox(listing, "Juveniles Learn from Adults", HerdsMod.Settings.enableJuvenileLearning, PacksMod.Settings.enableJuvenileLearning,
                value => { HerdsMod.Settings.enableJuvenileLearning = value; PacksMod.Settings.enableJuvenileLearning = value; });
            LinkedCheckbox(listing, "Habitat Quality Affects Wildlife", HerdsMod.Settings.enableHabitatEcology, PacksMod.Settings.enableHabitatEcology,
                value => { HerdsMod.Settings.enableHabitatEcology = value; PacksMod.Settings.enableHabitatEcology = value; });
            listing.CheckboxLabeled("Regional Wildlife Populations", ref HerdsMod.Settings.enableRegionalPopulations);
            bool migrationEnabled = GUI.enabled; GUI.enabled = migrationEnabled && HerdsMod.Settings.enableRegionalPopulations;
            listing.CheckboxLabeled("Regional Immigration and Emigration", ref HerdsMod.Settings.enableRegionalMigration);
            listing.CheckboxLabeled("Persistent Roaming Animals", ref HerdsMod.Settings.enablePersistentRoamingAnimals,
                "Notable, tagged, named, or remembered wild animals remain persistent while roaming nearby and can return later.");
            GUI.enabled = migrationEnabled;
            listing.CheckboxLabeled("Spatial Regional Wildlife Map", ref HerdsMod.Settings.enableRegionalMap);
            bool roamingEnabled = GUI.enabled;
            GUI.enabled = roamingEnabled && HerdsMod.Settings.enablePersistentRoamingAnimals;
            listing.CheckboxLabeled("Roaming Expedition Encounters", ref HerdsMod.Settings.enableRoamingExpeditionEncounters);
            listing.CheckboxLabeled("Advance Return Signs", ref HerdsMod.Settings.enableReturnSigns);
            listing.CheckboxLabeled("Visible Migration Waves", ref HerdsMod.Settings.enableVisibleMigrationWaves);
            listing.CheckboxLabeled("Territory History", ref HerdsMod.Settings.enableTerritoryHistory);
            listing.CheckboxLabeled("Persistent Family Lines", ref HerdsMod.Settings.enablePersistentFamilyLines);
            GUI.enabled = roamingEnabled;
            listing.CheckboxLabeled("Wildlife Management Goals", ref HerdsMod.Settings.enableWildlifeManagementGoals);
            listing.CheckboxLabeled("Conservation Structures and Actions", ref HerdsMod.Settings.enableConservationActions);
            listing.CheckboxLabeled("Population Consequences", ref HerdsMod.Settings.enablePopulationConsequences);
            listing.CheckboxLabeled("Wildlife Events", ref HerdsMod.Settings.enableWildlifeEvents);
            bool wildlifeEvents = GUI.enabled; GUI.enabled = wildlifeEvents && HerdsMod.Settings.enableWildlifeEvents;
            listing.CheckboxLabeled("Seasonal Ecological Events", ref HerdsMod.Settings.enableSeasonalEcologyEvents, "Creates breeding, scarcity, migration, and predator-pressure events when seasons change.");
            GUI.enabled = wildlifeEvents;
            listing.CheckboxLabeled("Notable Animals", ref HerdsMod.Settings.enableNotableAnimals, "Allows rare persistent wild animals with names, histories, and exceptional abilities.");
            listing.CheckboxLabeled("Hunting Regulations", ref HerdsMod.Settings.enableHuntingRegulations);
            listing.CheckboxLabeled("Persistent Animal Relationships", ref HerdsMod.Settings.enableAnimalRelationships);
            listing.CheckboxLabeled("Animal Personalities", ref HerdsMod.Settings.enableAnimalPersonalities,
                "Gives animals persistent behavioral tendencies that affect vigilance, memory, trust, and avoidance.");
            bool personalities = GUI.enabled; GUI.enabled = personalities && HerdsMod.Settings.enableAnimalPersonalities;
            listing.CheckboxLabeled("Inherited Personalities", ref HerdsMod.Settings.enablePersonalityInheritance,
                "Young animals tend to inherit a parent's temperament, with occasional variation.");
            GUI.enabled = personalities;
            bool lifeEvents = GUI.enabled; GUI.enabled = lifeEvents && HerdsMod.Settings.enableWildlifeEvents;
            listing.CheckboxLabeled("Wildlife Life Incidents", ref HerdsMod.Settings.enableWildlifeLifeIncidents,
                "Creates injured animals seeking help, displaced flocks, orphaned young, crop raids, and territorial disputes.");
            GUI.enabled = lifeEvents;
            listing.CheckboxLabeled("Individual Animal Memory", ref HerdsMod.Settings.enableAnimalMemory,
                "Wild animals remember colonists who harm, study, call, or tend them. Memory changes how closely they tolerate that person.");
            listing.CheckboxLabeled("Animal Traditions", ref HerdsMod.Settings.enableAnimalTraditions,
                "Animals pass beliefs about the colony through families and social groups. Traditions can spread, mutate, and become inaccurate.");
            listing.CheckboxLabeled("Colony as a Wildlife Landmark", ref HerdsMod.Settings.enableColonyWildlifeLandmark,
                "Each species develops a persistent reputation for the colony from structures, hunting, care, danger, and animal traditions.");
            listing.CheckboxLabeled("Wildlife Folklore", ref HerdsMod.Settings.enableWildlifeFolklore,
                "Important wildlife encounters become persistent colony stories in the Field Journal.");
            listing.CheckboxLabeled("Wildlife Ideology", ref HerdsMod.Settings.enableWildlifeIdeology,
                "Ideology precepts influence reactions to stewardship, field study, hunting, and notable animals.");
            listing.CheckboxLabeled("Folklore Retelling", ref HerdsMod.Settings.enableFolkloreRetelling,
                "Colonists retell recorded wildlife stories during recreation, sharing knowledge and social experience.");
            listing.CheckboxLabeled("Culturally Significant Animals", ref HerdsMod.Settings.enableCulturalAnimals,
                "Notable animals can become feared, beloved, sacred, or legendary.");
            listing.CheckboxLabeled("Wildlife Ceremonies", ref HerdsMod.Settings.enableWildlifeCeremonies,
                "Enables ideology-sensitive wildlife gatherings from the Field Journal.");
            listing.CheckboxLabeled("Folklore Displays", ref HerdsMod.Settings.enableFolkloreDisplays,
                "Enables physical story cairns that can be dedicated to recorded folklore.");
            listing.CheckboxLabeled("Legends Spread", ref HerdsMod.Settings.enableLegendSpread,
                "Frequently retold stories spread through visitors, other colonies, and new generations.");
            listing.CheckboxLabeled("Legend Challenges", ref HerdsMod.Settings.enableLegendQuests,
                "Wildlife legends can create time-limited study, protection, tracking, or hunting objectives.");
            listing.CheckboxLabeled("Physical Storytelling and Ceremonies", ref HerdsMod.Settings.enablePhysicalWildlifeStories,
                "Colonists physically gather to tell wildlife stories and hold ceremonies.");
            listing.CheckboxLabeled("Legendary Animal Presentation", ref HerdsMod.Settings.enableLegendaryPresentation,
                "Legendary animals leave distinctive signs and occasionally announce their presence.");
            listing.CheckboxLabeled("Wildlife Learning and Inspirations", ref HerdsMod.Settings.enableWildlifeLearning,
                "Wildlife experience can shape children, passions, traits, and inspirations.");
            listing.CheckboxLabeled("Wildlife Ideology Roles", ref HerdsMod.Settings.enableWildlifeIdeologyRoles,
                "Enables bonuses from the Master Hunter and Master Conservationist roles.");
            listing.CheckboxLabeled("Advanced Scavenging", ref HerdsMod.Settings.enableAdvancedScavenging);
            listing.CheckboxLabeled("Domestic Role Progression", ref HerdsMod.Settings.enableDomesticRoleProgression);
            Subheader(listing, "Industrial Wildlife Science");
            listing.CheckboxLabeled("Automated Camera Traps", ref HerdsMod.Settings.enableCameraTraps);
            listing.CheckboxLabeled("Tracking Collars and Telemetry", ref HerdsMod.Settings.enableTelemetry);
            listing.CheckboxLabeled("Wildlife Disease-Risk Assessment", ref HerdsMod.Settings.enableDiseaseMonitoring);
            listing.CheckboxLabeled("Applied Ecology Forecasts", ref HerdsMod.Settings.enableAppliedEcology);
            Subheader(listing, "Stewardship and Alerts");
            listing.CheckboxLabeled("Wildlife Steward", ref HerdsMod.Settings.enableWildlifeSteward);
            LinkedCheckbox(listing, "Ecological Consequences", HerdsMod.Settings.enableEcologicalConsequences, PacksMod.Settings.enableEcologicalConsequences,
                value => { HerdsMod.Settings.enableEcologicalConsequences = value; PacksMod.Settings.enableEcologicalConsequences = value; });
            LinkedCheckbox(listing, "Wildlife Alerts", HerdsMod.Settings.enableWildlifeAlerts, PacksMod.Settings.enableWildlifeAlerts,
                value => { HerdsMod.Settings.enableWildlifeAlerts = value; PacksMod.Settings.enableWildlifeAlerts = value; });
        }

        private static void ApplyPreset(int preset)
        {
            HerdsSettings h = HerdsMod.Settings;
            PacksSettings p = PacksMod.Settings;
            h.enablePreyAndHerds = p.enablePredators = true;
            h.enableHuntingChanges = h.enableHuntingExpeditions = true;
            h.enableOffMapHuntingExpeditions = true;
            h.enableExtendedHuntingExpeditions = preset != 1;
            h.enableExpeditionIncidents = preset != 1;
            h.enableExpeditionBiomeEvents = preset != 1;
            h.enableInteractiveExpeditionEncounters = preset != 1;
            h.enableNotableAnimals = preset != 1;
            h.enableSeasonalEcologyEvents = preset != 1;
            h.allowExpeditionDeaths = false;
            h.enablePlayerOnboarding = h.enableOutcomeHistory = h.enableUnlockLetters = true;
            if (preset == 1)
            {
                h.updateIntervalTicks = p.updateIntervalTicks = 900;
                h.enableRegionalPopulations = h.enableRegionalMigration = h.enableRegionalMap = false;
                h.enableWildlifeEvents = h.enablePopulationConsequences = h.enableAppliedEcology = false;
                h.enableCameraTraps = h.enableTelemetry = h.enableDiseaseMonitoring = false;
                h.enableAnimalTraditions = false;
                h.enableColonyWildlifeLandmark = false;
                h.enableWildlifeMysteries = false;
                p.enablePacks = false;
            }
            else
            {
                h.updateIntervalTicks = p.updateIntervalTicks = preset == 2 ? 300 : 450;
                h.enableRegionalPopulations = h.enableRegionalMigration = true;
                h.enableRegionalMap = preset == 2;
                h.enableWildlifeEvents = h.enablePopulationConsequences = true;
                h.enableCameraTraps = h.enableTelemetry = h.enableDiseaseMonitoring = h.enableAppliedEcology = preset == 2;
                h.enableAnimalTraditions = true;
                h.enableColonyWildlifeLandmark = true;
                h.enableWildlifeMysteries = true;
                p.enablePacks = true;
            }
            Messages.Message((preset == 0 ? "Balanced" : preset == 1 ? "Performance" : "Full Ecology") + " Wildlife preset applied.", MessageTypeDefOf.NeutralEvent, false);
            WildlifeProgression.RefreshDefGates();
        }

        public override void WriteSettings()
        {
            HerdsMod.Instance?.WriteSettings();
            PacksMod.Instance?.WriteSettings();
            base.WriteSettings();
        }
    }
}
