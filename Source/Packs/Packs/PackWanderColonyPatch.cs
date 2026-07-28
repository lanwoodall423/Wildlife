using HarmonyLib;
using RimWorld;
using Verse;

namespace Packs;

[HarmonyPatch(typeof(JobGiver_WanderColony), "GetWanderRoot")]
[HarmonyAfter(new string[] { "lan.herds" })]
public static class PackWanderColonyPatch
{
	public static void Postfix(Pawn pawn, ref IntVec3 __result)
	{
		__result = pawn?.Map?.GetComponent<PackMapComponent>()?.WanderRootFor(pawn, __result) ?? __result;
	}
}
