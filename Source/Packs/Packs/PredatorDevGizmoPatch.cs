using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Packs;

[HarmonyPatch(typeof(Pawn), "GetGizmos")]
public static class PredatorDevGizmoPatch
{
	private static bool expandedGizmos;
	public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Pawn __instance)
	{
		foreach (Gizmo item in __result)
		{
			yield return item;
		}
		if (!Prefs.DevMode)
		{
			yield break;
		}
		Pawn pawn = __instance;
		if (pawn == null || !pawn.Spawned || Find.Selector.SingleSelectedThing != __instance || !PackMapComponent.IsPackHunter(__instance))
		{
			yield break;
		}
		PackMapComponent component = __instance.Map.GetComponent<PackMapComponent>();
		AnimalPackSettings config = PacksMod.Settings.For(__instance.def);
		yield return Action("DEV: Wildlife Overview", "Open the complete organized wildlife development dashboard.", TexCommand.OpenLinkedQuestTex, OpenWildlifeDashboard);
		yield return new Command_Toggle
		{
			defaultLabel = "DEV: Complete Overlay", defaultDesc = "Toggle all prey, predator, hunt, den, home, range, role, and development visuals together.", icon = TexCommand.GatherSpotActive,
			isActive = CompleteOverlayActive, toggleAction = ToggleCompleteOverlay
		};
		yield return new Command_Toggle
		{
			defaultLabel = "DEV: Diagnostic Log", defaultDesc = "Toggle the shared structured wildlife diagnostic session.", icon = TexCommand.OpenLinkedQuestTex,
			isActive = () => WildlifeTestLog.Enabled, toggleAction = WildlifeTestLog.Toggle
		};
		yield return Action("DEV: Predator Tests...", "Open organized hunt, phase, den, movement, boldness, benchmark, and state tools.", TexCommand.SquadAttack, () => ShowPredatorTests(component, __instance, config));
		if (!expandedGizmos) yield break;
		yield return new Command_Toggle
		{
			defaultLabel = "DEV: Diagnostic Log",
			defaultDesc = "Toggle a shared Herds/Packs diagnostic session. Important hunting, defense, hiding, home, and den events are written to Player.log with the [WildlifeTest] prefix.",
			icon = TexCommand.OpenLinkedQuestTex,
			isActive = () => WildlifeTestLog.Enabled,
			toggleAction = delegate
			{
				bool enabling = !WildlifeTestLog.Enabled;
				WildlifeTestLog.Toggle();
				if (enabling) WildlifeTestLog.Write("Session", "Enabled from predator gizmo; social=" + config.socialStrategy + ", hunting=" + config.huntingStyle, __instance);
				Messages.Message("Wildlife diagnostic logging " + (WildlifeTestLog.Enabled ? "enabled. Reproduce the issue, then toggle it off and provide Player.log." : "disabled. The diagnostic session is now delimited in Player.log."), MessageTypeDefOf.NeutralEvent, historical: false);
			}
		};
		yield return Action("DEV: Human Boldness", "Set this predator group's learned response to humans to wary, cautious, or bold.", TexCommand.GatherSpotActive, delegate
		{
			Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>
			{
				new FloatMenuOption("Wary (0.1)", () => component.DebugSetHumanBoldness(__instance, 0.1f)),
				new FloatMenuOption("Cautious (0.45)", () => component.DebugSetHumanBoldness(__instance, 0.45f)),
				new FloatMenuOption("Bold (0.9)", () => component.DebugSetHumanBoldness(__instance, 0.9f))
			}));
		});
		yield return Action("DEV: Benchmark", "Run a performance soak test for a quarter-day, one day, or three days and write timing, path-failure, and stuck-job results to the log.", TexCommand.OpenLinkedQuestTex, delegate
		{
			Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>
			{
				BenchmarkOption(component, 15000, "Quarter day"),
				BenchmarkOption(component, 60000, "One day"),
				BenchmarkOption(component, 180000, "Three days")
			}));
		});
		yield return new Command_Toggle
		{
			defaultLabel = "DEV: Performance",
			defaultDesc = "Toggle live timings, cache sizes, path checks, active hunts, territory events, den lifecycle, and automated test status.",
			icon = TexCommand.GatherSpotActive,
			isActive = () => PredatorDevTools.PerformanceOverlayEnabled,
			toggleAction = delegate { PredatorDevTools.PerformanceOverlayEnabled = !PredatorDevTools.PerformanceOverlayEnabled; }
		};
		yield return Action("DEV: Detection Roll: " + WildlifeTestLog.DetectionOutcome, "Choose natural detection rolls, forced detection, or forced non-detection for repeatable tests. This only applies while Dev mode is enabled.", TexCommand.CannotShoot, delegate
		{
			Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>
			{
				DetectionOutcomeOption("Natural", TestRollMode.Natural),
				DetectionOutcomeOption("Always detected", TestRollMode.ForceSuccess),
				DetectionOutcomeOption("Never detected by roll", TestRollMode.ForceFailure)
			}));
		});
		yield return new Command_Toggle
		{
			defaultLabel = "DEV: Predator Overlay",
			defaultDesc = "Toggle the predator overlay showing dens, territories, groups, roles, movement targets, and active prey.",
			icon = TexCommand.GatherSpotActive,
			isActive = () => PredatorDevTools.OverlayEnabled,
			toggleAction = delegate
			{
				PredatorDevTools.OverlayEnabled = !PredatorDevTools.OverlayEnabled;
				Messages.Message("Predator overlay " + (PredatorDevTools.OverlayEnabled ? "enabled." : "disabled."), MessageTypeDefOf.NeutralEvent, historical: false);
			}
		};

		Command_Action goToDen = Action("DEV: Go to Den", "Force this predator to travel to its den.", TexCommand.Replant, delegate
		{
			bool started = component.DebugSendToDen(__instance, sleep: false);
			Messages.Message(started ? __instance.LabelShortCap + " is going to its den." : "Could not find a reachable den cell.", started ? MessageTypeDefOf.NeutralEvent : MessageTypeDefOf.RejectInput, historical: false);
		});
		Command_Action sleepAtDen = Action("DEV: Sleep at Den", "Force this predator to lie down and sleep at its den.", TexCommand.GatherSpotActive, delegate
		{
			bool started = component.DebugSendToDen(__instance, sleep: true);
			Messages.Message(started ? __instance.LabelShortCap + " is sleeping at its den." : "Could not find a reachable den cell.", started ? MessageTypeDefOf.NeutralEvent : MessageTypeDefOf.RejectInput, historical: false);
		});
		Command_Action gatherAtDen = Action("DEV: Gather at Den", "Force every available member of this predator group to gather at the den. This can be used to test resting, family life, and mating behavior.", TexCommand.SquadAttack, delegate
		{
			int count = component.DebugGatherAtDen(__instance);
			Messages.Message(count > 0 ? "Sent " + count + " predator(s) to the den." : "No group members could reach a den cell.", count > 0 ? MessageTypeDefOf.NeutralEvent : MessageTypeDefOf.RejectInput, historical: false);
		});
		if (!config.useDens)
		{
			const string reason = "Dens are disabled for this species.";
			goToDen.Disable(reason);
			sleepAtDen.Disable(reason);
			gatherAtDen.Disable(reason);
		}
		yield return goToDen;
		yield return sleepAtDen;
		yield return gatherAtDen;

		yield return Action("DEV: Set Den", "Choose a reachable tile to use as this predator or pack's den.", TexCommand.Replant, delegate
		{
			Find.Targeter.BeginTargeting(TargetingParameters.ForCell(), delegate(LocalTargetInfo target)
			{
				bool assigned = component.DebugSetDen(__instance, target.Cell);
				Messages.Message(assigned ? "Predator den updated." : "That cell is not a reachable den location.", assigned ? MessageTypeDefOf.NeutralEvent : MessageTypeDefOf.RejectInput, historical: false);
			});
		});
		yield return Action("DEV: Set Move Target", "Choose a reachable tile and immediately use it as this predator group's roaming target.", TexCommand.Replant, delegate
		{
			Find.Targeter.BeginTargeting(TargetingParameters.ForCell(), delegate(LocalTargetInfo target)
			{
				bool assigned = component.DebugSetMovementTarget(__instance, target.Cell);
				Messages.Message(assigned ? "Predator movement target updated." : "That cell is not a reachable movement target.", assigned ? MessageTypeDefOf.NeutralEvent : MessageTypeDefOf.RejectInput, historical: false);
			});
		});

		yield return Action("DEV: Initiate Hunt", "Choose prey for this predator or pack to hunt, beginning with its normal stealth phase.", TexCommand.Attack, delegate
		{
			Find.Targeter.BeginTargeting(TargetingParameters.ForPawns(), delegate(LocalTargetInfo target)
			{
				Pawn prey = target.Thing as Pawn;
				bool flag = component.DebugForceHunt(__instance, prey);
				Messages.Message(flag ? "Forced predator hunt started." : "Could not start the forced hunt.", flag ? MessageTypeDefOf.NeutralEvent : MessageTypeDefOf.RejectInput, historical: false);
			});
		});
		yield return Action("DEV: Auto Hunt Test", "Run 1, 3, 5, or 10 real hunts against unused prey of the selected species and write kills, escapes, hiding, detection, injuries, and duration to the log.", TexCommand.Attack, delegate
		{
			Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>
			{
				HuntTestOption(component, __instance, 1),
				HuntTestOption(component, __instance, 3),
				HuntTestOption(component, __instance, 5),
				HuntTestOption(component, __instance, 10)
			}));
		});
		yield return Action("DEV: Cancel Hunt Test", "Stop the active automated hunt test and write its partial summary.", TexCommand.CannotShoot, component.DebugCancelHuntTest);
		Command_Action forcePhase = Action("DEV: Hunt Phase", "Force the current hunt into stealth, positioning, or chase and restart active hunters' test jobs.", TexCommand.SquadAttack, delegate
		{
			Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>
			{
				PhaseOption(component, __instance, HuntPhase.Stealth),
				PhaseOption(component, __instance, HuntPhase.Positioning),
				PhaseOption(component, __instance, HuntPhase.Chase)
			}));
		});
		if (component.HuntPhaseFor(__instance) == HuntPhase.None) forcePhase.Disable("Start a hunt first.");
		yield return forcePhase;
		yield return Action("DEV: Clear Hunt", "Cancel this predator or pack's current hunt.", TexCommand.OpenLinkedQuestTex, delegate
		{
			component.DebugClearHunt(__instance);
			Messages.Message("Predator hunt cleared.", MessageTypeDefOf.NeutralEvent, historical: false);
		});
		yield return Action("DEV: Log State", "Log this predator's strategy, hunting style, group, roles, den, territory center, movement target, prey, and hunt phase.", TexCommand.OpenLinkedQuestTex, delegate
		{
			string state = component.DebugStateFor(__instance);
			Log.Message("[Packs and Predators]\n" + state);
			Messages.Message("Predator state written to the log.", MessageTypeDefOf.NeutralEvent, historical: false);
		});
	}

	private static FloatMenuOption PhaseOption(PackMapComponent component, Pawn pawn, HuntPhase phase)
	{
		return new FloatMenuOption("Force " + phase, delegate
		{
			bool applied = component.DebugForceHuntPhase(pawn, phase);
			Messages.Message(applied ? "Forced hunt phase: " + phase + "." : "Could not force that hunt phase.", applied ? MessageTypeDefOf.NeutralEvent : MessageTypeDefOf.RejectInput, historical: false);
		});
	}

	private static FloatMenuOption DetectionOutcomeOption(string label, TestRollMode mode)
	{
		return new FloatMenuOption(label, delegate
		{
			WildlifeTestLog.DetectionOutcome = mode;
			Messages.Message("Predator detection test outcome: " + label + ".", MessageTypeDefOf.NeutralEvent, historical: false);
		});
	}

	private static FloatMenuOption HuntTestOption(PackMapComponent component, Pawn hunter, int runs)
	{
		return new FloatMenuOption(runs + (runs == 1 ? " hunt" : " hunts"), delegate
		{
			Find.Targeter.BeginTargeting(TargetingParameters.ForPawns(), delegate(LocalTargetInfo target)
			{
				bool started = component.DebugStartHuntTest(hunter, target.Pawn, runs);
				Messages.Message(started ? "Automated hunt test started." : "Could not start the automated hunt test.", started ? MessageTypeDefOf.NeutralEvent : MessageTypeDefOf.RejectInput, historical: false);
			});
		});
	}

	private static FloatMenuOption BenchmarkOption(PackMapComponent component, int ticks, string label)
	{
		return new FloatMenuOption(label, delegate
		{
			component.DebugStartBenchmark(ticks);
			Messages.Message("Predator benchmark started: " + label + ".", MessageTypeDefOf.NeutralEvent, historical: false);
		});
	}

	private static Command_Action Action(string label, string description, Texture2D icon, Action action)
	{
		return new Command_Action
		{
			defaultLabel = label,
			defaultDesc = description,
			icon = icon,
			action = action
		};
	}

	internal static void OpenWildlifeDashboard()
	{
		Type type = AccessTools.TypeByName("Herds.WildlifeDevMaster"); MethodInfo method = AccessTools.Method(type, "OpenDashboard");
		if (method != null) method.Invoke(null, null); else Messages.Message("Herds and Hiders dashboard is unavailable.", MessageTypeDefOf.RejectInput, false);
	}

	internal static bool CompleteOverlayActive()
	{
		Type type = AccessTools.TypeByName("Herds.WildlifeDevMaster"); FieldInfo field = AccessTools.Field(type, "CompleteOverlayEnabled");
		return field != null ? (bool)field.GetValue(null) : PredatorDevTools.OverlayEnabled;
	}

	internal static void ToggleCompleteOverlay()
	{
		Type type = AccessTools.TypeByName("Herds.WildlifeDevMaster"); MethodInfo method = AccessTools.Method(type, "ToggleCompleteOverlay");
		if (method != null) method.Invoke(null, null); else PredatorDevTools.OverlayEnabled = !PredatorDevTools.OverlayEnabled;
	}

	private static void ShowPredatorTests(PackMapComponent component, Pawn predator, AnimalPackSettings config)
	{
		Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>
		{
			new FloatMenuOption("Initiate hunt", () => Find.Targeter.BeginTargeting(TargetingParameters.ForPawns(), target => component.DebugForceHunt(predator, target.Pawn))),
			new FloatMenuOption("Force hunt phase...", () => Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption> { PhaseOption(component, predator, HuntPhase.Stealth), PhaseOption(component, predator, HuntPhase.Positioning), PhaseOption(component, predator, HuntPhase.Chase) }))),
			new FloatMenuOption("Clear hunt", () => component.DebugClearHunt(predator)),
			new FloatMenuOption("Go to den", () => component.DebugSendToDen(predator, false)),
			new FloatMenuOption("Sleep at den", () => component.DebugSendToDen(predator, true)),
			new FloatMenuOption("Gather group at den", () => component.DebugGatherAtDen(predator)),
			new FloatMenuOption("Set den", () => Find.Targeter.BeginTargeting(TargetingParameters.ForCell(), target => component.DebugSetDen(predator, target.Cell))),
			new FloatMenuOption("Set movement target", () => Find.Targeter.BeginTargeting(TargetingParameters.ForCell(), target => component.DebugSetMovementTarget(predator, target.Cell))),
			new FloatMenuOption("Human boldness...", () => Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption> { new FloatMenuOption("Wary", () => component.DebugSetHumanBoldness(predator, 0.1f)), new FloatMenuOption("Cautious", () => component.DebugSetHumanBoldness(predator, 0.45f)), new FloatMenuOption("Bold", () => component.DebugSetHumanBoldness(predator, 0.9f)) }))),
			new FloatMenuOption("Automated hunt test...", () => Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption> { HuntTestOption(component, predator, 1), HuntTestOption(component, predator, 3), HuntTestOption(component, predator, 5), HuntTestOption(component, predator, 10) }))),
			new FloatMenuOption("Performance benchmark...", () => Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption> { BenchmarkOption(component, 15000, "Quarter day"), BenchmarkOption(component, 60000, "One day"), BenchmarkOption(component, 180000, "Three days") }))),
			new FloatMenuOption("Log predator state", () => Log.Message("[Packs and Predators]\n" + component.DebugStateFor(predator))),
			new FloatMenuOption("Expanded legacy gizmos: " + (expandedGizmos ? "ON" : "OFF"), () => expandedGizmos = !expandedGizmos)
		}));
	}
}
