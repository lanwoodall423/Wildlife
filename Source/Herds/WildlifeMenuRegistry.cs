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

        public static float RequiredHeight()
        {
            int count = VisibleEntries().Count;
            int columns = ColumnCount(Mathf.Max(360f, UI.screenWidth - 36f));
            return Mathf.Max(40f, Mathf.CeilToInt(count / (float)columns) * (ButtonHeight + Gap));
        }

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

        private static void EnsureBuiltIns()
        {
            if (builtInsRegistered) return;
            builtInsRegistered = true;
            Register("wildlife.overview", "Wildlife Overview",
                "Open the colony's wildlife status, recent outcomes, progression, and next recommended actions.",
                0, null, () => Find.WindowStack.Add(new Window_WildlifeOverview(Find.CurrentMap)));
            Register("wildlife.expeditions", "Wildlife Expeditions",
                "Review every active wildlife expedition or send a new party.",
                10,
                () => HerdsMod.Settings?.enableOffMapHuntingExpeditions == true &&
                    WildlifeProgression.Unlocked(WildlifeCapability.HuntingExpedition),
                () => Find.WindowStack.Add(new Window_WildlifeExpeditions(Find.CurrentMap)));
        }
    }
}
