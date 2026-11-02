using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Wildlife
{
    // Only observations witnessed by a colonist are retained. This is not a
    // count of wildlife in the region, a discovery unlock, or a simulation.
    public sealed class WildlifeNativeSightingRecord : IExposable
    {
        public ThingDef species;
        public int firstWitnessedTick;
        public int lastWitnessedTick;
        public int sampledSightings;

        public void ExposeData()
        {
            Scribe_Defs.Look(ref species, "species");
            Scribe_Values.Look(ref firstWitnessedTick, "firstWitnessedTick", 0);
            Scribe_Values.Look(ref lastWitnessedTick, "lastWitnessedTick", 0);
            Scribe_Values.Look(ref sampledSightings, "sampledSightings", 0);
        }
    }

    // Standard vanilla MapComponent discovery. No Harmony hooks, other mod
    // assemblies or legacy Herds/Packs save keys are required.
    public sealed class WildlifeNativeFieldLog : MapComponent
    {
        private const int ScanIntervalTicks = 1800;
        private const int MaxAnimalsPerScan = 256;
        private const int MaxColonistsPerScan = 24;
        private const int MaxRecordedSpecies = 48;
        private const int SightRangeSquared = 625;

        private List<WildlifeNativeSightingRecord> records =
            new List<WildlifeNativeSightingRecord>();
        private int nextScanTick;

        public WildlifeNativeFieldLog(Map map) : base(map) { }

        public IReadOnlyList<WildlifeNativeSightingRecord> Records => records;

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref records, "nativeWildlifeWitnessedSightingsV1", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (records == null)
                    records = new List<WildlifeNativeSightingRecord>();

                HashSet<ThingDef> seen = new HashSet<ThingDef>();
                records.RemoveAll(record =>
                    record == null || record.species == null || !seen.Add(record.species));
                if (records.Count > MaxRecordedSpecies)
                    records.RemoveRange(MaxRecordedSpecies, records.Count - MaxRecordedSpecies);
            }
        }

        public override void MapComponentTick()
        {
            if (WildlifeMod.Settings?.recordWitnessedSightings != true ||
                map?.mapPawns == null || Find.TickManager == null)
                return;

            int now = Find.TickManager.TicksGame;
            if (now < nextScanTick) return;
            nextScanTick = now + ScanIntervalTicks;

            IReadOnlyList<Pawn> colonists = map.mapPawns.FreeColonistsSpawned;
            if (colonists == null || colonists.Count == 0) return;

            HashSet<ThingDef> witnessed = new HashSet<ThingDef>();
            int examined = 0;
            foreach (Pawn animal in map.mapPawns.AllPawnsSpawned)
            {
                if (examined++ >= MaxAnimalsPerScan ||
                    witnessed.Count >= MaxRecordedSpecies)
                    break;

                if (animal?.Spawned != true || animal.Dead ||
                    animal.RaceProps?.Animal != true || animal.Faction != null ||
                    animal.Map != map || animal.def == null ||
                    animal.Position.Fogged(map))
                    continue;

                int limit = Math.Min(colonists.Count, MaxColonistsPerScan);
                for (int i = 0; i < limit; i++)
                {
                    Pawn observer = colonists[i];
                    if (observer?.Spawned != true || observer.Dead ||
                        observer.Downed || !observer.Awake() ||
                        observer.Map != map ||
                        observer.Position.DistanceToSquared(animal.Position) > SightRangeSquared ||
                        !GenSight.LineOfSight(observer.Position, animal.Position, map))
                        continue;

                    witnessed.Add(animal.def);
                    break;
                }
            }

            foreach (ThingDef species in witnessed)
                RecordWitnessedSpecies(species, now);
        }

        private void RecordWitnessedSpecies(ThingDef species, int tick)
        {
            for (int i = 0; i < records.Count; i++)
            {
                WildlifeNativeSightingRecord record = records[i];
                if (record.species != species) continue;
                record.lastWitnessedTick = tick;
                record.sampledSightings = Math.Min(record.sampledSightings + 1, 1000000);
                return;
            }

            if (records.Count >= MaxRecordedSpecies) return;
            records.Add(new WildlifeNativeSightingRecord
            {
                species = species,
                firstWitnessedTick = tick,
                lastWitnessedTick = tick,
                sampledSightings = 1
            });
        }
    }
}
