using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Herds
{
    public sealed class Window_SpeciesBehaviorProfiles : Window
    {
        private ThingDef selected;
        private string search = string.Empty;
        private Vector2 speciesScroll;

        public override Vector2 InitialSize => new Vector2(920f, 680f);

        public Window_SpeciesBehaviorProfiles()
        {
            doCloseX = true;
            doCloseButton = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
        }

        public override void PostOpen()
        {
            base.PostOpen();
            selected = EligibleSpecies().FirstOrDefault();
        }

        public override void PostClose()
        {
            PreyProfileDatabase.Clear();
            HerdsMod.Instance?.WriteSettings();
            base.PostClose();
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 34f), "Species Behavior Profiles");
            Text.Font = GameFont.Small;
            Rect body = new Rect(inRect.x, inRect.y + 42f, inRect.width, inRect.height - 82f);
            Rect left = new Rect(body.x, body.y, 280f, body.height);
            Rect right = new Rect(left.xMax + 16f, body.y, body.width - left.width - 16f, body.height);
            Widgets.DrawMenuSection(left);
            Widgets.DrawMenuSection(right);
            DrawSpeciesList(left.ContractedBy(10f));
            DrawProfile(right.ContractedBy(14f));
        }

        private void DrawSpeciesList(Rect rect)
        {
            search = Widgets.TextField(new Rect(rect.x, rect.y, rect.width, 30f), search);
            List<ThingDef> species = EligibleSpecies()
                .Where(def => search.NullOrEmpty() || def.LabelCap.ToString().IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();
            Rect outer = new Rect(rect.x, rect.y + 38f, rect.width, rect.height - 38f);
            Rect view = new Rect(0f, 0f, outer.width - 16f, Mathf.Max(outer.height, species.Count * 30f));
            Widgets.BeginScrollView(outer, ref speciesScroll, view);
            for (int i = 0; i < species.Count; i++)
            {
                ThingDef def = species[i];
                Rect row = new Rect(0f, i * 30f, view.width, 28f);
                if (def == selected) Widgets.DrawHighlightSelected(row); else Widgets.DrawHighlightIfMouseover(row);
                Widgets.Label(new Rect(row.x + 7f, row.y + 4f, row.width - 14f, 24f), def.LabelCap);
                if (Widgets.ButtonInvisible(row)) selected = def;
            }
            Widgets.EndScrollView();
        }

        private void DrawProfile(Rect rect)
        {
            if (selected == null)
            {
                Widgets.Label(rect, "No eligible prey species found.");
                return;
            }
            PreyProfile defaults = PreyProfileDatabase.DefaultFor(selected);
            SpeciesBehaviorOverride behaviorOverride = HerdsMod.Settings.speciesOverrides.FirstOrDefault(item => item.defName == selected.defName);
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, 32f), selected.LabelCap);
            Text.Font = GameFont.Small;
            Rect content = new Rect(rect.x, rect.y + 40f, rect.width, rect.height - 40f);
            var listing = new Listing_Standard();
            listing.Begin(content);
            bool enabled = behaviorOverride?.enabled == true;
            bool previous = enabled;
            listing.CheckboxLabeled("Use Species-Specific Behavior", ref enabled, "Overrides inferred or XML behavior for this species.");
            if (enabled != previous)
            {
                if (behaviorOverride == null)
                {
                    behaviorOverride = SpeciesBehaviorOverride.FromProfile(defaults);
                    HerdsMod.Settings.speciesOverrides.Add(behaviorOverride);
                }
                behaviorOverride.enabled = enabled;
                Changed();
            }
            listing.Label("Default: " + defaults.socialType + " / " + defaults.defenseStrategy + " / " + defaults.refugePreference);
            listing.GapLine();
            if (!enabled)
            {
                listing.Label("Enable the override to edit this species.");
                listing.End();
                return;
            }

            if (listing.ButtonTextLabeled("Social Structure", behaviorOverride.socialType.ToString())) ShowEnumMenu<PreySocialType>(value => { behaviorOverride.socialType = value; Changed(); });
            if (listing.ButtonTextLabeled("Defense Strategy", behaviorOverride.defenseStrategy.ToString())) ShowEnumMenu<PreyDefenseStrategy>(value => { behaviorOverride.defenseStrategy = value; Changed(); });
            if (listing.ButtonTextLabeled("Valid Refuge", RefugeLabel(behaviorOverride.refugePreference))) ShowRefugeMenu(value => { behaviorOverride.refugePreference = value; Changed(); });
            listing.Gap();
            if (behaviorOverride.socialType != PreySocialType.Solitary)
            {
                listing.Label("Preferred Group Size: " + behaviorOverride.preferredGroupSize);
                behaviorOverride.preferredGroupSize = Mathf.RoundToInt(listing.Slider(behaviorOverride.preferredGroupSize, 2f, 60f));
            }
            listing.Label("Vigilance: " + behaviorOverride.vigilanceChance.ToStringPercent());
            behaviorOverride.vigilanceChance = listing.Slider(behaviorOverride.vigilanceChance, 0.05f, 0.95f);
            TooltipHandler.TipRegion(listing.GetRect(0f), "Chance-weighted awareness used against stalking predators. Herds gain an additional group lookout bonus.");
            listing.Label("Maximum Hiding Body Size: " + behaviorOverride.maximumHidingBodySize.ToString("0.00"));
            behaviorOverride.maximumHidingBodySize = listing.Slider(behaviorOverride.maximumHidingBodySize, 0.1f, 4f);
            listing.Label("Refuge Search Radius: " + behaviorOverride.refugeSearchRadius.ToString("0") + " cells");
            behaviorOverride.refugeSearchRadius = listing.Slider(behaviorOverride.refugeSearchRadius, 6f, 50f);
            listing.Label("Concealment Success: " + behaviorOverride.hideSuccessChance.ToStringPercent());
            behaviorOverride.hideSuccessChance = listing.Slider(behaviorOverride.hideSuccessChance, 0.05f, 1f);
            TooltipHandler.TipRegion(listing.GetRect(0f), "Rolled once after the prey reaches and finishes entering a valid refuge. Holes are safer than trees; nearby and faster predators reduce the final chance.");
            listing.Gap();
            if (listing.ButtonText("Reset Species"))
            {
                HerdsMod.Settings.speciesOverrides.Remove(behaviorOverride);
                Changed();
            }
            listing.End();
        }

        private static List<ThingDef> EligibleSpecies()
        {
            return DefDatabase<ThingDef>.AllDefsListForReading
                .Where(def => PreyProfileDatabase.DefaultFor(def)?.eligible == true)
                .OrderBy(def => def.LabelCap.ToString())
                .ToList();
        }

        private static void ShowEnumMenu<T>(Action<T> selectedAction) where T : struct
        {
            var options = new List<FloatMenuOption>();
            foreach (T value in Enum.GetValues(typeof(T)))
            {
                T captured = value;
                options.Add(new FloatMenuOption(SplitWords(value.ToString()), () => selectedAction(captured)));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static void ShowRefugeMenu(Action<PreyRefugePreference> selectedAction)
        {
            var options = new List<FloatMenuOption>();
            PreyRefugePreference[] values = { PreyRefugePreference.None, PreyRefugePreference.Trees, PreyRefugePreference.Dens, PreyRefugePreference.TreesAndDens };
            for (int i = 0; i < values.Length; i++)
            {
                PreyRefugePreference captured = values[i];
                options.Add(new FloatMenuOption(RefugeLabel(captured), () => selectedAction(captured)));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static string RefugeLabel(PreyRefugePreference value)
        {
            if (value == PreyRefugePreference.TreesAndDens || value == PreyRefugePreference.Any || value == PreyRefugePreference.TreesAndVegetation) return "Trees And Hide Holes";
            if (value == PreyRefugePreference.Vegetation) return "Hide Holes";
            if (value == PreyRefugePreference.Dens) return "Hide Holes";
            return SplitWords(value.ToString());
        }

        private static string SplitWords(string value)
        {
            return string.Concat(value.Select((character, index) => index > 0 && char.IsUpper(character) ? " " + character : character.ToString()));
        }

        private static void Changed()
        {
            PreyProfileDatabase.Clear();
            if (Current.Game?.Maps == null) return;
            for (int i = 0; i < Current.Game.Maps.Count; i++) Current.Game.Maps[i].GetComponent<HerdMapComponent>()?.ForceRefresh();
        }
    }
}
