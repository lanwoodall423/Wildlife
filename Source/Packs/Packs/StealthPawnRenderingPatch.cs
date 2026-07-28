using HarmonyLib;
using Verse;

namespace Packs;

[HarmonyPatch(typeof(PawnRenderer), "GetDrawParms")]
public static class StealthPawnRenderingPatch
{
	public static void Postfix(Pawn ___pawn, ref PawnDrawParms __result)
	{
		if (!__result.Portrait && ___pawn != null && ___pawn.Spawned && PackMapComponent.IsStealthingForRendering(___pawn))
		{
			__result.flags |= PawnRenderFlags.Invisible;
		}
	}
}
