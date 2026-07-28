using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Packs;

public sealed class PackMapComponent : MapComponent
{
	private struct PreyCandidate
	{
		public Pawn pawn;
		public float score;
	}

	private struct DenCandidate
	{
		public IntVec3 cell;
		public float score;
	}

	private struct GroupKey : IEquatable<GroupKey>
	{
		public ThingDef species;

		public Faction faction;

		public bool Equals(GroupKey other)
		{
			if (species == other.species)
			{
				return faction == other.faction;
			}
			return false;
		}

		public override bool Equals(object obj)
		{
			if (obj is GroupKey other)
			{
				return Equals(other);
			}
			return false;
		}

		public override int GetHashCode()
		{
			return ((species?.shortHash ?? 0) * 397) ^ (faction?.loadID ?? (-1));
		}
	}

	private sealed class TargetMemory
	{
		public IntVec3 target;

		public int nextChangeTick;
	}

	private sealed class HuntMemory
	{
		public Pawn prey;

		public Pawn feeder;

		public int expiresTick;

		public int startedTick;

		public int cooldownUntil;

		public bool notifiedPlayer;

		public bool notifiedPreyDefense;

		public int phaseUntil;

		public int lastDetectionCheckTick;

		public bool detected;

		public bool forcedByDev;

		public HuntPhase phase;
	}

	private sealed class HuntTestSession
	{
		public int packId;
		public ThingDef preySpecies;
		public int requestedRuns;
		public int completedRuns;
		public int kills;
		public int escapes;
		public int hiddenEscapes;
		public int abandoned;
		public int detectionRolls;
		public int detections;
		public int totalDurationTicks;
		public float totalHunterInjury;
		public Pawn currentPrey;
		public int runStartTick;
		public int nextRunTick;
		public float startingHunterHealth;
		public readonly HashSet<int> usedPreyIds = new HashSet<int>();
	}

	private sealed class DangerMemoryRecord : IExposable
	{
		public IntVec3 cell;
		public int expiresTick;
		public string reason;

		public void ExposeData()
		{
			Scribe_Values.Look(ref cell, "cell");
			Scribe_Values.Look(ref expiresTick, "expiresTick", 0);
			Scribe_Values.Look(ref reason, "reason");
		}
	}

	private sealed class BenchmarkSession
	{
		public int startTick;
		public int endTick;
		public int samples;
		public long rebuildMicros;
		public long huntMicros;
		public long peakRebuildMicros;
		public long peakHuntMicros;
		public int stuckJobs;
		public long startingPathRequests;
		public long startingFailedPaths;
		public readonly Dictionary<Pawn, IntVec3> lastCell = new Dictionary<Pawn, IntVec3>();
		public readonly Dictionary<Pawn, int> lastMovedTick = new Dictionary<Pawn, int>();
	}

	private readonly List<PackSnapshot> packs = new List<PackSnapshot>();

	private readonly Dictionary<Pawn, PackSnapshot> packByPawn = new Dictionary<Pawn, PackSnapshot>();

	private readonly Dictionary<Pawn, IntVec3> rootByPawn = new Dictionary<Pawn, IntVec3>();

	private readonly Dictionary<int, TargetMemory> targetMemory = new Dictionary<int, TargetMemory>();

	private readonly Dictionary<int, HuntMemory> huntMemory = new Dictionary<int, HuntMemory>();

	private readonly Dictionary<Pawn, PackRole> roleByPawn = new Dictionary<Pawn, PackRole>();

	// RimWorld 1.6 prepares pawn rendering on worker threads. Rendering must not
	// call Map.GetComponent or touch live simulation dictionaries.
	private sealed class StealthRenderMarker { }

	private static readonly ConditionalWeakTable<Pawn, StealthRenderMarker> StealthRenderingPawns = new ConditionalWeakTable<Pawn, StealthRenderMarker>();

	private HashSet<Pawn> publishedStealthRenderPawns = new HashSet<Pawn>();

	private readonly Dictionary<IntVec2, List<Pawn>> preyBuckets = new Dictionary<IntVec2, List<Pawn>>();

	private readonly Dictionary<Pawn, Pawn> undetectedHunterByPrey = new Dictionary<Pawn, Pawn>();

	private readonly Dictionary<long, int> territoryCooldown = new Dictionary<long, int>();

	private readonly Dictionary<IntVec2, List<PackSnapshot>> territoryBuckets = new Dictionary<IntVec2, List<PackSnapshot>>();

	private readonly List<Thing> observationPosts = new List<Thing>();
	private readonly List<Thing> baitStations = new List<Thing>();
	private readonly List<Thing> predatorDeterrents = new List<Thing>();
	private readonly List<Thing> wildlifeReserves = new List<Thing>();

	private readonly List<Pawn> playerObserversScratch = new List<Pawn>();

	private readonly List<PreyCandidate> preyCandidateScratch = new List<PreyCandidate>(8);

	private readonly List<DenCandidate> denCandidateScratch = new List<DenCandidate>(8);

	private List<PackRecord> records = new List<PackRecord>();

	private List<DangerMemoryRecord> dangerMemories = new List<DangerMemoryRecord>();

	private Dictionary<Pawn, int> observedUntilTick = new Dictionary<Pawn, int>();

	private readonly List<Pawn> ranchGuardians = new List<Pawn>();

	private IntVec3 playerSettlementCenter = IntVec3.Invalid;

	private int nextRecordId = 1;

	private int nextRefreshTick;

	private int nextHuntScanTick;

	private int nextTerritoryTick;

	private int nextFamilyLifecycleTick;

	private int nextMigrationTick;

	private int nextPlayerInfluenceTick;

	private static FieldInfo wildlifeToolActiveField;

	private HuntTestSession huntTest;

	private BenchmarkSession benchmark;

	private long rebuildTotalMicroseconds;

	private long huntScanTotalMicroseconds;

	private int rebuildRuns;

	private int huntScanRuns;

	private long lastRebuildMicroseconds;

	private long lastHuntScanMicroseconds;

	private int pathRequestsSinceRebuild;

	private int territoryInteractions;

	private long totalPathRequests;

	private long failedPathRequests;

	private const int PreyBucketSize = 12;

	private const int MaximumReachabilityCandidates = 8;

	private const int TerritoryBucketSize = 64;

	public PackMapComponent(Map map) : base(map)
	{
	}

	public override void ExposeData()
	{
		base.ExposeData();
		Scribe_Collections.Look(ref records, "packRecords", LookMode.Deep);
		Scribe_Collections.Look(ref dangerMemories, "dangerMemories", LookMode.Deep);
		Scribe_Collections.Look(ref observedUntilTick, "observedUntilTick", LookMode.Reference, LookMode.Value);
		Scribe_Values.Look(ref nextRecordId, "nextPackRecordId", 1);
		if (records == null)
		{
			records = new List<PackRecord>();
		}
		nextRecordId = Mathf.Max(1, nextRecordId);
	}

	public override void FinalizeInit()
	{
		base.FinalizeInit();
		if (!PacksMod.Settings.enablePredators) return;
		Rebuild();
	}

	public override void MapComponentTick()
	{
		if (!PacksMod.Settings.enablePredators) return;
		int ticksGame = Find.TickManager.TicksGame;
		if (ticksGame >= nextRefreshTick)
		{
			Rebuild();
		}
		if (ticksGame >= nextHuntScanTick)
		{
			ScanHunts(ticksGame);
		}
		if (dangerMemories == null) dangerMemories = new List<DangerMemoryRecord>();
		if (observedUntilTick == null) observedUntilTick = new Dictionary<Pawn, int>();
		if (ticksGame >= nextTerritoryTick)
		{
			UpdateTerritories(ticksGame);
		}
		if (ticksGame >= nextMigrationTick)
		{
			UpdateMigrations(ticksGame);
		}
		if (ticksGame >= nextPlayerInfluenceTick) RebuildPlayerInfluences(ticksGame);
		if (huntTest != null && ticksGame % 60 == 0)
		{
			UpdateHuntTest(ticksGame);
		}
		if (benchmark != null && ticksGame % 300 == 0) UpdateBenchmark(ticksGame);
	}

	public override void MapComponentDraw()
	{
		base.MapComponentDraw();
		if (!PacksMod.Settings.enablePredators) return;
		if (Find.CurrentMap != map)
		{
			return;
		}
		PackSnapshot packSnapshot = null;
		if (Find.Selector.SingleSelectedThing is Pawn key) packByPawn.TryGetValue(key, out packSnapshot);
		else if (Find.Selector.SingleSelectedThing is Building_PredatorDen den) packSnapshot = PackForDen(den);
		if (packSnapshot != null && packSnapshot.record?.den.IsValid == true)
		{
			DrawDen(packSnapshot.record.den, packSnapshot.record.den == packSnapshot.movementTarget);
		}
		if (!Prefs.DevMode || !PredatorDevTools.OverlayEnabled)
		{
			return;
		}
		CellRect currentViewRect = Find.CameraDriver.CurrentViewRect;
		for (int i = 0; i < packs.Count; i++)
		{
			PackSnapshot packSnapshot2 = packs[i];
			if (!currentViewRect.Contains(packSnapshot2.center) && !currentViewRect.Contains(packSnapshot2.movementTarget))
			{
				PackRecord record = packSnapshot2.record;
				if (record == null || !record.den.IsValid || !currentViewRect.Contains(packSnapshot2.record.den))
				{
					Pawn prey = packSnapshot2.prey;
					if (prey == null || !prey.Spawned || !currentViewRect.Contains(packSnapshot2.prey.Position))
					{
						continue;
					}
				}
			}
			Pawn prey2 = packSnapshot2.prey;
			HuntMemory value2;
			bool flag = prey2 != null && prey2.Spawned && huntMemory.TryGetValue(packSnapshot2.id, out value2) && (value2.phase == HuntPhase.Stealth || value2.phase == HuntPhase.Positioning);
			Color obj;
			if (!flag)
			{
				Pawn prey3 = packSnapshot2.prey;
				obj = ((prey3 != null && prey3.Spawned) ? Color.red : ((packSnapshot2.members.Count > 1) ? Color.cyan : Color.yellow));
			}
			else
			{
				obj = new Color(1f, 0.55f, 0.1f);
			}
			Color color = obj;
			GenDraw.DrawRadiusRing(packSnapshot2.center, Mathf.Clamp(Mathf.Sqrt(packSnapshot2.members.Count), 0.65f, 5f), color);
			PackRecord record2 = packSnapshot2.record;
			if (record2 != null && record2.den.IsValid)
			{
				DrawDen(packSnapshot2.record.den, movementTarget: false);
				AnimalPackSettings animalPackSettings = PacksMod.Settings.For(packSnapshot2.species);
				if (animalPackSettings.useDens)
				{
					GenDraw.DrawRadiusRing(packSnapshot2.record.den, animalPackSettings.territoryRadius, new Color(1f, 0.55f, 0.1f, 0.7f));
				}
				GenDraw.DrawLineBetween(packSnapshot2.center.ToVector3Shifted(), packSnapshot2.record.den.ToVector3Shifted(), SimpleColor.White);
			}
			Pawn prey4 = packSnapshot2.prey;
			IntVec3 intVec = ((prey4 != null && prey4.Spawned) ? packSnapshot2.prey.Position : packSnapshot2.movementTarget);
			Vector3 a = packSnapshot2.center.ToVector3Shifted();
			Vector3 b = intVec.ToVector3Shifted();
			int color2;
			if (!flag)
			{
				Pawn prey5 = packSnapshot2.prey;
				color2 = ((prey5 != null && prey5.Spawned) ? 1 : 2);
			}
			else
			{
				color2 = 0;
			}
			GenDraw.DrawLineBetween(a, b, (SimpleColor)color2);
			Pawn prey6 = packSnapshot2.prey;
			if (prey6 != null && prey6.Spawned)
			{
				GenDraw.DrawRadiusRing(packSnapshot2.prey.Position, 0.8f, flag ? Color.yellow : Color.red);
			}
			for (int j = 0; j < packSnapshot2.members.Count; j++)
			{
				Pawn pawn = packSnapshot2.members[j];
				if (pawn.Spawned && currentViewRect.Contains(pawn.Position))
				{
					PackRole packRole = RoleFor(pawn);
					Color color3;
					switch (packRole)
					{
					default:
						color3 = Color.cyan;
						break;
					case PackRole.Chaser:
					case PackRole.Flanker:
					case PackRole.Ambusher:
						color3 = Color.magenta;
						break;
					case PackRole.Feeder:
						color3 = Color.red;
						break;
					case PackRole.Leader:
						color3 = Color.yellow;
						break;
					}
					Color color4 = color3;
					GenDraw.DrawRadiusRing(pawn.Position, (pawn == packSnapshot2.leader) ? 0.65f : 0.42f, color4);
					if (flag && packRole != PackRole.Member && packRole != PackRole.Leader && packRole != PackRole.Juvenile)
					{
						IntVec3 center = (PacksMod.Settings.For(packSnapshot2.species).Cooperative ? StagingCell(packSnapshot2, pawn, packRole) : SolitaryStagingCell(packSnapshot2, pawn, PacksMod.Settings.For(packSnapshot2.species).huntingStyle));
						GenDraw.DrawLineBetween(pawn.Position.ToVector3Shifted(), center.ToVector3Shifted(), SimpleColor.White);
						GenDraw.DrawRadiusRing(center, 0.35f, Color.magenta);
					}
				}
			}
		}
	}

	private static void DrawDen(IntVec3 den, bool movementTarget)
	{
		Color color = (movementTarget ? Color.green : new Color(1f, 0.55f, 0.1f));
		GenDraw.DrawRadiusRing(den, 0.85f, color);
		GenDraw.DrawRadiusRing(den, 1.25f, color);
	}

	public static bool IsPackHunter(Pawn pawn)
	{
		if (pawn != null && pawn.RaceProps?.Animal == true && pawn.Faction != Faction.OfPlayer)
		{
			return PacksMod.Settings?.IsEnabled(pawn.def) ?? (pawn.def.GetModExtension<PackHunterExtension>() != null);
		}
		return false;
	}

	public PackSnapshot PackFor(Pawn pawn)
	{
		if (!IsPackHunter(pawn)) return null;
		EnsureCurrent();
		if (pawn == null || !packByPawn.TryGetValue(pawn, out var value))
		{
			return null;
		}
		return value;
	}

	public IntVec3 WanderRootFor(Pawn pawn, IntVec3 fallback)
	{
		if (!IsPackHunter(pawn) || pawn.Downed || pawn.InMentalState || !PacksMod.Settings.For(pawn.def).coordinateMovement)
		{
			return fallback;
		}
		EnsureCurrent();
		if (!rootByPawn.TryGetValue(pawn, out var value) || !value.IsValid)
		{
			return fallback;
		}
		return value;
	}

	public override void MapComponentOnGUI()
	{
		base.MapComponentOnGUI();
		if (!Prefs.DevMode || Find.CurrentMap != map) return;
		if (PredatorDevTools.OverlayEnabled)
		{
			for (int i = 0; i < packs.Count; i++)
			{
				PackSnapshot pack = packs[i]; if (pack.leader?.Spawned != true) continue;
				string phase = HuntPhaseFor(pack.leader).ToString();
				GenMapUI.DrawThingLabel(pack.leader, "pack " + pack.id + " | bold " + (pack.record?.humanBoldness ?? 0.35f).ToString("0.00") + " | " + phase);
			}
		}
		if (!PredatorDevTools.PerformanceOverlayEnabled) return;
		Rect panel = new Rect(12f, 72f, 410f, huntTest == null ? 190f : 214f);
		Widgets.DrawMenuSection(panel);
		Text.Font = GameFont.Small;
		Widgets.Label(panel.ContractedBy(8f), PerformanceSummary());
	}

	public string PerformanceSummary()
	{
		long averageRebuild = rebuildRuns > 0 ? rebuildTotalMicroseconds / rebuildRuns : 0;
		long averageHunt = huntScanRuns > 0 ? huntScanTotalMicroseconds / huntScanRuns : 0;
		int activeHunts = 0;
		foreach (HuntMemory memory in huntMemory.Values) if (memory.prey != null) activeHunts++;
		int abandonedDens = 0;
		if (PacksDefOf.Packs_PredatorDen != null)
		{
			List<Thing> dens = map.listerThings.ThingsOfDef(PacksDefOf.Packs_PredatorDen);
			for (int i = 0; i < dens.Count; i++) if (dens[i] is Building_PredatorDen den && den.packId == 0) abandonedDens++;
		}
		string test = huntTest == null ? "idle" : huntTest.completedRuns + "/" + huntTest.requestedRuns + " (" + (huntTest.currentPrey?.LabelShortCap.ToString() ?? "waiting") + ")";
		string benchmarkState = benchmark == null ? "idle" : Mathf.Max(0, benchmark.endTick - (Find.TickManager?.TicksGame ?? 0)).ToStringTicksToPeriod() + " remaining";
		int claimedCarcasses = 0;
		for (int i = 0; i < records.Count; i++) if (records[i].claimedCorpse?.Spawned == true) claimedCarcasses++;
		return "Packs and Predators performance\n" +
			"Rebuild: " + lastRebuildMicroseconds + " us last / " + averageRebuild + " us avg\n" +
			"Hunt scan: " + lastHuntScanMicroseconds + " us last / " + averageHunt + " us avg\n" +
			"Groups: " + packs.Count + "   Active hunts: " + activeHunts + "   Prey buckets: " + preyBuckets.Count + "\n" +
			"Path checks/rebuild: " + pathRequestsSinceRebuild + "   Territory events: " + territoryInteractions + "   Abandoned dens: " + abandonedDens + "\n" +
			"Memory: " + dangerMemories.Count + " danger zones   Claimed carcasses: " + claimedCarcasses + "\n" +
			"Player tools: " + (observationPosts.Count + baitStations.Count + predatorDeterrents.Count + wildlifeReserves.Count) + "   Documented predators: " + observedUntilTick.Count + "\n" +
			"Automated hunt test: " + test + "   Benchmark: " + benchmarkState;
	}

