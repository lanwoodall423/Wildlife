using System;
using System.Collections.Generic;
using System.Linq;
using InsightCanvas;
using RimWorld;
using UnityEngine;
using Verse;

namespace Herds
{
    /// <summary>
    /// Shared retained presentations for compact inspect tabs. The data snapshot is rebuilt at a
    /// bounded cadence; the host itself is drawn every frame so focus, keyboard, and overlays remain
    /// owned by InsightCanvas.
    /// </summary>
    internal sealed class WildlifeInspectorCanvas
    {
        private readonly InsightUiDocument document;
        private readonly InsightUiHost host;
        private int lastKey = int.MinValue;
        private int lastBucket = int.MinValue;

        public WildlifeInspectorCanvas(string id)
        {
            document = new InsightUiDocument(id, InsightUi.Empty(id + ".loading"));
            WildlifeUiTheme.Configure(document, false, false);
            host = new InsightUiHost(document);
        }

        public void Draw(Rect rect, int key, Func<InsightUiElement> build)
        {
            int bucket = (Find.TickManager?.TicksGame ?? 0) / 30;
            if (document.Root == null || key != lastKey || bucket != lastBucket)
            {
                document.Root = build == null
                    ? InsightUi.Empty("inspector.empty")
                    : build();
                lastKey = key;
                lastBucket = bucket;
            }
            host.Draw(rect, Time.deltaTime);
        }

        public void PostClose()
        {
            host.PostClose();
            lastKey = int.MinValue;
            lastBucket = int.MinValue;
        }
    }

    internal static class WildlifeInspectorUi
    {
        private static string Get(string key) => WildlifeUiText.Get(key);

        private static InsightUiElement Root(string id, params InsightUiElement[] children)
        {
            return InsightUi.Scroll(id + ".scroll",
                InsightUi.Column(id + ".content", children).SetGap(8f).SetPadding(8f));
        }

        private static InsightUiElement Card(string id, params InsightUiElement[] children)
        {
            return InsightUi.Surface(id, InsightUi.Column(id + ".content", children)
                .SetGap(5f).SetPadding(8f));
        }

        private static InsightUiElement Notice(string id, string title, string body,
            InsightUiCalloutSeverity severity = InsightUiCalloutSeverity.Info)
        {
            return InsightUi.Callout(id, severity, title, body);
        }

        private static InsightUiButton FocusButton(string id, string label, Thing thing)
        {
            if (thing is Pawn pawn && !WildlifeInsightDisclosure.IsPresentationVisible(pawn)) thing = null;
            InsightUiButton button = InsightUi.Button(id, label,
                thing?.Spawned == true ? () => WildlifeUI.Focus(thing) : null);
            button.Enabled = thing?.Spawned == true;
            if (thing?.Spawned == true)
                button.SetTooltip(Get("Wildlife.UI.Inspector.Focus.Help"));
            return button;
        }

        private static string ThingLabel(Thing thing) => thing?.LabelShortCap.ToString() ?? Get("Wildlife.UI.Inspector.Value.None");

        private static Thing ObservedThing(Thing thing)
        {
            return thing is Pawn pawn && !WildlifeInsightDisclosure.IsPresentationVisible(pawn) ? null : thing;
        }

        private static string PawnStatus(Pawn pawn)
        {
            if (pawn == null) return Get("Wildlife.UI.Inspector.Value.Unknown");
            if (pawn.Downed) return Get("Wildlife.UI.Inspector.Status.Downed");
            if (pawn.InMentalState) return pawn.MentalStateDef?.LabelCap.ToString() ?? Get("Wildlife.UI.Inspector.Status.MentalState");
            return pawn.CurJobDef?.LabelCap.ToString() ?? Get("Wildlife.UI.Inspector.Status.Idle");
        }

