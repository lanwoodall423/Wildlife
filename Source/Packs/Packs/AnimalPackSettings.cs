using UnityEngine;
using Verse;

namespace Packs;

public sealed class AnimalPackSettings : IExposable
{
	public bool enabled;

	public PredatorSocialStrategy socialStrategy;

	public PredatorHuntingStyle huntingStyle;

	public int maxPackSize = 8;

	public float joinDistance = 30f;

	public int maximumHunters = 8;

	public int minimumWildSpawnSize = 3;

	public int maximumWildSpawnSize = 5;

	public float preyRiskTolerance = 1.35f;

	public float preySizeBonusPerHunter = 0.35f;

	public float roamingDistance = 32f;

	public float territoryRadius = 48f;

	public bool useDens = true;

	public bool restAtDen = true;

	public bool gatherAtDenToMate = true;

	public bool allowPackMerging = true;

	public bool allowPackSplitting = true;

	public bool coordinateMovement = true;

	public bool juvenilesHunt;
	public bool birdDefaultsApplied;

	public int GroupSizeLimit
	{
		get
		{
			if (socialStrategy != PredatorSocialStrategy.Solitary)
			{
				if (socialStrategy != PredatorSocialStrategy.Pair)
				{
					return maxPackSize;
				}
				return 2;
			}
			return 1;
		}
	}

	public bool Cooperative
	{
		get
		{
			if (socialStrategy != PredatorSocialStrategy.Pair && socialStrategy != PredatorSocialStrategy.Family)
			{
				return socialStrategy == PredatorSocialStrategy.Pack;
			}
			return true;
		}
	}

	public void ExposeData()
	{
		Scribe_Values.Look(ref enabled, "enabled", defaultValue: false);
		Scribe_Values.Look(ref socialStrategy, "socialStrategy", PredatorSocialStrategy.Disabled);
		Scribe_Values.Look(ref huntingStyle, "huntingStyle", PredatorHuntingStyle.Opportunistic);
		Scribe_Values.Look(ref maxPackSize, "maxPackSize", 8);
		Scribe_Values.Look(ref joinDistance, "joinDistance", 30f);
		Scribe_Values.Look(ref maximumHunters, "maximumHunters", 8);
		Scribe_Values.Look(ref minimumWildSpawnSize, "minimumWildSpawnSize", 3);
		Scribe_Values.Look(ref maximumWildSpawnSize, "maximumWildSpawnSize", 5);
		Scribe_Values.Look(ref preyRiskTolerance, "preyRiskTolerance", 1.35f);
		Scribe_Values.Look(ref preySizeBonusPerHunter, "preySizeBonusPerHunter", 0.35f);
		Scribe_Values.Look(ref roamingDistance, "roamingDistance", 32f);
		Scribe_Values.Look(ref territoryRadius, "territoryRadius", 48f);
		Scribe_Values.Look(ref useDens, "useDens", defaultValue: true);
		Scribe_Values.Look(ref restAtDen, "restAtDen", defaultValue: true);
		Scribe_Values.Look(ref gatherAtDenToMate, "gatherAtDenToMate", defaultValue: true);
		Scribe_Values.Look(ref allowPackMerging, "allowPackMerging", defaultValue: true);
		Scribe_Values.Look(ref allowPackSplitting, "allowPackSplitting", defaultValue: true);
		Scribe_Values.Look(ref coordinateMovement, "coordinateMovement", defaultValue: true);
		Scribe_Values.Look(ref juvenilesHunt, "juvenilesHunt", defaultValue: false);
		Scribe_Values.Look(ref birdDefaultsApplied, "birdDefaultsApplied", defaultValue: false);
		maxPackSize = Mathf.Clamp(maxPackSize, 2, 30);
		joinDistance = Mathf.Clamp(joinDistance, 10f, 80f);
		maximumHunters = Mathf.Clamp(maximumHunters, 1, maxPackSize);
		minimumWildSpawnSize = Mathf.Clamp(minimumWildSpawnSize, 2, maxPackSize);
		maximumWildSpawnSize = Mathf.Clamp(maximumWildSpawnSize, minimumWildSpawnSize, maxPackSize);
		preyRiskTolerance = Mathf.Clamp(preyRiskTolerance, 0.5f, 2f);
		preySizeBonusPerHunter = Mathf.Clamp(preySizeBonusPerHunter, 0f, 0.75f);
		roamingDistance = Mathf.Clamp(roamingDistance, 16f, 60f);
		territoryRadius = Mathf.Clamp(territoryRadius, 24f, 100f);
	}
}