	public List<string> DebugOverviewLines()
	{
		EnsureCurrent();
		List<string> lines = new List<string> { "PREDATORS packs=" + packs.Count + " records=" + records.Count + " activeHunts=" + huntMemory.Count + " dens=" + (PacksDefOf.Packs_PredatorDen == null ? 0 : map.listerThings.ThingsOfDef(PacksDefOf.Packs_PredatorDen).Count) };
		for (int i = 0; i < packs.Count; i++)
		{
			PackSnapshot pack = packs[i]; HuntPhase phase = HuntPhaseFor(pack.leader);
			lines.Add("PACK " + pack.id + " | " + pack.Label + " | members=" + pack.members.Count + " leader=" + pack.leader?.LabelShortCap + " | style=" + PacksMod.Settings.For(pack.species).huntingStyle + " social=" + PacksMod.Settings.For(pack.species).socialStrategy + " | boldness=" + (pack.record?.humanBoldness ?? 0.35f).ToString("0.00"));
			lines.Add("  den=" + (pack.record?.den.IsValid == true ? pack.record.den.ToString() : "none") + " center=" + pack.center + " target=" + pack.movementTarget + " prey=" + (pack.prey?.LabelShortCap.ToString() ?? "none") + " phase=" + phase);
			if (pack.prey != null) lines.Add("HUNT pack=" + pack.id + " | phase=" + phase + " | prey=" + pack.prey.LabelShortCap + "@" + pack.prey.Position + " | detected=" + (huntMemory.TryGetValue(pack.id, out HuntMemory memory) && memory.detected));
			for (int j = 0; j < pack.members.Count; j++) lines.Add("  MEMBER " + pack.members[j].LabelShortCap + " | role=" + RoleFor(pack.members[j]) + " job=" + (pack.members[j].CurJobDef?.defName ?? "none") + " health=" + pack.members[j].health.summaryHealth.SummaryHealthPercent.ToStringPercent());
		}
		return lines;
	}

	// Reflection-safe, dependency-neutral hook used by Wildlife Moments.
	public List<Pawn> WildlifeMomentHuntPair()
	{
		EnsureCurrent();
		for (int i = 0; i < packs.Count; i++)
		{
			PackSnapshot pack = packs[i];
			if (pack?.leader?.Spawned == true && pack.prey?.Spawned == true &&
				!pack.leader.Dead && !pack.prey.Dead &&
				huntMemory.TryGetValue(pack.id, out HuntMemory memory) &&
				memory.prey == pack.prey)
				return new List<Pawn> { pack.leader, pack.prey };
		}
		return new List<Pawn>();
	}

	public PackSnapshot PackForDen(Building_PredatorDen den)
	{
		if (den == null || den.Map != map) return null;
		EnsureCurrent();
		for (int i = 0; i < packs.Count; i++)
			if (packs[i].id == den.packId) return packs[i];
		return null;
	}

	public void NotifyDenDestroyed(int packId, IntVec3 cell)
	{
		for (int i = 0; i < records.Count; i++)
		{
			PackRecord record = records[i];
			if (record.id != packId) continue;
			record.denMarker = null;
			record.den = IntVec3.Invalid;
			record.nextDenSuitabilityCheckTick = 0;
			if (PacksMod.Settings.enableEcologicalConsequences)
			{
				record.ecologicalStressUntilTick = Find.TickManager.TicksGame + 60000;
				RememberDanger(cell, Find.TickManager.TicksGame + 60000, "den destroyed");
				WildlifeTestLog.Count("ecology.den_destroyed");
			}
			if (PacksMod.Settings.enableWildlifeAlerts) Messages.Message(record.Label + " lost its den. The group may relocate or become more dangerous.", record.leader, MessageTypeDefOf.NegativeEvent, false);
			nextRefreshTick = 0;
			return;
		}
	}

	public bool TryGetDenRestCell(Pawn pawn, out IntVec3 cell)
	{
		cell = IntVec3.Invalid;
		if (pawn?.Spawned != true || pawn.Map != map || pawn.Downed || pawn.InMentalState || !IsPackHunter(pawn)) return false;
		AnimalPackSettings config = PacksMod.Settings.For(pawn.def);
		if (!config.useDens || !config.restAtDen) return false;
		EnsureCurrent();
		if (!packByPawn.TryGetValue(pawn, out PackSnapshot pack) || pack.record?.den.IsValid != true) return false;
		int radialCount = GenRadial.NumCellsInRadius(6.9f);
		int start = Mathf.Abs(Gen.HashCombineInt(pawn.thingIDNumber, pack.record.den.GetHashCode())) % radialCount;
		for (int i = 0; i < radialCount; i++)
		{
			IntVec3 offset = GenRadial.RadialPattern[(start + i) % radialCount];
			if (offset.LengthHorizontalSquared < 4) continue;
			IntVec3 candidate = pack.record.den + offset;
			if (!candidate.InBounds(map) || !candidate.Standable(map) || candidate.IsForbidden(pawn) || candidate.GetTerrain(map).avoidWander || !pawn.CanReserve(candidate)) continue;
			if (!pawn.CanReach(candidate, PathEndMode.OnCell, Danger.Deadly)) continue;
			cell = candidate;
			return true;
		}
		return false;
	}

	public Job PreferredMateJobFor(Pawn pawn, Job fallback)
	{
		if (pawn?.Spawned != true || pawn.gender != Gender.Male || pawn.Sterile() || pawn.RaceProps.disableMating || !IsPackHunter(pawn)) return fallback;
		AnimalPackSettings config = PacksMod.Settings.For(pawn.def);
		if (!config.useDens || !config.gatherAtDenToMate || config.socialStrategy == PredatorSocialStrategy.Solitary) return fallback;
		EnsureCurrent();
		if (!packByPawn.TryGetValue(pawn, out PackSnapshot pack)) return fallback;
		Pawn best = null;
		float bestScore = float.MaxValue;
		for (int i = 0; i < pack.members.Count; i++)
		{
			Pawn candidate = pack.members[i];
			if (candidate == pawn || candidate?.Spawned != true || candidate.Downed || candidate.Faction != pawn.Faction || !candidate.CanCasuallyInteractNow() || candidate.IsForbidden(pawn)) continue;
			if (!PawnUtility.FertileMateTarget(pawn, candidate) || !pawn.Position.InHorDistOf(candidate.Position, 30f) || !pawn.CanReach(candidate, PathEndMode.Touch, Danger.Deadly)) continue;
			float score = pack.record?.den.IsValid == true ? candidate.Position.DistanceToSquared(pack.record.den) : pawn.Position.DistanceToSquared(candidate.Position);
			if (score < bestScore)
			{
				best = candidate;
				bestScore = score;
			}
		}
		if (best == null) return fallback;
		if (WildlifeTestLog.Enabled) WildlifeTestLog.WriteTransition("mate:" + pawn.thingIDNumber, "MateJob", "target=" + best.thingIDNumber + " den=" + (pack.record?.den.ToString() ?? "none"), pawn, best);
		return JobMaker.MakeJob(JobDefOf.Mate, best);
	}

	public Pawn RegisterHunt(Pawn hunter, Pawn proposedPrey)
	{
		if (!IsPackHunter(hunter) || proposedPrey == null || !proposedPrey.Spawned || proposedPrey.Dead)
		{
			return null;
		}
		EnsureCurrent();
		if (!packByPawn.TryGetValue(hunter, out var value))
		{
			return proposedPrey;
		}
		int ticksGame = Find.TickManager.TicksGame;
		if (!huntMemory.TryGetValue(value.id, out var value2))
		{
			huntMemory.Add(value.id, value2 = new HuntMemory());
		}
		if (ticksGame < value2.cooldownUntil)
		{
			if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("HuntRejected", "Pack cooldown active until tick=" + value2.cooldownUntil + ".", hunter, proposedPrey);
			return null;
		}
		bool flag = false;
		if (value2.prey == null || value2.prey.Dead || !value2.prey.Spawned || ticksGame >= value2.expiresTick)
		{
			value2.prey = proposedPrey;
			value2.startedTick = ticksGame;
			value2.notifiedPlayer = false;
			value2.notifiedPreyDefense = false;
			value2.detected = false;
			value2.forcedByDev = false;
			value2.lastDetectionCheckTick = ticksGame;
			value2.phase = HuntPhase.Stealth;
			PacksMod.Settings.For(value.species);
			int num = Mathf.RoundToInt(Mathf.Clamp(hunter.Position.DistanceTo(proposedPrey.Position) * 45f, 600f, 2400f));
			value2.phaseUntil = ticksGame + num;
			flag = true;
		}
		value2.expiresTick = ticksGame + 5000;
		value.prey = value2.prey;
		if (flag)
		{
			AssignHuntRoles(value, value2, hunter);
			UpdateUndetectedHunter(value, value2);
			RefreshStealthRenderSnapshot();
			MobilizePack(value, hunter);
			HerdsCompatibility.NotifyPredatorCoordination(hunter, proposedPrey);
			WildlifeTestLog.Count("hunts.started");
			if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("HuntStarted", "pack=" + value.id + " style=" + PacksMod.Settings.For(value.species).huntingStyle + " social=" + PacksMod.Settings.For(value.species).socialStrategy + " phase=" + value2.phase + " phaseUntil=" + value2.phaseUntil + " hunters=" + string.Join(",", value.members.Where(member => IsActiveHuntRole(RoleFor(member))).Select(member => member.thingIDNumber + ":" + RoleFor(member))), hunter, proposedPrey);
		}
		return value2.prey;
	}

	public PackRole RoleFor(Pawn pawn)
	{
		if (pawn != null && roleByPawn.TryGetValue(pawn, out var value))
		{
			return value;
		}
		PackSnapshot packSnapshot = PackFor(pawn);
		if (packSnapshot == null)
		{
			return PackRole.Member;
		}
		if (!pawn.ageTracker.Adult)
		{
			return PackRole.Juvenile;
		}
		if (pawn != packSnapshot.leader)
		{
			return PackRole.Member;
		}
		return PackRole.Leader;
	}

	public HuntPhase HuntPhaseFor(Pawn pawn)
	{
		if (pawn != null && packByPawn.TryGetValue(pawn, out var value) && huntMemory.TryGetValue(value.id, out var value2))
		{
			return value2.phase;
		}
		return HuntPhase.None;
	}

	public bool IsStealthing(Pawn pawn)
	{
		HuntPhase huntPhase = HuntPhaseFor(pawn);
		if (huntPhase != HuntPhase.Stealth && huntPhase != HuntPhase.Positioning)
		{
			return false;
		}
		return IsActiveHuntRole(RoleFor(pawn));
	}

	public static bool IsStealthingForRendering(Pawn pawn)
	{
		return PacksMod.Settings?.enablePredators == true && pawn != null && StealthRenderingPawns.TryGetValue(pawn, out _);
	}

	private void RefreshStealthRenderSnapshot()
	{
		HashSet<Pawn> snapshot = new HashSet<Pawn>();
		for (int i = 0; i < packs.Count; i++)
		{
			PackSnapshot pack = packs[i];
			if (!huntMemory.TryGetValue(pack.id, out HuntMemory memory) || (memory.phase != HuntPhase.Stealth && memory.phase != HuntPhase.Positioning)) continue;
			for (int j = 0; j < pack.members.Count; j++)
			{
				Pawn member = pack.members[j];
				if (member != null && roleByPawn.TryGetValue(member, out PackRole role) && IsActiveHuntRole(role)) snapshot.Add(member);
			}
		}
		foreach (Pawn pawn in publishedStealthRenderPawns)
			if (!snapshot.Contains(pawn)) StealthRenderingPawns.Remove(pawn);
		foreach (Pawn pawn in snapshot)
			if (!publishedStealthRenderPawns.Contains(pawn)) StealthRenderingPawns.GetValue(pawn, _ => new StealthRenderMarker());
		publishedStealthRenderPawns = snapshot;
	}

	private static bool IsActiveHuntRole(PackRole role)
	{
		return role == PackRole.Feeder || role == PackRole.Chaser || role == PackRole.Flanker || role == PackRole.Ambusher;
	}

	private static bool HasYoung(PackSnapshot pack)
	{
		for (int i = 0; i < pack.members.Count; i++) if (!pack.members[i].ageTracker.Adult) return true;
		return false;
	}

	private static int EffectiveHunterLimit(PackSnapshot pack, AnimalPackSettings config)
	{
		int available = 0;
		for (int i = 0; i < pack.members.Count; i++)
		{
			Pawn member = pack.members[i];
			if (!member.Downed && !member.InMentalState && (member.ageTracker.Adult || config.juvenilesHunt)) available++;
		}
		if (available <= 1 || config.socialStrategy == PredatorSocialStrategy.Solitary) return Mathf.Min(1, available);
		if (config.socialStrategy == PredatorSocialStrategy.Pair) return Mathf.Min(Mathf.Min(2, config.maximumHunters), available);
		if (config.socialStrategy == PredatorSocialStrategy.Family)
		{
			int familyHunters = HasYoung(pack) && !config.juvenilesHunt ? Mathf.Max(1, available - 1) : available;
			return Mathf.Min(Mathf.Min(2, config.maximumHunters), familyHunters);
		}
		return Mathf.Min(config.maximumHunters, available);
	}

	private void AssignHuntRoles(PackSnapshot pack, HuntMemory memory, Pawn initiatingHunter)
	{
		AnimalPackSettings animalPackSettings = PacksMod.Settings.For(pack.species);
		int hunterLimit = EffectiveHunterLimit(pack, animalPackSettings);
		List<Pawn> list = new List<Pawn>();
		if (initiatingHunter != null && !initiatingHunter.Downed && !initiatingHunter.InMentalState && (initiatingHunter.ageTracker.Adult || animalPackSettings.juvenilesHunt))
		{
			list.Add(initiatingHunter);
		}
		for (int i = 0; i < pack.members.Count; i++)
		{
			if (list.Count >= hunterLimit)
			{
				break;
			}
			Pawn pawn = pack.members[i];
			if (pawn != initiatingHunter && !pawn.Downed && !pawn.InMentalState && (pawn.ageTracker.Adult || animalPackSettings.juvenilesHunt))
			{
				list.Add(pawn);
			}
		}
		memory.feeder = null;
		float lowestFood = float.MaxValue;
		for (int i = 0; i < list.Count; i++)
		{
			Pawn member = list[i];
			if (member != initiatingHunter && !IsIdleForMobilization(member)) continue;
			float food = member.needs?.food?.CurLevelPercentage ?? 1f;
			if (memory.feeder == null || food < lowestFood)
			{
				memory.feeder = member;
				lowestFood = food;
			}
		}
		if (memory.feeder == null) memory.feeder = initiatingHunter ?? (list.Count > 0 ? list[0] : null);
		bool familyWithYoung = animalPackSettings.socialStrategy == PredatorSocialStrategy.Family && HasYoung(pack);
		int num = 0;
		for (int num2 = 0; num2 < pack.members.Count; num2++)
		{
			Pawn pawn2 = pack.members[num2];
			if (!list.Contains(pawn2))
			{
				roleByPawn[pawn2] = !pawn2.ageTracker.Adult ? PackRole.Juvenile : familyWithYoung ? PackRole.Guardian : (pawn2 == pack.leader ? PackRole.Leader : PackRole.Member);
				continue;
			}
			if (pawn2 == memory.feeder)
			{
				roleByPawn[pawn2] = PackRole.Feeder;
				continue;
			}
			if (animalPackSettings.socialStrategy == PredatorSocialStrategy.Pair) roleByPawn[pawn2] = PackRole.Flanker;
			else if (animalPackSettings.socialStrategy == PredatorSocialStrategy.Family) roleByPawn[pawn2] = num % 2 == 0 ? PackRole.Chaser : PackRole.Flanker;
			else roleByPawn[pawn2] = num % 4 == 0 ? PackRole.Chaser : num % 4 == 3 ? PackRole.Ambusher : PackRole.Flanker;
			num++;
		}
	}

	private void NotifyPlayerHunt(PackSnapshot pack, HuntMemory memory)
	{
		bool playerPrey = memory.prey?.Faction == Faction.OfPlayer;
		bool observedWildHunt = PacksMod.Settings.enableWildlifeAlerts && IsObserved(pack.leader);
		if (!memory.notifiedPlayer && (playerPrey || observedWildHunt))
		{
			memory.notifiedPlayer = true;
			if (playerPrey) HerdsCompatibility.NotifyPredatorTargetsColonyAnimal(pack.leader, memory.prey);
			string hunterLabel = PacksMod.Settings.For(pack.species).socialStrategy == PredatorSocialStrategy.Solitary ? pack.leader.LabelShortCap : pack.Label;
			if (!playerPrey && PacksMod.Settings.enableUncertainWarnings)
			{
				IntVec3 delta = pack.center - map.Center;
				string direction = Mathf.Abs(delta.x) > Mathf.Abs(delta.z) ? (delta.x > 0 ? "east" : "west") : (delta.z > 0 ? "north" : "south");
				Messages.Message("Field signs suggest " + hunterLabel + " is hunting somewhere to the " + direction + ".", MessageTypeDefOf.NeutralEvent, false);
			}
			else Messages.Message(hunterLabel + " hunting " + memory.prey.LabelShortCap, memory.prey, playerPrey ? MessageTypeDefOf.ThreatBig : MessageTypeDefOf.NeutralEvent);
		}
	}

