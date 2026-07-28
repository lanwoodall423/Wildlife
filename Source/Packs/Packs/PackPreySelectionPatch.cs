using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Packs;

[HarmonyPatch(typeof(JobGiver_GetFood), "TryGiveJob")]
public static class PackPreySelectionPatch
{
	public static void Postfix(Pawn pawn, ref Job __result)
	{
		if (__result?.def != JobDefOf.PredatorHunt || !__result.targetA.HasThing || !PackMapComponent.IsPackHunter(pawn))
		{
			return;
		}
		Pawn vanillaChoice = __result.targetA.Thing as Pawn;
		PackMapComponent packMapComponent = pawn.Map?.GetComponent<PackMapComponent>();
		if (packMapComponent != null)
		{
			vanillaChoice = packMapComponent.ChoosePackPrey(pawn, vanillaChoice);
			if (packMapComponent.RegisterHunt(pawn, vanillaChoice) == null)
			{
				__result = null;
			}
			else
			{
				__result = packMapComponent?.HuntJobFor(pawn) ?? __result;
			}
		}
	}
}
