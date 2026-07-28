using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Herds
{
    public sealed class WildlifeExperienceEvent : IExposable
    {
        public int tick;
        public string category;
        public string text;
        public int thingId = -1;
        public bool negative;

        public void ExposeData()
        {
            Scribe_Values.Look(ref tick, "tick");
            Scribe_Values.Look(ref category, "category");
            Scribe_Values.Look(ref text, "text");
            Scribe_Values.Look(ref thingId, "thingId", -1);
            Scribe_Values.Look(ref negative, "negative", false);
        }
    }

    public sealed class WildlifeExperienceGameComponent : GameComponent
    {
        private List<WildlifeExperienceEvent> events = new List<WildlifeExperienceEvent>();
        private bool introductionShown;
        private bool expeditionTutorialShown;
        private int unlockedMask;

        public WildlifeExperienceGameComponent(Game game) { }
        public IReadOnlyList<WildlifeExperienceEvent> Events => events;

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref events, "wildlifeExperienceEvents", LookMode.Deep);
            Scribe_Values.Look(ref introductionShown, "wildlifeIntroductionShown");
            Scribe_Values.Look(ref expeditionTutorialShown, "wildlifeExpeditionTutorialShown");
            Scribe_Values.Look(ref unlockedMask, "wildlifeUnlockMask");
            if (Scribe.mode == LoadSaveMode.PostLoadInit) events = events ?? new List<WildlifeExperienceEvent>();
        }

        public override void GameComponentTick()
        {
            HerdsSettings settings = HerdsMod.Settings;
            if (settings == null || (!settings.enablePlayerOnboarding && !settings.enableUnlockLetters)) return;
            int tick = Find.TickManager.TicksGame;
            if (tick % 600 != 0 || Find.CurrentMap == null) return;
            if (settings.enablePlayerOnboarding && !introductionShown && tick > 1200)
            {
                introductionShown = true;
                Find.LetterStack.ReceiveLetter("Wildlife", "Wild animals form social groups, seek homes, evade danger, and hunt with distinct tactics. Open Wildlife Overview at the top of the Wildlife tab to review the colony's knowledge and options.", LetterDefOf.NeutralEvent);
            }
            if (!settings.enableUnlockLetters) return;
            WildlifeCapability[] values = (WildlifeCapability[])Enum.GetValues(typeof(WildlifeCapability));
            for (int i = 0; i < values.Length && i < 30; i++)
            {
                int bit = 1 << i;
                if ((unlockedMask & bit) != 0 || !WildlifeProgression.Unlocked(values[i])) continue;
                unlockedMask |= bit;
                if (tick > 6000)
                    Find.LetterStack.ReceiveLetter("Wildlife Capability Available", WildlifeProgression.Label(values[i]) + " is now available. Open Wildlife Overview for related actions.", LetterDefOf.PositiveEvent);
            }
        }

        public void Add(string category, string text, Thing thing, bool negative)
        {
            events.Insert(0, new WildlifeExperienceEvent
            {
                tick = Find.TickManager?.TicksGame ?? 0,
                category = category,
                text = text,
                thingId = thing?.thingIDNumber ?? -1,
                negative = negative
            });
            if (events.Count > 30) events.RemoveRange(30, events.Count - 30);
        }

        public void ShowExpeditionTutorial()
        {
            if (expeditionTutorialShown || HerdsMod.Settings?.enablePlayerOnboarding != true) return;
            expeditionTutorialShown = true;
            Find.LetterStack.ReceiveLetter("First Wildlife Expedition",
                "Use Wildlife > Wildlife Expeditions to manage every field party. Send New Expedition opens the World Map; select a valid tile, then choose the objective, party, route, and supplies.\n\n" +
                "Expeditions become visible caravans after leaving the colony. Scouting reveals unknown tiles, while Animal Knowledge and fieldcraft improve later outcomes. " +
                "Food is consumed normally during travel. Review warnings in the expedition list, and send assistance there if a party becomes stranded.",
                LetterDefOf.NeutralEvent);
        }
    }

    public static class WildlifeExperience
    {
        private static readonly string[] NegativeTerms =
        {
            "failed", "failure", "abandoned", "withdrew", "downed", "unavailable", "timed out",
            "stopped", "depleted", "scarce", "overpopulated", "could not", "no response", "died",
            "evaded", "cancelled", "stranded", "overdue"
        };

        public static void Record(string category, string text, Thing thing = null, bool negative = false)
        {
            if (HerdsMod.Settings?.enableOutcomeHistory != true || Current.Game == null) return;
            Current.Game.GetComponent<WildlifeExperienceGameComponent>()?.Add(category, text, thing, negative);
        }

        public static bool IsNegative(WildlifeExperienceEvent entry)
        {
            if (entry == null) return false;
            if (entry.negative) return true;
            string text = (entry.category + " " + entry.text).ToLowerInvariant();
            for (int i = 0; i < NegativeTerms.Length; i++) if (text.Contains(NegativeTerms[i])) return true;
            return false;
        }

        public static Thing ResolveThing(int id)
        {
            if (id < 0) return null;
            foreach (Map map in Find.Maps)
            {
                Thing thing = map.listerThings.AllThings.FirstOrDefault(candidate => candidate.thingIDNumber == id);
                if (thing != null) return thing;
            }
            return null;
        }
    }

    public sealed class Window_WildlifeOverview : Window
    {
        private sealed class OverviewNavigation
        {
            public string title;
            public string detail;
            public string tooltip;
            public Color accent;
            public Action action;
        }

        private readonly Map map;
        private Vector2 scroll;
        private int nextTrailSuggestionTick;
        private int cachedUrgentTrailCount;
        public override Vector2 InitialSize => new Vector2(850f, 720f);

        public Window_WildlifeOverview(Map map)
        {
            this.map = map;
            doCloseX = true;
            absorbInputAroundWindow = true;
        }

        public override void DoWindowContents(Rect rect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, rect.width, 34f), "Wildlife Overview");
            Text.Font = GameFont.Small;
            GUI.color = new Color(0.72f, 0.78f, 0.72f);
            Widgets.Label(new Rect(0f, 29f, rect.width, 24f), "The colony's relationship with local and regional wildlife.");
            GUI.color = Color.white;
            if (map == null)
            {
                Widgets.Label(new Rect(0f, 58f, rect.width, 30f), "No active map.");
                return;
            }

            List<Pawn> animals = map.mapPawns.AllPawnsSpawned.Where(pawn => pawn.RaceProps?.Animal == true).ToList();
            int wild = animals.Count(pawn => pawn.Faction == null);
            int predators = animals.Count(pawn => pawn.RaceProps.predator && pawn.Faction == null);
            int knownSpecies = DefDatabase<ThingDef>.AllDefsListForReading.Count(def => def.race?.Animal == true && HuntingKnowledgeMapComponent.ColonyExperience(def) > 0f);
            RegionalWildlifeMapComponent regional = HerdsMod.Settings.enableRegionalPopulations ? map.GetComponent<RegionalWildlifeMapComponent>() : null;
            int regionalSpecies = regional?.Records.Count(record => record.population > 0.05f && HuntingKnowledgeMapComponent.ColonyExperience(record.species) > 0f) ?? 0;
            float cardWidth = (rect.width - 24f) / 4f;
            DrawMetricCard(new Rect(0f, 58f, cardWidth, 68f), "Local Wildlife", wild.ToString(), "wild animals", new Color(0.34f, 0.57f, 0.31f), "Wild animals currently spawned on this map.");
            DrawMetricCard(new Rect(cardWidth + 8f, 58f, cardWidth, 68f), "Predators", predators.ToString(), "wild predators", new Color(0.63f, 0.36f, 0.27f), "Wild predatory animals currently spawned on this map.");
            DrawMetricCard(new Rect((cardWidth + 8f) * 2f, 58f, cardWidth, 68f), "Regional Animals", regionalSpecies.ToString(), "known animals", new Color(0.29f, 0.52f, 0.50f), "Known animals currently estimated to have a population in this region.");
            DrawMetricCard(new Rect((cardWidth + 8f) * 3f, 58f, cardWidth, 68f), "Known Animals", knownSpecies.ToString(), "distinct animals", new Color(0.58f, 0.50f, 0.25f), "Distinct animal species for which the colony has gained any Animal Knowledge, even below the Recognized tier.");

            Rect suggestion = new Rect(0f, 134f, rect.width, 58f);
            Widgets.DrawMenuSection(suggestion);
            Widgets.DrawBoxSolid(new Rect(suggestion.x, suggestion.y, 5f, suggestion.height), new Color(0.43f, 0.61f, 0.34f));
            string landmark = HerdsMod.Settings.enableColonyWildlifeLandmark
                ? map.GetComponent<WildlifeLandmarkMapComponent>()?.OverviewSummary() : null;
            Widgets.Label(new Rect(suggestion.x + 14f, suggestion.y + 8f,
                suggestion.width - 134f, 44f),
                NextAction() + (landmark.NullOrEmpty() ? "" : "\nWildlife reputation: " + landmark));
            Rect openSuggestion = new Rect(suggestion.xMax - 108f,
                suggestion.y + 12f, 94f, 34f);
            if (Widgets.ButtonText(openSuggestion, "Open"))
                OpenSuggestedAction();
            TooltipHandler.TipRegion(openSuggestion,
                "Open the menu associated with the suggested next step.");

            List<OverviewNavigation> navigation = Navigation();
            float navigationY = 202f;
            float cardGap = 8f;
            float navigationWidth = (rect.width - cardGap * 2f) / 3f;
            float navigationHeight = 56f;
            for (int i = 0; i < navigation.Count; i++)
            {
                Rect navRect = new Rect((i % 3) * (navigationWidth + cardGap),
                    navigationY + (i / 3) * (navigationHeight + cardGap),
                    navigationWidth, navigationHeight);
                DrawNavigationCard(navRect, navigation[i]);
            }
            int navigationRows = Mathf.Max(1, Mathf.CeilToInt(navigation.Count / 3f));
            float outcomesY = navigationY + navigationRows *
                (navigationHeight + cardGap) + 8f;
            Rect outer = new Rect(0f, outcomesY, rect.width, rect.height - outcomesY);
            IReadOnlyList<WildlifeExperienceEvent> entries = HerdsMod.Settings.enableOutcomeHistory
                ? Current.Game.GetComponent<WildlifeExperienceGameComponent>()?.Events
                : null;
            Rect view = new Rect(0f, 0f, outer.width - 18f, Mathf.Max(outer.height, 90f + (entries?.Count ?? 0) * 48f));
            Widgets.BeginScrollView(outer, ref scroll, view);
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, view.width, 30f), "Recent Outcomes");
            Text.Font = GameFont.Small;
            if (!HerdsMod.Settings.enableOutcomeHistory)
                Widgets.Label(new Rect(0f, 36f, view.width, 30f), "Recent outcomes are disabled in Wildlife settings.");
            else if (entries == null || entries.Count == 0)
                Widgets.Label(new Rect(0f, 36f, view.width, 30f), "No wildlife outcomes have been recorded yet.");
            else
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    WildlifeExperienceEvent entry = entries[i];
                    Rect row = new Rect(0f, 38f + i * 48f, view.width, 42f);
                    Widgets.DrawHighlightIfMouseover(row);
                    if (WildlifeExperience.IsNegative(entry)) GUI.color = new Color(1f, 0.42f, 0.38f);
                    Widgets.Label(row.ContractedBy(6f), entry.category + " — " + entry.text);
                    GUI.color = Color.white;
                    Thing thing = WildlifeExperience.ResolveThing(entry.thingId);
                    if (thing != null && Widgets.ButtonInvisible(row))
                    {
                        CameraJumper.TryJumpAndSelect(thing);
                        Close();
                    }
                    TooltipHandler.TipRegion(row, "Occurred " + entry.tick.ToStringTicksToPeriod() + " after colony start." + (thing == null ? string.Empty : " Click to select."));
                }
            }
            Widgets.EndScrollView();
        }

        private static void DrawMetricCard(Rect rect, string title, string value, string detail, Color accent, string tooltip)
        {
            Widgets.DrawMenuSection(rect);
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, 5f, rect.height), accent);
            Rect content = rect.ContractedBy(10f);
            GUI.color = new Color(0.72f, 0.78f, 0.72f);
            Widgets.Label(new Rect(content.x + 2f, content.y, content.width - 4f, 20f), title);
            GUI.color = Color.white;
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(content.x + 2f, content.y + 18f, 52f, 28f), value);
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.72f, 0.78f, 0.72f);
            Widgets.Label(new Rect(content.x + 52f, content.y + 25f, content.width - 54f, 22f), detail);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            TooltipHandler.TipRegion(rect, tooltip);
        }

        private void DrawNavigationCard(Rect rect, OverviewNavigation item)
        {
            Widgets.DrawMenuSection(rect);
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, 5f, rect.height), item.accent);
            Widgets.DrawHighlightIfMouseover(rect);
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(rect.x + 13f, rect.y + 7f, rect.width - 21f, 22f),
                item.title);
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.68f, 0.75f, 0.69f);
            Widgets.Label(new Rect(rect.x + 13f, rect.y + 30f, rect.width - 21f, 18f),
                item.detail);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            if (Widgets.ButtonInvisible(rect)) item.action();
            TooltipHandler.TipRegion(rect, item.tooltip);
        }

        private List<OverviewNavigation> Navigation()
        {
            List<OverviewNavigation> result = new List<OverviewNavigation>
            {
                new OverviewNavigation
                {
                    title = "Animal Knowledge", detail = "What the colony understands",
                    tooltip = "Review every known animal and the information its current knowledge tier reveals.",
                    accent = new Color(0.58f, 0.50f, 0.25f),
                    action = () => Find.WindowStack.Add(new Window_ColonyWildlifeKnowledge())
                }
            };
            if (HerdsMod.Settings.enableRegionalPopulations)
                result.Add(new OverviewNavigation
                {
                    title = "Local Wildlife", detail = "Populations and roaming animals",
                    tooltip = "Review nearby populations, trends, confidence, habitat, and known animals beyond the map.",
                    accent = new Color(0.29f, 0.52f, 0.50f),
                    action = () => Find.WindowStack.Add(new Window_RegionalWildlife(map))
                });
            if (HerdsMod.Settings.enableTrailReading)
                result.Add(new OverviewNavigation
                {
                    title = "Trail Leads", detail = "Investigate physical evidence",
                    tooltip = "Combine signs into a trail, assign a tracker, and follow living quarry or regional leads.",
                    accent = new Color(0.43f, 0.55f, 0.29f),
                    action = () => Find.WindowStack.Add(new Window_WildlifeTrailBoard(map))
                });
            if (HerdsMod.Settings.enableWildlifeLandscaping)
                result.Add(new OverviewNavigation
                {
                    title = "Living Landscape",
                    detail = "Places shaped by wildlife",
                    tooltip = "Review persistent game trails, feeding grounds, nesting sites, wallows, shoreline works, and territorial landmarks. Select a place to study or protect it.",
                    accent = new Color(0.36f, 0.58f, 0.31f),
                    action = () => Find.WindowStack.Add(new Window_WildlifeLandscape(map))
                });
            if (HerdsMod.Settings.enableFieldJournal ||
                HerdsMod.Settings.enableDynamicWildlifeOpportunities ||
                HerdsMod.Settings.enableWildlifeMysteries ||
                HerdsMod.Settings.enableWildlifeFolklore)
                result.Add(new OverviewNavigation
                {
                    title = "Field Journal", detail = "Moments, mysteries, and stories",
                    tooltip = "Review the field guide, respond to Wildlife Moments, investigate mysteries, and manage stories.",
                    accent = new Color(0.42f, 0.62f, 0.38f),
                    action = () => Find.WindowStack.Add(new Window_WildlifeFieldJournal(map,
                        map.GetComponent<WildlifeFieldJournalMapComponent>()?.Opportunity != null ? 2 : 0))
                });
            if (HerdsMod.Settings.enableWildlifeSignalCulture)
                result.Add(new OverviewNavigation
                {
                    title = "Signal Guide", detail = "Learn animal calls and meanings",
                    tooltip = "Review local signal dialects, observed meanings, credibility, and colonist understanding.",
                    accent = new Color(0.43f, 0.38f, 0.62f),
                    action = () => Find.WindowStack.Add(new Window_WildlifeSignals(map, null))
                });
            if (HerdsMod.Settings.enableResearchProgression)
                result.Add(new OverviewNavigation
                {
                    title = "Progression", detail = "Research and feature unlocks",
                    tooltip = "See which Wildlife systems are available now and what research unlocks next.",
                    accent = new Color(0.52f, 0.45f, 0.31f),
                    action = () => Find.WindowStack.Add(new Window_WildlifeProgression())
                });
            return result;
        }

        private string NextAction()
        {
            WildlifeOpportunityRecord moment = HerdsMod.Settings.enableDynamicWildlifeOpportunities
                ? map?.GetComponent<WildlifeFieldJournalMapComponent>()?.Opportunity : null;
            if (moment != null)
                return "Wildlife Moment: " +
                    WildlifeFieldJournalMapComponent.OpportunityLabel(moment.kind) +
                    (moment.response == WildlifeMomentResponse.None
                        ? " is waiting for a response in the Field Journal."
                        : " - " + moment.response + " is underway.");
            if (HerdsMod.Settings.enableTrailReading && HerdsDefOf.Herds_WildlifeSign != null)
            {
                int now = Find.TickManager.TicksGame;
                if (now >= nextTrailSuggestionTick)
                {
                    nextTrailSuggestionTick = now + 120;
                    cachedUrgentTrailCount = map?.listerThings
                        .ThingsOfDef(HerdsDefOf.Herds_WildlifeSign)
                        .OfType<WildlifeSign>().Count(sign => sign.predator ||
                            sign.signKind == WildlifeSignKind.BloodTrail) ?? 0;
                }
                if (cachedUrgentTrailCount > 0)
                    return "Suggested next step: review " + cachedUrgentTrailCount +
                        " urgent predator or blood-trail clue" +
                        (cachedUrgentTrailCount == 1 ? "" : "s") +
                        " in Trail Leads.";
            }
            WildlifeMysteryRecord mystery = HerdsMod.Settings.enableWildlifeMysteries
                ? map?.GetComponent<WildlifeMysteryMapComponent>()?.Active : null;
            if (mystery != null)
                return mystery.Solved
                    ? "Suggested next step: choose a response to " + mystery.title + " in the Field Journal."
                    : "Suggested next step: investigate " + mystery.title + " in the Field Journal.";
            if (!WildlifeProgression.Unlocked(WildlifeCapability.BasicHunting)) return "Suggested next step: research Hunting to organize early hunts.";
            if (!DefDatabase<ThingDef>.AllDefsListForReading.Any(def => def.race?.Animal == true && HuntingKnowledgeMapComponent.ColonyExperience(def) > 0f))
                return "Suggested next step: observe wildlife from a manned observation post.";
            if (!WildlifeProgression.Unlocked(WildlifeCapability.Fieldcraft))
                return "Suggested next step: research Organized Hunting for coordinated hunts and tracking equipment.";
            if (!WildlifeProgression.Unlocked(WildlifeCapability.Stewardship))
                return "Suggested next step: develop Wildlife Stewardship to manage populations and migration.";
            return "Suggested next step: review regional trends and respond to changing wildlife populations.";
        }

        private void OpenSuggestedAction()
        {
            WildlifeFieldJournalMapComponent journal =
                map?.GetComponent<WildlifeFieldJournalMapComponent>();
            if (HerdsMod.Settings.enableDynamicWildlifeOpportunities &&
                journal?.Opportunity != null)
            {
                Find.WindowStack.Add(new Window_WildlifeFieldJournal(map, 2));
                return;
            }
            if (HerdsMod.Settings.enableTrailReading && cachedUrgentTrailCount > 0)
            {
                Find.WindowStack.Add(new Window_WildlifeTrailBoard(map));
                return;
            }
            if (HerdsMod.Settings.enableWildlifeMysteries &&
                map?.GetComponent<WildlifeMysteryMapComponent>()?.Active != null)
            {
                Find.WindowStack.Add(new Window_WildlifeFieldJournal(map, 1));
                return;
            }
            bool anyKnowledge = DefDatabase<ThingDef>.AllDefsListForReading.Any(def =>
                def.race?.Animal == true &&
                HuntingKnowledgeMapComponent.ColonyExperience(def) > 0f);
            if (!anyKnowledge)
            {
                Find.WindowStack.Add(new Window_ColonyWildlifeKnowledge());
                return;
            }
            if (HerdsMod.Settings.enableResearchProgression &&
                (!WildlifeProgression.Unlocked(WildlifeCapability.Fieldcraft) ||
                 !WildlifeProgression.Unlocked(WildlifeCapability.Stewardship)))
            {
                Find.WindowStack.Add(new Window_WildlifeProgression());
                return;
            }
            if (HerdsMod.Settings.enableRegionalPopulations)
                Find.WindowStack.Add(new Window_RegionalWildlife(map));
            else
                Find.WindowStack.Add(new Window_ColonyWildlifeKnowledge());
        }
    }
}
