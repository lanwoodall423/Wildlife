using Verse;

namespace Packs;

public sealed class PackHunterExtension : DefModExtension
{
	public PredatorSocialStrategy socialStrategy = PredatorSocialStrategy.Pack;

	public PredatorHuntingStyle huntingStyle = PredatorHuntingStyle.Pursuit;
}
