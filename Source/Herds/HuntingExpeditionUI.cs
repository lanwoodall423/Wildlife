using System;
using System.Collections.Generic;
using System.Linq;
using InsightCanvas;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Herds
{
    internal static class WildlifeExpeditionText
    {
        public static string Get(string key, params NamedArgument[] args)
        {
            return args == null || args.Length == 0
                ? key.Translate().ToString()
                : TranslatorFormattedStringExtensions.Translate(key, args).ToString();
        }

        public static string Number(float value) => value.ToString("0.##");

        public static string Objective(ExpeditionObjective value)
        {
            switch (value)
            {
                case ExpeditionObjective.Scout: return Get("Wildlife.UI.Expedition.Objective.Scout");
                case ExpeditionObjective.Capture: return Get("Wildlife.UI.Expedition.Objective.Capture");
                case ExpeditionObjective.Tag: return Get("Wildlife.UI.Expedition.Objective.Tag");
                case ExpeditionObjective.Protect: return Get("Wildlife.UI.Expedition.Objective.Protect");
                case ExpeditionObjective.Redirect: return Get("Wildlife.UI.Expedition.Objective.Redirect");
                default: return Get("Wildlife.UI.Expedition.Objective.Hunt");
            }
        }

        public static string Route(ExpeditionRoutePolicy value)
        {
            switch (value)
            {
                case ExpeditionRoutePolicy.Fastest: return Get("Wildlife.UI.Expedition.Route.Fastest");
                case ExpeditionRoutePolicy.Balanced: return Get("Wildlife.UI.Expedition.Route.Balanced");
                default: return Get("Wildlife.UI.Expedition.Route.Safest");
            }
        }

        public static string Risk(float value) => value < 0.34f
            ? Get("Wildlife.UI.Expedition.Risk.Cautious")
            : value < 0.67f ? Get("Wildlife.UI.Expedition.Risk.Balanced") : Get("Wildlife.UI.Expedition.Risk.Bold");

        public static string Confidence(float value) => value < 0.15f
            ? Get("Wildlife.UI.Expedition.Confidence.Unsurveyed")
            : value < 0.4f ? Get("Wildlife.UI.Expedition.Confidence.Low")
            : value < 0.7f ? Get("Wildlife.UI.Expedition.Confidence.Moderate")
            : Get("Wildlife.UI.Expedition.Confidence.High");

        public static string Stage(ExpeditionStage value)
        {
            string key = "Wildlife.UI.Expedition.Stage." + value;
            return Get(key);
        }

        public static string Destination(ExpeditionDestination destination, HuntingExpeditionMapComponent component)
        {
            if (destination == null) return Get("Wildlife.UI.Expedition.Destination.None");
            if (destination.knowledge?.discoveryLevel <= 0)
                return Get("Wildlife.UI.Expedition.Destination.Unknown", destination.distance);
            return component?.TileKnowledgeLabel(destination) ?? Get("Wildlife.UI.Expedition.Destination.Unavailable");
        }

        public static string Target(ExpeditionPlan plan, HuntingExpeditionMapComponent component)
        {
            if (plan?.objective == ExpeditionObjective.Scout) return Get("Wildlife.UI.Expedition.Target.NotRequired");
            if (plan?.unknownTarget == true) return Get("Wildlife.UI.Expedition.Target.Unknown");
            if (plan?.targetSpecies == null) return Get("Wildlife.UI.Expedition.Target.None");
            return plan.targetSpecies.LabelCap + " • " + Population(component, plan.destination, plan.targetSpecies);
        }

        public static string Population(HuntingExpeditionMapComponent component, ExpeditionDestination destination,
            ThingDef species)
        {
            if (component == null || destination == null || species == null || destination.knowledge?.discoveryLevel < 2)
                return Get("Wildlife.UI.Expedition.Value.Unknown");
            float population = component.PopulationAt(destination, species);
            if (destination.knowledge.confidence >= 0.75f)
                return Get("Wildlife.UI.Expedition.Population.About", Mathf.RoundToInt(population));
            if (population < 1f) return Get("Wildlife.UI.Expedition.Population.Unlikely");
            if (population < 5f) return Get("Wildlife.UI.Expedition.Population.Sparse");
            if (population < 15f) return Get("Wildlife.UI.Expedition.Population.Moderate");
            return Get("Wildlife.UI.Expedition.Population.Abundant");
        }

        public static string Party(ExpeditionPlan plan) => Get("Wildlife.UI.Expedition.Party.Summary",
            plan?.hunters?.Count ?? 0, plan?.packAnimals?.Count ?? 0);

        public static string Party(HuntingExpeditionRecord record) => Get("Wildlife.UI.Expedition.Party.Summary",
            record?.hunters?.Count ?? 0, record?.packAnimals?.Count ?? 0);

        public static string CurrentTarget(HuntingExpeditionRecord record)
        {
            if (record?.targetSpecies != null) return record.targetSpecies.LabelCap.ToString();
            if (record?.objective == ExpeditionObjective.Scout) return Get("Wildlife.UI.Expedition.Target.NotRequired");
            if (record?.objective == ExpeditionObjective.Redirect) return Get("Wildlife.UI.Expedition.Target.UnknownHerd");
            return Get("Wildlife.UI.Expedition.Target.Unknown");
        }

        public static string DestinationBiome(HuntingExpeditionRecord record)
        {
            if (record == null || Find.WorldGrid == null || record.destinationTile < 0)
                return Get("Wildlife.UI.Expedition.Value.Unknown");
            Tile tile = Find.WorldGrid[(PlanetTile)record.destinationTile];
            return tile?.PrimaryBiome?.LabelCap.ToString() ?? Get("Wildlife.UI.Expedition.Value.Unknown");
        }

        public static string RouteTiles(int count) => Get("Wildlife.UI.Expedition.Route.Tiles", count);
    }

    /// <summary>One native RimWorld shell around a document-owned InsightCanvas presentation.</summary>
    public abstract class WildlifeInsightWindow : Window
    {
        private readonly Vector2 initialSize;

        protected WildlifeInsightWindow(string id, string title, Vector2 initialSize, bool absorbInput = true,
            bool closeOnCancel = true, bool forcePause = false)
        {
            this.initialSize = initialSize;
            Document = new InsightUiDocument(id, InsightUi.Empty(id + ".root"));
            WildlifeUiTheme.Configure(Document, false, false);
            Host = new InsightUiHost(Document);
            optionalTitle = title;
            doCloseX = true;
            doCloseButton = false;
            resizeable = true;
            absorbInputAroundWindow = absorbInput;
            this.closeOnCancel = closeOnCancel;
            this.forcePause = forcePause;
        }

        protected InsightUiDocument Document { get; private set; }
        protected InsightUiHost Host { get; private set; }

        public override Vector2 InitialSize => initialSize;

        protected void InitializeDocument()
        {
            Document.Root = BuildRoot();
        }

        protected void RebuildDocument()
        {
            if (Document != null) Document.Root = BuildRoot();
        }

        protected abstract InsightUiElement BuildRoot();

        public override void DoWindowContents(Rect inRect)
        {
            Host.Draw(inRect, Time.deltaTime);
        }

        public override void PostClose()
        {
            Host.PostClose();
            base.PostClose();
        }

        protected static InsightUiElement Card(string id, params InsightUiElement[] children)
        {
            return InsightUi.Surface(id, InsightUi.Column(id + ".content", children).SetGap(5f).SetPadding(8f));
        }

        protected static InsightUiElement Empty(string key, string helpKey = null)
        {
            return InsightUi.Callout(key, InsightUiCalloutSeverity.Info, WildlifeExpeditionText.Get(key),
                helpKey == null ? null : WildlifeExpeditionText.Get(helpKey));
        }

        protected static InsightUiElement ButtonRow(string id, params InsightUiElement[] children)
        {
            return InsightUi.Wrap(id, children).SetGap(6f);
        }

        protected static InsightUiElement Summary(string id, params InsightUiElement[] children)
        {
            return InsightUi.Wrap(id, children).SetGap(6f).SetPadding(2f);
        }
    }

    public sealed class Window_InteractiveExpeditionEncounter : WildlifeInsightWindow
    {
        private readonly HuntingExpeditionMapComponent component;
        private readonly HuntingExpeditionRecord record;

        public Window_InteractiveExpeditionEncounter(HuntingExpeditionMapComponent component,
            HuntingExpeditionRecord record)
            : base("wildlife-expedition-encounter", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Encounter.Title"),
                new Vector2(700f, 620f), true, false, true)
        {
            this.component = component;
            this.record = record;
            doCloseX = false;
            InitializeDocument();
        }

        protected override InsightUiElement BuildRoot()
        {
            if (record == null || component == null)
                return InsightUi.Column("encounter.unavailable", Empty("Wildlife.UI.Expedition.Encounter.Unavailable"))
                    .SetGap(8f).SetPadding(10f);

            List<Tuple<string, string>> choices = Choices();
            InsightUiElement list = choices.Count == 0
                ? Empty("Wildlife.UI.Expedition.Encounter.NoChoices")
                : InsightUi.VirtualList("encounter.choices", choices.Count, 82f, index =>
                    Card("encounter.choice." + index,
                        InsightUi.Button("encounter.choice." + index + ".button", choices[index].Item1,
                            () => Resolve(index)),
                        InsightUi.Label("encounter.choice." + index + ".description", choices[index].Item2,
                            InsightUiTextStyle.Caption)));

            return InsightUi.Scroll("encounter.scroll",
                InsightUi.Column("encounter.root",
                    InsightUi.Breadcrumbs("encounter.breadcrumbs", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Breadcrumb"),
                        WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Encounter.Breadcrumb")),
                    InsightUi.SectionHeader("encounter.header", EncounterTitle(),
                        WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Encounter.PauseHelp")),
                    InsightUi.Callout("encounter.report", InsightUiCalloutSeverity.Info,
                        WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Encounter.Context"), EncounterReport()),
                    list).SetGap(8f).SetPadding(8f));
        }

        private string EncounterTitle()
        {
            if (record.roamingEncounterAnimal != null)
                return WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Encounter.RoamingTitle");
            if (record.pendingEvent != null) return record.pendingEvent.LabelCap.ToString();
            return WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Encounter.Title");
        }

        private List<Tuple<string, string>> Choices()
        {
            if (record.pendingEvent != null)
                return (record.pendingEvent.choices ?? new List<ExpeditionEventChoiceDef>())
                    .Select(choice => Tuple.Create(choice.label ?? WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Choice.Unavailable"),
                        choice.description ?? string.Empty)).ToList();
            if (record.roamingEncounterAnimal != null)
            {
                string[] labels =
                {
                    WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Choice.Observe"),
                    WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Choice.Help"),
                    WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Choice.Hunt"),
                    WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Choice.Tag"),
                    WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Choice.Redirect"),
                    WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Choice.Avoid")
                };
                string[] descriptions =
                {
                    WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Choice.Observe.Help"),
                    WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Choice.Help.Help"),
                    WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Choice.Hunt.Help"),
                    WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Choice.Tag.Help"),
                    WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Choice.Redirect.Help"),
                    WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Choice.Avoid.Help")
                };
                return labels.Select((label, index) => Tuple.Create(label, descriptions[index])).ToList();
            }
            return new List<Tuple<string, string>>
            {
                Tuple.Create(WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Choice.Follow"),
                    WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Choice.Follow.Help")),
                Tuple.Create(WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Choice.Document"),
                    WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Choice.Document.Help")),
                Tuple.Create(WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Choice.Detour"),
                    WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Choice.Detour.Help"))
            };
        }

        private string EncounterReport()
        {
            if (record.pendingEvent != null) return record.pendingEvent.narrative ?? record.pendingEvent.description;
            if (record.roamingEncounterAnimal != null)
                return WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Encounter.RoamingBody",
                    record.roamingEncounterAnimal.LabelShortCap, record.roamingEncounterAnimal.def.LabelCap);
            return WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Encounter.FieldSigns",
                record.interactiveEncounter ?? WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Value.Unknown"));
        }

        private void Resolve(int choice)
        {
            Host.RunWithOverlayOwnership(() => component.ResolveInteractiveEncounter(record, choice));
            Close(false);
        }
    }

    public sealed class Window_ExpeditionRange : WildlifeInsightWindow
    {
        private readonly Map map;
        private readonly List<ExpeditionDestination> destinations;
        private string search = string.Empty;

        public Window_ExpeditionRange(Map map)
            : base("wildlife-expedition-range", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Range.Title"),
                new Vector2(980f, 700f))
        {
            this.map = map;
            destinations = map?.GetComponent<HuntingExpeditionMapComponent>()?.Destinations()
                .Take(96).ToList() ?? new List<ExpeditionDestination>();
            InitializeDocument();
        }

        protected override InsightUiElement BuildRoot()
        {
            HuntingExpeditionMapComponent component = map?.GetComponent<HuntingExpeditionMapComponent>();
            if (component == null)
                return InsightUi.Column("range.unavailable", Empty("Wildlife.UI.Expedition.MapUnavailable"))
                    .SetGap(8f).SetPadding(10f);
            List<ExpeditionDestination> filtered = destinations.Where(destination => Matches(destination)).ToList();
            InsightUiElement list = filtered.Count == 0
                ? Empty("Wildlife.UI.Expedition.Range.Empty", "Wildlife.UI.Expedition.Range.Empty.Help")
                : InsightUi.VirtualList("range.items", filtered.Count, 74f, index => RangeRow(component, filtered[index], index));
            return InsightUi.Scroll("range.scroll",
                InsightUi.Column("range.root",
                    InsightUi.Breadcrumbs("range.breadcrumbs", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Breadcrumb")),
                    InsightUi.SectionHeader("range.header", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Range.Title"),
                        WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Range.Subtitle")),
                    InsightUi.SearchField("range.search", search,
                        WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Search"), _ => RebuildDocument())
                        .Bind(() => search, value => search = value ?? string.Empty),
                    list).SetGap(8f).SetPadding(8f));
        }

        private bool Matches(ExpeditionDestination destination)
        {
            if (string.IsNullOrWhiteSpace(search)) return true;
            string haystack = (destination.biome?.LabelCap.ToString() ?? string.Empty) + " " +
                WildlifeExpeditionText.Destination(destination, map?.GetComponent<HuntingExpeditionMapComponent>());
            return haystack.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private InsightUiElement RangeRow(HuntingExpeditionMapComponent component, ExpeditionDestination destination, int index)
        {
            bool known = destination?.knowledge?.discoveryLevel > 0;
            string biome = known ? destination.biome?.LabelCap.ToString() ?? WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Value.Unknown")
                : WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Value.Unknown");
            string survey = known ? WildlifeExpeditionText.Confidence(destination.knowledge.confidence)
                : WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Value.Unknown");
            string wildlife = !known ? WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Value.Unknown") :
                component.KnownSpecies(destination).Count == 0
                    ? WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Wildlife.None")
                    : string.Join(", ", component.KnownSpecies(destination).Take(3).Select(value => value.LabelCap.ToString()));
            string access = !known ? WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Value.Unknown") :
                (destination.road ? WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Access.Road") + " " : string.Empty) +
                (destination.river ? WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Access.River") : string.Empty);
            return Card("range.row." + index,
                InsightUi.Wrap("range.row." + index + ".summary",
                    InsightUi.Badge("range.row." + index + ".distance", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Range.Distance", destination.distance)),
                    InsightUi.Label("range.row." + index + ".biome", biome, InsightUiTextStyle.Heading),
                    InsightUi.Label("range.row." + index + ".survey", survey, InsightUiTextStyle.Caption),
                    InsightUi.Label("range.row." + index + ".wildlife", wildlife, InsightUiTextStyle.Caption),
                    InsightUi.Label("range.row." + index + ".access", access, InsightUiTextStyle.Caption)).SetGap(8f),
                InsightUi.Label("range.row." + index + ".discovery",
                    known && !destination.knowledge.discovery.NullOrEmpty()
                        ? destination.knowledge.discovery
                        : WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Discovery.None"), InsightUiTextStyle.Caption));
        }
    }

    public sealed class Window_HuntingExpeditionSetup : WildlifeInsightWindow
    {
        private readonly Map map;
        private readonly ExpeditionPlan plan = new ExpeditionPlan();
        private readonly ExpeditionDestination initialDestination;
        private readonly HashSet<Pawn> selectedHunters = new HashSet<Pawn>();
        private readonly HashSet<Pawn> selectedPackAnimals = new HashSet<Pawn>();
        private bool initialized;

        public Window_HuntingExpeditionSetup(Map map) : this(map, null) { }

        public Window_HuntingExpeditionSetup(Map map, ExpeditionDestination destination)
            : this(map, destination, null, ExpeditionObjective.Hunt) { }

        public Window_HuntingExpeditionSetup(Map map, ExpeditionDestination destination,
            ThingDef targetSpecies, ExpeditionObjective objective)
            : this(map, destination, targetSpecies, objective, null) { }

        public Window_HuntingExpeditionSetup(Map map, ExpeditionDestination destination,
            ThingDef targetSpecies, ExpeditionObjective objective, Pawn trailTargetAnimal)
            : base("wildlife-expedition-setup", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Setup.Title"),
                new Vector2(1120f, 780f))
        {
            this.map = map;
            initialDestination = destination;
            plan.targetSpecies = targetSpecies;
            plan.objective = objective;
            plan.trailTargetAnimal = trailTargetAnimal;
            InitializeDocument();
        }

        public static string ObjectiveLabel(ExpeditionObjective objective) => WildlifeExpeditionText.Objective(objective);

        protected override InsightUiElement BuildRoot()
        {
            HuntingExpeditionMapComponent component = map?.GetComponent<HuntingExpeditionMapComponent>();
            if (map == null || component == null)
                return InsightUi.Column("setup.unavailable", Empty("Wildlife.UI.Expedition.MapUnavailable"))
                    .SetGap(8f).SetPadding(10f);
            EnsureInitialized(component);
            if (plan.trailTargetAnimal != null)
            {
                plan.objective = ExpeditionObjective.Hunt;
                plan.destination = initialDestination;
                plan.targetSpecies = plan.trailTargetAnimal.def;
                plan.unknownTarget = false;
            }
            SyncPlan(component);

            bool ready = Readiness(component, out string reason);
            InsightUiElement header = InsightUi.Column("setup.header",
                InsightUi.Breadcrumbs("setup.breadcrumbs", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Breadcrumb")),
                InsightUi.SectionHeader("setup.title", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Setup.Title"),
                    WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Setup.Subtitle")),
                Summary("setup.summary",
                    InsightUi.StatRow("setup.summary.objective", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Summary.Objective"),
                        WildlifeExpeditionText.Objective(plan.objective)),
                    InsightUi.StatRow("setup.summary.destination", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Summary.Destination"),
                        WildlifeExpeditionText.Destination(plan.destination, component)),
                    InsightUi.StatRow("setup.summary.target", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Summary.Target"),
                        WildlifeExpeditionText.Target(plan, component)),
                    InsightUi.StatRow("setup.summary.party", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Summary.Party"),
                        WildlifeExpeditionText.Party(plan)),
                    InsightUi.StatRow("setup.summary.eta", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Summary.Travel"),
                        WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Summary.Days", WildlifeExpeditionText.Number(component.EstimateDays(plan))))),
                ready ? InsightUi.Badge("setup.ready", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Ready"))
                    : InsightUi.Badge("setup.not-ready", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.NotReady"))).SetGap(6f);

            InsightUiNavigation navigation = InsightUi.Navigation("setup.navigation", 720f);
            navigation.Add("overview", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Tab.Overview"), BuildOverview(component));
            navigation.Add("party", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Tab.Party"), BuildParty(component));
            navigation.Add("supplies", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Tab.Supplies"), BuildSupplies(component));
            navigation.Add("forecast", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Tab.Forecast"), BuildForecast(component, ready, reason));
            return InsightUi.Scroll("setup.scroll", InsightUi.Column("setup.root", header, navigation)
                .SetGap(10f).SetPadding(8f));
        }

        private InsightUiElement BuildOverview(HuntingExpeditionMapComponent component)
        {
            bool trail = plan.trailTargetAnimal != null;
            ExpeditionObjective[] objectives = ObjectiveOptions();
            string[] objectiveLabels = objectives.Select(WildlifeExpeditionText.Objective).ToArray();
            int objectiveIndex = Math.Max(0, Array.IndexOf(objectives, plan.objective));
            InsightUiSelect objective = InsightUi.Select("setup.objective", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Field.Objective"),
                objectiveLabels, objectiveIndex, (index, value) => ApplyObjective(objectives[index]));
            objective.Enabled = !trail;

            InsightUiElement destination = Card("setup.destination.card",
                InsightUi.SectionHeader("setup.destination.header", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Field.Destination"),
                    WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Destination.Help")),
                InsightUi.Label("setup.destination.value", WildlifeExpeditionText.Destination(plan.destination, component), InsightUiTextStyle.Heading),
                InsightUi.Button("setup.destination.action", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Action.BrowseDestinations"),
                    () => OpenDestinationPicker(component)));

            List<ThingDef> knownTargets = plan.destination == null ? new List<ThingDef>() : component.KnownSpecies(plan.destination);
            if (plan.objective == ExpeditionObjective.Redirect)
                knownTargets = knownTargets.Where(HuntingExpeditionMapComponent.IsHerdSpecies).ToList();
            List<ThingDef> targetValues = knownTargets.Take(32).ToList();
            string[] targetLabels = new[] { WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Target.Unknown") }
                .Concat(targetValues.Select(value => value.LabelCap.ToString())).ToArray();
            int targetIndex = plan.unknownTarget || plan.targetSpecies == null ? 0 :
                Math.Max(0, targetValues.IndexOf(plan.targetSpecies) + 1);
            InsightUiSelect target = InsightUi.Select("setup.target", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Field.Target"),
                targetLabels, targetIndex, (index, value) =>
                {
                    if (index == 0) { plan.targetSpecies = null; plan.unknownTarget = true; }
                    else { plan.targetSpecies = targetValues[index - 1]; plan.unknownTarget = false; }
                    RebuildDocument();
                });
            target.Enabled = !trail && plan.objective != ExpeditionObjective.Scout;

            string[] routes = Enum.GetValues(typeof(ExpeditionRoutePolicy)).Cast<ExpeditionRoutePolicy>()
                .Select(WildlifeExpeditionText.Route).ToArray();
            ExpeditionRoutePolicy[] routeValues = Enum.GetValues(typeof(ExpeditionRoutePolicy)).Cast<ExpeditionRoutePolicy>().ToArray();
            InsightUiSelect route = InsightUi.Select("setup.route", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Field.Route"), routes,
                Math.Max(0, Array.IndexOf(routeValues, plan.routePolicy)), (index, value) =>
                {
                    plan.routePolicy = routeValues[index];
                    RebuildDocument();
                });

            return InsightUi.Column("setup.overview",
                Card("setup.objective.card", objective,
                    InsightUi.Label("setup.objective.help", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Objective.Help"), InsightUiTextStyle.Caption)),
                destination,
                Card("setup.target.card", target,
                    InsightUi.Label("setup.target.help", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Target.Help"), InsightUiTextStyle.Caption)),
                Card("setup.route.card", route,
                    InsightUi.Label("setup.route.help", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Route.Help"), InsightUiTextStyle.Caption))).SetGap(8f);
        }

        private InsightUiElement BuildParty(HuntingExpeditionMapComponent component)
        {
            List<Pawn> hunters = EligibleHunters();
            List<Pawn> packs = EligiblePackAnimals();
            int count = hunters.Count + packs.Count;
            InsightUiElement list = count == 0
                ? Empty("Wildlife.UI.Expedition.Party.Empty", "Wildlife.UI.Expedition.Party.Empty.Help")
                : InsightUi.VirtualList("setup.party.items", count, 62f, index =>
                {
                    if (index < hunters.Count)
                    {
                        Pawn pawn = hunters[index];
                        bool blocked = plan.objective == ExpeditionObjective.Hunt && pawn.WorkTagIsDisabled(WorkTags.Violent);
                        float skill = plan.targetSpecies == null ? ColonistHuntingUtility.HuntingSkill(pawn) :
                            ColonistHuntingUtility.HuntingSkill(pawn, plan.targetSpecies);
                        InsightUiToggle toggle = InsightUi.Toggle("setup.party.hunter." + pawn.thingIDNumber,
                            pawn.LabelShortCap + " • " + WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Skill", WildlifeExpeditionText.Number(skill)),
                            selectedHunters.Contains(pawn), chosen =>
                            {
                                if (chosen && !blocked && selectedHunters.Count < 8) selectedHunters.Add(pawn);
                                else selectedHunters.Remove(pawn);
                                RebuildDocument();
                            });
                        toggle.Enabled = !blocked;
                        return Card("setup.party.hunter.card." + pawn.thingIDNumber, toggle,
                            InsightUi.Label("setup.party.hunter.detail." + pawn.thingIDNumber,
                                blocked ? WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Party.Pacifist") :
                                    pawn.equipment?.Primary?.LabelShortCap.ToString() ?? WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Party.Unarmed"),
                                InsightUiTextStyle.Caption));
                    }
                    Pawn animal = packs[index - hunters.Count];
                    InsightUiToggle toggleAnimal = InsightUi.Toggle("setup.party.pack." + animal.thingIDNumber,
                        animal.LabelShortCap + " • " + animal.def.LabelCap,
                        selectedPackAnimals.Contains(animal), chosen =>
                        {
                            if (chosen) selectedPackAnimals.Add(animal); else selectedPackAnimals.Remove(animal);
                            RebuildDocument();
                        });
                    return Card("setup.party.pack.card." + animal.thingIDNumber, toggleAnimal,
                        InsightUi.Label("setup.party.pack.detail." + animal.thingIDNumber,
                            WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Party.Carry", Mathf.RoundToInt(Mathf.Max(12f, animal.BodySize * 35f))),
                            InsightUiTextStyle.Caption));
                });
            return InsightUi.Column("setup.party",
                ButtonRow("setup.party.actions",
                    InsightUi.Button("setup.party.best", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Action.BestHunters"),
                        SelectBestHunters),
                    InsightUi.Button("setup.party.clear", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Action.ClearParty"),
                        () => { selectedHunters.Clear(); selectedPackAnimals.Clear(); RebuildDocument(); })),
                InsightUi.SectionHeader("setup.party.header", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Party.Title"),
                    WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Party.Help")),
                list).SetGap(8f);
        }

        private InsightUiElement BuildSupplies(HuntingExpeditionMapComponent component)
        {
            float days = component.EstimateDays(plan);
            float required = ExpeditionSupplyUtility.RequiredNutrition(plan, days);
            float selected = ExpeditionSupplyUtility.SelectedNutrition(plan.provisions);
            int bedrolls = ExpeditionSupplyUtility.AvailableBedrolls(map);
            int medicine = plan.medicines.Values.Sum();
            bool enoughBedrolls = selectedHunters.Count > 0 && bedrolls >= selectedHunters.Count;
            InsightUiToggle bedrollToggle = InsightUi.Toggle("setup.supplies.bedrolls",
                WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Supplies.Bedrolls", bedrolls), plan.useBedrolls,
                value => { plan.useBedrolls = value && enoughBedrolls; RebuildDocument(); });
            bedrollToggle.Enabled = enoughBedrolls;
            return InsightUi.Column("setup.supplies",
                Summary("setup.supplies.summary",
                    InsightUi.StatRow("setup.supplies.days", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Summary.Travel"), WildlifeExpeditionText.Number(days)),
                    InsightUi.StatRow("setup.supplies.medicine", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Supplies.Medicine"), medicine.ToString()),
                    InsightUi.StatRow("setup.supplies.capacity", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Supplies.Carry"),
                        WildlifeExpeditionText.Number(CarryCapacity()))),
                Card("setup.supplies.provisions",
                    InsightUi.SectionHeader("setup.supplies.provisions.header", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Supplies.Provisions"),
                        WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Supplies.Provisions.Help")),
                    InsightUi.Meter("setup.supplies.nutrition", selected, Mathf.Max(1f, required)),
                    InsightUi.StatRow("setup.supplies.nutrition.value", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Supplies.Nutrition"),
                        WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Supplies.NutritionValue", WildlifeExpeditionText.Number(selected), WildlifeExpeditionText.Number(required))),
                    InsightUi.Button("setup.supplies.provisions.open", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Action.EditProvisions"),
                        () => Find.WindowStack.Add(new Window_ExpeditionProvisions(map, plan, days)))),
                Card("setup.supplies.medicine",
                    InsightUi.SectionHeader("setup.supplies.medicine.header", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Supplies.Medicine"),
                        WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Supplies.Medicine.Help")),
                    InsightUi.Button("setup.supplies.medicine.open", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Action.EditMedicine"),
                        () => Find.WindowStack.Add(new Window_ExpeditionMedicine(map, plan.medicines)))),
                Card("setup.supplies.options", bedrollToggle,
                    InsightUi.Toggle("setup.supplies.alternatives", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Supplies.Alternatives"),
                        plan.allowAlternatives, value => { plan.allowAlternatives = value; RebuildDocument(); }),
                    InsightUi.Label("setup.supplies.options.help", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Supplies.OptionsHelp"), InsightUiTextStyle.Caption))).SetGap(8f);
        }

        private InsightUiElement BuildForecast(HuntingExpeditionMapComponent component, bool ready, string reason)
        {
            string details = component.ForecastDetails(plan);
            InsightUiElement readiness = ready
                ? InsightUi.Callout("setup.forecast.ready", InsightUiCalloutSeverity.Success,
                    WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Ready"), details)
                : InsightUi.Callout("setup.forecast.blocked", InsightUiCalloutSeverity.Warning,
                    WildlifeExpeditionText.Get("Wildlife.UI.Expedition.NotReady"), reason);
            InsightUiButton begin = InsightUi.Button("setup.forecast.begin",
                WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Action.Begin"), Begin);
            begin.Enabled = ready;
            return InsightUi.Column("setup.forecast", readiness,
                Summary("setup.forecast.stats",
                    InsightUi.StatRow("setup.forecast.route", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Field.Route"),
                        WildlifeExpeditionText.Route(plan.routePolicy)),
                    InsightUi.StatRow("setup.forecast.risk", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Field.Risk"),
                        WildlifeExpeditionText.Risk(plan.riskTolerance)),
                    InsightUi.StatRow("setup.forecast.target", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Field.Target"),
                        WildlifeExpeditionText.Target(plan, component))),
                begin).SetGap(8f);
        }

        private void EnsureInitialized(HuntingExpeditionMapComponent component)
        {
            if (initialized) return;
            initialized = true;
            plan.destination = initialDestination;
            foreach (Pawn pawn in EligibleHunters().OrderByDescending(ColonistHuntingUtility.HuntingSkill).Take(3))
                selectedHunters.Add(pawn);
            plan.hunters = selectedHunters.ToList();
            AutoFillProvisions(component.EstimateDays(plan));
            Current.Game?.GetComponent<HuntResourceDiscovery>()?.Refresh(map);
            foreach (HuntResourceDef resource in AvailableResourceDefs())
                if (CountResource(resource) >= resource.RequiredFor(selectedHunters.Count)) plan.resources.Add(resource.defName);
        }

        private void SyncPlan(HuntingExpeditionMapComponent component)
        {
            plan.hunters = selectedHunters.Where(pawn => pawn != null).ToList();
            plan.packAnimals = selectedPackAnimals.Where(pawn => pawn != null).ToList();
            float days = component.EstimateDays(plan);
            float required = ExpeditionSupplyUtility.RequiredNutrition(plan, days);
            float selected = ExpeditionSupplyUtility.SelectedNutrition(plan.provisions);
            float daily = ExpeditionSupplyUtility.DailyNutrition(plan);
            plan.foodDays = daily <= 0f ? 0 : Mathf.Clamp(Mathf.FloorToInt((selected - required) / daily), 0, 3);
            plan.medicine = plan.medicines.Values.Sum();
            if (plan.useBedrolls && ExpeditionSupplyUtility.AvailableBedrolls(map) < selectedHunters.Count)
                plan.useBedrolls = false;
        }

        private void ApplyObjective(ExpeditionObjective objective)
        {
            plan.objective = objective;
            if (objective == ExpeditionObjective.Scout)
            {
                plan.targetSpecies = null;
                plan.unknownTarget = false;
            }
            else if (objective == ExpeditionObjective.Redirect && plan.targetSpecies != null &&
                !HuntingExpeditionMapComponent.IsHerdSpecies(plan.targetSpecies))
            {
                plan.targetSpecies = null;
                plan.unknownTarget = true;
            }
            selectedHunters.RemoveWhere(pawn => objective == ExpeditionObjective.Hunt && pawn.WorkTagIsDisabled(WorkTags.Violent));
            RebuildDocument();
        }

        private void OpenDestinationPicker(HuntingExpeditionMapComponent component)
        {
            if (plan.trailTargetAnimal != null) return;
            Find.WindowStack.Add(new Window_ExpeditionDestinationPicker(component, plan.destination, destination =>
            {
                plan.destination = destination;
                if (plan.targetSpecies != null && !component.KnownSpecies(destination).Contains(plan.targetSpecies))
                {
                    plan.targetSpecies = null;
                    plan.unknownTarget = true;
                }
                AutoFillProvisions(component.EstimateDays(plan));
                RebuildDocument();
            }, this));
        }

        private void SelectBestHunters()
        {
            selectedHunters.Clear();
            foreach (Pawn pawn in EligibleHunters().Where(CanJoinCurrentObjective)
                .OrderByDescending(value => ColonistHuntingUtility.HuntingSkill(value)).Take(4)) selectedHunters.Add(pawn);
            RebuildDocument();
        }

        private void Begin()
        {
            HuntingExpeditionMapComponent component = map?.GetComponent<HuntingExpeditionMapComponent>();
            if (component == null) return;
            SyncPlan(component);
            if (component.Begin(plan, out string failure)) Close();
            else Messages.Message(failure ?? WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Begin.Failed"),
                MessageTypeDefOf.RejectInput, false);
        }

        private bool Readiness(HuntingExpeditionMapComponent component, out string reason)
        {
            reason = null;
            SyncPlan(component);
            float days = component.EstimateDays(plan);
            float requiredNutrition = ExpeditionSupplyUtility.RequiredNutrition(plan, days);
            float selectedNutrition = ExpeditionSupplyUtility.SelectedNutrition(plan.provisions);
            int bedrolls = ExpeditionSupplyUtility.AvailableBedrolls(map);
            List<HuntResourceDef> resources = AvailableResourceDefs();
            if (!WildlifeProgression.Unlocked(WildlifeCapability.HuntingExpedition)) reason = WildlifeProgression.LockReason(WildlifeCapability.HuntingExpedition);
            else if (plan.destination == null) reason = WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Readiness.Destination");
            else if (selectedHunters.Count == 0) reason = WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Readiness.Hunters");
            else if (selectedHunters.Count > 8) reason = WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Readiness.TooManyHunters");
            else if (plan.objective == ExpeditionObjective.Hunt && selectedHunters.Any(pawn => pawn.WorkTagIsDisabled(WorkTags.Violent)))
                reason = WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Readiness.Pacifist");
            else if (plan.objective == ExpeditionObjective.Tag && !WildlifeProgression.Unlocked(WildlifeCapability.Telemetry))
                reason = WildlifeProgression.LockReason(WildlifeCapability.Telemetry);
            else if (plan.objective != ExpeditionObjective.Scout && plan.targetSpecies == null && !plan.unknownTarget)
                reason = WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Readiness.Target");
            else if (plan.objective == ExpeditionObjective.Redirect && plan.targetSpecies != null &&
                !HuntingExpeditionMapComponent.IsHerdSpecies(plan.targetSpecies))
                reason = WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Readiness.Herd");
            else if (selectedNutrition + 0.001f < requiredNutrition)
                reason = WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Readiness.Provisions");
            else if (!ExpeditionSupplyUtility.ManifestAvailable(map, plan.provisions) ||
                !ExpeditionSupplyUtility.ManifestAvailable(map, plan.medicines))
                reason = WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Readiness.SupplyUnavailable");
            else if (plan.useBedrolls && bedrolls < selectedHunters.Count)
                reason = WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Readiness.Bedrolls");
            else if (resources.Any(resource => plan.resources.Contains(resource.defName) &&
                CountResource(resource) < resource.RequiredFor(selectedHunters.Count)))
                reason = WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Readiness.Equipment");
            else if (plan.targetSpecies != null && (plan.objective == ExpeditionObjective.Hunt || plan.objective == ExpeditionObjective.Capture) &&
                !map.GetComponent<WildlifeStewardMapComponent>().CanHunt(plan.targetSpecies, out reason)) { }
            return reason.NullOrEmpty();
        }

        private ExpeditionObjective[] ObjectiveOptions()
        {
            List<ExpeditionObjective> result = new List<ExpeditionObjective>
            {
                ExpeditionObjective.Scout, ExpeditionObjective.Hunt, ExpeditionObjective.Capture
            };
            if (WildlifeProgression.Unlocked(WildlifeCapability.Telemetry)) result.Add(ExpeditionObjective.Tag);
            result.Add(ExpeditionObjective.Redirect);
            return result.ToArray();
        }

        private List<Pawn> EligibleHunters() => map?.mapPawns?.FreeColonistsSpawned
            .Where(pawn => pawn?.Downed == false && !map.GetComponent<HuntingExpeditionMapComponent>().PawnOnExpedition(pawn))
            .OrderBy(pawn => pawn.LabelShortCap.ToString()).ToList() ?? new List<Pawn>();

        private List<Pawn> EligiblePackAnimals() => map?.mapPawns?.SpawnedColonyAnimals
            .Where(pawn => pawn?.Downed == false && pawn.RaceProps?.packAnimal == true &&
                !map.GetComponent<HuntingExpeditionMapComponent>().PawnOnExpedition(pawn))
            .OrderBy(pawn => pawn.LabelShortCap.ToString()).ToList() ?? new List<Pawn>();

        private bool CanJoinCurrentObjective(Pawn pawn) => pawn != null &&
            (plan.objective != ExpeditionObjective.Hunt || !pawn.WorkTagIsDisabled(WorkTags.Violent));

        private void AutoFillProvisions(float estimatedDays)
        {
            plan.provisions.Clear();
            float remaining = ExpeditionSupplyUtility.RequiredNutrition(plan, estimatedDays);
            foreach (KeyValuePair<ThingDef, int> pair in ExpeditionSupplyUtility.AvailableFoods(map)
                .OrderBy(pair => pair.Key.BaseMarketValue / Mathf.Max(0.01f, ExpeditionSupplyUtility.NutritionPerUnit(pair.Key))))
            {
                if (remaining <= 0.001f) break;
                float each = Mathf.Max(0.01f, ExpeditionSupplyUtility.NutritionPerUnit(pair.Key));
                int count = Mathf.Min(pair.Value, Mathf.CeilToInt(remaining / each));
                if (count <= 0) continue;
                plan.provisions[pair.Key] = count;
                remaining -= count * each;
            }
        }

        private List<HuntResourceDef> AvailableResourceDefs() =>
            DefDatabase<HuntResourceDef>.AllDefsListForReading
                .Where(resource => Current.Game?.GetComponent<HuntResourceDiscovery>()?.IsDiscovered(resource) == true &&
                    (!resource.enabledByScentMasking || HerdsMod.Settings.enableScentMasking))
                .OrderBy(resource => resource.label).Take(32).ToList();

        private int CountResource(HuntResourceDef resource)
        {
            if (resource.use == HuntResourceUse.ScentChargePerHunter)
                return map.listerBuildings.allBuildingsColonist.OfType<Building_WildlifeTool>()
                    .Where(tool => tool.def == resource.sourceBuildingDef && tool.active).Sum(tool => tool.scentCharges);
            return resource.thingDef == null ? 0 : ExpeditionSupplyUtility.AvailableThing(map, resource.thingDef);
        }

        private float CarryCapacity() => selectedHunters.Count * 18f +
            selectedPackAnimals.Sum(pawn => Mathf.Max(12f, pawn.BodySize * 35f));
    }

    public sealed class Window_ExpeditionDestinationPicker : WildlifeInsightWindow
    {
        private readonly HuntingExpeditionMapComponent component;
        private readonly Action<ExpeditionDestination> onConfirm;
        private readonly Window returnWindow;
        private readonly List<ExpeditionDestination> destinations;
        private ExpeditionDestination selected;
        private string search = string.Empty;

        public Window_ExpeditionDestinationPicker(HuntingExpeditionMapComponent component,
            ExpeditionDestination selected, Action<ExpeditionDestination> onConfirm, Window returnWindow = null)
            : base("wildlife-expedition-destinations", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Destination.Title"),
                new Vector2(1000f, 720f))
        {
            this.component = component;
            this.selected = selected;
            this.onConfirm = onConfirm;
            this.returnWindow = returnWindow;
            destinations = component?.Destinations().Take(96).ToList() ?? new List<ExpeditionDestination>();
            InitializeDocument();
        }

        protected override InsightUiElement BuildRoot()
        {
            if (component == null)
                return InsightUi.Column("destination.unavailable", Empty("Wildlife.UI.Expedition.MapUnavailable"))
                    .SetGap(8f).SetPadding(10f);
            List<ExpeditionDestination> filtered = destinations.Where(Matches).ToList();
            InsightUiElement list = filtered.Count == 0
                ? Empty("Wildlife.UI.Expedition.Destination.Empty", "Wildlife.UI.Expedition.Destination.Empty.Help")
                : InsightUi.VirtualList("destination.items", filtered.Count, 74f,
                    index => DestinationRow(filtered[index], index));
            InsightUiElement detail = selected == null ?
                Empty("Wildlife.UI.Expedition.Destination.Select", "Wildlife.UI.Expedition.Destination.Select.Help") :
                DestinationDetail();
            return InsightUi.Scroll("destination.scroll",
                InsightUi.Column("destination.root",
                    InsightUi.Breadcrumbs("destination.breadcrumbs", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Breadcrumb")),
                    InsightUi.SectionHeader("destination.header", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Destination.Title"),
                        WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Destination.Subtitle")),
                    InsightUi.SearchField("destination.search", search,
                        WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Search"), _ => RebuildDocument())
                        .Bind(() => search, value => search = value ?? string.Empty),
                    InsightUi.Split("destination.split", list, detail, 0.56f)).SetGap(8f).SetPadding(8f));
        }

        private bool Matches(ExpeditionDestination destination)
        {
            if (string.IsNullOrWhiteSpace(search)) return true;
            string text = (destination.biome?.LabelCap.ToString() ?? string.Empty) + " " + destination.tileId;
            return text.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private InsightUiElement DestinationRow(ExpeditionDestination destination, int index)
        {
            bool known = destination.knowledge?.discoveryLevel > 0;
            string title = known ? destination.biome?.LabelCap.ToString() ?? WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Value.Unknown")
                : WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Value.Unknown");
            string subtitle = known ? WildlifeExpeditionText.Confidence(destination.knowledge.confidence)
                : WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Destination.Unsurveyed");
            InsightUiButton button = InsightUi.Button("destination.row." + index + ".button", title,
                () => { selected = destination; RebuildDocument(); });
            button.SelectedProvider = () => selected?.tileId == destination.tileId;
            return Card("destination.row." + index, button,
                InsightUi.Label("destination.row." + index + ".detail",
                    WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Range.Distance", destination.distance) + " • " + subtitle,
                    InsightUiTextStyle.Caption));
        }

        private InsightUiElement DestinationDetail()
        {
            bool known = selected.knowledge?.discoveryLevel > 0;
            List<InsightUiElement> facts = new List<InsightUiElement>
            {
                InsightUi.StatRow("destination.detail.distance", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Destination.Distance"),
                    WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Range.Distance", selected.distance)),
                InsightUi.StatRow("destination.detail.survey", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Destination.Survey"),
                    known ? WildlifeExpeditionText.Confidence(selected.knowledge.confidence) : WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Value.Unknown")),
                InsightUi.StatRow("destination.detail.wildlife", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Destination.Wildlife"),
                    !known ? WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Value.Unknown") : component.KnownSpecies(selected).Count.ToString()),
                InsightUi.StatRow("destination.detail.risk", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Destination.Risk"),
                    known ? selected.danger.ToStringPercent() : WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Value.Unknown"))
            };
            if (known)
            {
                facts.Add(InsightUi.StatRow("destination.detail.access", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Destination.Access"),
                    (selected.road ? WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Access.Road") : string.Empty) +
                    (selected.river ? " " + WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Access.River") : string.Empty)));
                facts.Add(InsightUi.Label("destination.detail.discovery",
                    selected.knowledge.discovery.NullOrEmpty() ? WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Discovery.None") : selected.knowledge.discovery,
                    InsightUiTextStyle.Caption));
            }
            InsightUiButton choose = InsightUi.Button("destination.choose", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Action.ChooseDestination"),
                () => { onConfirm?.Invoke(selected); Close(false); });
            InsightUiButton world = InsightUi.Button("destination.world", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Action.WorldMap"), OpenWorldMap);
            return Card("destination.detail", InsightUi.SectionHeader("destination.detail.header",
                    known ? selected.biome?.LabelCap.ToString() : WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Destination.UnknownRegion")),
                InsightUi.Column("destination.detail.facts", facts.ToArray()).SetGap(6f),
                ButtonRow("destination.detail.actions", choose, world));
        }

        private void OpenWorldMap()
        {
            WildlifeWorldMapController.BeginDestinationSelection(component, returnWindow ?? this, destination =>
            {
                selected = destination;
                onConfirm?.Invoke(destination);
            });
            Close(false);
        }
    }

    public sealed class Window_ExpeditionMedicine : WildlifeInsightWindow
    {
        private readonly Map map;
        private readonly Dictionary<ThingDef, int> selected;
        private readonly List<ThingDef> defs;

        public Window_ExpeditionMedicine(Map map, Dictionary<ThingDef, int> selected)
            : base("wildlife-expedition-medicine", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Medicine.Title"),
                new Vector2(760f, 680f))
        {
            this.map = map;
            this.selected = selected ?? new Dictionary<ThingDef, int>();
            Dictionary<ThingDef, int> available = ExpeditionSupplyUtility.AvailableMedicines(map);
            defs = available.Keys.Concat(this.selected.Keys).Where(value => value != null).Distinct()
                .OrderBy(value => value.label).Take(64).ToList();
            InitializeDocument();
        }

        protected override InsightUiElement BuildRoot()
        {
            Dictionary<ThingDef, int> available = ExpeditionSupplyUtility.AvailableMedicines(map);
            InsightUiElement list = defs.Count == 0
                ? Empty("Wildlife.UI.Expedition.Medicine.Empty", "Wildlife.UI.Expedition.Medicine.Empty.Help")
                : InsightUi.VirtualList("medicine.items", defs.Count, 72f, index => MedicineRow(defs[index], available, index));
            return InsightUi.Scroll("medicine.scroll", InsightUi.Column("medicine.root",
                InsightUi.Breadcrumbs("medicine.breadcrumbs", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Breadcrumb"),
                    WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Supplies.Medicine")),
                InsightUi.SectionHeader("medicine.header", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Medicine.Title"),
                    WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Medicine.Summary", selected.Values.Sum(), available.Values.Sum())),
                list).SetGap(8f).SetPadding(8f));
        }

        private InsightUiElement MedicineRow(ThingDef def, Dictionary<ThingDef, int> available, int index)
        {
            int maximum = available.TryGetValue(def, out int value) ? value : 0;
            int chosen = selected.TryGetValue(def, out int selectedValue) ? Mathf.Min(selectedValue, maximum) : 0;
            return Card("medicine.row." + index,
                InsightUi.Label("medicine.row." + index + ".name", def.LabelCap + " • " +
                    WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Medicine.Available", maximum), InsightUiTextStyle.Heading),
                ButtonRow("medicine.row." + index + ".actions",
                    InsightUi.Button("medicine.row." + index + ".minus", "−", () => SetCount(def, chosen - 1)),
                    InsightUi.Badge("medicine.row." + index + ".count", chosen.ToString()),
                    InsightUi.Button("medicine.row." + index + ".plus", "+", () => SetCount(def, chosen + 1, maximum)),
                    InsightUi.Button("medicine.row." + index + ".max", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Action.Max"),
                        () => SetCount(def, maximum))));
        }

        private void SetCount(ThingDef def, int count, int maximum = int.MaxValue)
        {
            count = Mathf.Clamp(count, 0, maximum);
            if (count <= 0) selected.Remove(def); else selected[def] = count;
            RebuildDocument();
        }
    }

    public sealed class Window_ExpeditionProvisions : WildlifeInsightWindow
    {
        private readonly Map map;
        private readonly ExpeditionPlan plan;
        private readonly float estimatedDays;
        private readonly List<ThingDef> defs;

        public Window_ExpeditionProvisions(Map map, ExpeditionPlan plan, float estimatedDays)
            : base("wildlife-expedition-provisions", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Provisions.Title"),
                new Vector2(820f, 700f))
        {
            this.map = map;
            this.plan = plan;
            this.estimatedDays = estimatedDays;
            Dictionary<ThingDef, int> available = ExpeditionSupplyUtility.AvailableFoods(map);
            defs = available.Keys.Concat(plan?.provisions?.Keys ?? Enumerable.Empty<ThingDef>())
                .Where(value => value != null).Distinct().OrderBy(value => value.label).Take(64).ToList();
            InitializeDocument();
        }

        protected override InsightUiElement BuildRoot()
        {
            if (plan == null) return InsightUi.Column("provisions.unavailable", Empty("Wildlife.UI.Expedition.Provisions.Unavailable"));
            Dictionary<ThingDef, int> available = ExpeditionSupplyUtility.AvailableFoods(map);
            float required = ExpeditionSupplyUtility.RequiredNutrition(plan, estimatedDays);
            float chosen = ExpeditionSupplyUtility.SelectedNutrition(plan.provisions);
            InsightUiElement list = defs.Count == 0
                ? Empty("Wildlife.UI.Expedition.Provisions.Empty", "Wildlife.UI.Expedition.Provisions.Empty.Help")
                : InsightUi.VirtualList("provisions.items", defs.Count, 76f, index => ProvisionRow(defs[index], available, index));
            return InsightUi.Scroll("provisions.scroll", InsightUi.Column("provisions.root",
                InsightUi.Breadcrumbs("provisions.breadcrumbs", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Breadcrumb"),
                    WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Supplies.Provisions")),
                InsightUi.SectionHeader("provisions.header", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Provisions.Title"),
                    WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Provisions.Help")),
                InsightUi.Meter("provisions.meter", chosen, Mathf.Max(1f, required)),
                InsightUi.StatRow("provisions.total", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Supplies.Nutrition"),
                    WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Supplies.NutritionValue", WildlifeExpeditionText.Number(chosen), WildlifeExpeditionText.Number(required))),
                InsightUi.Button("provisions.auto", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Action.AutoFill"),
                    () => AutoFill(available, required)),
                list).SetGap(8f).SetPadding(8f));
        }

        private InsightUiElement ProvisionRow(ThingDef def, Dictionary<ThingDef, int> available, int index)
        {
            int maximum = available.TryGetValue(def, out int value) ? value : 0;
            int chosen = plan.provisions.TryGetValue(def, out int selected) ? Mathf.Min(selected, maximum) : 0;
            float each = ExpeditionSupplyUtility.NutritionPerUnit(def);
            return Card("provisions.row." + index,
                InsightUi.Label("provisions.row." + index + ".name", def.LabelCap + " • " +
                    WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Provisions.Available", maximum, WildlifeExpeditionText.Number(each)),
                    InsightUiTextStyle.Heading),
                ButtonRow("provisions.row." + index + ".actions",
                    InsightUi.Button("provisions.row." + index + ".minus", "−", () => SetCount(def, chosen - 1)),
                    InsightUi.Badge("provisions.row." + index + ".count", chosen.ToString()),
                    InsightUi.Button("provisions.row." + index + ".plus", "+", () => SetCount(def, chosen + 1, maximum)),
                    InsightUi.Button("provisions.row." + index + ".ten", "+10", () => SetCount(def, chosen + 10, maximum)),
                    InsightUi.Button("provisions.row." + index + ".max", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Action.Max"),
                        () => SetCount(def, maximum))));
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
            RebuildDocument();
        }

        private void SetCount(ThingDef def, int count, int maximum = int.MaxValue)
        {
            count = Mathf.Clamp(count, 0, maximum);
            if (count <= 0) plan.provisions.Remove(def); else plan.provisions[def] = count;
            RebuildDocument();
        }
    }

    public sealed class Window_WildlifeExpeditions : WildlifeInsightWindow
    {
        private readonly Map map;
        private string activeTab = "active";

        public Window_WildlifeExpeditions(Map map)
            : base("wildlife-expeditions", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.List.Title"),
                new Vector2(1040f, 760f))
        {
            this.map = map;
            Current.Game?.GetComponent<WildlifeExperienceGameComponent>()?.ShowExpeditionTutorial();
            InitializeDocument();
        }

        protected override InsightUiElement BuildRoot()
        {
            HuntingExpeditionMapComponent component = map?.GetComponent<HuntingExpeditionMapComponent>();
            if (component == null)
                return InsightUi.Column("list.unavailable", Empty("Wildlife.UI.Expedition.MapUnavailable"))
                    .SetGap(8f).SetPadding(10f);
            InsightUiButton send = InsightUi.Button("list.send", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Action.Send"),
                () =>
                {
                    Close(false);
                    WildlifeWorldMapController.BeginNewExpeditionSelection(component, this);
                });
            InsightUiTabs tabs = InsightUi.Tabs("list.tabs").Bind(() => activeTab, value =>
            {
                activeTab = value;
                RebuildDocument();
            });
            tabs.Add("active", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Tab.Active"), BuildActive(component));
            tabs.Add("history", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Tab.History"), BuildHistory(component));
            return InsightUi.Scroll("list.scroll", InsightUi.Column("list.root",
                InsightUi.Breadcrumbs("list.breadcrumbs", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Breadcrumb")),
                InsightUi.SectionHeader("list.header", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.List.Title"),
                    WildlifeExpeditionText.Get("Wildlife.UI.Expedition.List.Subtitle"), null, send),
                tabs).SetGap(8f).SetPadding(8f));
        }

        private InsightUiElement BuildActive(HuntingExpeditionMapComponent component)
        {
            List<HuntingExpeditionRecord> records = component.ActiveExpeditions
                .Where(record => record != null).OrderByDescending(record => Severity(component.Warning(record)))
                .ThenBy(record => record.expectedReturnTick).Take(64).ToList();
            if (records.Count == 0) return Empty("Wildlife.UI.Expedition.Active.Empty", "Wildlife.UI.Expedition.Active.Empty.Help");
            return InsightUi.VirtualList("list.active.items", records.Count, 190f,
                index => ActiveCard(component, records[index], index));
        }

        private InsightUiElement ActiveCard(HuntingExpeditionMapComponent component,
            HuntingExpeditionRecord record, int index)
        {
            string warning = component.Warning(record);
            List<InsightUiElement> actions = new List<InsightUiElement>
            {
                InsightUi.Button("list.active." + index + ".details", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Action.Details"),
                    () => Find.WindowStack.Add(new Window_HuntingExpeditionStatus(component, record))),
                InsightUi.Button("list.active." + index + ".route", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Action.Route"),
                    () => Find.WindowStack.Add(new Window_ExpeditionRoute(component, record)))
            };
            bool canView = record.caravan != null || record.marker != null;
            InsightUiButton world = InsightUi.Button("list.active." + index + ".world", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Action.WorldMap"),
                () => CameraJumper.TryJump((GlobalTargetInfo)(record.caravan ?? (WorldObject)record.marker)));
            world.Enabled = canView;
            actions.Add(world);
            if (record.needsRescue)
            {
                bool rescueActive = record.rescuers.Any(pawn => pawn != null && !pawn.Dead);
                InsightUiButton rescue = InsightUi.Button("list.active." + index + ".rescue",
                    rescueActive ? WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Action.RescueEnRoute") :
                        WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Action.SendRescue"),
                    () => ShowRescueMenu(component, record));
                rescue.Enabled = !rescueActive;
                actions.Add(rescue);
            }
            actions.Add(InsightUi.Button("list.active." + index + ".recall", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Action.Recall"),
                () => Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Recall.Confirm", record.id),
                    () => component.Cancel(record)))));
            return Card("list.active.card." + index,
                InsightUi.SectionHeader("list.active." + index + ".header",
                    WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Card.Title", record.id, WildlifeExpeditionText.Stage(record.stage)),
                    WildlifeExpeditionText.Objective(record.objective) + " • " + WildlifeExpeditionText.CurrentTarget(record)),
                Summary("list.active." + index + ".facts",
                    InsightUi.StatRow("list.active." + index + ".destination", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Summary.Destination"),
                        WildlifeExpeditionText.DestinationBiome(record)),
                    InsightUi.StatRow("list.active." + index + ".party", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Summary.Party"),
                        WildlifeExpeditionText.Party(record)),
                    InsightUi.StatRow("list.active." + index + ".status", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Summary.Status"),
                        component.Status(record))),
                InsightUi.Progress("list.active." + index + ".progress", component.Progress(record)),
                string.IsNullOrEmpty(warning) ? InsightUi.Empty("list.active." + index + ".warning") :
                    InsightUi.Callout("list.active." + index + ".warning", InsightUiCalloutSeverity.Warning,
                        WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Warning"), warning),
                ButtonRow("list.active." + index + ".actions", actions.ToArray()));
        }

        private InsightUiElement BuildHistory(HuntingExpeditionMapComponent component)
        {
            List<string> history = (component.History ?? new List<string>()).Where(value => !string.IsNullOrEmpty(value))
                .Take(64).ToList();
            if (history.Count == 0) return Empty("Wildlife.UI.Expedition.History.Empty", "Wildlife.UI.Expedition.History.Empty.Help");
            return InsightUi.VirtualList("list.history.items", history.Count, 58f,
                index => Card("list.history." + index, InsightUi.Label("list.history." + index + ".text", history[index])));
        }

        private void ShowRescueMenu(HuntingExpeditionMapComponent component, HuntingExpeditionRecord record)
        {
            List<Pawn> pawns = map.mapPawns.FreeColonistsSpawned.Where(pawn => pawn?.Downed == false &&
                !component.PawnOnExpedition(pawn)).OrderBy(pawn => pawn.LabelShortCap.ToString()).Take(32).ToList();
            if (pawns.Count == 0)
            {
                Messages.Message(WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Rescue.None"), MessageTypeDefOf.RejectInput, false);
                return;
            }
            Find.WindowStack.Add(new FloatMenu(pawns.Select(pawn => new FloatMenuOption(pawn.LabelShortCap,
                () => component.BeginRescue(record, pawn))).ToList()));
        }

        private static int Severity(string warning) => string.IsNullOrEmpty(warning) ? 0 :
            warning.IndexOf("Stranded", StringComparison.OrdinalIgnoreCase) >= 0 ? 4 :
            warning.IndexOf("Overdue", StringComparison.OrdinalIgnoreCase) >= 0 ? 3 :
            warning.IndexOf("Injured", StringComparison.OrdinalIgnoreCase) >= 0 ? 2 : 1;
    }

    public sealed class Window_HuntingExpeditionStatus : WildlifeInsightWindow
    {
        private readonly HuntingExpeditionMapComponent component;
        private readonly HuntingExpeditionRecord record;

        public Window_HuntingExpeditionStatus(HuntingExpeditionMapComponent component, HuntingExpeditionRecord record)
            : base("wildlife-expedition-status", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Status.Title"),
                new Vector2(820f, 700f))
        {
            this.component = component;
            this.record = record;
            InitializeDocument();
        }

        protected override InsightUiElement BuildRoot()
        {
            if (record == null || component == null || component.FindRecord(record.id) != record)
                return InsightUi.Column("status.unavailable", Empty("Wildlife.UI.Expedition.Status.Unavailable"))
                    .SetGap(8f).SetPadding(10f);
            List<string> log = (record.log ?? new List<string>()).Take(128).ToList();
            InsightUiElement fieldLog = log.Count == 0 ? Empty("Wildlife.UI.Expedition.Status.LogEmpty") :
                InsightUi.VirtualList("status.log.items", log.Count, 54f,
                    index => Card("status.log." + index, InsightUi.Label("status.log." + index + ".text", log[index])));
            InsightUiButton recall = InsightUi.Button("status.recall", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Action.Recall"),
                () => { component.Cancel(record); Close(false); });
            return InsightUi.Scroll("status.scroll", InsightUi.Column("status.root",
                InsightUi.Breadcrumbs("status.breadcrumbs", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Breadcrumb")),
                InsightUi.SectionHeader("status.header", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Status.Title"), component.Status(record)),
                InsightUi.Progress("status.progress", component.Progress(record)),
                Summary("status.summary",
                    InsightUi.StatRow("status.objective", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Summary.Objective"),
                        WildlifeExpeditionText.Objective(record.objective)),
                    InsightUi.StatRow("status.target", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Summary.Target"), WildlifeExpeditionText.CurrentTarget(record)),
                    InsightUi.StatRow("status.party", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Summary.Party"), WildlifeExpeditionText.Party(record)),
                    InsightUi.StatRow("status.supplies", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Supplies.Title"),
                        WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Supplies.Record", record.medicine, record.bedrolls))),
                string.IsNullOrEmpty(component.Warning(record)) ? InsightUi.Empty("status.warning") :
                    InsightUi.Callout("status.warning", InsightUiCalloutSeverity.Warning,
                        WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Warning"), component.Warning(record)),
                InsightUi.SectionHeader("status.log.header", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Status.Log")),
                fieldLog,
                record.needsRescue ? InsightUi.Callout("status.rescue", InsightUiCalloutSeverity.Warning,
                    WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Status.Stranded"),
                    WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Status.RescueHelp")) : InsightUi.Empty("status.rescue.empty"),
                ButtonRow("status.actions", InsightUi.Button("status.outcome", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Action.Outcome"),
                    () => Find.WindowStack.Add(new Window_ExpeditionAssessment(
                        WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Assessment.Outcome"), component.RecordDetails(record)))), recall)).SetGap(8f).SetPadding(8f));
        }
    }

    public sealed class Window_ExpeditionAssessment : WildlifeInsightWindow
    {
        private readonly string title;
        private readonly string body;

        public Window_ExpeditionAssessment(string title, string text)
            : base("wildlife-expedition-assessment", title ?? WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Assessment.Title"),
                new Vector2(760f, 600f))
        {
            this.title = title ?? WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Assessment.Title");
            body = text ?? WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Assessment.Empty");
            InitializeDocument();
        }

        protected override InsightUiElement BuildRoot() => InsightUi.Scroll("assessment.scroll",
            InsightUi.Column("assessment.root",
                InsightUi.Breadcrumbs("assessment.breadcrumbs", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Breadcrumb")),
                InsightUi.SectionHeader("assessment.header", title),
                Card("assessment.body", InsightUi.Label("assessment.body.text", body))).SetGap(8f).SetPadding(8f));
    }

    public sealed class Window_ExpeditionRoute : WildlifeInsightWindow
    {
        private readonly HuntingExpeditionMapComponent component;
        private readonly HuntingExpeditionRecord record;
        private readonly List<int> route;

        public Window_ExpeditionRoute(HuntingExpeditionMapComponent component, HuntingExpeditionRecord record)
            : base("wildlife-expedition-route", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Route.Title"),
                new Vector2(980f, 560f))
        {
            this.component = component;
            this.record = record;
            route = (record?.routeTiles ?? new List<int>()).Take(96).ToList();
            InitializeDocument();
        }

        protected override InsightUiElement BuildRoot()
        {
            if (component == null || record == null)
                return InsightUi.Column("route.unavailable", Empty("Wildlife.UI.Expedition.Status.Unavailable"))
                    .SetGap(8f).SetPadding(10f);
            if (route.Count == 0)
                return InsightUi.Column("route.empty", InsightUi.SectionHeader("route.header",
                    WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Route.Title"), component.Status(record)),
                    Empty("Wildlife.UI.Expedition.Route.Empty", "Wildlife.UI.Expedition.Route.Empty.Help"))
                    .SetGap(8f).SetPadding(8f);
            int current = record.caravan != null && !record.caravan.Destroyed ? (int)record.caravan.Tile :
                record.marker == null ? route[0] : (int)record.marker.Tile;
            return InsightUi.Scroll("route.scroll", InsightUi.Column("route.root",
                InsightUi.Breadcrumbs("route.breadcrumbs", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Breadcrumb")),
                InsightUi.SectionHeader("route.header", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Route.Title"), component.Status(record)),
                InsightUi.VirtualList("route.items", route.Count, 112f, index => RouteRow(route[index], index, current)),
                InsightUi.Callout("route.help", InsightUiCalloutSeverity.Info,
                    WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Route.Legend"),
                    WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Route.Legend.Help"))).SetGap(8f).SetPadding(8f));
        }

        private InsightUiElement RouteRow(int tileId, int index, int current)
        {
            Tile tile = Find.WorldGrid == null ? null : Find.WorldGrid[(PlanetTile)tileId];
            float movement = Mathf.Max(0.65f, tile?.PrimaryBiome?.movementDifficulty ?? 1f);
            float danger = Mathf.Clamp01(0.08f + index * 0.045f + Mathf.InverseLerp(1f, 4f, movement) * 0.18f +
                Mathf.Abs((tile?.temperature ?? 18f) - 18f) / 120f);
            string heading = index == 0 ? WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Route.Colony") :
                index == route.Count - 1 ? WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Route.Destination") :
                WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Route.Travel");
            string biome = tile?.PrimaryBiome?.LabelCap.ToString() ?? WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Value.Unknown");
            return Card("route.row." + index,
                InsightUi.SectionHeader("route.row." + index + ".header", heading,
                    biome + (tileId == current ? " • " + WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Route.Current") : string.Empty)),
                InsightUi.Meter("route.row." + index + ".danger", danger, 1f),
                InsightUi.StatRow("route.row." + index + ".risk", WildlifeExpeditionText.Get("Wildlife.UI.Expedition.Destination.Risk"), danger.ToStringPercent()));
        }
    }
}
