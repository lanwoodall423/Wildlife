using System.Collections.Generic;
using System.Collections;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Herds
{
    public static class WildlifeExpeditionCaravans
    {
        public static bool TryGet(Caravan caravan, out HuntingExpeditionMapComponent component,
            out HuntingExpeditionRecord record)
        {
            component = null;
            record = null;
            if (caravan == null || Find.Maps == null) return false;
            for (int i = 0; i < Find.Maps.Count; i++)
            {
                HuntingExpeditionMapComponent candidate = Find.Maps[i].GetComponent<HuntingExpeditionMapComponent>();
                HuntingExpeditionRecord found = candidate?.ActiveExpeditions.FirstOrDefault(item => item.caravan == caravan);
                if (found == null) continue;
                component = candidate;
                record = found;
                return true;
            }
            return false;
        }

        public static IEnumerable<Gizmo> StatusGizmos(Caravan caravan)
        {
            if (!TryGet(caravan, out HuntingExpeditionMapComponent component, out HuntingExpeditionRecord record))
                yield break;
            yield return new Command_Action
            {
                defaultLabel = "Expedition Status",
                defaultDesc = "Open expedition progress, outcome factors, and the field log.",
                icon = TexCommand.OpenLinkedQuestTex,
                action = () => Find.WindowStack.Add(new Window_HuntingExpeditionStatus(component, record))
            };
            yield return new Command_Action
            {
                defaultLabel = "View Expedition Route",
                defaultDesc = "Review the route, current world cell, biome changes, and danger.",
                icon = TexCommand.OpenLinkedQuestTex,
                action = () => Find.WindowStack.Add(new Window_ExpeditionRoute(component, record))
            };
            yield return new Command_Action
            {
                defaultLabel = "Recall Expedition",
                defaultDesc = "Abandon the objective and return home.",
                icon = TexCommand.CannotShoot,
                action = () => component.Cancel(record)
            };
        }
    }

    [HarmonyPatch(typeof(Caravan), nameof(Caravan.GetInspectString))]
    public static class WildlifeExpeditionCaravanInspectPatch
    {
        public static void Postfix(Caravan __instance, ref string __result)
        {
            if (!WildlifeExpeditionCaravans.TryGet(__instance, out HuntingExpeditionMapComponent component,
                out HuntingExpeditionRecord record)) return;
            string target = record.targetSpecies?.LabelCap.ToString() ??
                (record.objective == ExpeditionObjective.Scout ? "No target required" : "Unknown wildlife");
            string expedition = component.Status(record) + "\nObjective: " +
                Window_HuntingExpeditionSetup.ObjectiveLabel(record.objective) + "\nTarget: " + target +
                "\nParty: " + record.hunters.Count + " hunters, " + record.packAnimals.Count + " pack animals";
            __result = __result.NullOrEmpty() ? expedition : expedition + "\n" + __result.Trim();
        }
    }

    [HarmonyPatch(typeof(Caravan_PathFollower), nameof(Caravan_PathFollower.CostToMove),
        new[] { typeof(Caravan), typeof(PlanetTile), typeof(PlanetTile), typeof(int?) })]
    public static class WildlifeExpeditionCaravanTravelCostPatch
    {
        public static void Postfix(Caravan caravan, ref int __result)
        {
            if (!WildlifeExpeditionCaravans.TryGet(caravan, out _, out HuntingExpeditionRecord record)) return;
            float route = record.routePolicy == ExpeditionRoutePolicy.Fastest ? 0.82f :
                record.routePolicy == ExpeditionRoutePolicy.Safest ? 1.12f : 1f;
            float camp = record.bedrolls >= record.hunters.Count ? 0.94f : 1f;
            __result = Mathf.Max(1, Mathf.RoundToInt(__result * route * camp));
        }
    }

    [HarmonyPatch(typeof(Caravan), nameof(Caravan.GetGizmos))]
    public static class WildlifeExpeditionCaravanGizmosPatch
    {
        public static void Postfix(Caravan __instance, ref IEnumerable<Gizmo> __result)
        {
            if (!WildlifeExpeditionCaravans.TryGet(__instance, out _, out _)) return;
            __result = __result.Concat(WildlifeExpeditionCaravans.StatusGizmos(__instance));
        }
    }

    public sealed class WorldObject_HuntingExpeditionMarker : WorldObject
    {
        public int mapId = -1;
        public int expeditionId;

        public HuntingExpeditionMapComponent Component =>
            Find.Maps.Find(map => map.uniqueID == mapId)?.GetComponent<HuntingExpeditionMapComponent>();

        public HuntingExpeditionRecord Record => Component?.FindRecord(expeditionId);

        public override string Label
        {
            get
            {
                HuntingExpeditionRecord record = Record;
                return record == null ? base.Label : "Wildlife Expedition: " + HuntingExpeditionMapComponent.StageLabel(record.stage);
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref mapId, "mapId", -1);
            Scribe_Values.Look(ref expeditionId, "expeditionId", 0);
        }

        public override string GetInspectString()
        {
            HuntingExpeditionRecord record = Record;
            if (record == null) return "This expedition record is no longer available.";
            string target = record.targetSpecies?.LabelCap.ToString() ??
                (record.objective == ExpeditionObjective.Scout ? "No target required" :
                record.objective == ExpeditionObjective.Redirect ? "Unknown herd" : "Unknown wildlife");
            return Component.Status(record) + "\nObjective: " + Window_HuntingExpeditionSetup.ObjectiveLabel(record.objective) + "\nTarget: " + target +
                "\nParty: " + record.hunters.Count + " hunters, " + record.packAnimals.Count + " pack animals";
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos()) yield return gizmo;
            HuntingExpeditionRecord record = Record;
            if (record == null) yield break;
            yield return new Command_Action
            {
                defaultLabel = "Expedition Status",
                defaultDesc = "Open the expedition progress, outcome factors, and field log.",
                icon = TexCommand.OpenLinkedQuestTex,
                action = () => Find.WindowStack.Add(new Window_HuntingExpeditionStatus(Component, record))
            };
            yield return new Command_Action
            {
                defaultLabel = "View Expedition Route",
                defaultDesc = "Review the expedition route, current world cell, biome changes, and danger.",
                icon = TexCommand.OpenLinkedQuestTex,
                action = () => Find.WindowStack.Add(new Window_ExpeditionRoute(Component, record))
            };
            yield return new Command_Action
            {
                defaultLabel = "Recall Expedition",
                defaultDesc = "Order the party to abandon its objective and return home.",
                icon = TexCommand.CannotShoot,
                action = () => Component.Cancel(record)
            };
        }
    }

    public sealed class WorldDrawLayer_WildlifeExpeditionRoutes : WorldDrawLayer
    {
        public override bool Visible => HerdsMod.Settings?.enableOffMapHuntingExpeditions == true &&
            Find.Maps.Any(map => map.GetComponent<HuntingExpeditionMapComponent>()?.ActiveExpeditions.Count > 0) && base.Visible;

        public override IEnumerable Regenerate()
        {
            foreach (object item in base.Regenerate()) yield return item;
            if (HerdsMod.Settings?.enableOffMapHuntingExpeditions != true)
            {
                FinalizeMesh(MeshParts.Verts | MeshParts.Tris | MeshParts.Colors);
                yield break;
            }
            LayerSubMesh mesh = GetSubMesh(WorldMaterials.VertexColorTransparent);
            foreach (Map map in Find.Maps)
            {
                HuntingExpeditionMapComponent component = map.GetComponent<HuntingExpeditionMapComponent>();
                if (component == null) continue;
                foreach (HuntingExpeditionRecord expedition in component.ActiveExpeditions)
                {
                    if (expedition.routeTiles == null) continue;
                    for (int i = 0; i + 1 < expedition.routeTiles.Count; i++)
                    {
                        Vector3 a = planetLayer.GetTileCenter((PlanetTile)expedition.routeTiles[i]);
                        Vector3 b = planetLayer.GetTileCenter((PlanetTile)expedition.routeTiles[i + 1]);
                        float danger = Mathf.Clamp01(0.08f + (i + 1) * 0.045f +
                            Mathf.InverseLerp(1f, 4f, Find.WorldGrid[(PlanetTile)expedition.routeTiles[i + 1]].PrimaryBiome.movementDifficulty) * 0.18f);
                        Color32 color = danger < 0.22f ? new Color32(68, 171, 84, 210) :
                            danger < 0.40f ? new Color32(210, 160, 49, 220) : new Color32(214, 70, 55, 225);
                        AddSegment(mesh, a, b, color);
                    }
                }
            }
            FinalizeMesh(MeshParts.Verts | MeshParts.Tris | MeshParts.Colors);
        }

        private static void AddSegment(LayerSubMesh mesh, Vector3 start, Vector3 end, Color32 color)
        {
            start = start.normalized * (start.magnitude + 0.065f);
            end = end.normalized * (end.magnitude + 0.065f);
            Vector3 normal = (start + end).normalized;
            Vector3 side = Vector3.Cross(normal, (end - start).normalized).normalized * 0.055f;
            int first = mesh.verts.Count;
            mesh.verts.Add(start - side);
            mesh.verts.Add(start + side);
            mesh.verts.Add(end + side);
            mesh.verts.Add(end - side);
            for (int i = 0; i < 4; i++) mesh.colors.Add(color);
            mesh.tris.Add(first);
            mesh.tris.Add(first + 1);
            mesh.tris.Add(first + 2);
            mesh.tris.Add(first);
            mesh.tris.Add(first + 2);
            mesh.tris.Add(first + 3);
            mesh.tris.Add(first + 2);
            mesh.tris.Add(first + 1);
            mesh.tris.Add(first);
            mesh.tris.Add(first + 3);
            mesh.tris.Add(first + 2);
            mesh.tris.Add(first);
        }
    }
}
