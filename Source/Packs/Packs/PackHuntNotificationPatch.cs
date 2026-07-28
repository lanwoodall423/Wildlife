using HarmonyLib;
using RimWorld;
using Verse;

namespace Packs;

[HarmonyPatch(typeof(JobDriver_PredatorHunt), "CheckWarnPlayerInterval")]
public static class PackHuntNotificationPatch
{
	public static bool Prefix(Pawn ___pawn)
	{
		if (!PackMapComponent.IsPackHunter(___pawn))
		{
			return true;
		}
		return ___pawn?.Map?.GetComponent<PackMapComponent>()?.PackFor(___pawn) == null;
	}
}