	public Pawn ChoosePackPrey(Pawn hunter, Pawn vanillaChoice)
	{
		if (vanillaChoice?.RaceProps?.Humanlike == true && !PacksMod.Settings.predatorsAttackColonists) vanillaChoice = null;
		if (!IsPackHunter(hunter))
		{
			return vanillaChoice;
		}
		EnsureCurrent();
		if (!packByPawn.TryGetValue(hunter, out var value))
		{
			return vanillaChoice;
		}
		Pawn prey = value.prey;
		if (prey != null && prey.Spawned && !value.prey.Dead)
		{
			return value.prey;
		}
		int ticksGame = Find.TickManager.TicksGame;
		if (huntMemory.TryGetValue(value.id, out var value2) && ticksGame < value2.cooldownUntil)
		{
			return null;
		}
		AnimalPackSettings animalPackSettings = PacksMod.Settings.For(value.species);
		float hungerUrgency = HungerUrgency(value);
		int hunterLimit = EffectiveHunterLimit(value, animalPackSettings);
		int num = 0;
		float num2 = 0f;
		for (int i = 0; i < value.members.Count; i++)
		{
			if (num >= hunterLimit)
			{
				break;
			}
			Pawn pawn = value.members[i];
			if (!pawn.Downed && !pawn.InMentalState && (pawn.ageTracker.Adult || animalPackSettings.juvenilesHunt))
			{
				num++;
				num2 += EffectiveCombatPower(pawn);
			}
		}
		if (num < 1 || num2 <= 0f)
		{
			return vanillaChoice;
		}
		float maximumBodySize = hunter.RaceProps.maxPreyBodySize * (1f + animalPackSettings.preySizeBonusPerHunter * (float)(num - 1)) * Mathf.Lerp(0.9f, 1.15f, hungerUrgency);
		preyCandidateScratch.Clear();
		IReadOnlyList<Pawn> allPawnsSpawned = map.mapPawns.AllPawnsSpawned;
		for (int j = 0; j < allPawnsSpawned.Count; j++)
		{
			Pawn pawn3 = allPawnsSpawned[j];
			if (!ValidPackPreyFast(hunter, value, pawn3, maximumBodySize, num2 * animalPackSettings.preyRiskTolerance * Mathf.Lerp(0.78f, 1.42f, hungerUrgency)))
			{
				continue;
			}
			bool deterrentProtected = PacksMod.Settings.enableDeterrentInfluence && IsNearPlayerTool(pawn3.Position, predatorDeterrents, 38f);
			bool reserveProtected = PacksMod.Settings.enableReserveInfluence && IsNearPlayerTool(pawn3.Position, wildlifeReserves, 55f);
			if ((deterrentProtected || reserveProtected) && hungerUrgency < 0.82f) continue;
			bool baited = PacksMod.Settings.enableBaitInfluence && IsNearPlayerTool(pawn3.Position, baitStations, 12f);
			float summaryHealthPercent = pawn3.health.summaryHealth.SummaryHealthPercent;
			if (animalPackSettings.huntingStyle == PredatorHuntingStyle.Scavenger && !pawn3.Downed && summaryHealthPercent > 0.55f)
			{
				Pawn_NeedsTracker needs = hunter.needs;
				if (needs == null || needs.food?.CurCategory != HungerCategory.Starving)
				{
					continue;
				}
			}
			float value3 = (pawn3.Downed ? 0f : pawn3.health.capacities.GetLevel(PawnCapacityDefOf.Moving));
			float num4 = (1f - summaryHealthPercent) * 150f + (1f - Mathf.Clamp01(value3)) * 90f + (pawn3.Downed ? 260f : 0f);
			if (!pawn3.ageTracker.Adult)
			{
				num4 += 85f;
			}
			else if (pawn3.RaceProps.lifeExpectancy > 0f && pawn3.ageTracker.AgeBiologicalYearsFloat / pawn3.RaceProps.lifeExpectancy >= 0.8f)
			{
				num4 += 65f;
			}
			int num5 = CountNearbyProtectors(pawn3);
			float num6 = Mathf.Max(0f, 5 - num5) * 22f;
			float num7 = pawn3.BodySize * 90f;
			float num8 = hunter.Position.DistanceTo(pawn3.Position) * Mathf.Lerp(1.6f, 0.8f, hungerUrgency);
			float num9 = EffectiveCombatPower(pawn3) / Mathf.Max(1f, num2) * 25f;
			float num10 = 0f;
			if (animalPackSettings.useDens)
			{
				PackRecord record = value.record;
				if (record != null && record.den.IsValid)
				{
					float num11 = value.record.den.DistanceTo(pawn3.Position);
					if (num11 > animalPackSettings.territoryRadius)
					{
						float num12 = Mathf.Lerp(1f, 0.2f, hungerUrgency);
						num10 = (120f + (num11 - animalPackSettings.territoryRadius) * 2f) * num12;
					}
				}
			}
			float num13 = num7 + num4 + num6 - (float)num5 * 18f - num8 - num9 - num10;
			bool birdPrey = HerdsCompatibility.IsBird(pawn3);
			bool birdHunter = HerdsCompatibility.IsBird(hunter);
			if (birdPrey) num13 += birdHunter ? 70f : -180f;
			if (baited) num13 += 150f;
			if (deterrentProtected) num13 -= 260f;
			if (reserveProtected) num13 -= 180f;
			switch (animalPackSettings.huntingStyle)
			{
			case PredatorHuntingStyle.Opportunistic:
				num13 += num4 * 0.45f + num6 * 0.4f;
				break;
			case PredatorHuntingStyle.Ambush:
				num13 += num6 * 0.8f - num8 * 0.2f;
				break;
			case PredatorHuntingStyle.Stalk:
				num13 += num6 * 0.5f + num4 * 0.2f;
				break;
			case PredatorHuntingStyle.Pursuit:
				num13 += num7 * 0.2f + num8 * 0.45f;
				break;
			case PredatorHuntingStyle.Scavenger:
				if (!pawn3.Downed && summaryHealthPercent > 0.55f)
				{
					Pawn_NeedsTracker needs3 = hunter.needs;
					if (needs3 == null || needs3.food?.CurCategory != HungerCategory.Starving)
					{
						num13 -= 500f;
						break;
					}
				}
				num13 += num4 * 0.6f;
				break;
			}
			if (pawn3.RaceProps.Humanlike)
			{
				num13 -= 35f;
				if (PacksMod.Settings.enablePredatorBoldness) num13 += ((value.record?.humanBoldness ?? 0.35f) - 0.5f) * 240f;
				num13 += HerdsCompatibility.PredatorHumanPreyScore(hunter, pawn3);
			}
			AddPreyCandidate(pawn3, num13);
		}
		for (int i = 0; i < preyCandidateScratch.Count; i++)
		{
			Pawn candidate = preyCandidateScratch[i].pawn;
			pathRequestsSinceRebuild++;
			totalPathRequests++;
			bool reachable = hunter.CanReach(candidate, PathEndMode.ClosestTouch, Danger.Deadly);
			if (!reachable) failedPathRequests++;
			if (reachable)
			{
				if (WildlifeTestLog.Enabled) WildlifeTestLog.WriteTransition("prey-choice:" + value.id, "PreyChoice", "selected=" + candidate.thingIDNumber + " candidates=" + preyCandidateScratch.Count + " hunters=" + num + " hunterPower=" + num2.ToString("0.0") + " maxBody=" + maximumBodySize.ToString("0.00"), hunter, candidate);
				return candidate;
			}
		}
		if (animalPackSettings.huntingStyle == PredatorHuntingStyle.Scavenger)
		{
			Pawn_NeedsTracker needs4 = hunter.needs;
			if (needs4 == null || needs4.food?.CurCategory != HungerCategory.Starving)
			{
				return null;
			}
		}
		if (WildlifeTestLog.Enabled) WildlifeTestLog.WriteTransition("prey-choice:" + value.id, "PreyChoice", "fallback=" + (vanillaChoice?.thingIDNumber.ToString() ?? "none") + " candidates=" + preyCandidateScratch.Count + " hunters=" + num + " hunterPower=" + num2.ToString("0.0"), hunter, vanillaChoice);
		return vanillaChoice;
	}

	private int CountNearbyProtectors(Pawn prey)
	{
		int count = 0;
		IntVec2 origin = BucketFor(prey.Position, PreyBucketSize);
		for (int dx = -1; dx <= 1; dx++)
		{
			for (int dz = -1; dz <= 1; dz++)
			{
				if (!preyBuckets.TryGetValue(new IntVec2(origin.x + dx, origin.z + dz), out List<Pawn> bucket)) continue;
				for (int i = 0; i < bucket.Count; i++)
				{
					Pawn pawn = bucket[i];
					if (pawn != prey && !pawn.Dead && !pawn.Downed && pawn.def == prey.def && pawn.ageTracker.Adult && pawn.Position.DistanceToSquared(prey.Position) <= 100) count++;
				}
			}
		}
		if (PacksMod.Settings.enableGuardianInfluence)
		{
			for (int i = 0; i < ranchGuardians.Count; i++)
			{
				Pawn guardian = ranchGuardians[i];
				if (guardian?.Spawned == true && !guardian.Dead && !guardian.Downed && guardian.Position.DistanceToSquared(prey.Position) <= 225) count += 3;
			}
		}
		return count;
	}

	private void AddPreyCandidate(Pawn pawn, float score)
	{
		int index = 0;
		while (index < preyCandidateScratch.Count && preyCandidateScratch[index].score >= score) index++;
		if (index >= MaximumReachabilityCandidates) return;
		preyCandidateScratch.Insert(index, new PreyCandidate { pawn = pawn, score = score });
		if (preyCandidateScratch.Count > MaximumReachabilityCandidates) preyCandidateScratch.RemoveAt(preyCandidateScratch.Count - 1);
	}

	private bool ValidPackPreyFast(Pawn hunter, PackSnapshot pack, Pawn prey, float maximumBodySize, float maximumPower)
	{
		if (prey == null || prey == hunter || prey.Dead || !prey.Spawned || prey.def == pack.species)
		{
			return false;
		}
		if (!prey.RaceProps.canBePredatorPrey || !prey.RaceProps.IsFlesh || prey.BodySize > maximumBodySize)
		{
			return false;
		}
		if (prey.RaceProps.Humanlike && !PacksMod.Settings.predatorsAttackColonists)
		{
			return false;
		}
		if (!prey.Downed && EffectiveCombatPower(prey) > maximumPower)
		{
			return false;
		}
		if (hunter.Faction != null && prey.Faction != null && !hunter.HostileTo(prey))
		{
			return false;
		}
		if (hunter.Faction != null && prey.HostFaction != null && !hunter.HostileTo(prey))
		{
			return false;
		}
		if (hunter.Faction == Faction.OfPlayer && prey.Faction == Faction.OfPlayer)
		{
			return false;
		}
		if (prey.IsHiddenFromPlayer() || prey.IsPsychologicallyInvisible() || prey.IsForbidden(hunter))
		{
			return false;
		}
		if (ModsConfig.AnomalyActive && prey.IsMutant && !prey.mutant.Def.canBleed)
		{
			return false;
		}
		if (TutorSystem.TutorialMode && prey.Faction == Faction.OfPlayer)
		{
			return false;
		}
		if (hunter.GetDistrict() != prey.GetDistrict())
		{
			return false;
		}
		return true;
	}

	private static float EffectiveCombatPower(Pawn pawn)
	{
		if (pawn == null || pawn.Dead || pawn.Downed)
		{
			return 0f;
		}
		float learning = PacksMod.Settings.enableJuvenileLearning ? HerdsCompatibility.LearningFactorFor(pawn) : 0f;
		float experience = !PacksMod.Settings.enableJuvenileLearning ? 1f : pawn.ageTracker?.Adult == false ? 0.75f + learning * 0.35f : 1f + learning * 0.12f;
		return pawn.kindDef.combatPower * pawn.health.summaryHealth.SummaryHealthPercent * pawn.BodySize * experience;
	}

	private void MobilizePack(PackSnapshot pack, Pawn initiatingHunter)
	{
		AnimalPackSettings animalPackSettings = PacksMod.Settings.For(pack.species);
		int hunterLimit = EffectiveHunterLimit(pack, animalPackSettings);
		int num = ((initiatingHunter != null) ? 1 : 0);
		for (int i = 0; i < pack.members.Count; i++)
		{
			if (num >= hunterLimit)
			{
				break;
			}
			Pawn pawn = pack.members[i];
			if (pawn != initiatingHunter && !pawn.Downed && !pawn.InMentalState && (pawn.ageTracker.Adult || animalPackSettings.juvenilesHunt) && !PawnUtility.PlayerForcedJobNowOrSoon(pawn) && pawn.GetLord() == null && IsIdleForMobilization(pawn))
			{
				Job job = HuntJobFor(pawn);
				if (job != null)
				{
					pawn.jobs.StartJob(job, JobCondition.InterruptForced);
					num++;
				}
			}
		}
	}

	private static bool IsIdleForMobilization(Pawn pawn)
	{
		JobDef curJobDef = pawn.CurJobDef;
		if (curJobDef != null && curJobDef != JobDefOf.GotoWander && curJobDef != JobDefOf.Wait_Wander)
		{
			return curJobDef == JobDefOf.Wait;
		}
		return true;
	}

	public Job HuntJobFor(Pawn pawn)
	{
		if (!IsPackHunter(pawn) || pawn.Downed || pawn.InMentalState)
		{
			return null;
		}
		AnimalPackSettings animalPackSettings = PacksMod.Settings.For(pawn.def);
		if (!pawn.ageTracker.Adult && !animalPackSettings.juvenilesHunt)
		{
			return null;
		}
		EnsureCurrent();
		if (packByPawn.TryGetValue(pawn, out var value))
		{
			Pawn prey = value.prey;
			if (prey != null && prey.Spawned && !value.prey.Dead)
			{
				PackRole packRole = RoleFor(pawn);
				if (!IsActiveHuntRole(packRole))
				{
					return null;
				}
				if (!huntMemory.TryGetValue(value.id, out var value2))
				{
					return null;
				}
				int ticksGame = Find.TickManager.TicksGame;
				if (value2.phase == HuntPhase.Stealth)
				{
					CheckStealthDetection(value, pawn, value2, animalPackSettings);
					if (value2.phase == HuntPhase.Stealth)
					{
						IntVec3 intVec = StealthApproachCell(value, pawn, animalPackSettings.huntingStyle);
						if (ticksGame < value2.phaseUntil && intVec.IsValid && !pawn.Position.InHorDistOf(intVec, 2f))
						{
							return StealthMoveJob(intVec, animalPackSettings.huntingStyle);
						}
						BeginPositioning(value, value2, animalPackSettings, ticksGame);
					}
				}
				if (value2.phase == HuntPhase.Positioning)
				{
					CheckStealthDetection(value, pawn, value2, animalPackSettings);
					if (value2.phase == HuntPhase.Positioning)
					{
						IntVec3 intVec2 = (animalPackSettings.Cooperative ? StagingCell(value, pawn, packRole) : SolitaryStagingCell(value, pawn, animalPackSettings.huntingStyle));
						if (ticksGame < value2.phaseUntil && intVec2.IsValid && !pawn.Position.InHorDistOf(intVec2, 2f))
						{
							return StealthMoveJob(intVec2, animalPackSettings.huntingStyle);
						}
						if (ticksGame < value2.phaseUntil && animalPackSettings.Cooperative && !AllHuntersPositioned(value))
						{
							Job job = JobMaker.MakeJob(JobDefOf.Wait);
							job.expiryInterval = 90;
							job.checkOverrideOnExpire = true;
							return job;
						}
						BeginChase(value, value2);
					}
				}
				if (value2.phase != HuntPhase.Chase)
				{
					BeginChase(value, value2);
				}
				if (packRole == PackRole.Feeder)
				{
					Job job2 = JobMaker.MakeJob(JobDefOf.PredatorHunt, value.prey);
					job2.killIncappedTarget = true;
					return job2;
				}
				Job job3 = JobMaker.MakeJob(JobDefOf.AttackMelee, value.prey);
				job3.expiryInterval = 300;
				job3.checkOverrideOnExpire = true;
				return job3;
			}
		}
		return null;
	}

	private Job StealthMoveJob(IntVec3 destination, PredatorHuntingStyle style)
	{
		Job job = JobMaker.MakeJob(JobDefOf.Goto, destination);
		job.locomotionUrgency = ((style == PredatorHuntingStyle.Pursuit) ? LocomotionUrgency.Jog : LocomotionUrgency.Walk);
		job.expiryInterval = 180;
		job.checkOverrideOnExpire = true;
		return job;
	}

	private void BeginPositioning(PackSnapshot pack, HuntMemory memory, AnimalPackSettings config, int now)
	{
		HuntPhase previous = memory.phase;
		memory.phase = HuntPhase.Positioning;
		memory.phaseUntil = now + ((config.huntingStyle == PredatorHuntingStyle.Stalk) ? 720 : ((config.huntingStyle == PredatorHuntingStyle.Ambush) ? 600 : (config.Cooperative ? 480 : 300)));
		RefreshStealthRenderSnapshot();
		if (WildlifeTestLog.Enabled && previous != memory.phase) WildlifeTestLog.Write("HuntPhase", "pack=" + pack.id + " " + previous + "->" + memory.phase + " until=" + memory.phaseUntil, pack.leader, memory.prey);
	}

	private void BeginChase(PackSnapshot pack, HuntMemory memory)
	{
		HuntPhase previous = memory.phase;
		memory.phase = HuntPhase.Chase;
		memory.detected = true;
		if (memory.prey != null) undetectedHunterByPrey.Remove(memory.prey);
		RefreshStealthRenderSnapshot();
		NotifyActualHunt(pack);
		if (WildlifeTestLog.Enabled && previous != memory.phase) WildlifeTestLog.Write("HuntPhase", "pack=" + pack.id + " " + previous + "->Chase detected=true", pack.leader, memory.prey);
	}

	private void CheckStealthDetection(PackSnapshot pack, Pawn predator, HuntMemory memory, AnimalPackSettings config)
	{
		int ticksGame = Find.TickManager.TicksGame;
		float num = predator.Position.DistanceTo(pack.prey.Position);
		if (!(num > 8f) && ticksGame - memory.lastDetectionCheckTick >= 120)
		{
			memory.lastDetectionCheckTick = ticksGame;
			float num2 = 0.14f + Mathf.Clamp01(predator.BodySize / Mathf.Max(0.2f, pack.prey.BodySize)) * 0.08f;
			num2 += Mathf.InverseLerp(8f, 3f, num) * 0.18f;
			num2 += (float)CountNearbyProtectors(pack.prey) * 0.025f;
			float vigilance = HerdsCompatibility.VigilanceFor(pack.prey);
			num2 += (vigilance - 0.5f) * 0.34f;
			num2 += HerdsCompatibility.DetectionModifierFor(pack.prey);
			float environmentalModifier = EnvironmentalDetectionModifier(predator, pack.prey, out string environment);
			num2 += environmentalModifier;
			num2 += ((config.huntingStyle == PredatorHuntingStyle.Stalk) ? (-0.1f) : ((config.huntingStyle == PredatorHuntingStyle.Ambush) ? (-0.07f) : 0f));
			if (predator.Position.Roofed(map))
			{
				num2 -= 0.04f;
			}
			num2 = Mathf.Clamp(num2, 0.03f, 0.65f);
			TestRollMode overrideMode = Prefs.DevMode ? WildlifeTestLog.DetectionOutcome : TestRollMode.Natural;
			bool detected = overrideMode == TestRollMode.ForceSuccess || (overrideMode == TestRollMode.Natural && Rand.Chance(num2));
			if (huntTest != null && huntTest.packId == pack.id && huntTest.currentPrey == pack.prey)
			{
				huntTest.detectionRolls++;
				if (detected) huntTest.detections++;
			}
			WildlifeTestLog.Count("detection.rolls");
			WildlifeTestLog.Count(detected ? "detection.detected" : "detection.undetected");
			if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("DetectionRoll", "pack=" + pack.id + " phase=" + memory.phase + " distance=" + num.ToString("0.0") + " chance=" + num2.ToString("0.000") + " vigilance=" + vigilance.ToString("0.000") + " environment=" + environment + " envMod=" + environmentalModifier.ToString("+0.000;-0.000;0.000") + " override=" + overrideMode + " result=" + (detected ? "detected" : "undetected"), predator, pack.prey);
			if (detected)
			{
				BeginChase(pack, memory);
			}
		}
	}

	public bool SuppressPreyDetection(Pawn predator, Pawn observer)
	{
		if (PacksMod.Settings?.enablePredators != true) return false;
		if (predator == null || !predator.Spawned || observer == null || !observer.Spawned)
		{
			return false;
		}
		EnsureCurrent();
		if (!packByPawn.TryGetValue(predator, out var value) || !huntMemory.TryGetValue(value.id, out var value2) || value2.prey != observer || value2.detected || (value2.phase != HuntPhase.Stealth && value2.phase != HuntPhase.Positioning))
		{
			return false;
		}
		if (predator.Position.InHorDistOf(observer.Position, 3.5f))
		{
			BeginChase(value, value2);
			return false;
		}
		if (WildlifeTestLog.Enabled) WildlifeTestLog.WriteTransition("stealth-suppress:" + predator.thingIDNumber + ":" + observer.thingIDNumber, "Stealth", "prey-detection-suppressed phase=" + value2.phase, predator, observer);
		return true;
	}

