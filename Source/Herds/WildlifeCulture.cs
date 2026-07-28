using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Herds
{
    public sealed class CompProperties_FolkloreDisplay : CompProperties
    {
        public CompProperties_FolkloreDisplay() => compClass = typeof(CompFolkloreDisplay);
    }

    public sealed class CompFolkloreDisplay : ThingComp
    {
        public string storyTitle;
        public string storyText;
        public ThingDef species;

        public bool Assigned => !storyTitle.NullOrEmpty();

        public override void PostExposeData()
        {
            Scribe_Values.Look(ref storyTitle, "storyTitle");
            Scribe_Values.Look(ref storyText, "storyText");
            Scribe_Defs.Look(ref species, "storySpecies");
        }

        public override string CompInspectStringExtra() => Assigned
            ? "Dedicated story: " + storyTitle + "\n" + storyText
            : "No wildlife story has been dedicated here.";

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (HerdsMod.Settings?.enableFolkloreDisplays != true) yield break;
            yield return new Command_Action
            {
                defaultLabel = Assigned ? "Change Story" : "Dedicate Story",
                defaultDesc = "Choose a recorded piece of wildlife folklore for this cairn.",
                icon = TexCommand.OpenLinkedQuestTex,
                action = ChooseStory
            };
        }

        private void ChooseStory()
        {
            IReadOnlyList<WildlifeFolkloreRecord> stories = parent.Map
                .GetComponent<WildlifeMemoryMapComponent>()?.Folklore;
            List<FloatMenuOption> options = stories?.OrderByDescending(value => value.retellings)
                .Select(value => new FloatMenuOption(value.title, () =>
                {
                    storyTitle = value.title;
                    storyText = value.story;
                    species = value.species;
                    Messages.Message("The cairn is now dedicated to " + value.title + ".", parent,
                        MessageTypeDefOf.PositiveEvent, false);
                })).ToList() ?? new List<FloatMenuOption>();
            if (options.Count == 0) options.Add(new FloatMenuOption("No folklore has been recorded", null));
            Find.WindowStack.Add(new FloatMenu(options));
        }
    }

    public sealed class Building_FolkloreCairn : Building
    {
        public bool StoryAssigned => GetComp<CompFolkloreDisplay>()?.Assigned == true;

        public override string LabelNoCount => StoryAssigned
            ? base.LabelNoCount + ": " + GetComp<CompFolkloreDisplay>().storyTitle
            : base.LabelNoCount;
    }

    [HarmonyPatch(typeof(InteractionWorker_RecruitAttempt), nameof(InteractionWorker_RecruitAttempt.Interacted))]
    public static class WildlifeMemoryTamingPatch
    {
        public static void Prefix(Pawn initiator, Pawn recipient, out bool __state) =>
            __state = recipient?.Faction == Faction.OfPlayer;

        public static void Postfix(Pawn initiator, Pawn recipient, bool __state)
        {
            if (HerdsMod.Settings?.enableAnimalMemory != true || initiator?.Faction != Faction.OfPlayer ||
                recipient?.RaceProps?.Animal != true) return;
            WildlifeMemoryMapComponent memory = recipient.MapHeld?.GetComponent<WildlifeMemoryMapComponent>();
            float trust = memory?.TrustFor(recipient, initiator) ?? 0f;
            float fear = memory?.FearFor(recipient, initiator) ?? 0f;
            WildlifeMemoryUtility.Remember(recipient, initiator, AnimalMemoryKind.Called, 0.25f);
            if (__state || recipient.Faction == Faction.OfPlayer || trust < 0.2f) return;
            float bonusChance = Mathf.Clamp01(trust * 0.18f - fear * 0.08f);
            if (Rand.Chance(bonusChance))
            {
                InteractionWorker_RecruitAttempt.DoRecruit(initiator, recipient, true);
                Messages.Message(recipient.LabelShortCap + " trusted " + initiator.LabelShortCap +
                    " enough to accept taming.", recipient, MessageTypeDefOf.PositiveEvent, false);
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_InteractionsTracker), nameof(Pawn_InteractionsTracker.TryInteractWith))]
    public static class ColonyAnimalInteractionMemoryPatch
    {
        public static void Postfix(Pawn ___pawn, Pawn recipient, InteractionDef intDef, bool __result)
        {
            if (!__result || HerdsMod.Settings?.enableAnimalMemory != true || ___pawn == null ||
                recipient == null || intDef == null) return;
            if (HerdsMod.Settings.enableAnimalSocialMemory &&
                ___pawn.RaceProps?.Animal == true && recipient.RaceProps?.Animal == true)
            {
                string socialName = intDef.defName?.ToLowerInvariant() ?? string.Empty;
                AnimalSocialMemoryKind socialKind =
                    socialName.Contains("fight") || socialName.Contains("aggressive") ||
                    socialName.Contains("insult") || socialName.Contains("reject")
                        ? AnimalSocialMemoryKind.Fought :
                    socialName.Contains("mate") || socialName.Contains("lovin")
                        ? AnimalSocialMemoryKind.MateBond :
                    socialName.Contains("nuzzle") || socialName.Contains("groom")
                        ? AnimalSocialMemoryKind.SharedShelter :
                    AnimalSocialMemoryKind.PlayedTogether;
                WildlifeMemoryUtility.RememberAnimal(___pawn, recipient, socialKind,
                    socialKind == AnimalSocialMemoryKind.Fought ? 0.8f : 0.6f);
                return;
            }
            Pawn animal = ___pawn.RaceProps?.Animal == true ? ___pawn :
                recipient.RaceProps?.Animal == true ? recipient : null;
            Pawn colonist = ___pawn.RaceProps?.Humanlike == true && ___pawn.Faction == Faction.OfPlayer ? ___pawn :
                recipient.RaceProps?.Humanlike == true && recipient.Faction == Faction.OfPlayer ? recipient : null;
            if (animal == null || colonist == null) return;
            string name = intDef.defName?.ToLowerInvariant() ?? string.Empty;
            AnimalMemoryKind kind = name.Contains("nuzzle") ? AnimalMemoryKind.Nuzzled :
                name.Contains("insult") || name.Contains("fight") || name.Contains("aggressive") ||
                name.Contains("reject") || name.Contains("scold")
                    ? AnimalMemoryKind.NegativeInteraction : AnimalMemoryKind.PositiveInteraction;
            WildlifeMemoryUtility.Remember(animal, colonist, kind,
                kind == AnimalMemoryKind.Nuzzled ? 1f : 0.55f);
        }
    }

    [HarmonyPatch(typeof(MentalStateHandler), nameof(MentalStateHandler.TryStartMentalState))]
    public static class WildlifeMemoryRevengePatch
    {
        public static bool Prefix(MentalStateHandler __instance, MentalStateDef stateDef, Pawn otherPawn,
            Pawn ___pawn, ref bool __result)
        {
            if (HerdsMod.Settings?.enableAnimalMemory != true || ___pawn?.RaceProps?.Animal != true ||
                otherPawn?.Faction != Faction.OfPlayer ||
                stateDef != MentalStateDefOf.Manhunter && stateDef != MentalStateDefOf.ManhunterPermanent) return true;
            WildlifeMemoryMapComponent memory = ___pawn.MapHeld?.GetComponent<WildlifeMemoryMapComponent>();
            float trust = memory?.TrustFor(___pawn, otherPawn) ?? 0f;
            float hostility = memory?.HostilityFor(___pawn, otherPawn) ?? 0f;
            if (trust <= hostility || !Rand.Chance(Mathf.Clamp01((trust - hostility) * 0.65f))) return true;
            __result = false;
            if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("AnimalMemory",
                "revengeSuppressed trust=" + trust.ToString("0.00") + " hostility=" + hostility.ToString("0.00"),
                ___pawn, otherPawn);
            return false;
        }
    }
}
