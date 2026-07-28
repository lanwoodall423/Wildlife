using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace Packs;

public sealed class PacksMod : Mod
{
	public static PacksMod Instance;
	public static PacksSettings Settings;

	public static readonly Harmony Harmony = new Harmony("lan.packsandpredators");

	private static int page;

	private static string search = string.Empty;

	private static string selectedAnimalDef;

	private static Vector2 animalScroll;

	private static Vector2 animalConfigScroll;

	public PacksMod(ModContentPack content)
		: base(content)
	{
		Instance = this;
		Settings = GetSettings<PacksSettings>();
	}

	public override string SettingsCategory()
	{
		return null;
	}

	public override void DoSettingsWindowContents(Rect inRect)
	{
		Rect rect = new Rect(inRect.x, inRect.y, inRect.width, 38f);
		if (Widgets.ButtonText(new Rect(rect.x, rect.y, 150f, 34f), "General"))
		{
			page = 0;
		}
		if (Widgets.ButtonText(new Rect(rect.x + 158f, rect.y, 150f, 34f), "Animals"))
		{
			page = 1;
		}
		if (Widgets.ButtonText(new Rect(rect.x + 316f, rect.y, 170f, 34f), "Player Interaction")) page = 2;
		Rect rect2 = new Rect(inRect.x, inRect.y + 48f, inRect.width, inRect.height - 48f);
		if (page == 0)
		{
			DrawGeneral(rect2);
		}
		else if (page == 1)
		{
			DrawAnimals(rect2);
		}
		else DrawPlayerInteraction(rect2);
	}

	private static void DrawPlayerInteraction(Rect rect)
	{
		Listing_Standard listing = new Listing_Standard();
		listing.Begin(rect);
		Text.Font = GameFont.Medium;
		listing.Label("Player and Predator Interaction");
		Text.Font = GameFont.Small;
		listing.GapLine();
		listing.CheckboxLabeled("Wildlife Knowledge Panel", ref Settings.enableWildlifeKnowledge);
		listing.CheckboxLabeled("Require Observation For Details", ref Settings.requireObservationForDetails);
		listing.CheckboxLabeled("Bait Influences Hunts", ref Settings.enableBaitInfluence);
		listing.CheckboxLabeled("Predator Deterrents", ref Settings.enableDeterrentInfluence);
		listing.CheckboxLabeled("Wildlife Reserves", ref Settings.enableReserveInfluence);
		listing.CheckboxLabeled("Ecological Consequences", ref Settings.enableEcologicalConsequences);
		listing.CheckboxLabeled("Wildlife Alerts", ref Settings.enableWildlifeAlerts);
		listing.CheckboxLabeled("Ranch Guardians Influence Predator Risk", ref Settings.enableGuardianInfluence);
		listing.CheckboxLabeled("Predators May Hunt Colonists", ref Settings.predatorsAttackColonists, "Allows hungry predators to consider colonists as prey. This can make the map substantially more dangerous.");
		listing.CheckboxLabeled("Predators Learn Human Boldness", ref Settings.enablePredatorBoldness);
		listing.CheckboxLabeled("Uncertain Predator Warnings", ref Settings.enableUncertainWarnings);
		listing.Gap();
		listing.Label("Physical wildlife tools are supplied by Herds and Hiders when both mods are active.");
		listing.End();
	}

	private static void DrawGeneral(Rect rect)
	{
		Listing_Standard listing_Standard = new Listing_Standard();
		listing_Standard.Begin(rect);
		Text.Font = GameFont.Medium;
		listing_Standard.Label("Predator Simulation");
		Text.Font = GameFont.Small;
		listing_Standard.GapLine();
		listing_Standard.Label("Predator refresh interval: " + Settings.updateIntervalTicks + " ticks");
		Settings.updateIntervalTicks = Mathf.RoundToInt(listing_Standard.Slider(Settings.updateIntervalTicks, 120f, 2000f));
		listing_Standard.Gap();
		listing_Standard.Label("Configure solitary, paired, family, or pack behavior per species on the Animals page.");
		listing_Standard.Label("Herds and Hiders integration: " + (HerdsCompatibility.Active ? "Active" : "Not loaded"));
		listing_Standard.End();
	}