	public Pawn UndetectedHunterFor(Pawn observer)
	{
		if (observer == null || !observer.Spawned)
		{
			return null;
		}
		EnsureCurrent();
		return undetectedHunterByPrey.TryGetValue(observer, out Pawn hunter) && hunter?.Spawned == true ? hunter : null;
	}

	private float EnvironmentalDetectionModifier(Pawn predator, Pawn prey, out string description)
	{
		float rain = map.weatherManager?.RainRate ?? 0f;
		float glow = map.skyManager?.CurSkyGlow ?? 0.5f;
		int cover = 0;
		for (int i = 0; i < 9; i++)
		{
			IntVec3 cell = predator.Position + GenAdj.AdjacentCellsAndInside[i];
			if (!cell.InBounds(map)) continue;
			List<Thing> things = cell.GetThingList(map);
			for (int j = 0; j < things.Count; j++)
			{
				if (things[j] is Plant plant && plant.Growth > 0.35f) { cover++; break; }
				if (things[j].def.Fillage == FillCategory.Full) { cover += 2; break; }
			}
		}
		float angle = PositiveMod(map.uniqueID * 47 + Find.TickManager.TicksGame / 2500 * 29, 360) * Mathf.Deg2Rad;
		Vector2 wind = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
		Vector2 predatorToPrey = new Vector2(prey.Position.x - predator.Position.x, prey.Position.z - predator.Position.z).normalized;
		float scent = Vector2.Dot(wind, predatorToPrey) * 0.09f;
		float modifier = scent - rain * 0.12f + (glow - 0.5f) * 0.12f - Mathf.Min(0.14f, cover * 0.018f);
		description = "wind=" + scent.ToString("+0.00;-0.00;0.00") + ",rain=" + rain.ToString("0.00") + ",light=" + glow.ToString("0.00") + ",cover=" + cover;
		return modifier;
	}

	private void UpdateUndetectedHunter(PackSnapshot pack, HuntMemory memory)
	{
		if (pack == null || memory?.prey?.Spawned != true || memory.detected || (memory.phase != HuntPhase.Stealth && memory.phase != HuntPhase.Positioning)) return;
		Pawn hunter = memory.feeder?.Spawned == true ? memory.feeder : pack.leader?.Spawned == true ? pack.leader : null;
		if (hunter != null) undetectedHunterByPrey[memory.prey] = hunter;
	}

	private bool AllHuntersPositioned(PackSnapshot pack)
	{
		for (int i = 0; i < pack.members.Count; i++)
		{
			Pawn pawn = pack.members[i];
			PackRole packRole = RoleFor(pawn);
			if (IsActiveHuntRole(packRole) && !pawn.Downed && !pawn.InMentalState)
			{
				IntVec3 otherLoc = StagingCell(pack, pawn, packRole);
				if (otherLoc.IsValid && !pawn.Position.InHorDistOf(otherLoc, 2.5f))
				{
					return false;
				}
			}
		}
		return true;
	}

	private void NotifyActualHunt(PackSnapshot pack)
	{
		if (pack != null && huntMemory.TryGetValue(pack.id, out var value) && value.prey == pack.prey)
		{
			NotifyPlayerHunt(pack, value);
			if (!value.notifiedPreyDefense)
			{
				value.notifiedPreyDefense = true;
				if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("CrossModThreat", "Notifying Herds and Hiders that chase is active; integration=" + HerdsCompatibility.Active, pack.leader, value.prey);
				HerdsCompatibility.NotifyThreat(value.prey, pack.leader ?? pack.members.FirstOrDefault());
			}
		}
	}

	public bool DebugForceHunt(Pawn hunter, Pawn prey)
	{
		if (hunter == null || prey == null || hunter == prey || !hunter.Spawned || !prey.Spawned || prey.Dead)
		{
			return false;
		}
		EnsureCurrent();
		if (!packByPawn.TryGetValue(hunter, out var value))
		{
			return false;
		}
		if (!huntMemory.TryGetValue(value.id, out var value2))
		{
			huntMemory.Add(value.id, value2 = new HuntMemory());
		}
		value2.prey = null;
		value2.cooldownUntil = 0;
		value2.expiresTick = 0;
		if (RegisterHunt(hunter, prey) == null) return false;
		value2.forcedByDev = true;
		value2.expiresTick = int.MaxValue;
		Job job = HuntJobFor(hunter);
		if (job == null)
		{
			if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("DevHunt", "Forced hunt failed because no hunt job was produced.", hunter, prey);
			return false;
		}
		hunter.jobs.StartJob(job, JobCondition.InterruptForced);
		if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("DevHunt", "Forced hunt job started: " + job.def.defName + "; ordinary distance/risk/timeout abandonment disabled.", hunter, prey);
		return true;
	}

	public void DebugClearHunt(Pawn pawn)
	{
		EnsureCurrent();
		if (packByPawn.TryGetValue(pawn, out var value))
		{
			if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("DevHunt", "Clear requested for pack=" + value.id, pawn, value.prey);
			if (huntMemory.TryGetValue(value.id, out var value2))
			{
				value2.cooldownUntil = 0;
			}
			AbandonHunt(value, Find.TickManager.TicksGame);
			huntMemory.Remove(value.id);
		}
	}

	public bool DebugSetDen(Pawn pawn, IntVec3 cell)
	{
		EnsureCurrent();
		Building edifice = cell.InBounds(map) ? cell.GetEdifice(map) : null;
		if (pawn?.Spawned == true && cell.InBounds(map) && cell.Standable(map) && pawn.CanReach(cell, PathEndMode.OnCell, Danger.Deadly) &&
			packByPawn.TryGetValue(pawn, out var value) && value.record != null)
		{
			if (edifice != null && edifice != value.record.denMarker) return false;
			value.record.den = cell;
			EnsureDenMarker(value.record, PacksMod.Settings.For(value.species));
			if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("DevDen", "Den set to " + cell + " for pack=" + value.id, pawn);
			return true;
		}
		return false;
	}

	public bool DebugSetMovementTarget(Pawn pawn, IntVec3 cell)
	{
		EnsureCurrent();
		if (pawn?.Spawned == true && cell.InBounds(map) && cell.Standable(map) && pawn.CanReach(cell, PathEndMode.OnCell, Danger.Deadly) && packByPawn.TryGetValue(pawn, out var value))
		{
			if (!targetMemory.TryGetValue(value.id, out var value2))
			{
				targetMemory.Add(value.id, value2 = new TargetMemory());
			}
			value2.target = ClosestStandable(cell, pawn.Position);
			value2.nextChangeTick = Find.TickManager.TicksGame + 2500;
			value.movementTarget = value2.target;
			BuildRoots(value);
			if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("DevMovement", "Movement target set to " + value2.target + " for pack=" + value.id, pawn);
			return true;
		}
		return false;
	}

	public bool DebugSendToDen(Pawn pawn, bool sleep)
	{
		if (!TryGetDebugDenCell(pawn, 0, out IntVec3 cell))
		{
			if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("DevDenJob", "No reachable den cell; sleep=" + sleep, pawn);
			return false;
		}
		Job job = JobMaker.MakeJob(sleep ? JobDefOf.LayDown : JobDefOf.Goto, cell);
		if (sleep) job.forceSleep = true;
		else
		{
			job.expiryInterval = 1200;
			job.checkOverrideOnExpire = true;
			job.locomotionUrgency = LocomotionUrgency.Jog;
		}
		pawn.jobs.StartJob(job, JobCondition.InterruptForced);
		if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("DevDenJob", "Started " + job.def.defName + " to " + cell + " sleep=" + sleep, pawn);
		return true;
	}

	public int DebugGatherAtDen(Pawn pawn)
	{
		EnsureCurrent();
		if (pawn?.Spawned != true || !packByPawn.TryGetValue(pawn, out PackSnapshot pack) || pack.record?.den.IsValid != true) return 0;
		int started = 0;
		for (int i = 0; i < pack.members.Count; i++)
		{
			Pawn member = pack.members[i];
			if (member?.Spawned != true || member.Downed || member.InMentalState || !TryGetDebugDenCell(member, i, out IntVec3 cell)) continue;
			Job job = JobMaker.MakeJob(JobDefOf.Goto, cell);
			job.expiryInterval = 1200;
			job.checkOverrideOnExpire = true;
			job.locomotionUrgency = LocomotionUrgency.Jog;
			member.jobs.StartJob(job, JobCondition.InterruptForced);
			started++;
		}
		if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("DevDenGather", "pack=" + pack.id + " started=" + started + "/" + pack.members.Count + " den=" + pack.record.den, pawn);
		return started;
	}

	public bool DebugForceHuntPhase(Pawn pawn, HuntPhase phase)
	{
		if (phase == HuntPhase.None) return false;
		EnsureCurrent();
		if (pawn?.Spawned != true || !packByPawn.TryGetValue(pawn, out PackSnapshot pack) || !huntMemory.TryGetValue(pack.id, out HuntMemory memory) || memory.prey?.Spawned != true) return false;
		int now = Find.TickManager.TicksGame;
		memory.phase = phase;
		memory.phaseUntil = now + 1200;
		memory.expiresTick = Mathf.Max(memory.expiresTick, now + 3000);
		memory.lastDetectionCheckTick = now;
		if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("DevHuntPhase", "pack=" + pack.id + " requested=" + phase + " prey=" + memory.prey.thingIDNumber, pawn, memory.prey);
		if (phase == HuntPhase.Chase) BeginChase(pack, memory);
		else
		{
			memory.detected = false;
			undetectedHunterByPrey.Remove(memory.prey);
			UpdateUndetectedHunter(pack, memory);
			RefreshStealthRenderSnapshot();
		}
		for (int i = 0; i < pack.members.Count; i++)
		{
			Pawn member = pack.members[i];
			if (member?.Spawned != true || member.Downed || member.InMentalState || !IsActiveHuntRole(RoleFor(member))) continue;
			Job job = HuntJobFor(member);
			if (job != null) member.jobs.StartJob(job, JobCondition.InterruptForced);
		}
		return true;
	}

	private bool TryGetDebugDenCell(Pawn pawn, int radialOffset, out IntVec3 cell)
	{
		cell = IntVec3.Invalid;
		if (pawn?.Spawned != true || pawn.Map != map || pawn.Downed || pawn.InMentalState || !IsPackHunter(pawn)) return false;
		AnimalPackSettings config = PacksMod.Settings.For(pawn.def);
		if (!config.useDens) return false;
		EnsureCurrent();
		if (!packByPawn.TryGetValue(pawn, out PackSnapshot pack) || pack.record?.den.IsValid != true || !pack.record.den.InBounds(map)) return false;
		int radialCount = GenRadial.NumCellsInRadius(3.9f);
		for (int i = 0; i < radialCount; i++)
		{
			IntVec3 candidate = pack.record.den + GenRadial.RadialPattern[(i + radialOffset) % radialCount];
			if (!candidate.InBounds(map) || !candidate.Standable(map) || candidate.IsForbidden(pawn) || candidate.GetTerrain(map).avoidWander || !pawn.CanReserve(candidate)) continue;
			if (!pawn.CanReach(candidate, PathEndMode.OnCell, Danger.Deadly)) continue;
			cell = candidate;
			return true;
		}
		return false;
	}

	public string DebugStateFor(Pawn pawn)
	{
		EnsureCurrent();
		if (!packByPawn.TryGetValue(pawn, out var value))
		{
			return pawn.LabelShortCap + ": no predator record";
		}
		AnimalPackSettings animalPackSettings = PacksMod.Settings.For(value.species);
		string text = value.prey?.LabelShortCap.ToString() ?? "None";
		string text2 = string.Join(", ", value.members.Select((Pawn member) => member.LabelShortCap + " [" + RoleFor(member).ToString() + "]"));
		string[] obj = new string[25]
		{
			value.Label,
			" (#",
			value.id.ToString(),
			")\nStrategy: ",
			animalPackSettings.socialStrategy.ToString(),
			"\nHunting style: ",
			animalPackSettings.huntingStyle.ToString(),
			"\nMembers: ",
			value.members.Count.ToString(),
			"\nLeader: ",
			value.leader?.LabelShortCap.ToString() ?? "None",
			"\nPrey: ",
			text,
			"\nHunt phase: ",
			HuntPhaseFor(pawn).ToString(),
			"\nDen: ",
			value.record?.den.ToString() ?? "None",
			"\nCenter: ",
			null,
			null,
			null,
			null,
			null,
			null,
			null
		};
		IntVec3 center = value.center;
		obj[18] = center.ToString();
		obj[19] = "\nMovement target: ";
		center = value.movementTarget;
		obj[20] = center.ToString();
		obj[21] = "\nHerds integration: ";
		obj[22] = (HerdsCompatibility.Active ? "Active" : "Not loaded");
		obj[23] = "\nMember roles: ";
		obj[24] = text2;
		string result = string.Concat(obj);
		if (huntMemory.TryGetValue(value.id, out HuntMemory debugMemory)) result += "\nDev-forced hunt: " + debugMemory.forcedByDev;
		return result;
	}

	private IntVec3 SolitaryStagingCell(PackSnapshot pack, Pawn pawn, PredatorHuntingStyle style)
	{
		Vector2 vector = new Vector2(pack.prey.Position.x, pack.prey.Position.z);
		Vector2 vector2 = new Vector2(pawn.Position.x, pawn.Position.z) - vector;
		if (vector2.sqrMagnitude < 0.01f)
		{
			vector2 = Vector2.right;
		}
		vector2.Normalize();
		float num = ((style == PredatorHuntingStyle.Ambush) ? 7f : 4f);
		IntVec3 wanted = new IntVec3(Mathf.RoundToInt(vector.x + vector2.x * num), 0, Mathf.RoundToInt(vector.y + vector2.y * num));
		return ClosestStandable(wanted, pawn.Position);
	}

	private IntVec3 StealthApproachCell(PackSnapshot pack, Pawn pawn, PredatorHuntingStyle style)
	{
		Vector2 vector = new Vector2(pack.prey.Position.x, pack.prey.Position.z);
		Vector2 vector2 = new Vector2(pawn.Position.x, pawn.Position.z) - vector;
		if (vector2.sqrMagnitude < 0.01f)
		{
			vector2 = Vector2.right;
		}
		vector2.Normalize();
		float num = style switch
		{
			PredatorHuntingStyle.Pursuit => 7f, 
			PredatorHuntingStyle.Stalk => 10f, 
			PredatorHuntingStyle.Ambush => 12f, 
			_ => 9f, 
		};
		if (PacksMod.Settings.For(pack.species).Cooperative)
		{
			switch (RoleFor(pawn))
			{
			case PackRole.Flanker:
				vector2 = new Vector2(0f - vector2.y, vector2.x) * ((pawn.thingIDNumber % 2 == 0) ? 1f : (-1f));
				break;
			case PackRole.Ambusher:
				vector2 = -vector2;
				break;
			}
		}
		IntVec3 wanted = new IntVec3(Mathf.RoundToInt(vector.x + vector2.x * num), 0, Mathf.RoundToInt(vector.y + vector2.y * num));
		return ClosestStandable(wanted, pawn.Position);
	}

	private IntVec3 StagingCell(PackSnapshot pack, Pawn pawn, PackRole role)
	{
		Vector2 vector = new Vector2(pack.prey.Position.x, pack.prey.Position.z);
		Vector2 vector2 = vector - new Vector2(pack.center.x, pack.center.z);
		if (vector2.sqrMagnitude < 0.01f)
		{
			vector2 = Vector2.right;
		}
		vector2.Normalize();
		Vector2 vector3 = role switch
		{
			PackRole.Ambusher => vector2 * 6f, 
			PackRole.Feeder => -vector2 * 6f, 
			PackRole.Chaser => -vector2 * 8f, 
			_ => new Vector2(0f - vector2.y, vector2.x) * ((pawn.thingIDNumber % 2 == 0) ? 5f : (-5f)) - vector2 * 1.5f, 
		};
		IntVec3 wanted = new IntVec3(Mathf.RoundToInt(vector.x + vector3.x), 0, Mathf.RoundToInt(vector.y + vector3.y));
		return ClosestStandable(wanted, pawn.Position);
	}

	public void ForceRefresh()
	{
		nextRefreshTick = 0;
		nextPlayerInfluenceTick = 0;
	}

	private void EnsureCurrent()
	{
		if (Find.TickManager != null && Find.TickManager.TicksGame >= nextRefreshTick && packs.Count <= 0)
		{
			Rebuild();
		}
	}

	private void Rebuild()
	{
		long performanceStart = Stopwatch.GetTimestamp();
		pathRequestsSinceRebuild = 0;
		int num = Find.TickManager?.TicksGame ?? 0;
		nextRefreshTick = num + Mathf.Max(120, PacksMod.Settings?.updateIntervalTicks ?? 300);
		packs.Clear();
		packByPawn.Clear();
		rootByPawn.Clear();
		undetectedHunterByPrey.Clear();
		IReadOnlyList<Pawn> allPawnsSpawned = map.mapPawns.AllPawnsSpawned;
		RebuildPreyBuckets(allPawnsSpawned);
		HashSet<Pawn> hashSet = new HashSet<Pawn>();
		for (int i = 0; i < allPawnsSpawned.Count; i++)
		{
			Pawn pawn = allPawnsSpawned[i];
			if (!pawn.Dead && IsPackHunter(pawn))
			{
				hashSet.Add(pawn);
			}
		}
		ReconcileRecords(hashSet, num);
		UpdateEcologicalConsequences(num);
		UpdateLeadership(num);
		MergeCompatibleRecords(num);
		SplitOversizedRecords(num);
		UpdateFamilyLifecycle(num);
		ReconcileDenMarkers();
		for (int j = 0; j < records.Count; j++)
		{
			if (records[j].members.Count >= 1)
			{
				AddPack(records[j], num);
			}
		}
		HashSet<int> active = new HashSet<int>(packs.Select((PackSnapshot pack) => pack.id));
		foreach (int item in targetMemory.Keys.Where((int id) => !active.Contains(id)).ToList())
		{
			targetMemory.Remove(item);
		}
		foreach (int item2 in huntMemory.Keys.Where((int id) => !active.Contains(id)).ToList())
		{
			huntMemory.Remove(item2);
		}
		foreach (Pawn item3 in roleByPawn.Keys.Where((Pawn pawn2) => pawn2 == null || pawn2.Dead || !packByPawn.ContainsKey(pawn2)).ToList())
		{
			roleByPawn.Remove(item3);
		}
		RefreshStealthRenderSnapshot();
		lastRebuildMicroseconds = ElapsedMicroseconds(performanceStart);
		rebuildTotalMicroseconds += lastRebuildMicroseconds;
		rebuildRuns++;
	}

