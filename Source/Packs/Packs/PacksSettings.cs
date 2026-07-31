using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Packs;

public sealed class PacksSettings : ModSettings
{
	public bool enablePredators = true;
	public bool enablePacks = true;
	private static readonly Dictionary<ThingDef, PredatorSocialStrategy> DefaultStrategyCache = new Dictionary<ThingDef, PredatorSocialStrategy>();

	private static readonly Dictionary<ThingDef, PredatorHuntingStyle> DefaultHuntingStyleCache = new Dictionary<ThingDef, PredatorHuntingStyle>();

	public int updateIntervalTicks = 300;

	public bool enableWildlifeKnowledge = true;
	public bool requireObservationForDetails = true;
	public bool enableBaitInfluence = true;
	public bool enableDeterrentInfluence = true;
	public bool enableReserveInfluence = true;
	public bool enableEcologicalConsequences = true;
	public bool enableWildlifeAlerts = true;
	public bool enableGuardianInfluence = true;
	public bool predatorsAttackColonists = false;
	public bool enablePredatorBoldness = true;
	public bool enableUncertainWarnings = true;
	public bool enableJuvenileLearning = true;
	public bool enableHabitatEcology = true;

	public Dictionary<string, AnimalPackSettings> animalSettings = new Dictionary<string, AnimalPackSettings>();

	public override void ExposeData()
	{
		Scribe_Values.Look(ref enablePredators, "enablePredators", true);
		Scribe_Values.Look(ref enablePacks, "enablePacks", true);
		Scribe_Values.Look(ref updateIntervalTicks, "updateIntervalTicks", 300);
		Scribe_Values.Look(ref enableWildlifeKnowledge, "enableWildlifeKnowledge", true);
		Scribe_Values.Look(ref requireObservationForDetails, "requireObservationForDetails", true);
		Scribe_Values.Look(ref enableBaitInfluence, "enableBaitInfluence", true);
		Scribe_Values.Look(ref enableDeterrentInfluence, "enableDeterrentInfluence", true);
		Scribe_Values.Look(ref enableReserveInfluence, "enableReserveInfluence", true);
		Scribe_Values.Look(ref enableEcologicalConsequences, "enableEcologicalConsequences", true);
		Scribe_Values.Look(ref enableWildlifeAlerts, "enableWildlifeAlerts", true);
		Scribe_Values.Look(ref enableGuardianInfluence, "enableGuardianInfluence", true);
		Scribe_Values.Look(ref predatorsAttackColonists, "predatorsAttackColonists", false);
		Scribe_Values.Look(ref enablePredatorBoldness, "enablePredatorBoldness", true);
		Scribe_Values.Look(ref enableUncertainWarnings, "enableUncertainWarnings", true);
		Scribe_Values.Look(ref enableJuvenileLearning, "enableJuvenileLearning", true);
		Scribe_Values.Look(ref enableHabitatEcology, "enableHabitatEcology", true);
		Scribe_Collections.Look(ref animalSettings, "animalSettings", LookMode.Value, LookMode.Deep);
		if (animalSettings == null)
		{
			animalSettings = new Dictionary<string, AnimalPackSettings>();
		}
		updateIntervalTicks = Mathf.Clamp(updateIntervalTicks, 120, 2000);
	}

	public bool IsEnabled(ThingDef def)
	{
		return enablePredators && StrategyFor(def) != PredatorSocialStrategy.Disabled;
	}

	public static void ClearSpeciesCaches()
	{
		DefaultStrategyCache.Clear();
		DefaultHuntingStyleCache.Clear();
	}

	public PredatorSocialStrategy StrategyFor(ThingDef def)
	{
		if (!enablePredators) return PredatorSocialStrategy.Disabled;
		if (def == null)
		{
			return PredatorSocialStrategy.Disabled;
		}
		if (!animalSettings.TryGetValue(def.defName, out var value))
		{
			PredatorSocialStrategy inferred = DefaultStrategy(def);
			return !enablePacks && inferred != PredatorSocialStrategy.Disabled ? PredatorSocialStrategy.Solitary : inferred;
		}
		if (value.socialStrategy == PredatorSocialStrategy.Disabled && value.enabled)
		{
			value.socialStrategy = PredatorSocialStrategy.Pack;
		}
		value.enabled = value.socialStrategy != PredatorSocialStrategy.Disabled;
		return !enablePacks && value.socialStrategy != PredatorSocialStrategy.Disabled ? PredatorSocialStrategy.Solitary : value.socialStrategy;
	}

