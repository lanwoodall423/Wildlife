using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Herds
{
    public static class WildlifeDevBridge
    {
        private const string InFile = "Wildlife-Bridge-In.txt";
        private const string OutFile = "Wildlife-Bridge-Out.txt";
        private const string StatusFile = "Wildlife-Bridge-Status.txt";
        private const string WakeFile = "Wildlife-Bridge-Wake.request";
        // A requested session keeps only a blocked localhost listener and one idle thread.
        // Keeping it warm avoids repeated wake/file handshakes during a Codex work turn.
        private const int SessionIdleSeconds = 180;
        private static Observation activeObservation;
        private static List<string> completedObservation;
        private static HotObservation activeHotObservation;
        private static List<string> completedHotObservation;
        private static SynchronizationContext mainContext = null;
        private static FileSystemWatcher watcher;
        private static Timer sessionTimer;
        private static Timer observationTimer;
        private static Timer hotObservationTimer;
        private static TcpListener tcpListener;
        private static Thread tcpThread;
        private static volatile bool tcpRunning;
        private static volatile bool initialized;
        private static string sessionToken;
        private static int tcpPort;
        private static int tcpGeneration;
        private static long lastTcpActivityTicks;
        private static readonly Dictionary<string, string> lastCodexState =
            new Dictionary<string, string>();

        public static bool Enabled => initialized;
        public static string InputPath => Path.Combine(GenFilePaths.SaveDataFolderPath, InFile);
        public static string OutputPath => Path.Combine(GenFilePaths.SaveDataFolderPath, OutFile);
        public static string StatusPath => Path.Combine(GenFilePaths.SaveDataFolderPath, StatusFile);
        public static string WakePath => Path.Combine(GenFilePaths.SaveDataFolderPath, WakeFile);

        public static void Initialize()
        {
            // Transport and lifecycle now belong to the standalone RimWorld Dev Bridge mod.
            // Wildlife remains a zero-idle-cost command adapter discovered through reflection.
            Shutdown();
            TryDelete(StatusPath);
            TryDelete(OutputPath);
        }

        public static void Shutdown()
        {
            initialized = false;
            try { watcher?.Dispose(); } catch { }
            watcher = null;
            try { observationTimer?.Dispose(); } catch { }
            observationTimer = null;
            try { hotObservationTimer?.Dispose(); } catch { }
            hotObservationTimer = null;
            activeObservation = null;
            completedObservation = null;
            activeHotObservation = null;
            completedHotObservation = null;
            StopTcp(false);
            TryDelete(InputPath);
            TryDelete(WakePath);
        }

        public static bool ProtocolSelfTest()
        {
            string[] parsed = Parse("abc|PING|value");
            string[] commands = BridgeCommandSpecs().Select(value => value.Split('|')[0]).ToArray();
            return parsed.Length == 3 && parsed[0] == "abc" && parsed[1] == "PING" &&
                parsed[2] == "value" && commands.Contains("DEEP_SCAN") &&
                commands.Contains("BATCH_INSPECT") && commands.Contains("SOCIAL_GRAPH") &&
                commands.Contains("OPEN_SOCIAL") && commands.Contains("SIGNAL_TRACE") &&
                commands.Contains("FORCE_SIGNAL_SCENARIO") && commands.Contains("MOMENTS") &&
                commands.Contains("FORCE_MOMENT") && commands.Contains("CODEX") &&
                commands.Contains("LANDSCAPE") && commands.Contains("FORCE_LANDSCAPE") &&
                commands.Contains("TEST_LANDSCAPE_CROSSROADS") &&
                commands.Contains("TEST_SIGNAL_ANIMAL_LOG") &&
                commands.Contains("TEST_MOMENT_WINDOW");
        }

        public static string BridgeAdapterInfo() =>
            "Wildlife|1.5.0|Unclaimed Wildlife Moments now remain available for only 1-3 in-game hours.";

        public static string[] BridgeCommandSpecs() => new[]
        {
            "WILDLIFE_SNAPSHOT|R|Compact Wildlife map state",
            "CODEX|R|Token-minimized Wildlife development summary",
            "DEEP_SCAN|R|Detailed Wildlife systems assessment",
            "SYSTEMS|R|Wildlife system availability and state",
            "PERFORMANCE|R|Wildlife performance counters",
            "COLONISTS|R|Colonist fieldcraft and knowledge",
            "RECENT|R|Recent Wildlife events",
            "SIGNALS|R|Local wildlife signal cultures",
            "SIGNAL_TRACE|R|Recent signal behavior traces",
            "TEST_SIGNAL_ANIMAL_LOG|W|Emit a call and verify it appears in the animal Log",
            "TEST_MOMENT_WINDOW|R|Validate the 1-3 hour Wildlife Moment window",
            "MOMENTS|R|Current and recent Wildlife Moments",
            "LIST_ANIMALS|R|Compact animal list",
            "ANIMAL|R|Detailed animal state by thing ID",
            "WILDLIFE_DEFS|R|Animal definitions matching a filter",
            "LANDSCAPE|R|Landscape features and formation progress",
            "LANDSCAPE_ROLES|R|Inferred ecological roles for local species",
            "TEST_LANDSCAPE_CROSSROADS|W|Create and validate a visible Wildlife Crossroad",
            "SOCIAL_GRAPH|R|Animal social relationships and memories",
            "BATCH_INSPECT|R|Compact multi-section Wildlife inspection",
            "EMIT_SIGNAL|W|Emit a Wildlife signal for testing",
            "FORCE_SIGNAL_SCENARIO|W|Create a deterministic signal scenario",
            "FORCE_MOMENT|W|Create a Wildlife Moment",
            "OPEN_SIGNALS|W|Open Local Wildlife Signals",
            "SELECT_SIGN|W|Select a Wildlife sign",
            "OPEN_TRAIL|W|Open a trail lead",
            "OPEN_TRAIL_BOARD|W|Open Trail Leads",
            "OPEN_MEMORY|W|Open an animal Memory tab",
            "OPEN_SOCIAL|W|Open an animal Social tab",
            "CREATE_SOCIAL_MEMORY|W|Create a test animal social memory",
            "RUN_WILDLIFE_TESTS|W|Run the compact Wildlife in-game suite",
            "WILDLIFE_SETTINGS|R|List Wildlife settings",
            "SET_WILDLIFE_SETTING|W|Change one Wildlife setting",
            "WILDLIFE_OVERLAY|W|Toggle the complete Wildlife overlay",
            "FORCE_MYSTERY|W|Create a Wildlife mystery",
            "SOLVE_MYSTERY|W|Complete current mystery evidence",
            "RUN_ECOLOGY_DAY|W|Advance regional ecology one day",
            "FORCE_ECOLOGY_EVENT|W|Create a regional ecology event"
            ,"FORCE_LANDSCAPE|W|Create a valid ecological feature"
        };

        public static List<string> ExecuteBridgeCommand(string command, string argument, Map map)
        {
            switch ((command ?? "").ToUpperInvariant())
            {
                case "WILDLIFE_SNAPSHOT": return Snapshot(map);
                case "CODEX": return CodexCompact(map, argument);
                case "DEEP_SCAN": return DeepScan(map);
                case "SYSTEMS": return Systems(map);
                case "PERFORMANCE": return Performance(map);
                case "COLONISTS": return Colonists(map);
                case "RECENT": return Recent(map);
                case "SIGNALS": return map?.GetComponent<WildlifeSignalCultureMapComponent>()?
                    .BridgeLines() ?? new List<string> { "signals=no_map" };
                case "SIGNAL_TRACE": return map?.GetComponent<WildlifeSignalCultureMapComponent>()?
                    .TraceLines(argument) ?? new List<string> { "signalTrace=no_map" };
                case "TEST_SIGNAL_ANIMAL_LOG":
                    return map?.GetComponent<WildlifeSignalCultureMapComponent>()?
                        .DebugTestAnimalLog() ??
                        new List<string> { "signalLogTest=FAIL reason:no_map" };
                case "TEST_MOMENT_WINDOW":
                    return new List<string>
                    {
                        "momentWindowTest=" +
                        (WildlifeFieldJournalMapComponent.MomentAvailabilitySelfTest()
                            ? "PASS range:1-3h" : "FAIL")
                    };
                case "MOMENTS": return map?.GetComponent<WildlifeFieldJournalMapComponent>()?
                    .MomentBridgeLines() ?? new List<string> { "moments=no_map" };
                case "LIST_ANIMALS": return ListAnimals(map);
                case "ANIMAL": return AnimalDetails(map, ParseInt(argument));
                case "WILDLIFE_DEFS": return AnimalDefs(argument);
                case "LANDSCAPE": return map?.GetComponent<WildlifeLandscapeMapComponent>()?
                    .BridgeLines() ?? new List<string> { "landscape=no_map" };
                case "LANDSCAPE_ROLES":
                    return map?.GetComponent<WildlifeLandscapeMapComponent>()?
                        .RoleLines() ?? new List<string> { "roles=no_map" };
                case "TEST_LANDSCAPE_CROSSROADS":
                    return map?.GetComponent<WildlifeLandscapeMapComponent>()?
                        .DebugTestCrossroads() ??
                        new List<string> { "crossroadTest=FAIL reason:no_map" };
                case "SOCIAL_GRAPH": return SocialGraph(map, ParseInt(argument));
                case "BATCH_INSPECT": return BatchInspect(map, argument);
                case "EMIT_SIGNAL": return EmitSignal(map, argument);
                case "FORCE_SIGNAL_SCENARIO": return map?.GetComponent<WildlifeSignalCultureMapComponent>()?
                    .DebugSignalScenario(argument) ?? new List<string> { "scenario=no_map" };
                case "FORCE_MOMENT": return map?.GetComponent<WildlifeFieldJournalMapComponent>()?
                    .DebugForceMoment() ?? new List<string> { "moment=no_map" };
                case "OPEN_SIGNALS": return OpenSignals(map);
                case "SELECT_SIGN": return SelectSign(map, argument);
                case "OPEN_TRAIL": return OpenTrail(map, argument);
                case "OPEN_TRAIL_BOARD": return OpenTrailBoard(map);
                case "OPEN_MEMORY": return OpenMemory(map, ParseInt(argument));
                case "OPEN_SOCIAL": return OpenSocial(map, ParseInt(argument));
                case "CREATE_SOCIAL_MEMORY":
                    return new List<string>
                    {
                        map?.GetComponent<WildlifeMemoryMapComponent>()?
                            .DebugCreateSocialMemory() ?? "socialMemory=no_map"
                    };
                case "RUN_WILDLIFE_TESTS":
                    WildlifeInGameTestSuite.Run(true);
                    return File.ReadAllLines(WildlifeInGameTestSuite.ReportPath)
                        .Where(line => line.StartsWith("summary=") || line.StartsWith("WARN|") ||
                            line.StartsWith("FAIL|")).ToList();
                case "WILDLIFE_SETTINGS": return Settings(argument);
                case "SET_WILDLIFE_SETTING": return SetSetting(argument);
                case "WILDLIFE_OVERLAY":
                    bool enabled = argument.Equals("on", StringComparison.OrdinalIgnoreCase);
                    WildlifeDevMaster.SetCompleteOverlay(enabled);
                    return new List<string> { "overlay=" + (enabled ? "on" : "off") };
                case "FORCE_MYSTERY":
                    WildlifeMysteryMapComponent.DebugMystery();
                    return new List<string> { "mystery=attempted" };
                case "SOLVE_MYSTERY":
                    WildlifeMysteryMapComponent.DebugSolve();
                    return new List<string> { "mystery=evidence_completed" };
                case "RUN_ECOLOGY_DAY":
                    map?.GetComponent<RegionalWildlifeMapComponent>()?.DebugRunRegionalDay();
                    return Snapshot(map);
                case "FORCE_ECOLOGY_EVENT":
                    map?.GetComponent<RegionalWildlifeMapComponent>()?.DebugForceEvent();
                    return Snapshot(map);
                case "FORCE_LANDSCAPE":
                    return map?.GetComponent<WildlifeLandscapeMapComponent>()?
                        .DebugForceFeature(argument) ??
                        new List<string> { "created=none reason:no_map" };
                default: return new List<string> { "wildlifeAdapter=unsupported command:" + command };
            }
        }

        private static void OnBridgeFile(object sender, FileSystemEventArgs args)
        {
            if (!initialized || mainContext == null) return;
            string name = Path.GetFileName(args.FullPath);
            if (name.Equals(WakeFile, StringComparison.OrdinalIgnoreCase))
            {
                mainContext.Post(_ =>
                {
                    TryDelete(WakePath);
                    StartTcp();
                }, null);
            }
            else if (name.Equals(InFile, StringComparison.OrdinalIgnoreCase))
            {
                mainContext.Post(_ => ProcessFileCommand(), null);
            }
            else if (name.Equals(WildlifeBridgeHotReload.ModuleFileName,
                StringComparison.OrdinalIgnoreCase))
            {
                mainContext.Post(_ => WildlifeBridgeHotReload.Reload(), null);
            }
        }

        private static void ProcessFileCommand()
        {
            if (!initialized || !File.Exists(InputPath)) return;
            string raw = "";
            try
            {
                raw = File.ReadAllText(InputPath).Trim();
                if (raw.NullOrEmpty()) return;
                File.Delete(InputPath);
                Execute(raw, true);
            }
            catch (IOException) { }
            catch (Exception exception) { ExceptionResponse(raw, exception, true); }
        }

        private static string Execute(string raw, bool writeFile)
        {
            string[] parts = Parse(raw);
            string id = parts[0].NullOrEmpty() ? "unknown" : parts[0];
            string command = parts.Length > 1 ? parts[1].ToUpperInvariant() : "";
            string argument = parts.Length > 2 ? parts[2] : "";
            Map map = Find.CurrentMap;
            List<string> lines;

            switch (command)
            {
                case "PING":
                    lines = new List<string> { "pong", "tick=" + (Find.TickManager?.TicksGame ?? -1) };
                    break;
                case "HELP":
                    lines = new List<string>
                    {
                        "commands=" + string.Join(",", SupportedCommands()),
                        "hotCommands=" + string.Join(",",
                            WildlifeBridgeHotReload.CommandNames)
                    };
                    break;
                case "HOT_STATUS":
                    lines = WildlifeBridgeHotReload.StatusLines();
                    break;
                case "BEGIN_HOT_OBSERVATION":
                    lines = BeginHotObservation(map, argument);
                    break;
                case "HOT_OBSERVATION_STATUS":
                    lines = HotObservationStatus(map);
                    break;
                case "RELOAD_BRIDGE":
                    lines = WildlifeBridgeHotReload.Reload();
                    break;
                case "RESTART_BRIDGE":
                    lines = WildlifeBridgeHotReload.Reload();
                    mainContext?.Post(_ =>
                    {
                        StopTcp(false);
                        StartTcp();
                    }, null);
                    lines.Add("transport=restart_scheduled");
                    break;
                case "SNAPSHOT":
                    lines = Snapshot(map);
                    break;
                case "CODEX":
                    lines = CodexCompact(map, argument);
                    break;
                case "DEEP_SCAN":
                    lines = DeepScan(map);
                    break;
                case "SYSTEMS":
                    lines = Systems(map);
                    break;
                case "SIGNALS":
                    lines = map?.GetComponent<WildlifeSignalCultureMapComponent>()?.BridgeLines() ??
                        new List<string> { "signals=no_map" };
                    break;
                case "SIGNAL_TRACE":
                    lines = map?.GetComponent<WildlifeSignalCultureMapComponent>()?.TraceLines(argument) ??
                        new List<string> { "signalTrace=no_map" };
                    break;
                case "MOMENTS":
                    lines = map?.GetComponent<WildlifeFieldJournalMapComponent>()?.MomentBridgeLines() ??
                        new List<string> { "moments=no_map" };
                    break;
                case "EMIT_SIGNAL":
                    lines = EmitSignal(map, argument);
                    break;
                case "FORCE_SIGNAL_SCENARIO":
                    lines = map?.GetComponent<WildlifeSignalCultureMapComponent>()?
                        .DebugSignalScenario(argument) ?? new List<string> { "scenario=no_map" };
                    break;
                case "FORCE_MOMENT":
                    lines = map?.GetComponent<WildlifeFieldJournalMapComponent>()?.DebugForceMoment() ??
                        new List<string> { "moment=no_map" };
                    break;
                case "OPEN_SIGNALS":
                    lines = OpenSignals(map);
                    break;
                case "PERFORMANCE":
                    lines = Performance(map);
                    break;
                case "COLONISTS":
                    lines = Colonists(map);
                    break;
                case "RECENT":
                    lines = Recent(map);
                    break;
                case "UI_STATE":
                    lines = UiState();
                    break;
                case "SETTINGS":
                    lines = Settings(argument);
                    break;
                case "SET_SETTING":
                    lines = SetSetting(argument);
                    break;
                case "DEFS":
                    lines = AnimalDefs(argument);
                    break;
                case "LIST_ANIMALS":
                    lines = ListAnimals(map);
                    break;
                case "ANIMAL":
                    lines = AnimalDetails(map, ParseInt(argument));
                    break;
                case "SELECT":
                    lines = SelectThing(map, ParseInt(argument));
                    break;
                case "SELECT_SIGN":
                    lines = SelectSign(map, argument);
                    break;
                case "OPEN_TRAIL":
                    lines = OpenTrail(map, argument);
                    break;
                case "OPEN_TRAIL_BOARD":
                    lines = OpenTrailBoard(map);
                    break;
                case "OPEN_MEMORY":
                    lines = OpenMemory(map, ParseInt(argument));
                    break;
                case "OPEN_SOCIAL":
                    lines = OpenSocial(map, ParseInt(argument));
                    break;
                case "SOCIAL_GRAPH":
                    lines = SocialGraph(map, ParseInt(argument));
                    break;
                case "BATCH_INSPECT":
                    lines = BatchInspect(map, argument);
                    break;
                case "CLOSE_WINDOW":
                    lines = CloseWindow(argument);
                    break;
                case "CREATE_SOCIAL_MEMORY":
                    lines = new List<string>
                    {
                        map?.GetComponent<WildlifeMemoryMapComponent>()?
                            .DebugCreateSocialMemory() ?? "socialMemory=no_map"
                    };
                    break;
                case "RUN_TESTS":
                    WildlifeInGameTestSuite.Run(true);
                    lines = File.ReadAllLines(WildlifeInGameTestSuite.ReportPath)
                        .Where(line => line.StartsWith("summary=") || line.StartsWith("WARN|") ||
                            line.StartsWith("FAIL|")).ToList();
                    break;
                case "BEGIN_OBSERVATION":
                    lines = BeginObservation(id, map, ParseInt(argument));
                    break;
                case "OBSERVATION_STATUS":
                    lines = ObservationStatus(map);
                    break;
                case "SET_SPEED":
                    lines = SetSpeed(argument);
                    break;
                case "OVERLAY":
                    bool overlay = argument.Equals("on", StringComparison.OrdinalIgnoreCase);
                    WildlifeDevMaster.SetCompleteOverlay(overlay);
                    lines = new List<string> { "overlay=" + (overlay ? "on" : "off") };
                    break;
                case "FORCE_MYSTERY":
                    WildlifeMysteryMapComponent.DebugMystery();
                    lines = new List<string> { "mystery=attempted" };
                    break;
                case "SOLVE_MYSTERY":
                    WildlifeMysteryMapComponent.DebugSolve();
                    lines = new List<string> { "mystery=evidence_completed" };
                    break;
                case "RUN_ECOLOGY_DAY":
                    map?.GetComponent<RegionalWildlifeMapComponent>()?.DebugRunRegionalDay();
                    lines = Snapshot(map);
                    break;
                case "FORCE_ECOLOGY_EVENT":
                    map?.GetComponent<RegionalWildlifeMapComponent>()?.DebugForceEvent();
                    lines = Snapshot(map);
                    break;
                default:
                    if (WildlifeBridgeHotReload.TryExecute(command, argument, map,
                        ExecuteHotBuiltin, out lines))
                        break;
                    return CompleteResponse(id, "ERROR",
                        new[] { "unknown_command=" + command, "use=HELP" }, writeFile);
            }
            return CompleteResponse(id, "OK", lines, writeFile);
        }

        private static List<string> ExecuteHotBuiltin(string command, string argument, Map map)
        {
            switch ((command ?? "").ToUpperInvariant())
            {
                case "SNAPSHOT": return Snapshot(map);
                case "CODEX": return CodexCompact(map, argument);
                case "DEEP_SCAN": return DeepScan(map);
                case "SYSTEMS": return Systems(map);
                case "SIGNALS": return map?.GetComponent<WildlifeSignalCultureMapComponent>()?
                    .BridgeLines() ?? new List<string> { "signals=no_map" };
                case "SIGNAL_TRACE": return map?.GetComponent<WildlifeSignalCultureMapComponent>()?
                    .TraceLines(argument) ?? new List<string> { "signalTrace=no_map" };
                case "MOMENTS": return map?.GetComponent<WildlifeFieldJournalMapComponent>()?
                    .MomentBridgeLines() ?? new List<string> { "moments=no_map" };
                case "EMIT_SIGNAL": return EmitSignal(map, argument);
                case "FORCE_SIGNAL_SCENARIO": return map?.GetComponent<WildlifeSignalCultureMapComponent>()?
                    .DebugSignalScenario(argument) ?? new List<string> { "scenario=no_map" };
                case "FORCE_MOMENT": return map?.GetComponent<WildlifeFieldJournalMapComponent>()?
                    .DebugForceMoment() ?? new List<string> { "moment=no_map" };
                case "OPEN_SIGNALS": return OpenSignals(map);
                case "PERFORMANCE": return Performance(map);
                case "COLONISTS": return Colonists(map);
                case "RECENT": return Recent(map);
                case "UI_STATE": return UiState();
                case "SETTINGS": return Settings(argument);
                case "SET_SETTING": return SetSetting(argument);
                case "DEFS": return AnimalDefs(argument);
                case "LIST_ANIMALS": return ListAnimals(map);
                case "ANIMAL": return AnimalDetails(map, ParseInt(argument));
                case "SELECT": return SelectThing(map, ParseInt(argument));
                case "SELECT_SIGN": return SelectSign(map, argument);
                case "OPEN_TRAIL": return OpenTrail(map, argument);
                case "OPEN_TRAIL_BOARD": return OpenTrailBoard(map);
                case "OPEN_MEMORY": return OpenMemory(map, ParseInt(argument));
                case "OPEN_SOCIAL": return OpenSocial(map, ParseInt(argument));
                case "SOCIAL_GRAPH": return SocialGraph(map, ParseInt(argument));
                case "BATCH_INSPECT": return BatchInspect(map, argument);
                case "CLOSE_WINDOW": return CloseWindow(argument);
                case "CREATE_SOCIAL_MEMORY":
                    return new List<string>
                    {
                        map?.GetComponent<WildlifeMemoryMapComponent>()?
                            .DebugCreateSocialMemory() ?? "socialMemory=no_map"
                    };
                case "SET_SPEED": return SetSpeed(argument);
                case "OVERLAY":
                    bool enabled = argument.Equals("on", StringComparison.OrdinalIgnoreCase);
                    WildlifeDevMaster.SetCompleteOverlay(enabled);
                    return new List<string> { "overlay=" + (enabled ? "on" : "off") };
                default: return null;
            }
        }

        private static List<string> BeginObservation(string id, Map map, int duration)
        {
            if (map == null) return new List<string> { "observation=no_map" };
            duration = Mathf.Clamp(duration <= 0 ? 60000 : duration, 1000, 600000);
            activeObservation = new Observation
            {
                id = id,
                startTick = Find.TickManager.TicksGame,
                endTick = Find.TickManager.TicksGame + duration,
                before = CaptureMetrics(map)
            };
            completedObservation = null;
            try { observationTimer?.Dispose(); } catch { }
            observationTimer = new Timer(_ =>
            {
                if (initialized && mainContext != null)
                    mainContext.Post(__ => CompleteObservationIfReady(), null);
            }, null, 250, 250);
            return new List<string>
            {
                "observation=started",
                "durationTicks=" + duration,
                "completionTick=" + activeObservation.endTick,
                "note=result_will_be_written_automatically"
            };
        }

        private static List<string> BeginHotObservation(Map map, string argument)
        {
            if (map == null) return new List<string> { "hotObservation=no_map" };
            string[] parts = (argument ?? "").Split(new[] { ',' }, 3);
            int duration = Mathf.Clamp(ParseInt(parts.Length > 0 ? parts[0] : ""),
                1000, 600000);
            string command = parts.Length > 1 ? parts[1].Trim().ToUpperInvariant() : "";
            string commandArgument = parts.Length > 2 ? parts[2] : "";
            if (!WildlifeBridgeHotReload.CanObserve(command))
                return new List<string>
                {
                    "hotObservation=invalid_command",
                    "requirement=existing non-mutating hot command"
                };
            if (!WildlifeBridgeHotReload.TryExecute(command, commandArgument, map,
                ExecuteHotBuiltin, out List<string> before))
                return new List<string> { "hotObservation=command_failed" };
            activeHotObservation = new HotObservation
            {
                command = command,
                argument = commandArgument,
                startTick = Find.TickManager.TicksGame,
                endTick = Find.TickManager.TicksGame + duration,
                before = before
            };
            completedHotObservation = null;
            try { hotObservationTimer?.Dispose(); } catch { }
            hotObservationTimer = new Timer(_ =>
            {
                if (initialized && mainContext != null)
                    mainContext.Post(__ => CompleteHotObservationIfReady(map), null);
            }, null, 250, 250);
            return new List<string>
            {
                "hotObservation=started",
                "command=" + command,
                "startTick=" + activeHotObservation.startTick,
                "endTick=" + activeHotObservation.endTick
            };
        }

        private static List<string> HotObservationStatus(Map map)
        {
            CompleteHotObservationIfReady(map);
            if (completedHotObservation != null) return completedHotObservation;
            if (activeHotObservation == null)
                return new List<string> { "hotObservation=none" };
            return new List<string>
            {
                "hotObservation=running",
                "command=" + activeHotObservation.command,
                "remainingTicks=" + Mathf.Max(0,
                    activeHotObservation.endTick - Find.TickManager.TicksGame)
            };
        }

        private static void CompleteHotObservationIfReady(Map map)
        {
            if (activeHotObservation == null ||
                Find.TickManager.TicksGame < activeHotObservation.endTick) return;
            HotObservation observation = activeHotObservation;
            activeHotObservation = null;
            try { hotObservationTimer?.Dispose(); } catch { }
            hotObservationTimer = null;
            WildlifeBridgeHotReload.TryExecute(observation.command, observation.argument,
                map, ExecuteHotBuiltin, out List<string> after);
            completedHotObservation = DiffHotObservation(observation,
                after ?? new List<string>());
        }

        private static List<string> DiffHotObservation(HotObservation observation,
            List<string> after)
        {
            Dictionary<string, string> beforeByKey = observation.before.Skip(2)
                .GroupBy(HotLineKey).ToDictionary(group => group.Key, group => group.Last());
            Dictionary<string, string> afterByKey = after.Skip(2)
                .GroupBy(HotLineKey).ToDictionary(group => group.Key, group => group.Last());
            List<string> lines = new List<string>
            {
                "hotObservation=complete",
                "command=" + observation.command,
                "ticks=" + (observation.endTick - observation.startTick),
                "beforeLines=" + beforeByKey.Count,
                "afterLines=" + afterByKey.Count
            };
            foreach (string key in beforeByKey.Keys.Union(afterByKey.Keys)
                .OrderBy(value => value).Take(60))
            {
                beforeByKey.TryGetValue(key, out string before);
                afterByKey.TryGetValue(key, out string current);
                if (before == current) continue;
                lines.Add("change=" + Clean(key) + " before:" +
                    Clean(before ?? "missing") + " after:" + Clean(current ?? "missing"));
            }
            if (lines.Count == 5) lines.Add("change=none");
            return lines;
        }

        private static string HotLineKey(string line)
        {
            if (line.NullOrEmpty()) return "empty";
            int countMarker = line.IndexOf(" count:", StringComparison.Ordinal);
            if (countMarker > 0) return line.Substring(0, countMarker);
            int equals = line.IndexOf('=');
            if (equals < 0) return line;
            int space = line.IndexOf(' ', equals + 1);
            return space > equals ? line.Substring(0, space) : line.Substring(0, equals);
        }

        private static List<string> ObservationStatus(Map map)
        {
            CompleteObservationIfReady();
            if (completedObservation != null) return completedObservation;
            if (activeObservation == null) return new List<string> { "observation=none" };
            int remaining = Mathf.Max(0, activeObservation.endTick - Find.TickManager.TicksGame);
            return new List<string>
            {
                "observation=active",
                "remainingTicks=" + remaining,
                "progress=" + (1f - remaining / (float)(activeObservation.endTick -
                    activeObservation.startTick)).ToString("0.00")
            };
        }

        private static void CompleteObservationIfReady()
        {
            if (activeObservation == null || Find.CurrentMap == null ||
                Find.TickManager.TicksGame < activeObservation.endTick) return;
            Observation completed = activeObservation;
            activeObservation = null;
            try { observationTimer?.Dispose(); } catch { }
            observationTimer = null;
            completedObservation = CompareMetrics(completed.before,
                CaptureMetrics(Find.CurrentMap), completed.startTick,
                Find.TickManager.TicksGame);
            WriteResponse(completed.id, "OK", completedObservation);
        }

        private static ObservationMetrics CaptureMetrics(Map map)
        {
            List<Pawn> wild = map.mapPawns.AllPawnsSpawned.Where(pawn =>
                pawn.Faction == null && pawn.RaceProps?.Animal == true && !pawn.Dead).ToList();
            RegionalWildlifeMapComponent regional = map.GetComponent<RegionalWildlifeMapComponent>();
            return new ObservationMetrics
            {
                wild = wild.Count,
                predators = wild.Count(pawn => WildlifeSpeciesClassification.IsPredator(pawn.def)),
                signs = map.listerThings.ThingsOfDef(HerdsDefOf.Herds_WildlifeSign).Count,
                memories = map.GetComponent<WildlifeMemoryMapComponent>()?.Memories.Count ?? 0,
                notable = map.GetComponent<NotableWildlifeMapComponent>()?.ActiveCount ?? 0,
                mysteries = map.GetComponent<WildlifeMysteryMapComponent>()?.Mysteries.Count ?? 0,
                roaming = regional?.RoamingAnimals.Count ?? 0,
                regionalPopulation = regional?.Records.Sum(record => record.population) ?? 0f,
                seasonalEvent = regional?.ActiveSeasonalEvent ?? "none",
                species = wild.GroupBy(pawn => pawn.def.defName).ToDictionary(group => group.Key, group => group.Count())
            };
        }

        private static List<string> CompareMetrics(ObservationMetrics before, ObservationMetrics after,
            int startTick, int endTick)
        {
            List<string> lines = new List<string>
            {
                "observation=complete",
                "ticks=" + (endTick - startTick),
                "wildlife=" + before.wild + "->" + after.wild + " delta:" + (after.wild - before.wild),
                "predators=" + before.predators + "->" + after.predators,
                "regionalPopulation=" + before.regionalPopulation.ToString("0.0") + "->" +
                    after.regionalPopulation.ToString("0.0"),
                "roaming=" + before.roaming + "->" + after.roaming,
                "memories=" + before.memories + "->" + after.memories,
                "notable=" + before.notable + "->" + after.notable,
                "mysteries=" + before.mysteries + "->" + after.mysteries,
                "signs=" + before.signs + "->" + after.signs,
                "seasonalEvent=" + before.seasonalEvent + "->" + after.seasonalEvent
            };
            foreach (string species in before.species.Keys.Union(after.species.Keys).OrderBy(value => value))
            {
                int oldCount = before.species.TryGetValue(species, out int oldValue) ? oldValue : 0;
                int newCount = after.species.TryGetValue(species, out int newValue) ? newValue : 0;
                if (oldCount != newCount)
                    lines.Add("speciesChange=" + species + ":" + oldCount + "->" + newCount);
            }
            if (lines.Count == 10) lines.Add("speciesChange=none");
            return lines;
        }

        private static List<string> CodexCompact(Map map, string argument)
        {
            if (map == null) return new List<string> { "cx=v6 map=none" };
            string options = (argument ?? "").ToLowerInvariant();
            bool deltaOnly = options.Contains("delta");
            bool runTests = options.Contains("test");
            List<Pawn> animals = map.mapPawns.AllPawnsSpawned.Where(pawn =>
                pawn?.RaceProps?.Animal == true && !pawn.Dead).ToList();
            List<Pawn> wild = animals.Where(pawn => pawn.Faction == null).ToList();
            HerdMapComponent herds = map.GetComponent<HerdMapComponent>();
            WildlifeSignalCultureMapComponent signals =
                map.GetComponent<WildlifeSignalCultureMapComponent>();
            WildlifeFieldJournalMapComponent journal =
                map.GetComponent<WildlifeFieldJournalMapComponent>();
            WildlifeMemoryMapComponent memory = map.GetComponent<WildlifeMemoryMapComponent>();
            RegionalWildlifeMapComponent regional = map.GetComponent<RegionalWildlifeMapComponent>();
            WildlifeMysteryMapComponent mysteries = map.GetComponent<WildlifeMysteryMapComponent>();
            WildlifeTrailMapComponent trails = map.GetComponent<WildlifeTrailMapComponent>();
            WildlifeLandscapeMapComponent landscape =
                map.GetComponent<WildlifeLandscapeMapComponent>();
            HuntingExpeditionMapComponent expeditions =
                map.GetComponent<HuntingExpeditionMapComponent>();
            IReadOnlyList<HerdSnapshot> groups = herds?.AllHerds ??
                Array.Empty<HerdSnapshot>();
            int homes = wild.Select(pawn => herds?.HomeFor(pawn))
                .Where(home => home != null).Select(home => home.thingIDNumber).Distinct().Count();
            int tools = map.listerThings.AllThings.OfType<Building_WildlifeTool>().Count();
            int activeHunt = 0;
            MapComponent packs = map.components.FirstOrDefault(component =>
                component.GetType().FullName == "Packs.PackMapComponent");
            object huntPair = packs?.GetType().GetMethod("WildlifeMomentHuntPair")?
                .Invoke(packs, null);
            if ((huntPair as IEnumerable<Pawn>)?.Take(2).Count() == 2) activeHunt = 1;

            Dictionary<string, string> state = new Dictionary<string, string>
            {
                ["map"] = map.uniqueID.ToString(),
                ["bio"] = map.Biome.defName,
                ["sea"] = GenLocalDate.Season(map).ToString(),
                ["col"] = map.mapPawns.FreeColonistsSpawnedCount.ToString(),
                ["w"] = wild.Count.ToString(),
                ["pr"] = wild.Count(pawn => WildlifeSpeciesClassification.IsPredator(pawn.def)).ToString(),
                ["sp"] = wild.Select(pawn => pawn.def).Distinct().Count().ToString(),
                ["g"] = groups.Count.ToString(),
                ["th"] = groups.Count(group => group.defenseMode != HerdDefenseMode.None).ToString(),
                ["hm"] = homes.ToString(),
                ["pkh"] = activeHunt.ToString(),
                ["rg"] = (regional?.Records.Count ?? 0).ToString(),
                ["ro"] = (regional?.RoamingAnimals.Count ?? 0).ToString(),
                ["tool"] = tools.ToString(),
                ["sig"] = (signals?.ActiveSignals.Count ?? 0).ToString(),
                ["dial"] = (signals?.Dialects.Count ?? 0).ToString(),
                ["mom"] = journal?.Opportunity?.kind.ToString() ?? "none",
                ["resp"] = journal?.Opportunity?.response.ToString() ?? "none",
                ["trail"] = (trails?.TrailLeads.Count ?? 0).ToString(),
                ["land"] = (landscape?.Features.Count() ?? 0).ToString(),
                ["form"] = (landscape?.Activities.Count ?? 0).ToString(),
                ["cross"] = (landscape?.Crossroads.Count() ?? 0).ToString(),
                ["mem"] = (memory?.Memories.Count ?? 0).ToString(),
                ["soc"] = (memory?.SocialMemories.Count ?? 0).ToString(),
                ["not"] = (map.GetComponent<NotableWildlifeMapComponent>()?.ActiveCount ?? 0).ToString(),
                ["mys"] = (mysteries?.Mysteries.Count ?? 0).ToString(),
                ["jn"] = (journal?.Entries.Count ?? 0).ToString(),
                ["exp"] = (expeditions?.ActiveExpeditions.Count ?? 0).ToString()
            };
            int tick = Find.TickManager.TicksGame;
            List<string> lines = new List<string>();
            if (deltaOnly && lastCodexState.Count > 0)
            {
                List<string> changes = state.Where(pair =>
                    !lastCodexState.TryGetValue(pair.Key, out string old) || old != pair.Value)
                    .Select(pair => pair.Key + ":" + pair.Value).ToList();
                lines.Add("cx=v6 t=" + tick + " ch=" + changes.Count);
                lines.Add(changes.Count == 0 ? "d=stable" : "d=" + string.Join(",", changes));
            }
            else
            {
                lines.Add("cx=v6 t=" + tick + " m=" + state["map"] + " b=" +
                    state["bio"] + " s=" + state["sea"] + " c=" + state["col"]);
                lines.Add("pop=w" + state["w"] + "/p" + state["pr"] + "/sp" +
                    state["sp"] + " rg" + state["rg"] + "/ro" + state["ro"]);
                lines.Add("sim=g" + state["g"] + "/th" + state["th"] + "/hm" +
                    state["hm"] + " hunt" + state["pkh"] + " sig" + state["sig"] +
                    "/d" + state["dial"] + " mom=" + state["mom"] + "/" + state["resp"]);
                lines.Add("ply=tool" + state["tool"] + " trail" + state["trail"] +
                    " land" + state["land"] + "/f" + state["form"] +
                    "/x" + state["cross"] + " jn" + state["jn"] +
                    " exp" + state["exp"] + " story=mem" +
                    state["mem"] + "/soc" + state["soc"] + "/not" + state["not"] +
                    "/mys" + state["mys"]);
                List<string> gaps = new List<string>();
                if (tools == 0) gaps.Add("no-tools");
                if ((journal?.Entries.Count ?? 0) == 0) gaps.Add("no-knowledge");
                if ((signals?.Dialects.Count ?? 0) == 0) gaps.Add("no-signals");
                if (activeHunt > 0 && journal?.Opportunity?.kind !=
                    WildlifeOpportunityKind.PredatorStalk) gaps.Add("hunt-unfeatured");
                if (homes > 0 && (journal?.Entries.Count ?? 0) == 0)
                    gaps.Add("homes-undiscovered");
                if (HerdsMod.Settings.enableWildlifeLandscaping &&
                    (landscape?.Features.Count() ?? 0) == 0 &&
                    (landscape?.Activities.Count ?? 0) == 0)
                    gaps.Add("landscape-inactive");
                lines.Add("gap=" + (gaps.Count == 0 ? "none" : string.Join(",", gaps)));
            }
            lastCodexState.Clear();
            foreach (KeyValuePair<string, string> pair in state)
                lastCodexState[pair.Key] = pair.Value;
            if (runTests)
            {
                WildlifeInGameTestSuite.Run(true);
                lines.AddRange(File.ReadAllLines(WildlifeInGameTestSuite.ReportPath)
                    .Where(line => line.StartsWith("summary=") || line.StartsWith("WARN|") ||
                        line.StartsWith("FAIL|")).Take(6));
            }
            return lines;
        }

        private static List<string> Snapshot(Map map)
        {
            if (map == null) return new List<string> { "map=none" };
            List<Pawn> animals = map.mapPawns.AllPawnsSpawned.Where(pawn =>
                pawn.RaceProps?.Animal == true && !pawn.Dead).ToList();
            RegionalWildlifeMapComponent regional = map.GetComponent<RegionalWildlifeMapComponent>();
            WildlifeMysteryRecord mystery = map.GetComponent<WildlifeMysteryMapComponent>()?.Active;
            return new List<string>
            {
                "tick=" + Find.TickManager.TicksGame + " speed=" + Find.TickManager.CurTimeSpeed,
                "map=" + map.uniqueID + " biome=" + map.Biome.defName + " season=" + GenLocalDate.Season(map) +
                    " weather=" + (map.weatherManager.curWeather?.defName ?? "none"),
                "colony=colonists:" + map.mapPawns.FreeColonistsSpawnedCount +
                    " tameAnimals:" + animals.Count(pawn => pawn.Faction == Faction.OfPlayer),
                "wildlife=wild:" + animals.Count(pawn => pawn.Faction == null) +
                    " predators:" + animals.Count(pawn => pawn.Faction == null &&
                        WildlifeSpeciesClassification.IsPredator(pawn.def)) +
                    " species:" + animals.Select(pawn => pawn.def).Distinct().Count(),
                "regional=species:" + (regional?.Records.Count ?? 0) + " roaming:" + (regional?.RoamingAnimals.Count ?? 0) +
                    " event:" + (regional?.ActiveSeasonalEvent ?? "none"),
                "stories=notable:" + (map.GetComponent<NotableWildlifeMapComponent>()?.ActiveCount ?? 0) +
                    " memories:" + (map.GetComponent<WildlifeMemoryMapComponent>()?.Memories.Count ?? 0) +
                    " mysteries:" + (map.GetComponent<WildlifeMysteryMapComponent>()?.Mysteries.Count ?? 0),
                "landscape=features:" + (map.GetComponent<WildlifeLandscapeMapComponent>()?
                    .Features.Count() ?? 0) + " forming:" +
                    (map.GetComponent<WildlifeLandscapeMapComponent>()?.Activities.Count ?? 0),
                "activeMystery=" + (mystery == null ? "none" :
                    Clean(mystery.title) + ":" + mystery.progress.ToString("0.00"))
            };
        }

        private static List<string> DeepScan(Map map)
        {
            List<string> lines = Snapshot(map);
            if (map == null) return lines;
            List<Pawn> animals = map.mapPawns.AllPawnsSpawned.Where(pawn =>
                pawn.RaceProps?.Animal == true && !pawn.Dead).ToList();
            foreach (IGrouping<ThingDef, Pawn> group in animals.GroupBy(pawn => pawn.def)
                .OrderByDescending(group => group.Count()).Take(8))
                lines.Add("species=" + group.Key.defName + " total:" + group.Count() +
                    " wild:" + group.Count(pawn => pawn.Faction == null) +
                    " tame:" + group.Count(pawn => pawn.Faction == Faction.OfPlayer));

            RegionalWildlifeMapComponent regional = map.GetComponent<RegionalWildlifeMapComponent>();
            foreach (RegionalSpeciesRecord record in (regional?.Records ?? Array.Empty<RegionalSpeciesRecord>())
                .OrderByDescending(record => record.population).Take(6))
                lines.Add("region=" + record.species.defName + " population:" + record.population.ToString("0.0") +
                    " nearby:" + record.nearbyPopulation.ToString("0.0") +
                    " local:" + record.lastLocalCount + " confidence:" + record.confidence.ToString("0.00"));

            foreach (NotableAnimalRecord record in (map.GetComponent<NotableWildlifeMapComponent>()?.Records ??
                Array.Empty<NotableAnimalRecord>()).Take(5))
                lines.Add("notable=" + Clean(record.title) + " species:" + record.species?.defName +
                    " intent:" + record.intent + " status:" + record.culturalStatus +
                    " sightings:" + record.sightings + " escapes:" + record.escapes);

            WildlifeLandmarkMapComponent landmark = map.GetComponent<WildlifeLandmarkMapComponent>();
            foreach (WildlifeLandmarkReputation reputation in (landmark?.Reputations ??
                Array.Empty<WildlifeLandmarkReputation>()).OrderByDescending(LandmarkStrength).Take(5))
                lines.Add("landmark=" + reputation.species?.defName + " identity:" + reputation.lastIdentity +
                    " strength:" + LandmarkStrength(reputation).ToString("0.00") +
                    " evidence:" + Clean(reputation.latestEvidence));

            lines.AddRange(map.GetComponent<WildlifeLivesMapComponent>()?.DebugLines().Take(2) ??
                Enumerable.Empty<string>());
            lines.AddRange(map.GetComponent<WildlifeLandscapeMapComponent>()?.BridgeLines().Take(5) ??
                Enumerable.Empty<string>());
            lines.AddRange(WildlifeDevMaster.PacksOverview(map).Take(4).Select(line => "pack=" + Clean(line)));
            lines.AddRange(map.GetComponent<WildlifeHuntCoordinator>()?.DebugOverviewLines().Take(3)
                .Select(line => "hunt=" + Clean(line)) ?? Enumerable.Empty<string>());
            return lines.Take(40).ToList();
        }

        private static List<string> Systems(Map map)
        {
            if (map == null) return new List<string> { "map=none" };
            List<string> lines = Snapshot(map);
            HerdMapComponent herds = map.GetComponent<HerdMapComponent>();
            AddSection(lines, "herds", herds?.DebugSummaryLines(), 8);
            AddSection(lines, "packs", WildlifeDevMaster.PacksOverview(map), 8);
            AddSection(lines, "hunts", map.GetComponent<WildlifeHuntCoordinator>()?.DebugOverviewLines(), 8);
            AddSection(lines, "regional", map.GetComponent<RegionalWildlifeMapComponent>()?.DebugOverviewLines(), 10);
            AddSection(lines, "atlas", map.GetComponent<WildlifeEcologySnapshotMapComponent>()?.DebugOverviewLines(), 8);
            AddSection(lines, "signals", map.GetComponent<WildlifeSignalCultureMapComponent>()?.TraceLines(), 6);
            AddSection(lines, "landscape", map.GetComponent<WildlifeLandscapeMapComponent>()?.BridgeLines(), 10);
            AddSection(lines, "expeditions", map.GetComponent<HuntingExpeditionMapComponent>()?.DebugOverviewLines(), 8);
            AddSection(lines, "knowledge", map.GetComponent<HuntingKnowledgeMapComponent>()?.DebugOverviewLines(), 8);
            AddSection(lines, "memory", map.GetComponent<WildlifeMemoryMapComponent>()?.DebugOverviewLines(), 4);
            AddSection(lines, "journal", map.GetComponent<WildlifeFieldJournalMapComponent>()?.DebugOverviewLines(), 4);
            AddSection(lines, "lives", map.GetComponent<WildlifeLivesMapComponent>()?.DebugLines(), 3);
            return lines.Take(70).ToList();
        }

        private static List<string> Performance(Map map)
        {
            if (map == null) return new List<string> { "map=none" };
            return new List<string>
            {
                "herds=" + Clean(map.GetComponent<HerdMapComponent>()?.PerformanceSummary()),
                "packs=" + Clean(WildlifeDevMaster.PacksPerformance(map)),
                "landscape=features:" + (map.GetComponent<WildlifeLandscapeMapComponent>()?
                    .Features.Count() ?? 0) + " forming:" +
                    (map.GetComponent<WildlifeLandscapeMapComponent>()?.Activities.Count ?? 0) +
                    " scanInterval:2500 cap:14",
                "map=pawns:" + map.mapPawns.AllPawnsSpawnedCount +
                    " things:" + map.listerThings.AllThings.Count +
                    " components:" + map.components.Count
            };
        }

        private static List<string> Colonists(Map map)
        {
            if (map == null) return new List<string> { "map=none" };
            HuntingKnowledgeMapComponent knowledge = map.GetComponent<HuntingKnowledgeMapComponent>();
            return map.mapPawns.FreeColonistsSpawned.OrderBy(pawn => pawn.thingIDNumber).Take(40).Select(pawn =>
                "colonist=" + pawn.thingIDNumber + " name:" + Clean(pawn.LabelShort) +
                " animals:" + (pawn.skills?.GetSkill(SkillDefOf.Animals)?.Level ?? 0) +
                " shooting:" + (pawn.skills?.GetSkill(SkillDefOf.Shooting)?.Level ?? 0) +
                " melee:" + (pawn.skills?.GetSkill(SkillDefOf.Melee)?.Level ?? 0) +
                " wildlife:" + HuntingKnowledgeMapComponent.WildlifeProficiencyLabel(
                    knowledge?.WildlifeProficiencyLevel(pawn) ?? 0) +
                " coverage:" + (knowledge?.WildlifeProficiencyCoverage(pawn) ?? 0f).ToString("0.00") +
                " job:" + (pawn.CurJobDef?.defName ?? "none")).ToList();
        }

        private static List<string> Recent(Map map)
        {
            if (map == null) return new List<string> { "map=none" };
            List<string> lines = new List<string>();
            WildlifeFieldJournalMapComponent journal = map.GetComponent<WildlifeFieldJournalMapComponent>();
            lines.Add("journal=entries:" + (journal?.Entries.Count ?? 0) +
                " complete:" + (journal?.CompletedEntries ?? 0) +
                " projects:" + (journal?.CompletedProjects ?? 0));
            if (journal?.Opportunity != null)
                lines.Add("opportunity=" + journal.Opportunity.kind + " species:" +
                    journal.Opportunity.species?.defName + " accepted:" + journal.Opportunity.accepted +
                    " expires:" + journal.Opportunity.expiresTick);
            if (journal?.Project != null)
                lines.Add("project=" + journal.Project.kind + " species:" + journal.Project.species?.defName +
                    " progress:" + journal.Project.progress.ToString("0.00"));
            foreach (WildlifeMysteryRecord mystery in (map.GetComponent<WildlifeMysteryMapComponent>()?.Mysteries ??
                Array.Empty<WildlifeMysteryRecord>()).OrderByDescending(value => value.startedTick).Take(6))
                lines.Add("mystery=" + Clean(mystery.title) + " cause:" + mystery.cause +
                    " progress:" + mystery.progress.ToString("0.00") + " resolution:" + mystery.resolution);
            foreach (AnimalColonistMemory memory in (map.GetComponent<WildlifeMemoryMapComponent>()?.Memories ??
                Array.Empty<AnimalColonistMemory>()).OrderByDescending(value => value.lastTick).Take(10))
                lines.Add("memory=tick:" + memory.lastTick + " animal:" + Clean(memory.animal?.LabelShort) +
                    " colonist:" + Clean(memory.colonist?.LabelShort) + " event:" + Clean(memory.lastEvent) +
                    " trust:" + memory.trust.ToString("0.00") + " fear:" + memory.fear.ToString("0.00"));
            foreach (AnimalSocialMemory memory in (map.GetComponent<WildlifeMemoryMapComponent>()?.SocialMemories ??
                Array.Empty<AnimalSocialMemory>()).OrderByDescending(value => value.lastTick).Take(10))
                lines.Add("socialMemory=tick:" + memory.lastTick + " animal:" +
                    Clean(memory.animal?.LabelShort) + " other:" +
                    Clean(memory.otherAnimal?.LabelShort) + " event:" +
                    Clean(memory.lastEvent) + " bond:" + memory.bond.ToString("0.00") +
                    " fear:" + memory.fear.ToString("0.00") +
                    " rivalry:" + memory.rivalry.ToString("0.00"));
            return lines;
        }

        private static List<string> Settings(string filter)
        {
            if (HerdsMod.Settings == null) return new List<string> { "settings=unavailable" };
            filter = filter ?? "";
            return typeof(HerdsSettings).GetFields(BindingFlags.Instance | BindingFlags.Public)
                .Where(field => SupportedSettingType(field.FieldType) &&
                    (filter.NullOrEmpty() || field.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0))
                .OrderBy(field => field.Name).Take(100)
                .Select(field => "setting=" + field.Name + " value:" +
                    Convert.ToString(field.GetValue(HerdsMod.Settings), CultureInfo.InvariantCulture)).ToList();
        }

        private static List<string> SetSetting(string argument)
        {
            int separator = argument?.IndexOf('=') ?? -1;
            if (separator <= 0) return new List<string> { "usage=SET_SETTING|name=value" };
            string name = argument.Substring(0, separator);
            string value = argument.Substring(separator + 1);
            FieldInfo field = typeof(HerdsSettings).GetFields(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(candidate => candidate.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (field == null || !SupportedSettingType(field.FieldType))
                return new List<string> { "setting=not_found_or_unsupported" };
            try
            {
                object parsed = field.FieldType == typeof(bool) ? (object)bool.Parse(value) :
                    field.FieldType == typeof(int) ? int.Parse(value, CultureInfo.InvariantCulture) :
                    field.FieldType == typeof(float) ? float.Parse(value, CultureInfo.InvariantCulture) :
                    value;
                field.SetValue(HerdsMod.Settings, parsed);
                HerdsMod.Instance?.WriteSettings();
                return new List<string> { "setting=" + field.Name + " value:" +
                    Convert.ToString(field.GetValue(HerdsMod.Settings), CultureInfo.InvariantCulture) };
            }
            catch (Exception exception)
            {
                return new List<string> { "setting=invalid_value", "error=" + Clean(exception.Message) };
            }
        }

        private static List<string> AnimalDefs(string filter)
        {
            filter = filter ?? "";
            return DefDatabase<ThingDef>.AllDefsListForReading.Where(def => def.race?.Animal == true &&
                (filter.NullOrEmpty() || def.defName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    def.label.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0))
                .OrderBy(def => def.defName).Take(60)
                .Select(def => "def=" + def.defName + " label:" + Clean(def.label) +
                    " predator:" + WildlifeSpeciesClassification.IsPredator(def) +
                    " prey:" + WildlifeSpeciesClassification.IsPrey(def) +
                    " body:" + def.race.baseBodySize.ToString("0.00") +
                    " wildness:" + def.GetStatValueAbstract(StatDefOf.Wildness).ToString("0.00")).ToList();
        }

        private static void AddSection(List<string> target, string prefix, IEnumerable<string> source, int limit)
        {
            if (source == null) return;
            target.AddRange(source.Take(limit).Select(line => prefix + "=" + Clean(line)));
        }

        private static bool SupportedSettingType(Type type) =>
            type == typeof(bool) || type == typeof(int) || type == typeof(float) || type == typeof(string);

        private static List<string> ListAnimals(Map map)
        {
            if (map == null) return new List<string> { "map=none" };
            return map.mapPawns.AllPawnsSpawned.Where(pawn => pawn.RaceProps?.Animal == true && !pawn.Dead)
                .OrderBy(pawn => pawn.def.defName).ThenBy(pawn => pawn.thingIDNumber).Take(80)
                .Select(pawn => "animal=" + pawn.thingIDNumber + " label:" + Clean(pawn.LabelShort) +
                    " species:" + pawn.def.defName + " status:" +
                    (pawn.Faction == Faction.OfPlayer ? "tame" : pawn.Faction == null ? "wild" : "faction") +
                    " predator:" + (WildlifeSpeciesClassification.IsPredator(pawn.def) ? "yes" : "no") +
                    " pos:" + pawn.Position.x + "," + pawn.Position.z +
                    " job:" + (pawn.CurJobDef?.defName ?? "none")).ToList();
        }

        private static List<string> AnimalDetails(Map map, int id)
        {
            Pawn pawn = map?.mapPawns.AllPawnsSpawned.FirstOrDefault(value => value.thingIDNumber == id);
            if (pawn == null || pawn.RaceProps?.Animal != true) return new List<string> { "animal=not_found" };
            List<string> lines = new List<string>
            {
                "animal=" + pawn.thingIDNumber + " label:" + Clean(pawn.LabelShort) + " species:" + pawn.def.defName,
                "status=" + (pawn.Faction == Faction.OfPlayer ? "tame" : "wild") +
                    " predator:" + (WildlifeSpeciesClassification.IsPredator(pawn.def) ? "yes" : "no") +
                    " health:" + pawn.health.summaryHealth.SummaryHealthPercent.ToString("0.00"),
                "position=" + pawn.Position.x + "," + pawn.Position.z +
                    " job=" + (pawn.CurJobDef?.defName ?? "none"),
                "age=" + pawn.ageTracker.AgeBiologicalYearsFloat.ToString("0.0") +
                    " gender=" + pawn.gender
            };
            NotableAnimalRecord notable = map.GetComponent<NotableWildlifeMapComponent>()?.Records
                .FirstOrDefault(value => value.animal == pawn);
            if (notable != null)
                lines.Add("notable=" + Clean(notable.title) + " intent:" + notable.intent +
                    " status:" + notable.culturalStatus + " sightings:" + notable.sightings);
            WildlifeSignalCultureMapComponent signals =
                map.GetComponent<WildlifeSignalCultureMapComponent>();
            if (HerdsMod.Settings.enableWildlifeSignalCulture)
                lines.Add(WildlifeSpeciesClassification.IsPredator(pawn.def)
                    ? "signals=" + Clean(signals?.PredatorSummary(pawn.def))
                    : "signals=" + Clean(signals?.SignalSummary(pawn.def)));
            WildlifeMemoryMapComponent memory = map.GetComponent<WildlifeMemoryMapComponent>();
            foreach (AnimalSocialMemory relationship in (memory?.SocialFor(pawn) ??
                Array.Empty<AnimalSocialMemory>()).Take(5))
                lines.Add("animalMemory=other:" +
                    (relationship.otherAnimal?.thingIDNumber ?? -1) + " label:" +
                    Clean(relationship.otherAnimal?.LabelShort) + " relation:" +
                    Clean(memory.SocialRelationship(pawn, relationship.otherAnimal)) +
                    " bond:" + relationship.bond.ToString("0.00") +
                    " fear:" + relationship.fear.ToString("0.00") +
                    " rivalry:" + relationship.rivalry.ToString("0.00"));
            return lines;
        }

        private static List<string> EmitSignal(Map map, string argument)
        {
            if (map == null) return new List<string> { "signal=no_map" };
            string[] parts = (argument ?? "").Split(',');
            int id = ParseInt(parts.Length > 0 ? parts[0] : "");
            Pawn animal = map.mapPawns.AllPawnsSpawned.FirstOrDefault(pawn =>
                pawn.thingIDNumber == id && pawn.RaceProps?.Animal == true);
            if (animal == null) return new List<string> { "signal=animal_not_found" };
            WildlifeSignalKind kind = WildlifeSignalKind.Alarm;
            if (parts.Length > 1) Enum.TryParse(parts[1], true, out kind);
            bool truthful = parts.Length <= 2 ||
                !parts[2].Equals("false", StringComparison.OrdinalIgnoreCase);
            WildlifeSignalCultureMapComponent signals =
                map.GetComponent<WildlifeSignalCultureMapComponent>();
            signals?.NotifyAnimalSignal(animal.def, kind, animal, null, truthful);
            return new List<string>
            {
                "signal=emitted animal:" + id + " species:" + animal.def.defName +
                " kind:" + kind + " truthful:" + truthful
            }.Concat(signals?.BridgeLines() ?? Enumerable.Empty<string>()).ToList();
        }

        private static List<string> OpenSignals(Map map)
        {
            if (map == null) return new List<string> { "signalsWindow=no_map" };
            Window_WildlifeJournal.OpenSignals(map, null);
            return new List<string> { "signalsWindow=open journalPage=signals" };
        }

        private static List<string> SelectThing(Map map, int id)
        {
            Thing thing = map?.listerThings.AllThings.FirstOrDefault(value => value.thingIDNumber == id);
            if (thing == null) return new List<string> { "thing=not_found" };
            Find.Selector.ClearSelection();
            Find.Selector.Select(thing);
            Find.CameraDriver.JumpToCurrentMapLoc(thing.Position);
            return new List<string> { "selected=" + id + " label:" + Clean(thing.LabelShort) };
        }

        private static List<string> SetSpeed(string value)
        {
            TimeSpeed speed;
            if (!Enum.TryParse(value, true, out speed))
                return new List<string> { "valid=Paused,Normal,Fast,Superfast,Ultrafast" };
            Find.TickManager.CurTimeSpeed = speed;
            return new List<string> { "speed=" + speed };
        }

        private static List<string> SelectSign(Map map, string speciesName)
        {
            if (map == null || HerdsDefOf.Herds_WildlifeSign == null)
                return new List<string> { "sign=no_map" };
            string search = (speciesName ?? "").Trim();
            WildlifeSign sign = map.listerThings.ThingsOfDef(HerdsDefOf.Herds_WildlifeSign)
                .OfType<WildlifeSign>()
                .Where(value => value?.Spawned == true && value.species != null &&
                    (search.NullOrEmpty() ||
                     value.species.defName.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                     value.species.label.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0))
                .OrderByDescending(value => value.createdTick).FirstOrDefault();
            if (sign == null) return new List<string> { "sign=not_found", "query=" + Clean(search) };
            Find.Selector.ClearSelection();
            Find.Selector.Select(sign);
            Find.CameraDriver.JumpToCurrentMapLoc(sign.Position);
            WildlifeTrailLead lead = map.GetComponent<WildlifeTrailMapComponent>()?
                .LeadFor(sign.species);
            return new List<string>
            {
                "selectedSign=id:" + sign.thingIDNumber + " species:" + sign.species.defName +
                " kind:" + sign.signKind + " age:" +
                Mathf.Max(0, Find.TickManager.TicksGame - sign.createdTick) +
                " studied:" + sign.studiedBy.Count + " position:" + sign.Position,
                lead == null ? "trail=none" :
                    "trail=confidence:" + lead.confidence.ToString("0.00") +
                    " evidence:" + lead.evidenceCells.Count + " direction:" +
                    Clean(lead.direction) + " predicted:" + lead.predictedCell
            };
        }

        private static List<string> UiState()
        {
            List<string> result = new List<string>();
            Thing selected = Find.Selector?.SingleSelectedThing;
            result.Add(selected == null ? "selected=none" :
                "selected=id:" + selected.thingIDNumber + " type:" +
                selected.GetType().FullName + " def:" + selected.def?.defName +
                " label:" + Clean(selected.LabelShort) + " position:" +
                (selected.Spawned ? selected.Position.ToString() : "unspawned"));
            if (selected != null)
            {
                List<string> gizmos = selected.GetGizmos().OfType<Command>()
                    .Select(command => Clean(command.defaultLabel.ToString()))
                    .Where(label => !label.NullOrEmpty()).Take(30).ToList();
                result.Add("gizmos=count:" + gizmos.Count + " labels:" +
                    (gizmos.Count == 0 ? "none" : string.Join(",", gizmos)));
            }
            IList<Window> windows = Find.WindowStack?.Windows;
            result.Add("windows=count:" + (windows?.Count ?? 0));
            if (windows != null)
                for (int i = 0; i < windows.Count; i++)
                {
                    Window window = windows[i];
                    result.Add("window=type:" + window.GetType().FullName + " rect:" +
                        window.windowRect.width.ToString("0") + "x" +
                        window.windowRect.height.ToString("0") + " forcePause:" +
                        window.forcePause + " absorbsInput:" + window.absorbInputAroundWindow);
                }
            return result;
        }

        private static List<string> OpenTrail(Map map, string speciesName)
        {
            if (map == null) return new List<string> { "trail=no_map" };
            string search = (speciesName ?? "").Trim();
            WildlifeTrailLead lead = map.GetComponent<WildlifeTrailMapComponent>()?.TrailLeads
                .Where(value => value?.species != null &&
                    (search.NullOrEmpty() ||
                     value.species.defName.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                     value.species.label.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0))
                .OrderByDescending(value => value.createdTick).FirstOrDefault();
            if (lead == null) return new List<string> { "trail=not_found", "query=" + Clean(search) };
            Find.WindowStack.Add(new Window_WildlifeTrailBoard(map));
            return new List<string>
            {
                "openedTrail=species:" + lead.species.defName + " confidence:" +
                lead.confidence.ToString("0.00") + " evidence:" + lead.evidenceCells.Count
            };
        }

        private static List<string> OpenTrailBoard(Map map)
        {
            if (map == null) return new List<string> { "trailBoard=no_map" };
            Find.WindowStack.Add(new Window_WildlifeTrailBoard(map));
            int signs = HerdsDefOf.Herds_WildlifeSign == null ? 0 :
                map.listerThings.ThingsOfDef(HerdsDefOf.Herds_WildlifeSign).Count;
            int leads = map.GetComponent<WildlifeTrailMapComponent>()?.TrailLeads.Count ?? 0;
            return new List<string>
            {
                "openedTrailBoard=signs:" + signs + " reconstructed:" + leads
            };
        }

        private static List<string> OpenMemory(Map map, int id)
        {
            Pawn animal = map?.mapPawns.AllPawnsSpawned.FirstOrDefault(pawn =>
                pawn.thingIDNumber == id && pawn.RaceProps?.Animal == true);
            if (animal == null) return new List<string> { "memory=animal_not_found" };
            WildlifeMemoryMapComponent memory =
                map.GetComponent<WildlifeMemoryMapComponent>();
            Find.WindowStack.Add(new Window_AnimalMemoryTimeline(animal));
            return new List<string>
            {
                "openedMemory=animal:" + id + " colonists:" +
                (memory?.Memories.Count(value => value.animal == animal) ?? 0) +
                " animals:" + (memory?.SocialFor(animal).Count ?? 0)
            };
        }

        private static List<string> OpenSocial(Map map, int id)
        {
            Pawn animal = map?.mapPawns.AllPawnsSpawned.FirstOrDefault(pawn =>
                pawn.thingIDNumber == id && pawn.RaceProps?.Animal == true);
            if (animal == null) return new List<string> { "social=animal_not_found" };
            Find.WindowStack.Add(new Window_AnimalMemoryTimeline(animal, true));
            return SocialGraph(map, id).Prepend("openedSocial=animal:" + id).ToList();
        }

        private static List<string> SocialGraph(Map map, int id)
        {
            Pawn animal = map?.mapPawns.AllPawnsSpawned.FirstOrDefault(pawn =>
                pawn.thingIDNumber == id && pawn.RaceProps?.Animal == true);
            if (animal == null) return new List<string> { "social=animal_not_found" };
            WildlifeMemoryMapComponent memory =
                map.GetComponent<WildlifeMemoryMapComponent>();
            IReadOnlyList<AnimalSocialMemory> relationships = memory?.SocialFor(animal) ??
                Array.Empty<AnimalSocialMemory>();
            List<string> lines = new List<string>
            {
                "socialRoot=id:" + id + " name:" +
                Clean(AnimalMemoryPresentation.DisplayName(animal)) +
                " ties:" + relationships.Count
            };
            for (int i = 0; i < relationships.Count && i < 12; i++)
            {
                AnimalSocialMemory tie = relationships[i];
                Pawn other = tie.otherAnimal;
                lines.Add("socialTie=id:" + (other?.thingIDNumber ?? -1) +
                    " name:" + Clean(AnimalMemoryPresentation.DisplayName(other)) +
                    " relation:" + Clean(memory.SocialRelationship(animal, other)) +
                    " bond:" + tie.bond.ToString("0.00") +
                    " fear:" + tie.fear.ToString("0.00") +
                    " rivalry:" + tie.rivalry.ToString("0.00") +
                    " latest:" + Clean(tie.lastEvent) +
                    " spawned:" + (other?.Spawned == true));
            }
            return lines;
        }

        private static List<string> BatchInspect(Map map, string argument)
        {
            string[] requested = (argument ?? "").Split(new[] { ',' },
                StringSplitOptions.RemoveEmptyEntries);
            if (requested.Length == 0)
                requested = new[] { "CODEX" };
            List<string> lines = new List<string>();
            for (int i = 0; i < requested.Length && i < 12; i++)
            {
                string entry = requested[i].Trim();
                int separator = entry.IndexOf(':');
                string command = (separator < 0 ? entry :
                    entry.Substring(0, separator)).Trim().ToUpperInvariant();
                string commandArgument = separator < 0 ? "" :
                    entry.Substring(separator + 1).Trim();
                bool builtInAllowed = command == "SNAPSHOT" || command == "DEEP_SCAN" ||
                    command == "CODEX" ||
                    command == "SYSTEMS" || command == "SIGNALS" || command == "SIGNAL_TRACE" ||
                    command == "MOMENTS" ||
                    command == "PERFORMANCE" || command == "COLONISTS" ||
                    command == "RECENT" || command == "UI_STATE" ||
                    command == "LIST_ANIMALS" || command == "ANIMAL" ||
                    command == "SOCIAL_GRAPH";
                List<string> result = builtInAllowed
                    ? ExecuteHotBuiltin(command, commandArgument, map) : null;
                if (result == null && WildlifeBridgeHotReload.CanObserve(command))
                    WildlifeBridgeHotReload.TryExecute(command, commandArgument, map,
                        ExecuteHotBuiltin, out result);
                lines.Add("section=" + command);
                if (result == null)
                    lines.Add("batchError=unsupported_or_mutating");
                else
                    lines.AddRange(result.Take(100));
            }
            return lines;
        }

        private static List<string> CloseWindow(string typeName)
        {
            string search = (typeName ?? "").Trim();
            if (search.NullOrEmpty())
                return new List<string> { "usage=CLOSE_WINDOW|type name" };
            List<Window> matches = (Find.WindowStack?.Windows ?? Array.Empty<Window>())
                .Where(window => window != null &&
                    (window.GetType().Name.IndexOf(search,
                        StringComparison.OrdinalIgnoreCase) >= 0 ||
                     window.GetType().FullName.IndexOf(search,
                        StringComparison.OrdinalIgnoreCase) >= 0))
                .ToList();
            for (int i = 0; i < matches.Count; i++) matches[i].Close(false);
            return new List<string>
            {
                "closedWindows=" + matches.Count + " query:" + Clean(search)
            };
        }

        private static void StartTcp()
        {
            if (tcpRunning)
            {
                Interlocked.Exchange(ref lastTcpActivityTicks, DateTime.UtcNow.Ticks);
                return;
            }
            StopTcp(false);
            try
            {
                sessionToken = Guid.NewGuid().ToString("N");
                tcpListener = new TcpListener(IPAddress.Loopback, 0);
                tcpListener.Start(8);
                tcpPort = ((IPEndPoint)tcpListener.LocalEndpoint).Port;
                tcpRunning = true;
                int generation = Interlocked.Increment(ref tcpGeneration);
                TcpListener listener = tcpListener;
                Interlocked.Exchange(ref lastTcpActivityTicks, DateTime.UtcNow.Ticks);
                tcpThread = new Thread(() => TcpLoop(listener, generation))
                {
                    IsBackground = true,
                    Name = "Wildlife Dev Bridge"
                };
                tcpThread.Start();
                sessionTimer = new Timer(_ =>
                {
                    if (!tcpRunning || generation != tcpGeneration || mainContext == null) return;
                    long idleTicks = DateTime.UtcNow.Ticks - Interlocked.Read(ref lastTcpActivityTicks);
                    if (idleTicks >= TimeSpan.FromSeconds(SessionIdleSeconds).Ticks)
                        mainContext.Post(__ =>
                        {
                            if (generation != tcpGeneration) return;
                            long currentIdle = DateTime.UtcNow.Ticks -
                                Interlocked.Read(ref lastTcpActivityTicks);
                            if (currentIdle >= TimeSpan.FromSeconds(SessionIdleSeconds).Ticks)
                                StopTcp(true);
                        }, null);
                }, null, 10000, 10000);
                WriteStatus("ON");
            }
            catch (Exception)
            {
                tcpRunning = false;
                tcpPort = 0;
                sessionToken = "";
                try { tcpListener?.Stop(); } catch { }
                tcpListener = null;
                WriteStatus("DORMANT");
            }
        }

        private static void StopTcp(bool writeDormant)
        {
            tcpRunning = false;
            Interlocked.Increment(ref tcpGeneration);
            try { sessionTimer?.Dispose(); } catch { }
            sessionTimer = null;
            try { tcpListener?.Stop(); } catch { }
            tcpListener = null;
            tcpPort = 0;
            sessionToken = "";
            if (writeDormant && initialized) WriteStatus("DORMANT");
        }

        private static void TcpLoop(TcpListener listener, int generation)
        {
            while (tcpRunning && generation == tcpGeneration)
            {
                TcpClient client = null;
                try
                {
                    client = listener.AcceptTcpClient();
                    client.NoDelay = true;
                    client.ReceiveTimeout = 30000;
                    client.SendTimeout = 30000;
                    using (client)
                    using (NetworkStream stream = client.GetStream())
                    using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, false, 4096, true))
                    using (StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, true)
                    {
                        AutoFlush = true,
                        NewLine = "\n"
                    })
                    {
                        string raw = reader.ReadLine();
                        string prefix = sessionToken + "|";
                        if (raw.NullOrEmpty() || !raw.StartsWith(prefix, StringComparison.Ordinal))
                        {
                            writer.Write("id=unknown\nstatus=ERROR\nauth=failed");
                            continue;
                        }
                        Interlocked.Exchange(ref lastTcpActivityTicks, DateTime.UtcNow.Ticks);
                        TcpRequest request = new TcpRequest { raw = raw.Substring(prefix.Length) };
                        if (mainContext == null)
                        {
                            writer.Write("id=unknown\nstatus=ERROR\nbridge=main_thread_unavailable");
                            continue;
                        }
                        mainContext.Post(_ =>
                        {
                            try { request.response = Execute(request.raw, false); }
                            catch (Exception exception) { request.response =
                                ExceptionResponse(request.raw, exception, false); }
                            finally { request.done.Set(); }
                        }, null);
                        if (!request.done.Wait(30000))
                            writer.Write("id=unknown\nstatus=ERROR\nbridge=main_thread_timeout");
                        else
                            writer.Write(request.response);
                        Interlocked.Exchange(ref lastTcpActivityTicks, DateTime.UtcNow.Ticks);
                    }
                }
                catch (SocketException)
                {
                    if (!tcpRunning || generation != tcpGeneration) return;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (Exception)
                {
                    try { client?.Close(); } catch { }
                }
            }
        }

        private static void WriteStatus(string state)
        {
            AtomicWrite(StatusPath, new[]
            {
                "bridge=" + state,
                "protocol=v5",
                "transport=" + (tcpPort > 0 ? "tcp+file" : "wake-file"),
                "host=127.0.0.1",
                "port=" + tcpPort,
                "token=" + (sessionToken ?? ""),
                "hotGeneration=" + WildlifeBridgeHotReload.Generation,
                "hotCommands=" + WildlifeBridgeHotReload.CommandCount,
                "hotModule=" + WildlifeBridgeHotReload.ModulePath,
                "input=" + InputPath,
                "output=" + OutputPath,
                "tick=" + (Find.TickManager?.TicksGame ?? -1)
            });
        }

        private static void WriteResponse(string id, string status, IEnumerable<string> lines)
        {
            AtomicWrite(OutputPath, ResponseLines(id, status, lines));
        }

        private static string CompleteResponse(string id, string status, IEnumerable<string> lines, bool writeFile)
        {
            List<string> response = ResponseLines(id, status, lines).ToList();
            if (writeFile) AtomicWrite(OutputPath, response);
            return string.Join("\n", response);
        }

        private static IEnumerable<string> ResponseLines(string id, string status, IEnumerable<string> lines) =>
            new[] { "id=" + id, "status=" + status }.Concat(lines ?? Enumerable.Empty<string>());

        private static string ExceptionResponse(string raw, Exception exception, bool writeFile)
        {
            string[] parsed = Parse(raw ?? "");
            string id = parsed.Length > 0 && !parsed[0].NullOrEmpty() ? parsed[0] : "unknown";
            string command = parsed.Length > 1 ? parsed[1] : "unknown";
            Exception root = exception.GetBaseException();
            StackFrame[] frames = new StackTrace(root, false).GetFrames() ?? Array.Empty<StackFrame>();
            string location = string.Join(" <- ", frames.Take(4).Select(frame =>
                (frame.GetMethod()?.DeclaringType?.Name ?? "?") + "." +
                (frame.GetMethod()?.Name ?? "?")));
            return CompleteResponse(id, "ERROR", new[]
            {
                "command=" + Clean(command),
                "exception=" + root.GetType().Name,
                "message=" + Clean(root.Message),
                "at=" + Clean(location)
            }, writeFile);
        }

        private static void AtomicWrite(string path, IEnumerable<string> lines)
        {
            string temp = path + ".tmp";
            File.WriteAllLines(temp, lines);
            if (File.Exists(path)) File.Delete(path);
            File.Move(temp, path);
        }

        private static string[] Parse(string raw)
        {
            string line = raw.Replace('\r', '\n').Split(new[] { '\n' },
                StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
            return line.Split(new[] { '|' }, 3);
        }

        private static int ParseInt(string value)
        {
            int parsed;
            return int.TryParse(value, out parsed) ? parsed : -1;
        }

        private static string[] SupportedCommands() => new[]
        {
            "PING", "HELP", "CODEX", "SNAPSHOT", "DEEP_SCAN", "SYSTEMS", "PERFORMANCE", "COLONISTS",
            "SIGNALS", "SIGNAL_TRACE", "MOMENTS", "EMIT_SIGNAL", "FORCE_SIGNAL_SCENARIO",
            "FORCE_MOMENT", "OPEN_SIGNALS",
            "RECENT", "UI_STATE", "SETTINGS", "SET_SETTING", "DEFS", "LIST_ANIMALS", "ANIMAL", "SELECT",
            "SELECT_SIGN", "OPEN_TRAIL", "OPEN_TRAIL_BOARD", "OPEN_MEMORY", "OPEN_SOCIAL",
            "SOCIAL_GRAPH", "BATCH_INSPECT", "CLOSE_WINDOW",
            "CREATE_SOCIAL_MEMORY",
            "RUN_TESTS", "BEGIN_OBSERVATION", "OBSERVATION_STATUS", "SET_SPEED", "OVERLAY",
            "FORCE_MYSTERY", "SOLVE_MYSTERY",
            "RUN_ECOLOGY_DAY", "FORCE_ECOLOGY_EVENT", "HOT_STATUS", "RELOAD_BRIDGE",
            "RESTART_BRIDGE", "BEGIN_HOT_OBSERVATION", "HOT_OBSERVATION_STATUS"
        };

        private static float LandmarkStrength(WildlifeLandmarkReputation value) =>
            value == null ? 0f : new[] { value.sanctuary, value.water, value.feeding, value.forbidden,
                value.killingGround, value.predatorNest, value.sacred, value.unstable }.Max();

        private static string Clean(string value) =>
            (value ?? "none").Replace('|', '/').Replace('\r', ' ').Replace('\n', ' ');

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (IOException) { }
        }

        private sealed class TcpRequest
        {
            public string raw;
            public string response;
            public readonly ManualResetEventSlim done = new ManualResetEventSlim(false);
        }

        private sealed class Observation
        {
            public string id;
            public int startTick;
            public int endTick;
            public ObservationMetrics before;
        }

        private sealed class ObservationMetrics
        {
            public int wild;
            public int predators;
            public int signs;
            public int memories;
            public int notable;
            public int mysteries;
            public int roaming;
            public float regionalPopulation;
            public string seasonalEvent;
            public Dictionary<string, int> species;
        }

        private sealed class HotObservation
        {
            public string command;
            public string argument;
            public int startTick;
            public int endTick;
            public List<string> before;
        }
    }

    [HarmonyPatch(typeof(Game), nameof(Game.FinalizeInit))]
    public static class WildlifeDevBridgeStartupPatch
    {
        public static void Postfix()
        {
            LongEventHandler.ExecuteWhenFinished(WildlifeDevBridge.Initialize);
        }
    }
}
