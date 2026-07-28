using System;
using System.Collections.Generic;
using System.Linq;
using LudeonTK;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Herds
{
    public enum WildlifeEcologicalRole
    {
        TrailMaker,
        Grazer,
        Browser,
        Rooter,
        SeedCarrier,
        ShoreNester,
        WaterEngineer,
        TerritoryMarker,
        Scavenger
    }

    public enum WildlifeLandscapeKind
    {
        GameTrail,
        GrazingGround,
        BrowsedGrove,
        RootingWallow,
        SeedGrove,
        ShoreNest,
        WatersideWorks,
        ScentPost,
        FeedingRemains
    }

    public enum WildlifeCrossroadResponse
    {
        Undecided,
        LeftWild,
        Stewarded
    }

    public sealed class WildlifeNicheExtension : DefModExtension
    {
        public List<WildlifeEcologicalRole> forceRoles;
        public List<WildlifeEcologicalRole> suppressRoles;
    }

    public static class WildlifeNicheDatabase
    {
        private static readonly Dictionary<ThingDef, List<WildlifeEcologicalRole>> cache =
            new Dictionary<ThingDef, List<WildlifeEcologicalRole>>();

        public static IReadOnlyList<WildlifeEcologicalRole> RolesFor(ThingDef species)
        {
            if (species == null) return Array.Empty<WildlifeEcologicalRole>();
            if (!cache.TryGetValue(species, out List<WildlifeEcologicalRole> roles))
                cache.Add(species, roles = Infer(species));
            return roles;
        }

        public static void Clear() => cache.Clear();

        private static List<WildlifeEcologicalRole> Infer(ThingDef species)
        {
            List<WildlifeEcologicalRole> roles = new List<WildlifeEcologicalRole>();
            RaceProperties race = species?.race;
            if (race?.Animal != true || race.Humanlike || !race.IsFlesh ||
                race.IsAnomalyEntity || race.baseBodySize < 0.06f) return roles;

            string food = race.foodType.ToString().ToLowerInvariant();
            string identity = ((species.defName ?? "") + " " + (species.label ?? "") + " " +
                (race.body?.defName ?? "")).ToLowerInvariant();
            bool predator = race.predator;
            bool bird = PreyProfileDatabase.IsBird(species);
            bool flightless = PreyProfileDatabase.IsFlightlessBird(species);
            bool waterfowl = PreyProfileDatabase.IsWaterfowl(species);
            bool plantDiet = food.Contains("vegetarian") || food.Contains("dendrovore") ||
                food.Contains("omnivore");
            bool roughPlantDiet = food.Contains("rough") || food.Contains("dendrovore");
            bool meatDiet = food.Contains("carnivore") || food.Contains("omnivore");
            float body = race.baseBodySize;

            if (!predator && !bird && race.herdAnimal && plantDiet && body >= 0.65f && body <= 8f)
                roles.Add(WildlifeEcologicalRole.TrailMaker);
            if (!predator && !bird && roughPlantDiet && body >= 0.32f && body <= 6f)
                roles.Add(WildlifeEcologicalRole.Grazer);
            if (!predator && food.Contains("dendrovore") && body >= 0.25f && body <= 8f)
                roles.Add(WildlifeEcologicalRole.Browser);

            bool rootingShape = ContainsAny(identity, "boar", "pig", "warthog", "peccary",
                "tapir", "badger", "bear");
            if (!predator && !bird && rootingShape && food.Contains("omnivore") &&
                body >= 0.35f && body <= 3.5f)
                roles.Add(WildlifeEcologicalRole.Rooter);

            bool arborealCache = ContainsAny(identity, "squirrel", "chipmunk") &&
                body <= 0.65f;
            if (!predator && plantDiet &&
                ((bird && !flightless && !waterfowl && body <= 1.25f) || arborealCache))
                roles.Add(WildlifeEcologicalRole.SeedCarrier);
            if (!predator && bird && waterfowl && body >= 0.12f && body <= 2.5f)
                roles.Add(WildlifeEcologicalRole.ShoreNester);

            if (!predator && plantDiet && ContainsAny(identity, "beaver") &&
                body >= 0.3f && body <= 2.5f)
                roles.Add(WildlifeEcologicalRole.WaterEngineer);
            if (predator && body >= 0.25f && body <= 8f)
                roles.Add(WildlifeEcologicalRole.TerritoryMarker);
            if (meatDiet && body >= 0.25f && body <= 8f)
                roles.Add(WildlifeEcologicalRole.Scavenger);

            WildlifeNicheExtension extension = species.GetModExtension<WildlifeNicheExtension>();
            if (extension?.suppressRoles != null)
                roles.RemoveAll(role => extension.suppressRoles.Contains(role));
            if (extension?.forceRoles != null)
                for (int i = 0; i < extension.forceRoles.Count; i++)
                    if (!roles.Contains(extension.forceRoles[i])) roles.Add(extension.forceRoles[i]);
            return roles;
        }

        public static bool ConservativeRulesSelfTest()
        {
            ThingDef human = ThingDefOf.Human;
            return RolesFor(human).Count == 0 &&
                DefDatabase<ThingDef>.AllDefsListForReading
                    .Where(def => def.race?.Animal == true)
                    .All(def => RolesFor(def).Distinct().Count() == RolesFor(def).Count);
        }

        private static bool ContainsAny(string value, params string[] terms)
        {
            for (int i = 0; i < terms.Length; i++)
                if (value.Contains(terms[i])) return true;
            return false;
        }
    }

    public static class WildlifeLandscapeAPI
    {
        public static string RoleSummary(Pawn animal) =>
            WildlifeLandscapeUtility.RoleSummary(animal?.def);

        public static string RoleTooltip(Pawn animal)
        {
            if (animal?.def == null) return "No ecological landscape role.";
            IReadOnlyList<WildlifeEcologicalRole> roles =
                WildlifeNicheDatabase.RolesFor(animal.def);
            if (roles.Count == 0)
                return "This animal has no inferred landscape-building role. Its ordinary movement still contributes to the wider wildlife simulation.";
            return "These roles are inferred conservatively from anatomy, diet, body size, social behavior, terrain use, and observed activity. A physical feature forms only after sustained compatible activity.\n\n" +
                string.Join("\n", roles.Select(role => WildlifeLandscapeUtility.RoleLabel(role) +
                    ": " + WildlifeLandscapeUtility.RoleDescription(role)));
        }
    }

    public sealed class WildlifeLandscapeActivity : IExposable
    {
        public int id;
        public ThingDef species;
        public WildlifeLandscapeKind kind;
        public IntVec3 cell;
        public float progress;
        public int lastActivityTick;
        public bool noticed;
        public WildlifeCrossroadResponse response;
        public List<int> contributors = new List<int>();
        public List<int> observedBy = new List<int>();

        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id");
            Scribe_Defs.Look(ref species, "species");
            Scribe_Values.Look(ref kind, "kind");
            Scribe_Values.Look(ref cell, "cell");
            Scribe_Values.Look(ref progress, "progress");
            Scribe_Values.Look(ref lastActivityTick, "lastActivityTick");
            Scribe_Values.Look(ref noticed, "noticed");
            Scribe_Values.Look(ref response, "response");
            Scribe_Collections.Look(ref contributors, "contributors", LookMode.Value);
            Scribe_Collections.Look(ref observedBy, "observedBy", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                contributors = contributors ?? new List<int>();
                observedBy = observedBy ?? new List<int>();
            }
        }
    }

    public sealed class WildlifeLandscapeFeature : ThingWithComps
    {
        public ThingDef species;
        public WildlifeLandscapeKind kind;
        public float strength = 0.35f;
        public int createdTick;
        public int lastUsedTick;
        public bool protectedByColony;
        public WildlifeCrossroadResponse originResponse;
        public List<Pawn> studiedBy = new List<Pawn>();

        public float InfluenceRadius => 12f + strength * 10f;
        public override string LabelNoCount => WildlifeLandscapeUtility.Label(kind);
        public override string LabelMouseover => Find.Selector?.IsSelected(this) == true
            ? base.LabelMouseover : string.Empty;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Defs.Look(ref species, "species");
            Scribe_Values.Look(ref kind, "kind");
            Scribe_Values.Look(ref strength, "strength", 0.35f);
            Scribe_Values.Look(ref createdTick, "createdTick");
            Scribe_Values.Look(ref lastUsedTick, "lastUsedTick");
            Scribe_Values.Look(ref protectedByColony, "protectedByColony");
            Scribe_Values.Look(ref originResponse, "originResponse");
            Scribe_Collections.Look(ref studiedBy, "studiedBy", LookMode.Reference);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                studiedBy = studiedBy ?? new List<Pawn>();
        }

        public override void TickRare()
        {
            base.TickRare();
            if (HerdsMod.Settings?.enableWildlifeLandscaping != true || protectedByColony) return;
            int now = Find.TickManager.TicksGame;
            if (now - lastUsedTick < 600000) return;
            strength = Mathf.Max(0f, strength - 0.0002f);
            if (strength <= 0.02f && now - lastUsedTick > 1800000)
                Destroy(DestroyMode.Vanish);
        }

        public void Refresh(float amount)
        {
            lastUsedTick = Find.TickManager.TicksGame;
            strength = Mathf.Clamp01(strength + Mathf.Clamp(amount * 0.012f, 0.004f, 0.04f));
        }

        public override string GetInspectString()
        {
            int knowledge = species == null ? 0 :
                HuntingKnowledgeMapComponent.ColonyLevel(species);
            string maker = knowledge > 0 && species != null
                ? species.LabelCap.ToString()
                : "Unidentified wildlife";
            return maker + " established this " + WildlifeLandscapeUtility.Label(kind) + "." +
                "\nCondition: " + WildlifeLandscapeUtility.Condition(strength) +
                "\nEcological effect: " + WildlifeLandscapeUtility.Effect(kind) +
                (originResponse == WildlifeCrossroadResponse.LeftWild
                    ? "\nOrigin: The colony deliberately left this place wild."
                    : originResponse == WildlifeCrossroadResponse.Stewarded
                        ? "\nOrigin: A colonist helped wildlife establish this place."
                        : "") +
                "\nColony response: " + (protectedByColony ? "Protected" : "Undisturbed") +
                (studiedBy.Count > 0 ? "\nStudied by: " +
                    string.Join(", ", studiedBy.Where(pawn => pawn != null)
                        .Select(pawn => pawn.LabelShortCap)) : "");
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos()) yield return gizmo;
            yield return new Command_Action
            {
                defaultLabel = "Study Feature",
                defaultDesc = "Choose a colonist to examine how wildlife created and uses this place. This grants Animal and Biome Knowledge and improves fieldcraft around this species.",
                icon = TexCommand.GatherSpotActive,
                action = ShowStudyMenu
            };
            Command_Action protect = new Command_Action
            {
                defaultLabel = protectedByColony ? "End Protection" : "Protect Feature",
                defaultDesc = protectedByColony
                    ? "Allow this feature to fade naturally again."
                    : "Preserve this wildlife-shaped place. Protected features last and exert a stronger habitat and migration effect.",
                icon = TexCommand.ForbidOff,
                action = () =>
                {
                    protectedByColony = !protectedByColony;
                    WildlifeTestLog.Write("LivingLandscape",
                        "protection=" + protectedByColony + " kind=" + kind +
                        " species=" + (species?.defName ?? "unknown"), null, this);
                    Messages.Message(LabelCap + (protectedByColony
                        ? " is now protected by the colony."
                        : " is no longer protected."), this,
                        MessageTypeDefOf.NeutralEvent, false);
                }
            };
            if (!WildlifeProgression.Unlocked(WildlifeCapability.Stewardship))
                protect.Disable(WildlifeProgression.LockReason(WildlifeCapability.Stewardship));
            yield return protect;
            yield return new Command_Action
            {
                defaultLabel = "Clear Feature",
                defaultDesc = "Remove this natural feature. Its habitat, migration, and fieldcraft effects will be lost.",
                icon = TexCommand.ClearPrioritizedWork,
                action = () => Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    "Clear " + LabelNoCount + "?", () =>
                    {
            WildlifeExperience.Record("Landscape",
                            "The colony cleared a " + LabelNoCount + ".", this);
                        WildlifeTestLog.Write("LivingLandscape",
                            "cleared kind=" + kind + " species=" +
                            (species?.defName ?? "unknown"), null, this);
                        Destroy(DestroyMode.Vanish);
                    }, true))
            };
        }

        private void ShowStudyMenu()
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            foreach (Pawn colonist in Map.mapPawns.FreeColonistsSpawned
                .OrderByDescending(pawn => pawn.skills?.GetSkill(SkillDefOf.Animals)?.Level ?? 0))
            {
                Pawn pawn = colonist;
                string reason = pawn.Downed ? "downed" :
                    pawn.InMentalState ? "in a mental state" :
                    studiedBy.Contains(pawn) ? "already studied" :
                    !pawn.CanReserveAndReach(this, PathEndMode.Touch, Danger.Some)
                        ? "cannot reach" : null;
                string label = pawn.LabelShortCap + " - Animals Skill " +
                    (pawn.skills?.GetSkill(SkillDefOf.Animals)?.Level ?? 0);
                if (reason != null)
                    options.Add(new FloatMenuOption(label + " (" + reason + ")", null));
                else
                    options.Add(new FloatMenuOption(label, () =>
                    {
                        Job job = JobMaker.MakeJob(HerdsDefOf.Herds_StudyLandscapeFeature, this);
                        job.playerForced = true;
                        pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                    }));
            }
            if (options.Count == 0)
                options.Add(new FloatMenuOption("No colonists are available.", null));
            Find.WindowStack.Add(new FloatMenu(options));
        }

        public override void DrawExtraSelectionOverlays()
        {
            base.DrawExtraSelectionOverlays();
            GenDraw.DrawRadiusRing(Position, InfluenceRadius,
                protectedByColony ? new Color(0.35f, 0.78f, 0.38f) :
                WildlifeLandscapeUtility.Color(kind));
        }
    }

    public sealed class WildlifeLandscapeCrossroad : ThingWithComps
    {
        public int activityId;

        private WildlifeLandscapeMapComponent Component =>
            Map?.GetComponent<WildlifeLandscapeMapComponent>();
        private WildlifeLandscapeActivity Activity => Component?.ActivityById(activityId);

        public override string LabelNoCount
        {
            get
            {
                WildlifeLandscapeActivity activity = Activity;
                return activity == null ? "developing wild place" :
                    "developing " + WildlifeLandscapeUtility.Label(activity.kind);
            }
        }

        public override string LabelMouseover => Find.Selector?.IsSelected(this) == true
            ? base.LabelMouseover : string.Empty;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref activityId, "activityId");
        }

        public override string GetInspectString()
        {
            WildlifeLandscapeActivity activity = Activity;
            if (activity == null) return "The wildlife activity that shaped this place has faded.";
            int knowledge = activity.species == null ? 0 :
                HuntingKnowledgeMapComponent.ColonyLevel(activity.species);
            string maker = knowledge > 0 && activity.species != null
                ? activity.species.LabelCap.ToString()
                : "Unidentified wildlife";
            string progress = activity.observedBy.Count > 0
                ? Component.ProgressFraction(activity).ToStringPercent()
                : Component.ProgressStage(activity);
            return maker + " repeatedly uses this place." +
                "\nFormation: " + progress +
                "\nLikely result: " + WildlifeLandscapeUtility.Label(activity.kind).CapitalizeFirst() +
                "\nLikely effect: " + WildlifeLandscapeUtility.Effect(activity.kind) +
                "\nContributing animals: " + activity.contributors.Count +
                "\nColony response: " + Component.ResponseLabel(activity);
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos()) yield return gizmo;
            WildlifeLandscapeActivity activity = Activity;
            if (activity == null || HerdsMod.Settings?.enableLandscapeCrossroads != true)
                yield break;

            yield return new Command_Action
            {
                defaultLabel = "Observe Quietly",
                defaultDesc = "Send a colonist to watch from a respectful distance. This reveals exact progress and grants Animal and Biome Knowledge without disturbing the animals.",
                icon = TexCommand.GatherSpotActive,
                action = () => ShowColonistMenu(false)
            };

            Command_Action steward = new Command_Action
            {
                defaultLabel = "Steward This Place",
                defaultDesc = "Send a colonist to carefully support what the animals are building. This advances formation, teaches wildlife knowledge, and protects the finished feature.",
                icon = TexCommand.GatherSpotActive,
                action = () => ShowColonistMenu(true)
            };
            if (!WildlifeProgression.Unlocked(WildlifeCapability.Stewardship))
                steward.Disable(WildlifeProgression.LockReason(WildlifeCapability.Stewardship));
            else if (activity.response == WildlifeCrossroadResponse.LeftWild)
                steward.Disable("The colony promised to leave this place wild.");
            else if (activity.response == WildlifeCrossroadResponse.Stewarded)
                steward.Disable("This place has already been stewarded.");
            yield return steward;

            Command_Action leaveWild = new Command_Action
            {
                defaultLabel = "Leave It Wild",
                defaultDesc = "Promise not to develop this small area. If it remains undisturbed until formation completes, the resulting habitat will be stronger and more attractive to returning wildlife.",
                icon = TexCommand.ForbidOff,
                action = () => Component.TryLeaveWild(activityId)
            };
            if (activity.response == WildlifeCrossroadResponse.LeftWild)
                leaveWild.Disable("The colony has already promised to leave this place wild.");
            else if (activity.response == WildlifeCrossroadResponse.Stewarded)
                leaveWild.Disable("The colony already intervened to steward this place.");
            else if (Component.ColonyDisturbs(activity.cell))
                leaveWild.Disable("Remove nearby colony buildings and the home area before making this promise.");
            yield return leaveWild;
        }

        private void ShowColonistMenu(bool steward)
        {
            WildlifeLandscapeActivity activity = Activity;
            if (activity == null) return;
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            foreach (Pawn colonist in Map.mapPawns.FreeColonistsSpawned
                .OrderByDescending(pawn => pawn.skills?.GetSkill(SkillDefOf.Animals)?.Level ?? 0))
            {
                Pawn pawn = colonist;
                IntVec3 observationCell = IntVec3.Invalid;
                string reason = pawn.Downed ? "downed" :
                    pawn.InMentalState ? "in a mental state" :
                    !steward && activity.observedBy.Contains(pawn.thingIDNumber)
                        ? "already observed" :
                    steward && activity.response == WildlifeCrossroadResponse.Stewarded
                        ? "already stewarded" :
                    steward && !pawn.CanReserveAndReach(this, PathEndMode.Touch, Danger.Some)
                        ? "cannot reach" :
                    !steward && !TryObservationCell(pawn, out observationCell)
                        ? "no safe observation position" : null;
                string label = pawn.LabelShortCap + " - Animals Skill " +
                    (pawn.skills?.GetSkill(SkillDefOf.Animals)?.Level ?? 0);
                if (reason != null)
                    options.Add(new FloatMenuOption(label + " (" + reason + ")", null));
                else
                    options.Add(new FloatMenuOption(label, () =>
                    {
                        Job job;
                        if (steward)
                            job = JobMaker.MakeJob(HerdsDefOf.Herds_StewardLandscapeCrossroad, this);
                        else
                            job = JobMaker.MakeJob(HerdsDefOf.Herds_ObserveLandscapeCrossroad,
                                observationCell, this);
                        job.playerForced = true;
                        pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                    }));
            }
            if (options.Count == 0)
                options.Add(new FloatMenuOption("No colonists are available.", null));
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private bool TryObservationCell(Pawn pawn, out IntVec3 result)
        {
            result = GenRadial.RadialCellsAround(Position, 12f, true)
                .Where(cell => cell.InBounds(Map) &&
                    cell.DistanceToSquared(Position) >= 36 &&
                    cell.Standable(Map) && GenSight.LineOfSight(cell, Position, Map) &&
                    pawn.CanReach(cell, PathEndMode.OnCell, Danger.Some))
                .OrderBy(cell => cell.DistanceToSquared(pawn.Position))
                .FirstOrDefault();
            return result.IsValid;
        }

        public override void DrawExtraSelectionOverlays()
        {
            base.DrawExtraSelectionOverlays();
            WildlifeLandscapeActivity activity = Activity;
            if (activity != null)
                GenDraw.DrawRadiusRing(Position, 6f,
                    WildlifeLandscapeUtility.Color(activity.kind));
        }
    }

    public sealed class JobDriver_ObserveLandscapeCrossroad : JobDriver
    {
        private WildlifeLandscapeCrossroad Crossroad =>
            job.targetB.Thing as WildlifeLandscapeCrossroad;

        public override bool TryMakePreToilReservations(bool errorOnFailed) =>
            pawn.Reserve(job.targetB, job, 1, -1, null, errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.B);
            yield return Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.OnCell);
            Toil observe = Toils_General.Wait(900, TargetIndex.B);
            observe.socialMode = RandomSocialMode.Off;
            observe.WithProgressBarToilDelay(TargetIndex.B);
            yield return observe;
            Toil finish = ToilMaker.MakeToil("ObserveLandscapeCrossroad");
            finish.initAction = () =>
            {
                WildlifeLandscapeCrossroad crossroad = Crossroad;
                crossroad?.Map?.GetComponent<WildlifeLandscapeMapComponent>()?
                    .ResolveCrossroadWork(crossroad.activityId, pawn, false);
            };
            finish.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return finish;
        }
    }

    public sealed class JobDriver_StewardLandscapeCrossroad : JobDriver
    {
        private WildlifeLandscapeCrossroad Crossroad =>
            job.targetA.Thing as WildlifeLandscapeCrossroad;

        public override bool TryMakePreToilReservations(bool errorOnFailed) =>
            pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            Toil steward = Toils_General.Wait(1400, TargetIndex.A);
            steward.socialMode = RandomSocialMode.Off;
            steward.WithProgressBarToilDelay(TargetIndex.A);
            yield return steward;
            Toil finish = ToilMaker.MakeToil("StewardLandscapeCrossroad");
            finish.initAction = () =>
            {
                WildlifeLandscapeCrossroad crossroad = Crossroad;
                crossroad?.Map?.GetComponent<WildlifeLandscapeMapComponent>()?
                    .ResolveCrossroadWork(crossroad.activityId, pawn, true);
            };
            finish.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return finish;
        }
    }

    public sealed class JobDriver_StudyLandscapeFeature : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) =>
            pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            Toil study = Toils_General.Wait(700, TargetIndex.A);
            study.socialMode = RandomSocialMode.Off;
            study.WithProgressBarToilDelay(TargetIndex.A);
            yield return study;
            Toil finish = ToilMaker.MakeToil("StudyLandscapeFeature");
            finish.initAction = () =>
            {
                WildlifeLandscapeFeature feature =
                    job.targetA.Thing as WildlifeLandscapeFeature;
                if (feature?.Spawned != true || feature.studiedBy.Contains(pawn)) return;
                feature.studiedBy.Add(pawn);
                HuntingKnowledgeMapComponent knowledge =
                    feature.Map.GetComponent<HuntingKnowledgeMapComponent>();
                knowledge?.Learn(pawn, feature.species, 18f + feature.strength * 12f);
                knowledge?.LearnBiome(pawn, feature.Map.Biome, 10f + feature.strength * 8f);
                feature.Refresh(0.5f);
                Messages.Message(pawn.LabelShortCap + " studied the " +
                    feature.LabelNoCount + " and learned how " +
                    (feature.species?.LabelCap.ToString() ?? "wildlife") +
                    " shapes this habitat.", feature, MessageTypeDefOf.PositiveEvent, false);
                WildlifeExperience.Record("Landscape", pawn.LabelShortCap +
                    " studied a " + feature.LabelNoCount + ".", feature);
                WildlifeTestLog.Write("LivingLandscape",
                    "studied kind=" + feature.kind + " species=" +
                    (feature.species?.defName ?? "unknown"), pawn, feature);
            };
            finish.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return finish;
        }
    }

    public sealed class WildlifeLandscapeMapComponent : MapComponent
    {
        private const int ScanInterval = 2500;
        private const int MaxFeatures = 14;
        private List<WildlifeLandscapeActivity> activities =
            new List<WildlifeLandscapeActivity>();
        private readonly Dictionary<Pawn, IntVec3> lastCells =
            new Dictionary<Pawn, IntVec3>();
        private int nextScanTick;
        private int nextActivityId = 1;
        private int lastCrossroadLetterTick = -999999;

        public IReadOnlyList<WildlifeLandscapeActivity> Activities => activities;
        public IEnumerable<WildlifeLandscapeFeature> Features =>
            map.listerThings.AllThings.OfType<WildlifeLandscapeFeature>();
        public IEnumerable<WildlifeLandscapeCrossroad> Crossroads =>
            map.listerThings.AllThings.OfType<WildlifeLandscapeCrossroad>();

        public WildlifeLandscapeMapComponent(Map map) : base(map) { }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref activities, "wildlifeLandscapeActivities",
                LookMode.Deep);
            Scribe_Values.Look(ref nextScanTick, "nextWildlifeLandscapeScan");
            Scribe_Values.Look(ref nextActivityId, "nextWildlifeLandscapeActivityId", 1);
            Scribe_Values.Look(ref lastCrossroadLetterTick, "lastWildlifeCrossroadLetterTick", -999999);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                activities = activities?.Where(record => record?.species?.race?.Animal == true)
                    .ToList() ?? new List<WildlifeLandscapeActivity>();
                for (int i = 0; i < activities.Count; i++)
                    if (activities[i].id <= 0) activities[i].id = nextActivityId++;
                if (activities.Count > 0)
                    nextActivityId = Math.Max(nextActivityId, activities.Max(value => value.id) + 1);
            }
        }

        public override void MapComponentTick()
        {
            if (HerdsMod.Settings?.enableWildlifeLandscaping != true) return;
            int now = Find.TickManager.TicksGame;
            if (now < nextScanTick) return;
            nextScanTick = now + ScanInterval;
            Scan(now);
        }

        private void Scan(int now)
        {
            if (HerdsMod.Settings.enableLandscapeCrossroads != true)
                foreach (WildlifeLandscapeCrossroad marker in Crossroads.ToList())
                    marker.Destroy(DestroyMode.Vanish);
            List<WildlifeLandscapeFeature> features = Features.ToList();
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            Dictionary<ThingDef, int> localCounts = new Dictionary<ThingDef, int>();
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (!Eligible(pawn)) continue;
                localCounts[pawn.def] = localCounts.TryGetValue(pawn.def, out int count)
                    ? count + 1 : 1;
            }

            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (!Eligible(pawn)) continue;
                lastCells.TryGetValue(pawn, out IntVec3 previous);
                lastCells[pawn] = pawn.Position;
                bool moved = previous.IsValid &&
                    previous.DistanceToSquared(pawn.Position) >= 25;
                IReadOnlyList<WildlifeEcologicalRole> roles =
                    WildlifeNicheDatabase.RolesFor(pawn.def);
                for (int r = 0; r < roles.Count; r++)
                {
                    WildlifeEcologicalRole role = roles[r];
                    WildlifeLandscapeKind kind = KindFor(role);
                    if (!ActivityCell(pawn, role, previous, moved, out IntVec3 cell))
                        continue;
                    int count = localCounts.TryGetValue(pawn.def, out int value) ? value : 1;
                    if (RequiresPopulation(role) && count < 2) continue;
                    WildlifeLandscapeFeature existing = features.FirstOrDefault(feature =>
                        feature.kind == kind && feature.species == pawn.def &&
                        feature.Position.DistanceToSquared(cell) <= 256);
                    float amount = ActivityAmount(pawn, role);
                    if (existing != null)
                    {
                        existing.Refresh(amount);
                        continue;
                    }
                    AddActivity(pawn, kind, cell, amount, now);
                }
            }

            for (int i = activities.Count - 1; i >= 0; i--)
            {
                WildlifeLandscapeActivity activity = activities[i];
                if (now - activity.lastActivityTick > 180000)
                {
                    RemoveCrossroad(activity.id);
                    activities.RemoveAt(i);
                    continue;
                }
                if (now - activity.lastActivityTick > 15000)
                    activity.progress *= 0.97f;
                if (activity.response == WildlifeCrossroadResponse.LeftWild &&
                    ColonyDisturbs(activity.cell))
                {
                    activity.response = WildlifeCrossroadResponse.Undecided;
                    Messages.Message("The colony disturbed the developing " +
                        WildlifeLandscapeUtility.Label(activity.kind) +
                        "; the promise to leave it wild has ended.",
                        new TargetInfo(activity.cell, map), MessageTypeDefOf.CautionInput, false);
                    WildlifeTestLog.Write("LandscapeCrossroad",
                        "wild_promise_broken id=" + activity.id + " kind=" + activity.kind);
                }
                if (activity.progress >= Threshold(activity.kind) &&
                    activity.contributors.Count >= RequiredContributors(activity.kind) &&
                    features.Count < MaxFeatures &&
                    CreateFeature(activity.kind, activity.species, activity.cell, false,
                        out WildlifeLandscapeFeature created, activity.response))
                {
                    features.Add(created);
                    RemoveCrossroad(activity.id);
                    activities.RemoveAt(i);
                }
                else if (HerdsMod.Settings.enableLandscapeCrossroads &&
                    ProgressFraction(activity) >= 0.12f)
                    EnsureCrossroad(activity, now, true);
            }
            HashSet<Pawn> present = new HashSet<Pawn>(pawns.Where(Eligible));
            foreach (Pawn stale in lastCells.Keys.Where(pawn => !present.Contains(pawn)).ToList())
                lastCells.Remove(stale);
            foreach (WildlifeLandscapeCrossroad marker in Crossroads
                .Where(value => ActivityById(value.activityId) == null).ToList())
                marker.Destroy(DestroyMode.Vanish);
        }

        private void AddActivity(Pawn pawn, WildlifeLandscapeKind kind, IntVec3 cell,
            float amount, int now)
        {
            WildlifeLandscapeActivity activity = activities.FirstOrDefault(record =>
                record.kind == kind && record.species == pawn.def &&
                record.cell.DistanceToSquared(cell) <= 196);
            if (activity == null)
            {
                activity = new WildlifeLandscapeActivity
                {
                    id = nextActivityId++,
                    species = pawn.def,
                    kind = kind,
                    cell = cell,
                    lastActivityTick = now
                };
                activities.Add(activity);
            }
            if (now - activity.lastActivityTick > 60000)
                activity.contributors.Clear();
            activity.lastActivityTick = now;
            activity.cell = Midpoint(activity.cell, cell);
            activity.progress += amount;
            if (!activity.contributors.Contains(pawn.thingIDNumber))
                activity.contributors.Add(pawn.thingIDNumber);
        }

        public WildlifeLandscapeActivity ActivityById(int id) =>
            activities.FirstOrDefault(value => value.id == id);

        public WildlifeLandscapeCrossroad CrossroadFor(int id) =>
            Crossroads.FirstOrDefault(value => value.activityId == id);

        public float ProgressFraction(WildlifeLandscapeActivity activity) =>
            activity == null ? 0f : Mathf.Clamp01(activity.progress / Threshold(activity.kind));

        public string ProgressStage(WildlifeLandscapeActivity activity)
        {
            float value = ProgressFraction(activity);
            return value < 0.25f ? "Faint signs" :
                value < 0.55f ? "Taking shape" :
                value < 0.85f ? "Strong recurring use" : "Nearly established";
        }

        public string ResponseLabel(WildlifeLandscapeActivity activity) =>
            activity?.response == WildlifeCrossroadResponse.LeftWild ? "Promised wild" :
            activity?.response == WildlifeCrossroadResponse.Stewarded ? "Stewarded" :
            activity?.observedBy?.Count > 0 ? "Observed" : "Undecided";

        private WildlifeLandscapeCrossroad EnsureCrossroad(
            WildlifeLandscapeActivity activity, int now, bool announce)
        {
            if (activity == null || HerdsMod.Settings?.enableLandscapeCrossroads != true)
                return null;
            WildlifeLandscapeCrossroad marker = CrossroadFor(activity.id);
            if (marker == null)
            {
                marker = ThingMaker.MakeThing(HerdsDefOf.Herds_LandscapeCrossroad)
                    as WildlifeLandscapeCrossroad;
                if (marker == null) return null;
                marker.activityId = activity.id;
                GenSpawn.Spawn(marker, activity.cell, map);
            }
            bool firstNotice = !activity.noticed;
            activity.noticed = true;
            if (firstNotice && announce && HerdsMod.Settings.enableWildlifeAlerts &&
                now - lastCrossroadLetterTick > 150000)
            {
                lastCrossroadLetterTick = now;
                string species = HuntingKnowledgeMapComponent.ColonyLevel(activity.species) > 0
                    ? activity.species.LabelCap.ToString()
                    : "Wildlife";
                Find.LetterStack.ReceiveLetter("A Wild Place Taking Shape",
                    species + " are repeatedly using one place in a way that may permanently reshape it.\n\n" +
                    "Select the developing place to observe quietly, steward its formation, or leave it deliberately wild.",
                    LetterDefOf.NeutralEvent, marker);
            }
            return marker;
        }

        private void RemoveCrossroad(int activityId)
        {
            WildlifeLandscapeCrossroad marker = CrossroadFor(activityId);
            if (marker?.Destroyed == false) marker.Destroy(DestroyMode.Vanish);
        }

        public bool ColonyDisturbs(IntVec3 center)
        {
            int count = GenRadial.NumCellsInRadius(7f);
            for (int i = 0; i < count; i++)
            {
                IntVec3 cell = center + GenRadial.RadialPattern[i];
                if (!cell.InBounds(map)) continue;
                if (map.areaManager.Home[cell]) return true;
                Building building = cell.GetEdifice(map);
                if (building?.Faction == Faction.OfPlayer) return true;
            }
            return false;
        }

        public void TryLeaveWild(int activityId)
        {
            WildlifeLandscapeActivity activity = ActivityById(activityId);
            WildlifeLandscapeCrossroad marker = CrossroadFor(activityId);
            if (activity == null || marker == null ||
                activity.response != WildlifeCrossroadResponse.Undecided) return;
            if (ColonyDisturbs(activity.cell))
            {
                Messages.Message("This place is too close to colony development to be left wild.",
                    marker, MessageTypeDefOf.RejectInput, false);
                return;
            }
            activity.response = WildlifeCrossroadResponse.LeftWild;
            Messages.Message("The colony will leave this developing " +
                WildlifeLandscapeUtility.Label(activity.kind) + " wild.",
                marker, MessageTypeDefOf.PositiveEvent, false);
            WildlifeExperience.Record("Wildlife Crossroad",
                "The colony promised to leave a developing " +
                WildlifeLandscapeUtility.Label(activity.kind) + " wild.", marker);
            WildlifeTestLog.Write("LandscapeCrossroad",
                "response=LeftWild id=" + activity.id + " kind=" + activity.kind,
                null, marker);
        }

        public void ResolveCrossroadWork(int activityId, Pawn pawn, bool steward)
        {
            WildlifeLandscapeActivity activity = ActivityById(activityId);
            WildlifeLandscapeCrossroad marker = CrossroadFor(activityId);
            if (activity == null || marker?.Spawned != true || pawn == null) return;
            if (!activity.observedBy.Contains(pawn.thingIDNumber))
                activity.observedBy.Add(pawn.thingIDNumber);
            HuntingKnowledgeMapComponent knowledge =
                map.GetComponent<HuntingKnowledgeMapComponent>();
            if (steward)
            {
                if (!WildlifeProgression.Unlocked(WildlifeCapability.Stewardship) ||
                    activity.response != WildlifeCrossroadResponse.Undecided) return;
                activity.response = WildlifeCrossroadResponse.Stewarded;
                activity.progress += Threshold(activity.kind) * 0.22f;
                knowledge?.Learn(pawn, activity.species, 20f);
                knowledge?.LearnBiome(pawn, map.Biome, 11f);
                RememberContributors(activity, pawn, AnimalMemoryKind.Protected, 1.1f);
                Messages.Message(pawn.LabelShortCap + " helped wildlife establish the developing " +
                    WildlifeLandscapeUtility.Label(activity.kind) + ".",
                    marker, MessageTypeDefOf.PositiveEvent, false);
                WildlifeExperience.Record("Wildlife Crossroad", pawn.LabelShortCap +
                    " stewarded a developing " +
                    WildlifeLandscapeUtility.Label(activity.kind) + ".", marker);
            }
            else
            {
                knowledge?.Learn(pawn, activity.species, 12f);
                knowledge?.LearnBiome(pawn, map.Biome, 6f);
                RememberContributors(activity, pawn, AnimalMemoryKind.QuietObservation, 0.7f);
                Messages.Message(pawn.LabelShortCap + " quietly observed the developing " +
                    WildlifeLandscapeUtility.Label(activity.kind) +
                    " without disturbing its makers.",
                    marker, MessageTypeDefOf.PositiveEvent, false);
                WildlifeExperience.Record("Wildlife Crossroad", pawn.LabelShortCap +
                    " quietly observed a developing " +
                    WildlifeLandscapeUtility.Label(activity.kind) + ".", marker);
            }
            WildlifeTestLog.Write("LandscapeCrossroad",
                "response=" + (steward ? "Stewarded" : "Observed") +
                " id=" + activity.id + " kind=" + activity.kind, pawn, marker);
        }

        private void RememberContributors(WildlifeLandscapeActivity activity, Pawn pawn,
            AnimalMemoryKind kind, float strength)
        {
            if (HerdsMod.Settings?.enableAnimalMemory != true) return;
            foreach (int id in activity.contributors.Take(8))
            {
                Pawn animal = map.mapPawns.AllPawnsSpawned
                    .FirstOrDefault(value => value.thingIDNumber == id);
                if (animal?.Spawned == true)
                    WildlifeMemoryUtility.Remember(animal, pawn, kind, strength);
            }
        }

        private bool ActivityCell(Pawn pawn, WildlifeEcologicalRole role,
            IntVec3 previous, bool moved, out IntVec3 cell)
        {
            cell = pawn.Position;
            switch (role)
            {
                case WildlifeEcologicalRole.TrailMaker:
                    if (!moved) return false;
                    cell = Midpoint(previous, pawn.Position);
                    return true;
                case WildlifeEcologicalRole.Grazer:
                    return NearbyPlant(pawn.Position, false);
                case WildlifeEcologicalRole.Browser:
                    return NearbyPlant(pawn.Position, true);
                case WildlifeEcologicalRole.Rooter:
                    return !pawn.Position.Roofed(map) &&
                        map.fertilityGrid.FertilityAt(pawn.Position) >= 0.35f;
                case WildlifeEcologicalRole.SeedCarrier:
                    return moved && NearbyPlant(pawn.Position, false);
                case WildlifeEcologicalRole.ShoreNester:
                case WildlifeEcologicalRole.WaterEngineer:
                    return NearWater(pawn.Position);
                case WildlifeEcologicalRole.TerritoryMarker:
                    return moved && !pawn.Position.Roofed(map);
                case WildlifeEcologicalRole.Scavenger:
                    Corpse corpse = NearbyAnimalCorpse(pawn.Position);
                    if (corpse == null) return false;
                    cell = corpse.Position;
                    return true;
                default:
                    return false;
            }
        }

        private bool NearbyPlant(IntVec3 center, bool tree)
        {
            int count = GenRadial.NumCellsInRadius(tree ? 6f : 4f);
            for (int i = 0; i < count; i += 3)
            {
                IntVec3 cell = center + GenRadial.RadialPattern[i];
                Plant plant = cell.InBounds(map) ? cell.GetPlant(map) : null;
                if (plant != null && (!tree || plant.def.plant?.IsTree == true))
                    return true;
            }
            return false;
        }

        private bool NearWater(IntVec3 center)
        {
            int count = GenRadial.NumCellsInRadius(7f);
            for (int i = 0; i < count; i += 4)
            {
                IntVec3 cell = center + GenRadial.RadialPattern[i];
                if (cell.InBounds(map) && cell.GetTerrain(map).IsWater) return true;
            }
            return false;
        }

        private Corpse NearbyAnimalCorpse(IntVec3 center)
        {
            foreach (Thing thing in GenRadial.RadialDistinctThingsAround(center, map, 6f, true))
                if (thing is Corpse corpse &&
                    corpse.InnerPawn?.RaceProps?.Animal == true) return corpse;
            return null;
        }

        private bool CreateFeature(WildlifeLandscapeKind kind, ThingDef species,
            IntVec3 wanted, bool debug, out WildlifeLandscapeFeature feature,
            WildlifeCrossroadResponse response = WildlifeCrossroadResponse.Undecided)
        {
            feature = null;
            if (!FindValidCell(wanted, out IntVec3 cell)) return false;
            ThingDef def = WildlifeLandscapeUtility.DefFor(kind);
            if (def == null) return false;
            feature = ThingMaker.MakeThing(def) as WildlifeLandscapeFeature;
            if (feature == null) return false;
            feature.kind = kind;
            feature.species = species;
            feature.createdTick = feature.lastUsedTick = Find.TickManager.TicksGame;
            feature.originResponse = response;
            feature.protectedByColony = response == WildlifeCrossroadResponse.Stewarded;
            feature.strength = debug ? 0.75f :
                response == WildlifeCrossroadResponse.Stewarded ? 0.55f :
                response == WildlifeCrossroadResponse.LeftWild ? 0.48f : 0.35f;
            GenSpawn.Spawn(feature, cell, map);
            WildlifeTestLog.Count("landscape.formed." + kind);
            WildlifeTestLog.Write("LivingLandscape",
                "formed kind=" + kind + " species=" +
                (species?.defName ?? "unknown") + " strength=" +
                feature.strength.ToString("0.00"), null, feature);
            WildlifeExperience.Record("Landscape",
                (species?.LabelCap.ToString() ?? "Wildlife") + " established a " +
                feature.LabelNoCount + ".", feature);
            if (HerdsMod.Settings.enableWildlifeAlerts &&
                (species == null || HuntingKnowledgeMapComponent.ColonyExperience(species) > 0f))
                Messages.Message((species?.LabelCap.ToString() ?? "Wildlife") +
                    " activity has formed a " + feature.LabelNoCount + ".", feature,
                    MessageTypeDefOf.NeutralEvent, false);
            return true;
        }

        private bool FindValidCell(IntVec3 wanted, out IntVec3 result)
        {
            int count = GenRadial.NumCellsInRadius(8f);
            for (int i = 0; i < count; i++)
            {
                IntVec3 cell = wanted + GenRadial.RadialPattern[i];
                if (!cell.InBounds(map) || !cell.Standable(map) ||
                    cell.CloseToEdge(map, 6) || cell.GetEdifice(map) != null ||
                    map.areaManager.Home[cell] ||
                    Features.Any(feature => feature.Position.DistanceToSquared(cell) < 144))
                    continue;
                result = cell;
                return true;
            }
            result = IntVec3.Invalid;
            return false;
        }

        public float HabitatBonus()
        {
            if (HerdsMod.Settings?.enableWildlifeLandscaping != true ||
                HerdsMod.Settings.enableLandscapeEffects != true) return 0f;
            float bonus = 0f;
            foreach (WildlifeLandscapeFeature feature in Features)
                bonus += WildlifeLandscapeUtility.HabitatWeight(feature.kind) *
                    feature.strength * (feature.protectedByColony ? 1.35f : 1f) *
                    Effectiveness(feature);
            return Mathf.Min(0.14f, bonus);
        }

        public float MigrationAttraction(ThingDef species)
        {
            if (HerdsMod.Settings?.enableWildlifeLandscaping != true ||
                HerdsMod.Settings.enableLandscapeEffects != true || species == null) return 0f;
            float attraction = Features.Where(feature => feature.species == species)
                .Sum(feature => feature.strength *
                    (feature.protectedByColony ? 0.075f : 0.045f) * Effectiveness(feature));
            return Mathf.Min(0.35f, attraction);
        }

        public float Effectiveness(WildlifeLandscapeFeature feature)
        {
            if (feature?.Spawned != true) return 0f;
            int buildings = 0;
            int cells = GenRadial.NumCellsInRadius(feature.InfluenceRadius);
            for (int i = 0; i < cells; i++)
            {
                IntVec3 cell = feature.Position + GenRadial.RadialPattern[i];
                if (!cell.InBounds(map)) continue;
                Building building = cell.GetEdifice(map);
                if (building?.Faction == Faction.OfPlayer) buildings++;
            }
            return ObstructionEffectiveness(buildings);
        }

        internal static float ObstructionEffectiveness(int buildings) =>
            buildings <= 0 ? 1f : buildings == 1 ? 0.6f :
            buildings == 2 ? 0.35f : 0.15f;

        public IntVec3 PreferredFeatureTarget(Pawn animal, IntVec3 center, int seed)
        {
            if (HerdsMod.Settings?.enableWildlifeLandscaping != true ||
                HerdsMod.Settings.enableLandscapeEffects != true || animal == null ||
                PositiveMod(seed + Find.TickManager.TicksGame / 2500, 4) != 0)
                return IntVec3.Invalid;
            WildlifeLandscapeFeature feature = Features
                .Where(value => value.species == animal.def &&
                    value.Position.DistanceToSquared(center) <= 6400)
                .OrderBy(value => value.Position.DistanceToSquared(center))
                .FirstOrDefault();
            return feature?.Position ?? IntVec3.Invalid;
        }

        public float HuntingBonus(Pawn hunter, ThingDef species)
        {
            if (HerdsMod.Settings?.enableWildlifeLandscaping != true ||
                HerdsMod.Settings.enableLandscapeEffects != true ||
                hunter == null || species == null) return 0f;
            return Mathf.Min(1.5f, Features.Where(feature => feature.species == species &&
                feature.studiedBy.Contains(hunter)).Sum(feature => (0.35f +
                    feature.strength * 0.3f) * Effectiveness(feature)));
        }

        public List<string> BridgeLines()
        {
            List<WildlifeLandscapeFeature> features = Features.ToList();
            List<string> lines = new List<string>
            {
                "landscape=features:" + features.Count + " forming:" + activities.Count +
                    " protected:" + features.Count(feature => feature.protectedByColony) +
                    " crossroads:" + Crossroads.Count()
            };
            lines.AddRange(features.OrderByDescending(feature => feature.strength).Take(10)
                .Select(feature => "feature=" + feature.kind + " species:" +
                    (feature.species?.defName ?? "unknown") + " strength:" +
                    feature.strength.ToString("0.00") + " protected:" +
                    (feature.protectedByColony ? 1 : 0) + " cell:" +
                    feature.Position.x + "," + feature.Position.z));
            lines.AddRange(activities.OrderByDescending(record =>
                record.progress / Threshold(record.kind)).Take(6).Select(record =>
                    "forming=" + record.kind + " species:" +
                    (record.species?.defName ?? "unknown") + " progress:" +
                    Mathf.Clamp01(record.progress / Threshold(record.kind)).ToString("0.00") +
                    " contributors:" + record.contributors.Count +
                    " response:" + ResponseLabel(record) +
                    " marker:" + (CrossroadFor(record.id) != null ? 1 : 0)));
            return lines;
        }

        public List<string> RoleLines()
        {
            List<string> lines = new List<string>();
            foreach (IGrouping<ThingDef, Pawn> group in map.mapPawns.AllPawnsSpawned
                .Where(Eligible).GroupBy(pawn => pawn.def)
                .OrderBy(group => group.Key.defName).Take(40))
            {
                IReadOnlyList<WildlifeEcologicalRole> roles =
                    WildlifeNicheDatabase.RolesFor(group.Key);
                lines.Add("role=" + group.Key.defName + " count:" + group.Count() +
                    " niches:" + (roles.Count == 0 ? "none" :
                    string.Join(",", roles)));
            }
            return lines.Count == 0 ? new List<string> { "roles=none" } : lines;
        }

        public List<string> DebugForceFeature(string requested)
        {
            List<Pawn> candidates = map.mapPawns.AllPawnsSpawned.Where(Eligible).ToList();
            foreach (Pawn pawn in candidates.OrderBy(value => value.thingIDNumber))
            {
                foreach (WildlifeEcologicalRole role in WildlifeNicheDatabase.RolesFor(pawn.def))
                {
                    WildlifeLandscapeKind kind = KindFor(role);
                    if (!requested.NullOrEmpty() &&
                        kind.ToString().IndexOf(requested,
                            StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (CreateFeature(kind, pawn.def, pawn.Position, true,
                        out WildlifeLandscapeFeature feature))
                        return new List<string>
                        {
                            "created=" + kind + " species:" + pawn.def.defName +
                            " thing:" + feature.thingIDNumber
                        };
                }
            }
            return new List<string> { "created=none reason:no_valid_candidate_or_cell" };
        }

        public List<string> DebugTestCrossroads()
        {
            if (HerdsMod.Settings?.enableWildlifeLandscaping != true ||
                HerdsMod.Settings.enableLandscapeCrossroads != true)
                return new List<string> { "crossroadTest=FAIL reason:disabled" };
            WildlifeLandscapeActivity activity = activities
                .OrderByDescending(ProgressFraction).FirstOrDefault();
            if (activity == null)
            {
                Pawn pawn = map.mapPawns.AllPawnsSpawned.Where(Eligible)
                    .FirstOrDefault(value => WildlifeNicheDatabase.RolesFor(value.def).Count > 0);
                WildlifeEcologicalRole? role = pawn == null ? null :
                    (WildlifeEcologicalRole?)WildlifeNicheDatabase.RolesFor(pawn.def).First();
                if (pawn == null || !role.HasValue)
                    return new List<string> { "crossroadTest=FAIL reason:no_candidate" };
                activity = new WildlifeLandscapeActivity
                {
                    id = nextActivityId++,
                    species = pawn.def,
                    kind = KindFor(role.Value),
                    cell = pawn.Position,
                    lastActivityTick = Find.TickManager.TicksGame,
                    progress = Threshold(KindFor(role.Value)) * 0.2f,
                    contributors = new List<int> { pawn.thingIDNumber }
                };
                activities.Add(activity);
            }
            activity.progress = Mathf.Max(activity.progress, Threshold(activity.kind) * 0.2f);
            WildlifeLandscapeCrossroad marker =
                EnsureCrossroad(activity, Find.TickManager.TicksGame, false);
            bool passed = marker?.Spawned == true && marker.activityId == activity.id &&
                ActivityById(activity.id) == activity && ProgressFraction(activity) >= 0.19f;
            return new List<string>
            {
                "crossroadTest=" + (passed ? "PASS" : "FAIL") +
                    " id:" + activity.id + " kind:" + activity.kind +
                    " progress:" + ProgressFraction(activity).ToString("0.00") +
                    " marker:" + (marker?.thingIDNumber ?? -1)
            };
        }

        public override void MapComponentDraw()
        {
            base.MapComponentDraw();
            if (!Prefs.DevMode || !WildlifeDevMaster.CompleteOverlayEnabled ||
                HerdsMod.Settings?.enableWildlifeLandscaping != true ||
                Find.CurrentMap != map) return;
            foreach (WildlifeLandscapeFeature feature in Features)
            {
                GenDraw.DrawRadiusRing(feature.Position, feature.InfluenceRadius,
                    WildlifeLandscapeUtility.Color(feature.kind));
            }
            foreach (WildlifeLandscapeActivity activity in activities
                .OrderByDescending(value => value.progress / Threshold(value.kind)).Take(12))
            {
                GenDraw.DrawRadiusRing(activity.cell, 3f,
                    WildlifeLandscapeUtility.Color(activity.kind));
                foreach (int contributor in activity.contributors.Take(4))
                {
                    Pawn pawn = map.mapPawns.AllPawnsSpawned
                        .FirstOrDefault(value => value.thingIDNumber == contributor);
                    if (pawn?.Spawned == true)
                        GenDraw.DrawLineBetween(pawn.Position.ToVector3Shifted(),
                            activity.cell.ToVector3Shifted(),
                            activity.response == WildlifeCrossroadResponse.Stewarded
                                ? SimpleColor.Green
                                : activity.response == WildlifeCrossroadResponse.LeftWild
                                    ? SimpleColor.Cyan : SimpleColor.Yellow);
                }
            }
        }

        public override void MapComponentOnGUI()
        {
            base.MapComponentOnGUI();
            if (!Prefs.DevMode || !WildlifeDevMaster.CompleteOverlayEnabled ||
                HerdsMod.Settings?.enableWildlifeLandscaping != true ||
                Find.CurrentMap != map) return;
            foreach (WildlifeLandscapeFeature feature in Features)
                GenMapUI.DrawThingLabel(feature, feature.kind + " " +
                    feature.strength.ToStringPercent());
            foreach (WildlifeLandscapeActivity activity in activities
                .OrderByDescending(value => value.progress / Threshold(value.kind)).Take(12))
            {
                GenMapUI.DrawThingLabel(activity.cell.ToVector3Shifted(),
                    activity.kind + " forming " +
                    Mathf.Clamp01(activity.progress / Threshold(activity.kind))
                        .ToStringPercent() + " | " + ResponseLabel(activity), Color.white);
            }
        }

        private static bool Eligible(Pawn pawn) => pawn?.Spawned == true && !pawn.Dead &&
            !pawn.Downed && pawn.Faction == null && pawn.RaceProps?.Animal == true &&
            pawn.RaceProps.IsFlesh && !pawn.RaceProps.IsAnomalyEntity;

        private static bool RequiresPopulation(WildlifeEcologicalRole role) =>
            role == WildlifeEcologicalRole.TrailMaker ||
            role == WildlifeEcologicalRole.Grazer ||
            role == WildlifeEcologicalRole.SeedCarrier ||
            role == WildlifeEcologicalRole.ShoreNester;

        private static float ActivityAmount(Pawn pawn, WildlifeEcologicalRole role)
        {
            float body = Mathf.Clamp(pawn.BodySize, 0.2f, 3f);
            return role == WildlifeEcologicalRole.TrailMaker ? Mathf.Lerp(0.8f, 2f, body / 3f) :
                role == WildlifeEcologicalRole.Grazer ? Mathf.Lerp(0.6f, 1.6f, body / 3f) :
                role == WildlifeEcologicalRole.Scavenger ? 2.2f :
                role == WildlifeEcologicalRole.WaterEngineer ? 1.8f :
                role == WildlifeEcologicalRole.SeedCarrier ? 0.8f : 1f;
        }

        private static WildlifeLandscapeKind KindFor(WildlifeEcologicalRole role) =>
            role == WildlifeEcologicalRole.TrailMaker ? WildlifeLandscapeKind.GameTrail :
            role == WildlifeEcologicalRole.Grazer ? WildlifeLandscapeKind.GrazingGround :
            role == WildlifeEcologicalRole.Browser ? WildlifeLandscapeKind.BrowsedGrove :
            role == WildlifeEcologicalRole.Rooter ? WildlifeLandscapeKind.RootingWallow :
            role == WildlifeEcologicalRole.SeedCarrier ? WildlifeLandscapeKind.SeedGrove :
            role == WildlifeEcologicalRole.ShoreNester ? WildlifeLandscapeKind.ShoreNest :
            role == WildlifeEcologicalRole.WaterEngineer ? WildlifeLandscapeKind.WatersideWorks :
            role == WildlifeEcologicalRole.TerritoryMarker ? WildlifeLandscapeKind.ScentPost :
            WildlifeLandscapeKind.FeedingRemains;

        private static float Threshold(WildlifeLandscapeKind kind) =>
            kind == WildlifeLandscapeKind.GameTrail ? 80f :
            kind == WildlifeLandscapeKind.GrazingGround ? 95f :
            kind == WildlifeLandscapeKind.BrowsedGrove ? 55f :
            kind == WildlifeLandscapeKind.RootingWallow ? 45f :
            kind == WildlifeLandscapeKind.SeedGrove ? 60f :
            kind == WildlifeLandscapeKind.ShoreNest ? 50f :
            kind == WildlifeLandscapeKind.WatersideWorks ? 35f :
            kind == WildlifeLandscapeKind.ScentPost ? 40f : 12f;

        private static int RequiredContributors(WildlifeLandscapeKind kind) =>
            kind == WildlifeLandscapeKind.GameTrail ||
            kind == WildlifeLandscapeKind.GrazingGround ||
            kind == WildlifeLandscapeKind.SeedGrove ||
            kind == WildlifeLandscapeKind.ShoreNest ? 2 : 1;

        private static IntVec3 Midpoint(IntVec3 a, IntVec3 b) =>
            new IntVec3((a.x + b.x) / 2, 0, (a.z + b.z) / 2);

        private static int PositiveMod(int value, int divisor)
        {
            int result = value % divisor;
            return result < 0 ? result + divisor : result;
        }

        [DebugAction("Wildlife", "Create ecological landscape feature",
            actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void DebugCreate() =>
            Find.CurrentMap?.GetComponent<WildlifeLandscapeMapComponent>()?
                .DebugForceFeature(null);

        [DebugAction("Wildlife", "Create and test Wildlife Crossroad",
            actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void DebugCrossroad()
        {
            string result = Find.CurrentMap?.GetComponent<WildlifeLandscapeMapComponent>()?
                .DebugTestCrossroads().FirstOrDefault() ?? "crossroadTest=FAIL reason:no_map";
            Messages.Message(result, MessageTypeDefOf.NeutralEvent, false);
        }
    }

    public static class WildlifeLandscapeUtility
    {
        public static string RoleSummary(ThingDef species)
        {
            IReadOnlyList<WildlifeEcologicalRole> roles =
                WildlifeNicheDatabase.RolesFor(species);
            return roles.Count == 0 ? "No persistent feature" :
                string.Join(", ", roles.Take(3).Select(RoleLabel)) +
                (roles.Count > 3 ? " +" + (roles.Count - 3) : "");
        }

        public static string RoleLabel(WildlifeEcologicalRole role) =>
            role == WildlifeEcologicalRole.TrailMaker ? "Trail maker" :
            role == WildlifeEcologicalRole.Grazer ? "Grazer" :
            role == WildlifeEcologicalRole.Browser ? "Browser" :
            role == WildlifeEcologicalRole.Rooter ? "Rooter" :
            role == WildlifeEcologicalRole.SeedCarrier ? "Seed carrier" :
            role == WildlifeEcologicalRole.ShoreNester ? "Shore nester" :
            role == WildlifeEcologicalRole.WaterEngineer ? "Waterside builder" :
            role == WildlifeEcologicalRole.TerritoryMarker ? "Territory marker" :
            "Scavenger";

        public static string RoleDescription(WildlifeEcologicalRole role) =>
            role == WildlifeEcologicalRole.TrailMaker ? "Repeated group movement can wear a game trail." :
            role == WildlifeEcologicalRole.Grazer ? "Repeated feeding can maintain a grazing ground." :
            role == WildlifeEcologicalRole.Browser ? "Feeding on woody plants can shape a browsed grove." :
            role == WildlifeEcologicalRole.Rooter ? "Rooting in suitable soil can form a wallow." :
            role == WildlifeEcologicalRole.SeedCarrier ? "Seed caching or dispersal can establish a seed grove." :
            role == WildlifeEcologicalRole.ShoreNester ? "Repeated use of a shoreline can establish a nesting site." :
            role == WildlifeEcologicalRole.WaterEngineer ? "Sustained shoreline work can reshape wetland habitat." :
            role == WildlifeEcologicalRole.TerritoryMarker ? "Repeated patrols can establish a scent post." :
            "Repeated use of a real carcass can leave a persistent feeding site.";

        public static string Label(WildlifeLandscapeKind kind) =>
            kind == WildlifeLandscapeKind.GameTrail ? "game trail" :
            kind == WildlifeLandscapeKind.GrazingGround ? "grazing ground" :
            kind == WildlifeLandscapeKind.BrowsedGrove ? "browsed grove" :
            kind == WildlifeLandscapeKind.RootingWallow ? "rooting wallow" :
            kind == WildlifeLandscapeKind.SeedGrove ? "seed grove" :
            kind == WildlifeLandscapeKind.ShoreNest ? "shore nesting site" :
            kind == WildlifeLandscapeKind.WatersideWorks ? "waterside works" :
            kind == WildlifeLandscapeKind.ScentPost ? "territorial scent post" :
            "feeding remains";

        public static string Effect(WildlifeLandscapeKind kind) =>
            kind == WildlifeLandscapeKind.GameTrail ? "Reused route; encourages local movement and return migration." :
            kind == WildlifeLandscapeKind.GrazingGround ? "Maintained forage patch; improves habitat carrying capacity." :
            kind == WildlifeLandscapeKind.BrowsedGrove ? "Browsed woodland edge; attracts compatible herbivores." :
            kind == WildlifeLandscapeKind.RootingWallow ? "Disturbed fertile soil; attracts rooting omnivores." :
            kind == WildlifeLandscapeKind.SeedGrove ? "Cached and dispersed seed; strengthens shelter and forage habitat." :
            kind == WildlifeLandscapeKind.ShoreNest ? "Repeated waterside nesting; supports seasonal returns." :
            kind == WildlifeLandscapeKind.WatersideWorks ? "Reshaped shoreline habitat; supports wetland wildlife." :
            kind == WildlifeLandscapeKind.ScentPost ? "Territorial landmark; anchors predator movement." :
            "A real feeding site; reveals scavenger and predator activity.";

        public static string Condition(float strength) =>
            strength < 0.25f ? "Faint" : strength < 0.5f ? "Developing" :
            strength < 0.8f ? "Established" : "Deeply established";

        public static Color Color(WildlifeLandscapeKind kind) =>
            kind == WildlifeLandscapeKind.GameTrail ? new Color(0.52f, 0.39f, 0.22f) :
            kind == WildlifeLandscapeKind.GrazingGround ? new Color(0.48f, 0.58f, 0.25f) :
            kind == WildlifeLandscapeKind.BrowsedGrove ? new Color(0.30f, 0.48f, 0.24f) :
            kind == WildlifeLandscapeKind.RootingWallow ? new Color(0.37f, 0.27f, 0.18f) :
            kind == WildlifeLandscapeKind.SeedGrove ? new Color(0.27f, 0.60f, 0.31f) :
            kind == WildlifeLandscapeKind.ShoreNest ? new Color(0.69f, 0.60f, 0.37f) :
            kind == WildlifeLandscapeKind.WatersideWorks ? new Color(0.29f, 0.52f, 0.50f) :
            kind == WildlifeLandscapeKind.ScentPost ? new Color(0.48f, 0.28f, 0.20f) :
            new Color(0.64f, 0.61f, 0.48f);

        public static float HabitatWeight(WildlifeLandscapeKind kind) =>
            kind == WildlifeLandscapeKind.GrazingGround ? 0.018f :
            kind == WildlifeLandscapeKind.SeedGrove ? 0.022f :
            kind == WildlifeLandscapeKind.ShoreNest ? 0.016f :
            kind == WildlifeLandscapeKind.WatersideWorks ? 0.025f :
            kind == WildlifeLandscapeKind.BrowsedGrove ? 0.012f : 0.007f;

        public static ThingDef DefFor(WildlifeLandscapeKind kind) =>
            kind == WildlifeLandscapeKind.GameTrail ? HerdsDefOf.Herds_GameTrail :
            kind == WildlifeLandscapeKind.GrazingGround ? HerdsDefOf.Herds_GrazingGround :
            kind == WildlifeLandscapeKind.BrowsedGrove ? HerdsDefOf.Herds_BrowsedGrove :
            kind == WildlifeLandscapeKind.RootingWallow ? HerdsDefOf.Herds_RootingWallow :
            kind == WildlifeLandscapeKind.SeedGrove ? HerdsDefOf.Herds_SeedGrove :
            kind == WildlifeLandscapeKind.ShoreNest ? HerdsDefOf.Herds_ShoreNest :
            kind == WildlifeLandscapeKind.WatersideWorks ? HerdsDefOf.Herds_WatersideWorks :
            kind == WildlifeLandscapeKind.ScentPost ? HerdsDefOf.Herds_ScentPost :
            HerdsDefOf.Herds_FeedingRemains;
    }

    public sealed class Window_WildlifeLandscape : Window
    {
        private readonly Map map;
        private Vector2 scroll;
        public override Vector2 InitialSize => new Vector2(820f, 680f);

        public Window_WildlifeLandscape(Map map)
        {
            this.map = map;
            doCloseX = true;
            absorbInputAroundWindow = true;
        }

        public override void DoWindowContents(Rect rect)
        {
            WildlifeLandscapeMapComponent component =
                map?.GetComponent<WildlifeLandscapeMapComponent>();
            List<WildlifeLandscapeFeature> features =
                component?.Features.OrderByDescending(feature => feature.strength).ToList() ??
                new List<WildlifeLandscapeFeature>();
            IReadOnlyList<WildlifeLandscapeActivity> forming =
                component?.Activities ?? Array.Empty<WildlifeLandscapeActivity>();
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, rect.width, 32f), "Landscape");
            Text.Font = GameFont.Small;
            GUI.color = new Color(0.70f, 0.78f, 0.69f);
            Widgets.Label(new Rect(0f, 31f, rect.width, 42f),
                "Repeated wildlife activity gradually shapes shared trails, feeding places, nesting sites, and territorial landmarks.");
            GUI.color = Color.white;

            float width = (rect.width - 16f) / 3f;
            Metric(new Rect(0f, 78f, width, 58f), "Established", features.Count.ToString(),
                new Color(0.36f, 0.58f, 0.31f));
            Metric(new Rect(width + 8f, 78f, width, 58f), "Forming", forming.Count.ToString(),
                new Color(0.60f, 0.48f, 0.25f));
            Metric(new Rect((width + 8f) * 2f, 78f, width, 58f), "Protected",
                features.Count(feature => feature.protectedByColony).ToString(),
                new Color(0.29f, 0.52f, 0.50f));

            Rect outer = new Rect(0f, 148f, rect.width, rect.height - 148f);
            float contentHeight = Mathf.Max(outer.height,
                46f + features.Count * 64f + Mathf.Min(8, forming.Count) * 68f);
            Rect view = new Rect(0f, 0f, outer.width - 18f, contentHeight);
            Widgets.BeginScrollView(outer, ref scroll, view);
            Widgets.Label(new Rect(0f, 0f, view.width, 28f),
                features.Count == 0
                    ? "No persistent features yet. They require repeated compatible activity in the same place."
                    : "Established Features");
            float y = 36f;
            foreach (WildlifeLandscapeFeature feature in features)
            {
                Rect row = new Rect(0f, y, view.width, 56f);
                Widgets.DrawMenuSection(row);
                Widgets.DrawBoxSolid(new Rect(row.x, row.y, 5f, row.height),
                    WildlifeLandscapeUtility.Color(feature.kind));
                Widgets.Label(new Rect(row.x + 14f, row.y + 6f, row.width - 150f, 22f),
                    feature.LabelCap + " - " +
                    (feature.species?.LabelCap.ToString() ?? "Unknown wildlife"));
                Text.Font = GameFont.Tiny;
                GUI.color = new Color(0.70f, 0.76f, 0.69f);
                Widgets.Label(new Rect(row.x + 14f, row.y + 30f, row.width - 150f, 18f),
                    WildlifeLandscapeUtility.Condition(feature.strength) +
                    (feature.protectedByColony ? " - Protected" : "") + " - " +
                    Mathf.RoundToInt((component?.Effectiveness(feature) ?? 0f) * 100f) + "% effective");
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
                if (Widgets.ButtonText(new Rect(row.xMax - 118f, row.y + 11f, 104f, 34f),
                    "Focus"))
                {
                    WildlifeUI.Show(feature);
                }
                TooltipHandler.TipRegion(row, WildlifeLandscapeUtility.Effect(feature.kind));
                y += 64f;
            }
            if (forming.Count > 0)
            {
                Widgets.Label(new Rect(0f, y + 4f, view.width, 26f), "Developing Places");
                y += 34f;
                foreach (WildlifeLandscapeActivity activity in forming
                    .OrderByDescending(value => value.progress).Take(8))
                {
                    WildlifeLandscapeCrossroad marker = component.CrossroadFor(activity.id);
                    Rect row = new Rect(0f, y, view.width, 60f);
                    Widgets.DrawMenuSection(row);
                    Widgets.DrawBoxSolid(new Rect(row.x, row.y, 5f, row.height),
                        WildlifeLandscapeUtility.Color(activity.kind));
                    int knowledge = activity.species == null ? 0 :
                        HuntingKnowledgeMapComponent.ColonyLevel(activity.species);
                    Widgets.Label(new Rect(row.x + 14f, row.y + 5f, row.width - 230f, 22f),
                        WildlifeLandscapeUtility.Label(activity.kind).CapitalizeFirst() +
                        " — " + (knowledge > 0
                            ? activity.species.LabelCap.ToString()
                            : "Unidentified wildlife"));
                    float fraction = component.ProgressFraction(activity);
                    Rect bar = new Rect(row.x + 14f, row.y + 33f, row.width - 230f, 16f);
                    Widgets.FillableBar(bar, fraction);
                    Text.Font = GameFont.Tiny;
                    Widgets.Label(new Rect(bar.x + 6f, bar.y - 1f, bar.width - 12f, 18f),
                        activity.observedBy.Count > 0 ? fraction.ToStringPercent() :
                        component.ProgressStage(activity));
                    Text.Font = GameFont.Small;
                    Widgets.Label(new Rect(row.xMax - 212f, row.y + 18f, 104f, 24f),
                        component.ResponseLabel(activity));
                    if (marker != null && Widgets.ButtonText(
                        new Rect(row.xMax - 102f, row.y + 13f, 90f, 34f), "Focus"))
                    {
                        WildlifeUI.Show(marker);
                    }
                    TooltipHandler.TipRegion(row,
                        WildlifeLandscapeUtility.Effect(activity.kind) +
                        "\n\nSelect Focus to decide how the colony responds.");
                    y += 68f;
                }
            }
            Widgets.EndScrollView();
        }

        private static void Metric(Rect rect, string label, string value, Color color)
        {
            Widgets.DrawMenuSection(rect);
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, 5f, rect.height), color);
            Widgets.Label(new Rect(rect.x + 13f, rect.y + 7f, rect.width - 20f, 20f), label);
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(rect.x + 13f, rect.y + 27f, rect.width - 20f, 26f), value);
            Text.Font = GameFont.Small;
        }
    }
}
