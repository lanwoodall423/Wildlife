using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Wildlife
{
    public sealed class WildlifeNativeSettings : ModSettings
    {
        public bool recordWitnessedSightings = true;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref recordWitnessedSightings,
                "nativeWildlifeRecordWitnessedV1", true);
        }
    }

    // Vanilla-only field views. The deeper Herds/Packs simulation and companion
    // presentations remain separate, unvalidated migration work.
    public sealed class WildlifeMod : Mod
    {
        public static WildlifeNativeSettings Settings;

        public WildlifeMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<WildlifeNativeSettings>();
        }

        public override string SettingsCategory() => "Wildlife (Native Field View)";

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(inRect);
            listing.Label("Wildlife field observations");
            listing.CheckboxLabeled("Record colonist-witnessed wild animal sightings",
                ref Settings.recordWitnessedSightings);

            Map map = Find.CurrentMap;
            if (map == null || map.mapPawns == null)
            {
                listing.Label("Open a map to inspect local wildlife and its field log.");
                listing.End();
                return;
            }

            // Unfogged animals are a LIVE snapshot, not necessarily witnessed by
            // colonists. Keep that distinct from the persisted field log below.
            Dictionary<ThingDef, int> counts = new Dictionary<ThingDef, int>();
            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (pawn == null || !pawn.Spawned || pawn.Dead ||
                    pawn.RaceProps?.Animal != true || pawn.Faction != null ||
                    pawn.Position.Fogged(map))
                    continue;

                int count;
                counts.TryGetValue(pawn.def, out count);
                counts[pawn.def] = count + 1;
            }

            listing.Label("Unfogged wild animals currently on this map:");
            if (counts.Count == 0)
            {
                listing.Label("None in unfogged cells.");
            }
            else
            {
                List<KeyValuePair<ThingDef, int>> species =
                    new List<KeyValuePair<ThingDef, int>>(counts);
                species.Sort((left, right) =>
                    string.Compare(left.Key.label, right.Key.label,
                        StringComparison.OrdinalIgnoreCase));

                int displayed = Math.Min(species.Count, 4);
                for (int i = 0; i < displayed; i++)
                    listing.Label(species[i].Key.label + ": " + species[i].Value);

                if (species.Count > displayed)
                    listing.Label((species.Count - displayed) + " other unfogged species.");
            }

            listing.Label("Colonist-witnessed field log (sampled, not population):");
            WildlifeNativeFieldLog log = map.GetComponent<WildlifeNativeFieldLog>();
            if (log == null || log.Records == null || log.Records.Count == 0)
            {
                listing.Label("No direct sightings have been recorded on this map.");
            }
            else
            {
                List<WildlifeNativeSightingRecord> history =
                    new List<WildlifeNativeSightingRecord>(log.Records);
                history.Sort((left, right) =>
                    right.lastWitnessedTick.CompareTo(left.lastWitnessedTick));

                int displayed = Math.Min(history.Count, 3);
                for (int i = 0; i < displayed; i++)
                {
                    WildlifeNativeSightingRecord entry = history[i];
                    listing.Label(entry.species.label + ": " + entry.sampledSightings +
                        " witnessed checks, last at game tick " + entry.lastWitnessedTick);
                }

                if (history.Count > displayed)
                    listing.Label((history.Count - displayed) + " older species records.");
            }

            if (!Settings.recordWitnessedSightings)
                listing.Label("New sighting collection is paused; saved records remain.");

            listing.End();
        }
    }
}
