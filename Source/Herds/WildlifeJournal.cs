using System;
using System.Collections.Generic;
using System.Linq;
using KnowledgeFramework;
using RimWorld;
using UnityEngine;
using Verse;

namespace Herds
{
    public enum WildlifeJournalPage
    {
        FieldGuide = 0,
        LivingAtlas = 1,
        Signals = 2,
        Investigations = 3,
        Expeditions = 4,
        Stories = 5,
        FieldLog = 6,
        Knowledge = 7,
        Region = 8,
        Chronicle = 9
    }

    /// <summary>One responsive presentation surface for Wildlife's evidence loop.</summary>
    public sealed class Window_WildlifeJournal : Window
    {
        private readonly Map map;
        private WildlifeJournalPage page;
        private ThingDef selectedSpecies;
        private Vector2 leftScroll;
        private Vector2 bodyScroll;
        private string search = string.Empty;
        private int cachedTick = -1;
        private int cachedKnowledgeRevision = -1;
        private WildlifeEcologySnapshot cachedSnapshot;
        private List<ThingDef> cachedSpecies = new List<ThingDef>();
        private List<AtlasActivity> atlasActivities = new List<AtlasActivity>();
        private List<FieldLogItem> cachedFieldLog = new List<FieldLogItem>();
        private Pawn signalObserver;
        private Vector2? initialSignalSpeciesScroll;
        private Vector2? initialSignalDetailScroll;
        private readonly int focusedFieldLogTick;
        private WildlifeSignalJournalPanel signalPanel;
        private readonly Dictionary<string, IReadOnlyList<KnowledgeFacetSnapshotV2>> facetCache =
            new Dictionary<string, IReadOnlyList<KnowledgeFacetSnapshotV2>>(StringComparer.Ordinal);

        private enum AtlasActivityKind
        {
            Presence,
            Trend,
            Trail,
            Migration,
            Signal
        }

        private sealed class AtlasActivity
        {
            public AtlasActivityKind kind;
            public ThingDef species;
            public IntVec3 cell;
            public string title;
            public string detail;
            public float confidence;
            public int tick;
        }

        private enum FieldLogItemKind
        {
            Moment,
            Signal,
            Trail,
            Investigation,
            Expedition,
            Outcome
        }

        private sealed class FieldLogItem
        {
            public FieldLogItemKind kind;
            public string key;
            public string title;
            public string detail;
            public string meaning;
            public string location;
            public string certainty;
            public int tick;
            public ThingDef species;
            public bool urgent;
            public WildlifeTrailLead trail;
            public WildlifeExperienceEvent outcome;
        }

        public override Vector2 InitialSize => new Vector2(Mathf.Min(1180f, UI.screenWidth * 0.94f), Mathf.Min(780f, UI.screenHeight * 0.90f));

        public Window_WildlifeJournal(Map map, WildlifeJournalPage page = WildlifeJournalPage.FieldLog,
            ThingDef selectedSpecies = null, Pawn signalObserver = null,
            Vector2? signalSpeciesScroll = null, Vector2? signalDetailScroll = null,
            int focusedFieldLogTick = -1)
        {
            this.map = map ?? Find.CurrentMap;
            this.page = page;
            this.selectedSpecies = selectedSpecies;
            this.signalObserver = signalObserver;
            initialSignalSpeciesScroll = signalSpeciesScroll;
            initialSignalDetailScroll = signalDetailScroll;
            this.focusedFieldLogTick = focusedFieldLogTick;
            doCloseX = true;
            absorbInputAroundWindow = true;
            resizeable = true;
        }

        public static void OpenSignals(Map map, Pawn observer = null, ThingDef species = null,
            Vector2? speciesScroll = null, Vector2? detailScroll = null)
        {
            WildlifeJournalPage targetPage = SignalsVisible()
                ? WildlifeJournalPage.Signals : WildlifeJournalPage.FieldLog;
            Find.WindowStack.Add(new Window_WildlifeJournal(map, targetPage, species, observer,
                speciesScroll, detailScroll));
        }

        public static void OpenFieldLog(Map map, int focusedTick = -1)
        {
            Find.WindowStack.Add(new Window_WildlifeJournal(map, WildlifeJournalPage.FieldLog,
                null, null, null, null, focusedTick));
        }

        public override void DoWindowContents(Rect inRect)
        {
            Map activeMap = map ?? Find.CurrentMap;
            RefreshModel(activeMap);
            if (page == WildlifeJournalPage.Expeditions && !ExpeditionsVisible()) page = WildlifeJournalPage.FieldLog;
            if (page == WildlifeJournalPage.Signals && !SignalsVisible())
                page = WildlifeJournalPage.FieldLog;
            Text.Font = GameFont.Medium;
            float freshnessWidth = Mathf.Clamp(inRect.width * 0.30f, 220f, 300f);
            Widgets.Label(new Rect(inRect.x, inRect.y, Mathf.Max(160f, inRect.width - freshnessWidth - 18f), 34f), "Wildlife Journal");
            Text.Font = GameFont.Small;
            GUI.color = new Color(0.66f, 0.76f, 0.68f);
            Widgets.Label(new Rect(inRect.x, inRect.y + 30f, inRect.width - 220f, 24f),
                "Notice  >  gather evidence  >  form a hypothesis  >  investigate  >  decide  >  preserve the story");
            GUI.color = Color.white;
            DrawFreshnessMark(new Rect(inRect.xMax - freshnessWidth, inRect.y + 3f, freshnessWidth, 24f));

            Rect tabs = new Rect(inRect.x, inRect.y + 57f, inRect.width, 34f);
            DrawTabs(tabs);
            float bodyY = tabs.yMax + 8f;
            if (!IsHubPage(page))
            {
                DrawDetailNavigation(new Rect(inRect.x, bodyY, inRect.width, 28f));
                bodyY += 36f;
            }
            Rect body = new Rect(inRect.x, bodyY, inRect.width,
                Mathf.Max(1f, inRect.yMax - bodyY));
            if (activeMap == null)
            {
                Widgets.Label(body, "No active map. Wildlife evidence will appear here when a map is available.");
                return;
            }
            switch (page)
            {
                case WildlifeJournalPage.FieldLog: DrawFieldLog(body, activeMap); break;
                case WildlifeJournalPage.Knowledge: DrawKnowledgeHub(body, activeMap); break;
                case WildlifeJournalPage.Region: DrawRegionHub(body, activeMap); break;
                case WildlifeJournalPage.Chronicle: DrawChronicleHub(body, activeMap); break;
                case WildlifeJournalPage.LivingAtlas: DrawLivingAtlas(body, activeMap); break;
                case WildlifeJournalPage.Signals: DrawSignals(body, activeMap); break;
                case WildlifeJournalPage.Investigations: DrawInvestigations(body, activeMap); break;
                case WildlifeJournalPage.Expeditions: DrawExpeditions(body, activeMap); break;
                case WildlifeJournalPage.Stories: DrawStories(body, activeMap); break;
                default: DrawFieldGuide(body, activeMap); break;
            }
        }

        private void RefreshModel(Map activeMap)
        {
            int now = Find.TickManager?.TicksGame ?? 0;
            int knowledgeRevision = KnowledgeQuery.Revision;
            if (cachedSnapshot != null && cachedSnapshot.map == activeMap && cachedTick == now / 30 &&
                cachedKnowledgeRevision == knowledgeRevision) return;
            cachedSnapshot = WildlifeEcologySnapshots.For(activeMap);
            IReadOnlyList<WildlifeEvent> history = WildlifeEventRouter.Shared.History;
            HashSet<ThingDef> mapSpecies = new HashSet<ThingDef>(activeMap?.mapPawns?.AllPawnsSpawned
                .Where(pawn => pawn?.RaceProps?.Animal == true).Select(pawn => pawn.def) ?? Enumerable.Empty<ThingDef>());
            if (cachedSnapshot != null)
            {
                foreach (WildlifeMigrationSnapshot migration in cachedSnapshot.migrations)
                    if (migration?.species != null) mapSpecies.Add(migration.species);
                foreach (WildlifeTrailSnapshot trail in cachedSnapshot.trails)
                    if (trail?.species != null) mapSpecies.Add(trail.species);
            }
            foreach (WildlifeEvent value in history.Where(value => value?.map == activeMap && value.species != null))
                mapSpecies.Add(value.species);
            HashSet<ThingDef> seenSpecies = new HashSet<ThingDef>(history
                .Where(value => value?.map == activeMap && value.species != null &&
                    value.observer?.Faction == Faction.OfPlayer && value.observer.RaceProps?.Humanlike == true)
                .Select(value => value.species));
            foreach (Pawn colonist in activeMap?.mapPawns?.FreeColonistsSpawned ?? Enumerable.Empty<Pawn>())
                foreach (ThingDef species in mapSpecies)
                    if (WildlifeKnowledgeAdapter.PersonalKnowledge(colonist, species) > 0f) seenSpecies.Add(species);
            // TaggedString is intentionally not comparable in RimWorld's runtime. Use stable
            // strings for deterministic ordering instead of passing the wrapper to LINQ.
            cachedSpecies = OrderByKnowledge(cachedSnapshot?.species.Where(value => value?.species != null &&
                mapSpecies.Contains(value.species) && seenSpecies.Contains(value.species)) ?? Enumerable.Empty<WildlifeSpeciesSnapshot>())
                .Select(value => value.species)
                .ToList() ?? new List<ThingDef>();
            atlasActivities = BuildAtlasActivities(activeMap, cachedSnapshot, now);
            cachedFieldLog = BuildFieldLog(activeMap, now);
            cachedTick = now / 30;
            cachedKnowledgeRevision = knowledgeRevision;
            facetCache.Clear();
            if (page != WildlifeJournalPage.Signals &&
                (selectedSpecies == null || !cachedSpecies.Contains(selectedSpecies)))
                selectedSpecies = cachedSpecies.FirstOrDefault();
        }

        private void DrawTabs(Rect rect)
        {
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.MiddleCenter;
            List<string> labels = new List<string> { "Field Log", "Knowledge", "Region", "Chronicle" };
            List<WildlifeJournalPage> pages = new List<WildlifeJournalPage>
            {
                WildlifeJournalPage.FieldLog, WildlifeJournalPage.Knowledge,
                WildlifeJournalPage.Region, WildlifeJournalPage.Chronicle
            };
            Widgets.DrawBoxSolid(rect, new Color(0.11f, 0.13f, 0.13f, 1f));
            float width = rect.width / labels.Count;
            for (int i = 0; i < labels.Count; i++)
            {
                Rect tab = new Rect(rect.x + i * width, rect.y, width, rect.height);
                if (page == pages[i] || HubForPage(page) == pages[i])
                    Widgets.DrawBoxSolid(tab.ContractedBy(1f), new Color(0.28f, 0.43f, 0.33f, 1f));
                else Widgets.DrawHighlightIfMouseover(tab.ContractedBy(1f));
                if (Widgets.ButtonInvisible(tab))
                {
                    page = pages[i];
                    if (page != WildlifeJournalPage.Signals && pages[i] != WildlifeJournalPage.Signals)
                    {
                        bodyScroll = Vector2.zero;
                        leftScroll = Vector2.zero;
                    }
                }
                Widgets.Label(tab.ContractedBy(4f), labels[i]);
            }
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private void DrawDetailNavigation(Rect rect)
        {
            WildlifeJournalPage hub = HubForPage(page);
            Widgets.DrawMenuSection(rect);
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.68f, 0.78f, 0.70f);
            Widgets.Label(new Rect(rect.x + 9f, rect.y + 6f, rect.width - 190f, 18f),
                HubLabel(hub) + " / " + DetailLabel(page));
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            if (Widgets.ButtonText(new Rect(rect.xMax - 178f, rect.y + 2f, 168f, 24f),
                "Back to " + HubLabel(hub)))
            {
                page = hub;
                bodyScroll = Vector2.zero;
                leftScroll = Vector2.zero;
            }
        }

        private static bool IsHubPage(WildlifeJournalPage value) =>
            value == WildlifeJournalPage.FieldLog || value == WildlifeJournalPage.Knowledge ||
            value == WildlifeJournalPage.Region || value == WildlifeJournalPage.Chronicle;

        private static WildlifeJournalPage HubForPage(WildlifeJournalPage value)
        {
            switch (value)
            {
                case WildlifeJournalPage.FieldGuide:
                case WildlifeJournalPage.Signals:
                    return WildlifeJournalPage.Knowledge;
                case WildlifeJournalPage.LivingAtlas:
                case WildlifeJournalPage.Expeditions:
                    return WildlifeJournalPage.Region;
                case WildlifeJournalPage.Investigations:
                case WildlifeJournalPage.Stories:
                    return WildlifeJournalPage.Chronicle;
                default:
                    return value == WildlifeJournalPage.Knowledge || value == WildlifeJournalPage.Region ||
                        value == WildlifeJournalPage.Chronicle ? value : WildlifeJournalPage.FieldLog;
            }
        }

        private static string HubLabel(WildlifeJournalPage value) => value == WildlifeJournalPage.Knowledge
            ? "Knowledge" : value == WildlifeJournalPage.Region ? "Region" :
            value == WildlifeJournalPage.Chronicle ? "Chronicle" : "Field Log";

        private static string DetailLabel(WildlifeJournalPage value) => value == WildlifeJournalPage.FieldGuide
            ? "Field Guide" : value == WildlifeJournalPage.LivingAtlas ? "Living Atlas" :
            value == WildlifeJournalPage.Signals ? "Signals" : value == WildlifeJournalPage.Investigations
                ? "Investigations" : value == WildlifeJournalPage.Expeditions ? "Expeditions" :
                value == WildlifeJournalPage.Stories ? "Stories" : "Detail";