        private static string DefenseLabel(HerdDefenseMode mode)
        {
            switch (mode)
            {
                case HerdDefenseMode.ProtectYoung: return Get("Wildlife.UI.Inspector.Defense.ProtectYoung");
                case HerdDefenseMode.Flight: return Get("Wildlife.UI.Inspector.Defense.Flight");
                case HerdDefenseMode.Scatter: return Get("Wildlife.UI.Inspector.Defense.Scatter");
                case HerdDefenseMode.Hide: return Get("Wildlife.UI.Inspector.Defense.Hide");
                case HerdDefenseMode.Freeze: return Get("Wildlife.UI.Inspector.Defense.Freeze");
                case HerdDefenseMode.StandGround: return Get("Wildlife.UI.Inspector.Defense.StandGround");
                default: return Get("Wildlife.UI.Inspector.Defense.Inactive");
            }
        }

        private static string StateLabel(HerdSnapshot herd)
        {
            if (herd == null) return Get("Wildlife.UI.Inspector.Value.Unknown");
            if (herd.defenseMode != HerdDefenseMode.None)
                return (herd.simulatedHunt ? Get("Wildlife.UI.Inspector.State.Hunted") : Get("Wildlife.UI.Inspector.State.Threatened"))
                    + " — " + DefenseLabel(herd.defenseMode);
            if (herd.profile?.socialType == PreySocialType.Flock && herd.groundFeeding)
                return Get("Wildlife.UI.Inspector.State.GroundFeeding");
            return herd.profile?.socialType == PreySocialType.Flock
                ? Get("Wildlife.UI.Inspector.State.Flocking")
                : Get("Wildlife.UI.Inspector.State.Calm");
        }

        private static string StrategyLabel(PreyDefenseStrategy strategy) =>
            strategy == PreyDefenseStrategy.ProtectYoung ? Get("Wildlife.UI.Inspector.Defense.ProtectYoung") :
            strategy == PreyDefenseStrategy.StandGround ? Get("Wildlife.UI.Inspector.Defense.StandGround") :
            WildlifeUiText.EnumValue(strategy);

        private static string RefugeLabel(PreyRefugePreference preference) =>
            preference == PreyRefugePreference.TreesAndVegetation ? Get("Wildlife.UI.Inspector.Refuge.TreesVegetation") :
            preference == PreyRefugePreference.TreesAndDens ? Get("Wildlife.UI.Inspector.Refuge.TreesDens") :
            WildlifeUiText.EnumValue(preference);

