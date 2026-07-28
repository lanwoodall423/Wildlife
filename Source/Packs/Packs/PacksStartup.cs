using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using UnityEngine;
using Verse;

namespace Packs;

[StaticConstructorOnStartup]
public static class PacksStartup
{
	private static readonly Dictionary<PawnKindDef, IntRange> OriginalWildGroupSizes;

	static PacksStartup()
	{
		OriginalWildGroupSizes = new Dictionary<PawnKindDef, IntRange>();
		LongEventHandler.ExecuteWhenFinished(delegate
		{
			ApplyWildGroupSizes();
			RefreshTabs();
			PacksMod.Harmony.PatchAll(Assembly.GetExecutingAssembly());
		});
	}

	public static void ApplyWildGroupSizes()
	{
		foreach (PawnKindDef item in DefDatabase<PawnKindDef>.AllDefsListForReading)
		{
			ThingDef race = item.race;
			if (race == null || race.race?.Animal != true)
			{
				continue;
			}
			if (!OriginalWildGroupSizes.ContainsKey(item))
			{
				OriginalWildGroupSizes.Add(item, item.wildGroupSize);
			}
			if (!(PacksMod.Settings?.IsEnabled(race) ?? (race.GetModExtension<PackHunterExtension>() != null)))
			{
				item.wildGroupSize = OriginalWildGroupSizes[item];
				continue;
			}
			AnimalPackSettings animalPackSettings = PacksMod.Settings.For(race);
			if (animalPackSettings.socialStrategy == PredatorSocialStrategy.Solitary)
			{
				item.wildGroupSize = OriginalWildGroupSizes[item];
				continue;
			}
			if (animalPackSettings.socialStrategy == PredatorSocialStrategy.Pair)
			{
				item.wildGroupSize = new IntRange(2, 2);
				continue;
			}
			animalPackSettings.minimumWildSpawnSize = Mathf.Clamp(animalPackSettings.minimumWildSpawnSize, 2, animalPackSettings.GroupSizeLimit);
			animalPackSettings.maximumWildSpawnSize = Mathf.Clamp(animalPackSettings.maximumWildSpawnSize, animalPackSettings.minimumWildSpawnSize, animalPackSettings.GroupSizeLimit);
			item.wildGroupSize = new IntRange(animalPackSettings.minimumWildSpawnSize, animalPackSettings.maximumWildSpawnSize);
		}
	}

	public static void RefreshTabs()
	{
		foreach (ThingDef item in DefDatabase<ThingDef>.AllDefsListForReading)
		{
			if (item.category == ThingCategory.Pawn)
			{
				RaceProperties race = item.race;
				if (race != null && race.Animal && (PacksMod.Settings?.IsEnabled(item) ?? (item.GetModExtension<PackHunterExtension>() != null)))
				{
					AddTab(item, typeof(ITab_Pack));
				}
			}
		}
		if (PacksDefOf.Packs_PredatorDen != null) AddTab(PacksDefOf.Packs_PredatorDen, typeof(ITab_Pack));
	}

	private static void AddTab(ThingDef def, Type tabType)
	{
		if (def.inspectorTabs == null)
		{
			def.inspectorTabs = new List<Type>();
		}
		if (!def.inspectorTabs.Contains(tabType))
		{
			def.inspectorTabs.Add(tabType);
		}
		if (def.inspectorTabsResolved != null && !def.inspectorTabsResolved.Any((InspectTabBase tab) => tab.GetType() == tabType))
		{
			def.inspectorTabsResolved.Add(InspectTabManager.GetSharedInstance(tabType));
		}
		ReorderAnimalTabs(def);
	}

	private static void ReorderAnimalTabs(ThingDef def)
	{
		if (def?.race?.Animal != true || def.inspectorTabs == null) return;
		string[] order =
		{
			"RimWorld.ITab_Pawn_Needs",
			"Herds.ITab_AnimalMemory",
			"RimWorld.ITab_Pawn_Health",
			"RimWorld.ITab_Pawn_Social",
			"RimWorld.ITab_Pawn_Training",
			"Herds.ITab_Herd",
			"Packs.ITab_Pack",
			"RimWorld.ITab_Pawn_Log"
		};
		List<Type> original = def.inspectorTabs.Distinct().ToList();
		List<Type> ordered = new List<Type>();
		foreach (string name in order) ordered.AddRange(original.Where(type => type.FullName == name));
		ordered.AddRange(original.Where(type => !ordered.Contains(type)));
		def.inspectorTabs = ordered;
		if (def.inspectorTabsResolved != null)
			def.inspectorTabsResolved = ordered.Select(InspectTabManager.GetSharedInstance).ToList();
	}
}
