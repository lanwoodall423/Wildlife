using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Herds
{
    public enum WildlifeWorldMapMode
    {
        None,
        ExpeditionDestination,
        RegionalKnowledge
    }

    public static class WildlifeWorldMapController
    {
        private static HuntingExpeditionMapComponent component;
        private static Window returnWindow;
        private static Action<ExpeditionDestination> destinationChosen;
        private static ThingDef focusSpecies;
        private static WildlifeWorldMapMode mode;
        private static ExpeditionDestination selectedDestination;
        private static bool accepted;
        private static bool starting;

        public static bool Active => mode != WildlifeWorldMapMode.None && component != null && Find.WorldTargeter?.IsTargeting == true;
        public static HuntingExpeditionMapComponent Component => component;
        public static ThingDef FocusSpecies => focusSpecies;
        public static WildlifeWorldMapMode Mode => mode;

        public static void BeginDestinationSelection(HuntingExpeditionMapComponent source, Window planner,
            Action<ExpeditionDestination> onChosen)
        {
            Begin(source, planner, WildlifeWorldMapMode.ExpeditionDestination, null, onChosen);
        }

        public static void BeginNewExpeditionSelection(HuntingExpeditionMapComponent source, Window expeditionList)
        {
            Begin(source, expeditionList, WildlifeWorldMapMode.ExpeditionDestination, null,
                destination => returnWindow = new Window_HuntingExpeditionSetup(source.HomeMap, destination));
        }

        public static void BeginRegionalMap(HuntingExpeditionMapComponent source, Window overview, ThingDef species = null)
        {
            Begin(source, overview, WildlifeWorldMapMode.RegionalKnowledge, species, null);
        }

        private static void Begin(HuntingExpeditionMapComponent source, Window backWindow, WildlifeWorldMapMode newMode,
            ThingDef species, Action<ExpeditionDestination> onChosen)
        {
            if (source?.HomeMap == null || Find.WorldTargeter == null) return;
            component = source;
            returnWindow = backWindow;
            destinationChosen = onChosen;
            focusSpecies = species;
            mode = newMode;
            selectedDestination = null;
            accepted = false;
            HideWindowsForSelection();
            CameraJumper.TryShowWorld();
            CameraJumper.TryJump(source.HomeMap.Tile);
            source.HomeMap.Tile.Layer.SetDirty<WorldDrawLayer_WildlifeKnowledgeFog>();
            starting = true;
            try
            {
                Find.WorldTargeter.BeginTargeting(
                    SelectTarget,
                    true,
                    null,
                    false,
                    null,
                    TargetLabel,
                    CanSelect,
                    source.HomeMap.Tile,
                    true);
            }
            finally
            {
                starting = false;
            }
        }

        private static void HideWindowsForSelection()
        {
            foreach (Window window in Find.WindowStack.Windows.ToList())
                window?.Close(false);
            if (Find.MainTabsRoot?.OpenTab?.TabWindow != null)
                Find.MainTabsRoot.EscapeCurrentTab(false);
        }

        private static bool CanSelect(GlobalTargetInfo target)
        {
            if (!target.IsWorldTarget || component == null) return false;
            return component.CanExpeditionTo((int)target.Tile);
        }

        private static bool SelectTarget(GlobalTargetInfo target)
        {
            selectedDestination = component?.DestinationForTile((int)target.Tile,
                mode == WildlifeWorldMapMode.ExpeditionDestination);
            if (selectedDestination == null) return false;
            accepted = true;
            if (mode == WildlifeWorldMapMode.ExpeditionDestination) destinationChosen?.Invoke(selectedDestination);
            return true;
        }

        private static TaggedString TargetLabel(GlobalTargetInfo target)
        {
            ExpeditionDestination destination = component?.DestinationForTile((int)target.Tile, false);
            if (destination == null) return "Unavailable: settlements, outposts, water, and impassable tiles cannot be selected";
            ExpeditionCellRecord knowledge = destination.knowledge;
            string range = destination.distance + (destination.distance == 1 ? " tile" : " tiles") + " from the colony";
            if (knowledge.discoveryLevel <= 0)
                return "?\nUnknown region\n" + range + "\nSelect to " +
                    (mode == WildlifeWorldMapMode.ExpeditionDestination ? "plan an expedition" : "inspect regional knowledge");
            string animals = component.KnownSpecies(destination).Count == 0 ? "No animal signs identified" :
                string.Join(", ", component.KnownSpecies(destination).Take(3).Select(def => def.LabelCap.ToString()));
            string focus = focusSpecies == null ? animals : SpeciesTileLabel(destination, focusSpecies);
            return component.TileKnowledgeLabel(destination) + "\n" + range + "\n" + focus;
        }

        private static string SpeciesTileLabel(ExpeditionDestination destination, ThingDef species)
        {
            ExpeditionCellSpeciesRecord record = destination?.knowledge?.species?.FirstOrDefault(item => item.species == species);
            if (record == null) return "No known " + species.label + " signs";
            if (destination.knowledge.confidence < 0.35f) return species.LabelCap + ": uncertain signs";
            return species.LabelCap + ": " + (record.population < 2f ? "sparse" : record.population < 7f ? "moderate" : "abundant") + " signs";
        }

        public static void NotifyTargetingStopped()
        {
            if (starting || mode == WildlifeWorldMapMode.None) return;
            HuntingExpeditionMapComponent oldComponent = component;
            Window oldWindow = returnWindow;
            ThingDef oldSpecies = focusSpecies;
            WildlifeWorldMapMode oldMode = mode;
            ExpeditionDestination chosen = selectedDestination;
            bool wasAccepted = accepted;
            Clear();
            if (oldComponent?.HomeMap?.Tile.Valid == true)
                oldComponent.HomeMap.Tile.Layer.SetDirty<WorldDrawLayer_WildlifeKnowledgeFog>();
            if (oldMode == WildlifeWorldMapMode.ExpeditionDestination)
            {
                CameraJumper.TryHideWorld();
                if (oldWindow != null) Find.WindowStack.Add(oldWindow);
            }
            else if (wasAccepted && chosen != null)
                Find.WindowStack.Add(new Window_RegionalTileKnowledge(oldComponent, chosen, oldSpecies, oldWindow));
            else
            {
                CameraJumper.TryHideWorld();
                if (oldWindow != null) Find.WindowStack.Add(oldWindow);
            }
        }

        private static void Clear()
        {
            component = null;
            returnWindow = null;
            destinationChosen = null;
            focusSpecies = null;
            mode = WildlifeWorldMapMode.None;
            selectedDestination = null;
            accepted = false;
            starting = false;
        }
    }

    [HarmonyPatch(typeof(WorldTargeter), nameof(WorldTargeter.StopTargeting))]
    public static class WildlifeWorldTargeterStopPatch
    {
        public static void Postfix() => WildlifeWorldMapController.NotifyTargetingStopped();
    }

    public sealed class WITab_Nature : WITab
    {
        private Vector2 scroll;

        public WITab_Nature()
        {
            size = new Vector2(500f, 520f);
            labelKey = "Herds_Nature";
        }

        public override bool IsVisible => SelPlanetTile.Valid && NatureWorldUI.Enabled;

        protected override void FillTab()
        {
            HuntingExpeditionMapComponent component = NatureWorldUI.Component;
            int tileId = SelPlanetTile;
            ExpeditionCellRecord knowledge = component?.ExistingKnowledgeForTile(tileId);
            bool known = knowledge?.discoveryLevel > 0;
            Rect rect = new Rect(0f, 0f, size.x, size.y).ContractedBy(12f);

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, 30f), known ? "Nature" : "?  Nature");
            Text.Font = GameFont.Small;
            GUI.color = new Color(0.67f, 0.76f, 0.68f);
            Widgets.Label(new Rect(rect.x, rect.y + 31f, rect.width, 22f),
                known ? (knowledge.discoveryLevel >= 2 ? "Surveyed wildlife record" : "Expedition route record") : "Undiscovered");
            GUI.color = Color.white;

            Rect summary = new Rect(rect.x, rect.y + 62f, rect.width, known ? 112f : 168f);
            Widgets.DrawMenuSection(summary);
            if (!known)
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                Text.Font = GameFont.Medium;
                Widgets.Label(new Rect(summary.x + 12f, summary.y + 20f, summary.width - 24f, 44f), "?");
                Text.Font = GameFont.Small;
                Widgets.Label(new Rect(summary.x + 24f, summary.y + 70f, summary.width - 48f, 62f),
                    "Unknown\nAn expedition must travel through this tile before any nature information is revealed.");
                Text.Anchor = TextAnchor.UpperLeft;
                return;
            }

            Tile tile = Find.WorldGrid?[SelPlanetTile];
            string biome = tile?.PrimaryBiome?.LabelCap.ToString() ?? "Unknown biome";
            string level = knowledge.discoveryLevel >= 2 ? "Scouted" : "Traveled Through";
            Widgets.Label(new Rect(summary.x + 12f, summary.y + 10f, summary.width - 24f, 24f), biome + "  •  " + level);
            string visits = knowledge.discoveryLevel >= 2
                ? "Surveys: " + knowledge.visits + "  •  Traversals: " + knowledge.traversals
                : "Traversals: " + knowledge.traversals;
            Widgets.Label(new Rect(summary.x + 12f, summary.y + 39f, summary.width - 24f, 22f), visits);
            Widgets.Label(new Rect(summary.x + 12f, summary.y + 65f, 112f, 22f), "Confidence");
            Widgets.FillableBar(new Rect(summary.x + 124f, summary.y + 69f, summary.width - 136f, 13f), knowledge.confidence);
            TooltipHandler.TipRegion(summary, "Travel reveals the route and biome. Scouting reveals wildlife signs and improves confidence, making estimates more precise.");

            Rect details = new Rect(rect.x, summary.yMax + 10f, rect.width, rect.yMax - summary.yMax - 10f);
            Widgets.DrawMenuSection(details);
            if (knowledge.discoveryLevel < 2)
            {
                Widgets.Label(details.ContractedBy(12f),
                    "This tile was observed while traveling.\n\nIts wildlife, hazards, and notable natural features remain unknown until an expedition scouts the area.");
                return;
            }

            Rect view = new Rect(0f, 0f, details.width - 18f, Mathf.Max(details.height, 248f + knowledge.species.Count * 27f));
            Widgets.BeginScrollView(details.ContractedBy(8f), ref scroll, view);
            float y = 0f;
            Widgets.Label(new Rect(0f, y, view.width, 24f), "Field Discovery");
            y += 27f;
            Widgets.Label(new Rect(8f, y, view.width - 16f, 42f),
                knowledge.discovery.NullOrEmpty() ? "No notable natural feature recorded." :
                knowledge.discovery + "\n" + HuntingExpeditionMapComponent.DiscoveryEffect(knowledge.discovery));
            y += 54f;
            Widgets.Label(new Rect(0f, y, view.width, 24f), "Known Wildlife");
            y += 29f;
            List<ExpeditionCellSpeciesRecord> records = knowledge.species
                .Where(record => record?.species != null && record.population > 0.05f)
                .Where(record => !HerdsMod.Settings.enableSpeciesKnowledgeProgression ||
                    HuntingKnowledgeMapComponent.ColonyExperience(record.species) > 0f)
                .OrderByDescending(record => record.population)
                .ToList();
            if (records.Count == 0)
            {
                Widgets.Label(new Rect(8f, y, view.width - 16f, 28f), "No recognized animal signs.");
            }
            else
            {
                for (int i = 0; i < records.Count; i++)
                {
                    ExpeditionCellSpeciesRecord record = records[i];
                    string amount = knowledge.confidence < 0.35f ? "Uncertain" :
                        record.population < 2f ? "Sparse" : record.population < 7f ? "Moderate" : "Abundant";
                    Widgets.Label(new Rect(8f, y, view.width - 16f, 24f), record.species.LabelCap + "  •  " + amount);
                    y += 27f;
                }
            }
            Widgets.EndScrollView();
        }
    }

    public static class NatureWorldUI
    {
        public static bool Enabled => HerdsMod.Settings != null &&
            (HerdsMod.Settings.enableRegionalMap || HerdsMod.Settings.enableOffMapHuntingExpeditions) &&
            ExpeditionResearchComplete;

        private static bool ExpeditionResearchComplete
        {
            get
            {
                ResearchProjectDef research = DefDatabase<ResearchProjectDef>.GetNamedSilentFail("Wildlife_HuntingExpedition");
                return research?.IsFinished == true;
            }
        }

        public static HuntingExpeditionMapComponent Component
        {
            get
            {
                Map home = Find.Maps?.FirstOrDefault(candidate => candidate?.IsPlayerHome == true) ??
                    Find.CurrentMap;
                return home?.GetComponent<HuntingExpeditionMapComponent>();
            }
        }
    }

    public static class WildlifeExpeditionTilePlanning
    {
        public static bool Available(HuntingExpeditionMapComponent component, PlanetTile tile)
        {
            return component != null && tile.Valid &&
                HerdsMod.Settings?.enableOffMapHuntingExpeditions == true &&
                WildlifeProgression.Unlocked(WildlifeCapability.HuntingExpedition) &&
                component.CanExpeditionTo((int)tile);
        }

        public static void Open(HuntingExpeditionMapComponent component, PlanetTile tile)
        {
            if (!Available(component, tile)) return;
            ExpeditionDestination destination = component.DestinationForTile((int)tile, false);
            CameraJumper.TryHideWorld();
            Find.WindowStack.Add(new Window_HuntingExpeditionSetup(component.HomeMap, destination));
        }
    }

    [HarmonyPatch(typeof(GizmoGridDrawer), nameof(GizmoGridDrawer.DrawGizmoGrid))]
    public static class WildlifeSendExpeditionGizmoPatch
    {
        private static readonly MethodInfo FormCaravanAction =
            AccessTools.Method(typeof(WorldGizmoUtility), "GetFormCaravanAction");

        public static void Prefix(ref IEnumerable<Gizmo> gizmos)
        {
            if (!WorldRendererUtility.WorldSelected) return;
            PlanetTile tile = Find.WorldSelector.SelectedTile;
            HuntingExpeditionMapComponent component = NatureWorldUI.Component;
            if (!WildlifeExpeditionTilePlanning.Available(component, tile)) return;
            List<Gizmo> commands = gizmos?.ToList() ?? new List<Gizmo>();
            if (commands.OfType<Command>().Any(command => command.defaultLabel == "Send Expedition")) return;
            Action action = () => WildlifeExpeditionTilePlanning.Open(component, tile);
            Gizmo gizmo = FormCaravanAction?.Invoke(null, new object[]
            {
                "Send Expedition",
                "Plan a wildlife expedition to this tile from the colony.",
                action
            }) as Gizmo ?? new Command_Action
            {
                defaultLabel = "Send Expedition",
                defaultDesc = "Plan a wildlife expedition to this tile from the colony.",
                icon = TexCommand.OpenLinkedQuestTex,
                action = action
            };
            commands.Add(gizmo);
            gizmos = commands;
        }
    }

    [HarmonyPatch(typeof(WorldInspectPane), "get_CurTabs")]
    public static class NatureWorldInspectTabsPatch
    {
        public static void Postfix(ref IEnumerable<InspectTabBase> __result)
        {
            if (!NatureWorldUI.Enabled) return;
            __result = AddNature(__result);
        }

        private static IEnumerable<InspectTabBase> AddNature(IEnumerable<InspectTabBase> original)
        {
            bool found = false;
            if (original != null)
            {
                foreach (InspectTabBase tab in original)
                {
                    if (tab is WITab_Nature) found = true;
                    yield return tab;
                }
            }
            if (!found) yield return InspectTabManager.GetSharedInstance(typeof(WITab_Nature));
        }
    }

    [HarmonyPatch(typeof(WorldInterface), nameof(WorldInterface.WorldInterfaceOnGUI))]
    public static class NatureWorldUnknownHoverPatch
    {
        public static void Postfix()
        {
            if (!NatureWorldUI.Enabled || Find.WorldTargeter?.IsTargeting == true) return;
            PlanetTile tile = GenWorld.MouseTile(false);
            if (!tile.Valid) return;
            ExpeditionCellRecord knowledge = NatureWorldUI.Component?.ExistingKnowledgeForTile((int)tile);
            if (knowledge?.discoveryLevel > 0) return;
            Vector2 mouse = Event.current.mousePosition;
            Text.Anchor = TextAnchor.MiddleCenter;
            Text.Font = GameFont.Medium;
            GUI.color = new Color(0.86f, 0.88f, 0.84f);
            Widgets.Label(new Rect(mouse.x + 13f, mouse.y + 12f, 28f, 28f), "?");
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
        }
    }

    public sealed class WorldDrawLayer_WildlifeKnowledgeFog : WorldDrawLayer
    {
        public override bool Visible => WildlifeWorldMapController.Active && base.Visible;

        public override IEnumerable Regenerate()
        {
            foreach (object item in base.Regenerate()) yield return item;
            HuntingExpeditionMapComponent component = WildlifeWorldMapController.Component;
            if (component == null)
            {
                FinalizeMesh(MeshParts.Verts | MeshParts.Tris | MeshParts.Colors);
                yield break;
            }
            LayerSubMesh mesh = GetSubMesh(WorldMaterials.VertexColorTransparent);
            List<Vector3> tileVertices = new List<Vector3>(8);
            for (int i = 0; i < planetLayer.TilesCount; i++)
            {
                PlanetTile tile = planetLayer.PlanetTileForID(i);
                int tileId = tile;
                if (!component.CanExpeditionTo(tileId)) continue;
                ExpeditionCellRecord knowledge = component.ExistingKnowledgeForTile(tileId);
                Color32 color = TileColor(knowledge, WildlifeWorldMapController.FocusSpecies);
                AddTile(mesh, tileId, color, tileVertices);
            }
            FinalizeMesh(MeshParts.Verts | MeshParts.Tris | MeshParts.Colors);
        }

        private Color32 TileColor(ExpeditionCellRecord knowledge, ThingDef focus)
        {
            if (knowledge == null || knowledge.discoveryLevel <= 0) return new Color32(76, 79, 82, 246);
            if (focus != null)
            {
                ExpeditionCellSpeciesRecord record = knowledge.species.FirstOrDefault(item => item.species == focus);
                if (record == null) return new Color32(104, 96, 80, 185);
                float density = Mathf.InverseLerp(0f, 16f, record.population);
                Color color = Color.Lerp(new Color(0.42f, 0.32f, 0.18f, 0.68f), new Color(0.20f, 0.62f, 0.26f, 0.55f), density);
                return color;
            }
            return knowledge.discoveryLevel == 1
                ? new Color32(112, 118, 105, 175)
                : new Color32(70, 130, 82, (byte)Mathf.Lerp(145f, 75f, knowledge.confidence));
        }

        private void AddTile(LayerSubMesh mesh, int tileId, Color32 color, List<Vector3> vertices)
        {
            vertices.Clear();
            planetLayer.GetTileVertices((PlanetTile)tileId, vertices);
            if (vertices.Count < 3) return;
            Vector3 center = planetLayer.GetTileCenter((PlanetTile)tileId);
            center = center.normalized * (center.magnitude + 0.035f);
            int first = mesh.verts.Count;
            mesh.verts.Add(center);
            mesh.colors.Add(color);
            for (int i = 0; i < vertices.Count; i++)
            {
                Vector3 vertex = vertices[i];
                mesh.verts.Add(vertex.normalized * (vertex.magnitude + 0.035f));
                mesh.colors.Add(color);
            }
            for (int i = 0; i < vertices.Count; i++)
            {
                int a = first + 1 + i;
                int b = first + 1 + (i + 1) % vertices.Count;
                mesh.tris.Add(first);
                mesh.tris.Add(a);
                mesh.tris.Add(b);
                mesh.tris.Add(first);
                mesh.tris.Add(b);
                mesh.tris.Add(a);
            }
        }
    }

    public sealed class Window_RegionalTileKnowledge : Window
    {
        private readonly HuntingExpeditionMapComponent component;
        private readonly ExpeditionDestination destination;
        private readonly ThingDef focusSpecies;
        private readonly Window overview;
        public override Vector2 InitialSize => new Vector2(620f, 520f);

        public Window_RegionalTileKnowledge(HuntingExpeditionMapComponent component, ExpeditionDestination destination,
            ThingDef focusSpecies, Window overview)
        {
            this.component = component;
            this.destination = destination;
            this.focusSpecies = focusSpecies;
            this.overview = overview;
            doCloseX = false;
            absorbInputAroundWindow = true;
        }

        public override void DoWindowContents(Rect rect)
        {
            bool known = destination?.knowledge?.discoveryLevel > 0;
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, rect.width, 32f), known ? destination.biome.LabelCap.ToString() + " Region" : "Unknown Region");
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(0f, 35f, rect.width, 24f), destination.distance + (destination.distance == 1 ? " tile" : " tiles") + " from the colony");
            Rect knowledge = new Rect(0f, 70f, rect.width, 96f);
            Widgets.DrawMenuSection(knowledge);
            if (!known)
                Widgets.Label(knowledge.ContractedBy(12f), "No expedition has traveled through this tile. Its terrain, routes, hazards, and wildlife remain unknown.");
            else
            {
                Widgets.Label(new Rect(knowledge.x + 12f, knowledge.y + 10f, knowledge.width - 24f, 24f), component.TileKnowledgeLabel(destination));
                string hazard = destination.knowledge.discoveryLevel < 2 ? "hazards unknown" :
                    destination.danger < 0.22f ? "low hazard" : destination.danger < 0.4f ? "moderate hazard" : "high hazard";
                string route = "Route: " + (destination.road ? "Road observed" : destination.river ? "River crossing observed" : "No major route observed") +
                    "  •  " + hazard;
                Widgets.Label(new Rect(knowledge.x + 12f, knowledge.y + 38f, knowledge.width - 24f, 22f), route);
                Widgets.FillableBar(new Rect(knowledge.x + 12f, knowledge.y + 68f, knowledge.width - 24f, 12f), destination.knowledge.confidence);
                TooltipHandler.TipRegion(knowledge, "Confidence improves when expeditions visit and survey this tile. It controls the precision of wildlife and hazard estimates.");
            }
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 180f, rect.width, 28f), focusSpecies == null ? "Known Wildlife Signs" : focusSpecies.LabelCap.ToString() + " Signs");
            Text.Font = GameFont.Small;
            Rect signs = new Rect(0f, 214f, rect.width, 172f);
            Widgets.DrawMenuSection(signs);
            if (!known || destination.knowledge.discoveryLevel < 2)
                Widgets.Label(signs.ContractedBy(12f), known ? "This route was observed in passing, but its wildlife has not been surveyed." : "Wildlife information is unknown.");
            else
            {
                List<ExpeditionCellSpeciesRecord> records = destination.knowledge.species
                    .Where(record => record?.species != null && (focusSpecies == null || record.species == focusSpecies))
                    .Where(record => !HerdsMod.Settings.enableSpeciesKnowledgeProgression || HuntingKnowledgeMapComponent.ColonyExperience(record.species) > 0f)
                    .OrderByDescending(record => record.population)
                    .ToList();
                if (records.Count == 0) Widgets.Label(signs.ContractedBy(12f), focusSpecies == null ? "No animal signs have been identified." : "No known signs of this animal.");
                for (int i = 0; i < records.Count && i < 5; i++)
                {
                    ExpeditionCellSpeciesRecord record = records[i];
                    string amount = destination.knowledge.confidence < 0.35f ? "Uncertain" :
                        record.population < 2f ? "Sparse" : record.population < 7f ? "Moderate" : "Abundant";
                    Widgets.Label(new Rect(signs.x + 12f, signs.y + 10f + i * 29f, signs.width - 24f, 25f), record.species.LabelCap + "  •  " + amount);
                }
            }
            if (!destination.knowledge.discovery.NullOrEmpty())
                Widgets.Label(new Rect(0f, 396f, rect.width, 28f), "Field discovery: " + destination.knowledge.discovery);
            if (Widgets.ButtonText(new Rect(0f, rect.height - 40f, 180f, 36f), "Explore Another Tile"))
            {
                Close(false);
                WildlifeWorldMapController.BeginRegionalMap(component, overview, focusSpecies);
            }
            if (Widgets.ButtonText(new Rect(rect.width - 210f, rect.height - 40f, 210f, 36f), "Return to Regional Overview"))
            {
                Close(false);
                CameraJumper.TryHideWorld();
                if (overview != null) Find.WindowStack.Add(overview);
            }
        }
    }
}
