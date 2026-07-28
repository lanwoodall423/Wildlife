using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Herds
{
    public sealed class HuntPlanOptions
    {
        public float riskTolerance = 0.5f;
        public bool useFieldcraftGear = true;
        public HashSet<string> selectedResources = new HashSet<string>();
    }

    public sealed class Building_HuntingSpot : Building
    {
        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos()) yield return gizmo;
            if (!HerdsMod.Settings.enableHuntingChanges) yield break;
            WildlifeHuntCoordinator coordinator = Map?.GetComponent<WildlifeHuntCoordinator>();
            bool active = coordinator?.TryGetSpotStatus(this, out _, out _, out _, out _) == true;
            if (HerdsMod.Settings.enableHuntingExpeditions)
            {
                Command_Action fieldcraft = new Command_Action
                {
                    defaultLabel = "Form Hunt",
                    defaultDesc = "Plan a coordinated hunt: select prey, colonists, fieldcraft resources, pursuit policy, and risk tolerance.",
                    icon = TexCommand.Attack,
                    action = () => Find.WindowStack.Add(new Window_FieldcraftHuntSetup(this))
                };
                if (!WildlifeProgression.Unlocked(WildlifeCapability.BasicHunting)) fieldcraft.Disable(WildlifeProgression.LockReason(WildlifeCapability.BasicHunting));
                else if (active) fieldcraft.Disable("This Hunting Spot is already coordinating a hunt.");
                yield return fieldcraft;
                if (active && coordinator.TryGetSpotStatus(this, out string status, out string details, out _, out Pawn prey))
                {
                    yield return new Command_Action
                    {
                        defaultLabel = status,
                        defaultDesc = details + "\n\nClick to jump to the prey.",
                        icon = TexCommand.GatherSpotActive,
                        action = () => { if (prey != null) CameraJumper.TryJump(prey); }
                    };
                    yield return new Command_Action
                    {
                        defaultLabel = "Cancel hunt",
                        defaultDesc = "Recall this spot's hunters and cancel its active expedition.",
                        icon = TexCommand.CannotShoot,
                        action = () => coordinator.CancelHuntsFromSpot(this)
                    };
                }
            }
        }

        public override string GetInspectString()
        {
            WildlifeHuntCoordinator coordinator = Map?.GetComponent<WildlifeHuntCoordinator>();
            return coordinator?.TryGetSpotStatus(this, out string status, out string details, out _, out _) == true
                ? status + "\n" + details
                : WildlifeProgression.Unlocked(WildlifeCapability.BasicHunting) ? "Ready to plan a fieldcraft hunt." : "Research locked: " + WildlifeProgression.LockReason(WildlifeCapability.BasicHunting);
        }

        public override void DrawExtraSelectionOverlays()
        {
            base.DrawExtraSelectionOverlays();
            if (!HerdsMod.Settings.enableHuntingChanges || !HerdsMod.Settings.enableHuntingExpeditions) return;
            Map?.GetComponent<WildlifeHuntCoordinator>()?.DrawForSpot(this);
        }

    }

    public sealed class Window_FieldcraftHuntSetup : Window
    {
        private readonly Building_HuntingSpot spot;
        private readonly HuntPlanOptions options;
        private readonly HashSet<Pawn> selected;
        private Pawn prey;
        private Vector2 scroll;
        private Vector2 resourceScroll;
        private bool initializedResourceDefaults;

        public override Vector2 InitialSize => new Vector2(760f, 720f);

        public Window_FieldcraftHuntSetup(Building_HuntingSpot spot, IEnumerable<Pawn> selectedHunters = null, HuntPlanOptions options = null, Pawn prey = null)
        {
            this.spot = spot;
            this.options = options ?? new HuntPlanOptions();
            this.prey = prey;
            selected = selectedHunters != null ? new HashSet<Pawn>(selectedHunters) : new HashSet<Pawn>();
            if (selectedHunters == null && spot?.Map != null)
                foreach (Pawn pawn in EligibleColonists().OrderByDescending(ColonistHuntingUtility.HuntingSkill).Take(MaxHunters)) selected.Add(pawn);
            doCloseX = true;
            absorbInputAroundWindow = true;
        }

        public override void DoWindowContents(Rect rect)
        {
            if (spot?.Spawned != true) { Widgets.Label(rect, "The hunting spot is unavailable."); return; }
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, rect.width, 32f), "Plan Hunt");
            Text.Font = GameFont.Small;

            string target = prey?.Spawned == true ? prey.LabelShortCap + " | health " + prey.health.summaryHealth.SummaryHealthPercent.ToStringPercent() : "No prey selected";
            Widgets.DrawLightHighlight(new Rect(0f, 36f, rect.width, 34f));
            Widgets.Label(new Rect(8f, 41f, rect.width - 188f, 28f), "1. Target — " + target);
            if (Widgets.ButtonText(new Rect(rect.width - 170f, 38f, 170f, 30f), prey == null ? "Select Prey" : "Change Prey")) ShowPreyMenu();

            List<Pawn> colonists = EligibleColonists();
            Widgets.DrawLightHighlight(new Rect(0f, 74f, rect.width, 30f));
            Widgets.Label(new Rect(8f, 78f, 310f, 24f), "2. Hunters — " + selected.Count + " selected" + (MaxHunters == 2 ? " / 2 before Fieldcraft" : ""));
            if (Widgets.ButtonText(new Rect(320f, 74f, 95f, 28f), "Select all"))
                foreach (Pawn pawn in colonists.Take(MaxHunters)) selected.Add(pawn);
            if (Widgets.ButtonText(new Rect(423f, 74f, 95f, 28f), "Clear")) selected.Clear();
            Rect outer = new Rect(0f, 106f, rect.width, 190f);
            Rect view = new Rect(0f, 0f, outer.width - 18f, Mathf.Max(outer.height, colonists.Count * 30f));
            Widgets.BeginScrollView(outer, ref scroll, view);
            HuntingKnowledgeMapComponent knowledge = spot.Map.GetComponent<HuntingKnowledgeMapComponent>();
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn hunter = colonists[i];
                bool chosen = selected.Contains(hunter);
                float skill = prey == null ? ColonistHuntingUtility.HuntingSkill(hunter) : ColonistHuntingUtility.HuntingSkill(hunter, prey.def);
                string knowledgeLabel = prey == null ? "target-dependent knowledge" : HuntingKnowledgeMapComponent.LevelLabel(knowledge.Level(hunter, prey.def));
                string weapon = hunter.equipment?.Primary?.LabelShortCap.ToString() ?? "Unarmed";
                Widgets.CheckboxLabeled(new Rect(4f, i * 30f, view.width - 8f, 28f), hunter.LabelShortCap + " | Skill " + skill.ToString("0.0") + " | " + knowledgeLabel + " | " + weapon, ref chosen);
                if (chosen && !selected.Contains(hunter) && selected.Count >= MaxHunters) chosen = false;
                if (chosen) selected.Add(hunter); else selected.Remove(hunter);
            }
            Widgets.EndScrollView();

            float retreatAt = Mathf.Lerp(0.72f, 0.30f, options.riskTolerance);
            int launchOffset = Mathf.RoundToInt((0.5f - options.riskTolerance) * 240f);
            string risk = options.riskTolerance < 0.34f ? "Cautious" : options.riskTolerance < 0.67f ? "Balanced" : "Bold";
            Widgets.DrawLightHighlight(new Rect(0f, 302f, rect.width, 30f));
            Widgets.Label(new Rect(8f, 306f, rect.width - 16f, 24f), "3. Hunt Policy — Risk: " + risk + " | Retreat below " + retreatAt.ToStringPercent() + " health | Launch " + (launchOffset >= 0 ? "+" : "") + (launchOffset / 60f).ToString("0.0") + "s");
            Rect riskChoices = new Rect(0f, 332f, rect.width, 28f);
            DrawRiskChoices(riskChoices);
            TooltipHandler.TipRegion(riskChoices, "Cautious keeps distance and retreats sooner. Balanced uses standard positioning. Bold closes distance and retreats later. Risk never changes accuracy or Hunting Skill.");
            Widgets.Label(new Rect(0f, 362f, rect.width, 20f), "Cautious: farther and safer   •   Balanced: standard   •   Bold: closer and persistent");

            HuntResourceDiscovery discovery = Current.Game?.GetComponent<HuntResourceDiscovery>();
            discovery?.Refresh(spot.Map);
            List<HuntResourceDef> resources = HerdsMod.Settings.enableFieldcraftEquipment && WildlifeProgression.Unlocked(WildlifeCapability.Fieldcraft)
                ? DefDatabase<HuntResourceDef>.AllDefsListForReading.Where(def => discovery?.IsDiscovered(def) == true && (!def.enabledByScentMasking || HerdsMod.Settings.enableScentMasking)).OrderBy(def => def.label).ToList()
                : new List<HuntResourceDef>();
            if (!initializedResourceDefaults)
            {
                for (int i = 0; i < resources.Count; i++) if (CountAvailable(resources[i]) >= resources[i].RequiredFor(selected.Count)) options.selectedResources.Add(resources[i].defName);
                initializedResourceDefaults = true;
            }
            Widgets.DrawLightHighlight(new Rect(0f, 388f, rect.width, 30f));
            Widgets.Label(new Rect(8f, 392f, rect.width - 16f, 24f), WildlifeProgression.Unlocked(WildlifeCapability.Fieldcraft)
                ? "4. Resources — Within 12 cells of the spot or carried by selected hunters"
                : "4. Resources — Advanced equipment unlocks with Fieldcraft");
            Rect resourceOuter = new Rect(0f, 418f, rect.width, 88f);
            if (resources.Count == 0) Widgets.Label(new Rect(10f, 426f, rect.width - 20f, 28f), "None available.");
            else
            {
                Rect resourceView = new Rect(0f, 0f, resourceOuter.width - 18f, Mathf.Max(resourceOuter.height, resources.Count * 28f));
                Widgets.BeginScrollView(resourceOuter, ref resourceScroll, resourceView);
                for (int i = 0; i < resources.Count; i++)
                {
                    HuntResourceDef resource = resources[i];
                    int available = CountAvailable(resource);
                    bool enabled = options.selectedResources.Contains(resource.defName);
                    ResourceCheckbox(new Rect(10f, i * 28f, resourceView.width - 20f, 26f), resource.LabelCap + ": " + available + " available / " + resource.RequirementLabel(selected.Count), ref enabled, available);
                    if (enabled) options.selectedResources.Add(resource.defName); else options.selectedResources.Remove(resource.defName);
                }
                Widgets.EndScrollView();
            }

            List<Pawn> qualified = QualifiedHunters();
            string readiness = prey == null ? "Select Prey to calculate species knowledge and readiness." : qualified.Count + "/" + selected.Count + " selected hunters meet effective Skill " + HerdsMod.Settings.minimumFieldcraftSkill + ".";
            Widgets.DrawLightHighlight(new Rect(0f, 514f, rect.width - 195f, 48f));
            Widgets.Label(new Rect(8f, 518f, rect.width - 211f, 44f), "5. Readiness — " + readiness);
            string reason = null;
            WildlifeHuntCoordinator coordinator = spot.Map.GetComponent<WildlifeHuntCoordinator>();
            bool canStart = prey?.Spawned == true;
            if (!canStart) reason = "Select an available prey target.";
            else if (selected.Count == 0) { canStart = false; reason = "Select at least one hunter."; }
            else if (selected.Count > MaxHunters) { canStart = false; reason = "Fieldcraft is required to coordinate more than two hunters."; }
            else if (qualified.Count != selected.Count) { canStart = false; reason = "Every selected hunter must meet effective Skill " + HerdsMod.Settings.minimumFieldcraftSkill + "."; }
            else if (resources.FirstOrDefault(resource => options.selectedResources.Contains(resource.defName) && CountAvailable(resource) < resource.RequiredFor(selected.Count)) is HuntResourceDef missing) { canStart = false; reason = "Not enough " + missing.label + " for the selected hunters."; }
            else if (!spot.Map.GetComponent<WildlifeStewardMapComponent>().CanHunt(prey.def, out reason)) canStart = false;
            if (canStart && coordinator.HasActiveHunt(prey)) { canStart = false; reason = prey.LabelShortCap + " is already targeted by another coordinated hunt."; }
            if (!canStart && !reason.NullOrEmpty()) Widgets.Label(new Rect(0f, 570f, rect.width - 195f, 40f), reason);
            Rect beginRect = new Rect(rect.width - 185f, rect.height - 44f, 185f, 40f);
            if (!canStart) TooltipHandler.TipRegion(beginRect, reason ?? "Complete all hunt requirements first.");
            if (Widgets.ButtonText(beginRect, "Begin Hunt", active: canStart))
            {
                coordinator.Begin(prey, selected.ToList(), options, spot);
                Close();
            }
        }

        private void DrawRiskChoices(Rect rect)
        {
            string[] labels = { "Cautious", "Balanced", "Bold" };
            float[] values = { 0.2f, 0.5f, 0.8f };
            float gap = 5f;
            float width = (rect.width - gap * 2f) / 3f;
            int current = options.riskTolerance < 0.34f ? 0 : options.riskTolerance < 0.67f ? 1 : 2;
            options.riskTolerance = values[current];
            for (int i = 0; i < 3; i++)
            {
                Rect button = new Rect(rect.x + i * (width + gap), rect.y, width, rect.height);
                if (i == current) Widgets.DrawHighlightSelected(button);
                if (Widgets.ButtonText(button, labels[i])) options.riskTolerance = values[i];
            }
        }

        private List<Pawn> EligibleColonists()
        {
            return spot.Map.mapPawns.FreeColonistsSpawned.Where(pawn => pawn?.Spawned == true && !pawn.Downed && !pawn.WorkTagIsDisabled(WorkTags.Violent)).OrderBy(pawn => pawn.LabelShortCap.ToString()).ToList();
        }

        private int MaxHunters => WildlifeProgression.Unlocked(WildlifeCapability.Fieldcraft) ? 99 : 2;

        private List<Pawn> QualifiedHunters()
        {
            if (prey == null) return new List<Pawn>();
            return selected.Where(pawn => pawn?.Spawned == true && ColonistHuntingUtility.HuntingSkill(pawn, prey.def) >= HerdsMod.Settings.minimumFieldcraftSkill).ToList();
        }

        private void ShowPreyMenu()
        {
            List<Pawn> possible = spot.Map.mapPawns.AllPawnsSpawned
                .Where(target => target?.Spawned == true && !target.Dead && !target.Downed && target.Faction != Faction.OfPlayer && PreyProfileDatabase.IsEligible(target.def))
                .OrderBy(target => target.Position.DistanceToSquared(spot.Position))
                .ThenBy(target => target.LabelShortCap.ToString())
                .ToList();
            if (possible.Count == 0)
            {
                Messages.Message("No eligible prey are currently available on this map.", MessageTypeDefOf.RejectInput, false);
                return;
            }
            List<FloatMenuOption> choices = new List<FloatMenuOption>(possible.Count);
            for (int i = 0; i < possible.Count; i++)
            {
                Pawn candidate = possible[i];
                float distance = candidate.Position.DistanceTo(spot.Position);
                string label = candidate.LabelShortCap + " — " + distance.ToString("0") + " cells — Health " + candidate.health.summaryHealth.SummaryHealthPercent.ToStringPercent();
                choices.Add(new FloatMenuOption(label, () => prey = candidate));
            }
            Find.WindowStack.Add(new FloatMenu(choices));
        }

        private int CountAvailable(HuntResourceDef resource)
        {
            if (resource == null) return 0;
            if (resource.use == HuntResourceUse.ScentChargePerHunter) return AvailableScentCharges(resource.sourceBuildingDef);
            ThingDef def = resource.thingDef;
            if (def == null) return 0;
            int count = 0;
            List<Thing> mapThings = spot.Map.listerThings.ThingsOfDef(def);
            for (int i = 0; i < mapThings.Count; i++) if (mapThings[i].Position.DistanceToSquared(spot.Position) <= 144) count += mapThings[i].stackCount;
            foreach (Pawn pawn in selected) if (pawn?.inventory?.innerContainer != null) count += pawn.inventory.innerContainer.Where(thing => thing.def == def).Sum(thing => thing.stackCount);
            return count;
        }

        private int AvailableScentCharges(ThingDef stationDef)
        {
            if (stationDef == null) return 0;
            int count = 0;
            List<Thing> stations = spot.Map.listerThings.ThingsOfDef(stationDef);
            for (int i = 0; i < stations.Count; i++) if (stations[i] is Building_WildlifeTool station && station.active && station.Position.DistanceToSquared(spot.Position) <= 144) count += Mathf.Max(0, station.scentCharges);
            return count;
        }

        private static void ResourceCheckbox(Rect rect, string label, ref bool enabled, int available)
        {
            bool unavailable = available <= 0;
            if (unavailable) enabled = false;
            Widgets.CheckboxLabeled(rect, label + (unavailable ? " — Unavailable" : ""), ref enabled, unavailable);
        }
    }
}
