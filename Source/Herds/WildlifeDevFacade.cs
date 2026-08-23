using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using LudeonTK;
using RimWorld;
using Verse;

namespace Herds
{
    // This is the small player-assembly seam used by existing DevMode gizmos.
    // Development overlays remain in the gameplay assembly. The full test suite
    // is exposed through the optional RimBridgeServer companion instead.
    public static class WildlifeDebugState
    {
        public static bool CompleteOverlayEnabled;

        public static void ToggleCompleteOverlay()
        {
            SetCompleteOverlay(!CompleteOverlayEnabled);
            Messages.Message("Unified wildlife overlay " + (CompleteOverlayEnabled ? "enabled." : "disabled."),
                MessageTypeDefOf.NeutralEvent, false);
        }

        [DebugAction("Wildlife", "Toggle unified wildlife overlay",
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void DebugToggleUnifiedOverlay() => ToggleCompleteOverlay();

        public static void SetCompleteOverlay(bool enabled)
        {
            CompleteOverlayEnabled = enabled;
            HerdsDebugActions.OverlayEnabled = enabled;
            HerdsDebugActions.RefugeOverlayEnabled = enabled;
            FieldcraftDebug.HuntOverlay = enabled;
            FieldcraftDebug.KnowledgeOverlay = enabled;
            FieldcraftDebug.SignOverlay = enabled;
            FieldcraftDebug.GuardianOverlay = enabled;
            FieldcraftDebug.WarningOverlay = enabled;
            SetPacksDevField("OverlayEnabled", enabled);
        }

        private static void SetPacksDevField(string fieldName, bool value)
        {
            Type type = AccessTools.TypeByName("Packs.PredatorDevTools");
            FieldInfo field = type == null ? null : AccessTools.Field(type, fieldName);
            field?.SetValue(null, value);
        }

        public static List<string> PacksOverview(Map map)
        {
            MapComponent component = map?.components?.FirstOrDefault(item =>
                item.GetType().FullName == "Packs.PackMapComponent");
            if (component == null) return new List<string> { "Packs and Predators is not active on this map." };
            MethodInfo method = AccessTools.Method(component.GetType(), "DebugOverviewLines");
            return method?.Invoke(component, null) as List<string> ??
                new List<string> { "Predator overview API unavailable." };
        }

        public static string PacksPerformance(Map map)
        {
            MapComponent component = map?.components?.FirstOrDefault(item =>
                item.GetType().FullName == "Packs.PackMapComponent");
            return AccessTools.Method(component?.GetType(), "PerformanceSummary")?.Invoke(component, null) as string ??
                "Packs and Predators is not active.";
        }
    }

    public static class WildlifeDebugMenus
    {
        public static bool ShowExpandedPreyGizmos;
        public static bool ShowExpandedColonistGizmos;

        public static Command_Toggle DiagnosticToggle(Pawn context) => new Command_Toggle
        {
            defaultLabel = "DEV: Diagnostic Log",
            defaultDesc = "Toggle the shared structured wildlife diagnostic session.",
            icon = TexCommand.OpenLinkedQuestTex,
            isActive = () => WildlifeTestLog.Enabled,
            toggleAction = () =>
            {
                WildlifeTestLog.Toggle();
                if (WildlifeTestLog.Enabled)
                    WildlifeTestLog.Write("Session", "Enabled from organized Dev controls", context);
                Messages.Message("Wildlife diagnostic logging " +
                    (WildlifeTestLog.Enabled ? "enabled." : "disabled."),
                    MessageTypeDefOf.NeutralEvent, false);
            }
        };

        public static Command_Toggle CompleteOverlayToggle() => new Command_Toggle
        {
            defaultLabel = "DEV: Unified Overlay",
            defaultDesc = "Toggle the unified wildlife development overlay.",
            icon = TexCommand.GatherSpotActive,
            isActive = () => WildlifeDebugState.CompleteOverlayEnabled,
            toggleAction = WildlifeDebugState.ToggleCompleteOverlay
        };

        public static void ShowPreyTests(Pawn prey)
        {
            Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>
            {
                new FloatMenuOption("Response / defense test", () => HerdsDebugActions.OpenResponseTester(prey)),
                new FloatMenuOption("Start simulated hunt", () => HerdsDebugActions.BeginSimulatedHunt(prey)),
                new FloatMenuOption("Stop simulated hunt", () => HerdsDebugActions.StopSimulatedHunt(prey)),
                new FloatMenuOption("Go to home", () => HerdsDebugActions.SendHome(prey, false)),
                new FloatMenuOption("Sleep at home", () => HerdsDebugActions.SendHome(prey, true)),
                new FloatMenuOption("Set home", () => HerdsDebugActions.BeginSetHome(prey)),
                new FloatMenuOption("Create burrow home", () => HerdsDebugActions.CreateBurrowHome(prey)),
                new FloatMenuOption("Log prey state", () => HerdsDebugActions.LogPreyState(prey)),
                new FloatMenuOption("Refresh simulation", HerdsDebugActions.RefreshSimulation),
                new FloatMenuOption("Performance benchmark...", () => HerdsDebugActions.OpenBenchmarkMenu(prey.Map.GetComponent<HerdMapComponent>())),
                new FloatMenuOption("Individual overlays...", FieldcraftDebug.ShowOverlayMenu),
                new FloatMenuOption("Expanded legacy gizmos: " +
                    (ShowExpandedPreyGizmos ? "ON" : "OFF"),
                    () => ShowExpandedPreyGizmos = !ShowExpandedPreyGizmos)
            }));
        }

        public static void ShowColonistTests(Pawn colonist)
        {
            Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>
            {
                new FloatMenuOption("Set personal species knowledge", () => FieldcraftDevGizmoPatch.BeginKnowledgeTarget(colonist)),
                new FloatMenuOption("Master knowledge of all species", () => FieldcraftDevGizmoPatch.MasterAllKnowledge(colonist)),
                new FloatMenuOption("Start coordinated hunt test", () => FieldcraftDevGizmoPatch.BeginGroupHuntTarget(colonist)),
                new FloatMenuOption("Wound animal / blood trail", () => FieldcraftDevGizmoPatch.BeginWoundTarget(colonist)),
                new FloatMenuOption("Create wildlife sign", () => FieldcraftDevGizmoPatch.BeginSignTarget(colonist)),
                new FloatMenuOption("Spawn fieldcraft gear", () => FieldcraftDevGizmoPatch.SpawnGear(colonist)),
                new FloatMenuOption("Test predator warning", () => FieldcraftDevGizmoPatch.BeginWarningTarget(colonist)),
                new FloatMenuOption("Fit test tracking collar", HerdsDebugActions.BeginFitTestTrackingCollar),
                new FloatMenuOption("Wildlife progression status", HerdsDebugActions.OpenProgressionStatus),
                new FloatMenuOption("Individual overlays...", FieldcraftDebug.ShowOverlayMenu),
                new FloatMenuOption("Expanded legacy gizmos: " +
                    (ShowExpandedColonistGizmos ? "ON" : "OFF"),
                    () => ShowExpandedColonistGizmos = !ShowExpandedColonistGizmos)
            }));
        }
    }
}