        public static InsightUiElement Herd(Pawn selected, HerdMapComponent component)
        {
            if (selected == null)
                return Root("herd.inspector", Notice("herd.unavailable", Get("Wildlife.UI.Inspector.Title"),
                    Get("Wildlife.UI.Inspector.Unavailable")));

            HerdSnapshot herd = component?.HerdFor(selected);
            PreyProfile profile = herd?.profile ?? PreyProfileDatabase.For(selected.def);
            int knowledge = HuntingKnowledgeMapComponent.ColonyLevel(selected.def);
            float experience = HuntingKnowledgeMapComponent.ColonyExperience(selected.def);
            bool identity = WildlifeTabKnowledgePolicy.RevealsIdentity(knowledge);
            bool observed = component?.IsObserved(selected) == true;
            bool soloBird = profile?.socialType == PreySocialType.Flock && (herd?.members.Count ?? 1) <= 1;
            bool solitary = profile?.socialType == PreySocialType.Solitary || soloBird || herd == null;
            string title = identity ? selected.LabelShortCap.ToString() : Get("Wildlife.UI.Inspector.UnknownAnimal");
            List<InsightUiElement> content = new List<InsightUiElement>
            {
                InsightUi.SectionHeader("herd.header", title,
                    Get("Wildlife.UI.Inspector.Herd.Subtitle")),
                InsightUi.StatRow("herd.knowledge", Get("Wildlife.UI.Inspector.Knowledge"),
                    HuntingKnowledgeMapComponent.LevelLabel(knowledge)),
                InsightUi.Meter("herd.knowledge-meter", experience, 1200f)
                    .SetLabel(Get("Wildlife.UI.Inspector.KnowledgeProgress"))
            };

            if (!identity)
            {
                content.Add(Notice("herd.unknown", Get("Wildlife.UI.Inspector.UnknownAnimal"),
                    Get("Wildlife.UI.Inspector.Knowledge.IdentityHelp")));
                return Root("herd.inspector", content.ToArray());
            }

            content.Add(Card("herd.identity",
                InsightUi.StatRow("herd.identity.species", Get("Wildlife.UI.Inspector.Species"), selected.def.LabelCap.ToString()),
                InsightUi.StatRow("herd.identity.observation", Get("Wildlife.UI.Inspector.Observation"),
                    observed ? Get("Wildlife.UI.Inspector.Observed") : Get("Wildlife.UI.Inspector.NotObserved")),
                InsightUi.StatRow("herd.identity.type", Get("Wildlife.UI.Inspector.Lifestyle"),
                    WildlifeUiText.EnumValue(profile?.socialType ?? PreySocialType.Solitary))));

            if (!observed || !WildlifeTabKnowledgePolicy.RevealsSocialStructure(knowledge))
            {
                content.Add(Notice("herd.gated", Get("Wildlife.UI.Inspector.DetailsLocked"),
                    !observed ? Get("Wildlife.UI.Inspector.ObservationHelp") :
                    Get("Wildlife.UI.Inspector.Knowledge.SocialHelp")));
                return Root("herd.inspector", content.ToArray());
            }

            if (herd == null)
            {
                content.Add(Card("herd.independent",
                    InsightUi.SectionHeader("herd.independent.header", Get("Wildlife.UI.Inspector.Independent")),
                    InsightUi.StatRow("herd.independent.strategy", Get("Wildlife.UI.Inspector.Survival"),
                        WildlifeTabKnowledgePolicy.RevealsBehavior(knowledge) ? StrategyLabel(profile.defenseStrategy) : Get("Wildlife.UI.Inspector.Value.Unknown")),
                    InsightUi.StatRow("herd.independent.refuge", Get("Wildlife.UI.Inspector.Refuge"),
                        WildlifeTabKnowledgePolicy.RevealsHabitat(knowledge) ? RefugeLabel(profile.refugePreference) : Get("Wildlife.UI.Inspector.Value.Unknown"))));
                return Root("herd.inspector", content.ToArray());
            }

            List<Pawn> visibleMembers = (herd.members ?? new List<Pawn>())
                .Where(WildlifeInsightDisclosure.IsPresentationVisible).ToList();
            Pawn visibleLeader = herd.leader != null && WildlifeInsightDisclosure.IsPresentationVisible(herd.leader)
                ? herd.leader : null;
            Pawn visibleSentinel = herd.sentinel != null && WildlifeInsightDisclosure.IsPresentationVisible(herd.sentinel)
                ? herd.sentinel : null;
            string leaderLabel = visibleLeader == null ? Get("Wildlife.UI.Inspector.Value.Unknown") : ThingLabel(visibleLeader);
            string sentinelLabel = visibleSentinel == null ? Get("Wildlife.UI.Inspector.Value.Unknown") : ThingLabel(visibleSentinel);

            content.Add(Card("herd.social",
                InsightUi.SectionHeader("herd.social.header", soloBird ? Get("Wildlife.UI.Inspector.SoloBird") :
                    solitary ? Get("Wildlife.UI.Inspector.Individual") :
                    profile?.socialType == PreySocialType.Flock ? Get("Wildlife.UI.Inspector.Flock") : Get("Wildlife.UI.Inspector.SocialGroup")),
                solitary
                    ? InsightUi.StatRow("herd.social.lifestyle", Get("Wildlife.UI.Inspector.Lifestyle"),
                        soloBird ? Get("Wildlife.UI.Inspector.SoloBird") : Get("Wildlife.UI.Inspector.Solitary"))
                    : FocusButton("herd.social.leader", Get("Wildlife.UI.Inspector.Leader") + ": " + leaderLabel, visibleLeader),
                !solitary ? FocusButton("herd.social.sentinel", Get("Wildlife.UI.Inspector.Sentinel") + ": " + sentinelLabel, visibleSentinel) :
                    InsightUi.StatRow("herd.social.strategy", Get("Wildlife.UI.Inspector.Survival"),
                        WildlifeTabKnowledgePolicy.RevealsBehavior(knowledge) ? StrategyLabel(profile.defenseStrategy) : Get("Wildlife.UI.Inspector.Value.Unknown"))));

            bool revealBehavior = WildlifeTabKnowledgePolicy.RevealsBehavior(knowledge);
            content.Add(Card("herd.behavior",
                InsightUi.SectionHeader("herd.behavior.header", Get("Wildlife.UI.Inspector.CurrentBehavior")),
                InsightUi.StatRow("herd.behavior.state", Get("Wildlife.UI.Inspector.State"), revealBehavior ? StateLabel(herd) : Get("Wildlife.UI.Inspector.Value.Unknown")),
                InsightUi.StatRow("herd.behavior.vigilance", Get("Wildlife.UI.Inspector.Vigilance"),
                    revealBehavior ? WildlifeInsightDisclosure.InspectorVigilance(selected, component, knowledge) : Get("Wildlife.UI.Inspector.Value.Unknown")),
                InsightUi.StatRow("herd.behavior.personality", Get("Wildlife.UI.Inspector.Personality"),
                    revealBehavior ? WildlifeInsightDisclosure.InspectorPersonality(selected, component, knowledge) : Get("Wildlife.UI.Inspector.Value.Unknown"))));

            if (WildlifeTabKnowledgePolicy.RevealsHabitat(knowledge))
            {
                Thing home = component.HomeFor(selected);
                content.Add(Card("herd.habitat",
                    InsightUi.SectionHeader("herd.habitat.header", Get("Wildlife.UI.Inspector.HomeSafety")),
                    FocusButton("herd.habitat.home", Get("Wildlife.UI.Inspector.Home") + ": " + ThingLabel(home), home),
                    FocusButton("herd.habitat.threat", Get("Wildlife.UI.Inspector.Threat") + ": " + ThingLabel(ObservedThing(herd.defenseThreat)), ObservedThing(herd.defenseThreat)),
                    FocusButton("herd.habitat.pen", Get("Wildlife.UI.Inspector.Enclosure") + ": " + ThingLabel(herd.pen?.parent), herd.pen?.parent),
                    InsightUi.StatRow("herd.habitat.season", Get("Wildlife.UI.Inspector.Season"),
                        selected.Map.GetComponent<WildlifeLivesMapComponent>()?.Lifecycle(selected) ?? Get("Wildlife.UI.Inspector.Value.Unknown")),
                    InsightUi.StatRow("herd.habitat.landscape", Get("Wildlife.UI.Inspector.Landscape"),
                        WildlifeLandscapeAPI.RoleSummary(selected))));
            }

            if (HerdsMod.Settings.enableWildlifeSignalCulture && WildlifeTabKnowledgePolicy.RevealsSignals(knowledge))
            {
                WildlifeSignalCultureMapComponent signals = selected.Map.GetComponent<WildlifeSignalCultureMapComponent>();
                content.Add(Card("herd.signals",
                    InsightUi.SectionHeader("herd.signals.header", Get("Wildlife.UI.Inspector.LocalSignals")),
                    InsightUi.Label("herd.signals.summary", signals?.SignalSummary(selected.def) ?? Get("Wildlife.UI.Inspector.NoSignals"), InsightUiTextStyle.Body)));
            }

            if (HerdsMod.Settings.enableAnimalMemory && WildlifeTabKnowledgePolicy.RevealsIndividualMemory(knowledge))
            {
                WildlifeMemoryMapComponent memories = selected.Map.GetComponent<WildlifeMemoryMapComponent>();
                content.Add(Card("herd.memory",
                    InsightUi.SectionHeader("herd.memory.header", Get("Wildlife.UI.Inspector.IndividualMemory")),
                    InsightUi.Label("herd.memory.summary", memories?.Summary(selected) ?? Get("Wildlife.UI.Inspector.NoMemories"), InsightUiTextStyle.Body)));
            }

            content.Add(Card("herd.members",
                InsightUi.SectionHeader("herd.members.header", solitary ? Get("Wildlife.UI.Inspector.CurrentActivity") :
                    Get("Wildlife.UI.Inspector.GroupMembers")),
                InsightUi.VirtualList("herd.members.list", visibleMembers.Count, 34f, index =>
                {
                    Pawn member = visibleMembers[index];
                    return FocusButton("herd.member." + index,
                        member.LabelShortCap + " — " + PawnStatus(member), member);
                })));
            return Root("herd.inspector", content.ToArray());
        }

