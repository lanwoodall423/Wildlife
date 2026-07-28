using System.Collections.Generic;
using System.Linq;
using LudeonTK;
using RimWorld;
using UnityEngine;
using Verse;

namespace Herds
{
    public enum WildlifeLandmarkIdentity
    {
        Unformed,
        Sanctuary,
        WateringGround,
        FeedingGround,
        ForbiddenGround,
        KillingGround,
        PredatorNest,
        SacredGround,
        UnstableGround
    }

    public sealed class WildlifeLandmarkReputation : IExposable
    {
        public ThingDef species;
        public float sanctuary;
        public float water;
        public float feeding;
        public float forbidden;
        public float killingGround;
        public float predatorNest;
        public float sacred;
        public float unstable;
        public int formedTick;
        public int lastUpdateTick;
        public string latestEvidence;
        public WildlifeLandmarkIdentity lastIdentity;

        public void ExposeData()
        {
            Scribe_Defs.Look(ref species, "species");
            Scribe_Values.Look(ref sanctuary, "sanctuary");
            Scribe_Values.Look(ref water, "water");
            Scribe_Values.Look(ref feeding, "feeding");
            Scribe_Values.Look(ref forbidden, "forbidden");
            Scribe_Values.Look(ref killingGround, "killingGround");
            Scribe_Values.Look(ref predatorNest, "predatorNest");
            Scribe_Values.Look(ref sacred, "sacred");
            Scribe_Values.Look(ref unstable, "unstable");
            Scribe_Values.Look(ref formedTick, "formedTick");
            Scribe_Values.Look(ref lastUpdateTick, "lastUpdateTick");
            Scribe_Values.Look(ref latestEvidence, "latestEvidence");
            Scribe_Values.Look(ref lastIdentity, "lastIdentity");
        }
    }

    public sealed class WildlifeLandmarkMapComponent : MapComponent
    {
        private List<WildlifeLandmarkReputation> reputations = new List<WildlifeLandmarkReputation>();
        private int nextTick;
        private int cachedFires;
        private int cachedConstruction;

