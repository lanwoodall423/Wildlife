using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Herds
{
    [StaticConstructorOnStartup]
    public static class ProgressionEducationKnowledgeCompatibility
    {
        private const string PackageId = "ferny.ProgressionEducation";
        private static readonly Texture2D WildlifeIcon = ContentFinder<Texture2D>.Get("UI/WildlifeKnowledge");
        private static readonly Texture2D[] ProficiencyIcons =
        {
            ContentFinder<Texture2D>.Get("UI/WildlifeNovice"),
            ContentFinder<Texture2D>.Get("UI/WildlifeAdept"),
            ContentFinder<Texture2D>.Get("UI/WildlifeExpert"),
            ContentFinder<Texture2D>.Get("UI/WildlifeMaster")
        };
        private static bool initialized;

        public static bool Active => ModsConfig.IsActive(PackageId);

        public static void Initialize()
        {
            if (!Active || initialized) return;
            Type patchType = AccessTools.TypeByName("ProgressionEducation.CharacterCardUtility_DoLeftSection_Patch");
            MethodInfo addSection = AccessTools.Method(patchType, "AddProficienciesSection");
            if (addSection == null)
            {
                Log.Warning("[Wildlife] Progression: Education is active, but its Knowledge panel hook was not found.");
                return;
            }
            HerdsMod.Harmony.Patch(addSection,
                postfix: new HarmonyMethod(typeof(ProgressionEducationKnowledgeCompatibility), nameof(AfterKnowledgeSectionAdded)));
            initialized = true;
        }

        public static void AfterKnowledgeSectionAdded(object listObj, Pawn pawn)
        {
            if (pawn?.Faction != Faction.OfPlayer || pawn.RaceProps?.Humanlike != true || listObj is not IList list || list.Count == 0) return;
            object section = list[list.Count - 1];
            Type sectionType = section.GetType();
            FieldInfo rectField = AccessTools.Field(sectionType, "rect");
            FieldInfo drawerField = AccessTools.Field(sectionType, "drawer");
            if (rectField == null || drawerField == null || drawerField.GetValue(section) is not Action<Rect> original) return;
            Rect sectionRect = (Rect)rectField.GetValue(section);
            sectionRect.height += 24f;
            rectField.SetValue(section, sectionRect);
            drawerField.SetValue(section, (Action<Rect>)(rect =>
            {
                original(rect);
                DrawCompactWildlifeRow(new Rect(rect.x, rect.yMax - 24f, rect.width, 22f), pawn);
            }));
            // CharacterCardUtility.LeftRectSection is a value type. IList returns a boxed
            // copy, so the changed rectangle and drawer must be assigned back.
            list[list.Count - 1] = section;
        }

        private static void DrawCompactWildlifeRow(Rect rect, Pawn pawn)
        {
            Rect clickable = new Rect(rect.x, rect.y, Mathf.Min(130f, rect.width), rect.height);
            Widgets.DrawHighlightIfMouseover(clickable);
            HuntingKnowledgeMapComponent knowledge = pawn.MapHeld?.GetComponent<HuntingKnowledgeMapComponent>();
            int level = knowledge?.WildlifeProficiencyLevel(pawn) ?? 0;
            GUI.DrawTexture(new Rect(rect.x + 4f, rect.y + 2f, 18f, 18f), WildlifeIcon);
            Widgets.Label(new Rect(rect.x + 26f, rect.y, rect.width - 32f, rect.height), "Wildlife");
            TooltipHandler.TipRegion(clickable, knowledge?.WildlifeProficiencyTooltip(pawn) ?? ProficiencyDescription(level));
            float x = rect.x + 130f;
            for (int i = 0; i < ProficiencyIcons.Length; i++)
            {
                Rect tierRect = new Rect(x + i * 22f, rect.y + 2f, 18f, 18f);
                GUI.color = i <= level ? Color.white : new Color(0.22f, 0.22f, 0.22f, 0.70f);
                GUI.DrawTexture(tierRect, ProficiencyIcons[i]);
                GUI.color = Color.white;
                TooltipHandler.TipRegion(tierRect, ProficiencyDescription(i));
            }
            if (Widgets.ButtonInvisible(clickable)) Find.WindowStack.Add(new Window_ColonistWildlifeKnowledge(pawn));
        }

        private static string ProficiencyDescription(int level)
        {
            string description = level <= 0 ? "Novice\n\nThis person has novice knowledge of wildlife." :
                level == 1 ? "Adept\n\nThis person has adept knowledge of wildlife." :
                level == 2 ? "Expert\n\nThis person has expert knowledge of wildlife." :
                "Master\n\nThis person has a mastery of knowledge of wildlife.";
            return description + "\n\nBonuses at this tier:" +
                "\n• Hunting skill +" + (level * 0.5f).ToString("0.0") +
                "\n• Wildlife study time -" + (level * 0.08f).ToStringPercent() +
                "\n• Knowledge gain +" + (level * 0.10f).ToStringPercent() +
                "\n• Expedition travel time -" + (level * 0.02f).ToStringPercent() +
                "\n• Expedition incident risk -" + (level * 0.025f).ToStringPercent() +
                "\n• Expedition encounter and success +" + (level * 0.025f).ToStringPercent() +
                "\n• Animal-call success +" + (level * 0.04f).ToStringPercent() +
                "\n• Animal-call distance +" + (level * 3f).ToString("0") + " cells" +
                "\n• Regional survey confidence +" + (level * 0.02f).ToStringPercent();
        }

        private static void DrawWildlifeKnowledgeRow(Rect rect, Pawn pawn)
        {
            Widgets.DrawHighlightIfMouseover(rect);
            Widgets.Label(new Rect(rect.x + 6f, rect.y, rect.width - 12f, rect.height), "Wildlife Knowledge");
            HuntingKnowledgeMapComponent knowledge = pawn.MapHeld?.GetComponent<HuntingKnowledgeMapComponent>();
            int animals = knowledge?.KnownAnimalCount(pawn) ?? 0;
            int biomes = knowledge?.KnownBiomeCount(pawn) ?? 0;
            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = new Color(0.70f, 0.82f, 0.68f);
            Widgets.Label(new Rect(rect.x + 130f, rect.y, rect.width - 138f, rect.height), animals + " animals  •  " + biomes + " biomes");
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            TooltipHandler.TipRegion(rect, "Wildlife Knowledge\n\nReview this colonist's Animal Knowledge and Biome Knowledge. Experience improves identification, field decisions, route timing, encounter chances, and expedition safety.");
            if (Widgets.ButtonInvisible(rect)) Find.WindowStack.Add(new Window_ColonistWildlifeKnowledge(pawn));
        }
    }

    [HarmonyPatch(typeof(CharacterCardUtility), "PawnCardSize")]
    public static class WildlifeKnowledgePawnCardSizePatch
    {
        public static void Postfix(Pawn pawn, ref Vector2 __result)
        {
            if (!ProgressionEducationKnowledgeCompatibility.Active ||
                pawn?.Faction != Faction.OfPlayer || pawn.RaceProps?.Humanlike != true) return;
            __result.y += 24f;
        }
    }

    [HarmonyPatch(typeof(Pawn_FlightTracker), nameof(Pawn_FlightTracker.Notify_JobStarted))]
    public static class FlightTrackerJobSafetyPatch
    {
        private static bool warned;
        public static bool Prepare() => ModsConfig.IsActive("lan.codex.flockmasterpsycasts");

        public static Exception Finalizer(Exception __exception, Job job)
        {
            if (__exception is not NullReferenceException) return __exception;
            if (!warned)
            {
                warned = true;
                Log.Warning("[Wildlife] Recovered an invalid bird flight-tracker state while starting " + (job?.def?.defName ?? "an unknown job") + ".");
            }
            return null;
        }
    }
}
