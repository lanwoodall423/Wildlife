using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Herds
{
    public sealed class WildlifeMenuEntry
    {
        public string id;
        public string label;
        public string tooltip;
        public int order;
        public Func<bool> visible;
        public Action open;
    }

    public static class WildlifeMenuRegistry
    {
        internal const int OverviewOrder = 0;
        internal const int HorticultureOrder = 10;
        internal const int AquacultureOrder = 20;
        internal const int ExpeditionsOrder = 30;
        private const float ButtonWidth = 180f;
        private const float ButtonHeight = 32f;
        private const float Gap = 8f;
        private static readonly Dictionary<string, WildlifeMenuEntry> Entries =
            new Dictionary<string, WildlifeMenuEntry>(StringComparer.Ordinal);
        private static bool builtInsRegistered;

        public static void Register(string id, string label, string tooltip, int order,
            Func<bool> visible, Action open)
        {
            if (id.NullOrEmpty() || label.NullOrEmpty() || open == null) return;
            Entries[id] = new WildlifeMenuEntry
            {
                id = id,
                label = label,
                tooltip = tooltip ?? string.Empty,
                order = order,
                visible = visible,
                open = open
            };
        }

        public static float RequiredHeight(float width)
        {
            return RequiredHeight(VisibleEntries().Count, width);
        }

        internal static float RequiredHeight(int count, float width) =>
            Mathf.Max(40f, Mathf.CeilToInt(count / (float)ColumnCount(width)) * (ButtonHeight + Gap));

        public static void Draw(Rect rect)
        {
            List<WildlifeMenuEntry> entries = VisibleEntries();
            int columns = ColumnCount(rect.width);
            float width = Mathf.Min(ButtonWidth, (rect.width - Gap * Mathf.Max(0, columns - 1)) / columns);
            for (int i = 0; i < entries.Count; i++)
            {
                int row = i / columns;
                int column = i % columns;
                Rect button = new Rect(rect.x + column * (width + Gap),
                    rect.y + row * (ButtonHeight + Gap), width, ButtonHeight);
                WildlifeMenuEntry entry = entries[i];
                if (Widgets.ButtonText(button, entry.label)) entry.open();
                if (!entry.tooltip.NullOrEmpty()) TooltipHandler.TipRegion(button, entry.tooltip);
            }
        }

        private static int ColumnCount(float width) =>
            Mathf.Max(1, Mathf.FloorToInt((width + Gap) / (ButtonWidth + Gap)));

        private static List<WildlifeMenuEntry> VisibleEntries()
        {
            EnsureBuiltIns();
            return Entries.Values
                .Where(entry => entry.visible == null || entry.visible())
                .OrderBy(entry => entry.order)
                .ThenBy(entry => entry.label, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.id, StringComparer.Ordinal)
                .ToList();
        }

        internal static IReadOnlyList<WildlifeMenuEntry> VisibleEntriesForTesting() => VisibleEntries();

        private static void EnsureBuiltIns()
        {
            if (builtInsRegistered) return;
            builtInsRegistered = true;
            Register("wildlife.overview", "Wildlife Journal",
                "Open the Wildlife Journal Field Log: recent observations, interpretations, and actions.",
                OverviewOrder, null, () => Window_WildlifeJournal.OpenFieldLog(Find.CurrentMap));
            Register("wildlife.expeditions", "Expeditions",
                "Review every active wildlife expedition or send a new party.",
                ExpeditionsOrder,
                () => HerdsMod.Settings?.enableOffMapHuntingExpeditions == true &&
                    WildlifeProgression.Unlocked(WildlifeCapability.HuntingExpedition),
                () => Find.WindowStack.Add(new Window_WildlifeJournal(Find.CurrentMap, WildlifeJournalPage.Expeditions)));
        }
    }
}