        public static InsightUiElement Pen(CompAnimalPenMarker pen, IReadOnlyList<HerdSnapshot> herds)
        {
            List<InsightUiElement> content = new List<InsightUiElement>
            {
                InsightUi.SectionHeader("pen.header", Get("Wildlife.UI.Inspector.PenTitle"), pen?.RenamableLabel ?? Get("Wildlife.UI.Inspector.Value.Unknown"))
            };
            if (herds == null || herds.Count == 0)
            {
                content.Add(Notice("pen.empty", Get("Wildlife.UI.Inspector.NoGroups"), Get("Wildlife.UI.Inspector.NoGroups.Help")));
                return Root("pen.inspector", content.ToArray());
            }
            content.Add(InsightUi.StatRow("pen.group-count", Get("Wildlife.UI.Inspector.GroupCount"), herds.Count.ToString()));
            for (int i = 0; i < herds.Count; i++)
            {
                HerdSnapshot herd = herds[i];
                List<Pawn> visibleMembers = (herd.members ?? new List<Pawn>())
                    .Where(WildlifeInsightDisclosure.IsPresentationVisible).ToList();
                Pawn visibleLeader = herd.leader != null && WildlifeInsightDisclosure.IsPresentationVisible(herd.leader)
                    ? herd.leader : null;
                List<InsightUiElement> group = new List<InsightUiElement>
                {
                    InsightUi.SectionHeader("pen.group." + i + ".header", herd.Label,
                        visibleMembers.Count + " " + Get("Wildlife.UI.Inspector.Members")),
                    InsightUi.StatRow("pen.group." + i + ".state", Get("Wildlife.UI.Inspector.State"), StateLabel(herd)),
                    FocusButton("pen.group." + i + ".leader", Get("Wildlife.UI.Inspector.Leader") + ": " +
                        (visibleLeader == null ? Get("Wildlife.UI.Inspector.Value.Unknown") : ThingLabel(visibleLeader)), visibleLeader),
                    InsightUi.VirtualList("pen.group." + i + ".members", visibleMembers.Count, 32f, index =>
                    {
                        Pawn member = visibleMembers[index];
                        return FocusButton("pen.group." + i + ".member." + index,
                            member.LabelShortCap + " — " + PawnStatus(member), member);
                    })
                };
                content.Add(Card("pen.group." + i, group.ToArray()));
            }
            return Root("pen.inspector", content.ToArray());
        }