        private void DrawFreshnessMark(Rect rect)
        {
            float pulse = 0.50f + Mathf.Sin(Time.realtimeSinceStartup * 2.2f) * 0.08f;
            GUI.color = new Color(0.45f, 0.80f, 0.58f, pulse);
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y + 8f, 8f, 8f), GUI.color);
            GUI.color = new Color(0.68f, 0.76f, 0.69f);
            Widgets.Label(new Rect(rect.x + 14f, rect.y, rect.width - 14f, rect.height),
                cachedSnapshot == null ? "Waiting for field evidence" : "Live evidence view");
            GUI.color = Color.white;
        }

        public static bool SignalsVisible() => HerdsMod.Settings?.enableWildlifeSignalCulture == true;

        internal static IReadOnlyList<WildlifeJournalPage> TopLevelPagesForTesting() =>
            new[]
            {
                WildlifeJournalPage.FieldLog,
                WildlifeJournalPage.Knowledge,
                WildlifeJournalPage.Region,
                WildlifeJournalPage.Chronicle
            };

        public static bool ExpeditionsVisible() => HerdsMod.Settings?.enableOffMapHuntingExpeditions == true &&
            WildlifeProgression.Unlocked(WildlifeCapability.HuntingExpedition);

        private void DrawSignals(Rect rect, Map activeMap)
        {
            if (!SignalsVisible())
            {
                page = WildlifeJournalPage.FieldLog;
                Widgets.Label(rect, "Signal culture is disabled in Wildlife settings.");
                return;
            }
            if (signalPanel == null)
            {
                signalPanel = new WildlifeSignalJournalPanel(activeMap, signalObserver, selectedSpecies,
                    initialSignalSpeciesScroll, initialSignalDetailScroll);
                initialSignalSpeciesScroll = null;
                initialSignalDetailScroll = null;
            }
            signalPanel.DrawJournalPanel(rect);
            selectedSpecies = signalPanel.SelectedSpecies;
            signalObserver = signalPanel.Observer;
        }

        private void DrawFieldLog(Rect rect, Map activeMap)
        {
            const float headerHeight = 78f;
            const float rowHeight = 88f;
            List<FieldLogItem> attention = cachedFieldLog.Where(item => item.urgent).Take(6).ToList();
            List<FieldLogItem> recent = cachedFieldLog.Where(item => !item.urgent).Take(20).ToList();
            if (attention.Count == 0 && recent.Count == 0)
            {
                Widgets.DrawMenuSection(new Rect(rect.x, rect.y, rect.width, headerHeight + 132f));
                Text.Font = GameFont.Medium;
                Widgets.Label(new Rect(rect.x + 12f, rect.y + 12f, rect.width - 24f, 28f), "Field Log");
                Text.Font = GameFont.Small;
                Widgets.Label(new Rect(rect.x + 12f, rect.y + 48f, rect.width - 24f, 74f),
                    "The Field Log is empty. Records will appear after the colony sees wildlife, hears a call, follows a sign, responds to a moment, or receives a field report.\n\n" +
                    "Keep a colonist near wildlife, study a sign, or survey the region to begin reading the living world.");
                return;
            }

            float contentHeight = headerHeight +
                (attention.Count > 0 ? 38f + attention.Count * rowHeight : 0f) +
                (recent.Count > 0 ? 48f + recent.Count * rowHeight : 0f);
            Rect outer = new Rect(rect.x, rect.y, rect.width, rect.height);
            Rect view = new Rect(0f, 0f, Mathf.Max(1f, outer.width - 16f),
                Mathf.Max(outer.height, contentHeight));
            Widgets.BeginScrollView(outer, ref bodyScroll, view);
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, view.width, 28f), "Field Log");
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(0f, 32f, view.width, 38f),
                "Recent observations, interpretations, and decisions. Each note points back to the existing record or action that produced it.");
            float y = headerHeight;
            if (attention.Count > 0)
            {
                Text.Font = GameFont.Medium;
                Widgets.Label(new Rect(0f, y, view.width, 28f), "Needs attention");
                Text.Font = GameFont.Small;
                y += 38f;
                for (int i = 0; i < attention.Count; i++)
                {
                    DrawFieldLogRow(new Rect(0f, y, view.width, rowHeight - 6f), attention[i], activeMap);
                    y += rowHeight;
                }
            }
            if (recent.Count > 0)
            {
                Text.Font = GameFont.Medium;
                Widgets.Label(new Rect(0f, y, view.width, 28f), "Recent notes");
                Text.Font = GameFont.Small;
                y += 42f;
                for (int i = 0; i < recent.Count; i++)
                {
                    DrawFieldLogRow(new Rect(0f, y, view.width, rowHeight - 6f), recent[i], activeMap);
                    y += rowHeight;
                }
            }
            Widgets.EndScrollView();
        }

        private void DrawFieldLogRow(Rect rect, FieldLogItem item, Map activeMap)
        {
            bool focused = focusedFieldLogTick >= 0 && item.tick == focusedFieldLogTick;
            Widgets.DrawMenuSection(rect);
            if (focused) Widgets.DrawBoxSolid(rect, new Color(0.22f, 0.36f, 0.23f, 0.9f));
            Color accent = item.urgent ? new Color(0.88f, 0.34f, 0.25f) : item.kind == FieldLogItemKind.Signal
                ? new Color(0.45f, 0.38f, 0.72f) : item.kind == FieldLogItemKind.Trail
                    ? new Color(0.35f, 0.68f, 0.78f) : new Color(0.42f, 0.68f, 0.47f);
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, 5f, rect.height), accent);
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.68f, 0.78f, 0.70f);
            Widgets.Label(new Rect(rect.x + 14f, rect.y + 7f, rect.width * 0.28f, 18f),
                FieldLogKindLabel(item.kind));
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            float buttonWidth = FieldLogHasAction(item) ? 104f : 0f;
            float textWidth = Mathf.Max(100f, rect.width - 30f - buttonWidth);
            Widgets.Label(new Rect(rect.x + 14f, rect.y + 25f, textWidth, 22f), item.title);
            Widgets.Label(new Rect(rect.x + 14f, rect.y + 48f, textWidth, 30f),
                (item.detail ?? "A field record was preserved.") + "\n" +
                (item.meaning ?? "The colony is still interpreting this record."));
            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = new Color(0.62f, 0.73f, 0.66f);
            string time = item.tick <= 0 ? "Recently" :
                Mathf.Max(0, (Find.TickManager?.TicksGame ?? item.tick) - item.tick).ToStringTicksToPeriod() + " ago";
            Widgets.Label(new Rect(rect.x + 14f, rect.y + 70f, textWidth - 4f, 16f),
                (item.location ?? "Field record") + "  |  " + item.certainty + "  |  " +
                FreshnessLabel(item.tick) + "  |  " + time);
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            if (FieldLogHasAction(item) && Widgets.ButtonText(
                new Rect(rect.xMax - buttonWidth - 10f, rect.y + 27f, buttonWidth, 30f), FieldLogActionLabel(item)))
                OpenFieldLogItem(item, activeMap);
            TooltipHandler.TipRegion(rect, item.detail + "\n" + item.meaning + "\n" +
                (item.location ?? "Field record") + "\n" + item.certainty + ".");
        }

        private void DrawKnowledgeHub(Rect rect, Map activeMap)
        {
            List<WildlifeSpeciesSnapshot> records = cachedSnapshot?.species
                ?.Where(value => value?.species != null && cachedSpecies.Contains(value.species))
                .OrderByDescending(value => WildlifeKnowledgeAdapter.StageOrder(value.stageId))
                .ThenBy(value => value.species.LabelCap.ToString(), StringComparer.OrdinalIgnoreCase)
                .Take(8).ToList() ?? new List<WildlifeSpeciesSnapshot>();
            float contentHeight = 74f + 84f + 82f + 42f + Mathf.Max(104f, records.Count * 74f);
            Rect outer = new Rect(rect.x, rect.y, rect.width, rect.height);
            Rect view = new Rect(0f, 0f, Mathf.Max(1f, outer.width - 16f),
                Mathf.Max(outer.height, contentHeight));
            Widgets.BeginScrollView(outer, ref bodyScroll, view);
            DrawHubHeader(new Rect(0f, 0f, view.width, 74f), "Knowledge",
                "What the colony understands: species, behavior, interpreted signals, and the evidence behind each working conclusion.");
            bool hasSignals = SignalsVisible();
            WildlifeSignalCultureMapComponent signalCulture = activeMap?.GetComponent<WildlifeSignalCultureMapComponent>();
            bool hasWarningTraces = signalCulture != null && records.Any(value =>
                signalCulture.HasWarningSignals(value.species));
            WildlifeWarningKnowledgeState warningKnowledge = records.Select(value =>
                signalCulture?.ColonyWarningKnowledge(value.species)).FirstOrDefault(value => value?.hasEvidence == true);
            bool hasEvidence = records.Any(value => value.evidence?.Count > 0 || value.signals?.Count > 0 ||
                value.trails?.Count > 0 || value.migrations?.Count > 0);
            string recordState = records.Count == 0 ? "No field record yet" : records.Any(value =>
                WildlifeKnowledgeAdapter.StageOrder(value.stageId) >= 5) ? "Some records are documented" : "Records are growing";
            string behaviorState = records.Any(value => WildlifeKnowledgeAdapter.StageOrder(value.stageId) >= 3)
                ? "Behavior patterns are emerging" : records.Count == 0 ? "No behavior pattern yet" : "Behavior remains a working hypothesis";
            string signalState = !hasSignals ? "Signal culture is unavailable" :
                warningKnowledge != null ? warningKnowledge.PlayerLabel :
                hasWarningTraces ? "Warning calls are being compared" :
                cachedSnapshot?.signals?.Count > 0 ? "Recent interpretations are available" : "No interpreted calls yet";
            float statusWidth = Mathf.Max(80f, (view.width - 16f) / 3f);
            DrawHubStatus(new Rect(0f, 82f, statusWidth, 72f), "Species records", recordState,
                new Color(0.34f, 0.64f, 0.48f),
                "The Knowledge page only lists species the colony has encountered or learned about.");
            DrawHubStatus(new Rect(statusWidth + 8f, 82f, statusWidth, 72f), "Learned behavior", behaviorState,
                new Color(0.43f, 0.57f, 0.76f),
                "Repeated observations turn unfamiliar behavior into a usable pattern.");
            DrawHubStatus(new Rect((statusWidth + 8f) * 2f, 82f, statusWidth, 72f), "Interpreted signals", signalState,
                new Color(0.60f, 0.43f, 0.72f),
                "Signal detail remains available without treating internal thresholds as player knowledge.");

            float actionY = 164f;
            float actionWidth = Mathf.Max(120f, (view.width - 16f) / 3f);
            DrawHubAction(new Rect(0f, actionY, actionWidth, 72f), "Field Guide",
                "Read species records, provenance, and the next useful observation.", "Open Field Guide", true,
                () =>
                {
                    page = WildlifeJournalPage.FieldGuide;
                    bodyScroll = Vector2.zero;
                });
            DrawHubAction(new Rect(actionWidth + 8f, actionY, actionWidth, 72f), "Signals",
                hasSignals ? "Review interpreted calls and compare observers." : "Enable signal culture to study animal calls.",
                hasSignals ? "Review Signals" : "Unavailable", hasSignals,
                () =>
                {
                    page = WildlifeJournalPage.Signals;
                    bodyScroll = Vector2.zero;
                });
            DrawHubAction(new Rect((actionWidth + 8f) * 2f, actionY, actionWidth, 72f), "Colony knowledge",
                "Open the existing colony-wide knowledge detail window.", "Open details", true,
                () =>
                {
                    WildlifeUI.CloseMenus();
                    Find.WindowStack.Add(new Window_ColonyWildlifeKnowledge());
                });

            float y = actionY + 90f;
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, y, view.width, 28f), "Working records");
            Text.Font = GameFont.Small;
            y += 34f;
            if (records.Count == 0)
            {
                DrawHubEmpty(new Rect(0f, y, view.width, 94f),
                    "No wildlife knowledge has been preserved yet.",
                    "Observe an animal, study a sign, follow a trail, or review a signal to begin a record.");
            }
            else
            {
                for (int i = 0; i < records.Count; i++)
                {
                    DrawKnowledgeRecord(new Rect(0f, y + i * 74f, view.width, 68f), records[i], activeMap);
                }
            }
            if (hasEvidence)
            {
                GUI.color = new Color(0.66f, 0.76f, 0.68f);
                Widgets.Label(new Rect(0f, y + Mathf.Max(94f, records.Count * 74f) + 8f, view.width, 24f),
                    "The colony's conclusions remain provisional until repeated evidence agrees.");
                GUI.color = Color.white;
            }
            Widgets.EndScrollView();
        }

        private void DrawKnowledgeRecord(Rect rect, WildlifeSpeciesSnapshot value, Map activeMap)
        {
            Widgets.DrawMenuSection(rect);
            if (value?.species != null) Widgets.ThingIcon(new Rect(rect.x + 8f, rect.y + 9f, 48f, 48f), value.species);
            string stage = WildlifeKnowledgeAdapter.StageLabel(value?.stageId);
            string source = KnowledgeSourceLabel(value);
            string detail = stage + "  |  " + ConfidenceLabel(value?.confidence ?? 0f) + "  |  " +
                FreshnessLabel(AtlasLatestTick(value)) + "\n" + source + ". " + KnowledgeMeaning(value);
            WildlifeSignalCultureMapComponent signalCulture = activeMap?.GetComponent<WildlifeSignalCultureMapComponent>();
            if (signalCulture?.HasWarningSignals(value?.species) == true)
            {
                WildlifeWarningKnowledgeState warning = signalCulture.ColonyWarningKnowledge(value.species);
                detail += "\nWarning calls: " + warning.PlayerLabel + ".";
            }
            WildlifePredatorPressureKnowledgeState predatorPressure = signalCulture?.ColonyPredatorPressure(value?.species);
            if (predatorPressure?.hasEvidence == true)
                detail += "\nEcological pattern: " + predatorPressure.PlayerLabel + ".";
            Widgets.Label(new Rect(rect.x + 66f, rect.y + 8f, rect.width - 178f, 52f),
                (value?.species?.LabelCap ?? "Unknown wildlife") + "\n" + detail);
            if (Widgets.ButtonText(new Rect(rect.xMax - 102f, rect.y + 18f, 92f, 30f), "Field Guide"))
            {
                selectedSpecies = value.species;
                page = WildlifeJournalPage.FieldGuide;
                bodyScroll = Vector2.zero;
            }
        }

        private static string KnowledgeSourceLabel(WildlifeSpeciesSnapshot value)
        {
            if (value == null) return "No source has been preserved";
            if (value.evidence?.Any(item => item != null &&
                (item.observerCount > 0 || !item.observerName.NullOrEmpty())) == true)
                return "Direct field observation";
            if (value.signals?.Count > 0) return "Interpreted animal signals";
            if (value.trails?.Count > 0) return "Signs and tracking evidence";
            if (value.migrations?.Count > 0) return "Regional movement";
            return "A working field record";
        }

        private static string KnowledgeMeaning(WildlifeSpeciesSnapshot value)
        {
            int order = WildlifeKnowledgeAdapter.StageOrder(value?.stageId);
            return order >= 5 ? "The colony can make a cautious prediction from it." :
                order >= 3 ? "Repeated observation is turning it into a pattern." :
                "More observation is needed before acting on it.";
        }

        private static WildlifePredatorPressureKnowledgeState BestPredatorPressure(Map activeMap,
            WildlifeEcologySnapshot snapshot, RegionalWildlifeMapComponent regional)
        {
            WildlifeSignalCultureMapComponent signalCulture = activeMap?.GetComponent<WildlifeSignalCultureMapComponent>();
            if (signalCulture == null) return null;
            HashSet<ThingDef> species = new HashSet<ThingDef>();
            if (snapshot?.species != null)
                for (int i = 0; i < snapshot.species.Count; i++)
                    if (snapshot.species[i]?.species != null) species.Add(snapshot.species[i].species);
            if (regional?.Records != null)
                for (int i = 0; i < regional.Records.Count; i++)
                    if (regional.Records[i]?.species != null) species.Add(regional.Records[i].species);
            return species.Select(signalCulture.ColonyPredatorPressure)
                .Where(state => state?.hasEvidence == true)
                .OrderByDescending(state => state.claimSupported)
                .ThenByDescending(state => state.meaningInterpreted)
                .ThenByDescending(state => state.patternRecognized)
                .ThenByDescending(state => state.claimObservationCount)
                .FirstOrDefault();
        }

        private void DrawRegionHub(Rect rect, Map activeMap)
        {
            WildlifeEcologySnapshot snapshot = cachedSnapshot;
            RegionalWildlifeMapComponent regional = activeMap?.GetComponent<RegionalWildlifeMapComponent>();
            WildlifeLandscapeMapComponent landscape = activeMap?.GetComponent<WildlifeLandscapeMapComponent>();
            WildlifeTrailMapComponent trails = activeMap?.GetComponent<WildlifeTrailMapComponent>();
            HuntingExpeditionMapComponent expeditions = activeMap?.GetComponent<HuntingExpeditionMapComponent>();
            float contentHeight = 74f + 84f + 82f + 42f + 162f;
            Rect outer = new Rect(rect.x, rect.y, rect.width, rect.height);
            Rect view = new Rect(0f, 0f, Mathf.Max(1f, outer.width - 16f),
                Mathf.Max(outer.height, contentHeight));
            Widgets.BeginScrollView(outer, ref bodyScroll, view);
            DrawHubHeader(new Rect(0f, 0f, view.width, 74f), "Region",
                "What is happening around the colony: habitat, populations, movement, groups, trails, and expeditions.");
            string habitat = snapshot == null ? "Unknown baseline" :
                snapshot.habitatQuality >= 0.70f ? "Habitat is stable" :
                snapshot.habitatQuality >= 0.40f ? "Habitat is under observation" : "Habitat is under pressure";
            bool hasMovement = snapshot?.migrations?.Count > 0 || snapshot?.trails?.Count > 0;
            string movement = hasMovement ? "Movement is active" : snapshot == null ? "No movement baseline" : "Movement is quiet";
            WildlifePredatorPressureKnowledgeState predatorPressure = BestPredatorPressure(activeMap, snapshot, regional);
            string pressure = predatorPressure == null ? "No repeated local predator-encounter pattern" : predatorPressure.PlayerLabel;
            bool activeExpedition = expeditions?.ActiveExpeditions?.Any() == true;
            string population = regional?.Records?.Any(record => record?.population < record.previousPopulation * 0.98f) == true
                ? "Some populations are declining" : regional?.Records?.Any(record =>
                    record?.population > record.previousPopulation * 1.02f) == true ? "Some populations are increasing" :
                    regional == null ? "Population outlook unavailable" : "Population trend is mixed or stable";
            float statusWidth = Mathf.Max(80f, (view.width - 16f) / 3f);
            DrawHubStatus(new Rect(0f, 82f, statusWidth, 72f), "Habitat", habitat,
                new Color(0.34f, 0.64f, 0.40f),
                "Habitat reflects seasonal, landscape, water, shelter, and disturbance evidence.");
            DrawHubStatus(new Rect(statusWidth + 8f, 82f, statusWidth, 72f), "Population pressure", population,
                new Color(0.72f, 0.49f, 0.30f),
                "Population changes are presented as trends until the colony has enough evidence to act with confidence.");
            DrawHubStatus(new Rect((statusWidth + 8f) * 2f, 82f, statusWidth, 72f), "Movement and pressure",
                movement + ".\n" + pressure,
                new Color(0.34f, 0.62f, 0.74f),
                "Trails, migration, and repeated defensive responses show where attention may be useful next.");

            float y = 164f;
            float actionWidth = Mathf.Max(120f, (view.width - 16f) / 3f);
            DrawHubAction(new Rect(0f, y, actionWidth, 72f), "Living Atlas",
                "Review activity sectors, habitat, and population patterns.", "Open Atlas", true,
                () =>
                {
                    page = WildlifeJournalPage.LivingAtlas;
                    bodyScroll = Vector2.zero;
                });
            DrawHubAction(new Rect(actionWidth + 8f, y, actionWidth, 72f), "Local wildlife",
                regional == null ? "Local population information is unavailable." :
                    "Open detailed local populations, groups, and management actions.",
                regional == null ? "Unavailable" : "Open local wildlife", regional != null,
                () =>
                {
                    WildlifeUI.CloseMenus();
                    Find.WindowStack.Add(new Window_RegionalWildlife(activeMap));
                });
            DrawHubAction(new Rect((actionWidth + 8f) * 2f, y, actionWidth, 72f), "Landscape",
                landscape == null ? "Landscape information is unavailable." :
                    "Review persistent habitat features and developing places.",
                landscape == null ? "Unavailable" : "Open landscape", landscape != null,
                () =>
                {
                    WildlifeUI.CloseMenus();
                    Find.WindowStack.Add(new Window_WildlifeLandscape(activeMap));
                });
            y += 82f;
            DrawHubAction(new Rect(0f, y, actionWidth, 72f), "Trail leads",
                trails == null ? "Tracking information is unavailable." :
                    "Inspect physical signs and decide whether to follow them.",
                trails == null ? "Unavailable" : "Open trail leads", trails != null,
                () =>
                {
                    WildlifeUI.CloseMenus();
                    Find.WindowStack.Add(new Window_WildlifeTrailBoard(activeMap));
                });
            DrawHubAction(new Rect(actionWidth + 8f, y, actionWidth, 72f), "Expeditions",
                !ExpeditionsVisible() ? "Expeditions are unavailable until their research and settings are ready." :
                    activeExpedition ? "An expedition is in the field. Review its course or plan another investigation." :
                    "Review active parties or plan an investigation beyond the local map.",
                !ExpeditionsVisible() ? "Unavailable" : "Open expeditions", ExpeditionsVisible(),
                () =>
                {
                    page = WildlifeJournalPage.Expeditions;
                    bodyScroll = Vector2.zero;
                });
            DrawHubAction(new Rect((actionWidth + 8f) * 2f, y, actionWidth, 72f),
                predatorPressure?.claimSupported == true ? "Predator Deterrent" : "Groups and behavior",
                predatorPressure?.claimSupported == true
                    ? "Repeated local predator encounters support defensive herd behavior. A Predator Deterrent is the existing response for discouraging ordinary predators."
                    : "Select an animal from Local Wildlife or the Living Atlas to inspect its group context.",
                predatorPressure?.claimSupported == true ? "Open regional management" : "Use regional detail", regional != null,
                () =>
                {
                    WildlifeUI.CloseMenus();
                    Find.WindowStack.Add(new Window_RegionalWildlife(activeMap));
                });
            Widgets.EndScrollView();
        }

        private void DrawChronicleHub(Rect rect, Map activeMap)
        {
            WildlifeNarrativeDirector director = WildlifeNarrativeUtility.For(activeMap);
            WildlifeMemoryMapComponent memory = activeMap?.GetComponent<WildlifeMemoryMapComponent>();
            NotableWildlifeMapComponent notables = activeMap?.GetComponent<NotableWildlifeMapComponent>();
            WildlifeMysteryMapComponent mysteries = activeMap?.GetComponent<WildlifeMysteryMapComponent>();
            List<WildlifeExperienceEvent> outcomes = Current.Game?.GetComponent<WildlifeExperienceGameComponent>()?.Events
                ?.Where(IsChronicleOutcome).Take(8).ToList() ?? new List<WildlifeExperienceEvent>();
            bool hasStories = director?.Stories?.Any() == true;
            bool hasMemory = memory?.Memories?.Any() == true || memory?.SocialMemories?.Any() == true;
            bool hasFolklore = memory?.Folklore?.Any() == true;
            bool hasNotables = notables?.Records?.Any() == true;
            bool hasMysteryHistory = mysteries?.Mysteries?.Any(value => value?.Solved == true || value?.Resolved == true) == true;
            float contentHeight = Mathf.Max(464f, 364f + outcomes.Count * 58f);
            Rect outer = new Rect(rect.x, rect.y, rect.width, rect.height);
            Rect view = new Rect(0f, 0f, Mathf.Max(1f, outer.width - 16f),
                Mathf.Max(outer.height, contentHeight));
            Widgets.BeginScrollView(outer, ref bodyScroll, view);
            DrawHubHeader(new Rect(0f, 0f, view.width, 74f), "Chronicle",
                "What has become part of the colony's history: survivors, memories, stories, mysteries, and consequences.");
            DrawHubStatus(new Rect(0f, 82f, (view.width - 16f) / 3f, 72f), "Notable animals",
                hasNotables ? "Individual histories are taking shape" : "No notable history yet",
                new Color(0.75f, 0.57f, 0.28f),
                "Notable animals emerge from actual encounters, survival, and recognition.");
            DrawHubStatus(new Rect((view.width - 16f) / 3f + 8f, 82f, (view.width - 16f) / 3f, 72f), "Memory and folklore",
                hasFolklore ? "Encounters are being retold" : hasMemory ? "Encounters are being remembered" :
                    "No memory has been preserved yet",
                new Color(0.63f, 0.42f, 0.70f),
                "Memories, traditions, ceremonies, and folklore remain owned by the memory systems.");
            DrawHubStatus(new Rect(((view.width - 16f) / 3f + 8f) * 2f, 82f,
                (view.width - 16f) / 3f, 72f), "Consequences",
                hasMysteryHistory || outcomes.Count > 0 || hasStories ? "History is accumulating" : "No resolved consequence yet",
                new Color(0.43f, 0.65f, 0.54f),
                "Resolved mysteries and persisted outcomes become part of the colony's account of wildlife.");

            float y = 164f;
            float actionWidth = Mathf.Max(120f, (view.width - 16f) / 3f);
            DrawHubAction(new Rect(0f, y, actionWidth, 72f), "Stories",
                "Read narrative interpretations, folklore, and recognized animals.", "Open stories", true,
                () =>
                {
                    page = WildlifeJournalPage.Stories;
                    bodyScroll = Vector2.zero;
                });
            DrawHubAction(new Rect(actionWidth + 8f, y, actionWidth, 72f), "Theories and resolutions",
                "Review hypotheses, evidence, and resolved investigations.", "Open investigations", true,
                () =>
                {
                    page = WildlifeJournalPage.Investigations;
                    bodyScroll = Vector2.zero;
                });
            DrawHubAction(new Rect((actionWidth + 8f) * 2f, y, actionWidth, 72f), "Notable animals",
                hasNotables ? "Open the existing individual animal story detail." : "No notable animal story is available yet.",
                hasNotables ? "Open stories" : "Unavailable", hasNotables,
                () =>
                {
                    WildlifeUI.CloseMenus();
                    Find.WindowStack.Add(new Window_WildlifeFieldJournal(activeMap, 4));
                });
            y += 82f;
            bool memoryAnimal = TryFindMemoryAnimal(activeMap, memory, out Pawn animal);
            DrawHubAction(new Rect(0f, y, actionWidth, 72f), "Folklore and traditions",
                hasFolklore ? "Review retellings and hold ceremonies through the retained detail window." :
                    "No folklore is available yet, but future encounters will appear here.",
                hasFolklore ? "Open folklore" : "Unavailable", hasFolklore,
                () =>
                {
                    WildlifeUI.CloseMenus();
                    Find.WindowStack.Add(new Window_WildlifeFieldJournal(activeMap, 5));
                });
            DrawHubAction(new Rect(actionWidth + 8f, y, actionWidth, 72f), "Animal memory",
                memoryAnimal ? "Open the timeline and social web for a remembered animal." :
                    "No remembered animal is currently available on this map.",
                memoryAnimal ? "Open memory" : "Unavailable", memoryAnimal,
                () =>
                {
                    WildlifeUI.CloseMenus();
                    Find.WindowStack.Add(new Window_AnimalMemoryTimeline(animal));
                });
            DrawHubAction(new Rect((actionWidth + 8f) * 2f, y, actionWidth, 72f), "Field Log",
                "Return to recent observations, evidence, and actionable records.", "Open Field Log", true,
                () =>
                {
                    page = WildlifeJournalPage.FieldLog;
                    bodyScroll = Vector2.zero;
                });
            y += 90f;
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, y, view.width, 28f), "Persisted consequences");
            Text.Font = GameFont.Small;
            y += 34f;
            if (outcomes.Count == 0)
            {
                DrawHubEmpty(new Rect(0f, y, view.width, 94f),
                    "No durable wildlife outcome has entered the chronicle yet.",
                    "Expedition results, discoveries, mysteries, and remarkable encounters will appear after their owning system records them.");
            }
            else
            {
                for (int i = 0; i < outcomes.Count; i++)
                    DrawChronicleOutcome(new Rect(0f, y + i * 58f, view.width, 52f), outcomes[i]);
            }
            Widgets.EndScrollView();
        }

        private static bool IsChronicleOutcome(WildlifeExperienceEvent value)
        {
            if (value == null || value.text.NullOrEmpty() || value.category.NullOrEmpty()) return false;
            return value.category.IndexOf("Expedition", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.category.IndexOf("Notable", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.category.IndexOf("Mystery", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.category.IndexOf("Folklore", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.category.IndexOf("Roaming", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.category.IndexOf("Story", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool TryFindMemoryAnimal(Map activeMap, WildlifeMemoryMapComponent memory, out Pawn animal)
        {
            animal = activeMap?.mapPawns?.AllPawnsSpawned?.FirstOrDefault(pawn => pawn?.RaceProps?.Animal == true &&
                (memory?.Memories?.Any(value => value?.animal == pawn) == true ||
                 memory?.SocialMemories?.Any(value => value?.animal == pawn) == true));
            return animal != null;
        }

        private void DrawChronicleOutcome(Rect rect, WildlifeExperienceEvent outcome)
        {
            Widgets.DrawMenuSection(rect);
            GUI.color = WildlifeExperience.IsNegative(outcome)
                ? new Color(0.88f, 0.48f, 0.38f) : new Color(0.55f, 0.78f, 0.60f);
            Widgets.Label(new Rect(rect.x + 10f, rect.y + 7f, rect.width * 0.30f, 18f), outcome.category);
            GUI.color = Color.white;
            Widgets.Label(new Rect(rect.x + 10f, rect.y + 25f, rect.width - 170f, 22f), outcome.text);
            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = new Color(0.64f, 0.73f, 0.66f);
            Widgets.Label(new Rect(rect.x + rect.width - 154f, rect.y + 8f, 144f, 18f), FreshnessLabel(outcome.tick));
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            if (outcome.thingId >= 0 && Widgets.ButtonText(new Rect(rect.xMax - 104f, rect.y + 25f, 94f, 22f), "Focus"))
            {
                Thing thing = WildlifeExperience.ResolveThing(outcome.thingId);
                if (thing != null) WildlifeUI.Focus(thing);
            }
            TooltipHandler.TipRegion(rect, outcome.text + "\n" + FreshnessLabel(outcome.tick));
        }

        private static void DrawHubHeader(Rect rect, string title, string description)
        {
            Widgets.DrawMenuSection(rect);
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(rect.x + 10f, rect.y + 8f, rect.width - 20f, 26f), title);
            Text.Font = GameFont.Small;
            GUI.color = new Color(0.70f, 0.78f, 0.71f);
            Widgets.Label(new Rect(rect.x + 10f, rect.y + 36f, rect.width - 20f, 30f), description);
            GUI.color = Color.white;
        }

        private static void DrawHubStatus(Rect rect, string label, string value, Color accent, string tooltip)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.09f, 0.14f, 0.15f, 0.94f));
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, 5f, rect.height), accent);
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.66f, 0.78f, 0.72f);
            Widgets.Label(new Rect(rect.x + 12f, rect.y + 7f, rect.width - 20f, 17f), label);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(rect.x + 12f, rect.y + 26f, rect.width - 20f, rect.height - 30f), value);
            TooltipHandler.TipRegion(rect, tooltip);
        }

        private static void DrawHubAction(Rect rect, string title, string detail, string button,
            bool enabled, Action action)
        {
            Widgets.DrawMenuSection(rect);
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(rect.x + 10f, rect.y + 7f, rect.width - 126f, 23f), title);
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(rect.x + 10f, rect.y + 31f, rect.width - 126f, 34f), detail);
            if (Widgets.ButtonText(new Rect(rect.xMax - 116f, rect.y + 20f, 106f, 30f), button, active: enabled) &&
                enabled) action?.Invoke();
            TooltipHandler.TipRegion(rect, detail);
        }

        private static void DrawHubEmpty(Rect rect, string title, string detail)
        {
            Widgets.DrawMenuSection(rect);
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(rect.x + 12f, rect.y + 10f, rect.width - 24f, 26f), title);
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(rect.x + 12f, rect.y + 42f, rect.width - 24f, rect.height - 50f), detail);
        }

        private static string FieldLogKindLabel(FieldLogItemKind kind) => kind == FieldLogItemKind.Moment
            ? "WILDLIFE MOMENT" : kind == FieldLogItemKind.Signal ? "SIGNAL" :
            kind == FieldLogItemKind.Trail ? "TRAIL" : kind == FieldLogItemKind.Investigation
                ? "INVESTIGATION" : kind == FieldLogItemKind.Expedition ? "EXPEDITION" : "OUTCOME";

        private static string FieldLogActionLabel(FieldLogItem item) => item.kind == FieldLogItemKind.Moment
            ? "Respond" : item.kind == FieldLogItemKind.Signal ? "Review signal" :
            item.kind == FieldLogItemKind.Trail ? "Inspect trail" : item.kind == FieldLogItemKind.Investigation
                ? "Review evidence" : item.kind == FieldLogItemKind.Expedition ? "Open details" : "Focus";

        private static bool FieldLogHasAction(FieldLogItem item) => item?.kind != FieldLogItemKind.Outcome ||
            (item.outcome != null && item.outcome.thingId >= 0);

        private void OpenFieldLogItem(FieldLogItem item, Map activeMap)
        {
            if (item == null || activeMap == null) return;
            if (item.kind == FieldLogItemKind.Signal)
            {
                OpenSignals(activeMap, signalObserver, item.species);
                return;
            }
            if (item.kind == FieldLogItemKind.Trail && item.trail != null)
            {
                WildlifeUI.CloseMenus();
                Find.WindowStack.Add(new Window_WildlifeTrail(activeMap, item.trail));
                return;
            }
            if (item.kind == FieldLogItemKind.Moment)
            {
                WildlifeUI.CloseMenus();
                Find.WindowStack.Add(new Window_WildlifeFieldJournal(activeMap, 2));
                return;
            }
            if (item.kind == FieldLogItemKind.Investigation)
            {
                WildlifeUI.CloseMenus();
                Find.WindowStack.Add(new Window_WildlifeFieldJournal(activeMap, 1));
                return;
            }
            if (item.kind == FieldLogItemKind.Expedition)
            {
                WildlifeUI.CloseMenus();
                Find.WindowStack.Add(new Window_WildlifeExpeditions(activeMap));
                return;
            }
            Thing thing = WildlifeExperience.ResolveThing(item.outcome?.thingId ?? -1);
            if (thing != null) WildlifeUI.Focus(thing);
        }

        private List<FieldLogItem> BuildFieldLog(Map activeMap, int now)
        {
            List<FieldLogItem> items = new List<FieldLogItem>();
            if (activeMap == null) return items;
            WildlifeFieldJournalMapComponent fieldJournal = activeMap.GetComponent<WildlifeFieldJournalMapComponent>();
            WildlifeOpportunityRecord opportunity = fieldJournal?.Opportunity;
            if (opportunity != null)
            {
                items.Add(new FieldLogItem
                {
                    kind = FieldLogItemKind.Moment,
                    key = "moment:" + opportunity.eventKey,
                    title = WildlifeFieldJournalMapComponent.OpportunityLabel(opportunity.kind),
                    detail = opportunity.description,
                    meaning = opportunity.response == WildlifeMomentResponse.None
                        ? "The colony has not chosen a response." : "The response is underway: " + opportunity.response + ".",
                    location = SectorName(activeMap, opportunity.focusCell),
                    certainty = "Direct observation",
                    tick = opportunity.startedTick,
                    species = opportunity.species,
                    urgent = opportunity.response == WildlifeMomentResponse.None
                });
            }
            foreach (WildlifeMomentOutcomeRecord outcome in fieldJournal?.MomentHistory?.Take(8) ??
                Enumerable.Empty<WildlifeMomentOutcomeRecord>())
            {
                if (outcome == null) continue;
                items.Add(new FieldLogItem
                {
                    kind = FieldLogItemKind.Moment,
                    key = "moment-outcome:" + (outcome.species?.defName ?? "wildlife") + ":" + outcome.tick,
                    title = WildlifeFieldJournalMapComponent.OpportunityLabel(outcome.kind) + " resolved",
                    detail = outcome.text,
                    meaning = outcome.success ? "The colony learned from the encounter." : "The encounter left an unresolved lesson.",
                    location = "Field record",
                    certainty = "Recorded",
                    tick = outcome.tick,
                    species = outcome.species,
                    urgent = false
                });
            }

            foreach (WildlifeSignalSnapshot signal in cachedSnapshot?.signals?.OrderByDescending(value => value.tick).Take(8) ??
                Enumerable.Empty<WildlifeSignalSnapshot>())
            {
                if (signal?.species == null) continue;
                WildlifeSignalCultureMapComponent signalCulture = activeMap.GetComponent<WildlifeSignalCultureMapComponent>();
                bool warningSignal = WildlifeSignalCultureMapComponent.IsWarningCall(signal.kind);
                WildlifeWarningKnowledgeState warning = warningSignal
                    ? signalCulture?.ColonyWarningKnowledge(signal.species) : null;
                WildlifePredatorPressureKnowledgeState predatorPressure = signal.predatorPressureEligible
                        ? signalCulture?.ColonyPredatorPressure(signal.species) : null;
                items.Add(new FieldLogItem
                {
                    kind = FieldLogItemKind.Signal,
                    key = "signal:" + signal.species.defName + ":" + signal.tick + ":" + signal.cell,
                    title = signal.species.LabelCap + " signal",
                    detail = signal.historicalDescription.NullOrEmpty()
                        ? "A wildlife signal was recorded." : signal.historicalDescription,
                    meaning = predatorPressure?.hasEvidence == true ? predatorPressure.PlayerDescription :
                        warningSignal ? warning?.PlayerDescription ?? "A warning call was recorded, but its meaning remains uncertain." :
                        signal.verified ? "The colony currently treats this interpretation as confirmed." :
                            signal.behaviorConsistent ? "The pattern is becoming familiar, but remains incomplete." :
                            "The colony is still deciding what this signal means.",
                    location = SectorName(activeMap, signal.cell),
                    certainty = predatorPressure?.claimSupported == true ? "Supported pattern" :
                        predatorPressure?.patternRecognized == true ? "Developing pattern" :
                        warningSignal ? warning?.claimSupported == true ? "Supported interpretation" :
                            warning?.meaningInterpreted == true ? "Interpreted" :
                            warning?.familyRecognized == true ? "Family recognized" : "Uncertain" :
                        signal.verified ? "Confirmed" : signal.behaviorConsistent ? "Probable" : "Uncertain",
                    tick = signal.tick,
                    species = signal.species,
                    urgent = false
                });
            }

            foreach (WildlifeTrailLead trail in activeMap.GetComponent<WildlifeTrailMapComponent>()?.TrailLeads
                ?.Where(value => value?.species != null && value.expiresTick > now)
                .OrderByDescending(value => value.createdTick).Take(8) ?? Enumerable.Empty<WildlifeTrailLead>())
            {
                IntVec3 cell = trail.predictedCell.IsValid ? trail.predictedCell : trail.departureCell;
                items.Add(new FieldLogItem
                {
                    kind = FieldLogItemKind.Trail,
                    key = "trail:" + trail.species.defName + ":" + trail.createdTick + ":" + trail.departureCell,
                    title = trail.species.LabelCap + " trail",
                    detail = trail.lastOutcome.NullOrEmpty() ?
                        "Signs point toward " + SectorName(activeMap, cell) + "." : trail.lastOutcome,
                    meaning = trail.state == WildlifeTrailState.BeyondMap
                        ? "The trail continues beyond the local map." : trail.viableLead
                            ? "This lead can support a tracking response." : "The direction remains uncertain.",
                    location = SectorName(activeMap, cell),
                    certainty = ConfidenceLabel(trail.confidence),
                    tick = trail.createdTick,
                    species = trail.species,
                    urgent = trail.state == WildlifeTrailState.LiveQuarry || trail.state == WildlifeTrailState.Pursuit,
                    trail = trail
                });
            }

            foreach (WildlifeMysteryRecord mystery in activeMap.GetComponent<WildlifeMysteryMapComponent>()?.Mysteries
                ?.Where(value => value != null && !value.Resolved)
                .OrderByDescending(value => value.startedTick).Take(4) ?? Enumerable.Empty<WildlifeMysteryRecord>())
            {
                IntVec3 cell = mystery.animal?.Spawned == true ? mystery.animal.Position : IntVec3.Invalid;
                items.Add(new FieldLogItem
                {
                    kind = FieldLogItemKind.Investigation,
                    key = "mystery:" + mystery.id,
                    title = mystery.title,
                    detail = mystery.Solved ? mystery.explanation : mystery.anomaly,
                    meaning = mystery.Solved ? "A cause has been identified; the colony must choose a response." :
                        "The colony is comparing evidence before acting.",
                    location = SectorName(activeMap, cell),
                    certainty = mystery.Solved ? "Confirmed" : "Uncertain",
                    tick = mystery.Solved && mystery.solvedTick > 0 ? mystery.solvedTick : mystery.startedTick,
                    species = mystery.species,
                    urgent = mystery == activeMap.GetComponent<WildlifeMysteryMapComponent>()?.Active
                });
            }

            foreach (HuntingExpeditionRecord expedition in activeMap.GetComponent<HuntingExpeditionMapComponent>()?.ActiveExpeditions
                ?.Where(value => value != null).Take(4) ?? Enumerable.Empty<HuntingExpeditionRecord>())
            {
                items.Add(new FieldLogItem
                {
                    kind = FieldLogItemKind.Expedition,
                    key = "expedition:" + expedition.id,
                    title = "Wildlife expedition in progress",
                    detail = "Objective: " + expedition.objective + "." +
                        (expedition.targetSpecies == null ? string.Empty : " Target: " + expedition.targetSpecies.LabelCap + "."),
                    meaning = expedition.needsRescue ? "The field party needs assistance." :
                        "The colony is waiting for the next report from beyond the local map.",
                    location = "Beyond the local map",
                    certainty = expedition.needsRescue ? "Urgent report" : "Tracked",
                    tick = expedition.stageStartedTick > 0 ? expedition.stageStartedTick : expedition.departureTick,
                    species = expedition.targetSpecies,
                    urgent = expedition.needsRescue
                });
            }

            foreach (WildlifeExperienceEvent outcome in Current.Game?.GetComponent<WildlifeExperienceGameComponent>()?.Events
                ?.Where(value => value != null && !value.text.NullOrEmpty()).Take(30) ??
                Enumerable.Empty<WildlifeExperienceEvent>())
            {
                if (items.Any(item => item.tick == outcome.tick &&
                    string.Equals(item.detail, outcome.text, StringComparison.Ordinal))) continue;
                items.Add(new FieldLogItem
                {
                    kind = FieldLogItemKind.Outcome,
                    key = "outcome:" + outcome.category + ":" + outcome.tick + ":" + outcome.thingId,
                    title = outcome.category.NullOrEmpty() ? "Wildlife outcome" : outcome.category,
                    detail = outcome.text,
                    meaning = WildlifeExperience.IsNegative(outcome)
                        ? "The colony recorded a setback or warning." : "The colony recorded what happened for later understanding.",
                    location = "Colony record",
                    certainty = "Recorded",
                    tick = outcome.tick,
                    urgent = false,
                    outcome = outcome
                });
            }

            Dictionary<string, FieldLogItem> unique = new Dictionary<string, FieldLogItem>(StringComparer.Ordinal);
            foreach (FieldLogItem item in items.Where(value => value != null))
            {
                if (item.key.NullOrEmpty()) item.key = item.kind + ":" + item.tick + ":" + item.title;
                if (!unique.ContainsKey(item.key)) unique.Add(item.key, item);
            }
            return unique.Values.OrderByDescending(value => value.urgent)
                .ThenByDescending(value => value.tick).Take(28).ToList();
        }

        private void ShowSignals(Pawn observer, ThingDef species)
        {
            page = WildlifeJournalPage.Signals;
            signalObserver = observer;
            if (species != null) selectedSpecies = species;
            if (signalPanel != null) signalPanel.SetContext(observer, species);
        }

        private void DrawFieldGuide(Rect rect, Map activeMap)
        {
            bool stacked = rect.width < 720f;
            float leftWidth = stacked ? rect.width : Mathf.Clamp(rect.width * 0.29f, 220f, 330f);
            Rect left = new Rect(rect.x, rect.y, leftWidth, stacked ? Mathf.Min(190f, rect.height * 0.34f) : rect.height);
            Rect detail = stacked
                ? new Rect(rect.x, left.yMax + 8f, rect.width, rect.yMax - left.yMax - 8f)
                : new Rect(left.xMax + 10f, rect.y, rect.width - left.width - 10f, rect.height);
            DrawSpeciesList(left, activeMap);
            DrawDossier(detail, activeMap);
        }

        private void DrawSpeciesList(Rect rect, Map activeMap)
        {
            Widgets.DrawMenuSection(rect);
            Rect inner = rect.ContractedBy(8f);
            search = Widgets.TextField(new Rect(inner.x, inner.y, inner.width, 28f), search ?? string.Empty);
            List<ThingDef> filtered = cachedSpecies.Where(value => search.NullOrEmpty() ||
                value.LabelCap.ToString().IndexOf(search.Trim(), StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            Rect outer = new Rect(inner.x, inner.y + 36f, inner.width, inner.height - 36f);
            Rect view = new Rect(0f, 0f, outer.width - 16f, Mathf.Max(outer.height, filtered.Count * 48f));
            Widgets.BeginScrollView(outer, ref leftScroll, view);
            try
            {
                for (int i = 0; i < filtered.Count; i++)
                {
                    ThingDef species = filtered[i];
                    WildlifeSpeciesSnapshot value = cachedSnapshot?.For(species);
                    string stage = WildlifeKnowledgeAdapter.StageLabel(value?.stageId);
                    Rect row = new Rect(0f, i * 48f, view.width, 46f);
                    if (species == selectedSpecies) Widgets.DrawHighlightSelected(row); else Widgets.DrawHighlightIfMouseover(row);
                    DrawStageShape(new Rect(row.x + 4f, row.y + 5f, 32f, 32f), value?.stageId);
                    Widgets.Label(new Rect(row.x + 44f, row.y + 2f, row.width - 48f, 20f), species.LabelCap);
                    GUI.color = Color.gray;
                    Widgets.Label(new Rect(row.x + 44f, row.y + 21f, row.width - 48f, 22f), stage +
                        (value == null ? string.Empty : "  " + value.confidence.ToStringPercent()));
                    GUI.color = Color.white;
                    if (Widgets.ButtonInvisible(row))
                    {
                        selectedSpecies = species;
                        bodyScroll = Vector2.zero;
                    }
                    TooltipHandler.TipRegion(row, "Unknown is an empty outline. Rumored evidence is dashed. Observed evidence is muted. Verified knowledge is full color. Documented knowledge bears an archive seal.");
                }
            }
            finally { Widgets.EndScrollView(); }
            if (filtered.Count == 0) Widgets.Label(outer.ContractedBy(8f), "No species match the current search.");
        }

        private void DrawDossier(Rect rect, Map activeMap)
        {
            Widgets.DrawMenuSection(rect);
            Rect inner = rect.ContractedBy(12f);
            WildlifeSpeciesSnapshot value = cachedSnapshot?.For(selectedSpecies);
            if (selectedSpecies == null || value == null)
            {
                Widgets.Label(inner, "Select a species to read its field record.");
                return;
            }
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inner.x, inner.y, inner.width - 170f, 32f), selectedSpecies.LabelCap);
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(inner.x, inner.y + 31f, inner.width - 170f, 24f),
                WildlifeKnowledgeAdapter.StageLabel(value.stageId) + "  |  confidence " + value.confidence.ToStringPercent());
            if (Widgets.ButtonText(new Rect(inner.xMax - 158f, inner.y, 158f, 30f), "Focus nearest", activeMap != null))
                FocusSpecies(activeMap, selectedSpecies);
            if (SignalsVisible() &&
                Widgets.ButtonText(new Rect(inner.xMax - 318f, inner.y, 150f, 30f), "Signals"))
                ShowSignals(null, selectedSpecies);
            Rect portrait = new Rect(inner.x, inner.y + 84f, 148f, 148f);
            DrawProgressivePortrait(portrait, selectedSpecies, value.stageId);
            DrawFacetRing(new Rect(portrait.x + 8f, portrait.y + 8f, portrait.width - 16f, portrait.height - 16f), selectedSpecies, activeMap);
            Rect facts = new Rect(portrait.xMax + 14f, portrait.y, inner.width - portrait.width - 14f, 148f);
            Widgets.DrawMenuSection(facts);
            Widgets.Label(facts.ContractedBy(9f),
                "Local count: " + value.localCount + "\n" +
                "Nearby estimate: " + Approximate(value.nearbyPopulation, value.confidence) + "\n" +
                "Regional estimate: " + Approximate(value.regionalPopulation, value.confidence) + "\n" +
                "Habitat pressure: " + value.pressure.ToStringPercent() + "\n" +
                "Forecast: " + value.forecast + "\n" +
                "Policy: " + value.policy +
                (value.variations.Count == 0 ? string.Empty : "\nVariation: " + string.Join(", ", value.variations.Take(3))));
            HuntingKnowledgeMapComponent knowledge = activeMap.GetComponent<HuntingKnowledgeMapComponent>();
            PassiveObservationFamiliarity familiarity = knowledge?.FamiliarityFor(selectedSpecies, Find.TickManager?.TicksGame ?? 0);
            Widgets.Label(new Rect(inner.x, inner.y + 54f, inner.width, 20f), familiarity == null || familiarity.observerCount == 0
                ? "Routine observations: none today"
                : "Routine observation today: " + familiarity.dailyFraction.ToStringPercent() + " of daily learning cap  |  " + familiarity.observedHours.ToString("0.0") + " hours");
            float y = portrait.yMax + 12f;
            string next = NextStudy(value, activeMap);
            Widgets.DrawMenuSection(new Rect(inner.x, y, inner.width, 54f));
            GUI.color = new Color(0.67f, 0.82f, 0.68f);
            Widgets.Label(new Rect(inner.x + 10f, y + 7f, inner.width - 150f, 40f), "Next useful observation\n" + next);
            GUI.color = Color.white;
            if (Widgets.ButtonText(new Rect(inner.xMax - 132f, y + 11f, 120f, 32f), "Investigate"))
                InvestigateSpecies(activeMap, value);
            y += 64f;
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inner.x, y, inner.width, 28f), "Meaningful discoveries");
            Text.Font = GameFont.Small;
            Rect evidenceOuter = new Rect(inner.x, y + 32f, inner.width, inner.yMax - y - 32f);
            List<WildlifeEvidenceSnapshot> evidence = value.evidence.ToList();
            const float evidenceRowHeight = 62f;
            Rect evidenceView = new Rect(0f, 0f, evidenceOuter.width - 16f, Mathf.Max(evidenceOuter.height, evidence.Count * evidenceRowHeight));
            Widgets.BeginScrollView(evidenceOuter, ref bodyScroll, evidenceView);
            for (int i = 0; i < evidence.Count; i++)
            {
                WildlifeEvidenceSnapshot item = evidence[i];
                Rect row = new Rect(0f, i * evidenceRowHeight, evidenceView.width, evidenceRowHeight - 4f);
                Widgets.DrawHighlightIfMouseover(row);
                GUI.color = item.success ? new Color(0.66f, 0.84f, 0.68f) : new Color(0.92f, 0.55f, 0.45f);
                Widgets.Label(new Rect(row.x + 6f, row.y + 3f, row.width * 0.26f, 20f),
                    item.kind == WildlifeEventKind.Signal ? "Signal" : item.kind.ToString());
                GUI.color = Color.white;
                float textX = row.x + row.width * 0.27f;
                float textWidth = row.width * 0.57f;
                Widgets.Label(new Rect(textX, row.y + 3f, textWidth, 27f), item.summary ?? "Evidence recorded.");
                GUI.color = new Color(0.58f, 0.76f, 0.63f);
                Widgets.Label(new Rect(textX, row.y + 30f, textWidth, 24f), EvidenceResult(item));
                if (item.kind == WildlifeEventKind.Signal && SignalsVisible() &&
                    Widgets.ButtonText(new Rect(row.xMax - 96f, row.y + 30f, 88f, 26f), "Review Signal"))
                    ShowSignals(item.observer?.Dead == true ? null : item.observer, selectedSpecies);
                Text.Anchor = TextAnchor.MiddleRight;
                GUI.color = Color.gray;
                Widgets.Label(new Rect(row.x + row.width * 0.76f, row.y + 3f, row.width * 0.12f, 30f),
                    (Find.TickManager.TicksGame - item.tick).ToStringTicksToPeriod() + " ago");
                GUI.color = Color.white;
                Text.Anchor = TextAnchor.UpperLeft;
                TooltipHandler.TipRegion(row, "Quality " + item.quality.ToString("0.00") + "  Confidence " + item.confidence.ToStringPercent() +
                    (item.observerCount > 1 ? "\nObservers: " + item.observerCount : string.Empty) +
                    (item.observerName.NullOrEmpty() ? string.Empty : "\nObserver: " + item.observerName) +
                    (item.contextLabel.NullOrEmpty() ? string.Empty : "\nContext: " + item.contextLabel));
            }
            Widgets.EndScrollView();
            if (evidence.Count == 0) Widgets.Label(evidenceOuter.ContractedBy(8f), "No direct evidence has been preserved yet.");
        }

        private void DrawLivingAtlas(Rect rect, Map activeMap)
        {
            Rect header = new Rect(rect.x, rect.y, rect.width, 82f);
            Widgets.DrawMenuSection(header);
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(header.x + 10f, header.y + 7f, header.width - 20f, 24f), "Living Atlas");
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(header.x + 10f, header.y + 32f, header.width - 20f, 30f),
                "The Living Atlas shows where wildlife is active, how populations are changing, and what the colony can investigate or influence.");
            Rect overview = new Rect(rect.x, header.yMax + 8f, rect.width, 94f);
            DrawAtlasOverview(overview, activeMap, cachedSnapshot);
            Rect legend = new Rect(rect.x, overview.yMax + 8f, rect.width, 30f);
            Widgets.DrawMenuSection(legend);
            Widgets.Label(legend.ContractedBy(7f), "Evidence strength: Confirmed  |  Probable  |  Uncertain  |  Stale     " +
                "Cards describe decisions, not a precise map of every animal.");
            Rect outer = new Rect(rect.x, legend.yMax + 8f, rect.width, Mathf.Max(1f, rect.yMax - legend.yMax - 8f));
            if (cachedSnapshot == null)
            {
                Widgets.Label(outer.ContractedBy(8f), "The atlas is still gathering its first ecological snapshot.");
                return;
            }
            List<WildlifeSpeciesSnapshot> visibleSpecies = OrderByKnowledge(cachedSnapshot.species
                .Where(HasAtlasEvidence)).ToList();
            if (visibleSpecies.Count == 0 && atlasActivities.Count == 0)
            {
                Widgets.DrawMenuSection(outer.ContractedBy(8f));
                Widgets.Label(outer.ContractedBy(20f),
                    "The atlas has no current activity to place. Keep a colonist near wildlife, survey the region, or follow a trail to create the first useful ecological snapshot.");
                return;
            }
            const float activityHeight = 92f;
            const float speciesHeight = 112f;
            float contentHeight = 42f + atlasActivities.Count * activityHeight +
                (visibleSpecies.Count == 0 ? 0f : 34f + visibleSpecies.Count * speciesHeight);
            Rect view = new Rect(0f, 0f, Mathf.Max(1f, outer.width - 16f), Mathf.Max(outer.height, contentHeight));
            Widgets.BeginScrollView(outer, ref bodyScroll, view);
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, view.width, 30f), "Activity sectors");
            Text.Font = GameFont.Small;
            for (int i = 0; i < atlasActivities.Count; i++)
                DrawAtlasActivity(new Rect(0f, 34f + i * activityHeight, view.width, activityHeight - 6f), atlasActivities[i], activeMap);
            float speciesY = 34f + atlasActivities.Count * activityHeight;
            if (visibleSpecies.Count > 0)
            {
                Text.Font = GameFont.Medium;
                Widgets.Label(new Rect(0f, speciesY, view.width, 30f), "Species decisions");
                Text.Font = GameFont.Small;
                speciesY += 34f;
                int first = Mathf.Min(visibleSpecies.Count, Mathf.Max(0, Mathf.FloorToInt((bodyScroll.y - speciesY) / speciesHeight) - 1));
                int last = Mathf.Min(visibleSpecies.Count,
                    Mathf.CeilToInt((bodyScroll.y + outer.height - speciesY) / speciesHeight) + 1);
                for (int i = first; i < last; i++)
                    DrawAtlasSpecies(new Rect(0f, speciesY + i * speciesHeight, view.width, speciesHeight - 6f), visibleSpecies[i], activeMap);
            }
            Widgets.EndScrollView();
        }

        private static void DrawAtlasOverview(Rect rect, Map activeMap, WildlifeEcologySnapshot snapshot)
        {
            Widgets.DrawMenuSection(rect);
            if (snapshot == null)
            {
                Widgets.Label(rect.ContractedBy(8f), "Evidence is limited. Observe wildlife, study signs, or survey the region to establish a baseline.");
                return;
            }
            int signalCount = SignalsVisible() ? snapshot.signals.Count : 0;
            RegionalWildlifeMapComponent regional = activeMap?.GetComponent<RegionalWildlifeMapComponent>();
            int localSpecies = 0;
            int increasing = 0;
            int declining = 0;
            float pressure = 0f;
            int pressureCount = 0;
            for (int i = 0; i < snapshot.species.Count; i++)
            {
                WildlifeSpeciesSnapshot value = snapshot.species[i];
                if (value.localCount > 0) localSpecies++;
                pressure += value.pressure;
                pressureCount++;
                RegionalSpeciesRecord record = regional?.Records?.FirstOrDefault(item => item?.species == value.species);
                if (record != null)
                {
                    if (record.population > record.previousPopulation * 1.02f) increasing++;
                    else if (record.population < record.previousPopulation * 0.98f) declining++;
                }
            }
            float averagePressure = pressureCount == 0 ? 0f : pressure / pressureCount;
            string habitat = snapshot.habitatQuality >= 0.7f ? "Stable" :
                snapshot.habitatQuality >= 0.4f ? "Evidence is limited" : "Habitat under pressure";
            string population = increasing > declining ? "Population increasing" :
                declining > increasing ? "Population declining" :
                increasing == 0 && declining == 0 ? "Stable" : "Mixed population trend";
            string movement = snapshot.migrations.Count > 0 ? "Unusual movement detected" :
                snapshot.trails.Count > 0 ? "Fresh trails are active" :
                signalCount > 0 ? "Recent calls recorded" : "Movement is quiet";
            string unusual = snapshot.activeMysteries > 0 ? "Unusual activity requires investigation" :
                signalCount > 0 ? "Recent calls are available for review" : "No unusual activity flagged";
            string pressureState = averagePressure >= 0.7f ? "High ecological pressure" :
                averagePressure >= 0.4f ? "Moderate ecological pressure" : "Pressure is low";
            Season season = activeMap == null ? Season.Spring : GenLocalDate.Season(activeMap);
            string seasonEffect = regional?.ActiveSeasonalEvent;
            if (seasonEffect.NullOrEmpty())
                seasonEffect = season == Season.Spring ? "Spring supports breeding and new growth." :
                    season == Season.Summer ? "Summer changes forage and water access." :
                    season == Season.Fall ? "Autumn encourages movement and preparation." :
                    "Winter concentrates wildlife around shelter and forage.";
            Widgets.Label(new Rect(rect.x + 10f, rect.y + 7f, rect.width - 20f, 22f),
                "Regional overview  |  " + season + "  |  " + seasonEffect);
            float width = Mathf.Max(80f, (rect.width - 28f) / 3f);
            DrawAtlasMetric(new Rect(rect.x + 10f, rect.y + 34f, width, 50f), "Habitat", habitat,
                "Measures current habitat condition from the ecological habitat score. " +
                "It is " + ConfidenceLabel(snapshot.habitatQuality) + " and updates as seasonal and landscape evidence changes.");
            DrawAtlasMetric(new Rect(rect.x + 14f + width, rect.y + 34f, width, 50f), "Populations",
                population + "  |  " + localSpecies + " species local",
                "Compares authoritative regional population estimates with their previous estimates. " +
                "Survey wildlife or review Local Wildlife to improve an uncertain trend.");
            DrawAtlasMetric(new Rect(rect.x + 18f + width * 2f, rect.y + 34f, width, 50f), "Activity",
                movement + "  |  " + snapshot.trails.Count + " trails, " + snapshot.migrations.Count +
                " migrations, " + signalCount + " calls",
                "Summarizes fresh trail and regional migration records. " +
                "Inspect a trail, focus an activity sector, or wait for new evidence.");
            TooltipHandler.TipRegion(rect, "The Living Atlas is a command map: use it to decide where to investigate or intervene.\n" +
                "Habitat: " + habitat + "\nTrend: " + population + "\n" + pressureState +
                "\n" + unusual + "\nSeason: " + seasonEffect + "\nFreshness follows the underlying record age.");
        }

        private static void DrawAtlasMetric(Rect rect, string label, string value, string tooltip)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.09f, 0.14f, 0.15f, 0.92f));
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.65f, 0.78f, 0.73f);
            Widgets.Label(new Rect(rect.x + 6f, rect.y + 4f, rect.width - 12f, 16f), label);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(rect.x + 6f, rect.y + 20f, rect.width - 12f, rect.height - 22f), value);
            TooltipHandler.TipRegion(rect, tooltip);
        }

        private List<AtlasActivity> BuildAtlasActivities(Map activeMap, WildlifeEcologySnapshot snapshot, int now)
        {
            List<AtlasActivity> result = new List<AtlasActivity>();
            if (activeMap == null || snapshot == null) return result;
            for (int i = 0; i < snapshot.species.Count; i++)
            {
                WildlifeSpeciesSnapshot species = snapshot.species[i];
                if (species?.species == null) continue;
                Pawn local = activeMap.mapPawns.AllPawnsSpawned.FirstOrDefault(pawn => pawn?.def == species.species &&
                    pawn.Faction == null);
                if (local != null)
                    result.Add(new AtlasActivity
                    {
                        kind = AtlasActivityKind.Presence,
                        species = species.species,
                        cell = local.Position,
                        title = "Local presence",
                        detail = species.localCount + " present; habitat pressure " + species.pressure.ToStringPercent() + ".",
                        confidence = Mathf.Clamp01(Mathf.Max(species.confidence, 0.68f)),
                        tick = now
                    });
                if (Mathf.Abs(species.regionalPopulation - species.nearbyPopulation) >= 0.5f ||
                    !species.forecast.NullOrEmpty())
                    result.Add(new AtlasActivity
                    {
                        kind = AtlasActivityKind.Trend,
                        species = species.species,
                        cell = activeMap.Center,
                        title = "Population trend",
                        detail = Approximate(species.nearbyPopulation, species.confidence) + " nearby; " + species.forecast + ".",
                        confidence = species.confidence,
                        tick = snapshot.tick
                    });
                for (int j = 0; j < species.trails.Count && result.Count < 24; j++)
                {
                    WildlifeTrailSnapshot trail = species.trails[j];
                    IntVec3 cell = trail.predictedCell.IsValid ? trail.predictedCell : trail.departureCell;
                    result.Add(new AtlasActivity
                    {
                        kind = AtlasActivityKind.Trail,
                        species = species.species,
                        cell = cell,
                        title = "Trail lead",
                        detail = (trail.viable ? "A viable trail" : "An uncertain trail") + " points toward " + SectorName(activeMap, cell) + ".",
                        confidence = trail.confidence,
                        tick = snapshot.tick
                    });
                }
                for (int j = 0; j < species.migrations.Count && result.Count < 24; j++)
                {
                    WildlifeMigrationSnapshot migration = species.migrations[j];
                    result.Add(new AtlasActivity
                    {
                        kind = AtlasActivityKind.Migration,
                        species = species.species,
                        cell = migration.animal?.Spawned == true ? migration.animal.Position : activeMap.Center,
                        title = "Movement and return",
                        detail = (migration.direction.NullOrEmpty() ? "Wildlife is moving" : "Movement toward " + migration.direction) +
                            (migration.expectedReturnTick > now ? "; expected return " + (migration.expectedReturnTick - now).ToStringTicksToPeriod() + "." : "."),
                        confidence = migration.animal?.Spawned == true ? 0.82f : 0.48f,
                        tick = snapshot.tick
                    });
                }
                for (int j = 0; SignalsVisible() &&
                    j < species.signals.Count && result.Count < 24; j++)
                {
                    WildlifeSignalSnapshot signal = species.signals[j];
                    result.Add(new AtlasActivity
                    {
                        kind = AtlasActivityKind.Signal,
                        species = species.species,
                        cell = signal.cell,
                        title = "Signal activity",
                        detail = signal.historicalDescription.NullOrEmpty()
                            ? WildlifeSignalPresentation.Description(signal.kind, 0f, signal.truthful,
                                signal.verified, signal.behaviorConsistent, null, species.species, signal.radius, null, null, activeMap)
                            : signal.historicalDescription,
                        confidence = signal.verified ? 0.86f : 0.42f,
                        tick = signal.tick
                    });
                }
            }
            result.Sort((left, right) => right.tick.CompareTo(left.tick));
            if (result.Count > 24) result.RemoveRange(24, result.Count - 24);
            return result;
        }

        private static bool HasAtlasEvidence(WildlifeSpeciesSnapshot value) => value != null &&
            (value.evidence.Count > 0 || value.trails.Count > 0 || value.migrations.Count > 0 ||
             value.signals.Count > 0 || value.knowledge > 0f || value.localCount > 0 ||
             value.nearbyPopulation > 0f);

        private static IEnumerable<WildlifeSpeciesSnapshot> OrderByKnowledge(IEnumerable<WildlifeSpeciesSnapshot> values) =>
            values.Where(value => value?.species != null)
                .OrderByDescending(value => WildlifeKnowledgeAdapter.StageOrder(value.stageId))
                .ThenByDescending(value => value.knowledge)
                .ThenByDescending(value => value.confidence)
                .ThenBy(value => value.species.LabelCap.ToString(), StringComparer.OrdinalIgnoreCase)
                .ThenBy(value => value.species.defName, StringComparer.Ordinal);

        private void DrawAtlasActivity(Rect rect, AtlasActivity activity, Map activeMap)
        {
            Widgets.DrawMenuSection(rect);
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(rect.x + 10f, rect.y + 7f, rect.width * 0.38f, 22f),
                activity.species.LabelCap + "  |  " + activity.title);
            Text.Font = GameFont.Small;
            string freshness = FreshnessLabel(activity.tick);
            string certainty = ConfidenceLabel(activity.confidence);
            Widgets.Label(new Rect(rect.x + 10f, rect.y + 31f, rect.width * 0.58f, 43f),
                activity.detail + "\n" + SectorName(activeMap, activity.cell) + "  |  " + certainty + "  |  " + freshness);
            float buttonWidth = 92f;
            float buttonX = rect.xMax - buttonWidth - 10f;
            string action = activity.kind == AtlasActivityKind.Signal ? "Signals" :
                activity.kind == AtlasActivityKind.Trail ? "Inspect Trail" :
                activity.kind == AtlasActivityKind.Presence ? "Focus Area" : "Review";
            if (Widgets.ButtonText(new Rect(buttonX, rect.y + 15f, buttonWidth, 30f), action))
            {
                if (activity.kind == AtlasActivityKind.Signal)
                    ShowSignals(signalObserver, activity.species);
                else if (activity.kind == AtlasActivityKind.Trail && activity.cell.IsValid)
                {
                    selectedSpecies = activity.species;
                    FocusCell(activeMap, activity.cell, WildlifeJournalPage.Investigations);
                }
                else if (activity.kind == AtlasActivityKind.Presence && activity.cell.IsValid)
                {
                    selectedSpecies = activity.species;
                    FocusCell(activeMap, activity.cell, WildlifeJournalPage.LivingAtlas);
                }
                else if (HerdsMod.Settings?.enableRegionalPopulations == true)
                    Find.WindowStack.Add(new Window_RegionalWildlife(activeMap));
                else
                {
                    selectedSpecies = activity.species;
                    page = WildlifeJournalPage.FieldGuide;
                }
            }
            TooltipHandler.TipRegion(rect, activity.detail + "\n\n" + certainty + " means " +
                ConfidenceExplanation(activity.confidence) + "\n" + freshness + ".");
        }

        private void DrawAtlasSpecies(Rect rect, WildlifeSpeciesSnapshot value, Map activeMap)
        {
            Widgets.DrawMenuSection(rect);
            float alpha = Mathf.Lerp(0.18f, 0.56f, value.confidence);
            Color field = value.pressure > 0.72f ? new Color(0.84f, 0.36f, 0.25f, alpha) : new Color(0.30f, 0.67f, 0.47f, alpha);
            Widgets.DrawBoxSolid(new Rect(rect.x + 6f, rect.y + 7f, rect.width * 0.55f, rect.height - 14f), field);
            DrawDashedBorder(new Rect(rect.x + 6f, rect.y + 7f, rect.width * 0.55f, rect.height - 14f), field);
            Widgets.ThingIcon(new Rect(rect.x + 16f, rect.y + 20f, 54f, 54f), value.species);
            Widgets.Label(new Rect(rect.x + 80f, rect.y + 14f, rect.width * 0.35f, 24f), value.species.LabelCap);
            string trend = AtlasTrend(activeMap, value);
            string pattern = value.trails.Count > 0 ? "Following fresh trail evidence" :
                value.migrations.Count > 0 ? "Movement is changing locally" :
                value.localCount > 0 ? "Present in the local landscape" : "No local presence confirmed";
            string nextAction = AtlasRecommendation(value);
            Widgets.Label(new Rect(rect.x + 80f, rect.y + 38f, rect.width * 0.43f, 54f),
                "Local population: " + trend + "\n" +
                "Landscape use: " + pattern + "\n" +
                "Pressure: " + AtlasPressure(value.pressure) + "  |  " + ConfidenceLabel(value.confidence) +
                "  |  " + FreshnessLabel(AtlasLatestTick(value)));
            if (value.migrations.Count > 0)
            {
                Vector2 start = new Vector2(rect.x + rect.width * 0.63f, rect.center.y);
                Vector2 end = start + new Vector2(Mathf.Min(95f, rect.width * 0.22f), 0f);
                Widgets.DrawLine(start, end, new Color(0.80f, 0.72f, 0.35f), 2f);
                Widgets.DrawLine(end, end + new Vector2(-8f, -5f), new Color(0.80f, 0.72f, 0.35f), 2f);
                Widgets.DrawLine(end, end + new Vector2(-8f, 5f), new Color(0.80f, 0.72f, 0.35f), 2f);
                Widgets.Label(new Rect(start.x, rect.y + 12f, rect.width * 0.30f, 20f), "migration evidence");
            }
            if (value.trails.Count > 0)
            {
                GUI.color = new Color(0.45f, 0.76f, 0.87f);
                Widgets.Label(new Rect(rect.x + rect.width * 0.63f, rect.y + 56f, rect.width * 0.30f, 20f),
                    value.trails.Count + " fresh trail lead" + (value.trails.Count == 1 ? string.Empty : "s"));
                GUI.color = Color.white;
            }
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.66f, 0.78f, 0.72f);
            Widgets.Label(new Rect(rect.x + rect.width * 0.63f, rect.y + 32f, rect.width * 0.31f, 34f),
                "Next step: " + nextAction);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            float buttonWidth = SignalsVisible() ? 76f : 88f;
            float buttonY = rect.y + rect.height - 34f;
            if (SignalsVisible() &&
                Widgets.ButtonText(new Rect(rect.xMax - buttonWidth * 3f - 18f, buttonY, buttonWidth, 28f), "Signals"))
                ShowSignals(signalObserver, value.species);
            if (Widgets.ButtonText(new Rect(rect.xMax - buttonWidth * 2f - 12f, buttonY, buttonWidth, 28f), "Field Guide"))
            {
                selectedSpecies = value.species;
                page = WildlifeJournalPage.FieldGuide;
                bodyScroll = Vector2.zero;
            }
            if (HerdsMod.Settings?.enableRegionalPopulations == true &&
                Widgets.ButtonText(new Rect(rect.xMax - buttonWidth - 8f, buttonY, buttonWidth, 28f), "Local Wildlife"))
                Find.WindowStack.Add(new Window_RegionalWildlife(activeMap));
            if (Widgets.ButtonInvisible(new Rect(rect.x, rect.y, rect.width * 0.58f, rect.height - 4f)))
            {
                selectedSpecies = value.species;
                page = WildlifeJournalPage.FieldGuide;
                bodyScroll = Vector2.zero;
            }
            TooltipHandler.TipRegion(rect, "Population: " + trend + "\nLandscape use: " + pattern +
                "\nPressure: " + AtlasPressure(value.pressure) + "\nConfidence " + value.confidence.ToStringPercent() +
                ": " + ConfidenceExplanation(value.confidence) + "\n" + FreshnessLabel(AtlasLatestTick(value)) +
                ": new observations, trail work, or a regional survey can improve this card.");
        }

        private static string AtlasTrend(Map activeMap, WildlifeSpeciesSnapshot value)
        {
            RegionalSpeciesRecord record = activeMap?.GetComponent<RegionalWildlifeMapComponent>()?.Records
                ?.FirstOrDefault(item => item?.species == value.species);
            if (record == null) return value.localCount > 0 ? value.localCount + " present" : "uncertain";
            if (record.population > record.previousPopulation * 1.02f) return "increasing";
            if (record.population < record.previousPopulation * 0.98f) return "declining";
            return "stable";
        }

        private static string AtlasPressure(float pressure) => pressure >= 0.7f ? "high" :
            pressure >= 0.4f ? "moderate" : "low";

        private static string AtlasRecommendation(WildlifeSpeciesSnapshot value)
        {
            if (value.trails.Count > 0) return "Inspect a trail";
            if (value.confidence < 0.45f) return "Survey the area";
            if (value.pressure >= 0.7f) return "Protect habitat";
            if (value.migrations.Count > 0) return "Review movement";
            return "Review the Field Guide";
        }

        private static int AtlasLatestTick(WildlifeSpeciesSnapshot value)
        {
            int latest = 0;
            for (int i = 0; i < value.evidence.Count; i++) latest = Mathf.Max(latest, value.evidence[i].tick);
            for (int i = 0; i < value.signals.Count; i++) latest = Mathf.Max(latest, value.signals[i].tick);
            if (value.localCount > 0) latest = Mathf.Max(latest, Find.TickManager?.TicksGame ?? latest);
            return latest;
        }

        private static string ConfidenceLabel(float confidence)
        {
            return confidence >= 0.75f ? "Confirmed" : confidence >= 0.45f ? "Probable" : "Uncertain";
        }

        private static string ConfidenceExplanation(float confidence)
        {
            return confidence >= 0.75f ? "multiple or direct observations agree" :
                confidence >= 0.45f ? "the estimate is supported but incomplete" : "the location or estimate remains approximate";
        }

        private static string FreshnessLabel(int tick)
        {
            int age = Mathf.Max(0, (Find.TickManager?.TicksGame ?? tick) - tick);
            return age > 15000 ? "Stale" : age > 5000 ? "Aging" : "Fresh";
        }

        private static string SectorName(Map activeMap, IntVec3 cell)
        {
            if (activeMap == null || !cell.IsValid) return "regional view";
            int horizontal = cell.x < activeMap.Size.x / 3 ? -1 : cell.x > activeMap.Size.x * 2 / 3 ? 1 : 0;
            int vertical = cell.z < activeMap.Size.z / 3 ? -1 : cell.z > activeMap.Size.z * 2 / 3 ? 1 : 0;
            if (horizontal == 0 && vertical == 0) return "central sector";
            string verticalLabel = vertical < 0 ? "south" : vertical > 0 ? "north" : string.Empty;
            string horizontalLabel = horizontal < 0 ? "west" : horizontal > 0 ? "east" : string.Empty;
            return (verticalLabel + (verticalLabel.NullOrEmpty() || horizontalLabel.NullOrEmpty() ? string.Empty : "-") + horizontalLabel).CapitalizeFirst() + " sector";
        }

        private void DrawInvestigations(Rect rect, Map activeMap)
        {
            WildlifeNarrativeDirector director = WildlifeNarrativeUtility.For(activeMap);
            WildlifeHypothesisRecord[] hypotheses = director?.OpenHypotheses?.ToArray() ?? Array.Empty<WildlifeHypothesisRecord>();
            WildlifeMysteryRecord mystery = activeMap.GetComponent<WildlifeMysteryMapComponent>()?.Active;
            Rect outer = new Rect(rect.x, rect.y, rect.width, rect.height);
            float contentWidth = Mathf.Max(80f, outer.width - 36f);
            float[] hypothesisHeights = hypotheses.Select(value => HypothesisCardHeight(value, contentWidth)).ToArray();
            float contentHeight = mystery == null ? 0f : 244f;
            if (mystery != null) contentHeight += 12f;
            contentHeight += hypothesisHeights.Sum(value => value + 12f) + 8f;
            Rect view = new Rect(0f, 0f, Mathf.Max(1f, outer.width - 16f), Mathf.Max(outer.height, contentHeight));
            Widgets.BeginScrollView(outer, ref bodyScroll, view);
            float y = 0f;
            if (mystery != null)
            {
                DrawMysteryCard(new Rect(0f, y, view.width, 232f), activeMap, mystery);
                y += 256f;
            }
            for (int i = 0; i < hypotheses.Length; i++)
            {
                DrawHypothesisCard(new Rect(0f, y, view.width, hypothesisHeights[i]), activeMap, hypotheses[i]);
                y += hypothesisHeights[i] + 12f;
            }
            if (mystery == null && hypotheses.Length == 0)
                Widgets.Label(new Rect(8f, 8f, view.width - 16f, 50f), "No open investigation. Strange movement, contradictory calls, population pressure, and fading trails will create bounded hypotheses from real evidence.");
            Widgets.EndScrollView();
        }

        private void DrawMysteryCard(Rect rect, Map activeMap, WildlifeMysteryRecord mystery)
        {
            Widgets.DrawMenuSection(rect);
            Widgets.Label(new Rect(rect.x + 10f, rect.y + 8f, rect.width - 20f, 26f), mystery.title + "  " + mystery.progress.ToStringPercent());
            Widgets.Label(new Rect(rect.x + 10f, rect.y + 36f, rect.width - 20f, 48f), mystery.anomaly);
            Widgets.Label(new Rect(rect.x + 10f, rect.y + 90f, rect.width - 20f, 56f), "Observed evidence\n" +
                (mystery.evidence.Count == 0 ? "No evidence yet." : string.Join("\n", mystery.evidence.Take(3).Select(value => "- " + value.clue + " [" + value.source + "]"))));
            if (Widgets.ButtonText(new Rect(rect.x + 10f, rect.yMax - 44f, 128f, 32f), "Review evidence"))
                activeMap.GetComponent<WildlifeMysteryMapComponent>()?.ReviewEvidence(mystery);
            if (Widgets.ButtonText(new Rect(rect.x + 146f, rect.yMax - 44f, 112f, 32f), "Focus"))
                activeMap.GetComponent<WildlifeMysteryMapComponent>()?.FocusEvidence(mystery);
        }

        private void DrawHypothesisCard(Rect rect, Map activeMap, WildlifeHypothesisRecord hypothesis)
        {
            Widgets.DrawMenuSection(rect);
            GUI.color = hypothesis.state == WildlifeHypothesisState.Disputed ? new Color(0.92f, 0.57f, 0.32f) : new Color(0.62f, 0.81f, 0.67f);
            Widgets.Label(new Rect(rect.x + 10f, rect.y + 8f, rect.width - 20f, 28f), hypothesis.title + "  " + hypothesis.state);
            GUI.color = Color.white;
            Widgets.Label(new Rect(rect.x + 10f, rect.y + 38f, rect.width - 20f, 22f), "Confidence band: " + hypothesis.confidence.ToStringPercent());
            float y = rect.y + 66f;
            float contentWidth = Mathf.Max(80f, rect.width - 20f);
            for (int i = 0; i < (hypothesis.candidates?.Count ?? 0); i++)
            {
                WildlifeHypothesisCandidate candidate = hypothesis.candidates[i];
                if (candidate == null) continue;
                string candidateText = (i + 1) + ". " + candidate.explanation + "  +" + candidate.support.ToString("0.00") +
                    " / -" + candidate.contradiction.ToString("0.00");
                float rowHeight = HypothesisCandidateHeight(candidateText, contentWidth);
                Rect row = new Rect(rect.x + 10f, y, contentWidth, rowHeight);
                Widgets.DrawHighlightIfMouseover(row);
                Widgets.Label(row.ContractedBy(4f), candidateText);
                y += rowHeight + 4f;
            }
            string bestNext = "Best next observation: " + hypothesis.bestNextObservation;
            float bestHeight = WrappedTextHeight(bestNext, contentWidth, 38f);
            Widgets.Label(new Rect(rect.x + 10f, y, contentWidth, bestHeight), bestNext);
            y += bestHeight + 6f;
            string earlyRisk = "Risk of acting early: " + hypothesis.actingEarlyRisk;
            float riskHeight = WrappedTextHeight(earlyRisk, contentWidth, 34f);
            Widgets.Label(new Rect(rect.x + 10f, y, contentWidth, riskHeight), earlyRisk);
            WildlifeHypothesisCandidate leading = hypothesis.LeadingCandidate;
            if (leading != null && Widgets.ButtonText(new Rect(rect.x + 10f, rect.yMax - 42f, 148f, 30f), "Document theory"))
                activeMap.GetComponent<WildlifeNarrativeDirector>()?.ResolveHypothesis(hypothesis, leading.explanation);
            if (Widgets.ButtonText(new Rect(rect.x + 166f, rect.yMax - 42f, 100f, 30f), "Field Guide"))
            {
                selectedSpecies = hypothesis.species;
                page = WildlifeJournalPage.FieldGuide;
            }
        }

        private static float HypothesisCardHeight(WildlifeHypothesisRecord hypothesis, float contentWidth)
        {
            if (hypothesis == null) return 244f;
            float y = 66f;
            for (int i = 0; i < (hypothesis.candidates?.Count ?? 0); i++)
            {
                WildlifeHypothesisCandidate candidate = hypothesis.candidates[i];
                if (candidate == null) continue;
                string text = (i + 1) + ". " + candidate.explanation + "  +" + candidate.support.ToString("0.00") +
                    " / -" + candidate.contradiction.ToString("0.00");
                y += HypothesisCandidateHeight(text, contentWidth) + 4f;
            }
            y += WrappedTextHeight("Best next observation: " + hypothesis.bestNextObservation, contentWidth, 38f) + 6f;
            y += WrappedTextHeight("Risk of acting early: " + hypothesis.actingEarlyRisk, contentWidth, 34f);
            return Mathf.Max(244f, y + 50f);
        }

        private static float HypothesisCandidateHeight(string text, float contentWidth) =>
            Mathf.Max(32f, Text.CalcHeight(text ?? string.Empty, Mathf.Max(80f, contentWidth - 8f)) + 8f);

        private static float WrappedTextHeight(string text, float contentWidth, float minimum) =>
            Mathf.Max(minimum, Text.CalcHeight(text ?? string.Empty, Mathf.Max(80f, contentWidth)));

        private void DrawExpeditions(Rect rect, Map activeMap)
        {
            HuntingExpeditionMapComponent component = activeMap.GetComponent<HuntingExpeditionMapComponent>();
            Rect header = new Rect(rect.x, rect.y, rect.width, 72f);
            Widgets.DrawMenuSection(header);
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(header.x + 10f, header.y + 7f, header.width - 220f, 24f), "Expeditions");
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(header.x + 10f, header.y + 32f, header.width - 220f, 32f),
                (component?.ActiveExpeditions.Count ?? 0) + " active field parties. Reports become contextual evidence, not passive colony XP.");
            if (Widgets.ButtonText(new Rect(header.xMax - 196f, header.y + 20f, 184f, 32f), "Plan expedition"))
                Find.WindowStack.Add(new Window_HuntingExpeditionSetup(activeMap));
            List<HuntingExpeditionRecord> active = component?.ActiveExpeditions?.ToList() ?? new List<HuntingExpeditionRecord>();
            float historyWidth = Mathf.Max(80f, rect.width - 32f);
            float historyHeight = component?.History?.Take(8).Sum(value => Mathf.Max(34f,
                Text.CalcHeight(value ?? string.Empty, historyWidth) + 10f)) ?? 0f;
            float contentHeight = 74f + active.Count * 84f + (active.Count == 0 ? 50f : 0f) +
                (component?.History?.Count > 0 ? 42f + historyHeight : 0f);
            Rect outer = new Rect(rect.x, header.yMax + 8f, rect.width, rect.yMax - header.yMax - 8f);
            Rect view = new Rect(0f, 0f, Mathf.Max(1f, outer.width - 16f), Mathf.Max(outer.height, contentHeight));
            Widgets.BeginScrollView(outer, ref bodyScroll, view);
            float y = 0f;
            for (int i = 0; i < active.Count; i++)
            {
                HuntingExpeditionRecord expedition = active[i];
                Rect row = new Rect(0f, y, view.width, 76f);
                Widgets.DrawMenuSection(row);
                Widgets.Label(new Rect(row.x + 10f, row.y + 8f, row.width * 0.43f, 24f), "Expedition " + expedition.id + "  " + expedition.stage);
                Widgets.Label(new Rect(row.x + 10f, row.y + 34f, row.width * 0.48f, 30f),
                    "Objective: " + expedition.objective + "\nTarget: " + (expedition.targetSpecies?.LabelCap ?? "survey"));
                Text.Anchor = TextAnchor.MiddleRight;
                Widgets.Label(new Rect(row.x + row.width * 0.62f, row.y + 14f, row.width * 0.33f, 24f),
                    "Return " + Mathf.Max(0, expedition.expectedReturnTick - Find.TickManager.TicksGame).ToStringTicksToPeriod());
                Text.Anchor = TextAnchor.UpperLeft;
                y += 84f;
            }
            if (active.Count == 0)
            {
                Widgets.Label(new Rect(10f, y + 8f, view.width - 20f, 40f), "No active expedition. Choose a next observation in the Field Guide or send a survey party.");
                y += 50f;
            }
            if (component?.History?.Count > 0)
            {
                Widgets.Label(new Rect(0f, y, view.width, 28f), "Historical reports");
                y += 42f;
                for (int i = 0; i < component.History.Count && i < 8; i++)
                {
                    float rowHeight = Mathf.Max(34f, Text.CalcHeight(component.History[i] ?? string.Empty, historyWidth) + 10f);
                    Rect row = new Rect(0f, y, view.width, rowHeight);
                    Widgets.DrawHighlightIfMouseover(row);
                    Widgets.Label(row.ContractedBy(4f), component.History[i]);
                    y += rowHeight + 2f;
                }
            }
            Widgets.EndScrollView();
        }

        private void DrawStories(Rect rect, Map activeMap)
        {
            WildlifeNarrativeDirector director = WildlifeNarrativeUtility.For(activeMap);
            List<WildlifeNarrativeRecord> stories = director?.Stories?.ToList() ?? new List<WildlifeNarrativeRecord>();
            WildlifeMemoryMapComponent memory = activeMap.GetComponent<WildlifeMemoryMapComponent>();
            List<WildlifeFolkloreRecord> folklore = memory?.Folklore?.ToList() ?? new List<WildlifeFolkloreRecord>();
            NotableWildlifeMapComponent notables = activeMap.GetComponent<NotableWildlifeMapComponent>();
            float contentHeight = stories.Count * 126f + folklore.Count * 94f + (notables?.Records.Count ?? 0) * 84f + 130f;
            Rect outer = new Rect(rect.x, rect.y, rect.width, rect.height);
            Rect view = new Rect(0f, 0f, outer.width - 16f, Mathf.Max(outer.height, contentHeight));
            Widgets.BeginScrollView(outer, ref bodyScroll, view);
            float y = 0f;
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, y, view.width, 30f), "Preserved stories");
            Text.Font = GameFont.Small;
            y += 36f;
            for (int i = 0; i < stories.Count; i++)
            {
                WildlifeNarrativeRecord story = stories[i];
                Rect card = new Rect(0f, y, view.width, 112f);
                Widgets.DrawMenuSection(card);
                Widgets.Label(new Rect(card.x + 10f, card.y + 8f, card.width - 20f, 22f), story.title);
                Widgets.Label(new Rect(card.x + 10f, card.y + 32f, card.width - 20f, 36f), story.summary);
                GUI.color = new Color(0.67f, 0.76f, 0.68f);
                Widgets.Label(new Rect(card.x + 10f, card.y + 72f, card.width - 20f, 30f), "Interpretation: " + story.interpretation);
                GUI.color = Color.white;
                if (story.animal?.Spawned == true && Widgets.ButtonInvisible(card)) FocusThing(activeMap, story.animal, WildlifeJournalPage.Stories);
                y += 120f;
            }
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, y, view.width, 30f), "Folklore");
            Text.Font = GameFont.Small;
            y += 36f;
            for (int i = 0; i < folklore.Count; i++)
            {
                WildlifeFolkloreRecord story = folklore[i];
                Rect card = new Rect(0f, y, view.width, 80f);
                Widgets.DrawMenuSection(card);
                Widgets.Label(new Rect(card.x + 10f, card.y + 8f, card.width - 20f, 22f), story.title + "  " + story.retellings + " tellings");
                Widgets.Label(new Rect(card.x + 10f, card.y + 32f, card.width - 20f, 40f), story.story);
                y += 88f;
            }
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, y, view.width, 30f), "Notable animals");
            Text.Font = GameFont.Small;
            y += 36f;
            foreach (NotableAnimalRecord record in notables?.Records ?? Array.Empty<NotableAnimalRecord>())
            {
                Rect card = new Rect(0f, y, view.width, 70f);
                Widgets.DrawMenuSection(card);
                if (record.animal?.def != null) Widgets.ThingIcon(new Rect(card.x + 8f, card.y + 8f, 52f, 52f), record.animal);
                Widgets.Label(new Rect(card.x + 68f, card.y + 8f, card.width - 220f, 22f), record.title + " - " + record.species.LabelCap);
                Widgets.Label(new Rect(card.x + 68f, card.y + 32f, card.width - 220f, 28f), record.distinction + "  |  " + record.culturalStatus);
                if (record.animal?.Spawned == true && Widgets.ButtonText(new Rect(card.xMax - 126f, card.y + 19f, 112f, 30f), "Focus"))
                    FocusThing(activeMap, record.animal, WildlifeJournalPage.Stories);
                y += 78f;
            }
            Widgets.EndScrollView();
        }

        private IReadOnlyList<KnowledgeFacetSnapshotV2> Facets(ThingDef species, Map activeMap)
        {
            string key = species?.defName + ":" + (activeMap?.uniqueID ?? 0) + ":" + cachedKnowledgeRevision;
            if (facetCache.TryGetValue(key, out IReadOnlyList<KnowledgeFacetSnapshotV2> cached)) return cached;
            List<KnowledgeFacetSnapshotV2> values = WildlifeKnowledgeAdapter.FacetIds()
                .Select(id => WildlifeKnowledgeAdapter.Facet(null, species, id, activeMap, true)).ToList();
            facetCache[key] = values;
            return values;
        }

        private void DrawFacetRing(Rect rect, ThingDef species, Map activeMap)
        {
            IReadOnlyList<KnowledgeFacetSnapshotV2> values = Facets(species, activeMap);
            Vector2 center = rect.center;
            float radius = Mathf.Min(rect.width, rect.height) * 0.46f;
            for (int i = 0; i < values.Count; i++)
            {
                float a = -Mathf.PI * 0.5f + Mathf.PI * 2f * i / values.Count;
                Vector2 start = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (radius * 0.68f);
                Vector2 end = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
                KnowledgeFacetSnapshotV2 value = values[i];
                Color color = value.confidence < 0.35f ? new Color(0.75f, 0.62f, 0.32f) :
                    value.completeness >= 0.75f ? new Color(0.43f, 0.81f, 0.55f) : new Color(0.45f, 0.61f, 0.74f);
                color.a = Mathf.Lerp(0.28f, 0.95f, value.completeness);
                Widgets.DrawLine(start, end, color, 2f + value.completeness * 3f);
            }
            TooltipHandler.TipRegion(rect, string.Join("\n", values.Select((value, index) => WildlifeKnowledgeAdapter.FacetIds()[index] + ": " + value.completeness.ToStringPercent() + " / " + value.confidence.ToStringPercent())));
        }

        private static void DrawProgressivePortrait(Rect rect, ThingDef species, string stage)
        {
            int order = WildlifeKnowledgeAdapter.StageOrder(stage);
            Widgets.DrawMenuSection(rect);
            if (species?.uiIcon == null)
            {
                DrawStageShape(rect.ContractedBy(30f), stage);
                return;
            }
            Color old = GUI.color;
            if (order <= 0)
            {
                Widgets.DrawBoxSolid(rect.ContractedBy(23f), new Color(0.10f, 0.12f, 0.13f));
                GUI.color = new Color(0.16f, 0.18f, 0.18f, 0.9f);
                GUI.DrawTexture(rect.ContractedBy(26f), species.uiIcon, ScaleMode.ScaleToFit);
            }
            else
            {
                GUI.color = new Color(1f, 1f, 1f, Mathf.Lerp(0.30f, 1f, Mathf.InverseLerp(1f, 5f, order)));
                GUI.DrawTexture(rect.ContractedBy(18f), species.uiIcon, ScaleMode.ScaleToFit);
            }
            GUI.color = old;
            if (order >= 5)
            {
                GUI.color = new Color(0.86f, 0.75f, 0.32f, 0.90f);
                Widgets.DrawBox(rect.ContractedBy(5f), 2, Texture2D.whiteTexture);
                Text.Anchor = TextAnchor.LowerRight;
                Widgets.Label(rect.ContractedBy(8f), "ARCHIVE");
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
            }
            else if (order == 1)
                DrawDashedBorder(rect.ContractedBy(8f), new Color(0.76f, 0.65f, 0.36f));
        }

        private static void DrawStageShape(Rect rect, string stage)
        {
            int order = WildlifeKnowledgeAdapter.StageOrder(stage);
            Color color = order <= 0 ? new Color(0.22f, 0.24f, 0.24f) : order == 1 ? new Color(0.72f, 0.62f, 0.34f) :
                order >= 5 ? new Color(0.86f, 0.74f, 0.30f) : new Color(0.40f, 0.68f, 0.48f);
            Widgets.DrawBoxSolid(rect, new Color(color.r, color.g, color.b, 0.26f));
            Widgets.DrawBox(rect, order == 1 ? 1 : 2, Texture2D.whiteTexture);
            if (order >= 5)
            {
                GUI.color = color;
                Widgets.Label(rect.ContractedBy(3f), "*");
                GUI.color = Color.white;
            }
        }

        private static void DrawDashedBorder(Rect rect, Color color)
        {
            color.a = 0.75f;
            float dash = 9f;
            for (float x = rect.x; x < rect.xMax; x += dash * 2f)
            {
                Widgets.DrawLine(new Vector2(x, rect.y), new Vector2(Mathf.Min(x + dash, rect.xMax), rect.y), color, 1f);
                Widgets.DrawLine(new Vector2(x, rect.yMax), new Vector2(Mathf.Min(x + dash, rect.xMax), rect.yMax), color, 1f);
            }
            for (float y = rect.y; y < rect.yMax; y += dash * 2f)
            {
                Widgets.DrawLine(new Vector2(rect.x, y), new Vector2(rect.x, Mathf.Min(y + dash, rect.yMax)), color, 1f);
                Widgets.DrawLine(new Vector2(rect.xMax, y), new Vector2(rect.xMax, Mathf.Min(y + dash, rect.yMax)), color, 1f);
            }
        }

        private void InvestigateSpecies(Map activeMap, WildlifeSpeciesSnapshot value)
        {
            WildlifeTrailSnapshot trail = value.trails.FirstOrDefault();
            if (trail != null && trail.departureCell.IsValid)
            {
                FocusCell(activeMap, trail.departureCell, WildlifeJournalPage.Investigations);
                return;
            }
            Pawn animal = activeMap.mapPawns.AllPawnsSpawned.FirstOrDefault(pawn => pawn?.def == value.species && pawn.Faction == null);
            if (animal != null) FocusThing(activeMap, animal, WildlifeJournalPage.Investigations);
            else page = WildlifeJournalPage.Investigations;
        }

        private void FocusSpecies(Map activeMap, ThingDef species)
        {
            Pawn target = activeMap.mapPawns.AllPawnsSpawned.FirstOrDefault(value => value?.def == species && value.Faction == null);
            if (target != null) FocusThing(activeMap, target, WildlifeJournalPage.FieldGuide);
            else page = WildlifeJournalPage.LivingAtlas;
        }

        private void FocusThing(Map activeMap, Thing thing, WildlifeJournalPage restorePage)
        {
            WildlifeUI.CloseMenus();
            CameraJumper.TryJumpAndSelect(thing);
            Find.WindowStack.Add(new Window_WildlifeJournal(activeMap, restorePage, thing as Pawn != null ? (thing as Pawn).def : thing.def));
        }

        private void FocusCell(Map activeMap, IntVec3 cell, WildlifeJournalPage restorePage)
        {
            WildlifeUI.CloseMenus();
            CameraJumper.TryJump(cell, activeMap);
            Find.WindowStack.Add(new Window_WildlifeJournal(activeMap, restorePage, selectedSpecies));
        }

        private static string Approximate(float value, float confidence)
        {
            float margin = Mathf.Max(1f, value * Mathf.Lerp(0.5f, 0.12f, confidence));
            return Mathf.Max(0f, value - margin).ToString("0") + "-" + Mathf.Max(1f, value + margin).ToString("0");
        }

        private static string EvidenceResult(WildlifeEvidenceSnapshot value)
        {
            string result = string.Empty;
            if (!value.previousStage.NullOrEmpty() && !value.newStage.NullOrEmpty() && value.previousStage != value.newStage)
                result = "Stage " + WildlifeKnowledgeAdapter.StageLabel(value.previousStage) + " -> " + WildlifeKnowledgeAdapter.StageLabel(value.newStage);
            else if (Mathf.Abs(value.confidenceDelta) >= 0.01f)
                result = (value.facetId.NullOrEmpty() ? "Knowledge" : value.facetId.CapitalizeFirst()) + " confidence " + value.previousConfidence.ToStringPercent() + " -> " + value.newConfidence.ToStringPercent();
            else if (Mathf.Abs(value.amountDelta) >= 0.01f)
                result = (value.facetId.NullOrEmpty() ? "Knowledge" : value.facetId.CapitalizeFirst()) + " +" + value.amountDelta.ToString("0.##") + " evidence";
            else result = "A distinct field discovery was preserved.";
            if (value.observerCount > 1) result += "  " + value.observerCount + " observers";
            if (value.observationHours > 0.05f) result += "  " + value.observationHours.ToString("0.0") + " h observed";
            if (value.elapsedHours > 0.05f) result += " over " + value.elapsedHours.ToString("0.0") + " h";
            return result;
        }

        private string NextStudy(WildlifeSpeciesSnapshot value, Map activeMap)
        {
            if (value.stageId == WildlifeKnowledgeAdapter.StageUnknown) return "Find a quiet sighting, fresh sign, or Study target.";
            IReadOnlyList<KnowledgeFacetSnapshotV2> facets = Facets(value.species, activeMap);
            if (WildlifeKnowledgeAdapter.StageOrder(value.stageId) < 2) return "Use Study or fresh tracks to confirm identity; routine proximity is already familiar.";
            if (!FacetKnown(facets, WildlifeKnowledgeAdapter.FacetMovement)) return "Study movement in a different sector or time of day.";
            if (!FacetKnown(facets, WildlifeKnowledgeAdapter.FacetSocial)) return "Observe a herd-size or behavior context; passive proximity will not add another entry.";
            if (!FacetKnown(facets, WildlifeKnowledgeAdapter.FacetHabitat)) return "Survey a different biome or season to establish habitat.";
            if (WildlifeKnowledgeAdapter.StageOrder(value.stageId) < 4) return "Compare a different sector, season, or behavior context.";
            if (WildlifeKnowledgeAdapter.StageOrder(value.stageId) < 5) return "Write a report that preserves the evidence and uncertainty.";
            return "The record is documented. Watch for contradictions and regional change.";
        }

        private static bool FacetKnown(IReadOnlyList<KnowledgeFacetSnapshotV2> facets, string facetId)
        {
            KnowledgeFacetSnapshotV2 value = facets?.FirstOrDefault(item => item != null && item.facetId == facetId);
            return value != null && (value.amount > 0.01f || value.completeness > 0.01f);
        }
    }
}
