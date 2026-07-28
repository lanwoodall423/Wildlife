using System.Collections.Generic;
using System.Linq;
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

    public sealed class HuntingKnowledgeMapComponent : MapComponent
    {
        private List<ColonistSpeciesKnowledgeRecord> records = new List<ColonistSpeciesKnowledgeRecord>();
        private readonly Dictionary<long, ColonistSpeciesKnowledgeRecord> byColonistSpecies = new Dictionary<long, ColonistSpeciesKnowledgeRecord>();
        private List<ColonistBiomeKnowledgeRecord> biomeRecords = new List<ColonistBiomeKnowledgeRecord>();
        private readonly Dictionary<string, ColonistBiomeKnowledgeRecord> byColonistBiome = new Dictionary<string, ColonistBiomeKnowledgeRecord>();
        private int nextOralSharingTick;
        private int cachedAnimalTotal = -1;
        private int cachedBiomeTotal = -1;
        public HuntingKnowledgeMapComponent(Map map) : base(map) { }

        public override void ExposeData()
        {
            base.ExposeData(); Scribe_Collections.Look(ref records, "colonistSpeciesKnowledge", LookMode.Deep);
            Scribe_Collections.Look(ref biomeRecords, "colonistBiomeKnowledge", LookMode.Deep);
            Scribe_Values.Look(ref nextOralSharingTick, "nextOralKnowledgeSharingTick", 0);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                records = records?.Where(record => record?.colonist != null && record.species != null).ToList() ?? new List<ColonistSpeciesKnowledgeRecord>();
                byColonistSpecies.Clear(); for (int i = 0; i < records.Count; i++) byColonistSpecies[Key(records[i].colonist, records[i].species)] = records[i];
                biomeRecords = biomeRecords?.Where(record => record?.colonist != null && record.biome != null).ToList() ?? new List<ColonistBiomeKnowledgeRecord>();
                byColonistBiome.Clear(); for (int i = 0; i < biomeRecords.Count; i++) byColonistBiome[BiomeKey(biomeRecords[i].colonist, biomeRecords[i].biome)] = biomeRecords[i];
            }
        }

        public override void MapComponentTick()
        {
            if (!HerdsMod.Settings.enableSpeciesKnowledgeProgression || !WildlifeProgression.Unlocked(WildlifeCapability.OralKnowledge)) return;
            int now = Find.TickManager.TicksGame;
            if (now < nextOralSharingTick) return;
            nextOralSharingTick = now + 60000;
            ShareOralKnowledge();
        }

        private void ShareOralKnowledge()
        {
            List<Pawn> colonists = map.mapPawns.FreeColonists.Where(pawn => pawn != null && !pawn.Dead).ToList();
            if (colonists.Count < 2 || records.Count == 0) return;
            List<IGrouping<ThingDef, ColonistSpeciesKnowledgeRecord>> speciesGroups = records
                .Where(record => record?.species != null && record.colonist?.Faction == Faction.OfPlayer)
                .GroupBy(record => record.species).ToList();
            int shared = 0;
            for (int i = 0; i < speciesGroups.Count; i++)
            {
                ColonistSpeciesKnowledgeRecord teacher = speciesGroups[i].OrderByDescending(record => record.experience).FirstOrDefault();
                if (teacher == null || teacher.experience < 35f) continue;
                float lesson = Mathf.Min(10f, teacher.experience * 0.015f);
                for (int j = 0; j < colonists.Count; j++)
                {
                    Pawn student = colonists[j];
                    if (student == teacher.colonist) continue;
                    ColonistSpeciesKnowledgeRecord record = For(student, teacher.species);
                    if (record.experience >= teacher.experience * 0.6f) continue;
                    record.experience += lesson;
                    shared++;
                }
            }
            if (shared > 0 && WildlifeTestLog.Enabled) WildlifeTestLog.Write("OralKnowledge", "lessons=" + shared + " species=" + speciesGroups.Count);
        }

        public ColonistSpeciesKnowledgeRecord For(Pawn colonist, ThingDef species, bool create = true)
        {
            byColonistSpecies.TryGetValue(Key(colonist, species), out ColonistSpeciesKnowledgeRecord record);
            if (record == null && create) { record = new ColonistSpeciesKnowledgeRecord { colonist = colonist, species = species }; records.Add(record); byColonistSpecies[Key(colonist, species)] = record; }
            return record;
        }

        public int Level(Pawn colonist, ThingDef species)
        {
            float xp = For(colonist, species, false)?.experience ?? 0f;
            return xp >= 1200f ? 5 : xp >= 650f ? 4 : xp >= 300f ? 3 : xp >= 120f ? 2 : xp >= 35f ? 1 : 0;
        }

        public float ColonyExperienceFor(ThingDef species)
        {
            if (species == null) return 0f;
            return records.Where(record => record?.species == species && record.colonist?.Faction == Faction.OfPlayer).Sum(record => record.experience);
        }

        public static float ColonyExperience(ThingDef species)
        {
            if (species == null || Current.Game?.Maps == null) return 0f;
            float total = 0f;
            for (int i = 0; i < Current.Game.Maps.Count; i++) total += Current.Game.Maps[i].GetComponent<HuntingKnowledgeMapComponent>()?.ColonyExperienceFor(species) ?? 0f;
            return total;
        }

        public static int ColonyLevel(ThingDef species)
        {
            float xp = ColonyExperience(species);
            return xp >= 1200f ? 5 : xp >= 650f ? 4 : xp >= 300f ? 3 : xp >= 120f ? 2 : xp >= 35f ? 1 : 0;
        }

        public float TacticalBonus(Pawn colonist, ThingDef species) => Level(colonist, species) * 0.8f;

        public void Learn(Pawn colonist, ThingDef species, float amount, bool success = false, bool failure = false)
        {
            if (!HerdsMod.Settings.enableSpeciesKnowledgeProgression || colonist == null || species == null || amount <= 0f) return;
            int oldProficiency = WildlifeProficiencyLevel(colonist);
            amount *= (1f + oldProficiency * 0.10f) * WildlifeRoleUtility.AnimalKnowledgeFactor(colonist);
            ColonistSpeciesKnowledgeRecord record = For(colonist, species);
            int oldLevel = Level(colonist, species);
            record.experience += amount;
            if (success) record.successfulHunts++;
            if (failure) record.failedHunts++;
            int newLevel = Level(colonist, species);
            if (newLevel > oldLevel)
            {
                string outcome = colonist.LabelShortCap + " advanced to " + LevelLabel(newLevel) + " knowledge of " + species.label + ".";
                Messages.Message(outcome, colonist, MessageTypeDefOf.PositiveEvent, false);
                WildlifeExperience.Record("Animal Knowledge", outcome, colonist);
            }
            if (WildlifeTestLog.Enabled && (newLevel > oldLevel || success || failure)) WildlifeTestLog.Write("SpeciesKnowledge", "species=" + species.defName + " xp=" + record.experience.ToString("0.0") + " level=" + newLevel + " gained=" + amount.ToString("0.0") + " success=" + success + " failure=" + failure, colonist);
            NotifyProficiencyChange(colonist, oldProficiency);
        }

        public IEnumerable<ColonistSpeciesKnowledgeRecord> ForColonist(Pawn colonist) => records.Where(record => record.colonist == colonist).OrderByDescending(record => record.experience);

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

        public int BiomeLevel(Pawn colonist, BiomeDef biome) => LevelForExperience(BiomeFor(colonist, biome, false)?.experience ?? 0f);

        public IEnumerable<ColonistBiomeKnowledgeRecord> BiomesForColonist(Pawn colonist) =>
            biomeRecords.Where(record => record.colonist == colonist).OrderByDescending(record => record.experience);

        public void LearnBiome(Pawn colonist, BiomeDef biome, float amount, bool completed = false)
        {
            if (!HerdsMod.Settings.enableSpeciesKnowledgeProgression || colonist == null || biome == null || amount <= 0f) return;
            int oldProficiency = WildlifeProficiencyLevel(colonist);
            amount *= (1f + oldProficiency * 0.10f) * WildlifeRoleUtility.BiomeKnowledgeFactor(colonist);
            ColonistBiomeKnowledgeRecord record = BiomeFor(colonist, biome);
            int oldLevel = LevelForExperience(record.experience);
            record.experience += amount;
            if (completed) record.completedExpeditions++;
            int newLevel = LevelForExperience(record.experience);
            if (newLevel > oldLevel)
                Messages.Message(colonist.LabelShortCap + " advanced to " + LevelLabel(newLevel) + " knowledge of " + biome.LabelCap + ".", colonist, MessageTypeDefOf.PositiveEvent, false);
            if (WildlifeTestLog.Enabled && (newLevel > oldLevel || completed))
                WildlifeTestLog.Write("BiomeKnowledge", "biome=" + biome.defName + " xp=" + record.experience.ToString("0.0") + " level=" + newLevel, colonist);
            NotifyProficiencyChange(colonist, oldProficiency);
        }

        public int KnownAnimalCount(Pawn colonist) => records.Count(record => record.colonist == colonist && record.experience > 0f);
        public int KnownBiomeCount(Pawn colonist) => biomeRecords.Count(record => record.colonist == colonist && record.experience > 0f);

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
            ColonistSpeciesKnowledgeRecord record = For(colonist, species); record.experience = experience;
            if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("DevKnowledge", "species=" + species.defName + " xp=" + experience + " level=" + Level(colonist, species), colonist);
        }

        public int DebugMasterAllSpecies(Pawn colonist)
        {
            if (colonist == null) return 0;
            List<ThingDef> species = DefDatabase<ThingDef>.AllDefsListForReading.Where(def => def?.race?.Animal == true).ToList();
            for (int i = 0; i < species.Count; i++) For(colonist, species[i]).experience = 1200f;
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

        public static string LevelLabel(int level) => level <= 0 ? "Unfamiliar" : level == 1 ? "Recognized" : level == 2 ? "Tracked" : level == 3 ? "Studied" : level == 4 ? "Expert" : "Master";

        public static int LevelForExperience(float xp) =>
            xp >= 1200f ? 5 : xp >= 650f ? 4 : xp >= 300f ? 3 : xp >= 120f ? 2 : xp >= 35f ? 1 : 0;

        public static string BiomeKnowledgeTooltip(ColonistBiomeKnowledgeRecord record)
        {
            int level = LevelForExperience(record?.experience ?? 0f);
            float travel = level * 0.02f;
            float incidents = level * 0.03f;
            float field = level * 0.02f;
            string tier = level <= 0 ? "No reliable field knowledge yet." :
                level == 1 ? "Recognized: identifies common terrain conditions and improves basic route estimates." :
                level == 2 ? "Tracked: recognizes wildlife trails and recurring regional hazards." :
                level == 3 ? "Studied: provides dependable travel, encounter, and safety forecasts." :
                level == 4 ? "Expert: anticipates subtle seasonal and habitat changes." :
                "Master: reads this biome with exceptional confidence.";
            string text = record.biome.LabelCap + " — " + LevelLabel(level) + "\n\n" + tier +
                "\n\nExpedition Effects\n• Travel time: -" + travel.ToStringPercent() +
                "\n• Incident risk: -" + incidents.ToStringPercent() +
                "\n• Encounter and objective chance: +" + field.ToStringPercent() +
                "\n• Completed expeditions: " + record.completedExpeditions +
                "\n\nKnowledge: " + record.experience.ToString("0") + " XP";
            if (level < 5) text += "\nNext tier: " + LevelLabel(level + 1) + " at " + LevelThreshold(level + 1).ToString("0") + " XP.";
            return text;
        }

        public static string ColonyKnowledgeTooltip(ThingDef species, float experience, int level)
        {
            string effects = level <= 0
                ? "The colony cannot yet identify this animal reliably."
                : level == 1
                    ? "Recognized: identifies the animal, allows it to appear in Regional Wildlife when present, and reveals basic species, body size, movement, wildness, and trainability statistics."
                    : level == 2
                        ? "Tracked: reveals regional population trends with Wildlife Stewardship, plus food, market, and general animal statistics."
                        : level == 3
                            ? "Studied: unlocks species management, ecological forecasts when available, and combat and health statistics."
                            : level == 4
                                ? "Expert: reveals product yields, leather, meat, milk, wool, and resistance statistics."
                                : "Master: reveals every available statistic for this animal.";
            string result = species.LabelCap + " — " + LevelLabel(level) + "\n\n" + effects + "\n\nColony knowledge: " + experience.ToString("0") + " XP";
            if (level < 5)
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
            if (level < 5)
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
            if (level == 1) return 35f;
            if (level == 2) return 120f;
            if (level == 3) return 300f;
            if (level == 4) return 650f;
            return 1200f;
        }

        public static string RevealedStatistics(int level)
        {
            if (level <= 0) return "Detailed species statistics remain hidden.";
            if (level == 1) return "Reveals basic statistics, body size, movement speed, wildness, and trainability.";
            if (level == 2) return "Also reveals food, general animal, miscellaneous, and market statistics.";
            if (level == 3) return "Also reveals combat and health statistics.";
            if (level == 4) return "Also reveals resistances, productivity, and resource yields.";
            return "Reveals all species statistics.";
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

    public sealed class Window_ColonistWildlifeKnowledge : Window
    {
        private readonly Pawn colonist;
        private Vector2 scroll;
        private bool showBiomes;
        public override Vector2 InitialSize => new Vector2(700f, 600f);

        public Window_ColonistWildlifeKnowledge(Pawn colonist)
        {
            this.colonist = colonist;
            doCloseX = true;
            absorbInputAroundWindow = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 34f), colonist.LabelShortCap + " — Wildlife Knowledge");
            Text.Font = GameFont.Small;
            if (Widgets.ButtonText(new Rect(0f, 42f, 170f, 32f), "Animal Knowledge", active: showBiomes)) { showBiomes = false; scroll = Vector2.zero; }
            if (Widgets.ButtonText(new Rect(178f, 42f, 170f, 32f), "Biome Knowledge", active: !showBiomes)) { showBiomes = true; scroll = Vector2.zero; }
            if (showBiomes) DrawBiomes(new Rect(0f, 84f, inRect.width, inRect.height - 84f));
            else DrawAnimals(new Rect(0f, 84f, inRect.width, inRect.height - 84f));
        }

        private void DrawAnimals(Rect outer)
        {
            HuntingKnowledgeMapComponent component = colonist.MapHeld?.GetComponent<HuntingKnowledgeMapComponent>();
            List<ColonistSpeciesKnowledgeRecord> values = component?.ForColonist(colonist).ToList() ?? new List<ColonistSpeciesKnowledgeRecord>();
            Rect view = new Rect(0f, 0f, outer.width - 18f, Mathf.Max(outer.height, values.Count * 54f));
            Widgets.BeginScrollView(outer, ref scroll, view);
            for (int i = 0; i < values.Count; i++)
            {
                ColonistSpeciesKnowledgeRecord record = values[i];
                int level = component.Level(colonist, record.species);
                Rect row = new Rect(0f, i * 54f, view.width, 48f);
                Widgets.DrawMenuSection(row);
                Widgets.Label(new Rect(10f, row.y + 5f, row.width * 0.48f, 24f), record.species.LabelCap);
                Text.Anchor = TextAnchor.MiddleRight;
                Widgets.Label(new Rect(row.width * 0.50f, row.y + 3f, row.width * 0.47f, 26f), HuntingKnowledgeMapComponent.LevelLabel(level) + "  •  " + record.experience.ToString("0") + " XP");
                Text.Anchor = TextAnchor.UpperLeft;
                Widgets.FillableBar(new Rect(10f, row.y + 34f, row.width - 20f, 7f), Mathf.Clamp01(record.experience / 1200f));
                TooltipHandler.TipRegion(row, HuntingKnowledgeMapComponent.KnowledgeEffectsTooltip(colonist, record.species, record.experience, level));
            }
            Widgets.EndScrollView();
            if (values.Count == 0) Widgets.Label(outer.ContractedBy(10f), "No Animal Knowledge yet.");
        }

        private void DrawBiomes(Rect outer)
        {
            HuntingKnowledgeMapComponent component = colonist.MapHeld?.GetComponent<HuntingKnowledgeMapComponent>();
            List<ColonistBiomeKnowledgeRecord> values = component?.BiomesForColonist(colonist).ToList() ?? new List<ColonistBiomeKnowledgeRecord>();
            Rect view = new Rect(0f, 0f, outer.width - 18f, Mathf.Max(outer.height, values.Count * 54f));
            Widgets.BeginScrollView(outer, ref scroll, view);
            for (int i = 0; i < values.Count; i++)
            {
                ColonistBiomeKnowledgeRecord record = values[i];
                int level = HuntingKnowledgeMapComponent.LevelForExperience(record.experience);
                Rect row = new Rect(0f, i * 54f, view.width, 48f);
                Widgets.DrawMenuSection(row);
                Widgets.Label(new Rect(10f, row.y + 5f, row.width * 0.48f, 24f), record.biome.LabelCap);
                Text.Anchor = TextAnchor.MiddleRight;
                Widgets.Label(new Rect(row.width * 0.50f, row.y + 3f, row.width * 0.47f, 26f), HuntingKnowledgeMapComponent.LevelLabel(level) + "  •  " + record.experience.ToString("0") + " XP");
                Text.Anchor = TextAnchor.UpperLeft;
                Widgets.FillableBar(new Rect(10f, row.y + 34f, row.width - 20f, 7f), Mathf.Clamp01(record.experience / 1200f));
                TooltipHandler.TipRegion(row, HuntingKnowledgeMapComponent.BiomeKnowledgeTooltip(record));
            }
            Widgets.EndScrollView();
            if (values.Count == 0) Widgets.Label(outer.ContractedBy(10f), "No Biome Knowledge yet. Travel through and complete wildlife expeditions to learn terrain, routes, and hazards.");
        }
    }

    public sealed class Window_ColonyWildlifeKnowledge : Window
    {
        private sealed class KnowledgeRow
        {
            public ThingDef species;
            public float experience;
            public int level;
        }

        private Vector2 scroll;
        private readonly List<KnowledgeRow> rows;
        public override Vector2 InitialSize => new Vector2(700f, 600f);

        public Window_ColonyWildlifeKnowledge()
        {
            doCloseX = true;
            absorbInputAroundWindow = true;
            rows = DefDatabase<ThingDef>.AllDefsListForReading
                .Where(def => def.race?.Animal == true)
                .Select(def => new KnowledgeRow { species = def, experience = HuntingKnowledgeMapComponent.ColonyExperience(def), level = HuntingKnowledgeMapComponent.ColonyLevel(def) })
                .Where(row => row.experience > 0f)
                .OrderByDescending(row => row.experience)
                .ThenBy(row => row.species.label)
                .ToList();
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 34f), "Colony Animal Knowledge");
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(0f, 38f, inRect.width, 42f), "Combined knowledge gathered by every colonist through observation, tracks, encounters, and hunts. Higher tiers reveal more species statistics.");
            Rect outer = new Rect(0f, 86f, inRect.width, inRect.height - 86f);
            Rect view = new Rect(0f, 0f, outer.width - 18f, Mathf.Max(outer.height, rows.Count * 58f));
            Widgets.BeginScrollView(outer, ref scroll, view);
            for (int i = 0; i < rows.Count; i++)
            {
                KnowledgeRow knowledge = rows[i];
                ThingDef def = knowledge.species;
                float xp = knowledge.experience;
                int level = knowledge.level;
                Rect row = new Rect(0f, i * 58f, view.width, 52f);
                Widgets.DrawMenuSection(row);
                Widgets.Label(new Rect(row.x + 10f, row.y + 6f, row.width * 0.46f, 24f), def.LabelCap);
                Text.Anchor = TextAnchor.MiddleRight;
                Widgets.Label(new Rect(row.x + row.width * 0.48f, row.y + 4f, row.width * 0.49f, 26f), HuntingKnowledgeMapComponent.LevelLabel(level) + "  •  " + xp.ToString("0") + " XP");
                Text.Anchor = TextAnchor.UpperLeft;
                Widgets.FillableBar(new Rect(row.x + 10f, row.y + 35f, row.width - 20f, 8f), Mathf.Clamp01(xp / 1200f));
                TooltipHandler.TipRegion(row, HuntingKnowledgeMapComponent.ColonyKnowledgeTooltip(def, xp, level));
            }
            Widgets.EndScrollView();
            if (rows.Count == 0) Widgets.Label(new Rect(8f, 100f, inRect.width - 16f, 50f), "No wildlife species have been studied yet.");
        }
    }
}
