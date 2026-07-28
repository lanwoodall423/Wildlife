using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Packs;

[HarmonyPatch(typeof(JobGiver_GetRest), "TryGiveJob")]
[HarmonyAfter(new string[] { "lan.herds" })]
public static class DenRestPatch
{
	public static void Postfix(Pawn pawn, ref Job __result)
	{
		if (pawn?.Spawned != true || __result?.def != JobDefOf.LayDown || __result.targetA.HasThing || !PackMapComponent.IsPackHunter(pawn)) return;
		PackMapComponent component = pawn.Map.GetComponent<PackMapComponent>();
		if (component == null || !component.TryGetDenRestCell(pawn, out IntVec3 cell)) return;
		Job denRest = JobMaker.MakeJob(JobDefOf.LayDown, cell);
		denRest.forceSleep = __result.forceSleep;
		__result = denRest;
		if (WildlifeTestLog.Enabled) WildlifeTestLog.WriteTransition("den-rest:" + pawn.thingIDNumber, "DenRest", "cell=" + cell + " forceSleep=" + denRest.forceSleep, pawn);
	}
}

[HarmonyPatch(typeof(JobGiver_Mate), "TryGiveJob")]
public static class DenMatePatch
{
	public static void Postfix(Pawn pawn, ref Job __result)
	{
		if (pawn?.Spawned != true || !PackMapComponent.IsPackHunter(pawn)) return;
		__result = pawn.Map.GetComponent<PackMapComponent>()?.PreferredMateJobFor(pawn, __result) ?? __result;
	}
}