        public static InsightUiElement Hiding(Thing refuge, IReadOnlyList<Pawn> hidden, IReadOnlyList<Pawn> home)
        {
            List<Pawn> visibleHidden = (hidden ?? Array.Empty<Pawn>())
                .Where(WildlifeInsightDisclosure.IsPresentationVisible).ToList();
            List<Pawn> visibleHome = (home ?? Array.Empty<Pawn>())
                .Where(WildlifeInsightDisclosure.IsPresentationVisible).ToList();
            List<InsightUiElement> content = new List<InsightUiElement>
            {
                InsightUi.SectionHeader("refuge.header", refuge is Plant
                    ? Get("Wildlife.UI.Inspector.TreeHome") : Get("Wildlife.UI.Inspector.Refuge"),
                    refuge?.LabelCap.ToString() ?? Get("Wildlife.UI.Inspector.Value.Unknown")),
                InsightUi.StatRow("refuge.hidden-count", Get("Wildlife.UI.Inspector.CurrentlyHiding"), visibleHidden.Count.ToString()),
                InsightUi.VirtualList("refuge.hidden", visibleHidden.Count, 32f, index =>
                {
                    Pawn pawn = visibleHidden[index];
                    return FocusButton("refuge.hidden." + index, pawn.LabelShortCap + " — " + pawn.def.LabelCap, pawn);
                }),
                InsightUi.StatRow("refuge.home-count", Get("Wildlife.UI.Inspector.HomeAnimals"), visibleHome.Count.ToString()),
                InsightUi.VirtualList("refuge.home", visibleHome.Count, 32f, index =>
                {
                    Pawn pawn = visibleHome[index];
                    return FocusButton("refuge.home." + index, pawn.LabelShortCap + " — " + pawn.def.LabelCap, pawn);
                })
            };
            if ((hidden?.Count ?? 0) > visibleHidden.Count || (home?.Count ?? 0) > visibleHome.Count)
                content.Insert(1, Notice("refuge.gated", "Additional wildlife", "Other wildlife activity is recorded here, but individual identities are not currently available."));
            return Root("refuge.inspector", content.ToArray());
        }

