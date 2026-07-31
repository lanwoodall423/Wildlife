using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using KnowledgeFramework;
using RimWorld;
using UnityEngine;
using Verse;

namespace Herds
{
    public static class WildlifeSharedKnowledgeIntegration
    {
        public const float AdeptThreshold = 120f;
        public const float ExpertThreshold = 300f;
        public const float MasterThreshold = 650f;

        public static void Register()
        {
            KnowledgeProviderRegistry.Register("wildlife", 10, EntryFor);
        }

        public static KnowledgeRank RankFor(float experience) =>
            KnowledgeRanks.ForExperience(experience, AdeptThreshold, ExpertThreshold, MasterThreshold);

        public static float ProgressFor(float experience) =>
            KnowledgeRanks.Progress(experience, AdeptThreshold, ExpertThreshold, MasterThreshold);

        public static int TierFor(Pawn pawn, ThingDef species)
        {
            HuntingKnowledgeMapComponent component = pawn?.MapHeld?.GetComponent<HuntingKnowledgeMapComponent>();
            float experience = component?.For(pawn, species, false)?.experience ?? 0f;
            return (int)RankFor(experience);
        }

        private static KnowledgeEntry EntryFor(Pawn pawn)
        {
            HuntingKnowledgeMapComponent component = pawn?.MapHeld?.GetComponent<HuntingKnowledgeMapComponent>();
            if (component == null) return null;
            List<ColonistSpeciesKnowledgeRecord> species = component.ForColonist(pawn).ToList();
            List<ColonistBiomeKnowledgeRecord> biomes = component.BiomesForColonist(pawn).ToList();
            float experience = species.Sum(record => record.experience) + biomes.Sum(record => record.experience);
            float coverageExperience = Mathf.Max(experience, component.WildlifeProficiencyCoverage(pawn) * MasterThreshold);
            KnowledgeRank rank = RankFor(coverageExperience);
            string best = species.OrderByDescending(record => record.experience).FirstOrDefault()?.species?.LabelCap.ToString();
            string summary = component.KnownAnimalCount(pawn) + " animals / " + component.KnownBiomeCount(pawn) + " biomes";
            if (!best.NullOrEmpty()) summary += " / " + best;
            return new KnowledgeEntry
            {
                label = "Wildlife",
                rank = rank,
                progress = ProgressFor(coverageExperience),
                summary = summary,
                tooltip = "Wildlife - " + rank + "\n\n" +
                    "Knowledge grows through observation, tracks, hunts, handling, animal treatment, and expeditions." +
                    "\n\nCurrent effects:" +
                    "\n- Hunting effectiveness: +" + ((int)rank * 0.8f).ToString("0.0") +
                    "\n- Taming and training chance: +" + ((int)rank * 0.04f).ToStringPercent() +
                    "\n- Animal tending quality: +" + ((int)rank * 0.03f).ToStringPercent() +
                    "\n\nKnown animals: " + component.KnownAnimalCount(pawn) +
                    "\nKnown biomes: " + component.KnownBiomeCount(pawn) +
                    "\nTotal domain XP: " + experience.ToString("0"),
                openDetails = () => Find.WindowStack.Add(new Window_ColonistWildlifeKnowledge(pawn))
            };
        }
    }

    [HarmonyPatch(typeof(StatWorker), nameof(StatWorker.GetValueUnfinalized))]
    public static class WildlifeKnowledgeStatPatch
    {
        internal static bool IsPlayerPawn(Pawn pawn) =>
            pawn?.Faction?.def?.isPlayer == true;

        public static void Postfix(StatWorker __instance, StatRequest req, ref float __result)
        {
            if (req.Thing is not Pawn pawn || !IsPlayerPawn(pawn)) return;
            if (__instance == StatDefOf.TameAnimalChance.Worker || __instance == StatDefOf.TrainAnimalChance.Worker)
            {
                ThingDef species = WildlifeKnowledgeContext.Species;
                if (species != null) __result += WildlifeSharedKnowledgeIntegration.TierFor(pawn, species) * 0.04f;
            }
            else if (__instance == StatDefOf.MedicalTendQuality.Worker && WildlifeKnowledgeContext.Species != null)
            {
                __result += WildlifeSharedKnowledgeIntegration.TierFor(pawn, WildlifeKnowledgeContext.Species) * 0.03f;
            }
        }
    }

    public static class WildlifeKnowledgeContext
    {
        [ThreadStatic] public static ThingDef Species;
    }

    [HarmonyPatch(typeof(Pawn_InteractionsTracker), nameof(Pawn_InteractionsTracker.TryInteractWith))]
    public static class WildlifeHandlingContextPatch
    {
        public static void Prefix(Pawn recipient, InteractionDef intDef)
        {
            if (recipient?.RaceProps?.Animal == true && (intDef == InteractionDefOf.TameAttempt || intDef == InteractionDefOf.TrainAttempt))
                WildlifeKnowledgeContext.Species = recipient.def;
        }

        public static void Postfix(Pawn_InteractionsTracker __instance, Pawn recipient, InteractionDef intDef)
        {
            Pawn handler = AccessTools.Field(typeof(Pawn_InteractionsTracker), "pawn")?.GetValue(__instance) as Pawn;
            if (handler?.Map != null && recipient?.RaceProps?.Animal == true &&
                (intDef == InteractionDefOf.TameAttempt || intDef == InteractionDefOf.TrainAttempt))
                handler.Map.GetComponent<HuntingKnowledgeMapComponent>()?.Learn(handler, recipient.def, intDef == InteractionDefOf.TameAttempt ? 10f : 7f);
            WildlifeKnowledgeContext.Species = null;
        }

        public static Exception Finalizer(Exception __exception)
        {
            WildlifeKnowledgeContext.Species = null;
            return __exception;
        }
    }

    [HarmonyPatch(typeof(TendUtility), nameof(TendUtility.CalculateBaseTendQuality), new[] { typeof(Pawn), typeof(Pawn), typeof(float), typeof(float) })]
    public static class WildlifeAnimalTendKnowledgePatch
    {
        public static void Prefix(Pawn patient)
        {
            if (patient?.RaceProps?.Animal == true) WildlifeKnowledgeContext.Species = patient.def;
        }

        public static void Postfix(Pawn doctor, Pawn patient)
        {
            if (doctor?.Map != null && patient?.RaceProps?.Animal == true)
                doctor.Map.GetComponent<HuntingKnowledgeMapComponent>()?.Learn(doctor, patient.def, 8f);
            WildlifeKnowledgeContext.Species = null;
        }

        public static Exception Finalizer(Exception __exception)
        {
            WildlifeKnowledgeContext.Species = null;
            return __exception;
        }
    }
}