	private void ReconcileRecords(HashSet<Pawn> eligible, int now)
	{
		HashSet<Pawn> assigned = new HashSet<Pawn>();
		for (int num = records.Count - 1; num >= 0; num--)
		{
			PackRecord record = records[num];
			if (record != null && record.species != null)
			{
				PacksSettings settings = PacksMod.Settings;
				if (settings != null && settings.IsEnabled(record.species))
				{
					record.members.RemoveAll((Pawn member) => member == null || !eligible.Contains(member) || member.def != record.species || member.Faction != record.faction || assigned.Contains(member));
					for (int num2 = 0; num2 < record.members.Count; num2++)
					{
						assigned.Add(record.members[num2]);
					}
					if (record.members.Count == 0)
					{
						AbandonDen(record, now);
						records.RemoveAt(num);
						continue;
					}
					if (record.leader == null || !record.members.Contains(record.leader) || record.leader.Dead)
					{
						if (PacksMod.Settings.enableEcologicalConsequences && record.leader?.Dead == true)
						{
							record.leaderLossTick = now;
							record.ecologicalStressUntilTick = now + 60000;
							WildlifeTestLog.Count("ecology.leader_lost");
							if (PacksMod.Settings.enableWildlifeAlerts && IsObserved(record.leader)) Messages.Message(record.Label + " lost its leader and may destabilize.", record.denMarker ?? (Thing)record.leader, MessageTypeDefOf.NegativeEvent, false);
						}
						record.leader = ChooseLeader(record.members);
					}
					nextRecordId = Mathf.Max(nextRecordId, record.id + 1);
					EnsureDen(record);
					continue;
				}
			}
			AbandonDen(record, now);
			records.RemoveAt(num);
		}
		foreach (Pawn item in eligible)
		{
			if (assigned.Contains(item))
			{
				continue;
			}
			if (PacksMod.Settings.For(item.def).socialStrategy == PredatorSocialStrategy.Solitary) continue;
			PackRecord packRecord = null;
			float num3 = float.MaxValue;
			for (int num4 = 0; num4 < records.Count; num4++)
			{
				PackRecord packRecord2 = records[num4];
				if (packRecord2.species != item.def || packRecord2.faction != item.Faction)
				{
					continue;
				}
				AnimalPackSettings animalPackSettings = PacksMod.Settings.For(packRecord2.species);
				if (packRecord2.members.Count < animalPackSettings.GroupSizeLimit)
				{
					float num5 = RecordCenter(packRecord2).DistanceToSquared(item.Position);
					if (num5 <= animalPackSettings.joinDistance * animalPackSettings.joinDistance && num5 < num3)
					{
						packRecord = packRecord2;
						num3 = num5;
					}
				}
			}
			if (packRecord != null)
			{
				packRecord.members.Add(item);
				assigned.Add(item);
			}
		}
		Dictionary<GroupKey, List<Pawn>> dictionary = new Dictionary<GroupKey, List<Pawn>>();
		foreach (Pawn item2 in eligible)
		{
			if (assigned.Contains(item2))
			{
				continue;
			}
			if (PacksMod.Settings.For(item2.def).socialStrategy == PredatorSocialStrategy.Solitary)
			{
				CreateRecord(item2.def, item2.Faction, new List<Pawn> { item2 }, now, null);
				assigned.Add(item2);
				continue;
			}
			GroupKey key = new GroupKey
			{
				species = item2.def,
				faction = item2.Faction
			};
			if (!dictionary.TryGetValue(key, out var value))
			{
				dictionary.Add(key, value = new List<Pawn>());
			}
			value.Add(item2);
		}
		foreach (KeyValuePair<GroupKey, List<Pawn>> item3 in dictionary)
		{
			AddSpatialRecords(item3.Key, item3.Value, now);
		}
	}

	private void AddSpatialRecords(GroupKey key, List<Pawn> members, int now)
	{
		if (members.Count == 0)
		{
			return;
		}
		AnimalPackSettings animalPackSettings = PacksMod.Settings.For(key.species);
		float num = Mathf.Max(10f, animalPackSettings.joinDistance);
		float num2 = num * num;
		int divisor = Mathf.Max(5, Mathf.FloorToInt(num * 0.5f));
		Dictionary<IntVec2, List<int>> dictionary = new Dictionary<IntVec2, List<int>>();
		int[] array = new int[members.Count];
		for (int i = 0; i < members.Count; i++)
		{
			array[i] = i;
			IntVec2 key2 = new IntVec2(FloorDiv(members[i].Position.x, divisor), FloorDiv(members[i].Position.z, divisor));
			if (!dictionary.TryGetValue(key2, out var value))
			{
				dictionary.Add(key2, value = new List<int>());
			}
			for (int j = 0; j < value.Count; j++)
			{
				if ((float)members[i].Position.DistanceToSquared(members[value[j]].Position) <= num2)
				{
					Union(array, i, value[j]);
				}
			}
			value.Add(i);
		}
		foreach (KeyValuePair<IntVec2, List<int>> item in dictionary)
		{
			for (int k = -2; k <= 2; k++)
			{
				for (int l = -2; l <= 2; l++)
				{
					if (k < 0 || (k == 0 && l <= 0) || !dictionary.TryGetValue(new IntVec2(item.Key.x + k, item.Key.z + l), out var value2))
					{
						continue;
					}
					for (int m = 0; m < item.Value.Count; m++)
					{
						for (int n = 0; n < value2.Count; n++)
						{
							if ((float)members[item.Value[m]].Position.DistanceToSquared(members[value2[n]].Position) <= num2)
							{
								Union(array, item.Value[m], value2[n]);
							}
						}
					}
				}
			}
		}
		Dictionary<int, List<Pawn>> dictionary2 = new Dictionary<int, List<Pawn>>();
		for (int num3 = 0; num3 < members.Count; num3++)
		{
			int key3 = FindRoot(array, num3);
			if (!dictionary2.TryGetValue(key3, out var value3))
			{
				dictionary2.Add(key3, value3 = new List<Pawn>());
			}
			value3.Add(members[num3]);
		}
		foreach (List<Pawn> value4 in dictionary2.Values)
		{
			CreateRecordsWithLimit(key, value4, now, animalPackSettings.GroupSizeLimit);
		}
	}

	private void CreateRecordsWithLimit(GroupKey key, List<Pawn> members, int now, int maximum)
	{
		List<Pawn> list = new List<Pawn>(members.OrderBy((Pawn pawn) => pawn.thingIDNumber));
		while (list.Count >= 2)
		{
			Pawn seed = list[0];
			List<Pawn> list2 = list.OrderBy((Pawn pawn) => pawn.Position.DistanceToSquared(seed.Position)).Take(Mathf.Max(2, maximum)).ToList();
			for (int num = 0; num < list2.Count; num++)
			{
				list.Remove(list2[num]);
			}
			CreateRecord(key.species, key.faction, list2, now, null);
		}
		if (list.Count == 1)
		{
			CreateRecord(key.species, key.faction, list, now, null);
		}
	}

	private PackRecord CreateRecord(ThingDef species, Faction faction, List<Pawn> members, int now, PackRecord parent)
	{
		int id = nextRecordId++;
		PackRecord packRecord = new PackRecord
		{
			id = id,
			name = DefaultRecordName(species, id),
			species = species,
			faction = faction,
			formedTick = now,
			members = new List<Pawn>(members),
			leader = ChooseLeader(members)
		};
		if (parent != null)
		{
			packRecord.parentPackIds.Add(parent.id);
		}
		records.Add(packRecord);
		EnsureDen(packRecord);
		return packRecord;
	}

	private static string DefaultRecordName(ThingDef species, int id)
	{
		PredatorSocialStrategy predatorSocialStrategy = PacksMod.Settings.StrategyFor(species);
		return species.LabelCap.ToString() + predatorSocialStrategy switch
		{
			PredatorSocialStrategy.Pack => " pack ", 
			PredatorSocialStrategy.Family => " family ", 
			PredatorSocialStrategy.Pair => " pair ", 
			_ => " ", 
		} + id;
	}

	private static Pawn ChooseLeader(List<Pawn> members)
	{
		return (from member in members
			where member != null && !member.Dead
			orderby member.ageTracker.Adult descending
			select member).ThenByDescending(EffectiveCombatPower).ThenBy((Pawn member) => member.thingIDNumber).FirstOrDefault();
	}

	private void MergeCompatibleRecords(int now)
	{
		bool flag;
		do
		{
			flag = false;
			for (int i = 0; i < records.Count; i++)
			{
				if (flag)
				{
					break;
				}
				PackRecord packRecord = records[i];
				AnimalPackSettings animalPackSettings = PacksMod.Settings.For(packRecord.species);
				if (!animalPackSettings.allowPackMerging || animalPackSettings.GroupSizeLimit <= 1)
				{
					continue;
				}
				for (int j = i + 1; j < records.Count; j++)
				{
					PackRecord packRecord2 = records[j];
					if (now < packRecord.separatedUntilTick || now < packRecord2.separatedUntilTick || packRecord.species != packRecord2.species || packRecord.faction != packRecord2.faction || packRecord.members.Count + packRecord2.members.Count > animalPackSettings.GroupSizeLimit || (float)RecordCenter(packRecord).DistanceToSquared(RecordCenter(packRecord2)) > animalPackSettings.joinDistance * animalPackSettings.joinDistance)
					{
						continue;
					}
					PackRecord packRecord3 = ((packRecord.formedTick < packRecord2.formedTick || (packRecord.formedTick == packRecord2.formedTick && packRecord.id < packRecord2.id)) ? packRecord : packRecord2);
					PackRecord packRecord4 = ((packRecord3 == packRecord) ? packRecord2 : packRecord);
					for (int k = 0; k < packRecord4.members.Count; k++)
					{
						if (!packRecord3.members.Contains(packRecord4.members[k]))
						{
							packRecord3.members.Add(packRecord4.members[k]);
						}
					}
					if (!packRecord3.mergedPackIds.Contains(packRecord4.id))
					{
						packRecord3.mergedPackIds.Add(packRecord4.id);
					}
					for (int l = 0; l < packRecord4.parentPackIds.Count; l++)
					{
						if (!packRecord3.parentPackIds.Contains(packRecord4.parentPackIds[l]))
						{
							packRecord3.parentPackIds.Add(packRecord4.parentPackIds[l]);
						}
					}
					packRecord3.leader = ChooseLeader(packRecord3.members);
					AbandonDen(packRecord4, now);
					records.Remove(packRecord4);
					territoryInteractions++;
					if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("TerritoryMerge", "winner=" + packRecord3.id + " merged=" + packRecord4.id + " members=" + packRecord3.members.Count, packRecord3.leader, packRecord4.leader);
					flag = true;
					break;
				}
			}
		}
		while (flag);
	}

	private void SplitOversizedRecords(int now)
	{
		List<PackRecord> list = records.ToList();
		for (int i = 0; i < list.Count; i++)
		{
			PackRecord packRecord = list[i];
			AnimalPackSettings animalPackSettings = PacksMod.Settings.For(packRecord.species);
			if (!animalPackSettings.allowPackSplitting)
			{
				continue;
			}
			while (packRecord.members.Count > animalPackSettings.GroupSizeLimit)
			{
				int count = Mathf.Min(animalPackSettings.GroupSizeLimit, Mathf.Max(2, packRecord.members.Count - animalPackSettings.GroupSizeLimit));
				IntVec3 anchor = (packRecord.den.IsValid ? packRecord.den : RecordCenter(packRecord));
				List<Pawn> list2 = packRecord.members.OrderByDescending((Pawn member) => member.Position.DistanceToSquared(anchor)).Take(count).ToList();
				for (int num = 0; num < list2.Count; num++)
				{
					packRecord.members.Remove(list2[num]);
				}
				CreateRecord(packRecord.species, packRecord.faction, list2, now, packRecord);
			}
			packRecord.leader = ChooseLeader(packRecord.members);
		}
	}

	private IntVec3 RecordCenter(PackRecord record)
	{
		if (record?.members == null || record.members.Count == 0)
		{
			return IntVec3.Invalid;
		}
		int num = 0;
		int num2 = 0;
		int num3 = 0;
		for (int i = 0; i < record.members.Count; i++)
		{
			Pawn pawn = record.members[i];
			if (pawn != null && pawn.Spawned)
			{
				num += pawn.Position.x;
				num2 += pawn.Position.z;
				num3++;
			}
		}
		if (num3 <= 0)
		{
			return IntVec3.Invalid;
		}
		return new IntVec3(Mathf.RoundToInt((float)num / (float)num3), 0, Mathf.RoundToInt((float)num2 / (float)num3));
	}

	private void EnsureDen(PackRecord record)
	{
		IntVec3 oldDen = record.den;
		AnimalPackSettings animalPackSettings = PacksMod.Settings.For(record.species);
		IntVec3 intVec = RecordCenter(record);
		if (!animalPackSettings.useDens)
		{
			record.den = intVec;
			DestroyDenMarker(record);
		}
		else
		{
			int now = Find.TickManager?.TicksGame ?? 0;
			if (record.den.IsValid && record.den.InBounds(map) && record.den.Standable(map) && (now < record.nextDenSuitabilityCheckTick || SuitableDenCell(record.den)))
			{
				record.nextDenSuitabilityCheckTick = now + 6000;
				EnsureDenMarker(record, animalPackSettings);
				return;
			}
			if (TryClaimAbandonedDen(record, animalPackSettings))
			{
				if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("DenReclaimed", "pack=" + record.id + " den=" + record.den, record.leader, record.denMarker);
				return;
			}
			IntVec3 intVec2 = intVec;
			denCandidateScratch.Clear();
			for (int i = 0; i < GenRadial.NumCellsInRadius(30f); i += 3)
			{
				IntVec3 intVec3 = intVec + GenRadial.RadialPattern[i];
				if (!SuitableDenCell(intVec3) || intVec3.GetEdifice(map) != null)
				{
					continue;
				}
				float edgeDistance = Mathf.Min(Mathf.Min(intVec3.x, map.Size.x - 1 - intVec3.x), Mathf.Min(intVec3.z, map.Size.z - 1 - intVec3.z));
				float score = (intVec3.Roofed(map) ? 80f : 0f) + Mathf.Min(edgeDistance, 25f) - intVec.DistanceTo(intVec3) * 0.35f;
				if (PacksMod.Settings.enableHabitatEcology) score += HerdsCompatibility.HabitatScoreAt(map, intVec3) * 35f;
				AddDenCandidate(intVec3, score);
			}
			Pawn leader = record.leader;
			for (int i = 0; i < denCandidateScratch.Count; i++)
			{
				IntVec3 candidate = denCandidateScratch[i].cell;
				pathRequestsSinceRebuild++;
				totalPathRequests++;
				bool reachable = leader == null || !leader.Spawned || leader.CanReach(candidate, PathEndMode.OnCell, Danger.Deadly);
				if (!reachable) failedPathRequests++;
				if (reachable)
				{
					intVec2 = candidate;
					break;
				}
			}
			record.den = (intVec2.IsValid ? intVec2 : intVec);
			record.nextDenSuitabilityCheckTick = now + 6000;
			EnsureDenMarker(record, animalPackSettings);
		}
		if (WildlifeTestLog.Enabled && oldDen != record.den) WildlifeTestLog.Write("DenAssigned", "pack=" + record.id + " old=" + oldDen + " new=" + record.den + " useDens=" + animalPackSettings.useDens, record.leader);
	}

	private void EnsureDenMarker(PackRecord record, AnimalPackSettings config)
	{
		if (record == null || !config.useDens || PacksDefOf.Packs_PredatorDen == null || !record.den.IsValid || !record.den.InBounds(map))
		{
			DestroyDenMarker(record);
			return;
		}
		Building_PredatorDen marker = record.denMarker;
		if (marker?.Destroyed == true) marker = null;
		if (marker == null)
		{
			List<Thing> existing = map.listerThings.ThingsOfDef(PacksDefOf.Packs_PredatorDen);
			for (int i = 0; i < existing.Count; i++)
			{
				if (existing[i] is Building_PredatorDen candidate && candidate.packId == record.id)
				{
					marker = candidate;
					break;
				}
			}
		}
		IntVec3 markerCell = ClosestDenMarkerCell(record.den, marker);
		if (!markerCell.IsValid) return;
		record.den = markerCell;
		if (marker == null)
		{
			marker = ThingMaker.MakeThing(PacksDefOf.Packs_PredatorDen) as Building_PredatorDen;
			if (marker == null) return;
		}
		marker.packId = record.id;
		marker.abandonedTick = 0;
		marker.formerSpecies = record.species;
		if (marker.Spawned && (marker.Map != map || marker.Position != markerCell)) marker.DeSpawn(DestroyMode.Vanish);
		if (!marker.Spawned) GenSpawn.Spawn(marker, markerCell, map);
		record.denMarker = marker;
		if (WildlifeTestLog.Enabled) WildlifeTestLog.WriteTransition("den-marker:" + record.id, "DenMarker", "spawned=" + marker.Spawned + " cell=" + markerCell + " thing=" + marker.thingIDNumber, record.leader, marker);
	}

	private IntVec3 ClosestDenMarkerCell(IntVec3 wanted, Building_PredatorDen current)
	{
		for (int i = 0; i < GenRadial.NumCellsInRadius(5.9f); i++)
		{
			IntVec3 cell = wanted + GenRadial.RadialPattern[i];
			if (!cell.InBounds(map) || !cell.Standable(map)) continue;
			Building edifice = cell.GetEdifice(map);
			if (edifice == null || edifice == current) return cell;
		}
		return IntVec3.Invalid;
	}

	private static void DestroyDenMarker(PackRecord record)
	{
		if (record?.denMarker == null) return;
		record.denMarker.packId = 0;
		if (!record.denMarker.Destroyed) record.denMarker.Destroy(DestroyMode.Vanish);
		record.denMarker = null;
	}

	private void AbandonDen(PackRecord record, int now)
	{
		Building_PredatorDen marker = record?.denMarker;
		if (marker == null || marker.Destroyed) return;
		marker.packId = 0;
		marker.abandonedTick = Mathf.Max(1, now);
		marker.formerSpecies = record.species;
		record.denMarker = null;
		if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("DenAbandoned", "formerPack=" + record.id + " decayTick=" + (now + 120000), record.leader, marker);
	}

	private bool TryClaimAbandonedDen(PackRecord record, AnimalPackSettings config)
	{
		if (PacksDefOf.Packs_PredatorDen == null || record?.leader?.Spawned != true) return false;
		List<Thing> markers = map.listerThings.ThingsOfDef(PacksDefOf.Packs_PredatorDen);
		Building_PredatorDen best = null;
		float bestDistance = Mathf.Max(36f, config.territoryRadius);
		float bestDistanceSquared = bestDistance * bestDistance;
		for (int i = 0; i < markers.Count; i++)
		{
			Building_PredatorDen marker = markers[i] as Building_PredatorDen;
			if (marker == null || marker.packId != 0 || !marker.Spawned) continue;
			float distance = marker.Position.DistanceToSquared(record.leader.Position);
			if (distance >= bestDistanceSquared) continue;
			pathRequestsSinceRebuild++;
			totalPathRequests++;
			if (!record.leader.CanReach(marker, PathEndMode.Touch, Danger.Deadly)) { failedPathRequests++; continue; }
			best = marker;
			bestDistanceSquared = distance;
		}
		if (best == null) return false;
		best.packId = record.id;
		best.abandonedTick = 0;
		best.formerSpecies = record.species;
		record.den = best.Position;
		record.denMarker = best;
		WildlifeTestLog.Count("dens.reclaimed");
		return true;
	}

