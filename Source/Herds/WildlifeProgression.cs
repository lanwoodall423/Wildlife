using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Herds
{
    public enum WildlifeCapability
    {
        BasicHunting,
        AnimalHandling,
        OralKnowledge,
        FeedingGrounds,
        HabitatSupport,
        TreeHabitat,
        ManagedBurns,
        Fieldcraft,
        HuntingExpedition,
        Stewardship,
        WarningSystems,
        CameraMonitoring,
        Telemetry,
        DiseaseMonitoring,
        AppliedEcology
    }

    [StaticConstructorOnStartup]
    public static class WildlifeProgression
    {
        private static readonly Dictionary<WildlifeCapability, string> Labels = new Dictionary<WildlifeCapability, string>
        {
            { WildlifeCapability.BasicHunting, "Basic organized hunting" },
            { WildlifeCapability.AnimalHandling, "Animal calls and working-predator roles" },
            { WildlifeCapability.OralKnowledge, "Oral Animal Knowledge sharing" },
            { WildlifeCapability.FeedingGrounds, "Wildlife feeding grounds" },
            { WildlifeCapability.HabitatSupport, "Basic habitat water support" },
            { WildlifeCapability.TreeHabitat, "Habitat restoration" },
            { WildlifeCapability.ManagedBurns, "Managed habitat burns" },
            { WildlifeCapability.Fieldcraft, "Organized Hunting" },
            { WildlifeCapability.HuntingExpedition, "Wildlife Expedition" },
            { WildlifeCapability.Stewardship, "Wildlife Stewardship" },
            { WildlifeCapability.WarningSystems, "Animal warning systems" },
            { WildlifeCapability.CameraMonitoring, "Automated camera monitoring" },
            { WildlifeCapability.Telemetry, "Wildlife Telemetry" },
            { WildlifeCapability.DiseaseMonitoring, "Wildlife disease-risk assessment" },
            { WildlifeCapability.AppliedEcology, "Applied Ecology" }
        };

        static WildlifeProgression()
        {
            LinkOptionalPrerequisites();
        }

        public static bool Enabled => HerdsMod.Settings?.enableResearchProgression == true;

        public static bool Unlocked(WildlifeCapability capability)
        {
            HerdsSettings settings = HerdsMod.Settings;
            if (settings == null || !settings.enableResearchProgression) return true;
            if (IsHunting(capability) && !settings.gateHuntingByResearch) return true;
            if (IsKnowledge(capability) && !settings.gateKnowledgeByResearch) return true;
            if (IsStewardship(capability) && !settings.gateStewardshipByResearch) return true;
            if (IsIndustrial(capability) && !settings.gateIndustrialEcologyByResearch) return true;

            switch (capability)
            {
                case WildlifeCapability.BasicHunting: return FinishedIfPresent("VFET_Hunting");
                case WildlifeCapability.AnimalHandling: return FinishedIfPresent("VFET_AnimalHandling");
                case WildlifeCapability.OralKnowledge: return FinishedIfPresent("VFET_Culture");
                case WildlifeCapability.FeedingGrounds:
                    return FinishedFirstPresent("Ferny_AnimalFeeder", "VFET_Agriculture");
                case WildlifeCapability.HabitatSupport: return FinishedIfPresent("VFET_Agriculture");
                case WildlifeCapability.TreeHabitat: return FinishedIfPresent("TreeSowing");
                case WildlifeCapability.ManagedBurns: return FinishedIfPresent("VFET_Fire");
                case WildlifeCapability.Fieldcraft:
                    return Finished("Wildlife_Fieldcraft") && FinishedIfPresent("VFET_Hunting");
                case WildlifeCapability.HuntingExpedition:
                    return Finished("Wildlife_HuntingExpedition") && Finished("Wildlife_Fieldcraft");
                case WildlifeCapability.Stewardship:
                    return Finished("Wildlife_Stewardship");
                case WildlifeCapability.WarningSystems: return FinishedIfPresent("TribalCCTV");
                case WildlifeCapability.CameraMonitoring:
                    return Exists("IndustrialCCTV") ? Finished("IndustrialCCTV") : Finished("Wildlife_Telemetry");
                case WildlifeCapability.Telemetry:
                    return Finished("Wildlife_Telemetry");
                case WildlifeCapability.DiseaseMonitoring:
                    return Exists("Research_AnimalImmunology") ? Finished("Research_AnimalImmunology") : Finished("Wildlife_AppliedEcology");
                case WildlifeCapability.AppliedEcology:
                    return Finished("Wildlife_AppliedEcology");
                default: return true;
            }
        }

        public static string Label(WildlifeCapability capability) => Labels.TryGetValue(capability, out string label) ? label : capability.ToString();

        public static string LockReason(WildlifeCapability capability)
        {
            if (Unlocked(capability)) return null;
            return Label(capability) + " has not been researched. Open the Wildlife research tab or disable this research gate in Wildlife settings.";
        }

        public static string Status(WildlifeCapability capability) => Unlocked(capability) ? "Available" : "Locked";

        public static bool Finished(string defName)
        {
            ResearchProjectDef def = DefDatabase<ResearchProjectDef>.GetNamedSilentFail(defName);
            return def?.IsFinished == true;
        }

        public static bool Exists(string defName) => DefDatabase<ResearchProjectDef>.GetNamedSilentFail(defName) != null;

        public static void RefreshDefGates()
        {
            LinkOptionalPrerequisites();
            HerdsSettings settings = HerdsMod.Settings;
            bool hunting = settings?.enableResearchProgression == true && settings.gateHuntingByResearch;
            bool stewardship = settings?.enableResearchProgression == true && settings.gateStewardshipByResearch;
            bool industrial = settings?.enableResearchProgression == true && settings.gateIndustrialEcologyByResearch;
            SetThingGate("Herds_ObservationPost", hunting ? "Wildlife_Fieldcraft" : null);
            SetThingGate("Herds_HuntingSpot", hunting ? "Wildlife_Fieldcraft" : null);
            SetThingGate("Herds_AnimalHideHole", hunting ? "Wildlife_Fieldcraft" : null);
            SetThingGate("Herds_WildlifeBait", stewardship ? "Wildlife_Stewardship" : null);
            SetThingGate("Herds_PredatorDeterrent", stewardship ? "Wildlife_Stewardship" : null);
            SetThingGate("Herds_ScentMaskStation", hunting ? "Wildlife_Fieldcraft" : null);
            SetThingGate("Herds_WildlifeSnare", hunting ? "Wildlife_Fieldcraft" : null);
            SetThingGate("Herds_HabitatRestoration", stewardship ? "Wildlife_Stewardship" : null);
            SetThingGate("Herds_WildlifeWaterSource", stewardship ? "Wildlife_Stewardship" : null);
            SetThingGate("Herds_WildlifeReserve", stewardship ? "Wildlife_Stewardship" : null);
            SetThingGate("Herds_MigrationCorridor", stewardship ? "Wildlife_Stewardship" : null);
            SetThingGate("Herds_ManagedBurnMarker", stewardship ? "Wildlife_Stewardship" : null);
            SetThingGate("Herds_CameraTrap", industrial ? "Wildlife_Telemetry" : null);
            SetThingGate("Herds_TelemetryStation", industrial ? "Wildlife_Telemetry" : null);
            SetRecipeGate("Herds_MakeCamouflageSupplies", hunting ? "Wildlife_Fieldcraft" : null);
            SetRecipeGate("Herds_MakeFieldBinoculars", industrial ? "Wildlife_Telemetry" : null);
            SetRecipeGate("Herds_MakeTrackingCollar", industrial ? "Wildlife_Telemetry" : null);
        }

        private static bool IsHunting(WildlifeCapability capability) =>
            capability == WildlifeCapability.BasicHunting || capability == WildlifeCapability.Fieldcraft || capability == WildlifeCapability.HuntingExpedition;

        private static bool IsKnowledge(WildlifeCapability capability) =>
            capability == WildlifeCapability.AnimalHandling || capability == WildlifeCapability.OralKnowledge ||
            capability == WildlifeCapability.WarningSystems;

        private static bool IsStewardship(WildlifeCapability capability) =>
            capability == WildlifeCapability.FeedingGrounds || capability == WildlifeCapability.TreeHabitat ||
            capability == WildlifeCapability.HabitatSupport || capability == WildlifeCapability.ManagedBurns || capability == WildlifeCapability.Stewardship;

        private static bool IsIndustrial(WildlifeCapability capability) =>
            capability == WildlifeCapability.CameraMonitoring || capability == WildlifeCapability.Telemetry ||
            capability == WildlifeCapability.DiseaseMonitoring || capability == WildlifeCapability.AppliedEcology;

        private static bool FinishedIfPresent(string defName) => !Exists(defName) || Finished(defName);

        private static bool FinishedFirstPresent(params string[] defNames)
        {
            string defName = FirstExisting(defNames);
            return defName == null || Finished(defName);
        }

        private static string FirstExisting(params string[] defNames)
        {
            for (int i = 0; i < defNames.Length; i++) if (Exists(defNames[i])) return defNames[i];
            return null;
        }

        private static void LinkOptionalPrerequisites()
        {
            AddOptionalPrerequisite("Wildlife_Fieldcraft", "VFET_Hunting");
        }

        private static void AddOptionalPrerequisite(string projectName, string prerequisiteName)
        {
            ResearchProjectDef project = DefDatabase<ResearchProjectDef>.GetNamedSilentFail(projectName);
            ResearchProjectDef prerequisite = DefDatabase<ResearchProjectDef>.GetNamedSilentFail(prerequisiteName);
            if (project == null || prerequisite == null) return;
            if (project.prerequisites == null) project.prerequisites = new List<ResearchProjectDef>();
            if (!project.prerequisites.Contains(prerequisite)) project.prerequisites.Add(prerequisite);
        }

        private static void AddFirstOptionalPrerequisite(string projectName, params string[] prerequisiteNames)
        {
            string prerequisite = FirstExisting(prerequisiteNames);
            if (prerequisite != null) AddOptionalPrerequisite(projectName, prerequisite);
        }

        private static void SetThingGate(string defName, string researchName)
        {
            ThingDef thing = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (thing == null) return;
            if (researchName == null) thing.researchPrerequisites = null;
            else
            {
                ResearchProjectDef research = DefDatabase<ResearchProjectDef>.GetNamedSilentFail(researchName);
                thing.researchPrerequisites = research == null ? null : new List<ResearchProjectDef> { research };
            }
        }

        private static void SetRecipeGate(string defName, string researchName)
        {
            RecipeDef recipe = DefDatabase<RecipeDef>.GetNamedSilentFail(defName);
            if (recipe == null) return;
            recipe.researchPrerequisite = researchName == null ? null : DefDatabase<ResearchProjectDef>.GetNamedSilentFail(researchName);
        }
    }

    public sealed class Window_WildlifeProgression : Window
    {
        private Vector2 scroll;
        public override Vector2 InitialSize => new Vector2(720f, 650f);

        public Window_WildlifeProgression()
        {
            doCloseX = true;
            absorbInputAroundWindow = true;
        }

        public override void DoWindowContents(Rect rect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, rect.width, 32f), "Wildlife Progression");
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(0f, 36f, rect.width, 42f), "Animal behavior is always active. These stages govern the colony's ability to organize hunts, preserve knowledge, manage populations, and automate ecological work.");
            string[] projects = { "Wildlife_Fieldcraft", "Wildlife_HuntingExpedition", "Wildlife_Stewardship", "Wildlife_Telemetry", "Wildlife_AppliedEcology" };
            string[] requirements =
            {
                "Requires Hunting",
                "Requires Organized Hunting",
                "Requires Organized Hunting and Tree Sowing",
                "Requires Wildlife Stewardship and Circuitry",
                "Requires Wildlife Telemetry and Microelectronics"
            };
            string[] unlocks =
            {
                "Unlocks coordinated hunts, advanced tracking, camouflage, scent masking, snares, and specialist positioning.",
                "Unlocks supplied off-map parties for scouting, tracking, hunting, capturing, tagging, and redirecting wildlife.",
                "Unlocks reserves, migration corridors, hunting regulations, population goals, and colonist stewardship fieldwork.",
                "Unlocks camera monitoring, field optics, tracking collars, automated surveys, and regional migration tracking.",
                "Unlocks habitat-capacity, population-pressure, disease-risk, migration, and intervention forecasts."
            };
            Rect outer = new Rect(0f, 84f, rect.width, rect.height - 84f);
            const float rowStep = 112f;
            Rect view = new Rect(0f, 0f, outer.width - 18f, projects.Length * rowStep + 20f);
            Widgets.BeginScrollView(outer, ref scroll, view);
            for (int i = 0; i < projects.Length; i++)
            {
                ResearchProjectDef project = DefDatabase<ResearchProjectDef>.GetNamedSilentFail(projects[i]);
                Rect row = new Rect(0f, i * rowStep, view.width, rowStep - 8f);
                Widgets.DrawMenuSection(row);
                bool finished = project?.IsFinished == true;
                GUI.color = finished ? new Color(0.55f, 0.9f, 0.55f) : new Color(0.85f, 0.6f, 0.4f);
                Widgets.Label(new Rect(10f, row.y + 9f, 90f, 24f), finished ? "Complete" : "Locked");
                GUI.color = Color.white;
                Text.Font = GameFont.Medium;
                Widgets.Label(new Rect(105f, row.y + 6f, row.width - 115f, 28f), project?.LabelCap ?? projects[i]);
                Text.Font = GameFont.Small;
                Widgets.Label(new Rect(105f, row.y + 36f, row.width - 115f, 22f), requirements[i]);
                GUI.color = new Color(0.74f, 0.79f, 0.75f);
                Widgets.Label(new Rect(105f, row.y + 59f, row.width - 115f, 42f), unlocks[i]);
                GUI.color = Color.white;
            }
            Widgets.EndScrollView();
        }
    }
}