        public static InsightUiElement Memory(Pawn animal, WildlifeMemoryMapComponent component, Action openTimeline)
        {
            if (animal == null || component == null)
                return Root("memory.inspector", Notice("memory.unavailable", Get("Wildlife.UI.Memory.Title"), Get("Wildlife.UI.Inspector.Unavailable")));

            List<AnimalColonistMemory> memories = component.Memories.Where(value => value?.animal == animal)
                .OrderByDescending(value => value.lastTick).ToList();
            List<AnimalSocialMemory> social = component.SocialFor(animal)
                .Where(value => value?.otherAnimal != null)
                .OrderByDescending(value => WildlifeMemoryMapComponent.SocialStrength(value)).ToList();
            List<InsightUiElement> content = new List<InsightUiElement>
            {
                InsightUi.SectionHeader("memory.header", AnimalMemoryPresentation.DisplayName(animal), Get("Wildlife.UI.Memory.Subtitle")),
                InsightUi.Button("memory.timeline", Get("Wildlife.UI.Memory.OpenTimeline"), openTimeline),
                Card("memory.summary",
                    InsightUi.StatRow("memory.summary.temperament", Get("Wildlife.UI.Memory.Temperament"), WildlifeLifeUtility.PersonalityLabel(animal)),
                    InsightUi.StatRow("memory.summary.colonists", Get("Wildlife.UI.Memory.ColonistCount"), memories.Count.ToString()),
                    InsightUi.StatRow("memory.summary.animals", Get("Wildlife.UI.Memory.AnimalCount"), social.Count.ToString()),
                    InsightUi.Label("memory.summary.relationship", animal.Map.GetComponent<WildlifeLivesMapComponent>()?.RelationshipSummary(animal) ?? Get("Wildlife.UI.Memory.NoColonists"), InsightUiTextStyle.Body))
            };

            if (social.Count == 0)
                content.Add(Notice("memory.social.empty", Get("Wildlife.UI.Memory.Relationships"), Get("Wildlife.UI.Memory.NoRelationships")));
            else
                content.Add(Card("memory.social",
                    InsightUi.SectionHeader("memory.social.header", Get("Wildlife.UI.Memory.Relationships")),
                    InsightUi.VirtualList("memory.social.list", social.Count, 46f, index =>
                    {
                        AnimalSocialMemory relationship = social[index];
                        InsightUiElement row = InsightUi.Column("memory.social." + index + ".content",
                            FocusButton("memory.social." + index + ".focus", AnimalMemoryPresentation.DisplayName(relationship.otherAnimal), relationship.otherAnimal),
                            InsightUi.StatRow("memory.social." + index + ".metrics", Get("Wildlife.UI.Memory.BondFearRivalry"),
                                relationship.bond.ToStringPercent() + " / " + relationship.fear.ToStringPercent() + " / " + relationship.rivalry.ToStringPercent()),
                            InsightUi.Label("memory.social." + index + ".kind", component.SocialRelationship(animal, relationship.otherAnimal).CapitalizeFirst(), InsightUiTextStyle.Caption));
                        return InsightUi.Surface("memory.social." + index, row.SetGap(2f).SetPadding(5f));
                    })));

            if (memories.Count == 0)
                content.Add(Notice("memory.colonists.empty", Get("Wildlife.UI.Memory.Colonists"), Get("Wildlife.UI.Memory.NoColonists")));
            else
                content.Add(Card("memory.colonists",
                    InsightUi.SectionHeader("memory.colonists.header", Get("Wildlife.UI.Memory.Colonists")),
                    InsightUi.VirtualList("memory.colonists.list", memories.Count, 48f, index =>
                    {
                        AnimalColonistMemory memory = memories[index];
                        return Card("memory.colonist." + index,
                            FocusButton("memory.colonist." + index + ".focus", ThingLabel(memory.colonist), memory.colonist),
                            InsightUi.StatRow("memory.colonist." + index + ".metrics", Get("Wildlife.UI.Memory.TrustFearHostility"),
                                memory.trust.ToStringPercent() + " / " + memory.fear.ToStringPercent() + " / " + memory.hostility.ToStringPercent()),
                            InsightUi.StatRow("memory.colonist." + index + ".latest", Get("Wildlife.UI.Memory.Latest"), memory.lastEvent ?? Get("Wildlife.UI.Memory.NoEvent")));
                    })));

            List<AnimalMemoryDisplayRow> rows = AnimalMemoryPresentation.Rows(animal, component);
            content.Add(Card("memory.timeline.preview",
                InsightUi.SectionHeader("memory.timeline.preview.header", Get("Wildlife.UI.Memory.Timeline")),
                rows.Count == 0
                    ? Notice("memory.timeline.empty", Get("Wildlife.UI.Memory.NoEvents"), Get("Wildlife.UI.Memory.NoEvents.Help"))
                    : InsightUi.VirtualList("memory.timeline.rows", Mathf.Min(rows.Count, 12), 44f, index =>
                    {
                        AnimalMemoryDisplayRow row = rows[index];
                        InsightUiElement item = InsightUi.Column("memory.timeline." + index + ".content",
                            InsightUi.StatRow("memory.timeline." + index + ".who", Get("Wildlife.UI.Memory.Subject"),
                                row.otherAnimal ? AnimalMemoryPresentation.DisplayName(row.other) : ThingLabel(row.other)),
                            InsightUi.StatRow("memory.timeline." + index + ".event", Get("Wildlife.UI.Memory.Event"), row.text),
                            InsightUi.Label("memory.timeline." + index + ".age",
                                Mathf.Max(0, Find.TickManager.TicksGame - row.tick).ToStringTicksToPeriod() + " " + Get("Wildlife.UI.Memory.Ago"), InsightUiTextStyle.Caption));
                        return InsightUi.Surface("memory.timeline." + index, item.SetGap(2f).SetPadding(5f));
                    })));

            return Root("memory.inspector", content.ToArray());
        }