	private void ReconcileDenMarkers()
	{
		if (PacksDefOf.Packs_PredatorDen == null) return;
		List<Thing> markers = map.listerThings.ThingsOfDef(PacksDefOf.Packs_PredatorDen);
		for (int markerIndex = markers.Count - 1; markerIndex >= 0; markerIndex--)
		{
			if (!(markers[markerIndex] is Building_PredatorDen marker)) continue;
			PackRecord owner = null;
			for (int recordIndex = 0; recordIndex < records.Count; recordIndex++)
			{
				if (records[recordIndex].id == marker.packId)
				{
					owner = records[recordIndex];
					break;
				}
			}
			if (owner == null)
			{
				if (marker.packId != 0)
				{
					marker.packId = 0;
					marker.abandonedTick = Mathf.Max(1, Find.TickManager.TicksGame);
				}
				if (marker.abandonedTick > 0 && Find.TickManager.TicksGame - marker.abandonedTick >= 120000) marker.Destroy(DestroyMode.Vanish);
				continue;
			}
			if (!PacksMod.Settings.For(owner.species).useDens)
			{
				marker.packId = 0;
				marker.Destroy(DestroyMode.Vanish);
				continue;
			}
			if (owner.denMarker == null || owner.denMarker.Destroyed) owner.denMarker = marker;
			else if (owner.denMarker != marker) marker.Destroy(DestroyMode.Vanish);
		}
	}

	private void AddDenCandidate(IntVec3 cell, float score)
	{
		int index = 0;
		while (index < denCandidateScratch.Count && denCandidateScratch[index].score >= score) index++;
		if (index >= MaximumReachabilityCandidates) return;
		denCandidateScratch.Insert(index, new DenCandidate { cell = cell, score = score });
		if (denCandidateScratch.Count > MaximumReachabilityCandidates) denCandidateScratch.RemoveAt(denCandidateScratch.Count - 1);
	}

	private void AddPack(PackRecord record, int now)
	{
		List<Pawn> members = record.members;
		if (members.Count < 1)
		{
			return;
		}
		members.Sort((Pawn a, Pawn b) => a.thingIDNumber.CompareTo(b.thingIDNumber));
		int num = 0;
		int num2 = 0;
		for (int num3 = 0; num3 < members.Count; num3++)
		{
			num += members[num3].Position.x;
			num2 += members[num3].Position.z;
		}
		IntVec3 intVec = new IntVec3(Mathf.RoundToInt((float)num / (float)members.Count), 0, Mathf.RoundToInt((float)num2 / (float)members.Count));
		int id = record.id;
		if (!targetMemory.TryGetValue(id, out var value))
		{
			targetMemory.Add(id, value = new TargetMemory());
		}
		AnimalPackSettings animalPackSettings = PacksMod.Settings.For(record.species);
		if (record.claimedCorpse == null || record.claimedCorpse.DestroyedOrNull() || now >= record.corpseClaimUntilTick)
		{
			record.claimedCorpse = null;
			record.corpseClaimUntilTick = 0;
		}
		bool flag = animalPackSettings.useDens && record.den.IsValid && (float)intVec.DistanceToSquared(record.den) > animalPackSettings.territoryRadius * animalPackSettings.territoryRadius;
		bool gatherAtDen = animalPackSettings.useDens && animalPackSettings.gatherAtDenToMate && record.den.IsValid && ReadyToMateAtDen(members);
		if (record.claimedCorpse?.Spawned == true)
		{
			value.target = record.claimedCorpse.Position;
			value.nextChangeTick = now + 300;
		}
		else if (gatherAtDen)
		{
			value.target = record.den;
			value.nextChangeTick = now + 300;
		}
		else if (!value.target.IsValid || now >= value.nextChangeTick || !value.target.InBounds(map) || !value.target.Standable(map) || flag)
		{
			AnimalPackSettings animalPackSettings2 = animalPackSettings;
			float f = PositiveMod((id * 193) ^ (now / 600), 360) * Mathf.Deg2Rad;
			Vector2 vector = new Vector2(Mathf.Cos(f), Mathf.Sin(f));
			IntVec3 intVec2 = ((animalPackSettings2.useDens && record.den.IsValid) ? record.den : intVec);
			Vector2 vector2 = new Vector2(intVec2.x, intVec2.z);
			Vector2 vector3 = new Vector2(intVec.x, intVec.z);
			if (PacksMod.Settings.enablePredatorBoldness && playerSettlementCenter.IsValid)
			{
				Vector2 towardHumans = new Vector2(playerSettlementCenter.x - intVec.x, playerSettlementCenter.z - intVec.z).normalized;
				float boldness = record.humanBoldness;
				if (boldness >= 0.65f) vector = (vector * 0.72f + towardHumans * Mathf.InverseLerp(0.65f, 1f, boldness) * 0.55f).normalized;
				else if (boldness <= 0.3f && intVec.DistanceToSquared(playerSettlementCenter) < 10000) vector = (vector * 0.55f - towardHumans * Mathf.InverseLerp(0.3f, 0f, boldness)).normalized;
			}
			if ((vector3 - vector2).sqrMagnitude > animalPackSettings2.territoryRadius * animalPackSettings2.territoryRadius * 0.64f)
			{
				vector = (vector2 - vector3).normalized;
			}
			float num4 = Mathf.Min(20f, (float)Mathf.Min(map.Size.x, map.Size.z) * 0.2f);
			if (vector2.x < num4)
			{
				vector.x += (num4 - vector2.x) / num4 * 2.5f;
			}
			else if (vector2.x > (float)map.Size.x - num4)
			{
				vector.x -= (vector2.x - ((float)map.Size.x - num4)) / num4 * 2.5f;
			}
			if (vector2.y < num4)
			{
				vector.y += (num4 - vector2.y) / num4 * 2.5f;
			}
			else if (vector2.y > (float)map.Size.z - num4)
			{
				vector.y -= (vector2.y - ((float)map.Size.z - num4)) / num4 * 2.5f;
			}
			if (vector.sqrMagnitude < 0.01f)
			{
				vector = Vector2.up;
			}
			vector.Normalize();
			float num5 = Mathf.Min(animalPackSettings2.roamingDistance, animalPackSettings2.territoryRadius * 0.8f);
			int newX = Mathf.Clamp(Mathf.RoundToInt(vector2.x + vector.x * num5), Mathf.CeilToInt(num4), map.Size.x - Mathf.CeilToInt(num4) - 1);
			int newZ = Mathf.Clamp(Mathf.RoundToInt(vector2.y + vector.y * num5), Mathf.CeilToInt(num4), map.Size.z - Mathf.CeilToInt(num4) - 1);
			IntVec3 wanted = AvoidPlayerProtectedAreas(AvoidRememberedDanger(new IntVec3(newX, 0, newZ), intVec, now), intVec);
			value.target = ClosestStandable(wanted, intVec);
			value.nextChangeTick = now + 1200 + PositiveMod(id * 31, 1200);
		}
		record.leader = ((record.leader != null && members.Contains(record.leader)) ? record.leader : ChooseLeader(members));
		PackSnapshot packSnapshot = new PackSnapshot
		{
			id = id,
			record = record,
			species = record.species,
			faction = record.faction,
			leader = record.leader,
			center = intVec,
			movementTarget = value.target
		};
		packSnapshot.members.AddRange(members);
		if (huntMemory.TryGetValue(id, out var value2))
		{
			Pawn prey = value2.prey;
			if (prey != null && prey.Spawned && !value2.prey.Dead && now < value2.expiresTick)
			{
				packSnapshot.prey = value2.prey;
			}
		}
		packs.Add(packSnapshot);
		BuildRoots(packSnapshot);
		for (int num6 = 0; num6 < members.Count; num6++)
		{
			packByPawn[members[num6]] = packSnapshot;
		}
		if (huntMemory.TryGetValue(id, out HuntMemory activeHunt)) UpdateUndetectedHunter(packSnapshot, activeHunt);
	}

	private static bool ReadyToMateAtDen(List<Pawn> members)
	{
		for (int i = 0; i < members.Count; i++)
		{
			Pawn male = members[i];
			if (male?.Spawned != true || male.gender != Gender.Male || male.Downed || male.Sterile() || !male.CanCasuallyInteractNow()) continue;
			for (int j = 0; j < members.Count; j++)
			{
				Pawn female = members[j];
				if (female?.Spawned == true && !female.Downed && female.CanCasuallyInteractNow() && PawnUtility.FertileMateTarget(male, female)) return true;
			}
		}
		return false;
	}

	private void RebuildPreyBuckets(IReadOnlyList<Pawn> pawns)
	{
		preyBuckets.Clear();
		for (int i = 0; i < pawns.Count; i++)
		{
			Pawn pawn = pawns[i];
			if (pawn == null || pawn.Dead || !pawn.Spawned) continue;
			IntVec2 key = BucketFor(pawn.Position, PreyBucketSize);
			if (!preyBuckets.TryGetValue(key, out List<Pawn> bucket)) preyBuckets.Add(key, bucket = new List<Pawn>());
			bucket.Add(pawn);
		}
	}

	private void RebuildPlayerInfluences(int now)
	{
		nextPlayerInfluenceTick = now + 300;
		observationPosts.Clear();
		baitStations.Clear();
		predatorDeterrents.Clear();
		wildlifeReserves.Clear();
		ranchGuardians.Clear();
		playerSettlementCenter = IntVec3.Invalid;
		if (map.listerBuildings.allBuildingsColonist.Count > 0)
		{
			int sumX = 0, sumZ = 0;
			for (int i = 0; i < map.listerBuildings.allBuildingsColonist.Count; i++) { sumX += map.listerBuildings.allBuildingsColonist[i].Position.x; sumZ += map.listerBuildings.allBuildingsColonist[i].Position.z; }
			playerSettlementCenter = new IntVec3(sumX / map.listerBuildings.allBuildingsColonist.Count, 0, sumZ / map.listerBuildings.allBuildingsColonist.Count);
		}
		AddPlayerTools("Herds_ObservationPost", observationPosts, PacksMod.Settings.enableWildlifeKnowledge);
		AddPlayerTools("Herds_WildlifeBait", baitStations, PacksMod.Settings.enableBaitInfluence);
		AddPlayerTools("Herds_PredatorDeterrent", predatorDeterrents, PacksMod.Settings.enableDeterrentInfluence);
		AddPlayerTools("Herds_WildlifeReserve", wildlifeReserves, PacksMod.Settings.enableReserveInfluence);
		IReadOnlyList<Pawn> allPawns = map.mapPawns.AllPawnsSpawned;
		if (PacksMod.Settings.enableGuardianInfluence)
		{
			HediffDef guardianDef = DefDatabase<HediffDef>.GetNamedSilentFail("Herds_RanchGuardian");
			if (guardianDef != null)
				for (int i = 0; i < allPawns.Count; i++)
					if (allPawns[i]?.Faction == Faction.OfPlayer && allPawns[i].health?.hediffSet?.GetFirstHediffOfDef(guardianDef) != null) ranchGuardians.Add(allPawns[i]);
		}
		if (PacksMod.Settings.enableWildlifeKnowledge)
		{
			playerObserversScratch.Clear();
			for (int i = 0; i < allPawns.Count; i++) if (allPawns[i]?.Spawned == true && allPawns[i].Faction == Faction.OfPlayer && allPawns[i].RaceProps.Humanlike && !allPawns[i].Downed) playerObserversScratch.Add(allPawns[i]);
			for (int i = 0; i < packs.Count; i++)
				for (int j = 0; j < packs[i].members.Count; j++)
				{
					Pawn animal = packs[i].members[j];
					bool postObserved = observationPosts.Count > 0 && IsNearPlayerTool(animal.Position, observationPosts, 50f);
					bool personallyObserved = PlayerObserverNear(animal.Position, playerObserversScratch, 18f);
					if (postObserved || personallyObserved) observedUntilTick[animal] = now + (postObserved ? 60000 : 15000);
				}
		}
		foreach (Pawn stale in observedUntilTick.Keys.Where(pawn => pawn == null || pawn.Dead || observedUntilTick[pawn] <= now).ToList()) if (stale != null) observedUntilTick.Remove(stale);
	}

	private void AddPlayerTools(string defName, List<Thing> target, bool enabled)
	{
		if (!enabled) return;
		ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
		if (def == null) return;
		List<Thing> things = map.listerThings.ThingsOfDef(def);
		for (int i = 0; i < things.Count; i++)
		{
			Thing thing = things[i];
			if (!thing.Spawned) continue;
			if (wildlifeToolActiveField == null && thing.GetType().FullName == "Herds.Building_WildlifeTool") wildlifeToolActiveField = thing.GetType().GetField("active", BindingFlags.Instance | BindingFlags.Public);
			if (wildlifeToolActiveField != null && wildlifeToolActiveField.DeclaringType.IsInstanceOfType(thing) && wildlifeToolActiveField.GetValue(thing) is bool active && !active) continue;
			target.Add(thing);
		}
	}

	private static bool IsNearPlayerTool(IntVec3 cell, List<Thing> tools, float radius)
	{
		float radiusSquared = radius * radius;
		for (int i = 0; i < tools.Count; i++) if (tools[i].Position.DistanceToSquared(cell) <= radiusSquared) return true;
		return false;
	}

	private static bool PlayerObserverNear(IntVec3 cell, IReadOnlyList<Pawn> observers, float radius)
	{
		float radiusSquared = radius * radius;
		for (int i = 0; i < observers.Count; i++)
		{
			if (observers[i].Position.DistanceToSquared(cell) <= radiusSquared) return true;
		}
		return false;
	}

	public bool IsObserved(Pawn pawn)
	{
		if (!PacksMod.Settings.enableWildlifeKnowledge) return false;
		if (!PacksMod.Settings.requireObservationForDetails || pawn?.Faction == Faction.OfPlayer) return true;
		return pawn != null && observedUntilTick.TryGetValue(pawn, out int until) && until > (Find.TickManager?.TicksGame ?? 0);
	}

	public void NotifyHumanConflict(Pawn predator, bool predatorWon)
	{
		if (!PacksMod.Settings.enablePredatorBoldness || predator == null) return;
		EnsureCurrent();
		if (!packByPawn.TryGetValue(predator, out PackSnapshot pack) || pack.record == null) return;
		float change = predatorWon ? 0.14f : -0.18f;
		pack.record.humanBoldness = Mathf.Clamp01(pack.record.humanBoldness + change);
		if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("HumanBoldness", "pack=" + pack.id + " won=" + predatorWon + " boldness=" + pack.record.humanBoldness.ToString("0.00"), predator);
	}

	public bool DebugSetHumanBoldness(Pawn predator, float value)
	{
		EnsureCurrent();
		if (!packByPawn.TryGetValue(predator, out PackSnapshot pack) || pack.record == null) return false;
		pack.record.humanBoldness = Mathf.Clamp01(value);
		if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("DevHumanBoldness", "pack=" + pack.id + " value=" + pack.record.humanBoldness.ToString("0.00"), predator);
		return true;
	}

	private IntVec3 AvoidPlayerProtectedAreas(IntVec3 wanted, IntVec3 fallback)
	{
		Thing source = null;
		if (PacksMod.Settings.enableDeterrentInfluence)
			for (int i = 0; i < predatorDeterrents.Count; i++) if (predatorDeterrents[i].Position.DistanceToSquared(wanted) <= 1444) { source = predatorDeterrents[i]; break; }
		if (source == null && PacksMod.Settings.enableReserveInfluence)
			for (int i = 0; i < wildlifeReserves.Count; i++) if (wildlifeReserves[i].Position.DistanceToSquared(wanted) <= 3025) { source = wildlifeReserves[i]; break; }
		if (source == null) return wanted;
		Vector2 away = new Vector2(wanted.x - source.Position.x, wanted.z - source.Position.z);
		if (away.sqrMagnitude < 0.1f) away = new Vector2(fallback.x - source.Position.x, fallback.z - source.Position.z);
		if (away.sqrMagnitude < 0.1f) away = Vector2.right;
		away.Normalize();
		return new IntVec3(Mathf.Clamp(Mathf.RoundToInt(wanted.x + away.x * 22f), 1, map.Size.x - 2), 0, Mathf.Clamp(Mathf.RoundToInt(wanted.z + away.y * 22f), 1, map.Size.z - 2));
	}

	private void BuildRoots(PackSnapshot pack)
	{
		Vector2 vector = new Vector2(pack.center.x, pack.center.z);
		Vector2 vector2 = new Vector2(pack.movementTarget.x, pack.movementTarget.z) - vector;
		if (vector2.sqrMagnitude > 0.01f)
		{
			vector += vector2.normalized * Mathf.Min(12f, vector2.magnitude);
		}
		float num = Mathf.Clamp(Mathf.Sqrt(pack.members.Count) * 0.65f, 1.4f, 5f);
		for (int i = 0; i < pack.members.Count; i++)
		{
			Pawn pawn = pack.members[i];
			float f = PositiveMod(pawn.thingIDNumber * 137, 360) * Mathf.Deg2Rad;
			IntVec3 wanted = new IntVec3(Mathf.RoundToInt(vector.x + Mathf.Cos(f) * num), 0, Mathf.RoundToInt(vector.y + Mathf.Sin(f) * num));
			rootByPawn[pawn] = ClosestStandable(wanted, pawn.Position);
		}
	}

	private void ScanHunts(int now)
	{
		long performanceStart = Stopwatch.GetTimestamp();
		nextHuntScanTick = now + 60;
		for (int i = 0; i < packs.Count; i++)
		{
			PackSnapshot activePack = packs[i];
			for (int memberIndex = 0; memberIndex < activePack.members.Count; memberIndex++)
			{
				Pawn pawn = activePack.members[memberIndex];
				if (pawn.CurJobDef == JobDefOf.PredatorHunt && pawn.CurJob.targetA.HasThing && pawn.CurJob.targetA.Thing is Pawn { Dead: false } pawn2)
				{
					RegisterHunt(pawn, pawn2);
					NotifyActualHunt(activePack);
				}
			}
		}
		for (int j = 0; j < packs.Count; j++)
		{
			PackSnapshot packSnapshot = packs[j];
			if (packSnapshot.prey == null)
			{
				if (!huntMemory.TryGetValue(packSnapshot.id, out var value) || now >= value.cooldownUntil)
				{
					ClearHuntRoles(packSnapshot);
					huntMemory.Remove(packSnapshot.id);
				}
			}
		else if (packSnapshot.prey.Dead)
		{
			ClaimCarcass(packSnapshot, packSnapshot.prey, now);
			CompleteTestRun(packSnapshot, packSnapshot.prey, "kill", now);
			WildlifeTestLog.Count("hunts.prey_killed");
			undetectedHunterByPrey.Remove(packSnapshot.prey);
			ClearHuntRoles(packSnapshot);
			packSnapshot.prey = null;
			huntMemory.Remove(packSnapshot.id);
		}
		else if (!packSnapshot.prey.Spawned)
		{
			bool hidden = HerdsCompatibility.IsHidden(packSnapshot.prey);
			if (hidden) WildlifeTestLog.Count("hunts.prey_hidden");
			CompleteTestRun(packSnapshot, packSnapshot.prey, hidden ? "hidden" : "escape", now);
			AbandonHunt(packSnapshot, now);
		}
			else if (ShouldAbandonHunt(packSnapshot, now))
			{
				CompleteTestRun(packSnapshot, packSnapshot.prey, "abandoned", now);
				AbandonHunt(packSnapshot, now);
			}
		}
		lastHuntScanMicroseconds = ElapsedMicroseconds(performanceStart);
		huntScanTotalMicroseconds += lastHuntScanMicroseconds;
		huntScanRuns++;
		RefreshStealthRenderSnapshot();
	}