	public AnimalPackSettings For(ThingDef def)
	{
		if (!animalSettings.TryGetValue(def.defName, out var value))
		{
			PredatorSocialStrategy predatorSocialStrategy = DefaultStrategy(def);
			value = new AnimalPackSettings
			{
				enabled = (predatorSocialStrategy != PredatorSocialStrategy.Disabled),
				socialStrategy = predatorSocialStrategy,
				huntingStyle = DefaultHuntingStyle(def),
				useDens = !IsFlyingBird(def),
				restAtDen = !IsFlyingBird(def),
				gatherAtDenToMate = !IsFlyingBird(def),
				birdDefaultsApplied = IsFlyingBird(def)
			};
			animalSettings.Add(def.defName, value);
		}
		if (IsFlyingBird(def) && !value.birdDefaultsApplied)
		{
			value.useDens = false;
			value.restAtDen = false;
			value.gatherAtDenToMate = false;
			if (value.huntingStyle == PredatorHuntingStyle.Opportunistic) value.huntingStyle = DefaultHuntingStyle(def);
			value.birdDefaultsApplied = true;
		}
		if (value.socialStrategy == PredatorSocialStrategy.Disabled && value.enabled)
		{
			value.socialStrategy = PredatorSocialStrategy.Pack;
		}
		value.enabled = value.socialStrategy != PredatorSocialStrategy.Disabled;
		return value;
	}

	private static PredatorSocialStrategy DefaultStrategy(ThingDef def)
	{
		if (def == null) return PredatorSocialStrategy.Disabled;
		if (DefaultStrategyCache.TryGetValue(def, out PredatorSocialStrategy cached)) return cached;
		PackHunterExtension extension = def.GetModExtension<PackHunterExtension>();
		if (extension != null)
		{
			DefaultStrategyCache[def] = extension.socialStrategy;
			return extension.socialStrategy;
		}
		RaceProperties race = def.race;
		if (race == null || !HerdsCompatibility.IsPredator(def))
		{
			return DefaultStrategyCache[def] = PredatorSocialStrategy.Disabled;
		}
		string name = ((def.defName ?? string.Empty) + " " + (def.label ?? string.Empty)).ToLowerInvariant();
		PredatorSocialStrategy strategy = ContainsAny(name, "wolf", "warg", "hyena", "wild dog", "orca") ? PredatorSocialStrategy.Pack :
			ContainsAny(name, "lion", "pride") ? PredatorSocialStrategy.Family :
			ContainsAny(name, "fox", "coyote", "jackal") ? PredatorSocialStrategy.Pair : PredatorSocialStrategy.Solitary;
		DefaultStrategyCache[def] = strategy;
		return strategy;
	}

	private static PredatorHuntingStyle DefaultHuntingStyle(ThingDef def)
	{
		if (def == null) return PredatorHuntingStyle.Opportunistic;
		if (DefaultHuntingStyleCache.TryGetValue(def, out PredatorHuntingStyle cached)) return cached;
		PackHunterExtension extension = def.GetModExtension<PackHunterExtension>();
		if (extension != null) return DefaultHuntingStyleCache[def] = extension.huntingStyle;
		string name = ((def.defName ?? string.Empty) + " " + (def.label ?? string.Empty)).ToLowerInvariant();
		PredatorHuntingStyle style = ContainsAny(name, "vulture", "condor", "carrion", "scavenger") ? PredatorHuntingStyle.Scavenger :
			IsFlyingBird(def) ? PredatorHuntingStyle.Ambush :
			ContainsAny(name, "cougar", "lynx", "lion", "tiger", "panther", "leopard", "jaguar", "crocodile", "alligator", "snake", "cobra") ? PredatorHuntingStyle.Ambush :
			ContainsAny(name, "wolf", "warg", "wild dog", "hyena", "cheetah") ? PredatorHuntingStyle.Pursuit :
			ContainsAny(name, "fox", "coyote", "jackal") ? PredatorHuntingStyle.Stalk : PredatorHuntingStyle.Opportunistic;
		DefaultHuntingStyleCache[def] = style;
		return style;
	}

	private static bool IsFlyingBird(ThingDef def)
	{
		string body = def?.race?.body?.defName?.ToLowerInvariant() ?? string.Empty;
		string name = ((def?.defName ?? string.Empty) + " " + (def?.label ?? string.Empty)).ToLowerInvariant();
		bool bird = body.Contains("bird") || body.Contains("avian") ||
			ContainsAny(name, "eagle", "hawk", "owl", "falcon", "vulture", "condor", "raven", "crow");
		return bird && !ContainsAny(name, "emu", "ostrich", "cassowary", "kiwi", "penguin", "rhea");
	}

	private static bool ContainsAny(string value, params string[] terms)
	{
		for (int i = 0; i < terms.Length; i++) if (value.Contains(terms[i])) return true;
		return false;
	}
}
