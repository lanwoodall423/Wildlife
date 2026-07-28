using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using LudeonTK;
using RimWorld;
using UnityEngine;
using Verse;

namespace Herds
{
    public static class WildlifeDevMaster
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

        public static void OpenDashboard() => Find.WindowStack.Add(new Window_WildlifeDevelopmentOverview(Find.CurrentMap));

        private static void SetPacksDevField(string fieldName, bool value)
        {
            Type type = AccessTools.TypeByName("Packs.PredatorDevTools");
            FieldInfo field = type == null ? null : AccessTools.Field(type, fieldName);
            field?.SetValue(null, value);
        }

        public static List<string> PacksOverview(Map map)
        {
            MapComponent component = map?.components?.FirstOrDefault(item => item.GetType().FullName == "Packs.PackMapComponent");
            if (component == null) return new List<string> { "Packs and Predators is not active on this map." };
            MethodInfo method = AccessTools.Method(component.GetType(), "DebugOverviewLines");
            return method?.Invoke(component, null) as List<string> ?? new List<string> { "Predator overview API unavailable." };
        }

        public static string PacksPerformance(Map map)
        {
            MapComponent component = map?.components?.FirstOrDefault(item => item.GetType().FullName == "Packs.PackMapComponent");
            return AccessTools.Method(component?.GetType(), "PerformanceSummary")?.Invoke(component, null) as string ?? "Packs and Predators is not active.";
        }
    }

    public sealed class WildlifeUnifiedOverlayMapComponent : MapComponent
    {
        public WildlifeUnifiedOverlayMapComponent(Map map) : base(map) { }

        public override void MapComponentDraw()
        {
            base.MapComponentDraw();
            if (!Prefs.DevMode || !WildlifeDevMaster.CompleteOverlayEnabled ||
                Find.CurrentMap != map) return;
            CellRect view = Find.CameraDriver.CurrentViewRect;
            DrawProtectedAnimals(view);
            DrawCurrentMoment(view);
            DrawSelectedIntent(view);
        }

        public override void MapComponentOnGUI()
        {
            base.MapComponentOnGUI();
            if (!Prefs.DevMode || !WildlifeDevMaster.CompleteOverlayEnabled ||
                Find.CurrentMap != map) return;
            DrawLegend();
            CellRect view = Find.CameraDriver.CurrentViewRect;
            WildlifeOpportunityRecord moment =
                map.GetComponent<WildlifeFieldJournalMapComponent>()?.Opportunity;
            Pawn focal = moment?.animal;
            if (focal?.Spawned == true && view.Contains(focal.Position))
                GenMapUI.DrawThingLabel(focal, "MOMENT | " +
                    (moment.protectionDeclared ? "protected | " : "") +
                    (moment.response == WildlifeMomentResponse.None
                        ? "awaiting response" : moment.response.ToString().ToLowerInvariant()));
            IReadOnlyList<NotableAnimalRecord> records =
                map.GetComponent<NotableWildlifeMapComponent>()?.Records;
            if (records == null) return;
            for (int i = 0; i < records.Count; i++)
            {
                NotableAnimalRecord record = records[i];
                if (record?.intent != NotableAnimalIntent.Protect ||
                    record.animal == focal || record.animal?.Spawned != true ||
                    !view.Contains(record.animal.Position)) continue;
                GenMapUI.DrawThingLabel(record.animal, "PROTECTED | " + record.title);
            }
        }

        private void DrawProtectedAnimals(CellRect view)
        {
            IReadOnlyList<NotableAnimalRecord> records =
                map.GetComponent<NotableWildlifeMapComponent>()?.Records;
            if (records == null) return;
            IReadOnlyList<Pawn> colonists = map.mapPawns.FreeColonistsSpawned;
            for (int i = 0; i < records.Count; i++)
            {
                NotableAnimalRecord record = records[i];
                Pawn animal = record?.animal;
                if (record?.intent != NotableAnimalIntent.Protect ||
                    animal?.Spawned != true || !view.Contains(animal.Position)) continue;
                GenDraw.DrawRadiusRing(animal.Position, 1.45f,
                    new Color(0.25f, 1f, 0.42f, 0.95f));
                GenDraw.DrawRadiusRing(animal.Position, 55f,
                    new Color(0.25f, 1f, 0.42f, 0.18f));
                int drawn = 0;
                for (int pawnIndex = 0; pawnIndex < colonists.Count && drawn < 3; pawnIndex++)
                {
                    Pawn responder = colonists[pawnIndex];
                    if (responder?.Spawned != true || responder.Downed || responder.InMentalState ||
                        responder.WorkTagIsDisabled(WorkTags.Violent) ||
                        !responder.Position.InHorDistOf(animal.Position, 55f)) continue;
                    GenDraw.DrawLineBetween(responder.Position.ToVector3Shifted(),
                        animal.Position.ToVector3Shifted(), SimpleColor.Green);
                    drawn++;
                }
            }
        }

        private void DrawCurrentMoment(CellRect view)
        {
            WildlifeOpportunityRecord moment =
                map.GetComponent<WildlifeFieldJournalMapComponent>()?.Opportunity;
            if (moment == null) return;
            IntVec3 cell = moment.animal?.Spawned == true
                ? moment.animal.Position : moment.focusCell;
            if (!cell.IsValid || !cell.InBounds(map) || !view.Contains(cell)) return;
            Color color = moment.protectionDeclared
                ? new Color(0.25f, 1f, 0.42f, 0.95f)
                : new Color(0.75f, 0.48f, 1f, 0.95f);
            GenDraw.DrawRadiusRing(cell, 3.5f, color);
            if (moment.responder?.Spawned == true)
            {
                GenDraw.DrawLineBetween(moment.responder.Position.ToVector3Shifted(),
                    cell.ToVector3Shifted(), SimpleColor.Cyan);
                LocalTargetInfo observation = moment.responder.CurJob?.targetB ??
                    LocalTargetInfo.Invalid;
                if (observation.IsValid && observation.Cell.InBounds(map))
                {
                    GenDraw.DrawRadiusRing(observation.Cell, 0.9f, Color.cyan);
                    GenDraw.DrawLineBetween(moment.responder.Position.ToVector3Shifted(),
                        observation.Cell.ToVector3Shifted(), SimpleColor.Cyan);
                }
            }
            if (moment.evidence?.Spawned == true)
            {
                GenDraw.DrawRadiusRing(moment.evidence.Position, 0.75f,
                    new Color(0.48f, 0.34f, 0.18f, 0.95f));
                GenDraw.DrawLineBetween(cell.ToVector3Shifted(),
                    moment.evidence.Position.ToVector3Shifted(), SimpleColor.Yellow);
            }
        }

        private void DrawSelectedIntent(CellRect view)
        {
            Pawn pawn = Find.Selector?.SingleSelectedThing as Pawn;
            if (pawn?.Spawned != true || pawn.RaceProps?.Animal != true ||
                !view.Contains(pawn.Position) || pawn.CurJob == null) return;
            DrawTarget(pawn, pawn.CurJob.targetA, SimpleColor.White);
            DrawTarget(pawn, pawn.CurJob.targetB, SimpleColor.Cyan);
            DrawTarget(pawn, pawn.CurJob.targetC, SimpleColor.Yellow);
        }

        private void DrawTarget(Pawn pawn, LocalTargetInfo target, SimpleColor color)
        {
            if (!target.IsValid) return;
            IntVec3 cell = target.HasThing && target.Thing.Spawned
                ? target.Thing.Position : target.Cell;
            if (!cell.IsValid || !cell.InBounds(map) || cell == pawn.Position) return;
            GenDraw.DrawLineBetween(pawn.Position.ToVector3Shifted(),
                cell.ToVector3Shifted(), color);
            GenDraw.DrawRadiusRing(cell, 0.55f,
                color == SimpleColor.Cyan ? Color.cyan :
                color == SimpleColor.Yellow ? Color.yellow : Color.white);
        }

        private static void DrawLegend()
        {
            Rect panel = new Rect(UI.screenWidth - 344f, UI.screenHeight - 316f, 332f, 272f);
            Widgets.DrawMenuSection(panel);
            Rect inner = panel.ContractedBy(9f);
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inner.x, inner.y, inner.width, 28f), "Wildlife Overlay");
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.68f, 0.74f, 0.70f);
            Widgets.Label(new Rect(inner.x, inner.y + 29f, inner.width, 19f),
                "RINGS = ranges/states     LINES = targets/relationships");
            GUI.color = Color.white;
            float y = inner.y + 52f;
            DrawLegendRow(inner.x, ref y, Color.cyan,
                "Cyan — groups, awareness, signals, observation");
            DrawLegendRow(inner.x, ref y, new Color(1f, 0.55f, 0.1f),
                "Orange — predators, territories, positioning");
            DrawLegendRow(inner.x, ref y, Color.red,
                "Red — threats, prey targets, attacks, blood");
            DrawLegendRow(inner.x, ref y, Color.green,
                "Green — homes, protection, safe/ready states");
            DrawLegendRow(inner.x, ref y, new Color(0.48f, 0.34f, 0.18f),
                "Brown — tracks, signs, physical evidence");
            DrawLegendRow(inner.x, ref y, Color.magenta,
                "Purple — hiding, bonds, traditions, anomalies");
            DrawLegendRow(inner.x, ref y, Color.yellow,
                "Yellow — leaders, sentinels, staging, uncertainty");
            DrawLegendRow(inner.x, ref y, Color.white,
                "White — selected animal intent and direct links");
            Text.Font = GameFont.Small;
        }

        private static void DrawLegendRow(float x, ref float y, Color color, string label)
        {
            Widgets.DrawBoxSolid(new Rect(x, y + 3f, 13f, 13f), color);
            Widgets.Label(new Rect(x + 20f, y, 292f, 20f), label);
            y += 24f;
        }
    }

    public static class WildlifeDevMenus
    {
        public static bool ShowExpandedPreyGizmos;
        public static bool ShowExpandedColonistGizmos;

        public static Command_Toggle DiagnosticToggle(Pawn context) => new Command_Toggle
        {
            defaultLabel = "DEV: Diagnostic Log", defaultDesc = "Toggle the shared structured wildlife diagnostic session.", icon = TexCommand.OpenLinkedQuestTex,
            isActive = () => WildlifeTestLog.Enabled,
            toggleAction = () => { WildlifeTestLog.Toggle(); if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("Session", "Enabled from organized Dev controls", context); Messages.Message("Wildlife diagnostic logging " + (WildlifeTestLog.Enabled ? "enabled." : "disabled."), MessageTypeDefOf.NeutralEvent, false); }
        };

        public static Command_Toggle CompleteOverlayToggle() => new Command_Toggle
        {
            defaultLabel = "DEV: Unified Overlay", defaultDesc = "Toggle every prey, predator, hunt, signal, path, range, den, home, refuge, role, trail, moment, protection, warning, guardian, and knowledge visual together.", icon = TexCommand.GatherSpotActive,
            isActive = () => WildlifeDevMaster.CompleteOverlayEnabled, toggleAction = WildlifeDevMaster.ToggleCompleteOverlay
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
                new FloatMenuOption("Run full in-game test suite", WildlifeInGameTestSuite.Run),
                new FloatMenuOption("Refresh simulation", HerdsDebugActions.RefreshSimulation),
                new FloatMenuOption("Performance benchmark...", () => HerdsDebugActions.OpenBenchmarkMenu(prey.Map.GetComponent<HerdMapComponent>())),
                new FloatMenuOption("Individual overlays...", FieldcraftDebug.ShowOverlayMenu),
                new FloatMenuOption("Expanded legacy gizmos: " + (ShowExpandedPreyGizmos ? "ON" : "OFF"), () => ShowExpandedPreyGizmos = !ShowExpandedPreyGizmos)
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
                new FloatMenuOption("Run full in-game test suite", WildlifeInGameTestSuite.Run),
                new FloatMenuOption("Individual overlays...", FieldcraftDebug.ShowOverlayMenu),
                new FloatMenuOption("Expanded legacy gizmos: " + (ShowExpandedColonistGizmos ? "ON" : "OFF"), () => ShowExpandedColonistGizmos = !ShowExpandedColonistGizmos)
            }));
        }
    }

    public sealed class Window_WildlifeDevelopmentOverview : Window
    {
        private readonly Map map;
        private Vector2 scroll;
        private int tab;
        private static readonly string[] Tabs = { "Summary", "Prey", "Predators", "Hunts", "Homes & Tools", "Knowledge", "Regional", "Progression", "Performance" };
        public override Vector2 InitialSize => new Vector2(980f, 720f);
        public Window_WildlifeDevelopmentOverview(Map map) { this.map = map; doCloseX = true; resizeable = true; absorbInputAroundWindow = false; }

        public override void DoWindowContents(Rect rect)
        {
            float width = (rect.width - (Tabs.Length - 1) * 5f) / Tabs.Length;
            for (int i = 0; i < Tabs.Length; i++) if (Widgets.ButtonText(new Rect(i * (width + 5f), 0f, width, 32f), Tabs[i], active: i == tab)) { tab = i; scroll = Vector2.zero; }
            Rect controls = new Rect(0f, 40f, rect.width, 34f);
            if (Widgets.ButtonText(new Rect(0f, controls.y, 210f, 30f),
                WildlifeDevMaster.CompleteOverlayEnabled
                    ? "Disable unified overlay" : "Enable unified overlay"))
                WildlifeDevMaster.ToggleCompleteOverlay();
            if (Widgets.ButtonText(new Rect(218f, controls.y, 180f, 30f), WildlifeTestLog.Enabled ? "Stop diagnostic log" : "Start diagnostic log")) WildlifeTestLog.Toggle();
            if (Widgets.ButtonText(new Rect(406f, controls.y, 140f, 30f), "Refresh now")) map?.GetComponent<HerdMapComponent>()?.DebugRefresh();
            if (tab != 6 && Widgets.ButtonText(new Rect(554f, controls.y, 180f, 30f), "Run full test")) WildlifeInGameTestSuite.Run();
            if (tab == 6 && Widgets.ButtonText(new Rect(554f, controls.y, 180f, 30f), "Run ecology test")) map?.GetComponent<RegionalWildlifeMapComponent>()?.DebugRunRegionalDay();
            if (tab == 6 && Widgets.ButtonText(new Rect(742f, controls.y, 150f, 30f), "Force event")) map?.GetComponent<RegionalWildlifeMapComponent>()?.DebugForceEvent();
            List<string> lines = LinesForTab();
            Rect outer = new Rect(0f, 82f, rect.width, rect.height - 82f); Rect view = new Rect(0f, 0f, outer.width - 18f, Mathf.Max(outer.height, lines.Count * 25f + 12f));
            Widgets.BeginScrollView(outer, ref scroll, view);
            for (int i = 0; i < lines.Count; i++) { Rect row = new Rect(0f, i * 25f, view.width, 24f); if (i % 2 == 0) Widgets.DrawLightHighlight(row); Widgets.Label(new Rect(7f, row.y + 2f, row.width - 14f, 22f), lines[i]); }
            Widgets.EndScrollView();
        }

        private List<string> LinesForTab()
        {
            if (map == null) return new List<string> { "No current map." };
            HerdMapComponent herds = map.GetComponent<HerdMapComponent>();
            if (tab == 0) return herds.DebugSummaryLines().Concat(WildlifeDevMaster.PacksOverview(map).Take(3)).ToList();
            if (tab == 1) return herds.DebugPreyLines();
            if (tab == 2) return WildlifeDevMaster.PacksOverview(map);
            if (tab == 3) return map.GetComponent<WildlifeHuntCoordinator>().DebugOverviewLines().Concat(WildlifeDevMaster.PacksOverview(map).Where(line => line.StartsWith("HUNT"))).ToList();
            if (tab == 4) return herds.DebugHomeAndToolLines().Concat(map.GetComponent<WildlifeFieldcraftMapComponent>().DebugOverviewLines()).ToList();
            if (tab == 5) return map.GetComponent<HuntingKnowledgeMapComponent>().DebugOverviewLines();
            if (tab == 6)
                return map.GetComponent<RegionalWildlifeMapComponent>().DebugOverviewLines()
                    .Concat(map.GetComponent<HuntingExpeditionMapComponent>().DebugOverviewLines())
                    .Concat(map.GetComponent<WildlifeMemoryMapComponent>().DebugOverviewLines()).ToList();
            if (tab == 7) return Enum.GetValues(typeof(WildlifeCapability)).Cast<WildlifeCapability>()
                .Select(capability => WildlifeProgression.Status(capability).ToUpperInvariant() + " | " + WildlifeProgression.Label(capability) +
                    (WildlifeProgression.Unlocked(capability) ? "" : " | " + WildlifeProgression.LockReason(capability))).ToList();
            return new List<string> { herds.PerformanceSummary(), "", WildlifeDevMaster.PacksPerformance(map) };
        }
    }
}