	private bool ShouldAbandonHunt(PackSnapshot pack, int now)
	{
		if (!huntMemory.TryGetValue(pack.id, out var value) || value.prey == null)
		{
			return false;
		}
		if (value.prey.RaceProps.Humanlike && !PacksMod.Settings.predatorsAttackColonists) return true;
		if (value.forcedByDev)
		{
			if (WildlifeTestLog.Enabled) WildlifeTestLog.WriteTransition("forced-hunt:" + pack.id, "HuntAbandonDecision", "bypassed-for-dev-test distanceSquared=" + pack.center.DistanceToSquared(value.prey.Position), pack.leader, value.prey);
			return false;
		}
		AnimalPackSettings animalPackSettings = PacksMod.Settings.For(pack.species);
		float hungerUrgency = HungerUrgency(pack);
		if (PacksMod.Settings.enableDeterrentInfluence && hungerUrgency < 0.78f && IsNearPlayerTool(value.prey.Position, predatorDeterrents, 38f))
		{
			if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("HuntAbandonDecision", "pack=" + pack.id + " reason=player-deterrent hunger=" + hungerUrgency.ToString("0.00"), pack.leader, value.prey);
			return true;
		}
		int hunterLimit = EffectiveHunterLimit(pack, animalPackSettings);
		int num = 0;
		float num2 = 0f;
		float num3 = 0f;
		for (int i = 0; i < pack.members.Count; i++)
		{
			if (num >= hunterLimit)
			{
				break;
			}
			Pawn pawn = pack.members[i];
			if (!pawn.Downed && !pawn.InMentalState && (pawn.ageTracker.Adult || animalPackSettings.juvenilesHunt))
			{
				num++;
				num2 += EffectiveCombatPower(pawn);
				num3 += pawn.health.summaryHealth.SummaryHealthPercent;
			}
		}
		int num4 = animalPackSettings.socialStrategy == PredatorSocialStrategy.Pair || animalPackSettings.socialStrategy == PredatorSocialStrategy.Pack ? Mathf.Min(2, hunterLimit) : 1;
		float minimumHealth = Mathf.Lerp(0.55f, 0.36f, hungerUrgency);
		if (num < num4 || num3 / (float)num < minimumHealth)
		{
			if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("HuntAbandonDecision", "pack=" + pack.id + " reason=insufficient-or-injured-hunters active=" + num + " required=" + num4 + " healthTotal=" + num3.ToString("0.00"), pack.leader, value.prey);
			return true;
		}
		float hungerRiskTolerance = animalPackSettings.preyRiskTolerance * Mathf.Lerp(0.78f, 1.42f, hungerUrgency);
		if (!value.prey.Downed && EffectiveCombatPower(value.prey) > num2 * hungerRiskTolerance)
		{
			if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("HuntAbandonDecision", "pack=" + pack.id + " reason=prey-risk preyPower=" + EffectiveCombatPower(value.prey).ToString("0.0") + " hunterPower=" + num2.ToString("0.0") + " tolerance=" + hungerRiskTolerance.ToString("0.00") + " hunger=" + hungerUrgency.ToString("0.00"), pack.leader, value.prey);
			return true;
		}
		float maximumDistance = Mathf.Lerp(65f, 135f, hungerUrgency);
		if (pack.center.DistanceToSquared(value.prey.Position) > maximumDistance * maximumDistance)
		{
			if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("HuntAbandonDecision", "pack=" + pack.id + " reason=distance distanceSquared=" + pack.center.DistanceToSquared(value.prey.Position), pack.leader, value.prey);
			return true;
		}
		bool timedOut = now - value.startedTick > Mathf.RoundToInt(Mathf.Lerp(4200f, 8200f, hungerUrgency));
		if (WildlifeTestLog.Enabled && timedOut) WildlifeTestLog.Write("HuntAbandonDecision", "pack=" + pack.id + " reason=timeout age=" + (now - value.startedTick), pack.leader, value.prey);
		return timedOut;
	}

	private void AbandonHunt(PackSnapshot pack, int now)
	{
		if (!huntMemory.TryGetValue(pack.id, out var value))
		{
			return;
		}
		Pawn prey = value.prey;
		if (prey != null && prey.Position.IsValid) RememberDanger(prey.Position, now + 30000, "failed hunt");
		WildlifeTestLog.Count("hunts.abandoned");
		if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("HuntAbandoned", "pack=" + pack.id + " cooldownUntil=" + (now + 1800) + " phase=" + value.phase, pack.leader, prey);
		if (prey != null) undetectedHunterByPrey.Remove(prey);
		HashSet<Pawn> hashSet = new HashSet<Pawn>(pack.members.Where((Pawn member) => roleByPawn.TryGetValue(member, out var value2) && (value2 == PackRole.Flanker || value2 == PackRole.Ambusher)));
		value.prey = null;
		value.feeder = null;
		value.forcedByDev = false;
		value.expiresTick = 0;
		value.cooldownUntil = now + 1800;
		pack.prey = null;
		ClearHuntRoles(pack);
		for (int num = 0; num < pack.members.Count; num++)
		{
			Pawn pawn = pack.members[num];
			if (pawn.CurJob != null && pawn.CurJob.targetA.HasThing && pawn.CurJob.targetA.Thing == prey && (pawn.CurJobDef == JobDefOf.PredatorHunt || pawn.CurJobDef == JobDefOf.AttackMelee))
			{
				pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
			}
		}
		for (int num2 = 0; num2 < pack.members.Count; num2++)
		{
			Pawn pawn2 = pack.members[num2];
			if (hashSet.Contains(pawn2) && pawn2.CurJobDef == JobDefOf.Goto)
			{
				pawn2.jobs.EndCurrentJob(JobCondition.InterruptForced);
			}
		}
		HerdsCompatibility.NotifyThreatEnded(prey, pack.leader);
		RefreshStealthRenderSnapshot();
	}

	private void ClearHuntRoles(PackSnapshot pack)
	{
		for (int i = 0; i < pack.members.Count; i++)
		{
			Pawn pawn = pack.members[i];
			roleByPawn[pawn] = ((!pawn.ageTracker.Adult) ? PackRole.Juvenile : ((pawn == pack.leader) ? PackRole.Leader : PackRole.Member));
		}
	}

	private IntVec3 ClosestStandable(IntVec3 wanted, IntVec3 fallback)
	{
		for (int i = 0; i < GenRadial.NumCellsInRadius(8f); i++)
		{
			IntVec3 intVec = wanted + GenRadial.RadialPattern[i];
			if (intVec.InBounds(map) && intVec.Standable(map))
			{
				return intVec;
			}
		}
		return fallback;
	}

	private static int FloorDiv(int value, int divisor)
	{
		if (value < 0)
		{
			return (value - divisor + 1) / divisor;
		}
		return value / divisor;
	}

	private static float HungerUrgency(PackSnapshot pack)
	{
		if (pack == null || pack.members.Count == 0) return 0.35f;
		float total = 0f;
		int count = 0;
		for (int i = 0; i < pack.members.Count; i++)
		{
			Need_Food food = pack.members[i].needs?.food;
			if (food == null) continue;
			total += 1f - food.CurLevelPercentage;
			if (food.CurCategory == HungerCategory.Starving) total += 0.25f;
			count++;
		}
		float urgency = count > 0 ? Mathf.Clamp01(total / count) : 0.35f;
		if (PacksMod.Settings.enableEcologicalConsequences && pack.record != null && pack.record.ecologicalStressUntilTick > (Find.TickManager?.TicksGame ?? 0)) urgency += 0.18f;
		return Mathf.Clamp01(urgency);
	}

	private void UpdateEcologicalConsequences(int now)
	{
		if (!PacksMod.Settings.enableEcologicalConsequences) return;
		for (int i = 0; i < records.Count; i++)
		{
			PackRecord record = records[i];
			if (record.leaderLossTick <= record.handledLeaderLossTick) continue;
			record.handledLeaderLossTick = record.leaderLossTick;
			AnimalPackSettings config = PacksMod.Settings.For(record.species);
			if ((config.socialStrategy != PredatorSocialStrategy.Pack && config.socialStrategy != PredatorSocialStrategy.Family) || record.members.Count < 5) continue;
			IntVec3 anchor = record.den.IsValid ? record.den : RecordCenter(record);
			List<Pawn> splinterMembers = record.members.Where(member => member != record.leader).OrderByDescending(member => member.Position.DistanceToSquared(anchor)).Take(Mathf.Max(2, record.members.Count / 2)).ToList();
			if (splinterMembers.Count < 2) continue;
			for (int j = 0; j < splinterMembers.Count; j++) record.members.Remove(splinterMembers[j]);
			PackRecord splinter = CreateRecord(record.species, record.faction, splinterMembers, now, record);
			splinter.separatedUntilTick = now + 60000;
			splinter.ecologicalStressUntilTick = now + 60000;
			WildlifeTestLog.Count("ecology.pack_split");
			if (PacksMod.Settings.enableWildlifeAlerts && IsObserved(record.leader)) Messages.Message(record.Label + " split after losing its leader.", record.denMarker ?? (Thing)record.leader, MessageTypeDefOf.NegativeEvent, false);
			break;
		}
	}

	private void RememberDanger(IntVec3 cell, int expiresTick, string reason)
	{
		if (!cell.IsValid || !cell.InBounds(map)) return;
		for (int i = 0; i < dangerMemories.Count; i++)
		{
			if (dangerMemories[i].cell.DistanceToSquared(cell) <= 100)
			{
				dangerMemories[i].expiresTick = Mathf.Max(dangerMemories[i].expiresTick, expiresTick);
				dangerMemories[i].reason = reason;
				return;
			}
		}
		dangerMemories.Add(new DangerMemoryRecord { cell = cell, expiresTick = expiresTick, reason = reason });
		if (dangerMemories.Count > 32) dangerMemories.RemoveAt(0);
		WildlifeTestLog.Count("memory.dangers");
	}

	private IntVec3 AvoidRememberedDanger(IntVec3 wanted, IntVec3 fallback, int now)
	{
		for (int i = 0; i < dangerMemories.Count; i++)
		{
			DangerMemoryRecord danger = dangerMemories[i];
			if (now >= danger.expiresTick || wanted.DistanceToSquared(danger.cell) > 225) continue;
			Vector2 away = new Vector2(wanted.x - danger.cell.x, wanted.z - danger.cell.z);
			if (away.sqrMagnitude < 0.1f) away = new Vector2(fallback.x - danger.cell.x, fallback.z - danger.cell.z);
			if (away.sqrMagnitude < 0.1f) away = Vector2.right;
			away.Normalize();
			return new IntVec3(Mathf.Clamp(Mathf.RoundToInt(wanted.x + away.x * 16f), 1, map.Size.x - 2), 0, Mathf.Clamp(Mathf.RoundToInt(wanted.z + away.y * 16f), 1, map.Size.z - 2));
		}
		return wanted;
	}

	private bool SuitableDenCell(IntVec3 cell)
	{
		if (!cell.IsValid || !cell.InBounds(map) || !cell.Standable(map)) return false;
		if ((PacksMod.Settings.enableDeterrentInfluence && IsNearPlayerTool(cell, predatorDeterrents, 38f)) || (PacksMod.Settings.enableReserveInfluence && IsNearPlayerTool(cell, wildlifeReserves, 55f))) return false;
		TerrainDef terrain = cell.GetTerrain(map);
		if (terrain?.IsWater == true || cell.GetFirstThing<Fire>(map) != null) return false;
		float temperature = GenTemperature.GetTemperatureForCell(cell, map);
		if (temperature < -65f || temperature > 70f) return false;
		int now = Find.TickManager?.TicksGame ?? 0;
		for (int i = 0; i < dangerMemories.Count; i++) if (dangerMemories[i].expiresTick > now && dangerMemories[i].cell.DistanceToSquared(cell) <= 100) return false;
		return true;
	}

	private void UpdateMigrations(int now)
	{
		nextMigrationTick = now + 60000;
		for (int i = dangerMemories.Count - 1; i >= 0; i--) if (now >= dangerMemories[i].expiresTick) dangerMemories.RemoveAt(i);
		for (int i = 0; i < packs.Count; i++)
		{
			PackSnapshot pack = packs[i];
			PackRecord record = pack.record;
			AnimalPackSettings config = PacksMod.Settings.For(pack.species);
			if (!config.useDens || pack.prey != null || record.claimedCorpse != null || HasYoung(pack) || now - record.lastMigrationTick < 300000 || CountPreyNear(record.den, config.territoryRadius) >= 2) continue;
			int phase = PositiveMod(now / 900000 + map.uniqueID + record.id, 4);
			Vector2 direction = phase == 0 ? Vector2.right : phase == 1 ? Vector2.up : phase == 2 ? Vector2.left : Vector2.down;
			IntVec3 wanted = new IntVec3(Mathf.Clamp(Mathf.RoundToInt(record.den.x + direction.x * config.territoryRadius * 0.75f), 2, map.Size.x - 3), 0, Mathf.Clamp(Mathf.RoundToInt(record.den.z + direction.y * config.territoryRadius * 0.75f), 2, map.Size.z - 3));
			record.den = ClosestStandable(AvoidPlayerProtectedAreas(AvoidRememberedDanger(wanted, pack.center, now), pack.center), pack.center);
			record.lastMigrationTick = now;
			EnsureDenMarker(record, config);
			if (!targetMemory.TryGetValue(pack.id, out TargetMemory memory)) targetMemory.Add(pack.id, memory = new TargetMemory());
			memory.target = record.den;
			memory.nextChangeTick = now + 2400;
			WildlifeTestLog.Count("migration.predator");
			if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("SeasonalMigration", "pack=" + pack.id + " newDen=" + record.den + " preyDensity=low", pack.leader, record.denMarker);
			if (PacksMod.Settings.enableWildlifeAlerts && IsObserved(pack.leader)) Messages.Message(pack.Label + " is migrating after prey became scarce.", pack.leader, MessageTypeDefOf.NeutralEvent, false);
			break;
		}
	}

	private int CountPreyNear(IntVec3 cell, float radius)
	{
		if (!cell.IsValid) return 0;
		int range = Mathf.CeilToInt(radius / PreyBucketSize);
		IntVec2 origin = BucketFor(cell, PreyBucketSize);
		float radiusSquared = radius * radius;
		int count = 0;
		for (int dx = -range; dx <= range; dx++) for (int dz = -range; dz <= range; dz++)
		{
			if (!preyBuckets.TryGetValue(new IntVec2(origin.x + dx, origin.z + dz), out List<Pawn> bucket)) continue;
			for (int i = 0; i < bucket.Count; i++) if (!IsPackHunter(bucket[i]) && bucket[i].RaceProps.canBePredatorPrey && bucket[i].Position.DistanceToSquared(cell) <= radiusSquared) count++;
		}
		return count;
	}

	private void UpdateFamilyLifecycle(int now)
	{
		if (now < nextFamilyLifecycleTick) return;
		nextFamilyLifecycleTick = now + 6000;
		for (int i = 0; i < records.Count; i++)
		{
			PackRecord parent = records[i];
			if (PacksMod.Settings.For(parent.species).socialStrategy != PredatorSocialStrategy.Family || parent.members.Count < 4 || now - parent.formedTick < 60000 || now - parent.lastDispersalTick < 60000) continue;
			Pawn disperser = parent.members.Where(member => member != parent.leader && member?.Spawned == true && member.ageTracker.Adult && !member.Downed && !member.InMentalState)
				.OrderBy(member => member.ageTracker.AgeBiologicalYearsFloat).ThenBy(member => member.thingIDNumber).FirstOrDefault();
			if (disperser == null) continue;
			parent.members.Remove(disperser);
			parent.lastDispersalTick = now;
			PackRecord child = CreateRecord(parent.species, parent.faction, new List<Pawn> { disperser }, now, parent);
			child.separatedUntilTick = now + 60000;
			AnimalPackSettings config = PacksMod.Settings.For(parent.species);
			IntVec3 origin = parent.den.IsValid ? parent.den : RecordCenter(parent);
			Vector2 direction = new Vector2(disperser.Position.x - origin.x, disperser.Position.z - origin.z);
			if (direction.sqrMagnitude < 0.1f)
			{
				float angle = PositiveMod(disperser.thingIDNumber * 137, 360) * Mathf.Deg2Rad;
				direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
			}
			direction.Normalize();
			IntVec3 wanted = new IntVec3(Mathf.RoundToInt(origin.x + direction.x * (config.territoryRadius + 12f)), 0, Mathf.RoundToInt(origin.z + direction.y * (config.territoryRadius + 12f)));
			child.den = ClosestStandable(wanted, disperser.Position);
			EnsureDenMarker(child, config);
			if (disperser.CanReach(child.den, PathEndMode.OnCell, Danger.Deadly))
			{
				Job job = JobMaker.MakeJob(JobDefOf.Goto, child.den);
				job.expiryInterval = 2400;
				job.locomotionUrgency = LocomotionUrgency.Jog;
				disperser.jobs.StartJob(job, JobCondition.InterruptForced);
			}
			WildlifeTestLog.Count("families.dispersed");
			if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("FamilyDispersal", "parent=" + parent.id + " newPack=" + child.id + " newDen=" + child.den, disperser, child.denMarker);
			break;
		}
	}

	private void UpdateLeadership(int now)
	{
		for (int i = 0; i < records.Count; i++)
		{
			PackRecord record = records[i];
			if (record.members.Count < 2 || now - record.lastLeadershipTick < 15000) continue;
			record.lastLeadershipTick = now;
			Pawn current = record.leader;
			Pawn best = record.members.Where(member => member?.Spawned == true && member.ageTracker.Adult && !member.Downed)
				.OrderByDescending(LeadershipScore).ThenBy(member => member.thingIDNumber).FirstOrDefault();
			if (best == null || best == current) continue;
			float currentScore = current?.Spawned == true && !current.Downed ? LeadershipScore(current) : 0f;
			if (currentScore > 0f && best.health.summaryHealth.SummaryHealthPercent > 0.5f && LeadershipScore(best) < currentScore * 1.3f) continue;
			record.leader = best;
			WildlifeTestLog.Count("leadership.changes");
			if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("LeadershipChanged", "pack=" + record.id + " old=" + (current?.thingIDNumber.ToString() ?? "none") + " score=" + currentScore.ToString("0.0") + " newScore=" + LeadershipScore(best).ToString("0.0"), best, current);
		}
	}

