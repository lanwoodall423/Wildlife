using HarmonyLib;
using RimWorld;
using Verse;

namespace Packs;

[HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
public static class PredatorBoldnessKillPatch
{
	public static void Prefix(Pawn __instance, DamageInfo? dinfo)
	{
		if (PacksMod.Settings?.enablePredators != true || PacksMod.Settings.enablePredatorBoldness != true || __instance?.Map == null || dinfo?.Instigator is not Pawn killer) return;
		PackMapComponent component = __instance.Map.GetComponent<PackMapComponent>();
		if (__instance.RaceProps.Humanlike && __instance.Faction == Faction.OfPlayer &&
			HerdsCompatibility.IsPredator(killer.def)) component?.NotifyHumanConflict(killer, true);
		else if (HerdsCompatibility.IsPredator(__instance.def) && killer.RaceProps.Humanlike &&
			killer.Faction == Faction.OfPlayer) component?.NotifyHumanConflict(__instance, false);
	}
}
