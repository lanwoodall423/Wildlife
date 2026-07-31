using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Herds
{
    public enum HerdDefenseMode
    {
        None,
        Flight,
        Scatter,
        ProtectYoung,
        Hide,
        Freeze,
        StandGround
    }

    public sealed class HerdDefenseOrder
    {
        public HerdDefenseMode mode;
        public IntVec3 destination;
        public Thing threat;
        public Thing refuge;
        public bool guardian;
        public bool treeWaypoint;
        public bool exitMap;
    }

    public sealed class HerdSnapshot
    {
        public int id;
        public ThingDef species;
        public Faction faction;
        public CompAnimalPenMarker pen;
        public Pawn leader;
        public Pawn sentinel;
        public IntVec3 center;
        public IntVec3 movementTarget;
        public HerdDefenseMode defenseMode;
        public Thing defenseThreat;
        public bool simulatedHunt;
        public bool groundFeeding;
        public int youngCount;
        public PreyProfile profile;
        public readonly List<Pawn> members = new List<Pawn>();

        public string Label
        {
            get
            {
                string kind = profile?.socialType == PreySocialType.Herd ? "herd" :
                    profile?.socialType == PreySocialType.Flock ? "flock" :
                    profile?.socialType == PreySocialType.Colony ? "colony" :
                    profile?.socialType == PreySocialType.Family ? "family" : "prey";
                return species.LabelCap + " " + kind;
            }
        }
    }

    public sealed class HiddenPreyRecord : IExposable
    {
        public Pawn pawn;
        public Thing refuge;
        public Thing threat;
        public IntVec3 cell;
        public int minimumExitTick;
        public int maximumExitTick;

        public void ExposeData()
        {
            Scribe_References.Look(ref pawn, "pawn");
            Scribe_References.Look(ref refuge, "refuge");
            Scribe_References.Look(ref threat, "threat");
            Scribe_Values.Look(ref cell, "cell");
            Scribe_Values.Look(ref minimumExitTick, "minimumExitTick");
            Scribe_Values.Look(ref maximumExitTick, "maximumExitTick");
        }
    }

    public sealed class PreyDangerMemoryRecord : IExposable
    {
        public IntVec3 cell;
        public int expiresTick;

        public void ExposeData()
        {
            Scribe_Values.Look(ref cell, "cell");
            Scribe_Values.Look(ref expiresTick, "expiresTick", 0);
        }
    }

    public sealed class HerdMapComponent : MapComponent, IThingHolder
    {
        private struct GroupKey : IEquatable<GroupKey>
        {
            public ThingDef species;
            public Faction faction;
            public CompAnimalPenMarker pen;

            public bool Equals(GroupKey other) => species == other.species && faction == other.faction && pen == other.pen;
            public override bool Equals(object obj) => obj is GroupKey other && Equals(other);
            public override int GetHashCode() => (species?.shortHash ?? 0) * 397 ^ (faction?.loadID ?? -1) * 31 ^ (pen?.parent?.thingIDNumber ?? 0);
        }

        private sealed class TargetMemory
        {
            public IntVec3 target;
            public int nextChangeTick;
        }

        private sealed class DefenseMemory
        {
            public Thing threat;
            public int expiresTick;
            public HerdDefenseMode mode;
            public bool forced;
            public bool simulated;
            public int reactionTick;
            public bool alarmRaised;
            public bool alarmPropagated;
            public bool signalBroadcast;
        }

        private sealed class BenchmarkSession
        {
            public int startTick;
            public int endTick;
            public int samples;
            public long rebuildMicros;
            public long defenseMicros;
            public long peakRebuildMicros;
            public long peakDefenseMicros;
            public int stuckJobs;
            public long startingPathRequests;
            public long startingFailedPaths;
            public readonly Dictionary<Pawn, IntVec3> lastCell = new Dictionary<Pawn, IntVec3>();
            public readonly Dictionary<Pawn, int> lastMovedTick = new Dictionary<Pawn, int>();
        }

        private const int RefugeBucketSize = 12;
        private readonly List<HerdSnapshot> herds = new List<HerdSnapshot>();
        private readonly Dictionary<Pawn, HerdSnapshot> herdByPawn = new Dictionary<Pawn, HerdSnapshot>();
        private readonly Dictionary<Pawn, IntVec3> rootByPawn = new Dictionary<Pawn, IntVec3>();
        private readonly Dictionary<CompAnimalPenMarker, List<HerdSnapshot>> herdsByPen = new Dictionary<CompAnimalPenMarker, List<HerdSnapshot>>();
        private readonly Dictionary<Region, CompAnimalPenMarker> penByRegion = new Dictionary<Region, CompAnimalPenMarker>();
        private readonly Dictionary<int, TargetMemory> targetMemory = new Dictionary<int, TargetMemory>();
        private readonly Dictionary<int, DefenseMemory> defenseMemory = new Dictionary<int, DefenseMemory>();
        private readonly Dictionary<Pawn, HerdDefenseOrder> defenseByPawn = new Dictionary<Pawn, HerdDefenseOrder>();
        private readonly Dictionary<IntVec2, List<Thing>> refugeBuckets = new Dictionary<IntVec2, List<Thing>>();
        private readonly Dictionary<Thing, int> refugeReservations = new Dictionary<Thing, int>();
        private readonly Dictionary<Pawn, int> hideRetryAfter = new Dictionary<Pawn, int>();
        private Dictionary<Pawn, Thing> homeRefugeByPawn = new Dictionary<Pawn, Thing>();
        private readonly Dictionary<Thing, List<Pawn>> hiddenByRefuge = new Dictionary<Thing, List<Pawn>>();
        private readonly Dictionary<Thing, List<Pawn>> homesByRefuge = new Dictionary<Thing, List<Pawn>>();
        private Dictionary<Thing, int> abandonedHomeTick = new Dictionary<Thing, int>();
        private readonly List<Thing> refugeCandidates = new List<Thing>(32);
        private readonly List<Thing> orphanedBurrows = new List<Thing>();
        private readonly List<Building_WildlifeTool> observationPosts = new List<Building_WildlifeTool>();
        private readonly List<Building_WildlifeTool> baitStations = new List<Building_WildlifeTool>();
        private readonly List<Building_WildlifeTool> waterStations = new List<Building_WildlifeTool>();
        private readonly List<Building_WildlifeTool> predatorDeterrents = new List<Building_WildlifeTool>();
        private readonly List<Building_WildlifeTool> wildlifeReserves = new List<Building_WildlifeTool>();
        private readonly List<Pawn> playerObserversScratch = new List<Pawn>();
        private readonly List<Pawn> hiddenThreatScratch = new List<Pawn>();
        private readonly HashSet<Thing> drawnOccupiedRefuges = new HashSet<Thing>();
        private readonly HashSet<Pawn> fearEscapes = new HashSet<Pawn>();
        private ThingOwner<Pawn> hiddenPawns;
        private List<HiddenPreyRecord> hiddenRecords = new List<HiddenPreyRecord>();
        private List<PreyDangerMemoryRecord> dangerMemories = new List<PreyDangerMemoryRecord>();
        private Dictionary<Pawn, int> observedUntilTick = new Dictionary<Pawn, int>();
        private Dictionary<string, int> populationBySpecies = new Dictionary<string, int>();
        private int nextRefreshTick;
        private int nextPenRefreshTick;
        private int nextDefenseTick;
        private int nextRefugeRefreshTick;
        private bool initialized;
        private long rebuildTotalMicroseconds;
        private long defenseTotalMicroseconds;
        private int rebuildRuns;
        private int defenseRuns;
        private long lastRebuildMicroseconds;
        private long lastDefenseMicroseconds;
        private int pathRequestsSinceRebuild;
        private int treeRouteJobs;
        private int nextSentinelTick;
        private int nextInfluenceRefreshTick;
        private int nextPopulationCheckTick;
        private int nextSoloBirdTick;
        private int lastMigrationSeason = -1;
        private int alarmsRaised;
        private int falseAlarms;
        private long totalPathRequests;
        private long failedPathRequests;
        private BenchmarkSession benchmark;

        public HerdMapComponent(Map map) : base(map)
        {
            hiddenPawns = new ThingOwner<Pawn>(this) { dontTickContents = true };
        }

        public IThingHolder ParentHolder => null;
        public ThingOwner GetDirectlyHeldThings() => hiddenPawns;
        public void GetChildHolders(List<IThingHolder> outChildren) => ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, hiddenPawns);

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.Look(ref hiddenPawns, "hiddenPawns", this);
            Scribe_Collections.Look(ref hiddenRecords, "hiddenRecords", LookMode.Deep);
            Scribe_Collections.Look(ref dangerMemories, "dangerMemories", LookMode.Deep);
            Scribe_Collections.Look(ref observedUntilTick, "observedUntilTick", LookMode.Reference, LookMode.Value);
            Scribe_Collections.Look(ref populationBySpecies, "populationBySpecies", LookMode.Value, LookMode.Value);
            Scribe_Values.Look(ref lastMigrationSeason, "lastMigrationSeason", -1);
            Scribe_Collections.Look(ref homeRefugeByPawn, "homeRefugeByPawn", LookMode.Reference, LookMode.Reference);
            Scribe_Collections.Look(ref abandonedHomeTick, "abandonedHomeTick", LookMode.Reference, LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (hiddenPawns == null) hiddenPawns = new ThingOwner<Pawn>(this) { dontTickContents = true };
                hiddenPawns.dontTickContents = true;
                hiddenRecords = hiddenRecords?.Where(record => record?.pawn != null && hiddenPawns.Contains(record.pawn)).ToList() ?? new List<HiddenPreyRecord>();
                if (dangerMemories == null) dangerMemories = new List<PreyDangerMemoryRecord>();
                if (observedUntilTick == null) observedUntilTick = new Dictionary<Pawn, int>();
                if (populationBySpecies == null) populationBySpecies = new Dictionary<string, int>();
                if (homeRefugeByPawn == null) homeRefugeByPawn = new Dictionary<Pawn, Thing>();
                if (abandonedHomeTick == null) abandonedHomeTick = new Dictionary<Thing, int>();
                foreach (Pawn stale in homeRefugeByPawn.Keys.Where(pawn => pawn == null || pawn.Dead || homeRefugeByPawn[pawn] == null || homeRefugeByPawn[pawn].DestroyedOrNull()).ToList()) homeRefugeByPawn.Remove(stale);
                foreach (Thing stale in abandonedHomeTick.Keys.Where(home => home == null || home.DestroyedOrNull()).ToList()) abandonedHomeTick.Remove(stale);
                RebuildOccupancyIndexes();
            }
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            Rebuild();
        }

        public override void MapComponentTick()
        {
            if (!HerdsMod.Settings.enablePreyAndHerds) return;
            int now = Find.TickManager.TicksGame;
            if (now >= nextRefreshTick) Rebuild();
            if (HerdsMod.Settings.enableDefensiveBehavior && now >= nextDefenseTick) UpdateDefense(now);
            if (HerdsMod.Settings.enableDefensiveBehavior && now >= nextSentinelTick) UpdateSentinels(now);
            if (now >= nextInfluenceRefreshTick) RebuildPlayerInfluences(now);
            if (HerdsMod.Settings.enableEcologicalConsequences && now >= nextPopulationCheckTick) UpdatePopulationEcology(now);
            if (now >= nextSoloBirdTick) UpdateSoloBirdLifecycle(now);
            if (now % 60 == 0) UpdateHiddenPrey(now);
            if (benchmark != null && now % 300 == 0) UpdateBenchmark(now);
        }

        public override void MapComponentDraw()
        {
            base.MapComponentDraw();
            if (Find.CurrentMap != map) return;
            CellRect view = Find.CameraDriver.CurrentViewRect;
            foreach (KeyValuePair<Thing, List<Pawn>> pair in homesByRefuge)
            {
                Thing home = pair.Key;
                if (pair.Value.Count > 0 && home?.Spawned == true && home is Plant plant && plant.def.plant?.IsTree == true && view.Contains(home.Position))
                    GenDraw.DrawRadiusRing(home.Position, 0.62f, new Color(0.95f, 0.68f, 0.18f, 0.9f));
            }
            if (!Prefs.DevMode || !HerdsDebugActions.OverlayEnabled) return;
            for (int i = 0; i < herds.Count; i++)
            {
                HerdSnapshot herd = herds[i];
                if (!view.Contains(herd.center)) continue;
                Color color = DebugStateColor(herd.defenseMode);
                GenDraw.DrawRadiusRing(herd.center, Mathf.Clamp(Mathf.Sqrt(herd.members.Count), 0.6f, 6f), color);
                GenDraw.DrawLineBetween(herd.center.ToVector3Shifted(), herd.movementTarget.ToVector3Shifted());
                if (herd.defenseThreat?.Spawned == true) GenDraw.DrawLineBetween(herd.center.ToVector3Shifted(), herd.defenseThreat.Position.ToVector3Shifted(), SimpleColor.Red);
                if (WildlifeDevMaster.CompleteOverlayEnabled)
                {
                    for (int memberIndex = 0; memberIndex < herd.members.Count; memberIndex++)
                    {
                        Pawn member = herd.members[memberIndex]; if (member?.Spawned != true || !view.Contains(member.Position)) continue;
                        GenDraw.DrawRadiusRing(member.Position, 0.35f, color);
                        if (member == herd.sentinel) GenDraw.DrawRadiusRing(member.Position, 1f, Color.yellow);
                        if (member == herd.leader) GenDraw.DrawRadiusRing(member.Position, 0.72f, Color.white);
                        float awareness = 8f + VigilanceFor(member) * 18f;
                        GenDraw.DrawRadiusRing(member.Position, awareness, new Color(color.r, color.g, color.b, 0.28f));
                        Thing home = HomeFor(member); if (home?.Spawned == true) GenDraw.DrawLineBetween(member.Position.ToVector3Shifted(), home.Position.ToVector3Shifted(), SimpleColor.Green);
                    }
                }
            }
            if (HerdsDebugActions.RefugeOverlayEnabled)
            {
                foreach (List<Thing> bucket in refugeBuckets.Values)
                    for (int i = 0; i < bucket.Count; i++)
                        if (bucket[i]?.Spawned == true && view.Contains(bucket[i].Position)) GenDraw.DrawRadiusRing(bucket[i].Position, 0.45f, Color.green);
                for (int i = 0; i < hiddenRecords.Count; i++)
                    if (view.Contains(hiddenRecords[i].cell))
                    {
                        GenDraw.DrawRadiusRing(hiddenRecords[i].cell, 0.75f, Color.magenta);
                        if (WildlifeDevMaster.CompleteOverlayEnabled)
                        {
                            GenDraw.DrawRadiusRing(hiddenRecords[i].cell, HerdsMod.Settings.hiddenPreySafeDistance, new Color(1f, 0f, 1f, 0.32f));
                            if (hiddenRecords[i].threat?.Spawned == true) GenDraw.DrawLineBetween(hiddenRecords[i].cell.ToVector3Shifted(), hiddenRecords[i].threat.Position.ToVector3Shifted(), SimpleColor.Red);
                        }
                    }
            }
            if (WildlifeDevMaster.CompleteOverlayEnabled)
            {
                Action<List<Building_WildlifeTool>, Color> drawTools = (tools, color) => { for (int i = 0; i < tools.Count; i++) if (tools[i]?.Spawned == true && view.Contains(tools[i].Position)) GenDraw.DrawRadiusRing(tools[i].Position, tools[i].InfluenceRadius, color); };
                drawTools(observationPosts, Color.cyan); drawTools(baitStations, Color.yellow); drawTools(predatorDeterrents, Color.red); drawTools(wildlifeReserves, Color.green);
                Vector2 wind = WildlifeFieldcraftMapComponent.WindVector(map); IntVec3 windEnd = map.Center + new IntVec3(Mathf.RoundToInt(wind.x * 20f), 0, Mathf.RoundToInt(wind.y * 20f));
                GenDraw.DrawLineBetween(map.Center.ToVector3Shifted(), windEnd.ToVector3Shifted(), SimpleColor.Cyan);
            }
        }

        public override void MapComponentOnGUI()
        {
            base.MapComponentOnGUI();
            if (Find.CurrentMap != map) return;
            if (Prefs.DevMode && HerdsDebugActions.PerformanceOverlayEnabled)
            {
                Rect panel = new Rect(12f, 224f, 410f, 190f);
                Widgets.DrawMenuSection(panel);
                Text.Font = GameFont.Small;
                Widgets.Label(panel.ContractedBy(8f), PerformanceSummary());
            }
            if (Prefs.DevMode && WildlifeDevMaster.CompleteOverlayEnabled)
            {
                CellRect currentView = Find.CameraDriver.CurrentViewRect;
                for (int herdIndex = 0; herdIndex < herds.Count; herdIndex++)
                {
                    HerdSnapshot herd = herds[herdIndex];
                    for (int memberIndex = 0; memberIndex < herd.members.Count; memberIndex++)
                    {
                        Pawn member = herd.members[memberIndex]; if (member?.Spawned != true || !currentView.Contains(member.Position)) continue;
                        string rank = member == herd.leader ? " leader" : member == herd.sentinel ? " sentinel" : "";
                        GenMapUI.DrawThingLabel(member, herd.defenseMode + rank + " | " +
                            WildlifeLifeUtility.PersonalityLabel(member) + " | vig " +
                            VigilanceFor(member).ToString("0.00") + " | " +
                            (member.CurJobDef?.defName ?? "idle"));
                    }
                }
            }
            if (hiddenRecords.Count == 0) return;
            drawnOccupiedRefuges.Clear();
            CellRect view = Find.CameraDriver.CurrentViewRect;
            for (int i = 0; i < hiddenRecords.Count; i++)
            {
                Thing refuge = hiddenRecords[i].refuge;
                if (refuge?.Spawned != true || !Find.Selector.IsSelected(refuge) || !view.Contains(refuge.Position) || !drawnOccupiedRefuges.Add(refuge)) continue;
                GenMapUI.DrawThingLabel(refuge, "Hiding: " + HiddenCountAt(refuge));
            }
        }

        public string PerformanceSummary()
        {
            long averageRebuild = rebuildRuns > 0 ? rebuildTotalMicroseconds / rebuildRuns : 0;
            long averageDefense = defenseRuns > 0 ? defenseTotalMicroseconds / defenseRuns : 0;
            return "Herds and Hiders performance\n" +
                "Rebuild: " + lastRebuildMicroseconds + " us last / " + averageRebuild + " us avg\n" +
                "Defense: " + lastDefenseMicroseconds + " us last / " + averageDefense + " us avg\n" +
                "Groups: " + herds.Count + "   Defending: " + defenseByPawn.Count + "   Hidden: " + hiddenRecords.Count + "\n" +
                "Refuge buckets: " + refugeBuckets.Count + "   Homes: " + homeRefugeByPawn.Count + "   Abandoned: " + abandonedHomeTick.Count + "\n" +
                "Path checks/rebuild: " + pathRequestsSinceRebuild + "   Tree routes: " + treeRouteJobs + "   Alarms: " + alarmsRaised + " (false " + falseAlarms + ")\n" +
                "Player tools: " + (observationPosts.Count + baitStations.Count + predatorDeterrents.Count + wildlifeReserves.Count) + "   Documented animals: " + observedUntilTick.Count + "\n" +
                "Remembered dangers: " + dangerMemories.Count + "   Benchmark: " + (benchmark == null ? "idle" : Mathf.Max(0, benchmark.endTick - (Find.TickManager?.TicksGame ?? 0)).ToStringTicksToPeriod() + " remaining");
        }

        private static Color DebugStateColor(HerdDefenseMode mode)
        {
            switch (mode)
            {
                case HerdDefenseMode.Flight: return Color.yellow;
                case HerdDefenseMode.Scatter: return new Color(1f, 0.55f, 0.1f);
                case HerdDefenseMode.Hide: return Color.magenta;
                case HerdDefenseMode.ProtectYoung: return new Color(0.2f, 0.65f, 1f);
                case HerdDefenseMode.Freeze: return Color.blue;
                case HerdDefenseMode.StandGround: return Color.red;
                default: return Color.cyan;
            }
        }

        public List<string> DebugSummaryLines()
        {
            EnsureCurrent();
            int threatened = herds.Count(herd => herd.defenseMode != HerdDefenseMode.None);
            return new List<string>
            {
                "MAP " + map.Index + " | tick " + Find.TickManager.TicksGame + " | biome " + (map.Biome?.LabelCap.ToString() ?? "none"),
                "PREY groups=" + herds.Count + " visibleMembers=" + herdByPawn.Count + " threatenedGroups=" + threatened + " hidden=" + hiddenRecords.Count,
                "HOMES claimed=" + homeRefugeByPawn.Count + " occupiedRefuges=" + hiddenByRefuge.Count + " abandoned=" + abandonedHomeTick.Count,
                "PLAYER TOOLS observation=" + observationPosts.Count + " bait=" + baitStations.Count + " deterrent=" + predatorDeterrents.Count + " reserves=" + wildlifeReserves.Count,
                "DEFENSE orders=" + defenseByPawn.Count + " rememberedThreats=" + defenseMemory.Count + " alarms=" + alarmsRaised + " false=" + falseAlarms,
                "DIAGNOSTICS overlay=" + WildlifeDevMaster.CompleteOverlayEnabled + " logging=" + WildlifeTestLog.Enabled
            };
        }

        public List<string> DebugPreyLines()
        {
            EnsureCurrent(); List<string> lines = new List<string>();
            for (int i = 0; i < herds.Count; i++)
            {
                HerdSnapshot herd = herds[i];
                lines.Add("GROUP " + herd.id + " | " + herd.Label + " | members=" + herd.members.Count + " young=" + herd.youngCount + " | state=" + herd.defenseMode + " | center=" + herd.center + " target=" + herd.movementTarget + " | threat=" + (herd.defenseThreat?.LabelShortCap.ToString() ?? "none"));
                for (int j = 0; j < herd.members.Count; j++)
                {
                    Pawn pawn = herd.members[j]; Thing home = HomeFor(pawn);
                    lines.Add("  " + pawn.LabelShortCap + " | job=" + (pawn.CurJobDef?.defName ?? "none") + " | vigilance=" + VigilanceFor(pawn).ToString("0.00") + " | home=" + (home?.LabelShortCap.ToString() ?? "none") + "@" + (home?.Position.ToString() ?? "-") + " | health=" + pawn.health.summaryHealth.SummaryHealthPercent.ToStringPercent());
                }
            }
            for (int i = 0; i < hiddenRecords.Count; i++)
            {
                HiddenPreyRecord hidden = hiddenRecords[i];
                lines.Add("HIDDEN " + hidden.pawn?.LabelShortCap + " | refuge=" + hidden.refuge?.LabelShortCap + "@" + hidden.cell + " | threat=" + (hidden.threat?.LabelShortCap.ToString() ?? "none") + " | minExit=" + hidden.minimumExitTick + " | safeRange=" + HerdsMod.Settings.hiddenPreySafeDistance.ToString("0"));
            }
            return lines.Count > 0 ? lines : new List<string> { "No eligible prey groups." };
        }

        public List<string> DebugHomeAndToolLines()
        {
            List<string> lines = new List<string>();
            foreach (KeyValuePair<Pawn, Thing> pair in homeRefugeByPawn) lines.Add("HOME " + pair.Key?.LabelShortCap + " -> " + pair.Value?.LabelShortCap + " @ " + pair.Value?.Position + " | hidden=" + HiddenCountAt(pair.Value) + " residents=" + HomeCountAt(pair.Value));
            Action<string, List<Building_WildlifeTool>> addTools = (label, tools) => { for (int i = 0; i < tools.Count; i++) lines.Add("TOOL " + label + " @ " + tools[i].Position + " | active=" + tools[i].active + " radius=" + tools[i].InfluenceRadius); };
            addTools("observation", observationPosts); addTools("bait", baitStations); addTools("deterrent", predatorDeterrents); addTools("reserve", wildlifeReserves);
            return lines.Count > 0 ? lines : new List<string> { "No homes or active wildlife tools." };
        }

        public HerdSnapshot HerdFor(Pawn pawn)
        {
            EnsureCurrent();
            return pawn != null && herdByPawn.TryGetValue(pawn, out HerdSnapshot herd) ? herd : null;
        }

        public IReadOnlyList<HerdSnapshot> AllHerds
        {
            get
            {
                EnsureCurrent();
                return herds;
            }
        }

        public void LearnDanger(Pawn animal, IntVec3 cell, int duration)
        {
            if (HerdsMod.Settings?.enableAnimalMemory != true || animal?.RaceProps?.Animal != true) return;
            RememberDanger(cell, (Find.TickManager?.TicksGame ?? 0) + Mathf.Max(60000, duration));
        }

        public IReadOnlyList<HerdSnapshot> HerdsFor(CompAnimalPenMarker pen)
        {
            EnsureCurrent();
            return pen != null && herdsByPen.TryGetValue(pen, out List<HerdSnapshot> result) ? result : Array.Empty<HerdSnapshot>();
        }

        public IntVec3 WanderRootFor(Pawn pawn, IntVec3 fallback)
        {
            if (pawn?.Spawned != true || pawn.Downed || pawn.InMentalState) return fallback;
            PreyProfile profile = PreyProfileDatabase.For(pawn.def);
            if (profile?.IsSocial != true || (!HerdsMod.Settings.coordinateWildHerds && pawn.Faction != Faction.OfPlayer)) return fallback;
            EnsureCurrent();
            if (profile.socialType == PreySocialType.Flock && pawn.Faction != Faction.OfPlayer)
            {
                int hour = GenLocalDate.HourOfDay(pawn);
                if ((hour >= 19 || hour < 6) && homeRefugeByPawn.TryGetValue(pawn, out Thing roost) && roost?.Spawned == true)
                    return roost.Position;
            }
            if (pawn.Faction == null && GenLocalDate.Season(map) == Season.Winter &&
                homeRefugeByPawn.TryGetValue(pawn, out Thing winterHome) && winterHome?.Spawned == true)
                return winterHome.Position;
            return rootByPawn.TryGetValue(pawn, out IntVec3 root) && root.IsValid ? root : fallback;
        }

        public HerdDefenseOrder DefenseOrderFor(Pawn pawn)
        {
            if (!HerdsMod.Settings.enableDefensiveBehavior || pawn?.Spawned != true || pawn.Downed || pawn.InMentalState) return null;
            EnsureCurrent();
            return defenseByPawn.TryGetValue(pawn, out HerdDefenseOrder order) ? order : null;
        }

        public void NotifyThreat(Pawn member, Thing threat, int durationTicks = 900)
        {
            if (!HerdsMod.Settings.enableDefensiveBehavior || member?.Spawned != true || threat?.Spawned != true) return;
            EnsureCurrent();
            if (!herdByPawn.TryGetValue(member, out HerdSnapshot herd)) return;
            if (defenseMemory.TryGetValue(herd.id, out DefenseMemory existing) && existing.simulated) return;
            RememberThreat(herd, threat, Find.TickManager.TicksGame + Mathf.Max(300, durationTicks));
            defenseMemory[herd.id].forced = false;
            nextDefenseTick = 0;
        }

        public void NotifyThreatEnded(Pawn member, Thing threat)
        {
            if (member == null) return;
            if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("ThreatEnded", "Threat end notification received.", member, threat);
            if (!member.Spawned && hiddenPawns.Contains(member))
            {
                for (int i = 0; i < hiddenRecords.Count; i++)
                {
                    HiddenPreyRecord record = hiddenRecords[i];
                    if (record.pawn == member && (threat == null || record.threat == threat)) record.threat = null;
                }
                return;
            }
            if (!member.Spawned) return;
            EnsureCurrent();
            if (!herdByPawn.TryGetValue(member, out HerdSnapshot herd) || !defenseMemory.TryGetValue(herd.id, out DefenseMemory memory) || memory.simulated) return;
            if (threat != null && memory.threat != threat) return;
            memory.expiresTick = Mathf.Min(memory.expiresTick, Find.TickManager.TicksGame + 60);
            nextDefenseTick = 0;
        }

        public bool DebugTriggerDefense(Pawn member, Thing threat, HerdDefenseMode? forcedMode)
        {
            if (member?.Spawned != true || threat?.Spawned != true) return false;
            EnsureCurrent();
            if (!herdByPawn.TryGetValue(member, out HerdSnapshot herd)) return false;
            if (defenseMemory.TryGetValue(herd.id, out DefenseMemory existing) && existing.simulated)
            {
                existing.simulated = false;
                existing.expiresTick = 0;
            }
            RememberThreat(herd, threat, Find.TickManager.TicksGame + 3000);
            DefenseMemory memory = defenseMemory[herd.id];
            memory.simulated = false;
            memory.forced = forcedMode.HasValue;
            memory.reactionTick = Find.TickManager.TicksGame;
            if (forcedMode.HasValue) memory.mode = forcedMode.Value;
            UpdateDefense(Find.TickManager.TicksGame);
            return true;
        }

        public bool DebugStartHunted(Pawn member, Thing threat)
        {
            if (member?.Spawned != true || threat?.Spawned != true) return false;
            EnsureCurrent();
            if (!herdByPawn.TryGetValue(member, out HerdSnapshot herd)) return false;
            RememberThreat(herd, threat, int.MaxValue);
            DefenseMemory memory = defenseMemory[herd.id];
            memory.threat = threat;
            memory.expiresTick = int.MaxValue;
            memory.forced = false;
            memory.simulated = true;
            memory.reactionTick = Find.TickManager.TicksGame;
            UpdateDefense(Find.TickManager.TicksGame);
            return true;
        }

        public bool DebugStopHunted(Pawn member)
        {
            if (member == null) return false;
            EnsureCurrent();
            if (!herdByPawn.TryGetValue(member, out HerdSnapshot herd) || !defenseMemory.TryGetValue(herd.id, out DefenseMemory memory) || !memory.simulated) return false;
            for (int i = 0; i < herd.members.Count; i++)
            {
                Pawn pawn = herd.members[i];
                if (defenseByPawn.TryGetValue(pawn, out HerdDefenseOrder order) && IsDefenseJob(pawn, order)) pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, true);
                defenseByPawn.Remove(pawn);
            }
            defenseMemory.Remove(herd.id);
            herd.defenseMode = HerdDefenseMode.None;
            herd.defenseThreat = null;
            herd.simulatedHunt = false;
            return true;
        }

        public bool IsSimulatedHunt(Pawn member)
        {
            EnsureCurrent();
            return member != null && herdByPawn.TryGetValue(member, out HerdSnapshot herd) && defenseMemory.TryGetValue(herd.id, out DefenseMemory memory) && memory.simulated;
        }

        public int DebugRevealAllHidden()
        {
            int count = hiddenRecords.Count;
            for (int i = hiddenRecords.Count - 1; i >= 0; i--) Emerge(hiddenRecords[i], i);
            return count;
        }

        public void DebugRefresh()
        {
            ForceRefresh();
            Rebuild();
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
            Log.Message("[WildlifeBenchmark][Herds] START duration=" + (benchmark.endTick - now).ToStringTicksToPeriod() + " groups=" + herds.Count);
        }

        private void UpdateBenchmark(int now)
        {
            if (benchmark == null) return;
            benchmark.samples++;
            benchmark.rebuildMicros += lastRebuildMicroseconds;
            benchmark.defenseMicros += lastDefenseMicroseconds;
            benchmark.peakRebuildMicros = Math.Max(benchmark.peakRebuildMicros, lastRebuildMicroseconds);
            benchmark.peakDefenseMicros = Math.Max(benchmark.peakDefenseMicros, lastDefenseMicroseconds);
            for (int i = 0; i < herds.Count; i++)
            {
                for (int j = 0; j < herds[i].members.Count; j++)
                {
                    Pawn pawn = herds[i].members[j];
                    if (pawn?.Spawned != true || pawn.CurJob == null) continue;
                    if (!benchmark.lastCell.TryGetValue(pawn, out IntVec3 last) || last != pawn.Position)
                    {
                        benchmark.lastCell[pawn] = pawn.Position;
                        benchmark.lastMovedTick[pawn] = now;
                    }
                    else if (benchmark.lastMovedTick.TryGetValue(pawn, out int lastMoved) && now - lastMoved >= 600 && (pawn.CurJobDef == JobDefOf.Goto || pawn.CurJobDef == HerdsDefOf.Herds_Hide))
                    {
                        benchmark.stuckJobs++;
                        benchmark.lastMovedTick[pawn] = now;
                    }
                }
            }
            if (now < benchmark.endTick) return;
            string report = "[WildlifeBenchmark][Herds] COMPLETE duration=" + (now - benchmark.startTick).ToStringTicksToPeriod() + " samples=" + benchmark.samples + " rebuildAvg=" + (benchmark.samples > 0 ? benchmark.rebuildMicros / benchmark.samples : 0) + "us rebuildPeak=" + benchmark.peakRebuildMicros + "us defenseAvg=" + (benchmark.samples > 0 ? benchmark.defenseMicros / benchmark.samples : 0) + "us defensePeak=" + benchmark.peakDefenseMicros + "us pathRequests=" + (totalPathRequests - benchmark.startingPathRequests) + " failedPaths=" + (failedPathRequests - benchmark.startingFailedPaths) + " stuckJobs=" + benchmark.stuckJobs + " groups=" + herds.Count + " hidden=" + hiddenRecords.Count + " alarms=" + alarmsRaised + " falseAlarms=" + falseAlarms;
            Log.Message(report);
            Messages.Message("Prey benchmark complete. Report written to the log.", MessageTypeDefOf.NeutralEvent, false);
            benchmark = null;
        }

        public bool TryHide(Pawn pawn, Thing refuge, Thing threat)
        {
            if (pawn?.Spawned != true || refuge?.Spawned != true || pawn.Map != map || refuge.Map != map)
            {
                if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("HideRejected", "Pawn/refuge was unspawned or on the wrong map.", pawn, refuge ?? threat);
                return false;
            }
            PreyProfile profile = PreyProfileDatabase.For(pawn.def);
            if (profile?.eligible != true || !ValidRefuge(refuge, pawn, profile, false) || HiddenCountAt(refuge) >= RefugeCapacity(refuge))
            {
                if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("HideRejected", "Ineligible pawn, invalid refuge for species, or refuge at capacity. preference=" + profile?.refugePreference + " hidden=" + HiddenCountAt(refuge) + " capacity=" + RefugeCapacity(refuge), pawn, refuge);
                return false;
            }
            int now = Find.TickManager.TicksGame;
            float chance = profile.hideSuccessChance;
            chance += refuge.TryGetComp<CompHidingRefuge>() != null ? 0.12f : -0.05f;
            if (threat?.Spawned == true)
            {
                float distance = pawn.Position.DistanceTo(threat.Position);
                chance += Mathf.InverseLerp(3f, 18f, distance) * 0.18f - 0.12f;
                if (threat is Pawn predator && !predator.Downed)
                {
                    float preySpeed = pawn.GetStatValue(StatDefOf.MoveSpeed);
                    float predatorSpeed = predator.GetStatValue(StatDefOf.MoveSpeed);
                    chance += Mathf.Clamp((preySpeed - predatorSpeed) * 0.04f, -0.12f, 0.12f);
                }
            }
            chance = Mathf.Clamp(chance, 0.05f, 0.95f);
            TestRollMode overrideMode = Prefs.DevMode ? WildlifeTestLog.HideOutcome : TestRollMode.Natural;
            bool success = overrideMode == TestRollMode.ForceSuccess || (overrideMode == TestRollMode.Natural && Rand.Chance(chance));
            WildlifeTestLog.Count("hide.attempts");
            WildlifeTestLog.Count(success ? "hide.successes" : "hide.failures");
            if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("HideRoll", "chance=" + chance.ToString("0.000") + " override=" + overrideMode + " result=" + (success ? "success" : "failure") + " threat=" + (threat?.LabelShortCap.ToString() ?? "none"), pawn, refuge);
            if (!success)
            {
                RememberDanger(refuge.Position, now + 12000);
                hideRetryAfter[pawn] = now + Mathf.Max(120, HerdsMod.Settings.failedHideRetryTicks);
                if (herdByPawn.TryGetValue(pawn, out HerdSnapshot herd)) AddScatterOrder(pawn, herd, threat, herd.members.IndexOf(pawn));
                return false;
            }
            IntVec3 cell = pawn.Position;
            pawn.DeSpawnOrDeselect();
            if (!hiddenPawns.TryAdd(pawn))
            {
                GenSpawn.Spawn(pawn, cell, map);
                return false;
            }
            hiddenRecords.Add(new HiddenPreyRecord
            {
                pawn = pawn,
                refuge = refuge,
                threat = threat,
                cell = refuge.Position,
                minimumExitTick = now + HerdsMod.Settings.minimumHideTicks,
                maximumExitTick = now + HerdsMod.Settings.maximumHideTicks
            });
            AddToRefugeIndex(hiddenByRefuge, refuge, pawn);
            defenseByPawn.Remove(pawn);
            ForceRefresh();
            if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("HideEntered", "Prey despawned into refuge; exitWindow=" + (now + HerdsMod.Settings.minimumHideTicks) + "-" + (now + HerdsMod.Settings.maximumHideTicks), pawn, refuge);
            return true;
        }

        public void CancelHideAssignment(Pawn pawn)
        {
            if (pawn != null) defenseByPawn.Remove(pawn);
        }

        public bool IsHidden(Pawn pawn) => pawn != null && hiddenPawns.Contains(pawn);

        public Thing HiddenRefugeFor(Pawn pawn)
        {
            if (pawn == null) return null;
            for (int i = 0; i < hiddenRecords.Count; i++)
                if (hiddenRecords[i]?.pawn == pawn) return hiddenRecords[i].refuge;
            return null;
        }

        public bool TryHitTreeHiddenPrey(Thing refuge, DamageInfo source)
        {
            if (!(refuge is Plant plant) || plant.def.plant?.IsTree != true || !hiddenByRefuge.TryGetValue(refuge, out List<Pawn> hidden) || hidden.Count == 0) return false;
            Pawn shooter = source.Instigator as Pawn;
            bool ranged = source.Weapon?.IsRangedWeapon == true || shooter?.equipment?.Primary?.def.IsRangedWeapon == true;
            if (!ranged) return false;
            float shooting = shooter?.skills?.GetSkill(SkillDefOf.Shooting)?.Level ?? 0f;
            float chance = Mathf.Clamp(0.25f + shooting * 0.02f, 0.25f, 0.65f);
            Pawn target = hidden.FirstOrDefault(candidate => candidate != null && !candidate.Dead && !candidate.Destroyed);
            bool hit = target != null && Rand.Chance(chance);
            if (hit)
            {
                DamageInfo reduced = source;
                reduced.SetAmount(Mathf.Max(1f, source.Amount * 0.65f));
                target.TakeDamage(reduced);
            }
            if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("TreeShot", "accuracy=" + chance.ToString("0.00") + " result=" + (hit ? "hit" : "miss") + " damageScale=0.65", target, refuge);
            return hit;
        }

        public IReadOnlyList<Pawn> HiddenPreyAt(Thing refuge)
        {
            if (refuge == null) return Array.Empty<Pawn>();
            return hiddenByRefuge.TryGetValue(refuge, out List<Pawn> hidden) ? hidden : Array.Empty<Pawn>();
        }

        public IReadOnlyList<Pawn> HomePreyAt(Thing refuge)
        {
            if (refuge == null) return Array.Empty<Pawn>();
            return homesByRefuge.TryGetValue(refuge, out List<Pawn> homes) ? homes : Array.Empty<Pawn>();
        }

        public Thing HomeFor(Pawn pawn)
        {
            if (pawn == null) return null;
            EnsureCurrent();
            return homeRefugeByPawn.TryGetValue(pawn, out Thing home) && home?.Spawned == true ? home : null;
        }

        public float VigilanceFor(Pawn pawn)
        {
            PreyProfile profile = PreyProfileDatabase.For(pawn?.def);
            if (profile?.eligible != true) return 0.5f;
            float vigilance = profile.vigilanceChance;
            if (pawn?.Spawned == true)
            {
                EnsureCurrent();
                if (herdByPawn.TryGetValue(pawn, out HerdSnapshot herd) && herd.members.Count > 1)
                {
                    vigilance += Mathf.Min(0.2f, Mathf.Sqrt(herd.members.Count - 1) * 0.035f);
                    if (herd.groundFeeding) vigilance -= 0.16f;
                }
                if (HerdsMod.Settings.enableJuvenileLearning)
                    vigilance += (pawn.Map.GetComponent<RegionalWildlifeMapComponent>()?.LearningFactor(pawn) ?? 0f) * 0.12f;
            }
            vigilance *= WildlifeLifeUtility.VigilanceFactor(pawn);
            return Mathf.Clamp(vigilance, 0.05f, 0.95f);
        }

        public bool TryGetHomeRestCell(Pawn pawn, out IntVec3 cell)
        {
            cell = IntVec3.Invalid;
            if (pawn?.Spawned != true || pawn.Map != map || pawn.Downed || pawn.InMentalState) return false;
            PreyProfile profile = PreyProfileDatabase.For(pawn.def);
            if (profile?.eligible != true || profile.refugePreference == PreyRefugePreference.None || pawn.BodySize > profile.maximumHidingBodySize) return false;
            EnsureCurrent();
            Thing home = HomeFor(pawn);
            if (home == null || !ValidRefuge(home, pawn, profile, false)) home = FindRefuge(pawn, profile, false);
            if (home == null) return false;
            int radialCount = GenRadial.NumCellsInRadius(3.9f);
            for (int i = 0; i < radialCount; i++)
            {
                IntVec3 candidate = home.Position + GenRadial.RadialPattern[i];
                if (!candidate.InBounds(map) || !candidate.Standable(map) || candidate.IsForbidden(pawn) || candidate.GetTerrain(map).avoidWander || !pawn.CanReserve(candidate)) continue;
                if (!pawn.CanReach(candidate, PathEndMode.OnCell, Danger.Deadly)) continue;
                cell = candidate;
                return true;
            }
            return false;
        }

        public bool DebugEnsureHome(Pawn pawn, out Thing home)
        {
            home = null;
            if (pawn?.Spawned != true || pawn.Map != map || pawn.Downed || pawn.InMentalState) return false;
            PreyProfile profile = PreyProfileDatabase.For(pawn.def);
            if (profile?.eligible != true || profile.refugePreference == PreyRefugePreference.None || pawn.BodySize > profile.maximumHidingBodySize) return false;
            EnsureCurrent();
            home = HomeFor(pawn);
            if (home == null || !ValidRefuge(home, pawn, profile, false)) home = FindRefuge(pawn, profile, false);
            return home != null;
        }

        public bool DebugSetHome(Pawn pawn, Thing refuge)
        {
            if (pawn?.Spawned != true || pawn.Map != map || refuge?.Spawned != true || refuge.Map != map) return false;
            PreyProfile profile = PreyProfileDatabase.For(pawn.def);
            if (profile?.eligible != true || pawn.BodySize > profile.maximumHidingBodySize || !ValidRefuge(refuge, pawn, profile, false)) return false;
            EnsureCurrent();
            Thing current = HomeFor(pawn);
            if (current != refuge && HomeCountAt(refuge) >= RefugeCapacity(refuge)) return false;
            if (!pawn.CanReach(refuge, PathEndMode.Touch, Danger.Deadly)) return false;
            AssignHome(pawn, refuge);
            if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("DevHome", "Manual home assignment succeeded.", pawn, refuge);
            return true;
        }

        public bool DebugCreateBurrowHome(Pawn pawn, out Thing burrow)
        {
            burrow = null;
            if (pawn?.Spawned != true || pawn.Map != map || pawn.Downed || pawn.InMentalState) return false;
            PreyProfile profile = PreyProfileDatabase.For(pawn.def);
            if (profile?.eligible != true || !profile.CanUseDens || pawn.BodySize > profile.maximumHidingBodySize) return false;
            EnsureCurrent();
            burrow = TryCreateNaturalBurrow(pawn);
            if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("DevBurrow", burrow != null ? "Manual burrow creation succeeded." : "Manual burrow creation failed: no valid nearby cell.", pawn, burrow);
            return burrow != null;
        }

        public bool DebugSendHome(Pawn pawn, bool sleep)
        {
            if (!TryGetHomeRestCell(pawn, out IntVec3 cell))
            {
                if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("DevHomeJob", "Failed to find a valid home rest cell; sleep=" + sleep, pawn, HomeFor(pawn));
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
            if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("DevHomeJob", "Started " + job.def.defName + " to cell=" + cell + " sleep=" + sleep, pawn, HomeFor(pawn));
            return true;
        }

        public void ForceRefresh()
        {
            nextRefreshTick = 0;
            nextPenRefreshTick = 0;
            nextRefugeRefreshTick = 0;
            nextInfluenceRefreshTick = 0;
        }

        private void EnsureCurrent()
        {
            if (!initialized || Find.TickManager.TicksGame >= nextRefreshTick) Rebuild();
        }

        private void Rebuild()
        {
            long performanceStart = Stopwatch.GetTimestamp();
            pathRequestsSinceRebuild = 0;
            initialized = true;
            int now = Find.TickManager?.TicksGame ?? 0;
            nextRefreshTick = now + Mathf.Max(120, HerdsMod.Settings?.updateIntervalTicks ?? 300);
            herds.Clear();
            herdByPawn.Clear();
            rootByPawn.Clear();
            herdsByPen.Clear();
            fearEscapes.RemoveWhere(pawn => pawn == null || !pawn.Spawned);
            if (now >= nextPenRefreshTick || penByRegion.Count == 0)
            {
                penByRegion.Clear();
                BuildPenRegionIndex();
                nextPenRefreshTick = now + 1200;
            }
            PruneHomeAssignments(now);
            foreach (Pawn stale in hideRetryAfter.Keys.Where(pawn => pawn == null || pawn.Dead || hideRetryAfter[pawn] <= now).ToList()) hideRetryAfter.Remove(stale);
            RebuildOccupancyIndexes();
            if (now >= nextRefugeRefreshTick || refugeBuckets.Count == 0) RebuildRefugeIndex(now);

            var groups = new Dictionary<GroupKey, List<Pawn>>();
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                PreyProfile profile = PreyProfileDatabase.For(pawn.def);
                if (pawn.Dead || profile?.eligible != true) continue;
                CompAnimalPenMarker pen = null;
                Region region = pawn.GetRegion();
                if (region != null) penByRegion.TryGetValue(region, out pen);
                if (profile.socialType == PreySocialType.Solitary)
                {
                    AddGroup(new GroupKey { species = pawn.def, faction = pawn.Faction, pen = pen }, new List<Pawn> { pawn }, now, profile);
                    continue;
                }
                var key = new GroupKey { species = pawn.def, faction = pawn.Faction, pen = pen };
                if (!groups.TryGetValue(key, out List<Pawn> group)) groups.Add(key, group = new List<Pawn>());
                group.Add(pawn);
            }

            foreach (KeyValuePair<GroupKey, List<Pawn>> pair in groups)
            {
                PreyProfile profile = PreyProfileDatabase.For(pair.Key.species);
                if (pair.Key.pen != null) AddGroupsWithLimit(pair.Key, pair.Value, now, profile);
                else AddSpatialGroups(pair.Key, pair.Value, now, profile);
            }

            var activeIds = new HashSet<int>(herds.Select(herd => herd.id));
            foreach (int stale in targetMemory.Keys.Where(id => !activeIds.Contains(id)).ToList()) targetMemory.Remove(stale);
            foreach (int stale in defenseMemory.Keys.Where(id => !activeIds.Contains(id)).ToList()) defenseMemory.Remove(stale);
            UpdateDefense(now);
            lastRebuildMicroseconds = ElapsedMicroseconds(performanceStart);
            rebuildTotalMicroseconds += lastRebuildMicroseconds;
            rebuildRuns++;
        }

        private void BuildPenRegionIndex()
        {
            foreach (Building marker in map.listerBuildings.allBuildingsAnimalPenMarkers)
            {
                CompAnimalPenMarker pen = marker.TryGetComp<CompAnimalPenMarker>();
                if (pen?.PenState == null) continue;
                foreach (Region region in pen.PenState.ConnectedRegions)
                    if (!penByRegion.TryGetValue(region, out CompAnimalPenMarker existing) || pen.parent.thingIDNumber < existing.parent.thingIDNumber) penByRegion[region] = pen;
            }
        }

        private void RebuildRefugeIndex(int now)
        {
            refugeBuckets.Clear();
            List<Thing> plants = map.listerThings.ThingsInGroup(ThingRequestGroup.Plant);
            for (int i = 0; i < plants.Count; i++)
            {
                Plant plant = plants[i] as Plant;
                if (plant == null || plant.Growth < 0.45f || plant.def.plant?.IsTree != true) continue;
                AddRefuge(plant);
            }
            List<Thing> buildings = map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial);
            orphanedBurrows.Clear();
            for (int i = 0; i < buildings.Count; i++)
            {
                Thing building = buildings[i];
                if (building.TryGetComp<CompHidingRefuge>() == null) continue;
                if (building.def == HerdsDefOf.Herds_AnimalBurrow && HiddenCountAt(building) == 0 && HomeCountAt(building) == 0)
                {
                    if (!abandonedHomeTick.TryGetValue(building, out int abandonedTick)) abandonedHomeTick[building] = abandonedTick = now;
                    if (now - abandonedTick >= 120000 && !IsWithinToolInfluence(building.Position, wildlifeReserves)) orphanedBurrows.Add(building);
                    else AddRefuge(building);
                }
                else
                {
                    abandonedHomeTick.Remove(building);
                    AddRefuge(building);
                }
            }
            for (int i = 0; i < orphanedBurrows.Count; i++)
                if (!orphanedBurrows[i].Destroyed)
                {
                    abandonedHomeTick.Remove(orphanedBurrows[i]);
                    orphanedBurrows[i].Destroy(DestroyMode.Vanish);
                    WildlifeTestLog.Count("homes.decayed");
                }
            nextRefugeRefreshTick = now + Mathf.Max(600, HerdsMod.Settings.refugeRefreshIntervalTicks);
        }

        private void RebuildPlayerInfluences(int now)
        {
            nextInfluenceRefreshTick = now + 300;
            observationPosts.Clear();
            baitStations.Clear();
            waterStations.Clear();
            predatorDeterrents.Clear();
            wildlifeReserves.Clear();
            AddActiveTools(HerdsDefOf.Herds_ObservationPost, observationPosts, HerdsMod.Settings.enableObservationPosts);
            AddActiveTools(HerdsDefOf.Herds_WildlifeBait, baitStations, HerdsMod.Settings.enableWildlifeBait && WildlifeProgression.Unlocked(WildlifeCapability.FeedingGrounds));
            AddActiveTools(HerdsDefOf.Herds_WildlifeWaterSource, waterStations, HerdsMod.Settings.enableConservationActions && WildlifeProgression.Unlocked(WildlifeCapability.HabitatSupport));
            AddActiveTools(HerdsDefOf.Herds_PredatorDeterrent, predatorDeterrents, HerdsMod.Settings.enablePredatorDeterrents);
            AddActiveTools(HerdsDefOf.Herds_WildlifeReserve, wildlifeReserves, HerdsMod.Settings.enableWildlifeReserves && WildlifeProgression.Unlocked(WildlifeCapability.Stewardship));
            if (HerdsMod.Settings.enableWildlifeKnowledge)
            {
                IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
                playerObserversScratch.Clear();
                for (int i = 0; i < pawns.Count; i++) if (pawns[i]?.Spawned == true && pawns[i].Faction == Faction.OfPlayer && pawns[i].RaceProps.Humanlike && !pawns[i].Downed) playerObserversScratch.Add(pawns[i]);
                for (int i = 0; i < pawns.Count; i++)
                {
                    Pawn pawn = pawns[i];
                    if (pawn?.Spawned != true || pawn.RaceProps?.Animal != true) continue;
                    bool postObserved = observationPosts.Count > 0 && IsNearTool(pawn.Position, observationPosts, 50f);
                    bool personallyObserved = PlayerObserverNear(pawn.Position, playerObserversScratch, 18f);
                    if (postObserved || personallyObserved) observedUntilTick[pawn] = now + (postObserved ? 60000 : 15000);
                    if (HerdsMod.Settings.enableSpeciesKnowledgeProgression)
                    {
                        HuntingKnowledgeMapComponent knowledge = map.GetComponent<HuntingKnowledgeMapComponent>();
                        for (int observerIndex = 0; observerIndex < playerObserversScratch.Count; observerIndex++)
                        {
                            Pawn observer = playerObserversScratch[observerIndex];
                            bool closeStudy = observer.Position.DistanceToSquared(pawn.Position) <= 324;
                            bool manningPost = observer.CurJobDef == HerdsDefOf.Herds_ManObservationPost && observer.CurJob?.targetA.Thing is Building_WildlifeTool post && post.Position.DistanceToSquared(pawn.Position) <= post.InfluenceRadius * post.InfluenceRadius;
                            if (closeStudy || manningPost)
                            {
                                knowledge?.Learn(observer, pawn.def, manningPost ? 0.6f : 0.2f);
                            }
                        }
                    }
                }
            }
            foreach (Pawn stale in observedUntilTick.Keys.Where(pawn => pawn == null || pawn.Dead || observedUntilTick[pawn] <= now).ToList()) if (stale != null) observedUntilTick.Remove(stale);
        }

        private void AddActiveTools(ThingDef def, List<Building_WildlifeTool> target, bool enabled)
        {
            if (!enabled || def == null) return;
            List<Thing> things = map.listerThings.ThingsOfDef(def);
            for (int i = 0; i < things.Count; i++) if (things[i] is Building_WildlifeTool tool && tool.Spawned && tool.active) target.Add(tool);
        }

        private static bool IsNearTool(IntVec3 cell, List<Building_WildlifeTool> tools, float radius)
        {
            float radiusSquared = radius * radius;
            for (int i = 0; i < tools.Count; i++) if (tools[i].Position.DistanceToSquared(cell) <= radiusSquared) return true;
            return false;
        }

        private static bool IsWithinToolInfluence(IntVec3 cell, List<Building_WildlifeTool> tools)
        {
            for (int i = 0; i < tools.Count; i++)
            {
                float radius = tools[i].InfluenceRadius;
                if (tools[i].Position.DistanceToSquared(cell) <= radius * radius) return true;
            }
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

        private Building_WildlifeTool ClosestTool(IntVec3 cell, List<Building_WildlifeTool> tools, float radius)
        {
            Building_WildlifeTool best = null;
            float bestDistance = radius * radius;
            for (int i = 0; i < tools.Count; i++)
            {
                float distance = tools[i].Position.DistanceToSquared(cell);
                if (distance >= bestDistance) continue;
                best = tools[i];
                bestDistance = distance;
            }
            return best;
        }

        public bool IsObserved(Pawn pawn)
        {
            if (!HerdsMod.Settings.enableWildlifeKnowledge) return false;
            if (!HerdsMod.Settings.requireObservationForDetails || pawn?.Faction == Faction.OfPlayer) return true;
            return pawn != null && observedUntilTick.TryGetValue(pawn, out int until) && until > (Find.TickManager?.TicksGame ?? 0);
        }

        private void UpdatePopulationEcology(int now)
        {
            nextPopulationCheckTick = now + 60000;
            Dictionary<string, int> current = new Dictionary<string, int>();
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn?.Spawned != true || pawn.Dead || !PreyProfileDatabase.IsEligible(pawn.def)) continue;
                current[pawn.def.defName] = current.TryGetValue(pawn.def.defName, out int count) ? count + 1 : 1;
            }
            if (HerdsMod.Settings.enableWildlifeAlerts)
            {
                foreach (KeyValuePair<string, int> previous in populationBySpecies)
                {
                    int count = current.TryGetValue(previous.Key, out int value) ? value : 0;
                    if (previous.Value >= 5 && count <= Mathf.FloorToInt(previous.Value * 0.6f))
                    {
                        ThingDef species = DefDatabase<ThingDef>.GetNamedSilentFail(previous.Key);
                        Messages.Message((species?.LabelCap.ToString() ?? previous.Key) + " numbers have sharply declined. Predators may migrate or take greater risks.", MessageTypeDefOf.NegativeEvent, false);
                    }
                }
            }
            populationBySpecies = current;
            int season = now / 900000;
            if (lastMigrationSeason >= 0 && season != lastMigrationSeason && HerdsMod.Settings.enableWildlifeAlerts && (observationPosts.Count > 0 || wildlifeReserves.Count > 0))
                Messages.Message("Observed wildlife migration patterns are shifting with the season.", MessageTypeDefOf.NeutralEvent, false);
            lastMigrationSeason = season;
        }

        private void AddRefuge(Thing refuge)
        {
            IntVec2 bucket = BucketFor(refuge.Position);
            if (!refugeBuckets.TryGetValue(bucket, out List<Thing> list)) refugeBuckets.Add(bucket, list = new List<Thing>());
            list.Add(refuge);
        }

        private void AddSpatialGroups(GroupKey key, List<Pawn> members, int now, PreyProfile profile)
        {
            if (members.Count == 1)
            {
                AddGroup(key, members, now, profile);
                return;
            }
            float join = Mathf.Max(8f, HerdsMod.Settings?.unpennedJoinDistance ?? 24f);
            int bucketSize = Mathf.Max(4, Mathf.FloorToInt(join * 0.5f));
            var buckets = new Dictionary<IntVec2, List<int>>();
            int[] parent = new int[members.Count];
            for (int i = 0; i < members.Count; i++)
            {
                parent[i] = i;
                IntVec2 bucket = new IntVec2(FloorDiv(members[i].Position.x, bucketSize), FloorDiv(members[i].Position.z, bucketSize));
                if (!buckets.TryGetValue(bucket, out List<int> list)) buckets.Add(bucket, list = new List<int>());
                if (list.Count > 0) Union(parent, i, list[0]);
                list.Add(i);
            }
            float joinSquared = join * join;
            foreach (KeyValuePair<IntVec2, List<int>> bucket in buckets)
            {
                for (int dx = -2; dx <= 2; dx++) for (int dz = -2; dz <= 2; dz++)
                {
                    if (dx < 0 || (dx == 0 && dz <= 0)) continue;
                    if (!buckets.TryGetValue(new IntVec2(bucket.Key.x + dx, bucket.Key.z + dz), out List<int> other)) continue;
                    bool joined = false;
                    for (int a = 0; a < bucket.Value.Count && !joined; a++) for (int b = 0; b < other.Count; b++)
                    {
                        if (members[bucket.Value[a]].Position.DistanceToSquared(members[other[b]].Position) > joinSquared) continue;
                        Union(parent, bucket.Value[a], other[b]);
                        joined = true;
                        break;
                    }
                }
            }
            var components = new Dictionary<int, List<Pawn>>();
            for (int i = 0; i < members.Count; i++)
            {
                int root = FindRoot(parent, i);
                if (!components.TryGetValue(root, out List<Pawn> component)) components.Add(root, component = new List<Pawn>());
                component.Add(members[i]);
            }
            foreach (List<Pawn> component in components.Values) AddGroupsWithLimit(key, component, now, profile);
        }

        private void AddGroupsWithLimit(GroupKey key, List<Pawn> members, int now, PreyProfile profile)
        {
            int limit = profile.socialType == PreySocialType.Family ? Mathf.Clamp(profile.preferredGroupSize, 2, 12) :
                profile.socialType == PreySocialType.Colony ? Mathf.Clamp(profile.preferredGroupSize, 2, 60) :
                profile.socialType == PreySocialType.Flock ? Mathf.Clamp(profile.preferredGroupSize, 3, 40) :
                profile.socialType == PreySocialType.Herd ? Mathf.Clamp(profile.preferredGroupSize, 2, 60) : 1;
            if (profile.socialType == PreySocialType.Flock)
            {
                int seasonPhase = PositiveMod((Find.TickManager?.TicksGame ?? 0) / 900000 + map.uniqueID, 4);
                limit = Mathf.Clamp(Mathf.RoundToInt(limit * (seasonPhase == 0 || seasonPhase == 3 ? 1.35f : 0.75f)), 3, 40);
            }
            if (members.Count <= limit)
            {
                AddGroup(key, members, now, profile);
                return;
            }
            var remaining = new List<Pawn>(members);
            remaining.Sort((a, b) => a.thingIDNumber.CompareTo(b.thingIDNumber));
            WildlifeMemoryMapComponent socialMemory =
                HerdsMod.Settings.enableAnimalSocialMemory
                    ? map.GetComponent<WildlifeMemoryMapComponent>() : null;
            while (remaining.Count > 0)
            {
                Pawn seed = remaining[0];
                Dictionary<Pawn, float> affinity = socialMemory == null
                    ? null : remaining.ToDictionary(value => value,
                        value => value == seed ? 2f :
                            socialMemory.SocialAffinity(seed, value));
                remaining.Sort((a, b) =>
                {
                    if (affinity != null)
                    {
                        int social = affinity[b].CompareTo(affinity[a]);
                        if (social != 0) return social;
                    }
                    int distance = a.Position.DistanceToSquared(seed.Position).CompareTo(b.Position.DistanceToSquared(seed.Position));
                    return distance != 0 ? distance : a.thingIDNumber.CompareTo(b.thingIDNumber);
                });
                int count = Mathf.Min(limit, remaining.Count);
                var group = remaining.GetRange(0, count);
                remaining.RemoveRange(0, count);
                AddGroup(key, group, now, profile);
            }
        }

        private void AddGroup(GroupKey key, List<Pawn> members, int now, PreyProfile profile)
        {
            if (members.Count == 0) return;
            members.Sort((a, b) => a.thingIDNumber.CompareTo(b.thingIDNumber));
            int sumX = 0;
            int sumZ = 0;
            for (int i = 0; i < members.Count; i++)
            {
                sumX += members[i].Position.x;
                sumZ += members[i].Position.z;
            }
            IntVec3 center = new IntVec3(Mathf.RoundToInt(sumX / (float)members.Count), 0, Mathf.RoundToInt(sumZ / (float)members.Count));
            int id = members[0].thingIDNumber;
            if (!targetMemory.TryGetValue(id, out TargetMemory memory)) targetMemory.Add(id, memory = new TargetMemory());
            if (!memory.target.IsValid || now >= memory.nextChangeTick || !ValidForGroup(memory.target, key.pen))
            {
                memory.target = ChooseMovementTarget(key.pen, members, center, id, now, profile);
                memory.nextChangeTick = now + (profile.socialType == PreySocialType.Flock
                    ? 420 + PositiveMod(id * 31, 480)
                    : 1200 + PositiveMod(id * 31, 1200));
            }
            int youngCount = 0;
            for (int i = 0; i < members.Count; i++) if (!members[i].ageTracker.Adult) youngCount++;
            List<Pawn> adults = members.Where(member => member.ageTracker.Adult)
                .OrderBy(member => member.thingIDNumber).ToList();
            int lookoutPeriod = profile.socialType == PreySocialType.Flock ? 2500 : 7500;
            Pawn sentinel = adults.Count > 0 ? adults[PositiveMod(now / lookoutPeriod + id, adults.Count)] : members[0];
            var herd = new HerdSnapshot
            {
                id = id,
                species = key.species,
                faction = key.faction,
                pen = key.pen,
                leader = members[0],
                sentinel = sentinel,
                center = center,
                movementTarget = memory.target,
                profile = profile,
                youngCount = youngCount,
                groundFeeding = profile.socialType == PreySocialType.Flock &&
                    GenLocalDate.HourOfDay(members[0]) >= 6 && GenLocalDate.HourOfDay(members[0]) < 18 &&
                    NearbyPlantCount(center) >= 3
            };
            herd.members.AddRange(members);
            herds.Add(herd);
            if (key.pen != null)
            {
                if (!herdsByPen.TryGetValue(key.pen, out List<HerdSnapshot> penHerds)) herdsByPen.Add(key.pen, penHerds = new List<HerdSnapshot>());
                penHerds.Add(herd);
            }
            if (profile.IsSocial && members.Count > 1) BuildMemberRoots(herd);
            for (int i = 0; i < members.Count; i++) herdByPawn[members[i]] = herd;
            if (profile.socialType == PreySocialType.Flock && key.faction != Faction.OfPlayer) EnsureFlockRoost(herd);
        }

        private void BuildMemberRoots(HerdSnapshot herd)
        {
            if (herd.profile?.socialType == PreySocialType.Flock)
            {
                BuildFlockRoots(herd);
                return;
            }
            Vector2 center = new Vector2(herd.center.x, herd.center.z);
            Vector2 target = new Vector2(herd.movementTarget.x, herd.movementTarget.z);
            Vector2 direction = target - center;
            if (direction.sqrMagnitude > 0.01f) center += direction.normalized * Mathf.Min(7f, direction.magnitude);
            bool flock = herd.profile?.socialType == PreySocialType.Flock;
            float radius = Mathf.Clamp(Mathf.Sqrt(herd.members.Count) * (flock ? 0.8f : 0.55f), 1.2f, flock ? 7f : 5f);
            for (int i = 0; i < herd.members.Count; i++)
            {
                Pawn pawn = herd.members[i];
                float angle = PositiveMod(pawn.thingIDNumber * 137, 360) * Mathf.Deg2Rad;
                float distance = radius * (0.35f + PositiveMod(pawn.thingIDNumber * 53, 100) / 100f * 0.65f);
                float sweep = flock ? Mathf.Sin(angle * 2f) * radius * 0.35f : 0f;
                IntVec3 desired = new IntVec3(Mathf.RoundToInt(center.x + Mathf.Cos(angle) * distance + sweep), 0, Mathf.RoundToInt(center.y + Mathf.Sin(angle) * distance));
                IntVec3 treeRoot = herd.profile.CanClimbTrees ? ClosestTreeCell(desired) : IntVec3.Invalid;
                rootByPawn[pawn] = treeRoot.IsValid ? treeRoot : ClosestValid(desired, herd.pen, pawn.Position);
            }
        }

        private void BuildFlockRoots(HerdSnapshot flock)
        {
            Vector2 center = new Vector2(flock.center.x, flock.center.z);
            Vector2 target = new Vector2(flock.movementTarget.x, flock.movementTarget.z);
            Vector2 alignment = target - center;
            if (alignment.sqrMagnitude < 0.01f) alignment = Vector2.right;
            alignment.Normalize();
            for (int i = 0; i < flock.members.Count; i++)
            {
                Pawn bird = flock.members[i];
                Vector2 position = new Vector2(bird.Position.x, bird.Position.z);
                Vector2 cohesion = center - position;
                if (cohesion.sqrMagnitude > 0.01f) cohesion.Normalize();
                Vector2 separation = Vector2.zero;
                for (int j = 0; j < flock.members.Count; j++)
                {
                    if (i == j) continue;
                    Vector2 delta = position - new Vector2(flock.members[j].Position.x, flock.members[j].Position.z);
                    float squared = delta.sqrMagnitude;
                    if (squared > 0.01f && squared < 25f) separation += delta / squared;
                }
                Vector2 movement = alignment * 0.50f + cohesion * 0.28f + separation * 0.85f;
                if (movement.sqrMagnitude < 0.01f) movement = alignment;
                movement.Normalize();
                float spacing = 5f + PositiveMod(bird.thingIDNumber * 37, 40) / 10f;
                IntVec3 desired = new IntVec3(
                    Mathf.RoundToInt(position.x + movement.x * spacing), 0,
                    Mathf.RoundToInt(position.y + movement.y * spacing));
                rootByPawn[bird] = ClosestValid(desired, flock.pen, bird.Position);
            }
        }

        private void EnsureFlockRoost(HerdSnapshot flock)
        {
            if (flock?.members == null || flock.members.Count == 0 ||
                flock.members.All(member => homeRefugeByPawn.TryGetValue(member, out Thing home) && home?.Spawned == true)) return;
            IntVec2 origin = BucketFor(flock.center);
            Plant best = null;
            int bestDistance = 1600;
            for (int dx = -3; dx <= 3; dx++) for (int dz = -3; dz <= 3; dz++)
            {
                if (!refugeBuckets.TryGetValue(new IntVec2(origin.x + dx, origin.z + dz), out List<Thing> bucket)) continue;
                for (int i = 0; i < bucket.Count; i++)
                {
                    if (!(bucket[i] is Plant tree) || tree.def.plant?.IsTree != true || tree.Growth < 0.55f ||
                        HomeCountAt(tree) >= RefugeCapacity(tree)) continue;
                    int distance = tree.Position.DistanceToSquared(flock.center);
                    if (distance >= bestDistance) continue;
                    best = tree;
                    bestDistance = distance;
                }
            }
            if (best == null) return;
            int capacity = RefugeCapacity(best);
            for (int i = 0; i < flock.members.Count && HomeCountAt(best) < capacity; i++)
                if (!homeRefugeByPawn.ContainsKey(flock.members[i])) AssignHome(flock.members[i], best);
        }

        private IntVec3 ClosestTreeCell(IntVec3 desired)
        {
            IntVec2 origin = BucketFor(desired);
            Thing best = null;
            int bestDistance = 145;
            for (int dx = -1; dx <= 1; dx++) for (int dz = -1; dz <= 1; dz++)
            {
                if (!refugeBuckets.TryGetValue(new IntVec2(origin.x + dx, origin.z + dz), out List<Thing> bucket)) continue;
                for (int i = 0; i < bucket.Count; i++)
                {
                    Thing candidate = bucket[i];
                    if (!(candidate is Plant plant) || plant.def.plant?.IsTree != true) continue;
                    int distance = candidate.Position.DistanceToSquared(desired);
                    if (distance < bestDistance)
                    {
                        best = candidate;
                        bestDistance = distance;
                    }
                }
            }
            return best?.Position ?? IntVec3.Invalid;
        }

        private void UpdateDefense(int now)
        {
            long performanceStart = Stopwatch.GetTimestamp();
            nextDefenseTick = now + Mathf.Clamp(HerdsMod.Settings.defenseScanIntervalTicks, 30, 300);
            defenseByPawn.Clear();
            refugeReservations.Clear();
            if (herds.Count == 0)
            {
                RecordDefenseTiming(performanceStart);
                return;
            }

            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn attacker = pawns[i];
                bool playerHunt = attacker.CurJobDef == JobDefOf.Hunt && attacker.Faction == Faction.OfPlayer;
                bool activeHunt = attacker.CurJobDef == JobDefOf.PredatorHunt || attacker.CurJobDef == JobDefOf.AttackMelee || playerHunt;
                if (attacker.Dead || !activeHunt || attacker.CurJob?.targetA.HasThing != true) continue;
                Pawn prey = attacker.CurJob.targetA.Thing as Pawn;
                if (playerHunt && prey != null)
                {
                    bool concealed = IsNearTool(attacker.Position, observationPosts, 7f);
                    bool scentMasked = map.GetComponent<WildlifeFieldcraftMapComponent>()?.IsScentMasked(attacker) == true;
                    Vector2 approach = new Vector2(prey.Position.x - attacker.Position.x, prey.Position.z - attacker.Position.z).normalized;
                    float scentCarry = Vector2.Dot(WildlifeFieldcraftMapComponent.WindVector(map), approach);
                    float exposedDistance = Mathf.Lerp(20f, 32f, Mathf.InverseLerp(-1f, 1f, scentCarry));
                    float detectionDistance = scentMasked ? 10f : concealed ? 14f : exposedDistance;
                    if (!attacker.Position.InHorDistOf(prey.Position, detectionDistance)) continue;
                }
                if (prey != null && (WildlifeSpeciesClassification.IsPredator(attacker.def) ||
                    attacker.HostileTo(prey)) && herdByPawn.TryGetValue(prey, out HerdSnapshot huntedGroup))
                    RememberThreat(huntedGroup, attacker, now + 900);
            }

            if (HerdsMod.Settings.preyAvoidColonists)
            {
                IReadOnlyList<Pawn> colonists = map.mapPawns.FreeColonistsSpawned;
                for (int i = 0; i < herds.Count; i++)
                {
                    HerdSnapshot wildHerd = herds[i];
                    if (wildHerd.leader?.Faction == Faction.OfPlayer || defenseMemory.ContainsKey(wildHerd.id)) continue;
                    Pawn nearest = null;
                    float nearestDistance = float.MaxValue;
                    for (int j = 0; j < colonists.Count; j++)
                    {
                        Pawn colonist = colonists[j];
                        if (colonist?.Spawned != true || colonist.Downed) continue;
                        bool overt = colonist.Drafted || colonist.CurJobDef == JobDefOf.Hunt || colonist.CurJobDef == JobDefOf.AttackStatic || colonist.CurJobDef == JobDefOf.AttackMelee;
                        float radius = overt ? 26f : 10f;
                        if (ColonistHuntingUtility.IsSneaking(colonist))
                        {
                            float huntingSkill = ColonistHuntingUtility.HuntingSkill(colonist, wildHerd.leader?.def);
                            huntingSkill += map.GetComponent<WildlifeHuntCoordinator>()?.StealthBonus(colonist) ?? 0f;
                            radius = Mathf.Lerp(20f, 4.5f, Mathf.InverseLerp(0f, 20f, huntingSkill));
                            float glow = GenCelestial.CurCelestialSunGlow(map);
                            radius *= Mathf.Lerp(0.68f, 1.08f, glow);
                            List<Thing> coverThings = colonist.Position.GetThingList(map);
                            for (int coverIndex = 0; coverIndex < coverThings.Count; coverIndex++) if (coverThings[coverIndex] is Plant plant && plant.Growth > 0.35f) { radius *= 0.72f; break; }
                            Vector2 approach = new Vector2(wildHerd.center.x - colonist.Position.x, wildHerd.center.z - colonist.Position.z).normalized;
                            float scentCarry = Vector2.Dot(WildlifeFieldcraftMapComponent.WindVector(map), approach);
                            radius *= Mathf.Lerp(0.72f, 1.32f, Mathf.InverseLerp(-1f, 1f, scentCarry));
                        }
                        if (IsNearTool(colonist.Position, observationPosts, 7f)) radius = Mathf.Min(radius, 7f);
                        if (map.GetComponent<WildlifeFieldcraftMapComponent>()?.IsScentMasked(colonist) == true) radius *= 0.7f;
                        radius *= WildlifeMemoryUtility.AvoidanceFactor(wildHerd.leader, colonist);
                        radius *= AnimalTraditionUtility.AvoidanceFactor(wildHerd.leader, colonist);
                        radius *= map.GetComponent<WildlifeLandmarkMapComponent>()?.AvoidanceFactor(wildHerd.leader) ?? 1f;
                        radius *= WildlifeLifeUtility.AvoidanceFactor(wildHerd.leader);
                        float distance = colonist.Position.DistanceToSquared(wildHerd.center);
                        if (distance > radius * radius || distance >= nearestDistance || !GenSight.LineOfSight(colonist.Position, wildHerd.center, map)) continue;
                        nearest = colonist;
                        nearestDistance = distance;
                    }
                    if (nearest != null) RememberThreat(wildHerd, nearest, now + 600);
                }
            }

            for (int i = 0; i < herds.Count; i++)
            {
                HerdSnapshot herd = herds[i];
                herd.defenseMode = HerdDefenseMode.None;
                herd.defenseThreat = null;
                herd.simulatedHunt = false;
                if (!defenseMemory.TryGetValue(herd.id, out DefenseMemory memory))
                {
                    if (WildlifeTestLog.Enabled) WildlifeTestLog.WriteTransition("defense:" + herd.id, "Defense", "Calm", herd.leader);
                    continue;
                }
                if (now < memory.reactionTick)
                {
                    if (WildlifeTestLog.Enabled) WildlifeTestLog.WriteTransition("defense:" + herd.id, "Defense", "AlarmDelay until=" + memory.reactionTick, herd.sentinel, memory.threat);
                    continue;
                }
                if (!memory.alarmRaised)
                {
                    memory.alarmRaised = true;
                    alarmsRaised++;
                    WildlifeTestLog.Count("alarms.delayed");
                }
                if (!memory.signalBroadcast)
                {
                    memory.signalBroadcast = true;
                    WildlifeSignalKind signalKind = memory.threat is Pawn human && human.RaceProps?.Humanlike == true
                        ? WildlifeSignalKind.HumanDanger : WildlifeSignalKind.Alarm;
                    map.GetComponent<WildlifeSignalCultureMapComponent>()?.NotifyAnimalSignal(
                        herd.species, signalKind, herd.sentinel ?? herd.leader, memory.threat, true);
                }
                if (!memory.alarmPropagated)
                {
                    memory.alarmPropagated = true;
                    PropagateAlarm(herd, memory.threat, now);
                }
                bool invalidThreat = memory.threat == null || memory.threat.DestroyedOrNull() || !memory.threat.Spawned;
                bool tooFar = !memory.simulated && memory.threat != null && memory.threat.Spawned && memory.threat.Position.DistanceToSquared(herd.center) > 2500;
                if (now >= memory.expiresTick || invalidThreat || tooFar)
                {
                    if (memory.signalBroadcast)
                        map.GetComponent<WildlifeSignalCultureMapComponent>()?.NotifyAnimalSignal(
                            herd.species, WildlifeSignalKind.AllClear, herd.sentinel ?? herd.leader,
                            memory.threat, true, 24f);
                    if (WildlifeTestLog.Enabled) WildlifeTestLog.WriteTransition("defense:" + herd.id, "Defense", "Calm(reason=" + (now >= memory.expiresTick ? "expired" : invalidThreat ? "invalid-threat" : "too-far") + ")", herd.leader, memory.threat);
                    defenseMemory.Remove(herd.id);
                    continue;
                }
                if (!memory.forced)
                {
                    memory.mode = ChooseDefenseMode(herd, memory.threat);
                    if (herd.faction == null && herd.members.Any(member =>
                        member?.Spawned == true && NearMapEdge(member.Position, 18)))
                        memory.mode = HerdDefenseMode.Flight;
                }
                herd.defenseMode = memory.mode;
                herd.defenseThreat = memory.threat;
                herd.simulatedHunt = memory.simulated;
                if (WildlifeTestLog.Enabled) WildlifeTestLog.WriteTransition("defense:" + herd.id, "Defense", memory.mode + " forced=" + memory.forced + " simulated=" + memory.simulated + " members=" + herd.members.Count, herd.leader, memory.threat);
                switch (memory.mode)
                {
                    case HerdDefenseMode.ProtectYoung: BuildProtectiveOrders(herd, memory.threat); break;
                    case HerdDefenseMode.Hide: BuildHideOrders(herd, memory.threat, memory.forced); break;
                    case HerdDefenseMode.Scatter: BuildScatterOrders(herd, memory.threat); break;
                    case HerdDefenseMode.Freeze: BuildStationaryOrders(herd, memory.threat, false); break;
                    case HerdDefenseMode.StandGround: BuildStationaryOrders(herd, memory.threat, true); break;
                    default: BuildFlightOrders(herd, memory.threat); break;
                }
            }
            RecordDefenseTiming(performanceStart);
        }

        private void RememberThreat(HerdSnapshot herd, Thing threat, int expiresTick)
        {
            bool created = !defenseMemory.TryGetValue(herd.id, out DefenseMemory memory);
            if (created) defenseMemory.Add(herd.id, memory = new DefenseMemory());
            Thing previous = memory.threat;
            if (!memory.simulated && (memory.threat == null || memory.threat.DestroyedOrNull() || threat.Position.DistanceToSquared(herd.center) < memory.threat.Position.DistanceToSquared(herd.center))) memory.threat = threat;
            memory.expiresTick = Mathf.Max(memory.expiresTick, expiresTick);
            if (threat?.Spawned == true) RememberDanger(threat.Position, Find.TickManager.TicksGame + 30000);
            if (created)
            {
                float vigilance = VigilanceFor(herd.sentinel ?? herd.leader);
                bool concealedHunter = threat is Pawn hunter && hunter.Faction == Faction.OfPlayer && IsNearTool(hunter.Position, observationPosts, 7f);
                if (concealedHunter) vigilance *= 0.45f;
                bool scentMaskedHunter = threat is Pawn maskedHunter && maskedHunter.Faction == Faction.OfPlayer && map.GetComponent<WildlifeFieldcraftMapComponent>()?.IsScentMasked(maskedHunter) == true;
                if (scentMaskedHunter) vigilance *= 0.55f;
                if (threat is Pawn windHunter && windHunter.Faction == Faction.OfPlayer)
                {
                    Vector2 approach = new Vector2(herd.center.x - windHunter.Position.x, herd.center.z - windHunter.Position.z).normalized;
                    float scentCarry = Vector2.Dot(WildlifeFieldcraftMapComponent.WindVector(map), approach);
                    vigilance *= Mathf.Lerp(0.7f, 1.3f, Mathf.InverseLerp(-1f, 1f, scentCarry));
                }
                bool quickAlarm = Rand.Chance(vigilance);
                memory.reactionTick = Find.TickManager.TicksGame + (quickAlarm ? (concealedHunter || scentMaskedHunter ? 45 : 15) : (concealedHunter || scentMaskedHunter ? 180 : 90));
                memory.alarmRaised = quickAlarm;
                if (quickAlarm)
                {
                    alarmsRaised++;
                    WildlifeTestLog.Count("alarms.raised");
                }
            }
            if (WildlifeTestLog.Enabled && (created || previous != memory.threat)) WildlifeTestLog.Write("ThreatRemembered", "herd=" + herd.id + " expires=" + memory.expiresTick + " new=" + created + " replaced=" + (previous != null && previous != memory.threat), herd.leader, memory.threat);
        }

        private void UpdateSentinels(int now)
        {
            nextSentinelTick = now + 600;
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < herds.Count; i++)
            {
                HerdSnapshot herd = herds[i];
                if (herd.members.Count < 2 || defenseMemory.ContainsKey(herd.id) || PositiveMod(herd.id * 31 + now / 600, 50) != 0) continue;
                Pawn nearbyPredator = null;
                for (int j = 0; j < pawns.Count; j++)
                {
                    Pawn candidate = pawns[j];
                    if (candidate?.Spawned != true || candidate.Dead ||
                        !WildlifeSpeciesClassification.IsPredator(candidate.def) ||
                        candidate.def == herd.species) continue;
                    if (candidate.Position.DistanceToSquared(herd.center) <= 1225) { nearbyPredator = candidate; break; }
                }
                if (nearbyPredator == null) continue;
                RememberThreat(herd, nearbyPredator, now + 300);
                DefenseMemory memory = defenseMemory[herd.id];
                bool alreadyCounted = memory.alarmRaised;
                memory.reactionTick = now + 45;
                memory.alarmRaised = true;
                memory.signalBroadcast = true;
                falseAlarms++;
                if (!alreadyCounted) alarmsRaised++;
                WildlifeTestLog.Count("alarms.false");
                map.GetComponent<WildlifeSignalCultureMapComponent>()?.NotifyAnimalSignal(
                    herd.species, WildlifeSignalKind.Alarm, herd.sentinel ?? herd.leader,
                    nearbyPredator, false);
                if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("FalseAlarm", "sentinel reacted to nearby non-hunting predator", herd.sentinel, nearbyPredator);
            }
        }

        private static bool IsDefenseJob(Pawn pawn, HerdDefenseOrder order)
        {
            if (pawn?.CurJob == null) return false;
            if (pawn.CurJobDef == HerdsDefOf.Herds_Hide) return true;
            if (pawn.CurJobDef == JobDefOf.Goto && pawn.CurJob.targetA.Cell == order.destination) return true;
            if (pawn.CurJobDef == JobDefOf.Wait && (order.mode == HerdDefenseMode.Freeze || order.mode == HerdDefenseMode.StandGround)) return true;
            return pawn.CurJobDef == JobDefOf.AttackMelee && pawn.CurJob.targetA.Thing == order.threat;
        }

        private HerdDefenseMode ChooseDefenseMode(HerdSnapshot herd, Thing threat)
        {
            PreyDefenseStrategy strategy = herd.profile?.defenseStrategy ?? PreyDefenseStrategy.Flight;
            if (herd.profile?.socialType == PreySocialType.Flock)
            {
                bool aerialPredator = IsAerialPredator(threat);
                if (aerialPredator && HerdsMod.Settings.enableHiding && herd.profile.CanClimbTrees &&
                    herd.members.All(member => member.BodySize <= herd.profile.maximumHidingBodySize))
                    return HerdDefenseMode.Hide;
                return HerdDefenseMode.Scatter;
            }
            Pawn threatPawn = threat as Pawn;
            bool meleePursuit = threatPawn != null && threatPawn.equipment?.Primary?.def.IsRangedWeapon != true;
            if (meleePursuit && HerdsMod.Settings.enableHiding && herd.profile?.CanClimbTrees == true)
            {
                bool allFitTrees = true;
                for (int i = 0; i < herd.members.Count; i++)
                    if (herd.members[i].BodySize > herd.profile.maximumHidingBodySize) { allFitTrees = false; break; }
                if (allFitTrees) return HerdDefenseMode.Hide;
            }
            if (strategy == PreyDefenseStrategy.Hide && HerdsMod.Settings.enableHiding)
            {
                bool allCanHide = true;
                for (int i = 0; i < herd.members.Count; i++)
                    if (herd.members[i].BodySize > herd.profile.maximumHidingBodySize) { allCanHide = false; break; }
                if (allCanHide) return HerdDefenseMode.Hide;
            }
            if (strategy == PreyDefenseStrategy.Scatter) return HerdDefenseMode.Scatter;
            if (strategy == PreyDefenseStrategy.Freeze) return HerdDefenseMode.Freeze;
            if (strategy == PreyDefenseStrategy.StandGround) return HerdDefenseMode.StandGround;
            if (strategy == PreyDefenseStrategy.ProtectYoung || herd.profile?.socialType == PreySocialType.Herd)
            {
                int adults = 0;
                float adultBodySize = 0f;
                for (int i = 0; i < herd.members.Count; i++)
                    if (herd.members[i].ageTracker.Adult) { adults++; adultBodySize += herd.members[i].BodySize; }
                if (herd.youngCount > 0 && adults >= 3 && adultBodySize / adults >= HerdsMod.Settings.protectiveBodySizeThreshold) return HerdDefenseMode.ProtectYoung;
            }
            return HerdDefenseMode.Flight;
        }

        private void BuildHideOrders(HerdSnapshot herd, Thing threat, bool ignorePreference)
        {
            bool foundAny = false;
            for (int i = 0; i < herd.members.Count; i++)
            {
                Pawn pawn = herd.members[i];
                if (hideRetryAfter.TryGetValue(pawn, out int retryTick) && Find.TickManager.TicksGame < retryTick)
                {
                    AddScatterOrder(pawn, herd, threat, i);
                    continue;
                }
                Thing refuge = FindRefuge(pawn, herd.profile, ignorePreference);
                if (refuge == null) continue;
                foundAny = true;
                refugeReservations[refuge] = refugeReservations.TryGetValue(refuge, out int count) ? count + 1 : 1;
                IntVec3 waypoint = TreeEscapeWaypoint(pawn, refuge, threat, herd.profile);
                bool routed = waypoint.IsValid && waypoint != refuge.Position;
                if (routed) treeRouteJobs++;
                defenseByPawn[pawn] = new HerdDefenseOrder { mode = HerdDefenseMode.Hide, threat = threat, refuge = refuge, destination = routed ? waypoint : refuge.Position, treeWaypoint = routed };
            }
            if (!foundAny)
            {
                herd.defenseMode = HerdDefenseMode.Scatter;
                BuildScatterOrders(herd, threat);
                return;
            }
            for (int i = 0; i < herd.members.Count; i++)
                if (!defenseByPawn.ContainsKey(herd.members[i])) AddScatterOrder(herd.members[i], herd, threat, i);
        }

        private IntVec3 TreeEscapeWaypoint(Pawn pawn, Thing finalRefuge, Thing threat, PreyProfile profile)
        {
            if (!profile.CanClimbTrees || !(finalRefuge is Plant) || pawn.Position.DistanceToSquared(finalRefuge.Position) <= 64) return IntVec3.Invalid;
            IntVec2 origin = BucketFor(pawn.Position);
            Thing best = null;
            float bestScore = float.MaxValue;
            float currentFinalDistance = pawn.Position.DistanceToSquared(finalRefuge.Position);
            float currentThreatDistance = threat?.Spawned == true ? pawn.Position.DistanceToSquared(threat.Position) : 0f;
            for (int dx = -1; dx <= 1; dx++) for (int dz = -1; dz <= 1; dz++)
            {
                if (!refugeBuckets.TryGetValue(new IntVec2(origin.x + dx, origin.z + dz), out List<Thing> bucket)) continue;
                for (int i = 0; i < bucket.Count; i++)
                {
                    Thing candidate = bucket[i];
                    if (!(candidate is Plant) || candidate == finalRefuge || !ValidRefuge(candidate, pawn, profile, false)) continue;
                    float hopDistance = pawn.Position.DistanceToSquared(candidate.Position);
                    if (hopDistance < 9f || hopDistance > 196f) continue;
                    float finalDistance = candidate.Position.DistanceToSquared(finalRefuge.Position);
                    float threatDistance = threat?.Spawned == true ? candidate.Position.DistanceToSquared(threat.Position) : currentThreatDistance;
                    if (finalDistance >= currentFinalDistance && threatDistance <= currentThreatDistance) continue;
                    float score = finalDistance + hopDistance * 0.3f - Mathf.Max(0f, threatDistance - currentThreatDistance) * 0.35f;
                    if (score < bestScore)
                    {
                        best = candidate;
                        bestScore = score;
                    }
                }
            }
            if (best == null || !CanReachRefuge(pawn, best)) return IntVec3.Invalid;
            if (WildlifeTestLog.Enabled) WildlifeTestLog.WriteTransition("tree-route:" + pawn.thingIDNumber, "TreeRoute", "waypoint=" + best.thingIDNumber + " final=" + finalRefuge.thingIDNumber, pawn, threat);
            return best.Position;
        }

        private Thing FindRefuge(Pawn pawn, PreyProfile profile, bool ignorePreference)
        {
            float radius = Mathf.Clamp(profile.refugeSearchRadius, 6f, 50f);
            if (homeRefugeByPawn.TryGetValue(pawn, out Thing home) && ValidRefuge(home, pawn, profile, ignorePreference) &&
                home.Position.DistanceToSquared(pawn.Position) <= radius * radius && HiddenCountAt(home) + (refugeReservations.TryGetValue(home, out int homeReserved) ? homeReserved : 0) < RefugeCapacity(home) &&
                CanReachRefuge(pawn, home)) return home;
            Thing inheritedHome = InheritedHomeFor(pawn, profile, radius, ignorePreference);
            if (inheritedHome != null)
            {
                AssignHome(pawn, inheritedHome);
                return inheritedHome;
            }
            int range = Mathf.CeilToInt(radius / RefugeBucketSize);
            IntVec2 origin = BucketFor(pawn.Position);
            refugeCandidates.Clear();
            for (int dx = -range; dx <= range; dx++) for (int dz = -range; dz <= range; dz++)
            {
                if (!refugeBuckets.TryGetValue(new IntVec2(origin.x + dx, origin.z + dz), out List<Thing> bucket)) continue;
                for (int i = 0; i < bucket.Count; i++)
                {
                    Thing refuge = bucket[i];
                    if (!ValidRefuge(refuge, pawn, profile, ignorePreference) || refuge.Position.DistanceToSquared(pawn.Position) > radius * radius) continue;
                    int occupied = HiddenCountAt(refuge) + (refugeReservations.TryGetValue(refuge, out int reserved) ? reserved : 0);
                    if (occupied < RefugeCapacity(refuge)) refugeCandidates.Add(refuge);
                }
            }
            refugeCandidates.Sort((a, b) => RefugeScore(a, pawn).CompareTo(RefugeScore(b, pawn)));
            for (int i = 0; i < Mathf.Min(6, refugeCandidates.Count); i++)
            {
                Thing candidate = refugeCandidates[i];
                if (!CanReachRefuge(pawn, candidate)) continue;
                if (!homeRefugeByPawn.ContainsKey(pawn) && HomeCountAt(candidate) < RefugeCapacity(candidate)) AssignHome(pawn, candidate);
                return candidate;
            }
            if (!ignorePreference && profile.CanUseDens && HerdsMod.Settings.allowNaturalBurrows) return TryCreateNaturalBurrow(pawn);
            return null;
        }

        private Thing InheritedHomeFor(Pawn pawn, PreyProfile profile, float radius, bool ignorePreference)
        {
            if (pawn.ageTracker.Adult || !profile.IsSocial || !herdByPawn.TryGetValue(pawn, out HerdSnapshot herd)) return null;
            for (int i = 0; i < herd.members.Count; i++)
            {
                Pawn adult = herd.members[i];
                if (!adult.ageTracker.Adult || !homeRefugeByPawn.TryGetValue(adult, out Thing candidate)) continue;
                if (!ValidRefuge(candidate, pawn, profile, ignorePreference) || candidate.Position.DistanceToSquared(pawn.Position) > radius * radius || HomeCountAt(candidate) >= RefugeCapacity(candidate)) continue;
                if (!CanReachRefuge(pawn, candidate)) continue;
                WildlifeTestLog.Count("homes.inherited");
                if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("HomeInherited", "from=" + adult.thingIDNumber, pawn, candidate);
                return candidate;
            }
            return null;
        }

        private bool CanReachRefuge(Pawn pawn, Thing refuge)
        {
            pathRequestsSinceRebuild++;
            totalPathRequests++;
            bool reachable = pawn.CanReach(refuge, PathEndMode.Touch, Danger.Deadly);
            if (!reachable) failedPathRequests++;
            return reachable;
        }

        private Thing TryCreateNaturalBurrow(Pawn pawn)
        {
            if (HerdsDefOf.Herds_AnimalBurrow == null || pawn?.Spawned != true) return null;
            if (!CellFinder.TryRandomClosewalkCellNear(pawn.Position, map, 8, out IntVec3 cell, candidate =>
                candidate.InBounds(map) && candidate.Standable(map) && candidate.GetEdifice(map) == null &&
                !candidate.IsForbidden(pawn) && pawn.CanReach(candidate, PathEndMode.OnCell, Danger.Deadly))) return null;
            Thing burrow = ThingMaker.MakeThing(HerdsDefOf.Herds_AnimalBurrow);
            GenSpawn.Spawn(burrow, cell, map);
            AddRefuge(burrow);
            AssignHome(pawn, burrow);
            if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("BurrowCreated", "Natural burrow spawned and assigned at " + cell + ".", pawn, burrow);
            return burrow;
        }

        private float RefugeScore(Thing refuge, Pawn pawn)
        {
            float score = refuge.Position.DistanceToSquared(pawn.Position);
            if (refuge.TryGetComp<CompHidingRefuge>() != null) score -= 64f;
            if (HerdsMod.Settings.enableWildlifeReserves && IsWithinToolInfluence(refuge.Position, wildlifeReserves)) score -= 120f;
            return score;
        }

        private bool ValidRefuge(Thing refuge, Pawn pawn, PreyProfile profile, bool ignorePreference)
        {
            if (refuge?.Spawned != true || refuge.IsBurning() || !BasicHomeSuitability(refuge) || IsRememberedDanger(refuge.Position)) return false;
            bool den = refuge.TryGetComp<CompHidingRefuge>() != null;
            bool tree = refuge is Plant plant && plant.Growth >= 0.45f && plant.def.plant?.IsTree == true;
            if (!den && !tree) return false;
            if (ignorePreference) return true;
            if (den) return profile.CanUseDens;
            if (!tree) return false;
            return profile.CanClimbTrees;
        }

        private void BuildFlightOrders(HerdSnapshot herd, Thing threat)
        {
            Vector2 away = AwayVector(herd.center, threat.Position);
            Vector2 common = new Vector2(herd.center.x, herd.center.z) + away * HerdsMod.Settings.flightDistance;
            float spread = Mathf.Clamp(Mathf.Sqrt(herd.members.Count) * 0.45f, 1f, 4f);
            for (int i = 0; i < herd.members.Count; i++)
            {
                Pawn pawn = herd.members[i];
                if (herd.pen == null && pawn.Faction == null && NearMapEdge(pawn.Position, 18) &&
                    TryFindFearEscapeCell(pawn, away, out IntVec3 escape))
                {
                    defenseByPawn[pawn] = new HerdDefenseOrder
                    {
                        mode = HerdDefenseMode.Flight,
                        threat = threat,
                        destination = escape,
                        exitMap = true
                    };
                    if (fearEscapes.Add(pawn))
                        map.GetComponent<RegionalWildlifeMapComponent>()?.NotifyFearEmigration(pawn, escape);
                    continue;
                }
                Vector2 side = new Vector2(-away.y, away.x) * (((i % 5) - 2) * spread * 0.35f);
                Vector2 depth = away * ((i / 5) * -0.6f);
                IntVec3 wanted = new IntVec3(Mathf.RoundToInt(common.x + side.x + depth.x), 0, Mathf.RoundToInt(common.y + side.y + depth.y));
                defenseByPawn[pawn] = new HerdDefenseOrder { mode = HerdDefenseMode.Flight, threat = threat, destination = ClosestValid(wanted, herd.pen, pawn.Position) };
            }
        }

        private bool NearMapEdge(IntVec3 cell, int distance)
        {
            return cell.x <= distance || cell.z <= distance ||
                map.Size.x - 1 - cell.x <= distance || map.Size.z - 1 - cell.z <= distance;
        }

        private bool TryFindFearEscapeCell(Pawn pawn, Vector2 away, out IntVec3 result)
        {
            bool horizontal = Mathf.Abs(away.x) >= Mathf.Abs(away.y);
            int fixedAxis = horizontal
                ? (away.x < 0f ? 0 : map.Size.x - 1)
                : (away.y < 0f ? 0 : map.Size.z - 1);
            int projected = horizontal
                ? Mathf.Clamp(pawn.Position.z + Mathf.RoundToInt(away.y * 10f), 0, map.Size.z - 1)
                : Mathf.Clamp(pawn.Position.x + Mathf.RoundToInt(away.x * 10f), 0, map.Size.x - 1);
            for (int offset = 0; offset <= 20; offset++)
            {
                int signed = offset == 0 ? 0 : ((offset & 1) == 1 ? (offset + 1) / 2 : -offset / 2);
                int variable = projected + signed;
                IntVec3 cell = horizontal
                    ? new IntVec3(fixedAxis, 0, variable)
                    : new IntVec3(variable, 0, fixedAxis);
                if (!cell.InBounds(map) || !cell.Walkable(map) ||
                    !pawn.CanReach(cell, PathEndMode.OnCell, Danger.Deadly)) continue;
                result = cell;
                return true;
            }
            result = IntVec3.Invalid;
            return false;
        }

        private void BuildScatterOrders(HerdSnapshot herd, Thing threat)
        {
            bool flightBurst = herd.profile?.socialType == PreySocialType.Flock &&
                !PreyProfileDatabase.IsFlightlessBird(herd.species) && !IsAerialPredator(threat);
            if (flightBurst && HerdsDefOf.Herds_FlightBurst != null)
                for (int i = 0; i < herd.members.Count; i++)
                {
                    Pawn bird = herd.members[i];
                    if (bird.health?.hediffSet?.HasHediff(HerdsDefOf.Herds_FlightBurst) != true)
                        bird.health?.AddHediff(HerdsDefOf.Herds_FlightBurst);
                }
            for (int i = 0; i < herd.members.Count; i++) AddScatterOrder(herd.members[i], herd, threat, i);
        }

        private static bool IsAerialPredator(Thing threat) =>
            threat is Pawn pawn && WildlifeSpeciesClassification.IsPredator(pawn.def) &&
            PreyProfileDatabase.IsBird(pawn.def) && !PreyProfileDatabase.IsFlightlessBird(pawn.def);

        private void PropagateAlarm(HerdSnapshot source, Thing threat, int now)
        {
            if (source?.profile == null || source.profile.socialType == PreySocialType.Solitary ||
                threat?.Spawned != true) return;
            int alarmRadiusSquared = source.profile.socialType == PreySocialType.Flock ? 1225 : 900;
            for (int i = 0; i < herds.Count; i++)
            {
                HerdSnapshot nearby = herds[i];
                if (nearby == source || nearby.faction != source.faction ||
                    nearby.center.DistanceToSquared(source.center) > alarmRadiusSquared ||
                    defenseMemory.ContainsKey(nearby.id)) continue;
                float response = Mathf.Clamp01(0.25f + VigilanceFor(nearby.sentinel ?? nearby.leader) * 0.55f);
                if (nearby.species != source.species) response *= 0.55f;
                response *= map.GetComponent<WildlifeSignalCultureMapComponent>()?
                    .AlarmResponseFactor(source.species, threat) ?? 1f;
                if (!Rand.Chance(response)) continue;
                RememberThreat(nearby, threat, now + 600);
                DefenseMemory memory = defenseMemory[nearby.id];
                memory.reactionTick = now + 30;
                memory.alarmRaised = true;
                memory.alarmPropagated = true;
                memory.signalBroadcast = true;
                WildlifeTestLog.Count("alarms.propagated");
            }
        }

        private void AddScatterOrder(Pawn pawn, HerdSnapshot herd, Thing threat, int index)
        {
            Vector2 away = AwayVector(pawn.Position, threat.Position);
            float angle = (PositiveMod(pawn.thingIDNumber * 73 + index * 41, 91) - 45) * Mathf.Deg2Rad;
            Vector2 scattered = new Vector2(away.x * Mathf.Cos(angle) - away.y * Mathf.Sin(angle), away.x * Mathf.Sin(angle) + away.y * Mathf.Cos(angle));
            Vector2 target = new Vector2(pawn.Position.x, pawn.Position.z) + scattered * HerdsMod.Settings.flightDistance;
            IntVec3 wanted = new IntVec3(Mathf.RoundToInt(target.x), 0, Mathf.RoundToInt(target.y));
            defenseByPawn[pawn] = new HerdDefenseOrder { mode = HerdDefenseMode.Scatter, threat = threat, destination = ClosestValid(wanted, herd.pen, pawn.Position) };
        }

        private void BuildProtectiveOrders(HerdSnapshot herd, Thing threat)
        {
            int youngCount = 0;
            int adultCount = 0;
            for (int i = 0; i < herd.members.Count; i++)
            {
                if (herd.members[i].ageTracker.Adult) adultCount++;
                else youngCount++;
            }
            Vector2 away = AwayVector(herd.center, threat.Position);
            Vector2 protectedCenter = new Vector2(herd.center.x, herd.center.z) + away * 2f;
            int youngIndex = 0;
            int adultIndex = 0;
            float ringRadius = Mathf.Clamp(2.8f + Mathf.Sqrt(youngCount) * 0.45f, 3f, 6f);
            for (int i = 0; i < herd.members.Count; i++)
            {
                Pawn member = herd.members[i];
                if (member.ageTracker.Adult)
                {
                    float angle = adultIndex++ * 2f * Mathf.PI / Mathf.Max(1, adultCount);
                    IntVec3 wanted = new IntVec3(Mathf.RoundToInt(protectedCenter.x + Mathf.Cos(angle) * ringRadius), 0, Mathf.RoundToInt(protectedCenter.y + Mathf.Sin(angle) * ringRadius));
                    defenseByPawn[member] = new HerdDefenseOrder { mode = HerdDefenseMode.ProtectYoung, threat = threat, guardian = true, destination = ClosestValid(wanted, herd.pen, member.Position) };
                }
                else
                {
                    float angle = youngIndex++ * 2f * Mathf.PI / Mathf.Max(1, youngCount);
                    float radius = Mathf.Min(1.6f, 0.45f + youngCount * 0.08f);
                    IntVec3 wanted = new IntVec3(Mathf.RoundToInt(protectedCenter.x + Mathf.Cos(angle) * radius), 0, Mathf.RoundToInt(protectedCenter.y + Mathf.Sin(angle) * radius));
                    defenseByPawn[member] = new HerdDefenseOrder { mode = HerdDefenseMode.ProtectYoung, threat = threat, destination = ClosestValid(wanted, herd.pen, member.Position) };
                }
            }
        }

        private void BuildStationaryOrders(HerdSnapshot herd, Thing threat, bool guardians)
        {
            for (int i = 0; i < herd.members.Count; i++)
                defenseByPawn[herd.members[i]] = new HerdDefenseOrder { mode = guardians ? HerdDefenseMode.StandGround : HerdDefenseMode.Freeze, threat = threat, guardian = guardians, destination = herd.members[i].Position };
        }

        private void UpdateHiddenPrey(int now)
        {
            if (hiddenRecords.Count == 0) return;
            hiddenThreatScratch.Clear();
            IReadOnlyList<Pawn> spawnedPawns = map.mapPawns.AllPawnsSpawned;
            for (int pawnIndex = 0; pawnIndex < spawnedPawns.Count; pawnIndex++)
            {
                Pawn candidate = spawnedPawns[pawnIndex];
                if (candidate?.Spawned == true && !candidate.Dead && !candidate.Downed &&
                    WildlifeSpeciesClassification.IsPredator(candidate.def)) hiddenThreatScratch.Add(candidate);
            }
            float safeDistanceSquared = HerdsMod.Settings.hiddenPreySafeDistance * HerdsMod.Settings.hiddenPreySafeDistance;
            for (int i = hiddenRecords.Count - 1; i >= 0; i--)
            {
                HiddenPreyRecord record = hiddenRecords[i];
                if (record?.pawn == null || !hiddenPawns.Contains(record.pawn))
                {
                    hiddenRecords.RemoveAt(i);
                    continue;
                }
                bool refugeGone = record.refuge == null || record.refuge.DestroyedOrNull() || !record.refuge.Spawned;
                Thing blockingThreat = null;
                if (record.threat?.Spawned == true && record.threat.Map == map && !record.threat.DestroyedOrNull() && record.threat.Position.DistanceToSquared(record.cell) <= safeDistanceSquared) blockingThreat = record.threat;
                if (blockingThreat == null)
                    for (int threatIndex = 0; threatIndex < hiddenThreatScratch.Count; threatIndex++)
                        if (hiddenThreatScratch[threatIndex].Position.DistanceToSquared(record.cell) <= safeDistanceSquared) { blockingThreat = hiddenThreatScratch[threatIndex]; break; }
                if (refugeGone || (now >= record.minimumExitTick && blockingThreat == null)) Emerge(record, i);
                else if (WildlifeTestLog.Enabled && now % 600 == 0) WildlifeTestLog.WriteTransition("hide-held:" + record.pawn.thingIDNumber, "HideHeld", "safeDistance=" + HerdsMod.Settings.hiddenPreySafeDistance.ToString("0") + " blocker=" + (blockingThreat?.LabelShortCap.ToString() ?? "minimum-time"), record.pawn, blockingThreat ?? record.refuge);
            }
        }

        private void Emerge(HiddenPreyRecord record, int index)
        {
            Pawn pawn = hiddenPawns.Take(record.pawn);
            hiddenRecords.RemoveAt(index);
            RemoveFromRefugeIndex(hiddenByRefuge, record.refuge, record.pawn);
            if (pawn == null || pawn.Destroyed) return;
            IntVec3 cell = ClosestStandable(record.cell);
            GenSpawn.Spawn(pawn, cell, map, WipeMode.Vanish);
            ForceRefresh();
            WildlifeTestLog.Count("hide.exits");
            if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("HideExited", "Prey emerged at " + cell + "; threat=" + (record.threat?.LabelShortCap.ToString() ?? "none"), pawn, record.refuge);
        }

        private int HiddenCountAt(Thing refuge)
        {
            return refuge != null && hiddenByRefuge.TryGetValue(refuge, out List<Pawn> hidden) ? hidden.Count : 0;
        }

        private int HomeCountAt(Thing refuge)
        {
            return refuge != null && homesByRefuge.TryGetValue(refuge, out List<Pawn> homes) ? homes.Count : 0;
        }

        private void AssignHome(Pawn pawn, Thing refuge)
        {
            if (pawn == null || refuge == null) return;
            homeRefugeByPawn.TryGetValue(pawn, out Thing oldHome);
            if (oldHome != null) RemoveFromRefugeIndex(homesByRefuge, oldHome, pawn);
            if (oldHome != null && oldHome != refuge && HomeCountAt(oldHome) == 0) abandonedHomeTick[oldHome] = Find.TickManager.TicksGame;
            bool reclaimed = abandonedHomeTick.Remove(refuge);
            homeRefugeByPawn[pawn] = refuge;
            AddToRefugeIndex(homesByRefuge, refuge, pawn);
            if (oldHome != refuge) WildlifeTestLog.Count(refuge is Plant ? "homes.tree" : "homes.den");
            if (reclaimed) WildlifeTestLog.Count("homes.reclaimed");
            if (WildlifeTestLog.Enabled && oldHome != refuge) WildlifeTestLog.Write("HomeAssigned", "old=" + (oldHome?.LabelShortCap.ToString() ?? "none") + " new=" + refuge.LabelShortCap + " homeOccupancy=" + HomeCountAt(refuge) + "/" + RefugeCapacity(refuge), pawn, refuge);
        }

        private void PruneHomeAssignments(int now)
        {
            List<Pawn> stalePawns = homeRefugeByPawn.Keys.Where(pawn => pawn == null || pawn.Dead || homeRefugeByPawn[pawn] == null || homeRefugeByPawn[pawn].DestroyedOrNull() || !BasicHomeSuitability(homeRefugeByPawn[pawn])).ToList();
            for (int i = 0; i < stalePawns.Count; i++)
            {
                Pawn stale = stalePawns[i];
                if (stale == null) continue;
                Thing oldHome = homeRefugeByPawn.TryGetValue(stale, out Thing refuge) ? refuge : null;
                homeRefugeByPawn.Remove(stale);
                if (oldHome != null && !oldHome.DestroyedOrNull() && !homeRefugeByPawn.Values.Contains(oldHome))
                {
                    abandonedHomeTick[oldHome] = now;
                    if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("HomeAbandoned", "decayTick=" + (now + 120000), stale, oldHome);
                }
            }
            foreach (Thing claimed in abandonedHomeTick.Keys.Where(home => home == null || home.DestroyedOrNull() || homeRefugeByPawn.Values.Contains(home)).ToList()) abandonedHomeTick.Remove(claimed);
            for (int i = dangerMemories.Count - 1; i >= 0; i--) if (now >= dangerMemories[i].expiresTick) dangerMemories.RemoveAt(i);
        }

        private void RebuildOccupancyIndexes()
        {
            hiddenByRefuge.Clear();
            for (int i = 0; i < hiddenRecords.Count; i++)
            {
                HiddenPreyRecord record = hiddenRecords[i];
                if (record?.pawn != null && record.refuge != null) AddToRefugeIndex(hiddenByRefuge, record.refuge, record.pawn);
            }
            homesByRefuge.Clear();
            foreach (KeyValuePair<Pawn, Thing> pair in homeRefugeByPawn)
                if (pair.Key?.Spawned == true && pair.Key.Map == map && !pair.Key.Dead &&
                    pair.Value != null) AddToRefugeIndex(homesByRefuge, pair.Value, pair.Key);
        }

        private static void AddToRefugeIndex(Dictionary<Thing, List<Pawn>> index, Thing refuge, Pawn pawn)
        {
            if (!index.TryGetValue(refuge, out List<Pawn> pawns)) index.Add(refuge, pawns = new List<Pawn>());
            if (!pawns.Contains(pawn)) pawns.Add(pawn);
        }

        private static void RemoveFromRefugeIndex(Dictionary<Thing, List<Pawn>> index, Thing refuge, Pawn pawn)
        {
            if (refuge == null || !index.TryGetValue(refuge, out List<Pawn> pawns)) return;
            pawns.Remove(pawn);
            if (pawns.Count == 0) index.Remove(refuge);
        }

        private static int RefugeCapacity(Thing refuge)
        {
            CompHidingRefuge comp = refuge.TryGetComp<CompHidingRefuge>();
            if (comp != null) return Mathf.Max(1, comp.Props.capacity);
            return refuge is Plant plant && plant.def.plant?.IsTree == true ? 8 : 1;
        }

        private IntVec3 ChooseMovementTarget(CompAnimalPenMarker pen, List<Pawn> members, IntVec3 center, int seed, int now, PreyProfile profile)
        {
            if (pen != null)
            {
                List<Region> regions = pen.PenState.DirectlyConnectedRegions;
                if (regions.Count > 0)
                {
                    Region region = regions[PositiveMod(seed ^ now / 1200, regions.Count)];
                    int wanted = PositiveMod(seed * 397 ^ now, Mathf.Max(1, region.CellCount));
                    int index = 0;
                    foreach (IntVec3 cell in region.Cells) if (index++ == wanted && cell.Standable(map)) return cell;
                }
            }
            if (profile?.socialType == PreySocialType.Flock && members.Count == 1)
            {
                Pawn companion = ClosestCompatibleBird(members[0], center);
                if (companion != null)
                {
                    Vector2 toward = new Vector2(companion.Position.x - center.x,
                        companion.Position.z - center.z);
                    if (toward.sqrMagnitude > 1f)
                    {
                        toward.Normalize();
                        IntVec3 gathering = center + new IntVec3(
                            Mathf.RoundToInt(toward.x * 18f), 0, Mathf.RoundToInt(toward.y * 18f));
                        if (!IsRememberedDanger(gathering))
                            return ClosestValid(gathering, null, members[0].Position);
                    }
                }
            }
            IntVec3 calledTo = map.GetComponent<WildlifeFieldcraftMapComponent>()?.ActiveCallFor(members[0], center) ?? IntVec3.Invalid;
            if (calledTo.IsValid && !IsRememberedDanger(calledTo)) return ClosestValid(calledTo, null, members[0].Position);
            Building_WildlifeTool bait = HerdsMod.Settings.enableWildlifeBait ? ClosestTool(center, baitStations, 70f) : null;
            if (bait != null && !IsRememberedDanger(bait.Position))
            {
                float baitAngle = PositiveMod(seed * 97, 360) * Mathf.Deg2Rad;
                IntVec3 baitTarget = bait.Position + new IntVec3(Mathf.RoundToInt(Mathf.Cos(baitAngle) * 3f), 0, Mathf.RoundToInt(Mathf.Sin(baitAngle) * 3f));
                return ClosestValid(baitTarget, null, members[0].Position);
            }
            Building_WildlifeTool water = ClosestTool(center, waterStations, 65f);
            if (water != null && !IsRememberedDanger(water.Position) && PositiveMod(seed + now / 2500, 3) == 0)
            {
                float waterAngle = PositiveMod(seed * 109, 360) * Mathf.Deg2Rad;
                IntVec3 waterTarget = water.Position + new IntVec3(Mathf.RoundToInt(Mathf.Cos(waterAngle) * 2f), 0, Mathf.RoundToInt(Mathf.Sin(waterAngle) * 2f));
                return ClosestValid(waterTarget, null, members[0].Position);
            }
            Building_WildlifeTool reserve = HerdsMod.Settings.enableWildlifeReserves ? ClosestTool(center, wildlifeReserves, 90f) : null;
            if (reserve != null && !IsRememberedDanger(reserve.Position))
            {
                float reserveAngle = PositiveMod(seed * 131, 360) * Mathf.Deg2Rad;
                IntVec3 reserveTarget = reserve.Position + new IntVec3(Mathf.RoundToInt(Mathf.Cos(reserveAngle) * 12f), 0, Mathf.RoundToInt(Mathf.Sin(reserveAngle) * 12f));
                return ClosestValid(reserveTarget, null, members[0].Position);
            }
            IntVec3 landscapeTarget = IntVec3.Invalid;
            if (HerdsMod.Settings.enableWildlifeLandscaping &&
                HerdsMod.Settings.enableLandscapeEffects)
                landscapeTarget = map.GetComponent<WildlifeLandscapeMapComponent>()?
                    .PreferredFeatureTarget(members[0], center, seed) ?? IntVec3.Invalid;
            if (landscapeTarget.IsValid && !IsRememberedDanger(landscapeTarget))
                return ClosestValid(landscapeTarget, null, members[0].Position);
            float angle = PositiveMod(seed * 193 ^ now / 600, 360) * Mathf.Deg2Rad;
            Vector2 randomDirection = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            int phase = PositiveMod(now / 900000 + map.uniqueID + members[0].def.shortHash, 4);
            Vector2 seasonalDirection = phase == 0 ? Vector2.right : phase == 1 ? Vector2.up : phase == 2 ? Vector2.left : Vector2.down;
            bool flock = profile?.socialType == PreySocialType.Flock;
            Vector2 direction = (randomDirection * (flock ? 0.18f : 0.45f) + seasonalDirection * (flock ? 0.82f : 0.55f)).normalized;
            float distance = flock ? (NearbyPlantCount(center) < 3 ? 30f : 20f) : NearbyPlantCount(center) < 3 ? 20f : 12f;
            if (flock && PreyProfileDatabase.IsWaterfowl(members[0].def))
            {
                IntVec3 waterCell = NearbyWaterCell(center, seed);
                if (waterCell.IsValid) return ClosestValid(waterCell, null, members[0].Position);
            }
            IntVec3 desired = center + new IntVec3(Mathf.RoundToInt(direction.x * distance), 0, Mathf.RoundToInt(direction.y * distance));
            desired = AvoidRememberedDanger(desired, center, now);
            return ClosestValid(desired, null, members[0].Position);
        }

        private Pawn ClosestCompatibleBird(Pawn bird, IntVec3 center)
        {
            Pawn best = null;
            int bestDistance = 22500;
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn candidate = pawns[i];
                if (candidate == bird || candidate?.Dead != false || candidate.def != bird.def ||
                    candidate.Faction != bird.Faction) continue;
                int distance = candidate.Position.DistanceToSquared(center);
                if (distance >= bestDistance) continue;
                best = candidate;
                bestDistance = distance;
            }
            return best;
        }

        private void UpdateSoloBirdLifecycle(int now)
        {
            nextSoloBirdTick = now + 60000;
            for (int i = 0; i < herds.Count; i++)
            {
                HerdSnapshot group = herds[i];
                if (group?.profile?.socialType != PreySocialType.Flock || group.members.Count != 1) continue;
                Pawn bird = group.members[0];
                if (bird?.Spawned != true || bird.Faction != null || bird.Downed ||
                    bird.InMentalState || ClosestCompatibleBird(bird, bird.Position) != null) continue;
                if (PositiveMod(bird.thingIDNumber + now / 60000, 25) != 0) continue;
                if (!RCellFinder.TryFindRandomPawnEntryCell(out IntVec3 edge, map,
                    CellFinder.EdgeRoadChance_Animal) || !bird.CanReach(edge, PathEndMode.OnCell, Danger.Deadly)) continue;
                Job leave = JobMaker.MakeJob(JobDefOf.Goto, edge);
                leave.exitMapOnArrival = true;
                leave.expiryInterval = 15000;
                bird.jobs.TryTakeOrderedJob(leave, JobTag.Misc);
                if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("SoloBird",
                    "Leaving map to seek companions.", bird);
            }
        }

        private IntVec3 NearbyWaterCell(IntVec3 center, int seed)
        {
            int start = PositiveMod(seed, Mathf.Max(1, GenRadial.NumCellsInRadius(24f)));
            int count = GenRadial.NumCellsInRadius(24f);
            for (int offset = 0; offset < count; offset += 17)
            {
                IntVec3 cell = center + GenRadial.RadialPattern[(start + offset) % count];
                if (!cell.InBounds(map) || !cell.GetTerrain(map).IsWater) continue;
                for (int i = 1; i < GenRadial.NumCellsInRadius(4f); i++)
                {
                    IntVec3 shore = cell + GenRadial.RadialPattern[i];
                    if (shore.InBounds(map) && shore.Standable(map)) return shore;
                }
            }
            return IntVec3.Invalid;
        }

        private int NearbyPlantCount(IntVec3 center)
        {
            int count = 0;
            for (int i = 0; i < GenRadial.NumCellsInRadius(6f); i += 4)
            {
                IntVec3 cell = center + GenRadial.RadialPattern[i];
                if (!cell.InBounds(map)) continue;
                List<Thing> things = cell.GetThingList(map);
                for (int j = 0; j < things.Count; j++) if (things[j] is Plant plant && plant.Growth > 0.2f) { count++; break; }
            }
            return count;
        }

        private void RememberDanger(IntVec3 cell, int expiresTick)
        {
            if (!cell.IsValid || !cell.InBounds(map)) return;
            for (int i = 0; i < dangerMemories.Count; i++)
            {
                if (dangerMemories[i].cell.DistanceToSquared(cell) > 100) continue;
                dangerMemories[i].expiresTick = Mathf.Max(dangerMemories[i].expiresTick, expiresTick);
                return;
            }
            dangerMemories.Add(new PreyDangerMemoryRecord { cell = cell, expiresTick = expiresTick });
            if (dangerMemories.Count > 32) dangerMemories.RemoveAt(0);
            WildlifeTestLog.Count("memory.dangers");
        }

        private bool IsRememberedDanger(IntVec3 cell)
        {
            int now = Find.TickManager?.TicksGame ?? 0;
            for (int i = 0; i < dangerMemories.Count; i++) if (dangerMemories[i].expiresTick > now && dangerMemories[i].cell.DistanceToSquared(cell) <= 100) return true;
            return false;
        }

        private IntVec3 AvoidRememberedDanger(IntVec3 desired, IntVec3 fallback, int now)
        {
            for (int i = 0; i < dangerMemories.Count; i++)
            {
                PreyDangerMemoryRecord danger = dangerMemories[i];
                if (danger.expiresTick <= now || desired.DistanceToSquared(danger.cell) > 225) continue;
                Vector2 away = new Vector2(desired.x - danger.cell.x, desired.z - danger.cell.z);
                if (away.sqrMagnitude < 0.1f) away = new Vector2(fallback.x - danger.cell.x, fallback.z - danger.cell.z);
                if (away.sqrMagnitude < 0.1f) away = Vector2.right;
                away.Normalize();
                return new IntVec3(Mathf.Clamp(Mathf.RoundToInt(desired.x + away.x * 14f), 1, map.Size.x - 2), 0, Mathf.Clamp(Mathf.RoundToInt(desired.z + away.y * 14f), 1, map.Size.z - 2));
            }
            return desired;
        }

        private bool BasicHomeSuitability(Thing home)
        {
            if (home?.Spawned != true || home.IsBurning()) return false;
            TerrainDef terrain = home.Position.GetTerrain(map);
            if (terrain?.IsWater == true || home.Position.GetFirstThing<Fire>(map) != null) return false;
            float temperature = GenTemperature.GetTemperatureForCell(home.Position, map);
            return temperature >= -65f && temperature <= 70f;
        }

        private IntVec3 ClosestValid(IntVec3 desired, CompAnimalPenMarker pen, IntVec3 fallback)
        {
            for (int i = 0; i < GenRadial.NumCellsInRadius(6f); i++)
            {
                IntVec3 cell = desired + GenRadial.RadialPattern[i];
                if (!cell.InBounds(map) || !cell.Standable(map)) continue;
                if (pen != null)
                {
                    Region region = cell.GetRegion(map);
                    if (region == null || !penByRegion.TryGetValue(region, out CompAnimalPenMarker cellPen) || cellPen != pen) continue;
                }
                return cell;
            }
            if (pen != null)
            {
                for (int step = 1; step <= 24; step++)
                {
                    float t = step / 24f;
                    IntVec3 cell = new IntVec3(Mathf.RoundToInt(Mathf.Lerp(desired.x, fallback.x, t)), 0, Mathf.RoundToInt(Mathf.Lerp(desired.z, fallback.z, t)));
                    if (ValidForGroup(cell, pen)) return cell;
                }
            }
            return fallback;
        }

        private IntVec3 ClosestStandable(IntVec3 desired)
        {
            for (int i = 0; i < GenRadial.NumCellsInRadius(8f); i++)
            {
                IntVec3 cell = desired + GenRadial.RadialPattern[i];
                if (cell.InBounds(map) && cell.Standable(map)) return cell;
            }
            return CellFinder.RandomCell(map);
        }

        private bool ValidForGroup(IntVec3 cell, CompAnimalPenMarker pen)
        {
            if (!cell.IsValid || !cell.InBounds(map) || !cell.Standable(map)) return false;
            if (pen == null) return true;
            Region region = cell.GetRegion(map);
            return region != null && penByRegion.TryGetValue(region, out CompAnimalPenMarker cellPen) && cellPen == pen;
        }

        private static Vector2 AwayVector(IntVec3 from, IntVec3 threat)
        {
            Vector2 away = new Vector2(from.x - threat.x, from.z - threat.z);
            if (away.sqrMagnitude < 0.01f) away = Vector2.right;
            return away.normalized;
        }

        private void RecordDefenseTiming(long started)
        {
            lastDefenseMicroseconds = ElapsedMicroseconds(started);
            defenseTotalMicroseconds += lastDefenseMicroseconds;
            defenseRuns++;
        }

        private static long ElapsedMicroseconds(long started)
        {
            return (Stopwatch.GetTimestamp() - started) * 1000000L / Stopwatch.Frequency;
        }

        private static IntVec2 BucketFor(IntVec3 cell) => new IntVec2(FloorDiv(cell.x, RefugeBucketSize), FloorDiv(cell.z, RefugeBucketSize));
        private static int FloorDiv(int value, int divisor) => value >= 0 ? value / divisor : (value - divisor + 1) / divisor;
        private static int FindRoot(int[] parent, int value) { while (parent[value] != value) { parent[value] = parent[parent[value]]; value = parent[value]; } return value; }
        private static void Union(int[] parent, int a, int b) { a = FindRoot(parent, a); b = FindRoot(parent, b); if (a != b) parent[b] = a; }
        private static int PositiveMod(int value, int modulus) { int result = value % modulus; return result < 0 ? result + modulus : result; }
    }
}
