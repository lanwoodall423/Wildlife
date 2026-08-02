using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Herds
{
    public sealed class JobDriver_RetellWildlifeStory : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) =>
            pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            yield return Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.OnCell);
            Toil share = Toils_General.Wait(900, TargetIndex.None);
            share.socialMode = RandomSocialMode.Off;
            share.tickAction = () =>
            {
                if (share.actor.IsHashIntervalTick(240))
                    MoteMaker.ThrowText(share.actor.DrawPos, share.actor.Map,
                        job.count == 1 ? Rand.Element("Long ago...", "The tracks turned north...", "We still remember...") :
                            Rand.Element("What happened then?", "I've heard that story.", "And the animal returned?"));
            };
            yield return share;
            Toil finish = new Toil
            {
                initAction = () =>
                {
                    if (job.count == 1)
                        pawn.Map?.GetComponent<WildlifeMemoryMapComponent>()?.CompleteRetelling(pawn);
                },
                defaultCompleteMode = ToilCompleteMode.Instant
            };
            yield return finish;
        }
    }

    public sealed class JobDriver_WildlifeCeremonyGather : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) =>
            pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            yield return Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.OnCell);
            Toil gather = Toils_General.Wait(1500, TargetIndex.None);
            gather.socialMode = RandomSocialMode.Off;
            gather.tickAction = () =>
            {
                if (gather.actor.IsHashIntervalTick(300))
                    MoteMaker.ThrowText(gather.actor.DrawPos, gather.actor.Map,
                        Rand.Element("Remember the wild.", "Watch. Learn. Protect.", "Honor the old stories."));
            };
            yield return gather;
            yield return new Toil
            {
                initAction = () =>
                {
                    if (job.count == 1)
                        pawn.Map?.GetComponent<WildlifeMemoryMapComponent>()?.CompletePendingCeremony();
                },
                defaultCompleteMode = ToilCompleteMode.Instant
            };
        }
    }

    internal sealed class AnimalMemoryDisplayRow
    {
        public Pawn other;
        public int tick;
        public string text;
        public string tooltip;
        public bool negative;
        public bool otherAnimal;
        public Pawn cause;
    }

    internal static class AnimalMemoryPresentation
    {
        public static string DisplayName(Pawn pawn)
        {
            if (pawn == null) return "Unknown animal";
            if (pawn.Name != null) return pawn.LabelShortCap;
            string age = pawn.ageTracker?.Adult == false ? "young" :
                pawn.gender == Gender.Male ? "male" :
                pawn.gender == Gender.Female ? "female" : "";
            string personality = WildlifeLifeUtility.PersonalityLabel(pawn);
            string result = (personality == "Unrecorded" ? "" : personality + " ") +
                (age.NullOrEmpty() ? "" : age + " ") + pawn.def.label;
            return result.CapitalizeFirst();
        }

        public static List<AnimalMemoryDisplayRow> Rows(Pawn animal,
            WildlifeMemoryMapComponent component)
        {
            List<AnimalMemoryDisplayRow> rows = new List<AnimalMemoryDisplayRow>();
            if (animal == null || component == null) return rows;
            foreach (AnimalColonistMemory memory in component.Memories.Where(value =>
                value?.animal == animal))
                foreach (AnimalMemoryEvent entry in memory.events)
                    rows.Add(new AnimalMemoryDisplayRow
                    {
                        other = entry.cause ?? memory.colonist,
                        cause = entry.cause,
                        tick = entry.tick,
                        text = WildlifeMemoryMapComponent.EventLabel(entry.kind)
                            .CapitalizeFirst() + ".",
                        negative = entry.kind == AnimalMemoryKind.Wounded ||
                            entry.kind == AnimalMemoryKind.Hunted ||
                            entry.kind == AnimalMemoryKind.KinKilled ||
                            entry.kind == AnimalMemoryKind.NegativeInteraction ||
                            entry.kind == AnimalMemoryKind.Gunfire ||
                            entry.kind == AnimalMemoryKind.TrapEscaped ||
                            entry.kind == AnimalMemoryKind.BaitDanger ||
                            entry.kind == AnimalMemoryKind.WarningLearned ||
                            entry.kind == AnimalMemoryKind.Frightened,
                        otherAnimal = (entry.cause ?? memory.colonist)?.RaceProps?.Animal == true,
                        tooltip = "Animal memory\nTrust: " +
                            memory.trust.ToStringPercent() + "\nFear: " +
                            memory.fear.ToStringPercent() + "\nHostility: " +
                            memory.hostility.ToStringPercent() +
                            (entry.cause == null ? string.Empty : "\nCause: " + entry.cause.LabelShortCap)
                    });
            foreach (AnimalSocialMemory memory in component.SocialMemories.Where(value =>
                value?.animal == animal))
                foreach (AnimalSocialMemoryEvent entry in memory.events)
                    rows.Add(new AnimalMemoryDisplayRow
                    {
                        other = memory.otherAnimal,
                        cause = entry.cause,
                        tick = entry.tick,
                        text = WildlifeMemoryMapComponent.SocialEventLabel(entry.kind)
                            .CapitalizeFirst() + ".",
                        negative = entry.kind == AnimalSocialMemoryKind.Rivalry ||
                            entry.kind == AnimalSocialMemoryKind.Fought ||
                            entry.kind == AnimalSocialMemoryKind.PackMemberKilled,
                        otherAnimal = true,
                        tooltip = "Animal relationship\nBond: " +
                            memory.bond.ToStringPercent() + "\nFear: " +
                            memory.fear.ToStringPercent() + "\nRivalry: " +
                            memory.rivalry.ToStringPercent() +
                            (entry.cause == null ? string.Empty : "\nCause: " + entry.cause.LabelShortCap)
                    });
            return rows.OrderByDescending(value => value.tick).ToList();
        }

        public static void DrawTimeline(Rect rect, ref Vector2 scroll,
            List<AnimalMemoryDisplayRow> rows)
        {
            Rect view = new Rect(0f, 0f, rect.width - 18f,
                Mathf.Max(rect.height, rows.Count * 58f));
            Widgets.BeginScrollView(rect, ref scroll, view);
            for (int i = 0; i < rows.Count; i++)
            {
                AnimalMemoryDisplayRow row = rows[i];
                Rect card = new Rect(0f, i * 58f, view.width, 52f);
                Widgets.DrawMenuSection(card);
                Widgets.DrawBoxSolid(new Rect(card.x, card.y, 5f, card.height),
                    row.negative ? new Color(0.82f, 0.28f, 0.22f) :
                    row.otherAnimal ? new Color(0.35f, 0.64f, 0.78f) :
                    new Color(0.32f, 0.68f, 0.42f));
                GUI.color = row.negative ? new Color(1f, 0.58f, 0.52f) :
                    new Color(0.62f, 0.92f, 0.68f);
                Widgets.Label(new Rect(12f, card.y + 7f, card.width * 0.31f, 22f),
                    row.otherAnimal ? DisplayName(row.other) :
                        row.other?.LabelShortCap ?? "Unknown cause");
                GUI.color = Color.white;
                Widgets.Label(new Rect(card.width * 0.33f, card.y + 7f,
                    card.width * 0.43f, 36f), row.text);
                Text.Anchor = TextAnchor.MiddleRight;
                GUI.color = Color.gray;
                Widgets.Label(new Rect(card.width * 0.76f, card.y + 6f,
                    card.width * 0.22f, 38f),
                    (Find.TickManager.TicksGame - row.tick).ToStringTicksToPeriod() +
                    " ago");
                GUI.color = Color.white;
                Text.Anchor = TextAnchor.UpperLeft;
                if (row.other?.Spawned == true && Widgets.ButtonInvisible(card))
                {
                    Find.Selector.ClearSelection();
                    Find.Selector.Select(row.other);
                    CameraJumper.TryJump(row.other);
                }
                TooltipHandler.TipRegion(card, row.tooltip +
                    (row.other?.Spawned == true ? "\nClick to select." : ""));
            }
            Widgets.EndScrollView();
        }

        public static void DrawSocialWeb(Rect rect, Pawn animal,
            WildlifeMemoryMapComponent component)
        {
            List<AnimalSocialMemory> relationships = component?.SocialFor(animal)
                .Where(value => value?.otherAnimal != null).Take(8).ToList() ??
                new List<AnimalSocialMemory>();
            Widgets.DrawMenuSection(rect);
            Rect summary = new Rect(rect.x + 14f, rect.y + 10f,
                rect.width - 28f, 42f);
            Widgets.Label(summary, relationships.Count == 0
                ? "No lasting animal relationships have formed."
                : relationships.Count + (relationships.Count == 1
                    ? " remembered relationship. " : " remembered relationships. ") +
                  "Line color shows affection, fear, or rivalry; thickness shows strength.");

            Rect graph = new Rect(rect.x + 12f, rect.y + 54f,
                rect.width - 24f, Mathf.Min(330f, rect.height - 96f));
            Vector2 center = graph.center;
            center.y += 4f;
            Rect centerNode = new Rect(center.x - 82f, center.y - 31f, 164f, 62f);

            for (int i = 0; i < relationships.Count; i++)
            {
                AnimalSocialMemory relationship = relationships[i];
                float angle = -Mathf.PI / 2f +
                    Mathf.PI * 2f * i / Mathf.Max(1, relationships.Count);
                Vector2 position = center + new Vector2(Mathf.Cos(angle) *
                    Mathf.Min(270f, graph.width * 0.36f), Mathf.Sin(angle) *
                    Mathf.Min(112f, graph.height * 0.34f));
                Color color = WildlifeMemoryMapComponent.SocialColor(relationship);
                color.a = 0.82f;
                Widgets.DrawLine(center, position, color,
                    1.5f + WildlifeMemoryMapComponent.SocialStrength(relationship) * 4f);
                Rect node = new Rect(position.x - 75f, position.y - 25f, 150f, 50f);
                Widgets.DrawBoxSolid(node, new Color(color.r * 0.22f,
                    color.g * 0.22f, color.b * 0.22f, 0.96f));
                Widgets.DrawBox(node, 1, Texture2D.whiteTexture);
                Rect icon = new Rect(node.x + 6f, node.y + 7f, 34f, 34f);
                if (relationship.otherAnimal?.def?.uiIcon != null)
                    GUI.DrawTexture(icon, relationship.otherAnimal.def.uiIcon,
                        ScaleMode.ScaleToFit);
                Widgets.Label(new Rect(node.x + 45f, node.y + 5f,
                    node.width - 50f, 22f), DisplayName(relationship.otherAnimal));
                GUI.color = color;
                Text.Font = GameFont.Tiny;
                Widgets.Label(new Rect(node.x + 45f, node.y + 25f,
                    node.width - 50f, 18f), component.SocialRelationship(animal,
                        relationship.otherAnimal).CapitalizeFirst());
                Text.Font = GameFont.Small;
                GUI.color = Color.white;
                TooltipHandler.TipRegion(node, "Bond: " +
                    relationship.bond.ToStringPercent() + "\nFear: " +
                    relationship.fear.ToStringPercent() + "\nRivalry: " +
                    relationship.rivalry.ToStringPercent() + "\nLatest: " +
                    (relationship.lastEvent ?? "No recorded event") +
                    (relationship.otherAnimal?.Spawned == true
                        ? "\nClick to select and center." : "\nCurrently away from this map."));
                if (Widgets.ButtonInvisible(node) &&
                    relationship.otherAnimal?.Spawned == true)
                {
                    Find.Selector.ClearSelection();
                    Find.Selector.Select(relationship.otherAnimal);
                    CameraJumper.TryJump(relationship.otherAnimal);
                }
            }

            Widgets.DrawBoxSolid(centerNode, new Color(0.12f, 0.18f, 0.16f, 1f));
            Widgets.DrawBox(centerNode, 2, Texture2D.whiteTexture);
            Rect centerIcon = new Rect(centerNode.x + 8f, centerNode.y + 10f, 42f, 42f);
            if (animal?.def?.uiIcon != null)
                GUI.DrawTexture(centerIcon, animal.def.uiIcon, ScaleMode.ScaleToFit);
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(centerNode.x + 56f, centerNode.y + 9f,
                centerNode.width - 62f, 18f), "FOCAL ANIMAL");
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(centerNode.x + 56f, centerNode.y + 28f,
                centerNode.width - 62f, 25f), DisplayName(animal));

            Rect legend = new Rect(rect.x + 14f, rect.yMax - 30f,
                rect.width - 28f, 22f);
            GUI.color = new Color(0.42f, 0.9f, 0.5f);
            Widgets.Label(new Rect(legend.x, legend.y, 92f, 22f), "Green: bond");
            GUI.color = new Color(1f, 0.74f, 0.2f);
            Widgets.Label(new Rect(legend.x + 105f, legend.y, 92f, 22f), "Gold: fear");
            GUI.color = new Color(1f, 0.35f, 0.28f);
            Widgets.Label(new Rect(legend.x + 210f, legend.y, 105f, 22f), "Red: rivalry");
            GUI.color = Color.white;
        }
    }

    public sealed class Window_AnimalMemoryTimeline : Window
    {
        private readonly Pawn animal;
        private readonly WildlifeMemoryMapComponent component;
        private Vector2 scroll;
        private bool socialWeb;
        public override Vector2 InitialSize => new Vector2(820f, 660f);

        public Window_AnimalMemoryTimeline(Pawn animal, bool startWithSocialWeb = false)
        {
            this.animal = animal;
            component = animal?.MapHeld?.GetComponent<WildlifeMemoryMapComponent>();
            socialWeb = startWithSocialWeb;
            doCloseX = true;
            absorbInputAroundWindow = true;
        }

        public override void DoWindowContents(Rect rect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, rect.width - 240f, 32f),
                AnimalMemoryPresentation.DisplayName(animal) + " — Memory");
            Text.Font = GameFont.Small;
            if (Widgets.ButtonText(new Rect(rect.width - 230f, 0f, 108f, 30f),
                "Timeline", !socialWeb)) socialWeb = false;
            if (Widgets.ButtonText(new Rect(rect.width - 116f, 0f, 116f, 30f),
                "Social Web", socialWeb)) socialWeb = true;
            Rect outer = new Rect(0f, 42f, rect.width, rect.height - 42f);
            if (socialWeb)
            {
                AnimalMemoryPresentation.DrawSocialWeb(outer, animal, component);
                return;
            }
            List<AnimalMemoryDisplayRow> rows =
                AnimalMemoryPresentation.Rows(animal, component);
            AnimalMemoryPresentation.DrawTimeline(outer, ref scroll, rows);
            if (rows.Count == 0) Widgets.Label(new Rect(8f, 52f, rect.width - 16f, 40f),
                "This animal has no recorded encounters with colonists or other animals.");
        }
    }

    public sealed class ITab_AnimalMemory : ITab
    {
        private Vector2 scroll;

        public ITab_AnimalMemory()
        {
            size = new Vector2(560f, 480f);
            labelKey = "Herds_AnimalMemoryTab";
        }

        public override bool IsVisible
        {
            get
            {
                Pawn pawn = SelThing as Pawn;
                return HerdsMod.Settings?.enableAnimalMemory == true && pawn?.Spawned == true &&
                    pawn.RaceProps?.Animal == true;
            }
        }

        protected override void FillTab()
        {
            Pawn animal = SelThing as Pawn;
            WildlifeMemoryMapComponent component = animal?.Map?.GetComponent<WildlifeMemoryMapComponent>();
            if (animal == null || component == null) return;
            Rect rect = new Rect(0f, 0f, size.x, size.y).ContractedBy(12f);
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(rect.x, rect.y, rect.width - 112f, 30f),
                AnimalMemoryPresentation.DisplayName(animal) + " — Memory");
            Text.Font = GameFont.Small;
            if (Widgets.ButtonText(new Rect(rect.xMax - 108f, rect.y, 108f, 28f),
                "Social Web"))
                Find.WindowStack.Add(new Window_AnimalMemoryTimeline(animal, true));
            List<AnimalColonistMemory> memories = component.Memories.Where(value => value?.animal == animal)
                .OrderByDescending(value => value.lastTick).ToList();
            List<AnimalSocialMemory> social = component.SocialFor(animal)
                .OrderByDescending(value => WildlifeMemoryMapComponent.SocialStrength(value))
                .ToList();
            string temperament = WildlifeLifeUtility.PersonalityLabel(animal);
            string bonds = animal.Map.GetComponent<WildlifeLivesMapComponent>()?.RelationshipSummary(animal);
            WildlifeRegionalStoriesMapComponent stories = animal.Map.GetComponent<WildlifeRegionalStoriesMapComponent>();
            string family = HerdsMod.Settings.enablePersistentFamilyLines ? stories?.FamilySummary(animal) : null;
            string territory = HerdsMod.Settings.enableTerritoryHistory ? stories?.TerritorySummary(animal) : null;
            int knowledge = HuntingKnowledgeMapComponent.ColonyLevel(animal.def);
            string tradition = HerdsMod.Settings.enableAnimalTraditions
                ? animal.Map.GetComponent<AnimalTraditionMapComponent>()?.Summary(animal, knowledge) : null;
            string landmark = HerdsMod.Settings.enableColonyWildlifeLandmark
                ? animal.Map.GetComponent<WildlifeLandmarkMapComponent>()?.Summary(animal.def, knowledge) : null;
            Widgets.Label(new Rect(rect.x, rect.y + 34f, rect.width, 44f),
                temperament + " • " + (bonds ?? "No recognized colonists.") + "\n" +
                memories.Count + (memories.Count == 1 ? " remembered colonist" : " remembered colonists") +
                "; " + social.Count + (social.Count == 1 ? " remembered animal." : " remembered animals."));
            TooltipHandler.TipRegion(new Rect(rect.x, rect.y + 34f, rect.width, 44f),
                WildlifeLifeUtility.PersonalityDescription(animal) +
                "\n\nTrust reduces avoidance; fear, hostility, personality, and learned tactics alter future behavior." +
                (family.NullOrEmpty() ? "" : "\n\nFamily line:\n" + family) +
                (territory.NullOrEmpty() ? "" : "\n\nTerritory history:\n" + territory) +
                (tradition.NullOrEmpty() ? "" : "\n\nAnimal tradition:\n" + tradition) +
                (landmark.NullOrEmpty() ? "" : "\n\nColony reputation:\n" + landmark));
            float socialHeight = social.Count == 0 ? 0f : Mathf.Min(2, social.Count) * 28f + 6f;
            for (int i = 0; i < social.Count && i < 2; i++)
            {
                AnimalSocialMemory relationship = social[i];
                Rect relation = new Rect(rect.x, rect.y + 78f + i * 28f, rect.width, 24f);
                Widgets.DrawHighlight(relation);
                GUI.color = WildlifeMemoryMapComponent.SocialColor(relationship);
                Widgets.Label(relation.ContractedBy(5f, 2f),
                    (relationship.otherAnimal?.LabelShortCap ?? "Unknown animal") + " - " +
                    component.SocialRelationship(animal, relationship.otherAnimal).CapitalizeFirst());
                GUI.color = Color.white;
                TooltipHandler.TipRegion(relation, "Bond: " + relationship.bond.ToStringPercent() +
                    "\nFear: " + relationship.fear.ToStringPercent() +
                    "\nRivalry: " + relationship.rivalry.ToStringPercent() +
                    "\nPositive encounters: " + relationship.positiveEvents +
                    "\nNegative encounters: " + relationship.negativeEvents);
                if (relationship.otherAnimal?.Spawned == true && Widgets.ButtonInvisible(relation))
                {
                    Find.Selector.ClearSelection();
                    Find.Selector.Select(relationship.otherAnimal);
                    CameraJumper.TryJump(relationship.otherAnimal);
                }
            }
            float relationshipsHeight = memories.Count == 0 ? 0f : Mathf.Min(2, memories.Count) * 28f + 6f;
            for (int i = 0; i < memories.Count && i < 2; i++)
            {
                AnimalColonistMemory relationship = memories[i];
                Rect relation = new Rect(rect.x, rect.y + 78f + socialHeight + i * 28f, rect.width, 24f);
                Widgets.DrawHighlight(relation);
                Widgets.Label(relation.ContractedBy(5f, 2f),
                    (relationship.colonist?.LabelShortCap ?? "Unknown colonist") + " — " +
                    component.Relationship(animal, relationship.colonist).CapitalizeFirst());
                TooltipHandler.TipRegion(relation, "Trust: " + relationship.trust.ToStringPercent() +
                    "\nFear: " + relationship.fear.ToStringPercent() +
                    "\nHostility: " + relationship.hostility.ToStringPercent() +
                    "\nHunting encounters: " + relationship.huntingEncounters +
                    "\nGunfire encounters: " + relationship.rangedEncounters +
                    "\nTrap encounters: " + relationship.trapEncounters);
                if (relationship.colonist?.Spawned == true && Widgets.ButtonInvisible(relation))
                {
                    Find.Selector.ClearSelection();
                    Find.Selector.Select(relationship.colonist);
                    CameraJumper.TryJump(relationship.colonist);
                }
            }
            List<AnimalMemoryDisplayRow> rows = AnimalMemoryPresentation.Rows(animal, component);
            Rect outer = new Rect(rect.x, rect.y + 82f + socialHeight + relationshipsHeight,
                rect.width, rect.height - 82f - socialHeight - relationshipsHeight);
            AnimalMemoryPresentation.DrawTimeline(outer, ref scroll, rows);
            if (rows.Count == 0)
                Widgets.Label(new Rect(rect.x + 8f, outer.y + 10f, rect.width - 16f, 40f),
                    "No remembered encounters have been recorded.");
        }
    }

    public static class WildlifeRoleUtility
    {
        public static bool IsMasterHunter(Pawn pawn) =>
            HerdsMod.Settings?.enableWildlifeIdeologyRoles == true && ModsConfig.IdeologyActive &&
            pawn?.Ideo?.GetRole(pawn)?.def == HerdsDefOf.Herds_IdeoRole_MasterHunter;

        public static bool IsMasterConservationist(Pawn pawn) =>
            HerdsMod.Settings?.enableWildlifeIdeologyRoles == true && ModsConfig.IdeologyActive &&
            pawn?.Ideo?.GetRole(pawn)?.def == HerdsDefOf.Herds_IdeoRole_MasterConservationist;

        public static float AnimalKnowledgeFactor(Pawn pawn) =>
            IsMasterHunter(pawn) ? 1.5f : IsMasterConservationist(pawn) ? 1.65f : 1f;

        public static float BiomeKnowledgeFactor(Pawn pawn) =>
            IsMasterConservationist(pawn) ? 1.75f : IsMasterHunter(pawn) ? 1.2f : 1f;
    }

    [HarmonyPatch(typeof(StatWorker), "GetValueUnfinalized")]
    public static class DisabledWildlifeRoleStatsPatch
    {
        public static void Postfix(StatRequest req, StatDef ___stat, ref float __result)
        {
            if (HerdsMod.Settings?.enableWildlifeIdeologyRoles != false || !ModsConfig.IdeologyActive ||
                req.Thing is not Pawn pawn || pawn.Ideo == null) return;
            PreceptDef role = pawn.Ideo.GetRole(pawn)?.def;
            if (role == HerdsDefOf.Herds_IdeoRole_MasterHunter)
            {
                if (___stat.defName == "HuntingStealth") __result -= 0.25f;
                else if (___stat.defName == "ShootingAccuracyPawn") __result -= 3f;
                else if (___stat.defName == "AimingDelayFactor") __result += 0.15f;
                else if (___stat.defName == "MoveSpeed") __result -= 0.15f;
            }
            else if (role == HerdsDefOf.Herds_IdeoRole_MasterConservationist)
            {
                if (___stat.defName == "TameAnimalChance") __result -= 0.12f;
                else if (___stat.defName == "AnimalGatherSpeed") __result -= 0.25f;
                else if (___stat.defName == "AnimalGatherYield") __result -= 0.15f;
                else if (___stat.defName == "MedicalTendQuality") __result -= 0.08f;
            }
        }
    }
}
