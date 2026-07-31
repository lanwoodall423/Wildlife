using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace Packs;

internal static class HerdsCompatibility
{
	private static bool initialized;

	private static Action<Pawn, Thing, int> notifyThreat;

	private static Func<Pawn, bool> isHidden;

	private static Func<Pawn, float> vigilanceFor;

	private static Func<Pawn, float> detectionModifierFor;
	private static Func<Pawn, bool> isBird;

	private static Action<Pawn, Thing> notifyThreatEnded;
	private static Func<Pawn, float> learningFactorFor;
	private static Func<Map, IntVec3, float> habitatScoreAt;
	private static Func<Pawn, Pawn, float> predatorHumanPreyScore;
	private static Action<Pawn, Pawn> notifyPredatorTargetsColonyAnimal;
	private static Action<Pawn, Pawn> notifyPredatorCoordination;
	private static Func<Pawn, string> predatorSignalSummary;
	private static Func<Pawn, string> predatorSignalTooltip;
	private static Func<Pawn, string> ecologicalRoleSummary;
	private static Func<Pawn, string> ecologicalRoleTooltip;
	private static Func<ThingDef, bool> isPredator;
	private static Func<ThingDef, bool> isPrey;
	private static Func<ThingDef, bool> hasPredatorOverride;
	private static Func<ThingDef, bool> hasPreyOverride;
	private static Action<ThingDef, bool, bool> setPredatorOverride;
	private static Action<ThingDef, bool, bool> setPreyOverride;

	public static bool Active
	{
		get
		{
			Initialize();
			if (notifyThreat != null)
			{
				return isHidden != null;
			}
			return false;
		}
	}

	public static void NotifyThreat(Pawn prey, Thing predator)
	{
		Initialize();
		if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("CrossModCall", "NotifyThreat available=" + (notifyThreat != null), predator as Pawn, prey);
		if (prey != null && predator != null && notifyThreat != null)
		{
			notifyThreat(prey, predator, 900);
		}
	}

	public static bool IsHidden(Pawn prey)
	{
		Initialize();
		if (prey != null && isHidden != null)
		{
			return isHidden(prey);
		}
		return false;
	}

	public static float VigilanceFor(Pawn prey)
	{
		Initialize();
		return prey != null && vigilanceFor != null ? vigilanceFor(prey) : 0.5f;
	}

	public static float DetectionModifierFor(Pawn prey)
	{
		Initialize();
		return prey != null && detectionModifierFor != null ? detectionModifierFor(prey) : 0f;
	}

	public static bool IsBird(Pawn animal)
	{
		Initialize();
		return animal != null && isBird != null && isBird(animal);
	}

	public static void NotifyThreatEnded(Pawn prey, Thing predator)
	{
		Initialize();
		if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("CrossModCall", "NotifyThreatEnded available=" + (notifyThreatEnded != null), predator as Pawn, prey);
		if (prey != null && notifyThreatEnded != null) notifyThreatEnded(prey, predator);
	}

	public static float LearningFactorFor(Pawn pawn)
	{
		Initialize();
		return pawn != null && learningFactorFor != null ? learningFactorFor(pawn) : 0f;
	}

	public static float HabitatScoreAt(Map map, IntVec3 cell)
	{
		Initialize();
		return habitatScoreAt != null ? habitatScoreAt(map, cell) : 0.5f;
	}

	public static float PredatorHumanPreyScore(Pawn predator, Pawn human)
	{
		Initialize();
		return predatorHumanPreyScore != null ? predatorHumanPreyScore(predator, human) : 0f;
	}

	public static void NotifyPredatorTargetsColonyAnimal(Pawn predator, Pawn prey)
	{
		Initialize();
		notifyPredatorTargetsColonyAnimal?.Invoke(predator, prey);
	}

	public static void NotifyPredatorCoordination(Pawn predator, Pawn prey)
	{
		Initialize();
		notifyPredatorCoordination?.Invoke(predator, prey);
	}

	public static string PredatorSignalSummary(Pawn predator)
	{
		Initialize();
		return predatorSignalSummary?.Invoke(predator) ?? "No prey calls interpreted";
	}

	public static string PredatorSignalTooltip(Pawn predator)
	{
		Initialize();
		return predatorSignalTooltip?.Invoke(predator) ?? "No local signal information.";
	}

	public static string EcologicalRoleSummary(Pawn animal)
	{
		Initialize();
		return ecologicalRoleSummary?.Invoke(animal) ?? "No persistent feature";
	}

	public static string EcologicalRoleTooltip(Pawn animal)
	{
		Initialize();
		return ecologicalRoleTooltip?.Invoke(animal) ??
			"No ecological landscape role is available.";
	}

	public static bool IsPredator(ThingDef species)
	{
		Initialize();
		return isPredator?.Invoke(species) ?? species?.race?.predator == true;
	}