        public static InsightUiElement MemoryTimeline(Pawn animal, WildlifeMemoryMapComponent component,
            bool socialWeb, Action showTimeline, Action showSocial)
        {
            List<InsightUiElement> header = new List<InsightUiElement>
            {
                InsightUi.Breadcrumbs("memory.window.breadcrumbs", Get("Wildlife.UI.Memory.Title")),
                InsightUi.SectionHeader("memory.window.header", AnimalMemoryPresentation.DisplayName(animal), Get("Wildlife.UI.Memory.Subtitle")),
                InsightUi.Wrap("memory.window.navigation",
                    InsightUi.Button("memory.window.timeline", Get("Wildlife.UI.Memory.Timeline"), showTimeline),
                    InsightUi.Button("memory.window.social", Get("Wildlife.UI.Memory.SocialWeb"), showSocial)).SetGap(6f)
            };
            if (!socialWeb)
            {
                List<AnimalMemoryDisplayRow> rows = AnimalMemoryPresentation.Rows(animal, component);
                InsightUiElement list = rows.Count == 0
                    ? Notice("memory.window.empty", Get("Wildlife.UI.Memory.NoEvents"), Get("Wildlife.UI.Memory.NoEvents.Help"))
                    : InsightUi.VirtualList("memory.window.rows", rows.Count, 68f, index =>
                    {
                        AnimalMemoryDisplayRow row = rows[index];
                        InsightUiElement body = InsightUi.Column("memory.window.row." + index + ".body",
                            InsightUi.StatRow("memory.window.row." + index + ".subject", Get("Wildlife.UI.Memory.Subject"),
                                row.otherAnimal ? AnimalMemoryPresentation.DisplayName(row.other) : ThingLabel(row.other)),
                            InsightUi.StatRow("memory.window.row." + index + ".event", Get("Wildlife.UI.Memory.Event"), row.text),
                            InsightUi.StatRow("memory.window.row." + index + ".age", Get("Wildlife.UI.Memory.Time"),
                                Mathf.Max(0, Find.TickManager.TicksGame - row.tick).ToStringTicksToPeriod() + " " + Get("Wildlife.UI.Memory.Ago")));
                        InsightUiButton focus = FocusButton("memory.window.row." + index + ".focus", Get("Wildlife.UI.Memory.Focus"), row.other);
                        return Card("memory.window.row." + index, body, focus);
                    });
                header.Add(list);
            }
            else
            {
                List<AnimalSocialMemory> relationships = component?.SocialFor(animal)
                    .Where(value => value?.otherAnimal != null)
                    .OrderByDescending(value => WildlifeMemoryMapComponent.SocialStrength(value)).Take(16).ToList()
                    ?? new List<AnimalSocialMemory>();
                header.Add(relationships.Count == 0
                    ? Notice("memory.window.social.empty", Get("Wildlife.UI.Memory.Relationships"), Get("Wildlife.UI.Memory.NoRelationships"))
                    : InsightUi.VirtualList("memory.window.social.rows", relationships.Count, 92f, index =>
                    {
                        AnimalSocialMemory relationship = relationships[index];
                        return Card("memory.window.social." + index,
                            FocusButton("memory.window.social." + index + ".focus",
                                AnimalMemoryPresentation.DisplayName(relationship.otherAnimal), relationship.otherAnimal),
                            InsightUi.StatRow("memory.window.social." + index + ".relationship", Get("Wildlife.UI.Memory.Relationship"),
                                component.SocialRelationship(animal, relationship.otherAnimal).CapitalizeFirst()),
                            InsightUi.StatRow("memory.window.social." + index + ".metrics", Get("Wildlife.UI.Memory.BondFearRivalry"),
                                relationship.bond.ToStringPercent() + " / " + relationship.fear.ToStringPercent() + " / " + relationship.rivalry.ToStringPercent()),
                            InsightUi.StatRow("memory.window.social." + index + ".latest", Get("Wildlife.UI.Memory.Latest"),
                                relationship.lastEvent ?? Get("Wildlife.UI.Memory.NoEvent")));
                    }));
            }
            return Root("memory.window", header.ToArray());
        }

