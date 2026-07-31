using System.Collections.Generic;
using System.Linq;
using LudeonTK;
using RimWorld;
using UnityEngine;
using Verse;

namespace Herds
{
    public static class HerdsDebugActions
    {
        private const string Category = "Herds and Hiders";
        public static bool OverlayEnabled;
        public static bool RefugeOverlayEnabled;
        public static bool PerformanceOverlayEnabled;

        [DebugAction(Category, "Run wildlife expedition validation suite", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void ValidateHuntingExpeditions()
        {
            HuntingExpeditionMapComponent component = Find.CurrentMap?.GetComponent<HuntingExpeditionMapComponent>();
            List<string> lines = component?.DebugValidationLines() ?? new List<string> { "FAIL | No current map" };
            int failures = lines.Count(line => line.StartsWith("FAIL"));
            Log.Message("[WildlifeTest][ExpeditionValidation]\n" + string.Join("\n", lines));
            if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("ExpeditionValidation", "failures=" + failures + "\n" + string.Join("\n", lines));
            Messages.Message(failures == 0 ? "Wildlife expedition validation passed. Details were written to the log." :
                failures + " wildlife expedition validation check(s) failed. See the log.", failures == 0 ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.NegativeEvent, false);
        }

        [DebugAction(Category, "Test prey response...", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void TestPreyResponse(Pawn prey)
        {
            OpenResponseTester(prey);
        }

        public static void OpenResponseTester(Pawn prey)
        {
            if (!ValidatePrey(prey)) return;
            BeginPredatorTargeting(prey, predator => ShowModes(prey, predator));
        }

        public static void BeginSimulatedHunt(Pawn prey)
        {
            if (!ValidatePrey(prey)) return;
            BeginPredatorTargeting(prey, predator =>
            {
                bool applied = prey.Map?.GetComponent<HerdMapComponent>()?.DebugStartHunted(prey, predator) == true;
                if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("DevSimulatedHunt", "start result=" + applied, prey, predator);
                Messages.Message(applied ? prey.LabelShortCap + "'s group is now simulating being hunted." : "Could not start the simulated hunt.",
                    applied ? MessageTypeDefOf.NeutralEvent : MessageTypeDefOf.RejectInput, false);
            });
        }

        public static void StopSimulatedHunt(Pawn prey)
        {
            bool stopped = prey?.Map?.GetComponent<HerdMapComponent>()?.DebugStopHunted(prey) == true;
            if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("DevSimulatedHunt", "stop result=" + stopped, prey);
            Messages.Message(stopped ? "Stopped the simulated hunt." : "This prey group is not simulating a hunt.",
                stopped ? MessageTypeDefOf.NeutralEvent : MessageTypeDefOf.RejectInput, false);
        }

        public static void SendHome(Pawn prey, bool sleep)
        {
            if (!ValidatePrey(prey)) return;
            bool started = prey.Map?.GetComponent<HerdMapComponent>()?.DebugSendHome(prey, sleep) == true;
            string action = sleep ? "sleep at home" : "go home";
            Messages.Message(started ? prey.LabelShortCap + " is going to " + action + "." : "Could not find or reach a valid home for " + prey.LabelShortCap + ".",
                started ? MessageTypeDefOf.NeutralEvent : MessageTypeDefOf.RejectInput, false);
        }

        public static void BeginSetHome(Pawn prey)
        {
            if (!ValidatePrey(prey)) return;
            Find.Targeter.BeginTargeting(TargetingParameters.ForCell(), target =>
            {
                Thing refuge = target.Cell.GetThingList(prey.Map).FirstOrDefault(thing => thing.TryGetComp<CompHidingRefuge>() != null) ??
                    target.Cell.GetThingList(prey.Map).FirstOrDefault(thing => thing is Plant plant && plant.def.plant?.IsTree == true);
                bool assigned = prey.Map?.GetComponent<HerdMapComponent>()?.DebugSetHome(prey, refuge) == true;
                if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("DevSetHome", "result=" + assigned + " targetCell=" + target.Cell, prey, refuge);
                Messages.Message(assigned ? prey.LabelShortCap + " now uses " + refuge.LabelShortCap + " as its home." : "That cell does not contain a valid, reachable home for this species.",
                    assigned ? MessageTypeDefOf.NeutralEvent : MessageTypeDefOf.RejectInput, false);
            });
        }

        public static void CreateBurrowHome(Pawn prey)
        {
            if (!ValidatePrey(prey)) return;
            Thing burrow = null;
            bool created = prey.Map?.GetComponent<HerdMapComponent>()?.DebugCreateBurrowHome(prey, out burrow) == true;
            Messages.Message(created ? "Created and assigned a burrow home for " + prey.LabelShortCap + " at " + burrow.Position + "." : "Could not create a reachable burrow for this species.",
                created ? MessageTypeDefOf.NeutralEvent : MessageTypeDefOf.RejectInput, false);
        }

        [DebugAction(Category, "Log prey profile/group", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void LogPreyState(Pawn prey)
        {
            if (!ValidatePrey(prey)) return;
            PreyProfile profile = PreyProfileDatabase.For(prey.def);
            HerdMapComponent component = prey.Map.GetComponent<HerdMapComponent>();
            HerdSnapshot group = component?.HerdFor(prey);
            Thing home = component?.HomeFor(prey);
            Log.Message("[Herds and Hiders] " + prey.LabelShortCap +
                " | social=" + profile.socialType +
                " | defense=" + profile.defenseStrategy +
                " | refuge=" + profile.refugePreference +
                " | vigilance=" + (component?.VigilanceFor(prey) ?? profile.vigilanceChance).ToString("P0") +
                " | home=" + (home?.LabelShortCap.ToString() ?? "none") +
                " | homeCell=" + (home?.Position.ToString() ?? "none") +
                " | bodySize=" + prey.BodySize.ToString("0.00") +
                " | group=" + (group?.Label ?? "none") +
                " | members=" + (group?.members.Count ?? 0) +
                " | active=" + (group?.defenseMode.ToString() ?? "none"));
        }

        [DebugAction(Category, "Reveal all hidden prey", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void RevealAllHidden()
        {
            int count = Find.CurrentMap?.GetComponent<HerdMapComponent>()?.DebugRevealAllHidden() ?? 0;
            Messages.Message("Revealed " + count + " hidden prey.", MessageTypeDefOf.NeutralEvent, false);
        }

        [DebugAction(Category, "Refresh groups and refuges", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void RefreshSimulation()
        {
            Find.CurrentMap?.GetComponent<HerdMapComponent>()?.DebugRefresh();
            Messages.Message("Prey groups and refuge index refreshed.", MessageTypeDefOf.NeutralEvent, false);
        }

        [DebugAction(Category, "Open Wildlife progression status", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void OpenProgressionStatus()
        {
            Find.WindowStack.Add(new Window_WildlifeProgression());
        }

        [DebugAction(Category, "Fit test tracking collar", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void FitTestTrackingCollar(Pawn animal)
        {
            if (animal?.Spawned != true || animal.RaceProps?.Animal != true)
            {
                Messages.Message("Select a spawned animal.", MessageTypeDefOf.RejectInput, false);
                return;
            }
            if (animal.health.hediffSet.GetFirstHediffOfDef(HerdsDefOf.Herds_TrackingCollar) == null) animal.health.AddHediff(HerdsDefOf.Herds_TrackingCollar);
            Messages.Message("Test tracking collar fitted to " + animal.LabelShortCap + ".", animal, MessageTypeDefOf.NeutralEvent, false);
            if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("DevTelemetryTag", "fitted=true", animal);
        }

        public static void BeginFitTestTrackingCollar()
        {
            TargetingParameters parameters = new TargetingParameters
            {
                canTargetPawns = true, canTargetAnimals = true, canTargetHumans = false, canTargetLocations = false,
                validator = target => target.Thing is Pawn pawn && pawn.Spawned && pawn.RaceProps?.Animal == true
            };
            Find.Targeter.BeginTargeting(parameters, target => FitTestTrackingCollar((Pawn)target.Thing));
        }

        [DebugAction(Category, "Toggle group overlay", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void ToggleGroupOverlay()
        {
            OverlayEnabled = !OverlayEnabled;
            Messages.Message("Prey debug overlay " + (OverlayEnabled ? "enabled." : "disabled."), MessageTypeDefOf.NeutralEvent, false);
        }

        [DebugAction(Category, "Toggle refuge overlay", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void ToggleRefugeOverlay()
        {
            RefugeOverlayEnabled = !RefugeOverlayEnabled;
            if (RefugeOverlayEnabled) OverlayEnabled = true;
            Messages.Message("Refuge overlay " + (RefugeOverlayEnabled ? "enabled." : "disabled."), MessageTypeDefOf.NeutralEvent, false);
        }

        [DebugAction(Category, "Toggle performance dashboard", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void TogglePerformanceOverlay()
        {
            PerformanceOverlayEnabled = !PerformanceOverlayEnabled;
            Messages.Message("Prey performance dashboard " + (PerformanceOverlayEnabled ? "enabled." : "disabled."), MessageTypeDefOf.NeutralEvent, false);
        }

        public static void OpenBenchmarkMenu(HerdMapComponent component)
        {
            Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>
            {
                BenchmarkOption(component, 15000, "Quarter day"),
                BenchmarkOption(component, 60000, "One day"),
                BenchmarkOption(component, 180000, "Three days")
            }));
        }

        private static FloatMenuOption BenchmarkOption(HerdMapComponent component, int ticks, string label)
        {
            return new FloatMenuOption(label, () =>
            {
                component?.DebugStartBenchmark(ticks);
                Messages.Message("Prey benchmark started: " + label + ".", MessageTypeDefOf.NeutralEvent, false);
            });
        }

        private static void ShowModes(Pawn prey, Pawn threat)
        {
            var options = new List<DebugMenuOption>
            {
                ModeOption("Inferred behavior", prey, threat, null),
                ModeOption("Force flight", prey, threat, HerdDefenseMode.Flight),
                ModeOption("Force scatter", prey, threat, HerdDefenseMode.Scatter),
                ModeOption("Force protect young", prey, threat, HerdDefenseMode.ProtectYoung),
                ModeOption("Force hide", prey, threat, HerdDefenseMode.Hide),
                ModeOption("Force freeze", prey, threat, HerdDefenseMode.Freeze),
                ModeOption("Force stand ground", prey, threat, HerdDefenseMode.StandGround)
            };
            Find.WindowStack.Add(new Dialog_DebugOptionListLister(options));
        }

        private static void BeginPredatorTargeting(Pawn prey, System.Action<Pawn> action)
        {
            var parameters = new TargetingParameters
            {
                canTargetLocations = false,
                canTargetBuildings = false,
                canTargetItems = false,
                canTargetPawns = true,
                canTargetAnimals = true,
                canTargetHumans = false,
                canTargetMechs = false,
                canTargetSubhumans = false,
                canTargetEntities = false,
                mapObjectTargetsMustBeAutoAttackable = false,
                validator = target => target.Thing is Pawn predator && predator != prey &&
                    WildlifeSpeciesClassification.IsPredator(predator.def) && predator.Spawned && !predator.Dead
            };
            Find.Targeter.BeginTargeting(parameters, target => action(target.Pawn), prey);
        }

        private static DebugMenuOption ModeOption(string label, Pawn prey, Pawn threat, HerdDefenseMode? mode)
        {
            return new DebugMenuOption(label, DebugMenuOptionMode.Action, delegate
            {
                bool applied = prey.Map?.GetComponent<HerdMapComponent>()?.DebugTriggerDefense(prey, threat, mode) == true;
                if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("DevDefense", "requested=" + (mode?.ToString() ?? "Inferred") + " result=" + applied, prey, threat);
                Messages.Message(applied ? label + " applied to " + prey.LabelShortCap + "'s group." : "Could not apply the test response.",
                    applied ? MessageTypeDefOf.NeutralEvent : MessageTypeDefOf.RejectInput, false);
            });
        }

        private static bool ValidatePrey(Pawn pawn)
        {
            if (pawn?.Spawned == true && PreyProfileDatabase.IsEligible(pawn.def)) return true;
            Messages.Message("Select an eligible spawned prey animal.", MessageTypeDefOf.RejectInput, false);
            return false;
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class PreyDevGizmosPatch
    {
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> gizmos, Pawn __instance)
        {
            foreach (Gizmo gizmo in gizmos) yield return gizmo;
            if (!Prefs.DevMode || __instance?.Spawned != true || Find.Selector.SingleSelectedThing != __instance || !PreyProfileDatabase.IsEligible(__instance.def)) yield break;

            PreyProfile profile = PreyProfileDatabase.For(__instance.def);

            yield return new Command_Action { defaultLabel = "DEV: Wildlife Overview", defaultDesc = "Open the complete organized wildlife development dashboard.", icon = TexCommand.OpenLinkedQuestTex, action = WildlifeDevMaster.OpenDashboard };
            yield return WildlifeDevMenus.CompleteOverlayToggle();
            yield return WildlifeDevMenus.DiagnosticToggle(__instance);
            yield return new Command_Action { defaultLabel = "DEV: Prey Tests...", defaultDesc = "Open organized response, hiding, home, simulation, overlay, logging, and benchmark tools.", icon = TexCommand.SquadAttack, action = () => WildlifeDevMenus.ShowPreyTests(__instance) };
            if (!WildlifeDevMenus.ShowExpandedPreyGizmos) yield break;

            yield return new Command_Toggle
            {
                defaultLabel = "DEV: Diagnostic Log",
                defaultDesc = "Toggle a shared Herds/Packs diagnostic session. Important hunting, defense, hiding, home, and den events are written to Player.log with the [WildlifeTest] prefix.",
                icon = TexCommand.OpenLinkedQuestTex,
                isActive = () => WildlifeTestLog.Enabled,
                toggleAction = () =>
                {
                    bool enabling = !WildlifeTestLog.Enabled;
                    WildlifeTestLog.Toggle();
                    if (enabling) WildlifeTestLog.Write("Session", "Enabled from prey gizmo; social=" + profile.socialType + ", defense=" + profile.defenseStrategy + ", refuge=" + profile.refugePreference, __instance);
                    Messages.Message("Wildlife diagnostic logging " + (WildlifeTestLog.Enabled ? "enabled. Reproduce the issue, then toggle it off and provide Player.log." : "disabled. The diagnostic session is now delimited in Player.log."), MessageTypeDefOf.NeutralEvent, false);
                }
            };
            yield return new Command_Action
            {
                defaultLabel = "DEV: Hide Roll: " + WildlifeTestLog.HideOutcome,
                defaultDesc = "Choose natural hiding rolls, forced success, or forced failure for repeatable tests. This only applies while Dev mode is enabled.",
                icon = TexCommand.CannotShoot,
                action = () => Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>
                {
                    HideOutcomeOption("Natural", TestRollMode.Natural),
                    HideOutcomeOption("Always succeed", TestRollMode.ForceSuccess),
                    HideOutcomeOption("Always fail", TestRollMode.ForceFailure)
                }))
            };

            var goHome = new Command_Action
            {
                defaultLabel = "DEV: Go Home",
                defaultDesc = "Assign the nearest suitable tree or hole as this prey's home if needed, then force it to go there.",
                icon = TexCommand.Replant,
                action = () => HerdsDebugActions.SendHome(__instance, false)
            };
            var sleepHome = new Command_Action
            {
                defaultLabel = "DEV: Sleep at Home",
                defaultDesc = "Assign a suitable home if needed, then force this prey to lie down and sleep beside it.",
                icon = TexCommand.GatherSpotActive,
                action = () => HerdsDebugActions.SendHome(__instance, true)
            };
            var setHome = new Command_Action
            {
                defaultLabel = "DEV: Set Home",
                defaultDesc = "Choose a tree or hiding refuge to assign as this prey's home.",
                icon = TexCommand.Replant,
                action = () => HerdsDebugActions.BeginSetHome(__instance)
            };
            if (profile.refugePreference == PreyRefugePreference.None || __instance.BodySize > profile.maximumHidingBodySize)
            {
                const string reason = "This species does not use a home refuge, or is too large for one.";
                goHome.Disable(reason);
                sleepHome.Disable(reason);
                setHome.Disable(reason);
            }
            yield return goHome;
            yield return sleepHome;
            yield return setHome;

            var makeBurrow = new Command_Action
            {
                defaultLabel = "DEV: Make Burrow",
                defaultDesc = "Create a natural burrow near this prey and assign it as the animal's home.",
                icon = TexCommand.Replant,
                action = () => HerdsDebugActions.CreateBurrowHome(__instance)
            };
            if (!profile.CanUseDens || __instance.BodySize > profile.maximumHidingBodySize) makeBurrow.Disable("This species cannot use a burrow, or is too large for one.");
            yield return makeBurrow;

            yield return new Command_Action
            {
                defaultLabel = "DEV: Test Response",
                defaultDesc = "Choose a test threat and force or infer this prey group's defensive response.",
                icon = TexCommand.Attack,
                action = () => HerdsDebugActions.OpenResponseTester(__instance)
            };
            yield return new Command_Action
            {
                defaultLabel = "DEV: Simulate Hunted",
                defaultDesc = "Click a predator to make this prey group continuously behave as though it is being hunted.",
                icon = TexCommand.SquadAttack,
                action = () => HerdsDebugActions.BeginSimulatedHunt(__instance)
            };
            var stopHunted = new Command_Action
            {
                defaultLabel = "DEV: Stop Hunted",
                defaultDesc = "Stop the persistent simulated hunt for this prey group.",
                icon = TexCommand.CannotShoot,
                action = () => HerdsDebugActions.StopSimulatedHunt(__instance)
            };
            if (__instance.Map?.GetComponent<HerdMapComponent>()?.IsSimulatedHunt(__instance) != true) stopHunted.Disable("This group is not simulating a hunt.");
            yield return stopHunted;
            yield return new Command_Action
            {
                defaultLabel = "DEV: Log State",
                defaultDesc = "Log this animal's prey profile, group, and active response.",
                icon = TexCommand.OpenLinkedQuestTex,
                action = () => HerdsDebugActions.LogPreyState(__instance)
            };
            yield return new Command_Action
            {
                defaultLabel = "DEV: Refresh",
                defaultDesc = "Immediately rebuild prey groups and the refuge index.",
                icon = TexCommand.Replant,
                action = () =>
                {
                    __instance.Map?.GetComponent<HerdMapComponent>()?.DebugRefresh();
                    Messages.Message("Prey groups and refuges refreshed.", MessageTypeDefOf.NeutralEvent, false);
                }
            };
            yield return new Command_Action
            {
                defaultLabel = "DEV: Overlay",
                defaultDesc = "Toggle the prey group debug overlay. Right-click behavior is available through the dev actions menu.",
                icon = TexCommand.GatherSpotActive,
                action = HerdsDebugActions.ToggleGroupOverlay
            };
            yield return new Command_Action
            {
                defaultLabel = "DEV: Refuge Overlay",
                defaultDesc = "Toggle home, refuge, occupancy, and hidden-prey information in the prey debug overlay.",
                icon = TexCommand.GatherSpotActive,
                action = HerdsDebugActions.ToggleRefugeOverlay
            };
            yield return new Command_Toggle
            {
                defaultLabel = "DEV: Performance",
                defaultDesc = "Toggle live timings, cache sizes, path checks, active defenses, hidden prey, abandoned homes, and tree routes.",
                icon = TexCommand.GatherSpotActive,
                isActive = () => HerdsDebugActions.PerformanceOverlayEnabled,
                toggleAction = HerdsDebugActions.TogglePerformanceOverlay
            };
            yield return new Command_Action
            {
                defaultLabel = "DEV: Benchmark",
                defaultDesc = "Run a performance soak test and report timings, path failures, stuck jobs, alarms, hidden prey, and group counts.",
                icon = TexCommand.OpenLinkedQuestTex,
                action = () => HerdsDebugActions.OpenBenchmarkMenu(__instance.Map?.GetComponent<HerdMapComponent>())
            };
        }

        private static FloatMenuOption HideOutcomeOption(string label, TestRollMode mode)
        {
            return new FloatMenuOption(label, delegate
            {
                WildlifeTestLog.HideOutcome = mode;
                Messages.Message("Prey hiding test outcome: " + label + ".", MessageTypeDefOf.NeutralEvent, false);
            });
        }
    }
}
