using Verse;
using Verse.AI;

namespace Packs;

public sealed class JobGiver_PackHunt : ThinkNode_JobGiver
{
	protected override Job TryGiveJob(Pawn pawn)
	{
		return pawn?.Map?.GetComponent<PackMapComponent>()?.HuntJobFor(pawn);
	}
}