        public static InsightUiElement SpeciesKnowledge(Pawn colonist, HuntingKnowledgeMapComponent component)
        {
            List<ColonistSpeciesKnowledgeRecord> records = component?.ForColonist(colonist)
                .Where(record => record?.species != null).OrderByDescending(record => record.experience)
                .ThenBy(record => record.species.label).ToList() ?? new List<ColonistSpeciesKnowledgeRecord>();
            List<InsightUiElement> content = new List<InsightUiElement>
            {
                InsightUi.Breadcrumbs("knowledge.breadcrumbs", Get("Wildlife.UI.Knowledge.Breadcrumb")),
                InsightUi.SectionHeader("knowledge.header", colonist?.LabelShortCap.ToString() ?? Get("Wildlife.UI.Inspector.Value.Unknown"),
                    Get("Wildlife.UI.Knowledge.Subtitle")),
                InsightUi.StatRow("knowledge.count", Get("Wildlife.UI.Knowledge.SpeciesCount"), records.Count.ToString())
            };
            if (records.Count == 0)
            {
                content.Add(Notice("knowledge.empty", Get("Wildlife.UI.Knowledge.Empty"), Get("Wildlife.UI.Knowledge.Empty.Help")));
            }
            else
            {
                content.Add(InsightUi.VirtualList("knowledge.records", records.Count, 86f, index =>
                {
                    ColonistSpeciesKnowledgeRecord record = records[index];
                    int level = component.Level(colonist, record.species);
                    return Card("knowledge.record." + index,
                        InsightUi.StatRow("knowledge.record." + index + ".species", Get("Wildlife.UI.Inspector.Species"), record.species.LabelCap.ToString()),
                        InsightUi.StatRow("knowledge.record." + index + ".level", Get("Wildlife.UI.Inspector.Knowledge"), HuntingKnowledgeMapComponent.LevelLabel(level)),
                        InsightUi.StatRow("knowledge.record." + index + ".xp", Get("Wildlife.UI.Knowledge.Experience"), record.experience.ToString("0") + " XP"),
                        InsightUi.Progress("knowledge.record." + index + ".progress", Mathf.Clamp01(record.experience / 1200f)));
                }));
            }
            return Root("knowledge.window", content.ToArray());
        }
    }
}
