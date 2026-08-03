using System;
using System.Collections.Generic;
using System.Linq;
using KnowledgeFramework;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Herds
{
    public sealed class ColonistSpeciesKnowledgeRecord : IExposable
    {
        public Pawn colonist;
        public ThingDef species;
        public float experience;
        public int successfulHunts;
        public int failedHunts;
        public void ExposeData()
        {
            Scribe_References.Look(ref colonist, "colonist"); Scribe_Defs.Look(ref species, "species");
            Scribe_Values.Look(ref experience, "experience"); Scribe_Values.Look(ref successfulHunts, "successfulHunts"); Scribe_Values.Look(ref failedHunts, "failedHunts");
        }
    }

    public sealed class ColonistBiomeKnowledgeRecord : IExposable
    {
        public Pawn colonist;
        public BiomeDef biome;
        public float experience;
        public int completedExpeditions;

        public void ExposeData()
        {
            Scribe_References.Look(ref colonist, "colonist");
            Scribe_Defs.Look(ref biome, "biome");
            Scribe_Values.Look(ref experience, "experience");
            Scribe_Values.Look(ref completedExpeditions, "completedExpeditions");
        }
    }

    public sealed class PassiveObservationContextRecord : IExposable
    {
        public string key;
        public string label;

        public void ExposeData()
        {
            Scribe_Values.Look(ref key, "key");
            Scribe_Values.Look(ref label, "label");
        }
    }

    /// <summary>Saved familiarity state. It is deliberately not a WildlifeEvent.</summary>
    public sealed class PassiveObservationRecord : IExposable
    {
        public Pawn observer;
        public ThingDef species;
        public int day = -1;
        public float pendingExposure;
        public float dailyExposure;
        public int lastExposureTick;
        public bool pendingReacquisition;
        public bool usedObservationPost;
        public List<PassiveObservationContextRecord> pendingContexts = new List<PassiveObservationContextRecord>();
        public List<PassiveObservationContextRecord> knownContexts = new List<PassiveObservationContextRecord>();

        public void ExposeData()
        {
            Scribe_References.Look(ref observer, "observer");
            Scribe_Defs.Look(ref species, "species");
            Scribe_Values.Look(ref day, "day", -1);
            Scribe_Values.Look(ref pendingExposure, "pendingExposure", 0f);
            Scribe_Values.Look(ref dailyExposure, "dailyExposure", 0f);
            Scribe_Values.Look(ref lastExposureTick, "lastExposureTick", 0);
            Scribe_Values.Look(ref pendingReacquisition, "pendingReacquisition", false);
            Scribe_Values.Look(ref usedObservationPost, "usedObservationPost", false);
            Scribe_Collections.Look(ref pendingContexts, "pendingContexts", LookMode.Deep);
            Scribe_Collections.Look(ref knownContexts, "knownContexts", LookMode.Deep);
        }
    }

    public sealed class PassiveObservationFamiliarity
    {
        public readonly float dailyFraction;
        public readonly float observedHours;
        public readonly int observerCount;

        public PassiveObservationFamiliarity(float dailyFraction, float observedHours, int observerCount)
        {
            this.dailyFraction = dailyFraction;
            this.observedHours = observedHours;
            this.observerCount = observerCount;
        }
    }

    public sealed class HuntingKnowledgeMapComponent : MapComponent
    {
        private List<ColonistSpeciesKnowledgeRecord> records = new List<ColonistSpeciesKnowledgeRecord>();
        private readonly Dictionary<long, ColonistSpeciesKnowledgeRecord> byColonistSpecies = new Dictionary<long, ColonistSpeciesKnowledgeRecord>();
        private List<ColonistBiomeKnowledgeRecord> biomeRecords = new List<ColonistBiomeKnowledgeRecord>();
        private readonly Dictionary<string, ColonistBiomeKnowledgeRecord> byColonistBiome = new Dictionary<string, ColonistBiomeKnowledgeRecord>();
        private List<PassiveObservationRecord> passiveRecords = new List<PassiveObservationRecord>();
        private readonly Dictionary<long, PassiveObservationRecord> byPassiveObserverSpecies = new Dictionary<long, PassiveObservationRecord>();
        private List<string> passiveColonySpecies = new List<string>();
        private readonly HashSet<string> passiveColonySpeciesSet = new HashSet<string>(StringComparer.Ordinal);
        private int nextOralSharingTick;
        private int cachedAnimalTotal = -1;
        private int cachedBiomeTotal = -1;
        private int nextPassiveFlushTick;
        private const int PassiveDayTicks = 60000;
        private const int PassiveReacquisitionTicks = PassiveDayTicks * 3;
        private const float PassiveDailyCap = 18f;
        private const float PassiveObservationPostDailyCap = 36f;
        private const float PassiveHourPerExposure = 0.6f;
        public HuntingKnowledgeMapComponent(Map map) : base(map) { }

        // These are intentionally read-only views for the one-time V3 migration.
        // The legacy records remain in saves, but are no longer live knowledge state.
        public IReadOnlyList<ColonistSpeciesKnowledgeRecord> LegacySpeciesRecords => records;
        public IReadOnlyList<ColonistBiomeKnowledgeRecord> LegacyBiomeRecords => biomeRecords;

        public override void ExposeData()
        {
            base.ExposeData(); Scribe_Collections.Look(ref records, "colonistSpeciesKnowledge", LookMode.Deep);
            Scribe_Collections.Look(ref biomeRecords, "colonistBiomeKnowledge", LookMode.Deep);
            Scribe_Collections.Look(ref passiveRecords, "passiveObservationRecords", LookMode.Deep);
            Scribe_Collections.Look(ref passiveColonySpecies, "passiveColonySpecies", LookMode.Value);
            Scribe_Values.Look(ref nextOralSharingTick, "nextOralKnowledgeSharingTick", 0);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                records = records?.Where(record => record?.colonist != null && record.species != null).ToList() ?? new List<ColonistSpeciesKnowledgeRecord>();
                byColonistSpecies.Clear(); for (int i = 0; i < records.Count; i++) byColonistSpecies[Key(records[i].colonist, records[i].species)] = records[i];
                biomeRecords = biomeRecords?.Where(record => record?.colonist != null && record.biome != null).ToList() ?? new List<ColonistBiomeKnowledgeRecord>();
                byColonistBiome.Clear(); for (int i = 0; i < biomeRecords.Count; i++) byColonistBiome[BiomeKey(biomeRecords[i].colonist, biomeRecords[i].biome)] = biomeRecords[i];
                passiveRecords = passiveRecords?.Where(record => record?.observer != null && record.species?.race?.Animal == true).ToList() ?? new List<PassiveObservationRecord>();
                byPassiveObserverSpecies.Clear();
                for (int i = 0; i < passiveRecords.Count; i++)
                {
                    PassiveObservationRecord record = passiveRecords[i];
                    record.pendingContexts ??= new List<PassiveObservationContextRecord>();
                    record.knownContexts ??= new List<PassiveObservationContextRecord>();
                    record.pendingContexts = record.pendingContexts.Where(value => value != null && !value.key.NullOrEmpty()).Take(4).ToList();
                    record.knownContexts = record.knownContexts.Where(value => value != null && !value.key.NullOrEmpty()).Take(16).ToList();
                    byPassiveObserverSpecies[Key(record.observer, record.species)] = record;
                }
                passiveColonySpecies = passiveColonySpecies?.Where(value => !value.NullOrEmpty()).Distinct(StringComparer.Ordinal).ToList() ?? new List<string>();
                passiveColonySpeciesSet.Clear(); for (int i = 0; i < passiveColonySpecies.Count; i++) passiveColonySpeciesSet.Add(passiveColonySpecies[i]);
            }
        }

        public override void MapComponentTick()
        {
            if (!HerdsMod.Settings.enableSpeciesKnowledgeProgression) return;
            int now = Find.TickManager?.TicksGame ?? 0;
            if (now < nextPassiveFlushTick) return;
            nextPassiveFlushTick = now + 300;
            FlushPassiveObservations(now);
        }

        public ColonistSpeciesKnowledgeRecord For(Pawn colonist, ThingDef species, bool create = true)
        {
            byColonistSpecies.TryGetValue(Key(colonist, species), out ColonistSpeciesKnowledgeRecord record);
            if (record == null && create) { record = new ColonistSpeciesKnowledgeRecord { colonist = colonist, species = species }; records.Add(record); byColonistSpecies[Key(colonist, species)] = record; }
            return record;
        }

        public int Level(Pawn colonist, ThingDef species)
        {
            return WildlifeKnowledgeAdapter.TierFor(colonist, species);
        }

        public float ColonyExperienceFor(ThingDef species)
        {
            if (species == null) return 0f;
            return records.Where(record => record?.species == species && record.colonist?.Faction == Faction.OfPlayer).Sum(record => record.experience);
        }

        public float LegacyColonyExperienceFor(ThingDef species) => ColonyExperienceFor(species);

        public static float ColonyExperience(ThingDef species)
        {
            return WildlifeKnowledgeAdapter.ColonyKnowledge(species);
        }

        public static int ColonyLevel(ThingDef species)
        {
            return WildlifeKnowledgeAdapter.ColonyTierFor(species);
        }

        public float TacticalBonus(Pawn colonist, ThingDef species) =>
            KnowledgeFramework.KnowledgeRanks.Bonus((KnowledgeFramework.KnowledgeRank)WildlifeKnowledgeAdapter.TierFor(colonist, species), 0.8f);

        public void Learn(Pawn colonist, ThingDef species, float amount, bool success = false, bool failure = false)
        {
            if (!HerdsMod.Settings.enableSpeciesKnowledgeProgression || colonist == null || species == null || amount <= 0f) return;
            WildlifeKnowledgeAdapter.Learn(colonist, species, amount, success, failure);
        }

        public void AccumulatePassiveExposure(Pawn observer, ThingDef species, float exposure, string contextKey,
            string contextLabel, bool observationPost, int now)
        {
            if (!HerdsMod.Settings.enableSpeciesKnowledgeProgression || observer == null || species?.race?.Animal != true || exposure <= 0f) return;
            PassiveObservationRecord record = PassiveFor(observer, species);
            int day = Mathf.Max(0, now / PassiveDayTicks);
            if (record.day < 0) record.day = day;
            if (day < record.day) return;
            if (day > record.day)
            {
                if (!ApplyPassiveRecord(record, now)) return;
                BeginPassiveDay(record, day);
            }
            if (record.lastExposureTick > 0 && now - record.lastExposureTick >= PassiveReacquisitionTicks)
                record.pendingReacquisition = true;
            record.usedObservationPost |= observationPost;
            float cap = record.usedObservationPost ? PassiveObservationPostDailyCap : PassiveDailyCap;
            float remaining = Mathf.Max(0f, cap - record.dailyExposure);
            if (remaining <= 0f) { record.lastExposureTick = now; return; }
            float repetition = Mathf.Clamp01(record.dailyExposure / cap);
            float diminishing = Mathf.Lerp(1f, 0.08f, repetition);
            float accepted = Mathf.Min(remaining, Mathf.Max(0f, exposure) * diminishing);
            if (accepted <= 0.0001f) { record.lastExposureTick = now; return; }
            record.dailyExposure += accepted;
            record.pendingExposure += accepted;
            record.lastExposureTick = now;
            RememberPendingContext(record, contextKey, contextLabel);
        }

        public PassiveObservationFamiliarity FamiliarityFor(ThingDef species, int now)
        {
            if (species == null) return new PassiveObservationFamiliarity(0f, 0f, 0);
            int day = Mathf.Max(0, now / PassiveDayTicks);
            float exposure = 0f;
            float cap = 0f;
            float hours = 0f;
            int observers = 0;
            for (int i = 0; i < passiveRecords.Count; i++)
            {
                PassiveObservationRecord record = passiveRecords[i];
                if (record?.species != species || record.day != day || record.dailyExposure <= 0f) continue;
                float recordCap = record.usedObservationPost ? PassiveObservationPostDailyCap : PassiveDailyCap;
                exposure += record.dailyExposure;
                cap += recordCap;
                hours += record.dailyExposure * PassiveHourPerExposure;
                observers++;
            }
            return new PassiveObservationFamiliarity(cap <= 0f ? 0f : Mathf.Clamp01(exposure / cap), hours, observers);
        }

        public IReadOnlyList<PassiveObservationRecord> PassiveRecords => passiveRecords;

        public bool HasPassiveColonySighting(ThingDef species) => species != null && passiveColonySpeciesSet.Contains(species.defName);

        public List<string> DebugPassiveObservationLines()
        {
            List<string> lines = new List<string>();
            for (int i = 0; i < passiveRecords.Count; i++)
            {
                PassiveObservationRecord record = passiveRecords[i];
                if (record?.observer == null || record.species == null) continue;
                float cap = record.usedObservationPost ? PassiveObservationPostDailyCap : PassiveDailyCap;
                lines.Add("PASSIVE " + record.observer.LabelShortCap + " | " + record.species.LabelCap +
                    " | day=" + record.day + " exposure=" + record.dailyExposure.ToString("0.00") + "/" + cap.ToString("0") +
                    " pending=" + record.pendingExposure.ToString("0.00") + " contexts=" + record.knownContexts.Count +
                    " pendingContexts=" + record.pendingContexts.Count);
            }
            return lines.Count > 0 ? lines : new List<string> { "No passive observation records." };
        }

        private PassiveObservationRecord PassiveFor(Pawn observer, ThingDef species)
        {
            long key = Key(observer, species);
            if (byPassiveObserverSpecies.TryGetValue(key, out PassiveObservationRecord record)) return record;
            record = new PassiveObservationRecord { observer = observer, species = species };
            passiveRecords.Add(record);
            byPassiveObserverSpecies[key] = record;
            return record;
        }

        private void FlushPassiveObservations(int now)
        {
            int day = Mathf.Max(0, now / PassiveDayTicks);
            for (int i = 0; i < passiveRecords.Count; i++)
            {
                PassiveObservationRecord record = passiveRecords[i];
                if (record == null || record.day < 0 || record.day >= day) continue;
                if (record.pendingExposure > 0f && !ApplyPassiveRecord(record, now)) continue;
                BeginPassiveDay(record, day);
            }
        }

        private bool ApplyPassiveRecord(PassiveObservationRecord record, int now)
        {
            string contextKey = null;
            string contextLabel = null;
            for (int i = 0; i < record.pendingContexts.Count; i++)
            {
                PassiveObservationContextRecord context = record.pendingContexts[i];
                if (context == null || record.knownContexts.Any(value => value?.key == context.key)) continue;
                contextKey = context.key;
                contextLabel = context.label;
                break;
            }
            bool firstColony = !WildlifeKnowledgeAdapter.HasPassiveColonySighting(record.species);
            WildlifePassiveObservationResult result = WildlifeKnowledgeAdapter.ApplyPassiveObservation(record.observer,
                record.species, record.pendingExposure, map, record.day, contextKey, contextLabel, firstColony,
                record.pendingReacquisition);
            if (!result.applied) return false;
            if (firstColony)
            {
                passiveColonySpeciesSet.Add(record.species.defName);
                passiveColonySpecies.Add(record.species.defName);
            }
            for (int i = 0; i < record.pendingContexts.Count; i++)
            {
                PassiveObservationContextRecord context = record.pendingContexts[i];
                if (context == null || record.knownContexts.Any(value => value?.key == context.key)) continue;
                record.knownContexts.Add(context);
                if (record.knownContexts.Count >= 16) break;
            }
            record.pendingContexts.Clear();
            record.pendingExposure = 0f;
            record.pendingReacquisition = false;
            return true;
        }

        private static void BeginPassiveDay(PassiveObservationRecord record, int day)
        {
            record.day = day;
            record.dailyExposure = 0f;
            record.usedObservationPost = false;
        }

        private static void RememberPendingContext(PassiveObservationRecord record, string key, string label)
        {
            if (key.NullOrEmpty() || record.pendingContexts.Any(value => value?.key == key) || record.knownContexts.Any(value => value?.key == key)) return;
            if (record.pendingContexts.Count >= 4) return;
            record.pendingContexts.Add(new PassiveObservationContextRecord { key = key, label = label });
        }

        public IEnumerable<ColonistSpeciesKnowledgeRecord> ForColonist(Pawn colonist)
        {
            if (colonist == null) return Enumerable.Empty<ColonistSpeciesKnowledgeRecord>();
            List<ColonistSpeciesKnowledgeRecord> result = DefDatabase<ThingDef>.AllDefsListForReading
                .Where(def => def?.race?.Animal == true)
                .Select(def => new ColonistSpeciesKnowledgeRecord
                {
                    colonist = colonist,
                    species = def,
                    experience = WildlifeKnowledgeAdapter.PersonalKnowledge(colonist, def)
                })
                .Where(record => record.experience > 0f).ToList();
            return result.OrderByDescending(record => record.experience);
        }

        public ColonistBiomeKnowledgeRecord BiomeFor(Pawn colonist, BiomeDef biome, bool create = true)
        {
            if (colonist == null || biome == null) return null;
            byColonistBiome.TryGetValue(BiomeKey(colonist, biome), out ColonistBiomeKnowledgeRecord record);
            if (record == null && create)
            {
                record = new ColonistBiomeKnowledgeRecord { colonist = colonist, biome = biome };
                biomeRecords.Add(record);
                byColonistBiome[BiomeKey(colonist, biome)] = record;
            }
            return record;
        }

        public int BiomeLevel(Pawn colonist, BiomeDef biome)
        {
            int order = WildlifeKnowledgeAdapter.StageOrder(
                KnowledgeQuery.Subject(WildlifeKnowledgeAdapter.DomainId, WildlifeKnowledgeAdapter.BiomeSubjectId(biome), colonist, KnowledgeScope.Personal).stageId);
            return order >= 5 ? 3 : order >= 4 ? 2 : order >= 2 ? 1 : 0;
        }

        public IEnumerable<ColonistBiomeKnowledgeRecord> BiomesForColonist(Pawn colonist)
        {
            if (colonist == null) return Enumerable.Empty<ColonistBiomeKnowledgeRecord>();
            return DefDatabase<BiomeDef>.AllDefsListForReading.Select(biome => new ColonistBiomeKnowledgeRecord
            {
                colonist = colonist,
                biome = biome,
                experience = KnowledgeQuery.Facet(WildlifeKnowledgeAdapter.DomainId,
                    WildlifeKnowledgeAdapter.BiomeSubjectId(biome), WildlifeKnowledgeAdapter.FacetHabitat,
                    colonist, KnowledgeScope.Personal, true, true,
                    WildlifeKnowledgeAdapter.BiomeContext(biome), KnowledgeContextFallbackMode.ParentThenGlobal).amount
            }).Where(record => record.experience > 0f).OrderByDescending(record => record.experience);
        }

        public IEnumerable<ColonistBiomeKnowledgeRecord> ColonyBiomeRecords =>
            KnowledgeQuery.ColonyFacets(WildlifeKnowledgeAdapter.DomainId)
                .Where(value => value.subjectId.StartsWith("biome:", System.StringComparison.Ordinal) && value.amount > 0f)
                .GroupBy(value => value.subjectId).Select(group =>
                {
                    BiomeDef biome = DefDatabase<BiomeDef>.GetNamedSilentFail(group.Key.Substring("biome:".Length));
                    return new ColonistBiomeKnowledgeRecord { biome = biome, experience = group.Max(value => value.amount) };
                }).Where(record => record.biome != null);

        public void LearnBiome(Pawn colonist, BiomeDef biome, float amount, bool completed = false)
        {
            if (!HerdsMod.Settings.enableSpeciesKnowledgeProgression || colonist == null || biome == null || amount <= 0f) return;
            WildlifeKnowledgeAdapter.LearnBiome(colonist, biome, amount, completed);
        }

        public int KnownAnimalCount(Pawn colonist) => ForColonist(colonist).Count();
        public int KnownBiomeCount(Pawn colonist) => BiomesForColonist(colonist).Count();

        public float AnimalCoverage(Pawn colonist) =>
            Mathf.Clamp01(KnownAnimalCount(colonist) / (float)Mathf.Max(1, TotalAnimalCount()));

        public float BiomeCoverage(Pawn colonist) =>
            Mathf.Clamp01(KnownBiomeCount(colonist) / (float)Mathf.Max(1, TotalBiomeCount()));

        public float WildlifeProficiencyCoverage(Pawn colonist) =>
            (AnimalCoverage(colonist) + BiomeCoverage(colonist)) * 0.5f;

        public int WildlifeProficiencyLevel(Pawn colonist)
        {
            float coverage = WildlifeProficiencyCoverage(colonist);
            return coverage >= 0.90f ? 3 : coverage >= 0.65f ? 2 : coverage >= 0.25f ? 1 : 0;
        }

        public static string WildlifeProficiencyLabel(int level) =>
            level <= 0 ? "Novice" : level == 1 ? "Adept" : level == 2 ? "Expert" : "Master";

        public string WildlifeProficiencyTooltip(Pawn colonist)
        {
            int level = WildlifeProficiencyLevel(colonist);
            float animals = AnimalCoverage(colonist);
            float biomes = BiomeCoverage(colonist);
            float combined = WildlifeProficiencyCoverage(colonist);
            string next = level >= 3 ? "Mastery achieved." :
                "Next tier: " + WildlifeProficiencyLabel(level + 1) + " at " +
                (level == 0 ? 25 : level == 1 ? 65 : 90) + "% combined coverage.";
            string description = level <= 0 ? "This person has novice knowledge of wildlife." :
                level == 1 ? "This person has adept knowledge of wildlife." :
                level == 2 ? "This person has expert knowledge of wildlife." :
                "This person has a mastery of knowledge of wildlife.";
            return WildlifeProficiencyLabel(level) + "\n\n" + description +
                "\n\nAnimal coverage: " + animals.ToStringPercent() +
                "\nBiome coverage: " + biomes.ToStringPercent() +
                "\nCombined coverage: " + combined.ToStringPercent() +
                "\n\n" + next +
                "\n\nCurrent effects:" +
                "\n• Effective hunting skill: +" + (level * 0.5f).ToString("0.0") +
                "\n• Wildlife study time: -" + (level * 0.08f).ToStringPercent() +
                "\n• Animal and biome knowledge gain: +" + (level * 0.10f).ToStringPercent() +
                "\n• Expedition travel time: -" + (level * 0.02f).ToStringPercent() +
                "\n• Expedition incident risk: -" + (level * 0.025f).ToStringPercent() +
                "\n• Expedition encounter and success: +" + (level * 0.025f).ToStringPercent() +
                "\n• Animal-call success: +" + (level * 0.04f).ToStringPercent() +
                "\n• Animal-call attraction distance: +" + (level * 3f).ToString("0") + " cells" +
                "\n• Regional survey confidence: +" + (level * 0.02f).ToStringPercent();
        }

        private void NotifyProficiencyChange(Pawn colonist, int oldLevel)
        {
            int newLevel = WildlifeProficiencyLevel(colonist);
            if (newLevel <= oldLevel) return;
            string text = colonist.LabelShortCap + " advanced to " + WildlifeProficiencyLabel(newLevel) + " Wildlife proficiency.";
            Messages.Message(text, colonist, MessageTypeDefOf.PositiveEvent, false);
            WildlifeExperience.Record("Wildlife Proficiency", text, colonist);
            if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("WildlifeProficiency", "level=" + newLevel + " coverage=" + WildlifeProficiencyCoverage(colonist).ToString("0.000"), colonist);
        }

        private int TotalAnimalCount()
        {
            if (cachedAnimalTotal < 0)
                cachedAnimalTotal = DefDatabase<ThingDef>.AllDefsListForReading.Count(def => def?.race?.Animal == true);
            return cachedAnimalTotal;
        }

        private int TotalBiomeCount()
        {
            if (cachedBiomeTotal >= 0) return cachedBiomeTotal;
            HashSet<BiomeDef> biomes = new HashSet<BiomeDef>();
            if (Find.WorldGrid != null)
                for (int i = 0; i < Find.WorldGrid.TilesCount; i++)
                {
                    BiomeDef biome = Find.WorldGrid[(PlanetTile)i]?.PrimaryBiome;
                    if (biome != null) biomes.Add(biome);
                }
            cachedBiomeTotal = biomes.Count > 0 ? biomes.Count : DefDatabase<BiomeDef>.AllDefsListForReading.Count;
            return cachedBiomeTotal;
        }

        public void DebugSet(Pawn colonist, ThingDef species, float experience)
        {
            WildlifeKnowledgeAdapter.DebugSet(colonist, species, experience);
        }

        public int DebugMasterAllSpecies(Pawn colonist)
        {
            if (colonist == null) return 0;
            List<ThingDef> species = DefDatabase<ThingDef>.AllDefsListForReading.Where(def => def?.race?.Animal == true).ToList();
            for (int i = 0; i < species.Count; i++) WildlifeKnowledgeAdapter.DebugSet(colonist, species[i], 1200f);
            if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("DevKnowledgeAll", "species=" + species.Count + " xp=1200", colonist);
            return species.Count;
        }

        public List<string> DebugOverviewLines()
        {
            List<string> lines = new List<string>();
            foreach (ColonistSpeciesKnowledgeRecord record in records.OrderBy(record => record.colonist?.LabelShortCap.ToString()).ThenByDescending(record => record.experience))
                lines.Add((record.colonist?.LabelShortCap.ToString() ?? "missing") + " | " + (record.species?.LabelCap.ToString() ?? "missing") + " | " + LevelLabel(Level(record.colonist, record.species)) + " | xp=" + record.experience.ToString("0.0") + " | successes=" + record.successfulHunts + " failures=" + record.failedHunts + " | effectiveSkill=" + ColonistHuntingUtility.HuntingSkill(record.colonist, record.species).ToString("0.0"));
            return lines.Count > 0 ? lines : new List<string> { "No personal species knowledge recorded." };
        }

        public override void MapComponentOnGUI()
        {
            base.MapComponentOnGUI();
            if (!Prefs.DevMode || !FieldcraftDebug.KnowledgeOverlay || Find.CurrentMap != map) return;
            ThingDef selectedSpecies = (Find.Selector.SingleSelectedThing as Pawn)?.RaceProps?.Animal == true ? Find.Selector.SingleSelectedThing.def : null;
            IReadOnlyList<Pawn> colonists = map.mapPawns.FreeColonistsSpawned;
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn colonist = colonists[i];
                ThingDef species = selectedSpecies;
                if (species == null) species = records.FirstOrDefault(record => record.colonist == colonist)?.species;
                if (species == null) continue;
                int level = Level(colonist, species);
                GenMapUI.DrawThingLabel(colonist, species.LabelCap + ": " + LevelLabel(level) + " | hunt " + ColonistHuntingUtility.HuntingSkill(colonist, species).ToString("0.0"));
            }
        }

        public static string LevelLabel(int level) => ((KnowledgeFramework.KnowledgeRank)Mathf.Clamp(level, 0, 3)).ToString();

        public static int LevelForExperience(float xp) => (int)WildlifeSharedKnowledgeIntegration.RankFor(xp);

        public static string BiomeKnowledgeTooltip(ColonistBiomeKnowledgeRecord record)
        {
            int level = LevelForExperience(record?.experience ?? 0f);
            float travel = level * 0.02f;
            float incidents = level * 0.03f;
            float field = level * 0.02f;
            string tier = level <= 0 ? "No reliable field knowledge yet." :
                level == 1 ? "Adept: identifies common terrain conditions and improves basic route estimates." :
                level == 2 ? "Expert: recognizes wildlife trails, recurring hazards, and seasonal habitat changes." :
                "Master: provides dependable travel, encounter, and safety forecasts.";
            string text = record.biome.LabelCap + " — " + LevelLabel(level) + "\n\n" + tier +
                "\n\nExpedition Effects\n• Travel time: -" + travel.ToStringPercent() +
                "\n• Incident risk: -" + incidents.ToStringPercent() +
                "\n• Encounter and objective chance: +" + field.ToStringPercent() +
                "\n• Completed expeditions: " + record.completedExpeditions +
                "\n\nKnowledge: " + record.experience.ToString("0") + " XP";
            if (level < 3) text += "\nNext tier: " + LevelLabel(level + 1) + " at " + LevelThreshold(level + 1).ToString("0") + " XP.";
            return text;
        }

        public static string ColonyKnowledgeTooltip(ThingDef species, float experience, int level)
        {
            string effects = level <= 0
                ? "The colony cannot yet identify this animal reliably."
                : level == 1
                    ? "Adept: identifies the animal, allows it to appear in Regional Wildlife when present, and reveals basic species, body size, movement, wildness, and trainability statistics."
                    : level == 2
                        ? "Expert: reveals regional population trends with Wildlife Stewardship, plus food, market, combat, and health statistics."
                        : "Master: unlocks species management and ecological forecasts when available, and reveals every statistic and product yield for this animal.";
            string result = species.LabelCap + " — " + LevelLabel(level) + "\n\n" + effects + "\n\nColony knowledge: " + experience.ToString("0") + " XP";
            if (level < 3)
            {
                float next = LevelThreshold(level + 1);
                result += "\nNext tier: " + LevelLabel(level + 1) + " at " + next.ToString("0") + " XP (" + Mathf.Max(0f, next - experience).ToString("0") + " remaining).";
            }
            result += "\n\nGain knowledge through observation, tracks, encounters, tending, and hunts.";
            return result;
        }

        public static string KnowledgeEffectsTooltip(Pawn colonist, ThingDef species, float experience, int level)
        {
            float tacticalBonus = level * 0.8f;
            float effectiveSkill = ColonistHuntingUtility.HuntingSkill(colonist, species);
            int proficiency = colonist.MapHeld?.GetComponent<HuntingKnowledgeMapComponent>()?.WildlifeProficiencyLevel(colonist) ?? 0;
            float callChance = Mathf.Clamp(0.08f + level * 0.17f + proficiency * 0.04f, 0.08f, 0.97f);
            float callDistance = 8f + level * 10f + proficiency * 3f;
            int colonyLevel = ColonyLevel(species);
            float colonyExperience = ColonyExperience(species);
            string text = species.LabelCap + " — " + LevelLabel(level) +
                "\n\nPersonal Effects" +
                "\n• Effective Hunting Skill: " + effectiveSkill.ToString("0.0") + " (+" + tacticalBonus.ToString("0.0") + " species, +" + (proficiency * 0.5f).ToString("0.0") + " proficiency)" +
                "\n• Animal Call Success: " + callChance.ToStringPercent() +
                "\n• Animal Call Attraction Distance: " + callDistance.ToString("0") + " cells";
            if (level < 3)
            {
                float next = LevelThreshold(level + 1);
                text += "\n\nNext Personal Tier" +
                    "\n" + LevelLabel(level + 1) + " at " + next.ToString("0") + " XP (" + Mathf.Max(0f, next - experience).ToString("0") + " remaining)";
            }
            else text += "\n\nPersonal knowledge is mastered.";
            text += "\n\nColony Knowledge" +
                "\n" + LevelLabel(colonyLevel) + " — " + colonyExperience.ToString("0") + " combined XP" +
                "\n" + RevealedStatistics(colonyLevel);
            return text;
        }

        public static float LevelThreshold(int level)
        {
            if (level <= 0) return 0f;
            if (level == 1) return WildlifeSharedKnowledgeIntegration.AdeptThreshold;
            if (level == 2) return WildlifeSharedKnowledgeIntegration.ExpertThreshold;
            return WildlifeSharedKnowledgeIntegration.MasterThreshold;
        }

        public static string RevealedStatistics(int level)
        {
            if (level <= 0) return "Reveals identity and basic species statistics.";
            if (level == 1) return "Also reveals food, social, movement, market, and trainability statistics.";
            if (level == 2) return "Also reveals combat, health, habitat, signals, and regional trends.";
            return "Reveals all species statistics, yields, resistances, and individual field details.";
        }

        private static long Key(Pawn colonist, ThingDef species) => ((long)(colonist?.thingIDNumber ?? 0) << 16) ^ (species?.shortHash ?? 0);
        private static string BiomeKey(Pawn colonist, BiomeDef biome) => (colonist?.thingIDNumber ?? 0) + ":" + (biome?.defName ?? "none");
    }

    public sealed class Window_SpeciesHuntingKnowledge : Window
    {
        private readonly Pawn colonist;
        private Vector2 scroll;
        public override Vector2 InitialSize => new Vector2(580f, 520f);
        public Window_SpeciesHuntingKnowledge(Pawn colonist) { this.colonist = colonist; doCloseX = true; absorbInputAroundWindow = true; }
        public override void DoWindowContents(Rect inRect)
        {
            Widgets.Label(new Rect(0f, 0f, inRect.width, 32f), colonist.LabelShortCap + " — Animal Knowledge");
            List<ColonistSpeciesKnowledgeRecord> records = colonist.Map.GetComponent<HuntingKnowledgeMapComponent>().ForColonist(colonist).ToList();
            Rect outer = new Rect(0f, 40f, inRect.width, inRect.height - 40f); Rect view = new Rect(0f, 0f, outer.width - 16f, Mathf.Max(outer.height, records.Count * 44f));
            Widgets.BeginScrollView(outer, ref scroll, view);
            for (int i = 0; i < records.Count; i++)
            {
                ColonistSpeciesKnowledgeRecord record = records[i]; int level = colonist.Map.GetComponent<HuntingKnowledgeMapComponent>().Level(colonist, record.species);
                Rect row = new Rect(0f, i * 44f, view.width, 40f); Widgets.DrawHighlightIfMouseover(row);
                Widgets.Label(new Rect(8f, row.y + 3f, row.width * 0.46f, 24f), record.species.LabelCap);
                Widgets.Label(new Rect(row.width * 0.47f, row.y + 3f, row.width * 0.3f, 24f), HuntingKnowledgeMapComponent.LevelLabel(level));
                Widgets.Label(new Rect(row.width * 0.76f, row.y + 3f, row.width * 0.23f, 24f), record.experience.ToString("0") + " XP");
                Widgets.FillableBar(new Rect(8f, row.y + 28f, row.width - 16f, 7f), Mathf.Clamp01(record.experience / 1200f));
                TooltipHandler.TipRegion(row, HuntingKnowledgeMapComponent.KnowledgeEffectsTooltip(colonist, record.species, record.experience, level));
            }
            Widgets.EndScrollView();
            if (records.Count == 0) Widgets.Label(new Rect(8f, 52f, inRect.width - 16f, 50f), "No Animal Knowledge yet. Track, observe, wound, or successfully hunt wildlife to learn it.");
        }
    }

    public static class WildlifeKnowledgeMenuAdapter
    {
        public static KnowledgeFramework.KnowledgeRank ExpertiseFor(Pawn pawn) => WildlifeKnowledgeAdapter.ExpertiseFor(pawn);

        public static KnowledgeFramework.KnowledgeMenuModel ModelFor(Pawn pawn, bool colony) =>
            WildlifeKnowledgeAdapter.MenuModelFor(pawn, colony);

        private static float ExpertiseProgressFor(Pawn pawn) => WildlifeKnowledgeAdapter.ExpertiseProgressFor(pawn);
    }

    public sealed class Window_ColonistWildlifeKnowledge : Window
    {
        private readonly KnowledgeFramework.KnowledgeMenuState state = new KnowledgeFramework.KnowledgeMenuState();
        public override Vector2 InitialSize => new Vector2(1040f, 680f);

        public Window_ColonistWildlifeKnowledge(Pawn colonist)
        {
            state.scope = KnowledgeFramework.KnowledgeMenuScope.Colonist;
            state.selectedPawn = colonist;
            doCloseX = true;
            absorbInputAroundWindow = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            KnowledgeFramework.KnowledgeMenuUI.Draw(inRect, state, WildlifeKnowledgeMenuAdapter.ModelFor,
                WildlifeKnowledgeMenuAdapter.ExpertiseFor);
        }
    }

    public sealed class Window_ColonyWildlifeKnowledge : Window
    {
        private readonly KnowledgeFramework.KnowledgeMenuState state = new KnowledgeFramework.KnowledgeMenuState
        {
            scope = KnowledgeFramework.KnowledgeMenuScope.Colony
        };
        public override Vector2 InitialSize => new Vector2(1040f, 680f);

        public Window_ColonyWildlifeKnowledge()
        {
            doCloseX = true;
            absorbInputAroundWindow = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            KnowledgeFramework.KnowledgeMenuUI.Draw(inRect, state, WildlifeKnowledgeMenuAdapter.ModelFor,
                WildlifeKnowledgeMenuAdapter.ExpertiseFor);
        }
    }
}
