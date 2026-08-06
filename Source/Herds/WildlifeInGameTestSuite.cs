using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using KnowledgeFramework;
using LudeonTK;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;

namespace Herds
{
    public static class WildlifeInGameTestSuite
    {
        private const string FileName = "Wildlife-InGame-Test.txt";

        private sealed class Result
        {
            public string section;
            public string severity;
            public string text;
        }

        private sealed class DeterministicPredatorPressureResult
        {
            public bool noQualifyingThreat;
            public bool genuineDefense;
            public bool productionClassification;
            public bool sharedPresentations;
            public bool oneColonyEvent;
            public bool firstEvidenceAmbiguous;
            public bool reprocessingIdempotent;
            public bool laterEncounterReinforced;
            public bool localPredictionBounded;
            public bool hostileNonPredatorExcluded;
            public bool unobservedExcluded;
            public bool ineligibleObserverExcluded;
            public bool fabricatedExcluded;
            public bool simulatedPredatorEligible;
            public bool cleared;
            public bool allClearExcluded;
            public bool normalizationIdempotent;
            public bool scribeRoundTrip;
            public bool uiNonMutating;
            public bool warningPathPreserved;
            public bool deterrentRoute;
            public bool deterrentEffect;
            public bool isolatedCleanup;
            public bool activeStateUntouched;
            public string detail;
        }

        private sealed class IsolatedMapFixture
        {
            public Game previousGame;
            public Game game;
            public Map map;
            public MapParent parent;
            private Map previousCurrentMap;
            private Map previousGameCurrentMap;
            private List<object> previousSelectedObjects;
            private Designator previousDesignator;
            private List<Window> previousWindows;
            private bool uiCaptured;

