using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace Herds
{
    public enum PreySocialType
    {
        Herd,
        Family,
        Colony,
        Solitary,
        Flock
    }

    public enum PreyDefenseStrategy
    {
        Flight,
        Scatter,
        ProtectYoung,
        Hide,
        Freeze,
        StandGround
    }

    public enum PreyRefugePreference
    {
        None,
        Trees,
        Vegetation,
        Dens,
        TreesAndVegetation,
        Any,
        TreesAndDens
    }

    public sealed class PreyBehaviorExtension : DefModExtension
    {
        public bool forcePrey = true;
        public PreySocialType socialType = PreySocialType.Solitary;
        public PreyDefenseStrategy defenseStrategy = PreyDefenseStrategy.Flight;
        public PreyRefugePreference refugePreference = PreyRefugePreference.None;
        public float maximumHidingBodySize = 0.45f;
        public float refugeSearchRadius = 24f;
        public float hideSuccessChance = 0.7f;
        public float vigilanceChance = 0.5f;
        public int preferredGroupSize = 1;
    }

    public sealed class PreyProfile
    {
        public ThingDef race;
        public bool eligible;
        public PreySocialType socialType;
        public PreyDefenseStrategy defenseStrategy;
        public PreyRefugePreference refugePreference;
        public float maximumHidingBodySize;
        public float refugeSearchRadius;
        public float hideSuccessChance;
        public float vigilanceChance;
        public int preferredGroupSize;

        public bool IsSocial => socialType != PreySocialType.Solitary;
        public bool CanHide => defenseStrategy == PreyDefenseStrategy.Hide && refugePreference != PreyRefugePreference.None;
        public bool CanClimbTrees => refugePreference == PreyRefugePreference.Trees || refugePreference == PreyRefugePreference.TreesAndDens || refugePreference == PreyRefugePreference.TreesAndVegetation || refugePreference == PreyRefugePreference.Any;
        public bool CanUseDens => refugePreference == PreyRefugePreference.Dens || refugePreference == PreyRefugePreference.TreesAndDens || refugePreference == PreyRefugePreference.Vegetation || refugePreference == PreyRefugePreference.Any;
    }

    public sealed class SpeciesBehaviorOverride : IExposable
    {
        public string defName;
        public bool enabled;
        public PreySocialType socialType;
        public PreyDefenseStrategy defenseStrategy;
        public PreyRefugePreference refugePreference;
        public float maximumHidingBodySize = 0.45f;
        public float refugeSearchRadius = 24f;
        public float hideSuccessChance = 0.7f;
        public float vigilanceChance = 0.5f;
        public int preferredGroupSize = 1;

        public void ExposeData()
        {
            Scribe_Values.Look(ref defName, "defName");
            Scribe_Values.Look(ref enabled, "enabled");
            Scribe_Values.Look(ref socialType, "socialType");
            Scribe_Values.Look(ref defenseStrategy, "defenseStrategy");
            Scribe_Values.Look(ref refugePreference, "refugePreference");
            Scribe_Values.Look(ref maximumHidingBodySize, "maximumHidingBodySize", 0.45f);
            Scribe_Values.Look(ref refugeSearchRadius, "refugeSearchRadius", 24f);
            Scribe_Values.Look(ref hideSuccessChance, "hideSuccessChance", 0.7f);
            Scribe_Values.Look(ref vigilanceChance, "vigilanceChance", 0.5f);
            Scribe_Values.Look(ref preferredGroupSize, "preferredGroupSize", 1);
            maximumHidingBodySize = Mathf.Clamp(maximumHidingBodySize, 0.1f, 4f);
            refugeSearchRadius = Mathf.Clamp(refugeSearchRadius, 6f, 50f);
            hideSuccessChance = Mathf.Clamp(hideSuccessChance, 0.05f, 0.95f);
            vigilanceChance = Mathf.Clamp(vigilanceChance, 0.05f, 0.95f);
            if (socialType != PreySocialType.Solitary && preferredGroupSize <= 1)
                preferredGroupSize = socialType == PreySocialType.Family ? 6 :
                    socialType == PreySocialType.Colony ? 24 : socialType == PreySocialType.Flock ? 18 : 12;
            preferredGroupSize = Mathf.Clamp(preferredGroupSize, 1, 60);
        }

        public static SpeciesBehaviorOverride FromProfile(PreyProfile profile)
        {
            return new SpeciesBehaviorOverride
            {
                defName = profile.race.defName,
                socialType = profile.socialType,
                defenseStrategy = profile.defenseStrategy,
                refugePreference = profile.refugePreference,
                maximumHidingBodySize = profile.maximumHidingBodySize,
                refugeSearchRadius = profile.refugeSearchRadius,
                hideSuccessChance = profile.hideSuccessChance,
                vigilanceChance = profile.vigilanceChance,
                preferredGroupSize = profile.preferredGroupSize
            };
        }
    }

    public static class PreyProfileDatabase
    {
        private static readonly Dictionary<ThingDef, PreyProfile> Cache = new Dictionary<ThingDef, PreyProfile>();

        public static PreyProfile For(ThingDef race)
        {
            if (race == null) return null;
            if (Cache.TryGetValue(race, out PreyProfile profile)) return profile;
            profile = Build(race);
            Cache.Add(race, profile);
            return profile;
        }

        public static bool IsEligible(ThingDef race) => For(race)?.eligible == true;

        public static void Clear() => Cache.Clear();

        public static PreyProfile DefaultFor(ThingDef race) => BuildDefault(race);

        private static PreyProfile Build(ThingDef race)
        {
            PreyProfile profile = BuildDefault(race);
            SpeciesBehaviorOverride behaviorOverride = HerdsMod.Settings?.speciesOverrides?.FirstOrDefault(item => item.defName == race.defName && item.enabled);
            if (behaviorOverride == null) return profile;
            profile.socialType = behaviorOverride.socialType;
            profile.defenseStrategy = behaviorOverride.defenseStrategy;
            profile.refugePreference = behaviorOverride.refugePreference;
            profile.maximumHidingBodySize = behaviorOverride.maximumHidingBodySize;
            profile.refugeSearchRadius = behaviorOverride.refugeSearchRadius;
            profile.hideSuccessChance = behaviorOverride.hideSuccessChance;
            profile.vigilanceChance = behaviorOverride.vigilanceChance;
            profile.preferredGroupSize = behaviorOverride.preferredGroupSize;
            return profile;
        }

        private static PreyProfile BuildDefault(ThingDef race)
        {
            PreyBehaviorExtension extension = race.GetModExtension<PreyBehaviorExtension>();
            if (extension != null)
            {
                bool extensionBird = IsBird(race);
                return new PreyProfile
                {
                    race = race,
                    eligible = extension.forcePrey && race.race?.Animal == true,
                    socialType = extensionBird ? PreySocialType.Flock : extension.socialType,
                    defenseStrategy = extension.defenseStrategy,
                    refugePreference = extension.refugePreference,
                    maximumHidingBodySize = extension.maximumHidingBodySize,
                    refugeSearchRadius = extension.refugeSearchRadius,
                    hideSuccessChance = extension.hideSuccessChance,
                    vigilanceChance = extension.vigilanceChance,
                    preferredGroupSize = extension.preferredGroupSize
                };
            }

            bool eligible = race.race?.Animal == true && race.race.IsFlesh && !race.race.IsAnomalyEntity && !race.race.predator;
            float bodySize = race.race?.baseBodySize ?? 1f;
            bool herd = race.race?.herdAnimal == true;
            bool bird = IsBird(race);
            string speciesName = ((race.defName ?? string.Empty) + " " + (race.label ?? string.Empty)).ToLowerInvariant();
            bool colony = !herd && ContainsAny(speciesName, "rat", "mouse", "vole", "gerbil", "hamster", "guinea pig", "prairie dog", "chinchilla", "meerkat");
            bool family = !herd && !colony && ContainsAny(speciesName, "beaver", "otter", "squirrel", "marmot");
            PreySocialType socialType = bird ? PreySocialType.Flock : herd ? PreySocialType.Herd :
                colony ? PreySocialType.Colony : family ? PreySocialType.Family : PreySocialType.Solitary;
            float hideLimit = HerdsMod.Settings?.maximumInferredHidingBodySize ?? 0.45f;
            PreyRefugePreference inferredRefuge = bodySize <= hideLimit ? InferRefugePreference(race) : PreyRefugePreference.None;
            bool hider = inferredRefuge != PreyRefugePreference.None;
            return new PreyProfile
            {
                race = race,
                eligible = eligible,
                socialType = socialType,
                defenseStrategy = bird ? PreyDefenseStrategy.Scatter : hider ? PreyDefenseStrategy.Hide : PreyDefenseStrategy.Flight,
                refugePreference = inferredRefuge,
                maximumHidingBodySize = hideLimit,
                refugeSearchRadius = 24f,
                hideSuccessChance = HerdsMod.Settings?.defaultHideSuccessChance ?? 0.7f,
                vigilanceChance = bird ? 0.68f : herd ? 0.62f : colony ? 0.58f : family ? 0.55f : (hider ? 0.54f : 0.45f),
                preferredGroupSize = bird ? 18 : herd ? 12 : colony ? 24 : family ? 6 : 1
            };
        }

        public static bool IsBird(ThingDef race)
        {
            string bodyName = race?.race?.body?.defName?.ToLowerInvariant() ?? string.Empty;
            string name = ((race?.defName ?? string.Empty) + " " + (race?.label ?? string.Empty)).ToLowerInvariant();
            return bodyName.Contains("bird") || bodyName.Contains("avian") ||
                ContainsAny(name, "bird", "chicken", "duck", "goose", "swan", "turkey", "emu",
                    "ostrich", "cassowary", "penguin", "eagle", "hawk", "owl", "crow", "raven",
                    "sparrow", "finch", "parrot", "macaw", "peacock", "pheasant", "quail");
        }

        public static bool IsFlightlessBird(ThingDef race)
        {
            string name = ((race?.defName ?? string.Empty) + " " + (race?.label ?? string.Empty)).ToLowerInvariant();
            return ContainsAny(name, "emu", "ostrich", "cassowary", "kiwi", "penguin", "rhea");
        }

        public static bool IsWaterfowl(ThingDef race)
        {
            string name = ((race?.defName ?? string.Empty) + " " + (race?.label ?? string.Empty)).ToLowerInvariant();
            return ContainsAny(name, "duck", "goose", "swan", "loon", "pelican", "gull", "heron", "flamingo");
        }

        private static PreyRefugePreference InferRefugePreference(ThingDef race)
        {
            string name = ((race.defName ?? string.Empty) + " " + race.label).ToLowerInvariant();
            bool bird = IsBird(race);
            bool treeClimber = bird || ContainsAny(name, "squirrel", "monkey", "lemur", "raccoon", "possum", "opossum", "koala", "sloth");
            bool burrower = ContainsAny(name, "hare", "rabbit", "rat", "mouse", "vole", "mole", "gerbil", "hamster", "guinea pig", "chinchilla", "prairie dog");
            if (treeClimber && burrower) return PreyRefugePreference.TreesAndDens;
            if (treeClimber) return PreyRefugePreference.Trees;
            if (burrower) return PreyRefugePreference.Dens;
            return PreyRefugePreference.None;
        }

        private static bool ContainsAny(string value, params string[] terms)
        {
            for (int i = 0; i < terms.Length; i++)
                if (value.Contains(terms[i])) return true;
            return false;
        }
    }
}
