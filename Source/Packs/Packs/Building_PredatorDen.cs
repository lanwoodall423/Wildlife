using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Packs;

public sealed class Building_PredatorDen : Building
{
	public int packId;

	public int abandonedTick;

	public ThingDef formerSpecies;

	public override void ExposeData()
	{
		base.ExposeData();
		Scribe_Values.Look(ref packId, "packId", 0);
		Scribe_Values.Look(ref abandonedTick, "abandonedTick", 0);
		Scribe_Defs.Look(ref formerSpecies, "formerSpecies");
	}

	public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
	{
		Map previousMap = Map;
		IntVec3 previousPosition = Position;
		int previousPackId = packId;
		base.Destroy(mode);
		if (previousPackId > 0) previousMap?.GetComponent<PackMapComponent>()?.NotifyDenDestroyed(previousPackId, previousPosition);
	}

	public override string GetInspectString()
	{
		PackSnapshot pack = Map?.GetComponent<PackMapComponent>()?.PackForDen(this);
		if (pack == null)
		{
			string former = formerSpecies?.LabelCap.ToString() ?? "predator";
			return "Abandoned " + former + " den.\nIt can be reclaimed by a nearby predator group.";
		}
		AnimalPackSettings config = PacksMod.Settings.For(pack.species);
		return pack.Label + "\nSpecies: " + pack.species.LabelCap + "\nMembers: " + pack.members.Count +
			"\nTerritory radius: " + config.territoryRadius.ToString("0") + "\nState: " + (pack.prey == null ? "At large" : "Hunting " + pack.prey.LabelShortCap);
	}

	public override IEnumerable<Gizmo> GetGizmos()
	{
		foreach (Gizmo gizmo in base.GetGizmos()) yield return gizmo;
		PackMapComponent component = Map?.GetComponent<PackMapComponent>();
		PackSnapshot pack = component?.PackForDen(this);
		Pawn leader = pack?.leader;
		if (leader?.Spawned == true)
		{
			yield return new Command_Action
			{
				defaultLabel = "Select pack leader",
				defaultDesc = "Select and jump to the leader of the predator group that owns this den.",
				icon = TexCommand.OpenLinkedQuestTex,
				action = delegate
				{
					Find.Selector.ClearSelection();
					Find.Selector.Select(leader);
					CameraJumper.TryJump(leader);
				}
			};
		}
		if (!Prefs.DevMode) yield break;
		yield return new Command_Action
		{
			defaultLabel = "DEV: Wildlife Overview",
			defaultDesc = "Open the organized wildlife development dashboard.",
			icon = TexCommand.OpenLinkedQuestTex,
			action = PredatorDevGizmoPatch.OpenWildlifeDashboard
		};
		yield return new Command_Toggle
		{
			defaultLabel = "DEV: Complete Overlay",
			defaultDesc = "Toggle all wildlife assessment visuals together.",
			icon = TexCommand.GatherSpotActive,
			isActive = PredatorDevGizmoPatch.CompleteOverlayActive,
			toggleAction = PredatorDevGizmoPatch.ToggleCompleteOverlay
		};
		yield return new Command_Toggle
		{
			defaultLabel = "DEV: Diagnostic Log",
			defaultDesc = "Toggle the shared Herds/Packs diagnostic session.",
			icon = TexCommand.OpenLinkedQuestTex,
			isActive = () => WildlifeTestLog.Enabled,
			toggleAction = delegate
			{
				WildlifeTestLog.Toggle();
				Messages.Message("Wildlife diagnostic logging " + (WildlifeTestLog.Enabled ? "enabled." : "disabled."), MessageTypeDefOf.NeutralEvent, historical: false);
			}
		};
		if (leader?.Spawned == true)
		{
			yield return new Command_Action
			{
				defaultLabel = "DEV: Den Tests...",
				defaultDesc = "Open organized den and pack test controls.",
				icon = TexCommand.SquadAttack,
				action = delegate
				{
					Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>
					{
						new FloatMenuOption("Gather pack at den", delegate
						{
							int count = component.DebugGatherAtDen(leader);
							Messages.Message("Sent " + count + " predator(s) to the den.", MessageTypeDefOf.NeutralEvent, historical: false);
						}),
						new FloatMenuOption("Select pack leader", delegate
						{
							Find.Selector.ClearSelection(); Find.Selector.Select(leader); CameraJumper.TryJump(leader);
						})
					}));
				}
			};
		}
	}
}

[DefOf]
public static class PacksDefOf
{
	public static ThingDef Packs_PredatorDen;

	static PacksDefOf()
	{
		DefOfHelper.EnsureInitializedInCtor(typeof(PacksDefOf));
	}
}