            public static bool TryCreate(Map activeMap, out IsolatedMapFixture fixture, out string detail)
            {
                fixture = null;
                detail = "";
                if (activeMap == null || Current.Game == null)
                {
                    detail = "active game/map unavailable";
                    return false;
                }

                IsolatedMapFixture candidate = new IsolatedMapFixture
                {
                    previousGame = Current.Game,
                    game = new Game()
                };
                string phase = "constructing isolated game";
                try
                {
                    candidate.previousCurrentMap = Find.CurrentMap;
                    candidate.previousGameCurrentMap = candidate.previousGame.CurrentMap;
                    candidate.previousSelectedObjects = Find.Selector?.SelectedObjectsListForReading?.ToList() ?? new List<object>();
                    candidate.previousDesignator = Find.DesignatorManager?.SelectedDesignator;
                    candidate.previousWindows = Find.WindowStack?.Windows?.ToList() ?? new List<Window>();
                    candidate.uiCaptured = true;
                    phase = "initializing isolated game";
                    FieldInfo infoField = typeof(Game).GetField("info",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                    infoField?.SetValue(candidate.game, new GameInfo
                    {
                        startingTile = activeMap.Tile,
                        startingAndOptionalPawns = new List<Pawn>()
                    });
                    candidate.game.InitData = new GameInitData
                    {
                        startingTile = activeMap.Tile,
                        mapGeneratorDef = MapGeneratorDefOf.Base_Player,
                        mapSize = 80,
                        startingAndOptionalPawns = new List<Pawn>(),
                        startingPawnCount = 0,
                        playerFaction = Faction.OfPlayer
                    };
                    Scenario isolatedScenario = new Scenario();
                    typeof(Scenario).GetField("name", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                        ?.SetValue(isolatedScenario, "Wildlife isolated acceptance");
                    typeof(Scenario).GetField("enabled", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                        ?.SetValue(isolatedScenario, true);
                    typeof(Scenario).GetField("valid", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                        ?.SetValue(isolatedScenario, true);
                    ScenPart_PlayerFaction isolatedFactionPart = new ScenPart_PlayerFaction();
                    typeof(ScenPart_PlayerFaction).GetField("factionDef",
                            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                        ?.SetValue(isolatedFactionPart, FactionDefOf.PlayerColony);
                    typeof(Scenario).GetField("parts", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                        ?.SetValue(isolatedScenario, new List<ScenPart> { isolatedFactionPart });
                    candidate.game.Scenario = isolatedScenario;
                    phase = "initializing isolated game lifecycle";
                    Current.Game = candidate.game;
                    candidate.game.InitNewGame();
                    candidate.map = candidate.game.CurrentMap;
                    if (candidate.map == null)
                        throw new InvalidOperationException("Game.InitNewGame returned no map");
                    foreach (IntVec3 cell in candidate.map.AllCells)
                        candidate.map.terrainGrid.SetTerrain(cell, TerrainDefOf.Sand);
                    candidate.parent = candidate.map.Parent;
                    phase = "initializing required components";
                    if (candidate.map.GetComponent<HerdMapComponent>() == null ||
                        candidate.map.GetComponent<WildlifeSignalCultureMapComponent>() == null ||
                        candidate.map.GetComponent<RegionalWildlifeMapComponent>() == null)
                        throw new InvalidOperationException("isolated map components unavailable");
                    fixture = candidate;
                    return true;
                }
                catch (Exception exception)
                {
                    detail = "isolated map creation failed during " + phase + ": " + exception;
                    Map generatedMap = typeof(MapGenerator).GetField("mapBeingGenerated",
                        BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) as Map;
                    if (generatedMap != null)
                        detail += " generatedBiome=" + (generatedMap.Biome?.defName ?? "null") +
                            " tile=" + generatedMap.Tile;
                    detail += " activeTile=" + activeMap.Tile +
                        " activeBiome=" + (activeMap.Biome?.defName ?? "null") +
                        " parentBiome=" + (candidate.parent?.Biome?.defName ?? "null") +
                        " parentDef=" + (candidate.parent?.def?.defName ?? "null");
                    Log.Error("[WildlifeTest][IsolatedMap] " + detail);
                    candidate.Cleanup(out string cleanupDetail);
                    if (!cleanupDetail.NullOrEmpty()) detail += "; cleanup: " + cleanupDetail;
                    return false;
                }
            }

            public bool Cleanup(out string detail)
            {
                detail = "";
                Exception cleanupException = null;
                try
                {
                    if (map != null && game != null && game.Maps.Contains(map))
                        game.DeinitAndRemoveMap(map, true);
                    else if (map != null)
                        AccessTools.Method(typeof(Map), "Finalize")?.Invoke(map, null);
                }
                catch (Exception exception)
                {
                    cleanupException = exception.GetBaseException();
                }
                finally
                {
                    Current.Game = previousGame;
                    Map mapToRestore = previousGameCurrentMap ?? previousCurrentMap;
                    if (previousGame != null && mapToRestore != null && !previousGame.Maps.Contains(mapToRestore))
                        previousGame.AddMap(mapToRestore);
                    if (previousGame != null && mapToRestore != null)
                        previousGame.CurrentMap = mapToRestore;
                    if (uiCaptured)
                    {
                        bool uiRestored = RestoreUiState(out string uiDetail);
                        if (!uiRestored)
                            cleanupException = new InvalidOperationException("UI restoration failed: " + uiDetail);
                    }
                }
                if (cleanupException != null)
                {
                    detail = cleanupException.GetType().Name + ": " + cleanupException.Message;
                    return false;
                }
                map = null;
                game = null;
                parent = null;
                return true;
            }

            private bool RestoreUiState(out string detail)
            {
                detail = "";
                if (Find.CurrentMap != previousCurrentMap)
                    detail += "current map changed; ";
                try
                {
                    Designator currentDesignator = Find.DesignatorManager?.SelectedDesignator;
                    if (previousDesignator == null)
                        Find.DesignatorManager?.Deselect();
                    else
                        Find.DesignatorManager?.Select(previousDesignator);
                    Find.Selector?.ClearSelection();
                    if (previousSelectedObjects != null)
                        for (int i = 0; i < previousSelectedObjects.Count; i++)
                            if (previousSelectedObjects[i] != null)
                                Find.Selector.Select(previousSelectedObjects[i], false, false);
                    IList<Window> currentWindows = Find.WindowStack?.Windows;
                    if (currentWindows != null && previousWindows != null &&
                        (currentWindows.Count != previousWindows.Count || currentWindows.Where((value, index) => value != previousWindows[index]).Any()))
                        detail += "window stack changed; ";
                    if (currentDesignator != previousDesignator && Find.DesignatorManager?.SelectedDesignator != previousDesignator)
                        detail += "designator restore failed; ";
                }
                catch (Exception exception)
                {
                    detail += "UI restore " + exception;
                }
                return detail.NullOrEmpty();
            }
        }

        public static string ReportPath => Path.Combine(GenFilePaths.SaveDataFolderPath, FileName);

        private static PawnKindDef TestAnimalKind(bool predator, Map map,
            WildlifeSignalCultureMapComponent signals)
        {
            return DefDatabase<PawnKindDef>.AllDefsListForReading
                .Where(value => value?.race?.race?.Animal == true &&
                    WildlifeSpeciesClassification.IsPredator(value.race) == predator &&
                    (!predator || value.race.race.Humanlike != true))
                .OrderBy(value => signals?.ColonyPredatorPressure(value.race)?.claimObservationCount ?? 0)
                .FirstOrDefault(value => (signals?.ColonyPredatorPressure(value.race)?.claimObservationCount ?? 0) == 0);
        }

        private static bool TryTestCell(Map map, IntVec3 origin, float minDistance, float maxDistance,
            HashSet<IntVec3> used, out IntVec3 result)
        {
            for (int z = 1; z < map.Size.z - 1; z++)
                for (int x = 1; x < map.Size.x - 1; x++)
                {
                    IntVec3 candidate = new IntVec3(x, 0, z);
                    if (used.Contains(candidate) || !candidate.Standable(map) ||
                        !candidate.InHorDistOf(origin, maxDistance) ||
                        candidate.InHorDistOf(origin, minDistance)) continue;
                    result = candidate;
                    return true;
                }
            result = IntVec3.Invalid;
            return false;
        }

        private static Pawn SpawnTestPawn(Map map, PawnKindDef kind, Faction faction, IntVec3 cell,
            List<Pawn> created)
        {
            Pawn pawn = PawnGenerator.GeneratePawn(kind, faction);
            if (pawn == null) return null;
            GenSpawn.Spawn(pawn, cell, map, Rot4.North);
            if (pawn.Spawned) created.Add(pawn);
            return pawn;
        }

        private static bool StartProductionPredatorHunt(Pawn predator, Pawn prey)
        {
            if (predator?.Spawned != true || prey?.Spawned != true || predator.jobs == null) return false;
            predator.jobs.StartJob(JobMaker.MakeJob(JobDefOf.PredatorHunt, prey), JobCondition.InterruptForced);
            return predator.CurJobDef == JobDefOf.PredatorHunt && predator.CurJob?.targetA.Thing == prey;
        }

        private static Faction CreateFixtureHostileFaction(Map map)
        {
            return Faction.OfPirates;
        }

        private static int MaxTraceId(WildlifeSignalCultureMapComponent signals)
        {
            return (signals?.RecentSignals?.Select(value => value?.traceId ?? -1).DefaultIfEmpty(-1).Max() ?? -1) + 1;
        }

        private static WildlifeSignalTrace NewTrace(WildlifeSignalCultureMapComponent signals, int previousId,
            WildlifeSignalKind kind)
        {
            return signals?.RecentSignals?.FirstOrDefault(value => value != null && value.traceId >= previousId &&
                value.kind == kind);
        }

        private static bool VerifySignals(WildlifeSignalCultureMapComponent signals, int tick)
        {
            MethodInfo method = AccessTools.Method(typeof(WildlifeSignalCultureMapComponent), "VerifyActiveSignals");
            if (method == null) return false;
            try
            {
                method.Invoke(signals, new object[] { tick });
                return true;
            }
            catch { return false; }
        }

        private static bool UpdateDefense(HerdMapComponent herds, int tick)
        {
            MethodInfo method = AccessTools.Method(typeof(HerdMapComponent), "UpdateDefense");
            if (method == null) return false;
            try
            {
                method.Invoke(herds, new object[] { tick });
                return true;
            }
            catch { return false; }
        }

        private static string GameFingerprint(Game game)
        {
            if (game?.Maps == null) return "";
            string maps = string.Join("|", game.Maps.OrderBy(value => value?.uniqueID ?? -1).Select(map =>
            {
                return (map?.uniqueID ?? -1) + "/" + (map?.Tile.ToString() ?? "null") + "/" +
                    (map?.Parent?.def?.defName ?? "null") + "/" + FactionName(map?.ParentFaction) + ":" +
                    string.Join(",", (map?.mapPawns?.AllPawnsSpawned ?? new List<Pawn>())
                        .OrderBy(value => value?.thingIDNumber ?? -1)
                        .Select(value => ThingFingerprint(value))) + ":" +
                    string.Join(",", (map?.listerThings?.AllThings ?? new List<Thing>())
                        .OrderBy(value => value?.thingIDNumber ?? -1)
                        .Select(value => ThingFingerprint(value))) + ":" + SignalFingerprint(map);
            }));
            return "game=" + RuntimeHelpers.GetHashCode(game) + "/world=" + RuntimeHelpers.GetHashCode(game.World) +
                "/tick=" + (Find.TickManager?.TicksGame ?? -1) + "/currentMap=" + (game.CurrentMap?.uniqueID ?? -1) +
                "/worldObjects=" + WorldObjectFingerprint(game.World) + "/knowledge=" + KnowledgeFingerprint(game) +
                "/settings=" + SettingsFingerprint() + "/ui=" + UiFingerprint() + "/maps=" + maps;
        }

        private static string ThingFingerprint(Thing thing)
        {
            return (thing?.thingIDNumber ?? -1) + "/" + thing?.def?.defName + "/" + thing?.Position + "/" +
                FactionName(thing?.Faction) + "/" + (thing?.Spawned == true);
        }

        private static string FactionName(Faction faction)
        {
            return faction?.def?.defName ?? (faction == null ? "null" : "unknown");
        }

        private static string SignalFingerprint(Map map)
        {
            WildlifeSignalCultureMapComponent signals = map?.GetComponent<WildlifeSignalCultureMapComponent>();
            if (signals == null) return "none";
            return string.Join(",", signals.RecentSignals.Select(value =>
            {
                if (value == null) return "null";
                return value.traceId + "/" + value.kind + "/" + value.subjectWasPredator + "/" + value.developerScenario + "/" +
                    string.Join(";", value.presentations == null ? Enumerable.Empty<string>() : value.presentations
                        .Where(item => item != null).Select(item => item.observer?.thingIDNumber + "/" +
                            item.warningKnowledgeSourceInstanceId + "/" + item.predatorPressureSourceInstanceId + "/" +
                            item.predatorPressureSubmitted));
            })) +
                "/warningSources=" + string.Join(",", signals.WarningKnowledgeSources ?? Array.Empty<string>());
        }

        private static string WorldObjectFingerprint(World world)
        {
            object holder = world?.GetType().GetField("worldObjects", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(world);
            IEnumerable<object> objects = Enumerable.Empty<object>();
            object worldObjects = holder?.GetType().GetProperty("AllWorldObjects")?.GetValue(holder, null);
            if (worldObjects is System.Collections.IEnumerable enumerable)
                objects = enumerable.Cast<object>();
            return string.Join(",", objects.Select(value =>
                value?.GetType().GetProperty("ID")?.GetValue(value, null) + "/" +
                value?.GetType().GetProperty("def")?.GetValue(value, null)?.GetType().GetProperty("defName")?.GetValue(
                    value?.GetType().GetProperty("def")?.GetValue(value, null), null) + "/" +
                value?.GetType().GetProperty("Tile")?.GetValue(value, null)));
        }

        private static string KnowledgeFingerprint(Game game)
        {
            if (game == null || KnowledgeComponentType() == null) return "none";
            KnowledgeDiagnosticsSnapshot diagnostics = KnowledgeDiagnostics.Snapshot();
            return diagnostics.claimCount + "/" + diagnostics.measurementCount + "/" + diagnostics.contextCount + "/" +
                diagnostics.milestoneCount + "/" + diagnostics.relationCount + "/" + diagnostics.accrualPolicyKeyCount +
                "/revision=" + KnowledgeQuery.Revision + "/migration=" +
                KnowledgeMigrationService.IsCommitted("wildlife.v3.legacy", 1);
        }

        private static string SettingsFingerprint()
        {
            object settings = HerdsMod.Settings;
            if (settings == null) return "none";
            return string.Join(",", settings.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(field => field.FieldType.IsPrimitive || field.FieldType.IsEnum || field.FieldType == typeof(string))
                .OrderBy(field => field.Name)
                .Select(field => field.Name + "=" + (field.GetValue(settings) ?? "null")));
        }

        private static string UiFingerprint()
        {
            return "map=" + (Find.CurrentMap?.uniqueID ?? -1) + "/selected=" + string.Join(",",
                Find.Selector?.SelectedObjectsListForReading?.Select(value =>
                    value is Thing thing ? ThingFingerprint(thing) : value?.GetType().FullName + "/" + RuntimeHelpers.GetHashCode(value)) ?? Enumerable.Empty<string>()) +
                "/designator=" + Find.DesignatorManager?.SelectedDesignator?.GetType().FullName +
                "/windows=" + string.Join(",", Find.WindowStack?.Windows?.Select(value =>
                    value?.GetType().FullName + "/" + RuntimeHelpers.GetHashCode(value)) ?? Enumerable.Empty<string>());
        }

        private static MethodInfo ScribeLookMethod()
        {
            return typeof(Scribe_Deep).GetMethods(BindingFlags.Static | BindingFlags.Public)
                .Where(method => method.Name == "Look" && method.IsGenericMethodDefinition)
                .Where(method => method.GetGenericArguments().Length == 1)
                .Where(method => method.GetParameters().Length == 3)
                .FirstOrDefault();
        }

        private static object ScribeLook(Type type, object value, string label, params object[] ctorArgs)
        {
            MethodInfo method = ScribeLookMethod();
            if (method == null || type == null) return null;
            object[] arguments = { value, label, ctorArgs };
            method.MakeGenericMethod(type).Invoke(null, arguments);
            return arguments[0];
        }

        private static Type KnowledgeComponentType()
        {
            return typeof(GameComponent_KnowledgeFramework);
        }

        private static object CurrentKnowledgeComponent(Type type)
        {
            return GameComponent_KnowledgeFramework.Current;
        }

        private static bool InstallLoadedMapComponent(Map map, MapComponent loaded, out string detail)
        {
            detail = "";
            if (map == null || loaded == null)
            {
                detail = "loaded map component unavailable";
                return false;
            }
            FieldInfo componentsField = typeof(Map).GetField("components", BindingFlags.Instance | BindingFlags.NonPublic);
            if (!(componentsField?.GetValue(map) is System.Collections.IList components))
            {
                detail = "map component list unavailable";
                return false;
            }
            for (int i = 0; i < components.Count; i++)
                if (components[i]?.GetType() == loaded.GetType())
                {
                    components[i] = loaded;
                    return true;
                }
            components.Add(loaded);
            return true;
        }

        private static bool InstallLoadedGameComponent(Game game, object loaded, out string detail)
        {
            detail = "";
            if (game == null || loaded == null)
            {
                detail = "loaded game component unavailable";
                return false;
            }
            FieldInfo componentsField = typeof(Game).GetField("components", BindingFlags.Instance | BindingFlags.NonPublic);
            if (!(componentsField?.GetValue(game) is System.Collections.IList components))
            {
                detail = "game component list unavailable";
                return false;
            }
            for (int i = 0; i < components.Count; i++)
                if (components[i]?.GetType() == loaded.GetType())
                {
                    components[i] = loaded;
                    return true;
                }
            components.Add(loaded);
            return true;
        }

        private static bool ScribeRoundTripCheck(Map map, WildlifeSignalTrace expected, string eventSourceId,
            out string detail)
        {
            detail = "";
            string path = Path.Combine(GenFilePaths.SaveDataFolderPath,
                "Wildlife-Predator-Encounter-RoundTrip-" + Guid.NewGuid().ToString("N") + ".xml");
            string missingFieldPath = path + ".missing";
            try
            {
                if (map == null || expected == null || eventSourceId.NullOrEmpty())
                {
                    detail = "round-trip inputs unavailable";
                    return false;
                }
                Type knowledgeType = KnowledgeComponentType();
                object knowledge = CurrentKnowledgeComponent(knowledgeType);
                string populationSubject = WildlifeKnowledgeAdapter.PopulationSubjectId(map, expected.species);
                KnowledgeContextKey claimContext = WildlifeKnowledgeAdapter.ContextFor(map, expected.cell);
                KnowledgeClaimSnapshot expectedClaim = KnowledgeClaimService.Snapshot(
                    WildlifeKnowledgeAdapter.DomainId, populationSubject, WildlifeKnowledgeAdapter.FacetPopulation,
                    WildlifeKnowledgeAdapter.ClaimPredatorPressure, null, KnowledgeScope.Colony, claimContext,
                    KnowledgeContextFallbackMode.ExactOnly);
                Pawn expectedObserver = expected.presentations.FirstOrDefault(value => value?.observer != null)?.observer;
                if (expectedClaim == null || !expectedClaim.provenance.Any(value => value?.sourceInstanceId == eventSourceId) ||
                    expectedObserver == null)
                {
                    detail = "round-trip claim or observer unavailable";
                    return false;
                }

                WildlifeSignalCultureMapComponent liveSignals = map.GetComponent<WildlifeSignalCultureMapComponent>();
                if (liveSignals == null || knowledge == null)
                {
                    detail = "authoritative Scribe owners unavailable";
                    return false;
                }
                List<Pawn> observers = expected.presentations.Where(value => value?.observer != null)
                    .Select(value => value.observer).Distinct().ToList();
                Scribe.saver.InitSaving(path, "wildlifePredatorEncounterRoundTrip");
                for (int i = 0; i < observers.Count; i++)
                {
                    Pawn savedObserver = observers[i];
                    Scribe_References.Look(ref savedObserver, "observer" + i);
                }
                ScribeLook(typeof(WildlifeSignalCultureMapComponent), liveSignals, "signalCulture", map);
                ScribeLook(knowledgeType, knowledge, "knowledgeFramework", Current.Game);
                Scribe.saver.FinalizeSaving();

                Scribe.loader.InitLoading(path);
                List<Pawn> loadedObservers = new List<Pawn>();
                for (int i = 0; i < observers.Count; i++)
                {
                    Pawn roundTripObserver = null;
                    Scribe_References.Look(ref roundTripObserver, "observer" + i);
                    loadedObservers.Add(roundTripObserver);
                }
                WildlifeSignalCultureMapComponent loadedSignals = ScribeLook(
                    typeof(WildlifeSignalCultureMapComponent), null, "signalCulture", map)
                    as WildlifeSignalCultureMapComponent;
                object loadedKnowledge = ScribeLook(knowledgeType, null, "knowledgeFramework", Current.Game);
                Scribe.loader.FinalizeLoading();

                if (!InstallLoadedMapComponent(map, loadedSignals, out string installDetail) ||
                    !InstallLoadedGameComponent(Current.Game, loadedKnowledge, out installDetail))
                {
                    detail = "loaded owner installation failed: " + installDetail;
                    return false;
                }
                WildlifeSignalCultureMapComponent installedSignals = map.GetComponent<WildlifeSignalCultureMapComponent>();

                WildlifeSignalTrace loadedTrace = installedSignals?.RecentSignals?.FirstOrDefault(value =>
                    value?.traceId == expected.traceId);
                KnowledgeClaimSnapshot loadedClaim = KnowledgeClaimService.Snapshot(
                    WildlifeKnowledgeAdapter.DomainId, populationSubject, WildlifeKnowledgeAdapter.FacetPopulation,
                    WildlifeKnowledgeAdapter.ClaimPredatorPressure, null, KnowledgeScope.Colony, claimContext,
                    KnowledgeContextFallbackMode.ExactOnly);
                bool claimSourceLoaded = loadedClaim?.provenance.Any(value => value?.sourceInstanceId == eventSourceId) == true;
                bool traceLoaded = loadedTrace != null && loadedTrace.subjectWasPredator == expected.subjectWasPredator &&
                    loadedTrace.developerScenario == expected.developerScenario &&
                    loadedTrace.presentations.Any(value => loadedObservers.Contains(value?.observer)) &&
                    loadedTrace.presentations.Any(value => value?.predatorPressureSubmitted == true &&
                        value.predatorPressureSourceInstanceId == eventSourceId) &&
                    loadedTrace.presentations.Count(value => value?.predatorPressureSourceInstanceId == eventSourceId) ==
                    expected.presentations.Count(value => value?.predatorPressureSourceInstanceId == eventSourceId);
                bool alreadyApplied = WildlifeKnowledgeAdapter.PredatorPressureObservationAlreadyApplied(null,
                    expected.species, map, expected.cell, eventSourceId);
                int loadedClaimCount = installedSignals?.ColonyPredatorPressure(expected.species)?.claimObservationCount ?? 0;
                int loadedTraceCount = installedSignals?.RecentSignals?.Count ?? 0;
                WildlifeSignalTrace developerTrace = liveSignals.RecentSignals.FirstOrDefault(value =>
                    value?.developerScenario == true);
                bool developerRoundTrip = developerTrace == null || installedSignals.RecentSignals.Any(value =>
                    value?.traceId == developerTrace.traceId && value.developerScenario);
                loadedTrace?.NormalizePostLoadState();
                int normalizedCount = loadedTrace?.presentations?.Count ?? -1;
                string normalizedSource = loadedTrace?.presentations?.FirstOrDefault()?.predatorPressureSourceInstanceId;
                loadedTrace?.NormalizePostLoadState();
                bool normalizationIdempotent = loadedTrace != null && normalizedCount == loadedTrace.presentations.Count &&
                    normalizedSource == loadedTrace.presentations.FirstOrDefault()?.predatorPressureSourceInstanceId;

                WildlifeSignalTrace distinctTrace = null;
                Pawn loadedObserver = loadedObservers.FirstOrDefault(value => value != null);
                Pawn loadedPredator = map.mapPawns.AllPawnsSpawned.FirstOrDefault(value =>
                    value?.Spawned == true && WildlifeSpeciesClassification.IsPredator(value.def));
                if (loadedObserver != null && loadedPredator != null)
                {
                    int nextTrace = installedSignals.RecentSignals.Select(value => value?.traceId ?? -1)
                        .DefaultIfEmpty(-1).Max() + 1;
                    installedSignals.NotifyAnimalSignal(expected.species, WildlifeSignalKind.Alarm,
                        loadedObserver, loadedPredator, true, 35f);
                    distinctTrace = installedSignals.RecentSignals.FirstOrDefault(value =>
                        value?.traceId >= nextTrace && value.kind == WildlifeSignalKind.Alarm);
                }
                bool distinctTraceEligible = distinctTrace != null && distinctTrace.subjectWasPredator &&
                    distinctTrace.traceId != expected.traceId &&
                    WildlifeKnowledgeAdapter.PredatorPressureEventSourceInstanceId(map, distinctTrace.traceId) != eventSourceId;

                string serialized = File.ReadAllText(path);
                File.WriteAllText(missingFieldPath, serialized.Replace("<subjectWasPredator>True</subjectWasPredator>", "")
                    .Replace("<subjectWasPredator>False</subjectWasPredator>", ""));
                Scribe.loader.InitLoading(missingFieldPath);
                for (int i = 0; i < observers.Count; i++)
                {
                    Pawn missingObserver = null;
                    Scribe_References.Look(ref missingObserver, "observer" + i);
                }
                WildlifeSignalCultureMapComponent missingSignals = ScribeLook(
                    typeof(WildlifeSignalCultureMapComponent), null, "signalCulture", map)
                    as WildlifeSignalCultureMapComponent;
                ScribeLook(knowledgeType, null, "knowledgeFramework", Current.Game);
                Scribe.loader.FinalizeLoading();
                WildlifeSignalTrace missingTrace = missingSignals?.RecentSignals?.FirstOrDefault(value =>
                    value?.traceId == expected.traceId);
                bool missingDefaultsFalse = missingTrace != null && !missingTrace.subjectWasPredator;
                detail = "trace=" + traceLoaded + " claim=" + claimSourceLoaded +
                    " alreadyApplied=" + alreadyApplied + " missingDefaultsFalse=" + missingDefaultsFalse +
                    " installedClaimCount=" + loadedClaimCount + " loadedTraces=" + loadedTraceCount +
                    " developer=" + developerRoundTrip + " normalization=" + normalizationIdempotent +
                    " distinct=" + distinctTraceEligible;
                return traceLoaded && claimSourceLoaded && alreadyApplied && missingDefaultsFalse &&
                    developerRoundTrip && normalizationIdempotent && distinctTraceEligible;
            }
            catch (Exception exception)
            {
                detail = exception.GetBaseException().Message;
                return false;
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
                if (File.Exists(missingFieldPath)) File.Delete(missingFieldPath);
            }
        }

        private static DeterministicPredatorPressureResult DeterministicPredatorPressureCheck(Map activeMap)
        {
            DeterministicPredatorPressureResult result = new DeterministicPredatorPressureResult();
            string before = GameFingerprint(Current.Game);
            if (!IsolatedMapFixture.TryCreate(activeMap, out IsolatedMapFixture fixture, out string createDetail))
            {
                result.detail = createDetail;
                return result;
            }
            try
            {
                result = DeterministicPredatorPressureCheckOnMap(fixture.map);
            }
            catch (Exception exception)
            {
                result.detail = exception.ToString();
            }
            finally
            {
                bool cleaned = fixture.Cleanup(out string cleanupDetail);
                result.isolatedCleanup = cleaned;
                result.activeStateUntouched = before == GameFingerprint(Current.Game);
                if (!cleaned || !result.activeStateUntouched)
                    result.detail = (result.detail.NullOrEmpty() ? "" : result.detail + "; ") +
                        "isolatedCleanup=" + cleaned + " activeStateUntouched=" + result.activeStateUntouched +
                        (cleanupDetail.NullOrEmpty() ? "" : " cleanup=" + cleanupDetail);
            }
            return result;
        }

        private static DeterministicPredatorPressureResult DeterministicPredatorPressureCheckOnMap(Map map)
        {
            DeterministicPredatorPressureResult result = new DeterministicPredatorPressureResult();
            List<Pawn> created = new List<Pawn>();
            HashSet<IntVec3> used = new HashSet<IntVec3>();
            HashSet<int> traceIds = new HashSet<int>();
            ThingDef preySpecies = null;
            try
            {
                if (map == null || HerdsMod.Settings == null) throw new InvalidOperationException("test map/settings unavailable");
                if (!HerdsMod.Settings.enableDefensiveBehavior || !HerdsMod.Settings.enableWildlifeSignalCulture)
                    throw new InvalidOperationException("defensive behavior or signal culture is disabled");
                WildlifeSignalCultureMapComponent existingSignals = map.GetComponent<WildlifeSignalCultureMapComponent>();
                PawnKindDef preyKind = TestAnimalKind(false, map, existingSignals);
                PawnKindDef predatorKind = TestAnimalKind(true, map, existingSignals);
                if (preyKind == null || predatorKind == null) throw new InvalidOperationException("animal Def fixture unavailable");
                preySpecies = preyKind.race;
                 if (!TryTestCell(map, map.Center, 0f, 999f, used, out IntVec3 preyCell) ||
                     !TryTestCell(map, preyCell, 30f, 40f, used, out IntVec3 predatorCell) ||
                    !TryTestCell(map, preyCell, 1f, 10f, used, out IntVec3 colonistACell) ||
                    !TryTestCell(map, preyCell, 1f, 10f, used, out IntVec3 colonistBCell))
                    throw new InvalidOperationException("deterministic standable cells unavailable");
                used.Add(preyCell); used.Add(predatorCell); used.Add(colonistACell); used.Add(colonistBCell);
                Pawn prey = SpawnTestPawn(map, preyKind, null, preyCell, created);
                Pawn predator = SpawnTestPawn(map, predatorKind, null, predatorCell, created);
                Pawn colonistA = SpawnTestPawn(map, PawnKindDefOf.Colonist, Faction.OfPlayer, colonistACell, created);
                Pawn colonistB = SpawnTestPawn(map, PawnKindDefOf.Colonist, Faction.OfPlayer, colonistBCell, created);
                HerdMapComponent herds = map.GetComponent<HerdMapComponent>();
                WildlifeSignalCultureMapComponent signals = map.GetComponent<WildlifeSignalCultureMapComponent>();
                if (prey == null || predator == null || colonistA == null || colonistB == null || herds == null || signals == null)
                    throw new InvalidOperationException("deterministic actor or component creation failed");
                herds.ForceRefresh();
                if (herds.HerdFor(prey) == null) throw new InvalidOperationException("prey did not enter herd assessment");
                int baselineClaimCount = signals.ColonyPredatorPressure(preySpecies)?.claimObservationCount ?? 0;
                if (baselineClaimCount != 0) throw new InvalidOperationException("predator fixture was not clean");

                int beforeNoThreat = MaxTraceId(signals);
                bool noOrder = herds.DefenseOrderFor(prey) == null;
                result.noQualifyingThreat = noOrder && MaxTraceId(signals) == beforeNoThreat &&
                    (signals.ColonyPredatorPressure(preySpecies)?.claimObservationCount ?? 0) == baselineClaimCount;

                 MoveTestPawn(map, predator, prey.Position, 4f, used);
                 int beforeFirst = MaxTraceId(signals);
                 bool triggered = StartProductionPredatorHunt(predator, prey);
                 bool assessed = UpdateDefense(herds, Find.TickManager.TicksGame + 1);
                 UpdateDefense(herds, Find.TickManager.TicksGame + 121);
                 WildlifeSignalTrace firstAlarm = NewTrace(signals, beforeFirst, WildlifeSignalKind.Alarm);
                 if (firstAlarm == null) throw new InvalidOperationException("production Alarm was not emitted triggered=" +
                     triggered + " assessed=" + assessed + " herd=" + (herds.HerdFor(prey) != null) + " order=" +
                    (herds.DefenseOrderFor(prey) != null) + " traces=" + MaxTraceId(signals) +
                    " setting=" + HerdsMod.Settings.enableWildlifeSignalCulture + " recent=" +
                    string.Join(",", signals.RecentSignals.Select(value => value == null ? "null" :
                        value.traceId + ":" + value.kind)) + " prey=" + prey.def.defName +
                    " predator=" + predator.def.defName + " humanlike=" + predator.RaceProps.Humanlike +
                    " classified=" + WildlifeSpeciesClassification.IsPredator(predator.def));
                traceIds.Add(firstAlarm.traceId);
                bool firstObserved = VerifySignals(signals, firstAlarm.tick + 90);
                int firstClaimCount = signals.ColonyPredatorPressure(preySpecies)?.claimObservationCount ?? 0;
                WildlifePredatorPressureKnowledgeState firstState = signals.ColonyPredatorPressure(preySpecies);
                int firstSubmitted = firstAlarm.presentations.Count(value => value?.predatorPressureSubmitted == true);
                int firstSourceCount = firstAlarm.presentations.Where(value => value?.predatorPressureSubmitted == true)
                    .Select(value => value.predatorPressureSourceInstanceId).Distinct().Count();
                result.genuineDefense = triggered && herds.DefenseOrderFor(prey) != null && firstObserved && firstAlarm.verified;
                result.productionClassification = firstAlarm.subjectWasPredator &&
                    WildlifeKnowledgeAdapter.IsPredatorPressureTrace(firstAlarm);
                result.sharedPresentations = firstAlarm.presentations.Count == 2 && firstSubmitted == 2 &&
                    firstSourceCount == 1 && firstAlarm.presentations.Select(value => value.warningKnowledgeSourceInstanceId)
                        .Where(value => !value.NullOrEmpty()).Distinct().Count() == 2;
                result.oneColonyEvent = firstClaimCount == 1 && firstSourceCount == 1;
                result.firstEvidenceAmbiguous = firstClaimCount == baselineClaimCount + 1 &&
                    firstState?.hasEvidence == true && firstState.patternRecognized == false &&
                    firstState.meaningInterpreted == false && firstState.claimSupported == false;
                int afterFirst = firstClaimCount;
                VerifySignals(signals, firstAlarm.tick + 180);
                result.reprocessingIdempotent = (signals.ColonyPredatorPressure(preySpecies)?.claimObservationCount ?? 0) == afterFirst;
                for (int i = 0; i < firstAlarm.presentations.Count; i++)
                    if (firstAlarm.presentations[i]?.predatorPressureSourceInstanceId != null)
                        traceIds.Add(firstAlarm.traceId);
                MoveTestPawn(map, predator, prey.Position, 60f, used);
                 predator.jobs?.EndCurrentJob(JobCondition.InterruptForced);
                 UpdateDefense(herds, Find.TickManager.TicksGame + 61);
                 MoveTestPawn(map, colonistA, prey.Position, 4f, used);
                MoveTestPawn(map, colonistB, prey.Position, 60f, used);
                int beforeFiltered = MaxTraceId(signals);
                signals.NotifyAnimalSignal(preySpecies, WildlifeSignalKind.Alarm, prey, predator, true, 6f);
                WildlifeSignalTrace filtered = NewTrace(signals, beforeFiltered, WildlifeSignalKind.Alarm);
                if (filtered != null) traceIds.Add(filtered.traceId);
                VerifySignals(signals, (filtered?.tick ?? Find.TickManager.TicksGame) + 90);
                result.ineligibleObserverExcluded = filtered != null && filtered.observerCount == 1 &&
                    filtered.presentations.Count == 1 && filtered.presentations[0].observer == colonistA &&
                    filtered.presentations.All(value => value.observer != colonistB);
                if (filtered != null) foreach (WildlifeSignalObservationPresentation value in filtered.presentations)
                    if (!value.predatorPressureSourceInstanceId.NullOrEmpty()) traceIds.Add(filtered.traceId);

                MoveTestPawn(map, colonistA, prey.Position, 60f, used);
                MoveTestPawn(map, colonistB, prey.Position, 70f, used);
                int beforeUnobserved = MaxTraceId(signals);
                int claimsBeforeUnobserved = signals.ColonyPredatorPressure(preySpecies)?.claimObservationCount ?? 0;
                signals.NotifyAnimalSignal(preySpecies, WildlifeSignalKind.Alarm, prey, predator, true, 1f);
                WildlifeSignalTrace unobserved = NewTrace(signals, beforeUnobserved, WildlifeSignalKind.Alarm);
                if (unobserved != null) traceIds.Add(unobserved.traceId);
                VerifySignals(signals, (unobserved?.tick ?? Find.TickManager.TicksGame) + 90);
                result.unobservedExcluded = unobserved != null && unobserved.observerCount == 0 &&
                    unobserved.presentations.Count == 0 &&
                    (signals.ColonyPredatorPressure(preySpecies)?.claimObservationCount ?? 0) == claimsBeforeUnobserved;

                MoveTestPawn(map, colonistA, prey.Position, 4f, used);
                MoveTestPawn(map, colonistB, prey.Position, 6f, used);
                 Faction hostileFaction = CreateFixtureHostileFaction(map);
                 if (hostileFaction == null) throw new InvalidOperationException("hostile fixture faction unavailable");
                 colonistB.SetFaction(hostileFaction);
                 int beforeHostile = MaxTraceId(signals);
                 bool hostileHunt = StartProductionPredatorHunt(colonistB, prey);
                 UpdateDefense(herds, Find.TickManager.TicksGame + 1);
                 UpdateDefense(herds, Find.TickManager.TicksGame + 121);
                 WildlifeSignalTrace hostile = NewTrace(signals, beforeHostile, WildlifeSignalKind.HumanDanger);
                 if (hostile != null) traceIds.Add(hostile.traceId);
                 result.hostileNonPredatorExcluded = hostileHunt && hostile != null && hostile.subjectWasPredator == false &&
                     !WildlifeKnowledgeAdapter.IsPredatorPressureTrace(hostile) &&
                     !hostile.presentations.Any(value => value?.predatorPressureSubmitted == true);
                 colonistB.jobs?.EndCurrentJob(JobCondition.InterruptForced);
                 colonistB.SetFaction(Faction.OfPlayer);
                int warningBeforeHuman = signals.WarningKnowledgeSources.Count;
                int beforeHuman = MaxTraceId(signals);
                signals.NotifyAnimalSignal(preySpecies, WildlifeSignalKind.HumanDanger, prey, colonistB, true, 12f);
                WildlifeSignalTrace human = NewTrace(signals, beforeHuman, WildlifeSignalKind.HumanDanger);
                if (human != null) traceIds.Add(human.traceId);
                result.warningPathPreserved = human != null && human.presentations.Any(value => value?.warningKnowledgeSubmitted == true) &&
                    signals.WarningKnowledgeSources.Count > warningBeforeHuman;

                int warningBeforeDebug = signals.WarningKnowledgeSources.Count;
                int beforeDebug = MaxTraceId(signals);
                signals.NotifyDeveloperSignal(preySpecies, WildlifeSignalKind.Alarm, prey, predator, true, 35f);
                WildlifeSignalTrace debugAlarm = NewTrace(signals, beforeDebug, WildlifeSignalKind.Alarm);
                if (debugAlarm != null) traceIds.Add(debugAlarm.traceId);
                int beforeDebugHuman = MaxTraceId(signals);
                signals.NotifyDeveloperSignal(preySpecies, WildlifeSignalKind.HumanDanger, prey, predator, true, 35f);
                WildlifeSignalTrace debugHuman = NewTrace(signals, beforeDebugHuman, WildlifeSignalKind.HumanDanger);
                if (debugHuman != null) traceIds.Add(debugHuman.traceId);
                result.fabricatedExcluded = debugAlarm?.developerScenario == true && debugHuman?.developerScenario == true &&
                    debugAlarm.subjectWasPredator == false && debugHuman.subjectWasPredator == false &&
                    !debugAlarm.presentations.Any(value => value?.warningKnowledgeSubmitted == true) &&
                    !debugHuman.presentations.Any(value => value?.warningKnowledgeSubmitted == true) &&
                    signals.WarningKnowledgeSources.Count == warningBeforeDebug;

                 MoveTestPawn(map, predator, prey.Position, 4f, used);
                 int beforeSimulated = MaxTraceId(signals);
                 bool simulated = StartProductionPredatorHunt(predator, prey);
                 UpdateDefense(herds, Find.TickManager.TicksGame + 1);
                 UpdateDefense(herds, Find.TickManager.TicksGame + 121);
                 WildlifeSignalTrace simulatedAlarm = NewTrace(signals, beforeSimulated, WildlifeSignalKind.Alarm);
                if (simulatedAlarm != null) traceIds.Add(simulatedAlarm.traceId);
                result.simulatedPredatorEligible = simulated && simulatedAlarm?.subjectWasPredator == true &&
                    simulatedAlarm.developerScenario == false &&
                    WildlifeKnowledgeAdapter.IsPredatorPressureTrace(simulatedAlarm);
                 predator.jobs?.EndCurrentJob(JobCondition.InterruptForced);
                 UpdateDefense(herds, Find.TickManager.TicksGame + 61);

                int claimsBeforeReinforcement = signals.ColonyPredatorPressure(preySpecies)?.claimObservationCount ?? 0;
                for (int encounter = 1; encounter < 4; encounter++)
                {
                     MoveTestPawn(map, predator, prey.Position, 60f, used);
                     predator.jobs?.EndCurrentJob(JobCondition.InterruptForced);
                     UpdateDefense(herds, Find.TickManager.TicksGame + 61);
                     int prior = MaxTraceId(signals);
                     MoveTestPawn(map, predator, prey.Position, 4f, used);
                     if (!StartProductionPredatorHunt(predator, prey)) throw new InvalidOperationException("repeat predator hunt did not start");
                     UpdateDefense(herds, Find.TickManager.TicksGame + 1);
                     UpdateDefense(herds, Find.TickManager.TicksGame + 121);
                    WildlifeSignalTrace repeated = NewTrace(signals, prior, WildlifeSignalKind.Alarm);
                    if (repeated == null) throw new InvalidOperationException("repeat Alarm was not emitted");
                    traceIds.Add(repeated.traceId);
                    VerifySignals(signals, repeated.tick + 90);
                }
                WildlifePredatorPressureKnowledgeState finalState = signals.ColonyPredatorPressure(preySpecies);
                result.laterEncounterReinforced = (finalState?.claimObservationCount ?? 0) == claimsBeforeReinforcement + 3;
                result.localPredictionBounded = finalState?.claimSupported == true &&
                    !finalState.PlayerDescription.Contains("regional") &&
                    (finalState.PlayerDescription.Contains("nearby") || finalState.PlayerDescription.Contains("local"));

                int beforeClear = MaxTraceId(signals);
                int warningBeforeClear = signals.WarningKnowledgeSources.Count;
                 MoveTestPawn(map, predator, prey.Position, 60f, used);
                 predator.jobs?.EndCurrentJob(JobCondition.InterruptForced);
                 UpdateDefense(herds, Find.TickManager.TicksGame + 61);
                WildlifeSignalTrace allClear = NewTrace(signals, beforeClear, WildlifeSignalKind.AllClear);
                if (allClear != null) traceIds.Add(allClear.traceId);
                result.cleared = allClear != null && allClear.kind == WildlifeSignalKind.AllClear;
                result.allClearExcluded = signals.WarningKnowledgeSources.Count == warningBeforeClear &&
                    !signals.RecentSignals.Where(value => value?.traceId == allClear?.traceId)
                        .Any(value => value != null && value.presentations.Any(item => item?.warningKnowledgeSubmitted == true));

                WildlifeSignalTrace normalize = firstAlarm;
                WildlifeSignalObservationPresentation original = normalize.presentations.FirstOrDefault();
                if (original != null)
                {
                    normalize.presentations.Add(new WildlifeSignalObservationPresentation
                    {
                        observer = original.observer,
                        predatorPressureSourceInstanceId = original.predatorPressureSourceInstanceId,
                        predatorPressureSubmitted = original.predatorPressureSubmitted,
                        warningKnowledgeSourceInstanceId = original.warningKnowledgeSourceInstanceId,
                        warningKnowledgeSubmitted = original.warningKnowledgeSubmitted
                    });
                    normalize.NormalizePostLoadState();
                    int normalizedCount = normalize.presentations.Count;
                    string normalizedSource = normalize.presentations[0].predatorPressureSourceInstanceId;
                    normalize.NormalizePostLoadState();
                    result.normalizationIdempotent = normalizedCount == normalize.presentations.Count &&
                        normalizedSource == normalize.presentations[0].predatorPressureSourceInstanceId;
                }

                int claimsBeforeUi = signals.ColonyPredatorPressure(preySpecies)?.claimObservationCount ?? 0;
                int sourcesBeforeUi = signals.WarningKnowledgeSources.Count;
                _ = new Window_WildlifeJournal(map, WildlifeJournalPage.FieldLog);
                _ = new Window_WildlifeJournal(map, WildlifeJournalPage.Knowledge);
                _ = new Window_WildlifeJournal(map, WildlifeJournalPage.Region);
                _ = new Window_WildlifeSignals(map, colonistA, preySpecies);
                _ = new Window_WildlifeFieldJournal(map, 2);
                result.uiNonMutating = claimsBeforeUi == (signals.ColonyPredatorPressure(preySpecies)?.claimObservationCount ?? 0) &&
                    sourcesBeforeUi == signals.WarningKnowledgeSources.Count;
                bool selectedDeterrent = Window_WildlifeJournal.OpenPredatorDeterrent(map);
                ThingDef deterrentDef = HerdsDefOf.Herds_PredatorDeterrent;
                ResearchProjectDef pendingDeterrentResearch = deterrentDef?.researchPrerequisites?.FirstOrDefault(project =>
                    project != null && !project.IsFinished);
                bool unavailableDeterrent = !selectedDeterrent &&
                    (HerdsMod.Settings?.enablePredatorDeterrents != true || deterrentDef == null ||
                        deterrentDef.designationCategory == null || pendingDeterrentResearch != null);
                result.deterrentRoute = selectedDeterrent || unavailableDeterrent;
                if (selectedDeterrent) Find.DesignatorManager.Deselect();
                RegionalWildlifeMapComponent regional = map.GetComponent<RegionalWildlifeMapComponent>();
                string deterrentDetail = "regional ecology component unavailable";
                result.deterrentEffect = regional != null &&
                    regional.PredatorDeterrentIntegrationSelfTest(out deterrentDetail);
                RespawnTestPawn(map, predator, prey.Position, used);
                string firstEventSource = WildlifeKnowledgeAdapter.PredatorPressureEventSourceInstanceId(map, firstAlarm.traceId);
                result.scribeRoundTrip = ScribeRoundTripCheck(map, firstAlarm, firstEventSource, out string scribeDetail);
                result.detail = "first=" + firstClaimCount + " final=" + (finalState?.claimObservationCount ?? 0) +
                    " event=" + firstSourceCount + " traces=" + traceIds.Count + " scribe=" + scribeDetail +
                    " deterrent=" + deterrentDetail + " source=" + firstEventSource;
                return result;
            }
            catch (Exception exception)
            {
                result.detail = exception.GetType().Name + ": " + exception.Message;
                return result;
            }
            finally
            {
                for (int i = created.Count - 1; i >= 0; i--)
                    if (created[i]?.Spawned == true) created[i].Destroy(DestroyMode.Vanish);
            }
        }

        private static void MoveTestPawn(Map map, Pawn pawn, IntVec3 origin, float distance, HashSet<IntVec3> used)
        {
            if (pawn?.Spawned != true) return;
            pawn.DeSpawn(DestroyMode.Vanish);
            if (!TryTestCell(map, origin, distance, 999f, used, out IntVec3 cell))
                throw new InvalidOperationException("deterministic relocation cell unavailable");
            used.Add(cell);
            GenSpawn.Spawn(pawn, cell, map, Rot4.North);
        }

        private static void RespawnTestPawn(Map map, Pawn pawn, IntVec3 origin, HashSet<IntVec3> used)
        {
            if (pawn?.Spawned == true) return;
            if (!TryTestCell(map, origin, 1f, 8f, used, out IntVec3 cell))
                throw new InvalidOperationException("deterministic respawn cell unavailable");
            used.Add(cell);
            GenSpawn.Spawn(pawn, cell, map, Rot4.North);
        }

        [DebugAction("Wildlife", "Run full in-game test suite", actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void Run()
        {
            Run(false);
        }

        public static bool Run(bool quiet)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            List<Result> results = new List<Result>();
            Map map = Find.CurrentMap;
            void Check(string section, bool condition, string text) =>
                results.Add(new Result { section = section, severity = condition ? "PASS" : "FAIL", text = text });
            void Warn(string section, bool condition, string text)
            {
                if (!condition) results.Add(new Result { section = section, severity = "WARN", text = text });
                else results.Add(new Result { section = section, severity = "PASS", text = text });
            }
            void Section(string name, Action test)
            {
                try { test(); }
                catch (Exception exception)
                {
                    results.Add(new Result
                    {
                        section = name,
                        severity = "FAIL",
                        text = "Unhandled " + exception.GetType().Name + ": " + exception.GetBaseException().Message
                    });
                }
            }

            Section("Core", () =>
            {
                Check("Core", map != null, "Current map exists");
                Check("Core", Current.Game != null, "Current game exists");
                Check("Core", HerdsMod.Settings != null, "Wildlife settings loaded");
                Check("Core", AccessTools.TypeByName("Packs.PackMapComponent") != null, "Predator assembly loaded");
                Check("Core", AccessTools.TypeByName("Packs.ITab_Pack") != null, "Predator Wildlife tab loaded");
                Check("Core", typeof(WorldDrawLayer_WildlifeKnowledgeFog) != null, "World knowledge layer loaded");
                Check("Core", WildlifeDevBridge.ProtocolSelfTest(), "Activate Bridge protocol");
                Check("Core", typeof(WildlifeUnifiedOverlayMapComponent) != null &&
                    typeof(WildlifeDevMaster).GetMethod("DebugToggleUnifiedOverlay") != null,
                    "Unified dev overlay and global toggle are registered");
            });

            Section("Defs", () =>
            {
                Check("Defs", HerdsDefOf.Herds_WildlifeSign != null, "Wildlife sign def");
                Check("Defs", HerdsDefOf.Herds_StudyWildlifeSign != null, "Study Wildlife job");
                Check("Defs", HerdsDefOf.Herds_StudyLandscapeFeature?.driverClass ==
                    typeof(JobDriver_StudyLandscapeFeature), "Study landscape feature job");
                Check("Defs", HerdsDefOf.Herds_LandscapeCrossroad?.thingClass ==
                    typeof(WildlifeLandscapeCrossroad) &&
                    HerdsDefOf.Herds_ObserveLandscapeCrossroad?.driverClass ==
                    typeof(JobDriver_ObserveLandscapeCrossroad) &&
                    HerdsDefOf.Herds_StewardLandscapeCrossroad?.driverClass ==
                    typeof(JobDriver_StewardLandscapeCrossroad),
                    "Wildlife Crossroad marker and interaction jobs");
                Check("Defs", HerdsDefOf.Herds_LogSignalAlarm != null &&
                    HerdsDefOf.Herds_LogSignalHumanDanger != null &&
                    HerdsDefOf.Herds_LogSignalAllClear != null &&
                    HerdsDefOf.Herds_LogSignalContact != null &&
                    HerdsDefOf.Herds_LogSignalFood != null &&
                    HerdsDefOf.Herds_LogSignalWater != null &&
                    HerdsDefOf.Herds_LogSignalCoordination != null,
                    "Animal-call Log rule packs");
                Check("Defs", HerdsDefOf.Herds_GameTrail?.thingClass ==
                    typeof(WildlifeLandscapeFeature) &&
                    HerdsDefOf.Herds_GrazingGround != null &&
                    HerdsDefOf.Herds_ScentPost != null &&
                    HerdsDefOf.Herds_FeedingRemains != null,
                    "Landscape feature definitions");
                Check("Defs", HerdsDefOf.Herds_StudyNotableAnimal != null, "Study Notable Animal job");
                Check("Defs", HerdsDefOf.Herds_ObserveWildlifeMoment?.driverClass ==
                    typeof(JobDriver_ObserveWildlifeMoment), "Observe Wildlife Moment job");
                Check("Defs", HerdsDefOf.Herds_PerformStewardshipProject?.driverClass ==
                    typeof(JobDriver_PerformStewardshipProject),
                    "Stewardship projects use colonist fieldwork");
                Check("Defs", HerdsDefOf.Herds_WildlifeStory?.letterClass ==
                    typeof(ChoiceLetter_WildlifeStory),
                    "Colony Story notification uses Folklore routing letter");
                Check("Defs", HerdsDefOf.Herds_EmbarkHuntingExpedition != null, "Wildlife embark job");
                List<ExpeditionEventDef> expeditionEvents =
                    DefDatabase<ExpeditionEventDef>.AllDefsListForReading;
                Check("Defs", expeditionEvents.Count >= 3 && expeditionEvents.All(eventDef =>
                        eventDef.chance > 0f && !eventDef.choices.NullOrEmpty() &&
                        eventDef.choices.Any(choice => choice.turnBack) &&
                        eventDef.choices.Any(choice => choice.label == "Press On") &&
                        eventDef.choices.Any(choice => !choice.turnBack && choice.label != "Press On")),
                    "Expandable expedition events provide Turn Back, Press On, and event-specific choices");
                Check("Defs", HerdsDefOf.Herds_HuntingExpeditionMarker != null, "Wildlife expedition marker");
                Check("Defs", HerdsDefOf.Herds_HuntingSpot != null, "Hunting Spot");
                Check("Defs", HerdsDefOf.Herds_ObservationPost != null, "Observation Post");
                Check("Defs", HerdsDefOf.Herds_AnimalBurrow != null, "Animal burrow");
                Check("Defs", HerdsDefOf.Herds_CameraTrap != null, "Camera trap");
                Check("Defs", HerdsDefOf.Herds_TelemetryStation != null, "Telemetry station");
                Check("Defs", HerdsDefOf.Herds_FlightBurst != null, "Bird flight burst");
                Check("Defs", HerdsDefOf.Herds_WildlifeTrophy != null, "Wildlife trophy reward");
                Check("Defs", HerdsDefOf.Herds_FolkloreCairn != null &&
                    HerdsDefOf.Herds_FolkloreCairn.thingClass == typeof(Building_FolkloreCairn),
                    "Wildlife folklore cairn");
                Check("Defs", HerdsDefOf.Herds_RetellWildlifeStory?.driverClass == typeof(JobDriver_RetellWildlifeStory) &&
                    HerdsDefOf.Herds_WildlifeCeremonyGather?.driverClass == typeof(JobDriver_WildlifeCeremonyGather),
                    "Physical storytelling and ceremony jobs");
                Check("Defs", HerdsDefOf.Herds_WildlifeInsight != null &&
                    HerdsDefOf.Herds_WildlifeAttuned != null, "Wildlife inspiration and trait");
                Check("Defs", HerdsDefOf.Herds_ProtectedAnimalDied != null,
                    "Protected-animal death thought");
                Check("Defs", DefDatabase<HediffDef>.GetNamedSilentFail("Herds_NotableSwift") != null &&
                    DefDatabase<HediffDef>.GetNamedSilentFail("Herds_NotableCunning") != null &&
                    DefDatabase<HediffDef>.GetNamedSilentFail("Herds_NotableScarred") != null,
                    "Notable animal abilities");
                ResearchProjectDef expedition = DefDatabase<ResearchProjectDef>.GetNamedSilentFail("Wildlife_HuntingExpedition");
                Check("Defs", expedition != null && expedition.label == "wildlife expedition", "Wildlife Expedition research label");
                Check("Defs", expedition?.prerequisites?.Any(def => def.defName == "Wildlife_Fieldcraft") == true,
                    "Wildlife Expedition requires Organized Hunting");
            });

            if (map != null)
            {
                Section("Components", () =>
                {
                    Check("Components", map.GetComponent<HerdMapComponent>() != null, "Prey simulation component");
                    Check("Components", map.GetComponent<WildlifeFieldcraftMapComponent>() != null, "Fieldcraft component");
                    Check("Components", map.GetComponent<WildlifeSignalCultureMapComponent>() != null,
                        "Wildlife signal culture component");
                    Check("Components", map.GetComponent<HuntingKnowledgeMapComponent>() != null, "Animal Knowledge component");
                    Check("Components", map.GetComponent<WildlifeHuntCoordinator>() != null, "Hunt coordinator");
                    Check("Components", map.GetComponent<RegionalWildlifeMapComponent>() != null, "Regional wildlife component");
                    Check("Components", map.GetComponent<WildlifeLandscapeMapComponent>() != null,
                        "Landscape component");
                    Check("Components", map.GetComponent<HuntingExpeditionMapComponent>() != null, "Wildlife expedition component");
                    Check("Components", map.GetComponent<NotableWildlifeMapComponent>() != null, "Notable wildlife component");
                    Check("Components", map.GetComponent<WildlifeFieldJournalMapComponent>() != null, "Wildlife Field Journal component");
                    Check("Components", map.GetComponent<WildlifeUnifiedOverlayMapComponent>() != null,
                        "Unified Wildlife overlay component");
                    Check("Components", map.components.Any(component => component.GetType().FullName == "Packs.PackMapComponent"),
                        "Predator simulation component");
                });

                Section("Deferred Reality", () =>
                {
                    bool adapterLoaded = AccessTools.TypeByName("DeferredReality.Wildlife.WildlifeRealityProvider") != null;
                    Assembly FindLoaded(string name) => AppDomain.CurrentDomain.GetAssemblies()
                        .FirstOrDefault(assembly => assembly?.GetName().Name == name);
                    bool HasReference(Assembly assembly, string name) => assembly != null &&
                        assembly.GetReferencedAssemblies().Any(reference => reference.Name == name);
                    Assembly herdsAssembly = typeof(WildlifeInGameTestSuite).Assembly;
                    Assembly wildlifeAssembly = FindLoaded("Wildlife");
                    Check("Deferred Reality", !HasReference(herdsAssembly, "DeferredRealityFramework"),
                        "Herds remains usable without a Deferred Reality assembly reference");
                    Check("Deferred Reality", !HasReference(wildlifeAssembly, "DeferredRealityFramework"),
                        "Normal Wildlife remains usable without a Deferred Reality assembly reference");
                    Check("Deferred Reality", !adapterLoaded || WildlifeDeferredRealityBridge.MaterializeBeyondMap != null,
                        "Adjacent trail bridge is installed when the Deferred Reality Wildlife adapter is loaded");
                    Check("Deferred Reality", !adapterLoaded ||
                        typeof(WildlifeTrailMapComponent).GetMethod("NotifyAnimalDeparture") != null,
                        "Trail records expose the adjacent-departure handoff");
                    if (adapterLoaded)
                    {
                        Assembly adapterAssembly = AccessTools.TypeByName("DeferredReality.Wildlife.WildlifeRealityProvider")?.Assembly;
                        Check("Deferred Reality", HasReference(adapterAssembly, "DeferredRealityFramework"),
                            "Only the optional adapter references Deferred Reality");
                        Type providerType = AccessTools.TypeByName("DeferredReality.Wildlife.WildlifeRealityProvider");
                        Check("Deferred Reality", providerType.GetMethod("TryObserveExcursionTask") != null &&
                            providerType.GetMethod("HeartbeatExcursionTask") != null &&
                            providerType.GetMethod("CompleteExcursionTask") != null &&
                            providerType.GetMethod("AbandonExcursionTask") != null,
                            "Wildlife task observation and explicit lease hooks are available");
                        Type policyType = AccessTools.TypeByName("DeferredReality.Wildlife.DeferredRealityWildlifePolicy");
                        bool policySelfTest = false;
                        try
                        {
                            MethodInfo selfTest = policyType?.GetMethod("SelfTest",
                                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                            object[] arguments = { null };
                            policySelfTest = selfTest != null && (bool)selfTest.Invoke(null, arguments);
                        }
                        catch
                        {
                            policySelfTest = false;
                        }
                        Check("Deferred Reality", policySelfTest,
                            "Optional adapter departure and post-load evidence policy self-test");
                    }
                });

                Section("Landscape", () =>
                {
                    Check("Landscape", WildlifeNicheDatabase.ConservativeRulesSelfTest(),
                        "Ecological roles reject humans and contain no duplicates");
                    Check("Landscape", map.GetComponent<WildlifeLandscapeMapComponent>()
                        .Features.Count() <= 14,
                        "Persistent ecological feature cap");
                    Check("Landscape", map.mapPawns.AllPawnsSpawned
                        .Where(pawn => pawn.Faction == null &&
                            pawn.RaceProps?.Animal == true)
                        .All(pawn => WildlifeNicheDatabase.RolesFor(pawn.def)
                            .All(role => Enum.IsDefined(typeof(WildlifeEcologicalRole), role))),
                        "Local species resolve only valid ecological roles");
                    WildlifeLandscapeMapComponent landscape =
                        map.GetComponent<WildlifeLandscapeMapComponent>();
                    Check("Landscape", landscape.Features
                            .Where(feature => feature.kind == WildlifeLandscapeKind.FeedingRemains)
                            .All(feature => feature.strength > 0f &&
                                WildlifeLandscapeUtility.Effect(feature.kind).Contains("feeding site")) &&
                        typeof(WildlifeLandscapeFeature).GetMethod("TickRare") != null &&
                        typeof(WildlifeLandscapeMapComponent).GetMethod("MigrationAttraction") != null &&
                        typeof(WildlifeLandscapeMapComponent).GetMethod("PreferredFeatureTarget") != null,
                        "Feeding remains attract scavengers and expose gradual consumption lifecycle behavior");
                    Check("Landscape", landscape.Activities.All(activity => activity.id > 0) &&
                        landscape.Activities.Select(activity => activity.id).Distinct().Count() ==
                        landscape.Activities.Count,
                        "Wildlife Crossroad activity IDs are valid and unique");
                    Check("Landscape", landscape.Crossroads.All(marker =>
                        landscape.ActivityById(marker.activityId) != null),
                        "Wildlife Crossroad markers reference live activities");
                    Check("Landscape",
                        WildlifeLandscapeMapComponent.ObstructionEffectiveness(0) == 1f &&
                        WildlifeLandscapeMapComponent.ObstructionEffectiveness(1) <= 0.6f &&
                        WildlifeLandscapeMapComponent.ObstructionEffectiveness(3) <= 0.15f,
                        "Colony construction sharply reduces Landscape effectiveness");
                    Check("Landscape",
                        WildlifeLandscapeMapComponent.GrazingGrowthBonus(0f) == 0f &&
                        WildlifeLandscapeMapComponent.GrazingGrowthBonus(0.5f) > 0f &&
                        WildlifeLandscapeMapComponent.GrazingGrowthBonus(1f) >
                            WildlifeLandscapeMapComponent.GrazingGrowthBonus(0.5f) &&
                        GrazingGroundGrowthPatch.ApplyGrowthBonus(1f, true, 0.5f) > 1f &&
                        GrazingGroundGrowthPatch.ApplyGrowthBonus(1f, true, 0f) == 1f &&
                        GrazingGroundGrowthPatch.ApplyGrowthBonus(1f, false, 0.5f) == 1f &&
                        GrazingGroundGrowthPatch.IsGrass(ThingDefOf.Plant_Grass) &&
                        GrazingGroundGrowthPatch.ShouldQueryGrowthBonus(true, 1f, ThingDefOf.Plant_Grass) &&
                        !GrazingGroundGrowthPatch.ShouldQueryGrowthBonus(true, 1f,
                            DefDatabase<ThingDef>.GetNamed("Plant_Rice")) &&
                        !GrazingGroundGrowthPatch.ShouldQueryGrowthBonus(false, 1f, ThingDefOf.Plant_Grass) &&
                        !GrazingGroundGrowthPatch.ShouldQueryGrowthBonus(true, 0f, ThingDefOf.Plant_Grass),
                        "Grazing Grounds scale grass growth with effectiveness and remove inactive bonuses");
                    Check("Landscape",
                        WildlifeFieldJournalMapComponent.ProjectLabel(
                            WildlifeStewardProjectKind.RanchDefense) ==
                            "Protect Wildlife Habitat" &&
                        WildlifeFieldJournalMapComponent.ProjectDescription(
                            WildlifeStewardProjectKind.RanchDefense).Contains("habitat") &&
                        WildlifeFieldJournalMapComponent.RestoreSpeciesEligible(
                            new RegionalSpeciesRecord
                            {
                                population = 74f,
                                previousPopulation = 100f
                            }) &&
                        !WildlifeFieldJournalMapComponent.RestoreSpeciesEligible(
                            new RegionalSpeciesRecord
                            {
                                population = 80f,
                                previousPopulation = 100f
                            }),
                        "Stewardship labels describe habitat protection and restoration requires significant decline");
                });

                Section("Prey", () =>
                {
                    HerdMapComponent herds = map.GetComponent<HerdMapComponent>();
                    List<Pawn> prey = map.mapPawns.AllPawnsSpawned
                        .Where(pawn => pawn?.Dead == false && PreyProfileDatabase.IsEligible(pawn.def)).ToList();
                    int missingGroups = 0;
                    int badProfiles = 0;
                    int badSolitary = 0;
                    int badBirdDefaults = 0;
                    for (int i = 0; i < prey.Count; i++)
                    {
                        PreyProfile profile = PreyProfileDatabase.For(prey[i].def);
                        HerdSnapshot group = herds?.HerdFor(prey[i]);
                        if (profile == null) badProfiles++;
                        if (group == null) missingGroups++;
                        if (profile?.socialType == PreySocialType.Solitary && group?.members.Count > 1) badSolitary++;
                        if (PreyProfileDatabase.IsBird(prey[i].def) &&
                            PreyProfileDatabase.DefaultFor(prey[i].def)?.socialType != PreySocialType.Flock) badBirdDefaults++;
                    }
                    Check("Prey", badProfiles == 0, "All prey have behavior profiles");
                    Warn("Prey", missingGroups == 0, missingGroups + " prey currently lack a simulation group");
                    Check("Prey", badSolitary == 0, "Solitary prey are not grouped");
                    Check("Prey", badBirdDefaults == 0, "Bird species default to flock behavior");
                });

                Section("Predators", () =>
                {
                    MapComponent packs = map.components.FirstOrDefault(component => component.GetType().FullName == "Packs.PackMapComponent");
                    MethodInfo overview = AccessTools.Method(packs?.GetType(), "DebugOverviewLines");
                    List<string> lines = overview?.Invoke(packs, null) as List<string>;
                    Check("Predators", packs != null, "Pack component present");
                    Check("Predators", lines != null && lines.Count > 0, "Predator state API responds");
                    List<Pawn> wildPredators = map.mapPawns.AllPawnsSpawned
                        .Where(pawn => pawn?.Dead == false &&
                            WildlifeSpeciesClassification.IsPredator(pawn.def) &&
                            pawn.Faction != Faction.OfPlayer).ToList();
                    Warn("Predators", wildPredators.Count > 0, "No wild predators available for live behavior checks");
                });

                Section("Fieldcraft", () =>
                {
                    List<WildlifeSign> signs = map.listerThings.ThingsOfDef(HerdsDefOf.Herds_WildlifeSign)
                        .OfType<WildlifeSign>().ToList();
                    Check("Fieldcraft", signs.All(sign => sign.species != null), "All wildlife signs identify a species internally");
                    Check("Fieldcraft", signs.All(sign => sign.sourceAnimal == null ||
                        sign.sourceAnimal.def == sign.species),
                        "Wildlife signs retain a valid originating animal when available");
                    Check("Fieldcraft", signs.All(sign => sign.studiedBy != null), "All wildlife signs have study records");
                    WildlifeTrailMapComponent trails = map.GetComponent<WildlifeTrailMapComponent>();
                    Check("Fieldcraft", trails != null, "Interactive trail-reading component");
                    Check("Fieldcraft", HerdsDefOf.Herds_FollowWildlifeTrail != null,
                        "Follow Wildlife Trail job");
                    Check("Fieldcraft",
                        typeof(WildlifeSign).GetMethod("ShowStudyMenu") != null,
                        "Selected wildlife signs expose an explicit colonist study menu");
                    Check("Fieldcraft", trails?.TrailLeads != null,
                        "Trail records are available for bridge and UI assessment");
                    List<WildlifeSign> availableLeadSigns = trails?.AvailableLeadSigns() ??
                        new List<WildlifeSign>();
                    int urgentLeadCount = WildlifeTrailMapComponent.CountUrgentLeads(
                        availableLeadSigns);
                    Check("Fieldcraft", trails?.UrgentLeadCount == urgentLeadCount &&
                        WildlifeTrailMapComponent.CountUrgentLeads(
                            availableLeadSigns.Concat(availableLeadSigns)) == urgentLeadCount &&
                        urgentLeadCount <= availableLeadSigns.Select(sign =>
                            sign.sourceAnimal).Distinct().Count(),
                        "Wildlife tracking counts grouped Trail Leads rather than individual clues");
                    Check("Fieldcraft", JobDriver_StudyNotableAnimal.MinimumStudyDistance >= 18f &&
                        JobDriver_StudyNotableAnimal.MaximumStudyDistance >
                            JobDriver_StudyNotableAnimal.MinimumStudyDistance &&
                        typeof(JobDriver_StudyNotableAnimal).GetMethod("TryFindStudyCell") != null,
                        "Notable animal study uses a safe line-of-sight observation range");
                    Check("Fieldcraft", NotableAnimalActionPolicy.Order.SequenceEqual(new[]
                        { "Study", "Hunt", "Protect", "Capture" }),
                        "Notable animal actions use the requested study, hunt, protect, capture order");
                    Check("Fieldcraft", HuntingExpeditionMapComponent.TrailHuntBonus(
                        new TrailHuntOpportunity { quality = 0f }) > 0f &&
                        HuntingExpeditionMapComponent.TrailHuntBonus(
                            new TrailHuntOpportunity { quality = 1f }) >
                        HuntingExpeditionMapComponent.TrailHuntBonus(
                            new TrailHuntOpportunity { quality = 0f }),
                        "Trail hunt opportunities provide progressive expedition advantages");
                    Check("Fieldcraft", trails.TrailLeads.All(lead => lead?.species != null &&
                        lead.targetAnimal != null && !lead.targetAnimal.Spawned &&
                        Enum.IsDefined(typeof(WildlifeTrailState), lead.state) &&
                        lead.targetAnimal.def == lead.species &&
                        lead.state == WildlifeTrailState.BeyondMap && lead.predictedCell.IsValid),
                        "Every trail represents one exact animal that has already left the map");
                    Check("Fieldcraft", trails.TrailLeads.Where(lead => lead?.targetAnimal != null)
                        .All(lead => trails.LeadFor(lead.targetAnimal) == lead),
                        "Trail lookup remains bound to the exact departed animal");
                    HuntingExpeditionMapComponent trailExpeditions =
                        map.GetComponent<HuntingExpeditionMapComponent>();
                    Check("Fieldcraft", trailExpeditions.TrailHuntOpportunities.All(opportunity =>
                            opportunity?.species != null && opportunity.targetAnimal != null &&
                            opportunity.targetAnimal.def == opportunity.species) &&
                        trailExpeditions.ActiveExpeditions.All(record =>
                            record.trailTargetAnimal == null ||
                            record.trailTargetAnimal.def == record.targetSpecies),
                        "Temporary trail opportunities and expeditions preserve exact quarry identity");
                    Check("Fieldcraft",
                        typeof(WildlifeTrailMapComponent).GetMethod("Retains") != null &&
                        typeof(WildlifeTrailMapComponent).GetMethod("NotifyAnimalDeparture") != null &&
                        typeof(WildlifeTrailMapComponent).GetMethod("SafeFollowDestination") != null,
                        "Active trails can retain quarry and survive map departure");
                    Check("Fieldcraft",
                        typeof(WildlifeFieldcraftMapComponent).GetMethod(
                            "CanSafelyTrack") != null &&
                        typeof(WildlifeFieldcraftMapComponent).GetMethod(
                            "CreateSafeTrackingSign") != null,
                        "Wildlife Moment tracking uses safely separated physical evidence");
                    Check("Fieldcraft", typeof(Window_WildlifeTrailBoard) != null,
                        "Player-facing Trail Leads board");
                    Check("Fieldcraft", WildlifeTrailMapComponent.NaturalPaletteSelfTest(),
                        "Trail overlays use distinct, restrained natural colors");
                    Check("Fieldcraft", map.GetComponent<WildlifeHuntCoordinator>().DebugOverviewLines() != null,
                        "Coordinated hunt state API responds");
                });

                Section("Signals", () =>
                {
                    WildlifeSignalCultureMapComponent signals =
                        map.GetComponent<WildlifeSignalCultureMapComponent>();
                    List<ThingDef> species = map.mapPawns.AllPawnsSpawned
                        .Where(pawn => pawn?.RaceProps?.Animal == true)
                        .Select(pawn => pawn.def).Distinct().ToList();
                    Check("Signals", signals != null, "Local signal culture state API responds");
                    Check("Signals", signals != null && species.All(def =>
                    {
                        WildlifeDialectRecord dialect = signals.DialectFor(def);
                        return dialect != null && dialect.credibility >= 0f &&
                            dialect.credibility <= 1f && dialect.humanTrust >= 0f &&
                            dialect.humanTrust <= 1f && !signals.DialectName(def).NullOrEmpty();
                    }), "Animal dialect identities and trust values are valid");
                    Check("Signals", signals != null && map.mapPawns.FreeColonists.All(pawn =>
                        species.All(def =>
                        {
                            float value = signals.Understanding(pawn, def);
                            return value >= 0f && value <= 1f;
                        })), "Per-colonist signal understanding is bounded");
                    Check("Signals", signals != null && species.All(def =>
                    {
                        Pawn contributor = signals.ColonyContributor(def);
                        float displayed = signals.ColonyUnderstanding(def);
                        float expected = map.mapPawns.FreeColonistsSpawned
                            .Select(pawn => signals.Understanding(pawn, def))
                            .DefaultIfEmpty(0f).Max();
                        return Math.Abs(displayed - expected) < 0.0001f &&
                            (contributor == null
                                ? Math.Abs(displayed) < 0.0001f
                                : Math.Abs(signals.Understanding(contributor, def) - displayed) < 0.0001f);
                    }), "Colony signal knowledge names the currently contributing colonist");
                    Check("Signals", signals != null && signals.ActiveSignals.All(signal =>
                        signal.species?.race?.Animal == true && signal.radius >= 0f &&
                        signal.expiresTick >= signal.startedTick),
                        "Active signal visuals have valid state");
                    Check("Signals", signals != null && signals.RecentSignals.All(trace =>
                        trace.species?.race?.Animal == true && trace.traceId > 0 &&
                        trace.radius >= 0f && !trace.cause.NullOrEmpty() &&
                        !trace.expectedBehavior.NullOrEmpty() &&
                        (!trace.verified || !trace.observedBehavior.NullOrEmpty())),
                        "Signal history records cause, intent, and verified behavior");
                    Check("Signals", WildlifeSignalCultureMapComponent.VisualGrammarSelfTest(),
                        "Every signal kind has a distinct visual identity and player label");
                    Check("Signals", WildlifeSignalCultureMapComponent.ResponseSafetySelfTest(),
                        "Solitary or ungrouped signalers are safe during response verification");
                    Check("Signals", WildlifeSignalCultureMapComponent.IdentifiedSignalTextSelfTest(),
                        "Signal meaning labels appear only after exact identification");
                    Check("Signals", WildlifeKnowledgeAdapter.WarningKnowledgeSelfTest(),
                        "Warning calls progress from first evidence through family, meaning, support, and contradiction states");
                    Check("Signals", WildlifeKnowledgeAdapter.LegacyWarningState(0f, 1).hasEvidence &&
                        !WildlifeKnowledgeAdapter.LegacyWarningState(0f, 1).familyRecognized &&
                        WildlifeKnowledgeAdapter.LegacyWarningState(0.3f, 1).familyRecognized &&
                        !WildlifeKnowledgeAdapter.LegacyWarningState(0.3f, 1).meaningInterpreted,
                        "Legacy warning knowledge remains qualitative without inventing a V3 meaning claim");
                    Check("Signals", WildlifeKnowledgeAdapter.PredatorPressureKnowledgeSelfTest(),
                        "Local predator encounters progress from a herd consequence through pattern, meaning, support, and contradiction states");
                    Check("Signals", typeof(WildlifeKnowledgeAdapter).GetMethod("ObserveWarningCall") != null &&
                        typeof(WildlifeKnowledgeAdapter).GetMethod("WarningObservationAlreadyApplied") != null &&
                        typeof(WildlifeKnowledgeAdapter).GetMethod("WarningSourceInstanceId") != null,
                        "Warning knowledge uses a stable V3 observation identity and explicit duplicate guard");
                    Check("Signals", typeof(WildlifeKnowledgeAdapter).GetMethod("ObservePredatorPressure") != null &&
                        typeof(WildlifeKnowledgeAdapter).GetMethod("PredatorPressureObservationAlreadyApplied") != null &&
                        typeof(WildlifeKnowledgeAdapter).GetMethod("PredatorPressureSourceInstanceId") != null &&
                        typeof(WildlifeKnowledgeAdapter).GetMethod("PredatorPressureEventSourceInstanceId") != null &&
                        typeof(WildlifeKnowledgeAdapter).GetMethod("IsPredatorPressureTrace") != null,
                        "Local predator encounters use a stable colony event identity and duplicate guard");
                    Check("Signals", typeof(WildlifeSignalObservationPresentation).GetField("warningKnowledgeSubmitted") != null &&
                        typeof(WildlifeSignalCultureMapComponent).GetProperty("WarningKnowledgeSources") != null,
                        "Warning processing markers are retained on existing signal presentation owners");
                    Check("Signals", typeof(WildlifeSignalObservationPresentation).GetField("predatorPressureSubmitted") != null &&
                        typeof(WildlifeSignalObservationPresentation).GetField("predatorPressureSourceInstanceId") != null &&
                        typeof(WildlifeSignalCultureMapComponent).GetMethod("ColonyPredatorPressure") != null,
                        "Predator-encounter markers and qualitative colony projections remain on existing signal owners");
                    Check("Signals", typeof(WildlifeSignalTrace).GetField("developerScenario") != null &&
                        !WildlifeKnowledgeAdapter.IsPredatorPressureTrace(new WildlifeSignalTrace
                        {
                            kind = WildlifeSignalKind.Alarm,
                            hasSubject = true,
                            developerScenario = true
                        }),
                        "Developer scenarios cannot become ecological pressure evidence");
                    Check("Signals", typeof(WildlifeSignalTrace).GetField("subjectWasPredator",
                        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public) != null &&
                        WildlifeKnowledgeAdapter.IsPredatorPressureTrace(new WildlifeSignalTrace
                        {
                            kind = WildlifeSignalKind.Alarm,
                            hasSubject = true,
                            subjectWasPredator = true
                        }) &&
                        !WildlifeKnowledgeAdapter.IsPredatorPressureTrace(new WildlifeSignalTrace
                        {
                            kind = WildlifeSignalKind.Alarm,
                            hasSubject = true,
                            subjectWasPredator = false
                        }),
                        "Only production-classified predator subjects enter local encounter evidence");
                    Check("Signals", WildlifeKnowledgeAdapter.PredatorPressureEventSourceInstanceId(map, 17) ==
                        "wildlife:predator-encounter:" + map.uniqueID + ":17",
                        "One predator encounter trace has one colony-level identity independent of observers");
                    Check("Signals", RegionalWildlifeMapComponent.PredatorDeterrentEffectSelfTest(),
                        "Predator Deterrents reduce predator return and migration attraction in the established calculations");
                    DeterministicPredatorPressureResult predatorFixture = DeterministicPredatorPressureCheck(map);
                    Log.Message("[WildlifeTest][PredatorFixture] " + predatorFixture.detail);
                    Check("Signals", predatorFixture.noQualifyingThreat,
                        "No qualifying threat produces no predator-encounter evidence: " + predatorFixture.detail);
                    Check("Signals", predatorFixture.genuineDefense && predatorFixture.productionClassification,
                        "A genuine predator drives UpdateDefense, production classification, Alarm, and response verification: " + predatorFixture.detail);
                    Check("Signals", predatorFixture.sharedPresentations && predatorFixture.oneColonyEvent,
                        "Two eligible observers receive one shared colony encounter event: " + predatorFixture.detail);
                    Check("Signals", predatorFixture.firstEvidenceAmbiguous && predatorFixture.localPredictionBounded,
                        "First evidence is ambiguous and later local encounters progress to a bounded prediction: " + predatorFixture.detail);
                    Check("Signals", predatorFixture.reprocessingIdempotent && predatorFixture.laterEncounterReinforced,
                        "Repeated processing is idempotent and later distinct encounters reinforce knowledge: " + predatorFixture.detail);
                    Check("Signals", predatorFixture.hostileNonPredatorExcluded && predatorFixture.unobservedExcluded &&
                        predatorFixture.ineligibleObserverExcluded,
                        "Hostile non-predators and unobserved or ineligible listeners cannot create predator evidence: " + predatorFixture.detail);
                    Check("Signals", predatorFixture.fabricatedExcluded && predatorFixture.simulatedPredatorEligible,
                        "Fabricated debug signals are excluded while naturally simulated predator actors remain eligible: " + predatorFixture.detail);
                    Check("Signals", predatorFixture.cleared && predatorFixture.allClearExcluded,
                        "Threat clearing emits AllClear without creating warning or predator-encounter evidence: " + predatorFixture.detail);
                    Check("Signals", predatorFixture.normalizationIdempotent && predatorFixture.uiNonMutating &&
                        predatorFixture.warningPathPreserved,
                        "Save normalization, Journal/detail construction, and warning compatibility remain non-mutating: " + predatorFixture.detail);
                    Check("Signals", predatorFixture.scribeRoundTrip,
                        "Signal traces and Knowledge Framework claims round-trip through Scribe without replay: " + predatorFixture.detail);
                    Check("Signals", predatorFixture.deterrentRoute,
                        "Supported local encounters expose Predator Deterrent construction or a qualitative unavailable reason: " + predatorFixture.detail);
                    Check("Signals", predatorFixture.deterrentEffect,
                        "Predator Deterrent changes real cached regional calculations without touching the active map: " + predatorFixture.detail);
                    Check("Signals", predatorFixture.isolatedCleanup && predatorFixture.activeStateUntouched,
                        "Disposable fixture cleanup leaves the active game, maps, pawns, buildings, and claims untouched: " + predatorFixture.detail);
                    Check("Signals", signals != null && signals.WarningKnowledgeSources.Distinct().Count() ==
                        signals.WarningKnowledgeSources.Count,
                        "Warning source identity ledger remains duplicate-free after load normalization");
                    Check("Signals", signals != null && signals.RecentSignals.Where(trace =>
                        WildlifeSignalCultureMapComponent.IsWarningCall(trace.kind)).All(trace =>
                        trace.playerFacingDescription.NullOrEmpty() ||
                        !trace.playerFacingDescription.Contains("human-danger")),
                        "Warning projections do not expose hidden call identity in normal descriptions");
                    Check("Signals", signals != null && signals.RecentSignals.Where(trace =>
                        WildlifeKnowledgeAdapter.IsPredatorPressureTrace(trace)).All(trace =>
                        trace.playerFacingDescription.NullOrEmpty() ||
                        !trace.playerFacingDescription.Contains("predator")),
                        "Predator-encounter evidence remains ambiguous before its claim is supported");
                    Check("Signals", WildlifeSignalPresentation.SelfTest(),
                        "Signal descriptions use threshold-safe grammar and animal references");
                    Check("Signals", signals != null && signals.RecentSignals.All(trace => trace.playerFacingTier >= 0 &&
                        trace.playerFacingTier <= (int)WildlifeSignalDisplayTier.Truthfulness &&
                        !trace.playerFacingDescription.NullOrEmpty()),
                        "Signal history preserves a bounded historical player-facing description");
                    Check("Signals", typeof(WildlifeSignalJournalPanel) != null &&
                        typeof(WildlifeSignalCultureMapComponent).GetMethod("Replay") != null &&
                        typeof(WildlifeSignalCultureMapComponent).GetMethod("TraceLines") != null,
                        "Journal Signals supports replay and compact bridge traces");
                    Check("Signals", typeof(WildlifeSignalAudio).GetMethod("Replay") != null,
                        "Recorded signal replay is audio-only");
                    Check("Signals", WildlifeSignalAudio.SelfTest(),
                        "Signal vocalization pitch is deterministic, subtle, and bounded");
                    Check("Signals", signals == null || signals.RecentSignals.All(trace =>
                        trace.soundPitch >= 0.96f && trace.soundPitch <= 1.04f &&
                        (trace.soundDef == null || !trace.soundStatus.NullOrEmpty())),
                        "Signal audio identity and bounded pitch are persisted safely");
                    Check("Signals", typeof(HerdsSettings).GetField("enableWildlifeSignalCulture") != null &&
                        typeof(HerdsSettings).GetField("showIdentifiedSignalText") != null &&
                        typeof(HerdsSettings).GetField("enablePlayerSignalImitation") != null &&
                        typeof(HerdsSettings).GetField("enablePredatorSignalLearning") != null,
                        "Signal culture features have individual configuration switches");
                });

                Section("Expeditions", () =>
                {
                    HuntingExpeditionMapComponent expeditions = map.GetComponent<HuntingExpeditionMapComponent>();
                    List<string> validations = expeditions.DebugValidationLines();
                    Check("Expeditions", validations.All(line => !line.StartsWith("FAIL")), "Built-in expedition validation");
                    ExpeditionDestination near = expeditions.Destinations().FirstOrDefault();
                    Check("Expeditions", near != null, "At least one valid nearby destination");
                    ExpeditionDestination far = null;
                    PlanetLayer layer = map.Tile.Layer;
                    for (int i = 0; i < layer.TilesCount && far == null; i++)
                    {
                        PlanetTile tile = layer.PlanetTileForID(i);
                        if (Find.WorldGrid.ApproxDistanceInTiles(map.Tile, tile) > 20f && expeditions.CanExpeditionTo((int)tile))
                            far = expeditions.DestinationForTile((int)tile, false);
                    }
                    Check("Expeditions", far != null && far.distance > 20, "Distant valid world tiles are selectable");
                    PlanetTile settlementTile = Find.WorldObjects.Settlements.FirstOrDefault()?.Tile ?? PlanetTile.Invalid;
                    Check("Expeditions", !settlementTile.Valid || !expeditions.CanExpeditionTo((int)settlementTile),
                        "Settlements cannot be expedition destinations");
                    ResearchProjectDef expeditionResearch = DefDatabase<ResearchProjectDef>.GetNamedSilentFail("Wildlife_HuntingExpedition");
                    Check("Expeditions", !NatureWorldUI.Enabled ||
                        expeditionResearch?.IsFinished == true,
                        "Nature world tab requires Wildlife Expedition research");
                    Check("Expeditions", expeditions.ActiveExpeditions.SelectMany(record => record.Party)
                        .Where(pawn => pawn != null).GroupBy(pawn => pawn).All(group => group.Count() == 1),
                        "A pawn belongs to at most one active expedition");
                    Check("Expeditions", expeditions.ActiveExpeditions.SelectMany(record => record.Party)
                        .Where(pawn => pawn != null && !pawn.Dead)
                        .All(pawn => pawn.Spawned || Find.WorldPawns.Contains(pawn)),
                        "Every active expedition member is on-map or retained by WorldPawns");
                    Check("Expeditions", expeditions.ActiveExpeditions
                        .Where(record => record.stage != ExpeditionStage.Embarking)
                        .All(record => record.caravan?.Destroyed == false &&
                            record.Party.Where(pawn => pawn != null && !pawn.Dead && !pawn.Spawned)
                                .All(record.caravan.ContainsPawn)),
                        "Departed expedition members are held by a visible caravan");
                    Check("Expeditions", expeditions.ActiveExpeditions
                        .Where(record => record.caravan?.Destroyed == false)
                        .All(record => !record.caravan.GetInspectString().Contains("\n\n")),
                        "Expedition caravan inspect strings contain no empty lines");
                    Check("Expeditions", expeditions.ActiveExpeditions
                        .Where(record => record.caravan?.Destroyed == false &&
                            (record.stage == ExpeditionStage.OutboundTravel || record.stage == ExpeditionStage.Returning))
                        .All(record =>
                        {
                            PlanetTile target = record.stage == ExpeditionStage.Returning ? map.Tile : (PlanetTile)record.destinationTile;
                            return record.caravan.Tile == target ||
                                (record.caravan.pather.Moving && record.caravan.pather.Destination == target);
                        }), "Traveling expeditions use the caravan pather");
                    Check("Expeditions", expeditions.ActiveExpeditions.All(record =>
                        !record.interactiveEncounterPending || !record.interactiveEncounter.NullOrEmpty()),
                        "Pending interactive encounters have valid field reports");
                    Check("Expeditions", expeditions.ActiveExpeditions.All(record =>
                        record.foodNutrition >= 0f && record.dailyNutrition >= 0f &&
                        record.expectedReturnTick >= record.stageStartedTick),
                        "Active expedition timing and supply state survives save data");
                    Check("Expeditions", expeditions.TrailPaths.All(path => path != null &&
                            path.fromTile >= 0 && path.toTile >= 0 && path.fromTile != path.toTile &&
                            path.targetSpecies != null) &&
                        expeditions.TrailPaths.Select(path =>
                            Math.Min(path.fromTile, path.toTile) + ":" +
                            Math.Max(path.fromTile, path.toTile)).Distinct().Count() ==
                            expeditions.TrailPaths.Count,
                        "Permanent trail paths contain unique valid world-tile edges");
                    Check("Expeditions", expeditions.History.Count <= 20,
                        "Completed expedition history remains bounded");
                    if (near != null)
                    {
                        ExpeditionPlan without = new ExpeditionPlan { destination = near, objective = ExpeditionObjective.Scout, useBedrolls = false };
                        ExpeditionPlan with = new ExpeditionPlan { destination = near, objective = ExpeditionObjective.Scout, useBedrolls = true };
                        Check("Expeditions", expeditions.EstimateDays(with) < expeditions.EstimateDays(without),
                            "Bedrolls modestly reduce expedition time");
                        if (far != null)
                            Check("Expeditions", expeditions.EstimateDays(new ExpeditionPlan
                            {
                                destination = far,
                                objective = ExpeditionObjective.Scout,
                                useBedrolls = false
                            }) > expeditions.EstimateDays(without), "Distance increases expedition duration");
                    }
                });

                Section("Knowledge", () =>
                {
                    HuntingKnowledgeMapComponent knowledge = map.GetComponent<HuntingKnowledgeMapComponent>();
                    List<string> lines = knowledge.DebugOverviewLines();
                    Check("Knowledge", lines != null, "Animal Knowledge state API responds");
                    Check("Knowledge", !WildlifeKnowledgeStatPatch.IsPlayerPawn(null) &&
                        map.mapPawns.FreeColonists.All(WildlifeKnowledgeStatPatch.IsPlayerPawn) &&
                        map.mapPawns.AllPawnsSpawned.Where(pawn => pawn.Faction?.def?.isPlayer != true)
                            .All(pawn => !WildlifeKnowledgeStatPatch.IsPlayerPawn(pawn)),
                        "Knowledge stat evaluation identifies player pawns without the player-faction singleton");
                    Check("Knowledge",
                        WildlifeSpeciesClassification.Resolve(false, false, true) == false &&
                        WildlifeSpeciesClassification.Resolve(false, true, true) &&
                        !WildlifeSpeciesClassification.Resolve(true, true, false) &&
                        DefDatabase<ThingDef>.AllDefsListForReading.Where(def =>
                            def.race?.Animal == true).All(def =>
                            WildlifeSpeciesClassification.IsPredator(def) ||
                            !WildlifeSpeciesClassification.IsPredator(def)) &&
                        typeof(SpeciesBehaviorOverride).GetField("hasPredatorOverride") != null &&
                        typeof(SpeciesBehaviorOverride).GetField("hasPreyOverride") != null,
                        "Per-species Predator and Prey overrides preserve defaults and support loaded mod species");
                    Check("Knowledge", HuntingKnowledgeMapComponent.LevelForExperience(0f) == 0 &&
                        HuntingKnowledgeMapComponent.LevelForExperience(119.99f) == 0 &&
                        HuntingKnowledgeMapComponent.LevelForExperience(120f) == 1 &&
                        HuntingKnowledgeMapComponent.LevelForExperience(299.99f) == 1 &&
                        HuntingKnowledgeMapComponent.LevelForExperience(300f) == 2 &&
                        HuntingKnowledgeMapComponent.LevelForExperience(649.99f) == 2 &&
                        HuntingKnowledgeMapComponent.LevelForExperience(650f) == 3 &&
                        HuntingKnowledgeMapComponent.LevelForExperience(1200f) == 3,
                        "Biome Knowledge tiers use valid progression thresholds");
                    Check("Knowledge",
                        HuntingKnowledgeMapComponent.WildlifeProficiencyLabel(0) == "Novice" &&
                        HuntingKnowledgeMapComponent.WildlifeProficiencyLabel(1) == "Adept" &&
                        HuntingKnowledgeMapComponent.WildlifeProficiencyLabel(2) == "Expert" &&
                        HuntingKnowledgeMapComponent.WildlifeProficiencyLabel(3) == "Master",
                        "Wildlife proficiency tiers are ordered correctly");
                    Check("Knowledge", map.mapPawns.FreeColonists.All(pawn =>
                    {
                        float animalCoverage = knowledge.AnimalCoverage(pawn);
                        float biomeCoverage = knowledge.BiomeCoverage(pawn);
                        float combinedCoverage = knowledge.WildlifeProficiencyCoverage(pawn);
                        int proficiency = knowledge.WildlifeProficiencyLevel(pawn);
                        return animalCoverage >= 0f && animalCoverage <= 1f &&
                            biomeCoverage >= 0f && biomeCoverage <= 1f &&
                            Math.Abs(combinedCoverage - (animalCoverage + biomeCoverage) * 0.5f) < 0.001f &&
                            proficiency >= 0 && proficiency <= 3;
                    }), "Wildlife proficiency coverage and tiers are valid");
                    Check("Knowledge", map.mapPawns.FreeColonists.All(pawn =>
                        knowledge.BiomesForColonist(pawn).All(record =>
                            record.biome != null && record.experience >= 0f && record.completedExpeditions >= 0)),
                        "Biome Knowledge records are valid");
                    Check("Knowledge", ProgressionEducationKnowledgeCompatibility.Active ==
                        ModsConfig.IsActive("ferny.ProgressionEducation"),
                        "Optional Progression: Education integration state matches the active mod");
                    Check("Knowledge", DefDatabase<ThingDef>.AllDefsListForReading
                        .Where(def => def.race?.Animal == true)
                        .All(def => HuntingKnowledgeMapComponent.ColonyExperience(def) >= 0f),
                        "Species knowledge values are nonnegative");
                    Check("Knowledge", WildlifeTabKnowledgePolicy.RevealsIdentity(0) &&
                        !WildlifeTabKnowledgePolicy.RevealsBehavior(0) &&
                        WildlifeTabKnowledgePolicy.RevealsBehavior(1) &&
                        !WildlifeTabKnowledgePolicy.RevealsSignals(1) &&
                        WildlifeTabKnowledgePolicy.RevealsSignals(2) &&
                        !WildlifeTabKnowledgePolicy.RevealsIndividualMemory(2) &&
                        WildlifeTabKnowledgePolicy.RevealsIndividualMemory(3),
                        "Animal Wildlife tab reveals information progressively by colony knowledge");
                    Check("Knowledge", WildlifePassiveObservationPolicy.SelfTest(),
                        "Passive familiarity caps, diminishes repetition, and classifies meaningful discoveries");
                    List<PassiveObservationRecord> passiveRecords = knowledge.PassiveRecords.ToList();
                    Check("Knowledge", passiveRecords.Select(record => (record?.observer?.thingIDNumber ?? 0) + ":" +
                        (record.species?.defName ?? string.Empty)).Distinct().Count() == passiveRecords.Count,
                        "Passive exposure aggregates to one record per observer and species");
                    Check("Knowledge", passiveRecords.All(record => record != null && record.dailyExposure >= 0f &&
                        record.pendingExposure >= 0f && record.pendingExposure <= record.dailyExposure + 0.001f &&
                        record.dailyExposure <= (record.usedObservationPost ? WildlifePassiveObservationPolicy.ObservationPostDailyCap : WildlifePassiveObservationPolicy.DailyCap) + 0.001f),
                        "Passive exposure remains within its daily cap and save-safe pending balance");
                    List<WildlifeEvent> passiveEvents = WildlifeEventRouter.Shared.History
                        .Where(value => value?.metadata != null && value.metadata.TryGetValue("observationLayer", out string layer) &&
                            layer == "passive-meaningful").ToList();
                    Check("Knowledge", passiveEvents.GroupBy(value => value.sourceInstanceId ?? string.Empty)
                        .All(group => group.Key.NullOrEmpty() || group.Count() == 1),
                        "Stable passive day source IDs prevent duplicate rewards");
                    Check("Knowledge", passiveEvents.All(value => !value.summary.NullOrEmpty() &&
                        value.metadata.ContainsKey("previousAmount") && value.metadata.ContainsKey("newAmount") &&
                        value.metadata.ContainsKey("discoveryKind") && value.metadata.ContainsKey("observerId")),
                        "Meaningful passive events carry descriptive change metadata");
                    Check("Knowledge", typeof(PassiveObservationRecord).GetInterface(nameof(IExposable)) != null,
                        "Passive familiarity records are save-compatible");
                });

                Section("Regional", () =>
                {
                    RegionalWildlifeMapComponent regional = map.GetComponent<RegionalWildlifeMapComponent>();
                    Check("Regional", regional.DebugOverviewLines() != null, "Regional wildlife state API responds");
                    Check("Regional", regional.Records.All(record =>
                        record.nearbyPopulation >= 0f && record.previousNearbyPopulation >= 0f),
                        "Nearby population estimates are nonnegative");
                    Check("Regional",
                        !WildlifePopulationPolicy.CanAddLocalAnimal(100000, 90000, 0, 10f, 20f, false) &&
                        !WildlifePopulationPolicy.CanAddLocalAnimal(300000, 0, 3, 10f, 20f, false) &&
                        WildlifePopulationPolicy.CanAddLocalAnimal(300000, 90000, 0, 10f, 20f, false),
                        "Population policy prevents rapid replacement and excessive local spawning");
                    Check("Regional", typeof(RoamingAnimalRecord).GetField("herdId") != null &&
                        typeof(RegionalWildlifeMapComponent).GetMethod("NotifyLocalSpawn") != null &&
                        typeof(RegionalWildlifeMapComponent).GetMethod("NotifyLocalCapture") != null &&
                        typeof(RegionalWildlifeMapComponent).GetMethod("QueueDeparture", new[]
                            { typeof(Pawn), typeof(string), typeof(IntVec3) }) != null &&
                        typeof(RegionalWildlifeMapComponent).GetMethod("ShouldPreserveExit") != null,
                        "Population lifecycle and roaming herd state are save-compatible");
                    Check("Regional", regional.RoamingAnimals.All(record =>
                        record?.animal?.RaceProps?.Animal == true && record.species == record.animal.def &&
                        System.Enum.IsDefined(typeof(RoamingAnimalState), record.state) &&
                        (record.state == RoamingAnimalState.Present || record.state == RoamingAnimalState.Dead ||
                            record.expectedReturnTick > record.leftTick) &&
                        (record.animal.Spawned || Find.WorldPawns.Contains(record.animal))),
                        "Persistent roaming animals remain present or retained by WorldPawns");
                    HuntingExpeditionMapComponent expeditions = map.GetComponent<HuntingExpeditionMapComponent>();
                    Check("Regional", expeditions.KnownCellRecords.All(cell =>
                        cell.tileId >= 0 && cell.discoveryLevel >= 0 && cell.discoveryLevel <= 2 &&
                        cell.confidence >= 0f && cell.confidence <= 1f), "World-tile knowledge records are valid");
                    Check("Regional", expeditions.KnownCellRecords.All(cell =>
                    {
                        BiomeDef biome = Find.WorldGrid?[(PlanetTile)cell.tileId]?.PrimaryBiome;
                        return biome == null || cell.species.Where(entry => entry?.species != null && entry.population > 0f)
                            .All(entry => biome.AllWildAnimals.Any(kind => kind?.race == entry.species &&
                                biome.CommonalityOfAnimal(kind) > 0.001f));
                    }), "Recorded expedition animals are valid for their tile biome");
                    WildlifeRegionalStoriesMapComponent stories = map.GetComponent<WildlifeRegionalStoriesMapComponent>();
                    Check("Regional", stories != null &&
                        (stories.Wave == null || stories.Wave.species?.race?.Animal == true &&
                            stories.Wave.animals != null && stories.Wave.expectedExitTick > stories.Wave.startedTick),
                        "Visible migration wave state is valid");
                    Check("Regional", stories.TerritoryHistory.All(entry => entry?.animal?.RaceProps?.Animal == true &&
                        entry.from.IsValid && entry.to.IsValid && !entry.reason.NullOrEmpty()),
                        "Territory history entries are valid");
                    Check("Regional", stories.FamilyLines.All(line => line?.animal?.RaceProps?.Animal == true &&
                        line.parent?.RaceProps?.Animal == true && line.species == line.animal.def &&
                        line.generation > 0 && !line.lineName.NullOrEmpty()),
                        "Persistent wildlife family lines are valid");
                    WildlifeLandmarkMapComponent landmark = map.GetComponent<WildlifeLandmarkMapComponent>();
                    Check("Regional", landmark != null && landmark.Reputations.All(value =>
                        value?.species?.race?.Animal == true &&
                        value.sanctuary >= 0f && value.sanctuary <= 1f &&
                        value.water >= 0f && value.water <= 1f &&
                        value.feeding >= 0f && value.feeding <= 1f &&
                        value.forbidden >= 0f && value.forbidden <= 1f &&
                        value.killingGround >= 0f && value.killingGround <= 1f &&
                        value.predatorNest >= 0f && value.predatorNest <= 1f &&
                        value.sacred >= 0f && value.sacred <= 1f &&
                        value.unstable >= 0f && value.unstable <= 1f),
                        "Species-specific colony landmark reputations are valid");
                    Check("Regional", landmark.Reputations.All(value =>
                        landmark.MigrationAttraction(value.species) >= -1.5f &&
                        landmark.MigrationAttraction(value.species) <= 1.5f),
                        "Landmark migration effects remain within bounds");
                });

                Section("Notable", () =>
                {
                    NotableWildlifeMapComponent notable = map.GetComponent<NotableWildlifeMapComponent>();
                    Check("Notable", notable.Records.All(record => record?.species?.race?.Animal == true &&
                        !record.title.NullOrEmpty() && !record.distinction.NullOrEmpty() && record.history != null),
                        "Notable animal records are valid");
                    Check("Notable", notable.Records.All(record => record.lastProtectionResponseTick >= 0),
                        "Protected-animal response state is valid");
                    Check("Notable", notable.Records.Where(record => record?.animal?.Spawned == true && !record.animal.Dead)
                        .All(record => record.ability == null ||
                            record.animal.health.hediffSet.GetFirstHediffOfDef(record.ability) != null ||
                            !HerdsMod.Settings.enableNotableAnimals),
                        "Active notable animals have their distinction ability");
                    Check("Notable", typeof(Window_NotableAnimalStory) != null &&
                        typeof(JobDriver_StudyNotableAnimal) != null,
                        "Notable animal story UI and study job loaded");
                });

                Section("Journal", () =>
                {
                    WildlifeFieldJournalMapComponent journal = map.GetComponent<WildlifeFieldJournalMapComponent>();
                    Check("Journal", journal.DebugOverviewLines().Count >= 4, "Journal state API responds");
                    Check("Journal", journal.Entries.All(entry => entry?.species?.race?.Animal == true),
                        "Journal entries reference valid animal species");
                    Check("Journal", journal.OutcomeBonus >= 0f && journal.OutcomeBonus <= 0.10f &&
                        journal.HuntingSkillBonus >= 0f && journal.HuntingSkillBonus <= 2f,
                        "Permanent journal rewards remain within balance caps");
                    Check("Journal", journal.Opportunity == null ||
                        journal.Opportunity.expiresTick > journal.Opportunity.startedTick &&
                        journal.Opportunity.availableUntilTick > journal.Opportunity.startedTick &&
                        journal.Opportunity.species?.race?.Animal == true &&
                        !journal.Opportunity.eventKey.NullOrEmpty() &&
                        journal.Opportunity.wildlifeWitnesses >= 0,
                        "Active Wildlife Moment has valid real-event state");
                    Check("Journal", journal.MomentHistory.All(value =>
                        value?.species?.race?.Animal == true && !value.text.NullOrEmpty() &&
                        value.tick >= 0), "Wildlife Moment history is valid");
                    Check("Journal", Enum.GetValues(typeof(WildlifeMomentResponse)).Length == 6 &&
                        typeof(WildlifeFieldJournalMapComponent).GetMethod("CompleteMomentObservation") != null &&
                        typeof(WildlifeFieldJournalMapComponent).GetMethod("NotifyAnimalDeparture") != null &&
                        typeof(WildlifeFieldJournalMapComponent).GetMethod("ReferencesAnimal") != null &&
                        typeof(WildlifeFieldJournalMapComponent).GetMethod("MomentBridgeLines") != null,
                        "Wildlife Moments expose player responses, safe continuation, and bridge state");
                    Check("Journal",
                        WildlifeFieldJournalMapComponent.ResearchAllowsResponse(
                            WildlifeMomentResponse.Hunt) ==
                        WildlifeProgression.Unlocked(WildlifeCapability.Fieldcraft) &&
                        WildlifeFieldJournalMapComponent.ResearchAllowsResponse(
                            WildlifeMomentResponse.Track) ==
                        WildlifeProgression.Unlocked(WildlifeCapability.Telemetry),
                        "Moment Hunt and Track actions follow Organized Hunting and Telemetry research");
                    Check("Journal", WildlifeFieldJournalMapComponent.ProtectionAllowsFollowupSelfTest(),
                        "Protect is additive and leaves Observe available");
                    Check("Journal", WildlifeFieldJournalMapComponent.MomentAvailabilitySelfTest(),
                        "Unclaimed Wildlife Moments last a deterministic 1-3 in-game hours");
                    ResearchProjectDef expeditionResearch = DefDatabase<ResearchProjectDef>.GetNamedSilentFail("Wildlife_HuntingExpedition");
                    Check("Journal", Window_WildlifeJournal.ExpeditionsVisible() ==
                        (HerdsMod.Settings.enableOffMapHuntingExpeditions && expeditionResearch?.IsFinished == true),
                        "Field Guide Expeditions tab follows the expedition research gate");
                    Check("Journal", map.GetComponent<WildlifeEcologySnapshotMapComponent>()?.Current?.species
                        .SelectMany(value => value.evidence ?? Array.Empty<WildlifeEvidenceSnapshot>())
                        .All(value => value.summary != "A field observation added a small piece of evidence.") ?? true,
                        "Field Guide evidence excludes routine proximity summaries");
                    Check("Journal", typeof(WildlifeEvidenceSnapshot).GetField("amountDelta") != null &&
                        typeof(WildlifeEvidenceSnapshot).GetField("observerCount") != null &&
                        typeof(WildlifeEvidenceSnapshot).GetField("observationHours") != null,
                        "Field Guide evidence exposes concrete contribution and familiarity metadata");
                    WildlifeEcologySnapshot atlas = map.GetComponent<WildlifeEcologySnapshotMapComponent>()?.Current;
                    Check("Journal", atlas != null && atlas.species.All(value => value != null &&
                        value.species?.race?.Animal == true && value.confidence >= 0f && value.confidence <= 1f),
                        "Living Atlas derives bounded species activity state from the ecology snapshot");
                    Check("Journal", typeof(WildlifeEcologySnapshotMapComponent).GetMethod("DebugOverviewLines") != null,
                        "Living Atlas exposes bounded bridge diagnostics");
                    Check("Journal", typeof(WildlifeKnowledgeAdapter).GetMethod("PredatorPressureStateFor") != null &&
                        typeof(WildlifeSignalCultureMapComponent).GetMethod("ColonyPredatorPressure") != null,
                        "Region and Knowledge hubs can query qualitative predator-pressure state without owning it");
                    Window_WildlifeJournal defaultJournal = new Window_WildlifeJournal(map);
                    object defaultPage = AccessTools.Field(typeof(Window_WildlifeJournal), "page")?.GetValue(defaultJournal);
                    Check("Journal", defaultPage is WildlifeJournalPage &&
                        (WildlifeJournalPage)defaultPage == WildlifeJournalPage.FieldLog,
                        "Journal opens to the Field Log by default");
                    Check("Journal", (int)WildlifeJournalPage.FieldGuide == 0 &&
                        (int)WildlifeJournalPage.LivingAtlas == 1 &&
                        (int)WildlifeJournalPage.Signals == 2 &&
                        (int)WildlifeJournalPage.Investigations == 3 &&
                        (int)WildlifeJournalPage.Expeditions == 4 &&
                        (int)WildlifeJournalPage.Stories == 5 &&
                        (int)WildlifeJournalPage.FieldLog == 6 &&
                        Window_WildlifeJournal.TopLevelPagesForTesting().SequenceEqual(new[]
                        {
                            WildlifeJournalPage.FieldLog,
                            WildlifeJournalPage.Knowledge,
                            WildlifeJournalPage.Region,
                            WildlifeJournalPage.Chronicle
                        }),
                        "Journal preserves legacy page values and exposes four top-level hubs");
                    WildlifeJournalPage[] journalPages =
                    {
                        WildlifeJournalPage.FieldGuide, WildlifeJournalPage.LivingAtlas,
                        WildlifeJournalPage.Signals, WildlifeJournalPage.Investigations,
                        WildlifeJournalPage.Expeditions, WildlifeJournalPage.Stories,
                        WildlifeJournalPage.FieldLog, WildlifeJournalPage.Knowledge,
                        WildlifeJournalPage.Region, WildlifeJournalPage.Chronicle
                    };
                    Check("Journal", journalPages.All(value => new Window_WildlifeJournal(map, value) != null),
                        "Journal constructors retain direct page deep links");
                    WildlifeMenuEntry signalEntry = WildlifeMenuRegistry.VisibleEntriesForTesting()
                        .FirstOrDefault(entry => entry.id == "wildlife.signals");
                    Check("Journal", signalEntry == null &&
                        typeof(Window_WildlifeJournal).GetMethod("OpenSignals") != null &&
                        Window_WildlifeJournal.SignalsVisible() == (HerdsMod.Settings?.enableWildlifeSignalCulture == true),
                        "Signals is a setting-gated Journal page rather than a standalone menu entry");
                    Check("Journal", journal.Opportunity?.continuedAsTrail != true ||
                        journal.Opportunity.evidence is WildlifeSign,
                        "A departed Wildlife Moment retains physical trail evidence");
                    MapComponent packComponent = map.components.FirstOrDefault(component =>
                        component.GetType().FullName == "Packs.PackMapComponent");
                    Check("Journal", packComponent?.GetType().GetMethod("WildlifeMomentHuntPair") != null,
                        "Predator hunts can become Wildlife Moments without a hard dependency");
                    Check("Journal", journal.Project == null ||
                        journal.Project.species?.race?.Animal == true && journal.Project.progress >= 0f,
                        "Active stewardship project state is valid");
                    Check("Journal", !WildlifeFieldJournalMapComponent.ValidProject(null) &&
                        !WildlifeFieldJournalMapComponent.ProjectReady(null) &&
                        !WildlifeFieldJournalMapComponent.ValidProject(
                            new WildlifeStewardProjectRecord { progress = 1f }) &&
                        !WildlifeFieldJournalMapComponent.ProjectReady(
                            new WildlifeStewardProjectRecord { progress = 1f }),
                        "Invalid legacy stewardship projects are rejected before completion");
                    Check("Journal", Enum.GetValues(typeof(WildlifeStewardProjectKind)).Length >= 7,
                        "Expanded wildlife management goals are registered");
                    WildlifeMysteryMapComponent mysteries = map.GetComponent<WildlifeMysteryMapComponent>();
                    Check("Journal", mysteries != null && mysteries.Mysteries.All(value =>
                        value?.species?.race?.Animal == true && !value.title.NullOrEmpty() &&
                        !value.anomaly.NullOrEmpty() && !value.explanation.NullOrEmpty() &&
                        value.progress >= 0f && value.progress <= 1f && value.evidence != null &&
                        value.evidence.All(entry => entry != null && !entry.clue.NullOrEmpty() &&
                            !entry.source.NullOrEmpty() && entry.value > 0f) &&
                        (!value.Solved || value.solvedTick >= value.startedTick) &&
                        (!value.Resolved || value.Solved)),
                        "Living wildlife mysteries have valid causes, evidence, and resolutions");
                });

                Section("Memory", () =>
                {
                    Check("Memory",
                        WildlifeMemoryMapComponent.EventLabel(
                            AnimalMemoryKind.QuietObservation) ==
                        "quietly watching them observe wildlife",
                        "Wildlife can remember witnessing a colonist's quiet observation");
                    WildlifeMemoryMapComponent memory = map.GetComponent<WildlifeMemoryMapComponent>();
                    Check("Memory", memory != null && memory.DebugOverviewLines().Count == 2,
                        "Animal memory and folklore state API responds");
                    string detailedStory = WildlifeMemoryMapComponent.ContextSentence("Muffalo",
                        new[] { "Kim", "Lee" }, "the north pasture");
                    string fallbackStory = WildlifeMemoryMapComponent.ContextSentence(null,
                        null, null);
                    Check("Memory", detailedStory.Contains("Muffalo") &&
                        detailedStory.Contains("Kim") && detailedStory.Contains("Lee") &&
                        detailedStory.Contains("north pasture") &&
                        fallbackStory.Contains("identity was not preserved") &&
                        fallbackStory.Contains("names were not preserved") &&
                        fallbackStory.Contains("unrecorded place"),
                        "Colony Stories include animal, pawn, and location narrative with fallbacks");
                    Check("Memory", memory.Memories.All(value => value?.animal?.RaceProps?.Animal == true &&
                        value.colonist?.Faction == Faction.OfPlayer && value.trust >= 0f && value.trust <= 1f &&
                        value.fear >= 0f && value.fear <= 1f && value.hostility >= 0f && value.hostility <= 1f &&
                        value.huntingEncounters >= 0 && value.rangedEncounters >= 0 && value.trapEncounters >= 0 &&
                        value.events != null && value.events.All(entry => entry != null && entry.tick >= 0)),
                        "Individual animal memories are valid");
                    Check("Memory", memory.SocialMemories.All(value =>
                        value?.animal?.RaceProps?.Animal == true &&
                        value.otherAnimal?.RaceProps?.Animal == true &&
                        value.animal != value.otherAnimal &&
                        value.bond >= 0f && value.bond <= 1f &&
                        value.fear >= 0f && value.fear <= 1f &&
                        value.rivalry >= 0f && value.rivalry <= 1f &&
                        value.positiveEvents >= 0 && value.negativeEvents >= 0 &&
                        value.events != null && value.events.All(entry =>
                            entry != null && entry.tick >= 0 && entry.strength > 0f)),
                        "Animal-to-animal memories and encounters are valid");
                    Check("Memory", memory.SocialMemories.All(value =>
                        memory.SocialAffinity(value.animal, value.otherAnimal) >= -1f &&
                        memory.SocialAffinity(value.animal, value.otherAnimal) <= 1f),
                        "Remembered social affinity remains within behavior bounds");
                    Check("Memory", System.Enum.IsDefined(typeof(AnimalMemoryKind), AnimalMemoryKind.WarningLearned) &&
                        System.Enum.IsDefined(typeof(AnimalMemoryKind), AnimalMemoryKind.Gunfire),
                        "Learned tactics and socially shared warnings are registered");
                    Check("Memory", memory.Memories.All(value =>
                        memory.AvoidanceFactor(value.animal, value.colonist) >= 0.6f &&
                        memory.AvoidanceFactor(value.animal, value.colonist) <= 1.8f),
                        "Trust, fear, and learned hunting responses remain within behavior bounds");
                    Check("Memory", memory.Folklore.All(value => value != null && !value.title.NullOrEmpty() &&
                        !value.story.NullOrEmpty() && value.retellings >= 0 && value.reach >= 0 && value.reach <= 2),
                        "Folklore records and legend reach are valid");
                    AnimalTraditionMapComponent traditions = map.GetComponent<AnimalTraditionMapComponent>();
                    Check("Memory", traditions != null && traditions.Traditions.All(value =>
                        value?.species?.race?.Animal == true && value.holders != null &&
                        value.holders.All(holder => holder?.RaceProps?.Animal == true) &&
                        value.strength >= 0f && value.strength <= 1f &&
                        value.accuracy >= 0f && value.accuracy <= 1f &&
                        !value.title.NullOrEmpty() && !value.belief.NullOrEmpty()),
                        "Animal traditions, mutations, and holders are valid");
                    Check("Memory", map.mapPawns.AllPawnsSpawned.Where(pawn => pawn.RaceProps?.Animal == true)
                        .Take(20).All(pawn => map.mapPawns.FreeColonistsSpawned.Take(3).All(colonist =>
                        {
                            float factor = traditions.AvoidanceFactor(pawn, colonist);
                            return factor >= 0.55f && factor <= 1.9f;
                        })), "Animal tradition behavior factors remain within bounds");
                    Check("Memory", memory.LegendQuest == null ||
                        memory.LegendQuest.species?.race?.Animal == true &&
                        memory.LegendQuest.expiresTick > memory.LegendQuest.startedTick,
                        "Legend challenge state is valid");
                    Check("Memory", map.GetComponent<NotableWildlifeMapComponent>().Records.All(value =>
                        System.Enum.IsDefined(typeof(WildlifeCulturalStatus), value.culturalStatus)),
                        "Notable animal cultural status is valid");
                    WildlifeLivesMapComponent lives = map.GetComponent<WildlifeLivesMapComponent>();
                    Check("Memory", lives != null && lives.DebugLines().Count == 2,
                        "Wildlife lives state API responds");
                    Check("Memory", lives.Personalities.All(value => value?.animal?.RaceProps?.Animal == true &&
                        System.Enum.IsDefined(typeof(AnimalPersonality), value.personality) &&
                        (!value.inherited || value.inheritedFrom != null)),
                        "Animal personalities and inheritance records are valid");
                    if (ModsConfig.IdeologyActive)
                    {
                        Check("Memory", HerdsDefOf.Herds_WildlifeEthic_Reverence != null &&
                            HerdsDefOf.Herds_WildlifeEthic_Stewardship != null &&
                            HerdsDefOf.Herds_WildlifeEthic_Tradition != null,
                            "Wildlife Ideology precepts loaded");
                        Check("Memory", HerdsDefOf.Herds_IdeoRole_MasterHunter != null &&
                            HerdsDefOf.Herds_IdeoRole_MasterConservationist != null,
                            "Wildlife Ideology roles loaded");
                    }
                });

                Section("UI", () =>
                {
                    Check("UI", SpeciesKnowledgeStatsPatch.AnimalKnowledgeInsertIndex(
                            new[] { "Description", "Market Value" }) == 1 &&
                        SpeciesKnowledgeStatsPatch.AnimalKnowledgeInsertIndex(
                            new[] { "Market Value" }) == 0,
                        "Unlocked Description appears before Animal Knowledge in Stats");
                    ThingDef preyDef = DefDatabase<ThingDef>.AllDefsListForReading.FirstOrDefault(PreyProfileDatabase.IsEligible);
                    Check("UI", preyDef?.inspectorTabs?.Contains(typeof(ITab_Herd)) == true, "Prey Wildlife tab registered");
                    Check("UI", DefDatabase<ThingDef>.AllDefsListForReading.Where(def => def.race?.Animal == true)
                        .All(def => def.inspectorTabs?.Contains(typeof(ITab_AnimalMemory)) == true),
                        "Universal animal Memory tab registered");
                    Check("UI", AccessTools.Method(typeof(AnimalMemoryPresentation),
                        nameof(AnimalMemoryPresentation.DrawSocialWeb)) != null &&
                        typeof(Window_AnimalMemoryTimeline).GetConstructor(new[]
                        { typeof(Pawn), typeof(bool) }) != null,
                        "Interactive animal social web is available from Memory");
                    Check("UI", DefDatabase<ThingDef>.AllDefsListForReading.Where(def => def.race?.Animal == true).All(def =>
                        def.inspectorTabsResolved?.Any(tab => tab.GetType() == typeof(ITab_AnimalMemory)) == true &&
                        (!PreyProfileDatabase.IsEligible(def) ||
                            def.inspectorTabsResolved.Any(tab => tab.GetType() == typeof(ITab_Herd)))),
                        "Resolved selected-animal tabs retain Memory and applicable Wildlife entries");
                    Check("UI", DefDatabase<ThingDef>.AllDefsListForReading.Where(def => def.race?.Animal == true)
                        .All(def =>
                        {
                            int health = def.inspectorTabs.FindIndex(type => type.FullName == "RimWorld.ITab_Pawn_Health");
                            int needs = def.inspectorTabs.FindIndex(type => type.FullName == "RimWorld.ITab_Pawn_Needs");
                            int training = def.inspectorTabs.FindIndex(type => type.FullName == "RimWorld.ITab_Pawn_Training");
                            int social = def.inspectorTabs.FindIndex(type => type.FullName == "RimWorld.ITab_Pawn_Social");
                            int memory = def.inspectorTabs.IndexOf(typeof(ITab_AnimalMemory));
                            int log = def.inspectorTabs.FindIndex(type => type.FullName == "RimWorld.ITab_Pawn_Log");
                            int wildlife = def.inspectorTabs.FindIndex(type =>
                                type.FullName == "Herds.ITab_Herd" || type.FullName == "Packs.ITab_Pack");
                            bool Ordered(int left, int right) =>
                                left < 0 || right < 0 || left < right;
                            return memory >= 0 && Ordered(needs, memory) &&
                                Ordered(memory, health) && Ordered(health, social) &&
                                Ordered(social, training) && Ordered(training, wildlife) &&
                                Ordered(wildlife, log);
                        }), "Available animal tabs follow the safe right-to-left ordering");
                    Check("UI", AccessTools.Method(typeof(AnimalNeedsTabStaleSelectionGuard), "Prefix") != null,
                        "Needs tab safely handles despawned or cleared animal selection");
                    IReadOnlyList<WildlifeMenuEntry> wildlifeMenu =
                        WildlifeMenuRegistry.VisibleEntriesForTesting();
                    Check("UI", wildlifeMenu.Select(entry => entry.id).Distinct().Count() == wildlifeMenu.Count &&
                        wildlifeMenu.SequenceEqual(wildlifeMenu.OrderBy(entry => entry.order)
                            .ThenBy(entry => entry.label, StringComparer.OrdinalIgnoreCase)
                            .ThenBy(entry => entry.id, StringComparer.Ordinal)),
                        "Shared Wildlife menu entries are unique and use stable ordering");
                    Check("UI", WildlifeMenuRegistry.RequiredHeight(4, 560f) == 80f,
                        "Shared Wildlife menu reserves two rows when four buttons wrap at narrow width");
                    Check("UI", Window_WildlifeOverview.OutcomeRowHeight(
                            "A long wildlife outcome wraps across several lines without clipping its text.",
                            120f) > 48f,
                        "Recent Outcome rows grow for wrapped text");
                    Check("UI", typeof(ChoiceLetter_WildlifeStory).GetMethod("OpenLetter") != null &&
                        typeof(Window_WildlifeFieldJournal).GetConstructor(new[]
                        { typeof(Map), typeof(int), typeof(int) }) != null,
                        "Colony Story letters can reopen Folklore at a saved story tick");
                    Check("UI", typeof(Window_WildlifeTrail).GetConstructor(new[]
                            { typeof(Map), typeof(WildlifeTrailLead) }) != null &&
                        typeof(Window_WildlifeTrailBoard).GetConstructor(new[] { typeof(Map) }) != null &&
                        typeof(Window_WildlifeLandscape).GetConstructor(new[] { typeof(Map) }) != null &&
                        typeof(Window_RegionalWildlife).GetConstructor(new[] { typeof(Map) }) != null &&
                        typeof(Window_WildlifeExpeditions).GetConstructor(new[] { typeof(Map) }) != null,
                        "Journal detail destinations retain trail, region, landscape, and expedition constructors");
                    Check("UI", typeof(Window_WildlifeSignals).GetConstructor(new[]
                        {
                            typeof(Map), typeof(Pawn), typeof(ThingDef),
                            typeof(UnityEngine.Vector2?), typeof(UnityEngine.Vector2?)
                        }) != null,
                        "Legacy Signal Guide callers redirect with viewer, species, and scroll state");
                    Check("UI", AccessTools.Method(typeof(WildlifeUI), "Focus",
                            new[] { typeof(Thing) }) != null &&
                        AccessTools.Method(typeof(WildlifeUI), "Focus",
                            new[] { typeof(IntVec3), typeof(Map) }) != null,
                        "Focus actions share menu-closing target navigation");
                    Check("UI", wildlifeMenu.FirstOrDefault()?.id == "wildlife.overview" &&
                        wildlifeMenu.First().order == WildlifeMenuRegistry.OverviewOrder,
                         "Wildlife Journal is the first shared Wildlife menu button");
                    bool horticultureActive = ModsConfig.IsActive("lan.horticulture.novelseeds");
                    MainButtonDef cultivarRegistry =
                        DefDatabase<MainButtonDef>.GetNamedSilentFail("HNS_CultivarRegistry");
                    Check("UI", wildlifeMenu.Any(entry => entry.id == "horticulture.novel-seeds") ==
                            horticultureActive &&
                        (!horticultureActive || cultivarRegistry?.tabWindowClass?.FullName ==
                            "HorticultureNovelSeeds.MainTabWindow_CultivarRegistry"),
                        "Optional Horticulture button reuses the Novel Seeds Cultivar Registry");
                    bool aquacultureActive = ModsConfig.IsActive("lan.aquaculture.fishing");
                    MainButtonDef aquacultureJournal =
                        DefDatabase<MainButtonDef>.GetNamedSilentFail("AF_AquacultureJournal");
                    Check("UI", wildlifeMenu.Any(entry => entry.id == "aquaculture.fish-journal") ==
                            aquacultureActive &&
                        (!aquacultureActive || aquacultureJournal?.tabWindowClass?.FullName ==
                            "AquacultureFishing.MainTabWindow_AquacultureJournal"),
                        "Optional Aquaculture button reuses the existing Fish Journal");
                    List<string> expectedSharedButtons = new List<string> { "Wildlife Journal" };
                    if (horticultureActive) expectedSharedButtons.Add("Horticulture");
                    if (aquacultureActive) expectedSharedButtons.Add("Aquaculture");
                    Check("UI", wildlifeMenu.Take(expectedSharedButtons.Count)
                            .Select(entry => entry.label).SequenceEqual(expectedSharedButtons),
                        "Shared Wildlife menu begins with the requested available buttons in order");
                    WildlifeMenuEntry expeditionsEntry =
                        wildlifeMenu.FirstOrDefault(entry => entry.id == "wildlife.expeditions");
                    Check("UI", expeditionsEntry == null || expeditionsEntry.label == "Expeditions",
                        "Wildlife expedition navigation uses the concise Expeditions label");
                    Type predatorTab = AccessTools.TypeByName("Packs.ITab_Pack");
                    List<ThingDef> predatorDefs = DefDatabase<ThingDef>.AllDefsListForReading.Where(def =>
                        def.race?.Animal == true &&
                        WildlifeSpeciesClassification.IsPredator(def)).ToList();
                    Warn("UI", predatorDefs.Count == 0 || predatorDefs.Any(def => def.inspectorTabs?.Contains(predatorTab) == true),
                        "Predator Wildlife tab is not registered on any predator");
                    Check("UI", typeof(WITab_Nature).IsSubclassOf(typeof(WITab)), "World-map Nature tab loaded");
                    Check("UI", AccessTools.Method(typeof(WorldInspectPane), "get_CurTabs") != null,
                        "World inspector tab integration target exists");
                    Check("UI", AccessTools.Method(typeof(GizmoGridDrawer), nameof(GizmoGridDrawer.DrawGizmoGrid)) != null,
                        "Send Expedition world gizmo integration target exists");
                });
            }

            stopwatch.Stop();
            return WriteReport(map, results, stopwatch.ElapsedMilliseconds, quiet);
        }

        private static bool WriteReport(Map map, List<Result> results, long milliseconds, bool quiet)
        {
            int passed = results.Count(result => result.severity == "PASS");
            int warnings = results.Count(result => result.severity == "WARN");
            int failed = results.Count(result => result.severity == "FAIL");
            List<string> lines = new List<string>
            {
                "WILDLIFE_TEST_REPORT v1",
                "utc=" + DateTime.UtcNow.ToString("O") + " tick=" + (Find.TickManager?.TicksGame ?? -1),
                "summary=" + (failed == 0 ? "PASS" : "FAIL") + " pass=" + passed + " warn=" + warnings +
                    " fail=" + failed + " ms=" + milliseconds,
                "context=map:" + (map?.uniqueID.ToString() ?? "none") + " pawns:" +
                    (map?.mapPawns?.AllPawnsSpawnedCount.ToString() ?? "0") + " mods:" + LoadedModManager.RunningModsListForReading.Count()
            };
            if (map != null)
            {
                lines.Add("features=prey:" + Bool(HerdsMod.Settings.enablePreyAndHerds) +
                    " hunts:" + Bool(HerdsMod.Settings.enableHuntingChanges) +
                    " expeditions:" + Bool(HerdsMod.Settings.enableOffMapHuntingExpeditions) +
                    " regional:" + Bool(HerdsMod.Settings.enableRegionalPopulations) +
                    " knowledge:" + Bool(HerdsMod.Settings.enableWildlifeKnowledge));
                lines.Add("metrics=prey:" + map.mapPawns.AllPawnsSpawned.Count(pawn => PreyProfileDatabase.IsEligible(pawn.def)) +
                    " predators:" + map.mapPawns.AllPawnsSpawned.Count(pawn =>
                        WildlifeSpeciesClassification.IsPredator(pawn.def)) +
                    " signs:" + map.listerThings.ThingsOfDef(HerdsDefOf.Herds_WildlifeSign).Count +
                    " knownTiles:" + map.GetComponent<HuntingExpeditionMapComponent>().KnownCellRecords.Count +
                    " activeExpeditions:" + map.GetComponent<HuntingExpeditionMapComponent>().ActiveExpeditions.Count);
            }
            foreach (IGrouping<string, Result> section in results.GroupBy(result => result.section))
                lines.Add("section=" + section.Key + " pass=" + section.Count(result => result.severity == "PASS") +
                    " warn=" + section.Count(result => result.severity == "WARN") +
                    " fail=" + section.Count(result => result.severity == "FAIL"));
            foreach (Result result in results.Where(result => result.severity != "PASS"))
                lines.Add(result.severity + "|" + result.section + "|" + result.text.Replace('\n', ' '));
            try
            {
                File.WriteAllLines(ReportPath, lines);
                if (!quiet)
                {
                    Log.Message("[WildlifeTest][FullSuite] " + lines[2] + " report=" + ReportPath);
                    Messages.Message("Wildlife test " + (failed == 0 ? "passed" : "failed") + ": " + passed + " passed, " +
                        warnings + " warnings, " + failed + " failed. Report saved.", failed == 0 ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.NegativeEvent, false);
                }
                return failed == 0;
            }
            catch (Exception exception)
            {
                Log.Error("[WildlifeTest][FullSuite] Could not write report: " + exception);
                if (!quiet)
                    Messages.Message("Wildlife test ran but the report could not be saved: " + exception.GetBaseException().Message,
                        MessageTypeDefOf.NegativeEvent, false);
                return false;
            }
        }

        private static string Bool(bool value) => value ? "on" : "off";
    }
}
