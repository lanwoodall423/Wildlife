using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace Herds
{
    public sealed class WildlifeTestRequest
    {
        public string requestId;
        public string launchId;
        public int generation;
        public string suite;
        public string mode;
        public string agent;
        public string leaseId;
        public string requestedUtc;
    }

    public sealed class WildlifeTestResult
    {
        public string requestId;
        public string launchId;
        public int generation;
        public string suite;
        public string mode;
        public string agent;
        public string leaseId;
        public string status;
        public bool success;
        public string errorCode;
        public string error;
        public string reportPath;
        public string completedUtc;
    }

    [StaticConstructorOnStartup]
    public static class WildlifeDevHarnessStartup
    {
        static WildlifeDevHarnessStartup()
        {
            new Harmony("lan.wildlife.devharness").PatchAll(Assembly.GetExecutingAssembly());
        }
    }

    [HarmonyPatch(typeof(Game), nameof(Game.FinalizeInit))]
    public static class WildlifeAutomatedTestRunner
    {
        private static readonly object Gate = new object();
        private static bool running;

        public static string LaunchId =>
            Environment.GetEnvironmentVariable("DEVBRIDGE_LAUNCH_ID") ?? string.Empty;

        public static int Generation =>
            int.TryParse(Environment.GetEnvironmentVariable("DEVBRIDGE_GENERATION"),
                NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : 0;

        public static bool AutomaticMode => false;
        public static bool ExplicitRequestActive { get; private set; }

        public static string RequestDirectory =>
            Path.Combine(GenFilePaths.SaveDataFolderPath, "Wildlife-TestRequests");

        public static string ReportPath => WildlifeInGameTestSuite.ReportPath;

        public static void Postfix()
        {
            // This patch deliberately does not start a test.  Requests are claimed
            // only by WildlifeAutomatedTestGameComponent after an external caller
            // has written a matching, atomic JSON request.
        }

        internal static bool TryBeginRequest()
        {
            lock (Gate)
            {
                if (running) return false;
                running = true;
                ExplicitRequestActive = true;
                return true;
            }
        }

        internal static void EndRequest()
        {
            lock (Gate)
            {
                ExplicitRequestActive = false;
                running = false;
            }
        }

        internal static void WriteResult(WildlifeTestResult result)
        {
            Directory.CreateDirectory(RequestDirectory);
            string path = Path.Combine(RequestDirectory, result.requestId + ".result.json");
            AtomicWrite(path, JsonUtility.ToJson(result));
        }

        internal static string RequestPath(string requestId) =>
            Path.Combine(RequestDirectory, requestId + ".request.json");

        internal static string ActiveRequestPath(string requestId) =>
            Path.Combine(RequestDirectory, requestId + ".active.json");

        internal static bool TryReadRequest(string path, out WildlifeTestRequest request, out string error)
        {
            request = null;
            error = null;
            try
            {
                request = JsonUtility.FromJson<WildlifeTestRequest>(File.ReadAllText(path));
                if (request == null) throw new InvalidDataException("request JSON was null");
                return true;
            }
            catch (Exception exception)
            {
                error = exception.GetBaseException().Message;
                return false;
            }
        }

        internal static bool TryClaim(string requestPath, string activePath)
        {
            try
            {
                File.Move(requestPath, activePath);
                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        internal static bool IdentityMatches(WildlifeTestRequest request, out string errorCode, out string error)
        {
            errorCode = null;
            error = null;
            if (string.IsNullOrWhiteSpace(LaunchId) || Generation <= 0)
            {
                errorCode = "MISSING_LAUNCH_IDENTITY";
                error = "DevBridge2 launch identity is unavailable in the game process.";
                return false;
            }
            if (!string.Equals(request.launchId, LaunchId, StringComparison.Ordinal))
            {
                errorCode = "STALE_LAUNCH_ID";
                error = "request launchId does not match the active DevBridge2 launch.";
                return false;
            }
            if (request.generation != Generation)
            {
                errorCode = "STALE_GENERATION";
                error = "request generation does not match the active DevBridge2 generation.";
                return false;
            }
            return true;
        }

        public static bool ProtocolSelfTest()
        {
            try
            {
                WildlifeTestRequest sample = new WildlifeTestRequest
                {
                    requestId = "protocol-self-test",
                    launchId = "launch",
                    generation = 1,
                    suite = "full",
                    mode = "destructive",
                    agent = "agent",
                    leaseId = "lease"
                };
                WildlifeTestRequest roundTrip = JsonUtility.FromJson<WildlifeTestRequest>(
                    JsonUtility.ToJson(sample));
                return roundTrip != null && roundTrip.requestId == sample.requestId &&
                    roundTrip.launchId == sample.launchId && roundTrip.generation == sample.generation &&
                    roundTrip.suite == sample.suite && roundTrip.mode == sample.mode &&
                    roundTrip.agent == sample.agent && roundTrip.leaseId == sample.leaseId;
            }
            catch
            {
                return false;
            }
        }

        internal static WildlifeTestResult Rejected(WildlifeTestRequest request, string requestId,
            string errorCode, string error)
        {
            return new WildlifeTestResult
            {
                requestId = requestId,
                launchId = request?.launchId ?? LaunchId,
                generation = request?.generation ?? Generation,
                suite = request?.suite,
                mode = request?.mode,
                agent = request?.agent,
                leaseId = request?.leaseId,
                status = "REJECTED",
                success = false,
                errorCode = errorCode,
                error = error,
                reportPath = ReportPath,
                completedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)
            };
        }

        internal static void AtomicWrite(string path, string content)
        {
            string temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllText(temporary, content, new UTF8Encoding(false));
                if (File.Exists(path))
                {
                    try
                    {
                        File.Replace(temporary, path, null);
                        return;
                    }
                    catch (PlatformNotSupportedException) { }
                    catch (IOException) { }
                    File.Delete(path);
                }
                File.Move(temporary, path);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
    }

    public sealed class WildlifeAutomatedTestGameComponent : GameComponent
    {
        private const int PollIntervalTicks = 15;
        private int nextPollTick;

        public WildlifeAutomatedTestGameComponent(Game game) { }

        public override void GameComponentTick()
        {
            if (Current.Game == null || Find.CurrentMap == null ||
                Find.TickManager == null || Find.UIRoot == null) return;
            if (Find.TickManager.TicksGame < nextPollTick) return;
            nextPollTick = Find.TickManager.TicksGame + PollIntervalTicks;
            ProcessOneRequest();
        }

        private static void ProcessOneRequest()
        {
            Directory.CreateDirectory(WildlifeAutomatedTestRunner.RequestDirectory);
            if (!IsPlayableMap() || !WildlifeAutomatedTestRunner.TryBeginRequest()) return;

            string activePath = null;
            string requestId = null;
            WildlifeTestRequest request = null;
            try
            {
                string requestPath = Directory.EnumerateFiles(
                    WildlifeAutomatedTestRunner.RequestDirectory, "*.request.json")
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
                if (requestPath == null) return;

                string fileName = Path.GetFileName(requestPath);
                requestId = fileName.Substring(0, fileName.Length - ".request.json".Length);
                if (requestId.Length == 0 || requestId.Any(value => !char.IsLetterOrDigit(value) && value != '-' && value != '_'))
                {
                    WildlifeAutomatedTestRunner.WriteResult(
                        WildlifeAutomatedTestRunner.Rejected(null, requestId, "INVALID_REQUEST_ID", "request filename is invalid."));
                    File.Delete(requestPath);
                    return;
                }

                activePath = WildlifeAutomatedTestRunner.ActiveRequestPath(requestId);
                if (!WildlifeAutomatedTestRunner.TryClaim(requestPath, activePath)) return;

                if (!WildlifeAutomatedTestRunner.TryReadRequest(activePath, out request, out string parseError))
                {
                    WildlifeAutomatedTestRunner.WriteResult(
                        WildlifeAutomatedTestRunner.Rejected(null, requestId, "INVALID_JSON", parseError));
                    return;
                }

                if (!string.Equals(request.requestId, requestId, StringComparison.Ordinal))
                {
                    WildlifeAutomatedTestRunner.WriteResult(
                        WildlifeAutomatedTestRunner.Rejected(request, requestId, "REQUEST_ID_MISMATCH",
                            "requestId does not match the atomic request filename."));
                    return;
                }

                if (!WildlifeAutomatedTestRunner.IdentityMatches(request, out string identityCode, out string identityError))
                {
                    WildlifeAutomatedTestRunner.WriteResult(
                        WildlifeAutomatedTestRunner.Rejected(request, requestId, identityCode, identityError));
                    return;
                }

                if (!string.Equals(request.suite, "full", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(request.suite, "WildlifeInGameTestSuite", StringComparison.OrdinalIgnoreCase))
                {
                    WildlifeAutomatedTestRunner.WriteResult(
                        WildlifeAutomatedTestRunner.Rejected(request, requestId, "UNSUPPORTED_SUITE",
                            "only the full WildlifeInGameTestSuite is supported."));
                    return;
                }

                if (!string.Equals(request.mode, "destructive", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(request.mode, "automatic", StringComparison.OrdinalIgnoreCase))
                {
                    WildlifeAutomatedTestRunner.WriteResult(
                        WildlifeAutomatedTestRunner.Rejected(request, requestId, "UNSUPPORTED_MODE",
                            "the requested mode is not supported."));
                    return;
                }

                bool passed = WildlifeInGameTestSuite.Run(true);
                WildlifeAutomatedTestRunner.WriteResult(new WildlifeTestResult
                {
                    requestId = requestId,
                    launchId = WildlifeAutomatedTestRunner.LaunchId,
                    generation = WildlifeAutomatedTestRunner.Generation,
                    suite = request.suite,
                    mode = request.mode,
                    agent = request.agent,
                    leaseId = request.leaseId,
                    status = passed ? "PASS" : "FAIL",
                    success = passed,
                    reportPath = WildlifeInGameTestSuite.ReportPath,
                    completedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)
                });
                Log.Message("[WildlifeTest] Explicit Wildlife suite completed: " +
                    (passed ? "PASS" : "FAIL") + " requestId=" + requestId +
                    " generation=" + WildlifeAutomatedTestRunner.Generation.ToString(CultureInfo.InvariantCulture));
            }
            catch (Exception exception)
            {
                WildlifeAutomatedTestRunner.WriteResult(new WildlifeTestResult
                {
                    requestId = requestId ?? Guid.NewGuid().ToString("N"),
                    launchId = request?.launchId ?? WildlifeAutomatedTestRunner.LaunchId,
                    generation = request?.generation ?? WildlifeAutomatedTestRunner.Generation,
                    suite = request?.suite,
                    mode = request?.mode,
                    agent = request?.agent,
                    leaseId = request?.leaseId,
                    status = "FAIL",
                    success = false,
                    errorCode = "RUNNER_EXCEPTION",
                    error = exception.GetBaseException().ToString(),
                    reportPath = WildlifeInGameTestSuite.ReportPath,
                    completedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)
                });
                Log.Error("[WildlifeTest] Explicit request failed: " + exception);
            }
            finally
            {
                if (activePath != null && File.Exists(activePath))
                    File.Delete(activePath);
                WildlifeAutomatedTestRunner.EndRequest();
            }
        }

        private static bool IsPlayableMap()
        {
            try
            {
                return GenScene.InPlayScene && Current.Game != null && Find.CurrentMap != null &&
                    Find.TickManager != null && Find.UIRoot != null && Find.WorldInterface != null &&
                    Find.TilePicker != null;
            }
            catch
            {
                return false;
            }
        }
    }
}
