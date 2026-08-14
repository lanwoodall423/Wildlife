using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using InsightCanvas;
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

    /// <summary>Compatibility entry points for the one retained Wildlife Journal document.</summary>
    public static class WildlifeJournal
    {
        public static void Open(Map map, WildlifeJournalPage page = WildlifeJournalPage.FieldLog,
            ThingDef species = null, Pawn observer = null, int focusedFieldLogTick = -1)
        {
            WildlifeJournalPresentation presentation = new WildlifeJournalPresentation(
                map ?? Find.CurrentMap, page, species, observer, focusedFieldLogTick);
            Find.WindowStack.Add(presentation.Window);
        }

        public static void OpenSignals(Map map, Pawn observer = null, ThingDef species = null,
            Vector2? speciesScroll = null, Vector2? detailScroll = null) =>
            Open(map, SignalsVisible() ? WildlifeJournalPage.Signals : WildlifeJournalPage.FieldLog,
                species, observer);

        public static void OpenFieldLog(Map map, int focusedTick = -1) =>
            Open(map, WildlifeJournalPage.FieldLog, null, null, focusedTick);

        public static bool SignalsVisible() => HerdsMod.Settings?.enableWildlifeSignalCulture == true;

        internal static IReadOnlyList<WildlifeJournalPage> TopLevelPagesForTesting() =>
            new[]
            {
                WildlifeJournalPage.FieldLog,
                WildlifeJournalPage.Knowledge,
                WildlifeJournalPage.Region,
                WildlifeJournalPage.Chronicle,
                WildlifeJournalPage.FieldGuide,
                WildlifeJournalPage.LivingAtlas,
                WildlifeJournalPage.Signals,
                WildlifeJournalPage.Investigations,
                WildlifeJournalPage.Expeditions,
                WildlifeJournalPage.Stories
            };

        public static bool ExpeditionsVisible() => HerdsMod.Settings?.enableOffMapHuntingExpeditions == true &&
            WildlifeProgression.Unlocked(WildlifeCapability.HuntingExpedition);

        internal static bool OpenPredatorDeterrent(Map map)
        {
            if (map == null) return false;
            ThingDef deterrentDef = HerdsDefOf.Herds_PredatorDeterrent;
            Thing existing = deterrentDef == null ? null : map.listerThings?.ThingsOfDef(deterrentDef)
                ?.FirstOrDefault(thing => thing?.Spawned == true);
            if (existing != null)
            {
                WildlifeUI.Focus(existing);
                return true;
            }
            if (HerdsMod.Settings?.enablePredatorDeterrents != true)
            {
                Messages.Message(JournalText.Get("Wildlife.UI.Journal.Deterrent.Disabled"),
                    MessageTypeDefOf.RejectInput, false);
                return false;
            }
            if (deterrentDef == null || deterrentDef.designationCategory == null)
            {
                Messages.Message(JournalText.Get("Wildlife.UI.Journal.Deterrent.Unavailable"),
                    MessageTypeDefOf.RejectInput, false);
                return false;
            }
            ResearchProjectDef pendingResearch = deterrentDef.researchPrerequisites?.FirstOrDefault(project =>
                project != null && !project.IsFinished);
            if (pendingResearch != null)
            {
                Messages.Message(JournalText.Get("Wildlife.UI.Journal.Deterrent.Research"),
                    MessageTypeDefOf.RejectInput, false);
                return false;
            }
            WildlifeUI.CloseMenus();
            Find.DesignatorManager.Select(new Designator_Build(deterrentDef));
            return Find.DesignatorManager.SelectedDesignator is Designator_Build;
        }

    }

    internal static class JournalText
    {
        internal static string Get(string key) => key.Translate();

        internal static string Format(string key, params object[] values) =>
            string.Format(Get(key), values ?? new object[0]);

        internal static string PageLabel(WildlifeJournalPage page) => Get("Wildlife.UI.Journal.Page." + page);

        internal static string PageDescription(WildlifeJournalPage page) =>
            Get("Wildlife.UI.Journal.Page." + page + ".Description");

        internal static string State(string value) => Get("Wildlife.UI.Journal.State." + value);

        internal static string SignalKind(WildlifeSignalKind value) =>
            Get("Wildlife.UI.Journal.SignalKind." + value);

        internal static string EventKind(WildlifeEventKind value) =>
            Get("Wildlife.UI.Journal.EventKind." + value);

        internal static string TrailState(WildlifeTrailState value) =>
            Get("Wildlife.UI.Journal.TrailState." + value);

        internal static string StageLabel(string value)
        {
            if (value == WildlifeKnowledgeAdapter.StageSighted) return Get("Wildlife.UI.Journal.Stage.Sighted");
            if (value == WildlifeKnowledgeAdapter.StageIdentified) return Get("Wildlife.UI.Journal.Stage.Identified");
            if (value == WildlifeKnowledgeAdapter.StageStudied) return Get("Wildlife.UI.Journal.Stage.Studied");
            if (value == WildlifeKnowledgeAdapter.StageUnderstood) return Get("Wildlife.UI.Journal.Stage.Understood");
            if (value == WildlifeKnowledgeAdapter.StageDocumented) return Get("Wildlife.UI.Journal.Stage.Documented");
            return Get("Wildlife.UI.Journal.Stage.Unknown");
        }

        internal static string FacetLabel(string value)
        {
            if (value == WildlifeKnowledgeAdapter.FacetIdentity) return Get("Wildlife.UI.Journal.Facet.Identity");
            if (value == WildlifeKnowledgeAdapter.FacetHabitat) return Get("Wildlife.UI.Journal.Facet.Habitat");
            if (value == WildlifeKnowledgeAdapter.FacetMovement) return Get("Wildlife.UI.Journal.Facet.Movement");
            if (value == WildlifeKnowledgeAdapter.FacetSocial) return Get("Wildlife.UI.Journal.Facet.Social");
            if (value == WildlifeKnowledgeAdapter.FacetSignals) return Get("Wildlife.UI.Journal.Facet.Signals");
            if (value == WildlifeKnowledgeAdapter.FacetDanger) return Get("Wildlife.UI.Journal.Facet.Danger");
            if (value == WildlifeKnowledgeAdapter.FacetPopulation) return Get("Wildlife.UI.Journal.Facet.Population");
            return Get("Wildlife.UI.Journal.Meta.Facet");
        }

        internal static string ClaimLabel(string value)
        {
            if (value == "population-estimate") return Get("Wildlife.UI.Journal.Claim.PopulationEstimate");
            if (value == "movement-direction") return Get("Wildlife.UI.Journal.Claim.MovementDirection");
            if (value == "habitat-quality") return Get("Wildlife.UI.Journal.Claim.HabitatQuality");
            if (value == "signal-meaning") return Get("Wildlife.UI.Journal.Claim.SignalMeaning");
            if (value == "predator-pressure") return Get("Wildlife.UI.Journal.Claim.PredatorPressure");
            if (value == "danger-level") return Get("Wildlife.UI.Journal.Claim.DangerLevel");
            if (value == "diet-preference") return Get("Wildlife.UI.Journal.Claim.DietPreference");
            if (value == "den-location") return Get("Wildlife.UI.Journal.Claim.DenLocation");
            if (value == "social-pattern") return Get("Wildlife.UI.Journal.Claim.SocialPattern");
            if (value == "policy-outcome") return Get("Wildlife.UI.Journal.Claim.PolicyOutcome");
            return Get("Wildlife.UI.Journal.Knowledge.Claim");
        }

        internal static string Certainty(float confidence, bool success = true) =>
            confidence >= 0.75f ? Get("Wildlife.UI.Journal.Certainty.Confirmed") :
            confidence >= 0.45f ? Get("Wildlife.UI.Journal.Certainty.Probable") :
            success ? Get("Wildlife.UI.Journal.Certainty.Uncertain") : Get("Wildlife.UI.Journal.Certainty.Contradictory");

        internal static string Age(int tick, int now)
        {
            if (tick <= 0) return Get("Wildlife.UI.Journal.Time.Recent");
            return Mathf.Max(0, now - tick).ToStringTicksToPeriod() + " " + Get("Wildlife.UI.Journal.Time.Ago");
        }

        internal static string OutcomeTitle(string category) =>
            category.NullOrEmpty() ? Get("Wildlife.UI.Journal.Outcome.Title") :
            Get("Wildlife.UI.Journal.Outcome.Recorded");
    }

    internal sealed class WildlifeJournalPresentation
    {
        private static readonly WildlifeJournalPage[] Pages =
        {
            WildlifeJournalPage.FieldLog, WildlifeJournalPage.Knowledge,
            WildlifeJournalPage.Region, WildlifeJournalPage.Chronicle,
            WildlifeJournalPage.FieldGuide, WildlifeJournalPage.LivingAtlas,
            WildlifeJournalPage.Signals, WildlifeJournalPage.Investigations,
            WildlifeJournalPage.Expeditions, WildlifeJournalPage.Stories
        };

        private static readonly string[] KnowledgeFacets =
        {
            WildlifeKnowledgeAdapter.FacetIdentity, WildlifeKnowledgeAdapter.FacetHabitat,
            WildlifeKnowledgeAdapter.FacetMovement, WildlifeKnowledgeAdapter.FacetSocial,
            WildlifeKnowledgeAdapter.FacetSignals, WildlifeKnowledgeAdapter.FacetDanger,
            WildlifeKnowledgeAdapter.FacetPopulation
        };

        private readonly Map map;
        private WildlifeJournalPage page;
        private ThingDef selectedSpecies;
        private Pawn signalObserver;
        private readonly int focusedFieldLogTick;
        private string search = string.Empty;
        private string selectedLogId;
        private string selectedSignalId;
        private string selectedInvestigationId;
        private string selectedStoryId;
        private int fieldLogTypeFilter;
        private int fieldLogUrgencyFilter;
        private int snapshotBucket = -1;
        private int snapshotKnowledgeRevision = -1;
        private int snapshotObserverId = -1;
        private WildlifeJournalPresentationSnapshot snapshot;

        internal WildlifeJournalPresentation(Map map, WildlifeJournalPage page = WildlifeJournalPage.FieldLog,
            ThingDef selectedSpecies = null, Pawn signalObserver = null, int focusedFieldLogTick = -1)
        {
            this.map = map ?? Find.CurrentMap;
            this.page = page;
            this.selectedSpecies = selectedSpecies;
            this.signalObserver = signalObserver;
            this.focusedFieldLogTick = focusedFieldLogTick;
            Document = new InsightUiDocument("wildlife-journal", InsightUi.Empty("wildlife-journal-root"));
            WildlifeUiTheme.Configure(Document, false, false);
            Host = new InsightUiHost(Document);
            Window = new InsightUiWindow(JournalText.Get("Wildlife.UI.Journal.Title"), Document);
            Document.Root = BuildRoot();
        }

        internal InsightUiDocument Document { get; private set; }
        internal InsightUiHost Host { get; private set; }
        internal InsightUiWindow Window { get; private set; }

        internal InsightUiElement BuildRoot()
        {
            EnsureSnapshot(false);
            string pageId = PageId(page);
            InsightUiElement header = InsightUi.Column("journal.header",
                InsightUi.Breadcrumbs("journal.breadcrumbs", JournalText.Get("Wildlife.UI.Journal.Breadcrumb"), JournalText.PageLabel(page)),
                InsightUi.SectionHeader("journal.title", JournalText.Get("Wildlife.UI.Journal.Title"),
                    JournalText.Get("Wildlife.UI.Journal.Subtitle"), null, HeaderActions(), true),
                InsightUi.SearchField("journal.search", search, JournalText.Get("Wildlife.UI.Journal.SearchPlaceholder"),
                    _ => Rebuild()).Bind(() => search, value => search = value ?? string.Empty)
            ).SetGap(6f);

            InsightUiNavigation navigation = InsightUi.Navigation("journal.navigation", 720f)
                .Bind(() => PageId(page), value => SelectPage(value));
            for (int i = 0; i < Pages.Length; i++)
            {
                WildlifeJournalPage value = Pages[i];
                navigation.Add(PageId(value), JournalText.PageLabel(value), BuildPage(value));
            }
            if (!Pages.Any(value => PageId(value) == pageId)) page = WildlifeJournalPage.FieldLog;
            InsightUiElement root = InsightUi.Column("journal.root", header, navigation)
                .SetGap(10f).SetPadding(8f);
            return root;
        }

        private InsightUiElement HeaderActions()
        {
            return InsightUi.Row("journal.header-actions").SetGap(5f)
                .Add(InsightUi.Badge("journal.snapshot-status", snapshot == null
                    ? JournalText.Get("Wildlife.UI.Journal.Snapshot.Waiting")
                    : JournalText.Get("Wildlife.UI.Journal.Snapshot.Live")),
                    InsightUi.Button("journal.refresh", JournalText.Get("Wildlife.UI.Journal.Action.Refresh"),
                        () =>
                        {
                            snapshotBucket = -1;
                            Rebuild();
                        }));
        }

        private InsightUiElement BuildPage(WildlifeJournalPage value)
        {
            InsightUiElement content;
            switch (value)
            {
                case WildlifeJournalPage.FieldLog: content = BuildFieldLog(); break;
                case WildlifeJournalPage.Knowledge: content = BuildSpeciesPage(value, true); break;
                case WildlifeJournalPage.FieldGuide: content = BuildSpeciesPage(value, false); break;
                case WildlifeJournalPage.LivingAtlas: content = BuildRegionPage(true); break;
                case WildlifeJournalPage.Region: content = BuildRegionPage(false); break;
                case WildlifeJournalPage.Signals: content = BuildSignals(); break;
                case WildlifeJournalPage.Investigations: content = BuildInvestigations(); break;
                case WildlifeJournalPage.Expeditions: content = BuildExpeditions(); break;
                case WildlifeJournalPage.Stories: content = BuildStories(); break;
                default: content = BuildChronicle(); break;
            }
            InsightUiElement header = InsightUi.SectionHeader("journal.page." + PageId(value) + ".header",
                JournalText.PageLabel(value), JournalText.PageDescription(value));
            return InsightUi.Scroll("journal.page." + PageId(value) + ".scroll",
                InsightUi.Column("journal.page." + PageId(value) + ".content", header, content)
                    .SetGap(8f).SetPadding(2f));
        }

        private InsightUiElement BuildFieldLog()
        {
            List<JournalLogRecord> records = snapshot.FieldLog.Where(Matches)
                .Where(FieldLogTypeMatches).Where(FieldLogUrgencyMatches).ToList();
            JournalLogRecord selected = records.FirstOrDefault(value => value.Id == selectedLogId) ?? records.FirstOrDefault();
            selectedLogId = selected?.Id;
            InsightUiSelect typeFilter = InsightUi.Select("journal-field-log.type-filter",
                JournalText.Get("Wildlife.UI.Journal.FieldLog.Filter.Type"), FieldLogTypeOptions(), fieldLogTypeFilter)
                .Bind(() => fieldLogTypeFilter, value =>
                {
                    if (fieldLogTypeFilter == value) return;
                    fieldLogTypeFilter = value;
                    Rebuild();
                });
            InsightUiSelect urgencyFilter = InsightUi.Select("journal-field-log.urgency-filter",
                JournalText.Get("Wildlife.UI.Journal.FieldLog.Filter.Urgency"), FieldLogUrgencyOptions(), fieldLogUrgencyFilter)
                .Bind(() => fieldLogUrgencyFilter, value =>
                {
                    if (fieldLogUrgencyFilter == value) return;
                    fieldLogUrgencyFilter = value;
                    Rebuild();
                });
            InsightUiElement filters = InsightUi.Surface("journal-field-log.filters",
                InsightUi.Row("journal-field-log.filters.content").SetGap(6f).Add(typeFilter, urgencyFilter));
            bool filtered = !search.NullOrEmpty() || fieldLogTypeFilter != 0 || fieldLogUrgencyFilter != 0;
            InsightUiElement list = records.Count == 0
                ? Empty(filtered ? "Wildlife.UI.Journal.FieldLog.NoResults" : "Wildlife.UI.Journal.FieldLog.Empty",
                    filtered ? "Wildlife.UI.Journal.FieldLog.NoResults.Help" : "Wildlife.UI.Journal.FieldLog.Empty.Help")
                : BuildVirtualList("journal-field-log-items", records, 96f, BuildLogRow);
            InsightUiElement detail = selected == null ? Empty("Wildlife.UI.Journal.Selection.Empty", "Wildlife.UI.Journal.Selection.Empty.Help") : BuildLogDetail(selected);
            return InsightUi.Column("journal.field-log.content", filters,
                ResponsiveSplit("journal.field-log.split", list, detail, 0.56f)).SetGap(7f);
        }

        private string[] FieldLogTypeOptions() => new[]
        {
            JournalText.Get("Wildlife.UI.Journal.FieldLog.Filter.All"),
            JournalText.Get("Wildlife.UI.Journal.Type.Moment"),
            JournalText.Get("Wildlife.UI.Journal.Type.Signal"),
            JournalText.Get("Wildlife.UI.Journal.Type.Trail"),
            JournalText.Get("Wildlife.UI.Journal.Type.Investigation"),
            JournalText.Get("Wildlife.UI.Journal.Type.Expedition"),
            JournalText.Get("Wildlife.UI.Journal.Type.Outcome")
        };

        private string[] FieldLogUrgencyOptions() => new[]
        {
            JournalText.Get("Wildlife.UI.Journal.FieldLog.Filter.All"),
            JournalText.Get("Wildlife.UI.Journal.Urgency.Urgent"),
            JournalText.Get("Wildlife.UI.Journal.Urgency.Routine")
        };

        private bool FieldLogTypeMatches(JournalLogRecord value)
        {
            if (fieldLogTypeFilter <= 0) return true;
            string[] values = { "Moment", "Signal", "Trail", "Investigation", "Expedition", "Outcome" };
            return fieldLogTypeFilter <= values.Length && value.TypeKey.EndsWith("." + values[fieldLogTypeFilter - 1], StringComparison.Ordinal);
        }

        private bool FieldLogUrgencyMatches(JournalLogRecord value) => fieldLogUrgencyFilter == 0 ||
            (fieldLogUrgencyFilter == 1 && value.Urgent) || (fieldLogUrgencyFilter == 2 && !value.Urgent);

        private InsightUiElement BuildLogRow(JournalLogRecord value, int index)
        {
            InsightUiElement body = InsightUi.Column("journal.field-log.row." + value.Id,
                InsightUi.Row("journal.field-log.row." + value.Id + ".heading").SetGap(6f)
                    .Add(InsightUi.Badge("journal.field-log.row." + value.Id + ".type", JournalText.Get(value.TypeKey)),
                        InsightUi.Label("journal.field-log.row." + value.Id + ".title", value.Title)),
                InsightUi.Label("journal.field-log.row." + value.Id + ".detail", value.Detail, InsightUiTextStyle.Caption),
                InsightUi.Row("journal.field-log.row." + value.Id + ".meta").SetGap(8f)
                    .Add(InsightUi.StatRow("journal.field-log.row." + value.Id + ".species", JournalText.Get("Wildlife.UI.Journal.Meta.Species"), value.SpeciesName),
                        InsightUi.StatRow("journal.field-log.row." + value.Id + ".certainty", JournalText.Get("Wildlife.UI.Journal.Meta.Certainty"), value.Certainty),
                        InsightUi.StatRow("journal.field-log.row." + value.Id + ".urgency", JournalText.Get("Wildlife.UI.Journal.Meta.Urgency"),
                            value.Urgent ? JournalText.Get("Wildlife.UI.Journal.Urgency.Urgent") : JournalText.Get("Wildlife.UI.Journal.Urgency.Routine")),
                        InsightUi.StatRow("journal.field-log.row." + value.Id + ".age", JournalText.Get("Wildlife.UI.Journal.Meta.Time"), value.Age))
            ).SetGap(5f);
            if (value.Urgent) body = InsightUi.Column("journal.field-log.row." + value.Id + ".urgent",
                InsightUi.Callout("journal.field-log.row." + value.Id + ".attention", InsightUiCalloutSeverity.Warning,
                    JournalText.Get("Wildlife.UI.Journal.Attention")), body).SetGap(4f);
            InsightUiButton select = InsightUi.Button("journal.field-log.row." + value.Id + ".select",
                JournalText.Get("Wildlife.UI.Journal.Action.Inspect"), () =>
                {
                    selectedLogId = value.Id;
                    Rebuild();
                });
            select.Selected = value.Id == selectedLogId;
            return InsightUi.Surface("journal.field-log.card." + value.Id,
                InsightUi.Column("journal.field-log.card." + value.Id + ".content", body, select).SetGap(6f));
        }

        private InsightUiElement BuildLogDetail(JournalLogRecord value)
        {
            List<InsightUiElement> elements = new List<InsightUiElement>
            {
                InsightUi.SectionHeader("journal.field-log.detail.header", value.Title, value.Detail),
                InsightUi.StatRow("journal.field-log.detail.type", JournalText.Get("Wildlife.UI.Journal.Meta.Type"), JournalText.Get(value.TypeKey)),
                InsightUi.StatRow("journal.field-log.detail.species", JournalText.Get("Wildlife.UI.Journal.Meta.Species"), value.SpeciesName),
                InsightUi.StatRow("journal.field-log.detail.location", JournalText.Get("Wildlife.UI.Journal.Meta.Location"), value.Location),
                InsightUi.StatRow("journal.field-log.detail.certainty", JournalText.Get("Wildlife.UI.Journal.Meta.Certainty"), value.Certainty),
                InsightUi.StatRow("journal.field-log.detail.time", JournalText.Get("Wildlife.UI.Journal.Meta.Time"), value.Age),
                InsightUi.Callout("journal.field-log.detail.evidence", value.Urgent ? InsightUiCalloutSeverity.Warning : InsightUiCalloutSeverity.Info,
                    JournalText.Get("Wildlife.UI.Journal.Evidence"), value.Evidence)
            };
            if (value.Cell.IsValid || value.Species != null)
                elements.Add(InsightUi.Button("journal.field-log.detail.focus", JournalText.Get("Wildlife.UI.Journal.Action.FocusMap"),
                    () => Focus(value.Cell, value.Species)));
            if (value.Action == JournalAction.Response)
                elements.Add(InsightUi.Button("journal.field-log.detail.response", JournalText.Get("Wildlife.UI.Journal.Action.ChooseResponse"),
                    () => ChooseMomentResponse()));
            return InsightUi.Surface("journal.field-log.detail.surface",
                InsightUi.Column("journal.field-log.detail.content", elements.ToArray()).SetGap(8f));
        }

        private InsightUiElement BuildSpeciesPage(WildlifeJournalPage value, bool knowledge)
        {
            List<JournalSpeciesRecord> records = snapshot.Species
                .Where(item => !knowledge || item.KnowledgeVisible)
                .Where(Matches).ToList();
            JournalSpeciesRecord selected = records.FirstOrDefault(item => item.Species == selectedSpecies) ?? records.FirstOrDefault();
            if (selected != null) selectedSpecies = selected.Species;
            InsightUiElement list = records.Count == 0
                ? Empty(knowledge ? "Wildlife.UI.Journal.Knowledge.Empty" : "Wildlife.UI.Journal.FieldGuide.Empty",
                    knowledge ? "Wildlife.UI.Journal.Knowledge.Empty.Help" : "Wildlife.UI.Journal.FieldGuide.Empty.Help")
                : BuildVirtualList("journal." + PageId(value) + "-species-items", records, 78f,
                    (item, index) => BuildSpeciesRow(value, item, index));
            InsightUiElement detail = selected == null ? Empty("Wildlife.UI.Journal.Selection.Empty", "Wildlife.UI.Journal.Selection.Empty.Help") :
                BuildSpeciesDetail(value, selected, knowledge);
            return ResponsiveSplit("journal." + PageId(value) + ".split", list, detail, 0.38f);
        }

        private InsightUiElement BuildSpeciesRow(WildlifeJournalPage pageValue, JournalSpeciesRecord value, int index)
        {
            string id = "journal." + PageId(pageValue) + ".species." + value.Species.defName;
            InsightUiButton select = InsightUi.Button(id + ".select", value.Species.LabelCap.ToString(), () =>
            {
                selectedSpecies = value.Species;
                Rebuild();
            });
            select.Selected = value.Species == selectedSpecies;
            return InsightUi.Surface(id, InsightUi.Column(id + ".content", select,
                InsightUi.Row(id + ".meta").SetGap(6f)
                    .Add(InsightUi.Badge(id + ".stage", pageValue == WildlifeJournalPage.FieldGuide ? value.FieldGuideStageLabel : value.StageLabel),
                        InsightUi.Label(id + ".evidence", JournalText.Format("Wildlife.UI.Journal.Evidence.Count",
                            pageValue == WildlifeJournalPage.FieldGuide ? value.FieldGuideEvidenceCount : value.EvidenceCount), InsightUiTextStyle.Caption)))
                .SetGap(4f));
        }

        private InsightUiElement BuildSpeciesDetail(WildlifeJournalPage pageValue, JournalSpeciesRecord value, bool knowledge)
        {
            string prefix = "journal." + PageId(pageValue) + ".detail." + value.Species.defName;
            List<InsightUiElement> overviewChildren = new List<InsightUiElement>
            {
                InsightUi.SectionHeader(prefix + ".identity", value.Species.LabelCap.ToString(),
                    knowledge ? JournalText.Get("Wildlife.UI.Journal.Knowledge.IdentityHelp") :
                    JournalText.Get("Wildlife.UI.Journal.FieldGuide.IdentityHelp")),
                InsightUi.StatRow(prefix + ".stage", JournalText.Get("Wildlife.UI.Journal.Meta.Stage"),
                    knowledge || value.FieldGuideObserved ? value.StageLabel : JournalText.StageLabel(WildlifeKnowledgeAdapter.StageUnknown)),
                InsightUi.StatRow(prefix + ".confidence", JournalText.Get("Wildlife.UI.Journal.Meta.Confidence"),
                    knowledge || value.FieldGuideObserved ? value.ConfidenceLabel : JournalText.Get("Wildlife.UI.Journal.Value.Unknown")),
                InsightUi.StatRow(prefix + ".observations", JournalText.Get("Wildlife.UI.Journal.Meta.Evidence"),
                    (knowledge || value.FieldGuideObserved ? value.EvidenceCount : 0).ToString()),
                InsightUi.StatRow(prefix + ".facets", JournalText.Get("Wildlife.UI.Journal.Meta.Facets"),
                    (knowledge || value.FieldGuideObserved ? value.KnownFacetCount : 0) + "/" + value.Facets.Count)
            };
            if (knowledge)
            {
                InsightUiTabs tabs = InsightUi.Tabs(prefix + ".tabs");
                tabs.Add("claims", JournalText.Get("Wildlife.UI.Journal.Knowledge.Claims"), BuildClaims(prefix, value));
                tabs.Add("facets", JournalText.Get("Wildlife.UI.Journal.Knowledge.Facets"), BuildFacets(prefix, value));
                tabs.Add("evidence", JournalText.Get("Wildlife.UI.Journal.Knowledge.Evidence"), BuildEvidence(prefix, value));
                overviewChildren.Add(tabs);
            }
            else if (!value.FieldGuideObserved)
            {
                overviewChildren.Add(InsightUi.Callout(prefix + ".observation-gated", InsightUiCalloutSeverity.Info,
                    JournalText.Get("Wildlife.UI.Journal.FieldGuide.ObservationGated"),
                    JournalText.Get("Wildlife.UI.Journal.FieldGuide.ObservationGated.Help")));
                overviewChildren.Add(BuildUnknownFacets(prefix, value));
            }
            else
            {
                overviewChildren.Add(BuildFacets(prefix, value));
                overviewChildren.Add(BuildEvidence(prefix, value));
            }
            overviewChildren.Add(InsightUi.Callout(prefix + ".next-evidence", InsightUiCalloutSeverity.Info,
                JournalText.Get("Wildlife.UI.Journal.Knowledge.NextEvidence"), value.NextEvidence));
            overviewChildren.Add(InsightUi.Button(prefix + ".focus", JournalText.Get("Wildlife.UI.Journal.Action.FocusMap"),
                () => Focus(IntVec3.Invalid, value.Species)));
            InsightUiElement overview = InsightUi.Column(prefix + ".overview", overviewChildren.ToArray()).SetGap(7f);
            return InsightUi.Surface(prefix + ".surface", overview);
        }

        private InsightUiElement BuildClaims(string prefix, JournalSpeciesRecord value)
        {
            if (value.Claims.Count == 0)
                return Empty(prefix + ".claims.empty", "Wildlife.UI.Journal.Knowledge.Claims.Empty",
                    "Wildlife.UI.Journal.Knowledge.Claims.Empty");
            return InsightUi.Column(prefix + ".claims", value.Claims.Select((claim, index) =>
                InsightUi.Callout(prefix + ".claim." + index, claim.Contradictory ? InsightUiCalloutSeverity.Warning : InsightUiCalloutSeverity.Info,
                    claim.Label, claim.Detail)).ToArray()).SetGap(5f);
        }

        private InsightUiElement BuildFacets(string prefix, JournalSpeciesRecord value)
        {
            return InsightUi.Column(prefix + ".facets", value.Facets.Select((facet, index) =>
                InsightUi.Surface(prefix + ".facet." + index, InsightUi.Column(prefix + ".facet." + index + ".content",
                    InsightUi.StatRow(prefix + ".facet." + index + ".name", JournalText.Get("Wildlife.UI.Journal.Meta.Facet"), facet.Label),
                    InsightUi.StatRow(prefix + ".facet." + index + ".state", JournalText.Get("Wildlife.UI.Journal.Meta.State"), facet.Known ?
                        JournalText.Get("Wildlife.UI.Journal.Value.Known") : JournalText.Get("Wildlife.UI.Journal.Value.Unknown")),
                    InsightUi.StatRow(prefix + ".facet." + index + ".confidence", JournalText.Get("Wildlife.UI.Journal.Meta.Confidence"), facet.ConfidenceLabel),
                    InsightUi.StatRow(prefix + ".facet." + index + ".evidence", JournalText.Get("Wildlife.UI.Journal.Meta.Evidence"), facet.EvidenceCount.ToString()))
                    .SetGap(4f))).ToArray()).SetGap(5f);
        }

        private InsightUiElement BuildUnknownFacets(string prefix, JournalSpeciesRecord value)
        {
            return InsightUi.Column(prefix + ".unknown-facets", value.Facets.Select((facet, index) =>
                InsightUi.StatRow(prefix + ".unknown-facet." + index,
                    JournalText.Get("Wildlife.UI.Journal.Meta.Facet"), facet.Label + " • " +
                    JournalText.Get("Wildlife.UI.Journal.Value.Unknown"))).ToArray()).SetGap(5f);
        }

        private InsightUiElement BuildEvidence(string prefix, JournalSpeciesRecord value)
        {
            if (value.Evidence.Count == 0)
                return Empty(prefix + ".evidence.empty", "Wildlife.UI.Journal.Knowledge.Evidence.Empty",
                    "Wildlife.UI.Journal.Knowledge.Evidence.Empty");
            return BuildVirtualList(prefix + ".evidence-list", value.Evidence, 72f, (item, index) =>
                InsightUi.Surface(prefix + ".evidence." + index, InsightUi.Column(prefix + ".evidence." + index + ".content",
                    InsightUi.Label(prefix + ".evidence." + index + ".summary", item.Summary),
                    InsightUi.Row(prefix + ".evidence." + index + ".meta").SetGap(7f)
                        .Add(InsightUi.StatRow(prefix + ".evidence." + index + ".source", JournalText.Get("Wildlife.UI.Journal.Meta.Source"), item.Source),
                            InsightUi.StatRow(prefix + ".evidence." + index + ".confidence", JournalText.Get("Wildlife.UI.Journal.Meta.Confidence"), item.ConfidenceLabel),
                            InsightUi.StatRow(prefix + ".evidence." + index + ".observers", JournalText.Get("Wildlife.UI.Journal.Meta.Observers"), item.ObserverCount.ToString())))
                    .SetGap(3f)));
        }

        private InsightUiElement BuildRegionPage(bool atlas)
        {
            List<JournalRegionRecord> records = snapshot.Regions.Where(Matches).ToList();
            JournalRegionRecord selected = records.FirstOrDefault(item => item.Species == selectedSpecies) ?? records.FirstOrDefault();
            if (selected != null) selectedSpecies = selected.Species;
            string pageId = atlas ? "living-atlas" : "region";
            string prefix = "journal." + pageId;
            InsightUiElement list = records.Count == 0 ? Empty("Wildlife.UI.Journal.Region.Empty", "Wildlife.UI.Journal.Region.Empty.Help") :
                BuildVirtualList(prefix + "-items", records, 82f, (item, index) => BuildRegionRow(prefix, item, index, atlas));
            InsightUiElement detail = selected == null ? Empty("journal." + pageId + ".detail.empty",
                "Wildlife.UI.Journal.Selection.Empty", "Wildlife.UI.Journal.Region.Empty.Help") :
                BuildRegionDetail(prefix, selected, atlas);
            return ResponsiveSplit(prefix + ".split", list, detail, 0.46f);
        }

        private InsightUiElement BuildRegionDetail(string prefix, JournalRegionRecord selected, bool atlas)
        {
            List<InsightUiElement> details = new List<InsightUiElement>
            {
                InsightUi.SectionHeader(prefix + ".detail.header", selected.Species.LabelCap.ToString(),
                    atlas ? selected.AtlasSummary : selected.Summary),
                InsightUi.StatRow(prefix + ".detail.local", JournalText.Get("Wildlife.UI.Journal.Region.Local"), selected.LocalPopulation),
                InsightUi.StatRow(prefix + ".detail.regional", JournalText.Get("Wildlife.UI.Journal.Region.Regional"), selected.RegionalPopulation),
                InsightUi.StatRow(prefix + ".detail.trend", JournalText.Get("Wildlife.UI.Journal.Region.Trend"), selected.Trend),
                InsightUi.StatRow(prefix + ".detail.pressure", JournalText.Get("Wildlife.UI.Journal.Region.Pressure"), selected.Pressure),
                InsightUi.StatRow(prefix + ".detail.habitat", JournalText.Get("Wildlife.UI.Journal.Region.Habitat"), selected.Habitat),
                InsightUi.StatRow(prefix + ".detail.movement", JournalText.Get("Wildlife.UI.Journal.Region.Movement"), selected.Movement)
            };
            if (atlas)
            {
                details.Add(InsightUi.StatRow(prefix + ".detail.evidence", JournalText.Get("Wildlife.UI.Journal.Meta.Evidence"),
                    selected.KnownEvidenceCount.ToString()));
                details.Add(InsightUi.StatRow(prefix + ".detail.trails", JournalText.Get("Wildlife.UI.Journal.Region.Trails"),
                    selected.TrailCount.ToString()));
                details.Add(InsightUi.StatRow(prefix + ".detail.migrations", JournalText.Get("Wildlife.UI.Journal.Region.Migrations"),
                    selected.MigrationCount.ToString()));
                details.Add(InsightUi.StatRow(prefix + ".detail.notable", JournalText.Get("Wildlife.UI.Journal.Region.Notable"),
                    selected.NotableCount.ToString()));
                details.Add(InsightUi.Callout(prefix + ".detail.events", InsightUiCalloutSeverity.Info,
                    JournalText.Get("Wildlife.UI.Journal.LivingAtlas.Evidence"), selected.AtlasSummary));
            }
            else
            {
                details.Add(InsightUi.StatRow(prefix + ".detail.variations", JournalText.Get("Wildlife.UI.Journal.Region.Variations"),
                    selected.VariationCount.ToString()));
                details.Add(InsightUi.Callout(prefix + ".detail.summary", InsightUiCalloutSeverity.Info,
                    JournalText.Get("Wildlife.UI.Journal.Region.SummaryLabel"), selected.Summary));
            }
            details.Add(InsightUi.Button(prefix + ".detail.focus", JournalText.Get("Wildlife.UI.Journal.Action.FocusMap"),
                () => Focus(IntVec3.Invalid, selected.Species)));
            return InsightUi.Surface(prefix + ".detail", InsightUi.Column(prefix + ".detail.content", details.ToArray()).SetGap(7f));
        }

        private InsightUiElement BuildRegionRow(string prefix, JournalRegionRecord value, int index, bool atlas)
        {
            string id = prefix + ".row." + value.Species.defName;
            InsightUiButton select = InsightUi.Button(id + ".select", value.Species.LabelCap.ToString(), () =>
            {
                selectedSpecies = value.Species;
                Rebuild();
            });
            select.Selected = selectedSpecies == value.Species;
            return InsightUi.Surface(id, InsightUi.Column(id + ".content", select,
                InsightUi.Label(id + ".summary", atlas ? value.AtlasSummary : value.Summary, InsightUiTextStyle.Caption),
                atlas ? InsightUi.StatRow(id + ".movement", JournalText.Get("Wildlife.UI.Journal.Region.Movement"), value.Movement) :
                    InsightUi.StatRow(id + ".population", JournalText.Get("Wildlife.UI.Journal.Region.Local"), value.LocalPopulation),
                InsightUi.Meter(id + ".pressure", value.PressureValue, 1f)).SetGap(4f));
        }

        private InsightUiElement BuildSignals()
        {
            if (!WildlifeJournal.SignalsVisible())
                return InsightUi.Callout("journal.signals.disabled", InsightUiCalloutSeverity.Warning,
                    JournalText.Get("Wildlife.UI.Journal.Signals.Disabled"), JournalText.Get("Wildlife.UI.Journal.Signals.Disabled.Help"));
            List<JournalSignalRecord> records = snapshot.Signals.Where(Matches).ToList();
            JournalSignalRecord selected = records.FirstOrDefault(item => item.Id == selectedSignalId) ?? records.FirstOrDefault();
            selectedSignalId = selected?.Id;
            InsightUiElement list = records.Count == 0 ? Empty("Wildlife.UI.Journal.Signals.Empty", "Wildlife.UI.Journal.Signals.Empty.Help") :
                BuildVirtualList("journal-signals-items", records, 86f, BuildSignalRow);
            InsightUiElement detail = selected == null ? Empty("journal.signals.detail.empty",
                "Wildlife.UI.Journal.Selection.Empty", "Wildlife.UI.Journal.Selection.Empty.Help") :
                InsightUi.Surface("journal.signals.detail", InsightUi.Column("journal.signals.detail.content",
                    InsightUi.SectionHeader("journal.signals.detail.header", selected.SpeciesName, selected.Description),
                    InsightUi.StatRow("journal.signals.detail.family", JournalText.Get("Wildlife.UI.Journal.Signals.Family"), selected.Family),
                    InsightUi.StatRow("journal.signals.detail.observer", JournalText.Get("Wildlife.UI.Journal.Signals.Observer"), selected.ObserverName),
                    InsightUi.StatRow("journal.signals.detail.interpretation", JournalText.Get("Wildlife.UI.Journal.Signals.Interpretation"), selected.Interpretation),
                    InsightUi.StatRow("journal.signals.detail.confidence", JournalText.Get("Wildlife.UI.Journal.Meta.Confidence"), selected.ConfidenceLabel),
                    InsightUi.StatRow("journal.signals.detail.contradiction", JournalText.Get("Wildlife.UI.Journal.Signals.Contradictions"), selected.Contradictory ?
                        JournalText.Get("Wildlife.UI.Journal.Value.Present") : JournalText.Get("Wildlife.UI.Journal.Value.None")),
                    InsightUi.Callout("journal.signals.detail.evidence", selected.Contradictory ? InsightUiCalloutSeverity.Warning : InsightUiCalloutSeverity.Info,
                        JournalText.Get("Wildlife.UI.Journal.Evidence"), selected.Evidence),
                    InsightUi.Button("journal.signals.detail.focus", JournalText.Get("Wildlife.UI.Journal.Action.FocusMap"),
                        () => Focus(selected.Cell, selected.Species))).SetGap(7f));
            return ResponsiveSplit("journal.signals.split", list, detail, 0.45f);
        }

        private InsightUiElement BuildSignalRow(JournalSignalRecord value, int index)
        {
            string id = "journal.signals.row." + value.Id;
            InsightUiButton select = InsightUi.Button(id + ".select", value.SpeciesName, () =>
            {
                selectedSignalId = value.Id;
                Rebuild();
            });
            select.Selected = selectedSignalId == value.Id;
            return InsightUi.Surface(id, InsightUi.Column(id + ".content", InsightUi.Row(id + ".heading").SetGap(6f)
                    .Add(InsightUi.Badge(id + ".kind", value.Family), select),
                InsightUi.Label(id + ".description", value.Description, InsightUiTextStyle.Caption),
                InsightUi.StatRow(id + ".certainty", JournalText.Get("Wildlife.UI.Journal.Meta.Certainty"), value.ConfidenceLabel)).SetGap(4f));
        }

        private InsightUiElement BuildInvestigations()
        {
            List<JournalInvestigationRecord> records = snapshot.Investigations.Where(Matches).ToList();
            JournalInvestigationRecord selected = records.FirstOrDefault(item => item.Id == selectedInvestigationId) ?? records.FirstOrDefault();
            selectedInvestigationId = selected?.Id;
            InsightUiElement list = records.Count == 0 ? Empty("Wildlife.UI.Journal.Investigations.Empty", "Wildlife.UI.Journal.Investigations.Empty.Help") :
                BuildVirtualList("journal-investigation-items", records, 92f, BuildInvestigationRow);
            InsightUiElement detail = selected == null ? Empty("journal.investigations.detail.empty",
                "Wildlife.UI.Journal.Selection.Empty", "Wildlife.UI.Journal.Selection.Empty.Help") :
                InsightUi.Surface("journal.investigations.detail", InsightUi.Column("journal.investigations.detail.content",
                    InsightUi.SectionHeader("journal.investigations.detail.header", selected.Title, selected.Hypothesis),
                    InsightUi.Meter("journal.investigations.detail.progress", selected.Progress, 1f),
                    InsightUi.StatRow("journal.investigations.detail.state", JournalText.Get("Wildlife.UI.Journal.Meta.State"), selected.State),
                    InsightUi.StatRow("journal.investigations.detail.uncertainty", JournalText.Get("Wildlife.UI.Journal.Investigations.Uncertainty"), selected.Uncertainty),
                    InsightUi.Callout("journal.investigations.detail.evidence", selected.Contradictory ? InsightUiCalloutSeverity.Warning : InsightUiCalloutSeverity.Info,
                        JournalText.Get("Wildlife.UI.Journal.Evidence"), selected.Evidence),
                    InsightUi.StatRow("journal.investigations.detail.next", JournalText.Get("Wildlife.UI.Journal.Investigations.Next"), selected.NextAction),
                    InsightUi.Button("journal.investigations.detail.focus", JournalText.Get("Wildlife.UI.Journal.Action.FocusMap"),
                        () => Focus(selected.Cell, selected.Species))).SetGap(7f));
            return ResponsiveSplit("journal.investigations.split", list, detail, 0.48f);
        }

        private InsightUiElement BuildInvestigationRow(JournalInvestigationRecord value, int index)
        {
            string id = "journal.investigations.row." + value.Id;
            InsightUiButton select = InsightUi.Button(id + ".select", value.Title, () =>
            {
                selectedInvestigationId = value.Id;
                Rebuild();
            });
            select.Selected = selectedInvestigationId == value.Id;
            return InsightUi.Surface(id, InsightUi.Column(id + ".content", select,
                InsightUi.Label(id + ".hypothesis", value.Hypothesis, InsightUiTextStyle.Caption),
                InsightUi.Meter(id + ".progress", value.Progress, 1f),
                InsightUi.Badge(id + ".state", value.State)).SetGap(4f));
        }

        private InsightUiElement BuildExpeditions()
        {
            if (!WildlifeJournal.ExpeditionsVisible())
                return InsightUi.Callout("journal.expeditions.disabled", InsightUiCalloutSeverity.Info,
                    JournalText.Get("Wildlife.UI.Journal.Expeditions.Disabled"), JournalText.Get("Wildlife.UI.Journal.Expeditions.Disabled.Help"));
            List<JournalExpeditionRecord> records = snapshot.Expeditions.Where(Matches).ToList();
            InsightUiElement list = records.Count == 0 ? Empty("Wildlife.UI.Journal.Expeditions.Empty", "Wildlife.UI.Journal.Expeditions.Empty.Help") :
                BuildVirtualList("journal-expedition-items", records, 88f, (item, index) =>
                    InsightUi.Surface("journal.expedition." + item.Id, InsightUi.Column("journal.expedition." + item.Id + ".content",
                        InsightUi.SectionHeader("journal.expedition." + item.Id + ".header", item.Title, item.Detail),
                        InsightUi.Row("journal.expedition." + item.Id + ".meta").SetGap(8f)
                            .Add(InsightUi.StatRow("journal.expedition." + item.Id + ".stage", JournalText.Get("Wildlife.UI.Journal.Meta.State"), item.Stage),
                                InsightUi.StatRow("journal.expedition." + item.Id + ".return", JournalText.Get("Wildlife.UI.Journal.Expeditions.Return"), item.ReturnTime)),
                        InsightUi.Button("journal.expedition." + item.Id + ".open", JournalText.Get("Wildlife.UI.Journal.Action.OpenExpedition"),
                            () => OpenExpeditions())).SetGap(5f)));
            InsightUiElement action = InsightUi.Button("journal.expeditions.plan", JournalText.Get("Wildlife.UI.Journal.Action.PlanExpedition"), OpenExpeditions);
            return InsightUi.Column("journal.expeditions.content", action, list).SetGap(8f);
        }

        private InsightUiElement BuildChronicle()
        {
            List<JournalStoryRecord> stories = snapshot.Stories.Where(Matches).Take(12).ToList();
            List<JournalInvestigationRecord> investigations = snapshot.Investigations.Where(Matches).Take(12).ToList();
            return InsightUi.Column("journal.chronicle.content",
                InsightUi.Callout("journal.chronicle.help", InsightUiCalloutSeverity.Info,
                    JournalText.Get("Wildlife.UI.Journal.Chronicle.Help")),
                InsightUi.SectionHeader("journal.chronicle.stories.header",
                    JournalText.Get("Wildlife.UI.Journal.Page.Stories"), JournalText.Get("Wildlife.UI.Journal.Chronicle.StoriesHelp")),
                stories.Count == 0 ? Empty("journal.chronicle.stories.empty", "Wildlife.UI.Journal.Stories.Empty",
                    "Wildlife.UI.Journal.Stories.Empty.Help") :
                    BuildVirtualList("journal.chronicle.story-items", stories, 82f, BuildChronicleStoryRow),
                InsightUi.SectionHeader("journal.chronicle.investigations.header",
                    JournalText.Get("Wildlife.UI.Journal.Page.Investigations"), JournalText.Get("Wildlife.UI.Journal.Chronicle.InvestigationsHelp")),
                investigations.Count == 0 ? Empty("journal.chronicle.investigations.empty", "Wildlife.UI.Journal.Investigations.Empty",
                    "Wildlife.UI.Journal.Investigations.Empty.Help") :
                    BuildVirtualList("journal.chronicle.investigation-items", investigations, 88f, BuildChronicleInvestigationRow)).SetGap(8f);
        }

        private InsightUiElement BuildChronicleStoryRow(JournalStoryRecord value, int index)
        {
            string id = "journal.chronicle.story." + value.Id;
            return InsightUi.Surface(id, InsightUi.Column(id + ".content",
                InsightUi.SectionHeader(id + ".header", value.Title, value.Summary),
                InsightUi.Callout(id + ".consequence", InsightUiCalloutSeverity.Info,
                    JournalText.Get("Wildlife.UI.Journal.Stories.Interpretation"), value.Interpretation),
                InsightUi.StatRow(id + ".time", JournalText.Get("Wildlife.UI.Journal.Meta.Time"), value.Age)).SetGap(5f));
        }

        private InsightUiElement BuildChronicleInvestigationRow(JournalInvestigationRecord value, int index)
        {
            string id = "journal.chronicle.investigation." + value.Id;
            return InsightUi.Surface(id, InsightUi.Column(id + ".content",
                InsightUi.SectionHeader(id + ".header", value.Title, value.Hypothesis),
                InsightUi.Meter(id + ".progress", value.Progress, 1f),
                InsightUi.StatRow(id + ".state", JournalText.Get("Wildlife.UI.Journal.Meta.State"), value.State),
                InsightUi.Callout(id + ".next", value.Contradictory ? InsightUiCalloutSeverity.Warning : InsightUiCalloutSeverity.Info,
                    JournalText.Get("Wildlife.UI.Journal.Investigations.Next"), value.NextAction)).SetGap(5f));
        }

        private InsightUiElement BuildStories()
        {
            List<JournalStoryRecord> records = snapshot.Stories.Where(Matches).ToList();
            JournalStoryRecord selected = records.FirstOrDefault(value => value.Id == selectedStoryId) ?? records.FirstOrDefault();
            selectedStoryId = selected?.Id;
            InsightUiElement list = records.Count == 0 ? Empty("Wildlife.UI.Journal.Stories.Empty", "Wildlife.UI.Journal.Stories.Empty.Help") :
                BuildVirtualList("journal-story-items", records, 86f, (item, index) =>
                    InsightUi.Surface("journal.story." + item.Id, InsightUi.Column("journal.story." + item.Id + ".content",
                        InsightUi.Button("journal.story." + item.Id + ".select", item.Title, () =>
                        {
                            selectedStoryId = item.Id;
                            Rebuild();
                        }), InsightUi.Label("journal.story." + item.Id + ".summary", item.Summary, InsightUiTextStyle.Caption),
                        InsightUi.StatRow("journal.story." + item.Id + ".time", JournalText.Get("Wildlife.UI.Journal.Meta.Time"), item.Age)).SetGap(4f)));
            InsightUiElement detail = selected == null ? Empty("journal.stories.detail.empty",
                "Wildlife.UI.Journal.Selection.Empty", "Wildlife.UI.Journal.Selection.Empty.Help") :
                InsightUi.Surface("journal.stories.detail", InsightUi.Column("journal.stories.detail.content",
                    InsightUi.SectionHeader("journal.stories.detail.header", selected.Title, selected.Summary),
                    InsightUi.Callout("journal.stories.detail.interpretation", InsightUiCalloutSeverity.Info,
                        JournalText.Get("Wildlife.UI.Journal.Stories.Interpretation"), selected.Interpretation),
                    InsightUi.StatRow("journal.stories.detail.species", JournalText.Get("Wildlife.UI.Journal.Meta.Species"), selected.SpeciesName),
                    InsightUi.StatRow("journal.stories.detail.time", JournalText.Get("Wildlife.UI.Journal.Meta.Time"), selected.Age)).SetGap(7f));
            return ResponsiveSplit("journal.stories.split", list, detail, 0.50f);
        }

        private InsightUiElement Empty(string titleKey, string helpKey)
        {
            return Empty("journal.empty." + titleKey.Replace('.', '-'), titleKey, helpKey);
        }

        private InsightUiElement Empty(string id, string titleKey, string helpKey)
        {
            return InsightUi.Callout(id, InsightUiCalloutSeverity.Info,
                JournalText.Get(titleKey), JournalText.Get(helpKey));
        }

        private InsightUiElement BuildVirtualList<T>(string id, IReadOnlyList<T> values, float height,
            Func<T, int, InsightUiElement> factory)
        {
            InsightUiVirtualList list = InsightUi.VirtualList(id, values.Count, height, index => factory(values[index], index));
            list.CacheLimit = 96;
            list.Overscan = 2;
            return list.SetMinSize(0f, 180f);
        }

        private WildlifeResponsiveJournalSplit ResponsiveSplit(string id, InsightUiElement first,
            InsightUiElement second, float ratio) => new WildlifeResponsiveJournalSplit(id, first, second, ratio);

        private void EnsureSnapshot(bool force)
        {
            Map activeMap = map ?? Find.CurrentMap;
            int now = Find.TickManager?.TicksGame ?? 0;
            int knowledgeRevision = KnowledgeQuery.Revision;
            int observerId = signalObserver?.thingIDNumber ?? 0;
            if (!force && snapshot != null && snapshot.Map == activeMap && snapshotBucket == now / 30 &&
                snapshotKnowledgeRevision == knowledgeRevision && snapshotObserverId == observerId) return;
            snapshot = BuildSnapshot(activeMap, now, knowledgeRevision);
            snapshotBucket = now / 30;
            snapshotKnowledgeRevision = knowledgeRevision;
            snapshotObserverId = observerId;
            if (selectedSpecies != null && !snapshot.Species.Any(value => value.Species == selectedSpecies)) selectedSpecies = null;
        }

        private WildlifeJournalPresentationSnapshot BuildSnapshot(Map activeMap, int now, int knowledgeRevision)
        {
            WildlifeEcologySnapshot ecology = WildlifeEcologySnapshots.For(activeMap);
            List<JournalSpeciesRecord> species = new List<JournalSpeciesRecord>();
            foreach (WildlifeSpeciesSnapshot value in ecology?.species ?? Array.Empty<WildlifeSpeciesSnapshot>())
            {
                if (value?.species == null || HerdsMod.Settings?.enableWildlifeKnowledge != true) continue;
                bool knowledgeVisible = Observed(value, activeMap);
                species.Add(BuildSpecies(value, activeMap, now, knowledgeVisible));
            }
            species = species.OrderByDescending(value => WildlifeKnowledgeAdapter.StageOrder(value.StageId))
                .ThenBy(value => value.Species.LabelCap.ToString(), StringComparer.OrdinalIgnoreCase).Take(96).ToList();
            List<JournalSignalRecord> signals = BuildSignalsSnapshot(ecology, activeMap, now);
            List<JournalLogRecord> fieldLog = BuildFieldLogSnapshot(ecology, activeMap, now, signals);
            List<JournalRegionRecord> regions = species.Where(value => value.KnowledgeVisible)
                .Select(value => BuildRegion(value, ecology, activeMap)).Take(96).ToList();
            List<JournalInvestigationRecord> investigations = BuildInvestigationsSnapshot(activeMap, now);
            List<JournalExpeditionRecord> expeditions = BuildExpeditionsSnapshot(activeMap, now);
            List<JournalStoryRecord> stories = BuildStoriesSnapshot(activeMap, now);
            return new WildlifeJournalPresentationSnapshot(activeMap, now, knowledgeRevision, species, fieldLog,
                signals, regions, investigations, expeditions, stories);
        }

        private JournalSpeciesRecord BuildSpecies(WildlifeSpeciesSnapshot value, Map activeMap, int now,
            bool knowledgeVisible)
        {
            List<JournalFacetRecord> facets = new List<JournalFacetRecord>();
            List<JournalClaimRecord> claims = new List<JournalClaimRecord>();
            foreach (string facetId in KnowledgeFacets)
            {
                KnowledgeFacetSnapshotV2 facet = WildlifeKnowledgeAdapter.Facet(null, value.species, facetId, activeMap, true);
                if (facet == null) continue;
                facets.Add(new JournalFacetRecord(facetId, facet.amount, facet.completeness, facet.confidence,
                    facet.evidenceCount, facet.provisional));
                if (facetId == WildlifeKnowledgeAdapter.FacetIdentity)
                {
                    foreach (KnowledgeClaimSnapshot claim in KnowledgeQuery.Claims(WildlifeKnowledgeAdapter.DomainId,
                        value.subjectId, facetId, null, KnowledgeScope.Colony, WildlifeKnowledgeAdapter.MapContext(activeMap),
                        KnowledgeContextFallbackMode.ParentThenGlobal).Take(4))
                    {
                        if (claim == null) continue;
                        claims.Add(new JournalClaimRecord(claim.claimId, claim.value?.textValue,
                            claim.effectiveConfidence, claim.contradictory, claim.observationCount,
                            claim.bestQualitySource));
                    }
                }
            }
            string stage = WildlifeKnowledgeAdapter.ColonyStage(value.species);
            int evidenceCount = value.evidence.Count + value.signals.Count + value.trails.Count + value.migrations.Count;
            bool fieldGuideObserved = FieldGuideObserved(value, activeMap);
            string nextEvidence = NextEvidence(stage, facets);
            List<JournalEvidenceRecord> evidence = value.evidence.Take(8).Select(item => new JournalEvidenceRecord(
                JournalText.EventKind(item.kind), item.summary, item.source, item.confidence, item.observerCount,
                item.tick, JournalText.Age(item.tick, now))).ToList();
            return new JournalSpeciesRecord(value.species, value.subjectId, stage, value.confidence, evidenceCount,
                facets, claims, evidence, fieldGuideObserved, knowledgeVisible, nextEvidence);
        }

        private static bool FieldGuideObserved(WildlifeSpeciesSnapshot value, Map activeMap)
        {
            if (value == null) return false;
            if (HerdsMod.Settings?.enableWildlifeKnowledge != true ||
                HerdsMod.Settings.requireObservationForDetails != true) return true;
            if (value.evidence.Count > 0 || value.signals.Count > 0 || value.trails.Count > 0 || value.migrations.Count > 0)
                return true;
            HerdMapComponent herds = activeMap?.GetComponent<HerdMapComponent>();
            IReadOnlyList<Pawn> pawns = activeMap?.mapPawns?.AllPawnsSpawned;
            if (herds == null || pawns == null) return false;
            for (int i = 0; i < pawns.Count; i++)
                if (pawns[i]?.def == value.species && herds.IsObserved(pawns[i])) return true;
            return false;
        }

        private static string NextEvidence(string stage, IReadOnlyList<JournalFacetRecord> facets)
        {
            JournalFacetRecord unknown = facets?.FirstOrDefault(value => !value.Known);
            return unknown != null
                ? JournalText.Format("Wildlife.UI.Journal.Knowledge.NextEvidence.Facet", unknown.Label)
                : WildlifeKnowledgeAdapter.StageOrder(stage) < WildlifeKnowledgeAdapter.StageOrder(WildlifeKnowledgeAdapter.StageDocumented)
                    ? JournalText.Get("Wildlife.UI.Journal.Knowledge.NextEvidence.Document")
                    : JournalText.Get("Wildlife.UI.Journal.Knowledge.NextEvidence.None");
        }

        private List<JournalSignalRecord> BuildSignalsSnapshot(WildlifeEcologySnapshot ecology, Map activeMap, int now)
        {
            List<JournalSignalRecord> output = new List<JournalSignalRecord>();
            foreach (WildlifeSignalSnapshot value in ecology?.signals?.OrderByDescending(item => item.tick).Take(64) ??
                Enumerable.Empty<WildlifeSignalSnapshot>())
            {
                if (value?.species == null) continue;
                WildlifeSignalCultureMapComponent culture = activeMap?.GetComponent<WildlifeSignalCultureMapComponent>();
                WildlifeWarningKnowledgeState warning = WildlifeSignalCultureMapComponent.IsWarningCall(value.kind)
                    ? (signalObserver == null ? culture?.ColonyWarningKnowledge(value.species) : culture?.WarningKnowledge(signalObserver, value.species)) : null;
                WildlifePredatorPressureKnowledgeState pressure = value.predatorPressureEligible
                    ? culture?.ColonyPredatorPressure(value.species) : null;
                bool contradictory = warning?.contradictory == true || pressure?.contradictory == true;
                float confidence = pressure?.confidence ?? warning?.confidence ?? (value.verified ? 0.85f : value.behaviorConsistent ? 0.55f : 0.2f);
                output.Add(new JournalSignalRecord("signal:" + value.species.defName + ":" + value.tick + ":" + value.cell,
                    value.species, value.kind, value.cell, value.tick, JournalText.SignalKind(value.kind),
                    value.historicalDescription, signalObserver?.LabelShortCap.ToString() ?? JournalText.Get("Wildlife.UI.Journal.Signals.Colony"),
                    contradictory, confidence, JournalText.Certainty(confidence, !contradictory),
                    pressure?.PlayerLabel ?? warning?.PlayerLabel ?? JournalText.Get("Wildlife.UI.Journal.Signals.Unresolved"),
                    value.historicalDescription, JournalText.Age(value.tick, now)));
            }
            return output;
        }

        private List<JournalLogRecord> BuildFieldLogSnapshot(WildlifeEcologySnapshot ecology, Map activeMap, int now,
            IReadOnlyList<JournalSignalRecord> signals)
        {
            List<JournalLogRecord> values = new List<JournalLogRecord>();
            WildlifeFieldJournalMapComponent journal = activeMap?.GetComponent<WildlifeFieldJournalMapComponent>();
            WildlifeOpportunityRecord opportunity = journal?.Opportunity;
            if (opportunity != null)
                values.Add(new JournalLogRecord("moment:" + opportunity.eventKey, "Wildlife.UI.Journal.Type.Moment",
                    WildlifeFieldJournalMapComponent.OpportunityLabel(opportunity.kind), opportunity.description,
                    opportunity.response == WildlifeMomentResponse.None ? JournalText.Get("Wildlife.UI.Journal.Moment.Waiting") :
                    JournalText.Get("Wildlife.UI.Journal.Moment.Underway"), JournalText.Get("Wildlife.UI.Journal.Meta.Direct"),
                    Sector(activeMap, opportunity.focusCell), JournalText.Age(opportunity.startedTick, now), opportunity.species,
                    opportunity.focusCell, opportunity.startedTick, opportunity.response == WildlifeMomentResponse.None,
                    opportunity.response == WildlifeMomentResponse.None ? JournalAction.Response : JournalAction.Focus));
            foreach (WildlifeMomentOutcomeRecord outcome in journal?.MomentHistory?.Take(8) ?? Enumerable.Empty<WildlifeMomentOutcomeRecord>())
                if (outcome != null) values.Add(new JournalLogRecord("moment-outcome:" + outcome.tick + ":" + outcome.species?.defName,
                    "Wildlife.UI.Journal.Type.Moment", WildlifeFieldJournalMapComponent.OpportunityLabel(outcome.kind), outcome.text,
                    outcome.success ? JournalText.Get("Wildlife.UI.Journal.Moment.Learned") : JournalText.Get("Wildlife.UI.Journal.Moment.Unresolved"),
                    JournalText.Get("Wildlife.UI.Journal.Meta.Recorded"), JournalText.Get("Wildlife.UI.Journal.Meta.FieldRecord"),
                    JournalText.Age(outcome.tick, now), outcome.species, IntVec3.Invalid, outcome.tick, false, JournalAction.Focus));
            foreach (JournalSignalRecord signal in signals.Take(24))
                values.Add(new JournalLogRecord(signal.Id, "Wildlife.UI.Journal.Type.Signal", signal.SpeciesName,
                    signal.Description, signal.Interpretation, signal.ConfidenceLabel, Sector(activeMap, signal.Cell), signal.Age,
                    signal.Species, signal.Cell, signal.Tick, signal.Contradictory, JournalAction.Focus));
            foreach (WildlifeTrailLead trail in activeMap?.GetComponent<WildlifeTrailMapComponent>()?.TrailLeads
                ?.Where(value => value?.species != null && value.expiresTick > now).OrderByDescending(value => value.createdTick).Take(16) ??
                Enumerable.Empty<WildlifeTrailLead>())
            {
                IntVec3 cell = trail.predictedCell.IsValid ? trail.predictedCell : trail.departureCell;
                values.Add(new JournalLogRecord("trail:" + trail.species.defName + ":" + trail.createdTick + ":" + trail.departureCell,
                    "Wildlife.UI.Journal.Type.Trail", trail.species.LabelCap.ToString(),
                    trail.lastOutcome.NullOrEmpty() ? JournalText.Format("Wildlife.UI.Journal.Trail.Direction", Sector(activeMap, cell)) : trail.lastOutcome,
                    trail.viableLead ? JournalText.Get("Wildlife.UI.Journal.Trail.Actionable") : JournalText.Get("Wildlife.UI.Journal.Trail.Uncertain"),
                    JournalText.TrailState(trail.state), Sector(activeMap, cell), JournalText.Age(trail.createdTick, now), trail.species,
                    cell, trail.createdTick, trail.state == WildlifeTrailState.LiveQuarry || trail.state == WildlifeTrailState.Pursuit,
                    JournalAction.Focus));
            }
            foreach (WildlifeMysteryRecord mystery in activeMap?.GetComponent<WildlifeMysteryMapComponent>()?.Mysteries
                ?.Where(value => value != null && !value.Resolved).OrderByDescending(value => value.startedTick).Take(16) ??
                Enumerable.Empty<WildlifeMysteryRecord>())
            {
                IntVec3 cell = mystery.animal?.Spawned == true ? mystery.animal.Position : IntVec3.Invalid;
                values.Add(new JournalLogRecord("mystery:" + mystery.id, "Wildlife.UI.Journal.Type.Investigation", mystery.title,
                    mystery.Solved ? mystery.explanation : mystery.anomaly,
                    mystery.Solved ? JournalText.Get("Wildlife.UI.Journal.Investigation.Confirmed") : JournalText.Get("Wildlife.UI.Journal.Investigation.Working"),
                    mystery.Solved ? JournalText.Get("Wildlife.UI.Journal.Certainty.Confirmed") : JournalText.Get("Wildlife.UI.Journal.Certainty.Uncertain"),
                    Sector(activeMap, cell), JournalText.Age(mystery.Solved && mystery.solvedTick > 0 ? mystery.solvedTick : mystery.startedTick, now),
                    mystery.species, cell, mystery.Solved && mystery.solvedTick > 0 ? mystery.solvedTick : mystery.startedTick,
                    mystery == activeMap.GetComponent<WildlifeMysteryMapComponent>()?.Active, JournalAction.Focus));
            }
            foreach (HuntingExpeditionRecord expedition in activeMap?.GetComponent<HuntingExpeditionMapComponent>()?.ActiveExpeditions
                ?.Take(8) ?? Enumerable.Empty<HuntingExpeditionRecord>())
                if (expedition != null) values.Add(new JournalLogRecord("expedition:" + expedition.id,
                    "Wildlife.UI.Journal.Type.Expedition", JournalText.Get("Wildlife.UI.Journal.Expeditions.Active"),
                    JournalText.Format("Wildlife.UI.Journal.Expeditions.Objective", expedition.objective,
                        expedition.targetSpecies?.LabelCap.ToString() ?? JournalText.Get("Wildlife.UI.Journal.Value.Unknown")),
                    expedition.needsRescue ? JournalText.Get("Wildlife.UI.Journal.Expeditions.Rescue") : JournalText.Get("Wildlife.UI.Journal.Expeditions.Tracked"),
                    expedition.needsRescue ? JournalText.Get("Wildlife.UI.Journal.Certainty.Urgent") : JournalText.Get("Wildlife.UI.Journal.Certainty.Probable"),
                    JournalText.Get("Wildlife.UI.Journal.Meta.BeyondMap"), JournalText.Age(expedition.departureTick, now), expedition.targetSpecies,
                    IntVec3.Invalid, expedition.departureTick, expedition.needsRescue, JournalAction.OpenExpedition));
            foreach (WildlifeExperienceEvent outcome in Current.Game?.GetComponent<WildlifeExperienceGameComponent>()?.Events
                ?.Take(30) ?? Enumerable.Empty<WildlifeExperienceEvent>())
                if (outcome != null && !outcome.text.NullOrEmpty()) values.Add(new JournalLogRecord(
                    "outcome:" + outcome.category + ":" + outcome.tick + ":" + outcome.thingId,
                    "Wildlife.UI.Journal.Type.Outcome", JournalText.OutcomeTitle(outcome.category),
                    outcome.text, WildlifeExperience.IsNegative(outcome) ? JournalText.Get("Wildlife.UI.Journal.Outcome.Setback") :
                    JournalText.Get("Wildlife.UI.Journal.Outcome.Recorded"), JournalText.Get("Wildlife.UI.Journal.Meta.Recorded"),
                    JournalText.Get("Wildlife.UI.Journal.Meta.ColonyRecord"), JournalText.Age(outcome.tick, now), null, IntVec3.Invalid,
                    outcome.tick, WildlifeExperience.IsNegative(outcome), JournalAction.Focus));
            return values.GroupBy(value => value.Id).Select(group => group.First()).OrderByDescending(value => value.Tick)
                .ThenByDescending(value => value.Urgent).Take(128).ToList();
        }

        private List<JournalRegionRecord> BuildRegionsFromSpecies(IEnumerable<JournalSpeciesRecord> species,
            WildlifeEcologySnapshot ecology, Map activeMap)
        {
            return species.Select(value => BuildRegion(value, ecology, activeMap)).ToList();
        }

        private JournalRegionRecord BuildRegion(JournalSpeciesRecord value, WildlifeEcologySnapshot ecology, Map activeMap)
        {
            WildlifeSpeciesSnapshot source = ecology?.For(value.Species);
            int local = source?.localCount ?? 0;
            float regional = source?.regionalPopulation ?? 0f;
            float pressure = source?.pressure ?? 0f;
            int evidence = source?.evidence?.Count ?? 0;
            int trails = source?.trails?.Count ?? 0;
            int migrations = source?.migrations?.Count ?? 0;
            int variations = source?.variations?.Count ?? 0;
            int notable = activeMap?.GetComponent<NotableWildlifeMapComponent>()?.Records?.Count(item =>
                item?.species == value.Species && item.deathRecorded == false) ?? 0;
            string trend = local > 0 ? JournalText.Get("Wildlife.UI.Journal.Region.Present") :
                migrations > 0 ? JournalText.Get("Wildlife.UI.Journal.Region.Moving") : JournalText.Get("Wildlife.UI.Journal.Region.Unseen");
            string movement = migrations > 0 ? JournalText.Format("Wildlife.UI.Journal.Region.MigrationCount", migrations) :
                trails > 0 ? JournalText.Format("Wildlife.UI.Journal.Region.TrailCount", trails) : JournalText.Get("Wildlife.UI.Journal.Region.NoMovement");
            string summary = JournalText.Format("Wildlife.UI.Journal.Region.Summary", local, regional.ToString("0.##"));
            string atlasSummary = JournalText.Format("Wildlife.UI.Journal.LivingAtlas.Summary", evidence, trails,
                migrations, notable, ecology?.activeExpeditions ?? 0);
            return new JournalRegionRecord(value.Species, local.ToString(), regional.ToString("0.##"), trend,
                Mathf.Clamp01(pressure), pressure >= 0.7f ? JournalText.Get("Wildlife.UI.Journal.Region.Pressure.High") :
                pressure >= 0.4f ? JournalText.Get("Wildlife.UI.Journal.Region.Pressure.Moderate") : JournalText.Get("Wildlife.UI.Journal.Region.Pressure.Low"),
                source?.habitatQuality.ToString("0.##") ?? JournalText.Get("Wildlife.UI.Journal.Value.Unknown"), movement,
                summary, atlasSummary, evidence, trails, migrations, notable, variations);
        }

        private List<JournalInvestigationRecord> BuildInvestigationsSnapshot(Map activeMap, int now)
        {
            List<JournalInvestigationRecord> output = new List<JournalInvestigationRecord>();
            WildlifeMysteryMapComponent mysteries = activeMap?.GetComponent<WildlifeMysteryMapComponent>();
            foreach (WildlifeMysteryRecord value in mysteries?.Mysteries?.Where(item => item != null && !item.Resolved).Take(24) ??
                Enumerable.Empty<WildlifeMysteryRecord>())
                output.Add(new JournalInvestigationRecord("mystery:" + value.id, value.title, value.species,
                    value.Solved ? value.explanation : value.anomaly, value.Solved ? JournalText.State("Confirmed") : JournalText.State("Working"),
                    Mathf.Clamp01(value.progress), value.progress >= 0.75f ? JournalText.Get("Wildlife.UI.Journal.Uncertainty.Narrow") : JournalText.Get("Wildlife.UI.Journal.Uncertainty.Open"),
                    string.Join("\n", value.evidence.Take(6).Select(item => item.source + ": " + item.clue)), value.evidence.Any(item => item.value < 0f),
                    value.Solved ? JournalText.Get("Wildlife.UI.Journal.Investigation.Response") : JournalText.Get("Wildlife.UI.Journal.Investigation.Review"),
                    value.animal?.Spawned == true ? value.animal.Position : IntVec3.Invalid));
            foreach (WildlifeHypothesisRecord value in WildlifeNarrativeUtility.For(activeMap)?.OpenHypotheses?.Take(24) ??
                Enumerable.Empty<WildlifeHypothesisRecord>())
                if (value != null) output.Add(new JournalInvestigationRecord("hypothesis:" + value.id, value.title, value.species,
                    value.candidates?.FirstOrDefault()?.explanation ?? JournalText.Get("Wildlife.UI.Journal.Investigation.NoHypothesis"),
                    JournalText.State(value.state.ToString()), Mathf.Clamp01(value.confidence),
                    value.candidates?.Count > 1 ? JournalText.Get("Wildlife.UI.Journal.Uncertainty.Competing") : JournalText.Get("Wildlife.UI.Journal.Uncertainty.Open"),
                    string.Join("\n", value.evidence.Take(6).Select(item => item.source + ": " + item.text)),
                    value.evidence.Any(item => item.contradicts), value.bestNextObservation, IntVec3.Invalid));
            return output.OrderByDescending(value => value.Progress).Take(48).ToList();
        }

        private List<JournalExpeditionRecord> BuildExpeditionsSnapshot(Map activeMap, int now)
        {
            HuntingExpeditionMapComponent component = activeMap?.GetComponent<HuntingExpeditionMapComponent>();
            return (component?.ActiveExpeditions ?? Array.Empty<HuntingExpeditionRecord>()).Where(value => value != null).Take(32)
                .Select(value => new JournalExpeditionRecord(value.id,
                    JournalText.Format("Wildlife.UI.Journal.Expeditions.Title", value.id),
                    JournalText.Format("Wildlife.UI.Journal.Expeditions.Objective", value.objective,
                        value.targetSpecies?.LabelCap.ToString() ?? JournalText.Get("Wildlife.UI.Journal.Value.Unknown")),
                    JournalText.State(value.stage.ToString()),
                    Mathf.Clamp01(value.expectedReturnTick <= value.departureTick ? 0.5f :
                        1f - (float)Mathf.Max(0, value.expectedReturnTick - now) / Mathf.Max(1, value.expectedReturnTick - value.departureTick)),
                    value.expectedReturnTick <= now ? JournalText.Get("Wildlife.UI.Journal.Expeditions.ReturnSoon") :
                    JournalText.Age(value.expectedReturnTick, now), value.needsRescue)).ToList();
        }

        private List<JournalStoryRecord> BuildStoriesSnapshot(Map activeMap, int now)
        {
            List<JournalStoryRecord> output = new List<JournalStoryRecord>();
            foreach (WildlifeNarrativeRecord value in WildlifeNarrativeUtility.For(activeMap)?.Stories?.Take(40) ??
                Enumerable.Empty<WildlifeNarrativeRecord>())
                if (value != null) output.Add(new JournalStoryRecord("story:" + value.tick + ":" + value.title,
                    value.title, value.summary, value.interpretation, value.species, value.tick, JournalText.Age(value.tick, now)));
            foreach (WildlifeExperienceEvent value in Current.Game?.GetComponent<WildlifeExperienceGameComponent>()?.Events?.Take(30) ??
                Enumerable.Empty<WildlifeExperienceEvent>())
                if (value != null && !value.text.NullOrEmpty()) output.Add(new JournalStoryRecord(
                    "experience:" + value.tick + ":" + value.category, JournalText.OutcomeTitle(value.category),
                    value.text, WildlifeExperience.IsNegative(value) ? JournalText.Get("Wildlife.UI.Journal.Outcome.Setback") :
                    JournalText.Get("Wildlife.UI.Journal.Outcome.Recorded"), null, value.tick, JournalText.Age(value.tick, now)));
            return output.GroupBy(value => value.Id).Select(group => group.First()).OrderByDescending(value => value.Tick).Take(64).ToList();
        }

        private void SelectPage(string id)
        {
            WildlifeJournalPage next = Pages.FirstOrDefault(value => PageId(value) == id);
            if (!Pages.Contains(next)) next = WildlifeJournalPage.FieldLog;
            if (page == next) return;
            page = next;
            Rebuild();
        }

        private void Rebuild()
        {
            EnsureSnapshot(false);
            Document.Root = BuildRoot();
            Document.Invalidate();
        }

        private bool Matches(JournalLogRecord value) => Matches(value.Title, value.Detail, value.SpeciesName, value.Certainty, value.Location);
        private bool Matches(JournalSpeciesRecord value) => Matches(value.Species.LabelCap.ToString(), value.StageLabel, value.SubjectId);
        private bool Matches(JournalRegionRecord value) => Matches(value.Species.LabelCap.ToString(), value.Summary, value.Trend, value.Movement);
        private bool Matches(JournalSignalRecord value) => Matches(value.SpeciesName, value.Family, value.Description, value.Interpretation, value.ObserverName);
        private bool Matches(JournalInvestigationRecord value) => Matches(value.Title, value.Hypothesis, value.State, value.Evidence, value.NextAction);
        private bool Matches(JournalStoryRecord value) => Matches(value.Title, value.Summary, value.Interpretation, value.SpeciesName);
        private bool Matches(JournalExpeditionRecord value) => Matches(value.Title, value.Detail, value.Stage, value.ReturnTime);
        private bool Matches(params string[] values)
        {
            if (search.NullOrEmpty()) return true;
            string query = search.Trim();
            return values.Any(value => !value.NullOrEmpty() && value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private void Focus(IntVec3 cell, ThingDef species)
        {
            Host.RunWithOverlayOwnership(() =>
            {
                WildlifeUI.CloseMenus();
                Pawn pawn = map?.mapPawns?.AllPawnsSpawned?.FirstOrDefault(value => value?.def == species && value.Faction == null);
                if (pawn != null) WildlifeUI.Focus(pawn);
                else if (cell.IsValid) WildlifeUI.Focus(cell, map);
            });
        }

        private void ChooseMomentResponse()
        {
            WildlifeFieldJournalMapComponent component = map?.GetComponent<WildlifeFieldJournalMapComponent>();
            WildlifeOpportunityRecord opportunity = component?.Opportunity;
            if (component == null || opportunity == null) return;
            List<WildlifeMomentResponse> responses = new List<WildlifeMomentResponse> { WildlifeMomentResponse.Observe };
            if (WildlifeFieldJournalMapComponent.ResearchAllowsResponse(WildlifeMomentResponse.Track)) responses.Add(WildlifeMomentResponse.Track);
            if (HerdsMod.Settings?.enableNotableAnimals == true && !opportunity.protectionDeclared) responses.Add(WildlifeMomentResponse.Protect);
            if (WildlifeFieldJournalMapComponent.ResearchAllowsResponse(WildlifeMomentResponse.Hunt)) responses.Add(WildlifeMomentResponse.Hunt);
            responses.Add(WildlifeMomentResponse.Ignore);
            Find.WindowStack.Add(new FloatMenu(responses.Select(response => new FloatMenuOption(
                response == WildlifeMomentResponse.Ignore ? JournalText.Get("Wildlife.UI.Journal.Action.LeaveAlone") :
                    JournalText.Get("Wildlife.UI.Journal.Response." + response), () => component.ChooseMomentResponse(response))).ToList()));
        }

        private void OpenExpeditions()
        {
            page = WildlifeJournalPage.Expeditions;
            Rebuild();
        }

        private static bool Observed(WildlifeSpeciesSnapshot value, Map activeMap)
        {
            if (value == null || HerdsMod.Settings?.enableWildlifeKnowledge != true) return false;
            if (FieldGuideObserved(value, activeMap)) return true;
            return value.knowledge > 0f && HerdsMod.Settings.requireObservationForDetails != true;
        }

        private static string PageId(WildlifeJournalPage value)
        {
            switch (value)
            {
                case WildlifeJournalPage.FieldGuide: return "field-guide";
                case WildlifeJournalPage.LivingAtlas: return "living-atlas";
                case WildlifeJournalPage.FieldLog: return "field-log";
                default: return value.ToString().ToLowerInvariant();
            }
        }

        private static string Sector(Map activeMap, IntVec3 cell)
        {
            if (activeMap == null || !cell.IsValid) return JournalText.Get("Wildlife.UI.Journal.Meta.Regional");
            return JournalText.Format("Wildlife.UI.Journal.Meta.Sector", Mathf.Max(0, cell.x / 12), Mathf.Max(0, cell.z / 12));
        }
    }

    internal sealed class WildlifeResponsiveJournalSplit : InsightUiElement
    {
        private readonly InsightUiElement first;
        private readonly InsightUiElement second;
        private readonly float ratio;

        internal WildlifeResponsiveJournalSplit(string id, InsightUiElement first, InsightUiElement second, float ratio) : base(id)
        {
            this.first = first;
            this.second = second;
            this.ratio = Mathf.Clamp01(ratio);
            Style.Gap = 8f;
        }

        public override IReadOnlyList<InsightUiElement> Children => new[] { first, second };

        protected override InsightUiSize MeasureCore(InsightUiConstraints constraints, InsightUiFrame frame)
        {
            bool stacked = !float.IsPositiveInfinity(constraints.MaxWidth) && constraints.MaxWidth < 720f;
            float gap = EffectiveGap(frame);
            if (stacked)
            {
                InsightUiSize firstSize = first.Measure(new InsightUiConstraints(0f, constraints.MaxWidth, 0f, float.PositiveInfinity), frame);
                InsightUiSize secondSize = second.Measure(new InsightUiConstraints(0f, constraints.MaxWidth, 0f, float.PositiveInfinity), frame);
                return new InsightUiSize(Math.Max(firstSize.Width, secondSize.Width), firstSize.Height + secondSize.Height + gap);
            }
            float firstWidth = Math.Max(0f, constraints.MaxWidth * ratio - gap);
            InsightUiSize a = first.Measure(new InsightUiConstraints(0f, firstWidth, 0f, constraints.MaxHeight), frame);
            InsightUiSize b = second.Measure(new InsightUiConstraints(0f, Math.Max(0f, constraints.MaxWidth - firstWidth - gap), 0f, constraints.MaxHeight), frame);
            return new InsightUiSize(constraints.MaxWidth, Math.Max(a.Height, b.Height));
        }

        protected override void ArrangeCore(InsightRect rect, InsightUiFrame frame)
        {
            float gap = EffectiveGap(frame);
            if (rect.Width < 720f)
            {
                InsightUiSize firstSize = first.MeasuredSize;
                float firstHeight = Math.Max(180f, Math.Min(rect.Height * 0.48f, firstSize.Height));
                first.Arrange(new InsightRect(rect.X, rect.Y, rect.Width, firstHeight), frame);
                second.Arrange(new InsightRect(rect.X, rect.Y + firstHeight + gap, rect.Width,
                    Math.Max(0f, rect.Height - firstHeight - gap)), frame);
                return;
            }
            float firstWidth = Math.Max(0f, (rect.Width - gap) * ratio);
            first.Arrange(new InsightRect(rect.X, rect.Y, firstWidth, rect.Height), frame);
            second.Arrange(new InsightRect(rect.X + firstWidth + gap, rect.Y,
                Math.Max(0f, rect.Width - firstWidth - gap), rect.Height), frame);
        }

        protected override void PaintCore(IInsightUiPainter painter, InsightUiFrame frame)
        {
            first.Paint(painter, frame);
            float gap = EffectiveGap(frame);
            if (LayoutRect.Width >= 720f)
            {
                float firstWidth = Math.Max(0f, (LayoutRect.Width - gap) * ratio);
                painter.Divider(new InsightRect(LayoutRect.X + firstWidth, LayoutRect.Y, gap, LayoutRect.Height),
                    frame.Theme.SecondaryText.WithAlpha(0.24f), frame);
            }
            second.Paint(painter, frame);
        }
    }

    internal enum JournalAction { Focus, Response, OpenExpedition }

    internal sealed class WildlifeJournalPresentationSnapshot
    {
        internal WildlifeJournalPresentationSnapshot(Map map, int tick, int knowledgeRevision,
            IEnumerable<JournalSpeciesRecord> species, IEnumerable<JournalLogRecord> fieldLog,
            IEnumerable<JournalSignalRecord> signals, IEnumerable<JournalRegionRecord> regions,
            IEnumerable<JournalInvestigationRecord> investigations, IEnumerable<JournalExpeditionRecord> expeditions,
            IEnumerable<JournalStoryRecord> stories)
        {
            Map = map;
            Tick = tick;
            KnowledgeRevision = knowledgeRevision;
            Species = ReadOnly(species);
            FieldLog = ReadOnly(fieldLog);
            Signals = ReadOnly(signals);
            Regions = ReadOnly(regions);
            Investigations = ReadOnly(investigations);
            Expeditions = ReadOnly(expeditions);
            Stories = ReadOnly(stories);
        }

        internal readonly Map Map;
        internal readonly int Tick;
        internal readonly int KnowledgeRevision;
        internal readonly IReadOnlyList<JournalSpeciesRecord> Species;
        internal readonly IReadOnlyList<JournalLogRecord> FieldLog;
        internal readonly IReadOnlyList<JournalSignalRecord> Signals;
        internal readonly IReadOnlyList<JournalRegionRecord> Regions;
        internal readonly IReadOnlyList<JournalInvestigationRecord> Investigations;
        internal readonly IReadOnlyList<JournalExpeditionRecord> Expeditions;
        internal readonly IReadOnlyList<JournalStoryRecord> Stories;

        private static IReadOnlyList<T> ReadOnly<T>(IEnumerable<T> values) =>
            new ReadOnlyCollection<T>((values ?? Enumerable.Empty<T>()).Where(value => value != null).ToList());
    }

    internal sealed class JournalLogRecord
    {
        internal JournalLogRecord(string id, string typeKey, string title, string detail, string evidence, string certainty,
            string location, string age, ThingDef species, IntVec3 cell, int tick, bool urgent, JournalAction action)
        {
            Id = id ?? string.Empty; TypeKey = typeKey; Title = title ?? string.Empty; Detail = detail ?? string.Empty;
            Evidence = evidence ?? string.Empty; Certainty = certainty ?? string.Empty; Location = location ?? string.Empty;
            Age = age ?? string.Empty; Species = species; Cell = cell; Tick = tick; Urgent = urgent; Action = action;
        }
        internal readonly string Id, TypeKey, Title, Detail, Evidence, Certainty, Location, Age;
        internal readonly ThingDef Species;
        internal readonly IntVec3 Cell;
        internal readonly int Tick;
        internal readonly bool Urgent;
        internal readonly JournalAction Action;
        internal string SpeciesName => Species?.LabelCap.ToString() ?? JournalText.Get("Wildlife.UI.Journal.Value.Unknown");
    }

    internal sealed class JournalSpeciesRecord
    {
        internal JournalSpeciesRecord(ThingDef species, string subjectId, string stageId, float confidence, int evidenceCount,
            IEnumerable<JournalFacetRecord> facets, IEnumerable<JournalClaimRecord> claims, IEnumerable<JournalEvidenceRecord> evidence,
            bool fieldGuideObserved, bool knowledgeVisible, string nextEvidence)
        {
            Species = species; SubjectId = subjectId; StageId = stageId; Confidence = Mathf.Clamp01(confidence); EvidenceCount = evidenceCount;
            Facets = new ReadOnlyCollection<JournalFacetRecord>((facets ?? Enumerable.Empty<JournalFacetRecord>()).ToList());
            Claims = new ReadOnlyCollection<JournalClaimRecord>((claims ?? Enumerable.Empty<JournalClaimRecord>()).ToList());
            Evidence = new ReadOnlyCollection<JournalEvidenceRecord>((evidence ?? Enumerable.Empty<JournalEvidenceRecord>()).ToList());
            FieldGuideObserved = fieldGuideObserved; KnowledgeVisible = knowledgeVisible;
            NextEvidence = nextEvidence ?? JournalText.Get("Wildlife.UI.Journal.Knowledge.NextEvidence.None");
        }
        internal readonly ThingDef Species;
        internal readonly string SubjectId, StageId;
        internal readonly float Confidence;
        internal readonly int EvidenceCount;
        internal readonly IReadOnlyList<JournalFacetRecord> Facets;
        internal readonly IReadOnlyList<JournalClaimRecord> Claims;
        internal readonly IReadOnlyList<JournalEvidenceRecord> Evidence;
        internal readonly bool FieldGuideObserved, KnowledgeVisible;
        internal readonly string NextEvidence;
        internal string StageLabel => JournalText.StageLabel(StageId);
        internal string FieldGuideStageLabel => FieldGuideObserved ? StageLabel : JournalText.StageLabel(WildlifeKnowledgeAdapter.StageUnknown);
        internal int FieldGuideEvidenceCount => FieldGuideObserved ? EvidenceCount : 0;
        internal string ConfidenceLabel => JournalText.Certainty(Confidence);
        internal int KnownFacetCount => Facets.Count(value => value.Known);
    }

    internal sealed class JournalFacetRecord
    {
        internal JournalFacetRecord(string id, float amount, float completeness, float confidence, int evidenceCount, bool provisional)
        {
            Id = id; Amount = amount; Completeness = Mathf.Clamp01(completeness); Confidence = Mathf.Clamp01(confidence);
            EvidenceCount = evidenceCount; Provisional = provisional;
        }
        internal readonly string Id;
        internal readonly float Amount, Completeness, Confidence;
        internal readonly int EvidenceCount;
        internal readonly bool Provisional;
        internal bool Known => Amount > 0f || EvidenceCount > 0;
        internal string Label => JournalText.FacetLabel(Id);
        internal string ConfidenceLabel => JournalText.Certainty(Confidence);
    }

    internal sealed class JournalClaimRecord
    {
        internal JournalClaimRecord(string id, string value, float confidence, bool contradictory, int observations, string source)
        {
            Label = JournalText.ClaimLabel(id);
            Detail = (value.NullOrEmpty() ? JournalText.Get("Wildlife.UI.Journal.Value.Unknown") : value) + " • " +
                JournalText.Format("Wildlife.UI.Journal.Knowledge.ClaimDetail", observations, confidence.ToStringPercent(),
                    source.NullOrEmpty() ? JournalText.Get("Wildlife.UI.Journal.Value.Unknown") : source);
            Contradictory = contradictory;
        }
        internal readonly string Label, Detail;
        internal readonly bool Contradictory;
    }

    internal sealed class JournalEvidenceRecord
    {
        internal JournalEvidenceRecord(string kind, string summary, string source, float confidence, int observers, int tick, string age)
        {
            Kind = kind; Summary = summary ?? string.Empty; Source = source ?? JournalText.Get("Wildlife.UI.Journal.Value.Unknown");
            Confidence = confidence; ObserverCount = observers; Tick = tick; Age = age;
        }
        internal readonly string Kind, Summary, Source, Age;
        internal readonly float Confidence;
        internal readonly int ObserverCount, Tick;
        internal string ConfidenceLabel => JournalText.Certainty(Confidence);
    }

    internal sealed class JournalSignalRecord
    {
        internal JournalSignalRecord(string id, ThingDef species, WildlifeSignalKind kind, IntVec3 cell, int tick, string family,
            string description, string observerName, bool contradictory, float confidence, string confidenceLabel,
            string interpretation, string evidence, string age)
        {
            Id = id; Species = species; Kind = kind; Cell = cell; Tick = tick; Family = family; Description = description ?? string.Empty;
            ObserverName = observerName; Contradictory = contradictory; Confidence = confidence; ConfidenceLabel = confidenceLabel;
            Interpretation = interpretation; Evidence = evidence; Age = age;
        }
        internal readonly string Id, Family, Description, ObserverName, ConfidenceLabel, Interpretation, Evidence, Age;
        internal readonly ThingDef Species;
        internal readonly WildlifeSignalKind Kind;
        internal readonly IntVec3 Cell;
        internal readonly int Tick;
        internal readonly bool Contradictory;
        internal readonly float Confidence;
        internal string SpeciesName => Species?.LabelCap.ToString() ?? JournalText.Get("Wildlife.UI.Journal.Value.Unknown");
    }

    internal sealed class JournalRegionRecord
    {
        internal JournalRegionRecord(ThingDef species, string local, string regional, string trend, float pressureValue,
            string pressure, string habitat, string movement, string summary, string atlasSummary,
            int knownEvidenceCount, int trailCount, int migrationCount, int notableCount, int variationCount)
        {
            Species = species; LocalPopulation = local; RegionalPopulation = regional; Trend = trend; PressureValue = pressureValue;
            Pressure = pressure; Habitat = habitat; Movement = movement; Summary = summary; AtlasSummary = atlasSummary;
            KnownEvidenceCount = knownEvidenceCount; TrailCount = trailCount; MigrationCount = migrationCount;
            NotableCount = notableCount; VariationCount = variationCount;
        }
        internal readonly ThingDef Species;
        internal readonly string LocalPopulation, RegionalPopulation, Trend, Pressure, Habitat, Movement, Summary, AtlasSummary;
        internal readonly float PressureValue;
        internal readonly int KnownEvidenceCount, TrailCount, MigrationCount, NotableCount, VariationCount;
    }

    internal sealed class JournalInvestigationRecord
    {
        internal JournalInvestigationRecord(string id, string title, ThingDef species, string hypothesis, string state, float progress,
            string uncertainty, string evidence, bool contradictory, string nextAction, IntVec3 cell)
        {
            Id = id; Title = title ?? string.Empty; Species = species; Hypothesis = hypothesis ?? string.Empty; State = state ?? string.Empty;
            Progress = Mathf.Clamp01(progress); Uncertainty = uncertainty; Evidence = evidence; Contradictory = contradictory;
            NextAction = nextAction; Cell = cell;
        }
        internal readonly string Id, Title, Hypothesis, State, Uncertainty, Evidence, NextAction;
        internal readonly ThingDef Species;
        internal readonly float Progress;
        internal readonly bool Contradictory;
        internal readonly IntVec3 Cell;
    }

    internal sealed class JournalExpeditionRecord
    {
        internal JournalExpeditionRecord(int id, string title, string detail, string stage, float progress, string returnTime, bool rescue)
        {
            Id = id; Title = title; Detail = detail; Stage = stage; Progress = progress; ReturnTime = returnTime; Rescue = rescue;
        }
        internal readonly int Id;
        internal readonly string Title, Detail, Stage, ReturnTime;
        internal readonly float Progress;
        internal readonly bool Rescue;
    }

    internal sealed class JournalStoryRecord
    {
        internal JournalStoryRecord(string id, string title, string summary, string interpretation, ThingDef species, int tick, string age)
        {
            Id = id; Title = title ?? string.Empty; Summary = summary ?? string.Empty; Interpretation = interpretation ?? string.Empty;
            Species = species; Tick = tick; Age = age;
        }
        internal readonly string Id, Title, Summary, Interpretation, Age;
        internal readonly ThingDef Species;
        internal readonly int Tick;
        internal string SpeciesName => Species?.LabelCap.ToString() ?? JournalText.Get("Wildlife.UI.Journal.Value.NotSpeciesSpecific");
    }

    /// <summary>
    /// Compatibility window for older callers. It is presentation-only and delegates all drawing to the
    /// same InsightCanvas document/host used by WildlifeJournal.Open; no legacy manual renderer remains here.
    /// </summary>
}
