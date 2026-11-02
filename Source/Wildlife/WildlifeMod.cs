using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Wildlife
{
    // WILD-DEP-01A: native, read-only observation surface. Core gameplay and
    // companion-specific presentations remain separate migration work.
    public sealed class WildlifeMod : Mod
    {
        public WildlifeMod(ModContentPack content) : base(content) { }

        public override string SettingsCategory() => "Wildlife (Native Field View)";

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(inRect);
            listing.Label("Wildlife field observations");
            listing.Label("Unfogged, spawned, unowned wild animals only; not an estimated population.");

            Map map = Find.CurrentMap;
            if (map == null || map.mapPawns == null)
            {
                listing.Label("Open a map to observe local wildlife.");
                listing.End();
                return;
            }

            // Never turn hidden, unspawned, dead, or faction-owned Pawns into
            // observations. This view neither persists nor changes gameplay data.
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

            if (counts.Count == 0)
            {
                listing.Label("No unfogged wild animals are currently present.");
                listing.End();
                return;
            }

            List<KeyValuePair<ThingDef, int>> species =
                new List<KeyValuePair<ThingDef, int>>(counts);
            species.Sort((left, right) =>
                string.Compare(left.Key.label, right.Key.label, StringComparison.OrdinalIgnoreCase));

            listing.Label("Species observed on this map: " + species.Count);
            int displayed = Math.Min(species.Count, 8);
            for (int i = 0; i < displayed; i++)
                listing.Label(species[i].Key.label + ": " + species[i].Value);

            if (species.Count > displayed)
                listing.Label("Additional observed species: " + (species.Count - displayed));

            listing.End();
        }
    }
}