        public WildlifeLandmarkMapComponent(Map map) : base(map) { }
        public IReadOnlyList<WildlifeLandmarkReputation> Reputations => reputations;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref reputations, "wildlifeLandmarkReputations", LookMode.Deep);
            Scribe_Values.Look(ref nextTick, "nextWildlifeLandmarkTick");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                reputations = reputations?.Where(value => value?.species?.race?.Animal == true).ToList() ??
                    new List<WildlifeLandmarkReputation>();
        }

        public override void MapComponentTick()
        {
            if (HerdsMod.Settings?.enableColonyWildlifeLandmark != true) return;
            int now = Find.TickManager.TicksGame;
            if (now < nextTick) return;
            nextTick = now + 60000;
            UpdateReputations(now);
        }

        public override void MapComponentDraw()
        {
            if (!Prefs.DevMode || !WildlifeDevMaster.CompleteOverlayEnabled ||
                HerdsMod.Settings?.enableColonyWildlifeLandmark != true || Find.CurrentMap != map) return;
            foreach (IGrouping<ThingDef, Pawn> group in map.mapPawns.AllPawnsSpawned
                .Where(pawn => pawn.Faction == null && pawn.RaceProps?.Animal == true).GroupBy(pawn => pawn.def))
            {
                Pawn animal = group.First();
                WildlifeLandmarkReputation reputation = For(group.Key);
                if (reputation == null || Strength(reputation) < 0.2f) continue;
                Color color = ColorFor(Identity(reputation));
                GenDraw.DrawRadiusRing(animal.Position, 1f + Strength(reputation), color);
            }
            WildlifeLandmarkIdentity overall = OverallIdentity();
            if (overall != WildlifeLandmarkIdentity.Unformed)
                GenDraw.DrawRadiusRing(map.Center, 8f, ColorFor(overall));
        }

        public override void MapComponentOnGUI()
        {
            base.MapComponentOnGUI();
            if (!Prefs.DevMode || !WildlifeDevMaster.CompleteOverlayEnabled ||
                HerdsMod.Settings?.enableColonyWildlifeLandmark != true || Find.CurrentMap != map) return;
            foreach (IGrouping<ThingDef, Pawn> group in map.mapPawns.AllPawnsSpawned
                .Where(pawn => pawn.Faction == null && pawn.RaceProps?.Animal == true).GroupBy(pawn => pawn.def))
            {
                Pawn animal = group.First();
                WildlifeLandmarkReputation reputation = For(group.Key);
                if (reputation == null || Strength(reputation) < 0.2f) continue;
                GenMapUI.DrawThingLabel(animal,
                    "colony: " + IdentityLabel(Identity(reputation)).ToLowerInvariant());
            }
        }

        private void UpdateReputations(int now)
        {
            HashSet<ThingDef> species = new HashSet<ThingDef>(map.mapPawns.AllPawnsSpawned
                .Where(pawn => pawn.RaceProps?.Animal == true).Select(pawn => pawn.def));
            RegionalWildlifeMapComponent regional = map.GetComponent<RegionalWildlifeMapComponent>();
            if (regional != null)
                foreach (RegionalSpeciesRecord record in regional.Records)
                    if (HuntingKnowledgeMapComponent.ColonyExperience(record.species) > 0f) species.Add(record.species);
            foreach (AnimalTraditionRecord tradition in map.GetComponent<AnimalTraditionMapComponent>()?.Traditions ??
                Enumerable.Empty<AnimalTraditionRecord>()) species.Add(tradition.species);
            CacheMapEvidence();
            foreach (ThingDef animal in species) Update(animal, now);
        }

        private void CacheMapEvidence()
        {
            cachedFires = map.listerThings.ThingsInGroup(ThingRequestGroup.Fire).Count;
            cachedConstruction = map.listerBuildings.allBuildingsColonist.Count;
        }

        private void Update(ThingDef species, int now)
        {
            WildlifeLandmarkReputation value = For(species, true);
            WildlifeLandmarkIdentity before = Identity(value);
            List<Building_WildlifeTool> tools = map.listerBuildings.allBuildingsColonist
                .OfType<Building_WildlifeTool>().Where(tool => tool.active && tool.Operational).ToList();
            float reserves = tools.Count(tool => tool.Kind == WildlifeToolKind.Reserve ||
                tool.Kind == WildlifeToolKind.HabitatRestoration);
            float water = tools.Count(tool => tool.Kind == WildlifeToolKind.WaterSource);
            float bait = tools.Count(tool => tool.Kind == WildlifeToolKind.Bait);
            float deterrents = tools.Count(tool => tool.Kind == WildlifeToolKind.PredatorDeterrent);
            WildlifeMemoryMapComponent memory = map.GetComponent<WildlifeMemoryMapComponent>();
            List<AnimalColonistMemory> experiences = memory?.Memories.Where(record =>
                record?.animal?.def == species).ToList() ?? new List<AnimalColonistMemory>();
            float trust = experiences.Count == 0 ? 0f : experiences.Average(record => record.trust);
            float fear = experiences.Count == 0 ? 0f : experiences.Average(record => record.fear);
            float hostility = experiences.Count == 0 ? 0f : experiences.Average(record => record.hostility);
            float hunting = experiences.Sum(record => record.huntingEncounters + record.rangedEncounters * 0.6f +
                record.trapEncounters * 0.8f);
            List<AnimalTraditionRecord> traditions = map.GetComponent<AnimalTraditionMapComponent>()?.Traditions
                .Where(record => record.species == species).ToList() ?? new List<AnimalTraditionRecord>();
            float safeTradition = traditions.Where(record => record.kind == AnimalTraditionKind.SafeValley ||
                record.kind == AnimalTraditionKind.KindHands).Sum(record => record.strength) * 0.18f;
            float fearTradition = traditions.Where(record => record.kind == AnimalTraditionKind.FearedHunter ||
                record.kind == AnimalTraditionKind.ThunderSticks || record.kind == AnimalTraditionKind.TrapWise)
                .Sum(record => record.strength) * 0.16f;
            float ranchTradition = traditions.Where(record => record.kind == AnimalTraditionKind.EasyRanch)
                .Sum(record => record.strength) * 0.22f;
            float instability = Mathf.Clamp01(cachedFires * 0.18f +
                Mathf.Max(0, cachedConstruction - 35) * 0.0025f);
            float sanctuaryTarget = Mathf.Clamp01(reserves * 0.18f + trust * 0.5f + safeTradition);
            float waterTarget = Mathf.Clamp01(water * 0.32f + safeTradition * 0.25f);
            float feedingTarget = Mathf.Clamp01(bait * 0.25f +
                (species.race.predator ? ranchTradition : reserves * 0.06f));
            float forbiddenTarget = Mathf.Clamp01(fear * 0.38f + hostility * 0.22f +
                hunting * 0.018f + fearTradition + (species.race.predator ? deterrents * 0.18f : 0f));
            float killingTarget = species.race.predator
                ? Mathf.Clamp01(ranchTradition + feedingTarget * 0.32f - deterrents * 0.08f)
                : Mathf.Clamp01(hunting * 0.025f + fearTradition * 0.45f);
            float predatorNestTarget = species.race.predator ? 0f :
                Mathf.Clamp01(hunting * 0.018f + map.mapPawns.AllPawnsSpawned.Count(pawn =>
                    pawn.Faction == Faction.OfPlayer && pawn.RaceProps?.predator == true) * 0.10f);
            float sacredTarget = Mathf.Clamp01(safeTradition * 0.75f +
                map.GetComponent<WildlifeRegionalStoriesMapComponent>()?.FamilyLines.Count(line =>
                    line.species == species) * 0.035f ?? 0f);
            value.sanctuary = Approach(value.sanctuary, sanctuaryTarget);
            value.water = Approach(value.water, waterTarget);
            value.feeding = Approach(value.feeding, feedingTarget);
            value.forbidden = Approach(value.forbidden, forbiddenTarget);
            value.killingGround = Approach(value.killingGround, killingTarget);
            value.predatorNest = Approach(value.predatorNest, predatorNestTarget);
            value.sacred = Approach(value.sacred, sacredTarget);
            value.unstable = Approach(value.unstable, instability);
            value.lastUpdateTick = now;
            WildlifeLandmarkIdentity after = Identity(value);
            value.latestEvidence = Evidence(value, tools, experiences, traditions);
            if (after != before && Strength(value) >= 0.28f)
            {
                value.lastIdentity = after;
                if (HuntingKnowledgeMapComponent.ColonyLevel(species) >= 2 &&
                    HerdsMod.Settings.enableWildlifeAlerts)
                    Messages.Message(species.LabelCap + " increasingly treat the colony as " +
                        Article(after) + " " + IdentityLabel(after).ToLowerInvariant() + ".",
                        MessageTypeDefOf.NeutralEvent, false);
                if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("LandmarkReputation",
                    "species=" + species.defName + " identity=" + after + " strength=" + Strength(value).ToString("0.00"));
            }
        }

        private static float Approach(float current, float target) =>
            Mathf.Clamp01(Mathf.Lerp(current, target, target > current ? 0.075f : 0.035f));

        public WildlifeLandmarkReputation For(ThingDef species, bool create = false)
        {
            WildlifeLandmarkReputation value = reputations.FirstOrDefault(record => record.species == species);
            if (value == null && create)
            {
                value = new WildlifeLandmarkReputation { species = species,
                    formedTick = Find.TickManager?.TicksGame ?? 0 };
                reputations.Add(value);
            }
            return value;
        }

        public float MigrationAttraction(ThingDef species)
        {
            WildlifeLandmarkReputation value = For(species);
            if (value == null || HerdsMod.Settings?.enableColonyWildlifeLandmark != true) return 0f;
            float positive = value.sanctuary * 0.7f + value.water * 0.45f + value.feeding * 0.5f +
                value.sacred * 0.55f;
            if (species.race.predator) positive += value.killingGround * 0.75f;
            return Mathf.Clamp(positive - value.forbidden * 0.85f - value.predatorNest * 0.7f -
                value.unstable * 0.35f, -1.5f, 1.5f);
        }

        public float ReturnChanceModifier(ThingDef species) => MigrationAttraction(species) * 0.16f;

        public float AvoidanceFactor(Pawn animal)
        {
            WildlifeLandmarkReputation value = For(animal?.def);
            if (value == null || HerdsMod.Settings?.enableColonyWildlifeLandmark != true) return 1f;
            return Mathf.Clamp(1f + value.forbidden * 0.34f + value.predatorNest * 0.28f +
                value.unstable * 0.12f - value.sanctuary * 0.24f - value.sacred * 0.14f, 0.65f, 1.75f);
        }

        public float PredatorHumanPreyScore(Pawn predator)
        {
            WildlifeLandmarkReputation value = For(predator?.def);
            if (value == null || HerdsMod.Settings?.enableColonyWildlifeLandmark != true) return 0f;
            return value.killingGround * 125f + value.feeding * 45f -
                value.forbidden * 110f - value.unstable * 30f;
        }

        public string Summary(ThingDef species, int knowledge)
        {
            WildlifeLandmarkReputation value = For(species);
            if (value == null || Strength(value) < 0.12f) return null;
            WildlifeLandmarkIdentity identity = Identity(value);
            if (knowledge <= 0) return "Behavior suggests this species recognizes the colony as a landmark.";
            string result = IdentityLabel(identity);
            if (knowledge == 1) return result + ": movement patterns show a developing response to the colony.";
            result += ": " + Description(identity, species.race.predator);
            if (knowledge >= 3)
                result += "\nConfidence: " + Strength(value).ToStringPercent() +
                    (value.latestEvidence.NullOrEmpty() ? "" : "\nEvidence: " + value.latestEvidence);
            return result;
        }

        public string OverviewSummary()
        {
            List<WildlifeLandmarkReputation> known = reputations.Where(value =>
                Strength(value) >= 0.16f && HuntingKnowledgeMapComponent.ColonyExperience(value.species) > 0f).ToList();
            if (known.Count == 0) return "No stable wildlife reputation yet.";
            WildlifeLandmarkIdentity first = Identity(known[0]);
            bool conflict = known.Any(value => Identity(value) != first);
            return conflict ? known.Count + " known species interpret the colony differently." :
                known.Count + " known species regard it as " + Article(first) + " " +
                IdentityLabel(first).ToLowerInvariant() + ".";
        }

        public WildlifeLandmarkIdentity OverallIdentity()
        {
            WildlifeLandmarkReputation strongest = reputations.OrderByDescending(Strength).FirstOrDefault();
            return strongest == null ? WildlifeLandmarkIdentity.Unformed : Identity(strongest);
        }

        public static WildlifeLandmarkIdentity Identity(WildlifeLandmarkReputation value)
        {
            if (value == null || Strength(value) < 0.12f) return WildlifeLandmarkIdentity.Unformed;
            float[] scores = { value.sanctuary, value.water, value.feeding, value.forbidden,
                value.killingGround, value.predatorNest, value.sacred, value.unstable };
            int index = 0;
            for (int i = 1; i < scores.Length; i++) if (scores[i] > scores[index]) index = i;
            return (WildlifeLandmarkIdentity)(index + 1);
        }

        public static float Strength(WildlifeLandmarkReputation value) => value == null ? 0f :
            Mathf.Max(value.sanctuary, value.water, value.feeding, value.forbidden,
                value.killingGround, value.predatorNest, value.sacred, value.unstable);

        private static string Evidence(WildlifeLandmarkReputation value, List<Building_WildlifeTool> tools,
            List<AnimalColonistMemory> memories, List<AnimalTraditionRecord> traditions)
        {
            if (traditions.Count > 0) return "animal traditions and " + traditions.Sum(record => record.transmissions) + " retellings";
            if (memories.Count > 0) return memories.Count + " remembered encounters with colonists";
            if (tools.Count > 0) return tools.Count + " active wildlife management structures";
            return "repeated movement and local conditions";
        }

        public static string IdentityLabel(WildlifeLandmarkIdentity identity) =>
            identity == WildlifeLandmarkIdentity.Sanctuary ? "Sanctuary" :
            identity == WildlifeLandmarkIdentity.WateringGround ? "Watering Ground" :
            identity == WildlifeLandmarkIdentity.FeedingGround ? "Feeding Ground" :
            identity == WildlifeLandmarkIdentity.ForbiddenGround ? "Forbidden Ground" :
            identity == WildlifeLandmarkIdentity.KillingGround ? "Killing Ground" :
            identity == WildlifeLandmarkIdentity.PredatorNest ? "Predator Nest" :
            identity == WildlifeLandmarkIdentity.SacredGround ? "Sacred Ground" :
            identity == WildlifeLandmarkIdentity.UnstableGround ? "Unstable Ground" : "Unformed";

        private static string Description(WildlifeLandmarkIdentity identity, bool predator) =>
            identity == WildlifeLandmarkIdentity.Sanctuary ? "Animals may seek refuge near the colony during danger." :
            identity == WildlifeLandmarkIdentity.WateringGround ? "Water draws recurring visits and predictable routes." :
            identity == WildlifeLandmarkIdentity.FeedingGround ? "Food draws recurring visits and may attract predators." :
            identity == WildlifeLandmarkIdentity.ForbiddenGround ? "Animals prefer to route around the colony." :
            identity == WildlifeLandmarkIdentity.KillingGround ? predator ?
                "Predators associate the colony with vulnerable prey." : "Animals associate the colony with successful hunts and death." :
            identity == WildlifeLandmarkIdentity.PredatorNest ? "Prey treat the settlement itself as predator territory." :
            identity == WildlifeLandmarkIdentity.SacredGround ? "Generations are drawn back by inherited memory and tradition." :
            "Animals find the colony's rapid changes difficult to predict.";

        private static string Article(WildlifeLandmarkIdentity identity) =>
            identity == WildlifeLandmarkIdentity.UnstableGround ? "an" : "a";

        private static Color ColorFor(WildlifeLandmarkIdentity identity) =>
            identity == WildlifeLandmarkIdentity.Sanctuary || identity == WildlifeLandmarkIdentity.SacredGround ? Color.green :
            identity == WildlifeLandmarkIdentity.WateringGround ? Color.cyan :
            identity == WildlifeLandmarkIdentity.FeedingGround ? Color.yellow :
            identity == WildlifeLandmarkIdentity.UnstableGround ? Color.magenta :
            identity == WildlifeLandmarkIdentity.ForbiddenGround ? new Color(1f, 0.55f, 0.1f) : Color.red;

        [DebugAction("Wildlife", "Advance colony landmark reputation",
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void DebugAdvance()
        {
            WildlifeLandmarkMapComponent component = Find.CurrentMap?.GetComponent<WildlifeLandmarkMapComponent>();
            if (component == null) return;
            for (int i = 0; i < 12; i++) component.UpdateReputations(Find.TickManager.TicksGame + i * 60000);
            Messages.Message("Colony wildlife landmark reputation advanced by twelve days.", MessageTypeDefOf.NeutralEvent, false);
        }
    }
}