	public static bool IsPrey(ThingDef species)
	{
		Initialize();
		return isPrey?.Invoke(species) ?? species?.race?.Animal == true &&
			species.race.IsFlesh && !species.race.IsAnomalyEntity && !species.race.predator;
	}

	public static bool HasPredatorOverride(ThingDef species)
	{
		Initialize();
		return hasPredatorOverride?.Invoke(species) == true;
	}

	public static bool HasPreyOverride(ThingDef species)
	{
		Initialize();
		return hasPreyOverride?.Invoke(species) == true;
	}

	public static void SetPredatorOverride(ThingDef species, bool enabled, bool value)
	{
		Initialize();
		setPredatorOverride?.Invoke(species, enabled, value);
	}

	public static void SetPreyOverride(ThingDef species, bool enabled, bool value)
	{
		Initialize();
		setPreyOverride?.Invoke(species, enabled, value);
	}

	private static void Initialize()
	{
		if (initialized)
		{
			return;
		}
		initialized = true;
		try
		{
			Type type = AccessTools.TypeByName("Herds.PreyDefenseAPI");
			MethodInfo methodInfo = AccessTools.Method(type, "NotifyThreat", new Type[3]
			{
				typeof(Pawn),
				typeof(Thing),
				typeof(int)
			}, (Type[])null);
			MethodInfo methodInfo2 = AccessTools.Method(type, "IsHidden", new Type[1] { typeof(Pawn) }, (Type[])null);
			MethodInfo methodInfo3 = AccessTools.Method(type, "VigilanceFor", new Type[1] { typeof(Pawn) }, (Type[])null);
			MethodInfo methodInfo4 = AccessTools.Method(type, "NotifyThreatEnded", new Type[2] { typeof(Pawn), typeof(Thing) }, (Type[])null);
			MethodInfo methodInfo5 = AccessTools.Method(type, "DetectionModifierFor", new Type[1] { typeof(Pawn) }, (Type[])null);
			MethodInfo birdMethod = AccessTools.Method(type, "IsBird", new Type[1] { typeof(Pawn) }, (Type[])null);
			Type learningType = AccessTools.TypeByName("Herds.WildlifeLearningAPI");
			MethodInfo methodInfo6 = AccessTools.Method(learningType, "FactorFor", new Type[1] { typeof(Pawn) }, (Type[])null);
			MethodInfo methodInfo7 = AccessTools.Method(learningType, "HabitatScoreAt", new Type[2] { typeof(Map), typeof(IntVec3) }, (Type[])null);
			Type traditionType = AccessTools.TypeByName("Herds.AnimalTraditionUtility");
			MethodInfo traditionScore = AccessTools.Method(traditionType, "PredatorHumanPreyScore",
				new Type[2] { typeof(Pawn), typeof(Pawn) }, (Type[])null);
			MethodInfo traditionTarget = AccessTools.Method(traditionType, "NotifyPredatorTargetsColonyAnimal",
				new Type[2] { typeof(Pawn), typeof(Pawn) }, (Type[])null);
			Type signalType = AccessTools.TypeByName("Herds.WildlifeSignalCultureAPI");
			MethodInfo signalCoordination = AccessTools.Method(signalType, "NotifyPredatorCoordination",
				new Type[2] { typeof(Pawn), typeof(Pawn) }, (Type[])null);
			MethodInfo signalSummary = AccessTools.Method(signalType, "PredatorSummary",
				new Type[1] { typeof(Pawn) }, (Type[])null);
			MethodInfo signalTooltip = AccessTools.Method(signalType, "PredatorTooltip",
				new Type[1] { typeof(Pawn) }, (Type[])null);
			Type landscapeType = AccessTools.TypeByName("Herds.WildlifeLandscapeAPI");
			MethodInfo landscapeSummary = AccessTools.Method(landscapeType, "RoleSummary",
				new Type[1] { typeof(Pawn) }, (Type[])null);
			MethodInfo landscapeTooltip = AccessTools.Method(landscapeType, "RoleTooltip",
				new Type[1] { typeof(Pawn) }, (Type[])null);
				Type classificationType = AccessTools.TypeByName("Herds.WildlifeSpeciesClassification");
				MethodInfo predatorMethod = AccessTools.Method(classificationType, "IsPredator",
					new Type[1] { typeof(ThingDef) }, (Type[])null);
				MethodInfo preyMethod = AccessTools.Method(classificationType, "IsPrey",
					new Type[1] { typeof(ThingDef) }, (Type[])null);
				MethodInfo hasPredatorMethod = AccessTools.Method(classificationType,
					"HasPredatorOverride", new Type[1] { typeof(ThingDef) }, (Type[])null);
				MethodInfo hasPreyMethod = AccessTools.Method(classificationType,
					"HasPreyOverride", new Type[1] { typeof(ThingDef) }, (Type[])null);
				MethodInfo setPredatorMethod = AccessTools.Method(classificationType,
					"SetPredatorOverride", new Type[3]
					{ typeof(ThingDef), typeof(bool), typeof(bool) }, (Type[])null);
				MethodInfo setPreyMethod = AccessTools.Method(classificationType,
					"SetPreyOverride", new Type[3]
					{ typeof(ThingDef), typeof(bool), typeof(bool) }, (Type[])null);
			if (methodInfo != null)
			{
				notifyThreat = (Action<Pawn, Thing, int>)Delegate.CreateDelegate(typeof(Action<Pawn, Thing, int>), methodInfo);
			}
			if (methodInfo2 != null)
			{
				isHidden = (Func<Pawn, bool>)Delegate.CreateDelegate(typeof(Func<Pawn, bool>), methodInfo2);
			}
			if (methodInfo3 != null) vigilanceFor = (Func<Pawn, float>)Delegate.CreateDelegate(typeof(Func<Pawn, float>), methodInfo3);
			if (methodInfo4 != null) notifyThreatEnded = (Action<Pawn, Thing>)Delegate.CreateDelegate(typeof(Action<Pawn, Thing>), methodInfo4);
			if (methodInfo5 != null) detectionModifierFor = (Func<Pawn, float>)Delegate.CreateDelegate(typeof(Func<Pawn, float>), methodInfo5);
			if (birdMethod != null) isBird = (Func<Pawn, bool>)Delegate.CreateDelegate(typeof(Func<Pawn, bool>), birdMethod);
			if (methodInfo6 != null) learningFactorFor = (Func<Pawn, float>)Delegate.CreateDelegate(typeof(Func<Pawn, float>), methodInfo6);
			if (methodInfo7 != null) habitatScoreAt = (Func<Map, IntVec3, float>)Delegate.CreateDelegate(typeof(Func<Map, IntVec3, float>), methodInfo7);
			if (traditionScore != null) predatorHumanPreyScore =
				(Func<Pawn, Pawn, float>)Delegate.CreateDelegate(typeof(Func<Pawn, Pawn, float>), traditionScore);
			if (traditionTarget != null) notifyPredatorTargetsColonyAnimal =
				(Action<Pawn, Pawn>)Delegate.CreateDelegate(typeof(Action<Pawn, Pawn>), traditionTarget);
			if (signalCoordination != null) notifyPredatorCoordination =
				(Action<Pawn, Pawn>)Delegate.CreateDelegate(typeof(Action<Pawn, Pawn>), signalCoordination);
			if (signalSummary != null) predatorSignalSummary =
				(Func<Pawn, string>)Delegate.CreateDelegate(typeof(Func<Pawn, string>), signalSummary);
			if (signalTooltip != null) predatorSignalTooltip =
				(Func<Pawn, string>)Delegate.CreateDelegate(typeof(Func<Pawn, string>), signalTooltip);
			if (landscapeSummary != null) ecologicalRoleSummary =
				(Func<Pawn, string>)Delegate.CreateDelegate(typeof(Func<Pawn, string>), landscapeSummary);
			if (landscapeTooltip != null) ecologicalRoleTooltip =
				(Func<Pawn, string>)Delegate.CreateDelegate(typeof(Func<Pawn, string>), landscapeTooltip);
				if (predatorMethod != null) isPredator =
					(Func<ThingDef, bool>)Delegate.CreateDelegate(typeof(Func<ThingDef, bool>), predatorMethod);
				if (preyMethod != null) isPrey =
					(Func<ThingDef, bool>)Delegate.CreateDelegate(typeof(Func<ThingDef, bool>), preyMethod);
				if (hasPredatorMethod != null) hasPredatorOverride =
					(Func<ThingDef, bool>)Delegate.CreateDelegate(typeof(Func<ThingDef, bool>), hasPredatorMethod);
				if (hasPreyMethod != null) hasPreyOverride =
					(Func<ThingDef, bool>)Delegate.CreateDelegate(typeof(Func<ThingDef, bool>), hasPreyMethod);
				if (setPredatorMethod != null) setPredatorOverride =
					(Action<ThingDef, bool, bool>)Delegate.CreateDelegate(
						typeof(Action<ThingDef, bool, bool>), setPredatorMethod);
				if (setPreyMethod != null) setPreyOverride =
					(Action<ThingDef, bool, bool>)Delegate.CreateDelegate(
						typeof(Action<ThingDef, bool, bool>), setPreyMethod);
		}
		catch (Exception ex)
		{
			Log.Warning("[Packs and Predators] Herds and Hiders integration could not initialize: " + ex.Message);
			notifyThreat = null;
			isHidden = null;
			vigilanceFor = null;
			notifyThreatEnded = null;
			detectionModifierFor = null;
			isBird = null;
			learningFactorFor = null;
			habitatScoreAt = null;
			predatorHumanPreyScore = null;
			notifyPredatorTargetsColonyAnimal = null;
			notifyPredatorCoordination = null;
			predatorSignalSummary = null;
			predatorSignalTooltip = null;
			ecologicalRoleSummary = null;
			ecologicalRoleTooltip = null;
		}
	}
}
