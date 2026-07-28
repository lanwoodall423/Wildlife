using HarmonyLib;
using Verse;
using Verse.AI;

namespace Packs;

[HarmonyPatch(typeof(JobGiver_WanderAnywhere), "GetWanderRoot")]
public static class PackWanderAnywherePatch
{
	public static void Postfix(Pawn pawn, ref IntVec3 __result)
	{
		__result = pawn?.Map?.GetComponent<PackMapComponent>()?.WanderRootFor(pawn, __result) ?? __result;
	}
}
