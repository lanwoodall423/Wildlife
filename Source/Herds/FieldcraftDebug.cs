using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using LudeonTK;
using RimWorld;
using UnityEngine;
using Verse;

namespace Herds
{
    public static class FieldcraftDebug
    {
        private const string Category = "Herds and Hiders";
        public static bool HuntOverlay;
        public static bool KnowledgeOverlay;
        public static bool SignOverlay;
        public static bool GuardianOverlay;
        public static bool WarningOverlay;

        [DebugAction(Category, "Toggle hunting overlay", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void ToggleHunts() => Toggle(ref HuntOverlay, "Hunting");
        [DebugAction(Category, "Toggle knowledge overlay", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void ToggleKnowledge() => Toggle(ref KnowledgeOverlay, "Species knowledge");
        [DebugAction(Category, "Toggle wildlife-sign overlay", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void ToggleSigns() => Toggle(ref SignOverlay, "Wildlife sign");
        [DebugAction(Category, "Toggle guardian patrol overlay", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void ToggleGuardians() => Toggle(ref GuardianOverlay, "Guardian patrol");
        [DebugAction(Category, "Toggle predator-warning overlay", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void ToggleWarnings() => Toggle(ref WarningOverlay, "Predator warning");
        [DebugAction(Category, "Give colonist master knowledge of all species", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void MasterAllSpecies(Pawn colonist)
        {
            if (colonist?.Spawned != true || colonist.Faction != Faction.OfPlayer || !colonist.RaceProps.Humanlike)
            {
                Messages.Message("Choose a player colonist.", MessageTypeDefOf.RejectInput, false);
                return;
            }
            int count = colonist.Map.GetComponent<HuntingKnowledgeMapComponent>().DebugMasterAllSpecies(colonist);
            Messages.Message(colonist.LabelShortCap + " now has Master knowledge of " + count + " animal species.", colonist, MessageTypeDefOf.PositiveEvent, false);
        }

        private static void Toggle(ref bool value, string label) { value = !value; Messages.Message(label + " overlay " + (value ? "enabled." : "disabled."), MessageTypeDefOf.NeutralEvent, false); }

        public static void ShowOverlayMenu()
        {
            Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>
            {
                new FloatMenuOption("Hunts: " + OnOff(HuntOverlay), ToggleHunts),
                new FloatMenuOption("Knowledge: " + OnOff(KnowledgeOverlay), ToggleKnowledge),
                new FloatMenuOption("Signs: " + OnOff(SignOverlay), ToggleSigns),
                new FloatMenuOption("Guardians: " + OnOff(GuardianOverlay), ToggleGuardians),
                new FloatMenuOption("Warnings: " + OnOff(WarningOverlay), ToggleWarnings)
            }));
        }

        private static string OnOff(bool value) => value ? "ON" : "OFF";
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class FieldcraftDevGizmoPatch
    {
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> values, Pawn __instance)
        {
            foreach (Gizmo gizmo in values) yield return gizmo;
            if (!Prefs.DevMode || __instance?.Spawned != true || Find.Selector.SingleSelectedThing != __instance || __instance.Faction != Faction.OfPlayer || !__instance.RaceProps.Humanlike) yield break;
            yield return new Command_Action { defaultLabel = "DEV: Wildlife Overview", defaultDesc = "Open the complete organized wildlife development dashboard.", icon = TexCommand.OpenLinkedQuestTex, action = WildlifeDevMaster.OpenDashboard };
            yield return WildlifeDevMenus.CompleteOverlayToggle();
            yield return WildlifeDevMenus.DiagnosticToggle(__instance);
            yield return new Command_Action { defaultLabel = "DEV: Fieldcraft Tests...", defaultDesc = "Open organized hunt, knowledge, wound, sign, gear, and warning tests.", icon = TexCommand.SquadAttack, action = () => WildlifeDevMenus.ShowColonistTests(__instance) };
            if (!WildlifeDevMenus.ShowExpandedColonistGizmos) yield break;
            yield return new Command_Toggle
            {
                defaultLabel = "DEV: Diagnostic Log", defaultDesc = "Toggle the shared [WildlifeTest] diagnostic session.", icon = TexCommand.OpenLinkedQuestTex,
                isActive = () => WildlifeTestLog.Enabled,
                toggleAction = () => { WildlifeTestLog.Toggle(); if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("Session", "Enabled from colonist fieldcraft gizmo; skill=" + ColonistHuntingUtility.HuntingSkill(__instance).ToString("0.0"), __instance); Messages.Message("Wildlife diagnostic logging " + (WildlifeTestLog.Enabled ? "enabled." : "disabled."), MessageTypeDefOf.NeutralEvent, false); }
            };
            yield return new Command_Action { defaultLabel = "DEV: Fieldcraft Overlays", defaultDesc = "Toggle hunt positions, personal knowledge, signs, patrol areas, and warning visuals.", icon = TexCommand.GatherSpotActive, action = FieldcraftDebug.ShowOverlayMenu };
            yield return new Command_Action { defaultLabel = "DEV: Set Species Knowledge", defaultDesc = "Choose an animal, then set this colonist's personal knowledge tier.", icon = TexCommand.OpenLinkedQuestTex, action = () => TargetAnimal(animal => ShowKnowledgeMenu(__instance, animal)) };
            yield return new Command_Action { defaultLabel = "DEV: Test Group Hunt", defaultDesc = "Choose an animal and immediately create a balanced coordinated hunt using selected colonists.", icon = TexCommand.SquadAttack, action = () => TargetAnimal(animal =>
            {
                List<Pawn> hunters = Find.Selector.SelectedPawns.Where(pawn => pawn.Spawned && pawn.Faction == Faction.OfPlayer && pawn.RaceProps.Humanlike && !pawn.Downed).ToList();
                if (!hunters.Contains(__instance)) hunters.Add(__instance);
                __instance.Map.GetComponent<WildlifeHuntCoordinator>().Begin(animal, hunters, new HuntPlanOptions());
            }) };
            yield return new Command_Action { defaultLabel = "DEV: Wound Animal", defaultDesc = "Choose an animal and inflict a controlled wound to test blood trails and pursuit policy.", icon = TexCommand.Attack, action = () => TargetAnimal(animal => animal.TakeDamage(new DamageInfo(DamageDefOf.Cut, 18f, 0f, -1f, __instance))) };
            yield return new Command_Action { defaultLabel = "DEV: Create Wildlife Sign", defaultDesc = "Choose an animal and create an appropriate track, territory mark, or blood trail at its position.", icon = TexCommand.Replant, action = () => TargetAnimal(animal => __instance.Map.GetComponent<WildlifeFieldcraftMapComponent>().DebugCreateSign(animal)) };
            yield return new Command_Action { defaultLabel = "DEV: Spawn Fieldcraft Gear", defaultDesc = "Spawn camouflage supplies and binoculars beside this colonist.", icon = TexCommand.Replant, action = () =>
            {
                Thing camo = ThingMaker.MakeThing(HerdsDefOf.Herds_CamouflageSupplies); camo.stackCount = 10; GenSpawn.Spawn(camo, __instance.Position, __instance.Map);
                GenSpawn.Spawn(ThingMaker.MakeThing(HerdsDefOf.Herds_FieldBinoculars), __instance.Position, __instance.Map);
            } };
            yield return new Command_Action { defaultLabel = "DEV: Test Predator Warning", defaultDesc = "Choose a predator and issue an approximate warning immediately.", icon = TexCommand.CannotShoot, action = () => TargetAnimal(animal => __instance.Map.GetComponent<WildlifeStewardMapComponent>().DebugPredatorWarning(animal)) };
        }

        private static void TargetAnimal(System.Action<Pawn> action)
        {
            TargetingParameters parameters = new TargetingParameters { canTargetPawns = true, canTargetAnimals = true, canTargetHumans = false, canTargetLocations = false, validator = target => target.Thing is Pawn pawn && pawn.Spawned && pawn.RaceProps.Animal };
            Find.Targeter.BeginTargeting(parameters, target => action((Pawn)target.Thing));
        }

        private static void ShowKnowledgeMenu(Pawn colonist, Pawn animal)
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            float[] xp = { 0f, 35f, 120f, 300f, 650f, 1200f };
            for (int i = 0; i < xp.Length; i++) { int level = i; options.Add(new FloatMenuOption(HuntingKnowledgeMapComponent.LevelLabel(i), () => colonist.Map.GetComponent<HuntingKnowledgeMapComponent>().DebugSet(colonist, animal.def, xp[level]))); }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        public static void BeginKnowledgeTarget(Pawn colonist) => TargetAnimal(animal => ShowKnowledgeMenu(colonist, animal));
        public static void MasterAllKnowledge(Pawn colonist) => FieldcraftDebug.MasterAllSpecies(colonist);
        public static void BeginGroupHuntTarget(Pawn colonist) => TargetAnimal(animal =>
        {
            List<Pawn> hunters = Find.Selector.SelectedPawns.Where(pawn => pawn.Spawned && pawn.Faction == Faction.OfPlayer && pawn.RaceProps.Humanlike && !pawn.Downed).ToList();
            if (!hunters.Contains(colonist)) hunters.Add(colonist);
            colonist.Map.GetComponent<WildlifeHuntCoordinator>().Begin(animal, hunters, new HuntPlanOptions());
        });
        public static void BeginWoundTarget(Pawn colonist) => TargetAnimal(animal => animal.TakeDamage(new DamageInfo(DamageDefOf.Cut, 18f, 0f, -1f, colonist)));
        public static void BeginSignTarget(Pawn colonist) => TargetAnimal(animal => colonist.Map.GetComponent<WildlifeFieldcraftMapComponent>().DebugCreateSign(animal));
        public static void SpawnGear(Pawn colonist)
        {
            Thing camo = ThingMaker.MakeThing(HerdsDefOf.Herds_CamouflageSupplies); camo.stackCount = 10; GenSpawn.Spawn(camo, colonist.Position, colonist.Map);
            GenSpawn.Spawn(ThingMaker.MakeThing(HerdsDefOf.Herds_FieldBinoculars), colonist.Position, colonist.Map);
        }
        public static void BeginWarningTarget(Pawn colonist) => TargetAnimal(animal => colonist.Map.GetComponent<WildlifeStewardMapComponent>().DebugPredatorWarning(animal));
    }
}