	public static void DrawAnimals(Rect rect)
	{
		List<ThingDef> source = (from def in DefDatabase<ThingDef>.AllDefsListForReading
			where def.category == ThingCategory.Pawn && (def.race?.Animal ?? false)
			orderby def.LabelCap.ToString()
			select def).ToList();
		float num = Mathf.Clamp(rect.width * 0.34f, 280f, 380f);
		Rect rect2 = new Rect(rect.x, rect.y, num, rect.height);
		Rect rect3 = new Rect(rect2.xMax + 16f, rect.y, rect.width - num - 16f, rect.height);
		Widgets.DrawMenuSection(rect2);
		Widgets.DrawMenuSection(rect3);
		Rect rect4 = rect2.ContractedBy(10f);
		Text.Font = GameFont.Medium;
		Widgets.Label(new Rect(rect4.x, rect4.y, rect4.width, 32f), "Loaded Animals");
		Text.Font = GameFont.Small;
		search = Widgets.TextField(new Rect(rect4.x, rect4.y + 38f, rect4.width, 32f), search ?? string.Empty);
		List<ThingDef> list = source.Where((ThingDef def) => string.IsNullOrEmpty(search) || def.LabelCap.ToString().IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
		Rect outRect = new Rect(rect4.x, rect4.y + 78f, rect4.width, rect4.height - 78f);
		Rect viewRect = new Rect(0f, 0f, outRect.width - 16f, Mathf.Max(outRect.height, (float)list.Count * 32f));
		Widgets.BeginScrollView(outRect, ref animalScroll, viewRect);
		for (int num2 = 0; num2 < list.Count; num2++)
		{
			ThingDef thingDef = list[num2];
			Rect rect5 = new Rect(0f, (float)num2 * 32f, viewRect.width, 29f);
			if (selectedAnimalDef == thingDef.defName)
			{
				Widgets.DrawHighlightSelected(rect5);
			}
			else
			{
				Widgets.DrawHighlightIfMouseover(rect5);
			}
			Widgets.Label(new Rect(8f, rect5.y + 3f, rect5.width - 42f, 24f), thingDef.LabelCap);
			if (Settings.IsEnabled(thingDef))
			{
				Widgets.CheckboxDraw(rect5.xMax - 25f, rect5.y + 5f, active: true, disabled: true);
			}
			if (Widgets.ButtonInvisible(rect5))
			{
				selectedAnimalDef = thingDef.defName;
			}
		}
		Widgets.EndScrollView();
		ThingDef thingDef2 = ((!string.IsNullOrEmpty(selectedAnimalDef)) ? DefDatabase<ThingDef>.GetNamedSilentFail(selectedAnimalDef) : null);
		Rect rect6 = rect3.ContractedBy(16f);
		if (thingDef2 == null)
		{
			Text.Anchor = TextAnchor.MiddleCenter;
			Widgets.Label(rect6, "Select an animal to configure its predator behavior.");
			Text.Anchor = TextAnchor.UpperLeft;
			return;
		}
		AnimalPackSettings config = Settings.For(thingDef2);
		Rect outRect2 = rect6;
		Rect rect7 = new Rect(0f, 0f, outRect2.width - 16f, 860f);
		Widgets.BeginScrollView(outRect2, ref animalConfigScroll, rect7);
		Listing_Standard listing_Standard = new Listing_Standard();
		listing_Standard.Begin(rect7);
		Text.Font = GameFont.Medium;
		listing_Standard.Label(thingDef2.LabelCap);
		Text.Font = GameFont.Small;
		listing_Standard.Label(thingDef2.race.predator ? "Predator" : "Not classified as a predator by RimWorld");
		listing_Standard.GapLine();
		listing_Standard.Label("Social Strategy");
		if (Widgets.ButtonText(listing_Standard.GetRect(34f), config.socialStrategy.ToString()))
		{
			List<FloatMenuOption> list2 = new List<FloatMenuOption>();
			foreach (PredatorSocialStrategy strategy in Enum.GetValues(typeof(PredatorSocialStrategy)))
			{
				list2.Add(new FloatMenuOption(strategy.ToString(), delegate
				{
					config.socialStrategy = strategy;
					config.enabled = strategy != PredatorSocialStrategy.Disabled;
				}));
			}
			Find.WindowStack.Add(new FloatMenu(list2));
		}
		if (config.socialStrategy != PredatorSocialStrategy.Disabled)
		{
			if (!thingDef2.race.predator)
			{
				listing_Standard.Label("This animal can use predator grouping, but vanilla AI will not independently select live prey unless another mod gives it predator behavior.");
			}
			listing_Standard.Gap();
			listing_Standard.Label("Hunting Style");
			if (Widgets.ButtonText(listing_Standard.GetRect(34f), config.huntingStyle.ToString()))
			{
				List<FloatMenuOption> list3 = new List<FloatMenuOption>();
				foreach (PredatorHuntingStyle style in Enum.GetValues(typeof(PredatorHuntingStyle)))
				{
					list3.Add(new FloatMenuOption(style.ToString(), delegate
					{
						config.huntingStyle = style;
					}));
				}
				Find.WindowStack.Add(new FloatMenu(list3));
			}
			if (config.Cooperative)
			{
				if (config.socialStrategy != PredatorSocialStrategy.Pair)
				{
					listing_Standard.Label("Maximum Group Size: " + config.maxPackSize);
					config.maxPackSize = Mathf.RoundToInt(listing_Standard.Slider(config.maxPackSize, 2f, 30f));
				}
				listing_Standard.Label("Group Join Distance: " + config.joinDistance.ToString("0") + " cells");
				config.joinDistance = listing_Standard.Slider(config.joinDistance, 10f, 80f);
				listing_Standard.Label("Maximum Cooperative Hunters: " + config.maximumHunters);
				config.maximumHunters = Mathf.Clamp(Mathf.RoundToInt(listing_Standard.Slider(config.maximumHunters, 1f, config.GroupSizeLimit)), 1, config.GroupSizeLimit);
				listing_Standard.Label("Natural Spawn Group Size: " + config.minimumWildSpawnSize + " - " + config.maximumWildSpawnSize);
				config.minimumWildSpawnSize = Mathf.Clamp(Mathf.RoundToInt(listing_Standard.Slider(config.minimumWildSpawnSize, 2f, config.GroupSizeLimit)), 2, config.GroupSizeLimit);
				config.maximumWildSpawnSize = Mathf.Clamp(Mathf.RoundToInt(listing_Standard.Slider(config.maximumWildSpawnSize, config.minimumWildSpawnSize, config.GroupSizeLimit)), config.minimumWildSpawnSize, config.GroupSizeLimit);
				listing_Standard.Label("Prey Size Bonus Per Additional Hunter: " + config.preySizeBonusPerHunter.ToStringPercent());
				config.preySizeBonusPerHunter = listing_Standard.Slider(config.preySizeBonusPerHunter, 0f, 0.75f);
				listing_Standard.CheckboxLabeled("Allow Automatic Group Merging", ref config.allowPackMerging);
				listing_Standard.CheckboxLabeled("Allow Automatic Group Splitting", ref config.allowPackSplitting);
				listing_Standard.CheckboxLabeled("Coordinate Group Movement", ref config.coordinateMovement);
				listing_Standard.CheckboxLabeled("Juveniles Join Hunts", ref config.juvenilesHunt);
			}
			listing_Standard.Label("Prey Risk Tolerance: " + config.preyRiskTolerance.ToString("0.00") + "x");
			config.preyRiskTolerance = listing_Standard.Slider(config.preyRiskTolerance, 0.5f, 2f);
			listing_Standard.Label("Roaming Target Distance: " + config.roamingDistance.ToString("0") + " cells");
			config.roamingDistance = listing_Standard.Slider(config.roamingDistance, 16f, 60f);
			TooltipHandler.TipRegion(listing_Standard.GetRect(0f), "Distance used for persistent predator movement targets. Targets are biased away from map edges.");
			listing_Standard.Label("Territory Radius: " + config.territoryRadius.ToString("0") + " cells");
			config.territoryRadius = listing_Standard.Slider(config.territoryRadius, 24f, 100f);
			listing_Standard.CheckboxLabeled("Use Dens And Territory", ref config.useDens, "Choose a cached sheltered den and keep ordinary roaming centered on its territory.");
			if (config.useDens)
			{
				listing_Standard.CheckboxLabeled("Sleep Near The Den", ref config.restAtDen, "Ground-sleeping predators spread out around their cached den instead of piling onto its center.");
				listing_Standard.CheckboxLabeled("Gather At The Den To Mate", ref config.gatherAtDenToMate, "Pairs and social groups center their movement on the den when a fertile packmate is ready to mate.");
			}
		}
		listing_Standard.End();
		Widgets.EndScrollView();
	}

	public override void WriteSettings()
	{
		PacksStartup.ApplyWildGroupSizes();
		PacksStartup.RefreshTabs();
		if (Current.Game?.Maps != null)
		{
			for (int i = 0; i < Current.Game.Maps.Count; i++)
			{
				Current.Game.Maps[i].GetComponent<PackMapComponent>()?.ForceRefresh();
			}
		}
		base.WriteSettings();
	}
}
