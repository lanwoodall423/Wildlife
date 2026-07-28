using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Packs;

public sealed class PackRecord : IExposable
{
	public int id;

	public string name;

	public ThingDef species;

	public Faction faction;

	public Pawn leader;

	public List<Pawn> members = new List<Pawn>();

	public IntVec3 den = IntVec3.Invalid;

	public Building_PredatorDen denMarker;

	public int formedTick;

	public int lastDispersalTick;

	public int separatedUntilTick;

	public int lastLeadershipTick;

	public Corpse claimedCorpse;

	public int corpseClaimUntilTick;

	public int lastMigrationTick;

	public int nextDenSuitabilityCheckTick;

	public int ecologicalStressUntilTick;

	public int leaderLossTick;

	public int handledLeaderLossTick;

	public float humanBoldness = 0.35f;

	public List<int> parentPackIds = new List<int>();

	public List<int> mergedPackIds = new List<int>();

	public string Label
	{
		get
		{
			if (!string.IsNullOrEmpty(name))
			{
				return name;
			}
			return (species?.LabelCap.ToString() ?? "Animal") + " group";
		}
	}

	public void ExposeData()
	{
		Scribe_Values.Look(ref id, "id", 0);
		Scribe_Values.Look(ref name, "name");
		Scribe_Defs.Look(ref species, "species");
		Scribe_References.Look(ref faction, "faction");
		Scribe_References.Look(ref leader, "leader");
		Scribe_Collections.Look(ref members, "members", LookMode.Reference);
		Scribe_Values.Look(ref den, "den", IntVec3.Invalid);
		Scribe_References.Look(ref denMarker, "denMarker");
		Scribe_Values.Look(ref formedTick, "formedTick", 0);
		Scribe_Values.Look(ref lastDispersalTick, "lastDispersalTick", 0);
		Scribe_Values.Look(ref separatedUntilTick, "separatedUntilTick", 0);
		Scribe_Values.Look(ref lastLeadershipTick, "lastLeadershipTick", 0);
		Scribe_References.Look(ref claimedCorpse, "claimedCorpse");
		Scribe_Values.Look(ref corpseClaimUntilTick, "corpseClaimUntilTick", 0);
		Scribe_Values.Look(ref lastMigrationTick, "lastMigrationTick", 0);
		Scribe_Values.Look(ref nextDenSuitabilityCheckTick, "nextDenSuitabilityCheckTick", 0);
		Scribe_Values.Look(ref ecologicalStressUntilTick, "ecologicalStressUntilTick", 0);
		Scribe_Values.Look(ref leaderLossTick, "leaderLossTick", 0);
		Scribe_Values.Look(ref handledLeaderLossTick, "handledLeaderLossTick", 0);
		Scribe_Values.Look(ref humanBoldness, "humanBoldness", 0.35f);
		Scribe_Collections.Look(ref parentPackIds, "parentPackIds", LookMode.Value);
		Scribe_Collections.Look(ref mergedPackIds, "mergedPackIds", LookMode.Value);
		if (members == null)
		{
			members = new List<Pawn>();
		}
		if (parentPackIds == null)
		{
			parentPackIds = new List<int>();
		}
		if (mergedPackIds == null)
		{
			mergedPackIds = new List<int>();
		}
	}
}
