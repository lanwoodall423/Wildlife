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
        private float seasonCursor = 1f;
        private int cachedTick = -1;
        private int cachedKnowledgeRevision = -1;
        private WildlifeEcologySnapshot cachedSnapshot;
        private List<ThingDef> cachedSpecies = new List<ThingDef>();
        private readonly Dictionary<string, IReadOnlyList<KnowledgeFacetSnapshotV2>> facetCache =
            new Dictionary<string, IReadOnlyList<KnowledgeFacetSnapshotV2>>(StringComparer.Ordinal);

        public override Vector2 InitialSize => new Vector2(Mathf.Min(1180f, UI.screenWidth * 0.94f), Mathf.Min(780f, UI.screenHeight * 0.90f));

        public Window_WildlifeJournal(Map map, WildlifeJournalPage page = WildlifeJournalPage.FieldGuide, ThingDef selectedSpecies = null)
        {
            this.map = map ?? Find.CurrentMap;
            this.page = page;
            this.selectedSpecies = selectedSpecies;
            doCloseX = true;
            absorbInputAroundWindow = true;
            resizeable = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Map activeMap = map ?? Find.CurrentMap;
            RefreshModel(activeMap);
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width - 220f, 34f), "Wildlife Journal");
            Text.Font = GameFont.Small;
            GUI.color = new Color(0.66f, 0.76f, 0.68f);
            Widgets.Label(new Rect(inRect.x, inRect.y + 30f, inRect.width - 220f, 24f),
                "Notice  >  gather evidence  >  form a hypothesis  >  investigate  >  decide  >  preserve the story");
            GUI.color = Color.white;
            DrawFreshnessMark(new Rect(inRect.xMax - 190f, inRect.y + 3f, 182f, 24f));

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
            cachedSpecies = cachedSnapshot?.species.Where(value => value?.species != null &&
                mapSpecies.Contains(value.species) && seenSpecies.Contains(value.species))
                .Select(value => value.species)
                .OrderBy(value => value.LabelCap.ToString(), StringComparer.OrdinalIgnoreCase)
                .ThenBy(value => value.defName, StringComparer.Ordinal)
                .ToList() ?? new List<ThingDef>();
            cachedTick = now / 30;
            cachedKnowledgeRevision = knowledgeRevision;
            facetCache.Clear();
            if (selectedSpecies == null || !cachedSpecies.Contains(selectedSpecies)) selectedSpecies = cachedSpecies.FirstOrDefault();
        }

        private void DrawTabs(Rect rect)
        {
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.MiddleCenter;
            string[] labels =
            {
                "Field Guide", "Living Atlas", "Investigations", "Expeditions", "Stories"
            };
            WildlifeJournalPage[] pages =
            {
                WildlifeJournalPage.FieldGuide, WildlifeJournalPage.LivingAtlas,
                WildlifeJournalPage.Investigations, WildlifeJournalPage.Expeditions,
                WildlifeJournalPage.Stories
            };
            Widgets.DrawBoxSolid(rect, new Color(0.11f, 0.13f, 0.13f, 1f));
            float width = rect.width / labels.Length;
            for (int i = 0; i < labels.Length; i++)
            {
                Rect tab = new Rect(rect.x + i * width, rect.y, width, rect.height);
                if (page == pages[i]) Widgets.DrawBoxSolid(tab.ContractedBy(1f), new Color(0.28f, 0.43f, 0.33f, 1f));
                else Widgets.DrawHighlightIfMouseover(tab.ContractedBy(1f));
                if (Widgets.ButtonInvisible(tab))
                {
                    page = pages[i];
                    bodyScroll = Vector2.zero;
                    leftScroll = Vector2.zero;
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
                "Evidence view cached at " + (cachedSnapshot?.tick.ToString() ?? "-") + " ticks");
            GUI.color = Color.white;
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
            Rect portrait = new Rect(inner.x, inner.y + 68f, 148f, 148f);
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
            float y = portrait.yMax + 12f;
            string next = NextStudy(value);
            Widgets.DrawMenuSection(new Rect(inner.x, y, inner.width, 54f));
            GUI.color = new Color(0.67f, 0.82f, 0.68f);
            Widgets.Label(new Rect(inner.x + 10f, y + 7f, inner.width - 150f, 40f), "Next useful observation\n" + next);
            GUI.color = Color.white;
            if (Widgets.ButtonText(new Rect(inner.xMax - 132f, y + 11f, 120f, 32f), "Investigate"))
                InvestigateSpecies(activeMap, value);
            y += 64f;
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inner.x, y, inner.width, 28f), "Recent evidence");
            Text.Font = GameFont.Small;
            Rect evidenceOuter = new Rect(inner.x, y + 32f, inner.width, inner.yMax - y - 32f);
            List<WildlifeEvidenceSnapshot> evidence = value.evidence.ToList();
            Rect evidenceView = new Rect(0f, 0f, evidenceOuter.width - 16f, Mathf.Max(evidenceOuter.height, evidence.Count * 48f));
            Widgets.BeginScrollView(evidenceOuter, ref bodyScroll, evidenceView);
            for (int i = 0; i < evidence.Count; i++)
            {
                WildlifeEvidenceSnapshot item = evidence[i];
                Rect row = new Rect(0f, i * 48f, evidenceView.width, 42f);
                Widgets.DrawHighlightIfMouseover(row);
                GUI.color = item.success ? new Color(0.66f, 0.84f, 0.68f) : new Color(0.92f, 0.55f, 0.45f);
                Widgets.Label(new Rect(row.x + 6f, row.y + 3f, row.width * 0.26f, 20f), item.kind.ToString());
                GUI.color = Color.white;
                Widgets.Label(new Rect(row.x + row.width * 0.27f, row.y + 3f, row.width * 0.57f, 35f), item.summary ?? "Evidence recorded.");
                Text.Anchor = TextAnchor.MiddleRight;
                GUI.color = Color.gray;
                Widgets.Label(new Rect(row.x + row.width * 0.84f, row.y + 3f, row.width * 0.14f, 30f),
                    (Find.TickManager.TicksGame - item.tick).ToStringTicksToPeriod() + " ago");
                GUI.color = Color.white;
                Text.Anchor = TextAnchor.UpperLeft;
                TooltipHandler.TipRegion(row, "Quality " + item.quality.ToString("0.00") + "  Confidence " + item.confidence.ToStringPercent());
            }
            Widgets.EndScrollView();
            if (evidence.Count == 0) Widgets.Label(evidenceOuter.ContractedBy(8f), "No direct evidence has been preserved yet.");
        }

        private void DrawLivingAtlas(Rect rect, Map activeMap)
        {
            Rect header = new Rect(rect.x, rect.y, rect.width, 68f);
            Widgets.DrawMenuSection(header);
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(header.x + 10f, header.y + 7f, header.width - 20f, 24f), "Living Atlas");
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(header.x + 10f, header.y + 32f, header.width - 20f, 30f),
                "Translucent fields show population pressure. Dashed edges show uncertainty; arrows show movement and migration.");
            Rect scrub = new Rect(rect.x, header.yMax + 8f, rect.width, 36f);
            Widgets.Label(new Rect(scrub.x, scrub.y, 124f, scrub.height), "Seasonal history");
            seasonCursor = Widgets.HorizontalSlider(new Rect(scrub.x + 132f, scrub.y + 8f, Mathf.Max(80f, scrub.width - 250f), 20f), seasonCursor, 0f, 1f);
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(new Rect(scrub.xMax - 105f, scrub.y, 105f, scrub.height), seasonCursor < 0.34f ? "past" : seasonCursor > 0.66f ? "forecast" : "current");
            Text.Anchor = TextAnchor.UpperLeft;
            Rect outer = new Rect(rect.x, scrub.yMax + 8f, rect.width, rect.yMax - scrub.yMax - 8f);
            if (cachedSnapshot == null)
            {
                Widgets.Label(outer.ContractedBy(8f), "The atlas is still gathering its first ecological snapshot.");
                return;
            }
            const float rowHeight = 108f;
            Rect view = new Rect(0f, 0f, Mathf.Max(1f, outer.width - 16f), Mathf.Max(outer.height, cachedSnapshot.species.Count * rowHeight));
            Widgets.BeginScrollView(outer, ref bodyScroll, view);
            int first = Mathf.Max(0, Mathf.FloorToInt(bodyScroll.y / rowHeight) - 1);
            int last = Mathf.Min(cachedSnapshot.species.Count,
                Mathf.CeilToInt((bodyScroll.y + outer.height) / rowHeight) + 1);
            for (int i = first; i < last; i++)
                DrawAtlasSpecies(new Rect(0f, i * rowHeight, view.width, 98f), cachedSnapshot.species[i], activeMap);
            Widgets.EndScrollView();
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
            Widgets.Label(new Rect(rect.x + 80f, rect.y + 38f, rect.width * 0.40f, 40f),
                "" + value.localCount + " present  |  " + Approximate(value.regionalPopulation, value.confidence) + " regional\n" + value.forecast);
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
            if (Widgets.ButtonInvisible(rect))
            {
                selectedSpecies = value.species;
                page = WildlifeJournalPage.FieldGuide;
                bodyScroll = Vector2.zero;
            }
            TooltipHandler.TipRegion(rect, "Confidence " + value.confidence.ToStringPercent() + ". Pressure " + value.pressure.ToStringPercent() + ". Click for the dossier.");
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

        private static string NextStudy(WildlifeSpeciesSnapshot value)
        {
            if (value.stageId == WildlifeKnowledgeAdapter.StageUnknown) return "Find a quiet sighting or a fresh sign.";
            if (WildlifeKnowledgeAdapter.StageOrder(value.stageId) < 2) return "Confirm identity with a second sighting or study.";
            if (WildlifeKnowledgeAdapter.StageOrder(value.stageId) < 3) return "Study feeding, movement, and social behavior.";
            if (WildlifeKnowledgeAdapter.StageOrder(value.stageId) < 4) return "Compare a different sector or season.";
            if (WildlifeKnowledgeAdapter.StageOrder(value.stageId) < 5) return "Write a report that preserves the evidence and uncertainty.";
            return "The record is documented. Watch for contradictions and regional change.";
        }
    }
}
