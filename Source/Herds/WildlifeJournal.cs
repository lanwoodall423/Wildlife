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
        FieldGuide,
        LivingAtlas,
        Signals,
        Investigations,
        Expeditions,
        Stories
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
        private Pawn signalObserver;
        private Vector2? initialSignalSpeciesScroll;
        private Vector2? initialSignalDetailScroll;
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

        public override Vector2 InitialSize => new Vector2(Mathf.Min(1180f, UI.screenWidth * 0.94f), Mathf.Min(780f, UI.screenHeight * 0.90f));

        public Window_WildlifeJournal(Map map, WildlifeJournalPage page = WildlifeJournalPage.FieldGuide,
            ThingDef selectedSpecies = null, Pawn signalObserver = null,
            Vector2? signalSpeciesScroll = null, Vector2? signalDetailScroll = null)
        {
            this.map = map ?? Find.CurrentMap;
            this.page = page;
            this.selectedSpecies = selectedSpecies;
            this.signalObserver = signalObserver;
            initialSignalSpeciesScroll = signalSpeciesScroll;
            initialSignalDetailScroll = signalDetailScroll;
            doCloseX = true;
            absorbInputAroundWindow = true;
            resizeable = true;
        }

        public static void OpenSignals(Map map, Pawn observer = null, ThingDef species = null,
            Vector2? speciesScroll = null, Vector2? detailScroll = null)
        {
            WildlifeJournalPage targetPage = SignalsVisible()
                ? WildlifeJournalPage.Signals : WildlifeJournalPage.FieldGuide;
            Find.WindowStack.Add(new Window_WildlifeJournal(map, targetPage, species, observer,
                speciesScroll, detailScroll));
        }

        public override void DoWindowContents(Rect inRect)
        {
            Map activeMap = map ?? Find.CurrentMap;
            RefreshModel(activeMap);
            if (page == WildlifeJournalPage.Expeditions && !ExpeditionsVisible()) page = WildlifeJournalPage.FieldGuide;
            if (page == WildlifeJournalPage.Signals && !SignalsVisible())
                page = WildlifeJournalPage.FieldGuide;
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
            Rect body = new Rect(inRect.x, tabs.yMax + 8f, inRect.width, Mathf.Max(1f, inRect.height - 99f));
            if (activeMap == null)
            {
                Widgets.Label(body, "No active map. Wildlife evidence will appear here when a map is available.");
                return;
            }
            switch (page)
            {
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
            List<string> labels = new List<string> { "Field Guide", "Living Atlas" };
            List<WildlifeJournalPage> pages = new List<WildlifeJournalPage>
            {
                WildlifeJournalPage.FieldGuide, WildlifeJournalPage.LivingAtlas
            };
            if (SignalsVisible())
            {
                labels.Add("Signals");
                pages.Add(WildlifeJournalPage.Signals);
            }
            labels.Add("Investigations");
            pages.Add(WildlifeJournalPage.Investigations);
            if (ExpeditionsVisible())
            {
                labels.Add("Expeditions");
                pages.Add(WildlifeJournalPage.Expeditions);
            }
            labels.Add("Stories");
            pages.Add(WildlifeJournalPage.Stories);
            Widgets.DrawBoxSolid(rect, new Color(0.11f, 0.13f, 0.13f, 1f));
            float width = rect.width / labels.Count;
            for (int i = 0; i < labels.Count; i++)
            {
                Rect tab = new Rect(rect.x + i * width, rect.y, width, rect.height);
                if (page == pages[i]) Widgets.DrawBoxSolid(tab.ContractedBy(1f), new Color(0.28f, 0.43f, 0.33f, 1f));
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

        private void DrawFreshnessMark(Rect rect)
        {
            float pulse = 0.50f + Mathf.Sin(Time.realtimeSinceStartup * 2.2f) * 0.08f;
            GUI.color = new Color(0.45f, 0.80f, 0.58f, pulse);
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y + 8f, 8f, 8f), GUI.color);
            GUI.color = new Color(0.68f, 0.76f, 0.69f);
            Widgets.Label(new Rect(rect.x + 14f, rect.y, rect.width - 14f, rect.height),
                "Evidence cached at " + (cachedSnapshot?.tick.ToString() ?? "-") + " ticks");
            GUI.color = Color.white;
        }

        public static bool SignalsVisible() => HerdsMod.Settings?.enableWildlifeSignalCulture == true;

        public static bool ExpeditionsVisible() => HerdsMod.Settings?.enableOffMapHuntingExpeditions == true &&
            WildlifeProgression.Unlocked(WildlifeCapability.HuntingExpedition);

        private void DrawSignals(Rect rect, Map activeMap)
        {
            if (!SignalsVisible())
            {
                page = WildlifeJournalPage.FieldGuide;
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
