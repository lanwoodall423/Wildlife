using System;
using System.Globalization;
using System.IO;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace Herds
{
    [HarmonyPatch(typeof(Game), nameof(Game.FinalizeInit))]
    public static class WildlifeAutomatedTestRunner
    {
        private static bool scheduled;
        private const string RequestFile = "Wildlife-AutoTest.request";
        private const string StopFile = "Wildlife-AutoTest.stop";
        private const string StatusFile = "Wildlife-AutoTest.status";
        private static readonly string configuredLaunchId =
            Environment.GetEnvironmentVariable("DEVBRIDGE_LAUNCH_ID");
        private static readonly int configuredGeneration = ReadGeneration();

        public static bool ServerMode => GenCommandLine.CommandLineArgPassed("wildlifetestserver");
        public static bool AutomaticMode => !string.IsNullOrWhiteSpace(configuredLaunchId);
        public static string LaunchId => configuredLaunchId ?? "none";
        public static int Generation => configuredGeneration;
        public static string RequestPath => Path.Combine(GenFilePaths.SaveDataFolderPath, RequestFile);
        public static string StopPath => Path.Combine(GenFilePaths.SaveDataFolderPath, StopFile);
        public static string StatusPath => Path.Combine(GenFilePaths.SaveDataFolderPath, StatusFile);

        public static bool Prepare() =>
            GenCommandLine.CommandLineArgPassed("wildlifetest") || ServerMode;

        public static void Postfix()
        {
            if (scheduled) return;
            scheduled = true;
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                // DevBridge launches quicktest with the automatic mode environment.
                // The GameComponentTick runner waits for a stable playable UI/map;
                // running from FinalizeInit is early enough to leave World.UI unset.
                if (AutomaticMode) return;
                if (ServerMode)
                {
                    TryDelete(RequestPath);
                    TryDelete(StopPath);
                    File.WriteAllText(StatusPath, "READY");
                    return;
                }

                bool passed = WildlifeInGameTestSuite.Run(true);
                Application.Quit(passed ? 0 : 1);
            });
        }

        private static void TryDelete(string path)
        {
            if (File.Exists(path)) File.Delete(path);
        }

        private static int ReadGeneration()
        {
            return int.TryParse(Environment.GetEnvironmentVariable("DEVBRIDGE_GENERATION"),
                NumberStyles.Integer, CultureInfo.InvariantCulture, out int generation)
                ? generation
                : 0;
        }

        internal static void WriteAutomaticStatus(string state, string detail = null)
        {
            string value = state + " " + LaunchId + " generation=" + Generation.ToString(CultureInfo.InvariantCulture);
            if (!string.IsNullOrWhiteSpace(detail))
                value += " " + detail.Replace('\r', ' ').Replace('\n', ' ');
            string temporaryPath = StatusPath + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllText(temporaryPath, value);
                if (File.Exists(StatusPath))
                {
                    try
                    {
                        File.Replace(temporaryPath, StatusPath, null);
                        return;
                    }
                    catch
                    {
                        File.Delete(StatusPath);
                    }
                }
                File.Move(temporaryPath, StatusPath);
            }
            catch (Exception exception)
            {
                try
                {
                    if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
                }
                catch
                {
                    // Preserve the original error in the game log.
                }
                Log.Warning("[WildlifeTest] Could not write automatic test status: " + exception.Message);
            }
        }
    }

    public sealed class WildlifeAutomatedTestGameComponent : GameComponent
    {
        private const int StablePlayableTicks = 30;
        private bool initialized;
        private bool completed;
        private int playableTicks;

        public WildlifeAutomatedTestGameComponent(Game game)
        {
        }

        public override void GameComponentTick()
        {
            if (completed || !WildlifeAutomatedTestRunner.AutomaticMode) return;

            if (!initialized)
            {
                initialized = true;
                WildlifeAutomatedTestRunner.WriteAutomaticStatus("PENDING");
            }

            if (!IsPlayableMap())
            {
                playableTicks = 0;
                return;
            }

            if (++playableTicks < StablePlayableTicks) return;
            completed = true;
            WildlifeAutomatedTestRunner.WriteAutomaticStatus("RUNNING");
            try
            {
                bool passed = WildlifeInGameTestSuite.Run(true);
                WildlifeAutomatedTestRunner.WriteAutomaticStatus("DONE", passed ? "PASS" : "FAIL");
                Log.Message("[WildlifeTest] Automatic DevBridge2 suite completed: " +
                    (passed ? "PASS" : "FAIL") + ". Report=" + WildlifeInGameTestSuite.ReportPath);
            }
            catch (Exception exception)
            {
                WildlifeAutomatedTestRunner.WriteAutomaticStatus("ERROR",
                    exception.GetBaseException().GetType().Name + ": " + exception.GetBaseException().Message);
                Log.Error("[WildlifeTest] Automatic suite failed outside a test section: " + exception);
            }
        }

        private static bool IsPlayableMap()
        {
            try
            {
                // Quicktest can enter the map scene before Root_Play has finished
                // constructing the world-side UI.  Waiting here keeps the suite
                // out of that transient state; TickManager.ForcePaused itself
                // dereferences TilePicker during the same update path we verify.
                return GenScene.InPlayScene && Current.Game != null && Find.CurrentMap != null &&
                    Find.TickManager != null && Find.UIRoot != null &&
                    Find.WorldInterface != null && Find.TilePicker != null;
            }
            catch
            {
                return false;
            }
        }
    }

    [HarmonyPatch(typeof(Game), nameof(Game.UpdatePlay))]
    public static class WildlifeAutomatedTestServer
    {
        private static int nextCheckFrame;

        public static bool Prepare() => WildlifeAutomatedTestRunner.ServerMode;

        public static void Postfix()
        {
            if (Time.frameCount < nextCheckFrame) return;
            nextCheckFrame = Time.frameCount + 10;
            try
            {
                if (File.Exists(WildlifeAutomatedTestRunner.StopPath))
                {
                    File.Delete(WildlifeAutomatedTestRunner.StopPath);
                    Application.Quit(0);
                    return;
                }
                if (!File.Exists(WildlifeAutomatedTestRunner.RequestPath)) return;

                string request = File.ReadAllText(WildlifeAutomatedTestRunner.RequestPath).Trim();
                File.Delete(WildlifeAutomatedTestRunner.RequestPath);
                bool passed = WildlifeInGameTestSuite.Run(true);
                File.WriteAllText(WildlifeAutomatedTestRunner.StatusPath,
                    "DONE " + request + " " + (passed ? "PASS" : "FAIL"));
            }
            catch (Exception exception)
            {
                File.WriteAllText(WildlifeAutomatedTestRunner.StatusPath,
                    "ERROR " + exception.GetBaseException().Message.Replace('\r', ' ').Replace('\n', ' '));
            }
        }
    }
}
