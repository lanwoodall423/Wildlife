using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Herds
{
    public sealed class Building_HidingRefuge : Building
    {
        public override string LabelMouseover => Find.Selector?.IsSelected(this) == true ? base.LabelMouseover : string.Empty;
    }

    public sealed class CompProperties_HidingRefuge : CompProperties
    {
        public int capacity = 3;

        public CompProperties_HidingRefuge()
        {
            compClass = typeof(CompHidingRefuge);
        }
    }

    public sealed class CompHidingRefuge : ThingComp
    {
        public CompProperties_HidingRefuge Props => (CompProperties_HidingRefuge)props;

        public override string CompInspectStringExtra()
        {
            if (parent?.Spawned != true) return null;
            HerdMapComponent component = parent.Map.GetComponent<HerdMapComponent>();
            IReadOnlyList<Pawn> hidden = component?.HiddenPreyAt(parent);
            IReadOnlyList<Pawn> home = component?.HomePreyAt(parent);
            var lines = new List<string>();
            if (home?.Count > 0) lines.Add("Home animals: " + string.Join(", ", home.Select(pawn => pawn.LabelShortCap)));
            if (hidden?.Count > 0) lines.Add("Currently hiding: " + string.Join(", ", hidden.Select(pawn => pawn.LabelShortCap)));
            return lines.Count > 0 ? string.Join("\n", lines) : null;
        }
    }

    [DefOf]
    public static class HerdsDefOf
    {
        public static JobDef Herds_Hide;
        public static ThingDef Herds_AnimalBurrow;
        public static ThingDef Herds_ObservationPost;
        public static ThingDef Herds_WildlifeBait;
        public static ThingDef Herds_PredatorDeterrent;
        public static ThingDef Herds_WildlifeReserve;
        public static ThingDef Herds_ScentMaskStation;
        public static ThingDef Herds_WildlifeSign;
        public static JobDef Herds_ManObservationPost;
        public static JobDef Herds_FieldcraftHunt;
        public static JobDef Herds_EmbarkHuntingExpedition;
        public static JobDef Herds_StudyWildlifeSign;
        public static JobDef Herds_StudyLandscapeFeature;
        public static JobDef Herds_ObserveLandscapeCrossroad;
        public static JobDef Herds_StewardLandscapeCrossroad;
        public static JobDef Herds_FollowWildlifeTrail;
        public static JobDef Herds_StudyNotableAnimal;
        public static JobDef Herds_ObserveWildlifeMoment;
        public static JobDef Herds_RetellWildlifeStory;
        public static JobDef Herds_WildlifeCeremonyGather;
        public static HediffDef Herds_RanchGuardian;
        public static HediffDef Herds_HuntedAdrenaline;
        public static HediffDef Herds_HuntingOnTrack;
        public static HediffDef Herds_HuntFatigue;
        public static HediffDef Herds_FlightBurst;
        public static ThingDef Herds_CamouflageSupplies;
        public static ThingDef Herds_FieldBinoculars;
        public static ThingDef Herds_WildlifeSnare;
        public static ThingDef Herds_HuntingSpot;
        public static ThingDef Herds_HabitatRestoration;
        public static ThingDef Herds_WildlifeWaterSource;
        public static ThingDef Herds_MigrationCorridor;
        public static ThingDef Herds_ManagedBurnMarker;
        public static ThingDef Herds_CameraTrap;
        public static ThingDef Herds_TelemetryStation;
        public static ThingDef Herds_TrackingCollarItem;
        public static ThingDef Herds_WildlifeTrophy;
        public static ThingDef Herds_FolkloreCairn;
        public static ThingDef Herds_GameTrail;
        public static ThingDef Herds_GrazingGround;
        public static ThingDef Herds_BrowsedGrove;
        public static ThingDef Herds_RootingWallow;
        public static ThingDef Herds_SeedGrove;
        public static ThingDef Herds_ShoreNest;
        public static ThingDef Herds_WatersideWorks;
        public static ThingDef Herds_ScentPost;
        public static ThingDef Herds_FeedingRemains;
        public static ThingDef Herds_LandscapeCrossroad;
        public static RulePackDef Herds_LogSignalAlarm;
        public static RulePackDef Herds_LogSignalHumanDanger;
        public static RulePackDef Herds_LogSignalAllClear;
        public static RulePackDef Herds_LogSignalContact;
        public static RulePackDef Herds_LogSignalFood;
        public static RulePackDef Herds_LogSignalWater;
        public static RulePackDef Herds_LogSignalCoordination;
        public static HediffDef Herds_TrackingCollar;
        [MayRequireIdeology] public static PreceptDef Herds_WildlifeEthic_Reverence;
        [MayRequireIdeology] public static PreceptDef Herds_WildlifeEthic_Stewardship;
        [MayRequireIdeology] public static PreceptDef Herds_WildlifeEthic_Tradition;
        [MayRequireIdeology] public static PreceptDef Herds_WildlifeEthic_Indifferent;
        [MayRequireIdeology] public static ThoughtDef Herds_WildlifeHarmony;
        [MayRequireIdeology] public static ThoughtDef Herds_WildlifeDishonored;
        [MayRequireIdeology] public static ThoughtDef Herds_TraditionalHunt;
        [MayRequireIdeology] public static ThoughtDef Herds_WildlifeCeremony;
        public static ThoughtDef Herds_HeardWildlifeLegend;
        public static ThoughtDef Herds_InspiredByWildlifeMemorial;
        public static ThoughtDef Herds_ProtectedAnimalDied;
        public static InspirationDef Herds_WildlifeInsight;
        public static TraitDef Herds_WildlifeAttuned;
        [MayRequireIdeology] public static PreceptDef Herds_IdeoRole_MasterHunter;
        [MayRequireIdeology] public static PreceptDef Herds_IdeoRole_MasterConservationist;
        public static WorldObjectDef Herds_HuntingExpeditionMarker;

        static HerdsDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(HerdsDefOf));
        }
    }

    public sealed class JobDriver_HideInRefuge : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return Toils_General.Wait(Mathf.Max(30, HerdsMod.Settings.hideEntryTicks), TargetIndex.B);
            Toil enter = ToilMaker.MakeToil("EnterRefuge");
            enter.initAction = delegate
            {
                Thing refuge = job.targetA.Thing;
                Thing threat = job.targetB.Thing;
                Map refugeMap = refuge?.Map;
                refugeMap?.GetComponent<HerdMapComponent>()?.TryHide(pawn, refuge, threat);
            };
            enter.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return enter;
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(Plant), nameof(Plant.GetInspectString))]
    public static class PlantHidingInspectPatch
    {
        public static void Postfix(Plant __instance, ref string __result)
        {
            if (HerdsMod.Settings?.enablePreyAndHerds != true || __instance?.Spawned != true) return;
            HerdMapComponent component = __instance.Map.GetComponent<HerdMapComponent>();
            IReadOnlyList<Pawn> hidden = component?.HiddenPreyAt(__instance);
            IReadOnlyList<Pawn> home = component?.HomePreyAt(__instance);
            var lines = new List<string>();
            if (home?.Count > 0) lines.Add("Tree home: " + string.Join(", ", home.Select(pawn => pawn.LabelShortCap)));
            if (hidden?.Count > 0) lines.Add("Currently hiding: " + string.Join(", ", hidden.Select(pawn => pawn.LabelShortCap)));
            if (lines.Count == 0) return;
            string text = string.Join("\n", lines);
            __result = __result.NullOrEmpty() ? text : __result + "\n" + text;
        }
    }

    public sealed class ITab_HidingRefuge : ITab
    {
        private Vector2 scroll;

        public ITab_HidingRefuge()
        {
            size = new Vector2(460f, 320f);
            labelKey = "Herds_Hiding";
        }

        public override bool IsVisible
        {
            get
            {
                if (HerdsMod.Settings?.enablePreyAndHerds != true) return false;
                Thing refuge = SelThing;
                HerdMapComponent component = refuge?.Map?.GetComponent<HerdMapComponent>();
                return component != null && (component.HiddenPreyAt(refuge).Count > 0 || component.HomePreyAt(refuge).Count > 0);
            }
        }

        protected override void FillTab()
        {
            Thing refuge = SelThing;
            HerdMapComponent component = refuge?.Map?.GetComponent<HerdMapComponent>();
            if (component == null) return;
            IReadOnlyList<Pawn> hidden = component.HiddenPreyAt(refuge);
            IReadOnlyList<Pawn> home = component.HomePreyAt(refuge);
            Rect rect = new Rect(0f, 0f, size.x, size.y).ContractedBy(12f);
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, 32f), refuge is Plant ? refuge.LabelCap + " - claimed tree home" : refuge.LabelCap);
            Text.Font = GameFont.Small;
            Rect outer = new Rect(rect.x, rect.y + 38f, rect.width, rect.height - 38f);
            float contentHeight = 34f + hidden.Count * 30f + 42f + home.Count * 30f;
            Rect view = new Rect(0f, 0f, outer.width - 16f, Mathf.Max(outer.height, contentHeight));
            Widgets.BeginScrollView(outer, ref scroll, view);
            float y = 0f;
            Widgets.Label(new Rect(0f, y, view.width, 28f), "Currently Hiding (" + hidden.Count + ")");
            y += 32f;
            for (int i = 0; i < hidden.Count; i++, y += 30f) Widgets.Label(new Rect(12f, y, view.width - 12f, 26f), hidden[i].LabelShortCap + " - " + hidden[i].def.LabelCap);
            y += 8f;
            Widgets.Label(new Rect(0f, y, view.width, 28f), "Home Animals (" + home.Count + ")");
            y += 32f;
            for (int i = 0; i < home.Count; i++, y += 30f)
            {
                Rect row = new Rect(12f, y, view.width - 12f, 26f);
                Widgets.DrawHighlightIfMouseover(row);
                Widgets.Label(row, home[i].LabelShortCap + " - " + home[i].def.LabelCap);
                if (Widgets.ButtonInvisible(row) && home[i].Spawned)
                {
                    Find.Selector.ClearSelection();
                    Find.Selector.Select(home[i]);
                    CameraJumper.TryJump(home[i]);
                }
            }
            Widgets.EndScrollView();
        }
    }
}
