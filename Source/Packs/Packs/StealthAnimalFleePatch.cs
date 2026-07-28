using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Packs;

[HarmonyPatch(typeof(JobGiver_AnimalFlee), "TryGiveJob")]
[HarmonyAfter(new string[] { "lan.herds" })]
public static class StealthAnimalFleePatch
{
	public static void Postfix(Pawn pawn, ref Job __result)
	{
		if (__result != null && pawn != null && pawn.Spawned)
		{
			PackMapComponent component = pawn.Map.GetComponent<PackMapComponent>();
			Pawn pawn2 = component?.UndetectedHunterFor(pawn);
			if (pawn2 != null && component.SuppressPreyDetection(pawn2, pawn))
			{
				__result = null;
			}
		}
	}
}
