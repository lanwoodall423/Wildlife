using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Wildlife
{
    // A vanilla-only, read-only view of confirmed historical field sightings.
    // It never queries unseen Pawns or turns samples into population estimates.
    public sealed class WildlifeNativeFieldJournalWindow : Window
    {
        private readonly Map map;
        private Vector2 scrollPosition;
        private string speciesFilter = string.Empty;
        private bool newestFirst = true;

        public override Vector2 InitialSize => new Vector2(720f, 540f);

        public WildlifeNativeFieldJournalWindow(Map map)
        {
            this.map = map;
            doCloseX = true;
            absorbInputAroundWindow = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 32f),
                "Wildlife Field Journal");
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(0f, 36f, inRect.width, 40f),
                "Confirmed sightings witnessed by colonists on this map. " +
                "Sampled checks are not individual animals or population totals.");

            Widgets.Label(new Rect(0f, 88f, 72f, 28f), "Species:");
            speciesFilter = Widgets.TextField(
                new Rect(74f, 84f, Mathf.Max(80f, inRect.width - 279f), 30f),
                speciesFilter ?? string.Empty);
            if (Widgets.ButtonText(new Rect(inRect.width - 196f, 84f, 196f, 30f),
                newestFirst ? "Order: most recent" : "Order: species name"))
            {
                newestFirst = !newestFirst;
                scrollPosition = Vector2.zero;
            }

            if (map == null || map.mapPawns == null)
            {
                Widgets.Label(new Rect(0f, 130f, inRect.width, 35f),
                    "The original map is no longer available.");
                return;
            }

            WildlifeNativeFieldLog log = map.GetComponent<WildlifeNativeFieldLog>();
            if (log?.Records == null || log.Records.Count == 0)
            {
                Widgets.Label(new Rect(0f, 130f, inRect.width, 35f),
                    "No colonist-witnessed sightings have been recorded on this map.");
                return;
            }

            // All records are already bounded by the MapComponent. Filter only
            // persisted, directly witnessed data and never fetch live animals.
            List<WildlifeNativeSightingRecord> entries =
                new List<WildlifeNativeSightingRecord>();
            foreach (WildlifeNativeSightingRecord record in log.Records)
            {
                if (record?.species == null) continue;
                string label = record.species.label ?? record.species.defName;
                if (!string.IsNullOrEmpty(speciesFilter) &&
                    label.IndexOf(speciesFilter, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                entries.Add(record);
                if (entries.Count >= 48) break;
            }

            entries.Sort((left, right) =>
            {
                if (newestFirst)
                {
                    int recent = right.lastWitnessedTick.CompareTo(left.lastWitnessedTick);
                    if (recent != 0) return recent;
                }
                return string.Compare(left.species.label, right.species.label,
                    StringComparison.OrdinalIgnoreCase);
            });

            if (entries.Count == 0)
            {
                Widgets.Label(new Rect(0f, 130f, inRect.width, 35f),
                    "No recorded species match this search.");
                return;
            }

            Widgets.Label(new Rect(0f, 121f, inRect.width, 25f),
                entries.Count + " recorded species displayed");
            Rect outer = new Rect(0f, 152f, inRect.width,
                Mathf.Max(80f, inRect.height - 156f));
            const float rowHeight = 59f;
            Rect view = new Rect(0f, 0f, outer.width - 18f,
                Mathf.Max(outer.height, entries.Count * rowHeight));
            Widgets.BeginScrollView(outer, ref scrollPosition, view);

            for (int i = 0; i < entries.Count; i++)
            {
                WildlifeNativeSightingRecord entry = entries[i];
                Rect row = new Rect(0f, i * rowHeight, view.width, rowHeight - 3f);
                Widgets.DrawHighlightIfMouseover(row);
                Widgets.Label(new Rect(8f, row.y + 3f, row.width - 16f, 24f),
                    entry.species.label ?? entry.species.defName);
                Widgets.Label(new Rect(8f, row.y + 28f, row.width - 16f, 24f),
                    "First witnessed tick " + entry.firstWitnessedTick +
                    "    Last witnessed tick " + entry.lastWitnessedTick +
                    "    Sampled checks " + entry.sampledSightings);
            }

            Widgets.EndScrollView();
        }
    }
}
