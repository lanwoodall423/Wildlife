using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace Packs;

[HarmonyPatch]
[HarmonyBefore(new string[] { "lan.herds" })]
public static class HerdsStealthThreatPatch
{
	private static MethodBase TargetMethod()
	{
		return AccessTools.Method("Herds.HerdMapComponent:NotifyThreat", (Type[])null, (Type[])null);
	}

	private static bool Prepare()
	{
		return TargetMethod() != null;
	}

	public static bool Prefix(Pawn member, Thing threat)
	{
		Pawn pawn = threat as Pawn;
		if (member == null || !member.Spawned || pawn == null || !pawn.Spawned)
		{
			return true;
		}
		return !member.Map.GetComponent<PackMapComponent>().SuppressPreyDetection(pawn, member);
	}
}
