using System;
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

        public static bool ServerMode => GenCommandLine.CommandLineArgPassed("wildlifetestserver");
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
