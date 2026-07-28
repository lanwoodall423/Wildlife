using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Herds
{
    public sealed class Window_InteractiveExpeditionEncounter : Window
    {
        private readonly HuntingExpeditionMapComponent component;
        private readonly HuntingExpeditionRecord record;
        public override Vector2 InitialSize => new Vector2(650f, record?.roamingEncounterAnimal != null ? 610f : 430f);

        public Window_InteractiveExpeditionEncounter(HuntingExpeditionMapComponent component, HuntingExpeditionRecord record)
        {
            this.component = component;
            this.record = record;
            doCloseX = false;
            closeOnCancel = false;
            absorbInputAroundWindow = true;
            forcePause = true;
        }

        public override void DoWindowContents(Rect rect)
        {
            if (record?.roamingEncounterAnimal != null)
            {
                DrawRoamingEncounter(rect);
                return;
            }
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, rect.width, 34f), "Expedition Encounter");
            Text.Font = GameFont.Small;
            GUI.color = new Color(0.67f, 0.76f, 0.68f);
            Widgets.Label(new Rect(0f, 36f, rect.width, 24f), record?.interactiveEncounter ?? "Field signs");
            GUI.color = Color.white;
            Rect report = new Rect(0f, 70f, rect.width, 92f);
            Widgets.DrawMenuSection(report);
            Widgets.Label(report.ContractedBy(12f), EncounterReport());
            DrawChoice(new Rect(0f, 176f, rect.width, 58f), "Follow the Signs",
                ObjectiveFollowDescription(), 0);
            DrawChoice(new Rect(0f, 242f, rect.width, 58f), "Document and Proceed",
                "Gain regional confidence and Animal Knowledge with a short delay.", 1);
            DrawChoice(new Rect(0f, 308f, rect.width, 58f), "Take a Careful Detour",
                "Reduce danger, but lose time and some chance of finding game.", 2);
        }

        private void DrawRoamingEncounter(Rect rect)
        {
            Pawn animal = record.roamingEncounterAnimal;
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, rect.width, 34f), "Roaming Animal Encounter");
            Text.Font = GameFont.Small;
            Widgets.DrawMenuSection(new Rect(0f, 44f, rect.width, 74f));
            Widgets.Label(new Rect(12f, 55f, rect.width - 24f, 52f),
                "The party encountered " + animal.LabelShortCap + ", a known " + animal.def.label +
                " currently roaming beyond the colony map.");
            string[] labels = { "Observe", "Help", "Hunt", "Tag", "Redirect", "Avoid" };
            string[] descriptions =
            {
                "Gain Animal Knowledge and regional confidence without disturbance.",
                "Assist the animal, building trust and encouraging an earlier return.",
                "Attempt a field hunt. Skill is critical and failure increases danger.",
                "Fit a tracking collar so telemetry can follow future movements.",
                "Guide the animal toward managed habitat and encourage its return.",
                "Continue safely without affecting the animal."
            };
            for (int i = 0; i < labels.Length; i++)
                DrawChoice(new Rect(0f, 130f + i * 68f, rect.width, 58f), labels[i], descriptions[i], i);
        }

        private string EncounterReport()
        {
            string objective = record?.objective == ExpeditionObjective.Scout ? "survey" :
                record?.objective == ExpeditionObjective.Capture ? "capture" :
                record?.objective == ExpeditionObjective.Tag ? "tagging" :
                record?.objective == ExpeditionObjective.Redirect ? "herd redirection" : "hunt";
            return "During the " + objective + ", the party found " +
                (record?.interactiveEncounter ?? "field signs").ToLowerInvariant() +
                ". Their Animal Knowledge and fieldcraft helped identify three viable responses.";
        }

        private string ObjectiveFollowDescription()
        {
            if (record?.objective == ExpeditionObjective.Scout)
                return "Reveal more regional information quickly, with greater exposure and delay risk.";
            if (record?.objective == ExpeditionObjective.Capture || record?.objective == ExpeditionObjective.Tag)
                return "Improve contact and positioning chances, but risk alarming or injuring the animal.";
            if (record?.objective == ExpeditionObjective.Redirect)
                return "Locate the herd's movement line, accepting greater danger and a moderate delay.";
            return "Improve encounter and engagement chances, with greater danger and a moderate delay.";
        }

        private void DrawChoice(Rect rect, string label, string description, int choice)
        {
            Widgets.DrawMenuSection(rect);
            Widgets.Label(new Rect(rect.x + 10f, rect.y + 6f, 190f, 24f), label);
            GUI.color = new Color(0.72f, 0.78f, 0.72f);
            Widgets.Label(new Rect(rect.x + 202f, rect.y + 6f, rect.width - 214f, 42f), description);
            GUI.color = Color.white;
            Widgets.DrawHighlightIfMouseover(rect);
            if (!Widgets.ButtonInvisible(rect)) return;
            component?.ResolveInteractiveEncounter(record, choice);
            Close(false);
        }
    }

    public sealed class Window_ExpeditionRange : Window
    {
        private readonly Map map;
        private Vector2 scroll;
        public override Vector2 InitialSize => new Vector2(900f, 680f);

        public Window_ExpeditionRange(Map map)
        {
            this.map = map;
            doCloseX = true;
            resizeable = true;
            absorbInputAroundWindow = true;
        }

        public override void DoWindowContents(Rect rect)
        {
            HuntingExpeditionMapComponent component = map?.GetComponent<HuntingExpeditionMapComponent>();
            if (component == null)
            {
                Widgets.Label(rect, "Expedition information is unavailable.");
                return;
            }
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, rect.width, 32f), "Expedition Range");
            Text.Font = GameFont.Small;
            GUI.color = new Color(0.72f, 0.78f, 0.72f);
            Widgets.Label(new Rect(0f, 32f, rect.width, 22f),
                "Reachable world cells expand with Wildlife Stewardship" +
                (WildlifeProgression.Unlocked(WildlifeCapability.Telemetry)
                    ? " and Wildlife Telemetry" : "") +
                ". Scouting improves their information.");
            GUI.color = Color.white;
            List<ExpeditionDestination> destinations = component.Destinations();
            Rect header = new Rect(0f, 62f, rect.width, 28f);
            Widgets.DrawMenuSection(header);
            Widgets.Label(new Rect(10f, 65f, 70f, 24f), "Range");
            Widgets.Label(new Rect(82f, 65f, 190f, 24f), "Biome");
            Widgets.Label(new Rect(280f, 65f, 125f, 24f), "Survey");
            Widgets.Label(new Rect(410f, 65f, 175f, 24f), "Known Animals");
            Widgets.Label(new Rect(590f, 65f, rect.width - 600f, 24f), "Field Discovery");
            Rect outer = new Rect(0f, 96f, rect.width, rect.height - 96f);
            Rect view = new Rect(0f, 0f, outer.width - 18f, Mathf.Max(outer.height, destinations.Count * 58f));
            Widgets.BeginScrollView(outer, ref scroll, view);
            for (int i = 0; i < destinations.Count; i++)
            {
                ExpeditionDestination destination = destinations[i];
                Rect row = new Rect(0f, i * 58f, view.width, 52f);
                Widgets.DrawMenuSection(row);
                Widgets.DrawHighlightIfMouseover(row);
                bool known = destination.knowledge.discoveryLevel > 0;
                Widgets.Label(new Rect(row.x + 10f, row.y + 8f, 70f, 24f), destination.distance + (destination.distance == 1 ? " cell" : " cells"));
                Widgets.Label(new Rect(row.x + 82f, row.y + 8f, 190f, 24f), known ? destination.biome?.LabelCap.ToString() ?? "Unknown" : "Unknown");
                Widgets.Label(new Rect(row.x + 280f, row.y + 8f, 125f, 24f),
                    !known ? "Unknown" : destination.knowledge.discoveryLevel == 1 ? "Traversed" : destination.knowledge.confidence.ToStringPercent());
                List<ThingDef> species = component.KnownSpecies(destination);
                string animals = !known ? "Unknown" : species.Count == 0 ? "None identified" :
                    string.Join(", ", species.Take(3).Select(def => def.LabelCap.ToString())) + (species.Count > 3 ? " +" + (species.Count - 3) : "");
                Widgets.Label(new Rect(row.x + 410f, row.y + 8f, 175f, 38f), animals);
                Widgets.Label(new Rect(row.x + 590f, row.y + 8f, row.width - 600f, 38f),
                    !known ? "Unknown" : destination.knowledge.discovery.NullOrEmpty() ? "None" : destination.knowledge.discovery);
                string route = !known ? "No expedition has traveled through this tile." :
                    (destination.road ? "Road access. " : "") + (destination.river ? "River crossing. " : "") +
                    "Travel difficulty " + destination.travelFactor.ToString("0.0") + "; danger " + destination.danger.ToStringPercent() + ". " +
                    HuntingExpeditionMapComponent.DiscoveryEffect(destination.knowledge.discovery);
                TooltipHandler.TipRegion(row, route);
            }
            Widgets.EndScrollView();
            if (destinations.Count == 0)
                Widgets.Label(new Rect(8f, 106f, rect.width - 16f, 40f), "No passable expedition destinations are currently reachable.");
        }
    }

    public sealed class Window_HuntingExpeditionSetup : Window
    {
        private readonly Map map;
        private readonly ExpeditionPlan plan = new ExpeditionPlan();
        private ExpeditionDestination initialDestination;
        private readonly HashSet<Pawn> selectedHunters = new HashSet<Pawn>();
        private readonly HashSet<Pawn> selectedPackAnimals = new HashSet<Pawn>();
        private Vector2 partyScroll;
        private Vector2 resourceScroll;
        private bool initialized;

        public override Vector2 InitialSize => new Vector2(940f, 760f);

        public Window_HuntingExpeditionSetup(Map map)
            : this(map, null)
        {
        }

        public Window_HuntingExpeditionSetup(Map map, ExpeditionDestination destination)
            : this(map, destination, null, ExpeditionObjective.Hunt)
        {
        }

        public Window_HuntingExpeditionSetup(Map map, ExpeditionDestination destination,
            ThingDef targetSpecies, ExpeditionObjective objective)
        {
            this.map = map;
            initialDestination = destination;
            plan.targetSpecies = targetSpecies;
            plan.objective = objective;
            doCloseX = true;
            absorbInputAroundWindow = true;
            resizeable = true;
        }

        public override void DoWindowContents(Rect rect)
        {
            if (map == null)
            {
                Widgets.Label(rect, "The colony map is unavailable.");
                return;
            }
            HuntingExpeditionMapComponent component = map.GetComponent<HuntingExpeditionMapComponent>();
            EnsureInitialized(component);
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, rect.width, 32f), "Plan Wildlife Expedition");
            Text.Font = GameFont.Small;
            GUI.color = new Color(0.72f, 0.78f, 0.72f);
            Widgets.Label(new Rect(0f, 30f, rect.width, 24f), "Choose a real world cell, objective, party, route, and supplies. Predictions remain uncertain until the area is surveyed.");
            GUI.color = Color.white;

            DrawChoiceRow(new Rect(0f, 58f, rect.width, 34f), "1. Objective", ObjectiveLabel(plan.objective), "Change", ShowObjectiveMenu);
            string destination = plan.destination == null ? "No destination selected" :
                plan.destination.knowledge.discoveryLevel <= 0
                    ? "Unknown region • " + plan.destination.distance + " tile" + (plan.destination.distance == 1 ? "" : "s") + " away"
                    : component.TileKnowledgeLabel(plan.destination) +
                        (plan.destination.knowledge.discovery.NullOrEmpty() ? "" : " • " + plan.destination.knowledge.discovery);
            DrawChoiceRow(new Rect(0f, 96f, rect.width, 34f), "2. Destination", destination, "World Map", () => ShowDestinationMenu(component));
            string target = plan.objective == ExpeditionObjective.Scout ? "Not required for scouting" :
                plan.unknownTarget ? "Unknown — search for suitable wildlife" :
                plan.targetSpecies == null ? "No target selected" :
                plan.targetSpecies.LabelCap + " • " + PopulationLabel(component, plan.destination, plan.targetSpecies);
            DrawChoiceRow(new Rect(0f, 134f, rect.width, 34f), "3. Target", target, plan.objective == ExpeditionObjective.Scout ? "Not Required" : "Choose Animal", () => ShowTargetMenu(component),
                plan.objective != ExpeditionObjective.Scout);

            Widgets.DrawLightHighlight(new Rect(0f, 174f, rect.width, 30f));
            Widgets.Label(new Rect(8f, 178f, rect.width - 310f, 24f), "4. Expedition Party — " + selectedHunters.Count + " hunter" + (selectedHunters.Count == 1 ? "" : "s") + ", " + selectedPackAnimals.Count + " pack animal" + (selectedPackAnimals.Count == 1 ? "" : "s"));
            if (Widgets.ButtonText(new Rect(rect.width - 292f, 175f, 136f, 28f), "Best Hunters"))
            {
                selectedHunters.Clear();
                foreach (Pawn pawn in EligibleHunters().Where(CanJoinCurrentObjective).OrderByDescending(ColonistHuntingUtility.HuntingSkill).Take(4)) selectedHunters.Add(pawn);
            }
            if (Widgets.ButtonText(new Rect(rect.width - 148f, 175f, 140f, 28f), "Clear Party"))
            {
                selectedHunters.Clear();
                selectedPackAnimals.Clear();
            }
            List<Pawn> hunters = EligibleHunters();
            List<Pawn> packs = EligiblePackAnimals();
            float partyHeight = 182f;
            Rect partyOuter = new Rect(0f, 206f, rect.width, partyHeight);
            int partyRows = 2 + hunters.Count + packs.Count;
            Rect partyView = new Rect(0f, 0f, partyOuter.width - 18f, Mathf.Max(partyOuter.height, partyRows * 30f));
            Widgets.BeginScrollView(partyOuter, ref partyScroll, partyView);
            float y = 0f;
            DrawSubheader(new Rect(0f, y, partyView.width, 28f), "Hunters");
            y += 30f;
            for (int i = 0; i < hunters.Count; i++, y += 30f)
            {
                Pawn hunter = hunters[i];
                bool chosen = selectedHunters.Contains(hunter);
                bool pacifist = hunter.WorkTagIsDisabled(WorkTags.Violent);
                bool blocked = plan.objective == ExpeditionObjective.Hunt && pacifist;
                if (blocked)
                {
                    chosen = false;
                    selectedHunters.Remove(hunter);
                }
                float skill = plan.targetSpecies == null ? ColonistHuntingUtility.HuntingSkill(hunter) : ColonistHuntingUtility.HuntingSkill(hunter, plan.targetSpecies);
                bool biomeKnown = plan.destination?.knowledge?.discoveryLevel > 0;
                int specialist = biomeKnown ? component.SpecialistLevel(hunter, plan.destination.biome) : 0;
                string weapon = hunter.equipment?.Primary?.LabelShortCap.ToString() ?? "Unarmed";
                string biomeExperience = biomeKnown ? specialist.ToString() : "Unknown";
                Rect hunterRow = new Rect(6f, y, partyView.width - 12f, 28f);
                GUI.enabled = !blocked;
                Widgets.CheckboxLabeled(new Rect(6f, y, partyView.width - 12f, 28f), hunter.LabelShortCap + " • Skill " + skill.ToString("0.0") + " • " + weapon + " • Biome experience " + biomeExperience, ref chosen);
                GUI.enabled = true;
                if (blocked) TooltipHandler.TipRegion(hunterRow, "Pacifist: incapable of violence. This colonist can join scouting and other wildlife expeditions, but cannot join a Hunt.");
                if (chosen && selectedHunters.Count < 8) selectedHunters.Add(hunter); else if (!chosen) selectedHunters.Remove(hunter);
            }
            DrawSubheader(new Rect(0f, y, partyView.width, 28f), "Pack Animals");
            y += 30f;
            if (packs.Count == 0)
            {
                Widgets.Label(new Rect(8f, y, partyView.width - 16f, 26f), "None available. Pack animals increase carrying capacity and slightly reduce travel time.");
                y += 30f;
            }
            else for (int i = 0; i < packs.Count; i++, y += 30f)
            {
                Pawn animal = packs[i];
                bool chosen = selectedPackAnimals.Contains(animal);
                Widgets.CheckboxLabeled(new Rect(6f, y, partyView.width - 12f, 28f), animal.LabelShortCap + " • " + animal.def.LabelCap + " • Carry support " + Mathf.RoundToInt(Mathf.Max(12f, animal.BodySize * 35f)), ref chosen);
                if (chosen) selectedPackAnimals.Add(animal); else selectedPackAnimals.Remove(animal);
            }
            Widgets.EndScrollView();

            plan.hunters = selectedHunters.ToList();
            plan.packAnimals = selectedPackAnimals.ToList();
            float estimatedDays = component.EstimateDays(plan);
            float nutrition = ExpeditionSupplyUtility.RequiredNutrition(plan, estimatedDays);
            float selectedNutrition = ExpeditionSupplyUtility.SelectedNutrition(plan.provisions);
            float dailyNutrition = ExpeditionSupplyUtility.DailyNutrition(plan);
            plan.foodDays = dailyNutrition <= 0f ? 0 : Mathf.Clamp(Mathf.FloorToInt((selectedNutrition - nutrition) / dailyNutrition), 0, 3);
            plan.medicine = plan.medicines.Values.Sum();
            int bedrollsAvailable = ExpeditionSupplyUtility.AvailableBedrolls(map);

            Widgets.DrawLightHighlight(new Rect(0f, 394f, rect.width, 30f));
            Widgets.Label(new Rect(8f, 398f, rect.width - 16f, 24f), "5. Logistics — " + estimatedDays.ToString("0.0") + " days • " +
                selectedNutrition.ToString("0.0") + " / " + nutrition.ToString("0.0") + " nutrition • Carry capacity " + CarryCapacity().ToString("0"));
            Rect medicineButton = new Rect(8f, 430f, rect.width * 0.46f, 26f);
            if (Widgets.ButtonText(medicineButton, "Medicine: " + plan.medicine + " selected"))
                Find.WindowStack.Add(new Window_ExpeditionMedicine(map, plan.medicines));
            bool bedrolls = plan.useBedrolls;
            bool enoughBedrolls = bedrollsAvailable >= selectedHunters.Count && selectedHunters.Count > 0;
            if (!enoughBedrolls) bedrolls = false;
            Rect bedrollRect = new Rect(rect.width * 0.51f, 430f, rect.width * 0.47f, 24f);
            Widgets.CheckboxLabeled(bedrollRect, "Packed bedrolls (" + bedrollsAvailable + " available)", ref bedrolls, !enoughBedrolls);
            TooltipHandler.TipRegion(bedrollRect, "Optional. One bedroll per hunter reduces total expedition time by about 8%. Bedrolls are returned.");
            plan.useBedrolls = bedrolls;
            Rect provisionsButton = new Rect(8f, 460f, rect.width * 0.46f, 26f);
            if (Widgets.ButtonText(provisionsButton, "Provisions: " + selectedNutrition.ToString("0.0") + " / " + nutrition.ToString("0.0")))
                Find.WindowStack.Add(new Window_ExpeditionProvisions(map, plan, estimatedDays));
            bool alternatives = plan.allowAlternatives;
            Widgets.CheckboxLabeled(new Rect(rect.width * 0.51f, 460f, rect.width * 0.47f, 24f), "Accept alternative game", ref alternatives);
            plan.allowAlternatives = alternatives;

            Rect riskRect = new Rect(8f, 488f, rect.width * 0.46f, 24f);
            DrawRiskChoices(riskRect, ref plan.riskTolerance);
            TooltipHandler.TipRegion(riskRect, "Higher risk increases the chance the party presses on after difficult conditions and accepts dangerous engagement opportunities. It also raises injury and, if enabled, death risk.");
            Rect routeRect = new Rect(rect.width * 0.51f, 486f, rect.width * 0.47f, 28f);
            if (Widgets.ButtonText(routeRect, "Route: " + RouteLabel(plan.routePolicy))) ShowRouteMenu();
            TooltipHandler.TipRegion(routeRect, "Safest reduces incident risk but takes longer. Balanced uses normal travel and risk. Fastest shortens travel but accepts more danger.");

            List<HuntResourceDef> resources = AvailableResourceDefs();
            Widgets.DrawLightHighlight(new Rect(0f, 522f, rect.width, 30f));
            Widgets.Label(new Rect(8f, 526f, rect.width - 16f, 24f), "6. Field Equipment — discovered resources available anywhere in colony storage");
            Rect resourceOuter = new Rect(0f, 554f, rect.width, 82f);
            if (resources.Count == 0) Widgets.Label(new Rect(8f, 560f, rect.width - 16f, 26f), "None available.");
            else
            {
                Rect resourceView = new Rect(0f, 0f, resourceOuter.width - 18f, Mathf.Max(resourceOuter.height, resources.Count * 28f));
                Widgets.BeginScrollView(resourceOuter, ref resourceScroll, resourceView);
                for (int i = 0; i < resources.Count; i++)
                {
                    HuntResourceDef resource = resources[i];
                    int count = CountResource(resource);
                    int required = resource.RequiredFor(selectedHunters.Count);
                    bool chosen = plan.resources.Contains(resource.defName);
                    bool unavailable = count < required;
                    if (unavailable) chosen = false;
                    Widgets.CheckboxLabeled(new Rect(8f, i * 28f, resourceView.width - 16f, 26f), resource.LabelCap + " • " + count + " available / " + required + " required" + (unavailable ? " • Unavailable" : ""), ref chosen, unavailable);
                    if (chosen) plan.resources.Add(resource.defName); else plan.resources.Remove(resource.defName);
                }
                Widgets.EndScrollView();
            }

            bool ready = Readiness(component, nutrition, selectedNutrition, bedrollsAvailable, resources, out string reason);
            Rect readiness = new Rect(0f, 644f, rect.width - 205f, rect.height - 644f);
            Widgets.DrawMenuSection(readiness);
            GUI.color = ready ? new Color(0.58f, 0.88f, 0.58f) : new Color(1f, 0.58f, 0.46f);
            Widgets.Label(readiness.ContractedBy(9f), ready ? "Ready — " + Forecast(component) + "\nClick for the complete forecast." : reason);
            GUI.color = Color.white;
            if (ready && Widgets.ButtonInvisible(readiness))
                Find.WindowStack.Add(new Window_ExpeditionAssessment("Expedition Forecast", component.ForecastDetails(plan)));
            TooltipHandler.TipRegion(readiness, ready ? component.ForecastDetails(plan) : reason);
            Rect begin = new Rect(rect.width - 195f, rect.height - 44f, 195f, 40f);
            if (!ready) TooltipHandler.TipRegion(begin, reason);
            if (Widgets.ButtonText(begin, "Begin Expedition", active: ready))
            {
                if (component.Begin(plan, out string failure)) Close();
                else Messages.Message(failure ?? "The expedition could not begin.", MessageTypeDefOf.RejectInput, false);
            }
        }

        private void EnsureInitialized(HuntingExpeditionMapComponent component)
        {
            if (initialized) return;
            initialized = true;
            plan.destination = initialDestination;
            foreach (Pawn pawn in EligibleHunters().OrderByDescending(ColonistHuntingUtility.HuntingSkill).Take(3)) selectedHunters.Add(pawn);
            plan.hunters = selectedHunters.ToList();
            AutoFillProvisions(component.EstimateDays(plan));
            HuntResourceDiscovery discovery = Current.Game?.GetComponent<HuntResourceDiscovery>();
            discovery?.Refresh(map);
            foreach (HuntResourceDef resource in AvailableResourceDefs()) if (CountResource(resource) >= resource.RequiredFor(selectedHunters.Count)) plan.resources.Add(resource.defName);
        }

        private void DrawChoiceRow(Rect row, string title, string value, string button, System.Action action, bool active = true)
        {
            Widgets.DrawMenuSection(row);
            Widgets.Label(new Rect(row.x + 8f, row.y + 7f, 155f, 24f), title);
            Widgets.Label(new Rect(row.x + 162f, row.y + 7f, row.width - 340f, 24f), value);
            if (Widgets.ButtonText(new Rect(row.xMax - 170f, row.y + 2f, 162f, 30f), button, active: active)) action();
        }

        private static void DrawSubheader(Rect rect, string text)
        {
            Widgets.DrawLightHighlight(rect);
            GUI.color = new Color(0.72f, 0.78f, 0.72f);
            Widgets.Label(new Rect(rect.x + 7f, rect.y + 4f, rect.width - 14f, 22f), text);
            GUI.color = Color.white;
        }

        private void ShowObjectiveMenu()
        {
            List<ExpeditionObjective> objectives = new List<ExpeditionObjective>
            {
                ExpeditionObjective.Scout,
                ExpeditionObjective.Hunt,
                ExpeditionObjective.Capture
            };
            if (WildlifeProgression.Unlocked(WildlifeCapability.Telemetry)) objectives.Add(ExpeditionObjective.Tag);
            objectives.Add(ExpeditionObjective.Redirect);
            Find.WindowStack.Add(new FloatMenu(objectives
                .Select(value => new FloatMenuOption(ObjectiveLabel(value), () =>
                {
                    plan.objective = value;
                    if (value == ExpeditionObjective.Hunt)
                        selectedHunters.RemoveWhere(pawn => pawn.WorkTagIsDisabled(WorkTags.Violent));
                    if (value == ExpeditionObjective.Scout)
                    {
                        plan.targetSpecies = null;
                        plan.unknownTarget = false;
                    }
                    else if (value == ExpeditionObjective.Redirect && plan.targetSpecies != null && !HuntingExpeditionMapComponent.IsHerdSpecies(plan.targetSpecies))
                    {
                        plan.targetSpecies = null;
                        plan.unknownTarget = true;
                    }
                })).ToList()));
        }

        private void ShowRouteMenu()
        {
            ExpeditionRoutePolicy[] routes = { ExpeditionRoutePolicy.Safest, ExpeditionRoutePolicy.Balanced, ExpeditionRoutePolicy.Fastest };
            Find.WindowStack.Add(new FloatMenu(routes
                .Select(value => new FloatMenuOption(RouteLabel(value), () => plan.routePolicy = value)).ToList()));
        }

        private void ShowDestinationMenu(HuntingExpeditionMapComponent component)
        {
            List<ExpeditionDestination> destinations = component.Destinations();
            if (destinations.Count == 0)
            {
                Messages.Message("No traversable expedition destinations are in range.", MessageTypeDefOf.RejectInput, false);
                return;
            }
            Close(false);
            WildlifeWorldMapController.BeginDestinationSelection(component, this, destination =>
            {
                plan.destination = destination;
                if (plan.targetSpecies != null && !component.KnownSpecies(destination).Contains(plan.targetSpecies))
                {
                    plan.targetSpecies = null;
                    plan.unknownTarget = true;
                }
                AutoFillProvisions(component.EstimateDays(plan));
            });
        }

        private void ShowTargetMenu(HuntingExpeditionMapComponent component)
        {
            if (plan.objective == ExpeditionObjective.Scout) return;
            if (plan.destination == null)
            {
                Messages.Message("Choose a destination first.", MessageTypeDefOf.RejectInput, false);
                return;
            }
            List<ThingDef> species = component.KnownSpecies(plan.destination);
            if (plan.objective == ExpeditionObjective.Redirect)
                species = species.Where(HuntingExpeditionMapComponent.IsHerdSpecies).ToList();
            List<FloatMenuOption> options = new List<FloatMenuOption>
            {
                new FloatMenuOption(plan.objective == ExpeditionObjective.Redirect ? "Unknown Herd" : "Unknown", () =>
                {
                    plan.targetSpecies = null;
                    plan.unknownTarget = true;
                })
            };
            options.AddRange(species.Select(animal =>
            {
                ThingDef selected = animal;
                return new FloatMenuOption(selected.LabelCap + " • " + PopulationLabel(component, plan.destination, selected) + " • " +
                    HuntingKnowledgeMapComponent.LevelLabel(HuntingKnowledgeMapComponent.ColonyLevel(selected)), () =>
                    {
                        plan.targetSpecies = selected;
                        plan.unknownTarget = false;
                    });
            }));
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private bool Readiness(HuntingExpeditionMapComponent component, float nutrition, float selectedNutrition, int bedrollsAvailable, List<HuntResourceDef> resources, out string reason)
        {
            reason = null;
            if (!WildlifeProgression.Unlocked(WildlifeCapability.HuntingExpedition)) reason = WildlifeProgression.LockReason(WildlifeCapability.HuntingExpedition);
            else if (plan.destination == null) reason = "Choose a destination.";
            else if (selectedHunters.Count == 0) reason = "Assign at least one hunter.";
            else if (selectedHunters.Count > 8) reason = "An expedition supports at most eight hunters.";
            else if (plan.objective == ExpeditionObjective.Hunt && selectedHunters.Any(hunter => hunter.WorkTagIsDisabled(WorkTags.Violent))) reason = "Pacifists cannot join a Hunt.";
            else if (plan.objective == ExpeditionObjective.Tag && !WildlifeProgression.Unlocked(WildlifeCapability.Telemetry)) reason = WildlifeProgression.LockReason(WildlifeCapability.Telemetry);
            else if (plan.objective != ExpeditionObjective.Scout && plan.targetSpecies == null && !plan.unknownTarget) reason = "Choose a target animal or Unknown.";
            else if (plan.objective == ExpeditionObjective.Redirect && plan.targetSpecies != null && !HuntingExpeditionMapComponent.IsHerdSpecies(plan.targetSpecies)) reason = "Only herd animals can be redirected.";
            else if (plan.targetSpecies != null && selectedHunters.Any(hunter => ColonistHuntingUtility.HuntingSkill(hunter, plan.targetSpecies) < HerdsMod.Settings.minimumFieldcraftSkill)) reason = "Every hunter must meet effective Skill " + HerdsMod.Settings.minimumFieldcraftSkill + " for the target.";
            else if (plan.targetSpecies == null && plan.objective != ExpeditionObjective.Scout && selectedHunters.Any(hunter => ColonistHuntingUtility.HuntingSkill(hunter) < HerdsMod.Settings.minimumFieldcraftSkill)) reason = "Every hunter must meet Skill " + HerdsMod.Settings.minimumFieldcraftSkill + " for a blind search.";
            else if (selectedNutrition + 0.001f < nutrition) reason = "Select enough provisions for the planned journey.";
            else if (!ExpeditionSupplyUtility.ManifestAvailable(map, plan.provisions)) reason = "Some selected provisions are no longer available.";
            else if (!ExpeditionSupplyUtility.ManifestAvailable(map, plan.medicines)) reason = "Some selected medicine is no longer available.";
            else if (plan.useBedrolls && bedrollsAvailable < selectedHunters.Count) reason = "Not enough packed bedrolls.";
            else if (resources.Any(resource => plan.resources.Contains(resource.defName) && CountResource(resource) < resource.RequiredFor(selectedHunters.Count))) reason = "Selected field equipment is unavailable.";
            else if (plan.targetSpecies != null && (plan.objective == ExpeditionObjective.Hunt || plan.objective == ExpeditionObjective.Capture) &&
                !map.GetComponent<WildlifeStewardMapComponent>().CanHunt(plan.targetSpecies, out reason)) { }
            return reason.NullOrEmpty();
        }

        private string Forecast(HuntingExpeditionMapComponent component)
        {
            if (plan.destination?.knowledge?.discoveryLevel <= 0)
                return "Unknown region; route, wildlife, and hazard estimates will be learned by expedition travel.";
            float confidence = plan.destination?.knowledge?.confidence ?? 0f;
            string certainty = confidence < 0.2f ? "very uncertain" : confidence < 0.5f ? "uncertain" : confidence < 0.8f ? "informed" : "high-confidence";
            string encounter = plan.objective == ExpeditionObjective.Scout ? "survey conditions unknown" :
                plan.unknownTarget ? "blind wildlife search" : PopulationLabel(component, plan.destination, plan.targetSpecies);
            return certainty + " forecast; " + encounter + "; " + RiskLabel(plan.riskTolerance).ToLowerInvariant() + " engagement.";
        }

        private List<Pawn> EligibleHunters() =>
            map.mapPawns.FreeColonistsSpawned.Where(pawn => pawn?.Downed == false &&
                !map.GetComponent<HuntingExpeditionMapComponent>().PawnOnExpedition(pawn))
                .OrderBy(pawn => pawn.LabelShortCap.ToString()).ToList();

        private bool CanJoinCurrentObjective(Pawn pawn) =>
            pawn != null && (plan.objective != ExpeditionObjective.Hunt || !pawn.WorkTagIsDisabled(WorkTags.Violent));

        private List<Pawn> EligiblePackAnimals() =>
            map.mapPawns.SpawnedColonyAnimals.Where(pawn => pawn?.Downed == false &&
                pawn.RaceProps?.packAnimal == true &&
                !map.GetComponent<HuntingExpeditionMapComponent>().PawnOnExpedition(pawn))
                .OrderBy(pawn => pawn.LabelShortCap.ToString()).ToList();

        private void AutoFillProvisions(float estimatedDays)
        {
            plan.provisions.Clear();
            float remaining = ExpeditionSupplyUtility.RequiredNutrition(plan, estimatedDays);
            Dictionary<ThingDef, int> foods = ExpeditionSupplyUtility.AvailableFoods(map);
            foreach (KeyValuePair<ThingDef, int> pair in foods.OrderBy(pair =>
                pair.Key.BaseMarketValue / Mathf.Max(0.01f, ExpeditionSupplyUtility.NutritionPerUnit(pair.Key))))
            {
                if (remaining <= 0.001f) break;
                float nutrition = Mathf.Max(0.01f, ExpeditionSupplyUtility.NutritionPerUnit(pair.Key));
                int count = Mathf.Min(pair.Value, Mathf.CeilToInt(remaining / nutrition));
                if (count <= 0) continue;
                plan.provisions[pair.Key] = count;
                remaining -= count * nutrition;
            }
        }

        private List<HuntResourceDef> AvailableResourceDefs()
        {
            HuntResourceDiscovery discovery = Current.Game?.GetComponent<HuntResourceDiscovery>();
            return DefDatabase<HuntResourceDef>.AllDefsListForReading
                .Where(resource => discovery?.IsDiscovered(resource) == true && (!resource.enabledByScentMasking || HerdsMod.Settings.enableScentMasking))
                .OrderBy(resource => resource.label).ToList();
        }

        private int CountResource(HuntResourceDef resource)
        {
            if (resource.use == HuntResourceUse.ScentChargePerHunter)
                return map.listerBuildings.allBuildingsColonist.OfType<Building_WildlifeTool>().Where(tool => tool.def == resource.sourceBuildingDef && tool.active).Sum(tool => tool.scentCharges);
            return resource.thingDef == null ? 0 : ExpeditionSupplyUtility.AvailableThing(map, resource.thingDef);
        }

        private float CarryCapacity() => selectedHunters.Count * 18f + selectedPackAnimals.Sum(pawn => Mathf.Max(12f, pawn.BodySize * 35f));

        private static string PopulationLabel(HuntingExpeditionMapComponent component, ExpeditionDestination destination, ThingDef species)
        {
            if (destination == null || species == null) return "unknown population";
            float population = component.PopulationAt(destination, species);
            if (destination.knowledge.confidence >= 0.75f) return "about " + Mathf.RoundToInt(population);
            return population < 1f ? "unlikely" : population < 5f ? "sparse signs" : population < 15f ? "moderate signs" : "abundant signs";
        }

        private static string ConfidenceLabel(float confidence) =>
            confidence < 0.15f ? "Unsurveyed" : confidence < 0.4f ? "Low confidence" : confidence < 0.7f ? "Moderate confidence" : "High confidence";

        public static string ObjectiveLabel(ExpeditionObjective objective) =>
            objective == ExpeditionObjective.Hunt ? "Hunt" :
            objective == ExpeditionObjective.Scout ? "Scout" :
            objective == ExpeditionObjective.Capture ? "Capture an Animal" :
            objective == ExpeditionObjective.Tag ? "Tag an Animal" :
            objective == ExpeditionObjective.Redirect ? "Redirect Wild Herd" :
            "Protect Wildlife";

        private static string RouteLabel(ExpeditionRoutePolicy policy) =>
            policy == ExpeditionRoutePolicy.Fastest ? "Fastest" : policy == ExpeditionRoutePolicy.Safest ? "Safest" : "Balanced";

        private static string RiskLabel(float risk) => risk < 0.34f ? "Cautious" : risk < 0.67f ? "Balanced" : "Bold";

        private static void DrawRiskChoices(Rect rect, ref float risk)
        {
            string[] labels = { "Cautious", "Balanced", "Bold" };
            float[] values = { 0.2f, 0.5f, 0.8f };
            float gap = 4f;
            float width = (rect.width - gap * 2f) / 3f;
            int selected = risk < 0.34f ? 0 : risk < 0.67f ? 1 : 2;
            risk = values[selected];
            for (int i = 0; i < 3; i++)
            {
                Rect button = new Rect(rect.x + i * (width + gap), rect.y, width, rect.height);
                if (i == selected) Widgets.DrawHighlightSelected(button);
                if (Widgets.ButtonText(button, labels[i])) risk = values[i];
            }
        }
    }

    public sealed class Window_ExpeditionDestinationPicker : Window
    {
        private readonly HuntingExpeditionMapComponent component;
        private readonly System.Action<ExpeditionDestination> onConfirm;
        private ExpeditionDestination selected;
        public override Vector2 InitialSize => new Vector2(940f, 700f);

        public Window_ExpeditionDestinationPicker(HuntingExpeditionMapComponent component, ExpeditionDestination selected,
            System.Action<ExpeditionDestination> onConfirm)
        {
            this.component = component;
            this.selected = selected;
            this.onConfirm = onConfirm;
            doCloseX = true;
            absorbInputAroundWindow = true;
        }

        public override void DoWindowContents(Rect rect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, rect.width, 32f), "Choose Expedition Destination");
            Text.Font = GameFont.Small;
            GUI.color = new Color(0.72f, 0.78f, 0.72f);
            Widgets.Label(new Rect(0f, 32f, rect.width, 24f), "Select a world cell visually. Distance is measured outward from the colony.");
            GUI.color = Color.white;
            List<ExpeditionDestination> destinations = component?.Destinations() ?? new List<ExpeditionDestination>();
            Rect mapRect = new Rect(0f, 66f, rect.width - 254f, rect.height - 66f);
            Rect details = new Rect(mapRect.xMax + 10f, 66f, 244f, rect.height - 66f);
            Widgets.DrawMenuSection(mapRect);
            Widgets.DrawMenuSection(details);
            if (destinations.Count == 0)
            {
                Widgets.Label(mapRect.ContractedBy(14f), "No passable world cells are currently reachable.");
                return;
            }
            Vector2 origin = Find.WorldGrid.LongLatOf(component.HomeMap.Tile);
            float maxX = 1f;
            float maxY = 1f;
            Dictionary<ExpeditionDestination, Vector2> offsets = new Dictionary<ExpeditionDestination, Vector2>();
            for (int i = 0; i < destinations.Count; i++)
            {
                Vector2 point = Find.WorldGrid.LongLatOf((RimWorld.Planet.PlanetTile)destinations[i].tileId);
                Vector2 offset = new Vector2(Mathf.DeltaAngle(origin.x, point.x), point.y - origin.y);
                offsets[destinations[i]] = offset;
                maxX = Mathf.Max(maxX, Mathf.Abs(offset.x));
                maxY = Mathf.Max(maxY, Mathf.Abs(offset.y));
            }
            Rect plotting = mapRect.ContractedBy(40f);
            Vector2 center = plotting.center;
            foreach (ExpeditionDestination destination in destinations.OrderByDescending(item => item.distance))
            {
                Vector2 offset = offsets[destination];
                Vector2 position = center + new Vector2(offset.x / maxX * plotting.width * 0.44f, -offset.y / maxY * plotting.height * 0.44f);
                Color color = DestinationColor(destination);
                Widgets.DrawLine(center, position, new Color(color.r, color.g, color.b, 0.28f), 1f);
                Rect dot = new Rect(position.x - 11f, position.y - 11f, 22f, 22f);
                Widgets.DrawBoxSolid(dot, color);
                Widgets.DrawBox(dot);
                if (selected?.tileId == destination.tileId)
                {
                    Widgets.DrawHighlightSelected(dot.ExpandedBy(5f));
                    Widgets.DrawBox(dot.ExpandedBy(4f), 2);
                }
                if (Widgets.ButtonInvisible(dot.ExpandedBy(5f))) selected = destination;
                TooltipHandler.TipRegion(dot.ExpandedBy(5f), destination.biome.LabelCap + "\n" + destination.distance +
                    (destination.distance == 1 ? " cell" : " cells") + " away\nDanger " + destination.danger.ToStringPercent() +
                    "\n" + ConfidenceLabel(destination.knowledge.confidence));
            }
            Rect colony = new Rect(center.x - 15f, center.y - 15f, 30f, 30f);
            Widgets.DrawBoxSolid(colony, new Color(0.40f, 0.65f, 0.34f));
            Widgets.DrawBox(colony, 2);
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(colony, "C");
            Text.Anchor = TextAnchor.UpperLeft;
            DrawDestinationDetails(details.ContractedBy(12f));
        }

        private void DrawDestinationDetails(Rect rect)
        {
            if (selected == null)
            {
                Widgets.Label(rect, "Select a destination marker.");
                return;
            }
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, 30f), selected.biome.LabelCap);
            Text.Font = GameFont.Small;
            int known = component.KnownSpecies(selected).Count;
            string information = selected.distance + (selected.distance == 1 ? " world cell away" : " world cells away") +
                "\n\nSurvey: " + ConfidenceLabel(selected.knowledge.confidence) +
                "\nKnown animals: " + known +
                "\nDanger: " + selected.danger.ToStringPercent() +
                "\nTravel difficulty: " + selected.travelFactor.ToString("0.0") +
                (selected.road ? "\nRoad access" : "") +
                (selected.river ? "\nRiver crossing" : "") +
                "\n\nDiscovery: " + (selected.knowledge.discovery.NullOrEmpty() ? "None" : selected.knowledge.discovery);
            Widgets.Label(new Rect(rect.x, rect.y + 42f, rect.width, rect.height - 100f), information);
            if (Widgets.ButtonText(new Rect(rect.x, rect.yMax - 42f, rect.width, 38f), "Choose Destination"))
            {
                onConfirm?.Invoke(selected);
                Close();
            }
        }

        private static Color DestinationColor(ExpeditionDestination destination)
        {
            string biome = destination.biome?.label?.ToLowerInvariant() ?? string.Empty;
            Color baseColor = biome.Contains("desert") || biome.Contains("arid") ? new Color(0.72f, 0.57f, 0.27f) :
                biome.Contains("ice") || biome.Contains("tundra") ? new Color(0.55f, 0.76f, 0.80f) :
                biome.Contains("swamp") || biome.Contains("marsh") ? new Color(0.32f, 0.46f, 0.27f) :
                biome.Contains("forest") || biome.Contains("jungle") ? new Color(0.25f, 0.58f, 0.28f) :
                new Color(0.48f, 0.62f, 0.32f);
            return Color.Lerp(baseColor, new Color(0.72f, 0.20f, 0.16f), destination.danger * 0.45f);
        }

        private static string ConfidenceLabel(float confidence) =>
            confidence < 0.15f ? "Unsurveyed" : confidence < 0.4f ? "Low confidence" : confidence < 0.7f ? "Moderate confidence" : "High confidence";
    }

    public sealed class Window_ExpeditionMedicine : Window
    {
        private readonly Map map;
        private readonly Dictionary<ThingDef, int> selected;
        private Vector2 scroll;
        public override Vector2 InitialSize => new Vector2(680f, 620f);

        public Window_ExpeditionMedicine(Map map, Dictionary<ThingDef, int> selected)
        {
            this.map = map;
            this.selected = selected;
            doCloseX = true;
            absorbInputAroundWindow = true;
        }

        public override void DoWindowContents(Rect rect)
        {
            Dictionary<ThingDef, int> available = ExpeditionSupplyUtility.AvailableMedicines(map);
            List<ThingDef> defs = available.Keys.Concat(selected.Keys).Where(def => def != null).Distinct().OrderBy(def => def.label).ToList();
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, rect.width, 32f), "Select Medicine");
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(0f, 34f, rect.width, 24f), selected.Values.Sum() + " selected from " + available.Values.Sum() + " available.");
            Rect outer = new Rect(0f, 68f, rect.width, rect.height - 68f);
            Rect view = new Rect(0f, 0f, outer.width - 18f, Mathf.Max(outer.height, defs.Count * 58f));
            Widgets.BeginScrollView(outer, ref scroll, view);
            for (int i = 0; i < defs.Count; i++)
            {
                ThingDef def = defs[i];
                int maximum = available.TryGetValue(def, out int count) ? count : 0;
                int chosen = selected.TryGetValue(def, out int value) ? Mathf.Min(value, maximum) : 0;
                SetCount(selected, def, chosen);
                Rect row = new Rect(0f, i * 58f, view.width, 52f);
                Widgets.DrawMenuSection(row);
                if (def.uiIcon != null) GUI.DrawTexture(new Rect(row.x + 8f, row.y + 8f, 36f, 36f), def.uiIcon, ScaleMode.ScaleToFit);
                Widgets.Label(new Rect(row.x + 52f, row.y + 5f, row.width - 280f, 23f), def.LabelCap);
                Widgets.Label(new Rect(row.x + 52f, row.y + 28f, row.width - 280f, 20f),
                    maximum + " available • potency " + def.GetStatValueAbstract(StatDefOf.MedicalPotency).ToStringPercent());
                if (Widgets.ButtonText(new Rect(row.xMax - 218f, row.y + 10f, 42f, 32f), "−")) SetCount(selected, def, chosen - 1);
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(new Rect(row.xMax - 170f, row.y + 10f, 52f, 32f), chosen.ToString());
                Text.Anchor = TextAnchor.UpperLeft;
                if (Widgets.ButtonText(new Rect(row.xMax - 112f, row.y + 10f, 42f, 32f), "+")) SetCount(selected, def, chosen + 1, maximum);
                if (Widgets.ButtonText(new Rect(row.xMax - 64f, row.y + 10f, 58f, 32f), "Max")) SetCount(selected, def, maximum);
            }
            Widgets.EndScrollView();
            if (defs.Count == 0) Widgets.Label(new Rect(8f, 78f, rect.width - 16f, 30f), "No medicine is available in colony storage.");
        }

        private static void SetCount(Dictionary<ThingDef, int> manifest, ThingDef def, int count, int maximum = int.MaxValue)
        {
            count = Mathf.Clamp(count, 0, maximum);
            if (count <= 0) manifest.Remove(def); else manifest[def] = count;
        }
    }

    public sealed class Window_ExpeditionProvisions : Window
    {
        private readonly Map map;
        private readonly ExpeditionPlan plan;
        private readonly float estimatedDays;
        private Vector2 scroll;
        public override Vector2 InitialSize => new Vector2(760f, 680f);

        public Window_ExpeditionProvisions(Map map, ExpeditionPlan plan, float estimatedDays)
        {
            this.map = map;
            this.plan = plan;
            this.estimatedDays = estimatedDays;
            doCloseX = true;
            absorbInputAroundWindow = true;
        }

        public override void DoWindowContents(Rect rect)
        {
            Dictionary<ThingDef, int> available = ExpeditionSupplyUtility.AvailableFoods(map);
            List<ThingDef> defs = available.Keys.Concat(plan.provisions.Keys).Where(def => def != null).Distinct().OrderBy(def => def.label).ToList();
            float required = ExpeditionSupplyUtility.RequiredNutrition(plan, estimatedDays);
            float chosenNutrition = ExpeditionSupplyUtility.SelectedNutrition(plan.provisions);
            float daily = ExpeditionSupplyUtility.DailyNutrition(plan);
            int buffer = daily <= 0f ? 0 : Mathf.Clamp(Mathf.FloorToInt((chosenNutrition - required) / daily), 0, 3);
            plan.foodDays = buffer;
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, rect.width - 130f, 32f), "Select Provisions");
            Text.Font = GameFont.Small;
            if (Widgets.ButtonText(new Rect(rect.width - 124f, 0f, 124f, 30f), "Auto Fill")) AutoFill(available, required);
            Rect bar = new Rect(0f, 42f, rect.width, 28f);
            Widgets.FillableBar(bar, required <= 0f ? 1f : Mathf.Clamp01(chosenNutrition / required));
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(bar, chosenNutrition.ToString("0.0") + " / " + required.ToString("0.0") + " nutrition" +
                (buffer > 0 ? " • " + buffer + " extra day" + (buffer == 1 ? "" : "s") : ""));
            Text.Anchor = TextAnchor.UpperLeft;
            TooltipHandler.TipRegion(bar, "The party needs " + required.ToString("0.0") + " nutrition for the expected " +
                estimatedDays.ToString("0.0") + "-day journey. The expedition cannot depart until the bar is full. " +
                "Each complete extra day of provisions improves preparedness and reduces incident risk by 3%, up to three days.");
            GUI.color = new Color(0.72f, 0.78f, 0.72f);
            Widgets.Label(new Rect(0f, 76f, rect.width, 22f), "Choose exact food types and quantities from colony storage.");
            GUI.color = Color.white;
            Rect outer = new Rect(0f, 104f, rect.width, rect.height - 104f);
            Rect view = new Rect(0f, 0f, outer.width - 18f, Mathf.Max(outer.height, defs.Count * 62f));
            Widgets.BeginScrollView(outer, ref scroll, view);
            for (int i = 0; i < defs.Count; i++)
            {
                ThingDef def = defs[i];
                int maximum = available.TryGetValue(def, out int count) ? count : 0;
                int chosen = plan.provisions.TryGetValue(def, out int value) ? Mathf.Min(value, maximum) : 0;
                SetCount(def, chosen);
                float each = ExpeditionSupplyUtility.NutritionPerUnit(def);
                Rect row = new Rect(0f, i * 62f, view.width, 56f);
                Widgets.DrawMenuSection(row);
                if (def.uiIcon != null) GUI.DrawTexture(new Rect(row.x + 8f, row.y + 10f, 36f, 36f), def.uiIcon, ScaleMode.ScaleToFit);
                Widgets.Label(new Rect(row.x + 52f, row.y + 6f, row.width - 310f, 23f), def.LabelCap);
                Widgets.Label(new Rect(row.x + 52f, row.y + 30f, row.width - 310f, 20f),
                    maximum + " available • " + each.ToString("0.00") + " nutrition each • selected " + (chosen * each).ToString("0.0"));
                if (Widgets.ButtonText(new Rect(row.xMax - 248f, row.y + 12f, 40f, 32f), "−")) SetCount(def, chosen - 1);
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(new Rect(row.xMax - 202f, row.y + 12f, 48f, 32f), chosen.ToString());
                Text.Anchor = TextAnchor.UpperLeft;
                if (Widgets.ButtonText(new Rect(row.xMax - 148f, row.y + 12f, 40f, 32f), "+")) SetCount(def, chosen + 1, maximum);
                if (Widgets.ButtonText(new Rect(row.xMax - 102f, row.y + 12f, 44f, 32f), "+10")) SetCount(def, chosen + 10, maximum);
                if (Widgets.ButtonText(new Rect(row.xMax - 52f, row.y + 12f, 46f, 32f), "Max")) SetCount(def, maximum);
            }
            Widgets.EndScrollView();
            if (defs.Count == 0) Widgets.Label(new Rect(8f, 114f, rect.width - 16f, 30f), "No human-edible food is available in colony storage.");
        }

        private void AutoFill(Dictionary<ThingDef, int> available, float required)
        {
            plan.provisions.Clear();
            float remaining = required;
            foreach (KeyValuePair<ThingDef, int> pair in available.OrderBy(pair =>
                pair.Key.BaseMarketValue / Mathf.Max(0.01f, ExpeditionSupplyUtility.NutritionPerUnit(pair.Key))))
            {
                if (remaining <= 0.001f) break;
                float each = Mathf.Max(0.01f, ExpeditionSupplyUtility.NutritionPerUnit(pair.Key));
                int count = Mathf.Min(pair.Value, Mathf.CeilToInt(remaining / each));
                if (count > 0) plan.provisions[pair.Key] = count;
                remaining -= count * each;
            }
        }

        private void SetCount(ThingDef def, int count, int maximum = int.MaxValue)
        {
            count = Mathf.Clamp(count, 0, maximum);
            if (count <= 0) plan.provisions.Remove(def); else plan.provisions[def] = count;
        }
    }

    public sealed class Window_WildlifeExpeditions : Window
    {
        private enum ExpeditionSort { Status, Eta, Destination }
        private readonly Map map;
        private Vector2 scroll;
        private bool showHistory;
        private ExpeditionSort sort = ExpeditionSort.Status;

        public override Vector2 InitialSize => new Vector2(900f, 680f);

        public Window_WildlifeExpeditions(Map map)
        {
            this.map = map;
            doCloseX = true;
            absorbInputAroundWindow = true;
            resizeable = true;
            Current.Game?.GetComponent<WildlifeExperienceGameComponent>()?.ShowExpeditionTutorial();
        }

        public override void DoWindowContents(Rect rect)
        {
            HuntingExpeditionMapComponent component = map?.GetComponent<HuntingExpeditionMapComponent>();
            if (component == null)
            {
                Widgets.Label(rect, "The colony map is unavailable.");
                return;
            }

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, rect.width - 230f, 32f), "Wildlife Expeditions");
            Text.Font = GameFont.Small;
            GUI.color = new Color(0.68f, 0.78f, 0.69f);
            Widgets.Label(new Rect(0f, 32f, rect.width - 230f, 24f),
                component.ActiveExpeditions.Count + " active field part" +
                (component.ActiveExpeditions.Count == 1 ? "y" : "ies"));
            GUI.color = Color.white;

            Rect send = new Rect(rect.width - 220f, 2f, 220f, 38f);
            if (Widgets.ButtonText(send, "Send New Expedition"))
            {
                Close(false);
                WildlifeWorldMapController.BeginNewExpeditionSelection(component, this);
            }
            TooltipHandler.TipRegion(send, "Open the World Map, choose a valid tile, then plan and supply a new expedition.");

            if (Widgets.ButtonText(new Rect(0f, 62f, 120f, 30f), "Active", active: showHistory))
            {
                showHistory = false;
                scroll = Vector2.zero;
            }
            if (Widgets.ButtonText(new Rect(128f, 62f, 120f, 30f), "History", active: !showHistory))
            {
                showHistory = true;
                scroll = Vector2.zero;
            }
            Rect sortButton = new Rect(rect.width - 205f, 62f, 205f, 30f);
            if (!showHistory && Widgets.ButtonText(sortButton, "Sort: " +
                (sort == ExpeditionSort.Eta ? "Return Time" : sort.ToString())))
                sort = (ExpeditionSort)(((int)sort + 1) % 3);
            if (!showHistory)
                TooltipHandler.TipRegion(sortButton,
                    "Cycle between urgency, expected return time, and destination.");

            Rect divider = new Rect(0f, 102f, rect.width, 2f);
            Widgets.DrawLineHorizontal(divider.x, divider.y, divider.width);
            if (showHistory)
            {
                DrawHistory(new Rect(0f, 114f, rect.width, rect.height - 114f), component.History);
                return;
            }
            List<HuntingExpeditionRecord> records = component.ActiveExpeditions.ToList();
            if (sort == ExpeditionSort.Eta) records = records.OrderBy(record => record.expectedReturnTick).ToList();
            else if (sort == ExpeditionSort.Destination) records = records.OrderBy(record => DestinationBiome(record)).ThenBy(record => record.distance).ToList();
            else records = records.OrderByDescending(record => Severity(component.Warning(record))).ThenBy(record => record.stage).ToList();
            if (records.Count == 0)
            {
                Rect empty = new Rect(0f, 122f, rect.width, 150f);
                Widgets.DrawMenuSection(empty);
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = Color.gray;
                Widgets.Label(empty.ContractedBy(18f),
                    "No active expeditions.\n\nSend a field party to scout, hunt, capture, tag, or redirect wildlife.");
                GUI.color = Color.white;
                Text.Anchor = TextAnchor.UpperLeft;
                return;
            }

            Rect outer = new Rect(0f, 114f, rect.width, rect.height - 114f);
            float cardHeight = 142f;
            Rect view = new Rect(0f, 0f, outer.width - 18f, Mathf.Max(outer.height, records.Count * (cardHeight + 8f)));
            Widgets.BeginScrollView(outer, ref scroll, view);
            for (int i = 0; i < records.Count; i++)
                DrawExpeditionCard(new Rect(0f, i * (cardHeight + 8f), view.width, cardHeight), component, records[i]);
            Widgets.EndScrollView();
        }

        private void DrawExpeditionCard(Rect card, HuntingExpeditionMapComponent component, HuntingExpeditionRecord record)
        {
            Widgets.DrawMenuSection(card);
            Rect inner = card.ContractedBy(10f);
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inner.x, inner.y, inner.width - 390f, 27f),
                "Expedition " + record.id + " — " + HuntingExpeditionMapComponent.StageLabel(record.stage));
            Text.Font = GameFont.Small;
            string target = record.targetSpecies?.LabelCap.ToString() ??
                (record.objective == ExpeditionObjective.Scout ? "Survey" :
                record.objective == ExpeditionObjective.Redirect ? "Unknown herd" : "Unknown wildlife");
            Widgets.Label(new Rect(inner.x, inner.y + 30f, inner.width - 390f, 24f),
                Window_HuntingExpeditionSetup.ObjectiveLabel(record.objective) + " • " + target +
                " • " + record.distance + " tile" + (record.distance == 1 ? "" : "s"));
            Widgets.Label(new Rect(inner.x, inner.y + 52f, inner.width - 390f, 22f),
                "Destination: " + DestinationBiome(record));
            string party = string.Join(", ", record.hunters.Where(pawn => pawn != null && !pawn.Dead)
                .Select(pawn => pawn.LabelShortCap.ToString()));
            Widgets.Label(new Rect(inner.x, inner.y + 74f, inner.width - 390f, 24f),
                "Party: " + (party.NullOrEmpty() ? "No able hunters" : party));
            Widgets.FillableBar(new Rect(inner.x, inner.y + 100f, inner.width - 390f, 12f), component.Progress(record));
            Widgets.Label(new Rect(inner.x, inner.y + 115f, inner.width - 390f, 20f), component.Status(record));
            string warning = component.Warning(record);
            if (!warning.NullOrEmpty())
            {
                GUI.color = new Color(1f, 0.48f, 0.36f);
                Widgets.Label(new Rect(card.xMax - 370f, inner.y + 80f, 364f, 24f), warning);
                GUI.color = Color.white;
            }

            float x = card.xMax - 370f;
            Rect details = new Rect(x, inner.y, 116f, 30f);
            if (Widgets.ButtonText(details, "Details"))
                Find.WindowStack.Add(new Window_HuntingExpeditionStatus(component, record));
            TooltipHandler.TipRegion(details,
                "Review party condition, supplies, progress, risk, and expedition events.");
            Rect route = new Rect(x + 124f, inner.y, 116f, 30f);
            if (Widgets.ButtonText(route, "Route"))
                Find.WindowStack.Add(new Window_ExpeditionRoute(component, record));
            TooltipHandler.TipRegion(route,
                "Review the selected route and its speed, safety, and terrain effects.");
            bool canView = record.caravan != null || record.marker != null;
            Rect worldMap = new Rect(x + 248f, inner.y, 116f, 30f);
            if (Widgets.ButtonText(worldMap, "World Map", active: canView))
                CameraJumper.TryJump((GlobalTargetInfo)(record.caravan ?? (WorldObject)record.marker));
            TooltipHandler.TipRegion(worldMap, canView
                ? "Center the World Map on this expedition."
                : "The expedition has not yet formed a visible world party.");

            if (record.needsRescue)
            {
                bool rescueActive = record.rescuers.Any(pawn => pawn != null && !pawn.Dead);
                if (Widgets.ButtonText(new Rect(x, inner.y + 40f, 178f, 30f),
                    rescueActive ? "Rescue En Route" : "Send Rescue", active: !rescueActive))
                    ShowRescueMenu(component, record);
            }
            Rect recall = new Rect(x + 186f, inner.y + 40f, 178f, 30f);
            if (Widgets.ButtonText(recall, "Recall Expedition"))
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    "Recall Expedition " + record.id +
                    "? The party will abandon its objective and return to the colony.",
                    () => component.Cancel(record)));
            TooltipHandler.TipRegion(recall,
                "Abandon the objective and order the party to return. Confirmation is required.");
        }

        private void ShowRescueMenu(HuntingExpeditionMapComponent component, HuntingExpeditionRecord record)
        {
            List<Pawn> pawns = map.mapPawns.FreeColonistsSpawned
                .Where(pawn => pawn?.Downed == false &&
                    !component.PawnOnExpedition(pawn))
                .OrderBy(pawn => pawn.LabelShortCap.ToString()).ToList();
            if (pawns.Count == 0)
            {
                Messages.Message("No able colonist is available to send as a rescuer.", MessageTypeDefOf.RejectInput, false);
                return;
            }
            Find.WindowStack.Add(new FloatMenu(pawns.Select(pawn =>
                new FloatMenuOption(pawn.LabelShortCap, () => component.BeginRescue(record, pawn))).ToList()));
        }

        private void DrawHistory(Rect outer, IReadOnlyList<string> history)
        {
            if (history == null || history.Count == 0)
            {
                Widgets.DrawMenuSection(outer);
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = Color.gray;
                Widgets.Label(outer, "No completed expeditions have been recorded.");
                GUI.color = Color.white;
                Text.Anchor = TextAnchor.UpperLeft;
                return;
            }
            Rect view = new Rect(0f, 0f, outer.width - 18f, Mathf.Max(outer.height, history.Count * 52f));
            Widgets.BeginScrollView(outer, ref scroll, view);
            for (int i = 0; i < history.Count; i++)
            {
                Rect row = new Rect(0f, i * 52f, view.width, 46f);
                Widgets.DrawMenuSection(row);
                bool negative = WildlifeExperience.IsNegative(new WildlifeExperienceEvent
                    { category = "Expedition", text = history[i] });
                GUI.color = negative ? new Color(1f, 0.52f, 0.46f) : Color.white;
                Widgets.Label(row.ContractedBy(9f), history[i]);
                GUI.color = Color.white;
            }
            Widgets.EndScrollView();
        }

        private static int Severity(string warning) =>
            warning.NullOrEmpty() ? 0 : warning.Contains("Stranded") ? 4 :
            warning.Contains("Overdue") ? 3 : warning.Contains("Injured") ? 2 : 1;

        private static string DestinationBiome(HuntingExpeditionRecord record)
        {
            if (record == null || Find.WorldGrid == null || record.destinationTile < 0) return "Unknown biome";
            Tile tile = Find.WorldGrid[(PlanetTile)record.destinationTile];
            return tile?.PrimaryBiome?.LabelCap.ToString() ?? "Unknown biome";
        }
    }

    public sealed class Window_HuntingExpeditionStatus : Window
    {
        private readonly HuntingExpeditionMapComponent component;
        private readonly HuntingExpeditionRecord record;
        private Vector2 scroll;
        public override Vector2 InitialSize => new Vector2(720f, 610f);

        public Window_HuntingExpeditionStatus(HuntingExpeditionMapComponent component, HuntingExpeditionRecord record)
        {
            this.component = component;
            this.record = record;
            doCloseX = true;
            absorbInputAroundWindow = true;
        }

        public override void DoWindowContents(Rect rect)
        {
            if (record == null || component == null || component.FindRecord(record.id) != record)
            {
                Widgets.Label(rect, "This expedition is no longer active.");
                return;
            }
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, rect.width, 32f), "Wildlife Expedition");
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(0f, 34f, rect.width, 24f), component.Status(record));
            Widgets.FillableBar(new Rect(0f, 62f, rect.width, 12f), component.Progress(record));
            float width = (rect.width - 16f) / 3f;
            string target = record.targetSpecies?.LabelCap.ToString() ??
                (record.objective == ExpeditionObjective.Scout ? "No target required" :
                record.objective == ExpeditionObjective.Redirect ? "Unknown herd" : "Unknown wildlife");
            DrawCard(new Rect(0f, 86f, width, 66f), "Objective", Window_HuntingExpeditionSetup.ObjectiveLabel(record.objective), target);
            DrawCard(new Rect(width + 8f, 86f, width, 66f), "Party", record.hunters.Count + " hunters", record.packAnimals.Count + " pack animals");
            DrawCard(new Rect((width + 8f) * 2f, 86f, width, 66f), "Supplies", record.medicine + " medicine", record.bedrolls + " bedrolls");
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 166f, rect.width, 28f), "Field Log");
            Text.Font = GameFont.Small;
            Rect outer = new Rect(0f, 198f, rect.width, rect.height - 252f);
            Rect view = new Rect(0f, 0f, outer.width - 18f, Mathf.Max(outer.height, record.log.Count * 42f));
            Widgets.BeginScrollView(outer, ref scroll, view);
            for (int i = 0; i < record.log.Count; i++)
            {
                Rect row = new Rect(0f, i * 42f, view.width, 36f);
                Widgets.DrawMenuSection(row);
                Widgets.Label(row.ContractedBy(7f), record.log[i]);
            }
            Widgets.EndScrollView();
            if (record.needsRescue) Widgets.Label(new Rect(0f, rect.height - 44f, rect.width - 370f, 40f), "The party is stranded. Open Wildlife Expeditions to send a rescuer.");
            if (Widgets.ButtonText(new Rect(record.needsRescue ? rect.width - 350f : 0f, rect.height - 42f, 160f, 38f), "Outcome Factors"))
                Find.WindowStack.Add(new Window_ExpeditionAssessment("Expedition Outcome Factors", component.RecordDetails(record)));
            if (Widgets.ButtonText(new Rect(rect.width - 180f, rect.height - 42f, 180f, 38f), "Recall Expedition"))
            {
                component.Cancel(record);
                Close();
            }
        }

        private static void DrawCard(Rect rect, string title, string value, string detail)
        {
            Widgets.DrawMenuSection(rect);
            GUI.color = new Color(0.72f, 0.78f, 0.72f);
            Widgets.Label(new Rect(rect.x + 9f, rect.y + 7f, rect.width - 18f, 20f), title);
            GUI.color = Color.white;
            Widgets.Label(new Rect(rect.x + 9f, rect.y + 27f, rect.width - 18f, 20f), value);
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(rect.x + 9f, rect.y + 46f, rect.width - 18f, 18f), detail);
            Text.Font = GameFont.Small;
        }
    }

    public sealed class Window_ExpeditionAssessment : Window
    {
        private readonly string title;
        private readonly string text;
        public override Vector2 InitialSize => new Vector2(620f, 500f);

        public Window_ExpeditionAssessment(string title, string text)
        {
            this.title = title;
            this.text = text;
            doCloseX = true;
            absorbInputAroundWindow = true;
        }

        public override void DoWindowContents(Rect rect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, rect.width, 32f), title);
            Text.Font = GameFont.Small;
            Rect body = new Rect(0f, 42f, rect.width, rect.height - 42f);
            Widgets.DrawMenuSection(body);
            Widgets.Label(body.ContractedBy(14f), text);
        }
    }

    public sealed class Window_ExpeditionRoute : Window
    {
        private readonly HuntingExpeditionMapComponent component;
        private readonly HuntingExpeditionRecord record;
        public override Vector2 InitialSize => new Vector2(820f, 380f);

        public Window_ExpeditionRoute(HuntingExpeditionMapComponent component, HuntingExpeditionRecord record)
        {
            this.component = component;
            this.record = record;
            doCloseX = true;
            absorbInputAroundWindow = true;
        }

        public override void DoWindowContents(Rect rect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, rect.width, 32f), "Expedition Route");
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(0f, 34f, rect.width, 24f), component?.Status(record) ?? "Expedition unavailable");
            if (record?.routeTiles == null || record.routeTiles.Count == 0)
            {
                Widgets.Label(new Rect(0f, 72f, rect.width, 40f), "No route information is available.");
                return;
            }
            float gap = 10f;
            float cellWidth = Mathf.Min(140f, (rect.width - gap * (record.routeTiles.Count - 1)) / record.routeTiles.Count);
            float total = cellWidth * record.routeTiles.Count + gap * (record.routeTiles.Count - 1);
            float x = (rect.width - total) * 0.5f;
            int current = record.caravan != null && !record.caravan.Destroyed ? (int)record.caravan.Tile :
                record.marker == null ? record.routeTiles[0] : (int)record.marker.Tile;
            for (int i = 0; i < record.routeTiles.Count; i++)
            {
                int tileId = record.routeTiles[i];
                RimWorld.Planet.Tile tile = Find.WorldGrid[(RimWorld.Planet.PlanetTile)tileId];
                float movement = Mathf.Max(0.65f, tile.PrimaryBiome?.movementDifficulty ?? 1f);
                float danger = Mathf.Clamp01(0.08f + i * 0.045f + Mathf.InverseLerp(1f, 4f, movement) * 0.18f + Mathf.Abs(tile.temperature - 18f) / 120f);
                Rect box = new Rect(x + i * (cellWidth + gap), 92f, cellWidth, 128f);
                Color color = danger < 0.22f ? new Color(0.20f, 0.38f, 0.22f) :
                    danger < 0.40f ? new Color(0.45f, 0.36f, 0.16f) : new Color(0.48f, 0.20f, 0.16f);
                Widgets.DrawBoxSolid(box, color);
                Widgets.DrawBox(box);
                if (tileId == current)
                {
                    Widgets.DrawHighlightSelected(box);
                    Widgets.DrawBoxSolid(new Rect(box.x, box.y, box.width, 5f), new Color(0.90f, 0.82f, 0.35f));
                }
                Text.Anchor = TextAnchor.MiddleCenter;
                string heading = i == 0 ? "Colony" : i == record.routeTiles.Count - 1 ? "Destination" : "Travel";
                Widgets.Label(box.ContractedBy(7f), heading + "\n\n" + (tile.PrimaryBiome?.LabelCap.ToString() ?? "Unknown") +
                    "\nDanger " + danger.ToStringPercent() + (tileId == current ? "\nCURRENT" : ""));
                Text.Anchor = TextAnchor.UpperLeft;
                if (i < record.routeTiles.Count - 1)
                    Widgets.DrawLine(new Vector2(box.xMax, box.center.y), new Vector2(box.xMax + gap, box.center.y), Color.white, 2f);
            }
            Rect legend = new Rect(0f, 242f, rect.width, 62f);
            Widgets.DrawMenuSection(legend);
            Widgets.Label(legend.ContractedBy(10f), "The marker moves between these cells as travel progresses. Green is lower danger, ochre is moderate, and red is high. Roads reduce travel time; terrain, rivers, temperature, discoveries, and route policy alter risk.");
        }
    }
}