	private static float LeadershipScore(Pawn pawn)
	{
		float agePenalty = pawn.RaceProps.lifeExpectancy > 0f ? Mathf.Max(0f, pawn.ageTracker.AgeBiologicalYearsFloat / pawn.RaceProps.lifeExpectancy - 0.75f) * 0.7f : 0f;
		return EffectiveCombatPower(pawn) * pawn.health.summaryHealth.SummaryHealthPercent * (1f - agePenalty);
	}

	private void ClaimCarcass(PackSnapshot pack, Pawn prey, int now)
	{
		Corpse corpse = prey?.Corpse;
		if (pack?.record == null || corpse?.Spawned != true) return;
		pack.record.claimedCorpse = corpse;
		pack.record.corpseClaimUntilTick = now + 5000;
		WildlifeTestLog.Count("carcasses.claimed");
		if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("CarcassClaimed", "pack=" + pack.id + " until=" + pack.record.corpseClaimUntilTick, pack.leader, corpse);
	}

	private void UpdateTerritories(int now)
	{
		nextTerritoryTick = now + 1200;
		UpdateCarcassConflicts(now);
		territoryBuckets.Clear();
		for (int i = 0; i < packs.Count; i++)
		{
			PackSnapshot pack = packs[i];
			if (pack.record?.den.IsValid != true) continue;
			IntVec2 bucket = BucketFor(pack.record.den, TerritoryBucketSize);
			if (!territoryBuckets.TryGetValue(bucket, out List<PackSnapshot> list)) territoryBuckets.Add(bucket, list = new List<PackSnapshot>());
			list.Add(pack);
		}
		int processed = 0;
		foreach (KeyValuePair<IntVec2, List<PackSnapshot>> bucket in territoryBuckets)
		{
			if (processed >= 12) break;
			for (int dx = -2; dx <= 2 && processed < 12; dx++) for (int dz = -2; dz <= 2 && processed < 12; dz++)
			{
				if (!territoryBuckets.TryGetValue(new IntVec2(bucket.Key.x + dx, bucket.Key.z + dz), out List<PackSnapshot> nearby)) continue;
				for (int a = 0; a < bucket.Value.Count && processed < 12; a++) for (int b = 0; b < nearby.Count && processed < 12; b++)
				{
					PackSnapshot first = bucket.Value[a];
					PackSnapshot second = nearby[b];
					if (first.id >= second.id) continue;
					if (first.prey != null || second.prey != null) continue;
					float overlapDistance = Mathf.Min(PacksMod.Settings.For(first.species).territoryRadius, PacksMod.Settings.For(second.species).territoryRadius);
					if (first.record.den.DistanceToSquared(second.record.den) > overlapDistance * overlapDistance) continue;
					long key = ((long)first.id << 32) | (uint)second.id;
					if (territoryCooldown.TryGetValue(key, out int cooldown) && now < cooldown) continue;
					territoryCooldown[key] = now + 5000;
					processed++;
					ResolveTerritoryInteraction(first, second, now);
				}
			}
		}
		foreach (long stale in territoryCooldown.Keys.Where(key => territoryCooldown[key] < now - 60000).ToList()) territoryCooldown.Remove(stale);
	}

	private void UpdateCarcassConflicts(int now)
	{
		int processed = 0;
		for (int i = 0; i < packs.Count && processed < 8; i++)
		{
			PackSnapshot owner = packs[i];
			Corpse corpse = owner.record?.claimedCorpse;
			if (corpse?.Spawned != true || now >= owner.record.corpseClaimUntilTick) continue;
			for (int j = 0; j < packs.Count && processed < 8; j++)
			{
				PackSnapshot intruder = packs[j];
				if (intruder == owner || intruder.prey != null || intruder.center.DistanceToSquared(corpse.Position) > 196) continue;
				long key = ((((long)Mathf.Min(owner.id, intruder.id) << 32) | (uint)Mathf.Max(owner.id, intruder.id)) ^ 0x40000000L);
				if (territoryCooldown.TryGetValue(key, out int cooldown) && now < cooldown) continue;
				territoryCooldown[key] = now + 3000;
				processed++;
				if (PackStrength(owner) >= PackStrength(intruder) * 0.8f && owner.leader?.Spawned == true && intruder.leader?.Spawned == true && owner.leader.Position.InHorDistOf(intruder.leader.Position, 8f))
				{
					Job warningAttack = JobMaker.MakeJob(JobDefOf.AttackMelee, intruder.leader);
					warningAttack.maxNumMeleeAttacks = 1;
					warningAttack.expiryInterval = 120;
					owner.leader.jobs.StartJob(warningAttack, JobCondition.InterruptForced);
					LogTerritory("carcass-defense", owner, intruder);
				}
				else
				{
					Vector2 away = new Vector2(intruder.center.x - corpse.Position.x, intruder.center.z - corpse.Position.z).normalized;
					if (!targetMemory.TryGetValue(intruder.id, out TargetMemory memory)) targetMemory.Add(intruder.id, memory = new TargetMemory());
					memory.target = ClosestStandable(new IntVec3(Mathf.RoundToInt(intruder.center.x + away.x * 14f), 0, Mathf.RoundToInt(intruder.center.z + away.y * 14f)), intruder.center);
					memory.nextChangeTick = now + 1800;
					intruder.movementTarget = memory.target;
					BuildRoots(intruder);
					LogTerritory("carcass-yield", owner, intruder);
				}
			}
		}
	}

	private void ResolveTerritoryInteraction(PackSnapshot first, PackSnapshot second, int now)
	{
		float firstStrength = PackStrength(first);
		float secondStrength = PackStrength(second);
		bool comparable = firstStrength <= secondStrength * 1.25f && secondStrength <= firstStrength * 1.25f;
		bool hostile = first.leader?.Spawned == true && second.leader?.Spawned == true && first.leader.HostileTo(second.leader);
		bool limitedFight = comparable && first.leader?.Spawned == true && second.leader?.Spawned == true && first.leader.Position.InHorDistOf(second.leader.Position, 10f) && PositiveMod(first.id * 31 + second.id * 17 + now / 5000, 4) == 0;
		if ((hostile || limitedFight) && first.leader?.Spawned == true && second.leader?.Spawned == true)
		{
			Pawn attacker = firstStrength >= secondStrength ? first.leader : second.leader;
			Pawn defender = attacker == first.leader ? second.leader : first.leader;
			Job fight = JobMaker.MakeJob(JobDefOf.AttackMelee, defender);
			fight.maxNumMeleeAttacks = 1;
			fight.expiryInterval = 180;
			attacker.jobs.StartJob(fight, JobCondition.InterruptForced);
			LogTerritory("fight", first, second);
			if (PacksMod.Settings.enableWildlifeAlerts && (IsObserved(first.leader) || IsObserved(second.leader))) Messages.Message("Observed predator groups are fighting over territory.", attacker, MessageTypeDefOf.NegativeEvent, false);
			return;
		}
		if (comparable)
		{
			IntVec3 midpoint = new IntVec3((first.record.den.x + second.record.den.x) / 2, 0, (first.record.den.z + second.record.den.z) / 2);
			StartPosture(first.leader, midpoint, first.record.den);
			StartPosture(second.leader, midpoint, second.record.den);
			LogTerritory("posture", first, second);
			return;
		}
		PackSnapshot weaker = firstStrength <= secondStrength ? first : second;
		PackSnapshot stronger = weaker == first ? second : first;
		Vector2 away = new Vector2(weaker.record.den.x - stronger.record.den.x, weaker.record.den.z - stronger.record.den.z).normalized;
		IntVec3 wanted = new IntVec3(Mathf.RoundToInt(weaker.record.den.x + away.x * 18f), 0, Mathf.RoundToInt(weaker.record.den.z + away.y * 18f));
		if (!targetMemory.TryGetValue(weaker.id, out TargetMemory memory)) targetMemory.Add(weaker.id, memory = new TargetMemory());
		memory.target = ClosestStandable(wanted, weaker.center);
		memory.nextChangeTick = now + 2400;
		weaker.movementTarget = memory.target;
		BuildRoots(weaker);
		LogTerritory("avoid", first, second);
	}

	private void StartPosture(Pawn leader, IntVec3 midpoint, IntVec3 ownDen)
	{
		if (leader?.Spawned != true || leader.Downed || leader.InMentalState) return;
		Vector2 towardHome = new Vector2(ownDen.x - midpoint.x, ownDen.z - midpoint.z).normalized;
		IntVec3 cell = ClosestStandable(new IntVec3(Mathf.RoundToInt(midpoint.x + towardHome.x * 4f), 0, Mathf.RoundToInt(midpoint.z + towardHome.y * 4f)), leader.Position);
		Job posture = JobMaker.MakeJob(JobDefOf.Goto, cell);
		posture.expiryInterval = 300;
		posture.locomotionUrgency = LocomotionUrgency.Walk;
		leader.jobs.StartJob(posture, JobCondition.InterruptForced);
	}

	private static float PackStrength(PackSnapshot pack)
	{
		float strength = 0f;
		for (int i = 0; i < pack.members.Count; i++) if (!pack.members[i].Downed) strength += EffectiveCombatPower(pack.members[i]) * pack.members[i].health.summaryHealth.SummaryHealthPercent;
		return strength;
	}

	private void LogTerritory(string action, PackSnapshot first, PackSnapshot second)
	{
		territoryInteractions++;
		WildlifeTestLog.Count("territory." + action);
		if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("TerritoryConflict", "action=" + action + " first=" + first.id + " second=" + second.id, first.leader, second.leader);
	}

	public bool DebugStartHuntTest(Pawn hunter, Pawn firstPrey, int runs)
	{
		if (hunter?.Spawned != true || firstPrey?.Spawned != true || hunter == firstPrey) return false;
		EnsureCurrent();
		if (!packByPawn.TryGetValue(hunter, out PackSnapshot pack)) return false;
		if (huntTest != null) FinishHuntTest("replaced by new test");
		huntTest = new HuntTestSession
		{
			packId = pack.id,
			preySpecies = firstPrey.def,
			requestedRuns = Mathf.Clamp(runs, 1, 20),
			currentPrey = firstPrey,
			runStartTick = Find.TickManager.TicksGame,
			startingHunterHealth = PackHealth(pack)
		};
		huntTest.usedPreyIds.Add(firstPrey.thingIDNumber);
		if (!DebugForceHunt(hunter, firstPrey))
		{
			huntTest = null;
			AbandonHunt(pack, Find.TickManager.TicksGame);
			return false;
		}
		if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("AutoHuntTest", "started runs=" + runs + " preySpecies=" + firstPrey.def.defName, hunter, firstPrey);
		return true;
	}

	public void DebugStartBenchmark(int durationTicks)
	{
		int now = Find.TickManager.TicksGame;
		benchmark = new BenchmarkSession
		{
			startTick = now,
			endTick = now + Mathf.Clamp(durationTicks, 15000, 900000),
			startingPathRequests = totalPathRequests,
			startingFailedPaths = failedPathRequests
		};
		Log.Message("[WildlifeBenchmark][Packs] START duration=" + (benchmark.endTick - now).ToStringTicksToPeriod() + " packs=" + packs.Count);
	}

	private void UpdateBenchmark(int now)
	{
		if (benchmark == null) return;
		benchmark.samples++;
		benchmark.rebuildMicros += lastRebuildMicroseconds;
		benchmark.huntMicros += lastHuntScanMicroseconds;
		benchmark.peakRebuildMicros = Math.Max(benchmark.peakRebuildMicros, lastRebuildMicroseconds);
		benchmark.peakHuntMicros = Math.Max(benchmark.peakHuntMicros, lastHuntScanMicroseconds);
		for (int i = 0; i < packs.Count; i++)
		{
			for (int j = 0; j < packs[i].members.Count; j++)
			{
				Pawn pawn = packs[i].members[j];
				if (pawn?.Spawned != true || pawn.CurJob == null) continue;
				if (!benchmark.lastCell.TryGetValue(pawn, out IntVec3 last) || last != pawn.Position)
				{
					benchmark.lastCell[pawn] = pawn.Position;
					benchmark.lastMovedTick[pawn] = now;
				}
				else if (benchmark.lastMovedTick.TryGetValue(pawn, out int lastMoved) && now - lastMoved >= 600 && (pawn.CurJobDef == JobDefOf.Goto || pawn.CurJobDef == JobDefOf.PredatorHunt))
				{
					benchmark.stuckJobs++;
					benchmark.lastMovedTick[pawn] = now;
				}
			}
		}
		if (now < benchmark.endTick) return;
		long pathRequests = totalPathRequests - benchmark.startingPathRequests;
		long failedPaths = failedPathRequests - benchmark.startingFailedPaths;
		string report = "[WildlifeBenchmark][Packs] COMPLETE duration=" + (now - benchmark.startTick).ToStringTicksToPeriod() + " samples=" + benchmark.samples + " rebuildAvg=" + (benchmark.samples > 0 ? benchmark.rebuildMicros / benchmark.samples : 0) + "us rebuildPeak=" + benchmark.peakRebuildMicros + "us huntAvg=" + (benchmark.samples > 0 ? benchmark.huntMicros / benchmark.samples : 0) + "us huntPeak=" + benchmark.peakHuntMicros + "us pathRequests=" + pathRequests + " failedPaths=" + failedPaths + " stuckJobs=" + benchmark.stuckJobs + " packs=" + packs.Count + " territoryEvents=" + territoryInteractions;
		Log.Message(report);
		Messages.Message("Predator benchmark complete. Report written to the log.", MessageTypeDefOf.NeutralEvent, historical: false);
		benchmark = null;
	}

	public void DebugCancelHuntTest()
	{
		if (huntTest == null) return;
		int packId = huntTest.packId;
		FinishHuntTest("cancelled");
		PackSnapshot pack = packs.FirstOrDefault(candidate => candidate.id == packId);
		if (pack != null) AbandonHunt(pack, Find.TickManager.TicksGame);
	}

	private void UpdateHuntTest(int now)
	{
		if (huntTest == null) return;
		PackSnapshot pack = packs.FirstOrDefault(candidate => candidate.id == huntTest.packId);
		if (pack == null || pack.leader?.Spawned != true)
		{
			FinishHuntTest("pack unavailable");
			return;
		}
		if (huntTest.currentPrey != null)
		{
			if (now - huntTest.runStartTick > 12000)
			{
				Pawn timedOutPrey = huntTest.currentPrey;
				CompleteTestRun(pack, timedOutPrey, "abandoned", now);
				AbandonHunt(pack, now);
			}
			return;
		}
		if (huntTest.completedRuns >= huntTest.requestedRuns)
		{
			FinishHuntTest("complete");
			return;
		}
		if (now < huntTest.nextRunTick) return;
		Pawn next = map.mapPawns.AllPawnsSpawned.Where(candidate => candidate.def == huntTest.preySpecies && !candidate.Dead && !IsPackHunter(candidate) && !huntTest.usedPreyIds.Contains(candidate.thingIDNumber))
			.OrderBy(candidate => candidate.Position.DistanceToSquared(pack.center)).FirstOrDefault();
		if (next == null)
		{
			FinishHuntTest("no unused prey of target species");
			return;
		}
		huntTest.currentPrey = next;
		huntTest.usedPreyIds.Add(next.thingIDNumber);
		huntTest.runStartTick = now;
		huntTest.startingHunterHealth = PackHealth(pack);
		if (!DebugForceHunt(pack.leader, next))
		{
			CompleteTestRun(pack, next, "abandoned", now);
			AbandonHunt(pack, now);
		}
	}

	private void CompleteTestRun(PackSnapshot pack, Pawn prey, string outcome, int now)
	{
		if (huntTest == null || huntTest.packId != pack.id || huntTest.currentPrey != prey) return;
		huntTest.completedRuns++;
		huntTest.totalDurationTicks += Mathf.Max(0, now - huntTest.runStartTick);
		huntTest.totalHunterInjury += Mathf.Max(0f, huntTest.startingHunterHealth - PackHealth(pack));
		if (outcome == "kill") huntTest.kills++;
		else
		{
			huntTest.escapes++;
			if (outcome == "hidden") huntTest.hiddenEscapes++;
			if (outcome == "abandoned") huntTest.abandoned++;
		}
		huntTest.currentPrey = null;
		huntTest.nextRunTick = now + 180;
	}

	private void FinishHuntTest(string reason)
	{
		HuntTestSession test = huntTest;
		if (test == null) return;
		float averageSeconds = test.completedRuns > 0 ? test.totalDurationTicks / 60f / test.completedRuns : 0f;
		string summary = "[WildlifeTest][Packs][TEST SUMMARY] reason=" + reason + " runs=" + test.completedRuns + "/" + test.requestedRuns + " kills=" + test.kills + " escapes=" + test.escapes + " hidden=" + test.hiddenEscapes + " abandoned=" + test.abandoned + " detection=" + test.detections + "/" + test.detectionRolls + " injuries=" + test.totalHunterInjury.ToString("0.000") + " avgDuration=" + averageSeconds.ToString("0.0") + "s";
		Log.Message(summary);
		Messages.Message("Automated hunt test finished: " + test.kills + " kills, " + test.escapes + " escapes. Summary written to log.", MessageTypeDefOf.NeutralEvent, historical: false);
		huntTest = null;
	}

	private static float PackHealth(PackSnapshot pack)
	{
		float health = 0f;
		for (int i = 0; i < pack.members.Count; i++) if (pack.members[i] != null && !pack.members[i].Dead) health += pack.members[i].health.summaryHealth.SummaryHealthPercent;
		return health;
	}

	private static long ElapsedMicroseconds(long started)
	{
		return (Stopwatch.GetTimestamp() - started) * 1000000L / Stopwatch.Frequency;
	}

	private static IntVec2 BucketFor(IntVec3 cell, int divisor)
	{
		return new IntVec2(FloorDiv(cell.x, divisor), FloorDiv(cell.z, divisor));
	}

	private static int FindRoot(int[] parent, int value)
	{
		while (parent[value] != value)
		{
			parent[value] = parent[parent[value]];
			value = parent[value];
		}
		return value;
	}

	private static void Union(int[] parent, int a, int b)
	{
		a = FindRoot(parent, a);
		b = FindRoot(parent, b);
		if (a != b)
		{
			parent[b] = a;
		}
	}

	private static int PositiveMod(int value, int modulus)
	{
		int num = value % modulus;
		if (num >= 0)
		{
			return num;
		}
		return num + modulus;
	}
}
